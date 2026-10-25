using UnityEngine;
using UnityEngine.UI;

namespace Solaris.Rendering
{
    /// <summary>
    /// Static film grain over the whole app. The grain is a fixed texture mapped 1:1 to screen pixels
    /// and is never re-randomised per frame, so it reads as printed paper or film stock rather than
    /// TV static. It also carries sparse dust specks and hairline scratches like the key art.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class GrainOverlay : MonoBehaviour
    {
        const int Size = 1024;
        RawImage m_Image, m_LiftImage;
        Material m_Material, m_LiftMaterial;
        float m_Strength, m_Lift;
        Tween m_Tween;

        void Awake()
        {
            var rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            m_Material = SolarisAssets.Mat("Solaris/Grain");
            m_Material.SetTexture("_GrainTex", BuildTexture(20260930));
            m_Image = gameObject.AddComponent<RawImage>();
            m_Image.material = m_Material;
            m_Image.raycastTarget = false;

            // Second layer: light grain added on top, visible in the near-blacks a multiply can't touch.
            var lift = new GameObject("GrainLift", typeof(RectTransform));
            var lrt = (RectTransform)lift.transform;
            lrt.SetParent(transform, false);
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            m_LiftMaterial = SolarisAssets.Mat("Solaris/GrainLift");
            m_LiftMaterial.SetTexture("_GrainTex", m_Material.GetTexture("_GrainTex"));
            m_LiftImage = lift.AddComponent<RawImage>();
            m_LiftImage.material = m_LiftMaterial;
            m_LiftImage.raycastTarget = false;
            Set(0f, 0f);
        }

        public void FadeTo(float strength, float lift, float seconds)
        {
            m_Tween?.Kill();
            float from = m_Strength, fromLift = m_Lift;
            m_Tween = Anim.Run(seconds, k => Set(Mathf.Lerp(from, strength, k), Mathf.Lerp(fromLift, lift, k)), Ease.InOutSine, 0f, null, this);
        }

        void Set(float s, float lift)
        {
            m_Strength = s;
            m_Lift = lift;
            m_Material.SetFloat("_Strength", s);
            m_LiftMaterial.SetFloat("_Strength", lift);
            m_Image.enabled = s > 0.001f;
            m_LiftImage.enabled = lift > 0.001f;
        }

        static Texture2D BuildTexture(int seed)
        {
            var rng = new System.Random(seed);
            var raw = new float[Size * Size];
            // Gaussian grain (Box-Muller), then a light 3-tap blur for organic "clumps".
            for (int i = 0; i < raw.Length; i += 2)
            {
                double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                double r = System.Math.Sqrt(-2.0 * System.Math.Log(u1));
                raw[i] = (float)(r * System.Math.Cos(2 * System.Math.PI * u2));
                if (i + 1 < raw.Length) raw[i + 1] = (float)(r * System.Math.Sin(2 * System.Math.PI * u2));
            }

            var blur = new float[raw.Length];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int xl = (x + Size - 1) % Size, xr = (x + 1) % Size;
                int yu = (y + Size - 1) % Size, yd = (y + 1) % Size;
                blur[y * Size + x] = raw[y * Size + x] * 0.5f +
                                     (raw[y * Size + xl] + raw[y * Size + xr] + raw[yu * Size + x] + raw[yd * Size + x]) * 0.125f;
            }

            var px = new Color32[raw.Length];
            for (int i = 0; i < px.Length; i++)
            {
                byte g = (byte)Mathf.Clamp(128 + blur[i] * 52f, 0, 255);
                px[i] = new Color32(g, 128, 0, 255);
            }

            // Dust specks (G channel > 128 = lighten).
            for (int n = 0; n < 36; n++)
            {
                int cx = rng.Next(Size), cy = rng.Next(Size);
                float rad = 0.5f + (float)rng.NextDouble() * 1.1f;
                float strength = 0.15f + (float)rng.NextDouble() * 0.35f;
                int ri = Mathf.CeilToInt(rad + 1);
                for (int dy = -ri; dy <= ri; dy++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(rad + 0.5f - d) * strength;
                    if (a <= 0) continue;
                    int idx = ((cy + dy + Size) % Size) * Size + (cx + dx + Size) % Size;
                    px[idx].g = (byte)Mathf.Max(px[idx].g, 128 + a * 127);
                }
            }

            // A few hairline scratches.
            for (int n = 0; n < 4; n++)
            {
                float x0 = rng.Next(Size), y0 = rng.Next(Size);
                float ang = (float)(rng.NextDouble() * System.Math.PI);
                float len = 30 + rng.Next(160);
                float s = 0.12f + (float)rng.NextDouble() * 0.2f;
                for (int k = 0; k < len; k++)
                {
                    int xx = ((int)(x0 + Mathf.Cos(ang) * k) % Size + Size) % Size;
                    int yy = ((int)(y0 + Mathf.Sin(ang) * k + Mathf.Sin(k * 0.05f) * 3) % Size + Size) % Size;
                    int idx = yy * Size + xx;
                    px[idx].g = (byte)Mathf.Max(px[idx].g, 128 + s * 127 * Mathf.Sin(Mathf.PI * k / len));
                }
            }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                name = "SolarisGrain",
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
