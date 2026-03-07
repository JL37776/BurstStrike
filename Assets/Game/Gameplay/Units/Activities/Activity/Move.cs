using Game.Scripts.Fixed;
using Game.Unit.Ability.BaseAbilities;
using Game.World.Logic;
using UnityEngine;

namespace Game.Unit.Activity
{
    public class Move : IActivity
    {
        private bool _isFinished;
        private readonly Actor _actor;

        // Legacy/fallback target (kept to avoid breaking old call sites)
        private FixedVector3 _fallbackTarget;

        // Legacy/fallback speed (kept to avoid breaking old call sites)
        private Fixed _fallbackSpeed;

        // If true, this activity only rotates to face the target (no translation), then finishes.
        private readonly bool _rotateOnly;

        // When rotating only, finish once yaw delta is within this threshold.
        private static readonly Fixed RotateOnlyFinishThresholdRad = Fixed.FromInt(5) * FixedMath.Deg2Rad;

        // Debug
        // (Keep a switch but not as a compile-time constant to avoid unreachable-code warnings in the IDE analyzer.)
        private static bool DebugMove => false;
        private int _debugTick;

        // Prevent re-entrant chaining loops when we force-run the next activity in the same tick.
        [System.ThreadStatic] private static bool _chaining;

        public Move(FixedVector3 target, Fixed speed, Actor actor)
        {
            _fallbackTarget = target;
            _fallbackSpeed = speed;
            _actor = actor;
            _isFinished = false;
            _rotateOnly = false;
        }

        /// <summary>
        /// Overload for rotate-only behavior.
        /// If rotateOnly is true, Move will rotate towards the target direction and finish once aligned.
        /// </summary>
        public Move(FixedVector3 target, Fixed speed, Actor actor, bool rotateOnly)
            : this(target, speed, actor)
        {
            _rotateOnly = rotateOnly;
        }

        /// <summary>
        /// Convenience ctor: rotate-only to face target.
        /// </summary>
        public static Move RotateOnly(FixedVector3 lookAtTarget, Actor actor)
        {
            return new Move(lookAtTarget, Fixed.Zero, actor, rotateOnly: true);
        }

        public void SetSpeed(Fixed speed)
        {
            _fallbackSpeed = speed;
        }

        public void SetTarget(FixedVector3 target)
        {
            _fallbackTarget = target;
            _isFinished = false;
        }

        public bool IsFinished()
        {
            if (DebugMove && _isFinished)
                Debug.Log("[Move] Arrived");
            return _isFinished;
        }

        private bool IsCurrentCellFreeOfOthers()
        {
            if (_actor == null) return true;

            var world = _actor.World;
            // Avoid analyzer false positives on qualifier usage by splitting the expression.
            var occupancy = world != null ? world.Occupancy : null;
            if (occupancy == null) return true;

            uint movementMask = 1u;
            foreach (var ab in _actor.Abilities)
            {
                if (ab is Unit.Ability.Navigation nav)
                {
                    movementMask = nav.MovementMask;
                    if (movementMask == 0u) movementMask = 1u;
                    break;
                }
            }

            if (!occupancy.TryGetCellOfActor(_actor, out var cell))
                return true;

            return OccupancyUtil.IsCellFreeOfOthers(occupancy, cell, movementMask, _actor);
        }

        private Movement GetMovement()
        {
            if (_actor?.Abilities == null) return null;
            foreach (var a in _actor.Abilities)
            {
                if (a is Movement mv) return mv;
            }
            return null;
        }

        public void Tick()
        {
            if (_actor == null)
            {
                _isFinished = true;
                return;
            }

            Location loc = null;
            foreach (var a in _actor.Abilities)
            {
                if (a is Location l) { loc = l; break; }
            }

            if (loc == null)
            {
                _isFinished = true;
                return;
            }

            // Movement ability is still used for speed/clearing state, but NOT for providing the target.
            var movement = GetMovement();

            // IMPORTANT:
            // Navigate must be the only activity that sets movement targets.
            // Therefore, Move always uses its own (constructor) target and ignores movement.Target.
            var target = _fallbackTarget;

            var speed = movement != null
                ? (movement.Speed.Raw != 0 ? movement.Speed : movement.MaxSpeed)
                : _fallbackSpeed;
            if (speed.Raw == 0) speed = _fallbackSpeed.Raw != 0 ? _fallbackSpeed : Fixed.FromMilli(100);

            var diff = target - loc.Position;
            var distSqr = diff.SqrMagnitude();
            var thresh = Movement.ArrivalThreshold;

            if (DebugMove)
            {
                _debugTick++;
                if ((_debugTick % 10) == 1)
                    Debug.Log($"[Move] pos={loc.Position} target={target} diff={diff} distSqr={distSqr} threshSqr={(thresh * thresh)}");
            }

            if (distSqr <= thresh * thresh)
            {
                loc.Position = target;
                _isFinished = IsCurrentCellFreeOfOthers();
                if (_isFinished)
                {
                    // Only hard-stop for legacy constant-speed mode. If we have acceleration,
                    // keep current Speed so next waypoint can chain smoothly.
                    if (movement != null && movement.Acceleration.Raw == 0)
                        movement.Speed = Fixed.Zero;

                    // Do NOT clear movement target here; Navigate will immediately set a new one.
                    // Clearing it causes Movement.Tick() to stop integrating and can create stop-go behavior.
                    // movement?.ClearTarget();

                    // Same-tick chaining: let Navigate immediately schedule the next cell.
                    TryChainNextActivitySameTick();
                }
                return;
            }

            // Compute next position.
            var dir = diff.Normalized();

            // NEW: rotation + move gating (yaw only)
            if (movement != null)
            {
                // If TurnSpeedDeg==0 => infinite, always allow move.
                var turnSpeedDeg = movement.TurnSpeedDeg;

                // Treat 0 as infinite only if it's truly intended (default). If config authoring
                // results in very small per-tick values that quantize to 0, clamp to a tiny positive.
                // This avoids "TurnSpeedDeg=1" (deg/s) becoming 0 (deg/tick) and thus instant turning.
                if (turnSpeedDeg.Raw == 0)
                {
                    // If YAML provided a TurnSpeedDeg > 0 but got quantized to 0, we can't detect it here.
                    // However, for tanks we generally want finite turning, so prefer a tiny minimum when MaxSpeed>0.
                    // Heuristic: if MaxSpeed is non-zero and TurnSpeedDeg is zero, assume infinite; otherwise clamp.
                }

                var thresholdRad = Fixed.FromInt(25) * FixedMath.Deg2Rad;

                // Convert deg/tick -> rad/tick. If turnSpeedDeg is non-zero but too small to convert, clamp.
                var turnSpeedRad = turnSpeedDeg.Raw == 0 ? Fixed.Zero : (turnSpeedDeg * FixedMath.Deg2Rad);
                if (turnSpeedDeg.Raw != 0 && turnSpeedRad.Raw == 0)
                {
                    // Minimum ~0.001 deg per tick
                    turnSpeedRad = Fixed.FromMilli(1) * FixedMath.Deg2Rad;
                }

                // current yaw from quaternion, desired yaw from movement direction.
                var currentYaw = GetYawRad(loc.Rotation);
                var desiredYaw = FixedMath.Atan2(dir.x, dir.z); // yaw around Y, forward=+Z
                var deltaYaw = WrapPi(desiredYaw - currentYaw);
                var absDelta = Fixed.Abs(deltaYaw);

                // Rotate-only mode: always rotate towards desired yaw and finish when aligned.
                if (_rotateOnly)
                {
                    Fixed newYaw;
                    if (turnSpeedDeg.Raw == 0)
                    {
                        newYaw = desiredYaw;
                    }
                    else
                    {
                        var maxStep = turnSpeedRad;
                        var step = FixedMath.Clamp(deltaYaw, -maxStep, maxStep);
                        newYaw = currentYaw + step;
                    }

                    loc.Rotation = YawToQuaternion(newYaw);

                    // Finish once we're close enough in yaw.
                    var remain = Fixed.Abs(WrapPi(desiredYaw - newYaw));
                    if (remain <= RotateOnlyFinishThresholdRad)
                        _isFinished = true;
                    return;
                }

                // If we have a finite turn speed and we're outside the threshold:
                // - If we still have velocity, brake first (no rotation update this tick).
                // - Otherwise rotate-only (no translation).
                if (turnSpeedDeg.Raw != 0 && absDelta > thresholdRad)
                {

                    // Rotate-only step.
                    Fixed newYaw;
                    if (turnSpeedDeg.Raw == 0)
                    {
                        newYaw = desiredYaw;
                    }
                    else
                    {
                        var maxStep = turnSpeedRad;
                        var step = FixedMath.Clamp(deltaYaw, -maxStep, maxStep);
                        newYaw = currentYaw + step;
                    }
                    loc.Rotation = YawToQuaternion(newYaw);
                    return;
                }

                // Step rotation this tick (we are within threshold, so we can rotate while moving)
                Fixed moveYaw;
                if (turnSpeedDeg.Raw == 0)
                {
                    moveYaw = desiredYaw;
                }
                else
                {
                    var maxMoveStep = turnSpeedRad;
                    var moveStep = FixedMath.Clamp(deltaYaw, -maxMoveStep, maxMoveStep);
                    moveYaw = currentYaw + moveStep;
                }
                loc.Rotation = YawToQuaternion(moveYaw);
            }

            // If rotate-only and we don't have Movement ability (no turn speed info), just snap and finish.
            if (_rotateOnly)
            {
                var desiredYaw = FixedMath.Atan2(dir.x, dir.z);
                loc.Rotation = YawToQuaternion(desiredYaw);
                _isFinished = true;
                return;
            }

            var delta = dir * speed;
            var deltaSqr = delta.SqrMagnitude();

            if (DebugMove)
            {
                if ((_debugTick % 10) == 1)
                    Debug.Log($"[Move] dir={dir} speed={speed} delta={delta}");
            }

            if (deltaSqr >= distSqr)
            {
                loc.Position = target;
                _isFinished = IsCurrentCellFreeOfOthers();
                if (_isFinished)
                {
                    if (movement != null && movement.Acceleration.Raw == 0)
                        movement.Speed = Fixed.Zero;
                    // movement?.ClearTarget();

                    TryChainNextActivitySameTick();
                }
                return;
            }

            // grid walkability gate. Prevent stepping into blocked cells.
            var world = _actor.World;
            Game.Map.IMap map = null;
            if (world is LogicWorld lw) map = lw.Map;

            if (map != null)
            {
                uint movementMask = 1u;
                foreach (var ab in _actor.Abilities)
                {
                    if (ab is Unit.Ability.Navigation nav)
                    {
                        movementMask = nav.MovementMask;
                        if (movementMask == 0u) movementMask = 1u;
                        break;
                    }
                }

                // Predict next position on XZ.
                var nextPos = new FixedVector3(loc.Position.x + delta.x, loc.Position.y + delta.y, loc.Position.z + delta.z);
                var next2 = new FixedVector2(nextPos.x, nextPos.z);
                var nextCell = map.Grid.WorldToCell(next2);

                if (!map.IsWalkable(nextCell, (Game.Map.MapLayer)movementMask))
                {
                    _isFinished = true;
                    if (movement != null) movement.Speed = Fixed.Zero;
                    movement?.ClearTarget();
                    return;
                }

                // Also ensure current cell isn't blocked (handles spawn inside obstacle)
                var cur2 = new FixedVector2(loc.Position.x, loc.Position.z);
                var curCell = map.Grid.WorldToCell(cur2);
                if (!map.IsWalkable(curCell, (Game.Map.MapLayer)movementMask))
                {
                    _isFinished = true;
                    if (movement != null) movement.Speed = Fixed.Zero;
                    movement?.ClearTarget();
                    return;
                }

                loc.Position = nextPos;
                return;
            }

            // No map available: do NOT move freely (prevents drifting/ghosting through obstacles).
            _isFinished = true;
            if (movement != null) movement.Speed = Fixed.Zero;
            movement?.ClearTarget();
            return;
        }

        private void TryChainNextActivitySameTick()
        {
            if (_chaining) return;
            if (_actor == null) return;
            if (_actor.Activities == null) return;

            // Only chain when we are indeed at the top of the stack; otherwise we might advance the wrong actor state.
            if (_actor.Activities.Count == 0) return;
            if (!ReferenceEquals(_actor.Activities.Peek(), this)) return;

            // Temporarily run Actor.Tick() so the finished Move will be popped and Navigate can tick immediately.
            // Actor.Tick has its own transition cap (MaxActivityTransitionsPerTick).
            try
            {
                _chaining = true;
                _actor.Tick();
            }
            finally
            {
                _chaining = false;
            }
        }

        // NEW helpers for yaw rotation (Fixed radians)
        private static Fixed WrapPi(Fixed a)
        {
            // map angle into [-pi,pi]
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
