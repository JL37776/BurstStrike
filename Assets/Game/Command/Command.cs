using System;
using Game.Scripts.Fixed;

namespace Game.Command
{
    /// <summary>
    /// Data-only command emitted by an operator/controller and later executed by gameplay systems.
    ///
    /// Design goals (current stage):
    /// - Easy to create/use in gameplay.
    /// - No transport/compression concerns yet.
    /// - 'World gives what it has' (no frame ownership in the command).
    ///
    /// Performance notes:
    /// - This struct is an envelope; some payloads are reference types (int[]/FixedVector3[]).
    ///   That's OK for now. Later we can introduce pooling or a packed/binary form for networking.
    /// </summary>
    [Serializable]
    public struct Command
    {
        public CommandType Type;
        public PayloadKind Payload;

        /// <summary>
        /// General-purpose ids:
        /// - For unit group commands: UnitIds holds selection
        /// - For target commands: TargetId holds target entity id
        /// - For destroy: TargetId holds building entity id
        /// </summary>
        public int TargetId;

        /// <summary>
        /// Selection/group for unit commands.
        /// (Can be null for single-entity commands.)
        /// </summary>
        public int[] UnitIds;

        /// <summary>
        /// Fixed-point position payload.
        /// </summary>
        public FixedVector3 Point;

        /// <summary>
        /// Waypoints for patrol.
        /// </summary>
        public FixedVector3[] Waypoints;

        /// <summary>
        /// Extra ints for build and future extensions.
        /// Build: Int0 = builderUnitId, Int1 = buildingTypeId
        /// </summary>
        public int Int0;
        public int Int1;
        public int Int2;

        /// <summary>
        /// Optional deterministic ordering/scheduling fields for lockstep/networking.
        /// Tick: which logic tick this command is intended to execute on.
        /// Sequence: deterministic tie-break within the same tick.
        /// 
        /// Default (0) means "execute ASAP" (current behavior).
        /// </summary>
        public int Tick;
        public int Sequence;

        public static Command Create(CommandType type)
        {
            return new Command { Type = type, Payload = PayloadKind.None };
        }

        public static Command CreateUnitsPoint(CommandType type, int[] unitIds, FixedVector3 dest)
        {
            if (unitIds == null || unitIds.Length == 0)
                throw new ArgumentException("unitIds must not be null or empty", nameof(unitIds));

            return new Command
            {
                Type = type,
                Payload = PayloadKind.UnitsPoint,
                UnitIds = unitIds,
                Point = dest,
            };
        }

        public static Command CreateUnitsTarget(CommandType type, int[] unitIds, int targetId)
        {
            if (unitIds == null || unitIds.Length == 0)
                throw new ArgumentException("unitIds must not be null or empty", nameof(unitIds));
            if (targetId == 0)
                throw new ArgumentException("targetId must not be 0", nameof(targetId));

            return new Command
            {
                Type = type,
                Payload = PayloadKind.UnitsTarget,
                UnitIds = unitIds,
                TargetId = targetId,
            };
        }

        public static Command CreateUnitsWaypoints(CommandType type, int[] unitIds, FixedVector3[] waypoints)
        {
            if (unitIds == null || unitIds.Length == 0)
                throw new ArgumentException("unitIds must not be null or empty", nameof(unitIds));
            if (waypoints == null || waypoints.Length == 0)
                throw new ArgumentException("waypoints must not be null or empty", nameof(waypoints));

            return new Command
            {
                Type = type,
                Payload = PayloadKind.UnitsWaypoints,
                UnitIds = unitIds,
                Waypoints = waypoints,
            };
        }

        public static Command CreateBuild(int builderUnitId, int buildingTypeId, FixedVector3 pos)
        {
            if (builderUnitId == 0) throw new ArgumentException("builderUnitId must not be 0", nameof(builderUnitId));
            if (buildingTypeId == 0) throw new ArgumentException("buildingTypeId must not be 0", nameof(buildingTypeId));

            return new Command
            {
                Type = CommandType.Build,
                Payload = PayloadKind.Build,
                Int0 = builderUnitId,
                Int1 = buildingTypeId,
                Point = pos,
            };
        }

        public static Command CreateDestroy(int buildingEntityId)
        {
            if (buildingEntityId == 0) throw new ArgumentException("buildingEntityId must not be 0", nameof(buildingEntityId));

            return new Command
            {
                Type = CommandType.Destroy,
                Payload = PayloadKind.Target,
                TargetId = buildingEntityId,
            };
        }
    }

    public enum PayloadKind : byte
    {
        None = 0,
        UnitsPoint = 1,
        UnitsTarget = 2,
        UnitsWaypoints = 3,
        Target = 4,
        Build = 5,
        SpawnUnit = 6,
    }

    public enum CommandType : byte
    {
        None = 0,

        // Unit group commands
        UnitMove = 10,
        UnitMoveAttack = 11,
        UnitForceAttackPoint = 12,

        UnitAttackUnit = 20,
        UnitForceAttackUnit = 21,

        UnitPatrol = 30,

        // Build system
        Build = 40,
        Destroy = 41,

        // Misc
        Stop = 50,

        // Spawning
        UnitSpawn = 60,
    }
}
