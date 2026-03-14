using System;
using System.IO;

namespace Game.Map.Terrain
{
    /// <summary>
    /// TerrainMapData 的二进制序列化器。
    /// <para>
    /// 文件格式 (.tmap)：
    /// <code>
    /// [4B ] Signature   "TMAP" (0x50414D54)
    /// [4B ] Version     uint32
    /// [4B ] Width       int32   (顶点数)
    /// [4B ] Height      int32   (顶点数)
    /// [4B ] VertexSpacing float32
    /// [W*H*4B] Heightmap    float32[]
    /// [W*H*4B] SplatTop     float32[]
    /// [W*H*4B] SplatCliff   float32[]
    /// [W*H*4B] SplatBottom  float32[]
    /// </code>
    /// </para>
    /// <para>
    /// 总大小 = 20 + W*H*16 字节。
    /// 例：256×256 地图 = 20 + 65536*16 = ~1 MB。
    /// </para>
    /// <para>
    /// 也支持从 byte[] 读写，用于网络传输。
    /// </para>
    /// </summary>
    public static class TerrainMapSerializer
    {
        // ──────────────────── 文件读写 ────────────────────

        /// <summary>保存到磁盘文件。</summary>
        public static void SaveToFile(TerrainMapData data, string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var fs = File.Create(path);
            using var bw = new BinaryWriter(fs);
            WriteToStream(bw, data);
        }

        /// <summary>从磁盘文件加载。</summary>
        public static TerrainMapData LoadFromFile(string path)
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            return ReadFromStream(br);
        }

        // ──────────────────── byte[] 读写（网络传输用）────────────────────

        /// <summary>序列化为 byte[]。</summary>
        public static byte[] SerializeToBytes(TerrainMapData data)
        {
            int count = data.Width * data.Height;
            int size  = 20 + count * 16; // header 20B + 4 arrays * count * 4B

            using var ms = new MemoryStream(size);
            using var bw = new BinaryWriter(ms);
            WriteToStream(bw, data);
            return ms.ToArray();
        }

        /// <summary>从 byte[] 反序列化。</summary>
        public static TerrainMapData DeserializeFromBytes(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var br = new BinaryReader(ms);
            return ReadFromStream(br);
        }

        /// <summary>从 byte[] 的指定偏移反序列化（零拷贝读取）。</summary>
        public static TerrainMapData DeserializeFromBytes(byte[] bytes, int offset, int length)
        {
            using var ms = new MemoryStream(bytes, offset, length, writable: false);
            using var br = new BinaryReader(ms);
            return ReadFromStream(br);
        }

        // ──────────────────── 内部实现 ────────────────────

        private static void WriteToStream(BinaryWriter bw, TerrainMapData data)
        {
            bw.Write(TerrainConstants.FileSignature);
            bw.Write(TerrainConstants.FileVersion);
            bw.Write(data.Width);
            bw.Write(data.Height);
            bw.Write(data.VertexSpacing);

            int count = data.Width * data.Height;
            WriteFloats(bw, data.Heightmap,   count);
            WriteFloats(bw, data.SplatTop,    count);
            WriteFloats(bw, data.SplatCliff,  count);
            WriteFloats(bw, data.SplatBottom, count);
        }

        private static TerrainMapData ReadFromStream(BinaryReader br)
        {
            uint sig = br.ReadUInt32();
            if (sig != TerrainConstants.FileSignature)
                throw new InvalidDataException(
                    $"Invalid .tmap signature: 0x{sig:X8}, expected 0x{TerrainConstants.FileSignature:X8}");

            uint ver = br.ReadUInt32();
            if (ver > TerrainConstants.FileVersion)
                throw new InvalidDataException(
                    $"Unsupported .tmap version {ver}, max supported: {TerrainConstants.FileVersion}");

            int   w       = br.ReadInt32();
            int   h       = br.ReadInt32();
            float spacing = br.ReadSingle();

            if (w < 2 || h < 2)
                throw new InvalidDataException($"Invalid terrain size: {w}x{h}");

            int count = w * h;
            var heightmap   = ReadFloats(br, count);
            var splatTop    = ReadFloats(br, count);
            var splatCliff  = ReadFloats(br, count);
            var splatBottom = ReadFloats(br, count);

            return new TerrainMapData(w, h, spacing, heightmap, splatTop, splatCliff, splatBottom);
        }

        private static void WriteFloats(BinaryWriter bw, float[] arr, int count)
        {
            // 利用 BlockCopy 批量转为字节写入，避免逐元素调用
            var bytes = new byte[count * 4];
            Buffer.BlockCopy(arr, 0, bytes, 0, bytes.Length);
            bw.Write(bytes);
        }

        private static float[] ReadFloats(BinaryReader br, int count)
        {
            var bytes = br.ReadBytes(count * 4);
            if (bytes.Length != count * 4)
                throw new InvalidDataException("Unexpected end of .tmap data");

            var arr = new float[count];
            Buffer.BlockCopy(bytes, 0, arr, 0, bytes.Length);
            return arr;
        }
    }
}
