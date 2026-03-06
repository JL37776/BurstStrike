using System;
using System.Collections.Generic;
using System.IO;
using Game.Serialization;

namespace Game.World
{
    internal static class WorldConfigLoader
    {
        /// <summary>
        /// Load world config from a YAML file path. Returns defaults on missing/invalid input.
        /// Never throws.
        /// </summary>
        public static WorldConfigData LoadOrDefault(string yamlPath, out List<string> errors)
        {
            errors = new List<string>();
            var cfg = WorldConfigData.CreateDefault();

            if (string.IsNullOrWhiteSpace(yamlPath))
            {
                errors.Add("World config path is null/empty; using defaults.");
                return cfg;
            }

            if (!File.Exists(yamlPath))
            {
                errors.Add($"World config not found: '{yamlPath}'; using defaults.");
                return cfg;
            }

            string yaml;
            try { yaml = File.ReadAllText(yamlPath); }
            catch (Exception e)
            {
                errors.Add($"Read world config failed: '{yamlPath}': {e.Message}; using defaults.");
                return cfg;
            }

            try
            {
                // Reuse YamlHelper's PascalCase mapping.
                var data = YamlHelper.Deserialize<WorldConfigData>(yaml);
                if (data != null) cfg = data;
            }
            catch (Exception e)
            {
                errors.Add($"Deserialize world config failed: {e.Message}; using defaults.");
                return cfg;
            }

            // Non-throw validation.
            foreach (var err in cfg.Validate())
                errors.Add("[WorldConfig] " + err);

            return cfg;
        }
    }
}
