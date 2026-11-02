// An example to start your own plugin from: use, copy and change it for any purpose, with no conditions and no credit needed.

using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RealtimeWithPause
{
    /// <summary>What the clock is doing: a tint and a big PAUSED while paused, otherwise a thin bar filling up until you can act again, and the waiting orders under it. Beside the title, a button that pauses and resumes.</summary>
    internal sealed class Overlay
    {
        private static readonly Color Amber = new Color(1f, 0.76f, 0.29f);

        private GameObject root;
        private GameObject tint;
        private TextMeshProUGUI title, list, buttonText;
        private RectTransform fill, button;

        /// <summary>The pointer is on the button, so the click is the button's and not the world's.</summary>
        internal bool OverButton => root.activeSelf && button.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(button, Input.mousePosition, null);

        internal static Overlay Make(System.Action onPause)
        {
            var o = new Overlay { root = new GameObject("RealtimeWithPause_Overlay") };
            Object.DontDestroyOnLoad(o.root);
            Canvas c = o.root.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 400;
            CanvasScaler s = o.root.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920f, 1080f);
            o.root.AddComponent<GraphicRaycaster>();

            Image tint = o.Rect<Image>("Tint", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            tint.color = new Color(0.05f, 0.09f, 0.16f, 0.32f);
            tint.raycastTarget = false;
            o.tint = tint.gameObject;

            TMP_FontAsset font = Menu_HUD.instance != null ? Menu_HUD.instance.GetComponentInChildren<TMP_Text>(true)?.font : null;
            o.title = o.Label("Title", font, 30f, -128f, 44f);
            o.list = o.Label("Orders", font, 21f, -188f, 34f);

            Image back = o.Rect<Image>("Bar", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-150f, -172f), new Vector2(150f, -166f));
            back.color = new Color(0f, 0f, 0f, 0.6f);
            back.raycastTarget = false;
            Image fill = Object.Instantiate(back, back.transform);
            fill.color = Amber;
            o.fill = fill.rectTransform;
            o.fill.anchorMin = Vector2.zero;
            o.fill.anchorMax = Vector2.one;
            o.fill.offsetMin = o.fill.offsetMax = Vector2.zero;

            Image plate = o.Rect<Image>("PauseButton", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(170f, -170f), new Vector2(310f, -128f));
            plate.color = new Color(0.05f, 0.09f, 0.16f, 0.85f);
            o.button = plate.rectTransform;
            Button click = plate.gameObject.AddComponent<Button>();
            click.targetGraphic = plate;
            click.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = click.colors;
            colors.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            click.colors = colors;
            click.onClick.AddListener(() =>
            {
                onPause();
                // it mustn't stay selected, or Space and Enter would press it again
                EventSystem.current?.SetSelectedGameObject(null);
            });
            o.buttonText = o.Label("PauseLabel", font, 20f, 0f, 0f);
            o.buttonText.rectTransform.SetParent(plate.transform, false);
            o.buttonText.rectTransform.anchorMin = Vector2.zero;
            o.buttonText.rectTransform.anchorMax = Vector2.one;
            o.buttonText.rectTransform.offsetMin = o.buttonText.rectTransform.offsetMax = Vector2.zero;
            return o;
        }

        private T Rect<T>(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            T t = go.AddComponent<T>();
            RectTransform r = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
            r.anchorMin = anchorMin;
            r.anchorMax = anchorMax;
            r.offsetMin = offsetMin;
            r.offsetMax = offsetMax;
            return t;
        }

        private TextMeshProUGUI Label(string name, TMP_FontAsset font, float size, float y, float height)
        {
            TextMeshProUGUI t = Rect<TextMeshProUGUI>(name, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-700f, y - height), new Vector2(700f, y));
            if (font != null) t.font = font;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            return t;
        }

        internal void Show(bool paused, bool finishing, bool held, float next, List<string> orders, KeyCode key, bool withButton)
        {
            root.SetActive(true);
            button.gameObject.SetActive(withButton);
            buttonText.text = $"{(paused ? "Resume" : "Pause")} <color=#BBBBBB>({key})</color>";
            tint.SetActive(paused && !finishing);
            title.color = paused ? Amber : Color.white;
            title.text = finishing ? "PAUSING..." : paused ? "PAUSED" : held ? "REALTIME <size=70%>(holding)</size>" : "REALTIME";
            fill.anchorMax = new Vector2(next, 1f);
            fill.parent.gameObject.SetActive(!paused);
            string plan = orders.Count > 0 ? "Orders: " + string.Join(", ", orders.Select(o => "<b>" + o + "</b>")) : paused ? "No orders yet" : "";
            list.text = paused && !finishing ? plan + $"   <color=#BBBBBB>{key} resumes</color>" : plan;
        }

        internal void Hide()
        {
            if (root.activeSelf) root.SetActive(false);
        }
    }
}
