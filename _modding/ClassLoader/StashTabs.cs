using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// Extra stash pages. Page 1 is the vanilla stash, still saved in Game.scc, so removing the mod only hides the
    /// extra pages. Pages 2+ live in SCCL_StashPages.dat next to Game.scc, in the same InventorySaveInfo format.
    /// The 120 vanilla slots are reused: switching a page stores the visible slots and loads the other page into them.
    /// Drop an item on a tab to send it to that page.
    /// The last tab, "Own", is a page bound to the save rather than shared by every character: it lives in the mod's
    /// save slot (SideAreas.Data "stash_personal") and also takes the items the shared stash refuses (NOSTASH).
    /// Items only reach it from your bag and leave it to your bag, never straight to a shared page, so a reload
    /// can't copy anything between the two.
    /// </summary>
    internal static class StashTabs
    {
        internal static ConfigEntry<int> PageCount;
        internal static ConfigEntry<float> BarOffsetX, BarOffsetY;
        internal static ConfigEntry<KeyCode> NextKey, PrevKey;

        private static List<object> pages; // index 0 unused: page 0 is Misc_Control.stashItems
        private static int current, lastShown;
        private static RectTransform bar;
        private static readonly List<Image> tabImages = new List<Image>();
        private static readonly List<TextMeshProUGUI> tabLabels = new List<TextMeshProUGUI>();

        private static string FilePath => Path.Combine(Application.persistentDataPath, "SCCL_StashPages.dat");
        private static int Count => Mathf.Clamp(Tweaks.Int("stashPages", PageCount.Value), 1, 20);
        /// <summary>The save-bound page: after the shared ones.</summary>
        private static int Own => Count;
        private static int Total => Count + 1;
        private const string OwnKey = "stash_personal";
        internal static bool OnOwnPage => current == Own && Menu_Stash.instance_stash != null && Menu_Stash.instance_stash.GetIsShowing();

        internal static void Bind(ConfigFile cfg)
        {
            PageCount = cfg.Bind("Stash", "Pages", 6, "Stash pages (tabs), 1-20. Page 1 is the vanilla stash. Lowering this keeps the items of hidden pages on disk.");
            BarOffsetX = cfg.Bind("Stash", "TabBarOffsetX", 0f, "Nudges the tab bar sideways from the top-left of the stash grid.");
            BarOffsetY = cfg.Bind("Stash", "TabBarOffsetY", 6f, "Nudges the tab bar up (or down, if negative) from the top of the stash grid.");
            NextKey = cfg.Bind("Stash", "NextPageKey", KeyCode.PageDown, "Key that shows the next stash page while the stash is open.");
            PrevKey = cfg.Bind("Stash", "PrevPageKey", KeyCode.PageUp, "Key that shows the previous stash page while the stash is open.");
        }

        private static void LoadFile()
        {
            if (pages != null) return;
            pages = new List<object>();
            try
            {
                if (File.Exists(FilePath))
                    using (FileStream fs = File.OpenRead(FilePath))
                        pages = new BinaryFormatter().Deserialize(fs) as List<object> ?? new List<object>();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not read stash pages, keeping a copy as .bad: " + e.Message);
                try { File.Copy(FilePath, FilePath + ".bad", true); } catch { }
                pages = new List<object>();
            }
            while (pages.Count < Count) pages.Add(null);
        }

        private static void SaveFile()
        {
            if (pages == null) return;
            try
            {
                string tmp = FilePath + ".tmp";
                using (FileStream fs = File.Create(tmp)) new BinaryFormatter().Serialize(fs, pages);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception e) { Plugin.Log.LogError("Could not save stash pages: " + e); }
        }

        private static object PageData(int p)
        {
            if (p == 0) return Misc_Control.instance.stashItems;
            if (p == Own) return SideAreas.Data != null && SideAreas.Data.TryGetValue(OwnKey, out object o) ? o : null;
            LoadFile(); // the tabs count other pages' items before any page switch has loaded the file
            return p < pages.Count ? pages[p] : null;
        }

        private static void SetPageData(int p, object d)
        {
            if (p == 0) Misc_Control.instance.stashItems = d;
            else if (p == Own) { if (SideAreas.Data != null) SideAreas.Data[OwnKey] = d; }
            else pages[p] = d;
        }

        private static InventorySaveInfo Snapshot(Menu_Stash s)
        {
            var info = new InventorySaveInfo { items = new List<object>(), slotIds = new List<int>() };
            for (int i = 0; i < s.itemSlots.Count; i++)
            {
                if (s.itemSlots[i] == null || s.itemSlots[i].item == null) continue;
                info.items.Add(s.itemSlots[i].item.Serialize());
                info.slotIds.Add(i);
            }
            return info;
        }

        private static void ClearSlots(Menu_Stash s)
        {
            foreach (Player_InventorySlot slot in s.itemSlots)
            {
                if (slot == null) continue;
                if (slot.item != null) UnityEngine.Object.Destroy(slot.item.gameObject);
                slot.SetItem(null);
            }
            s.items = new List<Item>();
        }

        private static int ItemCount(int p)
        {
            if (p == current && Menu_Stash.instance_stash != null) return Menu_Stash.instance_stash.items.Count(i => i != null);
            return (PageData(p) as InventorySaveInfo)?.items?.Count ?? 0;
        }

        internal static void SwitchTo(int p)
        {
            Menu_Stash s = Menu_Stash.instance_stash;
            if (s == null || p == current || p < 0 || p >= Total) return;
            if (HUD.instance != null && HUD.instance.GetDraggedItem() != null) return;
            Swap(s, p);
            lastShown = p;
            s.ClearSelected();
            s.Refresh();
            Tooltip.instance?.Hide();
            Global_Control.instance.SaveGlobal(); // keep Game.scc and the pages file in step, so a crash can't duplicate items
            AudioManager.PlayEvent("event:/UI/UI_Click");
            RefreshTabs();
        }

        /// <summary>Stores the visible slots as the current page and loads page p into them.</summary>
        private static void Swap(Menu_Stash s, int p)
        {
            LoadFile();
            SetPageData(current, Snapshot(s));
            ClearSlots(s);
            current = p;
            if (PageData(p) is InventorySaveInfo data) s.Deserialize(data);
            SaveFile();
        }

        /// <summary>Sends the item being dragged (from the stash or the inventory) to another page.</summary>
        internal static void DropOnTab(int p)
        {
            UI_Item drag = HUD.instance?.GetDraggedItem();
            UI_Item src = drag?.draggingSource;
            Item item = src?.GetItem();
            Menu_Stash s = Menu_Stash.instance_stash;
            if (item == null || s == null || p == current || p < 0 || p >= Total || !(src.FLAG_stash || src.FLAG_inventory)) return;
            if (src.FLAG_stash && (p == Own || current == Own))
            {
                Game.LOG("Your own stash and the shared one don't trade directly: take it into your bag first.");
                AudioManager.PlayEvent("event:/UI/UI_Error");
                return;
            }
            if (src.FLAG_inventory && ((item.HasTag("NOSTASH") && p != Own) || item.HasTag("QUEST")))
            {
                Game.LOG(I2.Loc.LocalizationManager.GetTranslation("UI/NoStashX").Replace("[X]", item.GetName()));
                AudioManager.PlayEvent("event:/UI/UI_Error");
                return;
            }
            LoadFile();
            var data = PageData(p) as InventorySaveInfo ?? new InventorySaveInfo();
            if (data.items == null) data.items = new List<object>();
            if (data.slotIds == null) data.slotIds = new List<int>();
            int slot = Enumerable.Range(0, s.itemSlots.Count).FirstOrDefault(i => !data.slotIds.Contains(i));
            if (data.slotIds.Count >= s.itemSlots.Count || data.slotIds.Contains(slot))
            {
                Game.LOG($"Stash page {p + 1} is full.");
                AudioManager.PlayEvent("event:/UI/UI_Error");
                return;
            }
            object saved = item.Serialize();
            if (src.FLAG_stash) s.RemoveItem(item, null, isSilent: true);
            else Menu_Inventory.instance.RemoveItem(item, null, isSilent: true);
            UnityEngine.Object.Destroy(item.gameObject);
            data.items.Add(saved);
            data.slotIds.Add(slot);
            SetPageData(p, data);
            SaveFile();
            Global_Control.instance.SaveGlobal();
            if (!string.IsNullOrEmpty(item.FMOD_doDrop)) AudioManager.PlayEvent(item.FMOD_doDrop);
            drag.ClearDragIcon();
            HUD.instance.SetDraggedItem(null);
            RefreshTabs();
        }

        private static void BuildBar(Menu_Stash s)
        {
            if (bar != null || s.itemSlots.Count == 0 || s.itemSlots[0] == null) return;
            Transform grid = s.itemSlots[0].transform.parent;
            var go = new GameObject("SCCL_StashTabs", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            bar = (RectTransform)go.transform;
            bar.SetParent(grid.parent, false);
            UiLook.Attach(bar, "StashTabs");
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 3f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            TMP_FontAsset font = s.sortDropdown != null ? s.sortDropdown.captionText.font : null;
            Image style = s.sortDropdown != null ? s.sortDropdown.targetGraphic as Image : null;
            tabImages.Clear();
            tabLabels.Clear();
            for (int i = 0; i < Total; i++)
            {
                var tab = new GameObject("Tab" + (i + 1), typeof(RectTransform), typeof(Image), typeof(StashTab));
                tab.transform.SetParent(bar, false);
                var img = tab.GetComponent<Image>();
                if (style != null) { img.sprite = style.sprite; img.type = style.type; }
                tab.GetComponent<StashTab>().page = i;
                var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                label.transform.SetParent(tab.transform, false);
                var lr = (RectTransform)label.transform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
                var t = label.GetComponent<TextMeshProUGUI>();
                if (font != null) t.font = font;
                t.fontSize = 16;
                t.alignment = TextAlignmentOptions.Center;
                t.raycastTarget = false;
                tabImages.Add(img);
                tabLabels.Add(t);
            }
        }

        /// <summary>Sits the bar just above the slot grid. Done on every show, since the grid can be moved.</summary>
        private static void PlaceBar(Menu_Stash s)
        {
            if (bar == null) return;
            var corners = new Vector3[4];
            float minX = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            var parent = (RectTransform)bar.parent;
            foreach (Player_InventorySlot slot in s.itemSlots)
            {
                if (slot == null) continue;
                ((RectTransform)slot.transform).GetWorldCorners(corners);
                foreach (Vector3 c in corners)
                {
                    Vector3 l = parent.InverseTransformPoint(c);
                    minX = Mathf.Min(minX, l.x); maxX = Mathf.Max(maxX, l.x); maxY = Mathf.Max(maxY, l.y);
                }
            }
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
            bar.pivot = new Vector2(0f, 0f);
            bar.sizeDelta = new Vector2(maxX - minX, 26f);
            // Anchored at the parent's centre, so convert the parent-local corner into an anchored position.
            Vector2 centre = parent.rect.center;
            bar.anchoredPosition = new Vector2(minX - centre.x, maxY - centre.y) + new Vector2(BarOffsetX.Value, BarOffsetY.Value);
            bar.SetAsLastSibling();
        }

        internal static void RefreshTabs()
        {
            for (int i = 0; i < tabLabels.Count; i++)
            {
                bool on = i == current;
                tabLabels[i].text = i == Own ? "Own" : (i + 1).ToString();
                tabLabels[i].color = on ? new Color(1f, 0.85f, 0.4f) : (ItemCount(i) > 0 ? Color.white : new Color(1f, 1f, 1f, 0.45f));
                tabImages[i].color = on ? new Color(1f, 1f, 1f, 1f) : new Color(0.6f, 0.6f, 0.6f, 0.8f);
            }
        }

        internal static void ShowTip(int p)
        {
            if (Tooltip.instance == null || p >= Total) return;
            int n = ItemCount(p);
            if (p == Own)
            {
                Tooltip.instance.Show_TinyTip("Your own stash", $"{n} / {Menu_Stash.instance_stash.itemSlots.Count} items.\nKept with this save: only this character sees it, and it takes "
                    + "the unstashable items the shared stash won't (modded or floor-bound gear). Quest items still stay with you.\nItems come in from your bag and go back out to it.", null);
                return;
            }
            string desc = $"{n} / {Menu_Stash.instance_stash.itemSlots.Count} items.\nClick to open. Drop an item here to send it to this page."
                          + $"\n{PrevKey.Value} / {NextKey.Value} flip pages.";
            Tooltip.instance.Show_TinyTip($"Stash page {p + 1}", desc, null);
        }

        [HarmonyPatch(typeof(Menu_Stash), nameof(Menu_Stash.Start))]
        private static class Patch_Start
        {
            private static void Postfix(Menu_Stash __instance)
            {
                try { BuildBar(__instance); }
                catch (Exception e) { Plugin.Log.LogError("Stash tabs: " + e); }
            }
        }

        /// <summary>Vanilla Show reloads page 1; reopen the page the player last looked at.</summary>
        [HarmonyPatch(typeof(Menu_Stash), nameof(Menu_Stash.Show))]
        private static class Patch_Show
        {
            private static void Postfix(Menu_Stash __instance)
            {
                if (!__instance.GetIsShowing()) return;
                try
                {
                    BuildBar(__instance);
                    PlaceBar(__instance);
                    if (lastShown > 0 && lastShown < Total) SwitchTo(lastShown);
                    RefreshTabs();
                }
                catch (Exception e) { Plugin.Log.LogError("Stash tabs: " + e); }
            }
        }

        /// <summary>Closing always leaves page 1 in the slots, so everything vanilla does while the stash is shut sees it.</summary>
        [HarmonyPatch(typeof(Menu_Stash), nameof(Menu_Stash.Hide))]
        private static class Patch_Hide
        {
            private static void Prefix(Menu_Stash __instance)
            {
                if (current == 0) return;
                try { Swap(__instance, 0); }
                catch (Exception e) { Plugin.Log.LogError("Stash tabs: " + e); }
            }
        }

        /// <summary>Game.scc must always get page 1, whichever page is on screen when the game saves.</summary>
        [HarmonyPatch(typeof(Menu_Stash), nameof(Menu_Stash.Serialize))]
        private static class Patch_Serialize
        {
            private static void Postfix(ref object __result)
            {
                if (current == 0) return;
                if (current == Own) SetPageData(Own, __result);
                else
                {
                    if (pages == null) return;
                    pages[current] = __result;
                    SaveFile();
                }
                __result = Misc_Control.instance.stashItems;
            }
        }

        [HarmonyPatch(typeof(Menu_Stash), "Update")]
        private static class Patch_Update
        {
            private static void Postfix(Menu_Stash __instance)
            {
                if (!__instance.GetIsShowing()) return;
                if (Input.GetKeyDown(NextKey.Value)) SwitchTo((current + 1) % Total);
                else if (Input.GetKeyDown(PrevKey.Value)) SwitchTo((current + Total - 1) % Total);
            }
        }

        /// <summary>Saving the game while your own page is open: the slots on screen are what it holds.</summary>
        [HarmonyPatch(typeof(Game), nameof(Game.Serialize))]
        private static class Patch_SaveOwn
        {
            private static void Prefix()
            {
                if (current != Own || Menu_Stash.instance_stash == null) return;
                try { SetPageData(Own, Snapshot(Menu_Stash.instance_stash)); }
                catch (Exception e) { Plugin.Log.LogError("Own stash: " + e); }
            }
        }

        /// <summary>
        /// On your own page the shared stash's NOSTASH rule doesn't apply: the game's drop and right-click checks ask
        /// the item for the tag, so for the length of those calls it reads as absent (QUEST still counts).
        /// </summary>
        private static bool ownBypass;

        [HarmonyPatch(typeof(Item), nameof(Item.HasTag))]
        private static class Patch_NoStashTag
        {
            private static void Postfix(string tagName, ref bool __result)
            {
                if (ownBypass && __result && tagName == "NOSTASH") __result = false;
            }
        }

        [HarmonyPatch(typeof(Player_InventorySlot), nameof(Player_InventorySlot.OnDrop))]
        private static class Patch_DropOwn
        {
            private static void Prefix() => ownBypass = OnOwnPage;
            private static void Finalizer() => ownBypass = false;
        }

        [HarmonyPatch(typeof(Player_InventorySlot), nameof(Player_InventorySlot.OnPointerClick))]
        private static class Patch_ClickOwn
        {
            private static void Prefix() => ownBypass = OnOwnPage;
            private static void Finalizer() => ownBypass = false;
        }
    }

    internal class StashTab : MonoBehaviour, IPointerClickHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public int page;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) StashTabs.SwitchTo(page);
        }

        public void OnDrop(PointerEventData e) => StashTabs.DropOnTab(page);

        public void OnPointerEnter(PointerEventData e) => StashTabs.ShowTip(page);

        public void OnPointerExit(PointerEventData e) => Tooltip.instance?.Hide();
    }
}
