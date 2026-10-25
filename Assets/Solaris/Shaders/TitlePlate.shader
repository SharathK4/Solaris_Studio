// Scene 1: the key art, rebuilt from its layers so only the red glows are alive.
//   colour field (_Plate)  - sampled through a tiny displacement where _Motion.b > 0 (the reds)
//   film texture (_Detail) - grain, dust, scratches, light-leak flecks: never displaced (static)
// With _Live = 0 the output equals the original image.
Shader "Solaris/BG/TitlePlate"
{
    Properties
    {
        [PerRendererData] _MainTex ("Unused", 2D) = "white" {}
        _Plate ("Colour field", 2D) = "gray" {}
        _Detail ("Film texture", 2D) = "gray" {}
        _Motion ("Motion (rg dir, b weight)", 2D) = "black" {}
        _Fit ("Fit (scale xy)", Vector) = (1, 1, 0, 0)
        _ImageAspect ("Image aspect", Float) = 1.786
        _DetailScale ("Detail scale", Float) = 1.6
        _Live ("Live", Float) = 1
        _Breathe ("Breathe amount", Float) = 0.03
        _Wander ("Wander amount", Float) = 0.012
        _Morph ("Morph amount", Float) = 0.019
        _Glow ("Glow pulse", Float) = 0.45
        _T ("Time", Float) = 0
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

            sampler2D _Plate, _Detail, _Motion;
            float4 _Fit;
            float _ImageAspect, _DetailScale, _Live, _Breathe, _Wander, _Morph, _Glow, _T;

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
                // Fit the whole image width (or height); anything beyond is mirrored colour field.
                float2 uv = (i.uv - 0.5) * _Fit.xy + 0.5;

                float4 mo = tex2D(_Motion, uv);
                float w = mo.b * _Live;
                float2 inward = (mo.rg - 0.5) * 2.0;             // toward the glow's core (image px space)
                float t = _T;

                // Breathing: the glows swell and settle on two slow, unrelated cycles (~7 s, ~19 s).
                float breathe = 0.62 * sin(t * 0.88) + 0.38 * sin(t * 0.33 + 1.3);
                // Wander: each glow drifts a hair, never the same path twice.
                float2 wander = float2(sin(t * 0.26) + 0.5 * sin(t * 0.16 + 2.0), cos(t * 0.21) + 0.5 * cos(t * 0.14 + 0.7));
                // Morph: soft, organic undulation of the edges.
                float2 q = uv * float2(_ImageAspect, 1.0) * 1.6;
                float2 morph = float2(fbm(q + float2(t * 0.06, 0.0), 3), fbm(q + float2(3.7, -t * 0.052), 3));

                float2 disp = _Breathe * breathe * inward + _Wander * wander + _Morph * morph;
                disp.x /= _ImageAspect;                          // isotropic on screen
                float3 plate = tex2D(_Plate, uv + disp * w).rgb; // sRGB-encoded values

                // A breath of light in the red cores (like a living light leak), in step with the swell.
                float pulse = saturate(0.5 + 0.5 * breathe + 0.25 * fbm(q * 0.6 + t * 0.05, 2));
                float core = w * w;
                plate = lerp(plate, plate * float3(1.045, 0.985, 0.96) + float3(0.012, 0.003, 0.0), core * _Glow * pulse);

                float3 detail = (tex2D(_Detail, uv).rgb - 0.5) / _DetailScale;
                float3 c = plate + detail;

                float a = i.color.a;
                return float4(toOutput(c) * a, a);
            }
            ENDCG
        }
    }
}
