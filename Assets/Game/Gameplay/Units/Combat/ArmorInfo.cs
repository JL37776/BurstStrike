using Game.Combat;

namespace Game.Unit.Ability
{
    /// <summary>
    /// Armor type trait — determines how warhead damage is modified.
    /// Passive (no Tick work). Set from YAML archetype data.
    /// Reference: SC:BW unit size class (Small/Medium/Large).
    /// </summary>
    public sealed class ArmorInfo : IAbility
    {
        public Actor Self { get; set; }

        /// <summary>This unit's armor classification.</summary>
        public ArmorType Armor { get; set; } = ArmorType.None;

        public ArmorInfo() { }

        public ArmorInfo(ArmorType armor)
        {
            Armor = armor;
        }

        public void Init() { }
        public void Tick() { }
    }
}
