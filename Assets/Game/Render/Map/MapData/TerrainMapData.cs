using System;

namespace Game.Map.Terrain
{
    /// <summary>
    /// 地形地图的纯数据容器。
    /// <para>
    /// 存储内容：
    /// - Heightmap：每顶点高度（float），行优先 [z * Width + x]
    /// - Splatmap：每顶点 3 通道权重 (Top / Cliff / Bottom)，归一化
    /// </para>
    /// <para>
    /// Width/Height 为顶点数。实际格子数为 (Width-1)×(Height-1)。
    /// 世界坐标映射：顶点 (x,z) → 世界 (x * VertexSpacing, Heights[i], z * VertexSpacing)
    /// </para>
    /// <para>
    /// 职责边界：仅数据 + 修改/查询接口。不负责渲染、不负责 Mesh 生成。
    /// </para>
    /// </summary>
    public sealed class TerrainMapData : IDisposable
    {
        // ──────────────────── 元信息 ────────────────────
        public int   Width         { get; }
        public int   Height        { get; }
        public float VertexSpacing { get; }

        // ──────────────────── 数据缓冲 ────────────────────
        /// <summary>每顶点高度，长度 = Width * Height</summary>
        public float[] Heightmap { get; }

        /// <summary>Top 权重 (朝上法线面)</summary>
        public float[] SplatTop { get; }

        /// <summary>Cliff 权重 (近垂直面)</summary>
        public float[] SplatCliff { get; }

        /// <summary>Bottom 权重 (朝下法线面)</summary>
        public float[] SplatBottom { get; }

        public bool IsDisposed { get; private set; }

        // ──────────────────── 构造 ────────────────────

        /// <summary>
        /// 创建一张空白地形图。高度全 0，splat 全部为 Top。
        /// </summary>
        /// <param name="width">X 方向顶点数（≥2）</param>
        /// <param name="height">Z 方向顶点数（≥2）</param>
        /// <param name="vertexSpacing">顶点间世界距离</param>
        public TerrainMapData(int width, int height, float vertexSpacing = 1f)
        {
            if (width < 2 || height < 2)
                throw new ArgumentException("Terrain size must be >= 2 on each axis.");
            if (vertexSpacing <= 0f)
                throw new ArgumentException("VertexSpacing must be > 0.");

            Width         = width;
            Height        = height;
            VertexSpacing = vertexSpacing;

            int count = width * height;
            Heightmap   = new float[count];
            SplatTop    = new float[count];
            SplatCliff  = new float[count];
            SplatBottom = new float[count];

            // 默认：全部 Top 权重 = 1
            Array.Fill(SplatTop, 1f);
        }

        /// <summary>内部反序列化用构造（直接接收缓冲区）</summary>
        internal TerrainMapData(int width, int height, float vertexSpacing,
            float[] heightmap, float[] splatTop, float[] splatCliff, float[] splatBottom)
        {
            Width         = width;
            Height        = height;
            VertexSpacing = vertexSpacing;
            Heightmap     = heightmap;
            SplatTop      = splatTop;
            SplatCliff    = splatCliff;
            SplatBottom   = splatBottom;
        }

        // ──────────────────── 索引 ────────────────────

        public int Index(int x, int z)
        {
            return z * Width + x;
        }

        private void CheckBounds(int x, int z)
        {
            if ((uint)x >= (uint)Width || (uint)z >= (uint)Height)
                throw new ArgumentOutOfRangeException($"({x},{z}) out of [{Width},{Height})");
        }

        // ──────────────────── 高度读写 ────────────────────

        public float GetHeight(int x, int z)
        {
            CheckBounds(x, z);
            return Heightmap[Index(x, z)];
        }

        public void SetHeight(int x, int z, float h)
        {
            CheckBounds(x, z);
            Heightmap[Index(x, z)] = h;
        }

        /// <summary>
        /// 双线性插值采样世界坐标高度。
        /// </summary>
        public float SampleHeight(float worldX, float worldZ)
        {
            float fx = worldX / VertexSpacing;
            float fz = worldZ / VertexSpacing;

            int x0 = Math.Clamp((int)fx, 0, Width - 2);
            int z0 = Math.Clamp((int)fz, 0, Height - 2);

            float tx = fx - x0;
            float tz = fz - z0;

            float h00 = Heightmap[Index(x0,     z0)];
            float h10 = Heightmap[Index(x0 + 1, z0)];
            float h01 = Heightmap[Index(x0,     z0 + 1)];
            float h11 = Heightmap[Index(x0 + 1, z0 + 1)];

            float h0 = h00 + (h10 - h00) * tx;
            float h1 = h01 + (h11 - h01) * tx;
            return h0 + (h1 - h0) * tz;
        }

        // ──────────────────── Splat 读写 ────────────────────

        /// <summary>设置单顶点的 splat 权重（自动归一化）。</summary>
        public void SetSplat(int x, int z, float top, float cliff, float bottom)
        {
            CheckBounds(x, z);
            float sum = top + cliff + bottom;
            if (sum < 1e-6f) { top = 1f; sum = 1f; }
            float inv = 1f / sum;

            int i = Index(x, z);
            SplatTop[i]    = top    * inv;
            SplatCliff[i]  = cliff  * inv;
            SplatBottom[i] = bottom * inv;
        }

        /// <summary>
        /// 根据高度图的局部法线自动生成 splat 权重。
        /// </summary>
        public void AutoGenerateSplat(float topThreshold = 0.7f, float bottomThreshold = 0.3f)
        {
            for (int z = 0; z < Height; z++)
            for (int x = 0; x < Width; x++)
            {
                float ny = CalculateNormalY(x, z);

                float top    = Math.Max(0f, (ny - topThreshold) / (1f - topThreshold + 1e-6f));
                float bottom = Math.Max(0f, (-ny - bottomThreshold) / (1f - bottomThreshold + 1e-6f));
                float cliff  = Math.Max(0f, 1f - top - bottom);

                SetSplat(x, z, top, cliff, bottom);
            }
        }

        // ──────────────────── 法线计算 ────────────────────

        /// <summary>计算顶点法线的 Y 分量（用于 splat 判定）。</summary>
        public float CalculateNormalY(int x, int z)
        {
            float l = Heightmap[Index(Math.Max(x - 1, 0),         z)];
            float r = Heightmap[Index(Math.Min(x + 1, Width - 1), z)];
            float d = Heightmap[Index(x, Math.Max(z - 1, 0))];
            float u = Heightmap[Index(x, Math.Min(z + 1, Height - 1))];

            float dx = (l - r) / (2f * VertexSpacing);
            float dz = (d - u) / (2f * VertexSpacing);

            // normalize and return y component
            float lenSq = dx * dx + 1f + dz * dz;
            return 1f / MathF.Sqrt(lenSq);
        }

        // ──────────────────── 区域操作 ────────────────────

        /// <summary>
        /// 用平坦高度填充整个地图。
        /// </summary>
        public void FlatFill(float height)
        {
            Array.Fill(Heightmap, height);
        }

        /// <summary>
        /// 将矩形区域内的高度设置为指定值。
        /// </summary>
        public void FillRect(int x0, int z0, int x1, int z1, float height)
        {
            x0 = Math.Clamp(x0, 0, Width  - 1);
            x1 = Math.Clamp(x1, 0, Width  - 1);
            z0 = Math.Clamp(z0, 0, Height - 1);
            z1 = Math.Clamp(z1, 0, Height - 1);

            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                Heightmap[Index(x, z)] = height;
        }

        // ──────────────────── Dispose ────────────────────

        public void Dispose()
        {
            IsDisposed = true;
            // 托管数组无需显式释放，标记即可
        }
    }
}
