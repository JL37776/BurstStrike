using System.Collections.Generic;
using Game.Scripts.Fixed;
using Game.Grid;
using Game.Map;

namespace Game.Pathing
{
    public class GridPathResult : PathResult
    {
        private readonly IMap _map;
        private readonly List<GridPosition> _path;
        private int _index; // next index to head to

        public IMap Map => _map;

        public GridPathResult(IMap map, List<GridPosition> path)
        {
            _map = map;
            _path = path ?? new List<GridPosition>();
            _index = 0;
        }

        public override bool IsComplete => _path == null || _path.Count == 0 || _index >= _path.Count;
        public override bool HasPath => _path != null && _path.Count > 0;

        public override IReadOnlyList<GridPosition> RawPath => _path;

        public override FixedVector2? NextPoint(FixedVector2 agentWorldPos)
        {
            if (!HasPath) return null;
            // Advance index while agent is already at/near the target cell center
            while (_index < _path.Count)
            {
                var targetCenter = _map.Grid.GetCellCenterWorld(_path[_index]);
                // If agent is within half cell extents, consider reached
                var delta = agentWorldPos - targetCenter;
                var distSq = delta.SqrMagnitude();
                var half = _map.Grid.CellSize * Fixed.FromRatio(1, 2);
                var thresh = half.SqrMagnitude();
                if (distSq <= thresh)
                {
                    _index++;
                    continue;
                }

                return targetCenter;
            }

            return null;
        }
    }
}
