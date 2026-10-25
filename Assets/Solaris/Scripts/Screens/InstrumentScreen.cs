using Solaris.Rendering;
using Solaris.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Solaris.Screens
{
    /// <summary>Scene 6 — "Jamming Session": pick an instrument (acoustic guitar today, more to come).</summary>
    public sealed class InstrumentScreen : AppScreen
    {
        public override ScreenId Id => ScreenId.Instruments;
        public override VisionNeeds Vision => VisionNeeds.Hands;
        public override float Grain => 0.06f;

        static readonly (string id, string name, bool ready)[] k_Instruments =
        {
            ("acoustic_guitar", "Acoustic Guitar", true),
            ("electric_guitar", "Electric Guitar", false),
            ("piano", "Piano", false),
            ("drums", "Drums", false),
            ("ukulele", "Ukulele", false),
            ("keyboard", "Keyboard", false),
            ("violin", "Violin", false),
        };

        class Card
        {
            public RectTransform Rt;
            public ShapeGraphic Bg, Bar;
            public TextMeshProUGUI Name, Tag;
            public RawImage Image;
            public PillButton Hit;
            public float Sel;
        }

        readonly Card[] m_Cards = new Card[k_Instruments.Length];
        int m_Selected;
        LineGraphic m_Wave;
        RectTransform m_Hero, m_Row;
        TextMeshProUGUI m_Toast;
        CanvasGroup m_ToastGroup;
        PillButton m_Start;
        float m_T;
        static readonly Color k_Dark = Theme.Hex("2A1107").A(0.72f), k_DarkBottom = Theme.Hex("120602").A(0.85f),
            k_Light = Theme.Hex("FBF6EE"), k_LightBottom = Theme.Hex("F1E7D9"), k_Rim = Theme.Hex("F4B58A");

        protected override void Build()
        {
            var bg = ProceduralLayer.Create(Root, "Solaris/BG/GlowField", 0.5f);
            var m = bg.Material;
            m.SetColor("_Top", Theme.Hex("3A1706"));
            m.SetColor("_Bottom", Theme.Hex("070201"));
            m.SetVector("_G0", new Vector4(0.5f, 1.1f, 0.5f, 1.05f));
            m.SetColor("_C0", Theme.Hex("F0A447"));
            m.SetVector("_G1", new Vector4(0.5f, 0.98f, 0.9f, 0.8f));
            m.SetColor("_C1", Theme.Hex("B24A16"));
            m.SetVector("_G2", new Vector4(0.08f, 0.8f, 0.5f, 0.35f));
            m.SetColor("_C2", Theme.Hex("8A3312"));
            m.SetVector("_G3", new Vector4(0.92f, 0.8f, 0.5f, 0.35f));
            m.SetColor("_C3", Theme.Hex("8A3312"));
            m.SetFloat("_Smoke", 0.3f);
            m.SetFloat("_Pulse", 0.6f);

            Chrome.Header(Root, 3, Theme.Cream);

            m_Hero = UIKit.Node("Hero", Root).Center(new Vector2(0, 225), new Vector2(1000, 380));
            m_Wave = UIKit.Node("Waveform", m_Hero).gameObject.AddComponent<LineGraphic>();
            m_Wave.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(44, 56), new Vector2(0.5f, 1));
            m_Wave.color = Theme.Paper;
            m_Wave.Stroke = 2.2f;
            m_Wave.raycastTarget = false;

            var title = UIKit.Text(m_Hero, "Jamming Session", Theme.Serif, 80, Theme.Paper, TextAlignmentOptions.Center, -0.5f);
            title.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(1000, 110), new Vector2(0.5f, 1));
            var sub = UIKit.Text(m_Hero, "PICK AN INSTRUMENT AND START PLAYING", Theme.Sans, 14, Theme.Paper.A(0.8f), TextAlignmentOptions.Center, 52f);
            sub.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -196), new Vector2(1000, 24), new Vector2(0.5f, 1));

            m_Start = PillButton.Create(m_Hero, "Start Jamming", new Vector2(340, 78), VectorIcon.Kind.Play);
            m_Start.GetComponent<RectTransform>().Place(new Vector2(0.5f, 1), new Vector2(0, -250), new Vector2(340, 78), new Vector2(0.5f, 1));
            m_Start.Clicked += StartJamming;

            m_Row = UIKit.Node("Instruments", Root).Center(new Vector2(0, -250), new Vector2(1740, 420));
            var lbl = UIKit.Text(m_Row, "INSTRUMENTS", Theme.Sans, 13, Theme.Paper.A(0.9f), TextAlignmentOptions.Left, 40f);
            lbl.rectTransform.Place(new Vector2(0, 1), new Vector2(10, 0), new Vector2(300, 22), new Vector2(0, 1));
            var scroll = UIKit.Text(m_Row, "SCROLL", Theme.Sans, 13, Theme.Paper.A(0.9f), TextAlignmentOptions.Right, 40f);
            scroll.rectTransform.Place(new Vector2(1, 1), new Vector2(-40, 0), new Vector2(300, 22), new Vector2(1, 1));
            var arrow = UIKit.Icon(m_Row, VectorIcon.Kind.ArrowRight, Theme.Paper.A(0.9f), 16, 1.4f);
            arrow.rectTransform.Place(new Vector2(1, 1), new Vector2(-8, -3), new Vector2(18, 14), new Vector2(1, 1));

            var size = new Vector2(226, 326);
            float gap = 24f;
            float x0 = -(size.x * 7 + gap * 6) * 0.5f;
            for (int i = 0; i < k_Instruments.Length; i++) m_Cards[i] = CreateCard(i, new Vector2(x0 + i * (size.x + gap), -50), size);

            m_Toast = UIKit.Text(Root, "", Theme.Sans, 14, Theme.Paper, TextAlignmentOptions.Center, 4f);
            m_Toast.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(900, 24), new Vector2(0.5f, 0));
            m_ToastGroup = m_Toast.Group();
            m_ToastGroup.alpha = 0;

            var pointer = HandPointer.Create(Root);
            pointer.Register(m_Start);
            foreach (var c in m_Cards) pointer.Register(c.Hit);
            Pick(0, false);
        }

        Card CreateCard(int i, Vector2 pos, Vector2 size)
        {
            var info = k_Instruments[i];
            var c = new Card();
            c.Rt = UIKit.Node("Card " + info.name, m_Row).Place(new Vector2(0.5f, 1), pos, size, new Vector2(0, 1));
            c.Rt.anchorMin = c.Rt.anchorMax = new Vector2(0.5f, 1);
            c.Bg = c.Rt.gameObject.AddComponent<ShapeGraphic>();
            c.Bg.Radius = 16;
            c.Bg.BorderWidth = 1.2f;
            c.Bg.BorderFade = 0.3f;
            c.Bg.ShadowSize = 24;

            var tex = FindArt(info.id);
            c.Image = UIKit.Raw(c.Rt, tex, null, "Art");
            c.Image.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(size.x - 24, size.x - 4), new Vector2(0.5f, 1));
            if (tex == null)
            {
                c.Image.color = new Color(0, 0, 0, 0);
                var ph = UIKit.Icon(c.Image.rectTransform, VectorIcon.Kind.Waveform, Theme.Paper.A(0.35f), 60, 1.5f);
                ph.rectTransform.Center(Vector2.zero, new Vector2(60, 60));
            }
            else
            {
                float aspect = (float)tex.width / tex.height;
                var fit = c.Image.rectTransform.sizeDelta;
                c.Image.rectTransform.sizeDelta = aspect > 1 ? new Vector2(fit.x, fit.x / aspect) : new Vector2(fit.y * aspect, fit.y);
            }

            c.Name = UIKit.Text(c.Rt, info.name, Theme.Serif, 21, Theme.Paper, TextAlignmentOptions.Bottom);
            c.Name.textWrappingMode = TextWrappingModes.NoWrap;
            c.Name.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(size.x, 30), new Vector2(0.5f, 0));
            c.Bar = UIKit.Shape(c.Rt, Theme.Paper.A(0.6f), 1.5f, "Bar");
            c.Bar.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(34, 3), new Vector2(0.5f, 0.5f));
            if (!info.ready)
            {
                c.Tag = UIKit.Text(c.Rt, "SOON", Theme.Sans, 10, Theme.Paper.A(0.55f), TextAlignmentOptions.TopRight, 30f);
                c.Tag.rectTransform.Place(new Vector2(1, 1), new Vector2(-14, -12), new Vector2(80, 16), new Vector2(1, 1));
            }

            // Invisible hit target so mouse + hand pointer can pick the card.
            var hitRt = UIKit.Node("Hit", c.Rt);
            hitRt.Stretch();
            c.Hit = hitRt.gameObject.AddComponent<PillButton>();
            var hitShape = hitRt.gameObject.AddComponent<ShapeGraphic>();
            hitShape.color = new Color(0, 0, 0, 0);
            hitShape.raycastTarget = true;
            int index = i;
            c.Hit.Clicked += () => Pick(index, true);
            return c;
        }

        static Texture2D FindArt(string id)
        {
            foreach (var t in SolarisAssets.Get.instruments)
                if (t != null && t.name.StartsWith(id)) return t;
            return null;
        }

        void Pick(int i, bool feedback)
        {
            m_Selected = i;
            if (!feedback) return;
            var info = k_Instruments[i];
            if (info.ready) App.Audio.Strum("G", 0.5f, true);
            ShowToast(info.ready ? $"{info.name} selected" : $"{info.name} is coming soon  ·  Acoustic Guitar is ready to play");
        }

        void ShowToast(string text)
        {
            m_Toast.text = text.ToUpperInvariant();
            m_ToastGroup.alpha = 1f;
            Anim.Fade(m_ToastGroup, 0f, 0.8f, 2.2f);
        }

        void StartJamming()
        {
            if (Leaving) return;
            if (!k_Instruments[m_Selected].ready)
            {
                ShowToast($"{k_Instruments[m_Selected].name} is coming soon  ·  switching to Acoustic Guitar");
                m_Selected = 0;
                return;
            }
            App.Session.Instrument = k_Instruments[m_Selected].name;
            Go(ScreenId.Stage);
        }

        public override void Enter()
        {
            Anim.Reveal(m_Hero, 0.3f, 20f, 1.4f);
            for (int i = 0; i < m_Cards.Length; i++) Anim.Reveal(m_Cards[i].Rt, 0.6f + i * 0.07f, 30f, 1.2f);
        }

        protected override void Tick(float dt)
        {
            m_T += dt;
            // Breathing waveform glyph.
            m_Wave.Clear();
            for (int b = 0; b < 7; b++)
            {
                float x = b / 6f;
                float h = 0.25f + 0.75f * Mathf.Abs(Mathf.Sin(m_T * 2.1f + b * 0.9f)) * Mathf.Sin(Mathf.PI * (0.15f + 0.7f * x));
                m_Wave.Line(new Vector2(x, 0.5f - h * 0.5f), new Vector2(x, 0.5f + h * 0.5f));
            }

            for (int i = 0; i < m_Cards.Length; i++)
            {
                var c = m_Cards[i];
                bool sel = i == m_Selected;
                c.Sel = Mathf.Lerp(c.Sel, sel ? 1f : 0f, 1f - Mathf.Exp(-10f * dt));
                float hover = c.Hit.Hovered ? 1f : 0f;
                c.Bg.color = Color.Lerp(k_Dark, k_Light, c.Sel);
                c.Bg.FillBottom = Color.Lerp(k_DarkBottom, k_LightBottom, c.Sel);
                c.Bg.Border = Color.Lerp(k_Rim.A(0.16f + 0.2f * hover), Theme.Paper, c.Sel);
                c.Bg.Shadow = Color.Lerp(new Color(0, 0, 0, 0.4f), Theme.Amber.A(0.35f), c.Sel);
                c.Name.color = Color.Lerp(Theme.Paper, Theme.Ink, c.Sel);
                c.Bar.color = Color.Lerp(Theme.Paper.A(0.5f), Theme.Ink, c.Sel);
                c.Bar.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(28, 44, c.Sel), 3);
                if (c.Tag != null) c.Tag.color = Color.Lerp(Theme.Paper.A(0.55f), Theme.Ink.A(0.6f), c.Sel);
                c.Rt.localScale = Vector3.one * (1f + 0.02f * hover);
            }

            if (Leaving) return;
            if (Pressed(Key.RightArrow) || Pressed(Key.D)) Pick(Mathf.Min(m_Selected + 1, m_Cards.Length - 1), true);
            if (Pressed(Key.LeftArrow) || Pressed(Key.A)) Pick(Mathf.Max(m_Selected - 1, 0), true);
            if (ConfirmPressed()) m_Start.Press();
            if (Pressed(Key.Escape)) Go(ScreenId.ControlsTutorial);
        }
    }
}
