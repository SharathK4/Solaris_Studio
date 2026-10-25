using System;
using System.Collections.Generic;
using Solaris.Music;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Solaris.UI
{
    /// <summary>Guitar chord box: nut, 4 frets, 6 strings, finger dots, x/o markers.</summary>
    public sealed class ChordDiagram : MonoBehaviour
    {
        LineGraphic m_Grid;
        readonly List<ShapeGraphic> m_Dots = new List<ShapeGraphic>();
        readonly List<TextMeshProUGUI> m_Marks = new List<TextMeshProUGUI>();
        Color m_Color = Theme.Cream;
        /// <summary>Grid opacity relative to the dot colour.</summary>
        public float GridOpacity = 0.85f;
        /// <summary>Hairline width relative to the default (width / 70).</summary>
        public float StrokeScale = 1f;
        /// <summary>Nut (top line) thickness in hairlines; 1 = same as the frets.</summary>
        public float NutScale = 2.4f;
        /// <summary>Finger dot diameter as a fraction of the string spacing.</summary>
        public float DotScale = 0.72f;

        public static ChordDiagram Create(Transform parent, Vector2 size, Color color)
        {
            var rt = UIKit.Node("ChordDiagram", parent);
            rt.sizeDelta = size;
            var d = rt.gameObject.AddComponent<ChordDiagram>();
            d.m_Color = color;
            d.m_Grid = UIKit.Node("Grid", rt).gameObject.AddComponent<LineGraphic>();
            d.m_Grid.rectTransform.Stretch(0, size.y * 0.16f, 0, 0);
            d.m_Grid.color = color.A(color.a * 0.85f);
            d.m_Grid.Stroke = Mathf.Max(1.2f, size.x / 70f);
            d.m_Grid.raycastTarget = false;
            return d;
        }

        public void Show(string chordName)
        {
            var chord = Chords.Get(chordName);
            var rt = (RectTransform)transform;
            var size = rt.sizeDelta;
            // Re-derive the internal layout from the current size (the widget may have been resized).
            m_Grid.rectTransform.Stretch(0, size.y * 0.16f, 0, 0);
            m_Grid.Stroke = Mathf.Max(1.2f, size.x / 70f) * StrokeScale;
            m_Grid.color = m_Color.A(m_Color.a * GridOpacity);
            m_Grid.Clear();
            foreach (var old in m_Dots) Destroy(old.gameObject);
            foreach (var m in m_Marks) Destroy(m.gameObject);
            m_Dots.Clear();
            m_Marks.Clear();
            if (chord == null) return;

            int frets = 4;
            int minFret = 1;
            int maxFret = 0;
            foreach (var f in chord.Frets) maxFret = Mathf.Max(maxFret, f);
            if (maxFret > 4)
            {
                minFret = int.MaxValue;
                foreach (var f in chord.Frets) if (f > 0) minFret = Mathf.Min(minFret, f);
            }

            for (int s = 0; s < 6; s++) m_Grid.Line(new Vector2(s / 5f, 0), new Vector2(s / 5f, 1));
            if (minFret > 1)
            {
                var fr = UIKit.Text(rt, $"{minFret}fr", Theme.Sans, Mathf.Max(9f, size.x * 0.13f), m_Color.A(0.75f), TextAlignmentOptions.Right);
                fr.rectTransform.Place(new Vector2(0, 1), new Vector2(-size.x * 0.11f, -size.y * 0.16f - size.y * 0.84f / 8f), new Vector2(40, 18), new Vector2(1, 0.5f));
                m_Marks.Add(fr);
            }
            for (int f = 0; f <= frets; f++)
            {
                float y = 1f - f / (float)frets;
                m_Grid.Line(new Vector2(0, y), new Vector2(1, y), f == 0 && minFret == 1 ? m_Grid.Stroke * NutScale : -1f);
            }

            var gridRect = m_Grid.rectTransform;
            float gw = size.x, gh = size.y * 0.84f;
            float dot = gw / 5f * DotScale;
            float mark = gw / 5f * 0.72f;      // open/muted string markers keep their size
            for (int s = 0; s < 6; s++)
            {
                int fret = chord.Frets[s];
                float x = s / 5f * gw - gw * 0.5f;
                if (fret < 0)
                {
                    var cross = UIKit.Text(rt, "×", Theme.Sans, mark * 1.2f, m_Color.A(m_Color.a * 0.85f));
                    cross.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(x, 0), new Vector2(mark * 1.5f, size.y * 0.16f), new Vector2(0.5f, 1f));
                    m_Marks.Add(cross);
                    continue;
                }
                if (fret == 0)
                {
                    float r = mark * 0.62f;
                    var ring = UIKit.Shape(rt, new Color(1, 1, 1, 0), r * 0.5f, "Open");
                    ring.BorderWidth = Mathf.Max(1f, m_Grid.Stroke * 0.8f);
                    ring.Border = m_Color.A(m_Color.a * 0.85f);
                    ring.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(x, -size.y * 0.07f), new Vector2(r, r), new Vector2(0.5f, 0.5f));
                    m_Dots.Add(ring);
                    continue;
                }
                int rel = fret - minFret + 1;
                float y = gh - (rel - 0.5f) / frets * gh;
                var d = UIKit.Shape(gridRect, m_Color, dot * 0.5f, "Dot");
                d.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(x, y), new Vector2(dot, dot), new Vector2(0.5f, 0.5f));
                m_Dots.Add(d);
            }
        }
    }

    /// <summary>
    /// Cream pill call-to-action. Activates on click, Enter (if bound by the screen), or a hand
    /// pinch-hold / dwell routed through <see cref="HandPointer"/>; shows hold progress inside.
    /// </summary>
    public sealed class PillButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public event Action Clicked;
        public ShapeGraphic Background { get; private set; }
        public TextMeshProUGUI Label { get; private set; }
        /// <summary>Trailing icon (the arrow), if any.</summary>
        public VectorIcon RightIcon { get; private set; }
        public float Progress { get => m_Progress; set { m_Progress = Mathf.Clamp01(value); } }
        public bool Hovered => m_MouseHover || HandHover;
        public bool HandHover { get; set; }
        public bool Interactable = true;
        /// <summary>Colour of the soft glow under the pill (alpha = at rest; it grows a little on hover).</summary>
        public Color Glow = Theme.Ember.A(0.22f);

        ShapeGraphic m_Fill;
        RectTransform m_Rt;
        bool m_MouseHover;
        float m_Progress, m_Hover;

        public static PillButton Create(Transform parent, string label, Vector2 size, VectorIcon.Kind? left = null, VectorIcon.Kind? right = VectorIcon.Kind.ArrowRight)
        {
            var rt = UIKit.Node("Pill " + label, parent);
            rt.sizeDelta = size;
            var b = rt.gameObject.AddComponent<PillButton>();
            b.m_Rt = rt;

            b.Background = rt.gameObject.AddComponent<ShapeGraphic>();
            b.Background.color = Theme.Paper;
            b.Background.FillBottom = Theme.Hex("EFE5D6");
            b.Background.Radius = size.y * 0.5f;
            b.Background.ShadowSize = 26f;
            b.Background.Shadow = Theme.Ember.A(0.28f);
            b.Background.raycastTarget = true;

            b.m_Fill = UIKit.Shape(rt, Theme.Sand.A(0.9f), size.y * 0.5f, "HoldFill");
            b.m_Fill.rectTransform.Place(new Vector2(0, 0.5f), Vector2.zero, new Vector2(0, size.y), new Vector2(0, 0.5f));

            float textX = 0;
            if (left.HasValue)
            {
                var i = UIKit.Icon(rt, left.Value, Theme.Ink, size.y * 0.3f, 1.8f);
                i.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(size.y * 0.62f, 0), new Vector2(size.y * 0.3f, size.y * 0.3f), new Vector2(0.5f, 0.5f));
                textX += size.y * 0.16f;
            }
            if (right.HasValue)
            {
                var i = UIKit.Icon(rt, right.Value, Theme.Ink, size.y * 0.36f, 1.6f);
                i.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(-size.y * 0.6f, 0), new Vector2(size.y * 0.4f, size.y * 0.32f), new Vector2(0.5f, 0.5f));
                b.RightIcon = i;
                textX -= size.y * 0.2f;
            }
            b.Label = UIKit.Text(rt, label, Theme.Sans, size.y * 0.3f, Theme.Ink, TextAlignmentOptions.Center, 0.5f);
            b.Label.rectTransform.Stretch();
            b.Label.rectTransform.anchoredPosition = new Vector2(textX, 0);
            return b;
        }

        /// <summary>Re-derives the pill's rounding and hold-fill after its rect was resized.</summary>
        public void Fit()
        {
            var size = m_Rt.sizeDelta;
            Background.Radius = size.y * 0.5f;
            m_Fill.Radius = size.y * 0.5f;
            m_Fill.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(size.y, size.x, m_Progress), size.y);
        }

        public void Press()
        {
            if (!Interactable) return;
            Anim.Run(0.35f, k => { if (m_Rt) m_Rt.localScale = Vector3.one * (1f - 0.05f * Mathf.Sin(k * Mathf.PI)); }, Ease.OutCubic, 0, null, this);
            Clicked?.Invoke();
        }

        void Awake()
        {
            if (m_Rt == null) m_Rt = (RectTransform)transform;
        }

        public void OnPointerEnter(PointerEventData e) => m_MouseHover = true;
        public void OnPointerExit(PointerEventData e) => m_MouseHover = false;
        public void OnPointerClick(PointerEventData e)
        {
            if (!AppArgs.Automation) Press();
        }

        public bool ContainsScreenPoint(Vector2 screen, Camera cam) =>
            RectTransformUtility.RectangleContainsScreenPoint(m_Rt, screen, cam);

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            m_Hover = Mathf.Lerp(m_Hover, Hovered && Interactable ? 1f : 0f, 1f - Mathf.Exp(-12f * dt));
            if (Background == null) return;   // bare hit target (e.g. instrument cards)
            float s = 1f + 0.03f * m_Hover;
            if (m_Rt.localScale.x < 0.99f || m_Rt.localScale.x > 1.001f || m_Hover > 0.001f)
                m_Rt.localScale = Vector3.Lerp(m_Rt.localScale, Vector3.one * s, 1f - Mathf.Exp(-14f * dt));
            Background.ShadowSize = 22f + 18f * m_Hover;
            Background.Shadow = Glow.A(Glow.a * (1f + 1.2f * m_Hover));
            var size = m_Rt.sizeDelta;
            m_Fill.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(size.y, size.x, m_Progress), size.y);
            m_Fill.color = Theme.Sand.A(m_Progress > 0.001f ? 0.95f : 0f);
            Background.color = Interactable ? Theme.Paper : Theme.Paper.A(0.35f);
        }
    }

    /// <summary>
    /// Index-fingertip cursor for hand-driven UI: glowing dot + trail, pinch-hold ring. Also routes
    /// hover/pinch/dwell to <see cref="PillButton"/>s registered by the current screen.
    /// </summary>
    public sealed class HandPointer : MonoBehaviour
    {
        public const float PinchHold = 0.45f;
        public const float Dwell = 1.8f;

        readonly List<PillButton> m_Buttons = new List<PillButton>();
        ShapeGraphic m_Dot, m_Ring;
        LineGraphic m_Progress, m_Trail;
        RectTransform m_Rt, m_Space;
        CanvasGroup m_Group;
        Spring2 m_Pos;
        readonly Queue<Vector2> m_TrailPts = new Queue<Vector2>();
        PillButton m_Target;
        float m_Hold, m_DwellT;
        bool m_Visible;

        public Vector2 ViewUv { get; private set; }
        public bool Visible => m_Visible;
        public bool AllowDwell = true;

        public static HandPointer Create(RectTransform space)
        {
            var rt = UIKit.Node("HandPointer", space);
            rt.Stretch();
            var p = rt.gameObject.AddComponent<HandPointer>();
            p.m_Space = space;
            p.m_Rt = rt;
            p.m_Group = rt.Group();
            p.m_Group.blocksRaycasts = false;
            p.m_Group.alpha = 0;

            p.m_Trail = UIKit.Node("Trail", rt).gameObject.AddComponent<LineGraphic>();
            p.m_Trail.rectTransform.Stretch();
            p.m_Trail.color = Theme.Amber.A(0.55f);
            p.m_Trail.Stroke = 3f;
            p.m_Trail.raycastTarget = false;

            p.m_Ring = UIKit.Shape(rt, new Color(1, 1, 1, 0), 22, "Ring");
            p.m_Ring.BorderWidth = 1.5f;
            p.m_Ring.Border = Theme.Cream.A(0.8f);
            p.m_Ring.rectTransform.sizeDelta = new Vector2(44, 44);
            p.m_Dot = UIKit.Shape(rt, Theme.Paper, 7, "Dot");
            p.m_Dot.ShadowSize = 14;
            p.m_Dot.Shadow = Theme.Ember.A(0.8f);
            p.m_Dot.rectTransform.sizeDelta = new Vector2(14, 14);
            p.m_Progress = UIKit.Node("Progress", rt).gameObject.AddComponent<LineGraphic>();
            p.m_Progress.rectTransform.sizeDelta = new Vector2(56, 56);
            p.m_Progress.color = Theme.Amber;
            p.m_Progress.Stroke = 3f;
            p.m_Progress.raycastTarget = false;
            foreach (var r in new[] { p.m_Ring.rectTransform, p.m_Dot.rectTransform, p.m_Progress.rectTransform })
            {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
            }
            return p;
        }

        public void Register(PillButton b)
        {
            if (!m_Buttons.Contains(b)) m_Buttons.Add(b);
        }

        void Update()
        {
            var app = App.Instance;
            var g = app.Gestures;
            float dt = Time.unscaledDeltaTime;
            bool vis = g.PointerVisible;
            m_Visible = vis;
            m_Group.alpha = Mathf.Lerp(m_Group.alpha, vis ? 1f : 0f, 1f - Mathf.Exp(-10f * dt));

            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            var view = app.Feed.FrameToView(g.PointerUv, aspect);
            ViewUv = view;
            var size = m_Space.rect.size;
            var target = new Vector2((view.x - 0.5f) * size.x, (view.y - 0.5f) * size.y);
            if (!vis) m_Pos = new Spring2(target);
            var pos = m_Pos.Step(target, dt, 7f, 0.95f);
            m_Dot.rectTransform.anchoredPosition = pos;
            m_Ring.rectTransform.anchoredPosition = pos;
            m_Progress.rectTransform.anchoredPosition = pos;

            // Trail (in normalised space of the full rect).
            var n = new Vector2(pos.x / size.x + 0.5f, pos.y / size.y + 0.5f);
            m_TrailPts.Enqueue(n);
            while (m_TrailPts.Count > 18) m_TrailPts.Dequeue();
            m_Trail.Clear();
            if (vis)
            {
                Vector2 prev = default;
                int i = 0;
                foreach (var t in m_TrailPts)
                {
                    if (i > 0) m_Trail.Line(prev, t, 1f + 3f * i / 18f, i / 18f);
                    prev = t;
                    i++;
                }
            }

            // Button routing.
            var screen = new Vector2(view.x * Screen.width, view.y * Screen.height);
            PillButton over = null;
            if (vis)
                foreach (var b in m_Buttons)
                    if (b != null && b.isActiveAndEnabled && b.Interactable && b.ContainsScreenPoint(screen, app.Canvas.worldCamera))
                        over = b;
            foreach (var b in m_Buttons)
                if (b != null) b.HandHover = b == over;

            if (over != m_Target)
            {
                if (m_Target != null) m_Target.Progress = 0;
                m_Target = over;
                m_Hold = 0;
                m_DwellT = 0;
            }

            float progress = 0;
            if (m_Target != null)
            {
                if (g.Pinching) m_Hold += dt;
                else m_Hold = Mathf.Max(0, m_Hold - dt * 2f);
                if (AllowDwell) m_DwellT += dt;
                progress = Mathf.Max(m_Hold / PinchHold, AllowDwell ? m_DwellT / Dwell : 0);
                m_Target.Progress = progress;
                if (progress >= 1f)
                {
                    var t = m_Target;
                    m_Target.Progress = 0;
                    m_Target = null;
                    m_Hold = m_DwellT = 0;
                    t.Press();
                }
            }

            m_Progress.Clear();
            if (progress > 0.01f) m_Progress.Arc(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 90f, 90f - 360f * Mathf.Clamp01(progress), 40);
            float pinchScale = g.Pinching ? 0.7f : 1f;
            m_Ring.rectTransform.localScale = Vector3.Lerp(m_Ring.rectTransform.localScale, Vector3.one * pinchScale, 1f - Mathf.Exp(-16f * dt));
        }
    }
}
