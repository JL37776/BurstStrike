using Game.Map;
using Game.Grid;
using Game.Scripts.Fixed;

namespace Game.Pathing
{
    public class GridAStarPathFinder : IPathFinder
    {
        public PathResult FindPath(PathRequest request)
        {
            if (request == null) return null;
            var path = request.Map.FindPath(request.StartCell, request.GoalCell, request.MovementMask, allowDiagonals: true, allowCornerCutting: false);
            if (path == null) return new GridPathResult(request.Map, null);
            return new GridPathResult(request.Map, path);
        }
    }
}
