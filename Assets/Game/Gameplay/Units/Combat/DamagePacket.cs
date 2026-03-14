using Game.Scripts.Fixed;
using Game.Unit;

namespace Game.Combat
{
    /// <summary>
    /// Immutable damage packet passed through the damage pipeline.
    /// Created by <see cref="Weapon"/> / <see cref="Projectile"/>, consumed by Health.
    /// All values are integers (fixed-point where needed) for determinism.
    /// </summary>
    public readonly struct DamagePacket
    {
        /// <summary>Final computed damage (after all modifiers).</summary>
        public readonly int Damage;

        /// <summary>Damage type for visual/audio feedback.</summary>
        public readonly DamageType Type;

        /// <summary>Who dealt the damage (for kill credit, aggro).</summary>
        public readonly Actor Attacker;

        /// <summary>Weapon def id that caused this (for UI/logging).</summary>
        public readonly string WeaponId;

        public DamagePacket(int damage, DamageType type, Actor attacker, string weaponId = null)
        {
            Damage = damage > 0 ? damage : 0;
            Type = type;
            Attacker = attacker;
            WeaponId = weaponId;
        }

        public override string ToString()
            => $"Dmg({Damage} {Type} by {Attacker?.Id ?? -1})";
    }
}
