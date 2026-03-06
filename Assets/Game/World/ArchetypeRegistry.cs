using System;
using System.Collections.Generic;
using System.IO;
using Game.Serialization;

namespace Game.World
{
    /// <summary>
    /// Deterministic registry used by LogicWorld.
    /// Loads UnitData from YAML files on startup and caches them in-memory.
    /// </summary>
    internal sealed class ArchetypeRegistry : IArchetypeRegistry
    {
        private readonly Dictionary<int, UnitData> _byId = new Dictionary<int, UnitData>();
        private readonly Dictionary<string, UnitData> _byStringId = new Dictionary<string, UnitData>(System.StringComparer.Ordinal);

        public ArchetypeRegistry(IReadOnlyDictionary<int, UnitData> byId)
        {
            if (byId == null) return;
            foreach (var kv in byId)
            {
                if (kv.Key <= 0 || kv.Value == null) continue;
                if (_byId.ContainsKey(kv.Key)) continue;
                _byId.Add(kv.Key, kv.Value);

                if (!string.IsNullOrWhiteSpace(kv.Value.Id) && !_byStringId.ContainsKey(kv.Value.Id))
                    _byStringId.Add(kv.Value.Id, kv.Value);
            }
        }

        public ArchetypeRegistry(IEnumerable<(int archetypeId, string yamlPath)> entries)
        {
            if (entries == null) return;

            foreach (var (archetypeId, yamlPath) in entries)
            {
                if (archetypeId <= 0) continue;
                if (string.IsNullOrWhiteSpace(yamlPath)) continue;
                if (_byId.ContainsKey(archetypeId)) continue;

                // Deterministic load: file must exist in the build/runtime environment.
                // If missing, we simply skip; LogicWorld will fall back to defaults.
                if (!File.Exists(yamlPath))
                    continue;

                var data = YamlHelper.LoadFromFileValidated(yamlPath);
                if (data == null)
                    continue;

                _byId.Add(archetypeId, data);

                if (data != null && !string.IsNullOrWhiteSpace(data.Id) && !_byStringId.ContainsKey(data.Id))
                    _byStringId.Add(data.Id, data);
            }
        }

        public bool TryGetUnitData(int archetypeId, out UnitData data)
        {
            return _byId.TryGetValue(archetypeId, out data);
        }

        public bool TryGetUnitData(string archetypeStringId, out UnitData data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(archetypeStringId)) return false;
            return _byStringId.TryGetValue(archetypeStringId, out data) && data != null;
        }

        // Convenience helper used by World to create a default mapping.
        public static (int archetypeId, string yamlPath) Entry(int id, string path) => (id, path);
    }
}
