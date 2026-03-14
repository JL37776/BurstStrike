using System;
using Game.Unit;

namespace Game.Combat
{
    /// <summary>
    /// Weapon template — defines WHAT a weapon can do.
    /// Separated from warhead (HOW damage applies) and projectile (HOW it flies).
    /// Reference: OpenRA Armament/WeaponInfo, RA2 [WeaponTypes], SC:BW weapon data.
    ///
    /// Loaded from YAML. All ranges/distances in logic units (1 cell = 1024).
    /// </summary>
    [Serializable]
    public sealed class WeaponDef
    {
        /// <summary>Unique weapon identifier.</summary>
        public string Id;

        // ── Range ────────────────────────────────────────────────────────

        /// <summary>Maximum attack range in logic units.</summary>
        public int Range = 5120; // 5 cells

        /// <summary>
        /// Minimum attack range (0 = none). Prevents point-blank fire for artillery.
        /// Reference: RA2 MinimumRange=.
        /// </summary>
        public int MinRange;

        // ── Timing ───────────────────────────────────────────────────────

        /// <summary>
        /// Cooldown between attacks in ticks.
        /// Reference: SC:BW Marine cooldown = 15 ticks.
        /// </summary>
        public int Cooldown = 30;

        /// <summary>
        /// Pre-fire warmup in ticks (0 = instant fire).
        /// Used for charge-up weapons (e.g. Yamato Cannon).
        /// </summary>
        public int Warmup;

        /// <summary>Number of projectiles per attack cycle (burst fire).</summary>
        public int Burst = 1;

        /// <summary>Delay between burst rounds in ticks.</summary>
        public int BurstDelay;

        // ── References ───────────────────────────────────────────────────

        /// <summary>
        /// Warhead definition ID. Determines damage, splash, armor interaction.
        /// Must resolve via CombatRegistry.
        /// </summary>
        public string WarheadId;

        /// <summary>
        /// Projectile definition ID. Determines flight behavior.
        /// Null = hitscan (instant hit, like laser/railgun).
        /// </summary>
        public string ProjectileId;

        // ── Targeting ────────────────────────────────────────────────────

        /// <summary>
        /// Which UnitAlertLayers this weapon can engage.
        /// Uses the existing flags enum for consistency with Guard ability.
        /// Reference: RA2 Primary/Secondary weapon with different target domains.
        /// </summary>
        public UnitAlertLayer ValidTargetLayers = UnitAlertLayer.Ground;

        /// <summary>
        /// If true, the unit must face the target before firing.
        /// False for turret-type weapons that can fire in any direction.
        /// </summary>
        public bool RequiresFacing = true;

        // ── Domain modifiers ─────────────────────────────────────────────

        /// <summary>Damage modifier vs land targets (percentage, 100 = 1x).</summary>
        public int VsLandModifier = 100;

        /// <summary>Damage modifier vs naval targets (percentage).</summary>
        public int VsNavalModifier = 100;

        /// <summary>Damage modifier vs air targets (percentage).</summary>
        public int VsAirModifier = 100;

        // ── Muzzle ───────────────────────────────────────────────────────

        /// <summary>
        /// Muzzle offset from unit center in local space (logic units).
        /// Projectile spawns here. Also used for muzzle flash VFX.
        /// </summary>
        public int MuzzleOffsetX;
        public int MuzzleOffsetY;
        public int MuzzleOffsetZ;

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>Is this a hitscan (instant-hit) weapon?</summary>
        public bool IsHitscan => string.IsNullOrEmpty(ProjectileId);

        /// <summary>
        /// Get domain-based damage modifier for target layer.
        /// </summary>
        public int GetDomainModifier(UnitAlertLayer targetLayer)
        {
            // Map UnitAlertLayer to domain modifier
            if ((targetLayer & (UnitAlertLayer.LowAir | UnitAlertLayer.HighAir)) != 0)
                return VsAirModifier;
            if ((targetLayer & (UnitAlertLayer.Ocean | UnitAlertLayer.Underwater)) != 0)
                return VsNavalModifier;
            return VsLandModifier;
        }

        /// <summary>
        /// Quick check: can this weapon target the given layer at all?
        /// </summary>
        public bool CanTargetLayer(UnitAlertLayer targetLayer)
        {
            return (ValidTargetLayers & targetLayer) != 0;
        }
    }
}
