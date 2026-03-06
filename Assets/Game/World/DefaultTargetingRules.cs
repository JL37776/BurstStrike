using Game.Unit;

namespace Game.World
{
    /// <summary>
    /// Default deterministic targeting rules.
    /// Hostile if faction differs.
    /// </summary>
    internal sealed class DefaultTargetingRules : ITargetingRules
    {
        public bool IsHostile(Actor self, Actor other)
        {
            if (self == null || other == null) return false;
            return self.Faction != other.Faction;
        }

        public bool CanTarget(Actor self, Actor other)
        {
            if (self == null || other == null) return false;
            // Placeholder for future: dead, invisible, neutral, etc.
            return true;
        }
    }
}
