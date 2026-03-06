using Game.Scripts.Fixed;

namespace Game.Unit.Ability.BaseAbilities
{
    public class Movement : IAbility
    {
        private FixedVector3? _target;
        public Actor Self { get; set; }

        // Default parameters (can be overridden by UnitData/YAML)
        public Fixed MaxSpeed = Fixed.FromInt(3);
        public Fixed Acceleration = Fixed.Zero;

        // Current scalar move speed used by Move activity (units per tick)
        public Fixed Speed;

        // arrival threshold ~ 0.05 units
        public static readonly Fixed ArrivalThreshold = Fixed.FromCenti(5);

        // Cached direction towards target (updated each tick while we have a target).
        private FixedVector3 _desiredDir;
        private bool _hasDesiredDir;

        // Current velocity direction (unit-ish vector). Magnitude stored in Speed.
        private FixedVector3 _velocityDir;
        private bool _hasVelocityDir;

        // Quantized direction on dominant axis to detect reversal cheaply.
        private sbyte _desiredAxisSign;
        private sbyte _velocityAxisSign;

        private Fixed _desiredMaxSpeed;

        /// <summary>
        /// Current movement target in world space. Set by navigation/steering (e.g., Navigate).
        /// Executed by Move activity.
        /// </summary>
        public FixedVector3? Target => _target;

        /// <summary>
        /// Current velocity direction (magnitude is Speed). Useful for animation/debug.
        /// </summary>
        public FixedVector3 VelocityDir => _velocityDir;

        /// <summary>
        /// True if <see cref="VelocityDir"/> contains a meaningful direction.
        /// </summary>
        public bool HasVelocityDir => _hasVelocityDir;

        public bool HasTarget => _target.HasValue;

        /// <summary>
        /// Turn speed limit.
        /// 0 means infinite / instant turning (useful for humanoids).
        /// Unit: degrees per second when authored in YAML (if conversion enabled), otherwise degrees per tick.
        /// Note: Turning logic not implemented yet; this is config/storage only.
        /// </summary>
        public Fixed TurnSpeedDeg;

        /// <summary>
        /// If true, when close enough to target, Movement will decelerate to 0 using Acceleration and only then clear target.
        /// This is a logic/kinematics feature (not rendering). Default false to preserve existing behavior.
        /// </summary>
        public bool StopWhenArrived = false;

        private bool _isStopping;

        public Movement()
        {
            Speed = MaxSpeed;
            TurnSpeedDeg = Fixed.Zero; // default: infinite
        }

        private Location GetLocation()
        {
            foreach (var a in Self.Abilities)
            {
                if (a is Location l) return l;
            }
            return null;
        }

        private void UpdateDesiredDir(Location loc)
        {
            if (_target == null || loc == null) { _hasDesiredDir = false; _desiredAxisSign = 0; return; }
            var diff = _target.Value - loc.Position;
            var distSqr = diff.SqrMagnitude();
            if (distSqr <= ArrivalThreshold * ArrivalThreshold)
            {
                _hasDesiredDir = false;
                _desiredAxisSign = 0;
                return;
            }

            _desiredDir = diff.Normalized();
            _hasDesiredDir = true;
            _desiredAxisSign = ComputeDominantAxisSign(diff);
        }

        private static sbyte ComputeDominantAxisSign(FixedVector3 v)
        {
            var ax = Fixed.Abs(v.x);
            var ay = Fixed.Abs(v.y);
            var az = Fixed.Abs(v.z);

            if (ax >= ay && ax >= az)
                return (sbyte)(v.x.Raw >= 0 ? 1 : -1);
            if (az >= ay)
                return (sbyte)(v.z.Raw >= 0 ? 1 : -1);
            return (sbyte)(v.y.Raw >= 0 ? 1 : -1);
        }

        /// <summary>
        /// Cheap direction-reversal handling without floats.
        /// Integrates Speed and updates VelocityDir. Does not move Location.
        /// </summary>
        private void IntegrateSpeed()
        {
            // Determine active speed limit for this move
            var speedLimit = _desiredMaxSpeed.Raw != 0 ? Fixed.Min(_desiredMaxSpeed, MaxSpeed) : MaxSpeed;

            // If no acceleration is configured, treat it as instant max speed.
            if (Acceleration.Raw == 0)
            {
                Speed = speedLimit;
                if (_hasDesiredDir)
                {
                    _velocityDir = _desiredDir;
                    _hasVelocityDir = true;
                    _velocityAxisSign = _desiredAxisSign;
                }
                return;
            }

            // If we don't have a meaningful direction, keep current Speed (do not decelerate here).
            if (!_hasDesiredDir || _desiredAxisSign == 0)
                return;

            // Accelerate only while below max; once at max, keep it.
            if (Speed.Raw <= 0)
                Speed = Fixed.Zero;

            if (Speed < speedLimit)
                Speed = Fixed.Min(Speed + Acceleration, speedLimit);

            _velocityDir = _desiredDir;
            _hasVelocityDir = true;
            _velocityAxisSign = _desiredAxisSign;
        }

        /// <summary>
        /// Set movement target and desired max speed (clamped by MaxSpeed).
        /// Navigation/steering owns when to set/clear this; Movement only integrates speed.
        /// </summary>
        public void SetTarget(FixedVector3 dest, Fixed desiredMaxSpeed)
        {
            _target = dest;
            _desiredMaxSpeed = desiredMaxSpeed;

            var loc = GetLocation();
            UpdateDesiredDir(loc);

            // Do not forcibly modify Speed here. Speed is integrated in Tick() by Acceleration.
            // This is critical for "stop-before-turn" behavior, otherwise repeated SetTarget() calls
            // would keep kicking Speed above 0 and prevent us from ever reaching a full stop.

            if (_hasDesiredDir)
            {
                _velocityDir = _desiredDir;
                _hasVelocityDir = true;
                _velocityAxisSign = _desiredAxisSign;
            }
        }

        public void ClearTarget()
        {
            _target = null;
            _desiredMaxSpeed = Fixed.Zero;
            _isStopping = false;
            _hasDesiredDir = false;
            _hasVelocityDir = false;
            _desiredAxisSign = 0;
            _velocityAxisSign = 0;
        }

        // Back-compat: old name used by existing activities.
        public void MoveTo(FixedVector3 dest, Fixed speed) => SetTarget(dest, speed);

        // Back-compat: ABI used by older code.
        public FixedVector3? CurrentTarget => _target;

        public void Init()
        {
            if (MaxSpeed.Raw <= 0) MaxSpeed = Fixed.FromInt(1);
            Speed = Fixed.Min(Speed, MaxSpeed);
            if (Speed.Raw <= 0) Speed = MaxSpeed;
            _desiredMaxSpeed = Fixed.Zero;
            ClearTarget();
            if (TurnSpeedDeg.Raw < 0) TurnSpeedDeg = Fixed.Zero;
            _isStopping = false;
        }

        public void Tick()
        {
            // Pure kinematics: only integrate when we have a target or stopping is in progress.
            if (_target == null && !_isStopping) return;

            var loc = GetLocation();

            if (_target != null && loc != null)
            {
                // If we are very close, optionally enter stopping state.
                var diff = _target.Value - loc.Position;
                if (diff.SqrMagnitude() <= ArrivalThreshold * ArrivalThreshold)
                {
                    if (StopWhenArrived && Acceleration.Raw != 0)
                    {
                        _isStopping = true;
                        _target = null; // stop tracking direction; only brake
                    }
                    else
                    {
                        // legacy: immediate stop
                        ClearTarget();
                        Speed = Fixed.Zero;
                        return;
                    }
                }
            }

            if (_isStopping)
            {
                // Brake to 0, then clear.
                if (Acceleration.Raw != 0)
                    Speed = Fixed.Max(Speed - Acceleration, Fixed.Zero);
                else
                    Speed = Fixed.Zero;

                if (Speed.Raw == 0)
                    ClearTarget();
                return;
            }

            // Normal integrate
            UpdateDesiredDir(loc);
            IntegrateSpeed();

            if (_hasVelocityDir)
            {
                var _ = _velocityDir;
            }
        }
    }
}