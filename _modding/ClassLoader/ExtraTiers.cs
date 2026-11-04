using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// "addTiers": a mod adds skills to an existing class, vanilla or modded, after its normal ones:
    /// <code>
    /// { "addTiers": { "SKILLTREE_Cybertechnician": { "fillUnfinished": true, "effects": [ ... ], "tiers": [ { "newAbility": { ... } } ] } } }
    /// </code>
    /// Tiers go after the class's last tier, up to the skills screen's slots (TierSlots, 9 at most). With
    /// "fillUnfinished", the first ones take the place of the game's "* NYI *" placeholder tiers instead (keeping the
    /// placeholder's attribute and resistance bonuses unless the tier gives its own). Effects and "@Name" references belong
    /// to the adding mod, and paths are relative to its file, so several mods can add to one class. The added skills rank
    /// with the class they join. Turning the mod off takes them away again: a vanilla tree is never changed itself, the
    /// game is handed a copy with the extra tiers, and a save ranked into them keeps its rank for when the mod is back.
    /// </summary>
    internal static class ExtraTiers
    {
        /// <summary>The game's own trees we handed a copy for, by id, so a new game start begins from them again.</summary>
        private static readonly Dictionary<string, Skill_Tree> originals = new Dictionary<string, Skill_Tree>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Class id -> the mods that added tiers to it (for logs and the catalog).</summary>
        internal static readonly Dictionary<string, List<string>> AddedBy = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Tiers a plugin or script added in its OnLoad (PluginContext.AddTiers, mod.addTiers), applied after the json ones.</summary>
        private static readonly List<(ModPack pack, string folder, string classId, JObject spec)> queued = new List<(ModPack, string, string, JObject)>();

        internal static void Queue(ModPack pack, string folder, string classId, JObject spec)
        {
            if (string.IsNullOrEmpty(classId) || spec == null) throw new ArgumentException("AddTiers needs a class id and an object with \"tiers\"");
            queued.Add((pack, folder, classId, spec));
        }

        private class Spec
        {
            public bool fillUnfinished;
            public List<EffectSpec> effects;
            public List<TierDef> tiers;
        }

        /// <summary>A placeholder tier the game never finished.</summary>
        internal static bool Unfinished(Skill_Tier t)
        {
            if (t == null) return true;
            Ability a = t.ability != null ? t.ability : t.passive;
            if (a != null && a.internalName == "NYI") return true;
            string n = a != null ? (string)a.displayName : null;
            if (string.IsNullOrEmpty(n)) n = t.displayName;
            return n != null && n.Contains("NYI");
        }

        /// <summary>After ClassRegistry.Register: adds every loaded mod's tiers to their classes.</summary>
        internal static void Apply(Game game, Transform holder)
        {
            // start from the game's own trees each time
            for (int i = 0; i < game.skills.Count; i++)
                if (game.skills[i] != null && originals.TryGetValue(game.skills[i].internalName, out Skill_Tree orig) && game.skills[i] != orig)
                {
                    Object.Destroy(game.skills[i].gameObject);
                    game.skills[i] = orig;
                }
            originals.Clear();
            AddedBy.Clear();

            foreach (ModPack pack in ModPacks.Ordered.Where(ModPacks.IsLoaded))
                foreach (var (file, obj) in pack.data)
                {
                    if (!(obj["addTiers"] is JObject adds)) continue;
                    foreach (var prop in adds.Properties())
                    {
                        try { Add(game, holder, pack, file, prop.Name, prop.Value as JObject); }
                        catch (Exception e) { Plugin.Log.LogError($"{file}: tiers for '{prop.Name}' could not be added: {e}"); }
                    }
                }
            foreach (var (pack, folder, classId, spec) in queued)
            {
                if (!ModPacks.IsLoaded(pack)) continue;
                string where = Path.Combine(folder, pack.id + " (code)");
                try { Add(game, holder, pack, where, classId, (JObject)spec.DeepClone()); }
                catch (Exception e) { Plugin.Log.LogError($"{pack.id}: tiers for '{classId}' could not be added: {e}"); }
            }
        }

        /// <summary>The mods that added tiers to a class (empty when none).</summary>
        /// <summary>The game's own trees that were replaced by copies with added tiers: what a filled placeholder held
        /// (an unfinished tier's ability and the prefabs on it) is still found there.</summary>
        internal static IEnumerable<Skill_Tree> Originals => originals.Values;

        internal static IReadOnlyList<string> AddedTo(string classId) =>
            classId != null && AddedBy.TryGetValue(classId, out var by) ? by : (IReadOnlyList<string>)new string[0];

        private static void Add(Game game, Transform holder, ModPack pack, string file, string treeId, JObject o)
        {
            if (o == null) { Plugin.Log.LogError($"{file}: addTiers '{treeId}' needs an object with \"tiers\""); return; }
            foreach (var t in (o["tiers"] as JArray) ?? new JArray())
                if (t is JObject tj && tj["newAbility"] is JObject na) tj["newAbility"] = ClassRegistry.ResolveBase(na, file, 0);
            Spec spec = o.ToObject<Spec>();
            if (spec?.tiers == null || spec.tiers.Count == 0) { Plugin.Log.LogError($"{file}: addTiers '{treeId}' has no tiers"); return; }

            int at = game.skills.FindIndex(s => s != null && s.isLive && string.Equals(s.internalName, treeId, StringComparison.OrdinalIgnoreCase));
            if (at < 0) { Plugin.Log.LogWarning($"{file}: addTiers: no class '{treeId}' (its mod may be off); skipped"); return; }
            Skill_Tree tree = game.skills[at];
            if (!ClassRegistry.Templates.Contains(tree) && !originals.ContainsKey(tree.internalName))
            {
                // a vanilla tree: the game gets a copy, its own stays untouched
                Skill_Tree copy = Object.Instantiate(tree, holder, false);
                copy.name = tree.name;
                originals[tree.internalName] = tree;
                game.skills[at] = copy;
                tree = copy;
            }

            var def = new ClassDef
            {
                id = $"ADD_{new string((pack.id ?? "Mod").Where(char.IsLetterOrDigit).ToArray())}_{tree.internalName}",
                name = tree.displayName,
                folder = Path.GetDirectoryName(file),
                sourceFile = file,
                pack = pack,
                effects = spec.effects ?? new List<EffectSpec>(),
                tiers = spec.tiers,
            };
            ClassRegistry.SetFolder(def.id, def.folder);
            Sprite icon = tree.icon;
            ClassRegistry.RegisterEffects(def, icon);

            var open = new Queue<int>(spec.fillUnfinished ? Enumerable.Range(0, tree.skillTiers.Count).Where(i => Unfinished(tree.skillTiers[i])) : Enumerable.Empty<int>());
            int added = 0, filled = 0;
            foreach (TierDef td in spec.tiers)
            {
                bool fill = open.Count > 0;
                int slot = fill ? open.Dequeue() : tree.skillTiers.Count;
                if (!fill && slot >= Plugin.MaxTiers)
                {
                    Plugin.Log.LogWarning($"{file}: {tree.internalName} has no room past {Plugin.MaxTiers} tiers (TierSlots); {spec.tiers.Count - added - filled} added tier(s) dropped");
                    break;
                }
                if (td.newAbility == null) { Plugin.Log.LogError($"{file}: an added tier for {tree.internalName} needs a \"newAbility\""); continue; }
                if (fill)
                {
                    Skill_Tier old = tree.skillTiers[slot];
                    td.attributes = td.attributes ?? StatsOf(old?.attributeChanges);
                    td.resistances = td.resistances ?? ResistsOf(old?.resistanceChanges);
                }
                Skill_Tier made = ClassRegistry.NewTier(def, slot, td, tree, icon);
                if (made == null) continue;
                if (fill)
                {
                    Skill_Tier old = tree.skillTiers[slot];
                    tree.skillTiers[slot] = made;
                    if (old != null && old.transform.IsChildOf(tree.transform)) Object.Destroy(old.gameObject);
                    made.transform.SetSiblingIndex(slot);
                    filled++;
                }
                else { tree.skillTiers.Add(made); added++; }
            }
            ClassRegistry.FillEffectIcons(def, icon);
            if (!AddedBy.TryGetValue(tree.internalName, out var by)) AddedBy[tree.internalName] = by = new List<string>();
            by.Add(pack.name ?? pack.id);
            Plugin.Log.LogInfo($"Class {tree.internalName}: {pack.id} filled {filled} unfinished tier(s) and added {added}, now {tree.skillTiers.Count} tiers");
        }

        private static Dictionary<string, int> StatsOf(StatObj[] s) =>
            s == null || s.Length == 0 ? null : s.Where(x => x != null).GroupBy(x => x.statType.ToString()).ToDictionary(g => g.Key, g => (int)g.Sum(x => x.amount));

        private static Dictionary<string, int> ResistsOf(DmgObj[] r) =>
            r == null || r.Length == 0 ? null : r.Where(x => x != null).GroupBy(x => x.dmgType.ToString()).ToDictionary(g => g.Key, g => g.Sum(x => x.damage));

        /// <summary>
        /// A save ranked into tiers a mod added keeps its rank while the mod is off, so it counts as maxed with fewer
        /// tiers: the skills screen never offers (or reaches past) a tier that isn't there.
        /// </summary>
        [HarmonyPatch(typeof(Skill_Tree), nameof(Skill_Tree.Maxed))]
        private static class Patch_Maxed
        {
            private static void Postfix(Skill_Tree __instance, ref bool __result)
            {
                if (!__result && __instance.skillTiers != null && __instance.rank >= __instance.skillTiers.Count - 1) __result = true;
            }
        }
    }
}
