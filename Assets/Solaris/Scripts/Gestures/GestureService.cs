using System;
using Solaris.Vision;
using UnityEngine;

namespace Solaris.Gestures
{
    /// <summary>Per-slot analysed hand.</summary>
    public sealed class HandInfo
    {
        public HandTrack Track;
        public readonly HandPose Pose = new HandPose();
        public bool Visible => Track != null && Track.Visible;
        public int Slot => Track.Slot;

        /// <summary>Normalised mirrored frame UV of a landmark (what the user sees).</summary>
        public Vector2 Uv(int landmark, float frameAspect) => Track.Uv(landmark, frameAspect);
    }

    /// <summary>
    /// Turns tracked hands into musical intent:
    ///  - roles: the hand on the user's left is the CHORD hand (fingers 1..5 = C D Em G Am),
    ///    the other is the EXPRESSION hand (height = volume, tilt = tone, open/curl = major/minor,
    ///    horizontal position = direction). One hand alone is the chord hand and also drives volume.
    ///  - a debounced, majority-voted finger count so chords never flicker;
    ///  - a pointer (index fingertip) with pinch and circle gestures for navigating the UI.
    /// </summary>
    public sealed class GestureService : MonoBehaviour
    {
        const float VoteWindow = 0.14f;

        public readonly HandInfo[] Hands = { new HandInfo(), new HandInfo() };
        public HandInfo ChordHand { get; private set; }
        public HandInfo ExpressionHand { get; private set; }
        public HandInfo PointerHand { get; private set; }

        /// <summary>Swap which side plays chords (for left-handed players).</summary>
        public bool ChordOnRight;

        /// <summary>Stable finger count of the chord hand: -1 no hand, 0 fist, 1..5.</summary>
        public int Fingers { get; private set; } = -1;
        public event Action<int> FingersChanged;

        public float Volume { get; private set; } = 0.85f;
        public float Tone { get; private set; } = 0.5f;
        public float Pan { get; private set; }
        public float Depth { get; private set; }
        public bool Minor { get; private set; }
        public bool ExpressionActive => ExpressionHand != null;

        public readonly CircleGesture Circle = new CircleGesture();
        /// <summary>Clockwise turning accumulated since the last frame (radians).</summary>
        public float CircleDelta { get; private set; }
        public bool PointerVisible => PointerHand != null;
        /// <summary>Index fingertip in mirrored frame UV.</summary>
        public Vector2 PointerUv { get; private set; } = new Vector2(0.5f, 0.5f);
        public bool Pinching { get; private set; }
        public event Action PinchStarted;

        public int LastUpdateFrame { get; private set; }

        VisionService m_Vision;
        CameraFeed m_Feed;
        readonly int[] m_VoteCounts = new int[64];
        readonly float[] m_VoteTimes = new float[64];
        int m_VoteHead, m_VoteLen;
        float m_LastT;
        int[] m_TrackIds = { -1, -1 };

        public void Bind(VisionService vision, CameraFeed feed)
        {
            m_Vision = vision;
            m_Feed = feed;
            for (int i = 0; i < 2; i++) Hands[i].Track = vision.Hands[i];
            vision.Updated += OnVision;
        }

        void OnVision()
        {
            float now = Time.realtimeSinceStartup;
            float dt = m_LastT > 0 ? Mathf.Clamp(now - m_LastT, 1e-3f, 0.25f) : 1f / 30f;
            m_LastT = now;
            LastUpdateFrame = Time.frameCount;

            for (int i = 0; i < 2; i++)
            {
                var h = Hands[i];
                if (!h.Visible)
                {
                    h.Pose.Reset();
                    m_TrackIds[i] = -1;
                    continue;
                }
                if (m_TrackIds[i] != h.Track.Id)
                {
                    h.Pose.Reset();       // new hand in this slot: forget hysteresis state
                    m_TrackIds[i] = h.Track.Id;
                }
                h.Pose.Analyze(h.Track.View);
            }

            AssignRoles();
            UpdateChordVote(now);
            UpdateExpression(dt);
            UpdatePointer(dt);
        }

        void AssignRoles()
        {
            HandInfo a = Hands[0].Visible ? Hands[0] : null;
            HandInfo b = Hands[1].Visible ? Hands[1] : null;
            if (a != null && b != null)
            {
                bool aLeft = a.Track.View[Lm.Wrist].x < b.Track.View[Lm.Wrist].x;
                var left = aLeft ? a : b;
                var right = aLeft ? b : a;
                ChordHand = ChordOnRight ? right : left;
                ExpressionHand = ChordOnRight ? left : right;
            }
            else
            {
                ChordHand = a ?? b;
                ExpressionHand = null;
            }
        }

        void UpdateChordVote(float now)
        {
            int raw = ChordHand != null ? ChordHand.Pose.Count : -1;

            // Push into the vote window.
            m_VoteCounts[m_VoteHead] = raw;
            m_VoteTimes[m_VoteHead] = now;
            m_VoteHead = (m_VoteHead + 1) % m_VoteCounts.Length;
            m_VoteLen = Mathf.Min(m_VoteLen + 1, m_VoteCounts.Length);

            Span<int> tally = stackalloc int[7];
            int samples = 0;
            for (int i = 0; i < m_VoteLen; i++)
            {
                int idx = (m_VoteHead - 1 - i + m_VoteCounts.Length) % m_VoteCounts.Length;
                if (now - m_VoteTimes[idx] > VoteWindow && i >= 2) break;
                tally[m_VoteCounts[idx] + 1]++;
                samples++;
            }

            int best = -1, bestVotes = 0;
            for (int c = 0; c < tally.Length; c++)
                if (tally[c] > bestVotes) { bestVotes = tally[c]; best = c - 1; }

            // Losing the hand is reported quickly; changing chords needs a clear majority.
            bool decisive = samples >= 2 && bestVotes >= Mathf.CeilToInt(samples * 0.66f);
            if (raw == -1 && Fingers != -1 && samples >= 1 && tally[0] == samples) decisive = true;
            if (decisive && best != Fingers)
            {
                Fingers = best;
                FingersChanged?.Invoke(Fingers);
            }
        }

        void UpdateExpression(float dt)
        {
            float k = 1f - Mathf.Exp(-dt / 0.08f);
            var driver = ExpressionHand ?? ChordHand;
            if (driver != null)
            {
                float height = driver.Pose.Palm.y;                        // 0 = bottom .. 1 = top of frame
                Volume = Mathf.Lerp(Volume, Mathf.Lerp(0.18f, 1f, Mathf.InverseLerp(0.22f, 0.82f, height)), k);
            }

            if (ExpressionHand != null)
            {
                var pose = ExpressionHand.Pose;
                float roll = Mathf.Clamp(pose.Roll * Mathf.Rad2Deg, -55f, 55f);
                Tone = Mathf.Lerp(Tone, 0.5f + roll / 110f, k);
                float x = ExpressionHand.Track.View[Lm.Wrist].x / Mathf.Max(0.1f, m_Feed.Aspect);
                Pan = Mathf.Lerp(Pan, Mathf.Clamp((x - 0.5f) * 2.4f, -1f, 1f), k);
                Depth = Mathf.Lerp(Depth, Mathf.Clamp01(-pose.Pitch), k);
                Minor = Minor ? pose.Openness < 0.52f : pose.Openness < 0.36f;
            }
            else
            {
                Tone = Mathf.Lerp(Tone, 0.5f, k * 0.5f);
                Pan = Mathf.Lerp(Pan, 0f, k * 0.5f);
                Depth = Mathf.Lerp(Depth, 0f, k * 0.5f);
                Minor = false;
            }
        }

        void UpdatePointer(float dt)
        {
            // Keep the current pointer hand while it's visible; otherwise prefer a pointing index finger.
            if (PointerHand == null || !PointerHand.Visible)
            {
                PointerHand = null;
                foreach (var h in Hands)
                {
                    if (!h.Visible) continue;
                    if (PointerHand == null || (h.Pose.Extended[HandPose.Index] && !PointerHand.Pose.Extended[HandPose.Index]) ||
                        h.Pose.Palm.y > PointerHand.Pose.Palm.y + 0.08f)
                        PointerHand = h;
                }
                Circle.Reset();
            }

            if (PointerHand == null)
            {
                CircleDelta = 0f;
                Pinching = false;
                return;
            }

            var tip = PointerHand.Track.View[Lm.IndexTip];
            PointerUv = PointerHand.Uv(Lm.IndexTip, m_Feed.Aspect);
            CircleDelta = PointerHand.Pose.Extended[HandPose.Index] && !PointerHand.Pose.Pinching
                ? Circle.Update(new Vector2(tip.x, tip.y), dt)
                : 0f;
            if (!PointerHand.Pose.Extended[HandPose.Index]) Circle.Reset();

            bool was = Pinching;
            Pinching = PointerHand.Pose.Pinching;
            if (Pinching && !was) PinchStarted?.Invoke();
        }

        /// <summary>Consumes the circle turning gathered since the last call (for per-frame UI).</summary>
        public float TakeCircleDelta()
        {
            float d = CircleDelta;
            CircleDelta = 0f;
            return d;
        }
    }
}
