using Game.Unit;

namespace Game.Combat
{
    /// <summary>
    /// Pure stateless damage computation utility.
    /// All inputs are value types or definitions — no side effects.
    /// Reference: SC:BW damage formula: base * damageType_vs_armorSize * upgrades.
    /// </summary>
    public static class DamageResolver
    {
        /// <summary>
        /// Compute final damage from a warhead hitting a target.
        /// </summary>
        /// <param name="warhead">Warhead definition.</param>
        /// <param name="weaponDef">Weapon definition (for domain modifiers).</param>
        /// <param name="targetArmorType">Target's armor type.</param>
        /// <param name="targetLayer">Target's alert layer (determines domain modifier).</param>
        /// <param name="splashFalloffPct">Splash falloff percentage (100 = at center).</param>
        /// <returns>Final integer damage value (always ≥ 0).</returns>
        public static int ComputeDamage(
            WarheadDef warhead,
            WeaponDef weaponDef,
            ArmorType targetArmorType,
            UnitAlertLayer targetLayer,
            int splashFalloffPct = 100)
        {
            if (warhead == null) return 0;

            int baseDmg = warhead.Damage;
            if (baseDmg <= 0) return 0;

            // 1) Armor modifier (from warhead's vs-armor table)
            int armorMod = warhead.GetVsArmorModifier(targetArmorType);

            // 2) Domain modifier (from weapon's vs-land/naval/air)
            int domainMod = weaponDef != null
                ? weaponDef.GetDomainModifier(targetLayer)
                : 100;

            // 3) Splash falloff
            int falloff = splashFalloffPct;

            // Final = base * armorMod/100 * domainMod/100 * falloff/100
            // Use long to avoid overflow in intermediate products
            long result = (long)baseDmg * armorMod * domainMod * falloff;
            result /= (100L * 100L * 100L);

            // Minimum 1 damage if base > 0 and all modifiers > 0
            if (result <= 0 && baseDmg > 0 && armorMod > 0 && domainMod > 0 && falloff > 0)
                return 1;

            return result > int.MaxValue ? int.MaxValue : (int)result;
        }

        /// <summary>
        /// Build a full DamagePacket for a single-target hit.
        /// </summary>
        public static DamagePacket BuildPacket(
            WarheadDef warhead,
            WeaponDef weaponDef,
            ArmorType targetArmorType,
            UnitAlertLayer targetLayer,
            Actor attacker,
            int splashFalloffPct = 100)
        {
            int dmg = ComputeDamage(warhead, weaponDef, targetArmorType, targetLayer, splashFalloffPct);
            return new DamagePacket(dmg, warhead?.DamageType ?? DamageType.Kinetic, attacker, weaponDef?.Id);
        }
    }
}
