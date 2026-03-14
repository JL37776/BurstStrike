using System;
using System.Collections.Generic;
using System.IO;
using Game.Combat;
using Game.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Game.Serialization
{
    /// <summary>
    /// Loads combat definitions (weapons, warheads, projectiles) from YAML files
    /// and builds a <see cref="CombatRegistry"/>.
    /// 
    /// Expected directory layout:
    ///   Assets/Game/Data/Combat/Weapons/     — *.yaml WeaponDef files
    ///   Assets/Game/Data/Combat/Warheads/    — *.yaml WarheadDef files
    ///   Assets/Game/Data/Combat/Projectiles/ — *.yaml ProjectileDef files
    /// </summary>
    public static class CombatDataLoader
    {
        private static readonly IDeserializer Deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();

        /// <summary>
        /// Load all combat data from a root directory.
        /// Returns a ready-to-use CombatRegistry.
        /// </summary>
        public static CombatRegistry LoadFromDirectory(string combatDataRoot, out List<string> errors)
        {
            errors = new List<string>();

            var weapons = new Dictionary<string, WeaponDef>();
            var warheads = new Dictionary<string, WarheadDef>();
            var projectiles = new Dictionary<string, ProjectileDef>();

            // Load weapons
            var weaponsDir = Path.Combine(combatDataRoot, "Weapons");
            if (Directory.Exists(weaponsDir))
            {
                foreach (var file in Directory.GetFiles(weaponsDir, "*.yaml", SearchOption.AllDirectories))
                {
                    try
                    {
                        var yaml = File.ReadAllText(file);
                        var def = Deserializer.Deserialize<WeaponDef>(yaml);
                        if (def != null && !string.IsNullOrEmpty(def.Id))
                        {
                            weapons[def.Id] = def;
                        }
                        else
                        {
                            errors.Add($"WeaponDef missing Id in {file}");
                        }
                    }
                    catch (Exception e)
                    {
                        errors.Add($"Failed to load weapon {file}: {e.Message}");
                    }
                }
            }

            // Load warheads
            var warheadsDir = Path.Combine(combatDataRoot, "Warheads");
            if (Directory.Exists(warheadsDir))
            {
                foreach (var file in Directory.GetFiles(warheadsDir, "*.yaml", SearchOption.AllDirectories))
                {
                    try
                    {
                        var yaml = File.ReadAllText(file);
                        var def = Deserializer.Deserialize<WarheadDef>(yaml);
                        if (def != null && !string.IsNullOrEmpty(def.Id))
                        {
                            warheads[def.Id] = def;
                        }
                        else
                        {
                            errors.Add($"WarheadDef missing Id in {file}");
                        }
                    }
                    catch (Exception e)
                    {
                        errors.Add($"Failed to load warhead {file}: {e.Message}");
                    }
                }
            }

            // Load projectiles
            var projectilesDir = Path.Combine(combatDataRoot, "Projectiles");
            if (Directory.Exists(projectilesDir))
            {
                foreach (var file in Directory.GetFiles(projectilesDir, "*.yaml", SearchOption.AllDirectories))
                {
                    try
                    {
                        var yaml = File.ReadAllText(file);
                        var def = Deserializer.Deserialize<ProjectileDef>(yaml);
                        if (def != null && !string.IsNullOrEmpty(def.Id))
                        {
                            projectiles[def.Id] = def;
                        }
                        else
                        {
                            errors.Add($"ProjectileDef missing Id in {file}");
                        }
                    }
                    catch (Exception e)
                    {
                        errors.Add($"Failed to load projectile {file}: {e.Message}");
                    }
                }
            }

            GameLog.Info(GameLog.Tag.Config,
                $"CombatData loaded: weapons={weapons.Count} warheads={warheads.Count} projectiles={projectiles.Count}");

            return new CombatRegistry(weapons, warheads, projectiles);
        }

        /// <summary>
        /// Build default combat data (hardcoded) as fallback when no YAML files exist.
        /// Useful for testing.
        /// </summary>
        public static CombatRegistry BuildDefaults()
        {
            var weapons = new Dictionary<string, WeaponDef>();
            var warheads = new Dictionary<string, WarheadDef>();
            var projectiles = new Dictionary<string, ProjectileDef>();

            // Default warheads
            warheads["kinetic_standard"] = new WarheadDef
            {
                Id = "kinetic_standard",
                Damage = 15,
                DamageType = DamageType.Kinetic,
                VsArmor = new[] { 100, 100, 100, 100, 50, 100 } // None, Light, Medium, Heavy, Structure, Heroic
            };

            warheads["ap_shell"] = new WarheadDef
            {
                Id = "ap_shell",
                Damage = 40,
                DamageType = DamageType.ArmorPiercing,
                VsArmor = new[] { 50, 75, 100, 100, 75, 100 }
            };

            warheads["he_explosive"] = new WarheadDef
            {
                Id = "he_explosive",
                Damage = 30,
                DamageType = DamageType.Explosive,
                SplashRadius = 1024, // 1 cell
                SplashFalloff = new[] { 100, 75, 50, 25 },
                VsArmor = new[] { 100, 100, 75, 50, 100, 75 }
            };

            warheads["aa_missile_warhead"] = new WarheadDef
            {
                Id = "aa_missile_warhead",
                Damage = 50,
                DamageType = DamageType.Explosive,
                VsArmor = new[] { 100, 150, 100, 75, 50, 100 }
            };

            warheads["torpedo_warhead"] = new WarheadDef
            {
                Id = "torpedo_warhead",
                Damage = 80,
                DamageType = DamageType.Torpedo,
                VsArmor = new[] { 50, 75, 100, 130, 100, 100 }
            };

            // Default projectiles
            projectiles["bullet_standard"] = new ProjectileDef
            {
                Id = "bullet_standard",
                Type = ProjectileType.Bullet,
                Speed = 400,
                MaxLifetime = 120,
                HitRadius = 128
            };

            projectiles["shell_medium"] = new ProjectileDef
            {
                Id = "shell_medium",
                Type = ProjectileType.Bullet,
                Speed = 300,
                MaxLifetime = 150,
                HitRadius = 192
            };

            projectiles["missile_aa"] = new ProjectileDef
            {
                Id = "missile_aa",
                Type = ProjectileType.Missile,
                Speed = 250,
                MaxLifetime = 200,
                TrackingStrength = 80,
                TurnRate = 256,
                HitRadius = 160
            };

            projectiles["torpedo_mk1"] = new ProjectileDef
            {
                Id = "torpedo_mk1",
                Type = ProjectileType.Torpedo,
                Speed = 150,
                MaxLifetime = 300,
                TrackingStrength = 60,
                TurnRate = 128,
                HitRadius = 256
            };

            projectiles["mortar_shell"] = new ProjectileDef
            {
                Id = "mortar_shell",
                Type = ProjectileType.Ballistic,
                Speed = 200,
                MaxLifetime = 180,
                ArcHeight = 2048, // 2 cells high
                HitRadius = 256
            };

            // Default weapons

            // --- Land weapons ---
            weapons["mg_standard"] = new WeaponDef
            {
                Id = "mg_standard",
                Range = 4096, // 4 cells
                Cooldown = 8,
                Burst = 3,
                BurstDelay = 2,
                WarheadId = "kinetic_standard",
                ProjectileId = "bullet_standard",
                ValidTargetLayers = Game.Unit.UnitAlertLayer.Ground | Game.Unit.UnitAlertLayer.LowAir,
                VsAirModifier = 60,
                RequiresFacing = true
            };

            weapons["cannon_105mm"] = new WeaponDef
            {
                Id = "cannon_105mm",
                Range = 5120, // 5 cells
                Cooldown = 45,
                Warmup = 5,
                WarheadId = "ap_shell",
                ProjectileId = "shell_medium",
                ValidTargetLayers = Game.Unit.UnitAlertLayer.Ground,
                VsAirModifier = 0,
                RequiresFacing = true
            };

            weapons["mortar_light"] = new WeaponDef
            {
                Id = "mortar_light",
                Range = 7168, // 7 cells
                MinRange = 2048, // 2 cells minimum
                Cooldown = 60,
                WarheadId = "he_explosive",
                ProjectileId = "mortar_shell",
                ValidTargetLayers = Game.Unit.UnitAlertLayer.Ground,
                RequiresFacing = false // turret
            };

            // --- Air weapons ---
            weapons["air_cannon_20mm"] = new WeaponDef
            {
                Id = "air_cannon_20mm",
                Range = 3072, // 3 cells
                Cooldown = 10,
                Burst = 4,
                BurstDelay = 2,
                WarheadId = "kinetic_standard",
                ProjectileId = null, // hitscan
                ValidTargetLayers = Game.Unit.UnitAlertLayer.Ground | Game.Unit.UnitAlertLayer.Ocean,
                VsLandModifier = 60,
                RequiresFacing = true
            };

            weapons["aa_missile"] = new WeaponDef
            {
                Id = "aa_missile",
                Range = 6144, // 6 cells
                MinRange = 1024,
                Cooldown = 60,
                Burst = 2,
                BurstDelay = 8,
                WarheadId = "aa_missile_warhead",
                ProjectileId = "missile_aa",
                ValidTargetLayers = Game.Unit.UnitAlertLayer.LowAir | Game.Unit.UnitAlertLayer.HighAir,
                VsAirModifier = 150,
                VsLandModifier = 30,
                RequiresFacing = false
            };

            // --- Naval weapons ---
            weapons["naval_cannon"] = new WeaponDef
            {
                Id = "naval_cannon",
                Range = 8192, // 8 cells
                Cooldown = 50,
                Warmup = 8,
                WarheadId = "he_explosive",
                ProjectileId = "shell_medium",
                ValidTargetLayers = Game.Unit.UnitAlertLayer.Ground | Game.Unit.UnitAlertLayer.Ocean,
                RequiresFacing = true
            };

            weapons["torpedo_launcher"] = new WeaponDef
            {
                Id = "torpedo_launcher",
                Range = 7168, // 7 cells
                Cooldown = 90,
                WarheadId = "torpedo_warhead",
                ProjectileId = "torpedo_mk1",
                ValidTargetLayers = Game.Unit.UnitAlertLayer.Ocean | Game.Unit.UnitAlertLayer.Underwater,
                VsNavalModifier = 130,
                RequiresFacing = true
            };

            return new CombatRegistry(weapons, warheads, projectiles);
        }
    }
}
