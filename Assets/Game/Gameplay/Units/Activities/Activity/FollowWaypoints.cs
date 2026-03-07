using System.Collections.Generic;
using Game.Grid;
using Game.Map;
using Game.Scripts.Fixed;
using Game.Unit.Ability.BaseAbilities;

namespace Game.Unit.Activity
{
    /// <summary>
    /// Continuous movement along a simplified list of grid waypoints (cell centers).
    /// Unlike Navigate+Move-per-cell, this never pauses between cells.
    /// </summary>
    public sealed class FollowWaypoints : IActivity
    {
        public Actor Self { get; set; }

        private readonly IMap _map;
        private readonly List<GridPosition> _waypoints;
        private readonly Fixed _speed;

        private int _index;
        private bool _finished;

        public FollowWaypoints(IMap map, List<GridPosition> waypoints, Fixed speed, Actor self = null)
        {
            _map = map;
            _waypoints = waypoints;
            _speed = speed;
            Self = self;

            _index = 0;
            _finished = false;
        }

        public void Tick()
        {
            if (_finished) return;
            if (Self == null) { _finished = true; return; }

            Location loc = null;
            Movement movement = null;

            uint movementMaskU = (uint)Game.Map.MapLayer.Tanks;
            foreach (var ab in Self.Abilities)
            {
                if (loc == null && ab is Location l) loc = l;
                if (movement == null && ab is Movement mv) movement = mv;
                if (ab is Game.Unit.Ability.Navigation nav) movementMaskU = nav.MovementMask;
                if (loc != null && movement != null) break;
            }

            if (loc == null || _map == null || _waypoints == null || _waypoints.Count == 0)
            {
                _finished = true;
                movement?.ClearTarget();
                return;
            }

            var movementMask = (Game.Map.MapLayer)(movementMaskU == 0u ? (uint)Game.Map.MapLayer.Tanks : movementMaskU);

            // Advance index if we're already at(or very near) the current waypoint.
            while (_index < _waypoints.Count)
            {
                var c2 = _map.Grid.GetCellCenterWorld(_waypoints[_index]);
                var dst = new FixedVector3(c2.x, loc.Position.y, c2.y);
                var diff = dst - loc.Position;
                var thresh = Movement.ArrivalThreshold;
                if (diff.SqrMagnitude() <= thresh * thresh)
                {
                    _index++;
                    continue;
                }

                // Move toward current waypoint.
                var speed = _speed.Raw != 0 ? _speed : Fixed.FromMilli(100);
                movement?.MoveTo(dst, speed);

                var dir = diff.Normalized();
                var delta = dir * speed;

                FixedVector3 nextPos;
                if (delta.SqrMagnitude() >= diff.SqrMagnitude())
                    nextPos = dst;
                else
                    nextPos = new FixedVector3(loc.Position.x + delta.x, loc.Position.y + delta.y, loc.Position.z + delta.z);

                // Hard safety: don't step into blocked cells.
                var next2 = new FixedVector2(nextPos.x, nextPos.z);
                if (!_map.IsWalkableWorld(next2, movementMask))
                {
                    // stop here; current activity ends (caller may re-issue path/replan).
                    _finished = true;
                    movement?.ClearTarget();
                    return;
                }

                loc.Position = nextPos;
                return;
            }

            _finished = true;
            movement?.ClearTarget();
        }

        public bool IsFinished() => _finished;

        public void Start() { }

        public void Stop()
        {
            _finished = true;
            // ensure we don't keep a stale target and drift
            if (Self != null)
            {
                foreach (var ab in Self.Abilities)
                {
                    if (ab is Movement mv)
                    {
                        mv.ClearTarget();
                        break;
                    }
                }
            }
        }
    }
}
