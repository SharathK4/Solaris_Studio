using TMPro;
using UnityEngine;

namespace Solaris.UI
{
    /// <summary>
    /// Maps pixel coordinates of a reference mock-up (origin top-left) onto the canvas, using the same
    /// "fit width (or height), extend the rest" rule as the art shaders, so crisp UI drawn here lands
    /// exactly where it sits in the reference image on any screen shape.
    /// </summary>
    public readonly struct RefSpace
    {
        public readonly float W, H;
        public readonly Vector2 Canvas, Fit, Origin;

        public RefSpace(float imageWidth, float imageHeight, Vector2 canvas) : this(imageWidth, imageHeight, canvas, Vector2.zero) { }

        RefSpace(float imageWidth, float imageHeight, Vector2 canvas, Vector2 origin)
        {
            W = imageWidth;
            H = imageHeight;
            Canvas = canvas;
            float screenAspect = canvas.x / Mathf.Max(1f, canvas.y);
            float imageAspect = imageWidth / imageHeight;
            Fit = new Vector2(Mathf.Max(1f, screenAspect / imageAspect), Mathf.Max(1f, imageAspect / screenAspect));
            Origin = origin;
        }

        /// <summary>Same mapping, but positions relative to the reference point (x, y) - for children of a moving card.</summary>
        public RefSpace Local(float x, float y) => new RefSpace(W, H, Canvas, new RefSpace(W, H, Canvas).Point(x, y));

        /// <summary>Canvas units per reference pixel.</summary>
        public float Scale => Canvas.x / (W * Fit.x);

        public Vector4 FitVector => new Vector4(Fit.x, Fit.y, 0, 0);

        /// <summary>Reference pixel (x right, y down) to canvas position relative to the canvas centre.</summary>
        public Vector2 Point(float x, float y) =>
            new Vector2((x / W - 0.5f) / Fit.x * Canvas.x, (0.5f - y / H) / Fit.y * Canvas.y) - Origin;

        /// <summary>Places a centre-anchored rect over the reference pixel box [x0,x1] x [y0,y1].</summary>
        public void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var a = Point(x0, y0);
            var b = Point(x1, y1);
            rt.anchoredPosition = (a + b) * 0.5f;
            rt.sizeDelta = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        }

        /// <summary>
        /// Sizes a single line so its ink spans [x0, x1] and puts its baseline on <paramref name="baseline"/>
        /// (all reference px). The text keeps the font's own spacing; only the size is solved.
        /// </summary>
        public void FitLine(TMP_Text t, float x0, float x1, float baseline, float sizeHint = 40f)
        {
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Canvas.x * 2f, Canvas.y);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.alignment = TextAlignmentOptions.Midline;
            t.fontSize = sizeHint;
            t.ForceMeshUpdate();
            float ink = Ink(t).width;
            if (ink > 1e-3f) t.fontSize = sizeHint * ((x1 - x0) * Scale) / ink;
            t.ForceMeshUpdate();
            AlignLine(t, x0, x1, baseline);
        }

        /// <summary>Sizes text so its ink rises from the baseline to <paramref name="top"/> (reference px).</summary>
        public void FitHeight(TMP_Text t, float top, float baseline, float sizeHint = 40f)
        {
            Prepare(t, sizeHint);
            float h = Ink(t).yMax - Baseline(t);
            if (h > 1e-3f) t.fontSize = sizeHint * ((baseline - top) * Scale) / h;
            t.ForceMeshUpdate();
        }

        /// <summary>Letter-spaced caps: cap height from top..baseline, tracking solved so the ink spans x0..x1.</summary>
        public void FitCaps(TMP_Text t, float x0, float x1, float top, float baseline)
        {
            t.characterSpacing = 0f;
            FitHeight(t, top, baseline);
            int n = 0;
            foreach (var c in t.textInfo.characterInfo)
                if (c.isVisible) n++;
            n = Mathf.Min(n, t.textInfo.characterCount);
            if (n > 1)
            {
                float extra = (x1 - x0) * Scale - Ink(t).width;
                t.characterSpacing = extra / (n - 1) / t.fontSize * 100f;
                t.ForceMeshUpdate();
            }
            AlignLeft(t, x0, baseline);
        }

        static void Prepare(TMP_Text t, float size)
        {
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(4000f, 1000f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.alignment = TextAlignmentOptions.Midline;
            t.fontSize = size;
            t.ForceMeshUpdate();
        }

        /// <summary>Keeps the current size; centres the ink on [x0,x1] and sets the baseline.</summary>
        public void AlignLine(TMP_Text t, float x0, float x1, float baseline)
        {
            var rt = t.rectTransform;
            t.ForceMeshUpdate();
            var ink = Ink(t);
            var centre = Point((x0 + x1) * 0.5f, baseline);
            rt.anchoredPosition += new Vector2(centre.x - (rt.anchoredPosition.x + ink.center.x),
                                               centre.y - (rt.anchoredPosition.y + Baseline(t)));
        }

        /// <summary>Left-aligns the ink at x0 and sets the baseline (reference px).</summary>
        public void AlignLeft(TMP_Text t, float x0, float baseline)
        {
            var rt = t.rectTransform;
            t.ForceMeshUpdate();
            var ink = Ink(t);
            var p = Point(x0, baseline);
            rt.anchoredPosition += new Vector2(p.x - (rt.anchoredPosition.x + ink.xMin), p.y - (rt.anchoredPosition.y + Baseline(t)));
        }

        /// <summary>
        /// Bounds of the glyphs' actual ink (from glyph metrics) in the text's local space. TMP's own
        /// textBounds spans pen advances and the ascender line, so fitting measured ink to it would
        /// shrink capitals by roughly the cap-height/ascender ratio.
        /// </summary>
        public static Rect Ink(TMP_Text t)
        {
            var info = t.textInfo;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < info.characterCount; i++)
            {
                var c = info.characterInfo[i];
                if (!c.isVisible || c.textElement == null || c.textElement.glyph == null) continue;
                var g = c.textElement.glyph.metrics;
                float s = c.scale;
                float left = c.origin + g.horizontalBearingX * s;
                float top = c.baseLine + g.horizontalBearingY * s;
                x0 = Mathf.Min(x0, left);
                x1 = Mathf.Max(x1, left + g.width * s);
                y1 = Mathf.Max(y1, top);
                y0 = Mathf.Min(y0, top - g.height * s);
            }
            if (x0 > x1)
            {
                var b = t.textBounds;
                return new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
            }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        static float Baseline(TMP_Text t) => t.textInfo.lineCount > 0 ? t.textInfo.lineInfo[0].baseline : Ink(t).yMin;
    }

    /// <summary>Letters fade and rise into place one after another, then the component steps aside.</summary>
    [RequireComponent(typeof(TMP_Text))]
    public sealed class TextReveal : MonoBehaviour
    {
        public float Delay, Stagger = 0.035f, Duration = 0.9f, Rise = 10f;
        TMP_Text m_Text;
        float m_T;
        bool m_Done;

        void Awake() => m_Text = GetComponent<TMP_Text>();

        void OnEnable()
        {
            m_T = 0f;
            m_Done = false;
        }

        void LateUpdate()
        {
            if (m_Done) return;
            m_T += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            m_Text.ForceMeshUpdate();
            var info = m_Text.textInfo;
            bool all = true;
            for (int i = 0; i < info.characterCount; i++)
            {
                var c = info.characterInfo[i];
                if (!c.isVisible) continue;
                float k = Mathf.Clamp01((m_T - Delay - i * Stagger) / Duration);
                if (k < 1f) all = false;
                float e = Ease.OutCubic(k);
                var mesh = info.meshInfo[c.materialReferenceIndex];
                byte alpha = (byte)(c.color.a * e);
                for (int v = 0; v < 4; v++)
                {
                    mesh.colors32[c.vertexIndex + v].a = alpha;
                    mesh.vertices[c.vertexIndex + v] += new Vector3(0, -Rise * (1f - e), 0);
                }
            }
            m_Text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32 | TMP_VertexDataUpdateFlags.Vertices);
            if (all)
            {
                m_Done = true;
                m_Text.ForceMeshUpdate();
            }
        }
    }
}
