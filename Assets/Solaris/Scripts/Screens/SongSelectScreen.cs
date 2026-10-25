using System.Collections.Generic;
using System.Linq;
using Solaris.Music;
using Solaris.Rendering;
using Solaris.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Solaris.Screens
{
    /// <summary>
    /// Scene 3 — the song dial. Titles radiate like spokes from a hub off the left edge; drawing
    /// circles with the index finger turns the dial (clockwise = next), pinch-and-hold plays.
    /// </summary>
    public sealed class SongSelectScreen : AppScreen
    {
        public override ScreenId Id => ScreenId.SongSelect;
        public override VisionNeeds Vision => VisionNeeds.Hands;
        public override float Grain => 0.07f;
        public override float GrainLift => 0.09f;

        const float Radius = 880f;
        const float Spacing = 6.4f;          // degrees between titles
        const float SelectAngle = 5f;        // where the selected title sits (slightly above horizontal)
        const float RadiansPerSong = 1.35f;  // finger turning needed to advance one title
        const int Visible = 4;

        class Item
        {
            public RectTransform Rt;
            public TextMeshProUGUI Text;
            public ShapeGraphic Badge;
            public VectorIcon Play;
            public CanvasGroup Group;
            public int Slot;
        }

        List<Song> m_Songs;
        readonly List<Item> m_Items = new List<Item>();
        RectTransform m_Hub;
        Spring m_Scroll;
        float m_Target;
        float m_Idle;
        int m_Selected = -1;
        float m_PinchHold;
        TextMeshProUGUI m_SongTitle;
        RectTransform m_Details;
        CanvasGroup m_DetailsGroup;
        PillButton m_Play;
        HandPointer m_Pointer;
        RectTransform m_Wheel;

        protected override void Build()
        {
            m_Songs = App.Songs.Songs.ToList();

            var bg = ProceduralLayer.Create(Root, "Solaris/BG/GlowField", 0.5f);
            var m = bg.Material;
            m.SetColor("_Top", Theme.Hex("2B1007"));
            m.SetColor("_Bottom", Theme.Hex("0C0402"));
            m.SetVector("_G0", new Vector4(0.62f, 1.02f, 0.62f, 0.95f));
            m.SetColor("_C0", Theme.Hex("A2481F"));
            m.SetVector("_G1", new Vector4(0.52f, -0.08f, 0.55f, 0.75f));
            m.SetColor("_C1", Theme.Hex("8A3618"));
            m.SetVector("_G2", new Vector4(0.95f, 0.45f, 0.45f, 0.45f));
            m.SetColor("_C2", Theme.Hex("6E2C12"));
            m.SetVector("_G3", new Vector4(0.0f, 0.5f, 0.3f, 0f));
            m.SetFloat("_Smoke", 0.35f);

            var cam = Chrome.CameraBackdrop(Root, 0.95f, 0.15f);
            cam.material.SetColor("_Light", Theme.Hex("C98A5E"));
            cam.GetComponent<CameraBackdrop>().Spotlight = true;   // a warm pool of light follows your fingertip

            // Dial hub, off the left edge.
            m_Wheel = UIKit.Node("Dial", Root);
            m_Wheel.Stretch();
            m_Hub = UIKit.Node("Hub", m_Wheel).Place(new Vector2(0, 0.5f), new Vector2(-Radius + 560f, 30f), Vector2.zero);

            for (int i = 0; i < Visible * 2 + 1; i++) m_Items.Add(CreateItem(i));

            // Right column.
            var col = UIKit.Node("Details", Root).Place(new Vector2(0.5f, 0.5f), new Vector2(140, 10), new Vector2(660, 520), new Vector2(0, 0.5f));
            var heading = UIKit.Text(col, "Choose song to play", Theme.Serif, 60, Theme.Cream, TextAlignmentOptions.TopLeft, -0.5f);
            heading.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 0), new Vector2(660, 80), new Vector2(0, 1));
            var para = UIKit.Text(col, "From timeless classics to festive favourites, find a song, hold up your fingers and start playing it in real time with live AR feedback.",
                Theme.Sans, 19, Theme.Cream.A(0.78f), TextAlignmentOptions.TopLeft, 0.2f);
            para.lineSpacing = 10;
            para.rectTransform.Place(new Vector2(0, 1), new Vector2(2, -96), new Vector2(560, 90), new Vector2(0, 1));

            m_Details = UIKit.Node("Selected", col).Place(new Vector2(0, 1), new Vector2(2, -214), new Vector2(620, 170), new Vector2(0, 1));
            m_DetailsGroup = m_Details.Group();
            var cap = UIKit.Text(m_Details, "NOW SELECTED", Theme.Sans, 11, Theme.Periwinkle.A(0.95f), TextAlignmentOptions.TopLeft, 30f);
            cap.rectTransform.Place(new Vector2(0, 1), Vector2.zero, new Vector2(400, 18), new Vector2(0, 1));
            m_SongTitle = UIKit.Text(m_Details, "", Theme.SerifItalic, 30, Theme.Cream, TextAlignmentOptions.TopLeft);
            m_SongTitle.rectTransform.Place(new Vector2(0, 1), new Vector2(0, -24), new Vector2(620, 44), new Vector2(0, 1));
            m_SongTitle.textWrappingMode = TextWrappingModes.NoWrap;

            m_Play = PillButton.Create(col, "Play this song", new Vector2(236, 60), VectorIcon.Kind.Play, null);
            m_Play.GetComponent<RectTransform>().Place(new Vector2(0, 1), new Vector2(0, -300), new Vector2(236, 60), new Vector2(0, 1));
            m_Play.Clicked += Choose;

            // Gesture hints.
            var hints = UIKit.Node("Hints", Root).Place(new Vector2(0.5f, 0), new Vector2(142, 64), new Vector2(900, 24), new Vector2(0, 0));
            var h1 = Chrome.Hint(hints, VectorIcon.Kind.Rotate, "Circle your index finger to browse", Theme.Cream.A(0.62f));
            h1.anchorMin = h1.anchorMax = h1.pivot = new Vector2(0, 0.5f);
            var h2 = Chrome.Hint(hints, VectorIcon.Kind.Pinch, "Pinch & hold to play", Theme.Cream.A(0.62f));
            h2.anchorMin = h2.anchorMax = h2.pivot = new Vector2(0, 0.5f);
            h2.anchoredPosition = new Vector2(h1.sizeDelta.x + 44, 0);

            m_Pointer = HandPointer.Create(Root);
            m_Pointer.Register(m_Play);

            int start = App.Session.Song != null ? Mathf.Max(0, m_Songs.IndexOf(App.Session.Song)) : 0;
            m_Scroll = new Spring(start);
            m_Target = start;
            Select(start, false);
        }

        Item CreateItem(int slot)
        {
            var rt = UIKit.Node("Title", m_Hub);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(620, 48);
            var item = new Item { Rt = rt, Slot = slot, Group = rt.Group() };
            item.Badge = UIKit.Shape(rt, Theme.Periwinkle, 5, "Badge");
            item.Badge.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(28, 28), new Vector2(0, 0.5f));
            item.Play = UIKit.Icon(item.Badge.rectTransform, VectorIcon.Kind.Play, Theme.Paper, 12, 1.6f);
            item.Play.rectTransform.Center(new Vector2(1, 0), new Vector2(12, 12));
            item.Text = UIKit.Text(rt, "", Theme.Sans, 24, Theme.Cream, TextAlignmentOptions.Left);
            item.Text.textWrappingMode = TextWrappingModes.NoWrap;
            item.Text.rectTransform.Stretch(0, 0, 0, 0);
            return item;
        }

        public override void Enter()
        {
            Anim.Reveal(m_Wheel, 0.4f, 0, 1.6f);
            Anim.Reveal(m_Details.parent as RectTransform, 0.7f, 18f, 1.4f);
        }

        void Select(int index, bool feedback)
        {
            if (m_Songs.Count == 0) return;
            index = Wrap(index);
            if (index == m_Selected) return;
            m_Selected = index;
            var s = m_Songs[index];
            m_SongTitle.text = s.Title;
            if (feedback)
            {
                m_DetailsGroup.alpha = 0.35f;
                Anim.Fade(m_DetailsGroup, 1f, 0.35f);
                var first = s.Chords.Count > 0 ? s.Chords[0].Chord : "G";
                App.Audio.Strum(first, 0.35f, true);
            }
        }

        int Wrap(int i) => m_Songs.Count == 0 ? 0 : ((i % m_Songs.Count) + m_Songs.Count) % m_Songs.Count;

        protected override void Tick(float dt)
        {
            if (m_Songs.Count == 0) return;
            var g = App.Gestures;

            // Finger circles drive the dial; when the finger stops, glide to the nearest title.
            float turn = g.TakeCircleDelta();
            if (Mathf.Abs(turn) > 1e-4f)
            {
                m_Target += turn / RadiansPerSong;
                m_Idle = 0;
            }
            else
            {
                m_Idle += dt;
                if (m_Idle > 0.22f) m_Target = Mathf.Lerp(m_Target, Mathf.Round(m_Target), 1f - Mathf.Exp(-10f * dt));
            }

            if (!Leaving)
            {
                if (Pressed(Key.DownArrow) || Pressed(Key.S)) m_Target = Mathf.Round(m_Target) + 1;
                if (Pressed(Key.UpArrow) || Pressed(Key.W)) m_Target = Mathf.Round(m_Target) - 1;
                if (!AppArgs.Automation && Mouse.current != null)
                {
                    float wheel = Mouse.current.scroll.ReadValue().y;
                    if (Mathf.Abs(wheel) > 0.01f) m_Target = Mathf.Round(m_Target) - Mathf.Sign(wheel);
                }
                if (ConfirmPressed()) Choose();
                if (Pressed(Key.Escape)) Go(ScreenId.Welcome);

                // Pinch-and-hold anywhere also plays (the button shows the progress).
                if (g.Pinching && m_Pointer.Visible) m_PinchHold += dt;
                else m_PinchHold = 0;
                if (!m_Play.HandHover) m_Play.Progress = Mathf.Clamp01(m_PinchHold / 0.6f);
                if (m_PinchHold > 0.6f)
                {
                    m_PinchHold = 0;
                    m_Play.Press();
                }
            }

            float scroll = m_Scroll.Step(m_Target, dt, 2.6f, 0.9f);
            Select(Mathf.RoundToInt(scroll), true);

            // Lay out the spokes around the current scroll position.
            int center = Mathf.RoundToInt(scroll);
            for (int i = 0; i < m_Items.Count; i++)
            {
                var it = m_Items[i];
                int songIndex = center + (i - Visible);
                float k = songIndex - scroll;                 // signed distance from selection
                float deg = SelectAngle - k * Spacing;
                float rad = deg * Mathf.Deg2Rad;
                float sel = Mathf.Clamp01(1f - Mathf.Abs(k));
                float r = Radius + sel * 26f;
                it.Rt.anchoredPosition = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * r;
                it.Rt.localRotation = Quaternion.Euler(0, 0, deg);
                var song = m_Songs[Wrap(songIndex)];
                if (it.Text.text != song.Title) it.Text.text = song.Title;
                it.Text.fontSize = Mathf.Lerp(23f, 33f, sel);
                it.Text.color = Color.Lerp(Theme.Cream.A(0.82f), Theme.Periwinkle, sel);
                it.Text.rectTransform.offsetMin = new Vector2(Mathf.Lerp(0, 44, sel), 0);
                it.Badge.color = Theme.Periwinkle.A(sel);
                it.Play.color = Theme.Paper.A(sel);
                it.Badge.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, sel);
                it.Group.alpha = Mathf.Clamp01(1.1f - Mathf.Abs(k) * 0.2f) * Mathf.Clamp01(Visible + 0.5f - Mathf.Abs(k));
            }
        }

        void Choose()
        {
            if (Leaving || m_Songs.Count == 0) return;
            App.Session.Song = m_Songs[m_Selected];
            App.Audio.Strum(App.Session.Song.Chords[0].Chord, 0.8f, true);
            Go(ScreenId.ChordTutorial);
        }
    }
}
