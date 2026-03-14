using System;

namespace Game.Combat
{
    /// <summary>
    /// Projectile template — determines HOW the projectile flies.
    /// Reference: OpenRA Bullet/Missile projectile info, RA2 projectile types.
    ///
    /// Null projectile on a weapon = hitscan (instant damage, no flight).
    /// Loaded from YAML. All numeric values in logic units for determinism.
    /// </summary>
    [Serializable]
    public sealed class ProjectileDef
    {
        /// <summary>Unique identifier (referenced by WeaponDef.ProjectileId).</summary>
        public string Id;

        /// <summary>Flight behavior type.</summary>
        public ProjectileType Type;

        /// <summary>
        /// Flight speed in logic units per tick (1 cell = 1024).
        /// Higher = faster projectile. Typical bullet: 300-500, missile: 100-200.
        /// </summary>
        public int Speed = 300;

        /// <summary>
        /// Maximum lifetime in ticks before auto-destroy (prevents orphaned projectiles).
        /// Default 300 ticks ≈ 5 seconds @ 60 tick/s.
        /// </summary>
        public int MaxLifetime = 300;

        /// <summary>
        /// Tracking strength (0-100). Only used for Missile/Torpedo types.
        /// 0 = no tracking (dumb-fire), 100 = perfect tracking.
        /// </summary>
        public int TrackingStrength;

        /// <summary>
        /// Turn rate for tracking projectiles (degrees per tick, scaled by 1024).
        /// Higher = tighter turns. 0 = infinite turn (instant facing).
        /// </summary>
        public int TurnRate;

        /// <summary>
        /// Arc peak height for Ballistic type (logic units above ground plane).
        /// Higher = more lobbed trajectory.
        /// </summary>
        public int ArcHeight;

        /// <summary>
        /// Accuracy spread radius in logic units. 0 = perfect accuracy.
        /// On fire, the actual target position is offset randomly within this radius.
        /// Reference: RA2 Inaccurate=yes.
        /// </summary>
        public int Inaccuracy;

        /// <summary>
        /// If true, projectile passes through the first target and keeps flying.
        /// Reference: SC:BW Lurker spine attack.
        /// </summary>
        public bool Piercing;

        /// <summary>Damage retention after each pierce (percentage). 50 = half damage after each.</summary>
        public int PierceFalloff = 50;

        /// <summary>Maximum number of units the projectile can pierce through.</summary>
        public int MaxPierceCount = 1;

        /// <summary>
        /// Hit detection radius in logic units.
        /// Projectile "hits" when distance to target ≤ this value.
        /// </summary>
        public int HitRadius = 128; // ~1/8 cell
    }
}
