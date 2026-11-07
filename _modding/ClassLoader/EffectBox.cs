using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// A collapse tab on the game's own effect icon columns (Menu_HUD.effectContainer and debuffContainer), which grow down
    /// the screen and over the hotbar. Folded, the icons are hidden and the tab counts them: buffs in green, debuffs in red.
    /// The icons stay where they are behind a CanvasGroup: the game keeps adding to them and needs them active to do so.
    /// With EffectShrink on, a column longer than EffectShrinkFrom icons shrinks both columns (their shared box, about its
    /// top-right anchor) so the longest stays about that long, down to EffectShrinkMin; the tab keeps its own size.
    /// </summary>
    internal static class EffectBox
    {
        internal const string Key = "EffectBox";
        private const float Gap = 10f, TabHeight = 22f, TabScale = 1.75f; // the icons beside it are big, and a tab this wordy is unreadable at the usual size
        private const float ShrinkSpeed = 6f; // per second, toward the new size, so icons ease rather than snap

        private static ConfigEntry<bool> shrink;
        private static ConfigEntry<int> shrinkFrom, shrinkMin;

        internal static void Bind(ConfigFile cfg)
        {
            shrink = cfg.Bind("Interface", "EffectShrink", true, "The more effect icons (buffs and debuffs) you have, the smaller they all get, so a long list doesn't run down the screen. Off keeps them at full size.");
            shrinkFrom = cfg.Bind("Interface", "EffectShrinkFrom", 8, new ConfigDescription("How many icons a column holds at full size. Past this the icons shrink so the longest column stays about this long.", new AcceptableValueRange<int>(3, 30)));
            shrinkMin = cfg.Bind("Interface", "EffectShrinkMin", 50, new ConfigDescription("The smallest the effect icons get, in percent of their full size.", new AcceptableValueRange<int>(25, 100)));
        }

        /// <summary>How big the icons should be with `longest` icons in the longest column.</summary>
        internal static float ShrinkFor(int longest)
        {
            if (shrink == null || !shrink.Value || longest <= shrinkFrom.Value) return 1f;
            return Mathf.Clamp((float)shrinkFrom.Value / longest, shrinkMin.Value / 100f, 1f);
        }

        internal class Box : MonoBehaviour
        {
            public Transform buffs, debuffs;
            /// <summary>The game's draggable box holding both columns (and this tab): what shrinks.</summary>
            public Transform icons;
            public CanvasGroup buffGroup, debuffGroup;
            public UiLook.Look look;
            public GameObject content;

            /// <summary>What the tab counts, the live icons only (a removed one lasts until the end of its frame).</summary>
            internal static int Count(Transform column)
            {
                int n = 0;
                foreach (Transform t in column) if (t.gameObject.activeSelf && t.GetComponent<Effect_Icon>()?.effect != null) n++;
                return n;
            }

            /// <summary>The game just drew the icons, which can land after this looked at the same frame's change.</summary>
            internal static bool Drawn;

            /// <summary>The icons' places come out of the layout pass, after this runs, so one look isn't enough: keep looking until the tab holds still.</summary>
            private const int SettleFrames = 4;

            private EventBus.Watch watch;
            private int up, down, placeFor;
            private Vector3 lastBuffs, lastDebuffs;

            private void LateUpdate()
            {
                if (Drawn | !EventBus.Unchanged(ref watch, EventBus.Effects))
                {
                    Drawn = false;
                    up = Count(buffs);
                    down = Count(debuffs);
                    look.title = "Effects" + (up > 0 ? $" <color=#9FD99F>{up}</color>" : "") + (down > 0 ? $" <color=#FF7272>{down}</color>" : "");
                    placeFor = SettleFrames;
                }
                Shrink();
                // the game switches the columns off in HUD edit mode, and with no icons there is nothing to fold
                bool any = up + down > 0 && buffs.gameObject.activeInHierarchy;
                if (content.activeSelf != any) content.SetActive(any);
                bool hide = look.Folded;
                Show(buffGroup, !hide);
                Show(debuffGroup, !hide);
                if (buffs.position != lastBuffs || debuffs.position != lastDebuffs)
                {
                    lastBuffs = buffs.position;
                    lastDebuffs = debuffs.position;
                    placeFor = SettleFrames;
                }
                if (placeFor <= 0) return;
                placeFor--;
                if (!any) return;
                Vector3 was = transform.position;
                // an icon the game just added still sits where it was dropped: its layout groups (the column's and the row's,
                // each with a size fitter) only place it in the canvas update after this, so the tab would land beside the
                // old spot for a frame and then jump. Running that update now puts everything where the frame will show it.
                Canvas.ForceUpdateCanvases();
                Place();
                if (transform.position != was) placeFor = SettleFrames;
            }

            /// <summary>Eases the box toward its size for the longest column; this tab is inside it, so it scales back up to stay put.</summary>
            private void Shrink()
            {
                float target = ShrinkFor(Mathf.Max(up, down)), now = icons.localScale.x;
                if (Mathf.Approximately(now, target)) return;
                float next = Mathf.MoveTowards(now, target, ShrinkSpeed * Time.unscaledDeltaTime * Mathf.Max(0.15f, Mathf.Abs(target - now)));
                if (Mathf.Abs(next - target) < 0.002f) next = target;
                icons.localScale = new Vector3(next, next, 1f);
                transform.localScale = new Vector3(TabScale / next, TabScale / next, 1f);
                placeFor = SettleFrames; // the icons moved under the tab
            }

            private static void Show(CanvasGroup g, bool on)
            {
                if (g.alpha == (on ? 1f : 0f)) return;
                g.alpha = on ? 1f : 0f;
                g.blocksRaycasts = g.interactable = on;
            }

            /// <summary>Beside the top of the leftmost column, wherever the player dragged the columns to.</summary>
            private void Place()
            {
                var corners = new Vector3[4];
                float left = float.MaxValue, top = float.MinValue;
                foreach (Transform column in new[] { buffs, debuffs })
                    foreach (Transform t in column)
                    {
                        ((RectTransform)t).GetWorldCorners(corners);
                        left = Mathf.Min(left, corners[0].x);
                        top = Mathf.Max(top, corners[1].y);
                    }
                float s = transform.lossyScale.y;
                top = Mathf.Min(top, Screen.height); // a column too long for the screen is pushed up until its foot shows, which takes its head off the top
                transform.position = new Vector3(left - Gap * s, top - TabHeight * s, transform.position.z);
            }
        }

        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.UpdatePlayerEffects))]
        private static class Patch_Drawn
        {
            private static void Postfix(bool __runOriginal)
            {
                if (__runOriginal) Box.Drawn = true;
            }
        }

        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.Awake))]
        private static class Patch_Awake
        {
            private static void Postfix(Menu_HUD __instance)
            {
                try
                {
                    if (__instance.effectContainer == null || __instance.debuffContainer == null) return;
                    RectTransform rt = UiKit.Rect(__instance.effectContainer.parent, "SCCL_EffectBox", Vector2.zero);
                    rt.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
                    var box = rt.gameObject.AddComponent<Box>();
                    box.buffs = __instance.effectContainer;
                    box.debuffs = __instance.debuffContainer;
                    box.icons = __instance.effectContainer.parent;
                    box.buffGroup = box.buffs.GetComponent<CanvasGroup>() ?? box.buffs.gameObject.AddComponent<CanvasGroup>();
                    box.debuffGroup = box.debuffs.GetComponent<CanvasGroup>() ?? box.debuffs.gameObject.AddComponent<CanvasGroup>();
                    rt.localScale = new Vector3(TabScale, TabScale, 1f);
                    RectTransform shown = UiKit.Rect(rt, "Shown", Vector2.zero); // holds the tab, so off takes it away
                    box.content = shown.gameObject;
                    UiLook.Attach(rt, Key, shown);
                    box.look = rt.GetComponent<UiLook.Look>();
                    box.look.noGrip = true; // the icons are the game's: a grip would only size the tab
                    UiLook.Collapsible(rt, "Effects", UiLook.Corner.AboveRight, true);
                }
                catch (Exception e) { Plugin.Log.LogError("Effect box: " + e); }
            }
        }
    }
}
