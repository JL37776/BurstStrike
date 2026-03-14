using Game.Combat;
using Game.Grid;
using Game.Scripts.Fixed;
using Game.Unit.Ability;
using Game.Unit.Ability.BaseAbilities;

namespace Game.Unit.Activity
{
    /// <summary>
    /// Attack activity — manages targeting, facing, and firing loop for one target.
    /// Pushed onto the activity stack when a unit engages an enemy.
    /// Reference: OpenRA Attack activity, SC:BW unit attack state machine.
    ///
    /// State flow:
    ///   Approach → TurnToFace → Fire → CooldownWait → (loop or finish)
    /// </summary>
    public sealed class AttackTarget : IActivity
    {
        private readonly Actor _self;
        private readonly Actor _target;
        private readonly bool _allowChase;
        private readonly bool _forceAttack;

        private enum State { Approach, TurnToFace, Fire, CooldownWait }
        private State _state;
        private bool _finished;

        // Cached references (resolved once)
        private Armament _primaryWeapon;
        private Location _selfLoc;
        private Location _targetLoc;
        private bool _resolved;

        public AttackTarget(Actor self, Actor target, bool allowChase = true, bool forceAttack = false)
        {
            _self = self;
            _target = target;
            _allowChase = allowChase;
            _forceAttack = forceAttack;
            _state = State.Approach;
        }

        public bool IsFinished() => _finished;

        public void Tick()
        {
            if (_finished) return;

            // Resolve cached refs
            if (!_resolved) Resolve();

            // Target validation
            if (_target == null || IsTargetDead())
            {
                _finished = true;
                return;
            }

            // No weapon available
            if (_primaryWeapon == null)
            {
                _finished = true;
                return;
            }

            if (_selfLoc == null || _targetLoc == null)
            {
                _finished = true;
                return;
            }

            var diff = _targetLoc.Position - _selfLoc.Position;
            var distSq = diff.SqrMagnitude();
            var range = Fixed.FromRaw(_primaryWeapon.Def.Range);
            var rangeSq = range * range;

            switch (_state)
            {
                case State.Approach:
                    if (distSq.Raw <= rangeSq.Raw)
                    {
                        _state = _primaryWeapon.Def.RequiresFacing
                            ? State.TurnToFace : State.Fire;
                    }
                    else if (_allowChase)
                    {
                        // Push a ChaseTarget to move into range, then return here
                        var stopDist = Fixed.FromRaw(_primaryWeapon.Def.Range - 128);
                        _self.Activities?.Push(new ChaseTarget(
                            _self, _target.Id, default(GridPosition), _targetLoc.Position,
                            ChaseTarget.ChaseMode.MoveThenRotate, stopDist));
                    }
                    else
                    {
                        _finished = true; // can't move and out of range
                    }
                    break;

                case State.TurnToFace:
                    // Use Move.RotateOnly to face the target
                    if (diff.SqrMagnitude().Raw > 0)
                    {
                        var desiredYaw = FixedMath.Atan2(diff.x, diff.z);
                        var currentYaw = GetYaw(_selfLoc.Rotation);
                        var deltaYaw = WrapPi(desiredYaw - currentYaw);

                        if (Fixed.Abs(deltaYaw).Raw < (Fixed.FromInt(10) * FixedMath.Deg2Rad).Raw)
                        {
                            _state = State.Fire;
                        }
                        else
                        {
                            // Rotate towards target
                            var turnSpeed = GetTurnSpeed();
                            var step = turnSpeed.Raw == 0
                                ? desiredYaw
                                : currentYaw + FixedMath.Clamp(deltaYaw, -turnSpeed, turnSpeed);
                            _selfLoc.Rotation = YawToQuaternion(step);
                        }
                    }
                    else
                    {
                        _state = State.Fire;
                    }
                    break;

                case State.Fire:
                    // Re-check range (target may have moved)
                    if (distSq.Raw > rangeSq.Raw)
                    {
                        _state = State.Approach;
                        break;
                    }

                    if (_primaryWeapon.TryFire(_target))
                    {
                        _state = State.CooldownWait;
                    }
                    break;

                case State.CooldownWait:
                    if (_primaryWeapon.IsReady)
                    {
                        // Re-check target and range
                        if (IsTargetDead())
                        {
                            _finished = true;
                        }
                        else
                        {
                            _state = distSq.Raw <= rangeSq.Raw ? State.Fire : State.Approach;
                        }
                    }
                    break;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private void Resolve()
        {
            _resolved = true;
            if (_self?.Abilities == null) return;

            foreach (var ab in _self.Abilities)
            {
                if (ab is Location loc) _selfLoc = loc;
                if (ab is Armament arm && _primaryWeapon == null)
                {
                    // Pick first weapon that can target the target's layer
                    if (arm.Def.CanTargetLayer(_target.UnitAlertLayer))
                        _primaryWeapon = arm;
                }
            }

            if (_target?.Abilities == null) return;
            foreach (var ab in _target.Abilities)
            {
                if (ab is Location loc) { _targetLoc = loc; break; }
            }
        }

        private bool IsTargetDead()
        {
            if (_target?.Abilities == null) return true;
            foreach (var ab in _target.Abilities)
                if (ab is Health h) return h.IsDead;
            return false;
        }

        private Fixed GetTurnSpeed()
        {
            if (_self?.Abilities == null) return Fixed.Zero;
            foreach (var ab in _self.Abilities)
            {
                if (ab is Movement mv)
                    return mv.TurnSpeedDeg * FixedMath.Deg2Rad;
            }
            return Fixed.Zero;
        }

        private static Fixed GetYaw(FixedQuaternion q)
        {
            // Extract yaw from quaternion (rotation around Y axis)
            var siny = Fixed.FromInt(2) * (q.w * q.y + q.x * q.z);
            var cosy = Fixed.One - Fixed.FromInt(2) * (q.y * q.y + q.z * q.z);
            // Avoid using float — approximate with fixed atan2
            return FixedMath.Atan2(siny, cosy);
        }

        private static Fixed WrapPi(Fixed angle)
        {
            var pi = FixedMath.Pi;
            var twoPi = FixedMath.TwoPi;
            while (angle.Raw > pi.Raw) angle = angle - twoPi;
            while (angle.Raw < (-pi).Raw) angle = angle + twoPi;
            return angle;
        }

        private static FixedQuaternion YawToQuaternion(Fixed yaw)
        {
            var halfYaw = yaw * Fixed.FromRatio(1, 2);
            var sinH = FixedMath.Sin(halfYaw);
            var cosH = FixedMath.Cos(halfYaw);
            return new FixedQuaternion(Fixed.Zero, sinH, Fixed.Zero, cosH);
        }
    }
}
