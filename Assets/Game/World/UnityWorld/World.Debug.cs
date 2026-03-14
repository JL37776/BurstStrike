using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using Game.Core;
using Game.Scripts.Fixed;
using Game.Map;
using Game.Pathing.Debug;
using Game.World.Logic;

namespace Game.World
{
    /// <summary>
    /// World — Debug partial: all debug inspector fields, debug visualization methods,
    /// debug Update logic (snapshot logging, RenderIds, ChildDebug, PartitionSync),
    /// and debug map rendering.
    /// <para>
    /// This file is intentionally separated from the core World.cs so that
    /// debug-only code is easy to locate, review, and conditionally strip.
    /// </para>
    /// </summary>
    public sealed partial class World
    {
        // ═══════════════════════════════════════════════════════════════════
        //  Debug Inspector Fields
        // ═══════════════════════════════════════════════════════════════════

        [Header("Debug")] [Tooltip("If true, log snapshot data periodically (diagnostics).")]
        public bool logSnapshots = true;

        [Tooltip("Snapshot log interval in seconds.")]
        public float snapshotLogIntervalSeconds = 3f;

        [Tooltip("How many units to print in snapshot log (head).")]
        public int snapshotPrintHeadCount = 5;

        [Tooltip("If units exceed head count, also print the last unit (tail) for diagnostics.")]
        public bool snapshotPrintTail = true;

        [Tooltip("If true, log RenderSnapshot unit id stats (helps diagnose flicker/id reuse issues).")]
        public bool logRenderIds = false;

        [Tooltip("Render id log interval in seconds.")]
        public float renderIdLogIntervalSeconds = 2f;

        private float _nextRenderIdLogTime;

        [Header("Debug Map Render")]
        [Tooltip("If true, render a debug Tank map (example NxN) before starting LogicWorld.")]
        public bool renderDebugTankMap = true;

        [Tooltip("Example map size in cells (NxN).")]
        public int debugTankMapSize = 5;

        [Tooltip("Cell size in Unity units for debug tank map.")]
        public float debugTankMapCellSize = 0.5f;

        [Tooltip("Cube height in Unity units for debug tank map.")]
        public float debugTankMapCellHeight = 0.5f;

        [Tooltip("Obstacle probability for example map Tanks layer.")] [Range(0f, 1f)]
        public float debugTankObstacleProbability = 0.10f;

        [Tooltip("Seed for example map. Same seed => same layout.")]
        public int debugTankMapSeed = 12345;

        [Tooltip("Parent transform for generated debug tank map cubes. If null, uses this World transform.")]
        public Transform debugTankMapRoot;

        [Tooltip("If true, log which debug map parameters were used to render/build the example map.")]
        public bool logDebugTankMapParams = false;

        [Header("Debug Walkability")]
        [Tooltip("If true, draw a main-thread overlay of non-walkable cells for Tanks layer (red cubes).")]
        public bool debugRenderTanksBlocked = false;

        [Header("Debug Pathing")]
        [Tooltip("If enabled, pathfinding mode is forced by Debug Path Mode below.")]
        public bool debugForcePathMode = false;

        [Tooltip("Which pathfinding mode to force when Debug Force Path Mode is enabled.")]
        public DebugPathMode debugPathMode = DebugPathMode.AStar;

        [Tooltip("If Debug Force Path Mode is disabled, commands with unit count <= this threshold use A*; otherwise FlowField.")]
        [Min(1)]
        public int pathingAStarUnitCountThreshold = 10;

        [Header("Enemy Search (Partition Index)")]
        [Tooltip("EnemySearchService partition cell size in MAP CELLS. Min=5, default=5.")]
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

        // ═══════════════════════════════════════════════════════════════════
        //  WorldDebugPath — static helper for logic thread
        // ═══════════════════════════════════════════════════════════════════

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

        // ═══════════════════════════════════════════════════════════════════
        //  Debug Update (called from Update in World.cs)
        // ═══════════════════════════════════════════════════════════════════

        private void DebugUpdate()
        {
            // Sync cross-thread flag so LogicWorld knows whether to build LogicSnapshot.
            WorldDebugAccess.ShouldBuildLogicSnapshot = logSnapshots;

            // Partition sync — must run AFTER ApplyRenderSnapshot/ApplyInterpolatedRender.
            if (debugSyncRenderUnitPartition && _latestRenderSnapshot.HasValue)
                DebugUpdateRenderUnitPartitions(_latestRenderSnapshot.Value);

            // Child debug
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
                        int snapChild = u.ChildCount ?? -1;
                        string arch = u.RootArchetypeId ?? "<null>";
                        int renderChild = -1;
                        if (_renderUnitsById.TryGetValue(u.Id, out var ru) && ru != null)
                            renderChild = ru.ChildCount;

                        GameLog.Info(GameLog.Tag.Debug, $"ChildDebug tick={rs.Tick} unitId={u.Id} archetype={arch} snapChildCount={snapChild} renderChildCount={renderChild}");
                    }
                }
            }

            // Periodic snapshot log
            if (logSnapshots && snapshotLogIntervalSeconds > 0f && Time.unscaledTime >= _nextSnapshotLogTime)
            {
                _nextSnapshotLogTime = Time.unscaledTime + snapshotLogIntervalSeconds;
                if (_latestSnapshot.HasValue)
                {
                    var s = _latestSnapshot.Value;
                    var units = s.Units;
                    if (units == null)
                    {
                        GameLog.Info(GameLog.Tag.Snap, $"t={s.Tick} u=0");
                        return;
                    }

                    var count = units.Length;
                    if (count == 0)
                    {
                        GameLog.Info(GameLog.Tag.Snap, $"t={s.Tick} u=0");
                        return;
                    }

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

                    GameLog.Info(sb.ToString());
                }
            }

            // Render id stats
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

                        GameLog.Info(GameLog.Tag.RenderIds, $"tick={rs.Tick} units={units.Length} uniq={set.Count} dup={dup}");
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Debug Drain Path Queue (called from Update in World.cs)
        // ═══════════════════════════════════════════════════════════════════

        private void DebugDrainPathQueue()
        {
            if (_pathDebugCubes == null || _logicWorld == null || _logicWorld.Map == null) return;

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

        // ═══════════════════════════════════════════════════════════════════
        //  Debug Awake Init (called from Awake in World.cs)
        // ═══════════════════════════════════════════════════════════════════

        private void DebugAwakeInit()
        {
            // Cache path debug visualizer on main thread if present in scene.
            _pathDebugCubes = GetComponentInChildren<PathDebugCubes>();
            if (_pathDebugCubes == null)
                _pathDebugCubes = UnityEngine.Object.FindFirstObjectByType<PathDebugCubes>();

            // Allow logic thread to enqueue debug paths without holding a World instance.
            WorldDebugPath.Queue = _pathDebugQueue;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Debug OnDestroy cleanup
        // ═══════════════════════════════════════════════════════════════════

        private void DebugOnDestroy()
        {
            if (WorldDebugPath.Queue == _pathDebugQueue) WorldDebugPath.Queue = null;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Debug Map / Visualization
        // ═══════════════════════════════════════════════════════════════════

        private void RenderTanksBlockedOverlay()
        {
            if (_logicWorld == null || _logicWorld.Map == null)
            {
                GameLog.Warn(GameLog.Tag.Debug, "LogicWorld or Map not ready; cannot render blocked overlay.");
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
                    if (map.IsWalkable(cell, Game.Map.MapLayer.Tanks))
                        continue;

                    blockedCount++;
                    var c2 = grid.GetCellCenterWorld(cell);
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = $"Blocked_{x}_{y}";
                    go.transform.SetParent(_tanksBlockedRoot.transform, worldPositionStays: true);

                    float yPos = (debugTankMapCellHeight * 0.5f) + 0.5f;
                    go.transform.position = new Vector3(c2.x.ToFloat(), yPos, c2.y.ToFloat());
                    go.transform.localScale = Vector3.one * 0.35f;

                    var r = go.GetComponent<Renderer>();
                    if (r != null) r.sharedMaterial = mat;
                    var col = go.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                }
            }

            GameLog.Info(GameLog.Tag.Debug, $"map={map.Width}x{map.Height} blocked(Tanks)={blockedCount}");

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
            var existing = GetComponentInChildren<Game.Map.TankMapRenderer>();
            var size = Mathf.Max(1, debugTankMapSize);

            var map = Game.Map.MapLoader.CreateExampleMap(size, size, debugTankObstacleProbability, debugTankMapSeed);

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

                GameLog.Info(GameLog.Tag.World,
                    $"RenderDebugTankMap size={size} seed={debugTankMapSeed} p={debugTankObstacleProbability:F3} blocked={blocked}");
            }

            var root = debugTankMapRoot != null ? debugTankMapRoot : transform;
            var go = new GameObject("TankMapRenderer");
            go.transform.SetParent(root, false);

            var r = go.AddComponent<Game.Map.TankMapRenderer>();
            r.enabled = false;

            r.Configure(debugTankMapCellSize, debugTankMapCellHeight, Vector3.zero);
            r.Render(map);
        }

        private Game.Map.IMap BuildLogicMap()
        {
            var size = Mathf.Max(1, debugTankMapSize);
            var data = MapLoader.CreateExampleMap(size, size, debugTankObstacleProbability, debugTankMapSeed);

            if (logDebugTankMapParams)
            {
                int blocked = 0;
                for (int y = 0; y < data.height; y++)
                for (int x = 0; x < data.width; x++)
                    if ((data.Layers[y, x] & Game.Map.MapLayer.Tanks) != 0)
                        blocked++;

                GameLog.Info(GameLog.Tag.World,
                    $"BuildLogicMap size={size} seed={debugTankMapSeed} p={debugTankObstacleProbability:F3} blocked={blocked}");
            }

            var origin = FixedVector2.Zero;
            var cellSize = new FixedVector2(Fixed.FromFloat(debugTankMapCellSize),
                Fixed.FromFloat(debugTankMapCellSize));

            return MapWithHeight.FromMapData(data, origin, cellSize);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Debug Partition Sync
        // ═══════════════════════════════════════════════════════════════════

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
                        GameLog.Info(GameLog.Tag.Partition, $"unitId={u.Id} pos={u.Position} partition=({px},{py}) cellSize={es.PartitionCellSize}");
                }
                else
                {
                    if (debugLogRenderUnitPartition && i < 1)
                        GameLog.Warn(GameLog.Tag.Partition, $"failed: unitId={u.Id} pos={u.Position} (index/map not ready?)");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Debug Helpers
        // ═══════════════════════════════════════════════════════════════════

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
    }
}
