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

        /// <summary>A one-line text box with greyed placeholder text. It stays out of controller navigation.</summary>
        internal static TMP_InputField TextBox(Transform parent, string placeholder, TMP_FontAsset font, float fontSize, Vector2 size)
        {
            RectTransform rt = Panel(parent, "TextBox", size, ButtonColor);
            rt.gameObject.SetActive(false); // the field has to be wired up before its OnEnable builds the caret
            RectTransform area = Rect(rt, "Text Area", Vector2.zero);
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(10f, 2f); area.offsetMax = new Vector2(-10f, -2f);
            area.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI text = Label(area, "", font, fontSize, TextAlignmentOptions.Left);
            TextMeshProUGUI hint = Label(area, placeholder, font, fontSize, TextAlignmentOptions.Left);
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            hint.rectTransform.offsetMin = hint.rectTransform.offsetMax = Vector2.zero;
            hint.color = new Color(0.62f, 0.72f, 0.79f, 0.8f);

            var box = rt.gameObject.AddComponent<TMP_InputField>();
            box.textViewport = area;
            box.textComponent = text;
            box.placeholder = hint;
            box.targetGraphic = rt.GetComponent<Image>();
            box.lineType = TMP_InputField.LineType.SingleLine;
            box.caretColor = Color.white;
            box.customCaretColor = true;
            box.selectionColor = new Color(0.45f, 0.85f, 1f, 0.35f);
            var nav = box.navigation;
            nav.mode = Navigation.Mode.None;
            box.navigation = nav;
            rt.gameObject.SetActive(true);
            return box;
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
