using System.Collections.Generic;
using Game.Grid;
using Game.Unit.Activity;
using Game.World;
using Game.World.Logic;

namespace Game.Unit.Ability.BaseAbilities
{
    public class Guard : IAbility
    {
        public Actor Self { get; set; }
        private bool enemyInSight=false;

        /// <summary>
        /// Whether we are currently chasing a target (set when GuardActivity pushes ChaseTarget,
        /// cleared when ChaseTarget finishes).
        /// </summary>
        public bool IsChasing { get; private set; }

        // 1-tick delayed reset: mark on this tick, perform the clear on next tick.
        private bool _pendingReset;

        /// <summary>
        /// Guard radius in world units (Fixed). Used by enemy search.
        /// </summary>
        public Game.Scripts.Fixed.Fixed AlertRange = Game.Scripts.Fixed.Fixed.FromInt(5);

        /// <summary>
        /// Which target layers this unit can guard/detect.
        /// Flags for determinism + efficient filtering.
        /// </summary>
        public UnitAlertLayer AlertLayers = UnitAlertLayer.Ground;

        /// <summary>
        /// Convenience list view (requested "List" semantics). Setting this will overwrite AlertLayers.
        /// </summary>
        public System.Collections.Generic.List<UnitAlertLayer> AlertLayerList
        {
            get
            {
                var list = new System.Collections.Generic.List<UnitAlertLayer>(5);
                var m = AlertLayers;
                if ((m & UnitAlertLayer.Underwater) != 0) list.Add(UnitAlertLayer.Underwater);
                if ((m & UnitAlertLayer.Ocean) != 0) list.Add(UnitAlertLayer.Ocean);
                if ((m & UnitAlertLayer.Ground) != 0) list.Add(UnitAlertLayer.Ground);
                if ((m & UnitAlertLayer.LowAir) != 0) list.Add(UnitAlertLayer.LowAir);
                if ((m & UnitAlertLayer.HighAir) != 0) list.Add(UnitAlertLayer.HighAir);
                return list;
            }
            set
            {
                UnitAlertLayer m = UnitAlertLayer.None;
                if (value != null)
                {
                    for (int i = 0; i < value.Count; i++)
                        m |= value[i];
                }
                AlertLayers = m;
            }
        }

        // Track last known target cell for the currently chased target.
        private int _lastTrackedTargetId;
        private Game.Grid.GridPosition _lastTrackedTargetCell;
        private bool _hasLastTrackedTargetCell;

        private bool _inStopRange;

        public void Init()
        {
            enemyInSight = false;
            IsChasing = false;
            _pendingReset = false;

            _lastTrackedTargetId = 0;
            _hasLastTrackedTargetCell = false;
            _inStopRange = false;

            // Requirement #1: only push ONE GuardActivity at initialization.
            // GuardActivity never finishes, so it acts as a permanent behavior root.
            if (Self?.Activities == null)
                Self.Activities = new System.Collections.Generic.Stack<Game.Unit.Activity.IActivity>();
            
            // Avoid stacking: if a GuardActivity already exists anywhere, don't push again.
            bool hasGuard = false;
            if (Self?.Activities != null)
            {
                foreach (var a in Self.Activities)
                {
                    if (a is Game.Unit.Activity.GuardActivity) { hasGuard = true; break; }
                }
            }
            if (!hasGuard)
            {
                // Ensure there is at least an IdleActivity under it.
                if (Self.Activities.Count == 0)
                    Self.Activities.Push(new IdleActivity());

                Self.Activities.Push(new GuardActivity(Self, this));
            }
        }
        
        private void CheckNearByEnemy()
        {
            
        }
        
        public void Tick()
        {
            // Requirement #1: GuardActivity is only pushed in Init().
            // Tick is only responsible for monitoring chase state.
            if (Self == null) return;
            if (Self.Activities == null || Self.Activities.Count == 0) return;

            // Apply a delayed reset requested by last tick.
            if (_pendingReset)
            {
                _pendingReset = false;
                ResetToGuardActivity();
                IsChasing = false;
                return;
            }

            // Requirement #3: we only analyze target changes while chasing.
            if (!IsChasing)
                return;

            // IMPORTANT: Activities below the stack top won't tick. Abilities do tick.
            // So moving-target replanning must live here (ability layer).
            TryUpdateChaseNavigationInPlace();

            // If invalid, schedule clear for the NEXT tick.
            if (ShouldResetChaseNow())
                _pendingReset = true;
        }
        
        // Temporary chase stop distance (world units). Must match ChaseTarget's current fixed range.
        // Hysteresis avoids rapid pop/push when hovering around the boundary.
        private static readonly Game.Scripts.Fixed.Fixed ChaseStopDistanceEnter = Game.Scripts.Fixed.Fixed.FromInt(2);
        private static readonly Game.Scripts.Fixed.Fixed ChaseStopDistanceExit = Game.Scripts.Fixed.Fixed.FromInt(3);

        private void TryUpdateChaseNavigationInPlace()
        {
            // Find current ChaseTarget activity (nearest from top).
            Game.Unit.Activity.ChaseTarget chase = null;
            foreach (var a in Self.Activities)
            {
                if (a is Game.Unit.Activity.ChaseTarget ct) { chase = ct; break; }
            }
            if (chase == null) return;
            if (chase.TargetActorId == 0) return;

            var world = Self.World as LogicWorld;
            if (world == null || world.Map == null || world.Occupancy == null) return;

            if (!world.TryGetActorById(chase.TargetActorId, out var target) || target == null)
                return;

            // Guard responsibility:
            // 1) If Navigate+Move exist (i.e., navigation is in progress), monitor target cell changes and retarget in-place.
            // 2) If within stop-range AND Navigate+Move still exist, pop them so ChaseTarget becomes top and can rotate/finish.
            // Do NOT pop anything if navigation isn't currently running (prevents pop/push oscillation).

            // Determine if we have Navigate and Move above ChaseTarget (stack enumerates top->bottom).
            bool hasNavigateAboveChase = false;
            bool hasMoveAboveChase = false;
            foreach (var act in Self.Activities)
            {
                if (act is Game.Unit.Activity.ChaseTarget)
                    break;
                if (act is Game.Unit.Activity.Navigate) hasNavigateAboveChase = true;
                else if (act is Game.Unit.Activity.Move) hasMoveAboveChase = true;
                if (hasNavigateAboveChase && hasMoveAboveChase) break;
            }
            var hasNavigateAndMoveAboveChase = hasNavigateAboveChase && hasMoveAboveChase;

            // If already within stop distance and navigation is active, pop to ChaseTarget.
            try
            {
                var selfPos = default(Game.Scripts.Fixed.FixedVector3);
                var targetPos = default(Game.Scripts.Fixed.FixedVector3);
                bool hasSelf = false, hasTarget = false;
                foreach (var ab in Self.Abilities)
                    if (ab is Location l) { selfPos = l.Position; hasSelf = true; break; }
                foreach (var ab in target.Abilities)
                    if (ab is Location l) { targetPos = l.Position; hasTarget = true; break; }

                if (hasSelf && hasTarget)
                {
                    var diff = targetPos - selfPos;
                    var distSq = diff.SqrMagnitude();
                    var enterSq = ChaseStopDistanceEnter * ChaseStopDistanceEnter;
                    var exitSq = ChaseStopDistanceExit * ChaseStopDistanceExit;

                    if (_inStopRange)
                    {
                        // Stay in stop-range until we exceed the exit distance.
                        if (distSq.Raw > exitSq.Raw)
                            _inStopRange = false;
                    }
                    else
                    {
                        if (distSq.Raw <= enterSq.Raw)
                            _inStopRange = true;
                    }

                    if (_inStopRange)
                    {
                        // Only pop if navigation is actually running; otherwise leave stack untouched.
                        if (hasNavigateAndMoveAboveChase)
                        {
                            while (Self.Activities.Count > 0 && !(Self.Activities.Peek() is Game.Unit.Activity.ChaseTarget))
                                Self.Activities.Pop();
                        }
                        return;
                    }
                }
            }
            catch { }

            // Retargeting is only valid while both Navigate and Move are present.
            if (!hasNavigateAndMoveAboveChase)
                return;

            if (!world.Occupancy.TryGetCellOfActor(target, out var targetCell))
                return;

            // Reset tracking if target changed.
            if (_lastTrackedTargetId != chase.TargetActorId)
            {
                _lastTrackedTargetId = chase.TargetActorId;
                _lastTrackedTargetCell = targetCell;
                _hasLastTrackedTargetCell = true;
                return;
            }

            if (!_hasLastTrackedTargetCell)
            {
                _lastTrackedTargetCell = targetCell;
                _hasLastTrackedTargetCell = true;
                return;
            }

            if (targetCell.X == _lastTrackedTargetCell.X && targetCell.Y == _lastTrackedTargetCell.Y)
                return; // no movement in grid => no need to replan

            _lastTrackedTargetCell = targetCell;

            // Determine self start cell.
            if (!world.Occupancy.TryGetCellOfActor(Self, out var startCell))
                return;

            // Movement mask from Navigation ability.
            uint movementMask = 1u;
            foreach (var ab in Self.Abilities)
            {
                if (ab is Unit.Ability.Navigation nav)
                {
                    movementMask = nav.MovementMask;
                    if (movementMask == 0u) movementMask = 1u;
                    break;
                }
            }

            var mapLayer = (Game.Map.MapLayer)movementMask;

            // Ensure goal is walkable (or find nearest).
            var goalCell = targetCell;
            if (!world.Map.IsWalkable(goalCell, mapLayer))
            {
                if (!TryFindNearestWalkableCell(world.Map, goalCell, mapLayer, 8, out goalCell))
                    return;
            }

            var newPath = Game.Pathing.PathService.FindPathPointToPoint(world.Map, startCell, goalCell, mapLayer);
            if (newPath == null || !newPath.HasPath) return;

            // Find active Navigate (nearest from top) and update its path in-place.
            Game.Unit.Activity.Navigate navOnStack = null;
            foreach (var a in Self.Activities)
            {
                if (a is Game.Unit.Activity.Navigate n) { navOnStack = n; break; }
            }
            if (navOnStack != null)
            {
                var newTargetIndex = new Game.Unit.Activity.GridIndex(goalCell.X, goalCell.Y);
                navOnStack.MoveTo(newTargetIndex, newPath);
            }

            // Retarget current Move (if any) to the next waypoint so it starts steering immediately.
            if (Self.Activities.Count > 0 && Self.Activities.Peek() is Game.Unit.Activity.Move moveActivity)
             {
                 Game.Grid.GridPosition nextCell = goalCell;
                 if (newPath is Game.Pathing.GridPathResult gpr)
                 {
                     var cells = gpr.RawPath;
                     if (cells != null && cells.Count >= 2) nextCell = cells[1];
                     else if (cells != null && cells.Count == 1) nextCell = cells[0];
                 }

                 var nextWorld2 = world.Map.Grid.GetCellCenterWorld(nextCell);
                 // Y comes from current self location if possible.
                 var y = Game.Scripts.Fixed.Fixed.Zero;
                 foreach (var ab in Self.Abilities)
                 {
                     if (ab is Location l) { y = l.Position.y; break; }
                 }
                var newWorldTarget = new Game.Scripts.Fixed.FixedVector3(nextWorld2.x, y, nextWorld2.y);
                moveActivity.SetTarget(newWorldTarget);

                // IMPORTANT: also retarget Movement ability, because Move.Tick may drive translation
                // using Movement.Target/Speed, and otherwise it can keep following the old segment.
                foreach (var ab in Self.Abilities)
                {
                    if (ab is Game.Unit.Ability.BaseAbilities.Movement movementAbility)
                    {
                        var speed = movementAbility.MaxSpeed;
                        if (speed.Raw == 0) speed = Game.Scripts.Fixed.Fixed.FromMilli(100);
                        movementAbility.MoveTo(newWorldTarget, speed);
                        break;
                    }
                }
             }

        }
        
        private static bool TryFindNearestWalkableCell(Game.Map.IMap map, Game.Grid.GridPosition goal, Game.Map.MapLayer mask,
            int maxRadius, out Game.Grid.GridPosition found)
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
                        if (System.Math.Abs(dx) != r && System.Math.Abs(dy) != r) continue;
                        var p = new Game.Grid.GridPosition(goal.X + dx, goal.Y + dy);
                        if (!map.Grid.Contains(p)) continue;
                        if (!map.IsWalkable(p, mask)) continue;
                        found = p;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Called by GuardActivity when it successfully pushes a chase.
        /// </summary>
        public void NotifyChaseStarted()
        {
            IsChasing = true;
        }
        
        /// <summary>
        /// Called by ChaseTarget when it finishes naturally.
        /// </summary>
        public void NotifyChaseFinished()
        {
            IsChasing = false;
        }

        /// <summary>
        /// External override hook: if a player/network command replaces this unit's activities,
        /// guard should forget its current chase state so GuardActivity can resume searching.
        /// </summary>
        public void CancelChaseFromExternalCommand()
        {
            IsChasing = false;
            _pendingReset = false;

            // Also clear last tracked target bookkeeping.
            _lastTrackedTargetId = 0;
            _hasLastTrackedTargetCell = false;
            _inStopRange = false;
        }

        private bool ShouldResetChaseNow()
        {
            // Find nearest (top-most) ChaseTarget in stack.
            Game.Unit.Activity.ChaseTarget chase = null;
            foreach (var a in Self.Activities)
            {
                if (a is Game.Unit.Activity.ChaseTarget ct) { chase = ct; break; }
            }
            if (chase == null) return false;

            var world = Self.World as LogicWorld;
            if (world == null || world.Map == null || world.Occupancy == null) return false; // can't validate

            if (chase.TargetActorId == 0) return false;

            if (!world.TryGetActorById(chase.TargetActorId, out var target) || target == null)
                return true;

            // Optional safety: if target goes out of alert range, reset so guard can re-search.
            try
            {
                var selfPos = default(Game.Scripts.Fixed.FixedVector3);
                var targetPos = default(Game.Scripts.Fixed.FixedVector3);
                bool hasSelf = false, hasTarget = false;
                foreach (var ab in Self.Abilities)
                    if (ab is Location l) { selfPos = l.Position; hasSelf = true; break; }
                foreach (var ab in target.Abilities)
                    if (ab is Location l) { targetPos = l.Position; hasTarget = true; break; }

                if (hasSelf && hasTarget)
                {
                    var diff = targetPos - selfPos;
                    var distSq = diff.SqrMagnitude();
                    var rangeSq = AlertRange * AlertRange;
                    if (distSq.Raw > rangeSq.Raw)
                        return true;
                }
            }
            catch { }

            return false;
        }

        private void ResetToGuardActivity()
        {
            // Clear everything ABOVE the nearest GuardActivity.
            if (Self == null || Self.Activities == null || Self.Activities.Count == 0) return;

            bool hasGuard = false;
            foreach (var a in Self.Activities)
            {
                if (a is Game.Unit.Activity.GuardActivity) { hasGuard = true; break; }
            }
            if (!hasGuard) return;

            while (Self.Activities.Count > 0 && !(Self.Activities.Peek() is Game.Unit.Activity.GuardActivity))
                Self.Activities.Pop();
        }

    }
}
