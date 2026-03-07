using System.Collections.Generic;
using Game.Scripts.Fixed;

namespace Game.Grid
{
    public interface IGrid
    {
        GridLayout Layout { get; }
        FixedVector2 Origin { get; }
        FixedVector2 CellSize { get; }

        FixedVector2 CellToWorld(GridPosition cell);
        GridPosition WorldToCell(FixedVector2 worldPosition);
        FixedVector2 GetCellCenterWorld(GridPosition cell);
        (FixedVector2 center, FixedVector2 size) GetCellBounds(GridPosition cell);
        IEnumerable<GridPosition> GetCellsInArea(GridPosition fromInclusive, GridPosition toInclusive);
    }
}
