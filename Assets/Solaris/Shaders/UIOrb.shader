// Scene 2: the soft sun orb. Lit sphere with a lilac key light, molten orange core that slowly
// churns, a butter-yellow rim and feathered, slightly translucent edges.
Shader "Solaris/UI/Orb"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Squash ("Squash", Float) = 0
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

            float _Squash;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv * 2 - 1;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv * 1.08;
                float r = length(p);
                float edge = 1.0 - smoothstep(0.90, 1.0, r);       // feathered silhouette
                if (edge <= 0) return 0;

                float z = sqrt(saturate(1.0 - r * r));
                float3 n = normalize(float3(p, z));
                float t = _Time.y;

                float3 keyDir = normalize(float3(-0.75, 0.45, 0.55));
                float key = saturate(dot(n, keyDir));
                float rimDir = saturate(dot(n.xy, normalize(float2(0.55, -0.7))));
                float fres = pow(1.0 - z, 1.6);

                // Churning molten core (slow, low frequency).
                float churn = fbm(n.xy * 1.6 + float2(t * 0.05, -t * 0.04), 3);

                float3 core = hexcol(0xEB5E1E);
                float3 body = hexcol(0xF07A2E);
                float3 lilac = hexcol(0xE8A6B0);
                float3 butter = hexcol(0xF3CF6A);

                float3 col = lerp(body, core, saturate(0.55 + churn * 0.9 - key * 0.4));
                col = lerp(col, lilac, smoothstep(0.35, 0.95, key) * 0.85);
                col = lerp(col, butter, saturate(fres * rimDir * 1.8));
                col += 0.05 * key;                                  // lift

                float alpha = edge * lerp(0.92, 0.82, fres) * i.color.a;
                float3 outc = toOutput(col);
                return float4(outc * alpha, alpha);
            }
            ENDCG
        }
    }
}
