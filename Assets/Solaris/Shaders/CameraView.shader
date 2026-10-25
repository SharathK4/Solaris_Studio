// Mirrored, cover-fitted camera image with optional warm duotone grade (song dial / tutorials)
// and a debug tint of the person mask.
Shader "Solaris/CameraView"
{
    Properties
    {
        [PerRendererData] _MainTex ("Frame", 2D) = "black" {}
        _Mask ("Person mask", 2D) = "black" {}
        _Cover ("Cover (scale xy, offset zw)", Vector) = (1, 1, 0, 0)
        _Mirror ("Mirror", Float) = 1
        _Duotone ("Duotone", Float) = 0
        _Dark ("Duotone dark", Color) = (0.08, 0.03, 0.015, 1)
        _Light ("Duotone light", Color) = (0.95, 0.62, 0.38, 1)
        _Vignette ("Vignette", Float) = 0
        _MaskTint ("Mask tint", Float) = 0
        _Radius ("Round corners (px)", Float) = 0
        _Spot ("Spotlight (uv xy, radius z, strength w)", Vector) = (0.5, 0.5, 0.2, 0)
        _ViewAspect ("View aspect", Float) = 1.778
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "SolarisCommon.cginc"

            sampler2D _MainTex, _Mask;
            float4 _Cover, _Dark, _Light;
            float _Mirror, _Duotone, _Vignette, _MaskTint, _Radius, _ViewAspect;
            float4 _Spot;

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
                float2 uv = i.uv * _Cover.xy + _Cover.zw;
                if (_Mirror > 0.5) uv.x = 1 - uv.x;
                float3 c = tex2D(_MainTex, uv).rgb;

                if (_Duotone > 0)
                {
                    float l = dot(linearToSrgb(saturate(c)), float3(0.299, 0.587, 0.114));
                    l = smoothstep(0.04, 0.92, l);
                    float3 duo = lerp(_Dark.rgb, _Light.rgb, l);
                    c = lerp(c, duo, _Duotone);
                }

                if (_MaskTint > 0)
                {
                    float m = tex2D(_Mask, uv).r;
                    c = lerp(c, c * float3(0.4, 1.1, 0.7) + float3(0, 0.08, 0.04), m * _MaskTint);
                }

                float2 v = i.uv - 0.5;
                c *= 1 - _Vignette * dot(v, v) * 2.2;

                float a = i.color.a;
                // Warm pool of light around the fingertip: the room shows through a little more there.
                if (_Spot.w > 0)
                {
                    float2 sd = (i.uv - _Spot.xy) * float2(_ViewAspect, 1.0);
                    float spot = exp(-dot(sd, sd) / max(_Spot.z * _Spot.z, 1e-4)) * _Spot.w;
                    c *= 1.0 + spot * 0.55;
                    a = saturate(a + spot * 0.22);
                }
                if (_Radius > 0)
                {
                    float2 px = abs(i.uv - 0.5) / max(fwidth(i.uv), 1e-5);
                    float2 half_ = 0.5 / max(fwidth(i.uv), 1e-5);
                    float2 q = px - half_ + _Radius;
                    float d = length(max(q, 0)) - _Radius;
                    a *= saturate(0.5 - d);
                }
                return float4(c * i.color.rgb, a);
            }
            ENDCG
        }
    }
}
