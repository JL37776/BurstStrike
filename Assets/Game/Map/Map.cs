using System;
using System.Collections.Generic;
using Game.Grid;
using Game.Scripts.Fixed;

namespace Game.Map
{
    /// <summary>
    /// 基于二维数组的地图实现（带边界）。
    /// 存储 MapLayer 掩码，每个格子指示哪些层可通行（位掩码）。
    /// </summary>
    public class Map : IMap
    {
        private uint[] _masks; // per-cell bitmask of MapLayer
        public int Width { get; private set; }
        public int Height { get; private set; }
        public Grid2D Grid { get; private set; }

        public Map(int width, int height, FixedVector2 origin, FixedVector2 cellSize)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("width/height must be > 0");
            Width = width;
            Height = height;

            _masks = new uint[width * height];
            Grid = new Grid2D(origin, cellSize, GridLayout.Rectangle, CellAnchor.LowerLeft, width, height);

            // default: all layers walkable
            for (int i = 0; i < _masks.Length; i++) _masks[i] = (uint)MapLayer.All;
        }

        private int Index(GridPosition p) => p.Y * Width + p.X;

        public bool IsWalkable(GridPosition pos)
        {
            return IsWalkable(pos, MapLayer.All);
        }

        public bool IsWalkable(GridPosition pos, MapLayer mask)
        {
            if (!Grid.Contains(pos)) return false;
            uint cell = _masks[Index(pos)];
            return ((cell & (uint)mask) != 0);
        }

        public void SetWalkable(GridPosition pos, MapLayer layer, bool walkable)
        {
            if (!Grid.Contains(pos)) throw new ArgumentOutOfRangeException(nameof(pos));
            int idx = Index(pos);
            if (walkable)
                _masks[idx] |= (uint)layer;
            else
                _masks[idx] &= ~(uint)layer;
        }

        // convenience: set all layers
        public void SetWalkable(GridPosition pos, bool walkable)
        {
            if (!Grid.Contains(pos)) throw new ArgumentOutOfRangeException(nameof(pos));
            _masks[Index(pos)] = walkable ? (uint)MapLayer.All : (uint)MapLayer.None;
        }

        public bool IsWalkableWorld(FixedVector2 worldPos)
        {
            var p = Grid.WorldToCell(worldPos);
            return IsWalkable(p);
        }

        public bool IsWalkableWorld(FixedVector2 worldPos, MapLayer mask)
        {
            var p = Grid.WorldToCell(worldPos);
            return IsWalkable(p, mask);
        }

        public IEnumerable<GridPosition> GetNeighbors(GridPosition pos, bool allowDiagonals = true, bool allowCornerCutting = false, MapLayer? movementMask = null)
        {
            var candidates = new (int dx, int dy)[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
            var diag = new (int dx, int dy)[] { (-1, -1), (-1, 1), (1, -1), (1, 1) };

            MapLayer mask = movementMask ?? MapLayer.All;

            foreach (var c in candidates)
            {
                var np = new GridPosition(pos.X + c.dx, pos.Y + c.dy);
                if (Grid.Contains(np) && IsWalkable(np, mask)) yield return np;
            }

            if (allowDiagonals)
            {
                foreach (var d in diag)
                {
                    var np = new GridPosition(pos.X + d.dx, pos.Y + d.dy);
                    if (!Grid.Contains(np) || !IsWalkable(np, mask)) continue;

                    if (!allowCornerCutting)
                    {
                        // prevent cutting corners: require both adjacent orthogonals to be free
                        var n1 = new GridPosition(pos.X + d.dx, pos.Y);
                        var n2 = new GridPosition(pos.X, pos.Y + d.dy);
                        if (!(Grid.Contains(n1) && Grid.Contains(n2) && IsWalkable(n1, mask) && IsWalkable(n2, mask)))
                            continue;
                    }

                    yield return np;
                }
            }
        }

        public List<GridPosition> FindPath(GridPosition start, GridPosition goal, MapLayer? movementMask = null, bool allowDiagonals = true, bool allowCornerCutting = false)
        {
            if (!Grid.Contains(start) || !Grid.Contains(goal)) return null;

            MapLayer mask = movementMask ?? MapLayer.All;
            if (!IsWalkable(start, mask) || !IsWalkable(goal, mask)) return null;

            var open = new HashSet<GridPosition>();
            var cameFrom = new Dictionary<GridPosition, GridPosition>();
            var gScore = new Dictionary<GridPosition, int>();
            var fScore = new Dictionary<GridPosition, int>();

            Func<GridPosition, int> h = p => (Math.Abs(p.X - goal.X) + Math.Abs(p.Y - goal.Y)) * 10;

            open.Add(start);
            gScore[start] = 0;
            fScore[start] = h(start);

            var neighbors = new (int dx, int dy, int cost)[]
            {
                (-1, 0, 10), (1, 0, 10), (0, -1, 10), (0, 1, 10),
                (-1, -1, 14), (-1, 1, 14), (1, -1, 14), (1, 1, 14)
            };

            while (open.Count > 0)
            {
                GridPosition current = default;
                int bestF = int.MaxValue;
                int bestH = int.MaxValue;
                int bestG = int.MaxValue;

                // Deterministic tie-break:
                // 1) lowest f
                // 2) lowest h (closer to goal)
                // 3) lowest g (shallower)
                // 4) lowest (x,y)
                foreach (var n in open)
                {
                    int f = fScore.ContainsKey(n) ? fScore[n] : int.MaxValue;
                    int g = gScore.ContainsKey(n) ? gScore[n] : int.MaxValue;
                    int hn = f - g;

                    bool better = false;
                    if (f < bestF) better = true;
                    else if (f == bestF)
                    {
                        if (hn < bestH) better = true;
                        else if (hn == bestH)
                        {
                            if (g < bestG) better = true;
                            else if (g == bestG)
                            {
                                if (n.X < current.X || (n.X == current.X && n.Y < current.Y)) better = true;
                            }
                        }
                    }

                    if (better)
                    {
                        bestF = f;
                        bestH = hn;
                        bestG = g;
                        current = n;
                    }
                }

                if (current.X == goal.X && current.Y == goal.Y)
                {
                    var path = new List<GridPosition> { current };
                    while (cameFrom.ContainsKey(current))
                    {
                        current = cameFrom[current];
                        path.Add(current);
                    }

                    path.Reverse();
                    return path;
                }

                open.Remove(current);

                // iterate neighbors according to allowDiagonals/allowCornerCutting
                foreach (var d in neighbors)
                {
                    if (!allowDiagonals && Math.Abs(d.dx) + Math.Abs(d.dy) == 2) continue;
                    var np = new GridPosition(current.X + d.dx, current.Y + d.dy);
                    if (!Grid.Contains(np) || !IsWalkable(np, mask)) continue;

                    if (Math.Abs(d.dx) + Math.Abs(d.dy) == 2 && !allowCornerCutting)
                    {
                        var n1 = new GridPosition(current.X + d.dx, current.Y);
                        var n2 = new GridPosition(current.X, current.Y + d.dy);
                        if (!(Grid.Contains(n1) && Grid.Contains(n2) && IsWalkable(n1, mask) && IsWalkable(n2, mask)))
                            continue;
                    }

                    int tentative = gScore[current] + d.cost;
                    if (!gScore.ContainsKey(np) || tentative < gScore[np])
                    {
                        cameFrom[np] = current;
                        gScore[np] = tentative;
                        fScore[np] = tentative + h(np);
                        open.Add(np);
                    }
                }
            }

            return null;
        }

        public IEnumerable<GridPosition> GetAllWalkable()
        {
            return GetAllWalkable(MapLayer.All);
        }

        public IEnumerable<GridPosition> GetAllWalkable(MapLayer mask)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    var p = new GridPosition(x, y);
                    if (IsWalkable(p, mask)) yield return p;
                }
        }
    }
}
