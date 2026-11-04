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
            /// <summary>UTC ticks of the last save or load, for the "Recent" sort. 0 in files from before it existed.</summary>
            public long used;
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
            // Replacing keeps the build's place in the list (and its row, if the list is built); a new name goes at the end.
            Build had = All.FirstOrDefault(b => string.Equals(b.name, name, StringComparison.OrdinalIgnoreCase));
            if (had != null) { had.name = name; had.trees = trees.ToList(); had.used = DateTime.UtcNow.Ticks; }
            else All.Add(new Build { name = name, trees = trees.ToList(), used = DateTime.UtcNow.Ticks });
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
            b.used = DateTime.UtcNow.Ticks;
            Save();
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
        // Built once per creation screen and then kept: closing hides it, opening syncs the rows with the saved builds
        // (adds rows for new ones, drops rows for deleted ones, refreshes labels). Searching, sorting and deleting only
        // re-place the existing rows.

        private enum SortBy { Saved, Name, Recent }
        private static readonly string[] SortLabels = { "Saved", "A-Z", "Recent" };
        private const string SortPref = "SCCL_BuildSort";

        private class Row
        {
            public Build build;
            public RectTransform load, del;
            public TMPro.TextMeshProUGUI label, delLabel;
            public bool armed;
            public string search; // name + class names, lower case
        }

        private const float W = 460f, RowH = 44f, Step = RowH + 6f, ToolbarY = 56f, ToolbarH = 36f, ViewY = 100f;
        private const int VisibleRows = 8; // past this the rows scroll instead of the panel growing off screen

        private static Menu_Creation menu;
        private static TMPro.TMP_FontAsset font;
        private static RectTransform content;
        private static ScrollRect scroll;
        private static TMPro.TMP_InputField searchBox;
        private static TMPro.TextMeshProUGUI status, sortLabel, noneLabel;
        private static readonly List<Row> rows = new List<Row>();
        private static SortBy sort = (SortBy)Mathf.Clamp(PlayerPrefs.GetInt(SortPref, 0), 0, 2);
        private static bool refilling;

        internal static bool ListOpen => listPanel != null && listPanel.gameObject.activeSelf;

        /// <summary>True while the player types in the list's search box, so the creation screen's own keys stay quiet.</summary>
        internal static bool Typing => ListOpen && searchBox != null && searchBox.isFocused;

        private static void CloseList()
        {
            if (listPanel != null) listPanel.gameObject.SetActive(false);
        }

        private static void OpenList(Menu_Creation mc)
        {
            if (listPanel == null || menu != mc) MakePanel(mc);
            if (listPanel == null) return;
            var anchor = (RectTransform)(mc.customPresetName.GetComponentInParent<Button>()?.transform ?? mc.customPresetName.transform);
            var corners = new Vector3[4];
            anchor.GetWorldCorners(corners);
            listPanel.position = corners[1]; // sits on top of the preset button
            listPanel.SetAsLastSibling();
            Sync();
            refilling = true;
            searchBox.text = ""; // each open starts unfiltered, like the class search
            refilling = false;
            listPanel.gameObject.SetActive(true);
            Layout();
            scroll.verticalNormalizedPosition = 1f;
        }

        private static void MakePanel(Menu_Creation mc)
        {
            if (listPanel != null) UnityEngine.Object.Destroy(listPanel.gameObject);
            listPanel = null;
            rows.Clear();
            menu = mc;
            var anchor = (RectTransform)(mc.customPresetName.GetComponentInParent<Button>()?.transform ?? mc.customPresetName.transform);
            Canvas canvas = anchor.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            font = mc.customPresetName.font;

            float viewH = VisibleRows * Step;
            float h = ViewY + viewH + 24f + 50f;
            listPanel = UiKit.Panel(canvas.transform, "SCCL_BuildList", new Vector2(W, h), UiKit.PanelColor);
            listPanel.pivot = new Vector2(0f, 0f);
            UiLook.Attach(listPanel, "BuildList");
            UiLook.Grip(listPanel);
            var title = UiKit.Label(UiKit.Rect(listPanel, "Title", new Vector2(W, 46f)), "Saved builds", font, 26);
            UiKit.At((RectTransform)title.transform.parent, 0f, 6f);

            // toolbar: search by build or class name, and the sort order (remembered between sessions)
            searchBox = UiKit.TextBox(listPanel, "Search builds or classes...", font, 19f, new Vector2(W - 24f - 136f, ToolbarH));
            UiKit.At((RectTransform)searchBox.transform, 12f, ToolbarY);
            searchBox.onValueChanged.AddListener(_ => { if (!refilling) { Layout(); scroll.verticalNormalizedPosition = 1f; } });
            searchBox.onEndEdit.AddListener(_ =>
            {
                if (!searchBox.wasCanceled) return;
                CreationSearch.SwallowEscape(); // Esc in the box clears it instead of leaving the screen
                searchBox.text = "";
            });
            var sortBtn = UiKit.Button(listPanel, "", font, 18, new Vector2(128f, ToolbarH), () =>
            {
                sort = (SortBy)(((int)sort + 1) % 3);
                PlayerPrefs.SetInt(SortPref, (int)sort);
                Layout();
            });
            UiKit.At((RectTransform)sortBtn.transform, W - 140f, ToolbarY);
            sortLabel = sortBtn.GetComponentInChildren<TMPro.TextMeshProUGUI>();

            // the rows live in a masked, scrolling viewport so a long list keeps the panel a fixed size
            RectTransform view = UiKit.Panel(listPanel, "Viewport", new Vector2(W, viewH), new Color(0f, 0f, 0f, 0f));
            UiKit.At(view, 0f, ViewY);
            view.gameObject.AddComponent<RectMask2D>();
            content = UiKit.Rect(view, "Content", Vector2.zero);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
            scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content; scroll.viewport = view;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.inertia = false;
            scroll.scrollSensitivity = Step * 0.5f;
            noneLabel = UiKit.Label(UiKit.Rect(view, "None", new Vector2(W, RowH)), "", font, 20);
            UiKit.At((RectTransform)noneLabel.transform.parent, 0f, 0f);

            status = UiKit.Label(UiKit.Rect(listPanel, "Status", new Vector2(W, 20f)), "", font, 15);
            UiKit.At((RectTransform)status.transform.parent, 0f, ViewY + viewH + 2f);
            float y = ViewY + viewH + 24f;
            var close = UiKit.Button(listPanel, "Close", font, 20, new Vector2(120f, 38f), CloseList);
            UiKit.At((RectTransform)close.transform, W - 132f, y + 2f);
            var hint = UiKit.Label(UiKit.Rect(listPanel, "Hint", new Vector2(W - 150f, 38f)), "<color=#9FB7C9>Right-click the button: save the current pick</color>", font, 16, TMPro.TextAlignmentOptions.Left);
            UiKit.At((RectTransform)hint.transform.parent, 8f, y + 2f);
            listPanel.gameObject.SetActive(false);
        }

        private static Row MakeRow(Build b)
        {
            var r = new Row { build = b };
            var load = UiKit.Button(content, "", font, 22, new Vector2(W - 84f, RowH), () => Load(menu, r.build));
            r.load = (RectTransform)load.transform;
            r.label = load.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            r.label.alignment = TMPro.TextAlignmentOptions.Left;
            var del = UiKit.Button(content, "X", font, 22, new Vector2(48f, RowH), () =>
            {
                if (!r.armed) { r.armed = true; r.delLabel.text = "Del?"; return; }
                Delete(r.build);
                UnityEngine.Object.Destroy(r.load.gameObject);
                UnityEngine.Object.Destroy(r.del.gameObject);
                rows.Remove(r);
                Layout(); // the rows below move up; the scroll position stays (clamped)
            }, UiKit.DangerColor);
            r.del = (RectTransform)del.transform;
            r.delLabel = del.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            return r;
        }

        /// <summary>Matches the rows to the saved builds: new builds get a row, deleted ones lose theirs, labels refresh.</summary>
        private static void Sync()
        {
            foreach (Row r in rows.Where(r => !All.Contains(r.build)).ToList())
            {
                UnityEngine.Object.Destroy(r.load.gameObject);
                UnityEngine.Object.Destroy(r.del.gameObject);
                rows.Remove(r);
            }
            foreach (Build b in All.Where(b => rows.All(r => r.build != b))) rows.Add(MakeRow(b));
            var names = Game.instance?.skills?.Where(t => t != null).GroupBy(t => t.internalName).ToDictionary(g => g.Key, g => g.First().displayName.ToString())
                        ?? new Dictionary<string, string>();
            foreach (Row r in rows)
            {
                r.armed = false;
                r.delLabel.text = "X";
                var classNames = r.build.trees.Select(id => names.TryGetValue(id, out string n) ? n : id).ToList();
                r.label.text = $"{r.build.name}  <size=70%><color=#9FB7C9>{r.build.trees.Count} classes</color></size>";
                r.search = (r.build.name + " " + string.Join(" ", classNames)).ToLowerInvariant();
            }
        }

        /// <summary>Filters by the search words (all must match the build's name or one of its classes), sorts, places.</summary>
        private static void Layout()
        {
            if (listPanel == null) return;
            sortLabel.text = "Sort: " + SortLabels[(int)sort];
            string[] words = (searchBox.text ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<Row> shown = rows.Where(r => words.All(w => r.search.Contains(w)));
            // OrderBy is stable, so ties keep the save order
            if (sort == SortBy.Name) shown = shown.OrderBy(r => r.build.name, StringComparer.OrdinalIgnoreCase);
            else if (sort == SortBy.Recent) shown = shown.OrderByDescending(r => r.build.used);
            else shown = shown.OrderBy(r => All.IndexOf(r.build));
            var list = shown.ToList();
            var visible = new HashSet<Row>(list);
            foreach (Row r in rows)
            {
                bool on = visible.Contains(r);
                r.load.gameObject.SetActive(on);
                r.del.gameObject.SetActive(on);
            }
            for (int i = 0; i < list.Count; i++)
            {
                UiKit.At(list[i].load, 12f, i * Step);
                UiKit.At(list[i].del, W - 64f, i * Step);
            }
            content.sizeDelta = new Vector2(0f, list.Count * Step);
            noneLabel.text = rows.Count == 0 ? "<color=#9FB7C9>None yet. Right-click the button to save one.</color>"
                           : list.Count == 0 ? "<color=#FFC35C>No build matches</color>" : "";
            status.text = words.Length > 0 && rows.Count > 0 ? $"<color=#9FB7C9>{list.Count} of {rows.Count} builds</color>"
                        : list.Count > VisibleRows ? $"<color=#6F8794>{list.Count} builds, scroll for more</color>" : "";
        }

        /// <summary>For the devtest: types into the list's search box.</summary>
        internal static void SetSearch(string text) { if (searchBox != null) searchBox.text = text; }

        /// <summary>For the devtest: the visible build names, top to bottom.</summary>
        internal static List<string> Shown() =>
            rows.Where(r => r.load.gameObject.activeSelf).OrderByDescending(r => r.load.anchoredPosition.y).Select(r => r.build.name).ToList();

        /// <summary>For the devtest: steps the sort button once.</summary>
        internal static void NextSort() => listPanel?.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<TMPro.TextMeshProUGUI>().text.StartsWith("Sort:")).onClick.Invoke();

        /// <summary>For the devtest: how many row objects exist (rebuild check).</summary>
        internal static int RowObjects => content != null ? content.childCount : 0;

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
