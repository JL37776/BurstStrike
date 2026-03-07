using System.IO;
using UnityEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Game.Serialization
{
    public static class YamlHelper
    {
        // YAML samples in this project use PascalCase keys (Id, Health, IsPrimary...).
        // Use NullNamingConvention so property names map as-is.
        static readonly ISerializer serializer = new SerializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();

        static readonly IDeserializer deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();

        public static T Deserialize<T>(string yaml)
        {
            if (string.IsNullOrEmpty(yaml)) return default;
            return deserializer.Deserialize<T>(yaml);
        }

        public static string Serialize(UnitData data)
        {
            return serializer.Serialize(data);
        }

        public static UnitData Deserialize(string yaml)
        {
            return deserializer.Deserialize<UnitData>(yaml);
        }

        /// <summary>
        /// Deserialize UnitData and validate required fields.
        /// Returns null if YAML is missing required fields; errors are logged.
        /// </summary>
        public static UnitData DeserializeValidated(string yaml)
        {
            if (string.IsNullOrEmpty(yaml)) return null;

            UnitData data;
            try
            {
                data = Deserialize(yaml);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to deserialize UnitData YAML: {e.Message}");
                return null;
            }

            if (data == null) return null;

            if (!UnitDataValidator.TryValidate(data, out var errors))
            {
                foreach (var err in errors)
                    Debug.LogError("[UnitData] " + err);
                return null;
            }

            return data;
        }

        /// <summary>
        /// Try-deserialize UnitData and return validation errors instead of logging.
        /// Useful for editor tooling / tests.
        /// </summary>
        public static bool TryDeserializeValidated(string yaml, out UnitData data, out System.Collections.Generic.List<string> errors)
        {
            errors = new System.Collections.Generic.List<string>();
            data = null;

            if (string.IsNullOrEmpty(yaml))
            {
                errors.Add("YAML is empty");
                return false;
            }

            try
            {
                data = Deserialize(yaml);
            }
            catch (System.Exception e)
            {
                errors.Add("Deserialize exception: " + e.Message);
                data = null;
                return false;
            }

            if (data == null)
            {
                errors.Add("Deserialized UnitData is null");
                return false;
            }

            if (!UnitDataValidator.TryValidate(data, out var vErrors))
            {
                errors.AddRange(vErrors);
                data = null;
                return false;
            }

            return true;
        }

        public static void SaveToFile(UnitData data, string path)
        {
            var yaml = Serialize(data);
            File.WriteAllText(path, yaml);
        }

        public static UnitData LoadFromFile(string path)
        {
            if (!File.Exists(path)) return null;
            var yaml = File.ReadAllText(path);
            return Deserialize(yaml);
        }

        public static UnitData LoadFromFileValidated(string path)
        {
            if (!File.Exists(path)) return null;
            var yaml = File.ReadAllText(path);
            return DeserializeValidated(yaml);
        }
    }
}
