using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Solaris.UI
{
    /// <summary>Terse builders for code-constructed uGUI.</summary>
    public static class UIKit
    {
        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Anchor + pivot at the same normalised point, then offset and size in canvas px.</summary>
        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
            return rt;
        }

        public static RectTransform Center(this RectTransform rt, Vector2 position, Vector2 size) =>
            rt.Place(new Vector2(0.5f, 0.5f), position, size);

        public static TextMeshProUGUI Text(Transform parent, string text, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center, float tracking = 0f, string name = "Text")
        {
            var rt = Node(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.characterSpacing = tracking;
            t.text = text;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.extraPadding = true;
            return t;
        }

        public static RawImage Raw(Transform parent, Texture texture, Material material = null, string name = "Raw")
        {
            var rt = Node(name, parent);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = texture;
            img.material = material;
            img.raycastTarget = false;
            return img;
        }

        public static ShapeGraphic Shape(Transform parent, Color fill, float radius, string name = "Shape")
        {
            var rt = Node(name, parent);
            var s = rt.gameObject.AddComponent<ShapeGraphic>();
            s.color = fill;
            s.Radius = radius;
            s.raycastTarget = false;
            return s;
        }

        public static VectorIcon Icon(Transform parent, VectorIcon.Kind kind, Color color, float size, float stroke = 2f)
        {
            var rt = Node(kind + "Icon", parent);
            rt.sizeDelta = new Vector2(size, size);
            var i = rt.gameObject.AddComponent<VectorIcon>();
            i.Shape = kind;
            i.Stroke = stroke;
            i.color = color;
            i.raycastTarget = false;
            return i;
        }

        public static CanvasGroup Group(this Component c)
        {
            if (!c.TryGetComponent(out CanvasGroup g)) g = c.gameObject.AddComponent<CanvasGroup>();
            return g;
        }

        /// <summary>Canvas-space position of a normalised viewport point (0..1, y up) relative to a rect's centre.</summary>
        public static Vector2 ViewportToLocal(RectTransform space, Vector2 viewport)
        {
            var r = space.rect;
            return new Vector2(r.xMin + viewport.x * r.width, r.yMin + viewport.y * r.height);
        }
    }
}
