using System;

namespace Game.Grid
{
    public struct GridCell
    {
        public GridPosition Position;
        public int TileId;

        public GridCell(GridPosition pos, int tileId = 0)
        {
            Position = pos;
            TileId = tileId;
        }
    }
}
