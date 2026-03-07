using System;
using System.Collections.Generic;
using Game.Grid;
using Game.Map;

namespace Game.Pathing
{
    // NOTE:
    // Path calculation abstraction has been merged into PathService (see PathService.CalculatePaths).
    // This file remains as a compatibility shim to avoid breaking external code that may reference PathCaculator.

    [Obsolete("Use PathService.IBatchedPathCalculator / PathService.CalculatePaths instead.")]
    public interface IPathCaculator
    {
        IReadOnlyList<List<GridPosition>> CalculatePaths(
            IMap map,
            IReadOnlyList<GridPosition> startCells,
            GridPosition goalCell,
            MapLayer movementMask,
            bool allowDiagonals,
            bool allowCornerCutting);
    }

    [Obsolete("Use PathService.CalculatePaths instead.")]
    public static class PathCaculator
    {
        public static IReadOnlyList<List<GridPosition>> CalculatePaths(
            IMap map,
            IReadOnlyList<GridPosition> startCells,
            GridPosition goalCell,
            MapLayer movementMask,
            bool allowDiagonals,
            bool allowCornerCutting)
        {
            return PathService.CalculatePaths(map, startCells, goalCell, movementMask, allowDiagonals, allowCornerCutting);
        }
    }
}
