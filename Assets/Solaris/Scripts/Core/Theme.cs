using TMPro;
using UnityEngine;

namespace Solaris
{
    /// <summary>Brand tokens lifted from the Solaris brand sheet (colour palette + type pairing).</summary>
    public static class Theme
    {
        // Palette: "relaxing, earthy neutrals" + the ink/ember accents of the key art.
        public static readonly Color Cream = Hex("F7EFE3");
        public static readonly Color Paper = Hex("FBF6EE");
        public static readonly Color Sand = Hex("E4D6C3");
        public static readonly Color Rust = Hex("B4531D");
        public static readonly Color Sage = Hex("7E8B6A");
        public static readonly Color Umber = Hex("5A2E14");
        public static readonly Color Ink = Hex("120805");
        public static readonly Color Night = Hex("0B0503");
        public static readonly Color Ember = Hex("E6601E");
        public static readonly Color Amber = Hex("F2A546");
        public static readonly Color Blood = Hex("9E1F14");
        public static readonly Color Periwinkle = Hex("8E90F2");
        public static readonly Color Good = Hex("F2C48A");
        public static readonly Color Miss = Hex("C2533C");

        public static TMP_FontAsset Serif => SolarisAssets.Get.serif;
        public static TMP_FontAsset SerifItalic => SolarisAssets.Get.serifItalic;
        public static TMP_FontAsset SansLight => SolarisAssets.Get.sansLight;
        public static TMP_FontAsset Sans => SolarisAssets.Get.sans;
        public static TMP_FontAsset SansMedium => SolarisAssets.Get.sansMedium;
        public static TMP_FontAsset Garamond => SolarisAssets.Get.garamond != null ? SolarisAssets.Get.garamond : SolarisAssets.Get.serif;

        /// <summary>
        /// Opacity that reads like <paramref name="a"/> did in the sRGB-blended design files. The project
        /// blends in linear light, where a translucent white over dark comes out far brighter; the
        /// exponent is a compromise that matches over both the dark and the glowing parts of the art.
        /// </summary>
        public static float DesignAlpha(float a) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? Mathf.Pow(Mathf.Clamp01(a), 1.65f) : a;

        /// <summary><paramref name="c"/> at a design-file opacity (see <see cref="DesignAlpha"/>).</summary>
        public static Color Design(this Color c, float alpha) => c.A(DesignAlpha(alpha));

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        public static Color A(this Color c, float alpha)
        {
            c.a = alpha;
            return c;
        }
    }
}
