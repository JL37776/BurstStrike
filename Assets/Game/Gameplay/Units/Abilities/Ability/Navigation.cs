using System;
using Game.Unit.Activity;
using Game.Unit;

// Movement mask is stored as a bitmask compatible with Game.Map.MapLayer values.
// We use uint here to avoid a hard dependency on the Map assembly in this lightweight ability skeleton.

namespace Game.Unit.Ability
{
    /// <summary>
    /// Navigation ability: holds which MapLayer(s) this unit can navigate on.
    /// Provides configuration for pathfinding (movement mask) and hooks for higher-level systems.
    /// This is a lightweight component intended to be attached to agents (units).
    /// </summary>
    public class Navigation : IAbility
    {
        public Actor Self { get; set; }

        /// <summary>
        /// Movement mask (default: FootUnits) indicating which map layers the unit may traverse.
        /// </summary>
        // Default to Tanks bit (1<<1)
        public uint MovementMask { get; set; } = 2u;

        /// <summary>
        /// Whether the unit should allow diagonal movement when pathfinding.
        /// </summary>
        public bool AllowDiagonals { get; set; } = true;

        /// <summary>
        /// Whether the unit is allowed to cut corners when pathfinding.
        /// </summary>
        public bool AllowCornerCutting { get; set; } = false;

        // default ctor
        public Navigation() { }

        public void Init()
        {
            // placeholder for initialization logic if needed
        }

        public void Tick()
        {
            // placeholder for per-tick updates (if navigation manages path updates)
        }
    }
}
