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

        // convenience
        All = 0xFFFFFFFF
    }
}
