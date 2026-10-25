// Scene 1: slow "ink spilled in water" colour field (double domain-warped fBm).
// Composition follows the key art: red pools left-centre and bottom-right, cream heart, sage edges.
Shader "Solaris/BG/InkFlow"
{
    Properties
    {
        _Speed ("Speed", Float) = 1
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "SolarisCommon.cginc"

            float _T, _Aspect, _Speed;

            float blob(float2 p, float2 c, float2 r)
            {
                float2 d = (p - c) / r;
                return exp(-dot(d, d));
            }

            float3 ramp(float h)
            {
                // sRGB stops, cool -> hot.
                const int N = 9;
                float3 cols[N] = {
                    hexcol(0x5B8C66), hexcol(0x8CB08B), hexcol(0xD3D8B6), hexcol(0xECE2C3), hexcol(0xF0CF8C),
                    hexcol(0xF3A64B), hexcol(0xEA6B2D), hexcol(0xCC3A24), hexcol(0xA42418)
                };
                float stops[N] = { 0.00, 0.20, 0.38, 0.50, 0.60, 0.70, 0.80, 0.90, 1.00 };
                float3 c = cols[0];
                [unroll] for (int i = 1; i < N; i++)
                {
                    float k = saturate((h - stops[i - 1]) / (stops[i] - stops[i - 1]));
                    k = k * k * (3 - 2 * k);
                    c = lerp(c, cols[i], k);
                }
                return c;
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float2 p = (uv - 0.5) * float2(_Aspect, 1.0);
                float t = _T * 0.042 * _Speed;

                // Double domain warp: large, lazy, fluid swirls.
                float2 base = p * 1.05;
                float2 q = float2(fbm(base + float2(0.0, t), 4),
                                  fbm(base + float2(5.2, 1.3) - t * 0.8, 4));
                float2 r = float2(fbm(base + 2.1 * q + float2(1.7, 9.2) + t * 0.55, 4),
                                  fbm(base + 2.1 * q + float2(8.3, 2.8) - t * 0.45, 4));
                float n = fbm(base + 1.9 * r, 4);

                // Ink pools drift and breathe; evaluated in warped space so their rims feather.
                float2 w = p + 0.22 * r;
                float2 drift1 = 0.05 * float2(sin(t * 0.9), cos(t * 0.7));
                float2 drift2 = 0.05 * float2(cos(t * 0.6 + 1.3), sin(t * 0.8 + 0.4));
                float pools =
                      0.95 * blob(w, float2(-0.62 * _Aspect * 0.56, 0.10) + drift1, float2(0.52, 0.20))
                    + 0.95 * blob(w, float2(0.78 * _Aspect * 0.56, -0.20) + drift2, float2(0.34, 0.22))
                    + 0.55 * blob(w, float2(-0.95 * _Aspect * 0.56, -0.52), float2(0.30, 0.16))
                    + 0.35 * blob(w, float2(0.95 * _Aspect * 0.56, 0.42), float2(0.2, 0.2));
                float cool =
                      0.55 * blob(w, float2(0.55 * _Aspect * 0.56, 0.36), float2(0.5, 0.26))
                    + 0.45 * blob(w, float2(-0.25 * _Aspect * 0.56, -0.32), float2(0.45, 0.2));

                float h = 0.44 + 0.42 * n + 0.62 * pools - 0.34 * cool;
                // Fine filaments: thin ridges where the warp folds, like pigment threads.
                float ridge = 1.0 - abs(fbm(base * 2.3 + 3.0 * r + t * 0.3, 3));
                h += 0.05 * pow(saturate(ridge), 6.0) * smoothstep(0.55, 0.8, h);
                h = saturate(h);

                float3 col = ramp(h);

                // Exposure breathing + gentle film vignette with warm roll-off.
                float lum = 0.97 + 0.03 * gnoise(p * 0.8 + t * 0.2);
                float2 v = uv - 0.5;
                float vig = 1.0 - 0.28 * dot(v * float2(1.1, 1.3), v * float2(1.1, 1.3)) * 2.0;
                col *= lum * vig;
                col = lerp(col, col * float3(1.02, 0.97, 0.9), 0.35);

                return float4(toOutput(col) + dither(i.uv * 4096.0), 1);
            }
            ENDCG
        }
    }
}
