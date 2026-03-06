using System;
using System.Collections.Generic;

namespace Game.World
{
    /// <summary>
    /// YAML-authored world configuration.
    /// Keys are PascalCase to match existing YAML conventions (NullNamingConvention).
    /// </summary>
    [Serializable]
    public sealed class WorldConfigData
    {
        public TickRatesConfig TickRates;

        [Serializable]
        public sealed class TickRatesConfig
        {
            public AbilityTickRatesConfig Ability;
            public ActivityTickRatesConfig Activity;
        }

        [Serializable]
        public sealed class AbilityTickRatesConfig
        {
            // 0 = every logic tick, otherwise every N logic ticks.
            public int Movement;
            public int Guard;
            public int Weapon;
            public int Navigation;
        }

        [Serializable]
        public sealed class ActivityTickRatesConfig
        {
            // 0 = every logic tick, otherwise every N logic ticks.
            public int GuardActivity;
            public int ChaseTarget;
            public int Navigate;
            public int Move;
            public int Idle;
        }

        public static WorldConfigData CreateDefault()
        {
            return new WorldConfigData
            {
                TickRates = new TickRatesConfig
                {
                    Ability = new AbilityTickRatesConfig(),
                    Activity = new ActivityTickRatesConfig(),
                }
            };
        }

        public IEnumerable<string> Validate()
        {
            if (TickRates == null) yield return "TickRates is null";
        }
    }
}
