using System.Text;
using Solaris.Gestures;
using Solaris.UI;
using Solaris.Vision;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Solaris.Diagnostics
{
    /// <summary>
    /// F1: live diagnostics over everything - fps, camera, inference timing, landmark skeletons,
    /// finger states and expression values. C cycles cameras, H swaps the chord hand, M toggles mirror.
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        public static float Fps { get; private set; }
        public static bool Visible { get; set; }

        App m_App;
        RectTransform m_Root;
        LineGraphic m_Skeleton;
        TextMeshProUGUI m_Text;
        readonly StringBuilder m_Sb = new StringBuilder(1024);
        float m_FpsClock;
        int m_FpsFrames;

        public void Init(App app)
        {
            m_App = app;
            m_Root = (RectTransform)transform;
            m_Root.SetParent(app.Overlay, false);
            m_Root.Stretch();

            m_Skeleton = UIKit.Node("Skeleton", m_Root).gameObject.AddComponent<LineGraphic>();
            m_Skeleton.rectTransform.Stretch();
            m_Skeleton.color = new Color(0.55f, 1f, 0.8f, 0.95f);
            m_Skeleton.Stroke = 2.5f;
            m_Skeleton.raycastTarget = false;

            var panel = UIKit.Shape(m_Root, new Color(0, 0, 0, 0.62f), 10, "Panel");
            panel.rectTransform.Place(new Vector2(0, 1), new Vector2(16, -16), new Vector2(460, 420));
            m_Text = UIKit.Text(panel.rectTransform, "", Theme.Sans, 15, new Color(0.9f, 1f, 0.95f), TextAlignmentOptions.TopLeft);
            m_Text.rectTransform.Stretch(14, 12, 10, 10);
            m_Text.richText = true;

            Visible = AppArgs.DebugHud;
            m_Root.gameObject.SetActive(true);
            SetVisible(Visible);
        }

        void SetVisible(bool v)
        {
            Visible = v;
            foreach (Transform c in m_Root) c.gameObject.SetActive(v);
        }

        void Update()
        {
            m_FpsFrames++;
            m_FpsClock += Time.unscaledDeltaTime;
            if (m_FpsClock >= 0.5f)
            {
                Fps = m_FpsFrames / m_FpsClock;
                m_FpsFrames = 0;
                m_FpsClock = 0;
            }

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f1Key.wasPressedThisFrame) SetVisible(!Visible);
                if (Visible && kb.cKey.wasPressedThisFrame) m_App.Feed.CycleDevice();
                if (Visible && kb.mKey.wasPressedThisFrame) m_App.Feed.Mirror = !m_App.Feed.Mirror;
                if (kb.hKey.wasPressedThisFrame) m_App.Gestures.ChordOnRight = !m_App.Gestures.ChordOnRight;
            }
            if (m_Skeleton.gameObject.activeSelf != Visible) SetVisible(Visible);
            if (!Visible) return;

            DrawSkeletons();
            WriteText();
        }

        void DrawSkeletons()
        {
            m_Skeleton.Clear();
            var feed = m_App.Feed;
            float viewAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            foreach (var h in m_App.Vision.Hands)
            {
                if (!h.Visible) continue;
                for (int b = 0; b < Lm.Bones.Length; b += 2)
                {
                    var a = feed.FrameToView(h.Uv(Lm.Bones[b], feed.Aspect), viewAspect);
                    var c = feed.FrameToView(h.Uv(Lm.Bones[b + 1], feed.Aspect), viewAspect);
                    m_Skeleton.Line(a, c);
                }
                for (int i = 0; i < Lm.Count; i++)
                {
                    var p = feed.FrameToView(h.Uv(i, feed.Aspect), viewAspect);
                    m_Skeleton.Line(p, p + new Vector2(0.0005f, 0), 9f);
                }
            }

            var face = m_App.Vision.Face;
            if (face.Visible)
            {
                var c = feed.FrameToView(face.Center, viewAspect);
                var s = new Vector2(face.Size.x * feed.Aspect / viewAspect, face.Size.y) * 0.5f;
                m_Skeleton.Line(c + new Vector2(-s.x, -s.y), c + new Vector2(s.x, -s.y), 1.5f, 0.6f);
                m_Skeleton.Line(c + new Vector2(s.x, -s.y), c + new Vector2(s.x, s.y), 1.5f, 0.6f);
                m_Skeleton.Line(c + new Vector2(s.x, s.y), c + new Vector2(-s.x, s.y), 1.5f, 0.6f);
                m_Skeleton.Line(c + new Vector2(-s.x, s.y), c + new Vector2(-s.x, -s.y), 1.5f, 0.6f);
            }
        }

        void WriteText()
        {
            var v = m_App.Vision;
            var g = m_App.Gestures;
            var f = m_App.Feed;
            m_Sb.Clear();
            m_Sb.Append("<b>SOLARIS DIAGNOSTICS</b>   F1 hide  ·  C camera  ·  H swap  ·  M mirror\n");
            m_Sb.AppendFormat("render {0:0} fps   {1}x{2}   gpu {3}\n", Fps, Screen.width, Screen.height, SystemInfo.graphicsDeviceName);
            m_Sb.AppendFormat("camera {0}  {1}x{2}  {3:0} fps{4}\n", f.DeviceName, f.Width, f.Height, f.MeasuredFps, f.Error != null ? "  ERR " + f.Error : "");
            m_Sb.AppendFormat("vision {0}  {1:0.0} Hz  {2:0.0} ms/iter  hands {3}  face {4}\n",
                v.IsReady ? "ready" : v.IsLoading ? "loading" : v.LoadError ?? "idle", v.Rate, v.IterationMs, v.VisibleHands, v.Face.Visible ? "yes" : "no");
            m_Sb.AppendFormat("chord hand {0}   fingers {1}   chord {2}{3}\n",
                g.ChordHand != null ? "#" + g.ChordHand.Slot : "-", g.Fingers, Music.Chords.FromFingers(g.Fingers) ?? "mute", g.Minor ? " (minor)" : "");
            m_Sb.AppendFormat("vol {0:0.00}  tone {1:0.00}  pan {2:+0.00;-0.00}  depth {3:0.00}  pinch {4}  circle {5:0.0} rad\n",
                g.Volume, g.Tone, g.Pan, g.Depth, g.Pinching ? "ON" : "off", g.Circle.Total);
            foreach (var h in g.Hands)
            {
                if (!h.Visible) continue;
                var p = h.Pose;
                m_Sb.AppendFormat("\n<b>hand #{0}</b> id {1}  presence {2:0.00}  R {3:0.00}\n", h.Slot, h.Track.Id, h.Track.Presence, h.Track.RightHanded);
                m_Sb.AppendFormat("  count {0}   T {1:0.00} I {2:0.00} M {3:0.00} R {4:0.00} P {5:0.00}\n", p.Count,
                    p.Extension[0], p.Extension[1], p.Extension[2], p.Extension[3], p.Extension[4]);
                m_Sb.AppendFormat("  open {0:0.00}  roll {1:0}°  pitch {2:+0.00;-0.00}  pinch {3:0.00}\n", p.Openness, p.Roll * Mathf.Rad2Deg, p.Pitch, p.PinchDistance);
            }
            var a = m_App.Audio;
            m_Sb.AppendFormat("\naudio level {0:0.00}  held {1}  beat {2:0.00}", a.Level, a.HeldChord ?? "-", a.SongBeat);
            m_Text.text = m_Sb.ToString();
        }
    }
}
