﻿using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Game.World
{
    /// <summary>
    /// Minimal cross-thread accessors used by logic-side command execution.
    /// This avoids keeping large adapter classes nested inside World.cs.
    /// </summary>
    internal static class WorldDebugAccess
    {
        private static World _world;

        public readonly struct RenderUnitDebugFlags
        {
            public readonly bool SyncTopActivity;
            public readonly bool SyncActivities;
            public readonly bool SyncAbilities;

            public RenderUnitDebugFlags(bool syncTopActivity, bool syncActivities, bool syncAbilities)
            {
                SyncTopActivity = syncTopActivity;
                SyncActivities = syncActivities;
                SyncAbilities = syncAbilities;
            }
        }

        // Bitmask for cross-thread flags (volatile int is allowed).
        // 1 = top, 2 = activities, 4 = abilities
        private static volatile int _renderUnitDebugMask;

        // Cross-thread flag: if true, LogicWorld will build and enqueue LogicSnapshot each tick.
        // Set by main thread based on World.logSnapshots.
        private static volatile bool _shouldBuildLogicSnapshot = true;

        public static bool ShouldBuildLogicSnapshot
        {
            get => _shouldBuildLogicSnapshot;
            set => _shouldBuildLogicSnapshot = value;
        }

        public static void SetRenderUnitDebugFlags(in RenderUnitDebugFlags flags)
        {
            int mask = 0;
            if (flags.SyncTopActivity) mask |= 1;
            if (flags.SyncActivities) mask |= 2;
            if (flags.SyncAbilities) mask |= 4;
            _renderUnitDebugMask = mask;
        }

        public static RenderUnitDebugFlags GetRenderUnitDebugFlags()
        {
            var mask = _renderUnitDebugMask;
            return new RenderUnitDebugFlags(
                syncTopActivity: (mask & 1) != 0,
                syncActivities: (mask & 2) != 0,
                syncAbilities: (mask & 4) != 0);
        }

        public static void SetWorld(World world)
        {
            _world = world;
        }

        public static World TryGetWorld()
        {
            return _world;
        }
    }
}
