using System;
using System.Collections.Generic;
using Game.Core;
using Game.Scripts.Fixed;
using UnityEngine;
using Game.Unit;
using Game.Unit.Ability.BaseAbilities;
using Game.Unit.Ability;
using Game.Unit.Activity;
using Game.Map;

namespace Game.Serialization
{
    // Simple runtime component that holds UnitData on the GameObject
    public class UnitComponent : MonoBehaviour
    {
        public UnitData Data;
        public Actor Actor { get; private set; }

        [Header("Enemy Search (Debug)")]
        [SerializeField] private int enemyPartitionX;
        [SerializeField] private int enemyPartitionY;
        [SerializeField] private int enemyPartitionCellSize;

        public int EnemyPartitionX => enemyPartitionX;
        public int EnemyPartitionY => enemyPartitionY;
        public int EnemyPartitionCellSize => enemyPartitionCellSize;

        /// <summary>
        /// Main-thread debug only: update cached partition coordinates so they show up in Inspector.
        /// This should NOT be used by logic.
        /// </summary>
        public void DebugUpdateEnemyPartition(int px, int py, int cellSize)
        {
            enemyPartitionX = px;
            enemyPartitionY = py;
            enemyPartitionCellSize = cellSize;
        }

        [Header("Ability Param Units")]
        [Tooltip("If true, treat Movement.MaxSpeed/Acceleration in YAML as units-per-second and units-per-second^2, then convert by World.tickRate. If false, treat them as per-tick units.")]
        public bool movementParamsArePerSecond = true;

        public void ApplyData(UnitData data, Actor parentActor, int faction, int playerId, Color color)
        {
            Data = data;
            gameObject.name = string.IsNullOrEmpty(data.Id) ? "Unit" : data.Id;

            // Setup transform
            if (data.UseParentPosition && parentActor != null)
            {
                // If use parent position, keep localPosition as provided
                transform.localPosition = data.ToVector3();
            }
            else
            {
                transform.position = data.ToVector3();
            }

            // If requested, bind rotation to parent so this object's rotation follows the parent
            if (data.BindParentRotation && transform.parent != null)
            {
                transform.localRotation = Quaternion.identity;
            }

            // Create actor and link to parent if provided
            Actor = new Actor(faction, playerId);
            Actor.IsPrimaryUnit = data != null && data.IsPrimary;

            // Apply unit alert layer membership (used by global enemy search filtering)
            Actor.UnitAlertLayer = data != null ? data.AlertLayer : Game.Unit.UnitAlertLayer.Ground;

            // Ensure the actor is tick-safe even before being registered into LogicWorld.
            Actor.Activities = new Stack<Game.Unit.Activity.IActivity>();
            Actor.Activities.Push(new Game.Unit.Activity.IdleActivity());

            // Optional: if UnitData.Id is an integer string, use it as stable actor id.
            if (!string.IsNullOrEmpty(data.Id) && int.TryParse(data.Id, out var parsedId))
                Actor.Id = parsedId;

            if (parentActor != null)
            {
                Actor.parent = parentActor;
            }

            // Attach abilities from data
            var abilityNames = data.Abilities;
            var created = new System.Collections.Generic.List<IAbility>();
            if (abilityNames != null)
            {
                foreach (var abilityName in abilityNames)
                {
                    var a = AbilityFactory.CreateAbility(abilityName);
                    if (a != null) created.Add(a);
                }
            }

            // add created abilities to actor
            foreach (IAbility ab in created)
            {
                ab.BindActor(Actor);
                Actor.Abilities.Add(ab);
            }

            // Ensure core Navigation ability exists so pathfinding uses the correct layer mask.
            Navigation navAbility = null;
            foreach (var ab in Actor.Abilities)
            {
                if (ab is Navigation n) { navAbility = n; break; }
            }
            if (navAbility == null)
            {
                navAbility = new Navigation();
                navAbility.BindActor(Actor);
                Actor.Abilities.Add(navAbility);
            }

            // Align navigation mask with UnitData.Layer (e.g., Tanks).
            // UnitData.Layer uses Game.Map.MapLayer flags.
            navAbility.MovementMask = (uint)(data != null ? data.Layer : MapLayer.FootUnits);
            if (navAbility.MovementMask == 0u) navAbility.MovementMask = (uint)MapLayer.FootUnits;

            // After BaseUnit Init, ensure Location starts at the GameObject position.
            // (UseParentPosition already applied to Transform above.)
            foreach (var ab in Actor.Abilities)
            {
                if (ab is Location loc)
                {
                    var p = transform.position;
                    loc.Position = new FixedVector3(Fixed.FromFloat(p.x), Fixed.FromFloat(p.y), Fixed.FromFloat(p.z));

                    // Ensure deterministic initial rotation for rendering.
                    if (loc.Rotation.w.Raw == 0 && loc.Rotation.x.Raw == 0 && loc.Rotation.y.Raw == 0 && loc.Rotation.z.Raw == 0)
                        loc.Rotation = FixedQuaternion.Identity;

                    break;
                }
            }

            // Initialize abilities iteratively: if an ability's Init() adds more abilities (like BaseUnit),
            // bind and initialize those as well until no new abilities are added.
            var initialized = new System.Collections.Generic.HashSet<IAbility>();
            var toInit = new System.Collections.Generic.List<IAbility>();
            foreach (var a in created) { initialized.Add(a); toInit.Add(a); }
            while (toInit.Count > 0)
            {
                // Call Init on the current batch
                foreach (var ab in toInit)
                {
                    ab.Init();
                }

                // Find abilities that were added during Init
                var newlyAdded = new System.Collections.Generic.List<IAbility>();
                foreach (var ab in Actor.Abilities)
                {
                    if (!initialized.Contains(ab))
                        newlyAdded.Add(ab);
                }

                // Bind actor to newly added and mark them for initialization in next loop
                foreach (var ab in newlyAdded)
                {
                    if (ab.Self == null)
                        ab.BindActor(Actor);
                    initialized.Add(ab);
                }

                toInit = newlyAdded;
            }

            // Make sure all abilities have the Actor bound (some abilities might be added inside Init)
            foreach (var ab in Actor.Abilities)
            {
                if (ab.Self == null)
                    ab.BindActor(Actor);
            }

            // Apply ability parameters from YAML (if provided)
            ApplyAbilityParams(data);

            // Attach combat abilities from UnitData (ArmorInfo, Armament)
            ApplyCombatData(data);

            // Debug: list attached ability types for verification
            var attached = new System.Text.StringBuilder();
            foreach (var ab in Actor.Abilities)
            {
                if (attached.Length > 0) attached.Append(", ");
                attached.Append(ab.GetType().Name);
            }
            GameLog.Info(GameLog.Tag.Unit, $"Unit '{Data.Id}' attached abilities: {attached}");

            // Recursively create child UnitComponents if children exist
            if (data.Children != null)
            {
                foreach (var child in data.Children)
                {
                    // Create empty GameObject for child and attach component
                    var childGo = new GameObject();
                    childGo.transform.SetParent(this.transform, worldPositionStays: true);
                    var uc = childGo.AddComponent<UnitComponent>();
                    uc.ApplyData(child, Actor, faction, playerId, color);
                    // Register actor relationship
                    if (uc.Actor != null && Actor != null)
                    {
                        Actor.AddChild(uc.Actor);
                    }
                }
            }
        }

        private void ApplyAbilityParams(UnitData data)
        {
            if (data == null || data.AbilityParams == null) return;

            // Movement
            var mp = data.AbilityParams.Movement;
            if (mp != null)
            {
                Movement movement = null;
                foreach (var ab in Actor.Abilities)
                {
                    if (ab is Movement m) { movement = m; break; }
                }

                if (movement != null)
                {
                    // Determine tickRate if we convert from per-second
                    var tickRate = 0;
                    if (movementParamsArePerSecond)
                    {
                        // try find a World in scene
                        var world = FindObjectOfType<Game.World.World>();
                        tickRate = world != null ? Mathf.Max(1, world.tickRate) : 30;
                    }

                    if (mp.MaxSpeed.HasValue)
                    {
                        if (movementParamsArePerSecond)
                            movement.MaxSpeed = Fixed.FromDouble(mp.MaxSpeed.Value / (double)tickRate);
                        else
                            movement.MaxSpeed = Fixed.FromInt(mp.MaxSpeed.Value);

                        // default Speed tracks MaxSpeed
                        movement.Speed = movement.MaxSpeed;
                    }

                    if (mp.Acceleration.HasValue)
                    {
                        if (movementParamsArePerSecond)
                        {
                            // a per second^2 -> per tick: a / tickRate^2
                            var tr = (double)tickRate;
                            movement.Acceleration = Fixed.FromDouble(mp.Acceleration.Value / (tr * tr));
                        }
                        else
                        {
                            movement.Acceleration = Fixed.FromInt(mp.Acceleration.Value);
                        }
                    }

                    if (mp.TurnSpeedDeg.HasValue)
                    {
                        if (movementParamsArePerSecond)
                            movement.TurnSpeedDeg = Fixed.FromDouble(mp.TurnSpeedDeg.Value / (double)tickRate);
                        else
                            movement.TurnSpeedDeg = Fixed.FromInt(mp.TurnSpeedDeg.Value);
                    }
                }
            }

            // Guard
            var gp = data.AbilityParams.Guard;
            if (gp != null)
            {
                Game.Unit.Ability.BaseAbilities.Guard guard = null;
                foreach (var ab in Actor.Abilities)
                {
                    if (ab is Game.Unit.Ability.BaseAbilities.Guard g) { guard = g; break; }
                }

                if (guard != null)
                {
                    if (gp.AlertRange.HasValue)
                        guard.AlertRange = Fixed.FromInt(gp.AlertRange.Value);

                    if (gp.AlertLayers != null)
                        guard.AlertLayerList = gp.AlertLayers;
                }
            }
        }

        /// <summary>
        /// Create ArmorInfo and Armament abilities from UnitData combat fields.
        /// Called after normal abilities are initialized.
        /// </summary>
        private void ApplyCombatData(UnitData data)
        {
            if (data == null || Actor == null) return;

            // ArmorType
            if (!string.IsNullOrEmpty(data.ArmorType))
            {
                if (System.Enum.TryParse<Game.Combat.ArmorType>(data.ArmorType, true, out var armorType))
                {
                    var armorInfo = new ArmorInfo(armorType);
                    armorInfo.BindActor(Actor);
                    Actor.Abilities.Add(armorInfo);
                    armorInfo.Init();
                }
                else
                {
                    GameLog.Warn(GameLog.Tag.Unit, $"Unknown ArmorType '{data.ArmorType}' on unit '{data.Id}'");
                }
            }

            // Weapons → Armament abilities
            if (data.Weapons != null && data.Weapons.Count > 0)
            {
                // Try to find CombatRegistry from the World/LogicWorld
                Game.Combat.CombatRegistry combatReg = null;
                var world = FindObjectOfType<Game.World.World>();
                if (world != null)
                {
                    // Access via the public API chain. LogicWorld exposes CombatData.
                    // During startup, LogicWorld may not exist yet. In that case,
                    // Armament will lazily resolve the registry on first Tick.
                }

                for (int i = 0; i < data.Weapons.Count; i++)
                {
                    var weaponId = data.Weapons[i];
                    if (string.IsNullOrEmpty(weaponId)) continue;

                    // Create a placeholder Armament with a minimal WeaponDef.
                    // The actual WeaponDef will be resolved from CombatRegistry at runtime
                    // when the Armament ticks (lazy resolution via Actor.World -> LogicWorld.CombatData).
                    Game.Combat.WeaponDef weaponDef = null;
                    if (combatReg != null)
                        combatReg.TryGetWeapon(weaponId, out weaponDef);

                    if (weaponDef == null)
                    {
                        // Create a deferred-resolution Armament using a stub WeaponDef.
                        // The Id is set so it can resolve later.
                        weaponDef = new Game.Combat.WeaponDef { Id = weaponId };
                    }

                    var armament = new Armament(weaponDef);
                    armament.BindActor(Actor);
                    Actor.Abilities.Add(armament);
                    armament.Init();
                }
            }
        }
    }
}
