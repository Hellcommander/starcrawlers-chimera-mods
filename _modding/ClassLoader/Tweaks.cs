using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace SCClassLoader
{
    /// <summary>
    /// Named game settings a mod can change from json: { "tweaks": { "minionCap": 4 } }. Later mods win. Only the
    /// names listed in Known exist; anything else is reported in the log.
    /// </summary>
    internal static class Tweaks
    {
        internal static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["minionCap"] = "How many minions you can keep at once (vanilla 3).",
            ["rarityChanceRare"] = "Base % chance for a Rare drop (vanilla 10; grows with floor and Loot Find).",
            ["rarityChanceEpic"] = "Base % chance for an Epic drop (vanilla 5).",
            ["rarityChanceLegendary"] = "Base % chance for a Legendary drop (vanilla 1).",
            ["vendorMarkup"] = "Vendors sell at item value x this (vanilla 2).",
            ["vendorMarkdown"] = "Vendors buy at item value / this (vanilla 2).",
            ["stashPages"] = "Stash pages (overrides the [Stash] Pages config).",
            ["hotbarPages"] = "Hotbar pages (overrides the [Hotbar] Pages config).",
            ["breachChance"] = "% chance a new floor gets a side-area breach (overrides the [SideAreas] config).",
            ["breachCacheItems"] = "Bonus items in each side area (overrides the [SideAreas] config).",
        };

        private static readonly Dictionary<string, double> values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> setBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static void Load()
        {
            values.Clear();
            setBy.Clear();
            foreach (ModPack p in ModPacks.Ordered)
                foreach (var (file, obj) in p.data)
                {
                    if (!(obj["tweaks"] is JObject t)) continue;
                    foreach (JProperty prop in t.Properties())
                    {
                        if (!Known.ContainsKey(prop.Name)) { Plugin.Log.LogWarning($"{file}: unknown tweak '{prop.Name}' (known: {string.Join(", ", Known.Keys)})"); continue; }
                        if (prop.Value.Type != JTokenType.Integer && prop.Value.Type != JTokenType.Float) { Plugin.Log.LogWarning($"{file}: tweak '{prop.Name}' needs a number"); continue; }
                        if (setBy.TryGetValue(prop.Name, out string prev)) Plugin.Log.LogInfo($"Tweaks: {p.id} overrides {prop.Name} set by {prev}");
                        values[prop.Name] = (double)prop.Value;
                        setBy[prop.Name] = p.id;
                    }
                }
            foreach (var kv in values) Plugin.Log.LogInfo($"Tweaks: {kv.Key} = {kv.Value} ({setBy[kv.Key]})");
        }

        internal static bool Has(string name) => values.ContainsKey(name);

        /// <summary>For self-tests only.</summary>
        internal static void Set(string name, double v) => values[name] = v;

        internal static int Int(string name, int fallback) => values.TryGetValue(name, out double v) ? (int)Math.Round(v) : fallback;

        /// <summary>Settings held in game objects; run on every Game.Start, after the scene's managers exist.</summary>
        internal static void Apply()
        {
            LootManager lm = LootManager.instance;
            if (lm != null)
            {
                lm.rarityChance_rare = Int("rarityChanceRare", lm.rarityChance_rare);
                lm.rarityChance_epic = Int("rarityChanceEpic", lm.rarityChance_epic);
                lm.rarityChance_legendary = Int("rarityChanceLegendary", lm.rarityChance_legendary);
            }
        }

        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.GetPlayerMinionMax))]
        private static class Patch_MinionCap
        {
            private static void Postfix(ref int __result)
            {
                if (Has("minionCap")) __result += Int("minionCap", 3) - 3; // relative, so mod-summon allowances still stack
            }
        }

        [HarmonyPatch(typeof(Vendor), nameof(Vendor.Awake))]
        private static class Patch_Vendor
        {
            private static void Prefix(Vendor __instance)
            {
                __instance.vendor_markup = Math.Max(1, Int("vendorMarkup", __instance.vendor_markup));
                __instance.vendor_markdown = Math.Max(1, Int("vendorMarkdown", __instance.vendor_markdown));
            }
        }
    }
}
