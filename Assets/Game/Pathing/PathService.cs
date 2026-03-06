using System;
using System.Collections.Generic;
using Game.Map;
using Game.Grid;

namespace Game.Pathing
{
    public static class PathService
    {
        private static IPathFinder _default = new GridAStarPathFinder();
        private static FlowFieldPathFinder _flowBuilder = new FlowFieldPathFinder();

        // Cache: (map instance, goal cell, movement mask) -> shared flow field.
        // NOTE: LogicWorld runs on one thread; if you later build flow fields on multiple threads,
        // wrap this with a lock or switch to ConcurrentDictionary.
        private static readonly Dictionary<(IMap map, int gx, int gy, MapLayer mask), FlowFieldPathResult> _flowCache =
            new Dictionary<(IMap, int, int, MapLayer), FlowFieldPathResult>();

        public static void ClearFlowFieldCache() => _flowCache.Clear();

        public static void SetDefault(IPathFinder finder)
        {
            _default = finder ?? throw new ArgumentNullException(nameof(finder));
        }

        public static PathResult Submit(PathRequest request)
        {
            return _default.FindPath(request);
        }

        /// <summary>
        /// Point-to-point synchronous A* (returns GridPathResult wrapping the full A* path)
        /// </summary>
        public static PathResult FindPathPointToPoint(IMap map, GridPosition startCell, GridPosition goalCell, MapLayer mask)
        {
            var req = new PathRequest(mask, map, startCell, goalCell, map.Grid.GetCellCenterWorld(startCell), map.Grid.GetCellCenterWorld(goalCell));
            return Submit(req);
        }

        /// <summary>
        /// Build (or reuse) a shared flow-field for multiple agents that head to the same goal.
        /// Returns <see cref="FlowFieldPathResult"/>.
        /// </summary>
        public static FlowFieldPathResult CreateFlowField(IMap map, GridPosition goalCell, MapLayer mask)
        {
            if (map == null) return null;

            var key = (map, goalCell.X, goalCell.Y, mask);
            if (_flowCache.TryGetValue(key, out var cached) && cached != null && cached.HasPath)
                return cached;

            var built = _flowBuilder.Build(map, goalCell, mask);
            _flowCache[key] = built;
            return built;
        }

        public enum PathMode
        {
            AStar = 0,
            FlowField = 1,
        }

        /// <summary>
        /// Batch path calculation forced to A* (runs A* once per start cell and returns raw waypoint lists).
        /// </summary>
        public static IReadOnlyList<List<GridPosition>> CalculatePathsAStar(
            IMap map,
            IReadOnlyList<GridPosition> startCells,
            GridPosition goalCell,
            MapLayer movementMask,
            bool allowDiagonals,
            bool allowCornerCutting)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (startCells == null) throw new ArgumentNullException(nameof(startCells));

            var results = new List<List<GridPosition>>(startCells.Count);
            for (int i = 0; i < startCells.Count; i++)
            {
                var startCell = startCells[i];
                var raw = map.FindPath(startCell, goalCell, movementMask, allowDiagonals, allowCornerCutting);
                results.Add(raw);
            }
            return results;
        }

        /// <summary>
        /// Batch path calculation forced to flow-field.
        /// Contract (planned): build/reuse a shared flow-field for (map, goal, mask), and return a PathResult suitable for Navigate.
        ///
        /// Not implemented yet.
        /// </summary>
        public static IReadOnlyList<PathResult> CalculatePathsFlowField(
            IMap map,
            IReadOnlyList<GridPosition> startCells,
            GridPosition goalCell,
            MapLayer movementMask,
            bool allowDiagonals,
            bool allowCornerCutting)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (startCells == null) throw new ArgumentNullException(nameof(startCells));

            // Build/reuse a single shared flow-field for this goal.
            var flow = CreateFlowField(map, goalCell, movementMask);

            var results = new List<PathResult>(startCells.Count);
            for (int i = 0; i < startCells.Count; i++)
            {
                var sc = startCells[i];
                // Convert the shared flow-field into an A*-like raw path for this start.
                // This keeps downstream behavior identical to A* (Navigate expects GridPathResult today).
                var raw = flow != null ? flow.BuildRawPath(sc) : null;
                if (raw != null && raw.Count > 0)
                    results.Add(new GridPathResult(map, raw));
                else
                    results.Add(new GridPathResult(map, null));
            }

            return results;
        }

        /// <summary>
        /// Compatibility helper: returns A* raw paths (legacy signature).
        /// Prefer calling CalculatePathsAStar / CalculatePathsFlowField explicitly.
        /// </summary>
        public static IReadOnlyList<List<GridPosition>> CalculatePaths(
            IMap map,
            IReadOnlyList<GridPosition> startCells,
            GridPosition goalCell,
            MapLayer movementMask,
            bool allowDiagonals,
            bool allowCornerCutting)
        {
            return CalculatePathsAStar(map, startCells, goalCell, movementMask, allowDiagonals, allowCornerCutting);
        }

        /// <summary>
        /// Auto-select batch pathfinding based on unit count.
        /// - If forcedMode is provided, that mode is used.
        /// - Otherwise, if startCells.Count <= aStarUnitThreshold => A*, else => FlowField.
        /// Returns raw grid waypoint lists (same contract as A*).
        /// </summary>
        public static IReadOnlyList<List<GridPosition>> CalculatePathsAuto(
            IMap map,
            IReadOnlyList<GridPosition> startCells,
            GridPosition goalCell,
            MapLayer movementMask,
            bool allowDiagonals,
            bool allowCornerCutting,
            int aStarUnitThreshold,
            PathMode? forcedMode = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (startCells == null) throw new ArgumentNullException(nameof(startCells));

            var mode = forcedMode ?? (startCells.Count <= aStarUnitThreshold ? PathMode.AStar : PathMode.FlowField);
            switch (mode)
            {
                case PathMode.AStar:
                    return CalculatePathsAStar(map, startCells, goalCell, movementMask, allowDiagonals, allowCornerCutting);

                case PathMode.FlowField:
                {
                    // Build/reuse a shared flow-field once, then reconstruct raw paths per unit.
                    var flow = CreateFlowField(map, goalCell, movementMask);
                    var results = new List<List<GridPosition>>(startCells.Count);
                    for (int i = 0; i < startCells.Count; i++)
                    {
                        var sc = startCells[i];
                        var raw = flow != null ? flow.BuildRawPath(sc) : null;
                        results.Add(raw);
                    }
                    return results;
                }

                default:
                    return CalculatePathsAStar(map, startCells, goalCell, movementMask, allowDiagonals, allowCornerCutting);
            }
        }
    }
}
