using System.Collections.Generic;
using Game.Grid;
using Game.Scripts.Fixed;

namespace Game.Map
{
    /// <summary>
    /// Map API: 代表基于格子的二维地图。实现应当基于有边界的 Grid2D（有 Width/Height）。
    /// 支持按 MapLayer 标记的多层蒙版查询与寻路。
    /// </summary>
    public interface IMap
    {
        int Width { get; }
        int Height { get; }
        Grid2D Grid { get; }

        // 基础查询：如果任意层可行走则返回 true
        bool IsWalkable(GridPosition pos);
        // 按层掩码查询：当 mask 中任一位对应的层可行走时返回 true
        bool IsWalkable(GridPosition pos, MapLayer mask);
        void SetWalkable(GridPosition pos, MapLayer layer, bool walkable);
        // 旧的便捷方法：设置所有层为 walkable / blocked
        void SetWalkable(GridPosition pos, bool walkable);

        bool IsWalkableWorld(FixedVector2 worldPos);
        bool IsWalkableWorld(FixedVector2 worldPos, MapLayer mask);

        IEnumerable<GridPosition> GetNeighbors(GridPosition pos, bool allowDiagonals = true, bool allowCornerCutting = false, MapLayer? movementMask = null);

        /// <summary>
        /// 简易 A*，返回包含起点与终点的格子坐标列表；找不到返回 null。
        /// movementMask 指定可行走层的掩码（默认为所有层）。
        /// </summary>
        List<GridPosition> FindPath(GridPosition start, GridPosition goal, MapLayer? movementMask = null, bool allowDiagonals = true, bool allowCornerCutting = false);

        IEnumerable<GridPosition> GetAllWalkable();
        IEnumerable<GridPosition> GetAllWalkable(MapLayer mask);
    }
}
