using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Solaris.UI
{
    /// <summary>
    /// Anti-aliased strokes (segments, polylines, arcs) in the rect's normalised space (0..1).
    /// Edge AA is analytic in the shader, so hairlines stay crisp at any scale.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class LineGraphic : MaskableGraphic
    {
        public struct Segment
        {
            public Vector2 A, B;
            public float Width;   // canvas px; <= 0 uses the default stroke
            public float Alpha;
        }

        protected readonly List<Segment> Segments = new List<Segment>(64);
        [SerializeField] float m_Stroke = 2f;
        static Material s_Material;

        public float Stroke { get => m_Stroke; set { m_Stroke = value; SetVerticesDirty(); } }

        public override Material defaultMaterial
        {
            get
            {
                if (s_Material == null) s_Material = SolarisAssets.Mat("Solaris/UI/Line");
                return s_Material;
            }
        }

        public void Clear()
        {
            Segments.Clear();
            SetVerticesDirty();
        }

        public void Line(Vector2 a, Vector2 b, float width = -1f, float alpha = 1f)
        {
            Segments.Add(new Segment { A = a, B = b, Width = width, Alpha = alpha });
            SetVerticesDirty();
        }

        public void Polyline(IReadOnlyList<Vector2> pts, float width = -1f, float alpha = 1f)
        {
            for (int i = 1; i < pts.Count; i++) Line(pts[i - 1], pts[i], width, alpha);
        }

        /// <summary>Arc in normalised space; angles in degrees, 0 = +x, counter-clockwise.</summary>
        public void Arc(Vector2 center, Vector2 radius, float fromDeg, float toDeg, int steps = 32, float width = -1f, float alpha = 1f)
        {
            Vector2 prev = default;
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, i / (float)steps);
                var p = center + new Vector2(Mathf.Cos(a) * radius.x, Mathf.Sin(a) * radius.y);
                if (i > 0) Line(prev, p, width, alpha);
                prev = p;
            }
        }

        protected virtual void Rebuild() { }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rebuild();
            var r = GetPixelAdjustedRect();
            var c = (Color32)color;
            foreach (var s in Segments)
            {
                var a = new Vector2(r.xMin + s.A.x * r.width, r.yMin + s.A.y * r.height);
                var b = new Vector2(r.xMin + s.B.x * r.width, r.yMin + s.B.y * r.height);
                float w = (s.Width > 0 ? s.Width : m_Stroke) * 0.5f;
                var dir = b - a;
                float len = dir.magnitude;
                if (len < 1e-4f) continue;
                dir /= len;
                var n = new Vector2(-dir.y, dir.x);
                float pad = 1.0f;                    // 1px AA skirt
                float hw = w + pad;
                // Extend ends by half-width for rounded-ish caps.
                var a2 = a - dir * w;
                var b2 = b + dir * w;
                int i0 = vh.currentVertCount;
                var col = c;
                col.a = (byte)(c.a * Mathf.Clamp01(s.Alpha));
                Add(vh, a2 - n * hw, col, -hw, w, len + 2 * w, 0);
                Add(vh, a2 + n * hw, col, hw, w, len + 2 * w, 0);
                Add(vh, b2 + n * hw, col, hw, w, len + 2 * w, len + 2 * w);
                Add(vh, b2 - n * hw, col, -hw, w, len + 2 * w, len + 2 * w);
                vh.AddTriangle(i0, i0 + 1, i0 + 2);
                vh.AddTriangle(i0 + 2, i0 + 3, i0);
            }
        }

        static void Add(VertexHelper vh, Vector2 p, Color32 c, float across, float halfWidth, float length, float along)
        {
            var v = UIVertex.simpleVert;
            v.position = p;
            v.color = c;
            v.uv0 = new Vector4(along, across, halfWidth, length);
            vh.AddVert(v);
        }
    }

    /// <summary>Tiny built-in icon set drawn as strokes.</summary>
    public sealed class VectorIcon : LineGraphic
    {
        public enum Kind { ArrowRight, ArrowLeft, Play, Pause, Rotate, Pinch, Hand, Check, Waveform, Enter, Close, Up, Down }

        [SerializeField] Kind m_Shape;
        public Kind Shape { get => m_Shape; set { m_Shape = value; SetVerticesDirty(); } }

        protected override void Rebuild()
        {
            Segments.Clear();
            switch (m_Shape)
            {
                case Kind.ArrowRight:
                    Add(0.08f, 0.5f, 0.92f, 0.5f); Add(0.62f, 0.78f, 0.92f, 0.5f); Add(0.62f, 0.22f, 0.92f, 0.5f);
                    break;
                case Kind.ArrowLeft:
                    Add(0.92f, 0.5f, 0.08f, 0.5f); Add(0.38f, 0.78f, 0.08f, 0.5f); Add(0.38f, 0.22f, 0.08f, 0.5f);
                    break;
                case Kind.Up:
                    Add(0.5f, 0.08f, 0.5f, 0.92f); Add(0.22f, 0.62f, 0.5f, 0.92f); Add(0.78f, 0.62f, 0.5f, 0.92f);
                    break;
                case Kind.Down:
                    Add(0.5f, 0.92f, 0.5f, 0.08f); Add(0.22f, 0.38f, 0.5f, 0.08f); Add(0.78f, 0.38f, 0.5f, 0.08f);
                    break;
                case Kind.Play:
                    // Filled look: dense strokes inside a triangle.
                    for (int i = 0; i <= 8; i++)
                    {
                        float t = i / 8f;
                        Add(0.2f + 0.02f, 0.12f + 0.76f * t, 0.86f, 0.5f);
                    }
                    Add(0.22f, 0.12f, 0.22f, 0.88f);
                    break;
                case Kind.Pause:
                    Add(0.32f, 0.15f, 0.32f, 0.85f); Add(0.68f, 0.15f, 0.68f, 0.85f);
                    break;
                case Kind.Rotate:
                    Arc(new Vector2(0.5f, 0.5f), new Vector2(0.36f, 0.36f), 110f, 400f, 36);
                    Add(0.5f + 0.36f * Mathf.Cos(Mathf.Deg2Rad * 40f), 0.5f + 0.36f * Mathf.Sin(Mathf.Deg2Rad * 40f), 0.93f, 0.58f);
                    Add(0.5f + 0.36f * Mathf.Cos(Mathf.Deg2Rad * 40f), 0.5f + 0.36f * Mathf.Sin(Mathf.Deg2Rad * 40f), 0.66f, 0.95f);
                    break;
                case Kind.Pinch:
                    // Thumb and index fingertips meeting: two touching rings with a contact dot.
                    Arc(new Vector2(0.3f, 0.5f), new Vector2(0.24f, 0.24f), 0f, 360f, 28);
                    Arc(new Vector2(0.7f, 0.5f), new Vector2(0.24f, 0.24f), 0f, 360f, 28);
                    Segments.Add(new Segment { A = new Vector2(0.5f, 0.5f), B = new Vector2(0.505f, 0.5f), Width = 4f, Alpha = 1f });
                    break;
                case Kind.Hand:
                    Add(0.2f, 0.45f, 0.2f, 0.62f); Add(0.38f, 0.4f, 0.38f, 0.88f); Add(0.54f, 0.4f, 0.54f, 0.94f);
                    Add(0.7f, 0.4f, 0.7f, 0.86f); Add(0.84f, 0.42f, 0.84f, 0.74f);
                    Arc(new Vector2(0.52f, 0.36f), new Vector2(0.32f, 0.3f), 180f, 360f, 16);
                    break;
                case Kind.Check:
                    Add(0.12f, 0.52f, 0.4f, 0.24f); Add(0.4f, 0.24f, 0.9f, 0.78f);
                    break;
                case Kind.Waveform:
                    float[] h = { 0.2f, 0.42f, 0.7f, 0.95f, 0.62f, 0.36f, 0.18f };
                    for (int i = 0; i < h.Length; i++)
                    {
                        float x = 0.08f + i * (0.84f / (h.Length - 1));
                        Add(x, 0.5f - h[i] * 0.5f, x, 0.5f + h[i] * 0.5f);
                    }
                    break;
                case Kind.Enter:
                    Add(0.85f, 0.8f, 0.85f, 0.42f); Add(0.85f, 0.42f, 0.15f, 0.42f);
                    Add(0.15f, 0.42f, 0.36f, 0.62f); Add(0.15f, 0.42f, 0.36f, 0.22f);
                    break;
                case Kind.Close:
                    Add(0.2f, 0.2f, 0.8f, 0.8f); Add(0.2f, 0.8f, 0.8f, 0.2f);
                    break;
            }
        }

        void Add(float ax, float ay, float bx, float by) =>
            Segments.Add(new Segment { A = new Vector2(ax, ay), B = new Vector2(bx, by), Width = -1, Alpha = 1 });
    }
}
