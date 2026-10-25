// Rounded rectangle via signed distance: gradient fill, hairline gradient border, soft shadow/glow.
// All parameters arrive per-vertex from ShapeGraphic so every shape shares one material.
Shader "Solaris/UI/Shape"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            float4 _ClipRect;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float4 p : TEXCOORD0;       // local xy, half size zw
                float4 shape : TEXCOORD1;   // radius, border, shadow, borderFade
                float4 border : TEXCOORD2;
                float4 bottom : TEXCOORD3;
                float4 shadow : TEXCOORD4;  // rgb + a
                float4 world : TEXCOORD5;
                float2 glow : TEXCOORD6;    // inner glow, top alpha
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.p = v.uv0;
                o.shape = v.uv1;
                o.border = v.uv2;
                o.bottom = v.uv3;
                o.shadow = float4(v.normal, v.tangent.x);
                o.glow = v.tangent.yz;
                return o;
            }

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 p = i.p.xy;
                float2 half_ = i.p.zw;
                float d = sdRoundBox(p, half_, i.shape.x);
                float aa = max(fwidth(d), 1e-4);

                float t = saturate(p.y / max(half_.y, 1e-3) * 0.5 + 0.5);    // 0 bottom, 1 top
                // Vertex colour is the top fill; its alpha also carries CanvasGroup fades.
                float groupAlpha = i.color.a;
                float4 top = float4(i.color.rgb, i.glow.y);
                float4 fill = lerp(float4(i.bottom.rgb, i.bottom.a), top, t);

                float inside = saturate(0.5 - d / aa);
                float4 col = float4(fill.rgb * fill.a, fill.a) * inside;

                // Inner rim glow (glass): light that hugs the inside of the edge.
                if (i.glow.x > 0)
                {
                    float rim = exp(-max(-d, 0) / 10.0) * i.glow.x * inside;
                    float3 rimCol = lerp(float3(1, 0.85, 0.7), i.border.rgb, 0.5);
                    col.rgb += rimCol * rim * (0.4 + 0.6 * t);
                    col.a = saturate(col.a + rim * 0.5);
                }

                // Hairline border with optional fade toward the bottom.
                float bw = i.shape.y;
                if (bw > 0)
                {
                    float band = saturate(0.5 - d / aa) * saturate(0.5 + (d + bw) / aa);
                    float fade = lerp(1.0 - i.shape.w, 1.0, t);
                    float ba = i.border.a * band * fade;
                    col = col * (1 - ba) + float4(i.border.rgb * ba, ba);
                }

                // Soft shadow / glow behind the shape.
                float ss = i.shape.z;
                if (ss > 0)
                {
                    float od = max(d, 0);
                    float sh = exp(-(od * od) / (ss * ss)) * i.shadow.a * (1 - inside);
                    col = col + float4(i.shadow.rgb * sh, sh) * (1 - col.a);
                }

                col *= groupAlpha;   // CanvasGroup fade applies to fill, border and shadow alike

                #ifdef UNITY_UI_CLIP_RECT
                col *= UnityGet2DClipping(i.world.xy, _ClipRect);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
