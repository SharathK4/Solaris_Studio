using UnityEngine;

namespace Solaris.Vision
{
    /// <summary>MediaPipe hand landmark indices.</summary>
    public static class Lm
    {
        public const int Wrist = 0;
        public const int ThumbCmc = 1, ThumbMcp = 2, ThumbIp = 3, ThumbTip = 4;
        public const int IndexMcp = 5, IndexPip = 6, IndexDip = 7, IndexTip = 8;
        public const int MiddleMcp = 9, MiddlePip = 10, MiddleDip = 11, MiddleTip = 12;
        public const int RingMcp = 13, RingPip = 14, RingDip = 15, RingTip = 16;
        public const int PinkyMcp = 17, PinkyPip = 18, PinkyDip = 19, PinkyTip = 20;
        public const int Count = 21;

        public static readonly int[] Bones =
        {
            0, 1, 1, 2, 2, 3, 3, 4,
            0, 5, 5, 6, 6, 7, 7, 8,
            5, 9, 9, 10, 10, 11, 11, 12,
            9, 13, 13, 14, 14, 15, 15, 16,
            13, 17, 0, 17, 17, 18, 18, 19, 19, 20,
        };
    }

    /// <summary>Region of interest for the landmark model: rotated square in texture pixels (y-up).</summary>
    public struct Roi
    {
        public Vector2 Center;
        public float Size;
        public Vector2 Up;
    }

    /// <summary>One tracked hand slot. Updated by <see cref="VisionService"/>.</summary>
    public sealed class HandTrack
    {
        public readonly int Slot;
        public int Id;                      // changes each time the slot acquires a new hand
        public bool Tracking;               // has an ROI to refine next iteration
        public bool Visible;                // landmarks valid as of the last iteration
        public float Presence;
        public float RightHanded;           // model handedness (probability of a right hand, mirrored convention)
        public Roi Roi;
        public float LastSeen;

        /// <summary>Raw landmarks in texture pixels (y-up), z in pixels.</summary>
        public readonly Vector3[] Pixel = new Vector3[Lm.Count];

        /// <summary>
        /// Filtered landmarks in "metric view" space: mirrored as the user sees it, units of frame
        /// height (isotropic), x right, y up, z toward the camera negative.
        /// </summary>
        public readonly Vector3[] View = new Vector3[Lm.Count];

        readonly OneEuro[] m_Filters = new OneEuro[Lm.Count * 3];

        public HandTrack(int slot) => Slot = slot;

        public void Lose()
        {
            Tracking = false;
            Visible = false;
            Presence = 0f;
        }

        public void Acquire(Roi roi, int id)
        {
            Roi = roi;
            Id = id;
            Tracking = true;
            Visible = false;
            for (int i = 0; i < m_Filters.Length; i++) m_Filters[i].Reset();
        }

        /// <summary>Converts pixels to mirrored, isotropic view units and smooths them.</summary>
        public void UpdateView(int texW, int texH, bool mirror, float dt)
        {
            float inv = 1f / texH;
            // Hand-size-relative filtering: bigger (closer) hands move more pixels per cm.
            float scale = Mathf.Max(0.05f, (Pixel[Lm.Wrist] - Pixel[Lm.MiddleMcp]).magnitude * inv);
            float minCutoff = 1.4f;
            float beta = 6f / scale;
            for (int i = 0; i < Lm.Count; i++)
            {
                var p = Pixel[i];
                float x = mirror ? (texW - p.x) * inv : p.x * inv;
                float y = p.y * inv;
                float z = p.z * inv;
                View[i] = new Vector3(
                    m_Filters[i * 3 + 0].Filter(x, dt, minCutoff, beta),
                    m_Filters[i * 3 + 1].Filter(y, dt, minCutoff, beta),
                    m_Filters[i * 3 + 2].Filter(z, dt, minCutoff * 0.7f, beta));
            }
        }

        /// <summary>Normalised view UV (0..1 both axes) of a landmark, for overlays.</summary>
        public Vector2 Uv(int i, float frameAspect) => new Vector2(View[i].x / frameAspect, View[i].y);

        /// <summary>
        /// Next-frame ROI from the current landmarks (MediaPipe hand_landmark_landmarks_to_roi):
        /// rotation wrist -> middle knuckles, rotated bbox of the palm+finger bases, x2 and shifted to the fingers.
        /// </summary>
        public Roi RoiFromLandmarks()
        {
            Vector2 P(int i) => new Vector2(Pixel[i].x, Pixel[i].y);
            var wrist = P(Lm.Wrist);
            var mid = ((P(Lm.IndexMcp) + P(Lm.RingMcp)) * 0.5f + P(Lm.MiddleMcp)) * 0.5f;
            var up = (mid - wrist).normalized;
            if (up.sqrMagnitude < 1e-6f) up = Vector2.up;
            var right = new Vector2(up.y, -up.x);

            float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue;
            foreach (int i in k_RoiSubset)
            {
                var d = P(i) - wrist;
                float r = Vector2.Dot(d, right), u = Vector2.Dot(d, up);
                minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
                minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
            }

            float w = maxR - minR, h = maxU - minU;
            var center = wrist + right * ((minR + maxR) * 0.5f) + up * ((minU + maxU) * 0.5f);
            center += up * (0.1f * h);
            return new Roi { Center = center, Size = Mathf.Max(w, h) * 2.0f, Up = up };
        }

        static readonly int[] k_RoiSubset = { 0, 1, 2, 3, 5, 6, 9, 10, 13, 14, 17, 18 };
    }

    public struct FaceInfo
    {
        public bool Visible;
        public float Score;
        /// <summary>Face box centre in mirrored view UV (0..1).</summary>
        public Vector2 Center;
        /// <summary>Face box size in view UV.</summary>
        public Vector2 Size;
        public float LastSeen;
    }
}
