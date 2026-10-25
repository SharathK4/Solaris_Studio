using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Solaris.UI
{
    /// <summary>
    /// Scenes 4 and 5, rebuilt from their mock-ups (Tools/make_tutorials.py): the background and each
    /// glass card come straight from the design; text, dividers and diagrams are re-typeset crisply
    /// at the measured positions. Cards lift slightly (no glow) when the player makes their gesture.
    /// </summary>
    public sealed class TutorialBoard
    {
        [Serializable] public sealed class Line { public float x0, x1, top, baseline, bottom; }

        [Serializable]
        public sealed class CardMeta
        {
            public float[] box = new float[4];
            public Line[] lines = Array.Empty<Line>();
            public float divider;
            public float CenterX => (box[0] + box[2]) * 0.5f;
            public float CenterY => (box[1] + box[3]) * 0.5f;
        }

        [Serializable]
        public sealed class Meta
        {
            public int imageWidth = 1248, imageHeight = 691;
            public float radius = 18;
            public CardMeta[] cards = Array.Empty<CardMeta>();
            public Line[] titleLines = Array.Empty<Line>();
            public float[] button = new float[4];
            public Line buttonLabel;
            public float[] buttonArrow = new float[4];
        }

        public sealed class Card
        {
            public RectTransform Root;
            public RawImage Plate;
            public ShapeGraphic Border, Light;
            public CardMeta Meta;
            public bool Active;
            internal float Lift, Intro = 1f;
            internal Vector2 Home;
            /// <summary>Called on layout with the card-local reference space.</summary>
            public Action<RefSpace> OnLayout;
        }

        public static readonly Color Ink = new Color(0.955f, 0.925f, 0.9f, 1f);
        public readonly Meta M;
        public readonly List<Card> Cards = new List<Card>();
        public PillButton Button { get; }

        /// <summary>An element that fades and rises in. Its resting place is re-read on every layout, because the
        /// canvas is not final yet when the screen is built (Anim.Reveal would freeze that early position).</summary>
        sealed class Rise
        {
            public RectTransform Rt;
            public CanvasGroup Group;
            public Vector2 Home;
            public float Lift, T = 1f;
        }

        readonly List<Rise> m_Rises = new List<Rise>();
        readonly RectTransform m_Root;
        readonly Material m_Bg;
        readonly TextMeshProUGUI m_Title, m_Subtitle;
        Vector2 m_LaidOutFor;
        float m_Scale = 1f;

        public TutorialBoard(RectTransform root, int scene, string title, string subtitle, string button)
        {
            m_Root = root;
            var a = SolarisAssets.Get;
            var metaAsset = a.TutorialMeta($"tut{scene}_meta");
            M = metaAsset != null ? JsonUtility.FromJson<Meta>(metaAsset.text) : new Meta();

            m_Bg = SolarisAssets.Mat("Solaris/BG/TitlePlate");
            m_Bg.SetTexture("_Plate", a.TutorialTexture($"tut{scene}_plate"));
            m_Bg.SetTexture("_Detail", NeutralDetail());     // the built-in "gray" is sRGB: it would darken everything
            m_Bg.SetFloat("_ImageAspect", (float)M.imageWidth / M.imageHeight);
            m_Bg.SetFloat("_Live", 0f);
            var bg = UIKit.Raw(root, Texture2D.whiteTexture, m_Bg, "Backdrop");
            bg.rectTransform.Stretch();

            for (int i = 0; i < M.cards.Length; i++)
            {
                var c = new Card { Meta = M.cards[i] };
                c.Root = UIKit.Node("Card " + i, root);
                c.Plate = UIKit.Raw(c.Root, a.TutorialTexture($"tut{scene}_card{i}"), null, "Glass");
                c.Plate.rectTransform.Stretch();
                c.Light = UIKit.Shape(c.Root, new Color(1f, 0.93f, 0.86f, 0f), 18, "Light");
                c.Light.rectTransform.Stretch();
                c.Border = UIKit.Shape(c.Root, new Color(1, 1, 1, 0), 18, "Border");
                c.Border.rectTransform.Stretch();
                c.Border.BorderWidth = 1f;
                c.Border.BorderFade = 0.35f;
                Cards.Add(c);
            }

            // Opacities below are the design file's (sRGB-blended) values; see Theme.DesignAlpha.
            // Light weights: linear-light blending fattens glyph edges, so Light reads like the design's Regular.
            m_Title = UIKit.Text(root, title, Theme.SansLight, 30, Ink.Design(0.74f), TextAlignmentOptions.Midline, 0f, "Title");
            m_Subtitle = UIKit.Text(root, subtitle, Theme.SansLight, 18, Ink.Design(0.45f), TextAlignmentOptions.Midline, 0f, "Subtitle");

            Button = PillButton.Create(root, button, new Vector2(300, 70));
            Button.Glow = new Color(1f, 0.93f, 0.85f).Design(0.16f);
            Button.Background.color = Theme.Paper;
            Button.Background.FillBottom = Theme.Hex("F1E8DC");
            Button.Label.font = Theme.SansMedium;
            Button.Label.characterSpacing = 0f;
        }

        public void Layout()
        {
            var size = App.Instance.CanvasSize;
            if (size.x <= 1) return;
            m_LaidOutFor = size;
            var rs = new RefSpace(M.imageWidth, M.imageHeight, size);
            m_Scale = rs.Scale;
            m_Bg.SetVector("_Fit", rs.FitVector);

            foreach (var c in Cards)
            {
                var b = c.Meta.box;
                rs.Place(c.Root, b[0], b[1], b[2], b[3]);
                c.Home = c.Root.anchoredPosition;
                float r = M.radius * rs.Scale;
                c.Border.Radius = r;
                c.Light.Radius = r;
                c.Border.BorderWidth = Mathf.Max(1f, rs.Scale * 0.9f);
                c.OnLayout?.Invoke(rs.Local(c.Meta.CenterX, c.Meta.CenterY));
            }

            if (M.titleLines.Length > 0)
            {
                var t = M.titleLines[0];
                rs.FitCaps(m_Title, t.x0, t.x1, t.top, t.baseline);
            }
            if (M.titleLines.Length > 1)
            {
                var t = M.titleLines[1];
                rs.FitLine(m_Subtitle, t.x0, t.x1, t.baseline);
            }

            var bb = M.button;
            var brt = (RectTransform)Button.transform;
            rs.Place(brt, bb[0], bb[1], bb[2], bb[3]);
            Button.Fit();
            var inPill = rs.Local((bb[0] + bb[2]) * 0.5f, (bb[1] + bb[3]) * 0.5f);
            var bl = M.buttonLabel;
            if (bl != null && bl.x1 > bl.x0)
            {
                inPill.FitLine(Button.Label, bl.x0, bl.x1, bl.baseline);
                inPill.AlignLeft(Button.Label, bl.x0, bl.baseline);
            }
            else Button.Label.fontSize = brt.sizeDelta.y * 0.31f;
            var ba = M.buttonArrow;
            if (Button.RightIcon != null && ba != null && ba.Length == 4 && ba[2] > ba[0])
            {
                // The arrow glyph spans x 0.08..0.92 and y 0.22..0.78 of its rect; the stroke adds ~1px of ink.
                float cx = (ba[0] + ba[2]) * 0.5f, cy = (ba[1] + ba[3]) * 0.5f;
                float w = (ba[2] - ba[0] - 1.2f) / 0.84f, h = (ba[3] - ba[1] - 1.2f) / 0.56f;
                inPill.Place(Button.RightIcon.rectTransform, cx - w * 0.5f, cy - h * 0.5f, cx + w * 0.5f, cy + h * 0.5f);
                Button.RightIcon.Stroke = Mathf.Max(1f, 1.35f * rs.Scale);
            }

            foreach (var r in m_Rises) r.Home = r.Rt.anchoredPosition;     // Layout just placed it at rest
        }

        Vector2 Offset(Rise r) => new Vector2(0f, -r.Lift * m_Scale * (1f - r.T));

        /// <summary>Staggered entrance: title, then cards left to right, then the button.</summary>
        public void Enter()
        {
            StartRise(m_Title.rectTransform, 0.25f, 12f, 1.2f);
            StartRise(m_Subtitle.rectTransform, 0.4f, 12f, 1.2f);
            for (int i = 0; i < Cards.Count; i++)
            {
                var c = Cards[i];
                var g = c.Root.Group();
                g.alpha = 0f;
                c.Intro = 0f;
                Anim.Run(1.3f, k => { c.Intro = k; if (g) g.alpha = Mathf.Clamp01(k * 1.4f); }, Ease.OutQuint, 0.45f + i * 0.08f, null, c.Root);
            }
            StartRise((RectTransform)Button.transform, 1.0f, 10f, 1.1f);
        }

        void StartRise(RectTransform rt, float delay, float lift, float duration)
        {
            var r = new Rise { Rt = rt, Group = rt.Group(), Lift = lift, T = 0f, Home = rt.anchoredPosition };
            m_Rises.Add(r);
            r.Group.alpha = 0f;
            Anim.Run(duration, k => r.T = k, Ease.OutQuint, delay, null, rt);
        }

        public void Tick(float dt)
        {
            if (App.Instance.CanvasSize != m_LaidOutFor) Layout();
            foreach (var r in m_Rises)
            {
                if (!r.Rt) continue;
                r.Rt.anchoredPosition = r.Home + Offset(r);
                r.Group.alpha = Mathf.Clamp01(r.T * 1.4f);
            }
            foreach (var c in Cards)
            {
                c.Lift = Mathf.Lerp(c.Lift, c.Active ? 1f : 0f, 1f - Mathf.Exp(-8f * dt));
                c.Root.anchoredPosition = c.Home + new Vector2(0f, (9f * c.Lift - 26f * (1f - c.Intro)) * m_Scale);
                c.Light.color = new Color(1f, 0.93f, 0.86f).Design(0.05f * c.Lift);
                c.Border.Border = new Color(1f, 0.95f, 0.9f).Design(0.1f + 0.14f * c.Lift);
            }
        }

        static Texture2D s_Neutral;

        /// <summary>Signed-offset texture with no offset (0.5 stored as linear data).</summary>
        static Texture2D NeutralDetail()
        {
            if (s_Neutral != null) return s_Neutral;
            s_Neutral = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "NeutralDetail", hideFlags = HideFlags.DontSave };
            s_Neutral.SetPixel(0, 0, new Color(0.5f, 0.5f, 0.5f, 1f));
            s_Neutral.Apply(false, true);
            return s_Neutral;
        }

        // ---------------------------------------------------------------- shared card typography
        /// <summary>Hairline divider across the card at its measured row.</summary>
        public static ShapeGraphic Divider(RectTransform card, CardMeta m)
        {
            var d = UIKit.Shape(card, Ink.Design(0.2f), 0, "Divider");
            return d;
        }

        public static void LayoutDivider(ShapeGraphic d, RefSpace local, CardMeta m, float leftInk)
        {
            float inset = leftInk - m.box[0];
            var a = local.Point(leftInk, m.divider);
            var b = local.Point(m.box[2] - inset, m.divider);
            var rt = d.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = (a + b) * 0.5f;
            rt.sizeDelta = new Vector2(b.x - a.x, Mathf.Max(1f, local.Scale * 0.9f));
        }

        /// <summary>Body copy, one TMP per measured line so every baseline matches the design.</summary>
        public static List<TextMeshProUGUI> BodyLines(RectTransform card, string[] lines)
        {
            var list = new List<TextMeshProUGUI>();
            foreach (var l in lines)
                list.Add(UIKit.Text(card, l, Theme.SansLight, 16, Ink.Design(0.74f), TextAlignmentOptions.Midline, 0f, "Body"));
            return list;
        }

        public static void LayoutBody(List<TextMeshProUGUI> texts, RefSpace local, IList<Line> measured)
        {
            if (texts.Count == 0 || measured.Count == 0) return;
            // One size for the whole paragraph, solved from its longest line.
            int widest = 0;
            for (int i = 1; i < Mathf.Min(texts.Count, measured.Count); i++)
                if (measured[i].x1 - measured[i].x0 > measured[widest].x1 - measured[widest].x0) widest = i;
            var w = measured[widest];
            local.FitLine(texts[widest], w.x0, w.x1, w.baseline);
            float size = texts[widest].fontSize;
            for (int i = 0; i < texts.Count && i < measured.Count; i++)
            {
                var t = texts[i];
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(2000f, 400f);
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.alignment = TextAlignmentOptions.Midline;
                t.fontSize = size;
                local.AlignLeft(t, measured[i].x0, measured[i].baseline);
            }
        }
    }
}
