// Scene 2: cream-to-rust horizon gradient that slowly breathes, with a halo around the orb.
Shader "Solaris/BG/WarmHorizon"
{
    Properties
    {
        _Orb ("Orb (uv xy, radius z, glow w)", Vector) = (0.5, 0.5, 0.2, 0.5)
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

            float _T, _Aspect;
            float4 _Orb;

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float t = _T;
                // Horizon line undulates very slowly, so the scene feels alive.
                float wobble = 0.018 * sin(t * 0.35 + uv.x * 2.2) + 0.03 * fbm(float2(uv.x * 1.3, t * 0.05), 3);
                float y = uv.y + wobble;

                float3 c0 = hexcol(0xB4431B);  // bottom rust
                float3 c1 = hexcol(0xD9622A);
                float3 c2 = hexcol(0xEE8436);  // orange band
                float3 c3 = hexcol(0xF2A869);
                float3 c4 = hexcol(0xEFD0B8);  // peach cream
                float3 c5 = hexcol(0xEEE2D8);  // top
                float3 col = c0;
                col = lerp(col, c1, smoothstep(0.00, 0.22, y));
                col = lerp(col, c2, smoothstep(0.18, 0.46, y));
                col = lerp(col, c3, smoothstep(0.44, 0.62, y));
                col = lerp(col, c4, smoothstep(0.58, 0.76, y));
                col = lerp(col, c5, smoothstep(0.74, 0.98, y));

                // Soft atmospheric variation.
                float2 p = (uv - 0.5) * float2(_Aspect, 1);
                col *= 0.985 + 0.03 * fbm(p * 1.4 + float2(t * 0.02, -t * 0.015), 3);

                // Halo that follows the orb.
                float2 d = (uv - _Orb.xy) * float2(_Aspect, 1);
                float halo = exp(-dot(d, d) / (_Orb.z * _Orb.z * 2.2)) * _Orb.w;
                col = lerp(col, col * float3(1.06, 1.0, 0.94) + float3(0.05, 0.03, 0.02), halo);

                return float4(toOutput(col) + dither(uv * 4096.0), 1);
            }
            ENDCG
        }
    }
}
