// Dark screens (3-7): deep base gradient with up to four drifting, smoke-warped ember glows.
Shader "Solaris/BG/GlowField"
{
    Properties
    {
        _Top ("Top", Color) = (0.1, 0.04, 0.02, 1)
        _Bottom ("Bottom", Color) = (0.02, 0.01, 0.005, 1)
        _G0 ("Glow0 (x y r i)", Vector) = (0.1, 0.9, 0.5, 1)
        _G1 ("Glow1", Vector) = (0.9, 0.9, 0.5, 1)
        _G2 ("Glow2", Vector) = (0.5, 0.4, 0.5, 0)
        _G3 ("Glow3", Vector) = (0.5, 0.0, 0.5, 0)
        _C0 ("Color0", Color) = (0.9, 0.35, 0.1, 1)
        _C1 ("Color1", Color) = (0.7, 0.1, 0.05, 1)
        _C2 ("Color2", Color) = (0.9, 0.5, 0.2, 1)
        _C3 ("Color3", Color) = (0.9, 0.5, 0.2, 1)
        _Smoke ("Smoke", Float) = 0.25
        _Drift ("Drift", Float) = 1
        _Pulse ("Pulse", Float) = 0
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

            float _T, _Aspect, _Smoke, _Drift, _Pulse;
            float4 _Top, _Bottom, _G0, _G1, _G2, _G3, _C0, _C1, _C2, _C3;

            // Colour properties arrive linearised; work in display space for Figma-like glows.
            float3 disp(float4 c)
            {
            #ifdef UNITY_COLORSPACE_GAMMA
                return c.rgb;
            #else
                return linearToSrgb(c.rgb);
            #endif
            }

            float glow(float2 p, float4 g, float2 warp, float phase)
            {
                if (g.w <= 0) return 0;
                float2 c = (g.xy - 0.5) * float2(_Aspect, 1);
                c += 0.04 * _Drift * float2(sin(_T * 0.11 + phase), cos(_T * 0.09 + phase * 1.7));
                float2 d = (p + warp - c) / g.z;
                return exp(-dot(d, d) * 1.6) * g.w * (1.0 + _Pulse * 0.25 * sin(_T * 0.7 + phase));
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float2 p = (uv - 0.5) * float2(_Aspect, 1);
                float t = _T * 0.03 * _Drift;
                float2 warp = _Smoke * 0.35 * float2(fbm(p * 1.3 + float2(t, -t), 4), fbm(p * 1.3 + float2(4.1, 2.7) - t, 4));

                float3 col = lerp(disp(_Bottom), disp(_Top), smoothstep(0, 1, uv.y));
                col += disp(_C0) * glow(p, _G0, warp, 0.0);
                col += disp(_C1) * glow(p, _G1, warp, 2.1);
                col += disp(_C2) * glow(p, _G2, warp, 4.2);
                col += disp(_C3) * glow(p, _G3, warp, 5.3);

                // Smooth highlight roll-off instead of clipping.
                col = 1.0 - exp(-col * 1.25);
                col = col / 0.713;   // renormalise so a 1.0 glow still lands near its colour
                col = saturate(col);

                return float4(toOutput(col) + dither(uv * 4096.0), 1);
            }
            ENDCG
        }
    }
}
