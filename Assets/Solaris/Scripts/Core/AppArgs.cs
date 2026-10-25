using System;
using System.Globalization;

namespace Solaris
{
    /// <summary>
    /// Command-line switches used by automated tests and demos, e.g.
    /// <c>SolarisStudio.exe -solaris-screen stage -solaris-camera-image TestData/vsign_man.jpg -solaris-capture out.png</c>
    /// </summary>
    public static class AppArgs
    {
        static string[] s_Args;

        static string[] Args => s_Args ??= Environment.GetCommandLineArgs();

        public static bool Has(string flag) => Array.IndexOf(Args, flag) >= 0;

        public static string Get(string flag, string fallback = null)
        {
            int i = Array.IndexOf(Args, flag);
            return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : fallback;
        }

        public static float GetFloat(string flag, float fallback) =>
            float.TryParse(Get(flag), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public static string StartScreen => Get("-solaris-screen");
        public static string CapturePath => Get("-solaris-capture");
        public static float CaptureDelay => GetFloat("-solaris-delay", 4f);
        public static string CameraImage => Get("-solaris-camera-image");
        public static string CameraName => Get("-solaris-camera");
        public static string Song => Get("-solaris-song");
        public static string VisionSelfTest => Get("-solaris-selftest-vision");
        public static bool Autoplay => Has("-solaris-autoplay");
        /// <summary>Test switch: hold this chord for the whole song (e.g. to hear a wrong chord against a record).</summary>
        public static string HoldChord => Get("-solaris-hold");
        public static bool DebugHud => Has("-solaris-debug");

        /// <summary>Unattended run: ignore keyboard/mouse, never open the real webcam.</summary>
        public static bool Automation => CapturePath != null || VisionSelfTest != null;

        /// <summary>For tests: lets the editor inject args.</summary>
        public static void Override(params string[] args) => s_Args = args;
    }
}
