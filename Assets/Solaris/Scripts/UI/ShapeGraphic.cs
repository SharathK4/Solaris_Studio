using UnityEngine;
using UnityEngine.UI;

namespace Solaris.UI
{
    /// <summary>
    /// Resolution-independent rounded rectangle drawn with a signed distance field: vertical fill
    /// gradient, gradient hairline border, soft drop shadow / glow. All per-vertex, so any number
    /// of shapes batch into one draw call. Shader: Solaris/UI/Shape.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ShapeGraphic : MaskableGraphic
    {
        [SerializeField] float m_Radius = 16f;
        [SerializeField] float m_BorderWidth;
        [SerializeField] Color m_Border = new Color(1, 1, 1, 0);
        [SerializeField] Color m_FillBottom = new Color(1, 1, 1, -1);
        [SerializeField] float m_BorderFade;
        [SerializeField] float m_ShadowSize;
        [SerializeField] Color m_Shadow = new Color(0, 0, 0, 0);
        [SerializeField] float m_InnerGlow;

        static Material s_Material;

        public override Material defaultMaterial
        {
            get
            {
                if (s_Material == null) s_Material = SolarisAssets.Mat("Solaris/UI/Shape");
                return s_Material;
            }
        }

        public float Radius { get => m_Radius; set { m_Radius = value; SetVerticesDirty(); } }
        public float BorderWidth { get => m_BorderWidth; set { m_BorderWidth = value; SetVerticesDirty(); } }
        public Color Border { get => m_Border; set { m_Border = value; SetVerticesDirty(); } }
        /// <summary>Fill colour at the bottom edge; alpha &lt; 0 means "same as top" (flat fill).</summary>
        public Color FillBottom { get => m_FillBottom; set { m_FillBottom = value; SetVerticesDirty(); } }
        /// <summary>0 = uniform border, 1 = border fades out completely toward the bottom.</summary>
        public float BorderFade { get => m_BorderFade; set { m_BorderFade = value; SetVerticesDirty(); } }
        public float ShadowSize { get => m_ShadowSize; set { m_ShadowSize = value; SetVerticesDirty(); } }
        public Color Shadow { get => m_Shadow; set { m_Shadow = value; SetVerticesDirty(); } }
        /// <summary>Soft light that hugs the inside of the edge (glass rim).</summary>
        public float InnerGlow { get => m_InnerGlow; set { m_InnerGlow = value; SetVerticesDirty(); } }

        static Vector4 Pack(Color c)
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                var l = c.linear;
                return new Vector4(l.r, l.g, l.b, c.a);
            }
            return c;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            if (r.width <= 0 || r.height <= 0) return;

            float margin = Mathf.Max(1.5f, m_ShadowSize * 2.2f);
            var half = r.size * 0.5f;
            var center = r.center;
            float radius = Mathf.Min(m_Radius, Mathf.Min(half.x, half.y));

            var bottom = m_FillBottom.a < 0f ? (Color)color : m_FillBottom;
            var uv1 = new Vector4(radius, m_BorderWidth, m_ShadowSize, m_BorderFade);
            var uv2 = Pack(m_Border);
            var uv3 = Pack(bottom);
            var shadow = Pack(m_Shadow);
            var normal = new Vector3(shadow.x, shadow.y, shadow.z);
            var tangent = new Vector4(shadow.w, m_InnerGlow, color.a, 0);

            // Vertex alpha is left at 1 so the canvas can multiply CanvasGroup fades into it.
            var c = (Color32)new Color(color.r, color.g, color.b, 1f);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i == 1 || i == 2) ? 1f : -1f;
                float sy = (i >= 2) ? -1f : 1f;
                var local = new Vector2(sx * (half.x + margin), sy * (half.y + margin));
                var v = UIVertex.simpleVert;
                v.position = center + local;
                v.color = c;
                v.uv0 = new Vector4(local.x, local.y, half.x, half.y);
                v.uv1 = uv1;
                v.uv2 = uv2;
                v.uv3 = uv3;
                v.normal = normal;
                v.tangent = tangent;
                vh.AddVert(v);
            }
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        public override bool Raycast(Vector2 sp, Camera eventCamera)
        {
            if (!base.Raycast(sp, eventCamera)) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rectTransform, sp, eventCamera);
        }
    }
}
