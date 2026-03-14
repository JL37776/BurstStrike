using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Game.Map.Terrain;

namespace Game.Map
{
    /// <summary>
    /// 运行时地图编辑器 UI。简约设计，顶部工具栏 + 左侧面板 + 底部状态栏。
    /// </summary>
    public sealed class MapEditorUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MapEditorBridge bridge;

        // ── 尺寸 ──
        private const float TopBarH    = 36f;
        private const float PanelW     = 280f;
        private const float StatusH    = 28f;
        private const float BtnH       = 32f;
        private const float RowH       = 30f;
        private const float Gap        = 4f;
        private const int   FontL      = 16;
        private const int   FontM      = 14;
        private const int   FontS      = 13;

        // ── 颜色 ──
        private static readonly Color C_Bg       = c(30, 30, 34);
        private static readonly Color C_Panel    = c(38, 38, 42);
        private static readonly Color C_Header   = c(24, 24, 28);
        private static readonly Color C_Btn      = c(55, 55, 62);
        private static readonly Color C_BtnHover = c(70, 70, 80);
        private static readonly Color C_BtnPress = c(60, 120, 200);
        private static readonly Color C_Input    = c(20, 20, 24);
        private static readonly Color C_Text     = c(220, 220, 220);
        private static readonly Color C_TextDim  = c(130, 130, 140);
        private static readonly Color C_Accent   = c(70, 140, 220);
        private static readonly Color C_Sep      = c(18, 18, 20);
        private static Color c(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

        // ── UI 引用 ──
        private Font       _font;
        private GameObject _fileMenu;
        private bool       _fileMenuOpen;
        private int        _menuOpenFrame; // 菜单打开的帧号，用于跳过同帧关闭
        private Text       _statusText;
        private GameObject _newDlg;
        private InputField _dlgW, _dlgH, _dlgS;
        private GameObject _saveDlg;
        private InputField _savePathInput;
        private GameObject _openDlg;
        private RectTransform _openFileListContent;
        private RectTransform _panelContent;
        private MapGridRenderer _gridRenderer;
        private TerrainBrushTool _brushTool;

        // ── Slider 引用 ──
        private Slider _sliderRadius;
        private Slider _sliderStrength;
        private Slider _sliderHeight;
        private Slider _sliderStampHeight;

        // ── 工具选中状态 ──
        private GameObject _activeModeBtn;
        private static readonly Color C_BtnSelected = c(50, 100, 180);

        // ── 自动Splat ──
        private bool _autoSplat;

        // ── 贴图选择器 ──
        private GameObject    _texBrowserDlg;
        private RectTransform _texGridContent;
        private int           _texBrowserTargetLayer;
        private string        _texBrowserTargetProp; // 当前选择的 shader 属性名
        private readonly System.Collections.Generic.Dictionary<string, Image> _dynPreviews
            = new System.Collections.Generic.Dictionary<string, Image>();
        private readonly System.Collections.Generic.Dictionary<string, Text> _dynNames
            = new System.Collections.Generic.Dictionary<string, Text>();

        // ── Shader 选择器 ──
        private RectTransform _shaderParamsContainer;
        private TerrainShaderProfile _currentProfile;


        // ── 默认地图 ──
        private const int   DefaultMapSize    = 50;
        private const float DefaultMapSpacing = 1f;

        // ────────────────────────── 生命周期 ──────────────────────────

        private void Awake()
        {
            if (bridge == null)
            {
                bridge = GetComponent<MapEditorBridge>();
                if (bridge == null) bridge = gameObject.AddComponent<MapEditorBridge>();
            }
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, FontM);
            EnsureEventSystem();
            EnsureCamera();
            Build();
            InitDefaultMap();
        }

        private void Update()
        {
            UpdateStatus();

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.N)) OnNew();
            if (ctrl && Input.GetKeyDown(KeyCode.O)) OnOpen();
            if (ctrl && Input.GetKeyDown(KeyCode.S)) OnSave();

            // 点击空白处关闭下拉菜单
            if (_fileMenuOpen && Input.GetMouseButtonUp(0) && Time.frameCount > _menuOpenFrame)
            {
                var fmRect = _fileMenu.GetComponent<RectTransform>();
                if (!RectTransformUtility.RectangleContainsScreenPoint(fmRect, Input.mousePosition, null))
                {
                    _fileMenuOpen = false;
                    _fileMenu.SetActive(false);
                }
            }
        }

        // ────────────────────────── 初始化 ──────────────────────────

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        private void EnsureCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("EditorCamera");
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
                go.tag = "MainCamera";
            }
            if (cam.GetComponent<SceneViewCamera>() == null)
                cam.gameObject.AddComponent<SceneViewCamera>();

            // F 键归位回调
            cam.GetComponent<SceneViewCamera>().OnFocusRequest = FocusCameraOnMap;

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = c(18, 18, 22);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2000f;
            // 位置先设个默认，InitDefaultMap 会根据实际地图尺寸覆盖
            cam.transform.position = new Vector3(0, 40, -40);
            cam.transform.rotation = Quaternion.Euler(45, 0, 0);
        }

        /// <summary>创建默认空白地图 + 网格渲染 + 摄像机对准地图中心</summary>
        private void InitDefaultMap()
        {
            // 创建地图数据
            bridge.Editor.CreateNew(DefaultMapSize, DefaultMapSize, DefaultMapSpacing);
            bridge.CurrentFilePath = null;

            // 创建网格渲染器
            var gridGo = new GameObject("MapGrid");
            _gridRenderer = gridGo.AddComponent<MapGridRenderer>();
            RebuildGrid();

            // 创建笔刷工具
            _brushTool = gridGo.AddComponent<TerrainBrushTool>();
            _brushTool.Init(bridge, _gridRenderer);
            _brushTool.IsActive = false;

            // 摄像机以 RTS 俯视角对准地图中心
            FocusCameraOnMap();
        }

        /// <summary>刷新地形网格渲染</summary>
        private void RebuildGrid()
        {
            if (_gridRenderer == null || !bridge.Editor.HasData) return;
            _gridRenderer.Rebuild(bridge.Editor.GetCurrentMapData());
        }

        /// <summary>将摄像机以 RTS 视角对准地图中心</summary>
        private void FocusCameraOnMap()
        {
            if (!bridge.Editor.HasData) return;
            var data = bridge.Editor.GetCurrentMapData();

            float mapW = (data.Width - 1) * data.VertexSpacing;
            float mapH = (data.Height - 1) * data.VertexSpacing;
            var center = new Vector3(mapW * 0.5f, 0, mapH * 0.5f);

            // RTS 经典视角：俯角 ~55°，从地图南侧偏上方看向中心
            float camDist = Mathf.Max(mapW, mapH) * 0.75f;
            float pitch = 55f;
            float yaw = 0f;

            var cam = Camera.main;
            if (cam == null) return;

            cam.transform.position = center + Quaternion.Euler(pitch, yaw, 0) * new Vector3(0, 0, -camDist);
            cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0);

            // 同步 SceneViewCamera 内部状态
            var svc = cam.GetComponent<SceneViewCamera>();
            if (svc != null) svc.SetRotation(pitch, yaw);
        }

        // ════════════════════════════ UI 构建 ════════════════════════════

        private void Build()
        {
            // Canvas
            var cGo = new GameObject("EditorCanvas");
            cGo.transform.SetParent(transform, false);
            var canvas = cGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            cGo.AddComponent<GraphicRaycaster>();
            var scaler = cGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var root = cGo.GetComponent<RectTransform>();

            BuildTopBar(root);
            BuildPanel(root);
            BuildStatusBar(root);
            BuildCameraResetButton(root);
            BuildNewDialog(root);
            BuildSaveDialog(root);
            BuildOpenDialog(root);
            BuildTextureBrowserDialog(root);
        }

        // ──────────── 顶部工具栏 ────────────

        private void BuildTopBar(RectTransform root)
        {
            var bar = MakeRect(root, "TopBar", C_Header);
            Anchor(bar, 0, 1, 1, 1);
            bar.offsetMin = new Vector2(0, -TopBarH);
            bar.offsetMax = Vector2.zero;

            var hl = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.spacing = 2;
            hl.padding = new RectOffset(6, 6, 2, 2);
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;

            // 文件
            var fileBtn = MakeBtn(bar, "文件", 56, TopBarH - 4, FontM, C_Header);
            fileBtn.GetComponent<Button>().onClick.AddListener(ToggleFileMenu);

            // 文件下拉
            _fileMenu = new GameObject("FileMenu");
            _fileMenu.transform.SetParent(fileBtn.transform, false);

            // 覆盖 Canvas 确保下拉菜单渲染在所有面板之上
            var fmCanvas = _fileMenu.AddComponent<Canvas>();
            fmCanvas.overrideSorting = true;
            fmCanvas.sortingOrder = 200;
            _fileMenu.AddComponent<GraphicRaycaster>();

            var fmImg = _fileMenu.AddComponent<Image>();
            fmImg.color = C_Panel;
            var fmR = _fileMenu.GetComponent<RectTransform>();
            fmR.pivot = new Vector2(0, 1);
            fmR.anchorMin = fmR.anchorMax = Vector2.zero;
            fmR.anchoredPosition = Vector2.zero;

            var fmL = _fileMenu.AddComponent<VerticalLayoutGroup>();
            fmL.spacing = 1; fmL.padding = new RectOffset(2, 2, 4, 4);
            fmL.childForceExpandWidth = true; fmL.childForceExpandHeight = false;
            var fmF = _fileMenu.AddComponent<ContentSizeFitter>();
            fmF.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fmF.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            MenuItem(_fileMenu.transform, "新建        Ctrl+N", OnNew);
            MenuSep(_fileMenu.transform);
            MenuItem(_fileMenu.transform, "打开...", OnOpen);
            MenuItem(_fileMenu.transform, "保存        Ctrl+S", OnSave);
            MenuItem(_fileMenu.transform, "另存为...", OnSaveAs);
            MenuSep(_fileMenu.transform);
            MenuItem(_fileMenu.transform, "退出", OnExit);
            _fileMenu.SetActive(false);

            // 分隔
            MakeSep(bar, vertical: true);

            // 标题
            var title = MakeLabel(bar, "地图编辑器", FontM, C_TextDim);
            title.GetComponent<LayoutElement>().flexibleWidth = 1;
        }

        // ──────────── 左侧面板 ────────────

        private void BuildPanel(RectTransform root)
        {
            // 面板背景
            var panel = MakeRect(root, "Panel", C_Bg);
            Anchor(panel, 0, 0, 0, 1);
            panel.offsetMin = new Vector2(0, StatusH);
            panel.offsetMax = new Vector2(PanelW, -TopBarH);

            // ScrollView 容器
            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(panel, false);
            var scrollRT = scrollGo.GetComponent<RectTransform>();
            scrollRT.anchorMin = Vector2.zero;
            scrollRT.anchorMax = Vector2.one;
            scrollRT.offsetMin = Vector2.zero;
            scrollRT.offsetMax = Vector2.zero;
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            // Viewport
            var vpGo = new GameObject("Viewport", typeof(RectTransform));
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRT = vpGo.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero;
            vpRT.offsetMax = Vector2.zero;
            vpGo.AddComponent<Image>().color = C_Bg;
            vpGo.AddComponent<RectMask2D>();
            scroll.viewport = vpRT;

            // Content
            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vpGo.transform, false);
            _panelContent = cGo.GetComponent<RectTransform>();
            _panelContent.anchorMin = new Vector2(0, 1);
            _panelContent.anchorMax = new Vector2(1, 1);
            _panelContent.pivot = new Vector2(0.5f, 1);
            _panelContent.anchoredPosition = Vector2.zero;
            _panelContent.sizeDelta = new Vector2(0, 0);
            var vl = cGo.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 2;
            vl.padding = new RectOffset(6, 6, 6, 6);
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            cGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _panelContent;

            // ══════ 绘制地图 ══════
            Foldout("绘制地图", true, paintSec =>
            {
                // ── 高度绘制 ──
                SubFoldout(paintSec, "高度绘制", true, heightSec =>
                {
                    SectionLabel(heightSec, "连续绘制");
                    var shapeRow = Row(heightSec);

                    // 圆形升高
                    var btnCircleUp = ToolBtn(shapeRow, "⬆ 升高");
                    btnCircleUp.GetComponent<Button>().onClick.AddListener(() =>
                    {
                        SelectMode(btnCircleUp);
                        if (_brushTool != null)
                        {
                            _brushTool.IsActive = true;
                            _brushTool.Mode = BrushMode.Continuous;
                            _brushTool.HeightPerSec = Mathf.Abs(_brushTool.HeightPerSec);
                        }
                    });

                    // 圆形降低
                    var btnCircleDown = ToolBtn(shapeRow, "⬇ 降低");
                    btnCircleDown.GetComponent<Button>().onClick.AddListener(() =>
                    {
                        SelectMode(btnCircleDown);
                        if (_brushTool != null)
                        {
                            _brushTool.IsActive = true;
                            _brushTool.Mode = BrushMode.Continuous;
                            _brushTool.HeightPerSec = -Mathf.Abs(_brushTool.HeightPerSec);
                        }
                    });

                    Sep(heightSec);
                    SectionLabel(heightSec, "台阶绘制");
                    var stampRow = Row(heightSec);

                    var btnStamp = ToolBtn(stampRow, "◼ 台阶");
                    btnStamp.GetComponent<Button>().onClick.AddListener(() =>
                    {
                        SelectMode(btnStamp);
                        if (_brushTool != null)
                        {
                            _brushTool.IsActive = true;
                            _brushTool.Mode = BrushMode.Stamp;
                        }
                    });

                    _sliderStampHeight = SliderRow(heightSec, "高度", -20f, 20f, 5f);
                    _sliderStampHeight.onValueChanged.AddListener(v =>
                    {
                        if (_brushTool != null) _brushTool.StampHeight = v;
                    });

                    Sep(heightSec);
                    SectionLabel(heightSec, "笔刷参数");

                    _sliderRadius = SliderRow(heightSec, "大小", 1, 30, 5);
                    _sliderRadius.wholeNumbers = true;
                    _sliderRadius.onValueChanged.AddListener(v =>
                    {
                        if (_brushTool != null) _brushTool.BrushRadius = Mathf.RoundToInt(v);
                    });

                    _sliderStrength = SliderRow(heightSec, "强度", 0.05f, 1f, 0.5f);
                    _sliderStrength.onValueChanged.AddListener(v =>
                    {
                        if (_brushTool != null) _brushTool.BrushStrength = v;
                    });

                    _sliderHeight = SliderRow(heightSec, "速度", 1f, 20f, 5f);
                    _sliderHeight.wholeNumbers = true;
                    _sliderHeight.onValueChanged.AddListener(v =>
                    {
                        if (_brushTool != null)
                        {
                            float sign = _brushTool.HeightPerSec >= 0 ? 1f : -1f;
                            _brushTool.HeightPerSec = v * sign;
                        }
                    });
                });

                // ── 材质绘制 ──
                SubFoldout(paintSec, "材质绘制", false, matSec =>
                {
                    SectionLabel(matSec, "绘制层（选中即激活）");
                    var layerRow = Row(matSec);

                    var btnTop = ToolBtn(layerRow, "Top");
                    btnTop.GetComponent<Button>().onClick.AddListener(() =>
                    {
                        SelectMode(btnTop);
                        if (_brushTool != null)
                        {
                            _brushTool.IsActive = true;
                            _brushTool.Mode = BrushMode.SplatPaint;
                            _brushTool.SplatTargetTop = 1f;
                            _brushTool.SplatTargetCliff = 0f;
                            _brushTool.SplatTargetBottom = 0f;
                        }
                    });

                    var btnCliff = ToolBtn(layerRow, "Cliff");
                    btnCliff.GetComponent<Button>().onClick.AddListener(() =>
                    {
                        SelectMode(btnCliff);
                        if (_brushTool != null)
                        {
                            _brushTool.IsActive = true;
                            _brushTool.Mode = BrushMode.SplatPaint;
                            _brushTool.SplatTargetTop = 0f;
                            _brushTool.SplatTargetCliff = 1f;
                            _brushTool.SplatTargetBottom = 0f;
                        }
                    });

                    var btnBottom = ToolBtn(layerRow, "Bottom");
                    btnBottom.GetComponent<Button>().onClick.AddListener(() =>
                    {
                        SelectMode(btnBottom);
                        if (_brushTool != null)
                        {
                            _brushTool.IsActive = true;
                            _brushTool.Mode = BrushMode.SplatPaint;
                            _brushTool.SplatTargetTop = 0f;
                            _brushTool.SplatTargetCliff = 0f;
                            _brushTool.SplatTargetBottom = 1f;
                        }
                    });

                    Sep(matSec);

                    // ── Shader 选择器（下拉列表）──
                    SectionLabel(matSec, "Shader");
                    BuildShaderDropdown(matSec);

                    Sep(matSec);

                    // ── 动态参数容器 ──
                    var containerGo = new GameObject("ShaderParams");
                    containerGo.transform.SetParent(matSec, false);
                    _shaderParamsContainer = containerGo.AddComponent<RectTransform>();
                    var vl = containerGo.AddComponent<VerticalLayoutGroup>();
                    vl.spacing = 3; vl.childForceExpandWidth = true;
                    vl.childForceExpandHeight = false;
                    containerGo.AddComponent<ContentSizeFitter>().verticalFit
                        = ContentSizeFitter.FitMode.PreferredSize;

                    // 默认加载基础 shader
                    SwitchShaderProfile(TerrainShaderRegistry.All[0]);

                    Sep(matSec);
                    SectionLabel(matSec, "笔刷");

                    var matRadius = SliderRow(matSec, "大小", 1, 30, 5);
                    matRadius.wholeNumbers = true;
                    matRadius.onValueChanged.AddListener(v =>
                    {
                        if (_brushTool != null) _brushTool.BrushRadius = Mathf.RoundToInt(v);
                    });

                    var matStrength = SliderRow(matSec, "强度", 0.05f, 1f, 0.5f);
                    matStrength.onValueChanged.AddListener(v =>
                    {
                        if (_brushTool != null) _brushTool.BrushStrength = v;
                    });
                });
            });

            // ══════ 工具 ══════
            Foldout("工具", false, toolSec =>
            {
                // 自动 Splat 勾选框
                var autoSplatToggle = ToggleRow(toolSec, "修改高度时自动更新 Splat", false);
                autoSplatToggle.onValueChanged.AddListener(v =>
                {
                    _autoSplat = v;
                    if (_brushTool != null) _brushTool.AutoSplat = v;
                });

                Sep(toolSec);

                // 渲染平滑等级
                SectionLabel(toolSec, "渲染平滑");
                var subdivSlider = SliderRow(toolSec, "细分等级", 1, 4, 1);
                subdivSlider.wholeNumbers = true;
                subdivSlider.onValueChanged.AddListener(v =>
                {
                    if (_gridRenderer != null)
                    {
                        _gridRenderer.SubdivisionLevel = Mathf.RoundToInt(v);
                        RebuildGrid();
                    }
                });

                Sep(toolSec);
                var r = Row(toolSec);
                var btnFlatten = ToolBtn(r, "全图压平");
                btnFlatten.GetComponent<Button>().onClick.AddListener(() =>
                {
                    SelectMode(null); // 取消选中
                    if (_brushTool != null) _brushTool.IsActive = false;
                    if (bridge.Editor.HasData)
                    {
                        bridge.Editor.FlatFill(0f);
                        RebuildGrid();
                    }
                });
                var btnAutoSplat = ToolBtn(r, "自动Splat");
                btnAutoSplat.GetComponent<Button>().onClick.AddListener(() =>
                {
                    SelectMode(null);
                    if (_brushTool != null) _brushTool.IsActive = false;
                    if (bridge.Editor.HasData)
                    {
                        bridge.Editor.AutoGenerateSplat();
                        RebuildGrid();
                    }
                });
            });
        }

        // ──────────── 底部状态栏 ────────────

        private void BuildStatusBar(RectTransform root)
        {
            var bar = MakeRect(root, "StatusBar", C_Header);
            Anchor(bar, 0, 0, 1, 0);
            bar.offsetMin = Vector2.zero;
            bar.offsetMax = new Vector2(0, StatusH);

            var hl = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.padding = new RectOffset(10, 10, 0, 0);
            hl.childForceExpandHeight = true;

            _statusText = MakeLabel(bar, "就绪 — 右键旋转 | 中键平移 | 滚轮缩放 | WASD飞行",
                FontS, C_TextDim);
        }

        private void UpdateStatus()
        {
            if (_statusText == null) return;
            if (!bridge.Editor.HasData)
            {
                _statusText.text = "未加载 — 文件 > 新建  |  右键旋转 · 中键平移 · 滚轮缩放 · 右键+WASD飞行";
                return;
            }
            var d = bridge.Editor.GetCurrentMapData();
            string dirty = bridge.Editor.IsDirty ? "*" : "";
            string file = bridge.CurrentFilePath != null ? Path.GetFileName(bridge.CurrentFilePath) : "未保存";
            _statusText.text = d.Width + "x" + d.Height + "  间距:" + d.VertexSpacing.ToString("F1")
                + "  " + file + dirty
                + "  |  右键旋转 · 中键平移 · 滚轮缩放 · 右键+WASD飞行";
        }

        // ──────────── 右下角摄像机归位按钮 ────────────

        private void BuildCameraResetButton(RectTransform root)
        {
            var go = MakeBtn(root, "⟲ 归位 (F)", 100, BtnH + 4, FontS, C_Btn);
            var rt = go.GetComponent<RectTransform>();
            // 右下角定位，在状态栏上方
            rt.anchorMin = rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(1, 0);
            rt.anchoredPosition = new Vector2(-12, StatusH + 8);
            // 取消 LayoutElement 对锚点定位的干扰
            var le = go.GetComponent<LayoutElement>();
            le.ignoreLayout = true;
            rt.sizeDelta = new Vector2(100, BtnH + 4);

            go.GetComponent<Button>().onClick.AddListener(FocusCameraOnMap);
        }

        // ──────────── 新建对话框 ────────────

        private void BuildNewDialog(RectTransform root)
        {
            _newDlg = new GameObject("NewDialog");
            _newDlg.transform.SetParent(root, false);
            var oR = Stretch(_newDlg.AddComponent<RectTransform>());
            _newDlg.AddComponent<Image>().color = new Color(0, 0, 0, 0.55f);

            var dGo = new GameObject("Box");
            dGo.transform.SetParent(_newDlg.transform, false);
            var dR = dGo.AddComponent<RectTransform>();
            dR.anchorMin = dR.anchorMax = new Vector2(0.5f, 0.5f);
            dR.sizeDelta = new Vector2(380, 280);
            dGo.AddComponent<Image>().color = C_Panel;

            var vl = dGo.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.spacing = 8; vl.padding = new RectOffset(20, 20, 16, 16);
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;

            MakeLabel(dR, "新建地图", FontL, C_Text).alignment = TextAnchor.MiddleCenter;
            Sep(dR);

            _dlgW = InputRow(dR, "宽度 (顶点)", "128");
            _dlgH = InputRow(dR, "高度 (顶点)", "128");
            _dlgS = InputRow(dR, "顶点间距",    "1.0");

            var bRow = Row(dR);
            var ok = MakeBtn(bRow, "创建", -1, BtnH, FontM, C_BtnPress);
            ok.GetComponent<Button>().onClick.AddListener(OnNewConfirm);
            var cancel = MakeBtn(bRow, "取消", -1, BtnH, FontM, C_Btn);
            cancel.GetComponent<Button>().onClick.AddListener(() => _newDlg.SetActive(false));

            _newDlg.SetActive(false);
        }

        // ──────────── 保存对话框 ────────────

        private void BuildSaveDialog(RectTransform root)
        {
            _saveDlg = new GameObject("SaveDialog");
            _saveDlg.transform.SetParent(root, false);
            Stretch(_saveDlg.AddComponent<RectTransform>());
            _saveDlg.AddComponent<Image>().color = new Color(0, 0, 0, 0.55f);

            var dGo = new GameObject("Box");
            dGo.transform.SetParent(_saveDlg.transform, false);
            var dR = dGo.AddComponent<RectTransform>();
            dR.anchorMin = dR.anchorMax = new Vector2(0.5f, 0.5f);
            dR.sizeDelta = new Vector2(500, 200);
            dGo.AddComponent<Image>().color = C_Panel;

            var vl = dGo.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.spacing = 8; vl.padding = new RectOffset(20, 20, 16, 16);
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;

            MakeLabel(dR, "保存地图", FontL, C_Text).alignment = TextAnchor.MiddleCenter;
            Sep(dR);

            _savePathInput = InputRow(dR, "文件路径", "");

            var bRow = Row(dR);
            var ok = MakeBtn(bRow, "保存", -1, BtnH, FontM, C_BtnPress);
            ok.GetComponent<Button>().onClick.AddListener(OnSaveConfirm);
            var cancel = MakeBtn(bRow, "取消", -1, BtnH, FontM, C_Btn);
            cancel.GetComponent<Button>().onClick.AddListener(() => _saveDlg.SetActive(false));

            _saveDlg.SetActive(false);
        }

        // ──────────── 打开对话框 ────────────

        private void BuildOpenDialog(RectTransform root)
        {
            _openDlg = new GameObject("OpenDialog");
            _openDlg.transform.SetParent(root, false);
            Stretch(_openDlg.AddComponent<RectTransform>());
            _openDlg.AddComponent<Image>().color = new Color(0, 0, 0, 0.55f);

            var dGo = new GameObject("Box");
            dGo.transform.SetParent(_openDlg.transform, false);
            var dR = dGo.AddComponent<RectTransform>();
            dR.anchorMin = dR.anchorMax = new Vector2(0.5f, 0.5f);
            dR.sizeDelta = new Vector2(520, 400);
            dGo.AddComponent<Image>().color = C_Panel;

            var vl = dGo.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.spacing = 6; vl.padding = new RectOffset(16, 16, 14, 14);
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;

            MakeLabel(dR, "打开地图", FontL, C_Text).alignment = TextAnchor.MiddleCenter;
            Sep(dR);

            // 文件列表滚动区域
            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(dGo.transform, false);
            scrollGo.AddComponent<RectTransform>();
            var scrollLe = scrollGo.AddComponent<LayoutElement>();
            scrollLe.flexibleHeight = 1; scrollLe.flexibleWidth = 1;
            scrollLe.preferredHeight = 260;

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(scrollGo.transform, false);
            Stretch(vpGo.AddComponent<RectTransform>());
            vpGo.AddComponent<Image>().color = C_Input;
            vpGo.AddComponent<Mask>().showMaskGraphic = true;
            scroll.viewport = vpGo.GetComponent<RectTransform>();

            var cGo = new GameObject("Content");
            cGo.transform.SetParent(vpGo.transform, false);
            _openFileListContent = cGo.AddComponent<RectTransform>();
            _openFileListContent.anchorMin = new Vector2(0, 1);
            _openFileListContent.anchorMax = new Vector2(1, 1);
            _openFileListContent.pivot = new Vector2(0, 1);
            var cvl = cGo.AddComponent<VerticalLayoutGroup>();
            cvl.spacing = 2; cvl.padding = new RectOffset(4, 4, 4, 4);
            cvl.childForceExpandWidth = true; cvl.childForceExpandHeight = false;
            cGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _openFileListContent;

            // 底部按钮
            var bRow = Row(dR);
            var cancel = MakeBtn(bRow, "取消", -1, BtnH, FontM, C_Btn);
            cancel.GetComponent<Button>().onClick.AddListener(() => _openDlg.SetActive(false));

            _openDlg.SetActive(false);
        }

        // ════════════════════════════ 事件 ════════════════════════════

        private void ToggleFileMenu()
        {
            _fileMenuOpen = !_fileMenuOpen;
            _fileMenu.SetActive(_fileMenuOpen);
            if (_fileMenuOpen)
                _menuOpenFrame = Time.frameCount;
        }

        private void CloseMenus()
        {
            if (!_fileMenuOpen) return;
            _fileMenuOpen = false;
            _fileMenu.SetActive(false);
        }

        private void OnNew()
        {
            CloseMenus();
            _newDlg.SetActive(true);
            _dlgW.text = "128"; _dlgH.text = "128"; _dlgS.text = "1.0";
        }

        private void OnNewConfirm()
        {
            if (!int.TryParse(_dlgW.text, out int w)) w = 128;
            if (!int.TryParse(_dlgH.text, out int h)) h = 128;
            if (!float.TryParse(_dlgS.text, out float s)) s = 1f;
            w = Mathf.Clamp(w, 2, 1024);
            h = Mathf.Clamp(h, 2, 1024);
            s = Mathf.Clamp(s, 0.1f, 10f);
            bridge.Editor.CreateNew(w, h, s);
            bridge.CurrentFilePath = null;
            _newDlg.SetActive(false);
            RebuildGrid();
            FocusCameraOnMap();
        }

        private void OnOpen()
        {
            CloseMenus();
            // 扫描 StreamingAssets 下的 .tmap 文件
            string dir = Application.streamingAssetsPath;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // 清空旧列表
            for (int i = _openFileListContent.childCount - 1; i >= 0; i--)
                Destroy(_openFileListContent.GetChild(i).gameObject);

            var files = Directory.GetFiles(dir, "*.tmap");
            if (files.Length == 0)
            {
                MakeLabel(_openFileListContent, "  (未找到 .tmap 文件)", FontM, C_TextDim);
            }
            else
            {
                foreach (var f in files)
                {
                    string fullPath = f;
                    string fileName = Path.GetFileName(f);
                    long size = new FileInfo(f).Length;
                    string sizeStr = size < 1024 ? size + " B"
                        : size < 1048576 ? (size / 1024f).ToString("F1") + " KB"
                        : (size / 1048576f).ToString("F1") + " MB";

                    var item = MakeBtn(_openFileListContent, fileName + "  (" + sizeStr + ")",
                        -1, BtnH + 4, FontM, C_Btn);
                    item.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                    item.GetComponent<Button>().onClick.AddListener(() => OnOpenFile(fullPath));
                }
            }

            _openDlg.SetActive(true);
        }

        private void OnOpenFile(string path)
        {
            try
            {
                bridge.Editor.LoadFromFile(path);
                bridge.CurrentFilePath = path;
                _openDlg.SetActive(false);
                RebuildGrid();
                FocusCameraOnMap();
            }
            catch (Exception e)
            {
                Debug.LogError("Open failed: " + e.Message);
            }
        }

        private void OnSave()
        {
            CloseMenus();
            if (!bridge.Editor.HasData) return;

            // 有已知路径则直接保存
            if (!string.IsNullOrEmpty(bridge.CurrentFilePath))
            {
                try
                {
                    bridge.Editor.SaveToFile(bridge.CurrentFilePath);
                    RebuildGrid(); // 保存后刷新网格确保同步
                }
                catch (Exception e) { Debug.LogError("Save failed: " + e.Message); }
                return;
            }

            // 无路径则弹出另存为
            OnSaveAs();
        }

        private void OnSaveAs()
        {
            CloseMenus();
            if (!bridge.Editor.HasData) return;

            string dir = Application.streamingAssetsPath;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // 填充默认路径
            string defaultName = "map_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".tmap";
            string defaultPath = Path.Combine(dir, defaultName);
            _savePathInput.text = defaultPath;

            _saveDlg.SetActive(true);
        }

        private void OnSaveConfirm()
        {
            string path = _savePathInput.text.Trim();
            if (string.IsNullOrEmpty(path)) return;

            // 自动补扩展名
            if (!path.EndsWith(".tmap", StringComparison.OrdinalIgnoreCase))
                path += ".tmap";

            try
            {
                bridge.Editor.SaveToFile(path);
                bridge.CurrentFilePath = path;
                _saveDlg.SetActive(false);
                RebuildGrid(); // 保存后刷新网格确保同步
            }
            catch (Exception e)
            {
                Debug.LogError("Save failed: " + e.Message);
            }
        }

        private void OnExit()
        {
            CloseMenus();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ════════════════════════════ UI 工厂 ════════════════════════════

        // ---- 基础 ----

        private RectTransform MakeRect(Transform parent, string n, Color bg)
        {
            var go = new GameObject(n);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;
            return rt;
        }

        private static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
        }

        // ---- 按钮 ----

        private GameObject MakeBtn(Transform parent, string label, float w, float h,
            int fontSize, Color bg)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = bg;
            colors.highlightedColor = C_BtnHover;
            colors.pressedColor = C_BtnPress;
            colors.selectedColor = bg;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = h;
            if (w > 0) le.preferredWidth = w; else le.flexibleWidth = 1;

            var tGo = new GameObject("T");
            tGo.transform.SetParent(go.transform, false);
            var tR = tGo.AddComponent<RectTransform>();
            tR.anchorMin = Vector2.zero; tR.anchorMax = Vector2.one;
            tR.offsetMin = new Vector2(4, 0); tR.offsetMax = new Vector2(-4, 0);
            var txt = tGo.AddComponent<Text>();
            txt.text = label;
            txt.font = _font;
            txt.fontSize = fontSize;
            txt.color = C_Text;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;

            return go;
        }

        // ---- 菜单项 ----

        private void MenuItem(Transform parent, string label, Action onClick)
        {
            var go = MakeBtn(parent, label, -1, RowH, FontM, C_Panel);
            go.GetComponent<LayoutElement>().minWidth = 220;
            go.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            go.GetComponent<Button>().onClick.AddListener(() => onClick());
        }

        private void MenuSep(Transform parent)
        {
            var go = new GameObject("Sep");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = C_Sep;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 1; le.flexibleWidth = 1;
        }

        // ---- 文本 ----

        private Text MakeLabel(Transform parent, string text, int fs, Color col)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = RowH; le.flexibleWidth = 1;
            var t = go.AddComponent<Text>();
            t.text = text; t.font = _font; t.fontSize = fs;
            t.color = col; t.alignment = TextAnchor.MiddleLeft;
            t.raycastTarget = false;
            return t;
        }

        // ---- 面板组件 ----

        private void Foldout(string title, bool startOpen, Action<RectTransform> build)
        {
            var sec = new GameObject(title);
            sec.transform.SetParent(_panelContent, false);
            sec.AddComponent<RectTransform>();
            var vl = sec.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 2; vl.padding = new RectOffset(0, 0, 0, 2);
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            sec.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var hdr = MakeBtn(sec.transform, (startOpen ? "\u25bc " : "\u25b6 ") + title,
                -1, BtnH + 2, FontM, C_Header);
            hdr.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;

            var body = new GameObject("Body");
            body.transform.SetParent(sec.transform, false);
            var bR = body.AddComponent<RectTransform>();
            var bVl = body.AddComponent<VerticalLayoutGroup>();
            bVl.spacing = 3; bVl.padding = new RectOffset(8, 4, 6, 6);
            bVl.childForceExpandWidth = true; bVl.childForceExpandHeight = false;
            body.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            body.AddComponent<Image>().color = C_Panel;

            body.SetActive(startOpen);
            string t = title;
            var hdrTxt = hdr.GetComponentInChildren<Text>();
            hdr.GetComponent<Button>().onClick.AddListener(() =>
            {
                bool open = !body.activeSelf;
                body.SetActive(open);
                hdrTxt.text = (open ? "\u25bc " : "\u25b6 ") + t;
            });

            build(bR);
        }

        /// <summary>子级可折叠面板，可嵌套在任意父容器内</summary>
        private void SubFoldout(RectTransform parent, string title, bool startOpen,
            Action<RectTransform> build)
        {
            var sec = new GameObject(title);
            sec.transform.SetParent(parent, false);
            sec.AddComponent<RectTransform>();
            var vl = sec.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 2; vl.padding = new RectOffset(0, 0, 0, 2);
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            sec.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var hdr = MakeBtn(sec.transform, (startOpen ? "\u25bc " : "\u25b6 ") + title,
                -1, BtnH, FontS, c(32, 32, 38));
            hdr.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;

            var body = new GameObject("Body");
            body.transform.SetParent(sec.transform, false);
            var bR = body.AddComponent<RectTransform>();
            var bVl = body.AddComponent<VerticalLayoutGroup>();
            bVl.spacing = 3; bVl.padding = new RectOffset(10, 4, 6, 6);
            bVl.childForceExpandWidth = true; bVl.childForceExpandHeight = false;
            body.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            body.AddComponent<Image>().color = c(34, 34, 40);

            body.SetActive(startOpen);
            string t = title;
            var hdrTxt = hdr.GetComponentInChildren<Text>();
            hdr.GetComponent<Button>().onClick.AddListener(() =>
            {
                bool open = !body.activeSelf;
                body.SetActive(open);
                hdrTxt.text = (open ? "\u25bc " : "\u25b6 ") + t;
            });

            build(bR);
        }

        /// <summary>选中工具按钮高亮，取消其他按钮高亮，null 表示全取消</summary>
        private void SelectMode(GameObject btn)
        {
            // 恢复之前选中的按钮颜色
            if (_activeModeBtn != null)
            {
                var prevImg = _activeModeBtn.GetComponent<Image>();
                if (prevImg != null) prevImg.color = C_Btn;
                var prevBtn = _activeModeBtn.GetComponent<Button>();
                if (prevBtn != null)
                {
                    var colors = prevBtn.colors;
                    colors.normalColor = C_Btn;
                    prevBtn.colors = colors;
                }
            }

            _activeModeBtn = btn;

            // 高亮新选中的按钮
            if (btn != null)
            {
                var img = btn.GetComponent<Image>();
                if (img != null) img.color = C_BtnSelected;
                var b = btn.GetComponent<Button>();
                if (b != null)
                {
                    var colors = b.colors;
                    colors.normalColor = C_BtnSelected;
                    b.colors = colors;
                }
            }
        }

        private void SectionLabel(RectTransform parent, string text)
        {
            var t = MakeLabel(parent, text, FontS, C_TextDim);
            t.fontStyle = FontStyle.Bold;
            t.GetComponent<LayoutElement>().preferredHeight = RowH - 4;
        }

        private RectTransform Row(RectTransform parent)
        {
            var go = new GameObject("Row");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            var hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 3;
            hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;
            go.AddComponent<LayoutElement>().preferredHeight = BtnH;
            return rt;
        }

        private void RowLabel(RectTransform row, string text)
        {
            var go = new GameObject(text);
            go.transform.SetParent(row, false);
            var t = go.AddComponent<Text>();
            t.text = text; t.font = _font; t.fontSize = FontS;
            t.color = C_TextDim; t.alignment = TextAnchor.MiddleLeft;
            t.raycastTarget = false;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 44; le.preferredHeight = BtnH;
        }

        private GameObject ToolBtn(RectTransform row, string label)
        {
            return MakeBtn(row, label, -1, BtnH, FontS, C_Btn);
        }

        private void Sep(RectTransform parent)
        {
            var go = new GameObject("Sep");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = C_Sep;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 1; le.flexibleWidth = 1;
        }

        private void MakeSep(RectTransform parent, bool vertical)
        {
            var go = new GameObject("Sep");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = C_Sep;
            var le = go.AddComponent<LayoutElement>();
            if (vertical) { le.preferredWidth = 1; le.flexibleHeight = 1; }
            else { le.preferredHeight = 1; le.flexibleWidth = 1; }
        }

        // ---- Slider ----

        private Slider SliderRow(RectTransform parent, string label, float min, float max, float def)
        {
            var row = Row(parent);
            RowLabel(row, label);

            // slider
            var sGo = new GameObject("Slider");
            sGo.transform.SetParent(row, false);
            sGo.AddComponent<RectTransform>();
            sGo.AddComponent<LayoutElement>().flexibleWidth = 1;

            // bg
            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(sGo.transform, false);
            var bgR = bgGo.AddComponent<RectTransform>();
            bgR.anchorMin = new Vector2(0, 0.3f); bgR.anchorMax = new Vector2(1, 0.7f);
            bgR.offsetMin = bgR.offsetMax = Vector2.zero;
            bgGo.AddComponent<Image>().color = C_Input;

            // fill
            var faGo = new GameObject("FA");
            faGo.transform.SetParent(sGo.transform, false);
            var faR = faGo.AddComponent<RectTransform>();
            faR.anchorMin = new Vector2(0, 0.3f); faR.anchorMax = new Vector2(1, 0.7f);
            faR.offsetMin = faR.offsetMax = Vector2.zero;
            var fGo = new GameObject("F");
            fGo.transform.SetParent(faGo.transform, false);
            var fR = Stretch(fGo.AddComponent<RectTransform>());
            fGo.AddComponent<Image>().color = C_Accent;

            // handle
            var haGo = new GameObject("HA");
            haGo.transform.SetParent(sGo.transform, false);
            Stretch(haGo.AddComponent<RectTransform>());
            var hGo = new GameObject("H");
            hGo.transform.SetParent(haGo.transform, false);
            var hR = hGo.AddComponent<RectTransform>();
            hR.sizeDelta = new Vector2(14, 0);
            var hImg = hGo.AddComponent<Image>();
            hImg.color = C_Text;

            var slider = sGo.AddComponent<Slider>();
            slider.fillRect = fR; slider.handleRect = hR;
            slider.targetGraphic = hImg;
            slider.minValue = min; slider.maxValue = max; slider.value = def;

            // value label
            var vGo = new GameObject("V");
            vGo.transform.SetParent(row, false);
            var vT = vGo.AddComponent<Text>();
            vT.text = def.ToString("F1"); vT.font = _font;
            vT.fontSize = FontS; vT.color = C_Text;
            vT.alignment = TextAnchor.MiddleRight;
            vT.raycastTarget = false;
            vGo.AddComponent<LayoutElement>().preferredWidth = 42;

            slider.onValueChanged.AddListener(v => vT.text = v.ToString("F1"));

            return slider;
        }

        // ---- InputField ----

        private InputField InputRow(RectTransform parent, string label, string def)
        {
            var row = Row(parent);

            // label
            var lGo = new GameObject(label);
            lGo.transform.SetParent(row, false);
            var lT = lGo.AddComponent<Text>();
            lT.text = label; lT.font = _font; lT.fontSize = FontS;
            lT.color = C_TextDim; lT.alignment = TextAnchor.MiddleLeft;
            lT.raycastTarget = false;
            lGo.AddComponent<LayoutElement>().preferredWidth = 100;

            // input
            var iGo = new GameObject("Input");
            iGo.transform.SetParent(row, false);
            var iImg = iGo.AddComponent<Image>();
            iImg.color = C_Input; iImg.raycastTarget = true;
            iGo.AddComponent<LayoutElement>().flexibleWidth = 1;

            var tGo = new GameObject("T");
            tGo.transform.SetParent(iGo.transform, false);
            var tR = tGo.AddComponent<RectTransform>();
            tR.anchorMin = Vector2.zero; tR.anchorMax = Vector2.one;
            tR.offsetMin = new Vector2(8, 2); tR.offsetMax = new Vector2(-8, -2);
            var txt = tGo.AddComponent<Text>();
            txt.font = _font; txt.fontSize = FontM;
            txt.color = C_Text; txt.alignment = TextAnchor.MiddleLeft;
            txt.supportRichText = false;

            var input = iGo.AddComponent<InputField>();
            input.textComponent = txt; input.text = def;
            input.targetGraphic = iImg;
            return input;
        }

        // ---- Toggle (checkbox) ----

        private Toggle ToggleRow(RectTransform parent, string label, bool defaultOn)
        {
            var row = Row(parent);

            // 勾选框背景
            var bgGo = new GameObject("ToggleBG");
            bgGo.transform.SetParent(row, false);
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = C_Input;
            var bgLe = bgGo.AddComponent<LayoutElement>();
            bgLe.preferredWidth = 22; bgLe.preferredHeight = 22;

            // 勾选标记
            var checkGo = new GameObject("Check");
            checkGo.transform.SetParent(bgGo.transform, false);
            var checkR = checkGo.AddComponent<RectTransform>();
            checkR.anchorMin = new Vector2(0.15f, 0.15f);
            checkR.anchorMax = new Vector2(0.85f, 0.85f);
            checkR.offsetMin = checkR.offsetMax = Vector2.zero;
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color = C_Accent;

            // Toggle 组件
            var toggle = bgGo.AddComponent<Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic = checkImg;
            toggle.isOn = defaultOn;

            // 标签
            var lT = MakeLabel(row, label, FontS, C_Text);
            lT.GetComponent<LayoutElement>().flexibleWidth = 1;

            return toggle;
        }

        // ---- 层贴图槽位 (缩略图 + 选择/清除) ----

        // ════════════════ Shader Profile 切换 & 动态 UI ════════════════

        /// <summary>构建 Shader 下拉选择器</summary>
        private void BuildShaderDropdown(RectTransform parent)
        {
            var row = Row(parent);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;

            // 下拉按钮（显示当前选中的 shader 名称）
            var ddGo = new GameObject("ShaderDropdown");
            ddGo.transform.SetParent(row, false);
            var ddImg = ddGo.AddComponent<Image>();
            ddImg.color = C_Input;
            ddImg.raycastTarget = true;
            ddGo.AddComponent<LayoutElement>().flexibleWidth = 1;

            // 当前选中文本
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(ddGo.transform, false);
            var labelR = labelGo.AddComponent<RectTransform>();
            labelR.anchorMin = Vector2.zero; labelR.anchorMax = Vector2.one;
            labelR.offsetMin = new Vector2(8, 0); labelR.offsetMax = new Vector2(-24, 0);
            var labelT = labelGo.AddComponent<Text>();
            labelT.font = _font; labelT.fontSize = FontS;
            labelT.color = C_Text; labelT.alignment = TextAnchor.MiddleLeft;
            labelT.raycastTarget = false;

            // 箭头
            var arrowGo = new GameObject("Arrow");
            arrowGo.transform.SetParent(ddGo.transform, false);
            var arrowR = arrowGo.AddComponent<RectTransform>();
            arrowR.anchorMin = new Vector2(1, 0); arrowR.anchorMax = new Vector2(1, 1);
            arrowR.offsetMin = new Vector2(-22, 0); arrowR.offsetMax = Vector2.zero;
            var arrowT = arrowGo.AddComponent<Text>();
            arrowT.text = "▼"; arrowT.font = _font; arrowT.fontSize = 10;
            arrowT.color = C_TextDim; arrowT.alignment = TextAnchor.MiddleCenter;
            arrowT.raycastTarget = false;

            // 下拉列表面板（初始隐藏）
            var listGo = new GameObject("DropdownList");
            listGo.transform.SetParent(ddGo.transform, false);

            // 让列表渲染在最上层
            var listCanvas = listGo.AddComponent<Canvas>();
            listCanvas.overrideSorting = true;
            listCanvas.sortingOrder = 300;
            listGo.AddComponent<GraphicRaycaster>();

            var listImg = listGo.AddComponent<Image>();
            listImg.color = c(45, 45, 52);

            var listR = listGo.GetComponent<RectTransform>();
            listR.pivot = new Vector2(0, 1);
            listR.anchorMin = new Vector2(0, 0); listR.anchorMax = new Vector2(1, 0);
            listR.offsetMin = Vector2.zero; listR.offsetMax = Vector2.zero;

            var listVl = listGo.AddComponent<VerticalLayoutGroup>();
            listVl.spacing = 1; listVl.padding = new RectOffset(2, 2, 2, 2);
            listVl.childForceExpandWidth = true; listVl.childForceExpandHeight = false;
            var listFit = listGo.AddComponent<ContentSizeFitter>();
            listFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            listGo.SetActive(false);

            // 填充选项
            var profiles = TerrainShaderRegistry.All;
            if (profiles.Count > 0)
                labelT.text = profiles[0].Name;

            foreach (var profile in profiles)
            {
                var p = profile;
                var itemGo = MakeBtn(listGo.transform, p.Name, -1, RowH, FontS, c(45, 45, 52));
                itemGo.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                itemGo.GetComponent<Button>().onClick.AddListener(() =>
                {
                    labelT.text = p.Name;
                    listGo.SetActive(false);
                    SwitchShaderProfile(p);
                });
            }

            // 点击按钮切换下拉列表显示
            var mainBtn = ddGo.AddComponent<Button>();
            mainBtn.targetGraphic = ddImg;
            var btnColors = mainBtn.colors;
            btnColors.normalColor = C_Input;
            btnColors.highlightedColor = c(35, 35, 40);
            btnColors.pressedColor = C_BtnPress;
            mainBtn.colors = btnColors;
            mainBtn.onClick.AddListener(() =>
            {
                listGo.SetActive(!listGo.activeSelf);
            });
        }

        private void SwitchShaderProfile(TerrainShaderProfile profile)
        {
            if (profile == null || _gridRenderer == null) return;
            _currentProfile = profile;
            _gridRenderer.ApplyShaderProfile(profile);
            RebuildShaderParamsUI(profile);
            RebuildGrid();
        }

        private void RebuildShaderParamsUI(TerrainShaderProfile profile)
        {
            if (_shaderParamsContainer == null) return;

            // 清除旧控件
            for (int i = _shaderParamsContainer.childCount - 1; i >= 0; i--)
                Destroy(_shaderParamsContainer.GetChild(i).gameObject);
            _dynPreviews.Clear();
            _dynNames.Clear();

            string lastGroup = null;

            foreach (var desc in profile.Params)
            {
                // 分组标题
                if (!string.IsNullOrEmpty(desc.Group) && desc.Group != lastGroup)
                {
                    lastGroup = desc.Group;
                    Sep(_shaderParamsContainer);
                    SectionLabel(_shaderParamsContainer, desc.Group);
                }

                switch (desc.Type)
                {
                    case ShaderParamType.Texture2D:
                        BuildDynTextureSlot(_shaderParamsContainer, desc);
                        break;

                    case ShaderParamType.Float:
                    case ShaderParamType.Range:
                        BuildDynSlider(_shaderParamsContainer, desc);
                        break;

                    case ShaderParamType.Color:
                        BuildDynColorPicker(_shaderParamsContainer, desc);
                        break;
                }
            }
        }

        // ── 动态贴图槽 ──
        private void BuildDynTextureSlot(RectTransform parent, ShaderParamDescriptor desc)
        {
            var row = Row(parent);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;

            // 缩略图预览
            var previewGo = new GameObject("Preview");
            previewGo.transform.SetParent(row, false);
            var previewImg = previewGo.AddComponent<Image>();
            previewImg.color = C_Input;
            previewImg.preserveAspect = true;
            var previewLe = previewGo.AddComponent<LayoutElement>();
            previewLe.preferredWidth = 44; previewLe.preferredHeight = 44;
            _dynPreviews[desc.PropertyName] = previewImg;

            // 右侧
            var infoGo = new GameObject("Info");
            infoGo.transform.SetParent(row, false);
            var infoR = infoGo.AddComponent<RectTransform>();
            var infoVl = infoGo.AddComponent<VerticalLayoutGroup>();
            infoVl.spacing = 1; infoVl.childForceExpandWidth = true;
            infoVl.childForceExpandHeight = false;
            infoGo.AddComponent<LayoutElement>().flexibleWidth = 1;

            var nameT = MakeLabel(infoR, desc.DisplayName + "  (无)", FontS, C_Text);
            _dynNames[desc.PropertyName] = nameT;

            var btnRow = Row(infoR);
            string prop = desc.PropertyName;
            var btnSelect = MakeBtn(btnRow, "选择", -1, BtnH - 6, FontS, C_Btn);
            btnSelect.GetComponent<Button>().onClick.AddListener(() =>
            {
                OpenTextureBrowserForProp(prop);
            });

            var btnClear = MakeBtn(btnRow, "×", 30, BtnH - 6, FontS, C_Btn);
            btnClear.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (_gridRenderer != null) _gridRenderer.SetShaderTexture(prop, null);
                if (_dynPreviews.ContainsKey(prop))
                {
                    _dynPreviews[prop].sprite = null;
                    _dynPreviews[prop].color = C_Input;
                }
                if (_dynNames.ContainsKey(prop))
                    _dynNames[prop].text = desc.DisplayName + "  (无)";
            });
        }

        // ── 动态滑条 ──
        private void BuildDynSlider(RectTransform parent, ShaderParamDescriptor desc)
        {
            string prop = desc.PropertyName;
            var slider = SliderRow(parent, desc.DisplayName, desc.Min, desc.Max, desc.Default);
            slider.onValueChanged.AddListener(v =>
            {
                if (_gridRenderer != null) _gridRenderer.SetShaderFloat(prop, v);
            });
            // 立即应用默认值
            if (_gridRenderer != null) _gridRenderer.SetShaderFloat(prop, desc.Default);
        }

        // ── 动态颜色选择器（简易 RGB 滑条）──
        private void BuildDynColorPicker(RectTransform parent, ShaderParamDescriptor desc)
        {
            string prop = desc.PropertyName;
            SectionLabel(parent, "  " + desc.DisplayName);

            float r = 1f, g = 1f, b = 1f;

            var sr = SliderRow(parent, "  R", 0f, 1f, 1f);
            var sg = SliderRow(parent, "  G", 0f, 1f, 1f);
            var sb = SliderRow(parent, "  B", 0f, 1f, 1f);

            System.Action apply = () =>
            {
                if (_gridRenderer != null)
                    _gridRenderer.SetShaderColor(prop, new Color(r, g, b, 1f));
            };

            sr.onValueChanged.AddListener(v => { r = v; apply(); });
            sg.onValueChanged.AddListener(v => { g = v; apply(); });
            sb.onValueChanged.AddListener(v => { b = v; apply(); });
        }

        // ── 贴图浏览器（按属性名打开）──
        private void OpenTextureBrowserForProp(string propertyName)
        {
            _texBrowserTargetProp = propertyName;
            _texBrowserDlg.SetActive(true);
            RefreshTextureBrowser();
        }


        // ---- 贴图浏览器弹窗 ----

        private void BuildTextureBrowserDialog(RectTransform root)
        {
            _texBrowserDlg = new GameObject("TexBrowserDialog");
            _texBrowserDlg.transform.SetParent(root, false);
            Stretch(_texBrowserDlg.AddComponent<RectTransform>());
            _texBrowserDlg.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);

            var box = new GameObject("Box");
            box.transform.SetParent(_texBrowserDlg.transform, false);
            var boxR = box.AddComponent<RectTransform>();
            boxR.anchorMin = boxR.anchorMax = new Vector2(0.5f, 0.5f);
            boxR.sizeDelta = new Vector2(620, 480);
            box.AddComponent<Image>().color = C_Panel;

            var vl = box.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.spacing = 6; vl.padding = new RectOffset(14, 14, 12, 12);
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;

            MakeLabel(boxR, "选择贴图", FontL, C_Text).alignment = TextAnchor.MiddleCenter;
            var hintT = MakeLabel(boxR, "将贴图放入 Resources/TerrainTextures 文件夹", FontS, C_TextDim);
            hintT.alignment = TextAnchor.MiddleCenter;
            Sep(boxR);

            // 滚动区域 — 缩略图网格
            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(box.transform, false);
            var scrollLe = scrollGo.AddComponent<LayoutElement>();
            scrollLe.flexibleHeight = 1; scrollLe.flexibleWidth = 1;
            scrollLe.preferredHeight = 340;

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var vpGo = new GameObject("Viewport", typeof(RectTransform));
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpR = vpGo.GetComponent<RectTransform>();
            vpR.anchorMin = Vector2.zero; vpR.anchorMax = Vector2.one;
            vpR.offsetMin = vpR.offsetMax = Vector2.zero;
            vpGo.AddComponent<Image>().color = C_Input;
            vpGo.AddComponent<RectMask2D>();
            scroll.viewport = vpR;

            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vpGo.transform, false);
            _texGridContent = cGo.GetComponent<RectTransform>();
            _texGridContent.anchorMin = new Vector2(0, 1);
            _texGridContent.anchorMax = new Vector2(1, 1);
            _texGridContent.pivot = new Vector2(0.5f, 1);
            _texGridContent.anchoredPosition = Vector2.zero;
            _texGridContent.sizeDelta = new Vector2(0, 0);

            var grid = cGo.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(100, 120);
            grid.spacing = new Vector2(8, 8);
            grid.padding = new RectOffset(8, 8, 8, 8);
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.Flexible;
            cGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _texGridContent;

            // 底部按钮
            var bRow = Row(boxR);
            var btnCancel = MakeBtn(bRow, "取消", -1, BtnH, FontM, C_Btn);
            btnCancel.GetComponent<Button>().onClick.AddListener(() =>
            {
                _texBrowserDlg.SetActive(false);
            });

            _texBrowserDlg.SetActive(false);
        }


        private void RefreshTextureBrowser()
        {
            if (_texGridContent == null) return;

            // 清除旧项
            for (int i = _texGridContent.childCount - 1; i >= 0; i--)
                Destroy(_texGridContent.GetChild(i).gameObject);

            // 从 Resources/TerrainTextures 加载所有 Sprite
            var sprites = Resources.LoadAll<Sprite>("TerrainTextures");
            // 同时也加载 Texture2D（用户可能没有设置为 Sprite 模式）
            var textures = Resources.LoadAll<Texture2D>("TerrainTextures");

            // 合并：优先用 Sprite，没有 Sprite 的 Texture2D 也显示
            var shown = new System.Collections.Generic.HashSet<string>();

            // 先显示 Sprite 资源
            foreach (var spr in sprites)
            {
                if (shown.Contains(spr.texture.name)) continue;
                shown.Add(spr.texture.name);
                CreateThumbnailFromSprite(spr);
            }

            // 再显示没有对应 Sprite 的 Texture2D
            foreach (var tex in textures)
            {
                if (shown.Contains(tex.name)) continue;
                shown.Add(tex.name);
                CreateThumbnailFromTexture(tex);
            }

            if (shown.Count == 0)
            {
                var t = MakeLabel(_texGridContent,
                    "未找到贴图资源\n\n请将图片放入:\nResources/TerrainTextures/\n\n" +
                    "图片 Texture Type 设为\nSprite (2D and UI) 或 Default",
                    FontS, C_TextDim);
                t.alignment = TextAnchor.MiddleCenter;
            }
        }

        private void CreateThumbnailFromSprite(Sprite spr)
        {
            string name = spr.texture.name;

            var itemGo = new GameObject(name);
            itemGo.transform.SetParent(_texGridContent, false);
            itemGo.AddComponent<RectTransform>();
            var itemVl = itemGo.AddComponent<VerticalLayoutGroup>();
            itemVl.childAlignment = TextAnchor.UpperCenter;
            itemVl.spacing = 2;
            itemVl.childForceExpandWidth = true;
            itemVl.childForceExpandHeight = false;

            // 缩略图
            var thumbGo = new GameObject("Thumb");
            thumbGo.transform.SetParent(itemGo.transform, false);
            var thumbImg = thumbGo.AddComponent<Image>();
            thumbImg.sprite = spr;
            thumbImg.color = Color.white;
            thumbImg.preserveAspect = true;
            var thumbLe = thumbGo.AddComponent<LayoutElement>();
            thumbLe.preferredWidth = 90; thumbLe.preferredHeight = 90;

            var btn = thumbGo.AddComponent<Button>();
            btn.targetGraphic = thumbImg;
            var bc = btn.colors;
            bc.highlightedColor = new Color(0.8f, 0.9f, 1f);
            bc.pressedColor = C_BtnPress;
            btn.colors = bc;

            // 文件名
            var nameT = MakeLabel(itemGo.GetComponent<RectTransform>(), name, 11, C_TextDim);
            nameT.alignment = TextAnchor.UpperCenter;
            nameT.GetComponent<LayoutElement>().preferredHeight = 22;

            // 点击 → 应用到层
            var texture = spr.texture;
            string texName = spr.texture.name;
            btn.onClick.AddListener(() =>
            {
                ApplyTextureByProp(_texBrowserTargetProp, texture, spr, texName);
                _texBrowserDlg.SetActive(false);
            });
        }

        private void CreateThumbnailFromTexture(Texture2D tex)
        {
            string name = tex.name;

            // 从 Texture2D 创建临时 Sprite 用于显示
            var spr = Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f));

            var itemGo = new GameObject(name);
            itemGo.transform.SetParent(_texGridContent, false);
            itemGo.AddComponent<RectTransform>();
            var itemVl = itemGo.AddComponent<VerticalLayoutGroup>();
            itemVl.childAlignment = TextAnchor.UpperCenter;
            itemVl.spacing = 2;
            itemVl.childForceExpandWidth = true;
            itemVl.childForceExpandHeight = false;

            // 缩略图
            var thumbGo = new GameObject("Thumb");
            thumbGo.transform.SetParent(itemGo.transform, false);
            var thumbImg = thumbGo.AddComponent<Image>();
            thumbImg.sprite = spr;
            thumbImg.color = Color.white;
            thumbImg.preserveAspect = true;
            var thumbLe = thumbGo.AddComponent<LayoutElement>();
            thumbLe.preferredWidth = 90; thumbLe.preferredHeight = 90;

            var btn = thumbGo.AddComponent<Button>();
            btn.targetGraphic = thumbImg;
            var bc = btn.colors;
            bc.highlightedColor = new Color(0.8f, 0.9f, 1f);
            bc.pressedColor = C_BtnPress;
            btn.colors = bc;

            // 文件名
            var nameT = MakeLabel(itemGo.GetComponent<RectTransform>(), name, 11, C_TextDim);
            nameT.alignment = TextAnchor.UpperCenter;
            nameT.GetComponent<LayoutElement>().preferredHeight = 22;

            // 点击 → 应用到层
            string texName2 = tex.name;
            btn.onClick.AddListener(() =>
            {
                ApplyTextureByProp(_texBrowserTargetProp, tex, spr, texName2);
                _texBrowserDlg.SetActive(false);
            });
        }

        private void ApplyTextureByProp(string propName, Texture2D tex, Sprite spr, string displayName)
        {
            if (_gridRenderer != null)
                _gridRenderer.SetShaderTexture(propName, tex);

            // 更新动态面板缩略图预览
            if (_dynPreviews.ContainsKey(propName))
            {
                _dynPreviews[propName].sprite = spr;
                _dynPreviews[propName].color = Color.white;
            }

            // 更新文件名标签
            if (_dynNames.ContainsKey(propName))
            {
                string label = _dynNames[propName].text.Split(new[] { "  " },
                    StringSplitOptions.None)[0];
                _dynNames[propName].text = label + "  " + displayName;
            }
        }
    }
}
