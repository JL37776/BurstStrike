using Game.Scripts.Fixed;

namespace Game.Pathing
{
    public interface IPathFinder
    {
        /// <summary>
        /// Find a path for the given request. Implementations may be synchronous or asynchronous; this interface
        /// returns a PathResult which abstracts how the path is followed.
        /// </summary>
        PathResult FindPath(PathRequest request);
    }
}
