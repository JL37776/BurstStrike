using Game.Scripts.Fixed;
using Game.Unit.Ability.BaseAbilities;
using System;
using Game.Grid;
using Game.Pathing;
using Game.World;
using Game.World.Logic;

namespace Game.Unit.Activity
{
    /// <summary>
    /// Minimal chase/aim activity.
    /// v0: rotation-only. Turns to face the target position and then finishes.
    /// </summary>
    public sealed class ChaseTarget : IActivity
    {
        public enum ChaseMode
        {
            /// <summary>
            /// Only chase/move; do not rotate/aim.
            /// </summary>
            MoveOnly = 0,

            /// <summary>
            /// Chase until within StopDistance, then rotate to face the target.
            /// </summary>
            MoveThenRotate = 1,

            /// <summary>
            /// Only rotate to face the target (no movement).
            /// </summary>
            RotateOnly = 2,
        }

        private readonly Actor _self;
        private readonly int _targetActorId;
        private readonly GridPosition _targetAcquireCell;
        private FixedVector3 _targetPos;

        private readonly ChaseMode _mode;
        // For now, chase/attack range is fixed to 5 world units (will later come from Weapon ability).
        private static readonly Fixed DefaultAttackRange = Fixed.FromInt(5);
        private bool _finished;
        private bool _pushedNavigate;

        // Track latest observed target cell so we can detect movement and replan.
        private GridPosition _lastKnownTargetCell;
        private bool _hasLastKnownTargetCell;

        public int TargetActorId => _targetActorId;
        public GridPosition TargetAcquireCell => _targetAcquireCell;

        // Finish once yaw delta is within threshold.
        private static readonly Fixed FinishThresholdRad = Fixed.FromInt(2) * FixedMath.Deg2Rad;

        public ChaseTarget(Actor self, FixedVector3 targetPos)
            : this(self, targetActorId: 0, targetAcquireCell: default, targetPos: targetPos, ChaseMode.RotateOnly, stopDistance: Fixed.Zero)
        {
        }

        public ChaseTarget(Actor self, FixedVector3 targetPos, ChaseMode mode, Fixed stopDistance)
            : this(self, targetActorId: 0, targetAcquireCell: default, targetPos: targetPos, mode, stopDistance)
        {
        }

        public ChaseTarget(Actor self, int targetActorId, GridPosition targetAcquireCell, FixedVector3 targetPos, ChaseMode mode, Fixed stopDistance)
        {
            _self = self;
            _targetActorId = targetActorId;
            _targetAcquireCell = targetAcquireCell;
            _targetPos = targetPos;
            _mode = mode;
        }

        public bool IsFinished()
        {
            if (_finished)
            {
                // Notify guard that chasing ended.
                try
                {
                    if (_self?.Abilities != null)
                    {
                        foreach (var ab in _self.Abilities)
                        {
                            if (ab is Game.Unit.Ability.BaseAbilities.Guard g)
                            {
                                g.NotifyChaseFinished();
                                break;
                            }
                        }
                    }
                }
                catch { /* don't throw */ }
            }
            return _finished;
        }

        public void Tick()
        {
            if (_finished) return;
            if (_self == null) { _finished = true; return; }

            // When this activity is on top (it only ticks when on top), refresh target position from world.
            // GuardAbility handles path replanning while Navigate/Move are on top, but once we pop to ChaseTarget
            // (stop-range), we must face the CURRENT target location (otherwise we rotate to a stale position).
            if (_targetActorId != 0)
            {
                try
                {
                    var world = _self.World as LogicWorld;
                    if (world != null && world.TryGetActorById(_targetActorId, out var tgt) && tgt != null)
                    {
                        foreach (var ab in tgt.Abilities)
                        {
                            if (ab is Location l) { _targetPos = l.Position; break; }
                        }
                    }
                }
                catch { }
            }

    // NOTE: Moving-target replanning is handled by Guard ability (abilities tick every tick,
    // while this activity may not be on top of the stack). We keep ChaseTarget focused on
    // initial navigation push + finish conditions.

    // B1: move modes manage Navigate + optional rotate.
    if (_mode == ChaseMode.MoveOnly || _mode == ChaseMode.MoveThenRotate)
    {
        TickMoveMode();
        return;
    }

    Location loc = null;
    foreach (var ab in _self.Abilities)
    {
        if (ab is Location l) { loc = l; break; }
    }
    if (loc == null) { _finished = true; return; }

    var diff = _targetPos - loc.Position;
    // If target is essentially our current position, nothing to face.
    if (diff.SqrMagnitude().Raw == 0)
    {
        _finished = true;
        return;
    }

    var dir = diff.Normalized();
    var desiredYaw = FixedMath.Atan2(dir.x, dir.z);
    var currentYaw = GetYawRad(loc.Rotation);
    var deltaYaw = WrapPi(desiredYaw - currentYaw);

    // Turn speed (deg per tick) => rad per tick. 0 means infinite.
    Fixed newYaw;
    Movement movement = null;
    foreach (var ab in _self.Abilities)
    {
        if (ab is Movement m) { movement = m; break; }
    }
    if (movement == null || movement.TurnSpeedDeg.Raw == 0)
    {
        newYaw = desiredYaw;
    }
    else
    {
        var maxStep = movement.TurnSpeedDeg * FixedMath.Deg2Rad;
        if (maxStep.Raw == 0)
            maxStep = Fixed.FromMilli(1) * FixedMath.Deg2Rad; // tiny clamp

        var step = FixedMath.Clamp(deltaYaw, -maxStep, maxStep);
        newYaw = currentYaw + step;
    }

    loc.Rotation = YawToQuaternion(newYaw);

    var remain = Fixed.Abs(WrapPi(desiredYaw - newYaw));
    if (remain <= FinishThresholdRad)
        _finished = true;
}

private void RefreshTargetFromWorldAndMaybeCancelNavigate()
{
    var world = _self.World as LogicWorld;
    if (world == null || world.Map == null || world.Occupancy == null) return;

    if (!world.TryGetActorById(_targetActorId, out var target) || target == null)
    {
        _finished = true;
        return;
    }

    // Update target world position.
    foreach (var ab in target.Abilities)
    {
        if (ab is Location l)
        {
            _targetPos = l.Position;
            break;
        }
    }

    // Update target cell and detect movement.
    if (!world.Occupancy.TryGetCellOfActor(target, out var cellNow)) return;
    if (!_hasLastKnownTargetCell)
    {
        _lastKnownTargetCell = cellNow;
        _hasLastKnownTargetCell = true;
        return;
    }

    if (cellNow.X == _lastKnownTargetCell.X && cellNow.Y == _lastKnownTargetCell.Y)
        return;

    _lastKnownTargetCell = cellNow;

    // Target changed cell. Replan WITHOUT clearing the activity stack.
    // We keep the current Navigate/Move and just retarget it to the new path.
    if (_self.Activities == null || _self.Activities.Count == 0) return;

    // Determine our current cell.
    if (!world.Occupancy.TryGetCellOfActor(_self, out var startCell)) return;

    // Movement mask from Navigation ability.
    uint movementMask = 1u;
    foreach (var ab in _self.Abilities)
    {
        if (ab is Unit.Ability.Navigation nav)
        {
            movementMask = nav.MovementMask;
            if (movementMask == 0u) movementMask = 1u;
            break;
        }
    }

    // New goal cell from enemy current position.
    var goalCell = world.Map.Grid.WorldToCell(new FixedVector2(_targetPos.x, _targetPos.z));
    if (!world.Map.Grid.Contains(goalCell)) return;

    var goalMaskEnum = (Game.Map.MapLayer)movementMask;
    if (!world.Map.IsWalkable(goalCell, goalMaskEnum))
    {
        if (!TryFindNearestWalkableCell(world.Map, goalCell, goalMaskEnum, maxRadius: 8, out goalCell))
            return;
    }

    var newPath = PathService.FindPathPointToPoint(world.Map, startCell, goalCell, goalMaskEnum);
    if (newPath == null || !newPath.HasPath) return;

    // Update currently active Navigate in-place so it won't keep marching along the old path.
    // Stack shape during navigation is typically: ... GuardActivity, ChaseTarget, Navigate, Move (top)
    // Actor.Tick only ticks the TOP activity (Move), so Navigate won't re-evaluate unless we update its _path here.
    Navigate navOnStack = null;
    foreach (var act in _self.Activities)
    {
        if (act is Navigate n) { navOnStack = n; break; }
        if (ReferenceEquals(act, this)) break; // don't scan below ChaseTarget
    }
    if (navOnStack != null)
    {
        var newTargetIndex = new GridIndex(goalCell.X, goalCell.Y);
        navOnStack.MoveTo(newTargetIndex, newPath);
    }

    // If stack top is Move, retarget it to the NEXT waypoint (not the final goal),
    // otherwise it may run straight into obstacles and "keep going" as if on old path.
    if (_self.Activities.Peek() is Move m)
    {
        // Prefer extracting the next cell from a GridPathResult.
        GridPosition nextCell = goalCell;
        if (newPath is GridPathResult gpr)
        {
            // The path includes start; pick the immediate next step if available.
            var cells = gpr.RawPath;
            if (cells != null && cells.Count >= 2)
                nextCell = cells[1];
            else if (cells != null && cells.Count == 1)
                nextCell = cells[0];
        }

        var nextWorld2 = world.Map.Grid.GetCellCenterWorld(nextCell);
        m.SetTarget(new FixedVector3(nextWorld2.x, _targetPos.y, nextWorld2.y));
        return;
    }

    // If top isn't Move, simply allow the next TickMoveMode to push a fresh Navigate.
    _pushedNavigate = false;
}

private void TickMoveMode()
{
    if (_finished) return;

    var world = _self.World as LogicWorld;
    if (world == null || world.Map == null || world.Occupancy == null) { _finished = true; return; }

    // Need our current position to decide if we've reached melee/attack range.
    Location loc = null;
    foreach (var ab in _self.Abilities)
    {
        if (ab is Location l) { loc = l; break; }
    }
    if (loc == null) { _finished = true; return; }

    var diff = _targetPos - loc.Position;
    var distSq = diff.SqrMagnitude();
    var range = DefaultAttackRange;
    var rangeSq = range * range;

    // If within range, either finish (MoveOnly) or rotate then finish (MoveThenRotate).
    if (distSq.Raw <= rangeSq.Raw)
    {
        if (_mode == ChaseMode.MoveOnly)
        {
            _finished = true;
            return;
        }

        // MoveThenRotate: once in range, rotate to face target then finish.
        TickRotateOnly(loc);
        return;
    }

    // Not in range: ensure Navigate exists on stack or push one.
    if (_pushedNavigate)
    {
        // If our pushed Navigate completed (or got popped), allow replanning next tick.
        if (_self.Activities == null || _self.Activities.Count == 0 || !ContainsNavigateOnTopOfUs())
        {
            _pushedNavigate = false;
        }
        return;
    }

    if (!world.Occupancy.TryGetCellOfActor(_self, out var startCell)) return;

    // Movement mask from Navigation ability.
    uint movementMask = 1u;
    foreach (var ab in _self.Abilities)
    {
        if (ab is Unit.Ability.Navigation nav)
        {
            movementMask = nav.MovementMask;
            if (movementMask == 0u) movementMask = 1u;
            break;
        }
    }

    // Target cell from last known enemy position.
    var goalCell = world.Map.Grid.WorldToCell(new FixedVector2(_targetPos.x, _targetPos.z));
    if (!world.Map.Grid.Contains(goalCell)) { _finished = true; return; }

    var goalMaskEnum = (Game.Map.MapLayer)movementMask;
    if (!world.Map.IsWalkable(goalCell, goalMaskEnum))
    {
        if (!TryFindNearestWalkableCell(world.Map, goalCell, goalMaskEnum, maxRadius: 8, out goalCell))
        {
            _finished = true;
            return;
        }
    }

    var path = PathService.FindPathPointToPoint(world.Map, startCell, goalCell, goalMaskEnum);
    if (path == null || !path.HasPath)
    {
        _finished = true;
        return;
    }

    var targetIndex = new GridIndex(goalCell.X, goalCell.Y);
    var targetWorld2 = world.Map.Grid.GetCellCenterWorld(goalCell);
    _self.Activities?.Push(new Navigate(targetIndex, targetWorld2, _self, path));
    _pushedNavigate = true;

    // Eliminate stutter: immediately tick the new stack top so Navigate can push Move right away.
    // This avoids waiting an extra tick after a stack reset/replan.
    try
    {
        if (_self.Activities != null && _self.Activities.Count > 0)
        {
            var top = _self.Activities.Peek();
            top?.Tick();

            // If Navigate pushed a Move on top, tick it once too to start motion immediately.
            if (_self.Activities.Count > 0 && _self.Activities.Peek() is Move)
                _self.Activities.Peek()?.Tick();
        }
    }
    catch { /* don't throw in logic */ }
}

private bool ContainsNavigateOnTopOfUs()
{
    // We only care if Navigate is currently running above GuardActivity.
    // Since ChaseTarget itself is below it (not top), a simple stack scan is enough.
    if (_self?.Activities == null) return false;
    foreach (var a in _self.Activities)
    {
        if (a is Navigate) return true;
        if (ReferenceEquals(a, this)) break;
    }
    return false;
}

private void TickRotateOnly(Location loc)
{
    // Reuse existing RotateOnly computation but with provided Location.
    Movement movement = null;
    foreach (var ab in _self.Abilities)
    {
        if (ab is Movement m) { movement = m; break; }
    }

    var diff = _targetPos - loc.Position;
    if (diff.SqrMagnitude().Raw == 0) { _finished = true; return; }
    var dir = diff.Normalized();
    var desiredYaw = FixedMath.Atan2(dir.x, dir.z);
    var currentYaw = GetYawRad(loc.Rotation);
    var deltaYaw = WrapPi(desiredYaw - currentYaw);

    Fixed newYaw;
    if (movement == null || movement.TurnSpeedDeg.Raw == 0)
    {
        newYaw = desiredYaw;
    }
    else
    {
        var maxStep = movement.TurnSpeedDeg * FixedMath.Deg2Rad;
        if (maxStep.Raw == 0)
            maxStep = Fixed.FromMilli(1) * FixedMath.Deg2Rad;
        var step = FixedMath.Clamp(deltaYaw, -maxStep, maxStep);
        newYaw = currentYaw + step;
    }
    loc.Rotation = YawToQuaternion(newYaw);

    var remain = Fixed.Abs(WrapPi(desiredYaw - newYaw));
    if (remain <= FinishThresholdRad)
        _finished = true;
}

private static bool TryFindNearestWalkableCell(Game.Map.IMap map, GridPosition goal, Game.Map.MapLayer mask,
    int maxRadius, out GridPosition found)
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
                var p = new GridPosition(goal.X + dx, goal.Y + dy);
                if (!map.Grid.Contains(p)) continue;
                if (!map.IsWalkable(p, mask)) continue;
                found = p;
                return true;
            }
        }
    }

    return false;
}

private static Fixed WrapPi(Fixed a)
{
    while (a > FixedMath.Pi) a -= FixedMath.TwoPi;
    while (a < -FixedMath.Pi) a += FixedMath.TwoPi;
    return a;
}

private static Fixed GetYawRad(FixedQuaternion q)
{
    // yaw = atan2(2(wy + xz), 1 - 2(y^2 + z^2))
    var two = Fixed.Two;
    var t0 = two * (q.w * q.y + q.x * q.z);
    var t1 = Fixed.One - two * (q.y * q.y + q.z * q.z);
    return FixedMath.Atan2(t0, t1);
}

private static FixedQuaternion YawToQuaternion(Fixed yawRad)
{
    // q = [0, sin(y/2), 0, cos(y/2)]
    var half = yawRad / Fixed.Two;
    var s = FixedMath.Sin(half);
    var c = FixedMath.Cos(half);
    return new FixedQuaternion(Fixed.Zero, s, Fixed.Zero, c).Normalized();
}
    }
}

