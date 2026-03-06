using System;
using System.Buffers.Binary;
using Game.Scripts.Fixed;

namespace Game.Command
{
    /// <summary>
    /// Binary codec for <see cref="Command"/>.
    ///
    /// Goals:
    /// - Compact enough for networking.
    /// - Fast encode/decode.
    /// - Keep payload strictly fixed-point/int based.
    ///
    /// Format (little endian):
    ///   u8  type
    ///   u8  payloadKind
    ///   u16 reserved (alignment/version flags)
    ///   payload...
    ///
    /// Payloads:
    /// - None:
    ///     (nothing)
    /// - UnitsPoint:
    ///     varint unitCount
    ///     varint unitId * count
    ///     i32 rawX, rawY, rawZ
    /// - UnitsTarget:
    ///     varint unitCount
    ///     varint unitId * count
    ///     varint targetId
    /// - UnitsWaypoints:
    ///     varint unitCount
    ///     varint unitId * count
    ///     varint waypointCount
    ///     i32 rawX,rawY,rawZ * waypointCount
    /// - Target:
    ///     varint targetId
    /// - Build:
    ///     varint builderUnitId
    ///     varint buildingTypeId
    ///     i32 rawX,rawY,rawZ
    /// - SpawnUnit:
    ///     varint targetId
    ///     varint archetypeId
    ///     varint playerId
    ///     varint factionId
    ///     i32 rawX,rawY,rawZ
    ///
    /// varint uses unsigned LEB128 with zigzag for signed when needed (we only use unsigned here).
    /// </summary>
    public static class CommandCodec
    {
        public static byte[] Encode(in Command cmd)
        {
            // Likely small; grow if needed.
            var writer = new Writer(64);
            writer.WriteU8((byte)cmd.Type);
            writer.WriteU8((byte)cmd.Payload);

            // v1: use the previously-reserved u16 as a small header for ordering/scheduling.
            // Layout: [u8 tickDelta] [u8 sequence]
            // - tickDelta: 0 => execute ASAP (legacy behavior)
            // - sequence: tie-break within the same tick
            // Note: this is intentionally compact; if you need large tick values later, add a v2.
            byte tickDelta = 0;
            if (cmd.Tick > 0)
            {
                // Clamp to 255 to keep the header compact.
                // (Deterministic ordering still holds as long as you don't exceed this window.)
                tickDelta = (byte)Math.Clamp(cmd.Tick, 0, 255);
            }
            byte seq = (byte)Math.Clamp(cmd.Sequence, 0, 255);
            writer.WriteU8(tickDelta);
            writer.WriteU8(seq);

            switch (cmd.Payload)
            {
                case PayloadKind.None:
                    break;

                case PayloadKind.UnitsPoint:
                    WriteUnitIds(ref writer, cmd.UnitIds);
                    WritePoint(ref writer, cmd.Point);
                    break;

                case PayloadKind.SpawnUnit:
                    // SpawnUnit payload: TargetId(unitId), Int0(archetypeId), Int1(playerId), Int2(factionId), Point(spawnPos)
                    writer.WriteVarUInt((uint)cmd.TargetId);
                    writer.WriteVarUInt((uint)cmd.Int0);
                    writer.WriteVarUInt((uint)cmd.Int1);
                    writer.WriteVarUInt((uint)cmd.Int2);
                    WritePoint(ref writer, cmd.Point);
                    break;

                case PayloadKind.UnitsTarget:
                    WriteUnitIds(ref writer, cmd.UnitIds);
                    writer.WriteVarUInt((uint)cmd.TargetId);
                    break;

                case PayloadKind.UnitsWaypoints:
                    WriteUnitIds(ref writer, cmd.UnitIds);
                    WriteWaypoints(ref writer, cmd.Waypoints);
                    break;

                case PayloadKind.Target:
                    writer.WriteVarUInt((uint)cmd.TargetId);
                    break;

                case PayloadKind.Build:
                    writer.WriteVarUInt((uint)cmd.Int0);
                    writer.WriteVarUInt((uint)cmd.Int1);
                    WritePoint(ref writer, cmd.Point);
                    break;

                default:
                    throw new NotSupportedException($"Unsupported payload kind: {cmd.Payload}");
            }

            return writer.ToArray();
        }

        public static bool TryDecode(ReadOnlySpan<byte> data, out Command cmd)
        {
            cmd = default;
            if (data.Length < 4) return false;

            var reader = new Reader(data);
            var type = (CommandType)reader.ReadU8();
            var payload = (PayloadKind)reader.ReadU8();

            // v1 header (was reserved u16).
            // For backward compatibility, if old clients filled it with 0 this still works.
            var tickDelta = reader.ReadU8();
            var seq = reader.ReadU8();

            cmd.Type = type;
            cmd.Payload = payload;
            cmd.Tick = tickDelta; // interpreted as absolute tick by current game code
            cmd.Sequence = seq;

            try
            {
                switch (payload)
                {
                    case PayloadKind.None:
                        return reader.IsAtEnd;

                    case PayloadKind.UnitsPoint:
                        cmd.UnitIds = ReadUnitIds(ref reader);
                        cmd.Point = ReadPoint(ref reader);
                        return reader.IsAtEnd;

                    case PayloadKind.SpawnUnit:
                        cmd.TargetId = (int)reader.ReadVarUInt();
                        cmd.Int0 = (int)reader.ReadVarUInt();
                        cmd.Int1 = (int)reader.ReadVarUInt();
                        cmd.Int2 = (int)reader.ReadVarUInt();
                        cmd.Point = ReadPoint(ref reader);
                        return reader.IsAtEnd;

                    case PayloadKind.UnitsTarget:
                        cmd.UnitIds = ReadUnitIds(ref reader);
                        cmd.TargetId = (int)reader.ReadVarUInt();
                        return reader.IsAtEnd;

                    case PayloadKind.UnitsWaypoints:
                        cmd.UnitIds = ReadUnitIds(ref reader);
                        cmd.Waypoints = ReadWaypoints(ref reader);
                        return reader.IsAtEnd;

                    case PayloadKind.Target:
                        cmd.TargetId = (int)reader.ReadVarUInt();
                        return reader.IsAtEnd;

                    case PayloadKind.Build:
                        cmd.Int0 = (int)reader.ReadVarUInt();
                        cmd.Int1 = (int)reader.ReadVarUInt();
                        cmd.Point = ReadPoint(ref reader);
                        return reader.IsAtEnd;

                    default:
                        return false;
                }
            }
            catch
            {
                cmd = default;
                return false;
            }
        }

        private static void WriteUnitIds(ref Writer w, int[] unitIds)
        {
            if (unitIds == null) throw new ArgumentNullException(nameof(unitIds));
            w.WriteVarUInt((uint)unitIds.Length);
            for (int i = 0; i < unitIds.Length; i++)
                w.WriteVarUInt((uint)unitIds[i]);
        }

        private static int[] ReadUnitIds(ref Reader r)
        {
            var count = (int)r.ReadVarUInt();
            if (count < 0 || count > 1_000_000) throw new InvalidOperationException("unitId count out of range");
            var arr = new int[count];
            for (int i = 0; i < count; i++) arr[i] = (int)r.ReadVarUInt();
            return arr;
        }

        private static void WritePoint(ref Writer w, FixedVector3 p)
        {
            w.WriteI32(p.x.Raw);
            w.WriteI32(p.y.Raw);
            w.WriteI32(p.z.Raw);
        }

        private static FixedVector3 ReadPoint(ref Reader r)
        {
            var x = Fixed.FromRaw(r.ReadI32());
            var y = Fixed.FromRaw(r.ReadI32());
            var z = Fixed.FromRaw(r.ReadI32());
            return new FixedVector3(x, y, z);
        }

        private static void WriteWaypoints(ref Writer w, FixedVector3[] points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            w.WriteVarUInt((uint)points.Length);
            for (int i = 0; i < points.Length; i++) WritePoint(ref w, points[i]);
        }

        private static FixedVector3[] ReadWaypoints(ref Reader r)
        {
            var count = (int)r.ReadVarUInt();
            if (count < 0 || count > 1_000_000) throw new InvalidOperationException("waypoint count out of range");
            var arr = new FixedVector3[count];
            for (int i = 0; i < count; i++) arr[i] = ReadPoint(ref r);
            return arr;
        }

        private ref struct Reader
        {
            private ReadOnlySpan<byte> _data;
            private int _pos;

            public Reader(ReadOnlySpan<byte> data)
            {
                _data = data;
                _pos = 0;
            }

            public bool IsAtEnd => _pos == _data.Length;

            public byte ReadU8()
            {
                if (_pos + 1 > _data.Length) throw new IndexOutOfRangeException();
                return _data[_pos++];
            }

            public ushort ReadU16()
            {
                if (_pos + 2 > _data.Length) throw new IndexOutOfRangeException();
                ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(_pos, 2));
                _pos += 2;
                return v;
            }

            public int ReadI32()
            {
                if (_pos + 4 > _data.Length) throw new IndexOutOfRangeException();
                int v = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(_pos, 4));
                _pos += 4;
                return v;
            }

            public uint ReadVarUInt()
            {
                // LEB128
                uint result = 0;
                int shift = 0;
                while (true)
                {
                    if (shift >= 35) throw new InvalidOperationException("varint too long");
                    byte b = ReadU8();
                    result |= (uint)(b & 0x7F) << shift;
                    if ((b & 0x80) == 0) break;
                    shift += 7;
                }
                return result;
            }
        }

        private struct Writer
        {
            private byte[] _buf;
            private int _pos;

            public Writer(int initialCapacity)
            {
                _buf = new byte[Math.Max(16, initialCapacity)];
                _pos = 0;
            }

            public void WriteU8(byte v)
            {
                Ensure(1);
                _buf[_pos++] = v;
            }

            public void WriteU16(ushort v)
            {
                Ensure(2);
                BinaryPrimitives.WriteUInt16LittleEndian(_buf.AsSpan(_pos, 2), v);
                _pos += 2;
            }

            public void WriteI32(int v)
            {
                Ensure(4);
                BinaryPrimitives.WriteInt32LittleEndian(_buf.AsSpan(_pos, 4), v);
                _pos += 4;
            }

            public void WriteVarUInt(uint value)
            {
                // LEB128
                while (value >= 0x80)
                {
                    WriteU8((byte)(value | 0x80));
                    value >>= 7;
                }
                WriteU8((byte)value);
            }

            public byte[] ToArray()
            {
                var outv = new byte[_pos];
                Buffer.BlockCopy(_buf, 0, outv, 0, _pos);
                return outv;
            }

            private void Ensure(int more)
            {
                if (_pos + more <= _buf.Length) return;
                int newSize = _buf.Length * 2;
                while (_pos + more > newSize) newSize *= 2;
                Array.Resize(ref _buf, newSize);
            }
        }
    }
}
