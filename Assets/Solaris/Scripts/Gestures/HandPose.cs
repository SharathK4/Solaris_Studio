using Solaris.Vision;
using UnityEngine;

namespace Solaris.Gestures
{
    /// <summary>
    /// Geometric read-out of one hand from its 21 landmarks (view space: x right, y up, z toward
    /// camera negative, isotropic units). Pure C#, so it is unit-tested with synthetic skeletons.
    /// </summary>
    public sealed class HandPose
    {
        public const int Thumb = 0, Index = 1, Middle = 2, Ring = 3, Pinky = 4;

        /// <summary>Continuous extension evidence per finger, 0 = curled, 1 = straight out.</summary>
        public readonly float[] Extension = new float[5];
        /// <summary>Extension with hysteresis (what the game treats as "up").</summary>
        public readonly bool[] Extended = new bool[5];

        /// <summary>
        /// Chord gesture count. People leave the thumb loose when showing 1-3, so only index..pinky
        /// are counted; the thumb matters only to tell four fingers (G) from an open palm (Am).
        /// </summary>
        public int Count { get; private set; }
        /// <summary>Every extended digit including the thumb.</summary>
        public int RawCount { get; private set; }
        public float Openness { get; private set; }
        /// <summary>Tilt of the hand in the image plane, radians; + = leaning to the user's right.</summary>
        public float Roll { get; private set; }
        /// <summary>Fingers toward (+) or away from (-) the camera, roughly -1..1.</summary>
        public float Pitch { get; private set; }
        public Vector2 Palm { get; private set; }
        public float Scale { get; private set; }
        public float PinchDistance { get; private set; }
        public bool Pinching { get; private set; }

        const float On = 0.62f, Off = 0.40f;
        const float PinchOn = 0.30f, PinchOff = 0.45f;

        static readonly int[,] k_Chain =
        {
            { Lm.ThumbCmc, Lm.ThumbMcp, Lm.ThumbIp, Lm.ThumbTip },
            { Lm.IndexMcp, Lm.IndexPip, Lm.IndexDip, Lm.IndexTip },
            { Lm.MiddleMcp, Lm.MiddlePip, Lm.MiddleDip, Lm.MiddleTip },
            { Lm.RingMcp, Lm.RingPip, Lm.RingDip, Lm.RingTip },
            { Lm.PinkyMcp, Lm.PinkyPip, Lm.PinkyDip, Lm.PinkyTip },
        };

        public void Reset()
        {
            for (int i = 0; i < 5; i++)
            {
                Extension[i] = 0f;
                Extended[i] = false;
            }
            Count = 0;
            Pinching = false;
        }

        public void Analyze(Vector3[] p)
        {
            Vector3 wrist = p[Lm.Wrist];
            Scale = Mathf.Max(1e-4f, Vector3.Distance(wrist, p[Lm.MiddleMcp]));
            Palm = (Vector2)(wrist + p[Lm.IndexMcp] + p[Lm.MiddleMcp] + p[Lm.RingMcp] + p[Lm.PinkyMcp]) / 5f;

            // Four fingers: straightness of the PIP/DIP joints + how far the tip reaches from the wrist.
            for (int f = Index; f <= Pinky; f++)
            {
                Vector3 mcp = p[k_Chain[f, 0]], pip = p[k_Chain[f, 1]], dip = p[k_Chain[f, 2]], tip = p[k_Chain[f, 3]];
                float bend = (Angle(mcp, pip, dip) + Angle(pip, dip, tip)) * 0.5f;       // degrees of bend
                float reach = Vector3.Distance(wrist, tip) / Mathf.Max(1e-4f, Vector3.Distance(wrist, pip));
                float straight = Remap(bend, 75f, 22f);
                float far = Remap(reach, 1.0f, 1.32f);
                Extension[f] = 0.45f * straight + 0.55f * far;
            }

            // Thumb: an open thumb reaches ~0.8-1.0 palm lengths from the palm centre; folded across
            // the palm or over curled fingers it stays within ~0.5. Straightness only tips close calls.
            {
                Vector3 mcp = p[Lm.ThumbMcp], ip = p[Lm.ThumbIp], tip = p[Lm.ThumbTip];
                Vector3 palmCentre = (wrist + p[Lm.IndexMcp] + p[Lm.MiddleMcp] + p[Lm.RingMcp] + p[Lm.PinkyMcp]) / 5f;
                float reach = Vector3.Distance(tip, palmCentre) / Scale;
                float straight = Remap(Angle(mcp, ip, tip), 55f, 15f);
                Extension[Thumb] = 0.72f * Remap(reach, 0.56f, 0.82f) + 0.28f * straight;
            }

            int fingers = 0;
            for (int f = 0; f < 5; f++)
            {
                Extended[f] = Extended[f] ? Extension[f] > Off : Extension[f] > On;
                if (f > Thumb && Extended[f]) fingers++;
            }
            RawCount = fingers + (Extended[Thumb] ? 1 : 0);
            Count = fingers == 4 && Extended[Thumb] ? 5 : fingers;

            float open = 0f;
            for (int f = Index; f <= Pinky; f++)
                open += Remap(Vector3.Distance(wrist, p[k_Chain[f, 3]]) / Scale, 0.95f, 1.9f);
            Openness = Mathf.Clamp01(open / 4f * 0.85f + Extension[Thumb] * 0.15f);

            Vector3 axis = p[Lm.MiddleMcp] - wrist;
            Roll = Mathf.Atan2(axis.x, axis.y);
            Pitch = Mathf.Clamp((wrist.z - p[Lm.MiddleTip].z) / (Scale * 1.6f), -1f, 1f);

            PinchDistance = Vector3.Distance(p[Lm.ThumbTip], p[Lm.IndexTip]) / Scale;
            Pinching = Pinching ? PinchDistance < PinchOff : PinchDistance < PinchOn;
        }

        /// <summary>Bend at joint b in degrees (0 = straight line a-b-c).</summary>
        static float Angle(Vector3 a, Vector3 b, Vector3 c)
        {
            var u = b - a;
            var v = c - b;
            if (u.sqrMagnitude < 1e-10f || v.sqrMagnitude < 1e-10f) return 0f;
            return Vector3.Angle(u, v);
        }

        /// <summary>Linear map of x from [from..to] to [0..1], clamped (to may be below from).</summary>
        static float Remap(float x, float from, float to) => Mathf.Clamp01((x - from) / (to - from));
    }

    /// <summary>
    /// Direction-agnostic circle detector: accumulates the signed turning angle of the fingertip's
    /// velocity. Straight strokes add nothing, loops add +-2pi per turn, jitter is speed-gated.
    /// Positive = clockwise as the user sees it.
    /// </summary>
    public sealed class CircleGesture
    {
        public float MinSpeed = 0.18f;   // view units (frame heights) per second
        public float Total { get; private set; }
        public float Speed { get; private set; }

        Vector2 m_Prev, m_Vel;
        bool m_HasPrev, m_HasVel;

        public void Reset()
        {
            m_HasPrev = m_HasVel = false;
            Speed = 0f;
        }

        /// <summary>Feed a fingertip position; returns the clockwise turning (radians) since the last call.</summary>
        public float Update(Vector2 p, float dt)
        {
            if (!m_HasPrev || dt <= 0f)
            {
                m_Prev = p;
                m_HasPrev = true;
                return 0f;
            }

            var v = (p - m_Prev) / dt;
            m_Prev = p;
            float k = 1f - Mathf.Exp(-dt / 0.05f);
            var vel = m_HasVel ? Vector2.Lerp(m_Vel, v, k) : v;
            Speed = vel.magnitude;

            float turn = 0f;
            if (m_HasVel && Speed > MinSpeed && m_Vel.magnitude > MinSpeed)
            {
                float cross = m_Vel.x * vel.y - m_Vel.y * vel.x;
                float dot = Vector2.Dot(m_Vel, vel);
                turn = -Mathf.Atan2(cross, dot);                 // y-up: CCW is positive cross
                if (Mathf.Abs(turn) > 1.2f) turn = 0f;           // reversal / glitch, not a curve
            }

            m_Vel = vel;
            m_HasVel = true;
            Total += turn;
            return turn;
        }
    }
}
