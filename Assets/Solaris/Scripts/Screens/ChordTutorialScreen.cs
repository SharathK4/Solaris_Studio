using System.Collections.Generic;
using Solaris.Music;
using Solaris.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Solaris.Screens
{
    /// <summary>
    /// Scene 4: chord tutorial, rebuilt from the mock-up. 1..5 fingers = C, D, Em, G, Am.
    /// Live: hold up fingers and the matching card lifts and strums its chord.
    /// </summary>
    public sealed class ChordTutorialScreen : AppScreen
    {
        public override ScreenId Id => ScreenId.ChordTutorial;
        public override VisionNeeds Vision => VisionNeeds.Hands;
        public override float Grain => 0.03f;
        public override float GrainLift => 0.05f;

        static readonly string[][] k_Body =
        {
            new[] { "A bright and happy", "sound. Great for", "starting the song." },
            new[] { "A crisp and uplifting", "sound. Adds forward", "movement." },
            new[] { "A soft and emotional", "sound. Works well", "for calmer parts." },
            new[] { "A full and rich sound.", "Makes the music", "feel open and big." },
            new[] { "A warm and mellow", "sound. Perfect for", "heartfelt lyrics." },
        };

        TutorialBoard m_Board;
        int m_LastFingers = -1;

        protected override void Build()
        {
            m_Board = new TutorialBoard(Root, 4, "TUTORIALS", "Use hand gestures to play chords", "Let's Play");
            for (int i = 0; i < m_Board.Cards.Count && i < 5; i++) BuildCard(m_Board.Cards[i], i);
            m_Board.Button.Clicked += () => Go(ScreenId.ControlsTutorial);
            HandPointer.Create(Root).Register(m_Board.Button);
            m_Board.Layout();
        }

        void BuildCard(TutorialBoard.Card card, int index)
        {
            string chord = Chords.GestureChords[index];
            var (letter, quality) = Chords.Split(chord);
            var name = UIKit.Text(card.Root, letter + quality, Theme.Garamond, 40, TutorialBoard.Ink.Design(0.95f), TextAlignmentOptions.Midline, 0f, "Chord");
            var caps = UIKit.Text(card.Root, "CHORD", Theme.SansLight, 12, TutorialBoard.Ink.Design(0.56f), TextAlignmentOptions.Midline, 0f, "Caps");
            var ink = TutorialBoard.Ink.Design(0.95f);
            var diagram = ChordDiagram.Create(card.Root, new Vector2(60, 80), ink);
            diagram.GridOpacity = Theme.DesignAlpha(0.55f) / ink.a;     // hairline grid, bright dots
            diagram.NutScale = 1f;
            diagram.DotScale = 0.6f;
            var divider = TutorialBoard.Divider(card.Root, card.Meta);
            var body = TutorialBoard.BodyLines(card.Root, k_Body[index]);

            card.OnLayout = local =>
            {
                var m = card.Meta;
                float cx = m.CenterX;
                var right = new List<TutorialBoard.Line>();
                var left = new List<TutorialBoard.Line>();
                var below = new List<TutorialBoard.Line>();
                foreach (var l in m.lines)
                {
                    if (l.top > m.divider) below.Add(l);
                    else if (l.x0 > cx) right.Add(l);
                    else left.Add(l);
                }
                left.Sort((a, b) => a.top.CompareTo(b.top));
                below.Sort((a, b) => a.top.CompareTo(b.top));

                if (left.Count > 0)
                {
                    var l = left[0];
                    local.FitHeight(name, l.top, l.baseline);
                    local.AlignLeft(name, l.x0, l.baseline);
                }
                if (left.Count > 1)
                {
                    var l = left[1];
                    local.FitCaps(caps, l.x0, l.x1, l.top, l.baseline);
                }
                if (right.Count > 0)
                {
                    float x0 = float.MaxValue, x1 = float.MinValue, top = float.MaxValue, bottom = float.MinValue;
                    foreach (var l in right)
                    {
                        x0 = Mathf.Min(x0, l.x0); x1 = Mathf.Max(x1, l.x1);
                        top = Mathf.Min(top, l.top); bottom = Mathf.Max(bottom, l.bottom);
                    }
                    var drt = diagram.GetComponent<RectTransform>();
                    local.Place(drt, x0, top, x1, bottom);
                    diagram.Show(chord);
                }
                float leftInk = below.Count > 0 ? below[0].x0 : m.box[0] + 32;
                TutorialBoard.LayoutDivider(divider, local, m, leftInk);
                TutorialBoard.LayoutBody(body, local, below);
            };
        }

        public override void Enter() => m_Board.Enter();

        protected override void Tick(float dt)
        {
            m_Board.Tick(dt);
            int fingers = App.Gestures.Fingers;
            for (int i = 0; i < m_Board.Cards.Count; i++) m_Board.Cards[i].Active = fingers == i + 1;

            if (fingers != m_LastFingers)
            {
                m_LastFingers = fingers;
                var chord = Chords.FromFingers(fingers);
                if (chord != null) App.Audio.Strum(chord, 0.75f, true);
            }

            if (Leaving) return;
            for (int i = 0; i < 5; i++)
                if (Pressed(Key.Digit1 + i) || Pressed(Key.Numpad1 + i))
                    App.Audio.Strum(Chords.GestureChords[i], 0.8f, true);
            if (ConfirmPressed()) m_Board.Button.Press();
            if (Pressed(Key.Escape)) Go(ScreenId.SongSelect);
        }
    }
}
