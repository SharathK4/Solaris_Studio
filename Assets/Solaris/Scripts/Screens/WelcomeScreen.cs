using System;
using Solaris.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Solaris.Screens
{
    /// <summary>
    /// Scene 2: "lets play your favourite song". The key art is rebuilt from layers
    /// (Tools/make_welcome.py) so the orb can bounce: a slow, physical bounce on a soft
    /// spring floor, with a gentle squash, a faint stretch at speed, and the sky warming as it rises.
    /// Text is re-typeset crisp, exactly over the reference's lettering. Any key or click continues.
    /// </summary>
    public sealed class WelcomeScreen : AppScreen
    {
        public override ScreenId Id => ScreenId.Welcome;
        public override float Grain => 0.03f;   // the art carries its own grain

        [Serializable]
        sealed class Line { public float x0, x1, top, baseline, bottom; }

        [Serializable]
        sealed class Meta
        {
            public int imageWidth = 1246, imageHeight = 697;
            public float[] orbCenter = { 0.5016f, 0.4964f };
            public float orbRadius = 0.3257f, orbMargin = 1.12f, detailScale = 1.6f;
            public Line[] lines = Array.Empty<Line>();
        }

        // Bounce, in image-height units (1 = the reference image's height).
        const float Apex = 0.075f;          // how high the orb's base rises above the floor
        const float FlightTime = 3.9f;      // seconds in the air per bounce
        const float ContactTime = 0.62f;    // seconds pressed into the soft floor
        const float FloorBelowRef = 0.03f;  // floor sits a little below the orb's position in the art

        Meta m_Meta;
        Material m_Mat;
        TextMeshProUGUI m_Line1, m_Line2, m_Caption;
        Vector2 m_LaidOutFor;
        float m_Height, m_Velocity, m_Gravity, m_Launch, m_Spring, m_Intro;
        bool m_Starting;
        float m_StartAt;

        protected override void Build()
        {
            var a = SolarisAssets.Get;
            m_Meta = a.welcomeMeta != null ? JsonUtility.FromJson<Meta>(a.welcomeMeta.text) : new Meta();

            m_Mat = SolarisAssets.Mat("Solaris/BG/WelcomeArt");
            m_Mat.SetTexture("_Plate", a.welcomePlate);
            m_Mat.SetTexture("_Detail", a.welcomeDetail);
            m_Mat.SetTexture("_Orb", a.welcomeOrb);
            m_Mat.SetFloat("_ImageAspect", (float)m_Meta.imageWidth / m_Meta.imageHeight);
            m_Mat.SetFloat("_DetailScale", m_Meta.detailScale);
            m_Mat.SetFloat("_OrbR", m_Meta.orbRadius);
            m_Mat.SetFloat("_OrbMargin", m_Meta.orbMargin);
            var art = UIKit.Raw(Root, Texture2D.whiteTexture, m_Mat, "KeyArt");
            art.rectTransform.Stretch();

            var ink = new Color(1f, 0.985f, 0.965f, 1f);
            m_Line1 = UIKit.Text(Root, "lets play your", Theme.Sans, 60, ink, TextAlignmentOptions.Midline, 0f, "Line1");
            m_Line2 = UIKit.Text(Root, "favourite song", Theme.Sans, 60, ink, TextAlignmentOptions.Midline, 0f, "Line2");
            m_Caption = UIKit.Text(Root, "Camera access will be required", Theme.Sans, 18, ink, TextAlignmentOptions.Midline, 0f, "Caption");

            // Physics: slow gravity, launch speed for the apex, spring floor for the contact time.
            m_Gravity = 8f * Apex / (FlightTime * FlightTime);
            m_Launch = m_Gravity * FlightTime * 0.5f;
            float w = Mathf.PI / ContactTime;
            m_Spring = w * w;
            m_Height = Apex * 0.55f;
            m_Velocity = 0.35f * m_Launch;
            Layout();
        }

        void Layout()
        {
            var size = App.CanvasSize;
            if (size.x <= 1) return;
            m_LaidOutFor = size;
            var rs = new RefSpace(m_Meta.imageWidth, m_Meta.imageHeight, size);
            m_Mat.SetVector("_Fit", rs.FitVector);
            var texts = new[] { m_Line1, m_Line2, m_Caption };
            for (int i = 0; i < texts.Length && i < m_Meta.lines.Length; i++)
            {
                var l = m_Meta.lines[i];
                rs.FitLine(texts[i], l.x0, l.x1, l.baseline);
            }
        }

        public override void Enter()
        {
            AddReveal(m_Line1, 0.55f);
            AddReveal(m_Line2, 0.85f);
            var cap = m_Caption.Group();
            cap.alpha = 0f;
            Anim.Fade(cap, 1f, 1.6f, 2.0f);
            Anim.Run(2.4f, k => m_Intro = k, Ease.OutCubic, 0.1f, null, this);
        }

        static void AddReveal(TextMeshProUGUI t, float delay)
        {
            var r = t.gameObject.AddComponent<TextReveal>();
            r.Delay = delay;
            r.Stagger = 0.045f;
            r.Duration = 1.1f;
            r.Rise = 14f;
        }

        protected override void Tick(float dt)
        {
            if (App.CanvasSize != m_LaidOutFor) Layout();
            Simulate(dt);
            Pose();

            if (m_Starting)
            {
                UpdateStarting();
                return;
            }
            if (Leaving || Age < 1.2f) return;
            if (Pressed(Key.Escape)) Go(ScreenId.Title);
            else if (AnyKeyToContinue() || Clicked()) BeginCamera();
        }

        /// <summary>Ballistic flight above a soft spring floor; energy is topped up each launch so it never decays.</summary>
        void Simulate(float dt)
        {
            const int steps = 8;
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float accel = -m_Gravity;
                if (m_Height < 0f) accel += -m_Spring * m_Height;
                bool wasBelow = m_Height < 0f;
                m_Velocity += accel * h;
                m_Height += m_Velocity * h;
                if (wasBelow && m_Height >= 0f && m_Velocity > 0f) m_Velocity = m_Launch;
            }
        }

        void Pose()
        {
            float R = m_Meta.orbRadius;
            float press = Mathf.Max(0f, -m_Height);                        // how far the floor is pressed
            float squash = Mathf.Clamp(press / R, 0f, 0.12f);
            float speed = Mathf.Clamp01(Mathf.Abs(m_Velocity) / m_Launch);
            float stretch = m_Height > 0f ? speed * speed * 0.018f : 0f;
            float sy = (1f - squash * 1.05f) * (1f + stretch);
            float sx = (1f + squash * 0.9f) * (1f - stretch * 0.6f);
            float intro = Mathf.Lerp(0.94f, 1f, m_Intro);

            float floorY = m_Meta.orbCenter[1] - R - FloorBelowRef;        // image uv, y up
            float baseY = floorY + Mathf.Max(0f, m_Height);
            float cy = baseY + R * sy * intro;
            float cx = m_Meta.orbCenter[0] + Mathf.Sin(Age * 0.37f) * 0.0025f;
            m_Mat.SetVector("_OrbC", new Vector4(cx, cy, 0, 0));
            m_Mat.SetVector("_OrbScale", new Vector4(sx * intro, sy * intro, 0, 0));

            float rise = Mathf.Clamp01(Mathf.Max(0f, m_Height) / Apex);
            m_Mat.SetFloat("_Rise", rise * m_Intro);
            m_Mat.SetFloat("_Halo", (0.45f + 0.55f * rise + 0.6f * squash / 0.05f) * m_Intro);
        }

        static bool AnyKeyToContinue()
        {
            var kb = Keyboard.current;
            if (AppArgs.Automation || kb == null || !kb.anyKey.wasPressedThisFrame) return false;
            if (kb.altKey.isPressed || kb.escapeKey.wasPressedThisFrame || kb.f1Key.wasPressedThisFrame || kb.f11Key.wasPressedThisFrame)
                return false;
            return true;
        }

        void BeginCamera()
        {
            m_Starting = true;
            m_StartAt = Age;
            App.Feed.StartCamera();
            App.Vision.Warmup();
            SetCaption("Starting your camera…");
        }

        void SetCaption(string text)
        {
            if (m_Caption.text == text) return;
            m_Caption.text = text;
            var l = m_Meta.lines.Length > 2 ? m_Meta.lines[2] : null;
            if (l != null) new RefSpace(m_Meta.imageWidth, m_Meta.imageHeight, App.CanvasSize).AlignLine(m_Caption, l.x0, l.x1, l.baseline);
        }

        void UpdateStarting()
        {
            var feed = App.Feed;
            if (feed.IsRunning && App.Vision.IsReady)
            {
                SetCaption("Camera ready");
                if (Age - m_StartAt > 1.1f) Go(ScreenId.SongSelect);
            }
            else if (!string.IsNullOrEmpty(feed.Error))
            {
                SetCaption(feed.Error + " — continuing with keyboard & mouse");
                if (Age - m_StartAt > 3.2f) Go(ScreenId.SongSelect);
            }
            else if (Age - m_StartAt > 1.5f)
            {
                SetCaption(feed.IsRunning ? "Waking up hand tracking…" : "Starting your camera…");
            }
        }

        void OnDestroy()
        {
            if (m_Mat != null) Destroy(m_Mat);
        }
    }
}
