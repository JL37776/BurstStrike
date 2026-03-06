using System.Collections.Generic;
using Game.Grid;
using Game.Map;
using Game.Scripts.Fixed;

namespace Game.Pathing
{
    /// <summary>
    /// Flow field result: stores, for each cell, the next cell index to move toward the goal.
    /// NextPoint can be queried by providing the agent's current GridPosition (or world pos via override).
    /// </summary>
    public class FlowFieldPathResult : PathResult
    {
        private readonly IMap _map;
        private readonly GridPosition[] _next; // next cell to move to from index (or same if goal), or invalid if unreachable
        private readonly int _width;
        private readonly int _height;

        // sentinel for unreachable
        private static readonly GridPosition Invalid = new GridPosition(int.MinValue, int.MinValue);

        public FlowFieldPathResult(IMap map, GridPosition[] next, int width, int height)
        {
            _map = map;
            _next = next;
            _width = width;
            _height = height;
        }

        public override bool IsComplete => false; // flow-field is perpetual until disposed
        public override bool HasPath => _next != null;

        public override IReadOnlyList<GridPosition> RawPath => null;

        // default implementation: convert world pos -> cell and call cell-based next
        public override FixedVector2? NextPoint(FixedVector2 agentWorldPos)
        {
            var cell = _map.Grid.WorldToCell(agentWorldPos);
            return NextPoint(_map.Grid, cell);
        }

        // Provide a grid-aware NextPoint which accepts the grid and current cell.
        public override FixedVector2? NextPoint(Grid2D grid, GridPosition currentCell)
        {
            if (_next == null) return null;
            if (!grid.Contains(currentCell) || currentCell.X < 0 || currentCell.Y < 0 || currentCell.X >= _width || currentCell.Y >= _height)
                return null;
            int idx = currentCell.Y * _width + currentCell.X;
            var n = _next[idx];
            if (n.Equals(Invalid)) return null;
            return grid.GetCellCenterWorld(n);
        }

        // optional: get raw next cell
        public GridPosition? NextCell(GridPosition currentCell)
        {
            if (_next == null) return null;
            if (currentCell.X < 0 || currentCell.Y < 0 || currentCell.X >= _width || currentCell.Y >= _height) return null;
            var n = _next[currentCell.Y * _width + currentCell.X];
            if (n.Equals(Invalid)) return null;
            return n;
        }

        private int IndexOf(GridPosition p) => p.Y * _width + p.X;

        /// <summary>
        /// Reconstruct a raw grid path (like A*) from a given start cell by following the flow-field next pointers.
        /// This is deterministic and cheap compared to per-unit A*.
        /// Returns null if start is invalid/unreachable.
        /// </summary>
        public List<GridPosition> BuildRawPath(GridPosition startCell, int maxSteps = 4096)
        {
            if (_map == null || _next == null) return null;
            if (startCell.X < 0 || startCell.Y < 0 || startCell.X >= _width || startCell.Y >= _height)
                return null;

            var grid = _map.Grid;
            if (grid == null || !grid.Contains(startCell)) return null;

            var path = new List<GridPosition>(32);
            var cur = startCell;

            // Track visited to prevent infinite loops if next field is malformed.
            var visited = new HashSet<int>();

            for (int step = 0; step < maxSteps; step++)
            {
                int idx = IndexOf(cur);
                if (!visited.Add(idx))
                    return null;

                path.Add(cur);

                var n = _next[idx];
                if (n.Equals(Invalid))
                    return null;

                // reached goal when next points to self
                if (n.X == cur.X && n.Y == cur.Y)
                    return path;

                // safety: ensure the next cell is within bounds
                if (n.X < 0 || n.Y < 0 || n.X >= _width || n.Y >= _height)
                    return null;

                cur = n;
            }

            // exceeded max steps
            return null;
        }
    }
}
