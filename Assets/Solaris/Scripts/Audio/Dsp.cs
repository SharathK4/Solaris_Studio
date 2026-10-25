using System;

namespace Solaris.Audio
{
    /// <summary>Xorshift noise; allocation-free and safe on the audio thread.</summary>
    public struct Rng
    {
        uint m_S;
        public Rng(uint seed) => m_S = seed == 0 ? 0x9E3779B9u : seed;

        public float Next()   // -1..1
        {
            m_S ^= m_S << 13;
            m_S ^= m_S >> 17;
            m_S ^= m_S << 5;
            return (m_S & 0xFFFFFF) / 8388608f - 1f;
        }
    }

    /// <summary>RBJ biquad (direct form I).</summary>
    public struct Biquad
    {
        float b0, b1, b2, a1, a2, x1, x2, y1, y2;

        public void LowPass(float sr, float f, float q = 0.707f) => Set(sr, f, q, 0);
        public void HighPass(float sr, float f, float q = 0.707f) => Set(sr, f, q, 1);
        public void Peak(float sr, float f, float q, float gainDb) => Set(sr, f, q, 2, gainDb);

        void Set(float sr, float f, float q, int type, float gainDb = 0)
        {
            f = Math.Clamp(f, 10f, sr * 0.45f);
            double w = 2 * Math.PI * f / sr, cs = Math.Cos(w), sn = Math.Sin(w), alpha = sn / (2 * q);
            double B0, B1, B2, A0, A1, A2;
            switch (type)
            {
                case 0:
                    B0 = (1 - cs) / 2; B1 = 1 - cs; B2 = (1 - cs) / 2; A0 = 1 + alpha; A1 = -2 * cs; A2 = 1 - alpha;
                    break;
                case 1:
                    B0 = (1 + cs) / 2; B1 = -(1 + cs); B2 = (1 + cs) / 2; A0 = 1 + alpha; A1 = -2 * cs; A2 = 1 - alpha;
                    break;
                default:
                    double A = Math.Pow(10, gainDb / 40);
                    B0 = 1 + alpha * A; B1 = -2 * cs; B2 = 1 - alpha * A; A0 = 1 + alpha / A; A1 = -2 * cs; A2 = 1 - alpha / A;
                    break;
            }
            b0 = (float)(B0 / A0); b1 = (float)(B1 / A0); b2 = (float)(B2 / A0);
            a1 = (float)(A1 / A0); a2 = (float)(A2 / A0);
        }

        public float Process(float x)
        {
            float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return y;
        }
    }

    /// <summary>
    /// Extended Karplus-Strong plucked string: noise burst shaped by pick position and brightness,
    /// averaging loop filter, first-order allpass for exact tuning, per-period decay for a T60.
    /// </summary>
    public sealed class KsString
    {
        const int Size = 4096, Mask = Size - 1;
        readonly float[] m_Buf = new float[Size];
        readonly float m_Sr;
        int m_W, m_L;
        float m_C, m_ApIn, m_ApOut, m_Prev, m_Gain = 0.996f, m_Damp = 1f, m_DampTarget = 1f;
        Rng m_Rng;
        public bool Active;
        float m_Energy;

        public KsString(float sampleRate, uint seed)
        {
            m_Sr = sampleRate;
            m_Rng = new Rng(seed);
        }

        public void Pluck(float freq, float amp, float brightness, float t60, float pickPos = 0.13f)
        {
            float n = m_Sr / freq;                         // loop length in samples
            float d = n - 0.5f;                            // averaging filter contributes 0.5
            m_L = (int)(d - 0.1f);
            float frac = d - m_L;                          // 0.1 .. 1.1
            m_C = (1f - frac) / (1f + frac);
            m_Gain = (float)Math.Pow(10.0, -3.0 / (t60 * freq));
            m_Damp = m_DampTarget = 1f;

            // Excitation: one-pole low-passed noise (brightness) minus a delayed copy (pick position comb).
            int len = Math.Min(m_L, Size - 1);
            int pick = Math.Max(1, (int)(len * pickPos));
            float a = 0.08f + 0.9f * Math.Clamp(brightness, 0f, 1f);
            float lp = 0;
            Span<float> exc = stackalloc float[len];
            for (int i = 0; i < len; i++)
            {
                lp += a * (m_Rng.Next() - lp);
                exc[i] = lp;
            }
            float mean = 0;
            for (int i = 0; i < len; i++) mean += exc[i];
            mean /= Math.Max(1, len);

            for (int i = 0; i < len; i++)
            {
                float v = exc[i] - mean - (i >= pick ? exc[i - pick] - mean : 0f);
                int idx = (m_W - len + i) & Mask;
                m_Buf[idx] = m_Buf[idx] * 0.25f + v * amp;   // keep a little of the old vibration
            }
            m_ApIn = m_ApOut = m_Prev = 0;
            Active = true;
            m_Energy = 1f;
        }

        /// <summary>Palm-mute style damping (e.g. when the chord changes or the hand closes).</summary>
        public void Mute(float strength = 0.9985f) => m_DampTarget = strength;

        public float Tick()
        {
            if (!Active) return 0f;
            float x = m_Buf[(m_W - m_L) & Mask];
            float avg = 0.5f * (x + m_Prev);
            m_Prev = x;
            float ap = m_C * avg + m_ApIn - m_C * m_ApOut;
            m_ApIn = avg;
            m_ApOut = ap;
            m_Damp += (m_DampTarget - m_Damp) * 0.002f;
            float y = ap * m_Gain * m_Damp;
            m_Buf[m_W] = y;
            m_W = (m_W + 1) & Mask;

            m_Energy = m_Energy * 0.9995f + Math.Abs(x) * 0.0005f;
            if (m_Energy < 1e-5f) Active = false;
            return x;
        }
    }

    /// <summary>Compact Freeverb-style stereo reverb (4 combs + 2 allpasses per side).</summary>
    public sealed class Reverb
    {
        readonly float[][] m_Comb = new float[8][];
        readonly int[] m_CombIdx = new int[8];
        readonly float[] m_CombLp = new float[8];
        readonly float[][] m_Ap = new float[4][];
        readonly int[] m_ApIdx = new int[4];
        public float Feedback = 0.84f, Damping = 0.28f;

        public Reverb(float sr)
        {
            float s = sr / 44100f;
            int[] combs = { 1116, 1188, 1277, 1356 };
            int[] aps = { 556, 441 };
            for (int i = 0; i < 4; i++)
            {
                m_Comb[i] = new float[(int)(combs[i] * s)];
                m_Comb[i + 4] = new float[(int)((combs[i] + 23) * s)];
            }
            for (int i = 0; i < 2; i++)
            {
                m_Ap[i] = new float[(int)(aps[i] * s)];
                m_Ap[i + 2] = new float[(int)((aps[i] + 23) * s)];
            }
        }

        public void Process(float input, out float l, out float r)
        {
            l = Side(input, 0);
            r = Side(input, 4);
            l = Allpass(Allpass(l, 0), 1);
            r = Allpass(Allpass(r, 2), 3);
        }

        float Side(float x, int o)
        {
            float sum = 0;
            for (int i = o; i < o + 4; i++)
            {
                var buf = m_Comb[i];
                int k = m_CombIdx[i];
                float y = buf[k];
                m_CombLp[i] = y * (1 - Damping) + m_CombLp[i] * Damping;
                buf[k] = x + m_CombLp[i] * Feedback;
                m_CombIdx[i] = (k + 1) % buf.Length;
                sum += y;
            }
            return sum * 0.25f;
        }

        float Allpass(float x, int i)
        {
            var buf = m_Ap[i];
            int k = m_ApIdx[i];
            float b = buf[k];
            float y = -x + b;
            buf[k] = x + b * 0.5f;
            m_ApIdx[i] = (k + 1) % buf.Length;
            return y;
        }
    }
}
