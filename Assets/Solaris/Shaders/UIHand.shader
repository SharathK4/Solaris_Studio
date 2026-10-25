// Sculpted 3D hand, raymarched from the 21 MediaPipe-style joints supplied by HandFigure.cs.
// Round cones per bone + a smooth-blended palm fan; warm key light, subsurface wrap, ember rim.
Shader "Solaris/UI/Hand"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Skin ("Skin", Color) = (0.93, 0.72, 0.6, 1)
        _Rim ("Rim light", Color) = (1, 0.42, 0.16, 1)
        _Key ("Key light", Color) = (1, 0.92, 0.82, 1)
        _RimStrength ("Rim strength", Float) = 1.2
        _Glow ("Emissive glow", Float) = 0
        _FadeBottom ("Fade bottom", Float) = 0.28
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
            #pragma target 4.5
            #include "UnityCG.cginc"

            float4 _J[21];          // joint xyz (view space, camera at +z looking -z), w = radius
            float4 _Bounds;         // xyz centre, w radius
            float4 _Skin, _Rim, _Key;
            float _RimStrength, _Glow, _FadeBottom, _Aspect;

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

            float smin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            // IQ round cone between a (radius r1) and b (radius r2).
            float sdRoundCone(float3 p, float3 a, float3 b, float r1, float r2)
            {
                float3 ba = b - a;
                float l2 = dot(ba, ba);
                float rr = r1 - r2;
                float a2 = l2 - rr * rr;
                float il2 = 1.0 / max(l2, 1e-6);
                float3 pa = p - a;
                float y = dot(pa, ba);
                float z = y - l2;
                float3 xv = pa * l2 - ba * y;
                float x2 = dot(xv, xv);
                float y2 = y * y * l2;
                float z2 = z * z * l2;
                float k = sign(rr) * rr * rr * x2;
                if (sign(z) * a2 * z2 > k) return sqrt(x2 + z2) * il2 - r2;
                if (sign(y) * a2 * y2 < k) return sqrt(x2 + y2) * il2 - r1;
                return (sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
            }

            float bone(float3 p, int a, int b) { return sdRoundCone(p, _J[a].xyz, _J[b].xyz, _J[a].w, _J[b].w); }

            float map(float3 p)
            {
                // Palm: a fan of thick capsules from the wrist to each knuckle, welded together.
                float palm = bone(p, 0, 5);
                palm = smin(palm, bone(p, 0, 9), 0.12);
                palm = smin(palm, bone(p, 0, 13), 0.12);
                palm = smin(palm, bone(p, 0, 17), 0.12);
                palm = smin(palm, bone(p, 5, 9), 0.08);
                palm = smin(palm, bone(p, 9, 13), 0.08);
                palm = smin(palm, bone(p, 13, 17), 0.08);
                palm = smin(palm, bone(p, 0, 1), 0.1);
                palm = smin(palm, bone(p, 1, 5), 0.12);

                // Forearm stub below the wrist (fades out in the frag).
                float3 wrist = _J[0].xyz;
                float3 down = normalize(_J[0].xyz - _J[9].xyz);
                float arm = sdRoundCone(p, wrist + down * 1.6, wrist, _J[0].w * 1.05, _J[0].w * 0.92);
                float d = smin(palm, arm, 0.12);

                // Thumb and fingers.
                float f = bone(p, 1, 2);
                f = min(f, bone(p, 2, 3));
                f = min(f, bone(p, 3, 4));
                d = smin(d, f, 0.07);
                [unroll] for (int s = 5; s <= 17; s += 4)
                {
                    float g = bone(p, s, s + 1);
                    g = min(g, bone(p, s + 1, s + 2));
                    g = min(g, bone(p, s + 2, s + 3));
                    d = smin(d, g, 0.045);
                }
                return d;
            }

            float3 calcNormal(float3 p)
            {
                const float2 e = float2(1, -1) * 0.0025;
                return normalize(e.xyy * map(p + e.xyy) + e.yyx * map(p + e.yyx) +
                                 e.yxy * map(p + e.yxy) + e.xxx * map(p + e.xxx));
            }

            float3 toLinear(float3 c)
            {
            #ifdef UNITY_COLORSPACE_GAMMA
                return c;
            #else
                return pow(c, 2.2);
            #endif
            }

            float4 frag(v2f i) : SV_Target
            {
                // Perspective camera at +z, looking -z, 26 degree vertical FOV.
                float2 ndc = i.uv * 2 - 1;
                ndc.x *= _Aspect;
                float3 ro = float3(0, 0, 6.0);
                float3 rd = normalize(float3(ndc * 0.231, -1));

                // Bounding sphere early-out.
                float3 oc = ro - _Bounds.xyz;
                float bb = dot(oc, rd);
                float cc = dot(oc, oc) - _Bounds.w * _Bounds.w;
                float disc = bb * bb - cc;
                if (disc < 0) return 0;
                float t = max(0, -bb - sqrt(disc));
                float tEnd = -bb + sqrt(disc);

                float minD = 1e9, tMin = t;
                bool hit = false;
                [loop] for (int k = 0; k < 72; k++)
                {
                    float3 q = ro + rd * t;
                    float d = map(q);
                    float angular = d / max(t, 1e-3);
                    if (angular < minD) { minD = angular; tMin = t; }
                    if (d < 0.0015 * t) { hit = true; tMin = t; break; }
                    t += d * 0.9;
                    if (t > tEnd) break;
                }

                // Silhouette coverage from the closest approach (angular distance vs. pixel size).
                float2 fw = fwidth(i.uv);
                float pixelAngle = max(fw.x, fw.y) * 2.0 * 0.231;
                float coverage = hit ? 1 : saturate(1 - minD / max(pixelAngle, 1e-6));
                if (coverage <= 0.001) return 0;
                float hitT = tMin;

                float3 p = ro + rd * hitT;
                float3 n = calcNormal(p);
                float3 v = -rd;

                float3 keyDir = normalize(float3(-0.55, 0.65, 0.75));
                float3 rimDir = normalize(float3(0.4, 0.3, -0.85));
                float ndl = dot(n, keyDir);
                float wrap = saturate((ndl + 0.45) / 1.45);                 // subsurface-ish wrap
                float spec = pow(saturate(dot(reflect(-keyDir, n), v)), 28) * 0.18;
                float fres = pow(1 - saturate(dot(n, v)), 3);
                float rim = saturate(dot(n, -rimDir) * 0.5 + 0.5) * fres;

                // Cheap AO: probe the field along the normal.
                float ao = 0;
                ao += (0.03 - map(p + n * 0.03)) * 1.0;
                ao += (0.08 - map(p + n * 0.08)) * 0.5;
                ao = saturate(1 - ao * 6);

                float3 skin = _Skin.rgb;          // Color properties arrive already linearised
                float3 sss = toLinear(float3(0.95, 0.42, 0.28));
                float3 col = skin * _Key.rgb * wrap * (0.55 + 0.45 * ao);
                col += sss * (1 - wrap) * 0.18 * ao;                          // warm bleed in the shadows
                col += _Rim.rgb * rim * _RimStrength;
                col += spec * _Key.rgb;
                col += _Rim.rgb * _Glow * 0.35;

                float fade = saturate(i.uv.y / max(_FadeBottom, 1e-3));
                fade = fade * fade * (3 - 2 * fade);
                float alpha = coverage * fade * i.color.a;
                return float4(col * alpha, alpha);
            }
            ENDCG
        }
    }
}
