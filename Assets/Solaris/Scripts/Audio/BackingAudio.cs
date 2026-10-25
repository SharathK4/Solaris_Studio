using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Solaris.Audio
{
    /// <summary>
    /// A song's backing recording, decoded once from a 16-bit PCM WAV into interleaved stereo floats
    /// so the audio thread can play it sample-locked to the chart (no AudioSource drift).
    /// </summary>
    public sealed class BackingAudio
    {
        public float[] Data;       // interleaved L R
        public int Frames;
        public int Rate;

        static readonly Dictionary<string, BackingAudio> s_Cache = new Dictionary<string, BackingAudio>();

        public static BackingAudio Load(string path)
        {
            if (s_Cache.TryGetValue(path, out var cached)) return cached;
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[Solaris] Backing track not found: {path}");
                return null;
            }
            try
            {
                var b = Parse(File.ReadAllBytes(path));
                s_Cache[path] = b;
                Debug.Log($"[Solaris] Backing {Path.GetFileName(path)}: {b.Frames / (float)b.Rate:0.0}s at {b.Rate} Hz");
                return b;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Solaris] Could not read backing {path}: {e.Message}");
                return null;
            }
        }

        public static BackingAudio Parse(byte[] bytes)
        {
            if (bytes.Length < 44 || bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F')
                throw new FormatException("not a RIFF/WAV file");
            int channels = 0, rate = 0, bits = 0, pos = 12;
            while (pos + 8 <= bytes.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
                int size = BitConverter.ToInt32(bytes, pos + 4);
                int body = pos + 8;
                if (id == "fmt ")
                {
                    int format = BitConverter.ToInt16(bytes, body);
                    channels = BitConverter.ToInt16(bytes, body + 2);
                    rate = BitConverter.ToInt32(bytes, body + 4);
                    bits = BitConverter.ToInt16(bytes, body + 14);
                    if (format != 1 || bits != 16) throw new FormatException("backing must be 16-bit PCM");
                }
                else if (id == "data")
                {
                    if (channels < 1) throw new FormatException("fmt chunk missing");
                    size = Math.Min(size, bytes.Length - body);
                    int frames = size / (2 * channels);
                    var data = new float[frames * 2];
                    for (int f = 0; f < frames; f++)
                    {
                        int o = body + f * 2 * channels;
                        float l = BitConverter.ToInt16(bytes, o) / 32768f;
                        float r = channels > 1 ? BitConverter.ToInt16(bytes, o + 2) / 32768f : l;
                        data[2 * f] = l;
                        data[2 * f + 1] = r;
                    }
                    return new BackingAudio { Data = data, Frames = frames, Rate = rate };
                }
                pos = body + size + (size & 1);
            }
            throw new FormatException("data chunk missing");
        }
    }
}
