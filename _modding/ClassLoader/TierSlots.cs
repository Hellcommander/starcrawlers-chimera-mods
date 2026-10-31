using System;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// The skills screens ship 8 tier slots per tree (vanilla trees use 7) and have room for one more.
    /// Clones the last slot until each row has TierSlots slots; the row's HorizontalLayoutGroup places them.
    /// </summary>
    internal static class TierSlots
    {
        internal static ConfigEntry<int> Target;

        internal static void Bind(ConfigFile cfg) =>
            Target = cfg.Bind("Classes", "TierSlots", 9,
                new ConfigDescription("Tier slots per tree on the skills screens. 9 is what fits without overflowing.", new AcceptableValueRange<int>(7, 9)));

        internal static void Extend(Menu_Skills ms)
        {
            if (ms == null || !ms.gameObject.scene.IsValid()) return; // leave prefab assets alone
            ms.tree_0_tiers = Grow(ms.tree_0_tiers);
            ms.tree_1_tiers = Grow(ms.tree_1_tiers);
            ms.tree_2_tiers = Grow(ms.tree_2_tiers);
            ms.tree_3_tiers = Grow(ms.tree_3_tiers);
            ms.tree_4_tiers = Grow(ms.tree_4_tiers);
            ms.tree_5_tiers = Grow(ms.tree_5_tiers);
            ms.tree_6_tiers = Grow(ms.tree_6_tiers);
        }

        private static SkillSelect[] Grow(SkillSelect[] row)
        {
            int want = Target?.Value ?? 9;
            if (row == null || row.Length == 0 || row.Length >= want || row[row.Length - 1] == null) return row;
            SkillSelect last = row[row.Length - 1];
            var list = row.ToList();
            while (list.Count < want)
            {
                SkillSelect copy = Object.Instantiate(last, last.transform.parent, false);
                copy.name = "Tier_" + list.Count;
                copy.transform.SetSiblingIndex(list[list.Count - 1].transform.GetSiblingIndex() + 1);
                copy.gameObject.SetActive(false);
                list.Add(copy);
            }
            return list.ToArray();
        }

        /// <summary>Smallest row across every live skills screen, i.e. how many tiers a class can show.</summary>
        internal static int Count()
        {
            int min = int.MaxValue;
            foreach (Menu_Skills ms in Resources.FindObjectsOfTypeAll<Menu_Skills>().Where(m => m != null && m.gameObject.scene.IsValid()))
            {
                Extend(ms);
                foreach (SkillSelect[] arr in new[] { ms.tree_0_tiers, ms.tree_1_tiers, ms.tree_2_tiers, ms.tree_3_tiers, ms.tree_4_tiers, ms.tree_5_tiers, ms.tree_6_tiers })
                    if (arr != null && arr.Length > 0) min = Math.Min(min, arr.Length);
            }
            return min == int.MaxValue ? 0 : min;
        }
    }

    /// <summary>
    /// The panel only fits 8 slots at vanilla spacing. Rows that show more get tighter spacing so the whole row
    /// stays as wide as 8 vanilla slots; rows of 8 or fewer keep the original look.
    /// </summary>
    [HarmonyPatch(typeof(Menu_Skills), nameof(Menu_Skills.Refresh))]
    internal static class Patch_TierSpacing
    {
        private const int FitsAtVanillaSpacing = 8;
        private static readonly System.Collections.Generic.Dictionary<int, float> original = new System.Collections.Generic.Dictionary<int, float>();

        private static void Postfix(Menu_Skills __instance)
        {
            try
            {
                var rows = new[] { __instance.tree_0_tiers, __instance.tree_1_tiers, __instance.tree_2_tiers, __instance.tree_3_tiers,
                                   __instance.tree_4_tiers, __instance.tree_5_tiers, __instance.tree_6_tiers };
                for (int i = 0; i < rows.Length; i++)
                {
                    SkillSelect[] row = rows[i];
                    if (row == null || row.Length == 0 || row[0] == null) continue;
                    var group = row[0].transform.parent?.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                    if (group == null) continue;
                    int id = group.GetInstanceID();
                    if (!original.TryGetValue(id, out float spacing)) original[id] = spacing = group.spacing;
                    int shown = row.Count(s => s != null && s.gameObject.activeSelf);
                    float width = (row[0].transform as RectTransform)?.rect.width ?? 80f;
                    float want = shown <= FitsAtVanillaSpacing ? spacing
                        : (FitsAtVanillaSpacing * width + (FitsAtVanillaSpacing - 1) * spacing - shown * width) / (shown - 1);
                    if (!Mathf.Approximately(group.spacing, want)) group.spacing = want;
                    // the game lays the bar behind a row out for slots 100 apart: in a tighter row it would run on past the
                    // last lit slot and out through the side of the panel
                    var bar = i < __instance.skillTrees.Length ? __instance.skillTrees[i]?.bgBar?.rectTransform : null;
                    if (bar != null && shown > FitsAtVanillaSpacing)
                    {
                        float lit = (bar.sizeDelta.x + 15f) / 100f - 1f;
                        bar.sizeDelta = new Vector2(lit * (width + want) + 85f, bar.sizeDelta.y);
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Tier spacing: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Menu_Skills), nameof(Menu_Skills.Awake))]
    internal static class Patch_TierSlots
    {
        private static void Prefix(Menu_Skills __instance)
        {
            try { TierSlots.Extend(__instance); }
            catch (Exception e) { Plugin.Log.LogWarning("Extra tier slots: " + e.Message); }
        }
    }
}
