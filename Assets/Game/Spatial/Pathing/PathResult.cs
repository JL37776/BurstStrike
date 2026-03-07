using System.Collections.Generic;
using Game.Scripts.Fixed;
using Game.Grid;

namespace Game.Pathing
{
    /// <summary>
    /// Abstract path result. Concrete implementations will provide methods to query the next target point
    /// for a moving agent, as well as information about completion, remaining path, etc.
    /// </summary>
    public abstract class PathResult
    {
        public abstract bool IsComplete { get; }
        public abstract bool HasPath { get; }

        /// <summary>
        /// Given the agent's current world position, return the next world-space waypoint the agent should head to.
        /// Implementations may return the immediate next cell center, a smoothed steer target, or null if finished.
        /// </summary>
        public abstract FixedVector2? NextPoint(FixedVector2 agentWorldPos);

        /// <summary>
        /// Flow-field specific overload: provide grid and current cell to obtain next target.
        /// Default implementations may throw or map world pos to cell and call the other NextPoint.
        /// </summary>
        public virtual FixedVector2? NextPoint(Grid2D grid, GridPosition currentCell)
        {
            // default: map cell center to world and call world-based NextPoint
            var center = grid.GetCellCenterWorld(currentCell);
            return NextPoint(center);
        }

        /// <summary>
        /// Optional: return remaining raw grid positions for debugging or low-level control.
        /// </summary>
        public abstract IReadOnlyList<GridPosition> RawPath { get; }
    }
}
