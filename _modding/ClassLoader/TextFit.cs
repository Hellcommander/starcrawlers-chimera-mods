using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// Text that can't fit shrinks instead of being cut off or running off the screen, in the game's own panels as well
    /// as the mod's. A text cut short in its box (an ellipsis or a truncated line) turns on TextMeshPro's auto-size so it
    /// fits its box; a text whose letters run past the screen edge steps its font down until they don't. Both stop at
    /// half the text's own size (never under 8), and a shrunk text gets its size back when its words change.
    /// </summary>
    internal static class TextFit
    {
        internal static ConfigEntry<bool> Enabled;

        private const float MinScale = 0.5f, MinSize = 8f, Step = 0.9f, Slack = 2f;

        private class State
        {
            public float baseSize;  // the size the text had before we touched it
            public float ourSize;   // the size we last set (someone else changing it resets us)
            public string text;     // the words we shrank for
        }

        private static readonly ConditionalWeakTable<TMP_Text, State> states = new ConditionalWeakTable<TMP_Text, State>();
        // a rebuild can't dirty a graphic again, so fixes wait for the next Update
        private enum Fix { Fit, Shrink, Restore }
        private static readonly Dictionary<TextMeshProUGUI, Fix> pending = new Dictionary<TextMeshProUGUI, Fix>();
        private static readonly List<KeyValuePair<TextMeshProUGUI, Fix>> work = new List<KeyValuePair<TextMeshProUGUI, Fix>>();
        private static readonly Vector3[] corners = new Vector3[4];

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Interface", "ShrinkTextToFit", true, "Text that doesn't fit its box, or runs off the edge of the screen, shrinks (down to half size) instead of being cut off. Off: text is cut or spills as in the base game.");
        }

        internal static void Tick()
        {
            if (pending.Count == 0) return;
            work.Clear();
            work.AddRange(pending);
            pending.Clear();
            foreach (var kv in work)
            {
                TextMeshProUGUI t = kv.Key;
                if (t == null) continue;
                State s = states.GetValue(t, _ => new State { baseSize = t.fontSize, ourSize = -1f });
                if (s.ourSize >= 0f && !Mathf.Approximately(t.fontSize, s.ourSize)) s.baseSize = t.fontSize;
                if (kv.Value == Fix.Restore) { t.fontSize = s.baseSize; s.ourSize = -1f; s.text = null; continue; }
                float floor = Mathf.Max(MinSize, s.baseSize * MinScale);
                if (kv.Value == Fix.Shrink) // off the screen: one step smaller, checked again after the next rebuild
                {
                    if (t.fontSize <= floor + 0.01f) continue;
                    t.fontSize = s.ourSize = Mathf.Max(floor, t.fontSize * Step);
                    s.text = t.text;
                }
                else if (!t.enableAutoSizing) // cut short in its box: let TextMeshPro fit it
                {
                    t.fontSizeMax = s.baseSize;
                    t.fontSizeMin = floor;
                    t.enableAutoSizing = true;
                }
            }
        }

        /// <summary>A text we stepped down has new words: give it its size back and look again.</summary>
        private static bool Reworded(TextMeshProUGUI t) =>
            states.TryGetValue(t, out State s) && s.ourSize >= 0f && s.text != null && s.text != t.text && Mathf.Approximately(t.fontSize, s.ourSize);

        private static bool OffScreen(TextMeshProUGUI t)
        {
            Canvas c = t.canvas;
            if (c == null || string.IsNullOrEmpty(t.text)) return false;
            Canvas root = c.rootCanvas;
            if (root.renderMode == RenderMode.WorldSpace) return false;
            Camera cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            Bounds b = t.textBounds;
            if (b.size.x <= 0f && b.size.y <= 0f) return false;

            RectTransform rt = t.rectTransform;
            float w = Screen.width, h = Screen.height;
            // a panel parked off the screen (how menus hide) isn't text running off it
            Vector2 mid = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
            if (mid.x < 0f || mid.x > w || mid.y < 0f || mid.y > h) return false;

            Vector3 lo = b.min, hi = b.max;
            corners[0] = new Vector3(lo.x, lo.y); corners[1] = new Vector3(lo.x, hi.y);
            corners[2] = new Vector3(hi.x, hi.y); corners[3] = new Vector3(hi.x, lo.y);
            bool out_ = false;
            foreach (Vector3 p in corners)
            {
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(p));
                if (sp.x < -Slack || sp.x > w + Slack || sp.y < -Slack || sp.y > h + Slack) { out_ = true; break; }
            }
            // scrolled or masked content leaves the screen on purpose
            return out_ && t.GetComponentInParent<RectMask2D>() == null && t.GetComponentInParent<Mask>() == null;
        }

        [HarmonyPatch(typeof(TextMeshProUGUI), nameof(TextMeshProUGUI.Rebuild))]
        private static class Patch_Rebuild
        {
            private static void Postfix(TextMeshProUGUI __instance, CanvasUpdate update)
            {
                if (update != CanvasUpdate.PreRender || Enabled == null || !Enabled.Value || __instance == null) return;
                if (Reworded(__instance)) pending[__instance] = Fix.Restore;
                else if (OffScreen(__instance)) pending[__instance] = Fix.Shrink;
                else if (__instance.isTextTruncated && !__instance.enableAutoSizing && !pending.ContainsKey(__instance)) pending[__instance] = Fix.Fit;
            }
        }
    }
}
