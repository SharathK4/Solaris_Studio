using System;
using System.Globalization;
using Unity.InferenceEngine;
using UnityEngine;

namespace Solaris.Vision
{
    /// <summary>2x3 affine transform: p' = M * (x, y, 1).</summary>
    public struct Affine
    {
        public float A, B, C;   // row 0: x' = A x + B y + C
        public float D, E, F;   // row 1: y' = D x + E y + F

        public Vector2 Apply(Vector2 p) => new Vector2(A * p.x + B * p.y + C, D * p.x + E * p.y + F);
        public Vector2 ApplyVector(Vector2 v) => new Vector2(A * v.x + B * v.y, D * v.x + E * v.y);
        public float Scale => Mathf.Sqrt(Mathf.Abs(A * E - B * D));

        public Matrix4x4 ToMatrix() => new Matrix4x4(
            new Vector4(A, D, 0, 0),
            new Vector4(B, E, 0, 0),
            new Vector4(C, F, 1, 0),
            new Vector4(0, 0, 0, 1));

        /// <summary>
        /// Letterboxed square crop of the whole frame: tensor (0,0) is the top-left of a square of side
        /// max(W,H) centred on the frame. Texture space is y-up.
        /// </summary>
        public static Affine Letterbox(int texW, int texH, int tensorSize)
        {
            float size = Mathf.Max(texW, texH);
            float s = size / tensorSize;
            return new Affine
            {
                A = s, B = 0, C = 0.5f * (texW - size),
                D = 0, E = -s, F = 0.5f * (texH + size),
            };
        }

        /// <summary>Stretch the whole frame into a W x H tensor (no rotation).</summary>
        public static Affine Stretch(int texW, int texH, int tensorW, int tensorH) => new Affine
        {
            A = (float)texW / tensorW, B = 0, C = 0,
            D = 0, E = -(float)texH / tensorH, F = texH,
        };

        /// <summary>
        /// Rotated square crop: centre and side in texture pixels, <paramref name="up"/> is the unit
        /// direction (texture space, y-up) that should appear as "up" in the tensor.
        /// </summary>
        public static Affine RotatedCrop(Vector2 center, float side, Vector2 up, int tensorSize)
        {
            float k = side / tensorSize;
            var right = new Vector2(up.y, -up.x);
            float h = tensorSize * 0.5f;
            return new Affine
            {
                A = right.x * k, B = -up.x * k, C = center.x - h * k * (right.x - up.x),
                D = right.y * k, E = -up.y * k, F = center.y - h * k * (right.y - up.y),
            };
        }
    }

    public static class VisionMath
    {
        static ComputeShader s_Sampler;
        static int s_Kernel;
        static readonly int k_Optr = Shader.PropertyToID("Optr");
        static readonly int k_Tex = Shader.PropertyToID("X_tex2D");
        static readonly int k_OH = Shader.PropertyToID("O_height");
        static readonly int k_OW = Shader.PropertyToID("O_width");
        static readonly int k_XH = Shader.PropertyToID("X_height");
        static readonly int k_XW = Shader.PropertyToID("X_width");
        static readonly int k_M = Shader.PropertyToID("affineMatrix");
        static readonly int k_Scale = Shader.PropertyToID("OutScale");
        static readonly int k_Bias = Shader.PropertyToID("OutBias");
        static readonly int k_Srgb = Shader.PropertyToID("ToSrgb");
        static readonly int k_Planar = Shader.PropertyToID("Planar");

        /// <summary>Samples <paramref name="src"/> through <paramref name="m"/> into a (1,H,W,3) NHWC or (1,3,H,W) NCHW tensor.</summary>
        public static void Sample(Texture src, Tensor<float> dst, Affine m, float scale = 1f, float bias = 0f, bool planar = false)
        {
            if (s_Sampler == null)
            {
                s_Sampler = SolarisAssets.Get.imageSampler;
                s_Kernel = s_Sampler.FindKernel("Sample");
            }

            int h = planar ? dst.shape[2] : dst.shape[1];
            int w = planar ? dst.shape[3] : dst.shape[2];
            var data = ComputeTensorData.Pin(dst, false);
            s_Sampler.SetTexture(s_Kernel, k_Tex, src);
            s_Sampler.SetBuffer(s_Kernel, k_Optr, data.buffer);
            s_Sampler.SetInt(k_OH, h);
            s_Sampler.SetInt(k_OW, w);
            s_Sampler.SetInt(k_XH, src.height);
            s_Sampler.SetInt(k_XW, src.width);
            s_Sampler.SetMatrix(k_M, m.ToMatrix());
            s_Sampler.SetFloat(k_Scale, scale);
            s_Sampler.SetFloat(k_Bias, bias);
            s_Sampler.SetInt(k_Planar, planar ? 1 : 0);
            // In a linear project, sampling an sRGB texture returns linear values: re-encode for the model.
            bool srgbTexture = src is RenderTexture rt ? rt.sRGB : !(src is Texture2D t2 && !t2.isDataSRGB);
            s_Sampler.SetInt(k_Srgb, QualitySettings.activeColorSpace == ColorSpace.Linear && srgbTexture ? 1 : 0);
            s_Sampler.Dispatch(s_Kernel, (w + 7) / 8, (h + 7) / 8, 1);
        }

        public static float[,] LoadAnchors(string csv, int count)
        {
            var anchors = new float[count, 4];
            var lines = csv.Split('\n');
            for (int i = 0; i < count; i++)
            {
                var v = lines[i].Split(',');
                for (int j = 0; j < 4; j++) anchors[i, j] = float.Parse(v[j], CultureInfo.InvariantCulture);
            }
            return anchors;
        }

        public static float Sigmoid(float x) => 1f / (1f + Mathf.Exp(-x));

        public static float IoU(Rect a, Rect b)
        {
            float x0 = Math.Max(a.xMin, b.xMin), y0 = Math.Max(a.yMin, b.yMin);
            float x1 = Math.Min(a.xMax, b.xMax), y1 = Math.Min(a.yMax, b.yMax);
            float inter = Math.Max(0, x1 - x0) * Math.Max(0, y1 - y0);
            float union = a.width * a.height + b.width * b.height - inter;
            return union <= 0 ? 0 : inter / union;
        }
    }

    /// <summary>
    /// One Euro filter (Casiez et al. 2012): heavy smoothing when still, low lag when moving.
    /// </summary>
    public struct OneEuro
    {
        float m_X, m_Dx;
        bool m_Init;

        public float Filter(float x, float dt, float minCutoff, float beta, float dCutoff = 1f)
        {
            if (!m_Init || dt <= 0f)
            {
                m_Init = true;
                m_X = x;
                m_Dx = 0f;
                return x;
            }
            float dx = (x - m_X) / dt;
            m_Dx = Mathf.Lerp(m_Dx, dx, Alpha(dCutoff, dt));
            float cutoff = minCutoff + beta * Mathf.Abs(m_Dx);
            m_X = Mathf.Lerp(m_X, x, Alpha(cutoff, dt));
            return m_X;
        }

        public void Reset() => m_Init = false;

        static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * cutoff);
            return 1f / (1f + tau / dt);
        }
    }
}
