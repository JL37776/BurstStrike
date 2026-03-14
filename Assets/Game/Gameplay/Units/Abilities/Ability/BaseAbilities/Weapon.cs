﻿using Game.Combat;
using Game.Scripts.Fixed;
using Game.Unit.Ability;
using Game.Unit.Activity;
using Game.World.Logic;

namespace Game.Unit.Ability.BaseAbilities
{
    /// <summary>
    /// Auto-attack behavior ability. Scans for targets and pushes AttackTarget activity.
    /// Works in conjunction with <see cref="Armament"/> (which handles actual fire/cooldown)
    /// and <see cref="Guard"/> (which handles patrol/chase behavior).
    /// 
    /// This ability ticks periodically (rate controlled by LogicWorld) to avoid
    /// expensive per-tick enemy searches.
    /// </summary>
    public class Weapon : IAbility
    {
        public Actor Self { get; set; }

        /// <summary>
        /// Scan interval override. If 0, uses LogicWorld's configured rate.
        /// </summary>
        public int ScanInterval = 15; // every ~0.5s at 30 tick/s

        private int _scanCooldown;

        public void Init()
        {
            _scanCooldown = 0;
        }

        public void Tick()
        {
            if (Self == null) return;
            if (Self.Activities == null || Self.Activities.Count == 0) return;

            // Don't interrupt if already attacking
            if (Self.Activities.Peek() is AttackTarget)
                return;

            // Rate-limit scanning
            if (_scanCooldown > 0) { _scanCooldown--; return; }
            _scanCooldown = ScanInterval > 0 ? ScanInterval : 15;

            // Find best Armament on this actor
            Armament bestArm = null;
            foreach (var ab in Self.Abilities)
            {
                if (ab is Armament arm)
                {
                    if (bestArm == null || arm.Def.Range > bestArm.Def.Range)
                        bestArm = arm;
                }
            }
            if (bestArm == null) return;

            // Find target via EnemySearchService (already built into Guard ability)
            // For auto-attack, we piggyback on Guard's target acquisition.
            // If Guard has already pushed a ChaseTarget, we don't interfere.
            if (Self.Activities.Peek() is ChaseTarget)
                return;

            // Direct target scan via world
            if (!(Self.World is LogicWorld lw)) return;
            var es = lw.EnemySearch;
            if (es == null) return;

            // Find nearest enemy within weapon range
            var selfLoc = FindLocation();
            if (selfLoc == null) return;

            var rangeFixed = Fixed.FromRaw(bestArm.Def.Range);
            var request = new Game.World.EnemySearchRequest(
                Self.World, Self, selfLoc.Position, rangeFixed,
                bestArm.Def.ValidTargetLayers);

            if (!es.TryFindNearest(in request, out var candidate))
                return;

            // Resolve candidate actor
            if (!lw.TryGetActorById(candidate.ActorId, out var enemy))
                return;

            // Push attack activity
            Self.Activities.Push(new AttackTarget(Self, enemy, allowChase: true));
        }

        private Location FindLocation()
        {
            if (Self?.Abilities == null) return null;
            foreach (var ab in Self.Abilities)
                if (ab is Location loc) return loc;
            return null;
        }
    }
}