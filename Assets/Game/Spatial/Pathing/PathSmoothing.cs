using System;
using System.Collections.Generic;
using Game.Grid;
using Game.Map;

namespace Game.Pathing
{
    /// <summary>
    /// Utility for smoothing/simplifying grid paths.
    /// Produces a reduced set of waypoints that never crosses blocked cells.
    /// </summary>
    public static class PathSmoothing
    {
        /// <summary>
        /// Simplify a raw grid path by removing intermediate nodes when there's clear line-of-walkable between them.
        /// Uses Bresenham sampling on the grid and checks IsWalkable for every sampled cell.
        /// Returns a new list (may share no storage with input).
        /// </summary>
        public static List<GridPosition> Simplify(IMap map, IReadOnlyList<GridPosition> rawPath, MapLayer mask)
        {
            if (rawPath == null || rawPath.Count == 0) return new List<GridPosition>();
            if (rawPath.Count <= 2) return new List<GridPosition>(rawPath);
            if (map == null) return new List<GridPosition>(rawPath);

            var result = new List<GridPosition>(rawPath.Count);
            int anchor = 0;
            result.Add(rawPath[anchor]);

            while (anchor < rawPath.Count - 1)
            {
                int furthest = anchor + 1;

                // Greedily extend as far as we can see.
                for (int j = rawPath.Count - 1; j > furthest; j--)
                {
                    if (HasLineOfWalkable(map, rawPath[anchor], rawPath[j], mask))
                    {
                        furthest = j;
                        break;
                    }
                }

                result.Add(rawPath[furthest]);
                anchor = furthest;
            }

            return result;
        }

        /// <summary>
        /// True if all cells on the line segment a->b are walkable for mask.
        /// Includes both endpoints.
        /// </summary>
        public static bool HasLineOfWalkable(IMap map, GridPosition a, GridPosition b, MapLayer mask)
        {
            if (map == null) return false;
            if (!map.Grid.Contains(a) || !map.Grid.Contains(b)) return false;

            // Bresenham on integer grid.
            int x0 = a.X, y0 = a.Y;
            int x1 = b.X, y1 = b.Y;

            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                var p = new GridPosition(x0, y0);
                if (!IsWalkableWithClearance4(map, p, mask))
                    return false;

                if (x0 == x1 && y0 == y1) break;

                int e2 = err * 2;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }

            return true;
        }

        private static bool IsWalkableWithClearance4(IMap map, GridPosition p, MapLayer mask)
        {
            if (!map.IsWalkable(p, mask)) return false;

            // Require 4-neighborhood to be walkable too (conservative clearance).
            // This prevents smoothing segments that skim obstacle corners.
            var n1 = new GridPosition(p.X - 1, p.Y);
            var n2 = new GridPosition(p.X + 1, p.Y);
            var n3 = new GridPosition(p.X, p.Y - 1);
            var n4 = new GridPosition(p.X, p.Y + 1);

            if (map.Grid.Contains(n1) && !map.IsWalkable(n1, mask)) return false;
            if (map.Grid.Contains(n2) && !map.IsWalkable(n2, mask)) return false;
            if (map.Grid.Contains(n3) && !map.IsWalkable(n3, mask)) return false;
            if (map.Grid.Contains(n4) && !map.IsWalkable(n4, mask)) return false;

            return true;
        }
    }
}
