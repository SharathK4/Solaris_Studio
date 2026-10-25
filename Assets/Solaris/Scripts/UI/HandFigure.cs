using UnityEngine;
using UnityEngine.UI;

namespace Solaris.UI
{
    /// <summary>High-level hand shape: curl per finger (0 straight .. 1 closed), thumb fold, splay.</summary>
    [System.Serializable]
    public struct HandShape
    {
        public float Thumb, Index, Middle, Ring, Pinky;
        public float ThumbFold;   // 0 = thumb out, 1 = folded across the palm
        public float Spread;      // 0 = fingers together, 1 = natural splay

        public float Curl(int f) => f switch { 0 => Thumb, 1 => Index, 2 => Middle, 3 => Ring, _ => Pinky };

        public static HandShape Lerp(HandShape a, HandShape b, float t) => new HandShape
        {
            Thumb = Mathf.Lerp(a.Thumb, b.Thumb, t),
            Index = Mathf.Lerp(a.Index, b.Index, t),
            Middle = Mathf.Lerp(a.Middle, b.Middle, t),
            Ring = Mathf.Lerp(a.Ring, b.Ring, t),
            Pinky = Mathf.Lerp(a.Pinky, b.Pinky, t),
            ThumbFold = Mathf.Lerp(a.ThumbFold, b.ThumbFold, t),
            Spread = Mathf.Lerp(a.Spread, b.Spread, t),
        };

        /// <summary>The chord gestures: 1 = index, 2 = V, 3 = three fingers, 4 = four (thumb in), 5 = open palm; 0 = fist.</summary>
        public static HandShape Fingers(int n) => n switch
        {
            1 => new HandShape { Index = 0, Middle = 1, Ring = 1, Pinky = 1, Thumb = 0.8f, ThumbFold = 0.85f, Spread = 0.4f },
            2 => new HandShape { Index = 0, Middle = 0, Ring = 1, Pinky = 1, Thumb = 0.8f, ThumbFold = 0.85f, Spread = 0.9f },
            3 => new HandShape { Index = 0, Middle = 0, Ring = 0, Pinky = 1, Thumb = 0.75f, ThumbFold = 0.9f, Spread = 0.75f },
            4 => new HandShape { Index = 0, Middle = 0, Ring = 0, Pinky = 0, Thumb = 0.7f, ThumbFold = 1f, Spread = 0.75f },
            5 => new HandShape { Index = 0, Middle = 0, Ring = 0, Pinky = 0, Thumb = 0, ThumbFold = 0, Spread = 1f },
            _ => new HandShape { Index = 1, Middle = 1, Ring = 1, Pinky = 1, Thumb = 0.8f, ThumbFold = 0.9f, Spread = 0.2f },
        };

        public static HandShape Relaxed => new HandShape { Index = 0.12f, Middle = 0.16f, Ring = 0.2f, Pinky = 0.24f, Thumb = 0.1f, ThumbFold = 0.15f, Spread = 0.7f };
        public static HandShape Curled => new HandShape { Index = 0.72f, Middle = 0.78f, Ring = 0.82f, Pinky = 0.86f, Thumb = 0.5f, ThumbFold = 0.55f, Spread = 0.25f };
    }

    /// <summary>Forward kinematics for a right hand seen palm-on, 21 joints in MediaPipe order.</summary>
    public static class HandRig
    {
        static readonly Vector3[] k_Mcp =
        {
            new Vector3(-0.30f, 0.90f, 0f), new Vector3(-0.06f, 0.98f, 0f),
            new Vector3(0.17f, 0.93f, 0f), new Vector3(0.37f, 0.80f, 0f),
        };
        static readonly float[,] k_Len = { { 0.46f, 0.27f, 0.21f }, { 0.50f, 0.31f, 0.22f }, { 0.47f, 0.29f, 0.21f }, { 0.37f, 0.22f, 0.19f } };
        static readonly float[] k_Splay = { 9f, 1f, -7f, -16f };
        static readonly float[,] k_Radius =
        {
            { 0.19f, 0.155f, 0.13f, 0.112f, 0.098f },      // wrist, thumb cmc/mcp/ip/tip
            { 0.118f, 0.102f, 0.089f, 0.079f, 0 },         // index mcp pip dip tip
            { 0.122f, 0.106f, 0.092f, 0.081f, 0 },
            { 0.114f, 0.099f, 0.087f, 0.077f, 0 },
            { 0.101f, 0.087f, 0.077f, 0.069f, 0 },
        };

        /// <summary>Joints in hand space (wrist at origin, fingers +y, palm facing +z) with radius in w.</summary>
        public static void Solve(in HandShape s, Vector4[] joints)
        {
            joints[0] = new Vector4(0, 0, 0, k_Radius[0, 0]);
            var palmN = Vector3.forward;

            for (int f = 0; f < 4; f++)
            {
                int b = 5 + f * 4;
                var mcp = k_Mcp[f];
                joints[b] = Pack(mcp, k_Radius[f + 1, 0]);
                float splay = k_Splay[f] * Mathf.Lerp(0.25f, 1.2f, s.Spread) * Mathf.Deg2Rad;
                var d = new Vector3(-Mathf.Sin(splay), Mathf.Cos(splay), 0f);
                var n = palmN;
                float c = Mathf.Clamp01(s.Curl(f + 1));
                float[] flex = { c * 82f, c * 104f, c * 66f };
                var p = mcp;
                for (int k = 0; k < 3; k++)
                {
                    Bend(ref d, ref n, flex[k]);
                    p += d * k_Len[f, k];
                    joints[b + k + 1] = Pack(p, k_Radius[f + 1, k + 1]);
                }
            }

            // Thumb: swings from out-to-the-side to across the palm, then curls.
            var cmc = new Vector3(-0.21f, 0.22f, 0.06f);
            joints[1] = Pack(cmc, k_Radius[0, 1]);
            var open = new Vector3(-0.72f, 0.62f, 0.3f).normalized;
            var folded = new Vector3(0.7f, 0.55f, 0.3f).normalized;
            var td = Vector3.Slerp(open, folded, Mathf.Clamp01(s.ThumbFold));
            // The thumb flexes toward the centre of the palm (opposition), not out of it.
            var palmCentre = new Vector3(0.08f, 0.62f, 0.18f);
            float tc = Mathf.Clamp01(s.Thumb);
            float[] tlen = { 0.38f, 0.30f, 0.25f };
            float[] tflex = { tc * 25f, tc * 50f, tc * 65f };
            var tp = cmc;
            for (int k = 0; k < 3; k++)
            {
                var to = palmCentre - tp;
                var tn = to - Vector3.Dot(to, td) * td;
                tn = tn.sqrMagnitude > 1e-6f ? tn.normalized : palmN;
                Bend(ref td, ref tn, tflex[k]);
                tp += td * tlen[k];
                joints[2 + k] = Pack(tp, k_Radius[0, 2 + k]);
            }
        }

        static void Bend(ref Vector3 d, ref Vector3 n, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            var nd = d * c + n * s;
            var nn = n * c - d * s;
            d = nd.normalized;
            n = nn.normalized;
        }

        static Vector4 Pack(Vector3 p, float r) => new Vector4(p.x, p.y, p.z, r);
    }

    /// <summary>
    /// A sculpted 3D hand rendered in a UI rect. Set <see cref="Target"/> and it glides there;
    /// Euler/Offset let screens animate gestures (raise, tilt, push forward).
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class HandFigure : MonoBehaviour
    {
        public HandShape Target = HandShape.Relaxed;
        public Vector3 Euler;            // degrees: pitch (x), yaw (y), roll (z)
        public Vector2 Offset;           // view units
        public float Scale = 1f;
        public float Idle = 1f;
        public float Glow;
        public float Speed = 9f;
        public bool Mirror;              // true = left hand

        HandShape m_Current = HandShape.Relaxed;
        readonly Vector4[] m_Joints = new Vector4[21];
        readonly Vector4[] m_View = new Vector4[21];
        Material m_Mat;
        RawImage m_Img;
        float m_Phase;
        static readonly int k_J = Shader.PropertyToID("_J");
        static readonly int k_Bounds = Shader.PropertyToID("_Bounds");
        static readonly int k_Aspect = Shader.PropertyToID("_Aspect");
        static readonly int k_Glow = Shader.PropertyToID("_Glow");

        public Material Material => m_Mat;

        public static HandFigure Create(Transform parent, string name = "Hand")
        {
            var img = UIKit.Raw(parent, null, null, name);
            var fig = img.gameObject.AddComponent<HandFigure>();
            return fig;
        }

        void Awake()
        {
            m_Img = GetComponent<RawImage>();
            m_Mat = SolarisAssets.Mat("Solaris/UI/Hand");
            m_Img.material = m_Mat;
            m_Img.raycastTarget = false;
            m_Phase = Random.value * 10f;
        }

        public void Snap()
        {
            m_Current = Target;
            Apply();
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            m_Current = HandShape.Lerp(m_Current, Target, 1f - Mathf.Exp(-Speed * dt));
            m_Phase += dt;
            Apply();
        }

        void Apply()
        {
            HandRig.Solve(m_Current, m_Joints);

            // Idle life: slow sway and a breath in the fingers.
            float sway = Mathf.Sin(m_Phase * 0.7f) * 3f * Idle;
            float yaw = Mathf.Sin(m_Phase * 0.43f + 1f) * 6f * Idle;
            var rot = Quaternion.Euler(Euler.x, Euler.y + yaw, Euler.z + sway);
            var pivot = new Vector3(0f, 0.9f, 0f);
            float mirror = Mirror ? -1f : 1f;
            var shift = new Vector3(Offset.x, Offset.y - 0.12f + Mathf.Sin(m_Phase * 0.9f) * 0.02f * Idle, 0f);

            Vector3 center = Vector3.zero;
            for (int i = 0; i < 21; i++)
            {
                var p = (Vector3)m_Joints[i] - pivot;
                p.x *= mirror;
                p = rot * p * Scale + shift;
                m_View[i] = new Vector4(p.x, p.y, p.z, m_Joints[i].w * Scale);
                center += p;
            }
            center /= 21f;
            float radius = 0f;
            for (int i = 0; i < 21; i++)
                radius = Mathf.Max(radius, Vector3.Distance(center, m_View[i]) + m_View[i].w);
            var down = ((Vector3)m_View[0] - (Vector3)m_View[9]).normalized;
            radius = Mathf.Max(radius, Vector3.Distance(center, (Vector3)m_View[0] + down * 1.6f) + 0.25f);

            var rect = ((RectTransform)transform).rect;
            m_Mat.SetVectorArray(k_J, m_View);
            m_Mat.SetVector(k_Bounds, new Vector4(center.x, center.y, center.z, radius));
            m_Mat.SetFloat(k_Aspect, rect.height > 0 ? rect.width / rect.height : 1f);
            m_Mat.SetFloat(k_Glow, Glow);
        }

        void OnDestroy()
        {
            if (m_Mat != null) Destroy(m_Mat);
        }
    }
}
