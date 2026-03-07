using System;
using Game.Grid;
using Game.Scripts.Fixed;

namespace Game.Map
{
    /// <summary>
    /// MapData(层+高度) 到逻辑侧 IMap 的桥接实现。
    /// - Walkable 掩码语义：每个格子保存“可通行层”的 bitmask。
    /// - Heights：每个格子中心点高度（Fixed）。
    /// </summary>
    public sealed class MapWithHeight : Map
    {
        private readonly Fixed[] _heights;

        public MapWithHeight(int width, int height, FixedVector2 origin, FixedVector2 cellSize)
            : base(width, height, origin, cellSize)
        {
            _heights = new Fixed[width * height];
        }

        private int HeightIndex(GridPosition p) => p.Y * Width + p.X;

        public Fixed GetHeight(GridPosition pos)
        {
            if (!Grid.Contains(pos)) throw new ArgumentOutOfRangeException(nameof(pos));
            return _heights[HeightIndex(pos)];
        }

        public void SetHeight(GridPosition pos, Fixed height)
        {
            if (!Grid.Contains(pos)) throw new ArgumentOutOfRangeException(nameof(pos));
            _heights[HeightIndex(pos)] = height;
        }

        /// <summary>
        /// 从 MapLoader 的 MapData 构建 MapWithHeight。
        /// 约定：
        /// - MapData.Layers[y,x] 代表该格子对哪些 layer "blocked"（例如 Tanks 置位代表坦克不可走）。
        /// - IMap 存的是 walkable mask，所以这里会把 blocked 转成 walkable。
        /// </summary>
        public static MapWithHeight FromMapData(in MapData data, FixedVector2 origin, FixedVector2 cellSize)
        {
            var map = new MapWithHeight(data.width, data.height, origin, cellSize);

            for (int y = 0; y < data.height; y++)
            {
                for (int x = 0; x < data.width; x++)
                {
                    var pos = new GridPosition(x, y);

                    var blocked = data.Layers[y, x];
                    var walkable = MapLayer.All & ~blocked;

                    // SetWalkable(pos, bool) sets all layers. We want per-layer precision.
                    map.SetWalkable(pos, false);
                    map.SetWalkable(pos, walkable, true);

                    map.SetHeight(pos, data.Heights[y, x]);
                }
            }

            return map;
        }
    }
}
