Shader "MapEditor/TerrainAdvanced" {
    Properties {
        // ── 贴图 ──
        _TexTop      ("Top Texture",     2D) = "white" {}
        _NormTop     ("Top Normal",      2D) = "bump"  {}
        _TexCliff    ("Cliff Texture",   2D) = "white" {}
        _NormCliff   ("Cliff Normal",    2D) = "bump"  {}
        _TexBottom   ("Bottom Texture",  2D) = "white" {}
        _NormBottom  ("Bottom Normal",   2D) = "bump"  {}

        // ── Tiling ──
        _TilingTop    ("Top Tiling",    Float) = 4.0
        _TilingCliff  ("Cliff Tiling",  Float) = 4.0
        _TilingBottom ("Bottom Tiling", Float) = 4.0

        // ── Tint ──
        _ColorTop    ("Top Tint",    Color) = (1,1,1,1)
        _ColorCliff  ("Cliff Tint",  Color) = (1,1,1,1)
        _ColorBottom ("Bottom Tint", Color) = (1,1,1,1)

        // ── 混合 ──
        _BlendSharpness ("Blend Sharpness", Range(0.01, 1)) = 0.15
        _MacroScale     ("Macro Scale",     Range(0.05, 0.5)) = 0.15
        _MacroStrength  ("Macro Strength",  Range(0, 1)) = 0.4

        // ── 光照 ──
        _Ambient        ("Ambient",         Range(0, 1)) = 0.4
        _NormalStrength ("Normal Strength",  Range(0, 2)) = 1.0
        _SpecPower      ("Spec Power",       Range(2, 64)) = 16
        _SpecStrength   ("Spec Strength",    Range(0, 1)) = 0.3
        _AOStrength     ("AO Strength",      Range(0, 2)) = 0.8
        _TriplanarSharpness ("Triplanar Sharpness", Range(1, 8)) = 4.0
    }
    SubShader {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _TexTop, _NormTop;
            sampler2D _TexCliff, _NormCliff;
            sampler2D _TexBottom, _NormBottom;
            float _TilingTop, _TilingCliff, _TilingBottom;
            fixed4 _ColorTop, _ColorCliff, _ColorBottom;
            float _BlendSharpness, _MacroScale, _MacroStrength;
            float _Ambient, _NormalStrength, _SpecPower, _SpecStrength, _AOStrength;
            float _TriplanarSharpness;

            struct appdata {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                float4 tangent : TANGENT;
                float4 color   : COLOR;
            };

            struct v2f {
                float4 pos       : SV_POSITION;
                float3 worldPos  : TEXCOORD0;
                float3 worldNorm : TEXCOORD1;
                float3 tSpace0   : TEXCOORD2;
                float3 tSpace1   : TEXCOORD3;
                float3 tSpace2   : TEXCOORD4;
                float4 splat     : COLOR;
            };

            v2f vert(appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNorm = UnityObjectToWorldNormal(v.normal);
                o.splat = v.color;

                float3 wN = o.worldNorm;
                float3 wT = UnityObjectToWorldDir(v.tangent.xyz);
                float3 wB = cross(wN, wT) * v.tangent.w;
                o.tSpace0 = float3(wT.x, wB.x, wN.x);
                o.tSpace1 = float3(wT.y, wB.y, wN.y);
                o.tSpace2 = float3(wT.z, wB.z, wN.z);
                return o;
            }

            // ── Triplanar 采样 ──
            half4 TriSample(sampler2D tex, float3 wp, float3 tw, float tiling) {
                half4 xP = tex2D(tex, wp.zy * tiling);
                half4 yP = tex2D(tex, wp.xz * tiling);
                half4 zP = tex2D(tex, wp.xy * tiling);
                return xP * tw.x + yP * tw.y + zP * tw.z;
            }

            // ── Triplanar 法线采样 (世界空间 swizzle) ──
            half3 TriSampleNormal(sampler2D normMap, float3 wp, float3 tw, float tiling) {
                half3 tnX = UnpackNormal(tex2D(normMap, wp.zy * tiling));
                half3 tnY = UnpackNormal(tex2D(normMap, wp.xz * tiling));
                half3 tnZ = UnpackNormal(tex2D(normMap, wp.xy * tiling));
                // 重新排列各投影面的法线分量到世界空间
                half3 nX = half3(tnX.z, tnX.y, tnX.x); // YZ面 → 世界X轴为主
                half3 nY = half3(tnY.x, tnY.z, tnY.y); // XZ面 → 世界Y轴为主
                half3 nZ = half3(tnZ.x, tnZ.y, tnZ.z); // XY面 → 世界Z轴为主
                half3 result = nX * tw.x + nY * tw.y + nZ * tw.z;
                result.xy *= _NormalStrength;
                return normalize(result);
            }

            // ── Triplanar + 多倍频 ──
            half4 TriSampleMacro(sampler2D tex, float3 wp, float3 tw, float tiling) {
                half4 detail = TriSample(tex, wp, tw, tiling);
                half4 macro  = TriSample(tex, wp, tw, tiling * _MacroScale);
                return detail * (1.0 - _MacroStrength) + detail * macro * 2.0 * _MacroStrength;
            }

            // ── Height-based blend ──
            float3 HeightBlend(float3 splat, float hTop, float hCliff, float hBot) {
                float3 h = float3(
                    hTop   + splat.r * 2.0,
                    hCliff + splat.g * 2.0,
                    hBot   + splat.b * 2.0
                );
                float maxH = max(h.x, max(h.y, h.z)) - _BlendSharpness;
                float3 w = max(h - maxH, 0);
                return w / (w.x + w.y + w.z + 0.001);
            }

            fixed4 frag(v2f i) : SV_Target {
                // Triplanar 权重
                float3 n = abs(i.worldNorm);
                n = pow(n, _TriplanarSharpness);
                n /= (n.x + n.y + n.z + 0.0001);

                // 各层 triplanar 采样
                half4 cTop    = TriSampleMacro(_TexTop,    i.worldPos, n, _TilingTop)    * _ColorTop;
                half4 cCliff  = TriSampleMacro(_TexCliff,  i.worldPos, n, _TilingCliff)  * _ColorCliff;
                half4 cBottom = TriSampleMacro(_TexBottom, i.worldPos, n, _TilingBottom) * _ColorBottom;

                // 各层 triplanar 法线采样
                half3 nTop    = TriSampleNormal(_NormTop,    i.worldPos, n, _TilingTop);
                half3 nCliff  = TriSampleNormal(_NormCliff,  i.worldPos, n, _TilingCliff);
                half3 nBottom = TriSampleNormal(_NormBottom, i.worldPos, n, _TilingBottom);

                // Height-based blend
                float hTop   = dot(cTop.rgb,   float3(0.299, 0.587, 0.114));
                float hCliff = dot(cCliff.rgb,  float3(0.299, 0.587, 0.114));
                float hBot   = dot(cBottom.rgb, float3(0.299, 0.587, 0.114));
                float3 blend = HeightBlend(i.splat.rgb, hTop, hCliff, hBot);

                half3 albedo = cTop.rgb * blend.x + cCliff.rgb * blend.y + cBottom.rgb * blend.z;

                // 混合法线 (世界空间)
                half3 worldN = nTop * blend.x + nCliff * blend.y + nBottom * blend.z;
                worldN = normalize(worldN);

                // 光照
                float3 lightDir = normalize(float3(0.4, 0.9, 0.3));
                float  diff = saturate(dot(worldN, lightDir));

                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 halfDir = normalize(lightDir + viewDir);
                float  spec = pow(saturate(dot(worldN, halfDir)), _SpecPower) * _SpecStrength;

                float ao = 1.0 - (1.0 - i.splat.a) * _AOStrength;
                float lighting = _Ambient + (1.0 - _Ambient) * diff;
                half3 finalColor = albedo * lighting * ao + spec;

                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
