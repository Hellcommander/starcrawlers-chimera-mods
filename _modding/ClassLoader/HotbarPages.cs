using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SCClassLoader
{
    /// <summary>
    /// Swappable hotbar pages. The game's 21 slots show one page at a time; switching stores the visible slots as
    /// that page and loads the next one into them. Page 1 is what the save's own hotbar field holds, so saves still
    /// load without the mod; the other pages and the current page number live in the mod's save-file slot.
    /// </summary>
    internal static class HotbarPages
    {
        internal static ConfigEntry<int> PageCount;
        internal static ConfigEntry<KeyCode> NextKey, PrevKey, NextKey2, PrevKey2;
        internal static ConfigEntry<bool> MouseWheel;

        /// <summary>Devtest: a key that counts as pressed this frame.</summary>
        internal static KeyCode? TestKey;
        private static bool Down(KeyCode k) => k != KeyCode.None && (TestKey == k || Input.GetKeyDown(k));

        private static bool swapping;
        /// <summary>True while this mod is the one loading slots (page flips, transformations).</summary>
        internal static bool Swapping => swapping;
        private static TextMeshProUGUI label;

        private static int Count => Mathf.Clamp(Tweaks.Int("hotbarPages", PageCount.Value), 1, 10);

        internal static void Bind(ConfigFile cfg)
        {
            PageCount = cfg.Bind("Hotbar", "Pages", 4, "Hotbar pages, 1-10. Page 1 is the normal hotbar.");
            NextKey = cfg.Bind("Hotbar", "NextPageKey", KeyCode.RightBracket, "Shows the next hotbar page.");
            PrevKey = cfg.Bind("Hotbar", "PrevPageKey", KeyCode.LeftBracket, "Shows the previous hotbar page.");
            NextKey2 = cfg.Bind("Hotbar", "NextPageKey2", KeyCode.PageDown, "A second key for the next hotbar page (not while the stash is open: there it flips stash pages).");
            PrevKey2 = cfg.Bind("Hotbar", "PrevPageKey2", KeyCode.PageUp, "A second key for the previous hotbar page (not while the stash is open: there it flips stash pages).");
            MouseWheel = cfg.Bind("Hotbar", "MouseWheelOverHotbar", true, "Scrolling the mouse wheel over the hotbar flips pages.");
        }

        // ---- saved state ---------------------------------------------------------------------------------------

        /// <summary>The page on show (0-based).</summary>
        internal static int Page => Current;

        private static int Current
        {
            get => SideAreas.Data != null && SideAreas.Data.TryGetValue("hotbar_page", out object v) && v is int i ? i : 0;
            set { if (SideAreas.Data != null) SideAreas.Data["hotbar_page"] = value; }
        }

        private static string[] GetPage(int p) =>
            SideAreas.Data != null && SideAreas.Data.TryGetValue("hotbar_" + p, out object v) ? v as string[] : null;

        private static void SetPage(int p, string[] names)
        {
            if (SideAreas.Data != null) SideAreas.Data["hotbar_" + p] = names;
        }

        // ---- swapping -----------------------------------------------------------------------------------------

        internal static bool CanSwap => Menu_HUD.instance != null && Player.instance != null && !Game.TARGETING && !Game.RESOLVING
                                        && !Game.Game_isLoading && Menu_HUD.instance.GetDraggingItem() == null && !Forms.Active;

        /// <summary>The slots exactly as shown, without the page bookkeeping the SaveHotbar patch does.</summary>
        internal static string[] Visible()
        {
            swapping = true;
            try { return Menu_HUD.instance.SaveHotbar(); }
            finally { swapping = false; }
        }

        /// <summary>Puts a whole layout on the visible slots (transformations), leaving page bookkeeping alone.</summary>
        internal static void Replace(string[] layout)
        {
            Menu_HUD hud = Menu_HUD.instance;
            if (hud == null) return;
            swapping = true;
            try
            {
                foreach (Action_Icon_Slot slot in hud.actionIconSlots)
                {
                    if (slot == null || slot.actionIcon == null) continue;
                    UnityEngine.Object.DestroyImmediate(slot.actionIcon.gameObject);
                    slot.ClearActionIcon();
                }
                hud.LoadHotbar(Usable(layout, hud.actionIconSlots.Length));
                hud.Refresh_Hotbar();
            }
            catch (Exception e) { Plugin.Log.LogError("Hotbar layout: " + e); }
            finally { swapping = false; }
        }

        internal static void Show(int p)
        {
            Menu_HUD hud = Menu_HUD.instance;
            int cur = Current;
            if (hud == null || p == cur || p < 0 || p >= Count) return;
            swapping = true;
            try
            {
                SetPage(cur, hud.SaveHotbar());
                foreach (Action_Icon_Slot slot in hud.actionIconSlots)
                {
                    if (slot == null || slot.actionIcon == null) continue;
                    UnityEngine.Object.DestroyImmediate(slot.actionIcon.gameObject); // now, so the new page's icons can't be hit by a late destroy
                    slot.ClearActionIcon();
                }
                Current = p;
                hud.LoadHotbar(Usable(GetPage(p), hud.actionIconSlots.Length));
                hud.Refresh_Hotbar();
            }
            catch (Exception e) { Plugin.Log.LogError("Hotbar pages: " + e); }
            finally { swapping = false; }
            AudioManager.PlayEvent("event:/UI/UI_Click");
            UpdateLabel();
        }

        /// <summary>
        /// A stored page with entries the game would choke on removed: consumables are stored by inventory slot, and
        /// that slot may hold something else by now (the game's own loader assumes it doesn't).
        /// </summary>
        internal static string[] Usable(string[] names, int len)
        {
            var r = Enumerable.Repeat("EMPTY", len).ToArray();
            if (names == null) return r;
            for (int i = 0; i < Math.Min(len, names.Length); i++)
            {
                string n = names[i] ?? "EMPTY";
                if (n.StartsWith("I_"))
                {
                    if (!int.TryParse(n.Substring(2), out int slot) || !(Menu_Inventory.instance.GetItemInSlot(slot) is Item_Consumable)) n = "EMPTY";
                }
                else if (n.StartsWith("A_") && Player_AbilityControl.instance.GetAbility(n.Substring(2)) == null) n = "EMPTY";
                r[i] = n;
            }
            return r;
        }

        // ---- page indicator ------------------------------------------------------------------------------------

        internal class PageButton : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public void OnPointerClick(PointerEventData e)
            {
                if (!CanSwap) return;
                int n = Count;
                Show(e.button == PointerEventData.InputButton.Right ? (Current + n - 1) % n : (Current + 1) % n);
            }

            public void OnPointerEnter(PointerEventData e) =>
                Tooltip.instance?.Show_TinyTip($"Hotbar page {Current + 1} of {Count}",
                    $"Left-click: next page. Right-click: previous.\n{PrevKey.Value} / {NextKey.Value}, {PrevKey2.Value} / {NextKey2.Value}{(MouseWheel.Value ? " or the mouse wheel over the hotbar" : "")} also flip pages.", null);

            public void OnPointerExit(PointerEventData e) => Tooltip.instance?.Hide();

            public RectTransform panel;

            private void LateUpdate() => Place((RectTransform)transform, panel);

            private void Update()
            {
                if (Count < 2 || !CanSwap || Game.VENDOR || Game.EVENT) return;
                int n = Count;
                bool stashOpen = Menu_Stash.instance_stash != null && Menu_Stash.instance_stash.GetIsShowing(); // there Page Up/Down flip stash pages
                if (Down(NextKey.Value) || (!stashOpen && Down(NextKey2.Value))) Show((Current + 1) % n);
                else if (Down(PrevKey.Value) || (!stashOpen && Down(PrevKey2.Value))) Show((Current + n - 1) % n);
                else if (MouseWheel.Value && Input.mouseScrollDelta.y != 0f && Menu_HUD.instance.OverCheck_Hotbar())
                    Show(Input.mouseScrollDelta.y < 0f ? (Current + 1) % n : (Current + n - 1) % n);
            }
        }

        private static void BuildLabel(Menu_HUD hud)
        {
            if (label != null || hud.actionIconSlots == null || hud.actionIconSlots.Length == 0 || hud.actionIconSlots[0] == null) return;
            RectTransform first = (RectTransform)hud.actionIconSlots[0].transform; // only to find the hotbar canvas
            RectTransform row = (RectTransform)first.parent;
            var go = new GameObject("SCCL_HotbarPage", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(PageButton));
            var rt = (RectTransform)go.transform;
            rt.SetParent(row.parent, false);
            go.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true; // placed by hand each frame: stay out of the hotbar's own layout, which otherwise grew by our height
            label = go.GetComponent<TextMeshProUGUI>();
            TMP_Text any = hud.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.font != null);
            if (any != null) label.font = any.font;
            label.fontSize = 30;
            label.alignment = TextAlignmentOptions.BottomLeft;
            label.raycastTarget = true;
            label.enableWordWrapping = false;
            rt.sizeDelta = new Vector2(160f, 36f);
            rt.pivot = new Vector2(0f, 0f);
            go.GetComponent<PageButton>().panel = (RectTransform)hud.hotbar.transform;
            UiLook.Attach(rt, "HotbarPage");
            UpdateLabel();
        }

        /// <summary>Sits on the top-left corner of the hotbar panel, following it when the player drags the hotbar.</summary>
        private static void Place(RectTransform rt, RectTransform panel)
        {
            if (panel == null || panel.rect.width <= 0f) return;
            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            rt.position = corners[1]; // top-left
            rt.anchoredPosition += new Vector2(6f, 2f);
        }

        private static void UpdateLabel()
        {
            if (label == null) return;
            label.gameObject.SetActive(Count > 1);
            label.text = $"<color=#FFD966>Page {Current + 1}</color><size=75%><color=#9FB7C9>/{Count}</color></size>";
        }

        // ---- patches ------------------------------------------------------------------------------------------

        /// <summary>The save's own hotbar field always gets page 1; whatever page is showing is kept in the mod slot.</summary>
        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.SaveHotbar))]
        private static class Patch_Save
        {
            private static void Postfix(ref string[] __result)
            {
                if (swapping) return;
                // mid-transformation the slots hold the form's kit; the save gets the layout it replaced
                if (Forms.Stashed != null) __result = (string[])Forms.Stashed.Clone();
                int cur = Current;
                if (cur == 0) return;
                SetPage(cur, __result);
                __result = GetPage(0) ?? Enumerable.Repeat("EMPTY", __result.Length).ToArray();
            }
        }

        /// <summary>A loaded save shows page 1 first (the game's field); then switch to the page that was showing.</summary>
        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.LoadHotbar))]
        private static class Patch_Load
        {
            private static void Postfix(Menu_HUD __instance)
            {
                if (swapping) return;
                int saved = Current;
                Current = 0;
                if (saved > 0 && saved < Count) Show(saved);
                UpdateLabel();
            }
        }

        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.Awake))]
        private static class Patch_Awake
        {
            private static void Postfix(Menu_HUD __instance)
            {
                label = null;
                try { BuildLabel(__instance); }
                catch (Exception e) { Plugin.Log.LogError("Hotbar pages: " + e); }
            }
        }

        /// <summary>A new game starts on page 1 (its save slot is fresh, but the label may still show the old page).</summary>
        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.ClearAllLevelData))]
        private static class Patch_NewRun
        {
            private static void Postfix() => UpdateLabel();
        }
    }
}
