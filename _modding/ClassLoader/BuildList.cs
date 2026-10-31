using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// Saved builds: the creation screen's custom preset button keeps a list instead of one build. Right-click it to
    /// define a build from the current pick (it's added to the list, or replaces the one with the same name); left-click opens
    /// the list to load or delete one. Stored in SCCL_Builds.json next to Game.scc. The vanilla single preset is
    /// imported once and is kept in step with whatever build was last loaded or saved.
    /// </summary>
    internal static class BuildList
    {
        internal class Build
        {
            public string name;
            public List<string> trees = new List<string>();
        }

        private static List<Build> builds;
        private static RectTransform listPanel;

        private static string FilePath => Path.Combine(Application.persistentDataPath, "SCCL_Builds.json");

        internal static List<Build> All
        {
            get
            {
                if (builds != null) return builds;
                builds = new List<Build>();
                try
                {
                    if (File.Exists(FilePath)) builds = JsonConvert.DeserializeObject<List<Build>>(File.ReadAllText(FilePath)) ?? new List<Build>();
                    else if (PlayerPrefs.HasKey("CustomPresetChar_0"))
                    {
                        builds.Add(new Build { name = PlayerPrefs.GetString("CustomPresetChar_Name", "Custom Preset"), trees = VanillaTrees() });
                        Save();
                    }
                }
                catch (Exception e) { Plugin.Log.LogError("Saved builds: " + e.Message); }
                return builds;
            }
        }

        private static List<string> VanillaTrees() =>
            Enumerable.Range(0, 7).Select(i => PlayerPrefs.GetString("CustomPresetChar_" + i, "")).Where(s => !string.IsNullOrEmpty(s)).ToList();

        private static void Save()
        {
            try { File.WriteAllText(FilePath, JsonConvert.SerializeObject(builds, Formatting.Indented)); }
            catch (Exception e) { Plugin.Log.LogError("Saved builds: " + e.Message); }
        }

        internal static void AddOrReplace(string name, List<string> trees)
        {
            if (trees == null || trees.Count == 0) return;
            name = string.IsNullOrWhiteSpace(name) ? $"Build {All.Count + 1}" : name.Trim();
            All.RemoveAll(b => string.Equals(b.name, name, StringComparison.OrdinalIgnoreCase));
            All.Add(new Build { name = name, trees = trees.ToList() });
            Save();
        }

        internal static void Delete(Build b)
        {
            All.Remove(b);
            Save();
        }

        /// <summary>Writes a build into the vanilla preset keys (so the vanilla loader and button art use it).</summary>
        private static void MakeCurrent(Build b)
        {
            PlayerPrefs.SetString("CustomPresetChar_Name", b.name);
            for (int i = 0; i < 7; i++) PlayerPrefs.SetString("CustomPresetChar_" + i, i < b.trees.Count ? b.trees[i] : "");
            PlayerPrefs.Save();
        }

        private static void Load(Menu_Creation mc, Build b)
        {
            CloseList();
            MakeCurrent(b);
            if (mc.customPresetName != null) mc.customPresetName.text = b.name;
            SkillSelect first = mc.madSkills.FirstOrDefault(s => s?.skillTree != null && s.skillTree.internalName == b.trees.FirstOrDefault());
            if (mc.presetImage != null && first != null) mc.presetImage.sprite = first.skillTree.icon;
            loading = true;
            try { mc.SetPreset("customchar"); }
            finally { loading = false; }
        }

        private static bool loading;

        /// <summary>Vanilla's define mode, minus the wipe: the classes already picked stay picked and become the build.</summary>
        internal static void Define(Menu_Creation mc)
        {
            var tr = Traverse.Create(mc);
            if (tr.Field("isRandomizingSkills").GetValue<bool>()) return;
            tr.Field("isDefiningPreset").SetValue(true);
            mc.confirmPreset.gameObject.SetActive(true);
            mc.beginButton.gameObject.SetActive(false);
            mc.inputBaseText.SetTerm("UI/Enter Preset Name");
            SkillSelect first = mc.selectedSkills.FirstOrDefault(s => s != null);
            mc.presetTip.SetActive(first == null);
            if (first != null) mc.presetImage.sprite = first.skillTree.icon;
            AudioManager.PlayEvent("event:/UI/UI_Button_Keystroke", mc.transform.position);
            if (!string.IsNullOrEmpty(mc.nameInput.text)) mc.PresetNameCheck(); // a name typed beforehand counts
        }

        // ---- the list -----------------------------------------------------------------------------------------

        internal static bool ListOpen => listPanel != null;

        private static void CloseList()
        {
            if (listPanel != null) UnityEngine.Object.Destroy(listPanel.gameObject);
            listPanel = null;
        }

        private static void OpenList(Menu_Creation mc)
        {
            CloseList();
            var anchor = (RectTransform)(mc.customPresetName.GetComponentInParent<Button>()?.transform ?? mc.customPresetName.transform);
            Canvas canvas = anchor.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var font = mc.customPresetName.font;
            const float w = 420f, row = 44f;
            var items = All.ToList();
            float h = 56f + Mathf.Max(1, items.Count) * (row + 6f) + 50f;
            listPanel = UiKit.Panel(canvas.transform, "SCCL_BuildList", new Vector2(w, h), UiKit.PanelColor);
            var corners = new Vector3[4];
            anchor.GetWorldCorners(corners);
            listPanel.pivot = new Vector2(0f, 0f);
            listPanel.position = corners[1]; // sits on top of the preset button
            UiLook.Attach(listPanel, "BuildList");
            UiLook.Grip(listPanel);
            var title = UiKit.Label(UiKit.Rect(listPanel, "Title", new Vector2(w, 46f)), "Saved builds", font, 26);
            UiKit.At((RectTransform)title.transform.parent, 0f, 6f);
            float y = 56f;
            if (items.Count == 0)
            {
                var none = UiKit.Label(UiKit.Rect(listPanel, "None", new Vector2(w, row)), "<color=#9FB7C9>None yet. Right-click the button to save one.</color>", font, 20);
                UiKit.At((RectTransform)none.transform.parent, 0f, y);
                y += row + 6f;
            }
            foreach (Build b in items)
            {
                Build bb = b;
                var load = UiKit.Button(listPanel, $"{b.name}  <size=70%><color=#9FB7C9>{b.trees.Count} classes</color></size>", font, 22, new Vector2(w - 80f, row), () => Load(mc, bb));
                UiKit.At((RectTransform)load.transform, 12f, y);
                Button del = null;
                bool armed = false;
                del = UiKit.Button(listPanel, "X", font, 22, new Vector2(48f, row), () =>
                {
                    if (!armed) { armed = true; del.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "Del?"; return; }
                    Delete(bb);
                    OpenList(mc);
                }, UiKit.DangerColor);
                UiKit.At((RectTransform)del.transform, w - 60f, y);
                y += row + 6f;
            }
            var close = UiKit.Button(listPanel, "Close", font, 20, new Vector2(120f, 38f), CloseList);
            UiKit.At((RectTransform)close.transform, w - 132f, y + 2f);
            var hint = UiKit.Label(UiKit.Rect(listPanel, "Hint", new Vector2(w - 150f, 38f)), "<color=#9FB7C9>Right-click the button: save the current pick</color>", font, 16, TMPro.TextAlignmentOptions.Left);
            UiKit.At((RectTransform)hint.transform.parent, 8f, y + 2f);
        }

        // ---- patches ------------------------------------------------------------------------------------------

        /// <summary>Left-click on the custom preset button opens the list (unless we're loading from it).</summary>
        [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.SetPreset))]
        private static class Patch_SetPreset
        {
            private static bool Prefix(Menu_Creation __instance, string presetName)
            {
                if (presetName != "customchar") { CloseList(); return true; }
                if (loading) return true;
                bool define = Input.GetMouseButtonDown(1) || Keybindings.GetInstance().GetButton("Square");
                if (define) { CloseList(); Define(__instance); return false; }
                try { OpenList(__instance); }
                catch (Exception e) { Plugin.Log.LogError("Saved builds list: " + e); return true; }
                return false;
            }
        }

        /// <summary>Confirming a defined preset adds it to the list.</summary>
        [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.BTN_SetCustomConfirm))]
        private static class Patch_Confirm
        {
            private static void Prefix(Menu_Creation __instance, out List<string> __state) =>
                __state = __instance.selectedSkills.Where(s => s != null && s.skillTree != null).Select(s => s.skillTree.internalName).ToList();

            private static void Postfix(Menu_Creation __instance, List<string> __state)
            {
                AddOrReplace(__instance.customPresetName.text, __state);
                // Vanilla leaves old keys behind when the new build has fewer classes.
                for (int i = __state.Count; i < 7; i++) PlayerPrefs.SetString("CustomPresetChar_" + i, "");
                PlayerPrefs.Save();
            }
        }

        /// <summary>Cancelling define mode with Esc: vanilla never brings the begin button back, which strands a kept pick.</summary>
        [HarmonyPatch(typeof(Menu_Creation), "Update")]
        private static class Patch_CancelDefine
        {
            private static void Postfix(Menu_Creation __instance)
            {
                if (__instance.confirmPreset.gameObject.activeSelf || __instance.beginButton.gameObject.activeSelf) return;
                __instance.beginButton.gameObject.SetActive(true);
            }
        }

        [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.BTN_BeginGame))]
        private static class Patch_Begin
        {
            private static void Prefix() => CloseList();
        }
    }
}
