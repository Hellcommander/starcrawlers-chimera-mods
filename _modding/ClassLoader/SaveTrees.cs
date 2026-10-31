using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// The load screen shows each save's skill trees (up to seven) as a row of icons with their ranks, so you can
    /// tell your characters apart by build, not just by name. Trees from mods that aren't installed are left out.
    /// </summary>
    internal static class SaveTrees
    {
        private const float Icon = 44f, Gap = 5f;
        private static readonly Dictionary<string, (DateTime when, string[] trees, int[] ranks)> cache = new Dictionary<string, (DateTime, string[], int[])>();

        /// <summary>The save's tree ids and ranks (rank 0 = first tier), read straight from the file.</summary>
        internal static (string[] trees, int[] ranks) Read(string path)
        {
            DateTime when = File.GetLastWriteTimeUtc(path);
            if (cache.TryGetValue(path, out var hit) && hit.when == when) return (hit.trees, hit.ranks);
            string[] trees = null; int[] ranks = null;
            using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (new BinaryFormatter().Deserialize(fs) is SaveData sd && sd.iSaveData is SaveGameInfo info && info.playerData is PlayerSaveInfo ps)
                {
                    trees = ps.skillTrees;
                    ranks = ps.skillTreeRanks;
                }
            }
            cache[path] = (when, trees, ranks);
            return (trees, ranks);
        }

        private static void Add(SaveWidget w)
        {
            if (w == null || string.IsNullOrEmpty(w.path) || !File.Exists(w.path) || w.rect == null) return;
            var (trees, ranks) = Read(w.path);
            if (trees == null) return;
            var byId = (Game.instance?.skills ?? new List<Skill_Tree>()).Where(t => t != null)
                .GroupBy(t => t.internalName).ToDictionary(g => g.Key, g => g.First());

            Transform old = w.rect.Find("SCCL_SaveTrees");
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            var row = UiKit.Rect(w.rect, "SCCL_SaveTrees", Vector2.zero);
            UiLook.Attach(row, "SaveTrees");
            // under the save's info text, lined up with it (the bottom right holds a frame of the widget's own)
            row.anchorMin = row.anchorMax = new Vector2(0f, 0f);
            row.pivot = new Vector2(0f, 0f);
            float left = 260f;
            if (w.saveInfo != null)
            {
                var c = new Vector3[4];
                w.saveInfo.rectTransform.GetWorldCorners(c);
                left = w.rect.InverseTransformPoint(c[0]).x - w.rect.rect.xMin;
            }
            row.anchoredPosition = new Vector2(left, 10f);

            TMP_FontAsset font = UiKit.FontFrom(w);
            var shown = new List<(Skill_Tree tree, int rank)>();
            for (int i = 0; i < trees.Length; i++)
                if (!string.IsNullOrEmpty(trees[i]) && byId.TryGetValue(trees[i], out Skill_Tree t))
                    shown.Add((t, ranks != null && i < ranks.Length ? ranks[i] : 0));
            row.sizeDelta = new Vector2(shown.Count * (Icon + Gap), Icon);
            for (int i = 0; i < shown.Count; i++)
            {
                var (t, rank) = shown[i];
                RectTransform cell = UiKit.Panel(row, t.internalName, new Vector2(Icon, Icon), new Color(0f, 0f, 0f, 0.5f));
                cell.anchorMin = cell.anchorMax = new Vector2(0f, 0f);
                cell.pivot = new Vector2(0f, 0f);
                cell.anchoredPosition = new Vector2(i * (Icon + Gap), 0f);
                cell.GetComponent<Image>().raycastTarget = false;
                RectTransform pic = UiKit.Rect(cell, "Icon", Vector2.zero);
                pic.anchorMin = Vector2.zero; pic.anchorMax = Vector2.one;
                pic.offsetMin = new Vector2(2f, 2f); pic.offsetMax = new Vector2(-2f, -2f);
                var img = pic.gameObject.AddComponent<Image>();
                img.sprite = t.icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
                // tiers unlocked, on a small dark plate in the bottom-right corner
                RectTransform plate = UiKit.Panel(cell, "Rank", new Vector2(20f, 18f), new Color(0f, 0f, 0f, 0.75f));
                plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(1f, 0f);
                plate.anchoredPosition = Vector2.zero;
                plate.GetComponent<Image>().raycastTarget = false;
                TextMeshProUGUI lbl = UiKit.Label(plate, (rank + 1).ToString(), font, 16f);
                lbl.rectTransform.offsetMin = lbl.rectTransform.offsetMax = Vector2.zero;
                lbl.color = new Color(1f, 0.85f, 0.4f);
                lbl.overflowMode = TextOverflowModes.Overflow; // a line is a touch taller than the plate; don't cut it to nothing
            }
        }

        private static void AddAll(Menu_TitleLoad menu)
        {
            foreach (SaveWidget w in menu.saveWidgets)
            {
                try { Add(w); }
                catch (Exception e) { Plugin.Log.LogWarning($"Save trees for {w?.path}: {e}"); }
            }
        }

        [HarmonyPatch(typeof(Menu_TitleLoad), nameof(Menu_TitleLoad.CreateSaveWidgets))]
        private static class Patch_Widgets
        {
            private static void Postfix(Menu_TitleLoad __instance) => AddAll(__instance);
        }

        /// <summary>The list is first built at startup, before mod trees exist: redo the rows whenever it's opened.</summary>
        [HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
        private static class Patch_Show
        {
            private static void Postfix(Menu __instance) { if (__instance is Menu_TitleLoad m) AddAll(m); }
        }
    }
}
