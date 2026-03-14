using UnityEngine;
using Game.Map.Terrain;

namespace Game.Map
{
    /// <summary>
    /// 根据 TerrainMapData 生成地形网格（实心 Mesh + 线框叠加）。
    /// 调用 Rebuild 即可刷新。不持有数据，仅负责渲染。
    /// </summary>
    public sealed class MapGridRenderer : MonoBehaviour
    {
        private MeshFilter   _solidFilter;
        private MeshRenderer _solidRenderer;
        private MeshCollider _solidCollider;

        private MeshFilter   _wireFilter;
        private MeshRenderer _wireRenderer;

        private Material _solidMat;
        private Material _wireMat;

        private TerrainShaderProfile _activeProfile;

        /// <summary>渲染细分等级 (1=原始, 2=每格4面, 4=每格16面)</summary>
        public int SubdivisionLevel { get; set; } = 1;

        // ───────────── Catmull-Rom 插值 ─────────────

        private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
        {
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t
            );
        }

        /// <summary>从逻辑高度图中用 Catmull-Rom 采样平滑高度</summary>
        private static float SampleSmooth(float[] map, int w, int h, float fx, float fy)
        {
            int ix = Mathf.FloorToInt(fx);
            int iy = Mathf.FloorToInt(fy);
            float tx = fx - ix;
            float ty = fy - iy;

            float[] row = new float[4];
            for (int j = 0; j < 4; j++)
            {
                int sy = Mathf.Clamp(iy - 1 + j, 0, h - 1);
                float c0 = map[sy * w + Mathf.Clamp(ix - 1, 0, w - 1)];
                float c1 = map[sy * w + Mathf.Clamp(ix,     0, w - 1)];
                float c2 = map[sy * w + Mathf.Clamp(ix + 1, 0, w - 1)];
                float c3 = map[sy * w + Mathf.Clamp(ix + 2, 0, w - 1)];
                row[j] = CatmullRom(c0, c1, c2, c3, tx);
            }
            return CatmullRom(row[0], row[1], row[2], row[3], ty);
        }

        // ═══════════════ Shader Profile 系统 ═══════════════

        /// <summary>当前活跃的 shader 配置档</summary>
        public TerrainShaderProfile ActiveProfile => _activeProfile;

        /// <summary>
        /// 切换 shader。加载 profile 指定的 shader 并创建新材质。
        /// </summary>
        public void ApplyShaderProfile(TerrainShaderProfile profile)
        {
            if (profile == null) return;
            _activeProfile = profile;

            var shader = Resources.Load<Shader>(profile.ShaderResourceName);
            if (shader == null)
            {
                Debug.LogError($"MapGridRenderer: 找不到 Shader: Resources/{profile.ShaderResourceName}");
                return;
            }

            EnsureComponents();

            if (_solidMat != null) Destroy(_solidMat);
            _solidMat = new Material(shader);
            if (_solidRenderer != null)
                _solidRenderer.sharedMaterial = _solidMat;
        }

        /// <summary>通用设置材质参数（贴图）</summary>
        public void SetShaderTexture(string propertyName, Texture2D tex)
        {
            if (_solidMat == null || !_solidMat.HasProperty(propertyName)) return;
            _solidMat.SetTexture(propertyName, tex);
        }

        /// <summary>通用设置材质参数（浮点）</summary>
        public void SetShaderFloat(string propertyName, float value)
        {
            if (_solidMat == null || !_solidMat.HasProperty(propertyName)) return;
            _solidMat.SetFloat(propertyName, value);
        }

        /// <summary>通用设置材质参数（颜色）</summary>
        public void SetShaderColor(string propertyName, Color color)
        {
            if (_solidMat == null || !_solidMat.HasProperty(propertyName)) return;
            _solidMat.SetColor(propertyName, color);
        }

        // ═══════════════ 旧接口（向后兼容）═══════════════

        /// <summary>设置指定层的贴图。layer: 0=Top, 1=Cliff, 2=Bottom</summary>
        public void SetLayerTexture(int layer, Texture2D tex)
        {
            switch (layer)
            {
                case 0: SetShaderTexture("_TexTop",    tex); break;
                case 1: SetShaderTexture("_TexCliff",  tex); break;
                case 2: SetShaderTexture("_TexBottom", tex); break;
            }
        }

        /// <summary>设置指定层的贴图平铺密度。layer: 0=Top, 1=Cliff, 2=Bottom</summary>
        public void SetLayerTiling(int layer, float tiling)
        {
            switch (layer)
            {
                case 0: SetShaderFloat("_TilingTop",    tiling); break;
                case 1: SetShaderFloat("_TilingCliff",  tiling); break;
                case 2: SetShaderFloat("_TilingBottom", tiling); break;
            }
        }

        /// <summary>
        /// 从 TerrainMapData 重建网格。
        /// </summary>
        public void Rebuild(TerrainMapData data)
        {
            if (data == null) return;
            EnsureComponents();
            BuildSolidMesh(data);
            BuildWireMesh(data);
        }

        /// <summary>清除网格</summary>
        public void Clear()
        {
            if (_solidFilter != null && _solidFilter.sharedMesh != null)
            {
                Destroy(_solidFilter.sharedMesh);
                _solidFilter.sharedMesh = null;
            }
            if (_wireFilter != null && _wireFilter.sharedMesh != null)
            {
                Destroy(_wireFilter.sharedMesh);
                _wireFilter.sharedMesh = null;
            }
        }

        private void EnsureComponents()
        {
            // ── 实心地面 ──
            if (_solidFilter == null)
            {
                var solidGo = new GameObject("TerrainSolid");
                solidGo.transform.SetParent(transform, false);
                _solidFilter = solidGo.AddComponent<MeshFilter>();
                _solidRenderer = solidGo.AddComponent<MeshRenderer>();

                _solidMat = new Material(Resources.Load<Shader>("TerrainShader/TerrainSolid"));
                _solidRenderer.sharedMaterial = _solidMat;
                _solidCollider = solidGo.AddComponent<MeshCollider>();
            }

            // ── 线框叠加 ──
            if (_wireFilter == null)
            {
                var wireGo = new GameObject("TerrainWire");
                wireGo.transform.SetParent(transform, false);
                _wireFilter = wireGo.AddComponent<MeshFilter>();
                _wireRenderer = wireGo.AddComponent<MeshRenderer>();

                _wireMat = new Material(Resources.Load<Shader>("Shader/TerrainWire"));
                _wireMat.color = new Color(0.4f, 0.5f, 0.4f, 0.35f);
                _wireRenderer.sharedMaterial = _wireMat;
            }
        }

        // ───────────── 实心 Mesh ─────────────

        private void BuildSolidMesh(TerrainMapData data)
        {
            int w = data.Width;
            int h = data.Height;
            float s = data.VertexSpacing;
            int sub = Mathf.Max(1, SubdivisionLevel);

            // 渲染网格尺寸
            int rw = (w - 1) * sub + 1;
            int rh = (h - 1) * sub + 1;
            float rs = s / sub;

            int vertCount = rw * rh;
            int quadCount = (rw - 1) * (rh - 1);

            var verts    = new Vector3[vertCount];
            var normals  = new Vector3[vertCount];
            var tangents = new Vector4[vertCount];
            var uvs      = new Vector2[vertCount];
            var colors   = new Color[vertCount];
            var tris     = new int[quadCount * 6];

            float invStep = 1f / sub;

            for (int rz = 0; rz < rh; rz++)
            for (int rx = 0; rx < rw; rx++)
            {
                int vi = rz * rw + rx;

                // 渲染顶点在逻辑网格中的浮点坐标
                float fx = rx * invStep;
                float fz = rz * invStep;

                float height;
                float splatTop, splatCliff, splatBottom;

                if (sub == 1)
                {
                    int i = rz * w + rx;
                    height      = data.Heightmap[i];
                    splatTop    = data.SplatTop[i];
                    splatCliff  = data.SplatCliff[i];
                    splatBottom = data.SplatBottom[i];
                }
                else
                {
                    height      = SampleSmooth(data.Heightmap,   w, h, fx, fz);
                    splatTop    = Mathf.Clamp01(SampleSmooth(data.SplatTop,    w, h, fx, fz));
                    splatCliff  = Mathf.Clamp01(SampleSmooth(data.SplatCliff,  w, h, fx, fz));
                    splatBottom = Mathf.Clamp01(SampleSmooth(data.SplatBottom, w, h, fx, fz));

                    float sum = splatTop + splatCliff + splatBottom;
                    if (sum > 0.001f)
                    {
                        float inv = 1f / sum;
                        splatTop *= inv; splatCliff *= inv; splatBottom *= inv;
                    }
                    else
                    {
                        splatTop = 1f; splatCliff = 0f; splatBottom = 0f;
                    }
                }

                float worldX = fx * s;
                float worldZ = fz * s;
                verts[vi] = new Vector3(worldX, height, worldZ);
                uvs[vi]   = new Vector2(fx / (w - 1), fz / (h - 1));

                // 法线：用渲染网格步长做中心差分
                float hl, hr, hd, hu;
                if (sub == 1)
                {
                    int ix = rx, iz = rz;
                    hl = data.Heightmap[iz * w + Mathf.Max(ix - 1, 0)];
                    hr = data.Heightmap[iz * w + Mathf.Min(ix + 1, w - 1)];
                    hd = data.Heightmap[Mathf.Max(iz - 1, 0) * w + ix];
                    hu = data.Heightmap[Mathf.Min(iz + 1, h - 1) * w + ix];
                }
                else
                {
                    hl = SampleSmooth(data.Heightmap, w, h, fx - invStep, fz);
                    hr = SampleSmooth(data.Heightmap, w, h, fx + invStep, fz);
                    hd = SampleSmooth(data.Heightmap, w, h, fx, fz - invStep);
                    hu = SampleSmooth(data.Heightmap, w, h, fx, fz + invStep);
                }

                var n = new Vector3((hl - hr) / (2f * rs), 1f, (hd - hu) / (2f * rs));
                normals[vi] = n.normalized;

                var tg = new Vector3(1f, (hr - hl) / (2f * rs), 0f).normalized;
                tangents[vi] = new Vector4(tg.x, tg.y, tg.z, 1f);

                float avgNeighbor = (hl + hr + hd + hu) * 0.25f;
                float ao = Mathf.Clamp01(1f - Mathf.Max(0f, avgNeighbor - height) * 0.3f);
                colors[vi] = new Color(splatTop, splatCliff, splatBottom, ao);
            }

            int ti = 0;
            for (int rz = 0; rz < rh - 1; rz++)
            for (int rx = 0; rx < rw - 1; rx++)
            {
                int bl = rz * rw + rx;
                int br = bl + 1;
                int tl = bl + rw;
                int tr = tl + 1;
                tris[ti++] = bl; tris[ti++] = tl; tris[ti++] = br;
                tris[ti++] = br; tris[ti++] = tl; tris[ti++] = tr;
            }

            if (_solidFilter.sharedMesh != null)
                Destroy(_solidFilter.sharedMesh);

            var mesh = new Mesh { name = "TerrainSolid" };
            if (vertCount > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices  = verts;
            mesh.normals   = normals;
            mesh.tangents  = tangents;
            mesh.uv        = uvs;
            mesh.colors    = colors;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            _solidFilter.sharedMesh = mesh;
            _solidCollider.sharedMesh = mesh;
        }

        // ───────────── 线框 Mesh (Lines) ─────────────

        private void BuildWireMesh(TerrainMapData data)
        {
            int w = data.Width;
            int h = data.Height;
            float s = data.VertexSpacing;

            int vertCount = w * h;
            var verts = new Vector3[vertCount];
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                verts[i] = new Vector3(x * s, data.Heightmap[i] + 0.02f, z * s);
            }

            // 水平线 + 垂直线
            int hLineCount = h * (w - 1);       // 每行 w-1 条线段
            int vLineCount = w * (h - 1);       // 每列 h-1 条线段
            var indices = new int[(hLineCount + vLineCount) * 2];

            int idx = 0;
            // 水平
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w - 1; x++)
            {
                indices[idx++] = z * w + x;
                indices[idx++] = z * w + x + 1;
            }
            // 垂直
            for (int z = 0; z < h - 1; z++)
            for (int x = 0; x < w; x++)
            {
                indices[idx++] = z * w + x;
                indices[idx++] = (z + 1) * w + x;
            }

            if (_wireFilter.sharedMesh != null)
                Destroy(_wireFilter.sharedMesh);

            var mesh = new Mesh { name = "TerrainWire" };
            if (vertCount > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            _wireFilter.sharedMesh = mesh;
        }

        private void OnDestroy()
        {
            Clear();
            if (_solidMat != null) Destroy(_solidMat);
            if (_wireMat != null) Destroy(_wireMat);
        }
    }
}
