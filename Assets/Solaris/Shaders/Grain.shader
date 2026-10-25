// Static film grain: fixed texture, 1 texel = 1 screen pixel, 2x-multiply blend (0.5 = neutral).
Shader "Solaris/Grain"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _GrainTex ("Grain", 2D) = "gray" {}
        _Strength ("Strength", Float) = 0.1
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend DstColor SrcColor

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _GrainTex;
            float _Strength;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // Exactly one grain texel per screen pixel: static, crisp, never shimmering.
                float4 t = tex2D(_GrainTex, i.pos.xy / 1024.0);
                float grain = (t.r - 0.5) * 2.0;
                float dust = saturate((t.g - 0.5) * 2.0);
                float s = _Strength * i.color.a;
                float v = 0.5 + grain * s * 0.5 + dust * s * 0.9;
                return float4(v, v, v, 1);
            }
            ENDCG
        }
    }
}
