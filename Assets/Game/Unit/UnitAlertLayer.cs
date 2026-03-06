using System;

namespace Game.Unit
{
    /// <summary>
    /// Alert/guard target layers (what this unit can detect/engage).
    /// Kept separate from MapLayer (movement/occupancy) so design can evolve independently.
    /// 
    /// Note: this is a flags enum so units can combine multiple layers.
    /// </summary>
    [Flags]
    public enum UnitAlertLayer : uint
    {
        None = 0,

        // As requested
        Underwater = 1u << 0,
        Ocean = 1u << 1,
        Ground = 1u << 2,
        LowAir = 1u << 3,
        HighAir = 1u << 4,

        All = 0xFFFFFFFFu
    }
}
