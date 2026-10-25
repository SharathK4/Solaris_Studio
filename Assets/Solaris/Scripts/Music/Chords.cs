using System.Collections.Generic;

namespace Solaris.Music
{
    /// <summary>An open-position guitar chord: frets per string (low E -> high E), -1 = muted.</summary>
    public sealed class ChordShape
    {
        public readonly string Name;
        public readonly int[] Frets;
        public readonly int Root;          // MIDI note of the root (for bass/pad)
        public readonly bool Minor;
        public readonly string Mood;
        public int Index { get; internal set; }

        public ChordShape(string name, int[] frets, int root, bool minor, string mood)
        {
            Name = name;
            Frets = frets;
            Root = root;
            Minor = minor;
            Mood = mood;
        }

        /// <summary>MIDI note for a string (0 = low E) or -1 when muted.</summary>
        public int Note(int s) => Frets[s] < 0 ? -1 : Chords.OpenStrings[s] + Frets[s];

        /// <summary>Root, third, fifth as MIDI notes in the pad register (around middle C).</summary>
        public int[] Triad()
        {
            int r = 60 + ((Root % 12) + 12) % 12;
            if (r > 65) r -= 12;
            return new[] { r, r + (Minor ? 3 : 4), r + 7 };
        }
    }

    public static class Chords
    {
        /// <summary>Standard tuning, MIDI: E2 A2 D3 G3 B3 E4.</summary>
        public static readonly int[] OpenStrings = { 40, 45, 50, 55, 59, 64 };

        /// <summary>The five gesture chords, in finger-count order (1..5).</summary>
        public static readonly string[] GestureChords = { "C", "D", "Em", "G", "Am" };

        static readonly List<ChordShape> s_All = new List<ChordShape>();
        static readonly Dictionary<string, ChordShape> s_ByName = new Dictionary<string, ChordShape>();

        static Chords()
        {
            Add("C", new[] { -1, 3, 2, 0, 1, 0 }, 48, false, "A bright and happy sound. Great for starting the song.");
            Add("D", new[] { -1, -1, 0, 2, 3, 2 }, 50, false, "A crisp and uplifting sound. Adds forward movement.");
            Add("Em", new[] { 0, 2, 2, 0, 0, 0 }, 40, true, "A soft and emotional sound. Works well for calmer parts.");
            Add("G", new[] { 3, 2, 0, 0, 0, 3 }, 43, false, "A full and rich sound. Makes the music feel open and big.");
            Add("Am", new[] { -1, 0, 2, 2, 1, 0 }, 45, true, "A warm and mellow sound. Perfect for heartfelt lyrics.");
            // Major/minor twins reached with the expression hand.
            Add("Cm", new[] { -1, 3, 5, 5, 4, 3 }, 48, true, "Moody twin of C.");
            Add("Dm", new[] { -1, -1, 0, 2, 3, 1 }, 50, true, "Moody twin of D.");
            Add("E", new[] { 0, 2, 2, 1, 0, 0 }, 40, false, "Bright twin of Em.");
            Add("Gm", new[] { 3, 5, 5, 3, 3, 3 }, 43, true, "Moody twin of G.");
            Add("A", new[] { -1, 0, 2, 2, 2, 0 }, 45, false, "Bright twin of Am.");
            // Used by songs that bring their own gesture map (e.g. Channa Mereya in A minor).
            Add("F", new[] { 1, 3, 3, 2, 1, 1 }, 41, false, "Warm and wide; the lift in a minor key.");
            Add("Bb", new[] { -1, 1, 3, 3, 3, 1 }, 46, false, "A soaring borrowed chord.");
        }

        static void Add(string name, int[] frets, int root, bool minor, string mood)
        {
            var c = new ChordShape(name, frets, root, minor, mood) { Index = s_All.Count };
            s_All.Add(c);
            s_ByName[name] = c;
        }

        public static IReadOnlyList<ChordShape> All => s_All;

        public static ChordShape Get(string name) =>
            name != null && s_ByName.TryGetValue(name, out var c) ? c : null;

        public static bool IsKnown(string name) => name != null && s_ByName.ContainsKey(name);

        /// <summary>1..5 fingers -> gesture chord name; anything else -> null (mute).</summary>
        public static string FromFingers(int fingers) =>
            fingers >= 1 && fingers <= 5 ? GestureChords[fingers - 1] : null;

        /// <summary>How many fingers select this chord (its major/minor twin counts too), or 0.</summary>
        public static int FingersFor(string chord)
        {
            if (chord == null) return 0;
            string b = Base(chord);
            for (int i = 0; i < GestureChords.Length; i++)
                if (GestureChords[i] == b) return i + 1;
            return 0;
        }

        /// <summary>The gesture chord this chord is a twin of (Cm -> C, E -> Em, A -> Am).</summary>
        public static string Base(string chord) => chord switch
        {
            "Cm" => "C",
            "Dm" => "D",
            "E" => "Em",
            "Gm" => "G",
            "A" => "Am",
            _ => chord,
        };

        /// <summary>Major/minor twin (C &lt;-&gt; Cm, Em &lt;-&gt; E, ...).</summary>
        public static string Flip(string chord) => chord switch
        {
            "C" => "Cm", "Cm" => "C",
            "D" => "Dm", "Dm" => "D",
            "Em" => "E", "E" => "Em",
            "G" => "Gm", "Gm" => "G",
            "Am" => "A", "A" => "Am",
            _ => chord,
        };

        /// <summary>Display split: "Em" -> ("E", "m").</summary>
        public static (string letter, string quality) Split(string chord)
        {
            if (string.IsNullOrEmpty(chord)) return ("", "");
            return (chord.Substring(0, 1), chord.Length > 1 ? chord.Substring(1) : "");
        }
    }
}
