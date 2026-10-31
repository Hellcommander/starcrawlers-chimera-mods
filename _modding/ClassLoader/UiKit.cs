using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>Plain uGUI building blocks for the mod's own panels (build list, body picker).</summary>
    internal static class UiKit
    {
        internal static readonly Color PanelColor = new Color(0.03f, 0.06f, 0.09f, 0.94f);
        internal static readonly Color ButtonColor = new Color(0.08f, 0.15f, 0.2f, 0.95f);
        internal static readonly Color OnColor = new Color(0.12f, 0.38f, 0.42f, 0.95f);
        internal static readonly Color DangerColor = new Color(0.4f, 0.1f, 0.1f, 0.95f);

        internal static RectTransform Rect(Transform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            return rt;
        }

        internal static RectTransform Panel(Transform parent, string name, Vector2 size, Color color)
        {
            RectTransform rt = Rect(parent, name, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return rt;
        }

        internal static TextMeshProUGUI Label(Transform parent, string text, TMP_FontAsset font, float size, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            RectTransform rt = Rect(parent, "Text", Vector2.zero);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8f, 2f); rt.offsetMax = new Vector2(-8f, -2f);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        internal static Button Button(Transform parent, string label, TMP_FontAsset font, float fontSize, Vector2 size, Action onClick, Color? color = null)
        {
            RectTransform rt = Panel(parent, "Button", size, color ?? ButtonColor);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = rt.GetComponent<Image>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            b.colors = colors;
            Label(rt, label, font, fontSize);
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return b;
        }

        /// <summary>Places a child by its top-left corner inside a parent whose pivot is its top-left.</summary>
        internal static void At(RectTransform rt, float x, float y)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
        }

        internal static TMP_FontAsset FontFrom(Component c)
        {
            foreach (TMP_Text t in c.GetComponentsInChildren<TMP_Text>(true)) if (t.font != null) return t.font;
            foreach (TMP_Text t in UnityEngine.Object.FindObjectsOfType<TMP_Text>()) if (t.font != null) return t.font;
            return null;
        }
    }
}
