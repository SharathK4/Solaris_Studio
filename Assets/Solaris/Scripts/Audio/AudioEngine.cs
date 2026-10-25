using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Solaris.Music;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Solaris.Audio
{
    /// <summary>A strum that happened on the audio thread (used for scoring and visuals).</summary>
    public struct StrumEvent
    {
        public double Beat;
        public int Held;       // chord index held by the player, -1 = nothing / fist
        public int Target;     // chart chord index at that beat, -1 = none
        public bool Down;
        public float Velocity;
    }

    /// <summary>
    /// Everything audible is synthesised here, on the audio thread (OnAudioFilterRead):
    ///  - acoustic guitar: 6 Karplus-Strong strings, strummed with realistic string stagger;
    ///  - backing band that follows the chart: soft kick, shaker, bass, warm pad, count-in click;
    ///  - a sample-accurate sequencer that strums the chord the player is HOLDING on the song's
    ///    strum pattern, and reports every strum back for scoring.
    /// The main thread only sets a few atomics and posts commands.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioEngine : MonoBehaviour
    {
        // ---------------------------------------------------------------- main-thread API
        public float GuitarVolume { set => m_GuitarVol = Mathf.Clamp01(value); }
        public float Tone { set => m_Tone = Mathf.Clamp01(value); }
        public float Pan { set => m_Pan = Mathf.Clamp(value, -1f, 1f); }
        public float Space { set => m_Space = Mathf.Clamp01(value); }
        public float BandVolume { set => m_BandVol = Mathf.Clamp01(value); }

        /// <summary>Chord the player is holding (null = mute). Picked up at the next strum.</summary>
        public void Hold(string chord)
        {
            var shape = Chords.Get(chord);
            int idx = shape != null ? shape.Index : -1;
            int prev = Interlocked.Exchange(ref m_Held, idx);
            if (prev != idx) m_Commands.Enqueue(new Cmd { Kind = CmdKind.HoldChanged, A = idx });
        }

        public string HeldChord => m_Held >= 0 ? Chords.All[m_Held].Name : null;

        /// <summary>Immediate strum outside of a song (tutorial previews).</summary>
        public void Strum(string chord, float velocity = 0.9f, bool down = true)
        {
            var shape = Chords.Get(chord);
            if (shape == null) return;
            m_Commands.Enqueue(new Cmd { Kind = CmdKind.Strum, A = shape.Index, F = velocity, B = down ? 1 : 0 });
        }

        public void MuteGuitar() => m_Commands.Enqueue(new Cmd { Kind = CmdKind.Mute });

        public void Play(Song song, int countInBars = 1)
        {
            var plan = SongPlan.Build(song, countInBars, m_Sr);
            m_Commands.Enqueue(new Cmd { Kind = CmdKind.Start, Obj = plan });
            Finished = false;
            IsPlaying = true;
            m_PlanBeatsPerSample = song.Bpm / 60.0 / m_Sr;
            m_PlanCountIn = plan.CountInBeats;
        }

        public void Stop()
        {
            m_Commands.Enqueue(new Cmd { Kind = CmdKind.Stop });
            IsPlaying = false;
        }

        public void SetPaused(bool paused)
        {
            m_Paused = paused;
            if (paused) MuteGuitar();
        }

        public bool IsPlaying { get; private set; }
        public bool Paused => m_Paused;
        public bool Finished { get; private set; }

        /// <summary>Current song position in beats, as heard (latency-compensated, smooth per frame).</summary>
        public double SongBeat
        {
            get
            {
                if (!IsPlaying) return m_LastBeat;
                long pos = Interlocked.Read(ref m_PublishedSample);
                long stamp = Interlocked.Read(ref m_PublishedStamp);
                double since = (Stopwatch.GetTimestamp() - stamp) / (double)Stopwatch.Frequency;
                if (m_Paused) since = 0;
                since = Math.Min(since, 2.5 * m_BufferSeconds);
                double samples = pos + since * m_Sr - m_OutputLatency * m_Sr;
                m_LastBeat = samples * m_PlanBeatsPerSample - m_PlanCountIn;
                return m_LastBeat;
            }
        }

        public bool TryTakeStrum(out StrumEvent e) => m_Strums.TryDequeue(out e);

        /// <summary>Smoothed output level 0..1 (for visuals).</summary>
        public float Level { get; private set; }
        /// <summary>Level of the guitar alone, spiky on strums.</summary>
        public float GuitarLevel { get; private set; }
        public readonly float[] Spectrum = new float[256];

        // ---------------------------------------------------------------- internals
        enum CmdKind { Strum, Mute, Start, Stop, HoldChanged }

        struct Cmd
        {
            public CmdKind Kind;
            public int A, B;
            public float F;
            public object Obj;
        }

        readonly ConcurrentQueue<Cmd> m_Commands = new ConcurrentQueue<Cmd>();
        readonly ConcurrentQueue<StrumEvent> m_Strums = new ConcurrentQueue<StrumEvent>();

        int m_Sr = 48000;
        double m_BufferSeconds = 0.005, m_OutputLatency = 0.02;
        volatile float m_GuitarVol = 0.85f, m_Tone = 0.5f, m_Pan, m_Space = 0.25f, m_BandVol = 0.8f;
        int m_Held = -1;
        volatile bool m_Paused;
        long m_PublishedSample, m_PublishedStamp;
        double m_PlanBeatsPerSample;
        int m_PlanCountIn;
        double m_LastBeat;
        volatile float m_LevelRaw, m_GuitarRaw;

        // audio-thread state
        KsString[] m_Strings;
        float[] m_StringPan;
        readonly PendingPluck[] m_Pending = new PendingPluck[24];
        Biquad m_BodyA, m_BodyB, m_ToneL, m_ToneR, m_HpL, m_HpR;
        float m_ToneApplied = -1;
        Reverb m_Reverb;
        Band m_Band;
        SongPlan m_Plan;
        long m_Sample;
        int m_NextEighth;
        bool m_Running;
        int m_LastStrumEighth = int.MinValue;
        int m_LastStrumChord = -1;
        long m_LastStrumSample;

        struct PendingPluck
        {
            public int Countdown;   // samples
            public int String;
            public int Note;
            public float Amp;
            public bool Used;
        }

        void Awake()
        {
            m_Sr = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out int len, out int num);
            m_BufferSeconds = len / (double)m_Sr;
            m_OutputLatency = len * Math.Max(2, num) / (double)m_Sr * 0.5;

            m_Strings = new KsString[6];
            m_StringPan = new float[6];
            for (int i = 0; i < 6; i++)
            {
                m_Strings[i] = new KsString(m_Sr, (uint)(1234 + i * 7919));
                m_StringPan[i] = -0.18f + i * 0.072f;
            }
            m_BodyA.Peak(m_Sr, 105f, 1.4f, 5f);
            m_BodyB.Peak(m_Sr, 230f, 1.8f, 3f);
            m_HpL.HighPass(m_Sr, 55f);
            m_HpR.HighPass(m_Sr, 55f);
            m_Reverb = new Reverb(m_Sr);
            m_Band = new Band(m_Sr);

            var src = GetComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.loop = true;
            src.clip = AudioClip.Create("SolarisSilence", m_Sr, 2, m_Sr, false);
            src.Play();
            m_Running = true;
            Debug.Log($"[Solaris] Audio {m_Sr} Hz, buffer {len}x{num} ({m_BufferSeconds * 1000:0.0} ms)");
        }

        void Update()
        {
            AudioListener.GetSpectrumData(Spectrum, 0, FFTWindow.BlackmanHarris);
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.06f);
            Level = Mathf.Lerp(Level, m_LevelRaw, k);
            GuitarLevel = Mathf.Lerp(GuitarLevel, m_GuitarRaw, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.03f));
            if (m_PlanFinishedFlag)
            {
                m_PlanFinishedFlag = false;
                Finished = true;
                IsPlaying = false;
            }
        }

        volatile bool m_PlanFinishedFlag;

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!m_Running) return;
            while (m_Commands.TryDequeue(out var c)) Execute(c);

            float tone = m_Tone;
            if (Math.Abs(tone - m_ToneApplied) > 0.01f)
            {
                float cutoff = 1100f * (float)Math.Pow(2, tone * 3.6);
                m_ToneL.LowPass(m_Sr, cutoff, 0.6f);
                m_ToneR.LowPass(m_Sr, cutoff, 0.6f);
                m_ToneApplied = tone;
            }

            float gv = m_GuitarVol, pan = m_Pan, space = m_Space, band = m_BandVol;
            float panL = (float)Math.Cos((pan + 1) * Math.PI / 4) * 1.414f;
            float panR = (float)Math.Sin((pan + 1) * Math.PI / 4) * 1.414f;
            int frames = data.Length / channels;
            float sumSq = 0, gSq = 0;
            bool paused = m_Paused;

            // The record follows the player: on the chart's chord it plays full and open; on a wrong chord
            // it dips and dulls so your clash is what you hear; with no hand it sinks back.
            float backGainTarget = 1f, backCloseTarget = 0f;
            var plan = m_Plan;
            if (m_Back != null && plan != null && m_NextEighth > 0)
            {
                int e = Math.Min(m_NextEighth - 1, plan.ChordPerEighth.Length - 1);
                int target = plan.ChordPerEighth[e], held = m_Held;
                if (target >= 0 && held != target)
                {
                    backGainTarget = held < 0 ? 0.3f : 0.38f;
                    backCloseTarget = 1f;
                }
            }
            if (m_Back != null) band = 0f;          // the record has its own drums
            float upK = 1f / (0.06f * m_Sr), downK = 1f / (0.35f * m_Sr);

            for (int f = 0; f < frames; f++)
            {
                if (m_Plan != null && !paused) StepSequencer();
                TickPending();

                float gl = 0, gr = 0;
                for (int s = 0; s < 6; s++)
                {
                    var str = m_Strings[s];
                    if (!str.Active) continue;
                    float v = str.Tick();
                    gl += v * (1 - m_StringPan[s]);
                    gr += v * (1 + m_StringPan[s]);
                }
                float mono = (gl + gr) * 0.5f;
                float body = m_BodyB.Process(m_BodyA.Process(mono));
                gl = m_ToneL.Process(gl * 0.55f + body * 0.45f) * gv * panL;
                gr = m_ToneR.Process(gr * 0.55f + body * 0.45f) * gv * panR;
                gSq += gl * gl + gr * gr;

                m_Band.Tick(out float bl, out float br, out float bandSend);
                bl *= band;
                br *= band;

                float send = (gl + gr) * 0.5f * (0.25f + 0.55f * space) + bandSend * band * 0.3f;
                m_Reverb.Process(send, out float rl, out float rr);

                float lead = m_Back != null ? 0.62f * GuitarOverBacking : 0.62f;
                float l = gl * lead + bl + rl * 0.55f;
                float r = gr * lead + br + rr * 0.55f;
                if (m_Back != null && !paused)
                {
                    MixBacking(out float xl, out float xr, backGainTarget, backCloseTarget, upK, downK);
                    l += xl;
                    r += xr;
                }
                l = SoftClip(m_HpL.Process(l) * 0.9f);
                r = SoftClip(m_HpR.Process(r) * 0.9f);
                sumSq += l * l + r * r;

                int o = f * channels;
                data[o] = l;
                if (channels > 1) data[o + 1] = r;
                for (int c = 2; c < channels; c++) data[o + c] = 0;
                if (m_Plan != null && !paused) m_Sample++;
            }

            Record(data, channels);
            m_LevelRaw = (float)Math.Sqrt(sumSq / Math.Max(1, frames * 2)) * 2.2f;
            m_GuitarRaw = (float)Math.Sqrt(gSq / Math.Max(1, frames * 2)) * 3f;
            Interlocked.Exchange(ref m_PublishedSample, m_Sample);
            Interlocked.Exchange(ref m_PublishedStamp, Stopwatch.GetTimestamp());
        }

        static float SoftClip(float x) => x / (1f + Math.Abs(x) * 0.35f);

        BackingAudio m_Back;
        double m_BackPos, m_BackStep;
        float m_BackFade, m_BackFadeStep, m_BackGain = 1f, m_BackClose, m_BackLpL, m_BackLpR;
        volatile float m_TuningRatio = 1f;
        const float BackingLevel = 0.5f;      // the record sits under your guitar
        const float GuitarOverBacking = 1.8f; // your strums lead when a record plays

        void MixBacking(out float l, out float r, float gainTarget, float closeTarget, float upK, float downK)
        {
            l = r = 0f;
            var b = m_Back;
            m_BackFade = Math.Clamp(m_BackFade + m_BackFadeStep, 0f, 1f);
            if (m_BackFadeStep < 0f && m_BackFade <= 0f)
            {
                m_Back = null;
                return;
            }
            int i = (int)Math.Floor(m_BackPos);
            if (i >= 0 && i + 1 < b.Frames)
            {
                float t = (float)(m_BackPos - i);
                l = b.Data[2 * i] + (b.Data[2 * i + 2] - b.Data[2 * i]) * t;
                r = b.Data[2 * i + 1] + (b.Data[2 * i + 3] - b.Data[2 * i + 1]) * t;
            }
            else if (i + 1 >= b.Frames) m_BackFadeStep = -1f / (0.05f * m_Sr);
            m_BackPos += m_BackStep;

            m_BackGain += (gainTarget - m_BackGain) * (gainTarget > m_BackGain ? upK : downK);
            m_BackClose += (closeTarget - m_BackClose) * (closeTarget < m_BackClose ? upK : downK);
            float a = 1f - 0.9f * m_BackClose;      // one-pole low-pass: open .. about 900 Hz
            m_BackLpL += a * (l - m_BackLpL);
            m_BackLpR += a * (r - m_BackLpR);
            float g = m_BackGain * m_BackFade * BackingLevel;
            l = m_BackLpL * g;
            r = m_BackLpR * g;
        }

        void Execute(Cmd c)
        {
            switch (c.Kind)
            {
                case CmdKind.Strum:
                    DoStrum(c.A, c.B == 1, c.F, 1f);
                    break;
                case CmdKind.Mute:
                    foreach (var s in m_Strings) s.Mute(0.9975f);
                    break;
                case CmdKind.Start:
                    m_Plan = (SongPlan)c.Obj;
                    m_Sample = 0;
                    m_NextEighth = -m_Plan.CountInBeats * 2;
                    m_LastStrumEighth = int.MinValue;
                    m_Band.Reset();
                    m_Band.Drums = m_Plan.Drums;
                    m_TuningRatio = m_Plan.TuningRatio;
                    m_Back = m_Plan.Backing;
                    if (m_Back != null)
                    {
                        // During the count-in the record's previous bar plays, so the song leads itself in.
                        double countInSec = m_Plan.CountInBeats * m_Plan.SamplesPerEighth * 2 / m_Sr;
                        m_BackPos = (m_Plan.BackingOffset - countInSec) * m_Back.Rate;
                        m_BackStep = m_Back.Rate / (double)m_Sr;
                        m_BackFade = 0f;
                        m_BackFadeStep = 1f / (0.04f * m_Sr);
                        m_BackGain = 1f;
                        m_BackClose = 0f;
                    }
                    break;
                case CmdKind.Stop:
                    m_Plan = null;
                    m_TuningRatio = 1f;
                    if (m_Back != null) m_BackFadeStep = -1f / (0.3f * m_Sr);
                    m_Band.Silence();
                    foreach (var s in m_Strings) s.Mute(0.997f);
                    break;
                case CmdKind.HoldChanged:
                    OnHoldChanged(c.A);
                    break;
            }
        }

        /// <summary>
        /// Late chord correction: if the player lands the new chord just after a strum (camera + human
        /// latency), re-voice with a light strum instead of letting the wrong chord ring.
        /// </summary>
        void OnHoldChanged(int chord)
        {
            if (chord < 0)
            {
                foreach (var s in m_Strings) s.Mute(0.998f);
                return;
            }
            if (m_Plan == null || m_Paused) return;
            long since = m_Sample - m_LastStrumSample;
            if (since < m_Sr * 0.16 && m_LastStrumChord != chord)
            {
                int bar = (m_NextEighth - 1) / (m_Plan.Meter * 2);
                if (SongPlan.IsPicked(m_Plan.PatternFor(bar))) DoPick(chord, 'B', 0.8f);   // fingerstyle: re-pick the bass
                else DoStrum(chord, true, 0.55f, 0.7f);
                m_LastStrumChord = chord;
            }
        }

        void StepSequencer()
        {
            long eighthSample = (long)((m_NextEighth + m_Plan.CountInBeats * 2) * m_Plan.SamplesPerEighth);
            if (m_Sample < eighthSample) return;
            int e = m_NextEighth++;
            double beat = e * 0.5;

            if (beat < 0)
            {
                if ((e & 1) == 0 && m_Plan.Backing == null) m_Band.Click(Mod(e / 2, m_Plan.Meter) == 0);
                return;
            }
            if (beat >= m_Plan.TotalBeats)
            {
                if (m_Back != null) m_BackFadeStep = -1f / (2.5f * m_Sr);
                m_TuningRatio = 1f;
                m_Plan = null;
                m_Band.Silence();
                foreach (var s in m_Strings) s.Mute(0.9992f);
                m_PlanFinishedFlag = true;
                return;
            }

            int eighthInBar = Mod(e, m_Plan.Meter * 2);
            int target = m_Plan.ChordPerEighth[e];
            m_Band.OnEighth(eighthInBar, m_Plan.Meter, target >= 0 ? Chords.All[target] : null, m_Plan.IsChordStart[e]);

            string pattern = m_Plan.PatternFor(e / (m_Plan.Meter * 2));
            char stroke = pattern[eighthInBar];
            if (stroke == '.') return;
            bool pick = stroke != 'D' && stroke != 'U';
            bool down = stroke != 'U';
            float vel = pick ? (stroke == 'B' ? 0.95f : 0.62f) : eighthInBar == 0 ? 1f : down ? 0.8f : 0.58f;
            int held = m_Held;
            if (held >= 0)
            {
                if (pick) DoPick(held, stroke, vel);
                else DoStrum(held, down, vel, 1f);
            }
            else foreach (var s in m_Strings) s.Mute(0.998f);
            m_LastStrumEighth = e;
            m_Strums.Enqueue(new StrumEvent { Beat = beat, Held = held, Target = target, Down = down, Velocity = vel });
        }

        static int Mod(int a, int m) => ((a % m) + m) % m;

        void DoStrum(int chordIndex, bool down, float velocity, float spread)
        {
            var chord = Chords.All[chordIndex];
            float stagger = (down ? 0.011f : 0.008f) * spread * (1.25f - velocity * 0.4f);
            int order = 0;
            for (int k = 0; k < 6; k++)
            {
                int s = down ? k : 5 - k;
                int note = chord.Note(s);
                if (note < 0)
                {
                    m_Strings[s].Mute(0.996f);
                    continue;
                }
                // Up-strums mostly catch the treble strings.
                float amp = velocity * (down ? 1f : (s >= 2 ? 0.85f : 0.35f)) * (0.85f + 0.3f * (float)m_Jitter.Next() * 0.5f);
                Schedule(s, note, amp * 0.55f, (int)(order * stagger * m_Sr));
                order++;
            }
            m_LastStrumSample = m_Sample;
            m_LastStrumChord = chordIndex;
        }

        /// <summary>Fingerstyle: one string of the chord. 'B' = its lowest sounding string, '1'..'6' = guitar string.</summary>
        void DoPick(int chordIndex, char step, float velocity)
        {
            var chord = Chords.All[chordIndex];
            int s = -1;
            if (step == 'B')
            {
                for (int k = 0; k < 6 && s < 0; k++) if (chord.Note(k) >= 0) s = k;
            }
            else s = 6 - (step - '0');                         // string 1 (high E) = index 5
            if (s < 0 || s > 5) return;
            int note = chord.Note(s);
            if (note < 0) return;
            // Moving the fretting hand to this shape stops every string that is still ringing a note of
            // another chord (open strings that belong to both keep ringing, as on a real guitar).
            for (int k = 0; k < 6; k++)
                if (m_StringNote[k] >= 0 && m_StringNote[k] != chord.Note(k)) { m_Strings[k].Mute(0.994f); m_StringNote[k] = -1; }
            float amp = velocity * (0.9f + 0.2f * (float)m_Jitter.Next() * 0.5f);
            Schedule(s, note, amp * 0.62f, 0);
            m_LastStrumSample = m_Sample;
            m_LastStrumChord = chordIndex;
        }

        readonly int[] m_StringNote = { -1, -1, -1, -1, -1, -1 };   // what each string last sounded (audio thread)
        Rng m_Jitter = new Rng(99);

        void Schedule(int s, int note, float amp, int delay)
        {
            for (int i = 0; i < m_Pending.Length; i++)
            {
                if (m_Pending[i].Used) continue;
                m_Pending[i] = new PendingPluck { Used = true, Countdown = delay, String = s, Note = note, Amp = amp };
                return;
            }
        }

        void TickPending()
        {
            for (int i = 0; i < m_Pending.Length; i++)
            {
                if (!m_Pending[i].Used) continue;
                if (m_Pending[i].Countdown-- > 0) continue;
                var p = m_Pending[i];
                m_Pending[i].Used = false;
                float freq = 440f * (float)Math.Pow(2, (p.Note - 69) / 12.0) * m_TuningRatio;
                float t60 = 5.5f - (p.Note - 40) * 0.08f;
                m_StringNote[p.String] = p.Note;
                m_Strings[p.String].Pluck(freq, p.Amp, 0.35f + 0.55f * m_Tone, t60, 0.11f + 0.03f * p.String / 5f);
            }
        }

        // ---------------------------------------------------------------- test recording
        float[] m_Record;
        int m_RecordPos;

        void OnEnable()
        {
            if (!string.IsNullOrEmpty(AppArgs.Get("-solaris-record-audio"))) m_Record = new float[48000 * 2 * 240];
        }

        void Record(float[] data, int channels)
        {
            if (m_Record == null || channels < 2) return;
            int frames = data.Length / channels;
            for (int f = 0; f < frames && m_RecordPos + 1 < m_Record.Length; f++)
            {
                m_Record[m_RecordPos++] = data[f * channels];
                m_Record[m_RecordPos++] = data[f * channels + 1];
            }
        }

        void OnApplicationQuit()
        {
            string path = AppArgs.Get("-solaris-record-audio");
            if (m_Record == null || string.IsNullOrEmpty(path)) return;
            m_Running = false;
            int n = m_RecordPos;
            using var w = new System.IO.BinaryWriter(System.IO.File.Create(path));
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)2);
            w.Write(m_Sr); w.Write(m_Sr * 4); w.Write((short)4); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
            for (int i = 0; i < n; i++) w.Write((short)Mathf.Clamp(m_Record[i] * 32767f, -32768f, 32767f));
            Debug.Log($"[Solaris] Recorded {n / 2f / m_Sr:0.0}s of audio to {path}");
        }

        void OnDestroy() => m_Running = false;
    }

    /// <summary>Song pre-baked for the audio thread (no allocation or parsing while playing).</summary>
    public sealed class SongPlan
    {
        public int Meter, CountInBeats;
        public double SamplesPerEighth;
        public double TotalBeats;
        public int[] ChordPerEighth;
        public bool[] IsChordStart;
        public string Pattern;
        /// <summary>Per-bar right-hand pattern (song "patterns"); falls back to <see cref="Pattern"/>.</summary>
        public string[] BarPattern;
        public bool Drums = true;
        /// <summary>The song's own recording (guitar + drums), played sample-locked to the chart.</summary>
        public BackingAudio Backing;
        public double BackingOffset;          // seconds into the backing where beat 0 falls
        public float TuningRatio = 1f;        // gesture guitar retuned to the backing

        public string PatternFor(int bar) =>
            BarPattern != null && bar >= 0 && bar < BarPattern.Length && BarPattern[bar] != null ? BarPattern[bar] : Pattern;

        public static bool IsPicked(string pattern) => pattern.IndexOfAny(k_PickSteps) >= 0;
        static readonly char[] k_PickSteps = { 'B', '1', '2', '3', '4', '5', '6' };

        public static SongPlan Build(Song song, int countInBars, int sampleRate)
        {
            var p = new SongPlan
            {
                Meter = song.Meter,
                CountInBeats = countInBars * song.Meter,
                SamplesPerEighth = sampleRate * 60.0 / song.Bpm / 2.0,
                TotalBeats = song.TotalBeats,
                Drums = song.Drums,
                BackingOffset = song.BackingOffset,
                TuningRatio = (float)Math.Pow(2.0, song.TuningCents / 1200.0),
                Pattern = song.Meter == 3 ? "D.DUDU" : song.Meter == 2 ? "D.DU" : "D.DU.UDU",
            };
            if (!string.IsNullOrEmpty(song.Backing))
                p.Backing = BackingAudio.Load(System.IO.Path.Combine(Application.streamingAssetsPath, "Songs", song.Backing));
            int bars = (int)Math.Ceiling(song.TotalBeats / song.Meter);
            if (song.Patterns.Count > 0)
            {
                p.BarPattern = new string[bars];
                for (int b = 0; b < bars; b++) p.BarPattern[b] = song.PatternAt(b);
            }
            int n = (int)Math.Ceiling(song.TotalBeats * 2) + 2;
            p.ChordPerEighth = new int[n];
            p.IsChordStart = new bool[n];
            int last = -2;
            for (int e = 0; e < n; e++)
            {
                int ci = song.ChordIndexAt(e * 0.5f);
                var shape = ci >= 0 ? Chords.Get(song.Chords[ci].Chord) : null;
                p.ChordPerEighth[e] = shape != null ? shape.Index : -1;
                p.IsChordStart[e] = ci != last;
                last = ci;
            }
            return p;
        }
    }
}
