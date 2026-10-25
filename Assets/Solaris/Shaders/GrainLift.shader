// Additive half of the static film grain: lifts tiny specks of light out of dark areas
// (the 2x-multiply grain is invisible on near-black). Same fixed texture, 1 texel = 1 pixel.
Shader "Solaris/GrainLift"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _GrainTex ("Grain", 2D) = "gray" {}
        _Strength ("Strength", Float) = 0.05
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend One One

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
                float4 t = tex2D(_GrainTex, (i.pos.xy + 517.0) / 1024.0);   // offset so it doesn't mirror the multiply layer
                float grain = max((t.r - 0.5) * 2.0, 0.0);
                float v = grain * grain * _Strength * i.color.a * 0.35;    // linear-space add: small values read clearly in the darks
                return float4(v, v * 0.93, v * 0.86, 0);
            }
            ENDCG
        }
    }
}
