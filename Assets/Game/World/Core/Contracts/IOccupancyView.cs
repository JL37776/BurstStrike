namespace Game.World
{
    /// <summary>
    /// Read-only access to the current occupancy index.
    /// Exposed to abilities/actors so they can query the world without holding a direct LogicWorld reference.
    /// </summary>
    public interface IOccupancyView
    {
        LayeredOccupancyIndex Occupancy { get; }
        // Expose the map reference so actors/activities can query map data when available.
        Game.Map.IMap Map { get; }

        /// <summary>
        /// Global enemy/target search service (coarse spatial queries).
        /// Implementations must be deterministic.
        /// </summary>
        IEnemySearchService EnemySearch { get; }
    }
}
