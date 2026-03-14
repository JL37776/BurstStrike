using System;

namespace Game.Combat
{
    /// <summary>
    /// Warhead template — determines HOW damage is applied on impact.
    /// Separated from weapon so the same warhead can be reused by multiple weapons.
    /// Reference: OpenRA WarheadInfo / RA2 [Warheads] section.
    /// 
    /// Loaded from YAML. All fields are simple types for serialization.
    /// </summary>
    [Serializable]
    public sealed class WarheadDef
    {
        /// <summary>Unique identifier (referenced by WeaponDef.WarheadId).</summary>
        public string Id;

        /// <summary>Base damage value before modifiers.</summary>
        public int Damage;

        /// <summary>Damage type (interacts with armor table).</summary>
        public DamageType DamageType;

        /// <summary>
        /// Area-of-effect splash radius in logic units (1 cell = 1024).
        /// 0 = single-target only.
        /// </summary>
        public int SplashRadius;

        /// <summary>
        /// Splash damage falloff by distance band (percentages, 100 = full damage).
        /// Index 0 = center, last index = edge of splash radius.
        /// Example: [100, 75, 50, 25] → 4 equal bands.
        /// Null/empty = uniform full damage across entire splash.
        /// </summary>
        public int[] SplashFalloff;

        /// <summary>
        /// Per-armor-type damage modifier (percentage, 100 = 1x).
        /// Index matches <see cref="ArmorType"/> ordinal.
        /// Reference: SC:BW damage table — Concussive: [100, 50, 25], Explosive: [50, 75, 100].
        /// Null = 100% against all armor types.
        /// </summary>
        public int[] VsArmor;

        /// <summary>Whether splash hits friendly units.</summary>
        public bool AffectsAllies;

        /// <summary>Whether splash hits the attacker.</summary>
        public bool AffectsSelf;

        /// <summary>
        /// If > 0, applies damage-over-time: this much damage per tick for DotDuration ticks.
        /// Reference: RA2 fire damage, SC:BW Irradiate.
        /// </summary>
        public int DotDamagePerTick;

        /// <summary>DoT duration in ticks.</summary>
        public int DotDuration;

        /// <summary>DoT damage type (may differ from primary, e.g. Fire).</summary>
        public DamageType DotDamageType;

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Get damage modifier percentage for a given armor type.
        /// Returns 100 (1x) if no table is defined or index out of range.
        /// </summary>
        public int GetVsArmorModifier(ArmorType armor)
        {
            int idx = (int)armor;
            if (VsArmor == null || idx < 0 || idx >= VsArmor.Length)
                return 100;
            return VsArmor[idx];
        }

        /// <summary>
        /// Get splash falloff percentage at a given distance from impact center.
        /// Returns 0 if distance exceeds splash radius.
        /// </summary>
        public int GetFalloff(int distance)
        {
            if (SplashRadius <= 0) return 100;
            if (distance < 0) distance = 0;
            if (distance >= SplashRadius) return 0;

            if (SplashFalloff == null || SplashFalloff.Length == 0)
                return 100; // uniform

            // Map distance to band index
            int bandIdx = distance * SplashFalloff.Length / SplashRadius;
            if (bandIdx >= SplashFalloff.Length) bandIdx = SplashFalloff.Length - 1;
            return SplashFalloff[bandIdx];
        }
    }
}
