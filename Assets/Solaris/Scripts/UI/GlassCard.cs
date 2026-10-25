using UnityEngine;

namespace Solaris.UI
{
    /// <summary>
    /// Smoked-glass tutorial card: translucent warm fill, hairline gradient rim, inner glow; lifts
    /// and brightens when <see cref="Active"/> (e.g. the player is making this gesture right now).
    /// </summary>
    public sealed class GlassCard : MonoBehaviour
    {
        public ShapeGraphic Shape { get; private set; }
        public RectTransform Content { get; private set; }
        public bool Active;
        public float Lift = 16f;

        static readonly Color k_Rim = Theme.Hex("F4B58A"), k_Fill = Theme.Hex("5A1E0E").A(0.34f), k_FillHot = Theme.Hex("9A3614").A(0.5f);
        RectTransform m_Rt;
        Vector2 m_Home;
        float m_Active, m_Intro = 1f;
        bool m_HomeSet;

        /// <summary>Fade and rise into place after a delay.</summary>
        public void Appear(float delay, float duration = 1.3f)
        {
            var g = this.Group();
            g.alpha = 0f;
            m_Intro = 0f;
            Anim.Run(duration, k => { m_Intro = k; if (g) g.alpha = Mathf.Clamp01(k * 1.5f); }, Ease.OutQuint, delay, null, this);
        }

        public static GlassCard Create(Transform parent, Vector2 size, string name = "Card")
        {
            var rt = UIKit.Node(name, parent);
            rt.sizeDelta = size;
            var c = rt.gameObject.AddComponent<GlassCard>();
            c.m_Rt = rt;
            c.Shape = rt.gameObject.AddComponent<ShapeGraphic>();
            c.Shape.Radius = 24f;
            c.Shape.color = Theme.Hex("5A1E0E").A(0.34f);
            c.Shape.FillBottom = Theme.Hex("1A0703").A(0.62f);
            c.Shape.BorderWidth = 1.2f;
            c.Shape.Border = Theme.Hex("F4B58A").A(0.32f);
            c.Shape.BorderFade = 0.75f;
            c.Shape.InnerGlow = 0.18f;
            c.Shape.ShadowSize = 30f;
            c.Shape.Shadow = new Color(0, 0, 0, 0.35f);
            c.Shape.raycastTarget = false;
            c.Content = UIKit.Node("Content", rt);
            c.Content.Stretch();
            return c;
        }

        public void SetHome(Vector2 pos)
        {
            m_Home = pos;
            m_HomeSet = true;
            m_Rt.anchoredPosition = pos;
        }

        void Update()
        {
            if (!m_HomeSet) return;
            float dt = Time.unscaledDeltaTime;
            m_Active = Mathf.Lerp(m_Active, Active ? 1f : 0f, 1f - Mathf.Exp(-9f * dt));
            m_Rt.anchoredPosition = m_Home + new Vector2(0, Lift * m_Active - 40f * (1f - m_Intro));
            Shape.Border = k_Rim.A(0.32f + 0.5f * m_Active);
            Shape.InnerGlow = 0.18f + 0.55f * m_Active;
            Shape.color = Color.Lerp(k_Fill, k_FillHot, m_Active);
            Shape.Shadow = Color.Lerp(new Color(0, 0, 0, 0.35f), Theme.Ember.A(0.35f), m_Active);
            Shape.ShadowSize = 30f + 16f * m_Active;
        }
    }
}
