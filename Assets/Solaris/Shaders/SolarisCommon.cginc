#ifndef SOLARIS_COMMON_INCLUDED
#define SOLARIS_COMMON_INCLUDED

// ---------------------------------------------------------------- hashing / noise
float hash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// Gradient noise (smooth, isotropic enough for flowing ink), range ~[-1, 1].
float gnoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float2 ga = hash22(i + float2(0, 0)) * 2 - 1;
    float2 gb = hash22(i + float2(1, 0)) * 2 - 1;
    float2 gc = hash22(i + float2(0, 1)) * 2 - 1;
    float2 gd = hash22(i + float2(1, 1)) * 2 - 1;
    float va = dot(ga, f - float2(0, 0));
    float vb = dot(gb, f - float2(1, 0));
    float vc = dot(gc, f - float2(0, 1));
    float vd = dot(gd, f - float2(1, 1));
    return 1.6 * lerp(lerp(va, vb, u.x), lerp(vc, vd, u.x), u.y);
}

static const float2x2 k_Rot = float2x2(0.80, 0.60, -0.60, 0.80);

float fbm(float2 p, int octaves)
{
    float s = 0, a = 0.5;
    for (int i = 0; i < octaves; i++)
    {
        s += a * gnoise(p);
        p = mul(k_Rot, p) * 2.03 + 17.1;
        a *= 0.5;
    }
    return s;
}

// ---------------------------------------------------------------- colour helpers
float3 srgbToLinear(float3 c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }
float3 linearToSrgb(float3 c) { return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - 0.055; }

// Authoring colours are sRGB hex; convert when the project renders in linear space.
float3 toOutput(float3 srgb)
{
#ifdef UNITY_COLORSPACE_GAMMA
    return srgb;
#else
    return srgbToLinear(saturate(srgb));
#endif
}

float3 hexcol(uint rgb) { return float3((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255) / 255.0; }

// Tiny ordered dither to kill banding before 8-bit storage.
float dither(float2 px) { return (hash12(px) - 0.5) / 255.0; }

float smoothmax(float a, float b, float k)
{
    float h = saturate(0.5 + 0.5 * (a - b) / k);
    return lerp(b, a, h) + k * h * (1.0 - h);
}

#endif
