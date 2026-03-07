using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// World startup settings snapshot.
    /// World has safe defaults (from its inspector fields) and can be overridden by optional providers
    /// (e.g. WorldTestSpawner) BEFORE the world starts rendering debug map and starting the logic thread.
    /// </summary>
    [System.Serializable]
    public struct WorldSettings
    {
        [Header("Logic")]
        public int tickRate;

        [Header("Debug Map")]
        public bool renderDebugTankMap;
        public int debugTankMapSize;
        public float debugTankMapCellSize;
        public float debugTankMapCellHeight;
        public float debugTankObstacleProbability;
        public int debugTankMapSeed;
        public bool logDebugTankMapParams;

        [Header("Debug")]
        public bool debugRenderTanksBlocked;

        public static WorldSettings FromWorld(World w)
        {
            return new WorldSettings
            {
                tickRate = w != null ? w.tickRate : 30,
                renderDebugTankMap = w != null && w.renderDebugTankMap,
                debugTankMapSize = w != null ? w.debugTankMapSize : 200,
                debugTankMapCellSize = w != null ? w.debugTankMapCellSize : 0.5f,
                debugTankMapCellHeight = w != null ? w.debugTankMapCellHeight : 0.5f,
                debugTankObstacleProbability = w != null ? w.debugTankObstacleProbability : 0.10f,
                debugTankMapSeed = w != null ? w.debugTankMapSeed : 12345,
                logDebugTankMapParams = w != null && w.logDebugTankMapParams,
                debugRenderTanksBlocked = w != null && w.debugRenderTanksBlocked,
            };
        }

        public void ApplyTo(World w)
        {
            if (w == null) return;
            w.tickRate = tickRate > 0 ? tickRate : w.tickRate;
            w.renderDebugTankMap = renderDebugTankMap;
            w.debugTankMapSize = debugTankMapSize;
            w.debugTankMapCellSize = debugTankMapCellSize;
            w.debugTankMapCellHeight = debugTankMapCellHeight;
            w.debugTankObstacleProbability = debugTankObstacleProbability;
            w.debugTankMapSeed = debugTankMapSeed;
            w.logDebugTankMapParams = logDebugTankMapParams;
            w.debugRenderTanksBlocked = debugRenderTanksBlocked;
        }
    }
}
