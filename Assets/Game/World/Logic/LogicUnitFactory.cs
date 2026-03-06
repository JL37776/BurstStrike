using System;
using System.Collections.Generic;
using Game.Map;
using Game.Scripts.Fixed;
using Game.Serialization;
using Game.Unit;
using Game.Unit.Ability.BaseAbilities;

namespace Game.World.Logic
{
    /// <summary>
    /// Logic-only factory: builds Actor graphs (root + children) from YAML-authored UnitData.
    /// 
    /// Rules:
    /// - No UnityEngine API usage.
    /// - Deterministic: ability creation order follows UnitData.Abilities order; children follow YAML list order.
    /// - Reads archetype data from IArchetypeRegistry (preloaded on main thread by World).
    /// </summary>
    internal static class LogicUnitFactory
    {
        public static Actor BuildAndAddToWorld(LogicWorld world, int unitId, int archetypeId, int factionId, int ownerPlayerId, FixedVector3 spawnWorldPos)
        {
            if (world == null) return null;

            if (!world.TryGetArchetype(archetypeId, out var data) || data == null)
            {
                // Fallback: keep previous minimal spawn behavior.
                var fallback = BuildFallback(unitId, factionId, ownerPlayerId, spawnWorldPos);
                world.AddActor(fallback);
                return fallback;
            }

            // Build root actor.
            var root = BuildActorRecursive(data, parent: null, unitId, factionId, ownerPlayerId, spawnWorldPos, world);
            world.AddActor(root);

            return root;
        }

        private static Actor BuildActorRecursive(UnitData data, Actor parent, int unitId, int factionId, int ownerPlayerId, FixedVector3 rootSpawnPos, LogicWorld world)
        {
            var actor = new Actor(factionId, ownerPlayerId)
            {
                Id = parent == null ? unitId : 0, // child id can be 0 (render snapshot uses index fallback); can be extended later.
                IsPrimaryUnit = data != null && data.IsPrimary,
                Activities = new Stack<Game.Unit.Activity.IActivity>()
            };

            actor.DebugArchetypeId = data != null ? data.Id : null;

            actor.UnitAlertLayer = data != null ? data.AlertLayer : UnitAlertLayer.Ground;
            actor.Activities.Push(new Game.Unit.Activity.IdleActivity());

            // Attach this actor to its parent (if any).
            if (parent != null)
            {
                parent.AddChild(actor);
            }

            // 1) Create abilities declared in YAML.
            if (data?.Abilities != null)
            {
                for (int i = 0; i < data.Abilities.Count; i++)
                {
                    var name = data.Abilities[i];
                    var ab = AbilityFactory.CreateAbility(name);
                    if (ab == null) continue;
                    ab.BindActor(actor);
                    actor.Abilities.Add(ab);
                }
            }

            // 2) Ensure core abilities exist if YAML omitted them.
            EnsureAbility<Location>(actor, () => new Location());
            EnsureAbility<Game.Unit.Ability.Navigation>(actor, () => new Game.Unit.Ability.Navigation());
            EnsureAbility<Game.Unit.Ability.BaseAbilities.Movement>(actor, () => new Game.Unit.Ability.BaseAbilities.Movement());
            EnsureAbility<Health>(actor, () => new Health());
            EnsureAbility<BaseUnit>(actor, () => new BaseUnit());

            // 2.1) Apply core scalar stats from YAML (not part of AbilityParams).
            // Convention: UnitData.Health is authored as MaxHP.
            if (data != null)
            {
                var h = FindAbility<Health>(actor);
                if (h != null)
                {
                    // If YAML omits/invalid, keep ability defaults; Init() will clamp.
                    if (data.Health != 0)
                        h.MaxHP = data.Health;

                    // Spawn rule: start at full health unless already configured.
                    // (If someone set HP via custom ability in the future, we don't overwrite it unless it's still default/invalid.)
                    if (h.HP <= 0 || h.HP > h.MaxHP)
                        h.HP = h.MaxHP;
                }
            }

            // 3) Set navigation mask from UnitData.Layer.
            var nav = FindAbility<Game.Unit.Ability.Navigation>(actor);
            if (nav != null)
            {
                nav.MovementMask = (uint)(data != null ? data.Layer : MapLayer.FootUnits);
                if (nav.MovementMask == 0u) nav.MovementMask = (uint)MapLayer.FootUnits;
            }

            // 4) Initialize deterministic location.
            var loc = FindAbility<Location>(actor);
            if (loc != null)
            {
                // For root, use spawnWorldPos; for child, either use parent position (rootSpawnPos) or offset.
                if (parent == null)
                {
                    loc.Position = rootSpawnPos;
                }
                else
                {
                    // Minimal: if UseParentPosition => inherit parent position; else treat UnitData.Position as relative offset.
                    if (data != null && data.UseParentPosition)
                    {
                        loc.Position = rootSpawnPos;
                    }
                    else
                    {
                        // UnitData.Position is float[]; convert directly, interpret as local offset.
                        var off = ReadPositionOffset(data);
                        loc.Position = off + rootSpawnPos;
                    }
                }

                loc.Rotation = FixedQuaternion.Identity;
            }

            // 5) Apply ability parameters from YAML.
            ApplyAbilityParams(actor, data, world);

            // 5.1) Initialize abilities (logic-side). Some abilities (e.g., Guard) perform one-time setup here.
            // NOTE: Must be done after ApplyAbilityParams so Init can observe configured values.
            // Init abilities safely: Init may add abilities (BaseUnit does), and actor.Abilities is a HashSet.
            // So we must not enumerate it directly while it can be modified.
            var initList = new List<IAbility>(actor.Abilities);
            for (int idx = 0; idx < initList.Count; idx++)
            {
                var ab = initList[idx];
                try { ab?.Init(); }
                catch { /* don't throw in logic thread */ }

                // Pull in any newly-added abilities deterministically.
                if (actor.Abilities.Count > initList.Count)
                {
                    foreach (var current in actor.Abilities)
                    {
                        if (!initList.Contains(current))
                            initList.Add(current);
                    }
                }
            }

            // 6) Recursively build children (logic-only).
            if (data?.Children != null)
            {
                for (int i = 0; i < data.Children.Count; i++)
                {
                    var child = data.Children[i];
                    if (child == null) continue;

                    // Allow lightweight child references: children:
                    //   - Id: "weapon01"
                    // In this case, resolve full UnitData from archetype registry by string Id.
                    UnitData childResolved = child;
                    if (childResolved != null
                        && !string.IsNullOrWhiteSpace(childResolved.Id)
                        && (childResolved.Abilities == null || childResolved.Abilities.Count == 0)
                        && childResolved.AbilityParams == null
                        && (childResolved.Children == null || childResolved.Children.Count == 0)
                        // NOTE: don't gate on ArchetypeId here; a ref-only child may still have ArchetypeId defaulted or set.
                        )
                    {
                        // Only treat this as a reference when it's clearly a stub.
                        if (world != null && world.TryGetArchetype(childResolved.Id, out var byString) && byString != null)
                        {
                            childResolved = byString;
                        }
                    }

                    if (world != null)
                    {
                        try
                        {
                            var dbg = WorldDebugAccess.GetRenderUnitDebugFlags();
                            if (dbg.SyncAbilities)
                            {
                                // If child is a stub reference but can't be resolved, report it.
                                if (childResolved == child
                                    && !string.IsNullOrWhiteSpace(child?.Id)
                                    && (child?.Abilities == null || child.Abilities.Count == 0)
                                    && child.AbilityParams == null)
                                {
                                    // If this ever happens, it means the archetype YAML wasn't loaded into the registry.
                                    // We intentionally avoid logging from the logic thread (no Unity API).
                                    // If you want, we can add a dedicated logic->world debug event queue.
                                    // if (!world.TryGetArchetype(child.Id, out var _tmp) || _tmp == null) { ... }
                                }
                            }
                        }
                        catch { }
                    }

                    BuildActorRecursive(childResolved, actor, unitId, factionId, ownerPlayerId, rootSpawnPos, world);
                }
            }

            return actor;
        }

        private static void ApplyAbilityParams(Actor actor, UnitData data, LogicWorld world)
        {
            if (actor == null || data == null || data.AbilityParams == null) return;

            var mp = data.AbilityParams.Movement;
            if (mp != null)
            {
                var movement = FindAbility<Game.Unit.Ability.BaseAbilities.Movement>(actor);
                if (movement != null)
                {
                    // Movement ability expects per-tick units. YAML is authored in per-second units.
                    // Convert here for the logic path (UnitComponent had a per-second conversion option).
                    var tickRate = world != null && world.TickRate > 0 ? world.TickRate : 30;
                    Fixed PerTick(Fixed perSecond) => perSecond / Fixed.FromInt(tickRate);

                    if (mp.MaxSpeed.HasValue)
                    {
                        movement.MaxSpeed = PerTick(Fixed.FromInt(mp.MaxSpeed.Value));
                        movement.Speed = movement.MaxSpeed;
                    }

                    if (mp.Acceleration.HasValue)
                        movement.Acceleration = PerTick(Fixed.FromInt(mp.Acceleration.Value));

                    if (mp.TurnSpeedDeg.HasValue)
                        movement.TurnSpeedDeg = PerTick(Fixed.FromInt(mp.TurnSpeedDeg.Value));
                }
            }

            var gp = data.AbilityParams.Guard;
            if (gp != null)
            {
                var guard = FindAbility<Game.Unit.Ability.BaseAbilities.Guard>(actor);
                if (guard == null)
                {
                    guard = new Game.Unit.Ability.BaseAbilities.Guard();
                    guard.BindActor(actor);
                    actor.Abilities.Add(guard);
                }

                if (gp.AlertRange.HasValue)
                    guard.AlertRange = Fixed.FromInt(gp.AlertRange.Value);

                if (gp.AlertLayers != null)
                    guard.AlertLayerList = gp.AlertLayers;
            }

            var wp = data.AbilityParams.BaseWeapon;
            if (wp != null)
            {
                var baseWeapon = FindAbility<Game.Unit.BaseWeapon>(actor);
                if (baseWeapon == null)
                {
                    baseWeapon = new Game.Unit.BaseWeapon();
                    baseWeapon.BindActor(actor);
                    actor.Abilities.Add(baseWeapon);
                }

                if (wp.Damage.HasValue) baseWeapon.Damage = wp.Damage.Value;
                if (wp.Range.HasValue) baseWeapon.Range = wp.Range.Value;

                // Attachment settings and local offset come from UnitData.
                baseWeapon.UseParentPosition = data.UseParentPosition;
                baseWeapon.BindParentRotation = data.BindParentRotation;
                baseWeapon.LocalOffset = ReadPositionOffset(data);
            }
        }

        private static T FindAbility<T>(Actor actor) where T : class, IAbility
        {
            if (actor == null) return null;
            foreach (var ab in actor.Abilities)
                if (ab is T t) return t;
            return null;
        }

        private static void EnsureAbility<T>(Actor actor, Func<T> create) where T : class, IAbility
        {
            if (actor == null) return;
            foreach (var ab in actor.Abilities)
                if (ab is T) return;

            var a = create?.Invoke();
            if (a == null) return;
            a.BindActor(actor);
            actor.Abilities.Add(a);
        }

        private static Actor BuildFallback(int unitId, int factionId, int ownerPlayerId, FixedVector3 spawnPos)
        {
            var actor = new Actor(factionId, ownerPlayerId)
            {
                Id = unitId,
                IsPrimaryUnit = true,
                Activities = new Stack<Game.Unit.Activity.IActivity>()
            };

            actor.UnitAlertLayer = UnitAlertLayer.Ground;
            actor.Activities.Push(new Game.Unit.Activity.IdleActivity());

            var baseUnit = new BaseUnit();
            baseUnit.BindActor(actor);
            actor.Abilities.Add(baseUnit);

            var loc = new Location();
            loc.BindActor(actor);
            loc.Position = spawnPos;
            loc.Rotation = FixedQuaternion.Identity;
            actor.Abilities.Add(loc);

            var health = new Health();
            health.BindActor(actor);
            actor.Abilities.Add(health);

            var movement = new Game.Unit.Ability.BaseAbilities.Movement();
            movement.BindActor(actor);
            actor.Abilities.Add(movement);

            var nav = new Game.Unit.Ability.Navigation();
            nav.BindActor(actor);
            actor.Abilities.Add(nav);

            return actor;
        }

        private static FixedVector3 ReadPositionOffset(UnitData data)
        {
            if (data?.Position == null || data.Position.Length < 3)
                return FixedVector3.Zero;

            return new FixedVector3(
                Fixed.FromFloat(data.Position[0]),
                Fixed.FromFloat(data.Position[1]),
                Fixed.FromFloat(data.Position[2]));
        }
    }
}
