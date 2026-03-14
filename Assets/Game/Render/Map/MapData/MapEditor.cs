using System;

namespace Game.Map.Terrain
{
    /// <summary>
    /// 地图编辑器核心类。
    /// <para>
    /// 职责边界：
    /// - 内存中持有一份可编辑的 <see cref="TerrainMapData"/>
    /// - 提供点操作 / 笔刷面操作 API（高度、Splat）
    /// - 所有修改暂存内存，直到显式调用 Save
    /// - 通过 <see cref="GetCurrentMapData"/> 暴露当前数据给外部（渲染层等）
    /// </para>
    /// <para>
    /// 不负责：渲染、Mesh 生成、UI 交互。
    /// 上层类调用这些 API 来实现具体编辑功能。
    /// </para>
    /// </summary>
    public sealed class MapEditor : IDisposable
    {
        private TerrainMapData _data;
        private bool _dirty;

        /// <summary>当前是否有未保存的修改</summary>
        public bool IsDirty => _dirty;

        /// <summary>地图是否已加载/创建</summary>
        public bool HasData => _data != null && !_data.IsDisposed;

        // ════════════════════════════════════════════════════
        //  生命周期
        // ════════════════════════════════════════════════════

        /// <summary>创建一张新的空白地图。</summary>
        public void CreateNew(int width, int height, float vertexSpacing = 1f)
        {
            _data?.Dispose();
            _data  = new TerrainMapData(width, height, vertexSpacing);
            _dirty = true;
        }

        /// <summary>从文件加载地图到内存。</summary>
        public void LoadFromFile(string path)
        {
            _data?.Dispose();
            _data  = TerrainMapSerializer.LoadFromFile(path);
            _dirty = false;
        }

        /// <summary>从 byte[] 加载（网络接收后）。</summary>
        public void LoadFromBytes(byte[] bytes)
        {
            _data?.Dispose();
            _data  = TerrainMapSerializer.DeserializeFromBytes(bytes);
            _dirty = false;
        }

        /// <summary>保存到文件，清除 dirty 标记。</summary>
        public void SaveToFile(string path)
        {
            EnsureData();
            TerrainMapSerializer.SaveToFile(_data, path);
            _dirty = false;
        }

        /// <summary>序列化为 byte[]（用于网络发送），不清除 dirty。</summary>
        public byte[] SerializeToBytes()
        {
            EnsureData();
            return TerrainMapSerializer.SerializeToBytes(_data);
        }

        /// <summary>
        /// 获取当前内存中的地图数据（只读引用）。
        /// 渲染层通过此方法拿到数据来构建 Mesh。
        /// </summary>
        public TerrainMapData GetCurrentMapData()
        {
            EnsureData();
            return _data;
        }

        // ════════════════════════════════════════════════════
        //  点操作 — 高度
        // ════════════════════════════════════════════════════

        /// <summary>设置单个顶点的高度。</summary>
        public void SetHeight(int x, int z, float height)
        {
            EnsureData();
            _data.SetHeight(x, z, height);
            _dirty = true;
        }

        /// <summary>在单个顶点的当前高度上叠加增量。</summary>
        public void AddHeight(int x, int z, float delta)
        {
            EnsureData();
            float cur = _data.GetHeight(x, z);
            _data.SetHeight(x, z, cur + delta);
            _dirty = true;
        }

        // ════════════════════════════════════════════════════
        //  点操作 — Splat
        // ════════════════════════════════════════════════════

        /// <summary>设置单个顶点的 splat 权重（自动归一化）。</summary>
        public void SetSplat(int x, int z, float top, float cliff, float bottom)
        {
            EnsureData();
            _data.SetSplat(x, z, top, cliff, bottom);
            _dirty = true;
        }

        // ════════════════════════════════════════════════════
        //  笔刷面操作 — 高度
        // ════════════════════════════════════════════════════

        /// <summary>
        /// 笔刷设置高度：将笔刷范围内的顶点高度设为指定值，按衰减插值。
        /// 衰减为 0 的地方保持原值，衰减为 1 的地方完全变为 targetHeight。
        /// </summary>
        public void BrushSetHeight(in BrushParams brush, float targetHeight)
        {
            EnsureData();
            ForEachBrushVertex(brush, (x, z, weight) =>
            {
                float cur = _data.GetHeight(x, z);
                _data.SetHeight(x, z, cur + (targetHeight - cur) * weight);
            });
            _dirty = true;
        }

        /// <summary>
        /// 笔刷叠加高度：在笔刷范围内的顶点高度上叠加增量（乘以衰减权重）。
        /// </summary>
        public void BrushAddHeight(in BrushParams brush, float delta)
        {
            EnsureData();
            ForEachBrushVertex(brush, (x, z, weight) =>
            {
                float cur = _data.GetHeight(x, z);
                _data.SetHeight(x, z, cur + delta * weight);
            });
            _dirty = true;
        }

        /// <summary>
        /// 笔刷平滑：将笔刷范围内每个顶点的高度向其邻居均值靠拢。
        /// brush.Strength 控制平滑力度。
        /// </summary>
        public void BrushSmooth(in BrushParams brush)
        {
            EnsureData();
            int w = _data.Width;
            int h = _data.Height;

            // 先对当前高度做一份快照，避免读写同一帧数据
            // 只快照笔刷 AABB 区域
            int minX = Math.Clamp(brush.CenterX - brush.Radius, 0, w - 1);
            int maxX = Math.Clamp(brush.CenterX + brush.Radius, 0, w - 1);
            int minZ = Math.Clamp(brush.CenterZ - brush.Radius, 0, h - 1);
            int maxZ = Math.Clamp(brush.CenterZ + brush.Radius, 0, h - 1);

            int snapW = maxX - minX + 1;
            int snapH = maxZ - minZ + 1;
            var snapshot = new float[snapW * snapH];
            for (int sz = 0; sz < snapH; sz++)
            for (int sx = 0; sx < snapW; sx++)
                snapshot[sz * snapW + sx] = _data.Heightmap[_data.Index(minX + sx, minZ + sz)];

            ForEachBrushVertex(brush, (x, z, weight) =>
            {
                // 计算邻居均值（从快照读）
                float sum   = 0f;
                int   count = 0;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx;
                    int nz = z + dz;
                    if (nx < 0 || nx >= w || nz < 0 || nz >= h) continue;

                    // 如果在快照范围内就从快照读，否则从原数据读
                    if (nx >= minX && nx <= maxX && nz >= minZ && nz <= maxZ)
                        sum += snapshot[(nz - minZ) * snapW + (nx - minX)];
                    else
                        sum += _data.Heightmap[_data.Index(nx, nz)];
                    count++;
                }

                float avg = sum / count;
                float cur = snapshot[(z - minZ) * snapW + (x - minX)];
                _data.SetHeight(x, z, cur + (avg - cur) * weight);
            });
            _dirty = true;
        }

        /// <summary>
        /// 笔刷压平：将笔刷范围内的顶点高度拉向笔刷中心点的高度。
        /// </summary>
        public void BrushFlatten(in BrushParams brush)
        {
            EnsureData();
            float centerH = _data.GetHeight(
                Math.Clamp(brush.CenterX, 0, _data.Width - 1),
                Math.Clamp(brush.CenterZ, 0, _data.Height - 1));
            BrushSetHeight(brush, centerH);
        }

        // ════════════════════════════════════════════════════
        //  笔刷面操作 — Splat
        // ════════════════════════════════════════════════════

        /// <summary>
        /// 笔刷绘制 splat：在笔刷范围内将 splat 权重向目标值混合。
        /// weight 越大的地方越接近目标值。
        /// </summary>
        public void BrushPaintSplat(in BrushParams brush, float top, float cliff, float bottom)
        {
            EnsureData();

            // 先归一化目标
            float sum = top + cliff + bottom;
            if (sum < 1e-6f) { top = 1f; sum = 1f; }
            float inv = 1f / sum;
            top    *= inv;
            cliff  *= inv;
            bottom *= inv;

            ForEachBrushVertex(brush, (x, z, weight) =>
            {
                int i = _data.Index(x, z);
                float curTop    = _data.SplatTop[i];
                float curCliff  = _data.SplatCliff[i];
                float curBottom = _data.SplatBottom[i];

                float newTop    = curTop    + (top    - curTop)    * weight;
                float newCliff  = curCliff  + (cliff  - curCliff)  * weight;
                float newBottom = curBottom + (bottom - curBottom) * weight;

                _data.SetSplat(x, z, newTop, newCliff, newBottom);
            });
            _dirty = true;
        }

        /// <summary>
        /// 根据当前高度图的法线自动生成整张地图的 splat 权重。
        /// </summary>
        public void AutoGenerateSplat(float topThreshold = 0.7f, float bottomThreshold = 0.3f)
        {
            EnsureData();
            _data.AutoGenerateSplat(topThreshold, bottomThreshold);
            _dirty = true;
        }

        // ════════════════════════════════════════════════════
        //  矩形区域操作
        // ════════════════════════════════════════════════════

        /// <summary>全图填充为统一高度。</summary>
        public void FlatFill(float height)
        {
            EnsureData();
            _data.FlatFill(height);
            _dirty = true;
        }

        /// <summary>矩形区域设置为统一高度。</summary>
        public void RectSetHeight(int x0, int z0, int x1, int z1, float height)
        {
            EnsureData();
            _data.FillRect(x0, z0, x1, z1, height);
            _dirty = true;
        }

        /// <summary>矩形区域叠加高度增量。</summary>
        public void RectAddHeight(int x0, int z0, int x1, int z1, float delta)
        {
            EnsureData();
            ClampRect(ref x0, ref z0, ref x1, ref z1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int i = _data.Index(x, z);
                _data.Heightmap[i] += delta;
            }
            _dirty = true;
        }

        /// <summary>矩形区域设置 splat 权重。</summary>
        public void RectSetSplat(int x0, int z0, int x1, int z1,
            float top, float cliff, float bottom)
        {
            EnsureData();
            ClampRect(ref x0, ref z0, ref x1, ref z1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                _data.SetSplat(x, z, top, cliff, bottom);
            _dirty = true;
        }

        // ════════════════════════════════════════════════════
        //  查询
        // ════════════════════════════════════════════════════

        /// <summary>获取单顶点高度。</summary>
        public float GetHeight(int x, int z)
        {
            EnsureData();
            return _data.GetHeight(x, z);
        }

        /// <summary>世界坐标双线性插值高度。</summary>
        public float SampleHeight(float worldX, float worldZ)
        {
            EnsureData();
            return _data.SampleHeight(worldX, worldZ);
        }

        // ════════════════════════════════════════════════════
        //  内部工具
        // ════════════════════════════════════════════════════

        /// <summary>
        /// 遍历笔刷范围内的所有有效顶点，并对每个顶点调用 action(x, z, weight)。
        /// weight = brush.Strength × falloff(distance)，范围 [0,1]。
        /// </summary>
        private void ForEachBrushVertex(in BrushParams brush, Action<int, int, float> action)
        {
            int w = _data.Width;
            int h = _data.Height;
            int r = brush.Radius;

            int minX = Math.Max(brush.CenterX - r, 0);
            int maxX = Math.Min(brush.CenterX + r, w - 1);
            int minZ = Math.Max(brush.CenterZ - r, 0);
            int maxZ = Math.Min(brush.CenterZ + r, h - 1);

            float rFloat = Math.Max(r, 1);

            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                int dx = x - brush.CenterX;
                int dz = z - brush.CenterZ;

                // 计算归一化距离 [0,1]
                float dist;
                switch (brush.Shape)
                {
                    case BrushShape.Circle:
                        dist = MathF.Sqrt(dx * dx + dz * dz) / rFloat;
                        break;
                    case BrushShape.Square:
                        dist = Math.Max(Math.Abs(dx), Math.Abs(dz)) / rFloat;
                        break;
                    default:
                        dist = MathF.Sqrt(dx * dx + dz * dz) / rFloat;
                        break;
                }

                // 圆形笔刷超出半径的点跳过
                if (dist > 1f) continue;

                // 计算衰减
                float falloff;
                switch (brush.Falloff)
                {
                    case BrushFalloff.Constant:
                        falloff = 1f;
                        break;
                    case BrushFalloff.Linear:
                        falloff = 1f - dist;
                        break;
                    case BrushFalloff.Smooth:
                        // smoothstep: 3t²-2t³，其中 t = 1-dist
                        float t = 1f - dist;
                        falloff = t * t * (3f - 2f * t);
                        break;
                    default:
                        falloff = 1f;
                        break;
                }

                float weight = brush.Strength * falloff;
                if (weight <= 0f) continue;

                action(x, z, weight);
            }
        }

        private void ClampRect(ref int x0, ref int z0, ref int x1, ref int z1)
        {
            x0 = Math.Clamp(x0, 0, _data.Width  - 1);
            x1 = Math.Clamp(x1, 0, _data.Width  - 1);
            z0 = Math.Clamp(z0, 0, _data.Height - 1);
            z1 = Math.Clamp(z1, 0, _data.Height - 1);
            if (x0 > x1) (x0, x1) = (x1, x0);
            if (z0 > z1) (z0, z1) = (z1, z0);
        }

        private void EnsureData()
        {
            if (_data == null || _data.IsDisposed)
                throw new InvalidOperationException("MapEditor: no map data loaded. Call CreateNew or Load first.");
        }

        public void Dispose()
        {
            _data?.Dispose();
            _data = null;
        }
    }
}
