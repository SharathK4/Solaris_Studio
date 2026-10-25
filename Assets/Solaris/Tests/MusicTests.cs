using System.IO;
using NUnit.Framework;
using Solaris.Audio;
using Solaris.Music;
using UnityEngine;

namespace Solaris.Tests
{
    public class MusicTests
    {
        static string[] SongFiles => Directory.GetFiles(Path.Combine(Application.streamingAssetsPath, "Songs"), "*.json");

        [Test]
        public void AllSongsParseAndFitTheirChart()
        {
            Assert.That(SongFiles.Length, Is.GreaterThanOrEqualTo(8));
            foreach (var f in SongFiles)
            {
                var s = Song.Parse(Path.GetFileNameWithoutExtension(f), File.ReadAllText(f));
                Assert.That(s.Title, Is.Not.Empty, f);
                Assert.That(s.Chords.Count, Is.GreaterThan(0), f);
                Assert.That(s.Lines.Count, Is.GreaterThan(0), f);
                Assert.That(s.TotalBeats % s.Meter, Is.EqualTo(0).Within(1e-4), $"{f}: chart must be whole bars");
                Assert.That(s.LyricsEnd, Is.LessThanOrEqualTo(s.TotalBeats + 1e-3f), $"{f}: lyrics overrun the chart");
                foreach (var c in s.Chords)
                {
                    Assert.That(Chords.IsKnown(c.Chord), f + " " + c.Chord);
                    Assert.That(s.FingersFor(c.Chord), Is.InRange(1, 5), $"{f}: {c.Chord} must be playable by gesture");
                }
                Assert.That(s.ChordAt(0), Is.EqualTo(s.Chords[0].Chord));
                Assert.That(s.ChordAt(s.TotalBeats + 1), Is.Null);
            }
        }

        [Test]
        public void ParserHandlesPickupSyllablesAndRests()
        {
            var s = Song.Parse("t", "{\"title\":\"T\",\"bpm\":60,\"meter\":3,\"pickup\":1,\"chords\":\"G G:2 D:1\"," +
                                    "\"lyrics\":[\"A:1 | ma-:2 zing:1 _:2\",\"grace:1\"]}");
            Assert.That(s.TotalBeats, Is.EqualTo(6));
            Assert.That(s.Lines[0].Words[0].Beat, Is.EqualTo(-1));
            Assert.That(s.Lines[0].Text, Is.EqualTo("A mazing"));
            Assert.That(s.Lines[1].Words[0].Beat, Is.EqualTo(5));
            Assert.That(s.ChordAt(4.5f), Is.EqualTo("G"));
            Assert.That(s.ChordAt(5.2f), Is.EqualTo("D"));
        }

        [Test]
        public void DoubleHyphenKeepsARealHyphen()
        {
            var s = Song.Parse("t", "{\"title\":\"T\",\"bpm\":60,\"meter\":4,\"chords\":\"G\",\"lyrics\":[\"one--:1 horse:1 o-:1 pen:1\"]}");
            Assert.That(s.Lines[0].Text, Is.EqualTo("one-horse open"));
        }

        [Test]
        public void GestureChordMapping()
        {
            CollectionAssert.AreEqual(new[] { "C", "D", "Em", "G", "Am" }, Chords.GestureChords);
            for (int n = 1; n <= 5; n++) Assert.That(Chords.FingersFor(Chords.FromFingers(n)), Is.EqualTo(n));
            Assert.That(Chords.FromFingers(0), Is.Null);
            foreach (var c in Chords.GestureChords)
            {
                Assert.That(Chords.Flip(Chords.Flip(c)), Is.EqualTo(c));
                Assert.That(Chords.Base(Chords.Flip(c)), Is.EqualTo(c));
                Assert.That(Chords.Get(Chords.Flip(c)).Minor, Is.Not.EqualTo(Chords.Get(c).Minor));
            }
        }

        [Test]
        public void VoicingsAreStandardOpenChords()
        {
            // C = x32010 -> C3 E3 G3 C4 E4
            var c = Chords.Get("C");
            CollectionAssert.AreEqual(new[] { -1, 48, 52, 55, 60, 64 }, new[] { c.Note(0), c.Note(1), c.Note(2), c.Note(3), c.Note(4), c.Note(5) });
            var em = Chords.Get("Em");
            CollectionAssert.AreEqual(new[] { 40, 47, 52, 55, 59, 64 }, new[] { em.Note(0), em.Note(1), em.Note(2), em.Note(3), em.Note(4), em.Note(5) });
        }

        [Test]
        public void KarplusStrongRingsAtPitchAndDecays()
        {
            const int sr = 48000;
            var s = new KsString(sr, 7);
            s.Pluck(110f, 0.8f, 0.6f, 3f);
            var buf = new float[sr];
            for (int i = 0; i < buf.Length; i++) buf[i] = s.Tick();

            float early = Rms(buf, 0, 4800), late = Rms(buf, 43200, 4800);
            Assert.That(early, Is.GreaterThan(0.01f), "audible");
            Assert.That(late, Is.LessThan(early), "decays");
            foreach (var v in buf) Assert.That(float.IsNaN(v) || Mathf.Abs(v) > 2f, Is.False, "bounded");

            // Pitch by autocorrelation (a plucked string is rich in harmonics, so zero crossings lie).
            int best = 0;
            double bestR = double.MinValue;
            for (int lag = sr / 400; lag <= sr / 50; lag++)
            {
                double r = 0;
                for (int i = 6000; i < 6000 + 8192; i++) r += buf[i] * buf[i + lag];
                if (r > bestR) { bestR = r; best = lag; }
            }
            float hz = (float)sr / best;
            Assert.That(hz, Is.EqualTo(110f).Within(2f));
        }

        [Test]
        public void SongPlanMarksChordChanges()
        {
            var s = Song.Parse("t", "{\"title\":\"T\",\"bpm\":120,\"meter\":4,\"chords\":\"G C:2 D:2\",\"lyrics\":[\"la:8\"]}");
            var plan = SongPlan.Build(s, 1, 48000);
            Assert.That(plan.CountInBeats, Is.EqualTo(4));
            Assert.That(plan.SamplesPerEighth, Is.EqualTo(12000).Within(1e-6));
            Assert.That(plan.ChordPerEighth[0], Is.EqualTo(Chords.Get("G").Index));
            Assert.That(plan.ChordPerEighth[8], Is.EqualTo(Chords.Get("C").Index));
            Assert.That(plan.IsChordStart[8], Is.True);
            Assert.That(plan.IsChordStart[9], Is.False);
            Assert.That(plan.ChordPerEighth[12], Is.EqualTo(Chords.Get("D").Index));
        }

        [Test]
        public void PatternsSwitchPerBarAndValidate()
        {
            var s = Song.Parse("t", "{\"title\":\"T\",\"bpm\":90,\"meter\":4,\"chords\":\"Em C Am G\"," +
                                    "\"patterns\":[\"2:D.DU.UDU\",\"0:B3231323\"],\"lyrics\":[\"la:16\"]}");
            Assert.That(s.PatternAt(0), Is.EqualTo("B3231323"));
            Assert.That(s.PatternAt(1), Is.EqualTo("B3231323"));
            Assert.That(s.PatternAt(3), Is.EqualTo("D.DU.UDU"));
            var plan = SongPlan.Build(s, 1, 48000);
            Assert.That(plan.PatternFor(0), Is.EqualTo("B3231323"));
            Assert.That(plan.PatternFor(2), Is.EqualTo("D.DU.UDU"));
            Assert.That(SongPlan.IsPicked(plan.PatternFor(0)), Is.True);
            Assert.That(SongPlan.IsPicked(plan.PatternFor(2)), Is.False);
            Assert.Throws<System.FormatException>(() => Song.Parse("t",
                "{\"title\":\"T\",\"bpm\":90,\"meter\":4,\"chords\":\"G\",\"patterns\":[\"0:DDU\"],\"lyrics\":[\"la:4\"]}"));
        }

        [Test]
        public void LyricSlotsReplaceLinesInTime()
        {
            var s = Song.Parse("t", "{\"title\":\"T\",\"bpm\":90,\"meter\":4,\"drums\":false,\"chords\":\"G G G G\"," +
                                    "\"lyrics\":[\"Intro:0 G:8\",\"G:8\"]}");
            Assert.That(s.Drums, Is.False);
            var slots = Song.ReadLyricSlots("# comment\n>\n> one two three four\n");
            Assert.That(slots, Is.EqualTo(new[] { "", "one two three four" }));
            s.ApplyLyricSlots(slots);
            Assert.That(s.Lines[0].Text, Is.EqualTo("Intro G"));               // blank slot keeps the cue
            Assert.That(s.Lines[1].Text, Is.EqualTo("one two three four"));
            Assert.That(s.Lines[1].Start, Is.EqualTo(8f).Within(1e-4));
            Assert.That(s.Lines[1].End, Is.LessThanOrEqualTo(16f));
            Assert.That(s.LyricsEnd, Is.LessThanOrEqualTo(s.TotalBeats));
        }

        [Test]
        public void SongGestureMapAndAlternates()
        {
            var s = Song.Parse("t", "{\"title\":\"T\",\"bpm\":90,\"meter\":4,\"chords\":\"Am F Dm Bb\"," +
                                    "\"gestures\":[\"C\",\"Dm\",\"F\",\"G\",\"Am\"],\"alternates\":[\"F:Bb\"],\"lyrics\":[\"la:16\"]}");
            Assert.That(s.ChordForFingers(3), Is.EqualTo("F"));
            Assert.That(s.ChordForFingers(2), Is.EqualTo("Dm"));
            Assert.That(s.Alternate("F"), Is.EqualTo("Bb"));
            Assert.That(s.Alternate("Am"), Is.EqualTo("Am"), "only listed chords change with the curl");
            Assert.That(s.GestureBase("Bb"), Is.EqualTo("F"));
            Assert.That(s.FingersFor("Bb"), Is.EqualTo(3));
            Assert.That(s.NeedsAlternate("Bb"), Is.True);
            Assert.That(s.NeedsAlternate("Dm"), Is.False);
            // Songs without a map keep the standard gestures.
            var std = Song.Parse("t", "{\"title\":\"T\",\"bpm\":90,\"meter\":4,\"chords\":\"G\",\"lyrics\":[\"la:4\"]}");
            Assert.That(std.ChordForFingers(3), Is.EqualTo("Em"));
            Assert.That(std.Alternate("Em"), Is.EqualTo("E"));
        }

        [Test]
        public void BackingWavParsesToStereoFloats()
        {
            // 3 frames, mono 16-bit, 8 kHz
            var ms = new System.IO.MemoryStream();
            var w = new System.IO.BinaryWriter(ms);
            short[] pcm = { 0, 16384, -32768 };
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + pcm.Length * 2);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(8000); w.Write(16000); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(pcm.Length * 2);
            foreach (var v in pcm) w.Write(v);
            var b = BackingAudio.Parse(ms.ToArray());
            Assert.That(b.Rate, Is.EqualTo(8000));
            Assert.That(b.Frames, Is.EqualTo(3));
            Assert.That(b.Data[2], Is.EqualTo(0.5f).Within(1e-4));
            Assert.That(b.Data[3], Is.EqualTo(0.5f).Within(1e-4), "mono is copied to both sides");
            Assert.That(b.Data[4], Is.EqualTo(-1f).Within(1e-4));
        }

        static float Rms(float[] b, int start, int n)
        {
            double s = 0;
            for (int i = start; i < start + n; i++) s += b[i] * b[i];
            return (float)System.Math.Sqrt(s / n);
        }
    }
}
