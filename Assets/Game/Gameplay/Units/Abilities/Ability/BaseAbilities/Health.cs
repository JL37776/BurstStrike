﻿using System;
using Game.Combat;

namespace Game.Unit.Ability.BaseAbilities
{
    public class Health : IAbility
    {
        public Actor Self { get; set; }

        public int MaxHP { get; set; } = 100;
        public int HP { get; set; } = 100;
        public bool IsDead => HP <= 0;

        // ── DoT (Damage over Time) ──────────────────────────────────────
        private int _dotDamagePerTick;
        private int _dotRemaining;
        private DamageType _dotType;
        private Actor _dotSource;

        // ── Events ──────────────────────────────────────────────────────
        /// <summary>Last actor to deal damage (for kill credit).</summary>
        public Actor LastAttacker { get; private set; }

        public void Init()
        {
            if (MaxHP <= 0) MaxHP = 1;
            if (HP <= 0) HP = MaxHP;
            if (HP > MaxHP) HP = MaxHP;
        }

        public void Tick()
        {
            if (IsDead) return;

            // Process DoT
            if (_dotRemaining > 0)
            {
                _dotRemaining--;
                Damage(_dotDamagePerTick);
                if (IsDead && _dotSource != null)
                    LastAttacker = _dotSource;
            }
        }

        public void Damage(int amount)
        {
            if (amount <= 0 || IsDead) return;
            HP -= amount;
            if (HP < 0) HP = 0;
        }

        /// <summary>
        /// Apply damage with full tracking (attacker credit, kill notification).
        /// Preferred entry point for combat system.
        /// </summary>
        public void InflictDamage(in DamagePacket packet)
        {
            if (IsDead) return;
            LastAttacker = packet.Attacker;
            Damage(packet.Damage);
        }

        /// <summary>
        /// Apply a damage-over-time effect. Overwrites any existing DoT (latest wins).
        /// Reference: RA2 fire damage, SC:BW Irradiate.
        /// </summary>
        public void ApplyDoT(int damagePerTick, int durationTicks, DamageType type, Actor source)
        {
            if (damagePerTick <= 0 || durationTicks <= 0) return;
            _dotDamagePerTick = damagePerTick;
            _dotRemaining = durationTicks;
            _dotType = type;
            _dotSource = source;
        }

        public void Heal(int amount)
        {
            if (amount <= 0 || IsDead) return;
            HP += amount;
            if (HP > MaxHP) HP = MaxHP;
        }

        /// <summary>Clear DoT effect.</summary>
        public void ClearDoT()
        {
            _dotRemaining = 0;
            _dotDamagePerTick = 0;
            _dotSource = null;
        }
    }
}