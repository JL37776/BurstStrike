using System;
using System.Collections.Generic;
using System.Text;
using Game.Scripts.Fixed;
using UnityEngine;

namespace Game.Command
{
    /// <summary>
    /// High-level helpers to construct gameplay commands.
    ///
    /// Notes:
    /// - This factory doesn't deal with networking/serialization/compression.
    /// - IDs are logic entity ids (not Unity instance ids) unless you later decide otherwise.
    /// - All fixed-point positions are provided as FixedVector3.
    /// </summary>
    public static class CommandFactory
    {
        /// <summary>
        /// If true, CommandFactory will Debug.Log every command created/encoded/decoded.
        /// Useful for diagnosing operator->world command flow.
        /// </summary>
        public static bool EnableCommandLogging = false;

        /// <summary>
        /// Optional: override log sink (defaults to Debug.Log).
        /// </summary>
        public static Action<string> CommandLogSink;

        private static void LogCommand(string prefix, in Command cmd)
        {
            if (!EnableCommandLogging) return;
            var sink = CommandLogSink;
            var msg = $"[{nameof(CommandFactory)}] {prefix}: {Format(cmd)}";
            if (sink != null) sink(msg);
            else Debug.Log(msg);
        }

        private static string Format(in Command cmd)
        {
            // Keep it allocation-light but readable.
            var sb = new StringBuilder(128);
            sb.Append(cmd.Type).Append("/").Append(cmd.Payload);

            if (cmd.UnitIds != null)
            {
                sb.Append(" units[");
                // Print head only to avoid log spam.
                const int head = 8;
                int n = cmd.UnitIds.Length;
                int m = Math.Min(n, head);
                for (int i = 0; i < m; i++)
                {
                    if (i != 0) sb.Append(',');
                    sb.Append(cmd.UnitIds[i]);
                }
                if (n > head) sb.Append("...");
                sb.Append(']');
            }

            if (cmd.TargetId != 0) sb.Append(" target=").Append(cmd.TargetId);

            // Note: FixedVector3.ToString() should be stable enough for debug.
            if (cmd.Payload == PayloadKind.UnitsPoint || cmd.Payload == PayloadKind.Build)
                sb.Append(" point=").Append(cmd.Point);

            if (cmd.Payload == PayloadKind.UnitsWaypoints)
            {
                int count = cmd.Waypoints?.Length ?? 0;
                sb.Append(" waypoints=").Append(count);
                if (count > 0 && cmd.Waypoints != null) sb.Append(" first=").Append(cmd.Waypoints[0]);
            }

            if (cmd.Payload == PayloadKind.Build)
                sb.Append(" builder=").Append(cmd.Int0).Append(" buildingType=").Append(cmd.Int1);

            return sb.ToString();
        }

        public static Command Move(int[] unitIds, FixedVector3 dest)
        {
            var cmd = Command.CreateUnitsPoint(CommandType.UnitMove, unitIds, dest);
            LogCommand("Create", cmd);
            return cmd;
        }

        public static Command MoveAttack(int[] unitIds, FixedVector3 dest)
        {
            var cmd = Command.CreateUnitsPoint(CommandType.UnitMoveAttack, unitIds, dest);
            LogCommand("Create", cmd);
            return cmd;
        }

        public static Command ForceAttack(int[] unitIds, FixedVector3 dest)
        {
            var cmd = Command.CreateUnitsPoint(CommandType.UnitForceAttackPoint, unitIds, dest);
            LogCommand("Create", cmd);
            return cmd;
        }

        public static Command AttackUnit(int[] unitIds, int targetUnitId)
        {
            var cmd = Command.CreateUnitsTarget(CommandType.UnitAttackUnit, unitIds, targetUnitId);
            LogCommand("Create", cmd);
            return cmd;
        }

        public static Command ForceAttackUnit(int[] unitIds, int targetUnitId)
        {
            var cmd = Command.CreateUnitsTarget(CommandType.UnitForceAttackUnit, unitIds, targetUnitId);
            LogCommand("Create", cmd);
            return cmd;
        }

        public static Command Patrol(int[] unitIds, FixedVector3[] waypoints)
        {
            if (waypoints == null) throw new ArgumentNullException(nameof(waypoints));
            var cmd = Command.CreateUnitsWaypoints(CommandType.UnitPatrol, unitIds, waypoints);
            LogCommand("Create", cmd);
            return cmd;
        }

        public static Command Build(int builderUnitId, int buildingTypeId, FixedVector3 pos)
        {
            var cmd = Command.CreateBuild(builderUnitId, buildingTypeId, pos);
            LogCommand("Create", cmd);
            return cmd;
        }

        public static Command DestroyBuilding(int buildingEntityId)
        {
            var cmd = Command.CreateDestroy(buildingEntityId);
            LogCommand("Create", cmd);
            return cmd;
        }

        /// <summary>
        /// Convenience: add all commands to a buffer.
        /// (Useful for operators/controllers.)
        /// </summary>
        public static void Add(CommandBuffer buffer, in Command cmd)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            buffer.Add(cmd);
        }

        public static void AddRange(CommandBuffer buffer, IReadOnlyList<Command> cmds)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (cmds == null) return;
            for (int i = 0; i < cmds.Count; i++) buffer.Add(cmds[i]);
        }

        public static Command Move(int[] unitIds, Vector3 dest)
        {
            return Move(unitIds, FixedVector3.FromUnity(dest));
        }

        public static Command MoveAttack(int[] unitIds, Vector3 dest)
        {
            return MoveAttack(unitIds, FixedVector3.FromUnity(dest));
        }

        public static Command ForceAttack(int[] unitIds, Vector3 dest)
        {
            return ForceAttack(unitIds, FixedVector3.FromUnity(dest));
        }

        public static Command Patrol(int[] unitIds, Vector3[] waypoints)
        {
            return Patrol(unitIds, ToFixedArray(waypoints));
        }

        public static Command Build(int builderUnitId, int buildingTypeId, Vector3 pos)
        {
            return Build(builderUnitId, buildingTypeId, FixedVector3.FromUnity(pos));
        }

        private static FixedVector3[] ToFixedArray(Vector3[] vs)
        {
            if (vs == null) throw new ArgumentNullException(nameof(vs));
            var arr = new FixedVector3[vs.Length];
            for (int i = 0; i < vs.Length; i++) arr[i] = FixedVector3.FromUnity(vs[i]);
            return arr;
        }

        /// <summary>
        /// Encode a command into a compact byte array suitable for network transport.
        /// </summary>
        public static byte[] Encode(in Command cmd)
        {
            LogCommand("Encode", cmd);
            return CommandCodec.Encode(cmd);
        }

        /// <summary>
        /// Decode a command from a received byte buffer.
        /// </summary>
        public static bool TryDecode(ReadOnlySpan<byte> data, out Command cmd)
        {
            var ok = CommandCodec.TryDecode(data, out cmd);
            if (ok) LogCommand("Decode", cmd);
            return ok;
        }

        private static int _localSequence;

        /// <summary>
        /// Stamp deterministic ordering info onto a command.
        /// - tick: 0 means execute ASAP (legacy behavior)
        /// - sequence: if 0, an auto-incrementing local sequence is assigned
        /// </summary>
        public static Command WithOrder(in Command cmd, int tick, int sequence = 0)
        {
            var c = cmd;
            c.Tick = tick;
            c.Sequence = sequence != 0 ? sequence : System.Threading.Interlocked.Increment(ref _localSequence);
            return c;
        }

        /// <summary>
        /// Spawn a single unit.
        /// Encodes:
        /// - TargetId: unitId
        /// - Int0: archetypeId
        /// - Int1: playerId
        /// - Int2: factionId
        /// - Point: spawn position
        /// - (colorIndex): derived from playerId (stable palette)
        /// 
        /// Note: color is not sent as floats for determinism; receivers map playerId->color via PlayerPalette.
        /// </summary>
        public static Command SpawnUnit(int unitId, int archetypeId, int playerId, int factionId, FixedVector3 spawnPos)
        {
            if (unitId <= 0) throw new ArgumentOutOfRangeException(nameof(unitId));
            if (archetypeId <= 0) throw new ArgumentOutOfRangeException(nameof(archetypeId));

            var cmd = Command.Create(CommandType.UnitSpawn);
            cmd.Payload = PayloadKind.SpawnUnit;
            cmd.TargetId = unitId;
            cmd.Int0 = archetypeId;
            cmd.Int1 = playerId;
            cmd.Int2 = factionId;
            cmd.Point = spawnPos;
            LogCommand("Create", cmd);
            return cmd;
        }

        public static Command SpawnUnit(int unitId, int archetypeId, int playerId, int factionId, Vector3 spawnPos)
        {
            return SpawnUnit(unitId, archetypeId, playerId, factionId, FixedVector3.FromUnity(spawnPos));
        }
    }
}
