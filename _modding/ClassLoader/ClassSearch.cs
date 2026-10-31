using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// What the creation screen's class search looks through: for every tree its name, archetype and description, each
    /// tier's skills (names, descriptions, perks), the stats its tiers raise, and its synergies. Words match from their
    /// start, so "crit" finds "critical" but not "hypocrite", and every word typed has to be found somewhere in the class.
    /// </summary>
    internal static class ClassSearch
    {
        private class Entry
        {
            public string label, text;
        }

        private static readonly Dictionary<Skill_Tree, List<Entry>> index = new Dictionary<Skill_Tree, List<Entry>>();
        private static readonly Regex RichText = new Regex("<[^>]*>");
        private static readonly Regex Tag = new Regex(@"\[([A-Z]+)\]");
        private static readonly HashSet<string> Defaults = new HashSet<string> { "Skill Tree", "Skill Tree Description", "Skill Tier", "Skill Tier Description", "Ability", "AbilityDescription" };

        // the stat tags in descriptions draw as icons, so the words have to come from here
        private static readonly Dictionary<string, string> TagWords = new Dictionary<string, string>
        {
            { "STR", "Strength" }, { "TGH", "Toughness" }, { "AGL", "Agility" }, { "REC", "Recognition" }, { "SAV", "Savvy" },
            { "SMT", "Smarts" }, { "STL", "Stealth" }, { "PSI", "Psi Power" }, { "HP", "Health" }, { "ARM", "Armor" }, { "CRIT", "Critical" },
        };

        private static readonly Dictionary<STAT_TYPE, string> StatWords = new Dictionary<STAT_TYPE, string>
        {
            { STAT_TYPE.Crit, "Critical Chance" }, { STAT_TYPE.HealthMax, "Max Health" }, { STAT_TYPE.PsiPower, "Psi Power" },
            { STAT_TYPE.MeleePower, "Melee Power" }, { STAT_TYPE.RangedPower, "Ranged Power" }, { STAT_TYPE.RangedAccuracy, "Ranged Accuracy" },
            { STAT_TYPE.HealthRegen, "Health Regen" }, { STAT_TYPE.DodgeReduction, "Dodge Reduction" }, { STAT_TYPE.LootFind, "Loot Find" },
            { STAT_TYPE.ShieldMax, "Max Shield" }, { STAT_TYPE.ShieldRegen, "Shield Regen" }, { STAT_TYPE.VoidEnergy, "Void Energy" }, { STAT_TYPE.StaminaRegen, "Stamina Regen" },
        };

        /// <summary>Forget the index: a new language or newly loaded classes change what it holds.</summary>
        internal static void Reset() => index.Clear();

        private static string[] Terms(string query) =>
            (query ?? "").ToLowerInvariant().Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.StartsWith("crit") ? "crit" : t).ToArray(); // skills say crit, crits and critical alike

        private static bool HasWord(string text, string term)
        {
            for (int i = text.IndexOf(term, System.StringComparison.Ordinal); i >= 0; i = text.IndexOf(term, i + 1, System.StringComparison.Ordinal))
                if (i == 0 || !char.IsLetterOrDigit(text[i - 1])) return true;
            return false;
        }

        internal static bool Matches(Skill_Tree tree, string query)
        {
            string[] terms = Terms(query);
            if (terms.Length == 0) return true;
            if (tree == null || tree.internalName == "NYI") return false;
            List<Entry> entries = Entries(tree);
            return terms.All(t => entries.Any(e => HasWord(e.text, t)));
        }

        /// <summary>Where a class matched, for the hover text: "Found in: Lucky Shot, Tier stats".</summary>
        internal static string Why(Skill_Tree tree, string query)
        {
            string[] terms = Terms(query);
            if (terms.Length == 0 || tree == null) return "";
            var labels = Entries(tree).Where(e => terms.Any(t => HasWord(e.text, t))).Select(e => e.label).Distinct().ToList();
            if (labels.Count == 0) return "";
            string shown = string.Join(", ", labels.Take(4));
            return labels.Count > 4 ? $"{shown} and {labels.Count - 4} more" : shown;
        }

        private static List<Entry> Entries(Skill_Tree tree)
        {
            if (index.TryGetValue(tree, out List<Entry> known)) return known;
            var list = new List<Entry>();
            void Add(string label, params string[] parts)
            {
                string text = string.Join(" ", parts.Select(Clean).Where(p => p != ""));
                if (text != "") list.Add(new Entry { label = label, text = text.ToLowerInvariant() });
            }

            Add("Name", tree.displayName);
            Add("Archetype", tree.skillClass.ToString());
            Add("Description", tree.description);
            foreach (Skill_Tier tier in tree.skillTiers.Where(t => t != null))
            {
                string name = new[] { Clean(tier.displayName), Clean(tier.ability != null ? (string)tier.ability.displayName : ""), Clean(tier.passive != null ? (string)tier.passive.displayName : "") }
                    .FirstOrDefault(n => n != "") ?? tree.displayName;
                Add(name, tier.displayName, tier.descrption);
                foreach (Ability a in new[] { tier.ability, tier.passive }.Where(a => a != null))
                    Add(name, a.displayName, a.description, a.perkDescription);
                foreach (StatObj s in (tier.attributeChanges ?? new StatObj[0]).Where(s => s != null && s.amount > 0))
                    Add($"{StatName(s.statType)} +{s.amount}", StatName(s.statType));
            }
            foreach (SynergySpec s in Synergies.Involving(tree))
                Add("Synergy: " + s.name, s.name, s.description);
            index[tree] = list;
            return list;
        }

        private static string StatName(STAT_TYPE s) => StatWords.TryGetValue(s, out string words) ? words : s.ToString();

        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text) || Defaults.Contains(text)) return "";
            text = RichText.Replace(text, " ");
            return Tag.Replace(text, m => TagWords.TryGetValue(m.Groups[1].Value, out string word) ? word : " ").Trim();
        }
    }

    /// <summary>
    /// The search box over the creation grid. Typing narrows the grid to the matching classes, still in their archetype
    /// columns (CreationLayout does the arranging). Hovering a match says where it matched. Esc in the box clears it
    /// instead of leaving the screen.
    /// </summary>
    internal static class CreationSearch
    {
        private const float BoxWidth = 340f, BoxHeight = 38f, StatusWidth = 200f, Above = 74f; // beside the "Select Your Skills" title

        private static TMP_InputField box;
        private static TextMeshProUGUI status;
        private static Menu_Creation menu;
        private static bool swallowEscape;

        internal static string Query => box != null ? box.text.Trim() : "";

        /// <summary>Builds the box once per screen, clears it on every Show. `viewport` is the grid's scroll view.</summary>
        internal static void Ensure(Menu_Creation mc, RectTransform viewport)
        {
            menu = mc;
            ClassSearch.Reset();
            if (box == null)
            {
                TMP_FontAsset font = UiKit.FontFrom(mc);
                var parent = (RectTransform)viewport.parent;
                box = UiKit.TextBox(parent, "Search: name, skill, crit, savvy...", font, 20f, new Vector2(BoxWidth, BoxHeight));
                var rt = (RectTransform)box.transform;
                rt.anchorMin = rt.anchorMax = viewport.anchorMin;
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = viewport.anchoredPosition + new Vector2(-viewport.sizeDelta.x * 0.5f, Above);
                box.onValueChanged.AddListener(_ => Changed());
                box.onEndEdit.AddListener(_ => { if (box.wasCanceled) { swallowEscape = true; box.text = ""; } });

                RectTransform st = UiKit.Rect(parent, "SCCL_SearchStatus", new Vector2(StatusWidth, BoxHeight));
                st.anchorMin = st.anchorMax = viewport.anchorMin;
                st.pivot = new Vector2(0f, 0f);
                st.anchoredPosition = rt.anchoredPosition + new Vector2(viewport.sizeDelta.x - StatusWidth, 0f);
                status = UiKit.Label(st, "", font, 18f, TextAlignmentOptions.Right);
            }
            box.SetTextWithoutNotify("");
            status.text = "";
        }

        internal static void SetQuery(string text) => box.text = text; // fires Changed

        private static void Changed()
        {
            if (menu == null) return;
            CreationLayout.Arrange(menu);
            int found = menu.madSkills.Count(s => s != null && s.gameObject.activeSelf);
            status.text = Query == "" ? "" : found == 0 ? "<color=#FFC35C>No class matches</color>" : $"<color=#9FB7C9>{found} {(found == 1 ? "class" : "classes")}</color>";
        }

        /// <summary>Hidden cells are still real: presets and Random pick them, and the picked copy starts out hidden too.</summary>
        [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.Skill_Select))]
        private static class Patch_Select
        {
            private static void Postfix(Menu_Creation __instance)
            {
                foreach (SkillSelect s in __instance.selectedSkills)
                    if (s != null && !s.gameObject.activeSelf) s.gameObject.SetActive(true);
            }
        }

        [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.Skill_Highlight))]
        private static class Patch_Highlight
        {
            private static void Postfix(Menu_Creation __instance, SkillSelect s)
            {
                string why = ClassSearch.Why(s?.skillTree, Query);
                if (why != "") __instance.skillDescription.text += $"\n<color=#7FE0C8>Found in: {why}</color>";
            }
        }

        /// <summary>Typing (a rebound Esc key could be a letter) and the release of the Esc that cleared the box stay here.</summary>
        [HarmonyPatch(typeof(Menu_Creation), "Update")]
        private static class Patch_Keys
        {
            private static bool Prefix()
            {
                if (swallowEscape)
                {
                    if (Input.GetKeyUp(KeyCode.Escape)) swallowEscape = false;
                    return false;
                }
                return box == null || !box.isFocused;
            }
        }
    }
}
