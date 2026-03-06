using System.Collections.Generic;
using Game.Grid;
using Game.Map;
using UnityEngine;

namespace Game.Pathing.Debug
{
    /// <summary>
    /// Main-thread debug visualizer: draws cubes at grid cell centers for the latest generated GridPathResult.
    /// Clears previous cubes automatically.
    /// </summary>
    public sealed class PathDebugCubes : MonoBehaviour
    {
        [Header("Toggle")]
        public bool enabledInBuild = true;

        [Header("Style")]
        public float cubeSize = 0.3f;
        public float y = 1f;
        public Color color = Color.black;

        [Range(0f, 1f)]
        [Tooltip("Alpha for path cubes. 1=opaque, 0=invisible. Suggest 0.15-0.35.")]
        public float alpha = 0.25f;

        private readonly List<GameObject> _cubes = new List<GameObject>(256);
        private Material _mat;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public void Clear()
        {
            for (int i = 0; i < _cubes.Count; i++)
            {
                var go = _cubes[i];
                if (go != null)
                    Destroy(go);
            }
            _cubes.Clear();
        }

        public void ShowPath(IMap map, IReadOnlyList<GridPosition> rawPath)
        {
            if (!enabledInBuild) return;
            if (map == null) { Clear(); return; }

            Clear();
            if (rawPath == null || rawPath.Count == 0) return;

            EnsureMaterial();

            var grid = map.Grid;
            for (int i = 0; i < rawPath.Count; i++)
            {
                var cell = rawPath[i];
                var p2 = grid.GetCellCenterWorld(cell);
                var pos = new Vector3(p2.x.ToFloat(), y, p2.y.ToFloat());

                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"PathCell_{cell.X}_{cell.Y}";
                cube.transform.SetParent(transform, worldPositionStays: true);
                cube.transform.position = pos;
                cube.transform.localScale = Vector3.one * cubeSize;

                var r = cube.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = _mat;

                // No physics interaction
                var col = cube.GetComponent<Collider>();
                if (col != null) Destroy(col);

                _cubes.Add(cube);
            }
        }

        private void EnsureMaterial()
        {
            if (_mat != null) return;

            // Prefer built-in Standard; fall back to Unlit/Color.
            var shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            _mat = new Material(shader);

            // Apply alpha
            var c = color;
            c.a = Mathf.Clamp01(alpha);

            if (_mat.HasProperty(BaseColorId)) _mat.SetColor(BaseColorId, c);
            if (_mat.HasProperty(ColorId)) _mat.SetColor(ColorId, c);

            // If using Standard, switch to transparent blending so alpha works.
            // (Works in built-in render pipeline; other shaders will just use the color alpha if supported.)
            if (shader != null && shader.name == "Standard")
            {
                _mat.SetFloat("_Mode", 3f); // Transparent
                _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _mat.SetInt("_ZWrite", 0);
                _mat.DisableKeyword("_ALPHATEST_ON");
                _mat.EnableKeyword("_ALPHABLEND_ON");
                _mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                _mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
        }

        private void OnDestroy()
        {
            Clear();
            if (_mat != null) Destroy(_mat);
        }
    }
}
