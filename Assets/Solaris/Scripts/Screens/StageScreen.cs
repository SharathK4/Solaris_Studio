using System.Collections.Generic;
using System.Text;
using Solaris.Audio;
using Solaris.Gestures;
using Solaris.Music;
using Solaris.UI;
using Solaris.Vision;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Solaris.Screens
{
    /// <summary>
    /// Scene 7 — the performance. You, segmented from your room, with sound waves blooming behind you;
    /// current chord top-left, the next gesture top-right, karaoke lyrics below. The band follows the
    /// chart; your hands play the guitar.
    /// </summary>
    public sealed class StageScreen : AppScreen
    {
        public override ScreenId Id => ScreenId.Stage;
        public override VisionNeeds Vision => VisionNeeds.All;
        public override float Grain => 0.055f;

        const float GraceBefore = 0.12f, GraceAfter = 0.26f;

        Song m_Song;
        RawImage m_View;
        Material m_Composite;
        RectTransform m_Hud;
        Spring2 m_Parallax;

        // current chord
        TextMeshProUGUI m_Letter, m_ChordCap, m_Status;
        ChordDiagram m_Diagram;
        RectTransform m_ChordPanel;
        string m_ShownChord = "-";
        float m_StrumFlash;

        // next gesture
        GlassCard m_NextCard;
        HandFigure m_NextHand;
        TextMeshProUGUI m_NextName, m_NextFingers, m_NextCap;
        LineGraphic m_Countdown;

        // lyrics + progress
        TextMeshProUGUI m_Line, m_NextLine, m_Title, m_Time, m_Score;
        ShapeGraphic m_ProgressFill, m_Knob;
        int m_LineIndex = -1, m_WordIndex = -2;
        readonly StringBuilder m_Sb = new StringBuilder(256);

        // AR anchors
        readonly ShapeGraphic[] m_Tips = new ShapeGraphic[5];
        RectTransform m_HandTag;
        TextMeshProUGUI m_HandTagText;
        LineGraphic m_Gauge;
        TextMeshProUGUI m_GaugeText;

        // overlays
        TextMeshProUGUI m_Count;
        RectTransform m_Pause, m_Results;
        TextMeshProUGUI m_Grade, m_ResultLine, m_ResultSub;

        // audio-visual state
        readonly Vector4[] m_Pulses = new Vector4[16];
        readonly float[] m_PulseAge = new float[16], m_PulseAmp = new float[16];
        readonly Vector4[] m_Bands = new Vector4[4];
        readonly float[] m_BandSmooth = new float[16];
        int m_NextPulse;

        // scoring
        struct Pending { public float Time; public string Target; }
        readonly List<Pending> m_Pending = new List<Pending>();
        readonly List<(float t, string chord)> m_HeldHistory = new List<(float, string)>();
        readonly StageResult m_Result = new StageResult();
        int m_Streak;
        bool m_Started, m_Paused, m_Done;
        float m_StartAt = 1.6f;
        string m_KeyboardChord;

        protected override void Build()
        {
            m_Song = App.Session.Song ?? App.Songs.Find(AppArgs.Song ?? "amazing_grace");
            m_Result.SongTitle = m_Song?.Title;

            m_Composite = SolarisAssets.Mat("Solaris/StageComposite");
            m_View = UIKit.Raw(Root, null, m_Composite, "Stage");
            m_View.rectTransform.Stretch();
            m_View.color = Color.white;

            var scrim = UIKit.Shape(Root, new Color(0.05f, 0.02f, 0.01f, 0f), 0, "LyricScrim");
            scrim.FillBottom = new Color(0.05f, 0.02f, 0.01f, 0.72f);
            scrim.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, -10), new Vector2(4000, 420), new Vector2(0.5f, 0));
            var top = UIKit.Shape(Root, new Color(0.05f, 0.02f, 0.01f, 0.5f), 0, "TopScrim");
            top.FillBottom = new Color(0.05f, 0.02f, 0.01f, 0f);
            top.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, 10), new Vector2(4000, 300), new Vector2(0.5f, 1));

            m_Hud = UIKit.Node("HUD", Root);
            m_Hud.Stretch();
            BuildAnchors();
            BuildChordPanel();
            BuildNextCard();
            BuildLyrics();
            BuildOverlays();
        }

        // ------------------------------------------------------------------ build
        void BuildChordPanel()
        {
            m_ChordPanel = UIKit.Node("Chord", m_Hud).Place(new Vector2(0, 1), new Vector2(92, -86), new Vector2(360, 380), new Vector2(0, 1));
            m_Letter = UIKit.Text(m_ChordPanel, "", Theme.Serif, 132, Theme.Paper, TextAlignmentOptions.BottomLeft);
            m_Letter.richText = true;
            m_Letter.textWrappingMode = TextWrappingModes.NoWrap;
            m_Letter.rectTransform.Place(new Vector2(0, 1), new Vector2(0, -150), new Vector2(240, 160), new Vector2(0, 0));
            m_ChordCap = UIKit.Text(m_ChordPanel, "chord", Theme.Sans, 22, Theme.Paper.A(0.9f), TextAlignmentOptions.BottomLeft);
            m_ChordCap.rectTransform.Place(new Vector2(0, 1), new Vector2(96, -140), new Vector2(160, 30), new Vector2(0, 0));
            m_Diagram = ChordDiagram.Create(m_ChordPanel, new Vector2(118, 140), Theme.Paper.A(0.95f));
            m_Diagram.GetComponent<RectTransform>().Place(new Vector2(0, 1), new Vector2(6, -170), new Vector2(118, 140), new Vector2(0, 1));
            m_Status = UIKit.Text(m_ChordPanel, "", Theme.Sans, 11.5f, Theme.Paper.A(0.7f), TextAlignmentOptions.TopLeft, 30f);
            m_Status.richText = true;
            m_Status.textWrappingMode = TextWrappingModes.NoWrap;
            m_Status.rectTransform.Place(new Vector2(0, 1), new Vector2(6, -330), new Vector2(360, 20), new Vector2(0, 1));
        }

        void BuildNextCard()
        {
            m_NextCard = GlassCard.Create(m_Hud, new Vector2(270, 330), "Next");
            var rt = m_NextCard.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            m_NextCard.SetHome(new Vector2(-84, -78));
            m_NextCard.Lift = 10f;
            var c = m_NextCard.Content;
            m_NextCap = UIKit.Text(c, "NEXT GESTURE", Theme.Sans, 11, Theme.Paper.A(0.7f), TextAlignmentOptions.TopLeft, 34f);
            m_NextCap.rectTransform.Place(new Vector2(0, 1), new Vector2(24, -22), new Vector2(220, 18), new Vector2(0, 1));
            m_Countdown = UIKit.Node("Countdown", c).gameObject.AddComponent<LineGraphic>();
            m_Countdown.rectTransform.Place(new Vector2(1, 1), new Vector2(-22, -18), new Vector2(26, 26), new Vector2(1, 1));
            m_Countdown.color = Theme.Amber;
            m_Countdown.Stroke = 2.4f;
            m_Countdown.raycastTarget = false;
            m_NextHand = HandFigure.Create(c, "NextHand");
            m_NextHand.GetComponent<RectTransform>().Place(new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(230, 210), new Vector2(0.5f, 1));
            m_NextHand.Scale = 0.95f;
            m_NextName = UIKit.Text(c, "", Theme.Serif, 40, Theme.Paper, TextAlignmentOptions.BottomLeft);
            m_NextName.richText = true;
            m_NextName.rectTransform.Place(new Vector2(0, 0), new Vector2(24, 40), new Vector2(160, 52), new Vector2(0, 0));
            m_NextFingers = UIKit.Text(c, "", Theme.Sans, 11, Theme.Amber, TextAlignmentOptions.BottomRight, 30f);
            m_NextFingers.rectTransform.Place(new Vector2(1, 0), new Vector2(-24, 50), new Vector2(160, 18), new Vector2(1, 0));
        }

        void BuildLyrics()
        {
            var block = UIKit.Node("Lyrics", m_Hud).Place(new Vector2(0.5f, 0), new Vector2(0, 70), new Vector2(1400, 200), new Vector2(0.5f, 0));
            m_Line = UIKit.Text(block, "", Theme.Sans, 44, Theme.Paper, TextAlignmentOptions.Center, 0.5f);
            m_Line.richText = true;
            m_Line.textWrappingMode = TextWrappingModes.NoWrap;
            m_Line.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(1400, 60), new Vector2(0.5f, 1));
            m_NextLine = UIKit.Text(block, "", Theme.Sans, 30, Theme.Paper.A(0.42f), TextAlignmentOptions.Center, 0.5f);
            m_NextLine.textWrappingMode = TextWrappingModes.NoWrap;
            m_NextLine.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(1400, 44), new Vector2(0.5f, 1));

            var track = UIKit.Shape(block, Theme.Paper.A(0.25f), 1, "Track");
            track.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(460, 2f), new Vector2(0.5f, 0.5f));
            m_ProgressFill = UIKit.Shape(track.rectTransform, Theme.Hex("F2A56A"), 1, "Fill");
            m_ProgressFill.rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(0, 2f), new Vector2(0, 0.5f));
            m_Knob = UIKit.Shape(track.rectTransform, Theme.Paper, 6, "Knob");
            m_Knob.ShadowSize = 10;
            m_Knob.Shadow = Theme.Ember.A(0.7f);
            m_Knob.rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(12, 12), new Vector2(0.5f, 0.5f));

            m_Title = UIKit.Text(m_Hud, (m_Song?.Title ?? "").ToUpperInvariant(), Theme.Sans, 11.5f, Theme.Paper.A(0.75f), TextAlignmentOptions.Top, 36f);
            m_Title.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(900, 20), new Vector2(0.5f, 1));
            m_Time = UIKit.Text(m_Hud, "", Theme.Sans, 11, Theme.Paper.A(0.45f), TextAlignmentOptions.Top, 20f);
            m_Time.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -62), new Vector2(400, 18), new Vector2(0.5f, 1));
            m_Score = UIKit.Text(m_Hud, "", Theme.Sans, 11, Theme.Paper.A(0.6f), TextAlignmentOptions.BottomRight, 26f);
            m_Score.rectTransform.Place(new Vector2(1, 0), new Vector2(-60, 44), new Vector2(500, 18), new Vector2(1, 0));
        }

        void BuildAnchors()
        {
            for (int i = 0; i < 5; i++)
            {
                var dot = UIKit.Shape(m_Hud, Theme.Paper, 5, "Tip");
                dot.ShadowSize = 14;
                dot.Shadow = Theme.Ember.A(0.9f);
                dot.rectTransform.sizeDelta = new Vector2(10, 10);
                dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = dot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                m_Tips[i] = dot;
            }
            var tag = UIKit.Shape(m_Hud, Theme.Hex("2A1006").A(0.55f), 18, "HandTag");
            tag.BorderWidth = 1f;
            tag.Border = Theme.Paper.A(0.35f);
            tag.rectTransform.sizeDelta = new Vector2(76, 36);
            tag.rectTransform.anchorMin = tag.rectTransform.anchorMax = tag.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            m_HandTag = tag.rectTransform;
            m_HandTagText = UIKit.Text(m_HandTag, "", Theme.Serif, 20, Theme.Paper);
            m_HandTagText.richText = true;
            m_HandTagText.rectTransform.Stretch();

            m_Gauge = UIKit.Node("Gauge", m_Hud).gameObject.AddComponent<LineGraphic>();
            m_Gauge.rectTransform.sizeDelta = new Vector2(120, 120);
            m_Gauge.rectTransform.anchorMin = m_Gauge.rectTransform.anchorMax = m_Gauge.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            m_Gauge.color = Theme.Amber;
            m_Gauge.Stroke = 3f;
            m_Gauge.raycastTarget = false;
            m_GaugeText = UIKit.Text(m_Gauge.rectTransform, "", Theme.Sans, 10.5f, Theme.Paper.A(0.85f), TextAlignmentOptions.Center, 26f);
            m_GaugeText.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(12, 0), new Vector2(170, 16), new Vector2(0, 0.5f));
            m_GaugeText.alignment = TextAlignmentOptions.Left;
        }

        void BuildOverlays()
        {
            m_Count = UIKit.Text(Root, "", Theme.Serif, 170, Theme.Paper, TextAlignmentOptions.Center);
            m_Count.rectTransform.Center(new Vector2(0, 40), new Vector2(600, 260));

            m_Pause = Overlay("Paused", "Take a breath. Your band will wait.", out _, out _,
                ("Resume", () => SetPaused(false)), ("Restart", Restart), ("Choose song", () => Go(ScreenId.SongSelect)));
            m_Results = Overlay("", "", out m_Grade, out m_ResultSub,
                ("Play again", Restart), ("Choose another song", () => Go(ScreenId.SongSelect)));
            m_ResultLine = m_ResultSub;
        }

        RectTransform Overlay(string title, string sub, out TextMeshProUGUI titleText, out TextMeshProUGUI subText, params (string label, System.Action action)[] buttons)
        {
            var root = UIKit.Node("Overlay " + title, Root);
            root.Stretch();
            var dim = UIKit.Shape(root, new Color(0.03f, 0.01f, 0.005f, 0.72f), 0, "Dim");
            dim.rectTransform.Stretch(-20, -20, -20, -20);
            var card = GlassCard.Create(root, new Vector2(620, 460), "Card");
            var crt = card.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            card.SetHome(Vector2.zero);
            titleText = UIKit.Text(card.Content, title, Theme.Serif, 64, Theme.Paper, TextAlignmentOptions.Top);
            titleText.richText = true;
            titleText.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(560, 150), new Vector2(0.5f, 1));
            subText = UIKit.Text(card.Content, sub, Theme.Sans, 17, Theme.Paper.A(0.75f), TextAlignmentOptions.Top);
            subText.richText = true;
            subText.lineSpacing = 12;
            subText.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0, -196), new Vector2(520, 120), new Vector2(0.5f, 1));
            var pointer = HandPointer.Create(root);
            float y = 40;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                var b = PillButton.Create(card.Content, buttons[i].label, new Vector2(i == 0 ? 280 : 240, 56), null, i == 0 ? VectorIcon.Kind.ArrowRight : (VectorIcon.Kind?)null);
                var brt = b.GetComponent<RectTransform>();
                float x = buttons.Length == 1 ? 0 : (i - (buttons.Length - 1) * 0.5f) * 0;
                brt.Place(new Vector2(0.5f, 0), new Vector2(x, y), brt.sizeDelta, new Vector2(0.5f, 0));
                if (i > 0) b.Background.color = Theme.Paper.A(0.9f);
                var action = buttons[i].action;
                b.Clicked += () => action();
                pointer.Register(b);
                y += 66;
            }
            root.gameObject.SetActive(false);
            return root;
        }

        // ------------------------------------------------------------------ lifecycle
        public override void Enter()
        {
            Anim.Reveal(m_ChordPanel, 0.6f, 20f, 1.4f);
            m_NextCard.Appear(0.8f);
        }

        public override void Exit()
        {
            base.Exit();
            App.Audio.Stop();
            App.Audio.Hold(null);
            App.Audio.SetPaused(false);
        }

        void Restart()
        {
            m_Results.gameObject.SetActive(false);
            m_Pause.gameObject.SetActive(false);
            m_Paused = false;
            m_Done = false;
            m_Started = false;
            m_StartAt = Age + 0.8f;
            m_Pending.Clear();
            m_Streak = 0;
            m_Result.Strums = m_Result.Hits = m_Result.BestStreak = 0;
            m_LineIndex = -1;
            App.Audio.SetPaused(false);
            App.Audio.Stop();
        }

        void SetPaused(bool paused)
        {
            if (m_Done) return;
            m_Paused = paused;
            App.Audio.SetPaused(paused);
            m_Pause.gameObject.SetActive(paused);
        }

        // ------------------------------------------------------------------ frame
        protected override void Tick(float dt)
        {
            if (m_Song == null) return;
            var audio = App.Audio;
            var g = App.Gestures;

            if (!m_Started && Age >= m_StartAt && !Leaving)
            {
                m_Started = true;
                audio.Play(m_Song, 1);
            }

            double beat = m_Started ? audio.SongBeat : -m_Song.Meter - 1;
            float fbeat = (float)beat;
            float now = Time.unscaledTime;

            // --- what the player is holding ---
            string baseChord = m_Song.ChordForFingers(g.Fingers);
            if (!AppArgs.Automation) HandleKeyboardChords(ref baseChord);
            string held = baseChord != null && g.Minor ? m_Song.Alternate(baseChord) : baseChord;
            if (AppArgs.Autoplay)
            {
                held = m_Song.ChordAt(Mathf.Max(0, fbeat + 0.15f)) ?? m_Song.Chords[0].Chord;
                baseChord = m_Song.GestureBase(held);
            }
            if (AppArgs.HoldChord != null && Chords.IsKnown(AppArgs.HoldChord))
            {
                held = AppArgs.HoldChord;
                baseChord = m_Song.GestureBase(held);
            }
            if (!m_Paused && !m_Done) audio.Hold(held);
            audio.GuitarVolume = AppArgs.Autoplay ? 0.85f : g.Volume;
            audio.Tone = g.Tone;
            audio.Pan = g.Pan * 0.8f;
            audio.Space = 0.25f + 0.6f * g.Depth;
            m_HeldHistory.Add((now, m_Song.Gestures != null ? held : baseChord));
            while (m_HeldHistory.Count > 0 && now - m_HeldHistory[0].t > 1.2f) m_HeldHistory.RemoveAt(0);

            // --- strums: waves + scoring ---
            while (audio.TryTakeStrum(out var e))
            {
                string target = e.Target >= 0 ? Chords.All[e.Target].Name : null;
                if (e.Held >= 0) SpawnPulse(e.Velocity * (0.55f + 0.6f * g.Volume));
                if (target != null) m_Pending.Add(new Pending { Time = now, Target = m_Song.Gestures != null ? target : m_Song.GestureBase(target) });
                if (e.Held >= 0) m_StrumFlash = 1f;
            }
            ScorePending(now);

            UpdateComposite(dt);
            UpdateParallax(dt);
            UpdateChordPanel(held, baseChord, fbeat, dt);
            UpdateNext(fbeat);
            UpdateLyrics(fbeat);
            UpdateAnchors(baseChord, held);
            UpdateCount(fbeat);

            if (audio.Finished && !m_Done && m_Started) ShowResults();

            if (Leaving) return;
            if (Pressed(Key.Escape) || Pressed(Key.P))
            {
                if (m_Done) Go(ScreenId.SongSelect);
                else SetPaused(!m_Paused);
            }
            if ((m_Paused || m_Done) && ConfirmPressed())
            {
                if (m_Done) Restart();
                else SetPaused(false);
            }
        }

        void HandleKeyboardChords(ref string chord)
        {
            for (int i = 0; i < 5; i++)
                if (Pressed(Key.Digit1 + i) || Pressed(Key.Numpad1 + i)) m_KeyboardChord = m_Song.ChordForFingers(i + 1);
            if (Pressed(Key.Digit0) || Pressed(Key.Numpad0)) m_KeyboardChord = null;
            if (chord == null && App.Gestures.ChordHand == null && m_KeyboardChord != null) chord = m_KeyboardChord;
        }

        void ScorePending(float now)
        {
            for (int i = m_Pending.Count - 1; i >= 0; i--)
            {
                var p = m_Pending[i];
                if (now < p.Time + GraceAfter) continue;
                bool hit = false;
                foreach (var h in m_HeldHistory)
                    if (h.t >= p.Time - GraceBefore && h.t <= p.Time + GraceAfter && h.chord == p.Target) { hit = true; break; }
                m_Result.Strums++;
                if (hit)
                {
                    m_Result.Hits++;
                    m_Streak++;
                    m_Result.BestStreak = Mathf.Max(m_Result.BestStreak, m_Streak);
                }
                else m_Streak = 0;
                m_Pending.RemoveAt(i);
            }
        }

        void SpawnPulse(float amp)
        {
            int i = m_NextPulse;
            m_NextPulse = (m_NextPulse + 1) % m_Pulses.Length;
            m_PulseAge[i] = 0;
            m_PulseAmp[i] = Mathf.Clamp(amp, 0.2f, 1.3f);
        }

        void UpdateComposite(float dt)
        {
            var feed = App.Feed;
            var vision = App.Vision;
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            m_View.texture = feed.IsRunning ? feed.Frame : Texture2D.blackTexture;
            m_Composite.SetVector("_Cover", feed.CoverTransform(aspect));
            m_Composite.SetFloat("_Mirror", feed.Mirror ? 1 : 0);
            m_Composite.SetFloat("_Aspect", aspect);
            bool hasMask = vision.PersonMask != null && vision.SegmentationEnabled;
            if (hasMask) m_Composite.SetTexture("_Mask", vision.PersonMask);
            m_Composite.SetFloat("_HasMask", hasMask ? 1 : 0);

            var face = vision.Face;
            Vector2 fc = face.Visible ? feed.FrameToView(face.Center, aspect) : new Vector2(0.5f, 0.6f);
            float fr = face.Visible ? Mathf.Clamp(face.Size.y * 0.55f, 0.07f, 0.3f) : 0.12f;
            var cur = m_Composite.GetVector("_Face");
            var target = new Vector4(fc.x, fc.y, fr, face.Visible ? 1 : 0);
            m_Composite.SetVector("_Face", Vector4.Lerp(cur, target, 1f - Mathf.Exp(-8f * dt)));

            // Pulses expand outward from the face and fade.
            for (int i = 0; i < m_Pulses.Length; i++)
            {
                if (m_PulseAmp[i] <= 0) { m_Pulses[i] = Vector4.zero; continue; }
                m_PulseAge[i] += dt;
                float age = m_PulseAge[i];
                float a = m_PulseAmp[i] * Mathf.Exp(-age / 1.5f) * Mathf.Clamp01(age * 20f);
                if (a < 0.01f) { m_PulseAmp[i] = 0; m_Pulses[i] = Vector4.zero; continue; }
                m_Pulses[i] = new Vector4(fr * 1.3f + age * 0.28f, a, 0.02f + age * 0.05f, 0);
            }
            m_Composite.SetVectorArray("_Pulses", m_Pulses);

            // 16 log-spaced spectrum bands.
            var spec = App.Audio.Spectrum;
            for (int b = 0; b < 16; b++)
            {
                int lo = Mathf.RoundToInt(Mathf.Pow(2f, 1f + b * 7f / 16f));
                int hi = Mathf.Max(lo + 1, Mathf.RoundToInt(Mathf.Pow(2f, 1f + (b + 1) * 7f / 16f)));
                float sum = 0;
                for (int k = lo; k < hi && k < spec.Length; k++) sum += spec[k];
                float v = Mathf.Clamp01(Mathf.Sqrt(sum / (hi - lo)) * (6f + b * 1.4f));
                m_BandSmooth[b] = Mathf.Lerp(m_BandSmooth[b], v, 1f - Mathf.Exp(-(v > m_BandSmooth[b] ? 30f : 6f) * dt));
            }
            for (int q = 0; q < 4; q++)
                m_Bands[q] = new Vector4(m_BandSmooth[q * 4], m_BandSmooth[q * 4 + 1], m_BandSmooth[q * 4 + 2], m_BandSmooth[q * 4 + 3]);
            m_Composite.SetVectorArray("_Bands", m_Bands);
            m_Composite.SetFloat("_Level", Mathf.Clamp01(App.Audio.Level * 2.5f));
        }

        void UpdateParallax(float dt)
        {
            var face = App.Vision.Face;
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            Vector2 fc = face.Visible ? App.Feed.FrameToView(face.Center, aspect) : new Vector2(0.5f, 0.58f);
            var off = m_Parallax.Step(fc - new Vector2(0.5f, 0.58f), dt, 1.1f, 1f);
            m_Hud.anchoredPosition = new Vector2(-off.x * 46f, -off.y * 26f);
            m_Hud.localRotation = Quaternion.Euler(off.y * 5f, off.x * -7f, 0);
        }

        void UpdateChordPanel(string held, string baseChord, float beat, float dt)
        {
            string show = held ?? "";
            if (show != m_ShownChord)
            {
                m_ShownChord = show;
                var (letter, quality) = Chords.Split(show);
                m_Letter.text = show.Length == 0 ? "–" : $"{letter}<size=62%>{quality}</size>";
                m_Diagram.Show(held);
                m_Letter.transform.localScale = Vector3.one * 1.06f;
                m_Letter.ForceMeshUpdate();
                m_ChordCap.rectTransform.anchoredPosition = new Vector2(Mathf.Max(60f, m_Letter.preferredWidth) + 12f, -140f);
            }
            m_Letter.transform.localScale = Vector3.Lerp(m_Letter.transform.localScale, Vector3.one, 1f - Mathf.Exp(-10f * dt));
            m_StrumFlash = Mathf.Max(0, m_StrumFlash - dt * 3.5f);

            string target = beat >= 0 ? m_Song.ChordAt(beat) : m_Song.Chords.Count > 0 ? m_Song.Chords[0].Chord : null;
            string tBase = m_Song.GestureBase(target);
            // Songs with their own gesture map score the exact chord (e.g. Bb needs the curl, F alone is wrong).
            bool on = target != null && (m_Song.Gestures != null ? held == target : baseChord == tBase);
            var baseColor = held == null ? Theme.Paper.A(0.5f) : on || target == null ? Theme.Paper : Theme.Hex("E9A38A");
            m_Letter.color = Color.Lerp(baseColor, Theme.Hex("FFE6C7"), m_StrumFlash * 0.6f);

            int tf = m_Song.FingersFor(target);
            string curl = m_Song.Gestures != null && m_Song.NeedsAlternate(target) ? " + CURL" : "";
            if (target == null) m_Status.text = "";
            else if (held == null) m_Status.text = $"SHOW <color=#F2A546>{tf} FINGER{(tf == 1 ? "" : "S")}{curl}</color> FOR {target}";
            else if (on) m_Status.text = $"<color=#F2C48A>ON CHORD</color>   ·   STREAK {m_Streak}";
            else m_Status.text = $"SONG IS ON <color=#F2A546>{target}</color>  ·  {tf} FINGER{(tf == 1 ? "" : "S")}{curl}";
        }

        void UpdateNext(float beat)
        {
            // Find the next chord change after now (or the first chord during the count-in).
            float b = Mathf.Max(0, beat);
            int idx = beat < 0 ? 0 : m_Song.ChordIndexAt(b);
            string current = idx >= 0 ? m_Song.Chords[idx].Chord : null;
            int nextIdx = idx + 1;
            while (nextIdx < m_Song.Chords.Count && m_Song.Chords[nextIdx].Chord == current) nextIdx++;

            string next;
            float until;
            if (beat < 0)
            {
                next = m_Song.Chords[0].Chord;
                until = -beat;
            }
            else if (nextIdx < m_Song.Chords.Count)
            {
                next = m_Song.Chords[nextIdx].Chord;
                until = m_Song.Chords[nextIdx].Beat - b;
            }
            else
            {
                next = null;
                until = 0;
            }

            // Show the upcoming chord once it is within two bars; otherwise show what to hold now.
            bool soon = next != null && until <= m_Song.Meter * 2;
            string shown = soon ? next : current;
            m_NextCap.text = soon ? (beat < 0 ? "FIRST CHORD" : "NEXT CHORD") : "HOLD";
            if (shown != null)
            {
                int f = m_Song.FingersFor(shown);
                var (letter, quality) = Chords.Split(shown);
                m_NextName.text = $"{letter}<size=70%>{quality}</size>";
                m_NextFingers.text = $"{f} FINGER{(f == 1 ? "" : "S")}{(m_Song.Gestures != null && m_Song.NeedsAlternate(shown) ? " + CURL" : "")}";
                m_NextHand.Target = HandShape.Fingers(f);
            }

            float window = m_Song.Meter * 2f;
            float progress = soon ? 1f - Mathf.Clamp01(until / window) : 0f;
            m_Countdown.Clear();
            m_Countdown.Arc(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 0f, 360f, 36, 1.2f, 0.25f);
            if (progress > 0) m_Countdown.Arc(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 90f, 90f - 360f * progress, 36);
            m_NextCard.Active = soon && until < m_Song.Meter;
            m_NextHand.Glow = soon ? progress * 0.6f : 0f;
        }

        void UpdateLyrics(float beat)
        {
            if (m_Song.Lines.Count == 0) return;
            float look = beat + 0.2f;
            int li = Mathf.Clamp(m_Song.LineIndexAt(look), 0, m_Song.Lines.Count - 1);
            var line = m_Song.Lines[li];
            int wi = -1;
            for (int w = 0; w < line.Words.Count; w++)
                if (look >= line.Words[w].Beat) wi = w;

            if (li != m_LineIndex)
            {
                m_LineIndex = li;
                m_WordIndex = -2;
                m_NextLine.text = li + 1 < m_Song.Lines.Count ? m_Song.Lines[li + 1].Text : "";
                var lrt = m_Line.rectTransform;
                lrt.anchoredPosition = new Vector2(0, -24);
                Anim.Move(lrt, new Vector2(0, -8), 0.45f, 0, Ease.OutCubic);
            }

            if (wi != m_WordIndex)
            {
                m_WordIndex = wi;
                m_Sb.Clear();
                for (int w = 0; w < line.Words.Count; w++)
                {
                    var word = line.Words[w];
                    if (w < wi) m_Sb.Append("<color=#F2A56A>").Append(word.Text).Append("</color>");
                    else if (w == wi) m_Sb.Append("<color=#FFC08A>").Append(word.Text).Append("</color>");
                    else m_Sb.Append(word.Text);
                    if (!word.JoinNext && w < line.Words.Count - 1) m_Sb.Append("  ");
                }
                m_Line.text = m_Sb.ToString();
            }

            float total = m_Song.TotalBeats;
            float p = Mathf.Clamp01(beat / Mathf.Max(1, total));
            var track = (RectTransform)m_ProgressFill.rectTransform.parent;
            float width = track.sizeDelta.x;
            m_ProgressFill.rectTransform.sizeDelta = new Vector2(width * p, 2f);
            m_Knob.rectTransform.anchoredPosition = new Vector2(width * p, 0);

            float secs = Mathf.Max(0, beat) * m_Song.SecondsPerBeat;
            float dur = m_Song.TotalBeats * m_Song.SecondsPerBeat;
            m_Time.text = $"{(int)secs / 60}:{(int)secs % 60:00}  /  {(int)dur / 60}:{(int)dur % 60:00}";
            m_Score.text = m_Result.Strums > 0
                ? $"ACCURACY {Mathf.RoundToInt(m_Result.Accuracy * 100)}%   ·   STREAK {m_Streak}   ·   BEST {m_Result.BestStreak}"
                : "";
        }

        void UpdateAnchors(string baseChord, string held)
        {
            var g = App.Gestures;
            var feed = App.Feed;
            var size = App.CanvasSize;
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            Vector2 ToCanvas(Vector2 uv)
            {
                var v = feed.FrameToView(uv, aspect);
                // HUD is parallax-shifted; anchors must stay glued to the image, so undo it.
                return new Vector2((v.x - 0.5f) * size.x, (v.y - 0.5f) * size.y) - m_Hud.anchoredPosition;
            }

            int[] tips = { Lm.ThumbTip, Lm.IndexTip, Lm.MiddleTip, Lm.RingTip, Lm.PinkyTip };
            var chordHand = g.ChordHand;
            for (int f = 0; f < 5; f++)
            {
                bool on = chordHand != null && chordHand.Pose.Extended[f];
                var dot = m_Tips[f];
                dot.enabled = on;
                if (on)
                {
                    dot.rectTransform.anchoredPosition = ToCanvas(chordHand.Uv(tips[f], feed.Aspect));
                    float pulse = 1f + 0.35f * m_StrumFlash;
                    dot.rectTransform.localScale = Vector3.one * pulse;
                }
            }

            m_HandTag.gameObject.SetActive(chordHand != null && held != null);
            if (chordHand != null && held != null)
            {
                // Float the tag just above the highest point of the hand.
                Vector2 highest = new Vector2(0, float.MinValue);
                Vector2 sum = Vector2.zero;
                for (int k = 0; k < Lm.Count; k++)
                {
                    var q = ToCanvas(chordHand.Uv(k, feed.Aspect));
                    sum += q;
                    if (q.y > highest.y) highest = q;
                }
                var pos = new Vector2(sum.x / Lm.Count, highest.y + 58f);
                m_HandTag.anchoredPosition = Vector2.Lerp(m_HandTag.anchoredPosition, pos, 0.5f);
                var (letter, quality) = Chords.Split(held);
                m_HandTagText.text = $"{letter}<size=72%>{quality}</size>";
            }

            var expr = g.ExpressionHand;
            m_Gauge.gameObject.SetActive(expr != null);
            if (expr != null)
            {
                var palm = expr.Pose.Palm;
                var uv = new Vector2(palm.x / feed.Aspect, palm.y);
                m_Gauge.rectTransform.anchoredPosition = ToCanvas(uv);
                m_Gauge.Clear();
                m_Gauge.Arc(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 220f, -40f, 48, 1.2f, 0.25f);
                m_Gauge.Arc(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 220f, 220f - 260f * g.Volume, 48);
                m_GaugeText.text = $"VOL {Mathf.RoundToInt(g.Volume * 100)}  ·  {(g.Minor ? "MINOR" : "MAJOR")}";
            }
        }

        void UpdateCount(float beat)
        {
            if (!m_Started || beat >= 0 || m_Paused)
            {
                m_Count.text = "";
                return;
            }
            int n = Mathf.CeilToInt(-beat);
            float frac = 1f - (-beat - (n - 1));
            m_Count.text = n.ToString();
            m_Count.alpha = Mathf.Clamp01(1.2f - frac * 1.1f);
            m_Count.transform.localScale = Vector3.one * (1f + 0.12f * (1f - frac));
        }

        void ShowResults()
        {
            m_Done = true;
            App.Audio.Hold(null);
            // Flush anything still inside its grace window.
            ScorePending(Time.unscaledTime + GraceAfter + 1f);
            App.Session.LastResult = m_Result;
            m_Grade.text = $"{m_Result.Grade}<size=38%>\n<color=#F2C48A>{Mathf.RoundToInt(m_Result.Accuracy * 100)}% ON CHORD</color></size>";
            m_ResultLine.text = $"{m_Song.Title}\n{m_Result.Hits} of {m_Result.Strums} strums on the right chord  ·  best streak {m_Result.BestStreak}";
            m_Results.gameObject.SetActive(true);
            var grp = m_Results.Group();
            grp.alpha = 0;
            Anim.Fade(grp, 1f, 0.8f);
        }

        void OnDestroy()
        {
            if (m_Composite != null) Destroy(m_Composite);
        }
    }
}
