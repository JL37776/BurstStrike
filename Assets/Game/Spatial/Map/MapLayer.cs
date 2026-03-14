using System;

namespace Game.Map
{
    [Flags]
    public enum MapLayer : uint
    {
        None = 0,
        FootUnits = 1 << 0,
        Tanks = 1 << 1,
        Naval = 1 << 2,
        Buildable = 1 << 3,
        AirLow = 1 << 4,   // Low-altitude air (helicopters) — can be hit by some ground weapons
        AirHigh = 1 << 5,  // High-altitude air (jets/bombers) — only AA weapons
        Submarine = 1 << 6, // Underwater layer

        // convenience
        AllGround = FootUnits | Tanks,
        AllAir = AirLow | AirHigh,
        AllNaval = Naval | Submarine,
        All = 0xFFFFFFFF
    }
}
