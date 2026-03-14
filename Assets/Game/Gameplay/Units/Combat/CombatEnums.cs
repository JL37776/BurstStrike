namespace Game.Combat
{
    /// <summary>
    /// Damage type classification — determines interaction with armor.
    /// Reference: SC:BW damage types (Normal/Concussive/Explosive) + RA2 weapon Warhead system.
    /// </summary>
    public enum DamageType : byte
    {
        /// <summary>Normal kinetic damage (bullets, cannons). Neutral against all armor.</summary>
        Kinetic = 0,

        /// <summary>Explosive damage (missiles, bombs). Bonus vs Heavy, penalty vs Light.</summary>
        Explosive = 1,

        /// <summary>Armor-piercing (sniper, AT rounds). Bonus vs Medium/Heavy, penalty vs None.</summary>
        ArmorPiercing = 2,

        /// <summary>Concussive/anti-personnel. Bonus vs None/Light, penalty vs Heavy.</summary>
        Concussive = 3,

        /// <summary>Energy damage (laser, plasma). Ignores some armor.</summary>
        Energy = 4,

        /// <summary>Fire/incendiary. Can apply DoT.</summary>
        Fire = 5,

        /// <summary>EMP — disables units temporarily (RA2-style).</summary>
        EMP = 6,

        /// <summary>Torpedo — naval-specific explosive.</summary>
        Torpedo = 7,
    }

    /// <summary>
    /// Armor classification for damage reduction.
    /// Reference: SC:BW armor sizes (Small/Medium/Large) mapped to gameplay roles.
    /// The actual damage modifiers are defined per-warhead in <see cref="WarheadDef.VsArmor"/>.
    /// </summary>
    public enum ArmorType : byte
    {
        /// <summary>Unarmored (infantry, light scouts).</summary>
        None = 0,

        /// <summary>Light armor (light vehicles, helicopters).</summary>
        Light = 1,

        /// <summary>Medium armor (APCs, medium tanks, frigates).</summary>
        Medium = 2,

        /// <summary>Heavy armor (heavy tanks, battleships).</summary>
        Heavy = 3,

        /// <summary>Structure armor (buildings, walls).</summary>
        Structure = 4,

        /// <summary>Heroic/boss armor (special units).</summary>
        Heroic = 5,

        /// Sentinel — keep last for array sizing.
        _Count = 6,
    }

    /// <summary>
    /// Projectile flight behavior.
    /// </summary>
    public enum ProjectileType : byte
    {
        /// <summary>Straight-line bullet. No tracking.</summary>
        Bullet = 0,

        /// <summary>Tracking missile with configurable turn rate.</summary>
        Missile = 1,

        /// <summary>Parabolic arc (mortars, artillery).</summary>
        Ballistic = 2,

        /// <summary>Instant beam (laser). Applies damage on fire tick, visual persists.</summary>
        Beam = 3,

        /// <summary>Torpedo — travels at water level only.</summary>
        Torpedo = 4,
    }
}
