using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// The game's tooltip grows to fit its words, so a long one (a passive with many triggers, a table of outcomes) runs
    /// off the bottom of the screen instead of counting as "doesn't fit" for TextFit. When the panel is taller than the
    /// screen, its description texts step their font down until it fits, never below half their size; a tooltip with
    /// new words starts again at full size. The game's own clamp compares canvas units with screen pixels, so on a scaled
    /// canvas (a 4K screen) a tall tooltip still hangs off the top or bottom; after it places the tooltip each frame, the
    /// panel is pushed back inside the screen in pixels.
    /// </summary>
    internal static class TooltipFit
    {
        private const float MinScale = 0.5f, Room = 0.94f;

        private class State { public float baseSize; public string text; }
        private static readonly Dictionary<TextMeshProUGUI, State> states = new Dictionary<TextMeshProUGUI, State>();

        internal static void Tick()
        {
            Tooltip tip = Tooltip.instance;
            if (tip == null || !tip.IsShowing() || tip.content == null) return;
            TextMeshProUGUI[] texts = { tip.ability_description, tip.itemDescription, tip.perkDescription };

            bool reworded = false;
            foreach (TextMeshProUGUI t in texts)
            {
                if (t == null) continue;
                if (!states.TryGetValue(t, out State s)) states[t] = s = new State { baseSize = Size(t) };
                if (s.text != t.text) { s.text = t.text; SetSize(t, s.baseSize); reworded = true; }
            }
            if (reworded) return; // measure once the new words are laid out

            float h = ScreenHeight(tip.content), room = Screen.height * Room;
            if (h <= room) return;
            float k = Mathf.Sqrt(room / h) * 0.98f; // wrapped text's height goes roughly with the square of its size
            bool shrunk = false;
            foreach (TextMeshProUGUI t in texts)
            {
                if (t == null || !t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) continue;
                State s = states[t];
                float floor = s.baseSize * MinScale, now = Size(t);
                if (now <= floor + 0.01f) continue;
                SetSize(t, Mathf.Max(floor, now * k));
                shrunk = true;
            }
            if (shrunk) LayoutRebuilder.ForceRebuildLayoutImmediate(tip.content);
        }

        [HarmonyPatch(typeof(Tooltip), "OnGUI")]
        private static class Patch_Place
        {
            private static void Postfix(Tooltip __instance)
            {
                if (Event.current == null || Event.current.type != EventType.Repaint) return;
                if (__instance == null || !__instance.IsShowing() || __instance.content == null) return;
                RectTransform rt = __instance.content;
                Canvas root = rt.GetComponentInParent<Canvas>()?.rootCanvas;
                if (root == null) return;
                Camera cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
                rt.GetWorldCorners(corners);
                Vector2 lo = RectTransformUtility.WorldToScreenPoint(cam, corners[0]), hi = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                float dy = hi.y > Screen.height ? Screen.height - hi.y : lo.y < 0f ? -lo.y : 0f;
                float dx = hi.x > Screen.width ? Screen.width - hi.x : lo.x < 0f ? -lo.x : 0f;
                if (dx == 0f && dy == 0f) return;
                var parent = rt.parent as RectTransform;
                if (parent == null) return;
                Vector2 mid = (lo + hi) * 0.5f;
                if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, mid, cam, out Vector3 a0)
                    && RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, mid + new Vector2(dx, dy), cam, out Vector3 a1))
                    rt.position += a1 - a0;
            }
        }

        private static float Size(TextMeshProUGUI t) => t.enableAutoSizing ? t.fontSizeMax : t.fontSize;

        private static void SetSize(TextMeshProUGUI t, float size)
        {
            if (t.enableAutoSizing) t.fontSizeMax = size;
            t.fontSize = size;
        }

        private static readonly Vector3[] corners = new Vector3[4];

        private static float ScreenHeight(RectTransform rt)
        {
            Canvas root = rt.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera cam = root == null || root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            rt.GetWorldCorners(corners);
            return RectTransformUtility.WorldToScreenPoint(cam, corners[1]).y - RectTransformUtility.WorldToScreenPoint(cam, corners[0]).y;
        }
    }
}
