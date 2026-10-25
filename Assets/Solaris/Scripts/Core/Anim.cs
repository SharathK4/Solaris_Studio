using System;
using System.Collections.Generic;
using UnityEngine;

namespace Solaris
{
    public static class Ease
    {
        public static float Linear(float t) => t;
        public static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        public static float InCubic(float t) => t * t * t;
        public static float InOutCubic(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        public static float OutQuint(float t) => 1f - Mathf.Pow(1f - t, 5f);
        public static float OutExpo(float t) => t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
        public static float InOutSine(float t) => -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f;
        public static float InOutQuart(float t) => t < 0.5f ? 8f * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 4f) / 2f;

        public static float OutBack(float t)
        {
            const float c1 = 1.20158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        /// <summary>Frame-rate independent exponential approach: fraction to move this frame.</summary>
        public static float Damp(float sharpness, float dt) => 1f - Mathf.Exp(-sharpness * dt);
    }

    /// <summary>A critically-damped-ish spring for UI values that should glide rather than snap.</summary>
    [Serializable]
    public struct Spring
    {
        public float Value, Velocity;

        public Spring(float v) { Value = v; Velocity = 0f; }

        public float Step(float target, float dt, float frequency = 6f, float damping = 1f)
        {
            // Semi-implicit Euler on a damped harmonic oscillator; stable for dt up to ~1/frequency.
            dt = Mathf.Min(dt, 0.05f);
            float w = 2f * Mathf.PI * frequency;
            float f = 1f + 2f * dt * damping * w;
            float oo = w * w, hoo = dt * oo, hhoo = dt * hoo;
            float detInv = 1f / (f + hhoo);
            float detX = f * Value + dt * Velocity + hhoo * target;
            float detV = Velocity + hoo * (target - Value);
            Value = detX * detInv;
            Velocity = detV * detInv;
            return Value;
        }
    }

    [Serializable]
    public struct Spring2
    {
        public Spring X, Y;
        public Spring2(Vector2 v) { X = new Spring(v.x); Y = new Spring(v.y); }
        public Vector2 Value => new Vector2(X.Value, Y.Value);

        public Vector2 Step(Vector2 target, float dt, float frequency = 6f, float damping = 1f) =>
            new Vector2(X.Step(target.x, dt, frequency, damping), Y.Step(target.y, dt, frequency, damping));
    }

    public sealed class Tween
    {
        internal float Duration, Delay, Elapsed;
        internal Action<float> Step;
        internal Func<float, float> Curve;
        internal Action Done;
        internal UnityEngine.Object Owner;
        internal bool HasOwner, Killed, Unscaled = true;

        public void Kill() => Killed = true;
        public bool IsAlive => !Killed;
    }

    /// <summary>Allocation-light tween runner. Tweens bound to an owner die with it.</summary>
    public sealed class Anim : MonoBehaviour
    {
        static Anim s_Instance;
        readonly List<Tween> m_Tweens = new List<Tween>(128);
        readonly List<Tween> m_Adding = new List<Tween>(32);

        static Anim Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    var go = new GameObject("[Anim]");
                    DontDestroyOnLoad(go);
                    s_Instance = go.AddComponent<Anim>();
                }
                return s_Instance;
            }
        }

        public static Tween Run(float duration, Action<float> step, Func<float, float> ease = null, float delay = 0f,
            Action done = null, UnityEngine.Object owner = null)
        {
            var t = new Tween
            {
                Duration = Mathf.Max(0.0001f, duration),
                Delay = delay,
                Step = step,
                Curve = ease ?? Ease.OutCubic,
                Done = done,
                Owner = owner,
                HasOwner = owner != null,
            };
            Instance.m_Adding.Add(t);
            if (delay <= 0f) step?.Invoke(0f);
            return t;
        }

        public static Tween Fade(CanvasGroup g, float to, float duration, float delay = 0f, Func<float, float> ease = null, Action done = null)
        {
            float from = g.alpha;
            return Run(duration, k => { if (g) g.alpha = Mathf.LerpUnclamped(from, to, k); }, ease ?? Ease.InOutSine, delay, done, g);
        }

        public static Tween Move(RectTransform rt, Vector2 to, float duration, float delay = 0f, Func<float, float> ease = null)
        {
            Vector2 from = rt.anchoredPosition;
            return Run(duration, k => { if (rt) rt.anchoredPosition = Vector2.LerpUnclamped(from, to, k); }, ease, delay, null, rt);
        }

        /// <summary>Stagger-in helper: fades and lifts an element into place.</summary>
        public static void Reveal(RectTransform rt, float delay, float lift = 24f, float duration = 1.1f)
        {
            if (!rt) return;
            if (!rt.TryGetComponent(out CanvasGroup g)) g = rt.gameObject.AddComponent<CanvasGroup>();
            Vector2 home = rt.anchoredPosition;
            g.alpha = 0f;
            rt.anchoredPosition = home - new Vector2(0f, lift);
            Run(duration, k =>
            {
                if (!rt) return;
                g.alpha = Mathf.Clamp01(k * 1.4f);
                rt.anchoredPosition = Vector2.LerpUnclamped(home - new Vector2(0f, lift), home, k);
            }, Ease.OutQuint, delay, null, rt);
        }

        void Update()
        {
            if (m_Adding.Count > 0)
            {
                m_Tweens.AddRange(m_Adding);
                m_Adding.Clear();
            }

            // Clamp so a hitch (first frame, shader warm-up, window drag) pauses animations instead of skipping them.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            for (int i = m_Tweens.Count - 1; i >= 0; i--)
            {
                var t = m_Tweens[i];
                if (t.Killed || (t.HasOwner && t.Owner == null))
                {
                    m_Tweens.RemoveAt(i);
                    continue;
                }

                if (t.Delay > 0f)
                {
                    t.Delay -= dt;
                    if (t.Delay > 0f) continue;
                }

                t.Elapsed += dt;
                float k = Mathf.Clamp01(t.Elapsed / t.Duration);
                try
                {
                    t.Step?.Invoke(t.Curve(k));
                    if (k >= 1f)
                    {
                        t.Killed = true;
                        t.Done?.Invoke();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    t.Killed = true;
                }
            }
        }
    }
}
