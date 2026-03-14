using System.Collections.Generic;
using UnityEngine;

namespace Game.Map
{
    /// <summary>Shader 参数类型</summary>
    public enum ShaderParamType
    {
        Texture2D,
        Float,
        Range,
        Color,
    }

    /// <summary>
    /// 描述一个 shader 属性，用于 UI 自动生成控件。
    /// </summary>
    public sealed class ShaderParamDescriptor
    {
        public string         PropertyName;   // shader 中的属性名，如 "_TexTop"
        public string         DisplayName;    // UI 显示名，如 "Top 贴图"
        public ShaderParamType Type;
        public float          Min, Max, Default;  // Float/Range 用
        public string         Group;          // 分组名，如 "Top层"、"全局"

        public static ShaderParamDescriptor Tex(string prop, string display, string group = "")
            => new ShaderParamDescriptor
            {
                PropertyName = prop, DisplayName = display,
                Type = ShaderParamType.Texture2D, Group = group
            };

        public static ShaderParamDescriptor FloatParam(string prop, string display,
            float min, float max, float def, string group = "")
            => new ShaderParamDescriptor
            {
                PropertyName = prop, DisplayName = display,
                Type = ShaderParamType.Float, Min = min, Max = max, Default = def, Group = group
            };

        public static ShaderParamDescriptor RangeParam(string prop, string display,
            float min, float max, float def, string group = "")
            => new ShaderParamDescriptor
            {
                PropertyName = prop, DisplayName = display,
                Type = ShaderParamType.Range, Min = min, Max = max, Default = def, Group = group
            };

        public static ShaderParamDescriptor ColorParam(string prop, string display, string group = "")
            => new ShaderParamDescriptor
            {
                PropertyName = prop, DisplayName = display,
                Type = ShaderParamType.Color, Group = group
            };
    }

    /// <summary>
    /// 地形 Shader 配置档，描述一个 shader 的元信息和所有可调参数。
    /// </summary>
    public sealed class TerrainShaderProfile
    {
        public string Name;                          // 显示名称
        public string ShaderResourceName;            // Resources 下的 shader 路径（不含扩展名）
        public List<ShaderParamDescriptor> Params;   // 所有可调参数

        // ════════════════════════════════════════════
        //  内置配置档工厂
        // ════════════════════════════════════════════

        /// <summary>基础 Shader — 3 层贴图 + Tiling + Tint + Triplanar</summary>
        public static TerrainShaderProfile CreateBasic()
        {
            return new TerrainShaderProfile
            {
                Name = "基础 (Basic)",
                ShaderResourceName = "TerrainShader/TerrainSolid",
                Params = new List<ShaderParamDescriptor>
                {
                    // Top 层
                    ShaderParamDescriptor.Tex("_TexTop",       "Top 贴图",    "Top 层"),
                    ShaderParamDescriptor.FloatParam("_TilingTop", "Top 缩放", 0.5f, 30f, 4f, "Top 层"),
                    ShaderParamDescriptor.ColorParam("_ColorTop",  "Top 色调",  "Top 层"),

                    // Cliff 层
                    ShaderParamDescriptor.Tex("_TexCliff",       "Cliff 贴图",    "Cliff 层"),
                    ShaderParamDescriptor.FloatParam("_TilingCliff", "Cliff 缩放", 0.5f, 30f, 4f, "Cliff 层"),
                    ShaderParamDescriptor.ColorParam("_ColorCliff",  "Cliff 色调",  "Cliff 层"),

                    // Bottom 层
                    ShaderParamDescriptor.Tex("_TexBottom",       "Bottom 贴图",    "Bottom 层"),
                    ShaderParamDescriptor.FloatParam("_TilingBottom", "Bottom 缩放", 0.5f, 30f, 4f, "Bottom 层"),
                    ShaderParamDescriptor.ColorParam("_ColorBottom",  "Bottom 色调",  "Bottom 层"),

                    // 全局
                    ShaderParamDescriptor.RangeParam("_Ambient", "环境光", 0f, 1f, 0.45f, "全局"),
                    ShaderParamDescriptor.RangeParam("_TriplanarSharpness", "三向投射锐度", 1f, 8f, 4f, "全局"),
                },
            };
        }

        /// <summary>高级 Shader — 法线贴图 + Height Blend + 多倍频 + 顶点AO</summary>
        public static TerrainShaderProfile CreateAdvanced()
        {
            return new TerrainShaderProfile
            {
                Name = "高级 (Advanced)",
                ShaderResourceName = "TerrainShader/TerrainAdvanced",
                Params = new List<ShaderParamDescriptor>
                {
                    // Top 层
                    ShaderParamDescriptor.Tex("_TexTop",       "Top 贴图",      "Top 层"),
                    ShaderParamDescriptor.Tex("_NormTop",      "Top 法线",      "Top 层"),
                    ShaderParamDescriptor.FloatParam("_TilingTop", "Top 缩放", 0.5f, 30f, 4f, "Top 层"),
                    ShaderParamDescriptor.ColorParam("_ColorTop",  "Top 色调", "Top 层"),

                    // Cliff 层
                    ShaderParamDescriptor.Tex("_TexCliff",       "Cliff 贴图",      "Cliff 层"),
                    ShaderParamDescriptor.Tex("_NormCliff",      "Cliff 法线",      "Cliff 层"),
                    ShaderParamDescriptor.FloatParam("_TilingCliff", "Cliff 缩放", 0.5f, 30f, 4f, "Cliff 层"),
                    ShaderParamDescriptor.ColorParam("_ColorCliff",  "Cliff 色调", "Cliff 层"),

                    // Bottom 层
                    ShaderParamDescriptor.Tex("_TexBottom",       "Bottom 贴图",      "Bottom 层"),
                    ShaderParamDescriptor.Tex("_NormBottom",      "Bottom 法线",      "Bottom 层"),
                    ShaderParamDescriptor.FloatParam("_TilingBottom", "Bottom 缩放", 0.5f, 30f, 4f, "Bottom 层"),
                    ShaderParamDescriptor.ColorParam("_ColorBottom",  "Bottom 色调", "Bottom 层"),

                    // 混合
                    ShaderParamDescriptor.RangeParam("_BlendSharpness", "混合锐度", 0.01f, 1f, 0.15f, "混合"),
                    ShaderParamDescriptor.RangeParam("_MacroScale", "远景缩放比", 0.05f, 0.5f, 0.15f, "混合"),
                    ShaderParamDescriptor.RangeParam("_MacroStrength", "远景强度", 0f, 1f, 0.4f, "混合"),

                    // 光照
                    ShaderParamDescriptor.RangeParam("_Ambient", "环境光", 0f, 1f, 0.4f, "光照"),
                    ShaderParamDescriptor.RangeParam("_NormalStrength", "法线强度", 0f, 2f, 1f, "光照"),
                    ShaderParamDescriptor.RangeParam("_SpecPower", "高光锐度", 2f, 64f, 16f, "光照"),
                    ShaderParamDescriptor.RangeParam("_SpecStrength", "高光强度", 0f, 1f, 0.3f, "光照"),
                    ShaderParamDescriptor.RangeParam("_AOStrength", "AO 强度", 0f, 2f, 0.8f, "光照"),
                    ShaderParamDescriptor.RangeParam("_TriplanarSharpness", "三向投射锐度", 1f, 8f, 4f, "光照"),
                },
            };
        }

        /// <summary>
        /// 从 Shader 对象自动生成 profile（用于未手动注册的 shader）。
        /// 通过反射 shader properties 自动创建 UI 描述。
        /// </summary>
        public static TerrainShaderProfile CreateFromShader(Shader shader, string resourcePath)
        {
            var parms = new List<ShaderParamDescriptor>();
            int count = shader.GetPropertyCount();

            for (int i = 0; i < count; i++)
            {
                string propName = shader.GetPropertyName(i);
                string displayName = shader.GetPropertyDescription(i);
                var propType = shader.GetPropertyType(i);

                // 跳过内部属性
                if (propName.StartsWith("_unity_", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                switch (propType)
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        parms.Add(ShaderParamDescriptor.Tex(propName, displayName));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                        parms.Add(ShaderParamDescriptor.FloatParam(propName, displayName,
                            0f, 100f, shader.GetPropertyDefaultFloatValue(i)));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        var range = shader.GetPropertyRangeLimits(i);
                        parms.Add(ShaderParamDescriptor.RangeParam(propName, displayName,
                            range.x, range.y, shader.GetPropertyDefaultFloatValue(i)));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        parms.Add(ShaderParamDescriptor.ColorParam(propName, displayName));
                        break;
                }
            }

            // 用 shader 名称的最后一段作为显示名
            string name = shader.name;
            int lastSlash = name.LastIndexOf('/');
            if (lastSlash >= 0) name = name.Substring(lastSlash + 1);

            return new TerrainShaderProfile
            {
                Name = name,
                ShaderResourceName = resourcePath,
                Params = parms,
            };
        }
    }

    /// <summary>
    /// Shader 注册表 — 自动扫描 Resources/TerrainShader/ 下所有 shader。
    /// 已知 shader 使用手动 profile（有精调的分组和中文名），其余自动生成。
    /// </summary>
    public static class TerrainShaderRegistry
    {
        private static List<TerrainShaderProfile> _profiles;

        // 已知 shader 的手动 profile 映射 (shader 文件名 → 工厂方法)
        private static readonly Dictionary<string, System.Func<TerrainShaderProfile>> BuiltInProfiles
            = new Dictionary<string, System.Func<TerrainShaderProfile>>
            {
                { "TerrainSolid",    TerrainShaderProfile.CreateBasic },
                { "TerrainAdvanced", TerrainShaderProfile.CreateAdvanced },
            };

        public static List<TerrainShaderProfile> All
        {
            get
            {
                if (_profiles == null)
                    ScanShaders();
                return _profiles;
            }
        }

        /// <summary>扫描 Resources/TerrainShader/ 并构建 profile 列表</summary>
        private static void ScanShaders()
        {
            _profiles = new List<TerrainShaderProfile>();

            // 加载 TerrainShader 文件夹下所有 Shader
            var shaders = Resources.LoadAll<Shader>("TerrainShader");
            var added = new HashSet<string>();

            // 先添加已知 shader（保持顺序：Basic 在前）
            string[] order = { "TerrainSolid", "TerrainAdvanced" };
            foreach (var key in order)
            {
                if (BuiltInProfiles.TryGetValue(key, out var factory))
                {
                    _profiles.Add(factory());
                    added.Add(key);
                }
            }

            // 再添加文件夹中其余的 shader（自动生成 profile）
            foreach (var shader in shaders)
            {
                // 从 shader.name 提取文件名部分
                string shaderName = shader.name;
                int lastSlash = shaderName.LastIndexOf('/');
                string fileName = lastSlash >= 0 ? shaderName.Substring(lastSlash + 1) : shaderName;

                if (added.Contains(fileName)) continue;
                added.Add(fileName);

                string resourcePath = "TerrainShader/" + fileName;
                _profiles.Add(TerrainShaderProfile.CreateFromShader(shader, resourcePath));
            }
        }

        /// <summary>重新扫描（用于热重载）</summary>
        public static void Refresh()
        {
            _profiles = null;
        }

        /// <summary>注册自定义 Shader 配置档</summary>
        public static void Register(TerrainShaderProfile profile)
        {
            if (_profiles == null) ScanShaders();
            _profiles.Add(profile);
        }

        public static TerrainShaderProfile GetByName(string name)
        {
            foreach (var p in All)
                if (p.Name == name) return p;
            return null;
        }
    }
}
