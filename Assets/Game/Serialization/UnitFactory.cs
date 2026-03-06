using Game.Unit;
using UnityEngine;

namespace Game.Serialization
{
    public static class UnitFactory
    {
        // Create a GameObject for the UnitData. If Prefab is set, try to load from Resources.
        // The parent parameter is optional; used when instantiating child units.
        public static GameObject CreateUnit(UnitData data, int faction, int playerId, Color color, Transform parent = null)
        {
            GameObject go;

            if (!string.IsNullOrEmpty(data.Prefab))
            {
                var prefab = Resources.Load<GameObject>(data.Prefab);
                if (prefab != null)
                {
                    go = Object.Instantiate(prefab, parent);
                }
                else
                {
                    go = new GameObject();
                }
            }
            else
            {
                go = new GameObject();
            }

            if (parent != null)
                go.transform.SetParent(parent, worldPositionStays: true);

            var comp = go.AddComponent<UnitComponent>();

            // Determine parent actor if available
            Actor parentActor = null;
            if (parent != null)
            {
                var parentComp = parent.GetComponent<UnitComponent>();
                if (parentComp != null)
                    parentActor = parentComp.Actor;
            }

            comp.ApplyData(data, parentActor, faction, playerId, color);

            // UnitComponent is responsible for creating child UnitComponents now
            return go;
        }

        // Convenience: create units from YAML string
        public static GameObject CreateUnitFromYaml(string yaml, int faction, int playerId, Color color, Transform parent = null)
        {
            var data = YamlHelper.DeserializeValidated(yaml);
            if (data == null) return null;
            return CreateUnit(data, faction, playerId, color, parent);
        }

        // Convenience: create unit from a YAML file path
        public static GameObject CreateUnitFromFile(string path, int faction, int playerId, Color color, Transform parent = null)
        {
            var data = YamlHelper.LoadFromFileValidated(path);
            if (data == null) return null;
            return CreateUnit(data, faction, playerId, color, parent);
        }
    }
}
