using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// "Mods" on the title screen: every mod under Mods/ with an on/off switch and its status, what it needs and what needs it,
    /// what it holds, which saves use its classes and what the log said about it this session. Choices save at once
    /// (<see cref="ModChoices"/>); mods load while the game starts, so the page shows what the next start will load (a
    /// requirement turned off takes its dependants with it) and offers a restart.
    /// </summary>
    internal static class ModsPage
    {
        private static Window win;
        internal static Window Win => win;

        private static readonly Color Green = new Color(0.55f, 0.9f, 0.6f), Amber = new Color(1f, 0.72f, 0.3f),
            Red = new Color(1f, 0.45f, 0.45f), Grey = new Color(0.6f, 0.66f, 0.72f), Dim = new Color(0.68f, 0.75f, 0.82f);

        // ---- What the log said this session (errors and warnings only)

        private static readonly List<(LogLevel level, string text)> lines = new List<(LogLevel, string)>();

        internal static void Listen(ManualLogSource log) => log.LogEvent += (s, e) =>
        {
            if ((e.Level & (LogLevel.Fatal | LogLevel.Error | LogLevel.Warning)) == 0) return;
            lock (lines) if (lines.Count < 2000) lines.Add((e.Level, e.Data?.ToString() ?? ""));
        };

        private static List<(LogLevel level, string text)> Lines() { lock (lines) return lines.ToList(); }

        /// <summary>This session's errors (not warnings), oldest first.</summary>
        internal static List<string> Errors() => Lines().Where(l => IsError(l.level)).Select(l => l.text).ToList();

        private static bool IsError(LogLevel l) => (l & (LogLevel.Fatal | LogLevel.Error)) != 0;

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        /// <summary>Starts the game again once this process has exited, then quits.</summary>
        internal static void Restart()
        {
            string exe = Paths.ExecutablePath.Replace("'", "''");
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            string cmd = $"Wait-Process -Id {pid} -Timeout 60 -ErrorAction SilentlyContinue; Start-Process -FilePath '{exe}' -WorkingDirectory '{Path.GetDirectoryName(exe)}'";
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -Command \"{cmd}\"")
                    { UseShellExecute = false, CreateNoWindow = true });
            }
            catch (Exception e) { Plugin.Log.LogError("Mods: could not schedule the restart, quitting only: " + e.Message); }
            Application.Quit();
        }

        /// <summary>Class id -> names of the saves whose characters have it.</summary>
        internal static Dictionary<string, List<string>> SavesByClass()
        {
            var use = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string dir = Path.Combine(Application.persistentDataPath, "Saves");
            foreach (string f in Directory.Exists(dir) ? Directory.GetFiles(dir, "*.dat") : new string[0])
            {
                try
                {
                    foreach (string t in SaveTrees.Read(f).trees ?? new string[0])
                    {
                        if (string.IsNullOrEmpty(t)) continue;
                        if (!use.TryGetValue(t, out var l)) use[t] = l = new List<string>();
                        l.Add(Path.GetFileNameWithoutExtension(f));
                    }
                }
                catch (Exception) { } // not a character save (or an unreadable one): nothing to list for it
            }
            return use;
        }

        internal class Window : MonoBehaviour
        {
            public TMP_FontAsset font;
            /// <summary>The full-screen backdrop the panel sits on; shown and hidden with it.</summary>
            public GameObject root;
            private RectTransform body;
            private ModPack selected;
            private float listScroll, infoScroll;
            /// <summary>Class id -> names of the saves that use it; read once per opening.</summary>
            private Dictionary<string, List<string>> saveUse;

            internal void Open()
            {
                saveUse = null;
                root.SetActive(true);
                root.transform.SetAsLastSibling();
                AudioManager.PlayEvent("event:/UI/UI_Click");
                Rebuild();
            }

            internal void Close() => root.SetActive(false);

            private void Update()
            {
                if (Input.GetKeyDown(KeyCode.Escape)) Close();
            }

            internal void Select(string id)
            {
                selected = ModPacks.Find(id);
                infoScroll = 0f;
                Rebuild();
            }

            internal void Toggle(ModPack p)
            {
                ModChoices.Set(p, !ModChoices.IsOn(p));
                selected = p;
                AudioManager.PlayEvent("event:/UI/UI_Click");
                Rebuild();
            }

            /// <summary>Turns on everything the mod needs (and what that needs) that is installed but off.</summary>
            internal void TurnOnNeeds(ModPack p)
            {
                var seen = new HashSet<ModPack>();
                void Up(ModPack m)
                {
                    if (!seen.Add(m)) return;
                    foreach (ModPack r in (m.requires ?? new List<string>()).Select(ModPacks.Find).Where(r => r != null))
                    {
                        if (!ModChoices.IsOn(r)) ModChoices.Set(r, true);
                        Up(r);
                    }
                }
                Up(p);
                AudioManager.PlayEvent("event:/UI/UI_Click");
                Rebuild();
            }

            /// <summary>Puts every choice back to what this session loaded with.</summary>
            private void Undo()
            {
                foreach (ModPack p in ModPacks.All) if (ModChoices.IsOn(p) != p.enabled) ModChoices.Set(p, p.enabled);
                AudioManager.PlayEvent("event:/UI/UI_Click");
                Rebuild();
            }

            private static void OpenFolder(string path)
            {
                try { System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\""); }
                catch (Exception e) { Plugin.Log.LogError($"Mods: could not open {path}: {e.Message}"); }
            }

            private Dictionary<string, List<string>> SaveUse() => saveUse ?? (saveUse = SavesByClass());

            /// <summary>Short status for the list and its colour, as of the next start.</summary>
            private static (string text, Color color) Status(ModPack p, ModPlan next)
            {
                bool now = ModPacks.IsLoaded(p);
                int at = next.Ordered.IndexOf(p);
                if (at >= 0) return now ? ($"Loaded  ·  #{at + 1} in load order", Green) : ("Loads after a restart", Amber);
                string why = next.Skipped.TryGetValue(p, out string w) ? w : "";
                if (why == ModPacks.Off) return now ? ("Unloads after a restart", Amber) : ("Off", Grey);
                if (why == ModPacks.NeedsOk) return ("Waiting for your OK to run its code", Amber);
                if (why == "") return ("Not loaded", Red);
                return (char.ToUpper(why[0]) + why.Substring(1), Red);
            }

            internal void Rebuild()
            {
                if (body != null) Destroy(body.gameObject);
                var size = ((RectTransform)transform).sizeDelta;
                body = UiKit.Rect(transform, "Body", size);
                body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one; body.offsetMin = body.offsetMax = Vector2.zero;
                float W = size.x, H = size.y;

                ModPlan next = ModPacks.Resolve(ModChoices.IsOn);
                var mods = ModPacks.All.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList();
                if (selected == null || !mods.Contains(selected)) selected = mods.FirstOrDefault();

                var log = Lines();
                int errors = log.Count(l => IsError(l.level)), warnings = log.Count - errors;
                string summary = $"{ModPacks.Ordered.Count} of {mods.Count} mods loaded";
                if (log.Count > 0) summary += $"  ·  this session's log: <color={Hex(errors > 0 ? Red : Grey)}>{ModPacks.Count(errors, "error", "errors")}</color>, " +
                    $"<color={Hex(warnings > 0 ? Amber : Grey)}>{ModPacks.Count(warnings, "warning", "warnings")}</color>";
                Text(body, "Mods", 26, 20f, 12f, 300f, 34f, TextAlignmentOptions.TopLeft, new Color(0.75f, 0.9f, 1f));
                Text(body, summary, 14, 20f, 48f, W - 340f, 22f, TextAlignmentOptions.TopLeft, Dim);
                Button(body, "Open Mods Folder", W - 266f, 12f, 160f, 30f, () => OpenFolder(ClassRegistry.ModsDir));
                Button(body, "Close", W - 96f, 12f, 80f, 30f, Close);

                float top = 80f, footer = 52f, listW = 420f, viewH = H - top - footer;
                ModList(mods, next, 16f, top, listW, viewH);
                Details(selected, next, 16f + listW + 12f, top, W - listW - 44f, viewH);
                Footer(next, W, H - footer + 10f);
            }

            private void ModList(List<ModPack> mods, ModPlan next, float x, float top, float w, float h)
            {
                RectTransform view = UiKit.Panel(body, "List", new Vector2(w, h), new Color(0f, 0f, 0f, 0.2f));
                UiKit.At(view, x, top);
                view.gameObject.AddComponent<RectMask2D>();
                RectTransform content = UiKit.Rect(view, "Content", new Vector2(w, h));
                UiKit.At(content, 0f, 0f);

                float y = 4f;
                foreach (ModPack p in mods)
                {
                    ModPack mod = p;
                    bool on = ModChoices.IsOn(p);
                    var (status, color) = Status(p, next);
                    Button row = Button(content, "", 4f, y, w - 8f, 54f, () => { selected = mod; infoScroll = 0f; Rebuild(); },
                        p == selected ? new Color(0.1f, 0.24f, 0.3f, 0.95f) : new Color(0.04f, 0.08f, 0.11f, 0.9f));
                    row.gameObject.name = "SCCL_ModRow_" + p.id;
                    Button(content, on ? "On" : "Off", 12f, y + 12f, 54f, 30f, () => Toggle(mod), on ? UiKit.OnColor : UiKit.ButtonColor)
                        .gameObject.name = "SCCL_ModToggle_" + p.id;
                    Text(content, p.Title, 17, 76f, y + 6f, w - 160f, 24f, TextAlignmentOptions.TopLeft, on ? Color.white : Grey);
                    if (!string.IsNullOrEmpty(p.version)) Text(content, "v" + p.version, 13, w - 84f, y + 8f, 70f, 20f, TextAlignmentOptions.TopRight, Grey);
                    Text(content, status, 13, 76f, y + 30f, w - 90f, 20f, TextAlignmentOptions.TopLeft, color);
                    y += 58f;
                }
                content.sizeDelta = new Vector2(w, Mathf.Max(h, y + 4f));
                Scroll(view, content, listScroll, v => listScroll = v);
            }

            private void Details(ModPack p, ModPlan next, float x, float top, float w, float h)
            {
                RectTransform view = UiKit.Panel(body, "Details", new Vector2(w, h), new Color(0f, 0f, 0f, 0.2f));
                UiKit.At(view, x, top);
                view.gameObject.AddComponent<RectMask2D>();
                RectTransform content = UiKit.Rect(view, "Content", new Vector2(w, h));
                UiKit.At(content, 0f, 0f);
                if (p == null)
                {
                    Text(content, "No mods found in the Mods folder.", 16, 16f, 16f, w - 32f, 24f, TextAlignmentOptions.TopLeft, Dim);
                    return;
                }

                float cx = 16f, cw = w - 32f;
                bool on = ModChoices.IsOn(p);
                Button(content, on ? "On" : "Off", w - 106f, 12f, 90f, 30f, () => Toggle(p), on ? UiKit.OnColor : UiKit.ButtonColor);
                float y = Para(content, p.Title, 22, cx, 10f, cw - 110f, Color.white);
                string meta = string.Join("  ·  ", new[] { p.id, string.IsNullOrEmpty(p.version) ? null : "v" + p.version,
                    string.IsNullOrEmpty(p.author) ? null : "by " + p.author, p.hasManifest ? null : "no mod.json" }.Where(s => s != null));
                y = Para(content, meta, 14, cx, y, cw - 110f, Dim);
                if (!string.IsNullOrEmpty(p.license)) y = Para(content, "License: " + p.license, 13, cx, y, cw, Grey);

                var (status, color) = Status(p, next);
                if (next.Skipped.TryGetValue(p, out string why) && why != ModPacks.Off && ModPacks.IsLoaded(p)) status += "  ·  still loaded until a restart";
                y = Para(content, status, 15, cx, y + 4f, cw, color);
                var offNeeds = (p.requires ?? new List<string>()).Select(ModPacks.Find).Where(r => r != null && !next.Ordered.Contains(r)).ToList();
                if (on && offNeeds.Count > 0)
                {
                    Button(content, "Turn On What It Needs", cx, y, 220f, 30f, () => TurnOnNeeds(p), UiKit.OnColor).gameObject.name = "SCCL_ModNeeds";
                    y += 38f;
                }
                if (!string.IsNullOrEmpty(p.description)) y = Para(content, p.description, 15, cx, y + 4f, cw, new Color(0.88f, 0.92f, 0.95f));
                y += 8f;
                if (p.scriptFiles.Count > 0)
                {
                    y = Field(content, "Scripts", string.Join(", ", p.scriptFiles.Select(Path.GetFileName)), cx, y, cw);
                    y = Para(content, "Scripts run in a sandbox: they reach the game only through the scripting API, never your files or the internet, so they need no OK.", 14, cx, y, cw, Grey);
                }
                if (p.pluginFiles.Count > 0) y = Code(content, p, cx, y, cw);

                string root = Path.GetFullPath(Path.Combine(ClassRegistry.ModsDir, ".."));
                string rel = p.folder.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? p.folder.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar) : p.folder;
                Button(content, "Open Folder", w - 136f, y - 2f, 120f, 26f, () => OpenFolder(p.folder));
                y = Field(content, "Folder", rel, cx, y, cw - 130f);

                string Dep(string id)
                {
                    ModPack d = ModPacks.Find(id);
                    if (d == null) return $"<color={Hex(Red)}>{id} (not installed)</color>";
                    bool loads = next.Ordered.Contains(d);
                    return $"<color={Hex(loads ? Green : Red)}>{d.Title}{(loads ? "" : ModChoices.IsOn(d) ? " (not loaded)" : " (off)")}</color>";
                }
                if (p.requires?.Count > 0) y = Field(content, "Needs", string.Join(", ", p.requires.Select(Dep)), cx, y, cw);
                if (p.loadAfter?.Count > 0)
                    y = Field(content, "Loads after", string.Join(", ", p.loadAfter.Select(id => ModPacks.Find(id) == null ? $"<color={Hex(Grey)}>{id} (not installed)</color>" : Dep(id))), cx, y, cw);
                var neededBy = ModPacks.All.Where(o => o.requires?.Any(r => string.Equals(r, p.id, StringComparison.OrdinalIgnoreCase)) == true)
                    .OrderBy(o => o.Title).Select(o => Dep(o.id)).ToList();
                if (neededBy.Count > 0) y = Field(content, "Needed by", string.Join(", ", neededBy), cx, y, cw);
                y = Field(content, "Contents", ModPacks.Contents(p), cx, y, cw);

                // Saves whose characters have this mod's classes: without the mod, that skill slot loads empty.
                var use = SaveUse();
                var saves = p.classes.Where(c => ModPacks.PatchTarget(p, c.id) == null).SelectMany(c => use.TryGetValue(c.id, out var l) ? l.Select(s => (save: s, cls: c.name)) : Enumerable.Empty<(string save, string cls)>())
                    .GroupBy(t => t.save).OrderBy(g => g.Key).Select(g => $"{g.Key} ({string.Join(", ", g.Select(t => t.cls).Distinct())})").ToList();
                if (saves.Count > 0)
                {
                    bool loads = next.Ordered.Contains(p);
                    y = Field(content, "Saves using its classes", string.Join(", ", saves), cx, y, cw);
                    if (!loads) y = Para(content, "While this mod is not loaded, those characters load with that skill slot empty.", 14, cx, y, cw, Amber);
                }

                y += 6f;
                var mine = Lines().Where(l => ModPacks.Mentions(p, l.text, ClassRegistry.ModsDir)).ToList();
                y = Para(content, mine.Count == 0 ? "Nothing about this mod in this session's log." : $"This session's log ({ModPacks.Count(mine.Count, "line", "lines")}):",
                    14, cx, y, cw, mine.Count == 0 ? Grey : Dim);
                foreach (var (level, text) in mine.Take(15))
                    y = Para(content, text.Length > 400 ? text.Substring(0, 400) + "…" : text, 13, cx + 10f, y, cw - 10f, IsError(level) ? Red : Amber);
                if (mine.Count > 15) y = Para(content, $"…and {mine.Count - 15} more in BepInEx/LogOutput.log", 13, cx + 10f, y, cw - 10f, Grey);

                content.sizeDelta = new Vector2(w, Mathf.Max(h, y + 12f));
                Scroll(view, content, infoScroll, v => infoScroll = v);
            }

            /// <summary>What a mod's plugin code is, and the player's say on whether it runs.</summary>
            private float Code(RectTransform content, ModPack p, float x, float y, float w)
            {
                y = Field(content, "Code", string.Join(", ", p.pluginFiles.Select(Path.GetFileName)), x, y, w);
                y = Para(content, "This mod runs its own program inside the game, with the same access to your computer as the game itself. " +
                    "Only allow it if you trust where it came from.", 14, x, y, w, Amber);
                void Set(Action change)
                {
                    change();
                    AudioManager.PlayEvent("event:/UI/UI_Click");
                    Rebuild();
                }
                if (ModChoices.CodeOk(p))
                {
                    string how = ModChoices.AnyCodeOk(p) ? "Allowed, even after it changes." : "Allowed as it is now; you will be asked again if it changes.";
                    y = Para(content, how, 14, x, y, w - 150f, Green);
                    Button(content, "Stop Allowing", x + w - 140f, y - 26f, 140f, 26f, () => Set(() => ModChoices.ForbidCode(p))).gameObject.name = "SCCL_ModForbidCode";
                    return y + 6f;
                }
                Button(content, "Allow This Version", x, y, 170f, 30f, () => Set(() => ModChoices.AllowCode(p)), UiKit.OnColor).gameObject.name = "SCCL_ModAllowCode";
                Button(content, "Always Allow", x + 180f, y, 130f, 30f, () => Set(() => ModChoices.AllowCode(p, always: true))).gameObject.name = "SCCL_ModAlwaysAllowCode";
                return y + 40f;
            }

            private void Footer(ModPlan next, float W, float y)
            {
                var add = next.Ordered.Where(p => !ModPacks.IsLoaded(p)).Select(p => p.Title).ToList();
                var drop = ModPacks.Ordered.Where(p => !next.Ordered.Contains(p)).Select(p => p.Title).ToList();
                if (add.Count + drop.Count == 0)
                {
                    Text(body, "Choices are saved at once. Mods load while the game starts, so a change applies after a restart.", 14, 20f, y + 6f, W - 40f, 22f,
                        TextAlignmentOptions.TopLeft, Dim);
                    return;
                }
                string s = "After a restart: " + string.Join(";  ", new[] { add.Count > 0 ? "loads " + string.Join(", ", add) : null,
                    drop.Count > 0 ? "unloads " + string.Join(", ", drop) : null }.Where(t => t != null));
                Text(body, s, 14, 20f, y + 6f, W - 350f, 22f, TextAlignmentOptions.TopLeft, Amber);
                Button(body, "Undo Changes", W - 316f, y, 140f, 32f, Undo);
                Button(body, "Restart Now", W - 166f, y, 150f, 32f, Restart, UiKit.OnColor).gameObject.name = "SCCL_ModRestart";
            }

            private static void Scroll(RectTransform view, RectTransform content, float at, Action<float> keep)
            {
                var sr = view.gameObject.AddComponent<ScrollRect>();
                sr.content = content;
                sr.horizontal = false;
                sr.movementType = ScrollRect.MovementType.Clamped;
                sr.scrollSensitivity = 30f;
                sr.verticalNormalizedPosition = 1f - at;
                sr.onValueChanged.AddListener(v => keep(1f - v.y));
            }

            private float Field(Transform parent, string label, string value, float x, float y, float w) =>
                Para(parent, $"<color=#9FB4C4>{label}:</color> {value}", 15, x, y, w, Color.white);

            /// <summary>Wrapped text at (x, y); returns the y below it.</summary>
            private float Para(Transform parent, string text, float size, float x, float y, float w, Color color)
            {
                TextMeshProUGUI t = Text(parent, text, size, x, y, w, 20f, TextAlignmentOptions.TopLeft, color);
                t.enableWordWrapping = true;
                t.overflowMode = TextOverflowModes.Overflow;
                float h = Mathf.Max(size + 6f, t.GetPreferredValues(text, w, 0f).y);
                ((RectTransform)t.transform.parent).sizeDelta = new Vector2(w, h);
                return y + h + 4f;
            }

            private TextMeshProUGUI Text(Transform parent, string text, float size, float x, float y, float w, float h, TextAlignmentOptions align, Color color)
            {
                RectTransform box = UiKit.Rect(parent, "Text", new Vector2(w, h));
                UiKit.At(box, x, y);
                TextMeshProUGUI t = UiKit.Label(box, text, font, size, align);
                ((RectTransform)t.transform).offsetMin = ((RectTransform)t.transform).offsetMax = Vector2.zero;
                t.color = color;
                return t;
            }

            private Button Button(Transform parent, string label, float x, float y, float w, float h, Action click, Color? color = null)
            {
                Button b = UiKit.Button(parent, label, font, 14, new Vector2(w, h), click, color);
                UiKit.At((RectTransform)b.transform, x, y);
                return b;
            }
        }

        private static Menu_Title builtFor;

        /// <summary>The title menu starts before the loader can patch it, so each new one is fitted out from here.</summary>
        internal static void Tick()
        {
            Menu_Title title = Menu_Title.instance;
            if (title == null || title == builtFor) return;
            builtFor = title;
            Build(title);
        }

        /// <summary>Builds the page on the title screen and adds a "Mods" button under Options.</summary>
        private static void Build(Menu_Title title)
        {
            try
            {
                // The menu object sits outside the UI; its buttons are on the title canvas.
                GameObject src = title.optionsButton;
                Canvas canvas = src != null ? src.GetComponentInParent<Canvas>() : null;
                if (canvas == null) { Plugin.Log.LogWarning("Mods page: the title menu's Options button is missing or not on a canvas"); return; }
                canvas = canvas.rootCanvas;

                RectTransform back = UiKit.Panel(canvas.transform, "SCCL_ModManager", Vector2.zero, new Color(0f, 0f, 0f, 0.82f));
                back.anchorMin = Vector2.zero; back.anchorMax = Vector2.one; back.offsetMin = back.offsetMax = Vector2.zero;
                RectTransform w = UiKit.Panel(back, "Panel", new Vector2(1100f, 680f), new Color(0.03f, 0.06f, 0.09f, 1f));
                w.anchorMin = w.anchorMax = w.pivot = new Vector2(0.5f, 0.5f);
                w.anchoredPosition = Vector2.zero;
                w.gameObject.AddComponent<Outline>().effectColor = new Color(0.3f, 0.6f, 0.7f, 0.8f);
                win = w.gameObject.AddComponent<Window>();
                win.font = UiKit.FontFrom(title);
                win.root = back.gameObject;
                UiLook.Attach(back, "ModsPage", w);
                UiLook.Grip(back);
                back.gameObject.SetActive(false);

                GameObject copy = UnityEngine.Object.Instantiate(src, src.transform.parent, false);
                copy.name = "SCCL_ModsButton";
                copy.SetActive(true);
                // The original's label is localized; drop that so ours keeps its own text.
                foreach (Component c in copy.GetComponentsInChildren<Component>(true))
                    if (c != null && c.GetType().Name.Contains("Localize")) UnityEngine.Object.DestroyImmediate(c);
                foreach (TMP_Text t in copy.GetComponentsInChildren<TMP_Text>(true)) t.text = "Mods";
                Button b = copy.GetComponentInChildren<Button>(true);
                if (b != null)
                {
                    b.onClick = new Button.ButtonClickedEvent();
                    b.onClick.AddListener(() => win?.Open());
                }
                copy.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);
                if (src.transform.parent.GetComponent<LayoutGroup>() == null)
                    Plugin.Log.LogInfo("Mods button: the title menu has no layout group; " + string.Join(", ", src.transform.parent.Cast<Transform>()
                        .Select(t => $"{t.name}@{((RectTransform)t).anchoredPosition}")));
            }
            catch (Exception e) { Plugin.Log.LogError("Mods page: " + e); }
        }
    }
}
