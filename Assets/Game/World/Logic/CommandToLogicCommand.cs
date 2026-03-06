using System;
using System.Collections.Generic;
using Game.Map;
using Game.Scripts.Fixed;
using Game.Unit;
using Game.Unit.Ability.BaseAbilities;
using Game.Unit.Activity;

namespace Game.World.Logic
{
    /// <summary>
    /// Adapter: converts a serialized Command into an executable ILogicCommand.
    /// Kept outside of World.cs to avoid bloating the MonoBehaviour bridge.
    /// </summary>
    internal sealed class CommandToLogicCommand : ILogicCommand
    {
        private readonly Game.Command.Command _cmd;

        public CommandToLogicCommand(Game.Command.Command cmd)
        {
            _cmd = cmd;
        }

        public void Execute(LogicWorld world)
        {
            if (world == null) return;

            switch (_cmd.Type)
            {
                case Game.Command.CommandType.UnitSpawn:
                {
                    // Payload contract (see CommandFactory.SpawnUnit):
                    // - TargetId: unitId
                    // - Int0: archetypeId
                    // - Int1: playerId/userId
                    // - Int2: factionId
                    // - Point: spawn position
                    var unitId = _cmd.TargetId;
                    var archetypeId = _cmd.Int0;
                    var playerId = _cmd.Int1;
                    var factionId = _cmd.Int2;

                    // Build actor from preloaded YAML archetype (no file IO here).
                    LogicUnitFactory.BuildAndAddToWorld(world, unitId, archetypeId, factionId, playerId, _cmd.Point);
                    break;
                }

                case Game.Command.CommandType.UnitMove:
                case Game.Command.CommandType.UnitMoveAttack:
                case Game.Command.CommandType.UnitForceAttackPoint:
                {
                    // Cmd → logic movement pipeline (how pathing is triggered):
                    // 1) World(main thread) receives input and enqueues an encoded Command to LogicWorld.
                    // 2) LogicWorld executes this adapter (CommandToLogicCommand) at the start of a logic tick.
                    // 3) Here we:
                    //    - Resolve each unit id → Actor (logic thread, deterministic)
                    //    - Read per-unit navigation flags/masks
                    //    - Resolve a shared goal cell (walkable)
                    //    - Batch pathfinding once via PathService.CalculatePathsAuto(...)
                    //    - Push activities (Navigate / Move) onto each Actor's activity stack
                    //
                    // New rule (per request): for any command-triggered move/attack/move-attack,
                    // we prune the activity stack so that ONLY Idle and GuardActivity are kept.
                    // Everything else (Navigate/Move/Chase/etc.) is cleared before applying the new order.

                    var unitIds = _cmd.UnitIds;
                    if (unitIds == null || unitIds.Length == 0) return;

                    var map = world.Map;
                    var occ = world.Occupancy;

                    // Resolve goal cell once per command.
                    Grid.GridPosition goalCell = default;
                    MapLayer goalMaskEnum = MapLayer.All;
                    bool goalAllowDiagonals = true;
                    bool goalAllowCornerCutting = false;

                    // Collect per-unit data first so we can batch pathfinding.
                    var actors = new List<Actor>(unitIds.Length);
                    var movements = new List<Game.Unit.Ability.BaseAbilities.Movement>(unitIds.Length);
                    var speeds = new List<Fixed>(unitIds.Length);
                    var startCells = new List<Grid.GridPosition>(unitIds.Length);

                    // Read optional debug settings from World (main-thread object referenced via WorldRef).
                    Game.Pathing.PathService.PathMode? forcedMode = null;
                    var worldRef = WorldDebugAccess.TryGetWorld();
                    if (worldRef != null && worldRef.debugForcePathMode)
                    {
                        forcedMode = worldRef.debugPathMode == World.DebugPathMode.FlowField
                            ? Game.Pathing.PathService.PathMode.FlowField
                            : Game.Pathing.PathService.PathMode.AStar;
                    }

                    var threshold = (worldRef != null && worldRef.pathingAStarUnitCountThreshold > 0)
                        ? worldRef.pathingAStarUnitCountThreshold
                        : 10;

                    for (int i = 0; i < unitIds.Length; i++)
                    {
                        var id = unitIds[i];
                        if (!world.TryGetRootActorByIndex(id - 1, out var actor) || actor == null)
                            continue;

                        Game.Unit.Ability.BaseAbilities.Movement movement = null;
                        uint movementMask = (uint)MapLayer.All;
                        bool allowDiagonals = true;
                        bool allowCornerCutting = false;

                        foreach (var ab in actor.Abilities)
                        {
                            if (movement == null && ab is Game.Unit.Ability.BaseAbilities.Movement m)
                                movement = m;
                            if (ab is Game.Unit.Ability.Navigation nav)
                            {
                                movementMask = nav.MovementMask;
                                allowDiagonals = nav.AllowDiagonals;
                                allowCornerCutting = nav.AllowCornerCutting;
                            }
                        }

                        var speed = movement != null
                            ? (movement.MaxSpeed.Raw != 0 ? movement.MaxSpeed : Fixed.FromMilli(100))
                            : Fixed.FromMilli(100);

                        // Activity pruning: keep only Idle + GuardActivity.
                        actor.Activities ??= new Stack<Game.Unit.Activity.IActivity>();
                        if (actor.Activities.Count > 0)
                        {
                            var kept = (Game.Unit.Activity.IActivity)null;
                            var keptGuard = (Game.Unit.Activity.IActivity)null;

                            // Stack<T> enumerates from top → bottom.
                            foreach (var act in actor.Activities)
                            {
                                if (act == null) continue;
                                if (kept == null && act is IdleActivity) kept = act;
                                else if (keptGuard == null && act is Game.Unit.Activity.GuardActivity) keptGuard = act;
                                if (kept != null && keptGuard != null) break;
                            }

                            // If we are keeping GuardActivity, cancel any ongoing chase state in Guard ability.
                            // Otherwise GuardActivity might stop reacting because Guard.IsChasing is still true.
                            if (keptGuard != null)
                            {
                                foreach (var ab in actor.Abilities)
                                {
                                    if (ab is Guard g)
                                    {
                                        g.CancelChaseFromExternalCommand();
                                        break;
                                    }
                                }
                            }

                            actor.Activities.Clear();

                            // Rebuild: bottom first, then top.
                            // Ensure Idle always exists so the unit has a baseline state.
                            actor.Activities.Push(kept ?? new IdleActivity());
                            if (keptGuard != null)
                                actor.Activities.Push(keptGuard);
                        }
                        else
                        {
                            actor.Activities.Push(new IdleActivity());
                        }

                        movement?.ClearTarget();

                        if (map == null || occ == null || !occ.TryGetCellOfActor(actor, out var startCell))
                        {
                            // Can't path this unit now; fall back to direct movement below.
                            actors.Add(actor);
                            movements.Add(movement);
                            speeds.Add(speed);
                            startCells.Add(default);
                            continue;
                        }

                        // Compute goal cell lazily (once) using the first unit's nav settings.
                        if (actors.Count == 0)
                        {
                            var goalWorld2 = new FixedVector2(_cmd.Point.x, _cmd.Point.z);
                            var requestedGoal = map.Grid.WorldToCell(goalWorld2);

                            goalMaskEnum = (MapLayer)movementMask;
                            goalAllowDiagonals = allowDiagonals;
                            goalAllowCornerCutting = allowCornerCutting;

                            goalCell = requestedGoal;
                            if (!map.IsWalkable(requestedGoal, goalMaskEnum))
                                TryFindNearestWalkableCell(map, requestedGoal, goalMaskEnum, maxRadius: 8, out goalCell);
                        }

                        actors.Add(actor);
                        movements.Add(movement);
                        speeds.Add(speed);
                        startCells.Add(startCell);
                    }

                    // Batch pathfinding exactly once for this command.
                    IReadOnlyList<List<Grid.GridPosition>> rawPaths = null;
                    if (map != null && actors.Count > 0)
                    {
                        rawPaths = Game.Pathing.PathService.CalculatePathsAuto(
                            map,
                            startCells,
                            goalCell,
                            goalMaskEnum,
                            goalAllowDiagonals,
                            goalAllowCornerCutting,
                            threshold,
                            forcedMode);
                    }

                    // Apply results per unit.
                    for (int i = 0; i < actors.Count; i++)
                    {
                        var actor = actors[i];
                        var movement = movements[i];
                        var speed = speeds[i];

                        var raw = (rawPaths != null && i < rawPaths.Count) ? rawPaths[i] : null;
                        if (raw != null && raw.Count > 0)
                        {

                            var targetIndex = new Game.Unit.Activity.GridIndex(goalCell.X, goalCell.Y);
                            var targetWorld2 = map.Grid.GetCellCenterWorld(goalCell);
                            var path = new Game.Pathing.GridPathResult(map, raw);

                            // best effort advance based on current world pos (XZ)
                            Location loc = null;
                            foreach (var ab2 in actor.Abilities)
                                if (ab2 is Location l2) { loc = l2; break; }
                            if (loc != null)
                            {
                                var agent2 = new FixedVector2(loc.Position.x, loc.Position.z);
                                path.NextPoint(agent2);
                            }

                            actor.Activities.Push(new Game.Unit.Activity.Navigate(targetIndex, targetWorld2, actor, path));
                            continue;
                        }

                        // Fallback: direct Move to target point
                        actor.Activities.Push(new Game.Unit.Activity.Move(_cmd.Point, speed, actor));
                        movement?.SetTarget(_cmd.Point, speed);
                    }

                    break;
                }

                default:
                    break;
            }
        }

        private static bool TryFindNearestWalkableCell(Game.Map.IMap map, Grid.GridPosition goal, MapLayer mask, int maxRadius, out Grid.GridPosition found)
        {
            found = goal;
            if (map == null) return false;
            if (map.IsWalkable(goal, mask)) return true;

            for (int r = 1; r <= maxRadius; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        var p = new Grid.GridPosition(goal.X + dx, goal.Y + dy);
                        if (!map.Grid.Contains(p)) continue;
                        if (!map.IsWalkable(p, mask)) continue;
                        found = p;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
