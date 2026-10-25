// Scene 7 AR composite, back to front:
//   studio backdrop (+ faint camera texture) -> bokeh -> sound waves -> you (person mask) -> glow.
// Waves are vertically-elongated rings around the face: pulses emitted per strum travel outward,
// standing rings breathe with the live spectrum. Everything sits behind the player.
Shader "Solaris/StageComposite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Frame", 2D) = "black" {}
        _Mask ("Person mask", 2D) = "white" {}
        _Cover ("Cover", Vector) = (1, 1, 0, 0)
        _Mirror ("Mirror", Float) = 1
        _HasMask ("Has mask", Float) = 0
        _Face ("Face (uv xy, radius z, confidence w)", Vector) = (0.5, 0.6, 0.12, 0)
        _Level ("Level", Float) = 0
        _Studio ("Backdrop strength", Float) = 0.8
        _WaveGain ("Wave gain", Float) = 1
        _Accent ("Accent", Color) = (0.95, 0.45, 0.18, 1)
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
            #include "SolarisCommon.cginc"

            sampler2D _MainTex, _Mask;
            float4 _Cover, _Face, _Accent;
            float _Mirror, _HasMask, _Level, _Studio, _WaveGain, _Aspect;
            float4 _Pulses[16];      // radius, amplitude, width, unused
            float4 _Bands[4];        // 16 spectrum bands, 0..1

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

            float3 grade(float3 lin, float amount)
            {
                float3 c = linearToSrgb(saturate(lin));
                float l = dot(c, float3(0.299, 0.587, 0.114));
                c = lerp(float3(l, l, l), c, 0.9);                              // a touch less saturation
                c = (c - 0.5) * 1.08 + 0.5;                                      // gentle contrast
                c = lerp(c, c * float3(1.06, 0.97, 0.88) + float3(0.02, 0.005, 0), amount); // warm
                c = lerp(float3(0.10, 0.04, 0.02), float3(1.0, 0.96, 0.9), c);   // lifted brown blacks
                return c;
            }

            float3 waveRamp(float i)
            {
                float3 ember = hexcol(0x6E1E0A);
                float3 orange = hexcol(0xE9702B);
                float3 cream = hexcol(0xFFE3BE);
                float3 c = lerp(ember, orange, smoothstep(0.0, 0.55, i));
                return lerp(c, cream, smoothstep(0.55, 1.2, i)) * saturate(i * 1.6);
            }

            float band(int k) { return _Bands[k >> 2][k & 3]; }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float2 fuv = uv * _Cover.xy + _Cover.zw;
                if (_Mirror > 0.5) fuv.x = 1 - fuv.x;
                float3 cam = tex2D(_MainTex, fuv).rgb;               // linear
                float mask = _HasMask > 0.5 ? smoothstep(0.28, 0.72, tex2D(_Mask, fuv).r) : 1;

                // --- backdrop: warm studio sweep with a whisper of the real room ---
                float3 camS = grade(cam, 1);
                float3 top = hexcol(0x3A1A0C), bottom = hexcol(0x120704);
                float3 backdrop = lerp(bottom, top, smoothstep(0.0, 1.0, uv.y));
                float2 fc = (uv - _Face.xy) * float2(_Aspect, 1);
                backdrop += hexcol(0x5A2610) * exp(-dot(fc, fc) * 2.2) * 0.9;   // key light spill behind the head
                float3 bg = lerp(camS * 0.55, backdrop + camS * 0.12, _Studio);

                // --- bokeh: slow drifting discs ---
                float3 bok = 0;
                [unroll] for (int b = 0; b < 14; b++)
                {
                    float2 h = hash22(float2(b, 7.3));
                    float2 p = float2(frac(h.x + _Time.y * 0.004 * (0.5 + h.y)), frac(h.y * 1.7 + _Time.y * 0.007 * (0.3 + h.x)));
                    float r = 0.012 + 0.03 * hash12(float2(b, 3.1));
                    float2 dd = (uv - p) * float2(_Aspect, 1);
                    float disc = smoothstep(r, r * 0.55, length(dd));
                    bok += hexcol(0xF2A04A) * disc * (0.05 + 0.08 * hash12(float2(b, 9.2))) * (0.6 + 0.4 * sin(_Time.y * 0.5 + b));
                }
                bg += bok;

                // --- sound waves around the face: blurred ")))" arcs beside the head ---
                float faceR = max(_Face.z, 0.06);
                float2 d = (uv - _Face.xy) * float2(_Aspect, 1);
                float ex = abs(d.x), ey = d.y;
                float r = sqrt(ex * ex + (ey * 0.7) * (ey * 0.7));      // tall ellipses
                float side = saturate(ex / max(r, 1e-4));                   // 1 beside the head, 0 above/below
                float cone = pow(side, 2.2);
                float taper = exp(-pow(ey / (0.21 + 0.3 * r), 2));         // arcs taper vertically
                float inner = smoothstep(faceR * 1.25, faceR * 2.2, r);
                float outer = 1 - smoothstep(0.4, 1.1, r);

                float w = 0;
                [unroll] for (int k = 0; k < 16; k++)
                {
                    float4 P = _Pulses[k];
                    if (P.y <= 0.0001) continue;
                    float x = (r - P.x) / P.z;
                    float glow = exp(-x * x);
                    float core = exp(-pow((r - P.x) / (P.z * 0.35), 2));
                    w += P.y * (0.6 * glow + 0.9 * core);
                }
                // A few standing rings that breathe with the spectrum (low, mid, high).
                float drift = frac(_Time.y * 0.08);
                [unroll] for (int s = 0; s < 8; s++)
                {
                    float R = faceR * 2.0 + (s + drift) * 0.055;
                    float a = (band(s * 2) + band(s * 2 + 1)) * 0.5;
                    a = a * (0.25 + 0.75 * _Level) * (1 - s / 10.0);
                    float x = (r - R) / (0.014 + 0.004 * s);
                    w += a * 0.7 * exp(-x * x);
                }
                w *= cone * taper * inner * outer * _WaveGain;
                float3 waves = waveRamp(w);

                // --- you ---
                float3 person = grade(cam, 0.8);
                float3 col = lerp(bg + waves, person, mask);
                col += waves * 0.08 * mask;                                  // waves wash lightly over you

                // film vignette
                float2 v = uv - 0.5;
                col *= 1 - 0.55 * dot(v * float2(1.0, 1.25), v * float2(1.0, 1.25));

                float3 outc = toOutput(col);
                float a = i.color.a;
                return float4(outc * a, a);
            }
            ENDCG
        }
    }
}
