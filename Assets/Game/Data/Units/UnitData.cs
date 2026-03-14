using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Serialization
{
    [Serializable]
    public class UnitData
    {
        /// <summary>
        /// Deterministic archetype id used by spawn commands (Command.Int0).
        /// Loaded from YAML. Must be unique across all loaded archetypes.
        /// </summary>
        public int ArchetypeId;

        public string Id;
        public int Health;
        // Optional prefab name (Resources path) to instantiate as a visual/functional child
        public string Prefab;

        // New: unit model/type identifier (e.g., TankMarkI)
        public string Model;

        // Is this the primary/main unit (e.g., chassis of a tank)
        public bool IsPrimary;

        // If true, this unit will use its parent's position (i.e., position is relative to parent)
        public bool UseParentPosition;

        // If true, this unit will bind/align its rotation to the parent
        public bool BindParentRotation;

        // Required: occupancy / movement layer for this unit (flags match Game.Map.MapLayer).
        // YAML must provide this explicitly.
        public Game.Map.MapLayer Layer;

        /// <summary>
        /// What alert/target layer this unit belongs to (underwater/ocean/ground/low air/high air).
        /// If omitted, defaults to Ground.
        /// </summary>
        public Game.Unit.UnitAlertLayer AlertLayer = Game.Unit.UnitAlertLayer.Ground;

        // Position as array [x,y,z] to keep YAML simple and avoid Unity-specific types in YAML
        public float[] Position;

        // Nested child units (e.g. a tank has chassis and weapon as children)
        public List<UnitData> Children;

        // New: list of ability identifiers to attach (e.g., ["BaseUnit"]).
        public List<string> Abilities;

        // Optional: per-ability parameter blocks (keeps Abilities list simple)
        public AbilityParamsData AbilityParams;

        [Serializable]
        public class AbilityParamsData
        {
            public MovementParamsData Movement;

            // Optional: Guard ability params
            public GuardParamsData Guard;

            // Optional: BaseWeapon ability params
            public BaseWeaponParamsData BaseWeapon;
        }

        [Serializable]
        public class MovementParamsData
        {
            public int? MaxSpeed;
            public int? Acceleration;
            public int? TurnSpeedDeg;
        }

        [Serializable]
        public class GuardParamsData
        {
            /// <summary>
            /// Guard radius in world units.
            /// </summary>
            public int? AlertRange;

            /// <summary>
            /// Which target alert layers Guard can detect. YAML as list.
            /// Example: [Ground, LowAir]
            /// </summary>
            public List<Game.Unit.UnitAlertLayer> AlertLayers;
        }

        [Serializable]
        public class BaseWeaponParamsData
        {
            public int? Damage;
            public int? Range;
        }

        // ── Combat system data (loaded from YAML) ────────────────────────

        /// <summary>
        /// Armor type string (maps to Game.Combat.ArmorType enum).
        /// Example: "None", "Light", "Medium", "Heavy", "Structure"
        /// </summary>
        public string ArmorType;

        /// <summary>
        /// List of weapon definition IDs to attach as Armament abilities.
        /// References WeaponDef.Id in the CombatRegistry.
        /// Example: ["cannon_105mm", "mg_coaxial"]
        /// </summary>
        public List<string> Weapons;

        public static UnitData FromVector3(string id, int health, Vector3 pos, string prefab = null, string model = null, bool isPrimary = false, bool useParentPosition = false, bool bindParentRotation = false, List<UnitData> children = null, List<string> abilities = null)
        {
            return new UnitData
            {
                Id = id,
                Health = health,
                Prefab = prefab,
                Model = model,
                IsPrimary = isPrimary,
                UseParentPosition = useParentPosition,
                BindParentRotation = bindParentRotation,
                Layer = Game.Map.MapLayer.FootUnits,
                Position = new[] { pos.x, pos.y, pos.z },
                Children = children,
                Abilities = abilities
            };
        }

        public Vector3 ToVector3()
        {
            if (Position == null || Position.Length < 3) return Vector3.zero;
            return new Vector3(Position[0], Position[1], Position[2]);
        }
    }
}
