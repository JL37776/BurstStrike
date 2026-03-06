using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using Game.Scripts.Fixed;
using Game.Map;
using Game.Pathing.Debug;
using Game.Unit;
using Game.World.Logic;

namespace Game.World
{
    /// <summary>
    /// Unity-facing World bridge (MonoBehaviour). Unity 主线程的 World 桥接层（MonoBehaviour）。
    /// </summary>
    /// <remarks>
    /// Threading model: World runs on Unity main thread; LogicWorld runs on a background logic thread. 线程模型：World 在 Unity 主线程；LogicWorld 在后台逻辑线程。
    /// Data crossing threads must be marshalled via queues/snapshots. 跨线程数据必须通过队列/快照封送。
    /// </remarks>
    public sealed class World : MonoBehaviour
    {
        public enum DebugPathMode
        {
            AStar = 0,
            FlowField = 1,
        }

        [Header("Logic")] [Tooltip("Logic ticks per second. 逻辑 Tick 频率（每秒 tick 次数）。")]
        public int tickRate = 30;

        // Tick-rate configuration is now loaded from YAML (Assets/Game/World/world_config.yaml).
        // Keep this field for compatibility with existing scenes, but it will be overridden when YAML loads.
        [NonSerialized] private WorldConfigData _worldConfig;

        [Header("Debug")] [Tooltip("If true, log snapshot data periodically (diagnostics). 是否周期性打印快照信息（用于诊断单位状态）。")]
        public bool logSnapshots = true;

        [Tooltip("Snapshot log interval in seconds. 快照日志间隔（秒）。")]
        public float snapshotLogIntervalSeconds = 3f;

        [Tooltip("How many units to print in snapshot log (head). 日志中最多打印多少个单位（取头部）。")]
        public int snapshotPrintHeadCount = 5;

        [Tooltip(
            "If units exceed head count, also print the last unit (tail) for diagnostics. 如果单位数超过 head count，也打印最后一个单位（帮助定位尾部异常）。")]
        public bool snapshotPrintTail = true;

        [Tooltip(
            "If true, log RenderSnapshot unit id stats (helps diagnose flicker/id reuse issues). 是否周期性打印 RenderSnapshot 的单位 id 统计（帮助定位闪烁/id 复用问题）。")]
        public bool logRenderIds = false;

        [Tooltip("Render id log interval in seconds. Render id 统计日志间隔（秒）。")]
        public float renderIdLogIntervalSeconds = 2f;

        private float _nextRenderIdLogTime;

        [Header("Rendering")]
        [Tooltip("Enable RenderUnit proxies driven by logic snapshots. 启用渲染代理（RenderUnit），由逻辑快照驱动。")]
        public bool enableRendering = true;

        [Tooltip("Cube size for RenderUnit placeholders. RenderUnit 占位方块尺寸。")]
        public float renderUnitSize = 0.6f;

        [Tooltip("Optional parent transform for all RenderUnits. 所有 RenderUnit 的父节点（可选）。")]
        public Transform renderRoot;

        [Header("Rendering Smooth")]
        [Tooltip(
            "Interpolate between RenderSnapshots for smoother rendering instead of snapping. 是否对 RenderSnapshot 插值以获得更平滑渲染（避免直接跳变）。")]
        public bool enableInterpolation = true;

        [Tooltip(
            "How many logic ticks to stay behind latest snapshot when interpolating (typically 1-3). 插值时落后最新快照的 tick 数（通常 1-3）。")]
        public int interpolationBackTicks = 2;

        [Tooltip("Max ticks to extrapolate when we don't have a newer snapshot yet. 最大预测插值的 tick 数（当还没有更新快照时使用）。")]
        public int maxExtrapolationTicks = 1;

        [Tooltip(
            "If distance between two snapshots exceeds this, snap (treat as teleport) instead of interpolating. 如果两次快照间距超过此值，直接跳跃到目标位置，而不是插值过渡。")]
        public float teleportSnapDistance = 3f;

        [Tooltip("Render snapshot ring buffer size. 渲染快照环形缓冲区大小。")]
        public int renderSnapshotBufferSize = 64;

        [Tooltip(
            "How many seconds to stay behind latest snapshot when interpolating. ~0.05-0.15 works well. 插值时落后最新快照的时间（秒）。通常 0.05-0.15 之间。")]
        public float interpolationBackTimeSeconds = 0.1f;

        [Tooltip("Max seconds to extrapolate when we don't have a newer snapshot yet. 最大预测插值的时间（秒）。当还没有更新快照时使用。")]
        public float maxExtrapolationTimeSeconds = 0.05f;

        [Header("Rendering Smooth (Rotation)")]
        [Tooltip(
            "If true, smooth render rotation using a capped turn speed (rounder turns on grid paths). 启用渲染端转向平滑（让网格折线移动更圆润）。")]
        public bool enableRenderTurnSmoothing = true;

        [Tooltip("Max render turn speed in degrees per second when smoothing is enabled. 渲染端最大转向速度（度/秒）。")]
        public float renderTurnSpeedDegPerSec = 360f;

        // Per-unit render state for turn smoothing.
        private readonly Dictionary<int, Quaternion> _renderRotById = new Dictionary<int, Quaternion>(256);
        private readonly Dictionary<int, Vector3> _renderLastPosById = new Dictionary<int, Vector3>(256);

        [Header("Render Proxy GC")]
        [Tooltip(
            "If true, automatically destroy render proxies for units that disappear from RenderSnapshot. 是否自动销毁不再快照中的渲染代理（RenderUnit）。")]
        public bool autoRemoveRenderProxies = false;

        [Tooltip(
            "Seconds a unit can be missing from snapshots before its proxy is destroyed. Helps against jitter/temporary drop. 单位在快照中消失后，渲染代理被销毁前的宽限时间（秒）。")]
        public float renderProxyDespawnGraceSeconds = 0.5f;

        [Tooltip("How often (seconds) to run proxy cleanup. 代理清理的频率（秒）。")]
        public float renderProxyCleanupIntervalSeconds = 0.25f;

        [Header("Debug Map Render")]
        [Tooltip(
            "If true, render a debug Tank map (example NxN) before starting LogicWorld. 如果为真，渲染调试用的 Tank 地图（示例 NxN）。")]
        public bool renderDebugTankMap = true;

        [Tooltip("Example map size in cells (NxN). 示例地图的大小（单元格数 NxN）。")]
        public int debugTankMapSize = 5;

        [Tooltip("Cell size in Unity units for debug tank map. 调试坦克地图的单元格大小（Unity 单位）。")]
        public float debugTankMapCellSize = 0.5f;

        [Tooltip("Cube height in Unity units for debug tank map. 调试坦克地图的单元格高度（Unity 单位）。")]
        public float debugTankMapCellHeight = 0.5f;

        [Tooltip("Obstacle probability for example map Tanks layer. 示例地图 Tanks 图层的障碍物概率。")] [Range(0f, 1f)]
        public float debugTankObstacleProbability = 0.10f;

        [Tooltip("Seed for example map. Same seed => same layout. 示例地图的种子。相同种子 => 相同布局。")]
        public int debugTankMapSeed = 12345;

        [Tooltip(
            "Parent transform for generated debug tank map cubes. If null, uses this World transform. 生成的调试坦克地图立方体的父节点。如果为空，使用 World 的 Transform。")]
        public Transform debugTankMapRoot;

        [Tooltip(
            "If true, log which debug map parameters were used to render/build the example map. 如果为真，记录用于渲染/构建示例地图的调试地图参数。")]
        public bool logDebugTankMapParams = false;

        [Header("Debug Walkability")]
        [Tooltip(
            "If true, draw a main-thread overlay of non-walkable cells for Tanks layer (red cubes). 如果为真，主线程覆盖显示 Tanks 图层的不可行走单元格（红色立方体）。")]
        public bool debugRenderTanksBlocked = false;

        [Header("Debug Pathing")]
        [Tooltip(
            "If enabled, pathfinding mode is forced by Debug Path Mode below. If disabled, PathService auto-selects based on unit count threshold.")]
        public bool debugForcePathMode = false;

        [Tooltip("Which pathfinding mode to force when Debug Force Path Mode is enabled.")]
        public DebugPathMode debugPathMode = DebugPathMode.AStar;

        [Tooltip(
            "If Debug Force Path Mode is disabled, commands with unit count <= this threshold use A*; otherwise use FlowField.")]
        [Min(1)]
        public int pathingAStarUnitCountThreshold = 10;

        [Header("Enemy Search (Partition Index)")]
        [Tooltip("EnemySearchService partition cell size in MAP CELLS (not world units). Min=5, default=5. Smaller => more partitions, tighter searches, slightly higher update overhead.")]
        [Min(5)]
        public int enemySearchPartitionCellSize = 5;

        [Header("Debug Enemy Search")]
        [Tooltip("If true, sync partition info into RenderUnit inspector fields and optionally log the updates.")]
        public bool debugSyncRenderUnitPartition = true;

        [Tooltip("If true, log partition sync for the first few units each frame (noisy).")]
        public bool debugLogRenderUnitPartition = false;

        [Header("Debug RenderUnit (Inspector Sync)")]
        [Tooltip("Sync top activity name into RenderUnit inspector each frame (allocations).")]
        public bool debugSyncTopActivity = false;

        [Tooltip("Sync full activity stack into RenderUnit inspector each frame (allocations).")]
        public bool debugSyncActivities = false;

        [Tooltip("Sync ability list into RenderUnit inspector each frame (allocations).")]
        public bool debugSyncAbilities = false;

        private GameObject _tanksBlockedRoot;

        private readonly ConcurrentQueue<ILogicInput> _toLogic = new ConcurrentQueue<ILogicInput>();
        private readonly ConcurrentQueue<ILogicOutput> _fromLogic = new ConcurrentQueue<ILogicOutput>();

        // Path debug (queued from logic thread, consumed on main thread)
        private readonly ConcurrentQueue<PathDebugPayload> _pathDebugQueue = new ConcurrentQueue<PathDebugPayload>();
        private PathDebugCubes _pathDebugCubes;

        private readonly struct PathDebugPayload
        {
            public readonly int[] X;
            public readonly int[] Y;

            public PathDebugPayload(int[] x, int[] y)
            {
                X = x;
                Y = y;
            }
        }

        private Thread _logicThread;
        private CancellationTokenSource _cts;
        private LogicWorld _logicWorld;

        private LogicSnapshot? _latestSnapshot;
        private RenderSnapshot? _latestRenderSnapshot;
        private float _nextSnapshotLogTime;

        private readonly Dictionary<int, RenderUnit> _renderUnitsById = new Dictionary<int, RenderUnit>(256);
        private RenderSnapshotBuffer _renderBuffer;

        private float _nextProxyCleanupTime;
        private readonly HashSet<int> _seenUnitIdsThisFrame = new HashSet<int>();
        private readonly Dictionary<int, float> _lastSeenTimeByUnitId = new Dictionary<int, float>(256);

        private WorldRef _selfRef;

        /// <summary>
        /// Exposes the logic world instance (read-only). 对外暴露逻辑世界实例（只读）。
        /// </summary>
        public LogicWorld Logic => _logicWorld;

        /// <summary>
        /// Minimal thread-safe wrapper that can be nulled on destroy so background thread code can avoid referencing a destroyed MonoBehaviour.
        /// 最小线程安全包装：OnDestroy 时可置空，避免后台线程引用已销毁的 MonoBehaviour。
        /// </summary>
        private sealed class WorldRef
        {
            public volatile World Value;

            public WorldRef(World w)
            {
                Value = w;
            }
        }

        // Resolved startup settings snapshot (defaults + optional overrides).
        [NonSerialized] private WorldSettings _resolvedSettings;
        private bool _started;

        private void Awake()
        {
            _selfRef = new WorldRef(this);
            _renderBuffer = new RenderSnapshotBuffer(Mathf.Max(8, renderSnapshotBufferSize));

            // Resolve startup settings (defaults from World inspector + optional overrides).
            _resolvedSettings = WorldSettings.FromWorld(this);
            ApplySettingsProviders(ref _resolvedSettings);
            _resolvedSettings.ApplyTo(this);

            // Cache path debug visualizer on main thread if present in scene.
            _pathDebugCubes = GetComponentInChildren<PathDebugCubes>();
            if (_pathDebugCubes == null)
                _pathDebugCubes = UnityEngine.Object.FindFirstObjectByType<PathDebugCubes>();

            // Allow logic thread to enqueue debug paths without holding a World instance.
            WorldDebugPath.Queue = _pathDebugQueue;

            // Expose debug settings + path debug queue to extracted command adapter.
            WorldDebugAccess.SetWorld(this);

            // Default debug flags (can be changed live in inspector).
            WorldDebugAccess.SetRenderUnitDebugFlags(new WorldDebugAccess.RenderUnitDebugFlags(
                debugSyncTopActivity,
                debugSyncActivities,
                debugSyncAbilities));
            // WorldDebugAccess.SetPathDebugQueue(_pathDebugQueue);

            // Expose this World instance to logic-thread command execution (debug settings only).
            WorldRefHolder.Ref = _selfRef;
        }

        // Holds a reference to the current World so logic thread can read debug settings.
        private static class WorldRefHolder
        {
            public static WorldRef Ref;
        }

        private void Start()
        {
            // Two-phase init: heavy / side-effectful work happens in Start so settings providers had a chance
            // to override values without relying on script execution order.
            if (_started) return;
            _started = true;

            if (renderDebugTankMap)
                RenderDebugTankMap();

            StartLogicWorld();

            if (debugRenderTanksBlocked)
                RenderTanksBlockedOverlay();
        }

        private void ApplySettingsProviders(ref WorldSettings settings)
        {
            // Scan same GameObject for any optional providers (e.g., WorldTestSpawner) and apply overrides
            // deterministically by Priority.
            var monos = GetComponents<MonoBehaviour>();
            if (monos == null || monos.Length == 0) return;

            var providers = new List<IWorldSettingsProvider>(4);
            for (int i = 0; i < monos.Length; i++)
            {
                var m = monos[i];
                if (m == null || !m.isActiveAndEnabled) continue;
                if (m is IWorldSettingsProvider p) providers.Add(p);
            }

            if (providers.Count == 0) return;

            providers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            for (int i = 0; i < providers.Count; i++)
                providers[i].Mutate(ref settings);
        }

        private void RenderTanksBlockedOverlay()
        {
            if (_logicWorld == null || _logicWorld.Map == null)
            {
                Debug.LogWarning("[Debug_TanksBlocked] LogicWorld or Map not ready; cannot render blocked overlay.");
                return;
            }

            if (_tanksBlockedRoot != null) Destroy(_tanksBlockedRoot);
            _tanksBlockedRoot = new GameObject("Debug_TanksBlocked");
            _tanksBlockedRoot.transform.SetParent(transform, false);

            var map = _logicWorld.Map;
            var grid = map.Grid;

            var mat = new Material(Shader.Find("Standard")) { color = Color.red };

            int blockedCount = 0;
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var cell = new Game.Grid.GridPosition(x, y);
                    // blocked for tanks if NOT walkable for Tanks
                    if (map.IsWalkable(cell, Game.Map.MapLayer.Tanks))
                        continue;

                    blockedCount++;
                    var c2 = grid.GetCellCenterWorld(cell);
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = $"Blocked_{x}_{y}";
                    go.transform.SetParent(_tanksBlockedRoot.transform, worldPositionStays: true);

                    // put it above the map: cellHeight/2 (cube center) + 0.5
                    float yPos = (debugTankMapCellHeight * 0.5f) + 0.5f;
                    go.transform.position = new Vector3(c2.x.ToFloat(), yPos, c2.y.ToFloat());
                    go.transform.localScale = Vector3.one * 0.35f;

                    var r = go.GetComponent<Renderer>();
                    if (r != null) r.sharedMaterial = mat;
                    var col = go.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                }
            }

            Debug.Log($"[Debug_TanksBlocked] map={map.Width}x{map.Height} blocked(Tanks)={blockedCount}");

            // If there are no blocked cells (or something's wrong), spawn a single visible marker so we know this ran.
            if (blockedCount == 0)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "BlockedOverlay_Marker";
                marker.transform.SetParent(_tanksBlockedRoot.transform, worldPositionStays: false);
                marker.transform.localPosition = new Vector3(0f, 2f, 0f);
                marker.transform.localScale = Vector3.one * 0.6f;
                var mr = marker.GetComponent<Renderer>();
                if (mr != null)
                {
                    var m = new Material(Shader.Find("Standard")) { color = Color.yellow };
                    mr.sharedMaterial = m;
                }

                var mc = marker.GetComponent<Collider>();
                if (mc != null) Destroy(mc);
            }
        }

        private void RenderDebugTankMap()
        {
            // If a renderer already exists, ensure it matches current World settings.
            var existing = GetComponentInChildren<Game.Map.TankMapRenderer>();
            var size = Mathf.Max(1, debugTankMapSize);

            // Build map data from World settings.
            var map = Game.Map.MapLoader.CreateExampleMap(size, size, debugTankObstacleProbability, debugTankMapSeed);

            // If we already have a renderer, just (re)configure and render.
            if (existing != null)
            {
                existing.Configure(debugTankMapCellSize, debugTankMapCellHeight, Vector3.zero);
                existing.Render(map);
                return;
            }

            if (logDebugTankMapParams)
            {
                int blocked = 0;
                for (int y = 0; y < map.height; y++)
                for (int x = 0; x < map.width; x++)
                    if ((map.Layers[y, x] & Game.Map.MapLayer.Tanks) != 0)
                        blocked++;

                Debug.Log(
                    $"[World] RenderDebugTankMap size={size} seed={debugTankMapSeed} p={debugTankObstacleProbability:F3} blocked={blocked}");
            }

            var root = debugTankMapRoot != null ? debugTankMapRoot : transform;
            var go = new GameObject("TankMapRenderer");
            go.transform.SetParent(root, false);

            var r = go.AddComponent<Game.Map.TankMapRenderer>();

            // IMPORTANT: TankMapRenderer has its own Start() that can auto-render an inspector-defined example map.
            // World owns debug rendering, so disable that behavior to avoid mismatched sizes.
            r.enabled = false;

            r.Configure(debugTankMapCellSize, debugTankMapCellHeight, Vector3.zero);
            r.Render(map);
        }

        private Game.Map.IMap BuildLogicMap()
        {
            // For now we reuse the same example map that the debug renderer uses.
            // This ensures the logic occupancy/index sees the same obstacles.
            var size = Mathf.Max(1, debugTankMapSize);
            var data = MapLoader.CreateExampleMap(size, size, debugTankObstacleProbability, debugTankMapSeed);

            if (logDebugTankMapParams)
            {
                int blocked = 0;
                for (int y = 0; y < data.height; y++)
                for (int x = 0; x < data.width; x++)
                    if ((data.Layers[y, x] & Game.Map.MapLayer.Tanks) != 0)
                        blocked++;

                Debug.Log(
                    $"[World] BuildLogicMap size={size} seed={debugTankMapSeed} p={debugTankObstacleProbability:F3} blocked={blocked}");
            }

            // Map.cs uses FixedVector2 for origin/cell size.
            // World origin is (0,0). Cell size is square.
            var origin = FixedVector2.Zero;
            var cellSize = new FixedVector2(Fixed.FromFloat(debugTankMapCellSize),
                Fixed.FromFloat(debugTankMapCellSize));

            return MapWithHeight.FromMapData(data, origin, cellSize);
        }

        private void OnDestroy()
        {
            // prevent logic thread from calling into a destroyed MonoBehaviour
            if (_selfRef != null) _selfRef.Value = null;

            // Clear debug accessors
            WorldDebugAccess.SetWorld(null);
            // WorldDebugAccess.SetPathDebugQueue(null);
            StopLogicWorld();

            // Clear static queue if this instance owns it.
            if (WorldDebugPath.Queue == _pathDebugQueue) WorldDebugPath.Queue = null;
        }

        // Add a small static helper inside World to allow logic thread to enqueue debug paths without a World instance.
        private static class WorldDebugPath
        {
            public static ConcurrentQueue<PathDebugPayload> Queue;

            public static void Enqueue(IReadOnlyList<Game.Grid.GridPosition> cells)
            {
                var q = Queue;
                if (q == null || cells == null || cells.Count == 0) return;
                var xs = new int[cells.Count];
                var ys = new int[cells.Count];
                for (int i = 0; i < cells.Count; i++)
                {
                    xs[i] = cells[i].X;
                    ys[i] = cells[i].Y;
                }

                q.Enqueue(new PathDebugPayload(xs, ys));
            }
        }

        public void StartLogicWorld()
        {
            if (_logicThread != null) return;

            _cts = new CancellationTokenSource();

            // Load world config YAML (main thread) to control tick-rate throttling.
            // We avoid inspector-driven values for determinism & snapshot reproducibility.
            var cfgPath = Path.Combine(Application.dataPath, "Game/World/world_config.yaml");
            _worldConfig = WorldConfigLoader.LoadOrDefault(cfgPath, out var cfgErrors);
            if (cfgErrors != null && cfgErrors.Count > 0)
            {
                for (int i = 0; i < cfgErrors.Count; i++)
                    Debug.LogWarning("[WorldConfig] " + cfgErrors[i]);
            }

            // Build logic map (layers + heights) and pass it into LogicWorld.
            // Note: LogicWorld runs off the main thread, so we only pass pure data.
            var logicMap = BuildLogicMap();

            // Archetypes (deterministic data): preload ALL YAML on main thread, then inject into logic.
            // This is behind an interface so we can swap to network source later.
            var archetypeSource = new DirectoryYamlArchetypeSource(
                rootDir: "Assets/Game/Serialization/Samples",
                recursive: true);
            var dict = archetypeSource.LoadAll(out var errors);
            Debug.Log($"[ArchetypeLoad] loaded={dict?.Count ?? 0} from '{"Assets/Game/Serialization/Samples"}'");
             if (errors != null && errors.Count > 0)
             {
                 // Dev-time behavior: log and continue with whatever loaded.
                 // If you want to hard-fail instead, we can add a bool flag.
                 for (int i = 0; i < errors.Count; i++)
                     Debug.LogError("[ArchetypeLoad] " + errors[i]);
             }

            var archetypes = new ArchetypeRegistry(dict);

            var abilityRates = _worldConfig?.TickRates?.Ability;
            var injectedAbilityRates = new LogicWorld.AbilityTickRates(
                movement: abilityRates != null ? abilityRates.Movement : 0,
                guard: abilityRates != null ? abilityRates.Guard : 0,
                weapon: abilityRates != null ? abilityRates.Weapon : 0,
                navigation: abilityRates != null ? abilityRates.Navigation : 0);

            var activityRates = _worldConfig?.TickRates?.Activity;
            var injectedActivityRates = new LogicWorld.ActivityTickRates(
                guardActivity: activityRates != null ? activityRates.GuardActivity : 0,
                chaseTarget: activityRates != null ? activityRates.ChaseTarget : 0,
                navigate: activityRates != null ? activityRates.Navigate : 0,
                move: activityRates != null ? activityRates.Move : 0,
                idle: activityRates != null ? activityRates.Idle : 0);

            _logicWorld = new LogicWorld(tickRate, _toLogic, _fromLogic, logicMap, enemySearchPartitionCellSize, archetypes, injectedAbilityRates, injectedActivityRates);

            _logicThread = new Thread(() =>
            {
                try
                {
                    _logicWorld.Run(_cts.Token);
                }
                catch (Exception e)
                {
                    _fromLogic.Enqueue(new LogicError(e));
                }
            })
            {
                IsBackground = true,
                Name = "LogicWorld"
            };

            _logicThread.Start();
        }

        public void StopLogicWorld()
        {
            if (_logicThread == null) return;

            try
            {
                _cts.Cancel();
                if (!_logicThread.Join(2000))
                    _logicThread.Abort(); // last resort in editor; avoid in production
            }
            catch
            {
                // ignore
            }
            finally
            {
                _logicThread = null;
                _cts.Dispose();
                _cts = null;
                _logicWorld = null;
            }
        }

        private void Update()
        {
            // Keep debug flags in sync for the logic thread (cheap, no allocations).
            WorldDebugAccess.SetRenderUnitDebugFlags(new WorldDebugAccess.RenderUnitDebugFlags(
                debugSyncTopActivity,
                debugSyncActivities,
                debugSyncAbilities));

            // Apply queued path debug (main thread)
            if (_pathDebugCubes != null && _logicWorld != null && _logicWorld.Map != null)
            {
                // drain - keep only last payload this frame
                PathDebugPayload last = default;
                bool has = false;
                while (_pathDebugQueue.TryDequeue(out var p))
                {
                    last = p;
                    has = true;
                }

                if (has && last.X != null && last.Y != null && last.X.Length == last.Y.Length)
                {
                    var list = new List<Game.Grid.GridPosition>(last.X.Length);
                    for (int i = 0; i < last.X.Length; i++)
                        list.Add(new Game.Grid.GridPosition(last.X[i], last.Y[i]));
                    _pathDebugCubes.ShowPath(_logicWorld.Map, list);
                }
            }

            // Drain outputs on main thread
            while (_fromLogic.TryDequeue(out var msg))
            {
                HandleLogicOutput(msg);
            }

            if (enableRendering)
            {
                if (enableInterpolation)
                    ApplyInterpolatedRender();
                else if (_latestRenderSnapshot.HasValue)
                    ApplyRenderSnapshot(_latestRenderSnapshot.Value);

                // Disabled by default while stabilizing ids/render pipeline.
                if (autoRemoveRenderProxies)
                    CleanupRenderProxies();
            }

            // Debug: update RenderUnit inspector partition fields
            // Important: do this AFTER ApplyRenderSnapshot/ApplyInterpolatedRender so RenderUnits exist.
            if (debugSyncRenderUnitPartition && _latestRenderSnapshot.HasValue)
                DebugUpdateRenderUnitPartitions(_latestRenderSnapshot.Value);

            // Main-thread debug: print child count for first few units so we can verify whether
            // the render snapshot actually carries children info.
            if (debugSyncAbilities && _latestRenderSnapshot.HasValue)
            {
                var rs = _latestRenderSnapshot.Value;
                var units = rs.Units;
                if (units != null)
                {
                    var limit = Mathf.Min(3, units.Length);
                    for (int di = 0; di < limit; di++)
                    {
                        var u = units[di];
                        // Print both snapshot and current RenderUnit values to distinguish sync vs. apply issues.
                        int snapChild = u.ChildCount ?? -1;
                        string arch = u.RootArchetypeId ?? "<null>";
                        int renderChild = -1;
                        if (_renderUnitsById.TryGetValue(u.Id, out var ru) && ru != null)
                            renderChild = ru.ChildCount;

                        Debug.Log($"[ChildDebug] tick={rs.Tick} unitId={u.Id} archetype={arch} snapChildCount={snapChild} renderChildCount={renderChild}");
                    }
                }
            }

            if (logSnapshots && snapshotLogIntervalSeconds > 0f && Time.unscaledTime >= _nextSnapshotLogTime)
            {
                _nextSnapshotLogTime = Time.unscaledTime + snapshotLogIntervalSeconds;
                if (_latestSnapshot.HasValue)
                {
                    var s = _latestSnapshot.Value;
                    var units = s.Units;
                    if (units == null)
                    {
                        Debug.Log($"[Snap] t={s.Tick} u=0");
                        return;
                    }

                    var count = units.Length;

                    if (count == 0)
                    {
                        Debug.Log($"[Snap] t={s.Tick} u=0");
                        return;
                    }

                    // Print a compact sample list: first N + (optional) last one.
                    // Example: [Snap] t=64 u=30 | 0:hp100@(0,0,0) 1:hp100@(1,0,0) ... 29:hp100@(29,0,0)
                    var head = snapshotPrintHeadCount <= 0 ? 0 : snapshotPrintHeadCount;
                    var sb = new System.Text.StringBuilder(128);
                    sb.Append("[Snap] t=").Append(s.Tick).Append(" u=").Append(count).Append(" | ");

                    int shown = 0;
                    for (int i = 0; i < count && i < head; i++)
                    {
                        if (shown++ > 0) sb.Append(' ');
                        var u = units[i];
                        if (u.Name == null && !u.Hp.HasValue && !u.Position.HasValue)
                            sb.Append(i).Append(":-");
                        else
                            AppendUnitShort(sb, u);
                    }

                    if (snapshotPrintTail && count > head)
                    {
                        sb.Append(" ... ");
                        var u = units[count - 1];
                        if (u.Name == null && !u.Hp.HasValue && !u.Position.HasValue)
                            sb.Append(count - 1).Append(":-");
                        else
                            AppendUnitShort(sb, u);
                    }

                    Debug.Log(sb.ToString());
                }
            }

            if (logRenderIds && renderIdLogIntervalSeconds > 0f && Time.unscaledTime >= _nextRenderIdLogTime)
            {
                _nextRenderIdLogTime = Time.unscaledTime + renderIdLogIntervalSeconds;
                if (_latestRenderSnapshot.HasValue)
                {
                    var rs = _latestRenderSnapshot.Value;
                    var units = rs.Units;
                    if (units != null)
                    {
                        var set = new HashSet<int>();
                        int dup = 0;
                        for (int i = 0; i < units.Length; i++)
                        {
                            if (!set.Add(units[i].Id)) dup++;
                        }

                        Debug.Log($"[RenderIds] tick={rs.Tick} units={units.Length} uniq={set.Count} dup={dup}");
                    }
                }
            }
        }

        private void HandleLogicOutput(ILogicOutput msg)
        {
            switch (msg)
            {
                case RenderSnapshot rs:
                    _latestRenderSnapshot = rs;
                    _renderBuffer ??= new RenderSnapshotBuffer(Mathf.Max(8, renderSnapshotBufferSize));
                    // Record main-thread arrival time so we can interpolate smoothly.
                    _renderBuffer.Add(rs, Time.unscaledTime);
                    break;

                case LogicSnapshot snapshot:
                    _latestSnapshot = snapshot;
                    break;

                case LogicTicked ticked:
                    // hook point: drive view interpolation / debug UI
                    // keep reference to avoid "unused" warnings in some analyzers
                    _ = ticked.Tick;
                    break;

                case LogicError err:
                    Debug.LogException(err.Exception);
                    break;
            }
        }

        private void CleanupRenderProxies()
        {
            var now = Time.unscaledTime;
            if (renderProxyCleanupIntervalSeconds > 0f && now < _nextProxyCleanupTime)
                return;

            _nextProxyCleanupTime = now + Mathf.Max(0.01f, renderProxyCleanupIntervalSeconds);

            // 1) Remove entries whose RenderUnit has been destroyed (Unity fake-null)
            if (_renderUnitsById.Count == 0) return;

            var toRemove = (List<int>)null;
            foreach (var kv in _renderUnitsById)
            {
                if (kv.Value == null)
                {
                    toRemove ??= new List<int>();
                    toRemove.Add(kv.Key);
                }
            }

            if (toRemove != null)
            {
                for (int i = 0; i < toRemove.Count; i++)
                {
                    var id = toRemove[i];
                    _renderUnitsById.Remove(id);
                    _lastSeenTimeByUnitId.Remove(id);
                }
            }

            // 2) Despawn proxies missing for too long
            var grace = Mathf.Max(0f, renderProxyDespawnGraceSeconds);
            if (grace <= 0f) return;

            toRemove = null;
            foreach (var kv in _lastSeenTimeByUnitId)
            {
                var id = kv.Key;
                var lastSeen = kv.Value;

                // If it re-appeared this frame, skip
                if (_seenUnitIdsThisFrame.Contains(id))
                    continue;

                if (now - lastSeen >= grace)
                {
                    toRemove ??= new List<int>();
                    toRemove.Add(id);
                }
            }

            if (toRemove == null) return;

            for (int i = 0; i < toRemove.Count; i++)
            {
                var id = toRemove[i];
                if (_renderUnitsById.TryGetValue(id, out var ru) && ru != null)
                {
                    Destroy(ru.gameObject);
                }

                _renderUnitsById.Remove(id);
                _lastSeenTimeByUnitId.Remove(id);
            }
        }

        private void MarkUnitSeen(int unitId)
        {
            _seenUnitIdsThisFrame.Add(unitId);
            _lastSeenTimeByUnitId[unitId] = Time.unscaledTime;
        }

        private void ApplyInterpolatedRender()
        {
            if (_renderBuffer == null || _renderBuffer.Count < 2) return;
            if (!_renderBuffer.TryGetLatest(out var latest, out var latestTime)) return;

            // Choose a render time slightly behind latest to guarantee we have A/B.
            var backT = interpolationBackTimeSeconds;
            if (backT <= 0f)
            {
                // Fallback to tick-based if time-back is disabled.
                var back = interpolationBackTicks < 0 ? 0 : interpolationBackTicks;
                var targetTick = latest.Tick - back;
                if (_renderBuffer.TryGetBracketingTicks(targetTick, out var ta, out var tb))
                {
                    var dt = tb.Tick - ta.Tick;
                    var alpha = dt <= 0 ? 0f : Mathf.Clamp01((targetTick - ta.Tick) / (float)dt);
                    ApplyRenderSnapshotInterpolated(ta, tb, alpha);
                    return;
                }

                ApplyRenderSnapshot(latest);
                return;
            }

            var renderTime = Time.unscaledTime - backT;

            // Ideal: interpolate between two timed snapshots.
            if (_renderBuffer.TryGetBracketingTimes(renderTime, out var a, out var aT, out var b, out var bT))
            {
                var denom = bT - aT;
                var alpha = denom <= 0.0001f ? 0f : Mathf.Clamp01((renderTime - aT) / denom);
                ApplyRenderSnapshotInterpolated(a, b, alpha);
                return;
            }

            // If renderTime is newer than latest, allow a tiny extrapolation from the newest pair.
            var extraMax = Mathf.Max(0f, maxExtrapolationTimeSeconds);
            if (extraMax > 0f && renderTime > latestTime && renderTime - latestTime <= extraMax)
            {
                // Use bracketing on latestTime - epsilon to get the last pair available.
                var eps = 0.0001f;
                if (_renderBuffer.TryGetBracketingTimes(latestTime - eps, out var prev, out var prevT, out var last,
                        out var lastT))
                {
                    var denom = lastT - prevT;
                    if (denom > 0.0001f)
                    {
                        var k = Mathf.Clamp01((renderTime - lastT) / denom);
                        ApplyRenderSnapshotExtrapolated(prev, last, k);
                        return;
                    }
                }
            }

            // Fallback: snap to latest
            ApplyRenderSnapshot(latest);
        }

        private void ApplyRenderSnapshotInterpolated(in RenderSnapshot a, in RenderSnapshot b, float alpha)
        {
            _seenUnitIdsThisFrame.Clear();

            var unitsA = a.Units;
            var unitsB = b.Units;
            if (unitsA == null || unitsB == null) return;

            // Build a fast lookup from id -> pose for A
            var mapA = new Dictionary<int, (Vector3 pos, Quaternion rot)>(unitsA.Length);
            for (int i = 0; i < unitsA.Length; i++)
            {
                var ua = unitsA[i];
                mapA[ua.Id] = (ua.Position.ToUnity(), ua.Rotation.ToUnity());
            }

            var dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            var maxStep = Mathf.Max(0f, renderTurnSpeedDegPerSec) * dt;

            for (int i = 0; i < unitsB.Length; i++)
            {
                var ub = unitsB[i];
                MarkUnitSeen(ub.Id);

                if (!_renderUnitsById.TryGetValue(ub.Id, out var ru) || ru == null)
                {
                    ru = CreateRenderUnit(ub.Id);
                    _renderUnitsById[ub.Id] = ru;
                }

                var cB = new Color(ub.ColorRgb.x.ToFloat(), ub.ColorRgb.y.ToFloat(), ub.ColorRgb.z.ToFloat(), 1f);
                ru.ApplyOwnership(ub.OwnerUserId, ub.FactionId, cB);

                ru.ApplyHealth(ub.CurrentHp, ub.MaxHp);

                if (debugSyncTopActivity) ru.ApplyTopActivity(ub.TopActivity);
                if (debugSyncActivities) ru.ApplyActivityStack(ub.ActivityStack);
                if (debugSyncAbilities) ru.ApplyAbilities(ub.Abilities);
                if (debugSyncAbilities) ru.ApplyChildrenDebug(ub.ChildCount, ub.ChildAbilities);
                if (debugSyncAbilities) ru.ApplyRootArchetypeId(ub.RootArchetypeId);

                var pb = ub.Position.ToUnity();
                var rbLogic = ub.Rotation.ToUnity();
                if (!mapA.TryGetValue(ub.Id, out var aPose))
                {
                    ru.ApplyPosition(pb);
                    ru.ApplyRotation(rbLogic);
                    _renderRotById[ub.Id] = rbLogic;
                    _renderLastPosById[ub.Id] = pb;
                    continue;
                }

                var pa = aPose.pos;
                var ra = aPose.rot;

                Vector3 p = pb;
                if (teleportSnapDistance > 0f && (pb - pa).sqrMagnitude > teleportSnapDistance * teleportSnapDistance)
                {
                    ru.ApplyPosition(pb);
                    ru.ApplyRotation(rbLogic);
                    _renderRotById[ub.Id] = rbLogic;
                    _renderLastPosById[ub.Id] = pb;
                    continue;
                }
                else
                {
                    p = Vector3.LerpUnclamped(pa, pb, alpha);
                    ru.ApplyPosition(p);
                }

                // Rotation smoothing
                if (!enableRenderTurnSmoothing)
                {
                    var r = Quaternion.SlerpUnclamped(ra, rbLogic, alpha);
                    ru.ApplyRotation(r);
                    _renderRotById[ub.Id] = r;
                    _renderLastPosById[ub.Id] = p;
                    continue;
                }

                // Desired rotation from movement direction (fall back to logic rotation if almost stationary)
                _renderLastPosById.TryGetValue(ub.Id, out var lastP);
                var v = p - lastP;
                Quaternion desired;
                if (v.sqrMagnitude > 0.000001f)
                    desired = Quaternion.LookRotation(v.normalized, Vector3.up);
                else
                    desired = rbLogic;

                // Current render rotation state
                if (!_renderRotById.TryGetValue(ub.Id, out var curR))
                    curR = ru.transform.rotation;

                var newR = Quaternion.RotateTowards(curR, desired, maxStep);
                ru.ApplyRotation(newR);
                _renderRotById[ub.Id] = newR;
                _renderLastPosById[ub.Id] = p;
            }
        }

        private void ApplyRenderSnapshotExtrapolated(in RenderSnapshot prev, in RenderSnapshot last, float k)
        {
            _seenUnitIdsThisFrame.Clear();

            var aUnits = prev.Units;
            var bUnits = last.Units;
            if (aUnits == null || bUnits == null) return;

            var mapA2 = new Dictionary<int, Vector3>(aUnits.Length);
            for (int i = 0; i < aUnits.Length; i++)
            {
                var ua = aUnits[i];
                mapA2[ua.Id] = ua.Position.ToUnity();
            }

            for (int i = 0; i < bUnits.Length; i++)
            {
                var ub = bUnits[i];
                MarkUnitSeen(ub.Id);

                if (!_renderUnitsById.TryGetValue(ub.Id, out var ru) || ru == null)
                {
                    ru = CreateRenderUnit(ub.Id);
                    _renderUnitsById[ub.Id] = ru;
                }

                var cB = new Color(ub.ColorRgb.x.ToFloat(), ub.ColorRgb.y.ToFloat(), ub.ColorRgb.z.ToFloat(), 1f);
                ru.ApplyOwnership(ub.OwnerUserId, ub.FactionId, cB);

                ru.ApplyHealth(ub.CurrentHp, ub.MaxHp);

                if (debugSyncTopActivity) ru.ApplyTopActivity(ub.TopActivity);
                if (debugSyncActivities) ru.ApplyActivityStack(ub.ActivityStack);
                if (debugSyncAbilities) ru.ApplyAbilities(ub.Abilities);
                if (debugSyncAbilities) ru.ApplyChildrenDebug(ub.ChildCount, ub.ChildAbilities);
                if (debugSyncAbilities) ru.ApplyRootArchetypeId(ub.RootArchetypeId);

                var pb = ub.Position.ToUnity();
                var rb = ub.Rotation.ToUnity();
                if (!mapA2.TryGetValue(ub.Id, out var pa))
                {
                    ru.ApplyPosition(pb);
                    ru.ApplyRotation(rb);
                    continue;
                }

                var vel = (pb - pa);
                var p = pb + vel * k;
                ru.ApplyPosition(p);
                ru.ApplyRotation(rb);
            }
        }

        private void ApplyRenderSnapshot(in RenderSnapshot snapshot)
        {
            _seenUnitIdsThisFrame.Clear();

            var units = snapshot.Units;
            if (units == null || units.Length == 0) return;
            for (int i = 0; i < units.Length; i++)
            {
                var u = units[i];
                MarkUnitSeen(u.Id);

                if (!_renderUnitsById.TryGetValue(u.Id, out var ru) || ru == null)
                {
                    ru = CreateRenderUnit(u.Id);
                    _renderUnitsById[u.Id] = ru;
                }

                var c = new Color(u.ColorRgb.x.ToFloat(), u.ColorRgb.y.ToFloat(), u.ColorRgb.z.ToFloat(), 1f);
                ru.ApplyOwnership(u.OwnerUserId, u.FactionId, c);

                ru.ApplyHealth(u.CurrentHp, u.MaxHp);

                if (debugSyncTopActivity) ru.ApplyTopActivity(u.TopActivity);
                if (debugSyncActivities) ru.ApplyActivityStack(u.ActivityStack);
                if (debugSyncAbilities) ru.ApplyAbilities(u.Abilities);

                if (debugSyncAbilities)
                    ru.ApplyChildrenDebug(u.ChildCount, u.ChildAbilities);
                if (debugSyncAbilities)
                    ru.ApplyRootArchetypeId(u.RootArchetypeId);
            }
        }

        private RenderUnit CreateRenderUnit(int unitId)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (renderRoot != null) go.transform.SetParent(renderRoot, worldPositionStays: true);
            go.transform.localScale = Vector3.one * renderUnitSize;

            // Color will be applied from RenderUnitSnapshot ownership data.

            var ru = go.AddComponent<RenderUnit>();
            ru.Bind(unitId);
            return ru;
        }

        private static void AppendUnitShort(System.Text.StringBuilder sb, in LogicUnitSnapshot u)
        {
            if (sb == null) return;
            sb.Append(u.Index).Append(':');

            if (!string.IsNullOrEmpty(u.Name))
                sb.Append(u.Name);

            if (u.Hp.HasValue)
                sb.Append("hp").Append(u.Hp.Value);

            if (u.Position.HasValue)
            {
                var p = u.Position.Value;
                sb.Append("@(")
                    .Append(p.x.ToString()).Append(',')
                    .Append(p.y.ToString()).Append(',')
                    .Append(p.z.ToString())
                    .Append(')');
            }
        }

        /// <summary>
        /// Thread-safe: enqueue a message to be applied on the logic thread. 线程安全：将输入消息入队，逻辑线程在 Tick 中处理。
        /// </summary>
        public void EnqueueToLogic(ILogicInput input)
        {
            if (input == null) return;
            if (_cts == null || _logicWorld == null) return;
            _toLogic.Enqueue(input);
        }

        /// <summary>
        /// Main-thread: register a root actor into the logic world. 主线程：向 LogicWorld 注册 Actor（通常由测试生成器/引导代码调用）。
        /// </summary>
        [Obsolete(
            "Do not register Actors from the main thread. Send a UnitSpawn command instead so LogicWorld creates Actors on a deterministic tick.",
            error: true)]
        public void RegisterActor(Actor actor)
        {
            if (actor == null) return;
            if (_logicWorld == null) return;
            _logicWorld.AddActor(actor);
        }

        /// <summary>
        /// Thread-safe: decode a received command buffer and enqueue it to the logic thread. 线程安全：解码收到的命令并入队到逻辑线程。
        /// </summary>
        public bool ReceiveEncodedCommand(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return false;

            if (!Game.Command.CommandFactory.TryDecode(bytes, out var cmd))
                return false;

            EnqueueToLogic(new EnqueueCommandInput(cmd));
            return true;
        }

        private readonly struct EnqueueCommandInput : ILogicInput
        {
            private readonly Game.Command.Command _cmd;

            public EnqueueCommandInput(Game.Command.Command cmd)
            {
                _cmd = cmd;
            }

            public void Apply(LogicWorld world)
            {
                if (world == null) return;

                var logic = new CommandToLogicCommand(_cmd);
                // If cmd.Tick > 0, schedule deterministically; else keep legacy ASAP behavior.
                if (_cmd.Tick > 0)
                    world.EnqueueCommandAt(logic, _cmd.Tick, _cmd.Sequence);
                else
                    world.EnqueueCommand(logic);
            }
        }

        private void DebugUpdateRenderUnitPartitions(in RenderSnapshot rs)
        {
            if (_logicWorld == null || _logicWorld.Map == null) return;

            var es = _logicWorld.EnemySearch as EnemySearchService;
            if (es == null) return;

            var units = rs.Units;
            if (units == null || units.Length == 0) return;

            for (int i = 0; i < units.Length; i++)
            {
                var u = units[i];
                if (!_renderUnitsById.TryGetValue(u.Id, out var ru) || ru == null) continue;

                if (es.TryGetPartitionForWorldPos(u.Position, out var px, out var py))
                {
                    ru.ApplyEnemyPartition(px, py, es.PartitionCellSize);

                    if (debugLogRenderUnitPartition && i < 3)
                        Debug.Log($"[PartitionSync] unitId={u.Id} pos={u.Position} partition=({px},{py}) cellSize={es.PartitionCellSize}");
                }
                else
                {
                    if (debugLogRenderUnitPartition && i < 1)
                        Debug.LogWarning($"[PartitionSync] failed: unitId={u.Id} pos={u.Position} (index/map not ready?)");
                }
            }
        }
    }
}
