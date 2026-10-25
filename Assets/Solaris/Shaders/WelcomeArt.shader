// Scene 2, rebuilt from the key art's layers (Tools/make_welcome.py):
//   plate (sky bands) -> warm halo -> orb (moves, squashes; composited in linear light) -> static grain.
// With the orb at its reference position and _Rise = _Halo = 0 the output equals the original art.
Shader "Solaris/BG/WelcomeArt"
{
    Properties
    {
        [PerRendererData] _MainTex ("Unused", 2D) = "white" {}
        _Plate ("Sky", 2D) = "gray" {}
        _Detail ("Film grain", 2D) = "gray" {}
        _Orb ("Orb (straight alpha)", 2D) = "black" {}
        _Fit ("Fit (scale xy)", Vector) = (1, 1, 0, 0)
        _ImageAspect ("Image aspect", Float) = 1.788
        _DetailScale ("Detail scale", Float) = 1.6
        _OrbC ("Orb centre (image uv, y up)", Vector) = (0.5, 0.5, 0, 0)
        _OrbR ("Orb radius (image heights)", Float) = 0.326
        _OrbScale ("Orb scale (x, y)", Vector) = (1, 1, 0, 0)
        _OrbMargin ("Orb texture margin", Float) = 1.12
        _Halo ("Halo", Float) = 0
        _Rise ("Sunrise warmth", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "SolarisCommon.cginc"

            sampler2D _Plate, _Detail, _Orb;
            float4 _Fit, _OrbC, _OrbScale;
            float _ImageAspect, _DetailScale, _OrbR, _OrbMargin, _Halo, _Rise;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = (i.uv - 0.5) * _Fit.xy + 0.5;          // image uv; beyond 0..1 mirrors
                float3 sky = srgbToLinear(tex2D(_Plate, uv).rgb);

                // The sky warms a touch while the orb is high, like light rising with it.
                float band = smoothstep(0.15, 0.45, uv.y) * (1.0 - smoothstep(0.62, 0.9, uv.y));
                sky *= 1.0 + _Rise * 0.035 * band * float3(1.0, 0.92, 0.82);

                // Orb in its own frame (image-height units), squashed about its centre.
                float2 d = uv - _OrbC.xy;
                d.x *= _ImageAspect;
                float2 p = d / (_OrbR * _OrbScale.xy);
                float2 ouv = p / _OrbMargin * 0.5 + 0.5;
                float4 orb = 0;
                if (all(ouv > 0.0) && all(ouv < 1.0)) orb = tex2D(_Orb, ouv);

                // Soft warm bloom just outside the orb's rim.
                float r = length(p);
                float halo = exp(-pow(max(r - 0.95, 0.0) / 0.42, 2.0)) * smoothstep(0.85, 1.0, r) * _Halo;
                sky += float3(1.0, 0.55, 0.28) * halo * 0.16;

                float3 col = lerp(sky, srgbToLinear(orb.rgb), orb.a);
                float3 s = linearToSrgb(col) + (tex2D(_Detail, uv).rgb - 0.5) / _DetailScale;

                float a = i.color.a;
                return float4(toOutput(s) * a, a);
            }
            ENDCG
        }
    }
}
