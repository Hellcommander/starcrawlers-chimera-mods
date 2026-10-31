using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// Class items (Flux Savant runes, Rig Jockey hardpoints, anything with "forClasses") only drop as loot when a
    /// body in your party can use them: you, or one of your Conjoined bodies. A roll that lands on one nobody can use
    /// is rolled again ([Loot] ClassItemsForParty). Vendors and quest rewards that name the item still give it.
    /// </summary>
    internal static class ClassLoot
    {
        internal static ConfigEntry<bool> Enabled;
        private const int Rerolls = 8;

        /// <summary>Internal item id -> the class ids it is for (items not in here are for everyone).</summary>
        private static Dictionary<string, HashSet<string>> owners;
        private static bool rolling;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Loot", "ClassItemsForParty", true,
                "Class-specific items (runes, rig hardware, items marked forClasses) only drop when you or another of your bodies has a class that uses them.");
        }

        /// <summary>Who an item is for; rebuilt whenever the item list is (a mod reload makes new ids).</summary>
        private static Dictionary<string, HashSet<string>> Owners()
        {
            if (owners != null && owners.Count <= ItemRegistry.Defs.Count && owners.Keys.All(ItemRegistry.Defs.ContainsKey)) return owners;
            owners = new Dictionary<string, HashSet<string>>();
            var classes = ClassRegistry.AllDefs.Where(c => c != null && c.enabled).ToList();
            foreach (var kv in ItemRegistry.Defs)
            {
                ItemDef d = kv.Value;
                if (d == null) continue;
                var who = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (d.forClasses != null && d.forClasses.Count > 0)
                    foreach (string c in d.forClasses.Where(x => !string.IsNullOrEmpty(x)))
                        who.Add(classes.FirstOrDefault(x => string.Equals(x.name, c, StringComparison.OrdinalIgnoreCase))?.id ?? c);
                else if (d.rune != null)
                {
                    // the classes of the rune's own mod with sockets it fits; a mod of runes alone fits every socket class
                    var fits = classes.Where(c => Takes(c, d.rune)).ToList();
                    var own = fits.Where(c => c.pack != null && c.pack == d.pack).ToList();
                    foreach (ClassDef c in own.Count > 0 ? own : fits) who.Add(c.id);
                }
                if (who.Count > 0) owners[kv.Key] = who;
            }
            return owners;
        }

        private static bool Takes(ClassDef c, RuneSpec r) =>
            (c.tiers ?? new List<TierDef>()).Any(t => t?.newAbility != null && t.newAbility.runeSlots > 0
                && (t.newAbility.runeKinds == null || t.newAbility.runeKinds.Count == 0 || t.newAbility.runeKinds.Any(k => string.Equals(k, r.kind, StringComparison.OrdinalIgnoreCase))));

        /// <summary>The trees every body in the party has: yours, and your other Conjoined bodies' saved ones.</summary>
        internal static HashSet<string> PartyTrees()
        {
            var trees = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Skill_Tree t in Player_SkillControl.instance?.skillTrees ?? new Skill_Tree[0])
                if (t != null && !string.IsNullOrEmpty(t.internalName)) trees.Add(t.internalName);
            if (Conjoined.IsConjoined)
                foreach (Conjoined.Body b in Conjoined.Bodies)
                    foreach (string t in b.Save?.skillTrees ?? new string[0])
                        if (!string.IsNullOrEmpty(t)) trees.Add(t);
            return trees;
        }

        /// <summary>Can someone in the party use it? (Always true for items that aren't class items.)</summary>
        internal static bool Wanted(Item i)
        {
            if (i == null || string.IsNullOrEmpty(i.itemId) || !Owners().TryGetValue(i.itemId, out var who)) return true;
            return PartyTrees().Overlaps(who);
        }

        /// <summary>For tests and the log: the class ids an item is for (empty = anyone).</summary>
        internal static IEnumerable<string> For(string itemId) =>
            itemId != null && Owners().TryGetValue(itemId, out var who) ? who : Enumerable.Empty<string>();

        [HarmonyPatch(typeof(LootManager), nameof(LootManager.Generate_Loot),
            new[] { typeof(LOOT_TYPE), typeof(RARITY), typeof(bool), typeof(float), typeof(int), typeof(Container), typeof(bool) })]
        private static class Patch_Roll
        {
            private static void Postfix(LootManager __instance, LOOT_TYPE requestedType, RARITY requestedRarity, bool isContainer, float lfm,
                int lootFloorOverride, Container container, bool isQuestReward, ref Item __result)
            {
                if (rolling || __result == null || !Enabled.Value || Player.instance == null || Wanted(__result)) return;
                rolling = true;
                try
                {
                    for (int n = 0; n < Rerolls && __result != null && !Wanted(__result); n++)
                    {
                        Object.Destroy(__result.gameObject);
                        __result = __instance.Generate_Loot(requestedType, requestedRarity, isContainer, lfm, lootFloorOverride, container, isQuestReward);
                    }
                    if (__result != null && !Wanted(__result)) { Object.Destroy(__result.gameObject); __result = null; }
                }
                catch (Exception e) { Plugin.Log.LogWarning("Class loot: " + e.Message); }
                finally { rolling = false; }
            }
        }
    }
}
