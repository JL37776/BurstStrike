using System.Collections.Generic;
using Game.Combat;
using Game.Scripts.Fixed;
using Game.Unit.Ability.BaseAbilities;
using Game.World.Logic;

namespace Game.Unit.Ability
{
    /// <summary>
    /// Runtime weapon instance attached to an Actor.
    /// Each weapon slot is one Armament. An Actor can have multiple (primary/secondary/AA).
    /// Reference: OpenRA Armament trait.
    ///
    /// This is a logic-thread-only class — no Unity dependencies.
    /// </summary>
    public sealed class Armament : IAbility
    {
        public Actor Self { get; set; }

        /// <summary>The weapon definition (immutable template).</summary>
        public readonly WeaponDef Def;

        // ── Runtime state ────────────────────────────────────────────────

        /// <summary>Remaining cooldown ticks before next fire (0 = ready).</summary>
        public int CooldownRemaining { get; private set; }

        /// <summary>Remaining warmup ticks (0 = warmup complete or not warming up).</summary>
        public int WarmupRemaining { get; private set; }

        /// <summary>Remaining burst rounds in current cycle.</summary>
        public int BurstRemaining { get; private set; }

        /// <summary>Delay ticks between burst rounds.</summary>
        private int _burstDelayRemaining;

        /// <summary>Current target being engaged (may be null).</summary>
        public Actor CurrentTarget { get; private set; }

        /// <summary>Is the weapon ready to fire (no cooldown/warmup)?</summary>
        public bool IsReady => CooldownRemaining <= 0 && WarmupRemaining <= 0 && _burstDelayRemaining <= 0;

        // ── Cached references ────────────────────────────────────────────

        private Location _selfLocation;
        private CombatRegistry _combatRegistry;

        public Armament(WeaponDef def)
        {
            Def = def;
        }

        public void Init()
        {
            CooldownRemaining = 0;
            WarmupRemaining = 0;
            BurstRemaining = 0;
            _burstDelayRemaining = 0;
            CurrentTarget = null;
            CacheReferences();
        }

        private void CacheReferences()
        {
            _selfLocation = null;
            if (Self?.Abilities == null) return;
            foreach (var ab in Self.Abilities)
            {
                if (ab is Location loc) { _selfLocation = loc; break; }
            }
        }

        private CombatRegistry GetCombatRegistry()
        {
            if (_combatRegistry != null) return _combatRegistry;
            // Resolve from LogicWorld via Actor.World
            // Actor.World is IOccupancyView which should be the LogicWorld
            if (Self?.World is LogicWorld lw)
                _combatRegistry = lw.CombatData;
            return _combatRegistry;
        }

        // ── Targeting checks ─────────────────────────────────────────────

        /// <summary>
        /// Can this weapon target the given actor? Checks domain, range, alive status.
        /// </summary>
        public bool CanTarget(Actor target)
        {
            if (target == null) return false;

            // Must be alive
            var targetHealth = FindHealth(target);
            if (targetHealth != null && targetHealth.HP <= 0) return false;

            // Layer check — can this weapon hit the target's alert layer?
            if (!Def.CanTargetLayer(target.UnitAlertLayer)) return false;

            // Range check
            if (_selfLocation == null) CacheReferences();
            if (_selfLocation == null) return false;

            var targetLoc = FindLocation(target);
            if (targetLoc == null) return false;

            var diff = _selfLocation.Position - targetLoc.Position;
            var distSq = diff.SqrMagnitude();
            var rangeFixed = Fixed.FromRaw(Def.Range);
            var rangeSq = rangeFixed * rangeFixed;
            if (distSq.Raw > rangeSq.Raw) return false;

            if (Def.MinRange > 0)
            {
                var minRangeFixed = Fixed.FromRaw(Def.MinRange);
                var minRangeSq = minRangeFixed * minRangeFixed;
                if (distSq.Raw < minRangeSq.Raw) return false;
            }

            return true;
        }

        /// <summary>Check if target is within range (does not check layer/alive).</summary>
        public bool IsInRange(Actor target)
        {
            if (target == null || _selfLocation == null) return false;
            var targetLoc = FindLocation(target);
            if (targetLoc == null) return false;

            var diff = _selfLocation.Position - targetLoc.Position;
            var distSq = diff.SqrMagnitude();
            var rangeFixed = Fixed.FromRaw(Def.Range);
            var rangeSq = rangeFixed * rangeFixed;
            return distSq.Raw <= rangeSq.Raw;
        }

        // ── Fire ─────────────────────────────────────────────────────────

        /// <summary>
        /// Attempt to fire at target. Returns true if fire initiated this tick.
        /// Call once per tick from Attack activity.
        /// </summary>
        public bool TryFire(Actor target)
        {
            if (!IsReady) return false;
            if (!CanTarget(target)) return false;

            CurrentTarget = target;

            // Start warmup if needed
            if (Def.Warmup > 0 && WarmupRemaining <= 0)
            {
                WarmupRemaining = Def.Warmup;
                return false; // warming up, will fire when warmup reaches 0
            }

            ExecuteFire(target);
            return true;
        }

        private void ExecuteFire(Actor target)
        {
            var registry = GetCombatRegistry();
            var targetLoc = FindLocation(target);
            var targetPos = targetLoc != null ? targetLoc.Position : _selfLocation.Position;

            if (Def.IsHitscan)
            {
                // Instant hit — apply warhead immediately
                ApplyHitscanDamage(target, registry);
            }
            else
            {
                // Spawn projectile
                SpawnProjectile(target, targetPos, registry);
            }

            // Burst management
            BurstRemaining = Def.Burst - 1;
            if (BurstRemaining > 0)
                _burstDelayRemaining = Def.BurstDelay;
            else
                CooldownRemaining = Def.Cooldown;
        }

        private void ApplyHitscanDamage(Actor target, CombatRegistry registry)
        {
            if (registry == null) return;
            var warhead = registry.GetWarhead(Def.WarheadId);
            if (warhead == null) return;

            if (warhead.SplashRadius > 0)
            {
                // AOE hitscan
                ApplySplashDamage(warhead, FindLocation(target)?.Position ?? _selfLocation.Position, registry);
            }
            else
            {
                // Single target
                var armorType = GetArmorType(target);
                var packet = DamageResolver.BuildPacket(
                    warhead, Def, armorType, target.UnitAlertLayer, Self);
                ApplyDamageToTarget(target, packet, warhead);
            }
        }

        private void SpawnProjectile(Actor target, FixedVector3 targetPos, CombatRegistry registry)
        {
            if (registry == null || Self?.World == null) return;
            if (!(Self.World is LogicWorld lw)) return;

            var projDef = registry.GetProjectile(Def.ProjectileId);
            if (projDef == null) return;

            // Compute muzzle position
            var muzzlePos = _selfLocation != null ? _selfLocation.Position : FixedVector3.Zero;
            // TODO: apply muzzle offset rotated by unit facing

            lw.SpawnProjectile(Def, projDef, Def.WarheadId, Self, target, muzzlePos, targetPos);
        }

        /// <summary>
        /// Apply splash damage around impact point. Called by hitscan or when projectile hits.
        /// </summary>
        public void ApplySplashDamage(WarheadDef warhead, FixedVector3 impactPos, CombatRegistry registry)
        {
            if (warhead == null || Self?.World == null) return;
            if (!(Self.World is LogicWorld lw)) return;

            var victims = lw.FindActorsInRadius(impactPos, warhead.SplashRadius);
            if (victims == null) return;

            for (int i = 0; i < victims.Count; i++)
            {
                var victim = victims[i];
                if (victim == null) continue;

                // Friendly fire check
                if (!warhead.AffectsAllies && victim.Faction == Self.Faction && victim != Self)
                    continue;
                if (!warhead.AffectsSelf && victim == Self)
                    continue;

                var victimLoc = FindLocation(victim);
                if (victimLoc == null) continue;

                int dist = Distance(impactPos, victimLoc.Position);
                int falloff = warhead.GetFalloff(dist);
                if (falloff <= 0) continue;

                var armorType = GetArmorType(victim);
                var packet = DamageResolver.BuildPacket(
                    warhead, Def, armorType, victim.UnitAlertLayer, Self, falloff);
                ApplyDamageToTarget(victim, packet, warhead);
            }
        }

        private static void ApplyDamageToTarget(Actor target, in DamagePacket packet, WarheadDef warhead)
        {
            if (target == null) return;
            var health = FindHealth(target);
            if (health == null) return;

            health.Damage(packet.Damage);

            // Apply DoT if warhead specifies it
            if (warhead != null && warhead.DotDamagePerTick > 0 && warhead.DotDuration > 0)
            {
                health.ApplyDoT(warhead.DotDamagePerTick, warhead.DotDuration,
                    warhead.DotDamageType, packet.Attacker);
            }
        }

        // ── Tick ─────────────────────────────────────────────────────────

        public void Tick()
        {
            if (CooldownRemaining > 0) CooldownRemaining--;

            // Warmup countdown
            if (WarmupRemaining > 0)
            {
                WarmupRemaining--;
                if (WarmupRemaining == 0 && CurrentTarget != null)
                {
                    // Warmup complete — fire now
                    if (CanTarget(CurrentTarget))
                        ExecuteFire(CurrentTarget);
                    else
                        CooldownRemaining = Def.Cooldown; // target lost during warmup
                }
            }

            // Burst delay
            if (_burstDelayRemaining > 0)
            {
                _burstDelayRemaining--;
                if (_burstDelayRemaining == 0 && BurstRemaining > 0)
                {
                    BurstRemaining--;
                    if (CurrentTarget != null && CanTarget(CurrentTarget))
                        ExecuteFire(CurrentTarget);
                    else
                    {
                        BurstRemaining = 0;
                        CooldownRemaining = Def.Cooldown;
                    }
                }
            }
        }

        /// <summary>Clear target (called when switching targets or activity ends).</summary>
        public void ClearTarget()
        {
            CurrentTarget = null;
            WarmupRemaining = 0;
            BurstRemaining = 0;
            _burstDelayRemaining = 0;
        }

        // ── Utility (static to avoid allocations) ────────────────────────

        private static Health FindHealth(Actor actor)
        {
            if (actor?.Abilities == null) return null;
            foreach (var ab in actor.Abilities)
                if (ab is Health h) return h;
            return null;
        }

        private static Location FindLocation(Actor actor)
        {
            if (actor?.Abilities == null) return null;
            foreach (var ab in actor.Abilities)
                if (ab is Location loc) return loc;
            return null;
        }

        private static ArmorType GetArmorType(Actor actor)
        {
            // Check for ArmorInfo ability
            if (actor?.Abilities == null) return ArmorType.None;
            foreach (var ab in actor.Abilities)
                if (ab is ArmorInfo armor) return armor.Armor;
            return ArmorType.None;
        }

        private static int DistanceSquared(FixedVector3 a, FixedVector3 b)
        {
            // Use Fixed SqrMagnitude for deterministic distance comparison.
            // Returns raw Q16.16 squared magnitude.
            var diff = a - b;
            return diff.SqrMagnitude().Raw;
        }

        private static int Distance(FixedVector3 a, FixedVector3 b)
        {
            var diff = a - b;
            return diff.Magnitude().Raw;
        }
    }
}
