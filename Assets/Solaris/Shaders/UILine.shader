// Analytic anti-aliased strokes for LineGraphic (uv0 = along, across, halfWidth, length).
Shader "Solaris/UI/Line"
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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            float4 _ClipRect;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float4 uv0 : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; float4 world : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv0;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float hw = i.uv.z;
                // Distance to the capsule core (rounded caps).
                float along = i.uv.x;
                float len = i.uv.w;
                float dx = max(max(hw - along, along - (len - hw)), 0);
                float d = length(float2(dx, abs(i.uv.y))) - hw;
                float aa = max(fwidth(d), 1e-4);
                float a = saturate(0.5 - d / aa);
                // Thin strokes (< 1px) fade out instead of aliasing.
                a *= saturate(hw * 2.0);
                float4 c = i.color;
                c.a *= a;
                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(i.world.xy, _ClipRect);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
