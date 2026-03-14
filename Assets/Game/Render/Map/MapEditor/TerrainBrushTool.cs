using UnityEngine;
using UnityEngine.EventSystems;
using Game.Map.Terrain;

namespace Game.Map
{
    /// <summary>笔刷绘制模式</summary>
    public enum BrushMode
    {
        /// <summary>连续绘制：按住鼠标持续增减高度</summary>
        Continuous,
        /// <summary>台阶绘制：点击后圈选区域直接跳到目标高度</summary>
        Stamp,
        /// <summary>材质绘制：按住鼠标绘制地表材质</summary>
        SplatPaint,
    }

    /// <summary>
    /// 地形高度笔刷工具。
    /// <list type="bullet">
    ///   <item>在地形上跟随鼠标绘制一个圆圈指示器</item>
    ///   <item>按住鼠标左键升高圈选区域的地形</item>
    ///   <item>外部通过属性设置半径、强度、每秒高度增量</item>
    /// </list>
    /// </summary>
    public sealed class TerrainBrushTool : MonoBehaviour
    {
        // ── 外部引用 ──
        private MapEditorBridge _bridge;
        private MapGridRenderer _gridRenderer;

        // ── 笔刷参数 ──
        public bool      IsActive       { get; set; } = true;
        public BrushMode Mode           { get; set; } = BrushMode.Continuous;
        public int       BrushRadius    { get; set; } = 5;
        public float     BrushStrength  { get; set; } = 0.5f;
        public float     HeightPerSec   { get; set; } = 5f;
        public float     StampHeight    { get; set; } = 5f;
        public bool      AutoSplat     { get; set; } = true;

        // ── 材质绘制目标 (Top/Cliff/Bottom 权重) ──
        public float SplatTargetTop    { get; set; } = 1f;
        public float SplatTargetCliff  { get; set; } = 0f;
        public float SplatTargetBottom { get; set; } = 0f;

        // ── 圆圈指示器 ──
        private const int CircleSegments = 64;
        private GameObject   _circleGo;
        private LineRenderer _circleLine;

        // ── 状态 ──
        private bool    _brushVisible;
        private Vector3 _brushWorldPos;
        private int     _brushVertX;
        private int     _brushVertZ;
        private bool    _isPainting;

        // ── 常量 ──
        private static readonly Color CircleColor       = new Color(1f, 0.85f, 0.2f, 0.9f);
        private static readonly Color CircleColorActive = new Color(1f, 0.4f, 0.15f, 0.95f);
        private static readonly Color CircleColorStamp  = new Color(0.2f, 0.8f, 1f, 0.9f);
        private static readonly Color CircleColorSplat  = new Color(0.4f, 1f, 0.4f, 0.9f);

        public void Init(MapEditorBridge bridge, MapGridRenderer gridRenderer)
        {
            _bridge = bridge;
            _gridRenderer = gridRenderer;
            CreateCircle();
        }

        private void CreateCircle()
        {
            _circleGo = new GameObject("BrushCircle");
            _circleGo.transform.SetParent(transform, false);

            _circleLine = _circleGo.AddComponent<LineRenderer>();
            _circleLine.useWorldSpace = true;
            _circleLine.loop = true;
            _circleLine.positionCount = CircleSegments;
            _circleLine.startWidth = 0.15f;
            _circleLine.endWidth = 0.15f;
            _circleLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _circleLine.receiveShadows = false;

            var mat = new Material(Resources.Load<Shader>("BrushLine"));
            _circleLine.material = mat;
            _circleLine.startColor = CircleColor;
            _circleLine.endColor = CircleColor;

            _circleGo.SetActive(false);
        }

        private void Update()
        {
            if (!IsActive || _bridge == null || !_bridge.Editor.HasData)
            {
                HideCircle();
                return;
            }

            // 如果鼠标在 UI 上，不处理
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                HideCircle();
                _isPainting = false;
                return;
            }

            // Raycast 到地形
            var cam = Camera.main;
            if (cam == null) { HideCircle(); return; }

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 2000f))
            {
                _brushVisible = true;
                _brushWorldPos = hit.point;

                // 世界坐标转顶点坐标
                var data = _bridge.Editor.GetCurrentMapData();
                float spacing = data.VertexSpacing;
                _brushVertX = Mathf.RoundToInt(hit.point.x / spacing);
                _brushVertZ = Mathf.RoundToInt(hit.point.z / spacing);
                _brushVertX = Mathf.Clamp(_brushVertX, 0, data.Width - 1);
                _brushVertZ = Mathf.Clamp(_brushVertZ, 0, data.Height - 1);

                UpdateCircle(data);

                // ── 绘制逻辑 ──
                if (Input.GetMouseButton(0))
                {
                    if (!_isPainting)
                        _isPainting = true;

                    Paint(data);
                }
                else
                {
                    _isPainting = false;
                }
            }
            else
            {
                HideCircle();
                _isPainting = false;
            }
        }

        private void Paint(TerrainMapData data)
        {
            var brush = new BrushParams
            {
                CenterX  = _brushVertX,
                CenterZ  = _brushVertZ,
                Radius   = BrushRadius,
                Shape    = BrushShape.Circle,
                Falloff  = BrushFalloff.Smooth,
                Strength = BrushStrength,
            };

            switch (Mode)
            {
                case BrushMode.Continuous:
                {
                    float delta = HeightPerSec * BrushStrength * Time.deltaTime;
                    brush.Strength = 1f;
                    _bridge.Editor.BrushAddHeight(brush, delta);
                    break;
                }
                case BrushMode.Stamp:
                {
                    // 台阶模式：按住鼠标连续将圈选区域设为目标高度
                    brush.Strength = 1f;
                    brush.Falloff  = BrushFalloff.Constant;
                    _bridge.Editor.BrushSetHeight(brush, StampHeight);
                    break;
                }
                case BrushMode.SplatPaint:
                {
                    // 材质绘制：按住鼠标连续绘制地表材质
                    brush.Falloff = BrushFalloff.Smooth;
                    _bridge.Editor.BrushPaintSplat(brush,
                        SplatTargetTop, SplatTargetCliff, SplatTargetBottom);
                    break;
                }
            }

            // 高度修改后自动更新 splat
            if (AutoSplat && Mode != BrushMode.SplatPaint)
                _bridge.Editor.AutoGenerateSplat();

            // 实时重建网格
            if (_gridRenderer != null)
                _gridRenderer.Rebuild(data);
        }

        // ────────────────────── 圆圈可视化 ──────────────────────

        private void UpdateCircle(TerrainMapData data)
        {
            if (!_brushVisible)
            {
                HideCircle();
                return;
            }

            _circleGo.SetActive(true);

            float worldRadius = BrushRadius * data.VertexSpacing;
            var positions = new Vector3[CircleSegments];

            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = (float)i / CircleSegments * Mathf.PI * 2f;
                float px = _brushWorldPos.x + Mathf.Cos(angle) * worldRadius;
                float pz = _brushWorldPos.z + Mathf.Sin(angle) * worldRadius;

                // 在圆圈上采样地形高度，让圈贴地
                float py = SampleTerrainHeight(data, px, pz) + 0.15f;

                positions[i] = new Vector3(px, py, pz);
            }

            _circleLine.positionCount = CircleSegments;
            _circleLine.SetPositions(positions);

            // 颜色变化
            Color baseColor;
            switch (Mode)
            {
                case BrushMode.Stamp:      baseColor = CircleColorStamp; break;
                case BrushMode.SplatPaint: baseColor = CircleColorSplat; break;
                default:                   baseColor = CircleColor;      break;
            }
            var color = _isPainting ? CircleColorActive : baseColor;
            _circleLine.startColor = color;
            _circleLine.endColor = color;

            // 线宽随半径缩放（但限制范围）
            float width = Mathf.Clamp(worldRadius * 0.03f, 0.08f, 0.4f);
            _circleLine.startWidth = width;
            _circleLine.endWidth = width;
        }

        private float SampleTerrainHeight(TerrainMapData data, float worldX, float worldZ)
        {
            float spacing = data.VertexSpacing;
            int w = data.Width;
            int h = data.Height;

            float fx = worldX / spacing;
            float fz = worldZ / spacing;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, w - 2);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, h - 2);
            int x1 = x0 + 1;
            int z1 = z0 + 1;

            float tx = Mathf.Clamp01(fx - x0);
            float tz = Mathf.Clamp01(fz - z0);

            float h00 = data.Heightmap[z0 * w + x0];
            float h10 = data.Heightmap[z0 * w + x1];
            float h01 = data.Heightmap[z1 * w + x0];
            float h11 = data.Heightmap[z1 * w + x1];

            return Mathf.Lerp(
                Mathf.Lerp(h00, h10, tx),
                Mathf.Lerp(h01, h11, tx),
                tz);
        }

        private void HideCircle()
        {
            if (_circleGo != null && _circleGo.activeSelf)
                _circleGo.SetActive(false);
            _brushVisible = false;
        }

        private void OnDestroy()
        {
            if (_circleLine != null && _circleLine.material != null)
                Destroy(_circleLine.material);
        }
    }
}
