using System;
using Solaris.Music;

namespace Solaris.Audio
{
    /// <summary>
    /// Soft acoustic backing that follows the chart: felt kick, brushed shaker, round bass and a
    /// warm detuned pad, plus the count-in click. Quiet by design; the player's guitar leads.
    /// </summary>
    public sealed class Band
    {
        readonly float m_Sr;
        Rng m_Rng = new Rng(4242);

        // kick
        float m_KickT = 10, m_KickPhase;
        // shaker
        float m_ShakeEnv, m_ShakeLp, m_ShakeAmp;
        // click
        float m_ClickT = 10, m_ClickFreq, m_ClickPhase;
        // bass
        float m_BassFreq, m_BassPhase, m_BassEnv, m_BassLp;
        // pad: 3 voices x 2 detuned saws
        readonly float[] m_PadFreq = new float[3], m_PadTarget = new float[3];
        readonly float[] m_PadPhase = new float[6];
        float m_PadAmp, m_PadAmpTarget, m_PadLpL, m_PadLpR, m_PadLp2L, m_PadLp2R;

        /// <summary>False = ballad: no kick or shaker, the bass only marks each bar softly.</summary>
        public bool Drums = true;

        public Band(float sampleRate) => m_Sr = sampleRate;

        public void Reset()
        {
            m_PadAmpTarget = 0;
            m_BassEnv = 0;
        }

        public void Silence()
        {
            m_PadAmpTarget = 0;
            m_BassEnv *= 0.3f;
        }

        public void Click(bool accent)
        {
            m_ClickT = 0;
            m_ClickFreq = accent ? 2100f : 1500f;
            m_ClickPhase = 0;
        }

        public void OnEighth(int eighthInBar, int meter, ChordShape chord, bool chordStart)
        {
            bool onBeat = (eighthInBar & 1) == 0;
            int beatInBar = eighthInBar / 2;

            // Kick on 1 (and 3 in 4/4).
            if (Drums && onBeat && (beatInBar == 0 || (meter == 4 && beatInBar == 2)))
            {
                m_KickT = 0;
                m_KickPhase = 0;
            }

            // Shaker on every eighth, accented on the "and".
            if (Drums)
            {
                m_ShakeEnv = 1f;
                m_ShakeAmp = onBeat ? 0.35f : 0.75f;
            }

            if (chord == null) return;

            // Pad glides to the new triad.
            if (chordStart || m_PadAmpTarget <= 0)
            {
                var triad = chord.Triad();
                for (int i = 0; i < 3; i++)
                {
                    m_PadTarget[i] = Hz(triad[i]);
                    if (m_PadFreq[i] <= 0) m_PadFreq[i] = m_PadTarget[i];
                }
                m_PadAmpTarget = 1f;
            }

            // Bass: root on 1, fifth on the middle beat.
            if (!Drums)
            {
                if (chordStart)
                {
                    m_BassFreq = Hz(28 + ((chord.Root - 28) % 12 + 12) % 12);
                    m_BassEnv = 0.75f;
                }
            }
            else if (onBeat && (beatInBar == 0 || beatInBar == meter / 2 + (meter == 3 ? 1 : 0)))
            {
                int root = 28 + ((chord.Root - 28) % 12 + 12) % 12;   // E1..D#2 register
                int note = beatInBar == 0 ? root : root + 7;
                m_BassFreq = Hz(note);
                m_BassEnv = beatInBar == 0 ? 1f : 0.7f;
            }
        }

        static float Hz(int midi) => 440f * (float)Math.Pow(2, (midi - 69) / 12.0);

        public void Tick(out float l, out float r, out float send)
        {
            float dt = 1f / m_Sr;
            float mono = 0f;

            // Kick: pitch drop 110 -> 48 Hz, soft (felt beater).
            if (m_KickT < 0.5f)
            {
                float f = 48f + 62f * (float)Math.Exp(-m_KickT / 0.028f);
                m_KickPhase += f * dt;
                float env = (float)Math.Exp(-m_KickT / 0.16f);
                mono += (float)Math.Sin(2 * Math.PI * m_KickPhase) * env * 0.34f;
                m_KickT += dt;
            }

            // Shaker: high-passed noise with a quick envelope.
            if (m_ShakeEnv > 1e-4f)
            {
                float n = m_Rng.Next();
                m_ShakeLp += 0.35f * (n - m_ShakeLp);
                float hp = n - m_ShakeLp;
                mono += hp * m_ShakeEnv * m_ShakeAmp * 0.045f;
                m_ShakeEnv *= 1f - 1f / (0.028f * m_Sr);
            }

            // Bass: sine + a touch of second harmonic, low-passed.
            if (m_BassEnv > 1e-4f)
            {
                m_BassPhase += m_BassFreq * dt;
                if (m_BassPhase > 1) m_BassPhase -= 1;
                float s = (float)(Math.Sin(2 * Math.PI * m_BassPhase) + 0.25 * Math.Sin(4 * Math.PI * m_BassPhase));
                m_BassLp += 0.08f * (s - m_BassLp);
                mono += m_BassLp * m_BassEnv * 0.2f;
                m_BassEnv *= 1f - 1f / (0.9f * m_Sr);
            }

            // Click (count-in).
            float click = 0;
            if (m_ClickT < 0.05f)
            {
                m_ClickPhase += m_ClickFreq * dt;
                click = (float)Math.Sin(2 * Math.PI * m_ClickPhase) * (float)Math.Exp(-m_ClickT / 0.012f) * 0.22f;
                m_ClickT += dt;
            }

            // Pad.
            m_PadAmp += (m_PadAmpTarget - m_PadAmp) * (m_PadAmpTarget > m_PadAmp ? 0.00004f : 0.00008f);
            float pl = 0, pr = 0;
            if (m_PadAmp > 1e-4f)
            {
                for (int v = 0; v < 3; v++)
                {
                    m_PadFreq[v] += (m_PadTarget[v] - m_PadFreq[v]) * 0.0006f;
                    for (int d = 0; d < 2; d++)
                    {
                        int k = v * 2 + d;
                        float det = d == 0 ? 0.9965f : 1.0035f;
                        m_PadPhase[k] += m_PadFreq[v] * det * dt;
                        if (m_PadPhase[k] > 1) m_PadPhase[k] -= 1;
                        float saw = m_PadPhase[k] * 2 - 1;
                        if (d == 0) pl += saw; else pr += saw;
                    }
                }
                // Two one-pole low-passes ~ 700 Hz: warm, no fizz.
                const float a = 0.085f;
                m_PadLpL += a * (pl - m_PadLpL); m_PadLp2L += a * (m_PadLpL - m_PadLp2L);
                m_PadLpR += a * (pr - m_PadLpR); m_PadLp2R += a * (m_PadLpR - m_PadLp2R);
                pl = m_PadLp2L * m_PadAmp * 0.035f;
                pr = m_PadLp2R * m_PadAmp * 0.035f;
            }

            l = mono + pl + click;
            r = mono + pr + click;
            send = (pl + pr) * 0.5f;
        }
    }
}
