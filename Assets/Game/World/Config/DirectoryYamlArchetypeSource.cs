using System;
using System.Collections.Generic;
using System.IO;
using Game.Serialization;

namespace Game.World
{
    /// <summary>
    /// Archetype source that scans a directory for *.yaml and maps them to archetypeId.
    /// 
    /// Current mapping rule (deterministic):
    /// - archetypeId is read from YAML field UnitData.ArchetypeId.
    /// 
    /// This is intentionally simple for now; later we can replace with a network-backed source
    /// or embed an explicit archetypeId field.
    /// </summary>
    internal sealed class DirectoryYamlArchetypeSource : IArchetypeSource
    {
        private readonly string _rootDir;
        private readonly SearchOption _searchOption;

        public DirectoryYamlArchetypeSource(string rootDir, bool recursive)
        {
            _rootDir = rootDir;
            _searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        }

        public IReadOnlyDictionary<int, UnitData> LoadAll(out List<string> errors)
        {
            errors = new List<string>();

            var dict = new SortedDictionary<int, UnitData>();
            if (string.IsNullOrWhiteSpace(_rootDir))
            {
                errors.Add("Archetype source root directory is null/empty.");
                return dict;
            }

            var dir = _rootDir;
            if (!Directory.Exists(dir))
            {
                errors.Add($"Archetype directory not found: '{dir}'");
                return dict;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*.yaml", _searchOption);
            }
            catch (Exception e)
            {
                errors.Add($"Failed to scan archetype directory '{dir}': {e.Message}");
                return dict;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < files.Length; i++)
            {
                var path = files[i];
                if (string.IsNullOrWhiteSpace(path)) continue;

                string yaml;
                try { yaml = File.ReadAllText(path); }
                catch (Exception e)
                {
                    errors.Add($"Read file failed: '{path}' : {e.Message}");
                    continue;
                }

                if (!YamlHelper.TryDeserializeValidated(yaml, out var data, out var vErrors) || data == null)
                {
                    for (int ei = 0; ei < vErrors.Count; ei++)
                        errors.Add($"{path}: {vErrors[ei]}");
                    continue;
                }

                if (data.ArchetypeId <= 0)
                {
                    errors.Add($"{path}: ArchetypeId must be > 0");
                    continue;
                }

                if (dict.ContainsKey(data.ArchetypeId))
                {
                    errors.Add($"{path}: duplicate ArchetypeId={data.ArchetypeId}");
                    continue;
                }

                dict[data.ArchetypeId] = data;
            }

            return dict;
        }
    }
}
