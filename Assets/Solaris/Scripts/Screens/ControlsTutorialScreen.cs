using System.Collections.Generic;
using Solaris.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Solaris.Screens
{
    /// <summary>
    /// Scene 5: expression tutorial, rebuilt from the mock-up. Cards lift while your second hand
    /// performs their control (raise, lower, tilt, curl, lean).
    /// </summary>
    public sealed class ControlsTutorialScreen : AppScreen
    {
        public override ScreenId Id => ScreenId.ControlsTutorial;
        public override VisionNeeds Vision => VisionNeeds.Hands;
        public override float Grain => 0.03f;
        public override float GrainLift => 0.05f;

        static readonly string[] k_Titles = { "Increase Volume", "Decrease Volume", "Tilt to Change Tone", "Switch Major / Minor", "Change Direction" };

        static readonly string[][] k_Body =
        {
            new[] { "Move your hand up", "to make the sound", "louder." },
            new[] { "Move your hand down", "to make the sound", "softer." },
            new[] { "Tilt your hand left or", "right to smoothly", "change the sound", "amplitude and direction." },
            new[] { "Open your fingers for", "a brighter, happier sound.", "Close or curl fingers for", "a softer, moodier sound." },
            new[] { "Tilt your hand forward", "or sideways to direct", "the sound in space", "and feel the music flow", "around you." },
        };

        TutorialBoard m_Board;

        protected override void Build()
        {
            m_Board = new TutorialBoard(Root, 5, "TUTORIALS", "Control sound with your hands", "Let's Try");
            for (int i = 0; i < m_Board.Cards.Count && i < 5; i++) BuildCard(m_Board.Cards[i], i);
            m_Board.Button.Clicked += () => Go(ScreenId.Instruments);
            HandPointer.Create(Root).Register(m_Board.Button);
            m_Board.Layout();
        }

        void BuildCard(TutorialBoard.Card card, int index)
        {
            var title = UIKit.Text(card.Root, k_Titles[index], Theme.Garamond, 26, TutorialBoard.Ink.Design(0.97f), TextAlignmentOptions.Midline, 0f, "Title");
            var divider = TutorialBoard.Divider(card.Root, card.Meta);
            var body = TutorialBoard.BodyLines(card.Root, k_Body[index]);

            card.OnLayout = local =>
            {
                var m = card.Meta;
                var above = new List<TutorialBoard.Line>();
                var below = new List<TutorialBoard.Line>();
                foreach (var l in m.lines) (l.top > m.divider ? below : above).Add(l);
                above.Sort((a, b) => a.top.CompareTo(b.top));
                below.Sort((a, b) => a.top.CompareTo(b.top));
                if (above.Count > 0)
                {
                    var t = above[above.Count - 1];     // the title sits right above the divider
                    local.FitLine(title, t.x0, t.x1, t.baseline);
                    local.AlignLeft(title, t.x0, t.baseline);
                }
                float leftInk = below.Count > 0 ? below[0].x0 : m.box[0] + 30;
                TutorialBoard.LayoutDivider(divider, local, m, leftInk);
                TutorialBoard.LayoutBody(body, local, below);
            };
        }

        public override void Enter() => m_Board.Enter();

        protected override void Tick(float dt)
        {
            m_Board.Tick(dt);
            var g = App.Gestures;
            bool live = g.ChordHand != null || g.ExpressionHand != null;
            var cards = m_Board.Cards;
            if (cards.Count >= 5)
            {
                cards[0].Active = live && g.Volume > 0.72f;
                cards[1].Active = live && g.Volume < 0.35f;
                cards[2].Active = live && Mathf.Abs(g.Tone - 0.5f) > 0.18f;
                cards[3].Active = live && g.ExpressionActive && g.Minor;
                cards[4].Active = live && Mathf.Abs(g.Pan) > 0.45f;
            }

            if (Leaving) return;
            if (ConfirmPressed()) m_Board.Button.Press();
            if (Pressed(Key.Escape)) Go(ScreenId.ChordTutorial);
        }
    }
}
