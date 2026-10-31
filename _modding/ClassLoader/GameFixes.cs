using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>
    /// Fixes for vanilla bugs players report. [Fixes] in the config file turns each one off.
    /// <list type="bullet">
    /// <item>Sorting (inventory and apartment locker) rebuilt the contents from an internal list that can hold an item
    /// twice or hold one that isn't there any more; the sort then put it in two slots and the save wrote it twice
    /// (duplicate items). The sort now works only on what is actually in the slots, each item once.</item>
    /// <item>Sort by value never sorted (its pick loop forgot the best item before using it).</item>
    /// <item>Sorting the locker pulled ammo matching your equipped ammo out of the locker into your ammo slot.</item>
    /// <item>The locker was only saved while your inventory held something, so moving your last items into it
    /// and closing it could lose them.</item>
    /// <item>Enemy effects that start with a random number of charges up to a set most (Demented, Doomed and other
    /// <c>Effect_Mod_Damage_Charge</c> effects) never rolled that most: Unity's Random.Range leaves the top out.</item>
    /// <item>Loading a game while already in one (the in-game Load menu, a Conjoined body swap) broke the minimap: its
    /// camera and tile container are static, the new HUD made its own camera and drew the new map into the old
    /// container, then the old HUD's OnDestroy destroyed both at the end of the frame.</item>
    /// <item>War Crafting two shields added their Shield Regen together. Shield Regen is turns per shield point, so
    /// the result refilled slower than either shield (two Heavy Shields at 8 made one at 16). It is now their average.
    /// Older builds also kept only 65% of each shield's stats, which could leave a War Crafted shield with no Shield
    /// Max: a useless shield that can't be sold or crafted again. Such a shield gets its Shield Max (and Shield Regen)
    /// back from its base item when the save loads.</item>
    /// </list>
    /// </summary>
    internal static class GameFixes
    {
        internal static ConfigEntry<bool> SortFix, LockerSaveFix, ChargeRollFix, MinimapLoadFix, WarCraftShieldFix;

        internal static void Bind(ConfigFile cfg)
        {
            SortFix = cfg.Bind("Fixes", "SortFix", true,
                "Inventory and locker sorting only rearranges what is in the slots (no duplicated items), sort by value really sorts, and sorting the locker leaves its ammo alone.");
            LockerSaveFix = cfg.Bind("Fixes", "LockerSaveFix", true,
                "The apartment locker is saved even when your inventory is empty (vanilla skipped it, which could lose items moved into the locker).");
            ChargeRollFix = cfg.Bind("Fixes", "EnemyChargeRoll", true,
                "Enemy effects like Demented and Doomed can start with their full stated number of charges (vanilla's roll never reached the top).");
            MinimapLoadFix = cfg.Bind("Fixes", "MinimapAfterLoad", true,
                "The minimap keeps working after loading a save while in a game (and after a Conjoined body swap); vanilla lost its camera and tiles.");
            WarCraftShieldFix = cfg.Bind("Fixes", "WarCraftShields", true,
                "War Crafting two shields averages their Shield Regen instead of adding it (which made the shield refill slower), and a War Crafted shield left with no Shield Max gets it back on load.");
        }

        // ---- War Crafted shields ------------------------------------------------------------------------------

        /// <summary>
        /// Runs once the merged stats are in place: for shields every stat of both is kept and summed, so the
        /// first shield's regen is what's left after taking out the second's.
        /// </summary>
        [HarmonyPatch(typeof(PA_Modder_WarCrafting), "DoResistances")]
        private static class Patch_WarCraftRegen
        {
            private static void Prefix(Item_Equipment first, Item_Equipment second)
            {
                if (!WarCraftShieldFix.Value || !(first is Item_Weapon w) || w.weaponType != WEAPON_TYPE.Shield || second == null) return;
                StatObj merged = first.stats.FirstOrDefault(s => s != null && s.statType == STAT_TYPE.ShieldRegen);
                int b = second.stats.Where(s => s != null && s.statType == STAT_TYPE.ShieldRegen).Sum(s => s.amount);
                if (merged == null || b <= 0 || merged.amount - b <= 0) return; // only one of them had regen: keep it
                merged.amount = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(merged.amount / 2f));
            }
        }

        /// <summary>A War Crafted shield with no Shield Max takes its base item's at its level.</summary>
        [HarmonyPatch(typeof(Item_Weapon), nameof(Item_Weapon.Deserialize))]
        private static class Patch_RepairWarCraftedShield
        {
            private static void Postfix(Item_Weapon __instance)
            {
                if (!WarCraftShieldFix.Value || __instance.weaponType != WEAPON_TYPE.Shield || !__instance.HasTag("MODDER_WAR_CRAFTING")
                    || __instance.stats.Any(s => s != null && s.statType == STAT_TYPE.ShieldMax && s.amount > 0)) return;
                var tpl = ItemDatabase.instance?.weaponTemplates.FirstOrDefault(t => t?.weaponPrefab != null && t.weaponPrefab.itemId == __instance.itemId);
                if (tpl == null || tpl.weaponStats.Count == 0) return;
                var lvl = tpl.weaponStats[UnityEngine.Mathf.Clamp(__instance.itemLevel, 0, tpl.weaponStats.Count - 1)].stats;
                __instance.stats.RemoveAll(s => s != null && s.statType == STAT_TYPE.ShieldMax);
                foreach (StatObj s in lvl)
                {
                    if (s == null) continue;
                    if (s.statType == STAT_TYPE.ShieldMax || (s.statType == STAT_TYPE.ShieldRegen && !__instance.stats.Any(x => x != null && x.statType == STAT_TYPE.ShieldRegen)))
                        __instance.AddStat(s.statType, s.amount);
                }
                Plugin.Log.LogInfo($"War Craft fix: {__instance.itemId} had no Shield Max; restored it from the base item.");
            }
        }

        // ---- minimap after an in-game load ---------------------------------------------------------------------

        /// <summary>The old map's tiles go before the new HUD builds the new map, so it starts a container of its own.</summary>
        [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
        private static class Patch_LoadMinimap
        {
            private static void Prefix()
            {
                if (!MinimapLoadFix.Value || Menu_MiniMap.instance == null) return;
                try { Menu_MiniMap.instance.Reset(); }
                catch (Exception e) { Plugin.Log.LogWarning("Minimap load fix: " + e.Message); }
            }
        }

        /// <summary>A HUD going away while a newer one is up leaves the shared camera and container to the new one.</summary>
        [HarmonyPatch(typeof(Menu_MiniMap), "OnDestroy")]
        private static class Patch_MinimapDestroy
        {
            private static bool Prefix(Menu_MiniMap __instance)
            {
                if (!MinimapLoadFix.Value || Menu_MiniMap.instance == null || Menu_MiniMap.instance == __instance) return true;
                UnityEngine.PlayerPrefs.SetFloat("PREF_minimap_zoom", Traverse.Create(__instance).Field("miniMapSize").GetValue<float>());
                return false;
            }
        }

        // ---- sorting ------------------------------------------------------------------------------------------

        private static bool rebuildingLocker;

        /// <summary>The item list the sort reads becomes exactly what the slots show, each item once.</summary>
        [HarmonyPatch(typeof(Menu_Inventory), nameof(Menu_Inventory.BTN_Sort))]
        private static class Patch_Sort
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Menu_Inventory __instance)
            {
                if (!SortFix.Value || __instance.itemSlots == null) return;
                var shown = new List<Item>();
                foreach (Player_InventorySlot slot in __instance.itemSlots)
                {
                    Item it = slot != null ? slot.GetItem() : null;
                    if (it != null && !shown.Contains(it)) shown.Add(it);
                }
                // An item showing in both the locker and the inventory would be saved twice: it stays in the inventory.
                Menu_Inventory other = __instance.isStash ? Menu_Inventory.instance : Menu_Stash.instance_stash;
                if (other != null && other.itemSlots != null)
                    foreach (Player_InventorySlot slot in other.itemSlots)
                    {
                        Item twin = slot != null ? slot.GetItem() : null;
                        if (twin == null || !shown.Contains(twin)) continue;
                        Plugin.Log.LogWarning($"Sort fix: {twin.itemId} was in both the locker and the inventory; keeping it in the inventory.");
                        if (__instance.isStash) shown.Remove(twin);
                        else { slot.SetItem(null); other.items.Remove(twin); }
                    }
                int before = __instance.items?.Count ?? 0;
                if (before != shown.Count || (__instance.items != null && __instance.items.Any(i => i != null && !shown.Contains(i))))
                    Plugin.Log.LogInfo($"Sort fix: {(__instance.isStash ? "locker" : "inventory")} list had {before} entries for {shown.Count} items in its slots; sorting what's in the slots.");
                __instance.items = shown;
            }
        }

        /// <summary>Most valuable first; equal values keep their order.</summary>
        [HarmonyPatch(typeof(Menu_Inventory), "Sort_Value")]
        private static class Patch_SortValue
        {
            private static bool Prefix(Menu_Inventory __instance)
            {
                if (!SortFix.Value) return true;
                var t = Traverse.Create(__instance);
                var sortList = t.Field("sortList").GetValue<List<Item>>();
                sortList.AddRange(__instance.items.Where(i => i != null).OrderByDescending(i => i.GetItemValue()));
                __instance.items.Clear();
                t.Method("RebuildItemList").GetValue();
                return false;
            }
        }

        [HarmonyPatch(typeof(Menu_Inventory), "RebuildItemList")]
        private static class Patch_Rebuild
        {
            private static void Prefix(Menu_Inventory __instance) => rebuildingLocker = SortFix.Value && __instance.isStash;
            private static void Finalizer() => rebuildingLocker = false;
        }

        /// <summary>While the locker re-places its own items, ammo stays in the locker instead of topping up your ammo slot.</summary>
        [HarmonyPatch(typeof(Menu_Player), nameof(Menu_Player.AmmoCollectCheck))]
        private static class Patch_Ammo
        {
            private static bool Prefix(ref bool __result)
            {
                if (!rebuildingLocker) return true;
                __result = false;
                return false;
            }
        }

        // ---- locker saving ------------------------------------------------------------------------------------

        /// <summary>True once this session's locker has loaded its saved contents (so saving it can't wipe it).</summary>
        private static bool lockerLoaded;

        [HarmonyPatch(typeof(Menu_Stash), nameof(Menu_Stash.Show))]
        private static class Patch_LockerShow
        {
            private static void Postfix(Menu_Stash __instance) { if (__instance.GetIsShowing()) lockerLoaded = true; }
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.ClearAllLevelData))]
        private static class Patch_NewRun
        {
            private static void Postfix() => lockerLoaded = false;
        }

        /// <summary>Vanilla only saves the locker while your inventory holds something; once it's been opened, always save it.</summary>
        [HarmonyPatch(typeof(Misc_Control), nameof(Misc_Control.Serialize))]
        private static class Patch_SaveLocker
        {
            private static void Prefix(Misc_Control __instance)
            {
                if (!LockerSaveFix.Value || !lockerLoaded || Menu_Stash.instance_stash == null || Game.Game_isLoading) return;
                var inv = Menu_Inventory.GetItems();
                if (inv != null && inv.Count > 0) return; // vanilla saves it in this case
                __instance.stashItems = Menu_Stash.instance_stash.Serialize();
            }
        }

        // ---- starting charges ---------------------------------------------------------------------------------

        /// <summary>Replaces <c>Random.Range(1, initialChargeGainMax)</c>, whose int overload never returns the max.</summary>
        public static int RollCharges(int min, int max) => UnityEngine.Random.Range(min, ChargeRollFix.Value ? max + 1 : max);

        [HarmonyPatch(typeof(Effect_Mod_Damage_Charge), nameof(Effect.Init))]
        private static class Patch_ChargeRoll
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
            {
                var range = AccessTools.Method(typeof(UnityEngine.Random), nameof(UnityEngine.Random.Range), new[] { typeof(int), typeof(int) });
                int n = 0;
                foreach (CodeInstruction ci in code)
                {
                    if (ci.Calls(range)) { ci.operand = AccessTools.Method(typeof(GameFixes), nameof(RollCharges)); n++; }
                    yield return ci;
                }
                if (n != 1) Plugin.Log.LogWarning($"Charge roll fix: found {n} rolls to patch (expected 1)");
            }
        }
    }
}
