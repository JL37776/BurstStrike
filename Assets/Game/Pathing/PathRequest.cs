using Game.Map;
using Game.Grid;
using Game.Scripts.Fixed;

namespace Game.Pathing
{
    public class PathRequest
    {
        public MapLayer MovementMask { get; }
        public IMap Map { get; }
        public GridPosition StartCell { get; }
        public GridPosition GoalCell { get; }
        public FixedVector2 StartWorld { get; }
        public FixedVector2 GoalWorld { get; }

        public PathRequest(MapLayer mask, IMap map, GridPosition startCell, GridPosition goalCell, FixedVector2 startWorld, FixedVector2 goalWorld)
        {
            MovementMask = mask;
            Map = map;
            StartCell = startCell;
            GoalCell = goalCell;
            StartWorld = startWorld;
            GoalWorld = goalWorld;
        }
    }
}
