using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Solaris.Music
{
    public struct LyricWord
    {
        public string Text;
        public float Beat;      // song beats; 0 = first full bar, negative = pickup
        public float Length;
        public bool JoinNext;   // syllable continues into the next token (no space)
        public float End => Beat + Length;
    }

    public sealed class LyricLine
    {
        public readonly List<LyricWord> Words = new List<LyricWord>();
        public float Start => Words.Count > 0 ? Words[0].Beat : 0;
        public float End => Words.Count > 0 ? Words[Words.Count - 1].End : 0;
        public string Text
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                foreach (var w in Words)
                {
                    sb.Append(w.Text);
                    if (!w.JoinNext) sb.Append(' ');
                }
                return sb.ToString().TrimEnd();
            }
        }
    }

    public struct ChordEvent
    {
        public float Beat;
        public float Length;
        public string Chord;
    }

    /// <summary>
    /// A playable chart. Authored as JSON in StreamingAssets/Songs:
    /// <code>
    /// { "title": "...", "artist": "...", "bpm": 80, "meter": 3, "pickup": 1,
    ///   "chords": "G G C G | G:2 D:1 ...",          // one token per bar, or chord:beats
    ///   "lyrics": [ "A:1 ma-:2 zing:1 grace,:2", ... ] }  // word:beats, "_" rests, "-" joins syllables
    /// </code>
    /// </summary>
    public sealed class Song
    {
        public string Id, Title, Artist, Blurb, Mood, Difficulty;
        public float Bpm = 80;
        public int Meter = 4;
        public float Pickup;
        /// <summary>False for ballads: the band keeps bass and pad but drops the kick and shaker.</summary>
        public bool Drums = true;

        /// <summary>Optional backing recording in StreamingAssets/Songs (16-bit PCM WAV) that plays in sync.</summary>
        public string Backing;
        /// <summary>Seconds into the backing file where beat 0 of the chart falls.</summary>
        public float BackingOffset;
        /// <summary>Tuning of the backing in cents (the gesture guitar is retuned to match).</summary>
        public float TuningCents;

        /// <summary>Song-specific chord per finger count (1..5); null = the standard C D Em G Am.</summary>
        public string[] Gestures;
        /// <summary>Song-specific "other hand curled" chords, e.g. F -> Bb. Null = the major/minor twins.</summary>
        public readonly Dictionary<string, string> Alternates = new Dictionary<string, string>();

        public string ChordForFingers(int fingers) =>
            Gestures == null ? Music.Chords.FromFingers(fingers) : fingers >= 1 && fingers <= Gestures.Length ? Gestures[fingers - 1] : null;

        /// <summary>The chord heard when the expression hand curls (minor / alternate).</summary>
        public string Alternate(string chord)
        {
            if (chord == null) return null;
            if (Gestures == null) return Music.Chords.Flip(chord);
            return Alternates.TryGetValue(chord, out var alt) ? alt : chord;
        }

        /// <summary>The finger-count chord a chart chord is reached from.</summary>
        public string GestureBase(string chord)
        {
            if (chord == null || Gestures == null) return Music.Chords.Base(chord);
            if (Array.IndexOf(Gestures, chord) >= 0) return chord;
            foreach (var kv in Alternates) if (kv.Value == chord) return kv.Key;
            return chord;
        }

        public int FingersFor(string chord)
        {
            if (Gestures == null) return Music.Chords.FingersFor(chord);
            int i = Array.IndexOf(Gestures, GestureBase(chord));
            return i >= 0 ? i + 1 : 0;
        }

        /// <summary>True when the chord needs the second hand (curl) on top of the finger count.</summary>
        public bool NeedsAlternate(string chord) => chord != null && GestureBase(chord) != chord;
        public readonly List<ChordEvent> Chords = new List<ChordEvent>();
        public readonly List<LyricLine> Lines = new List<LyricLine>();
        /// <summary>
        /// Optional right-hand pattern changes, sorted by bar: one character per eighth note.
        /// D / U = down / up strum, . = rest, B = pick the chord's bass string, 1..6 = pick that string
        /// (guitar numbering: 1 = high E). Bars before the first entry use the default strum.
        /// </summary>
        public readonly List<(int Bar, string Pattern)> Patterns = new List<(int, string)>();

        public float TotalBeats { get; private set; }
        public float SecondsPerBeat => 60f / Bpm;
        public float DurationSeconds => (TotalBeats + Pickup) * SecondsPerBeat;

        public IEnumerable<string> DistinctChords()
        {
            var seen = new HashSet<string>();
            foreach (var c in Chords)
                if (seen.Add(c.Chord)) yield return c.Chord;
        }

        public int ChordIndexAt(float beat)
        {
            int lo = 0, hi = Chords.Count - 1;
            if (hi < 0 || beat < 0) return -1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Chords[mid].Beat <= beat) lo = mid;
                else hi = mid - 1;
            }
            return beat < Chords[lo].Beat + Chords[lo].Length ? lo : -1;
        }

        public string ChordAt(float beat)
        {
            int i = ChordIndexAt(beat);
            return i >= 0 ? Chords[i].Chord : null;
        }

        public int LineIndexAt(float beat)
        {
            for (int i = 0; i < Lines.Count; i++)
                if (beat < Lines[i].End) return i;
            return Lines.Count - 1;
        }

        [Serializable]
        class Json
        {
            public string title, artist, blurb, mood, difficulty, chords;
            public float bpm = 80, pickup;
            public int meter = 4;
            public string[] lyrics;
            public string[] patterns;     // "bar:pattern", e.g. "0:B3231323"
            public bool drums = true;
            public string backing;
            public float backingOffset, tuningCents;
            public string[] gestures, alternates;   // alternates: "F:Bb"
        }

        public static Song Parse(string id, string json)
        {
            var j = JsonUtility.FromJson<Json>(json);
            var s = new Song
            {
                Id = id,
                Title = j.title,
                Artist = j.artist,
                Blurb = j.blurb,
                Mood = j.mood,
                Difficulty = string.IsNullOrEmpty(j.difficulty) ? "Easy" : j.difficulty,
                Bpm = j.bpm,
                Meter = Mathf.Max(2, j.meter),
                Pickup = j.pickup,
                Drums = j.drums,
                Backing = string.IsNullOrEmpty(j.backing) ? null : j.backing,
                BackingOffset = j.backingOffset,
                TuningCents = j.tuningCents,
            };
            if (j.gestures != null && j.gestures.Length > 0)
            {
                foreach (var g in j.gestures)
                    if (!Music.Chords.IsKnown(g)) throw new FormatException($"{id}: unknown gesture chord '{g}'");
                s.Gestures = j.gestures;
            }
            foreach (var a in j.alternates ?? Array.Empty<string>())
            {
                var parts = a.Split(':');
                if (parts.Length != 2 || !Music.Chords.IsKnown(parts[0]) || !Music.Chords.IsKnown(parts[1]))
                    throw new FormatException($"{id}: alternate '{a}' must be Chord:Chord");
                s.Alternates[parts[0]] = parts[1];
            }

            float beat = 0;
            foreach (var raw in (j.chords ?? "").Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw == "|") continue;
                var (name, len) = Token(raw, s.Meter);
                if (!Music.Chords.IsKnown(name)) throw new FormatException($"{id}: unknown chord '{name}'");
                s.Chords.Add(new ChordEvent { Beat = beat, Length = len, Chord = name });
                beat += len;
            }
            s.TotalBeats = beat;

            foreach (var raw in j.patterns ?? Array.Empty<string>())
            {
                int c = raw.IndexOf(':');
                if (c <= 0 || !int.TryParse(raw.Substring(0, c), NumberStyles.Integer, CultureInfo.InvariantCulture, out int bar))
                    throw new FormatException($"{id}: pattern '{raw}' must be bar:pattern");
                string pat = raw.Substring(c + 1);
                if (pat.Length != s.Meter * 2) throw new FormatException($"{id}: pattern '{pat}' needs {s.Meter * 2} steps (one per eighth)");
                foreach (char ch in pat)
                    if ("DU.B123456".IndexOf(ch) < 0) throw new FormatException($"{id}: pattern '{pat}' has unknown step '{ch}'");
                s.Patterns.Add((bar, pat));
            }
            s.Patterns.Sort((a, b) => a.Bar.CompareTo(b.Bar));

            float t = -s.Pickup;
            foreach (var line in j.lyrics ?? Array.Empty<string>())
            {
                var l = new LyricLine();
                foreach (var raw in line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (raw == "|") continue;
                    var (word, len) = Token(raw, 1);
                    if (word != "_")
                    {
                        // "syl-" joins the next syllable; "one--" keeps a real hyphen ("one-horse").
                        bool hyphen = word.EndsWith("--", StringComparison.Ordinal) && word.Length > 2;
                        bool join = hyphen || (word.EndsWith("-", StringComparison.Ordinal) && word.Length > 1);
                        string text = hyphen ? word.Substring(0, word.Length - 1) : join ? word.Substring(0, word.Length - 1) : word;
                        l.Words.Add(new LyricWord { Text = text, Beat = t, Length = len, JoinNext = join });
                    }
                    t += len;
                }
                if (l.Words.Count > 0) s.Lines.Add(l);
            }
            return s;
        }

        static (string, float) Token(string raw, float fallback)
        {
            int c = raw.LastIndexOf(':');
            if (c <= 0) return (raw, fallback);
            return (raw.Substring(0, c), float.Parse(raw.Substring(c + 1), CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Replaces chart lyric lines with your own words, one slot per chart line, in order. A slot's
        /// words are spread evenly over the first 85% of that line's time (the rest is a breath).
        /// Null or blank slots keep the chart's line (for example, chord cues in an instrumental).
        /// </summary>
        public void ApplyLyricSlots(IList<string> slots)
        {
            for (int i = 0; i < slots.Count && i < Lines.Count; i++)
            {
                var words = (slots[i] ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) continue;
                var old = Lines[i];
                float start = old.Start, len = old.End - old.Start;
                float step = len * 0.85f / words.Length;
                var line = new LyricLine();
                for (int w = 0; w < words.Length; w++)
                    line.Words.Add(new LyricWord { Text = words[w], Beat = start + w * step, Length = step });
                Lines[i] = line;
            }
        }

        /// <summary>
        /// Reads a lyrics file: lines starting with "&gt;" are slots (in order), "#" lines are comments.
        /// </summary>
        public static List<string> ReadLyricSlots(string text)
        {
            var slots = new List<string>();
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var l = raw.TrimStart();
                if (l.StartsWith(">", StringComparison.Ordinal)) slots.Add(l.Substring(1).Trim());
            }
            return slots;
        }

        /// <summary>Right-hand pattern for a bar, or null for the default strum.</summary>
        public string PatternAt(int bar)
        {
            string p = null;
            foreach (var (b, pat) in Patterns)
            {
                if (b > bar) break;
                p = pat;
            }
            return p;
        }

        /// <summary>End of the last lyric, in beats.</summary>
        public float LyricsEnd => Lines.Count > 0 ? Lines[Lines.Count - 1].End : 0;
    }

    public sealed class SongLibrary
    {
        public readonly List<Song> Songs = new List<Song>();

        public string Folder => Path.Combine(Application.streamingAssetsPath, "Songs");

        public void Load()
        {
            Songs.Clear();
            if (!Directory.Exists(Folder))
            {
                Debug.LogWarning($"[Solaris] No song folder at {Folder}");
                return;
            }
            foreach (var file in Directory.GetFiles(Folder, "*.json"))
            {
                try
                {
                    var song = Song.Parse(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
                    // Your own lyrics (not shipped): Songs/<id>.lyrics.txt, one "> line" per chart line.
                    string lyrics = Path.ChangeExtension(file, ".lyrics.txt");
                    if (File.Exists(lyrics)) song.ApplyLyricSlots(Song.ReadLyricSlots(File.ReadAllText(lyrics)));
                    Songs.Add(song);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Solaris] Skipping song {Path.GetFileName(file)}: {e.Message}");
                }
            }
            Songs.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            Debug.Log($"[Solaris] Loaded {Songs.Count} songs");
        }

        public Song Find(string idOrTitle)
        {
            foreach (var s in Songs)
                if (string.Equals(s.Id, idOrTitle, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Title, idOrTitle, StringComparison.OrdinalIgnoreCase))
                    return s;
            return Songs.Count > 0 ? Songs[0] : null;
        }
    }
}
