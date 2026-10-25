// SDF wordmark: razor-sharp at any size, with a focus-pull reveal (_Soft), a soft warm drop
// shadow for legibility over the ink field, and a slow light sweep.
Shader "Solaris/UI/Logo"
{
    Properties
    {
        [PerRendererData] _MainTex ("SDF", 2D) = "white" {}
        _Soft ("Softness", Float) = 0
        _Dilate ("Dilate", Float) = 0
        _ShadowAlpha ("Shadow", Float) = 0.25
        _Sweep ("Sweep", Float) = -1
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
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Soft, _Dilate, _ShadowAlpha, _Sweep;

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
                float d = tex2D(_MainTex, i.uv).r + _Dilate;
                float w = fwidth(d) * 0.75 + _Soft;
                float a = smoothstep(0.5 - w, 0.5 + w, d);

                float ds = tex2D(_MainTex, i.uv + float2(-0.0015, 0.006)).r;
                float sh = smoothstep(0.30, 0.56, ds) * _ShadowAlpha * (1 - a);

                float3 col = i.color.rgb;
                // Light sweep: a soft diagonal band travelling across the letters.
                float band = exp(-pow((i.uv.x + i.uv.y * 0.35 - _Sweep) * 7.0, 2.0));
                col = lerp(col, float3(1, 0.98, 0.94), band * 0.55 * a);

                float alpha = a * i.color.a;
                float3 shadowCol = float3(0.22, 0.08, 0.03);
                float4 outc = float4(col * alpha, alpha);
                float sha = sh * i.color.a;
                outc += float4(shadowCol * sha, sha) * (1 - outc.a);
                return outc;
            }
            ENDCG
        }
    }
}
