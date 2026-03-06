using System;
using System.Collections.Generic;
using Game.Scripts.Fixed;

namespace Game.Grid
{
    public enum CellAnchor
    {
        LowerLeft,
        Center
    }

    public class Grid2D : IGrid
    {
        public GridLayout Layout { get; private set; }
        public FixedVector2 Origin { get; private set; }
        public FixedVector2 CellSize { get; private set; }
        public CellAnchor Anchor { get; private set; }

        // optional bounds
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool IsBounded => Width > 0 && Height > 0;

        public Grid2D(FixedVector2 origin, FixedVector2 cellSize, GridLayout layout = GridLayout.Rectangle, CellAnchor anchor = CellAnchor.LowerLeft, int width = 0, int height = 0)
        {
            if (cellSize.x == Fixed.Zero || cellSize.y == Fixed.Zero)
                throw new ArgumentException("CellSize components must be non-zero.");

            Origin = origin;
            CellSize = cellSize;
            Layout = layout;
            Anchor = anchor;
            Width = width;
            Height = height;
        }

        // Returns the world position of the cell's lower-left corner (origin) according to the Grid's anchor semantics
        public FixedVector2 CellToWorld(GridPosition cell)
        {
            if (Layout != GridLayout.Rectangle)
                throw new NotImplementedException("Only Rectangle layout implemented.");

            // base = Origin + CellSize * (cell.X, cell.Y)
            var mult = new FixedVector2(Fixed.FromInt(cell.X) * CellSize.x, Fixed.FromInt(cell.Y) * CellSize.y);
            var basePos = Origin + mult;

            if (Anchor == CellAnchor.Center)
            {
                // If origin is center of cell (0,0), then CellToWorld should return lower-left corner: subtract half cell size
                var half = CellSize * Fixed.FromRatio(1, 2);
                return basePos - half;
            }

            return basePos;
        }

        // Converts a world position to cell coordinates using mathematical floor (toward -infinity)
        public GridPosition WorldToCell(FixedVector2 worldPosition)
        {
            if (Layout != GridLayout.Rectangle)
                throw new NotImplementedException("Only Rectangle layout implemented.");

            // local = world - origin
            var local = worldPosition - Origin;

            if (Anchor == CellAnchor.Center)
            {
                // For center anchor, shift local so that cell (0,0) lower-left is at -half
                var half = CellSize * Fixed.FromRatio(1, 2);
                local = local + half; // because Origin is center, adding half aligns to lower-left grid
            }

            // fx = local.x / CellSize.x
            var fx = local.x / CellSize.x;
            var fy = local.y / CellSize.y;

            int ix = FixedFloorToInt(fx);
            int iy = FixedFloorToInt(fy);

            return new GridPosition(ix, iy);
        }

        public FixedVector2 GetCellCenterWorld(GridPosition cell)
        {
            var origin = CellToWorld(cell);
            var half = CellSize * Fixed.FromRatio(1, 2);
            return origin + half;
        }

        public (FixedVector2 center, FixedVector2 size) GetCellBounds(GridPosition cell)
        {
            var center = GetCellCenterWorld(cell);
            var size = CellSize;
            return (center, size);
        }

        public IEnumerable<GridPosition> GetCellsInArea(GridPosition fromInclusive, GridPosition toInclusive)
        {
            int x0 = Math.Min(fromInclusive.X, toInclusive.X);
            int x1 = Math.Max(fromInclusive.X, toInclusive.X);
            int y0 = Math.Min(fromInclusive.Y, toInclusive.Y);
            int y1 = Math.Max(fromInclusive.Y, toInclusive.Y);

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    yield return new GridPosition(x, y);
        }

        private static int FixedFloorToInt(Fixed v)
        {
            // explicit cast truncates toward zero; implement floor toward -infinity
            int trunc = (int)v; // truncation toward zero
            // if v >= 0 or exactly integer, return trunc
            var asFixed = Fixed.FromInt(trunc);
            if (v == asFixed || v.Raw >= 0)
                return trunc;

            // v < 0 and not integer -> floor = trunc - 1
            return trunc - 1;
        }

        // Optional helpers for bounded grid
        public bool Contains(GridPosition p)
        {
            if (!IsBounded) return true;
            return p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
        }
    }
}
