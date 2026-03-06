using System;

namespace Game.Grid
{
    public struct GridPosition : IEquatable<GridPosition>
    {
        public int X;
        public int Y;

        public GridPosition(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(GridPosition other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridPosition p && Equals(p);
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => $"({X},{Y})";

        public static GridPosition Zero => new GridPosition(0, 0);
    }
}
