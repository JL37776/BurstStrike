using UnityEditor;
using UnityEngine;
using System.IO;
using Game.Serialization;

namespace Game.Serialization.Editor
{
    public static class YamlSerializationMenu
    {
        const string sampleFileName = "unit_data.yaml";

        [MenuItem("Tools/Serialization/Save Unit YAML")]
        public static void SaveUnitYaml()
        {
            var unit = UnitData.FromVector3("soldier1", 100, new Vector3(0, 1, 0));
            var path = Path.Combine(Application.persistentDataPath, sampleFileName);
            YamlHelper.SaveToFile(unit, path);
            Debug.Log($"Saved YAML to {path}");

            // Also write into Assets for easy inspection in the Editor
            var assetsPath = Path.Combine(Application.dataPath, "Game/Serialization/Samples", sampleFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(assetsPath));
            YamlHelper.SaveToFile(unit, assetsPath);
            Debug.Log($"Saved YAML copy to {assetsPath}");
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Serialization/Load Unit YAML")]
        public static void LoadUnitYaml()
        {
            var assetsPath = Path.Combine(Application.dataPath, "Game/Serialization/Samples", sampleFileName);
            UnitData unit = null;
            if (File.Exists(assetsPath))
                unit = YamlHelper.LoadFromFile(assetsPath);
            else
            {
                var path = Path.Combine(Application.persistentDataPath, sampleFileName);
                if (File.Exists(path))
                    unit = YamlHelper.LoadFromFile(path);
            }

            if (unit == null)
            {
                Debug.LogWarning("No YAML file found to load.");
                return;
            }

            // Print detailed info recursively
            PrintUnit(unit, 0);
        }

        [MenuItem("Tools/Serialization/Spawn Unit From Sample")]
        public static void SpawnUnitFromSample()
        {
            var assetsPath = Path.Combine(Application.dataPath, "Game/Serialization/Samples", "tank_example.yaml");
            if (!File.Exists(assetsPath))
            {
                Debug.LogWarning($"Sample YAML not found at {assetsPath}");
                return;
            }

            var yaml = File.ReadAllText(assetsPath);
            var root = YamlHelper.Deserialize(yaml);
            if (root == null)
            {
                Debug.LogWarning("Failed to deserialize sample YAML.");
                return;
            }

            // Create in the active scene
            var go = UnitFactory.CreateUnit(root, faction: 0, playerId: 0, color: Game.Unit.PlayerPalette.GetColor(0));
            Selection.activeGameObject = go;
            Debug.Log($"Spawned unit '{root.Id}' in scene.");
        }

        // Recursive pretty-print for UnitData
        static void PrintUnit(UnitData unit, int depth)
        {
            var indent = new string(' ', depth * 2);
            var abilities = unit.Abilities == null ? "[]" : "[" + string.Join(",", unit.Abilities) + "]";
            Debug.Log($"{indent}Unit Id={unit.Id}, Model={unit.Model}, IsPrimary={unit.IsPrimary}, UseParentPosition={unit.UseParentPosition}, BindParentRotation={unit.BindParentRotation}, Health={unit.Health}, Position={unit.ToVector3()}, Abilities={abilities}");
            if (unit.Children != null)
            {
                foreach (var child in unit.Children)
                {
                    PrintUnit(child, depth + 1);
                }
            }
        }
    }
}
