using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// The Rogue Modder out-scaled every other tree: Mod It Better gave +rank (up to +5) to a random stat three times
    /// per item, the first mod could never fail, a failed one cost 1 point at the top rank, and Item (Re)Designer wiped
    /// the mod count, so any miss was rerolled for free. [Balance] ModderRebalance turns this off.
    /// <list type="bullet">
    /// <item>Mods per item come with rank: 1 at ranks 1-2, 2 at ranks 3-4, 3 at rank 5.</item>
    /// <item>A mod adds 1 + rank / 2 (1, 2, 2, 3, 3). The 1st, 2nd and 3rd mod on an item fail 10%, 30% and 50% of
    /// the time, and a failure takes as much off the stat as it would have added. A failure still uses up the slot.</item>
    /// <item>Item (Re)Designer keeps the item's mod and Illegal Weapon Modification counts (and their failure odds): redesign first,
    /// then mod, because a redesign rolls the stats again and the mods' bonuses go with them.</item>
    /// </list>
    /// [Fixes] ModderStaysInSave (separate): the Modder's skills could target an item sitting in the shared stash, where
    /// the reworked item stayed and every other character could take it. They now only work on your bag, your gear and
    /// your Own stash page.
    /// </summary>
    internal static class ModderBalance
    {
        internal static ConfigEntry<bool> Rebalance, StayInSave;

        internal static void Bind(ConfigFile cfg)
        {
            Rebalance = cfg.Bind("Balance", "ModderRebalance", true,
                "Mod It Better: mods per item come with Modder rank (1/1/2/2/3), each adds 1 + rank/2, fail 10/30/50% and a failure takes off what it would have added. Item (Re)Designer no longer resets the mod counts. Off = vanilla.");
            StayInSave = cfg.Bind("Fixes", "ModderStaysInSave", true,
                "The Modder's item skills can't target items in the shared stash (the reworked item stayed there for your other characters).");
        }

        private static bool On => Rebalance.Value && !Game.Game_isLoading;

        /// <summary>Modder rank, 1-5 (0 without the tree).</summary>
        private static int Rank => (Player_SkillControl.instance?.GetRank("SKILLTREE_Modder") ?? -1) + 1;

        internal static int MaxMods(int rank) => 1 + Mathf.Max(0, rank - 1) / 2;
        internal static int ModAmount(int rank) => 1 + rank / 2;
        /// <summary>Mods already on the item (GetTag says -1 for one never modded).</summary>
        internal static int Mods(Item i) => Mathf.Max(0, i.GetTag("MOD_COUNT"));
        internal static int FailChance(int modsOnItem) => Mathf.Clamp(10 + 20 * modsOnItem, 0, 100);

        // ---- Shared stash ----------------------------------------------------------------------------------------

        private static bool InSharedStash(Item i) => i != null && i.inPlayerStash && !StashTabs.OnOwnPage;

        [HarmonyPatch]
        private static class Patch_NotInStash
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                foreach (var t in new[] { typeof(PA_Modder_ModItBetter), typeof(PA_Modder_IWM), typeof(PA_Modder_ItemRedesigner),
                                          typeof(PA_Modder_FusionForge), typeof(PA_Modder_WarCrafting) })
                    yield return AccessTools.Method(t, nameof(Ability.CanUseOn), new[] { typeof(Item) });
            }

            private static void Postfix(Item i, ref bool __result)
            {
                if (__result && StayInSave.Value && InSharedStash(i)) __result = false;
            }
        }

        // ---- Mod It Better --------------------------------------------------------------------------------------

        private static readonly AccessTools.FieldRef<PA_Modder_ModItBetter, Item_Equipment> ModTarget =
            AccessTools.FieldRefAccess<PA_Modder_ModItBetter, Item_Equipment>("targetItem");
        private static readonly AccessTools.FieldRef<Ability, string> UseFail =
            AccessTools.FieldRefAccess<Ability, string>("_useFail");

        [HarmonyPatch(typeof(PA_Modder_ModItBetter), nameof(PA_Modder_ModItBetter.CanUseOn), typeof(Item))]
        private static class Patch_ModCap
        {
            private static void Postfix(PA_Modder_ModItBetter __instance, Item i, ref bool __result)
            {
                if (!__result || !On || !(i is Item_Equipment eq) || Mods(eq) < MaxMods(Rank)) return;
                UseFail(__instance) = Ability.T("Weapon Mods/NoModMore").Replace("[X]", eq.GetName());
                __result = false;
            }
        }

        /// <summary>The fail chance the Yes/No prompt shows (and Selected rolls) comes from the item's mod count.</summary>
        [HarmonyPatch(typeof(PA_Modder_ModItBetter), nameof(PA_Modder_ModItBetter.Use))]
        private static class Patch_ModOdds
        {
            private static void Prefix()
            {
                if (On && Game.TARGET_I is Item_Equipment eq) eq.SetTag("MOD", FailChance(Mods(eq)));
            }
        }

        [HarmonyPatch(typeof(PA_Modder_ModItBetter), nameof(PA_Modder_ModItBetter.Selected))]
        private static class Patch_ModRoll
        {
            private static bool Prefix(PA_Modder_ModItBetter __instance)
            {
                if (!On) return true;
                Item_Equipment item = ModTarget(__instance);
                StatTemplate tpl = ItemDatabase.instance.GetRandomStatTemplate();
                if (item == null || tpl == null) return false;
                var pool = new List<STAT_TYPE>();
                foreach (var s in tpl.stats_major) if (s != null && !pool.Contains(s.stat)) pool.Add(s.stat);
                foreach (var s in tpl.stats_minor) if (s != null && !pool.Contains(s.stat)) pool.Add(s.stat);
                if (pool.Count == 0) return false;

                STAT_TYPE stat = pool[Random.Range(0, pool.Count)];
                int amount = ModAmount(Rank);
                bool failed = Random.Range(1, 101) <= FailChance(Mods(item));
                if (failed) amount = -amount;
                AudioManager.PlayEvent(failed ? "event:/Abilities/Misc/Modder_ModIt_Fail" : "event:/Abilities/Misc/Modder_ModIt_Win",
                    Player.instance.transform.position);
                item.AddStat(stat, amount);

                string change = failed
                    ? "<color=#FF7272>- " + -amount + " " + Game.GetSpriteString(stat) + "</color>"
                    : "<color=#72FFAC>+ " + amount + " " + Game.GetSpriteString(stat) + "</color>";
                Game.LOG(Ability.T("Events/Modder_ModItBetter_Result").Replace("[X]", item.GetName()).Replace("[Y]", change));

                if (Menu_Player.instance.IsEquipped(item))
                {
                    Player.instance.CalculateStats();
                    var slot = Menu_Player.instance.GetItemSlot(item);
                    if (slot != null) slot.DoShing();
                }
                else
                {
                    var slot = Menu_Inventory.instance.GetItemSlot(item);
                    if (slot != null) slot.DoShing();
                }

                item.AddTag("MOD_COUNT", 1);
                item.SetTag("MOD", FailChance(Mods(item)));
                item.SetTag("NOSTASH", 1);
                __instance.hitChance = 0;
                Game.ClearTarget(item);
                return false;
            }
        }

        // ---- Item (Re)Designer ------------------------------------------------------------------------------------

        private static readonly string[] KeptTags = { "MOD", "MOD_COUNT", "IWM", "IWM_COUNT" };

        [HarmonyPatch(typeof(PA_Modder_ItemRedesigner), nameof(PA_Modder_ItemRedesigner.Selected))]
        private static class Patch_RedesignKeepsMods
        {
            private static void Prefix(PA_Modder_ItemRedesigner __instance, out Dictionary<string, int> __state)
            {
                __state = null;
                Item_Equipment item = Traverse.Create(__instance).Field("targetItem").GetValue<Item_Equipment>();
                if (!On || item == null) return;
                __state = new Dictionary<string, int>();
                foreach (string t in KeptTags) if (item.HasTag(t)) __state[t] = item.GetTag(t);
                __state["__item"] = item.GetInstanceID();
            }

            private static void Postfix(PA_Modder_ItemRedesigner __instance, Dictionary<string, int> __state)
            {
                if (__state == null) return;
                // Selected clears targetItem through Game.ClearTarget only, so the field still names the item.
                Item_Equipment item = Traverse.Create(__instance).Field("targetItem").GetValue<Item_Equipment>();
                if (item == null || item.GetInstanceID() != __state["__item"]) return;
                foreach (string t in KeptTags)
                {
                    if (__state.TryGetValue(t, out int v)) item.SetTag(t, v);
                    else item.RemoveTag(t);
                }
            }
        }
    }
}
