﻿﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using Game.Core;
using Game.Scripts.Fixed;
using Game.Unit;
using Game.World.Logic;
using BurstStrike.Net.Session;

namespace Game.World
{
    /// <summary>
    /// Unity-facing World bridge (MonoBehaviour). Unity 主线程的 World 桥接层（MonoBehaviour）。
    /// </summary>
    /// <remarks>
    /// Threading model: World runs on Unity main thread; LogicWorld runs on a background logic thread. 线程模型：World 在 Unity 主线程；LogicWorld 在后台逻辑线程。
    /// Data crossing threads must be marshalled via queues/snapshots. 跨线程数据必须通过队列/快照封送。
    /// </remarks>
    public sealed partial class World : MonoBehaviour
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

        // ── Debug fields are in World.Debug.cs (partial class) ──

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

        // ── Debug map/walkability/pathing/partition/inspector fields are in World.Debug.cs ──

        // ═══════════════════════════════════════════════════════════════
        //  Game Session (Local / LAN / Network)
        // ═══════════════════════════════════════════════════════════════

        [Header("Game Mode")]
        [Tooltip("Game mode: Local (single player), LanHost (host a LAN game), LanGuest (join a LAN game), Network (remote server).")]
        public GameMode gameMode = GameMode.Local;

        [Header("Network / LAN Settings")]
        [Tooltip("Server host IP (for Network and LAN-Guest modes).")]
        public string serverHost = "127.0.0.1";

        [Tooltip("Server port.")]
        public int serverPort = 9050;

        [Tooltip("Max players per room (LAN Host only).")]
        public int maxPlayersPerRoom = 2;

        [Tooltip("Countdown ticks after room full (LAN Host only). 90 ticks = 3 seconds at 30 tick/s.")]
        public int countdownTicks = 90;

        [Tooltip("Auth token from web server (Network mode only).")]
        public string authToken = "";

        [Tooltip("Room ID for auto-match (empty = auto). 房间ID（空=自动匹配）。")]
        public string roomId = "";

        /// <summary>The active game session (null before Start, set during StartLogicWorld).</summary>
        private IGameSession _session;

        /// <summary>Public accessor for the current session.</summary>
        public IGameSession Session => _session;

        private readonly ConcurrentQueue<ILogicInput> _toLogic = new ConcurrentQueue<ILogicInput>();
        private readonly ConcurrentQueue<ILogicOutput> _fromLogic = new ConcurrentQueue<ILogicOutput>();


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

            // Debug init (path debug cubes, debug path queue, debug flags).
            DebugAwakeInit();

            // Expose debug settings to extracted command adapter.
            WorldDebugAccess.SetWorld(this);

            // Default debug flags (can be changed live in inspector).
            WorldDebugAccess.SetRenderUnitDebugFlags(new WorldDebugAccess.RenderUnitDebugFlags(
                debugSyncTopActivity,
                debugSyncActivities,
                debugSyncAbilities));

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


        private void OnDestroy()
        {
            // prevent logic thread from calling into a destroyed MonoBehaviour
            if (_selfRef != null) _selfRef.Value = null;

            // Clear debug accessors
            WorldDebugAccess.SetWorld(null);
            StopLogicWorld();

            // Debug cleanup is in World.Debug.cs
            DebugOnDestroy();
        }

        // ── RenderTanksBlockedOverlay, RenderDebugTankMap, BuildLogicMap, WorldDebugPath
        //    are in World.Debug.cs (partial class) ──

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
                    GameLog.Warn(GameLog.Tag.Config, cfgErrors[i]);
            }

            // Build logic map (layers + heights) and pass it into LogicWorld.
            // Note: LogicWorld runs off the main thread, so we only pass pure data.
            var logicMap = BuildLogicMap();

            // Archetypes (deterministic data): preload ALL YAML on main thread, then inject into logic.
            // This is behind an interface so we can swap to network source later.
            var archetypeRootDir = "Assets/Game/Data/Units/Samples";
            var source = new DirectoryYamlArchetypeSource(archetypeRootDir, recursive: true);
            var dict = source.LoadAll(out var errors);

            GameLog.Info(GameLog.Tag.Archetype, $"loaded={dict?.Count ?? 0} from '{archetypeRootDir}'");
            if (errors != null && errors.Count > 0)
            {
                // Dev-time behavior: log and continue with whatever loaded.
                for (int i = 0; i < errors.Count; i++)
                    GameLog.Error(GameLog.Tag.Archetype, errors[i]);
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

            // Combat data: load weapons/warheads/projectiles from YAML (main thread).
            var combatDataRoot = Path.Combine(Application.dataPath, "Game/Data/Combat");
            Game.Combat.CombatRegistry combatRegistry;
            if (System.IO.Directory.Exists(combatDataRoot))
            {
                combatRegistry = Game.Serialization.CombatDataLoader.LoadFromDirectory(combatDataRoot, out var combatErrors);
                if (combatErrors != null && combatErrors.Count > 0)
                    for (int i = 0; i < combatErrors.Count; i++)
                        GameLog.Warn(GameLog.Tag.Config, combatErrors[i]);
            }
            else
            {
                GameLog.Info(GameLog.Tag.Config, $"No combat data directory at '{combatDataRoot}', using defaults.");
                combatRegistry = Game.Serialization.CombatDataLoader.BuildDefaults();
            }
            _logicWorld.SetCombatRegistry(combatRegistry);

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

            // ── Create and start the game session ──
            _session?.Dispose();
            _session = GameSessionFactory.Create(new SessionConfig
            {
                Mode = gameMode,
                TickRate = tickRate,
                Host = serverHost,
                Port = serverPort,
                MaxPlayers = maxPlayersPerRoom,
                CountdownTicks = countdownTicks,
                AuthToken = authToken,
                RoomId = string.IsNullOrEmpty(roomId) ? null : roomId,
            });

            // Wire session callbacks → command injection into LogicWorld
            _session.OnCommandReady = (bytes, tick, seq) =>
            {
                if (bytes == null || bytes.Length == 0) return;
                if (!Game.Command.CommandFactory.TryDecode(bytes, out var cmd)) return;
                cmd.Tick = tick;
                cmd.Sequence = seq;
                EnqueueToLogic(new EnqueueCommandInput(cmd));
            };

            _session.OnTickReady = tick =>
            {
                // In networked modes, this signals that tick N's commands are complete.
                // The LogicWorld's self-driven tick loop handles pacing independently.
            };

            _session.OnStateChanged = state =>
            {
                GameLog.Info(GameLog.Tag.Config, $"[Session] State → {state} (mode={_session.ModeName})");
            };

            _session.OnLog = msg =>
            {
                GameLog.Info(GameLog.Tag.Config, msg);
            };

            _session.Start();
            GameLog.Info(GameLog.Tag.Config, $"[Session] Started: mode={_session.ModeName}");
        }

        public void StopLogicWorld()
        {
            // Stop session first (may have embedded server threads)
            _session?.Dispose();
            _session = null;

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
            DebugDrainPathQueue();

            // Poll game session (drains network messages, processes ticks)
            _session?.Update();

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

            // All debug-only Update work (snapshot logging, RenderIds, ChildDebug, PartitionSync).
            DebugUpdate();
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

        /// <summary>
        /// Submit encoded commands through the active game session.
        /// In Local mode: injected directly into LogicWorld.
        /// In network modes: sent to server, which echoes merged commands back.
        /// This is the primary entry point for all command submission in networked play.
        /// </summary>
        public void SubmitCommandsThroughSession(int tick, byte[][] encodedCommands)
        {
            if (_session == null || _session.State != SessionState.Running)
            {
                // Fallback: inject directly (legacy behavior for offline / pre-session state)
                if (encodedCommands != null)
                {
                    for (int i = 0; i < encodedCommands.Length; i++)
                        ReceiveEncodedCommand(encodedCommands[i]);
                }
                return;
            }
            _session.SubmitCommands(tick, encodedCommands);
        }

        /// <summary>
        /// Submit a single encoded command through the active game session.
        /// Convenience overload for single-command submission.
        /// </summary>
        public void SubmitCommandThroughSession(int tick, byte[] encodedCommand)
        {
            if (encodedCommand == null || encodedCommand.Length == 0) return;
            SubmitCommandsThroughSession(tick, new[] { encodedCommand });
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

    }
}
