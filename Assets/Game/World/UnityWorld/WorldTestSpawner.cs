﻿using System;
using System.Collections;
using System.Collections.Generic;
using Game.Command;
using Game.Serialization;
using Game.Scripts.Fixed;
using Game.Unit;
using Game.Unit.Ability.BaseAbilities;
using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Test MonoBehaviour:
    /// - Starts a World with tickRate=10.
    /// - After delay, creates N tank_example logic instances (Actor graphs) and registers them into LogicWorld.
    /// - Every moveIntervalSeconds, sends a random Move command for a random subset of units.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class WorldTestSpawner : MonoBehaviour, IWorldSettingsProvider
    {
        [Header("Spawn")]
        [Tooltip("How many units to spawn.")]
        public int spawnCount = 5;

        [Tooltip("Seconds to wait before spawning")]
        public float delaySeconds = 10f;

        [Tooltip("Spawn position random radius (XZ).")]
        public float spawnRadius = 15f;

        [Tooltip("Random seed (0 = use time).")]
        public int randomSeed = 0;

        [Tooltip("Path under Assets to YAML sample")]
        public string yamlAssetPath = "Assets/Game/Data/Units/Samples/tank_example.yaml";

        [Header("Debug Map")]
        [Tooltip("If true, override World's debug map size before it starts logic.")]
        public bool overrideWorldDebugMapSize = true;

        [Tooltip("Debug map width/height (cells). Only used when overrideWorldDebugMapSize=true.")]
        public int debugMapSize = 200;

        [Header("Logic")]
        [Tooltip("Logic tick rate for World (ticks per second).")]
        public int logicTickRate = 10;

        [Header("Random Navigate Test")]
        [Tooltip("If true, periodically send random move commands (interpreted as Navigate/pathfinding by World).")]
        public bool enableRandomMove = true;

        [Tooltip("Seconds between move commands.")]
        public float moveIntervalSeconds = 5f;

        [Tooltip("Percentage of spawned units to include in each move command. 0-1. e.g. 0.2 = 20%.")]
        [Range(0f, 1f)]
        public float unitPercentPerCommand = 0.2f;

        [Tooltip("Hard cap for units per move command. 0/negative means no cap.")]
        public int maxUnitsPerMoveCommand = 0;

        [Header("Spawn (UnitFactory)")]
        [Tooltip("Optional parent transform for spawned unit GameObjects.")]
        public Transform spawnedUnitsRoot;

        [Header("Spawn Mode")]
        [Tooltip("If true, spawn via UnitFactory/UnitComponent (instantiates prefabs). If false, spawn logic-only Actors and rely on World to render cubes.")]
        public bool spawnViaUnitFactory = false;

        [Header("Debug")]
        [Tooltip("If true, enable CommandFactory logging so created/encoded/decoded commands print to Console.")]
        public bool enableCommandFactoryLogging = true;

        [Header("Debug (Navigation Marker)")]
        [Tooltip("If true, show a red box (size=0.5) at the current random navigation destination.")]
        public bool showNavigationDestinationMarker = true;

        private World _world;
        private readonly List<int> _spawnedUnitIds = new List<int>(128);
        private readonly List<GameObject> _spawnedUnitGos = new List<GameObject>(128);
        private readonly List<UnitComponent> _spawnedUnitComps = new List<UnitComponent>(128);
        private System.Random _rng;

        private Game.Map.IMap _cachedMap;
        private readonly List<Game.Grid.GridPosition> _walkableCells = new List<Game.Grid.GridPosition>(1024);

        private GameObject _navDestMarkerGo;
        private Renderer _navDestMarkerRenderer;

        public int Priority => 1000;

        public void Mutate(ref WorldSettings settings)
        {
            // Apply tick rate.
            settings.tickRate = logicTickRate > 0 ? logicTickRate : settings.tickRate;

            // Override debug map size/seed before World starts.
            if (overrideWorldDebugMapSize && debugMapSize > 0)
            {
                settings.renderDebugTankMap = true;
                settings.debugTankMapSize = debugMapSize;
                if (randomSeed != 0) settings.debugTankMapSeed = randomSeed;
            }
        }

        private void Awake()
        {
            // WorldTestSpawner is optional; it shouldn't create core systems.
            _world = GetComponent<World>();

            _rng = randomSeed == 0 ? new System.Random(Environment.TickCount) : new System.Random(randomSeed);

            if (enableCommandFactoryLogging)
                Game.Command.CommandFactory.EnableCommandLogging = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Keep World inspector values in sync while tweaking in Editor.
            // Note: OnValidate can be called before Awake, so be defensive.
            if (!isActiveAndEnabled) return;
            var w = GetComponent<World>();
            if (w == null) return;

            // NOTE: We intentionally don't mutate World runtime here (World resolves settings on Awake/Start).
        }
#endif

        // Remove old push-based API; World pulls settings via IWorldSettingsProvider.

        private void Start()
        {
            if (_world == null)
            {
                Debug.LogError("WorldTestSpawner requires a World component on the same GameObject.");
                return;
            }

            StartCoroutine(SpawnAfterDelay());
            if (enableRandomMove)
                StartCoroutine(SendRandomMoveLoop());
        }

        private IEnumerator SpawnAfterDelay()
        {
            yield return new WaitForSeconds(delaySeconds);

            CacheWalkableCellsOrFail();
            if (_walkableCells.Count == 0)
            {
                Debug.LogError("No walkable cells found on map yet. Try increasing delaySeconds so LogicWorld is ready.");
                yield break;
            }

            // Issue Spawn commands (LogicWorld will create Actors on the scheduled tick).
            // Unit/archetype are identified by an int (archetypeId). For now, tests use 1.
            const int archetypeId = 1;

            // Schedule at a future tick to ensure the logic thread is running.
            var spawnTick = _world != null && _world.Logic != null ? Math.Max(1, _world.Logic.Tick + 1) : 1;
            var seq = 1;

            for (int i = 0; i < spawnCount; i++)
            {
                var unitId = i + 1;

                // Override spawn position:
                // - must be on a walkable cell
                // - height(Y) must be 0
                var spawnCell = PickRandomWalkableCell();
                if (_cachedMap == null || !_cachedMap.IsWalkable(spawnCell, Game.Map.MapLayer.Tanks))
                {
                    // Extremely defensive: if cache changed or invalid, sample again a few times.
                    const int retries = 16;
                    bool found = false;
                    for (int t = 0; t < retries; t++)
                    {
                        var c = PickRandomWalkableCell();
                        if (_cachedMap != null && _cachedMap.IsWalkable(c, Game.Map.MapLayer.Tanks))
                        {
                            spawnCell = c;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        Debug.LogError("Failed to sample a walkable spawn cell.");
                        continue;
                    }
                }

                var spawnPos = CellCenterToUnity(spawnCell);
                spawnPos.y = 0f;

                // Assign stable player ownership (hard-coded palette).
                // Player id: 0-7. 0-3 cool tones, 4-7 warm tones.
                int userId = i % Game.Unit.PlayerPalette.MaxPlayers;
                int factionId = userId; // injection rule: userId == factionId

                var cmd = CommandFactory.SpawnUnit(unitId, archetypeId, userId, factionId, spawnPos);
                cmd = CommandFactory.WithOrder(cmd, spawnTick, seq++);
                var bytes = CommandFactory.Encode(cmd);
                _world.ReceiveEncodedCommand(bytes);

                _spawnedUnitIds.Add(unitId);

                if (spawnViaUnitFactory)
                {
                    // Spawn visual objects via UnitFactory; they will have UnitComponent.
                    // NOTE: logic actor creation must still go through commands; the GO side is for rendering/debug.
                    var go = UnitFactory.CreateUnitFromFile(yamlAssetPath, factionId, userId, Game.Unit.PlayerPalette.GetColor(userId), spawnedUnitsRoot);
                    if (go != null)
                    {
                        _spawnedUnitGos.Add(go);
                        var uc = go.GetComponent<UnitComponent>();
                        if (uc != null) _spawnedUnitComps.Add(uc);
                    }
                }
            }

            Debug.Log($"Spawned and registered {_spawnedUnitIds.Count} units. spawnViaUnitFactory={spawnViaUnitFactory}");
        }

        private IEnumerator SendRandomMoveLoop()
        {
            // Wait until after initial spawn.
            yield return new WaitForSeconds(delaySeconds + 0.1f);
            
            CacheWalkableCellsOrFail();
            if (_walkableCells.Count == 0)
                yield break;
            
            while (enableRandomMove)
            {
                try
                {
                    if (_world != null && _spawnedUnitIds.Count > 0)
                    {
                        var unitIds = PickRandomUnitIds();
                        if (unitIds != null && unitIds.Length > 0)
                        {
                            var destCell = PickRandomWalkableCell();
                            var dest = CellCenterToUnity(destCell);

                            if (showNavigationDestinationMarker)
                                UpdateNavigationDestinationMarker(dest);

                            var cmd = CommandFactory.Move(unitIds, dest);
                            var bytes = CommandFactory.Encode(cmd);
                            _world.ReceiveEncodedCommand(bytes);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            
                yield return new WaitForSeconds(moveIntervalSeconds > 0.1f ? moveIntervalSeconds : 0.1f);
            }

            yield break;
        }

        private void UpdateNavigationDestinationMarker(Vector3 dest)
        {
            // Force marker on the ground plane.
            dest.y = 0f;

            if (_navDestMarkerGo == null)
            {
                _navDestMarkerGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _navDestMarkerGo.name = "NavDestinationMarker";
                _navDestMarkerGo.transform.SetParent(transform, worldPositionStays: true);
                _navDestMarkerGo.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

                // Remove collider so it doesn't interfere with gameplay physics.
                var col = _navDestMarkerGo.GetComponent<Collider>();
                if (col != null) Destroy(col);

                _navDestMarkerRenderer = _navDestMarkerGo.GetComponent<Renderer>();
                if (_navDestMarkerRenderer != null)
                {
                    // Use sharedMaterial to avoid leaking a new material instance per marker.
                    var mat = new Material(Shader.Find("Standard"));
                    mat.color = Color.red;
                    _navDestMarkerRenderer.sharedMaterial = mat;
                }
            }

            _navDestMarkerGo.transform.position = dest + new Vector3(0f, 0.25f, 0f);
        }

        private void OnDestroy()
        {
            if (_navDestMarkerGo != null)
            {
                // If we created a unique sharedMaterial above, destroy it too.
                if (_navDestMarkerRenderer != null && _navDestMarkerRenderer.sharedMaterial != null)
                {
                    Destroy(_navDestMarkerRenderer.sharedMaterial);
                }

                Destroy(_navDestMarkerGo);
                _navDestMarkerGo = null;
                _navDestMarkerRenderer = null;
            }
        }

        private int[] PickRandomUnitIds()
        {
            if (_spawnedUnitIds == null || _spawnedUnitIds.Count == 0)
                return Array.Empty<int>();

            // Decide K based on percent.
            var pct = Mathf.Clamp01(unitPercentPerCommand);
            int k = Mathf.Clamp(Mathf.RoundToInt(_spawnedUnitIds.Count * pct), 1, _spawnedUnitIds.Count);

            // Apply optional cap.
            if (maxUnitsPerMoveCommand > 0)
                k = Math.Min(k, maxUnitsPerMoveCommand);

            // Sample without replacement (Fisher-Yates on a small temp array)
            var tmp = new int[_spawnedUnitIds.Count];
            for (int i = 0; i < _spawnedUnitIds.Count; i++) tmp[i] = _spawnedUnitIds[i];
            for (int i = 0; i < k; i++)
            {
                int j = _rng.Next(i, tmp.Length);
                (tmp[i], tmp[j]) = (tmp[j], tmp[i]);
            }

            var res = new int[k];
            Array.Copy(tmp, 0, res, 0, k);
            return res;
        }

        private void CacheWalkableCellsOrFail()
        {
            if (_cachedMap != null && _walkableCells.Count > 0) return;
            if (_world == null || _world.Logic == null) return;

            _cachedMap = _world.Logic.Map;
            if (_cachedMap == null) return;

            _walkableCells.Clear();
            // Use Tanks layer as movement mask for this test.
            foreach (var p in _cachedMap.GetAllWalkable(Game.Map.MapLayer.Tanks))
                _walkableCells.Add(p);
        }

        private Game.Grid.GridPosition PickRandomWalkableCell()
        {
            if (_walkableCells == null || _walkableCells.Count == 0)
                return default;
            int idx = _rng.Next(0, _walkableCells.Count);
            return _walkableCells[idx];
        }

        private Vector3 CellCenterToUnity(Game.Grid.GridPosition cell)
        {
            // Map's Grid is in fixed coordinates on logic side.
            // Convert to Unity Vector3 for command payload.
            if (_cachedMap == null) return Vector3.zero;
            var fixed2 = _cachedMap.Grid.GetCellCenterWorld(cell);
            var u2 = fixed2.ToUnity();
            // Height is forced to 0 for this test.
            return new Vector3(u2.x, 0f, u2.y);
        }
    }
}
