using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Solaris.UI
{
    /// <summary>Shared editorial chrome: small wordmark top-left, numbered step rail top-right.</summary>
    public static class Chrome
    {
        static readonly string[] k_Steps = { "Song", "Chords", "Controls", "Instrument", "Play" };

        public static RectTransform Header(RectTransform root, int step, Color color)
        {
            var header = UIKit.Node("Chrome", root);
            header.Stretch();

            var sdf = SolarisAssets.Get.wordmarkSdf;
            var mark = UIKit.Raw(header, sdf, SolarisAssets.Mat("Solaris/UI/Logo"), "Wordmark");
            float w = 150f;
            mark.rectTransform.Place(new Vector2(0, 1), new Vector2(46, -34), new Vector2(w, w * sdf.height / sdf.width), new Vector2(0, 1));
            mark.color = color.A(0.9f);
            mark.material.SetFloat("_ShadowAlpha", 0f);

            float x = -56f;
            for (int i = k_Steps.Length - 1; i >= 0; i--)
            {
                bool active = i == step;
                bool done = i < step;
                var label = UIKit.Text(header, $"{i + 1:00}  {k_Steps[i].ToUpperInvariant()}", Theme.Sans, 11,
                    color.A(active ? 0.95f : done ? 0.55f : 0.3f), TextAlignmentOptions.Right, 22f, "Step");
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.rectTransform.Place(new Vector2(1, 1), new Vector2(x, -44), new Vector2(130, 20), new Vector2(1, 0.5f));
                if (active)
                {
                    var bar = UIKit.Shape(header, color.A(0.9f), 1, "Active");
                    bar.rectTransform.Place(new Vector2(1, 1), new Vector2(x, -58), new Vector2(label.preferredWidth, 1.5f), new Vector2(1, 0.5f));
                }
                x -= label.preferredWidth + 34f;
            }
            return header;
        }

        /// <summary>Small caps hint with a leading icon, e.g. "↻ CIRCLE YOUR INDEX FINGER TO BROWSE".</summary>
        public static RectTransform Hint(RectTransform parent, VectorIcon.Kind icon, string text, Color color)
        {
            var rt = UIKit.Node("Hint", parent);
            var i = UIKit.Icon(rt, icon, color, 18, 1.4f);
            i.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(18, 18), new Vector2(0, 0.5f));
            var t = UIKit.Text(rt, text.ToUpperInvariant(), Theme.Sans, 11.5f, color, TextAlignmentOptions.Left, 24f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(t.preferredWidth + 4, 20), new Vector2(0, 0.5f));
            rt.sizeDelta = new Vector2(30 + t.preferredWidth + 4, 22);
            return rt;
        }

        /// <summary>Full-screen mirrored camera image with the warm duotone grade.</summary>
        public static RawImage CameraBackdrop(RectTransform parent, float duotone, float alpha)
        {
            var img = UIKit.Raw(parent, null, SolarisAssets.Mat("Solaris/CameraView"), "Camera");
            img.rectTransform.Stretch();
            img.material.SetFloat("_Duotone", duotone);
            img.material.SetFloat("_Vignette", 0.9f);
            img.color = new Color(1, 1, 1, alpha);
            img.gameObject.AddComponent<CameraBackdrop>();
            return img;
        }
    }

    /// <summary>Keeps a CameraView material bound to the live frame and cover-fitted to the screen.</summary>
    public sealed class CameraBackdrop : MonoBehaviour
    {
        RawImage m_Img;
        float m_Target = 1f;
        float m_Alpha, m_Spot;
        Vector2 m_SpotPos = new Vector2(0.5f, 0.5f);

        /// <summary>When on, a soft warm light follows the index fingertip across the camera image.</summary>
        public bool Spotlight;

        void Awake()
        {
            m_Img = GetComponent<RawImage>();
            m_Target = m_Img.color.a;
            m_Alpha = 0f;
        }

        void LateUpdate()
        {
            var feed = App.Instance.Feed;
            bool live = feed.IsRunning && feed.Frame != null;
            m_Img.texture = live ? feed.Frame : null;
            var rect = ((RectTransform)transform).rect;
            float aspect = rect.height > 0 ? rect.width / rect.height : 1.78f;
            m_Img.material.SetVector("_Cover", feed.CoverTransform(aspect));
            m_Img.material.SetFloat("_Mirror", feed.Mirror ? 1f : 0f);
            m_Alpha = Mathf.Lerp(m_Alpha, live ? m_Target : 0f, 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime));
            var c = m_Img.color;
            c.a = m_Alpha;
            m_Img.color = c;
            m_Img.enabled = m_Alpha > 0.002f;

            if (Spotlight)
            {
                var g = App.Instance.Gestures;
                float dt = Time.unscaledDeltaTime;
                m_Spot = Mathf.Lerp(m_Spot, g.PointerVisible ? 1f : 0f, 1f - Mathf.Exp(-4f * dt));
                if (g.PointerVisible) m_SpotPos = Vector2.Lerp(m_SpotPos, feed.FrameToView(g.PointerUv, aspect), 1f - Mathf.Exp(-12f * dt));
                m_Img.material.SetVector("_Spot", new Vector4(m_SpotPos.x, m_SpotPos.y, 0.24f, m_Spot));
                m_Img.material.SetFloat("_ViewAspect", aspect);
            }
        }
    }
}
