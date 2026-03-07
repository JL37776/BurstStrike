using UnityEngine;

namespace Game.Map
{
    /// <summary>
    /// 一个简单的地图渲染器：只渲染 Tanks layer。
    /// 规则：
    /// - 世界坐标 (0,0,0) 映射到 mapdata 的左下角格子（x=0,y=0）的左下角。
    /// - 每个格子边长为 cellSize（默认 0.5 Unity 单位）。
    /// - 通路：绿色立方体；非通路：白色立方体。
    /// 
    /// 注意：该类用于快速可视化/调试，不做对象池/合批优化。
    /// </summary>
    public sealed class TankMapRenderer : MonoBehaviour
    {
        [Header("Grid")]
        [SerializeField] private float cellSize = 0.5f;
        [SerializeField] private float cellHeight = 0.5f;
        [SerializeField] private Vector3 worldOrigin = Vector3.zero;

        [Header("Example Map")]
        [SerializeField] private int exampleMapSize = 200;
        [SerializeField, Range(0f, 1f)] private float tankObstacleProbability = 0.1f;
        [SerializeField] private int seed = 12345;
        [SerializeField] private bool useUnityRandomSeed;

        [Header("Render")]
        [SerializeField] private bool renderOnStart = true;
        [SerializeField] private bool clearExistingChildrenBeforeRender = true;

        private Material _passMat;
        private Material _blockMat;

        /// <summary>
        /// 运行时配置（避免只能靠 Inspector 赋值）。
        /// </summary>
        public void Configure(float cellSize, float cellHeight, Vector3 worldOrigin)
        {
            this.cellSize = cellSize;
            this.cellHeight = cellHeight;
            this.worldOrigin = worldOrigin;
        }

        private void Start()
        {
            if (!renderOnStart) return;

            var size = Mathf.Max(1, exampleMapSize);
            var map = MapLoader.CreateExampleMap(
                size,
                size,
                tankObstacleProbability,
                useUnityRandomSeed ? null : seed);

            Render(map);
        }

        public void Render(MapData map)
        {
            EnsureMaterials();

            if (clearExistingChildrenBeforeRender)
                ClearChildrenImmediate();

            // map 的 y=0 是“第一行”。这里约定它对应世界的“下方”(z=0)；y 向上对应世界 z 递增。
            // x 向右对应世界 x 递增。
            for (int y = 0; y < map.height; y++)
            {
                for (int x = 0; x < map.width; x++)
                {
                    bool isBlockedForTank = (map.Layers[y, x] & MapLayer.Tanks) != 0;
                    var mat = isBlockedForTank ? _blockMat : _passMat;

                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = $"Cell_{x}_{y}_{(isBlockedForTank ? "B" : "P")}";
                    cube.transform.SetParent(transform, false);

                    // 以 worldOrigin 为 map 左下角 (grid 0,0) 的左下角。
                    // cube 放在格子中心。
                    float wx = worldOrigin.x + (x + 0.5f) * cellSize;
                    float wz = worldOrigin.z + (y + 0.5f) * cellSize;
                    float wy = worldOrigin.y + cellHeight * 0.5f;

                    cube.transform.position = new Vector3(wx, wy, wz);
                    cube.transform.localScale = new Vector3(cellSize, cellHeight, cellSize);

                    var cubeRenderer = cube.GetComponent<Renderer>();
                    cubeRenderer.sharedMaterial = mat;
                }
            }
        }

        private void EnsureMaterials()
        {
            if (_passMat == null)
            {
                _passMat = new Material(Shader.Find("Standard"));
                _passMat.color = Color.green;
            }

            if (_blockMat == null)
            {
                _blockMat = new Material(Shader.Find("Standard"));
                _blockMat.color = Color.white;
            }
        }

        private void ClearChildrenImmediate()
        {
            // 注意：DestroyImmediate 适合编辑器/调试；运行时使用 Destroy。
            // 这里做一个兼容：运行时 Destroy，编辑器非运行态 DestroyImmediate。
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }
        }

        private void OnDestroy()
        {
            // 清理运行时创建的材质
            if (_passMat != null)
            {
                if (Application.isPlaying) Destroy(_passMat);
                else DestroyImmediate(_passMat);
            }

            if (_blockMat != null)
            {
                if (Application.isPlaying) Destroy(_blockMat);
                else DestroyImmediate(_blockMat);
            }
        }
    }
}
