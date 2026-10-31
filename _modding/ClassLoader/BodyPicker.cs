using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// The panel that opens when a new Conjoined body joins, laid out like the starting class picker: tree icons in
    /// Muscle / Guile / Intellect columns (scrolling), the hovered tree's name and description below, its synergies
    /// in a panel beside it, and the chosen classes in a bar along the bottom (Conjoined is always the first). On the
    /// left: copy the classes of the body you're playing, roll random ones, or load one of your saved builds.
    /// The creation screen's "can't be combined" rules apply. "Later" puts it off until the next floor or rank-up.
    /// </summary>
    internal static class BodyPicker
    {
        private const float Cell = 76f, Gap = 10f, GroupGap = 34f, Side = 230f, M = 16f;
        private const int VisibleRows = 5;
        private static readonly SKILL_CLASS[] Groups = { SKILL_CLASS.Muscle, SKILL_CLASS.Guile, SKILL_CLASS.Intellect };
        private static readonly Color Frame = new Color(0.16f, 0.32f, 0.38f, 1f), FrameOn = new Color(0.45f, 0.9f, 1f, 1f);

        private static RectTransform panel, descFrame;
        private static readonly List<string> picked = new List<string>();
        private static int need;
        private static readonly Dictionary<string, (Image frame, Image icon, RectTransform rt)> cells = new Dictionary<string, (Image, Image, RectTransform)>();
        private static readonly List<Image> slots = new List<Image>();
        private static Button join;
        private static TextMeshProUGUI countText, descName, descText, message;
        private static TMP_FontAsset font;

        internal static bool Open => panel != null;
        internal static IReadOnlyList<string> Picked => picked;

        private static IEnumerable<Skill_Tree> Trees =>
            Game.instance.skills.Where(s => s != null && s.isLive && s.internalName != "NYI" && s.internalName != Conjoined.ClassId && !Unlocks.IsLocked(s.internalName));

        private static Skill_Tree Tree(string id) => Game.instance.skills.FirstOrDefault(s => s != null && s.internalName == id);

        internal static void Show(int count)
        {
            Close();
            if (Menu_HUD.instance == null) return;
            Canvas canvas = Menu_HUD.instance.GetComponentInParent<Canvas>()?.rootCanvas ?? Object.FindObjectsOfType<Canvas>().FirstOrDefault(c => c.isRootCanvas);
            if (canvas == null) return;
            need = count;
            picked.Clear(); cells.Clear(); slots.Clear();
            font = UiKit.FontFrom(Menu_HUD.instance);

            float groupW = 3 * Cell + 2 * Gap, gridW = 3 * groupW + 2 * GroupGap, viewH = VisibleRows * (Cell + Gap);
            float gx = M + Side + 2 * M; // grid's left edge
            float w = gx + gridW + 20f + M, h = 980f;
            panel = UiKit.Panel(canvas.transform, "SCCL_BodyPicker", new Vector2(w, h), UiKit.PanelColor);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            UiLook.Attach(panel, "BodyPicker");
            UiLook.Collapsible(panel, "New body", UiLook.Corner.InsideLeft, false);
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.3f, 0.7f, 0.85f, 0.8f);
            outline.effectDistance = new Vector2(2f, -2f);

            Text(panel, "<color=#FFD15C>Another body pulls free</color>", 32, new Vector2(w, 46f), 0f, 10f, TextAlignmentOptions.Center);
            Text(panel, $"It has Conjoined and starts at your level with every point unspent and nothing equipped. Choose its other {need} classes.",
                18, new Vector2(w, 30f), 0f, 58f, TextAlignmentOptions.Center);

            // ---- left: quick fills and saved builds, like the creation screen's presets
            float sy = 110f;
            Btn("Copy my classes", () => Fill(Player_SkillControl.instance.skillTrees.Where(t => t != null && t.internalName != Conjoined.ClassId).Select(t => t.internalName)),
                "Pick the same classes as the body you're playing.", ref sy);
            Btn("Random", () => Fill(Trees.Select(t => t.internalName).OrderBy(_ => Random.value)), "Roll a random set (no clashing classes).", ref sy);
            Btn("Clear", () => { picked.Clear(); Refresh(); }, "Start over.", ref sy);
            sy += 12f;
            Text(panel, "<color=#9FB7C9>Saved builds</color>", 20, new Vector2(Side, 30f), M, sy, TextAlignmentOptions.Left);
            sy += 34f;
            var builds = BuildList.All.ToList();
            if (builds.Count == 0) Text(panel, "<color=#6F8794>None yet: define one with the creation screen's custom preset.</color>", 15, new Vector2(Side, 60f), M, sy, TextAlignmentOptions.TopLeft).enableWordWrapping = true;
            foreach (BuildList.Build build in builds.Take(12))
            {
                BuildList.Build bb = build;
                string names = string.Join(", ", bb.trees.Where(x => x != Conjoined.ClassId).Select(id => Tree(id)?.displayName.ToString() ?? $"<color=#FF7272>{id} (not installed)</color>"));
                Btn(build.name, () => Fill(bb.trees.Where(x => x != Conjoined.ClassId)), names, ref sy);
            }

            // ---- the grid: three archetype columns of icons, scrolling
            for (int g = 0; g < 3; g++)
                Text(panel, Groups[g].ToString(), 28, new Vector2(groupW, 40f), gx + g * (groupW + GroupGap), 104f, TextAlignmentOptions.Center);
            RectTransform view = UiKit.Panel(panel, "Viewport", new Vector2(gridW + 20f, viewH), new Color(0f, 0f, 0f, 0f));
            UiKit.At(view, gx, 150f);
            view.gameObject.AddComponent<RectMask2D>();
            RectTransform content = UiKit.Rect(view, "Content", Vector2.zero);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
            int rows = 0;
            for (int g = 0; g < 3; g++)
            {
                var list = Trees.Where(t => t.skillClass == Groups[g]).OrderBy(t => ClassRegistry.IsModTree(t.internalName)).ThenBy(t => t.displayName.ToString()).ToList();
                rows = Mathf.Max(rows, Mathf.CeilToInt(list.Count / 3f));
                for (int i = 0; i < list.Count; i++)
                    MakeCell(content, list[i], g * (groupW + GroupGap) + (i % 3) * (Cell + Gap), (i / 3) * (Cell + Gap));
            }
            content.sizeDelta = new Vector2(0f, rows * (Cell + Gap));
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content; scroll.viewport = view;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.inertia = false;
            scroll.scrollSensitivity = Cell * 0.5f;
            if (rows > VisibleRows)
            {
                Text(panel, "<color=#6F8794>scroll for more</color>", 15, new Vector2(gridW, 20f), gx, 150f + viewH + 2f, TextAlignmentOptions.Center);
            }

            // ---- description of the hovered tree
            descFrame = UiKit.Panel(panel, "Description", new Vector2(gridW + 20f, 150f), new Color(0.02f, 0.05f, 0.07f, 0.9f));
            UiKit.At(descFrame, gx, 150f + viewH + 26f);
            descName = UiKit.Label(descFrame, "", font, 30, TextAlignmentOptions.Left);
            descName.rectTransform.anchorMax = new Vector2(0.3f, 1f);
            descText = UiKit.Label(descFrame, "<color=#6F8794>Hover a class to read about it and see what it combines with.</color>", font, 19, TextAlignmentOptions.Left);
            descText.rectTransform.anchorMin = new Vector2(0.3f, 0f);
            descText.enableWordWrapping = true;
            descText.overflowMode = TextOverflowModes.Overflow;
            descText.enableAutoSizing = true; descText.fontSizeMin = 12f; descText.fontSizeMax = 19f;

            // ---- chosen classes: Conjoined first (fixed), then the picks
            float by = h - 112f;
            for (int i = 0; i <= need; i++)
            {
                int slot = i;
                RectTransform s = UiKit.Panel(panel, "Slot" + i, new Vector2(Cell, Cell), i == 0 ? FrameOn * 0.7f : Frame);
                UiKit.At(s, gx + i * (Cell + Gap), by);
                RectTransform pic = UiKit.Rect(s, "Icon", Vector2.zero);
                pic.anchorMin = Vector2.zero; pic.anchorMax = Vector2.one; pic.offsetMin = new Vector2(4f, 4f); pic.offsetMax = new Vector2(-4f, -4f);
                Image img = pic.gameObject.AddComponent<Image>();
                img.preserveAspect = true;
                slots.Add(img);
                if (i > 0)
                {
                    var b = s.gameObject.AddComponent<Button>();
                    b.targetGraphic = s.GetComponent<Image>();
                    b.onClick.AddListener(() => { if (slot - 1 < picked.Count) { picked.RemoveAt(slot - 1); Refresh(); } });
                }
                Skill_Tree conj = Tree(Conjoined.ClassId);
                if (i == 0 && conj != null) img.sprite = conj.icon;
            }
            message = Text(panel, "", 17, new Vector2(gridW, 26f), gx, by - 30f, TextAlignmentOptions.Left);
            // count and buttons in the left column, so the bar of chosen classes has the full width
            countText = Text(panel, "", 20, new Vector2(Side, 30f), M, by - 8f, TextAlignmentOptions.Left);
            float bw = (Side - 10f) / 2f;
            var later = UiKit.Button(panel, "Later", font, 22, new Vector2(bw, 50f), () => { Conjoined.DeferPicker(); Close(); });
            UiKit.At((RectTransform)later.transform, M, by + 28f);
            join = UiKit.Button(panel, "Join", font, 24, new Vector2(bw, 50f), Confirm, UiKit.OnColor);
            UiKit.At((RectTransform)join.transform, M + bw + 10f, by + 28f);

            Refresh();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static TextMeshProUGUI Text(Transform parent, string text, float size, Vector2 box, float x, float y, TextAlignmentOptions align)
        {
            RectTransform r = UiKit.Rect(parent, "Text", box);
            UiKit.At(r, x, y);
            return UiKit.Label(r, text, font, size, align);
        }

        private static void Btn(string label, System.Action click, string hint, ref float y)
        {
            Button b = UiKit.Button(panel, label, font, 18, new Vector2(Side, 40f), click);
            UiKit.At((RectTransform)b.transform, M, y);
            OnHover(b.gameObject, () => { descName.text = label; descText.text = hint; }, null);
            y += 48f;
        }

        private static void MakeCell(RectTransform content, Skill_Tree t, float x, float y)
        {
            string id = t.internalName;
            RectTransform cell = UiKit.Panel(content, id, new Vector2(Cell, Cell), Frame);
            UiKit.At(cell, x, y);
            RectTransform pic = UiKit.Rect(cell, "Icon", Vector2.zero);
            pic.anchorMin = Vector2.zero; pic.anchorMax = Vector2.one; pic.offsetMin = new Vector2(4f, 4f); pic.offsetMax = new Vector2(-4f, -4f);
            Image img = pic.gameObject.AddComponent<Image>();
            img.sprite = t.icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var b = cell.gameObject.AddComponent<Button>();
            b.targetGraphic = cell.GetComponent<Image>();
            b.onClick.AddListener(() => Toggle(id));
            OnHover(cell.gameObject, () => Hover(t, cell), () => SynergyTip.Hide());
            cells[id] = (cell.GetComponent<Image>(), img, cell);
        }

        private static void OnHover(GameObject go, System.Action enter, System.Action exit)
        {
            var trig = go.AddComponent<EventTrigger>();
            var e = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            e.callback.AddListener(_ => enter?.Invoke());
            trig.triggers.Add(e);
            if (exit == null) return;
            var x = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            x.callback.AddListener(_ => exit());
            trig.triggers.Add(x);
        }

        /// <summary>Shows a tree's name, description and synergies (with the classes chosen so far).</summary>
        internal static void Hover(Skill_Tree t, RectTransform cell)
        {
            if (panel == null || t == null) return;
            descName.text = t.displayName;
            descText.text = Tags(t.description);
            var with = picked.Select(Tree).Concat(new[] { Tree(Conjoined.ClassId) }).Where(x => x != null).ToList();
            SynergyTip.Show(panel, cell, descFrame, Synergies.CreationText(t, with), 22f, font);
        }

        internal static void Hover(string id) { if (cells.TryGetValue(id, out var c)) Hover(Tree(id), c.rt); }

        private static string Tags(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            foreach (var (tag, stat) in new[] { ("[STR]", STAT_TYPE.Strength), ("[TGH]", STAT_TYPE.Toughness), ("[AGL]", STAT_TYPE.Agility), ("[REC]", STAT_TYPE.Recognition),
                ("[SAV]", STAT_TYPE.Savvy), ("[SMT]", STAT_TYPE.Smarts), ("[STL]", STAT_TYPE.Stealth), ("[PSI]", STAT_TYPE.PsiPower), ("[HP]", STAT_TYPE.HealthMax), ("[ARM]", STAT_TYPE.Armor) })
                text = text.Replace(tag, StatObj.GetStatString(stat));
            return text;
        }

        /// <summary>Why this tree can't join the picks (the creation screen's rules), or null.</summary>
        private static string Clash(string id)
        {
            Skill_Tree t = Tree(id);
            foreach (Skill_Tree other in picked.Select(Tree).Concat(new[] { Tree(Conjoined.ClassId) }).Where(x => x != null && x != t))
            {
                string why = ClassRegistry.Conflict(t, other);
                if (why != null) return $"{t.displayName} can't be combined with {other.displayName}: one {why}.";
            }
            return null;
        }

        internal static void Toggle(string id)
        {
            message.text = "";
            if (picked.Remove(id)) { Refresh(); return; }
            if (picked.Count >= need) { message.text = $"<color=#FFC35C>That's all {need}: click a chosen class (below, or in the grid) to swap it out.</color>"; Refresh(); return; }
            string clash = Clash(id);
            if (clash != null) { message.text = $"<color=#FF7272>{clash}</color>"; AudioManager.PlayEvent("event:/UI/UI_Error"); return; }
            picked.Add(id);
            Refresh();
        }

        internal static void Fill(IEnumerable<string> ids)
        {
            picked.Clear();
            message.text = "";
            var skipped = new List<string>();
            foreach (string id in ids)
            {
                if (picked.Count >= need) break;
                if (!cells.ContainsKey(id) || picked.Contains(id)) { if (!cells.ContainsKey(id) && id != Conjoined.ClassId) skipped.Add(id); continue; }
                if (Clash(id) != null) { skipped.Add(Tree(id)?.displayName ?? id); continue; }
                picked.Add(id);
            }
            if (skipped.Count > 0) message.text = $"<color=#FFC35C>Left out: {string.Join(", ", skipped)}.</color>";
            Refresh();
        }

        private static void Refresh()
        {
            bool full = picked.Count >= need;
            foreach (var kv in cells)
            {
                bool on = picked.Contains(kv.Key);
                kv.Value.frame.color = on ? FrameOn : Frame;
                kv.Value.icon.color = on || !full ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
            }
            for (int i = 1; i < slots.Count; i++)
            {
                Skill_Tree t = i - 1 < picked.Count ? Tree(picked[i - 1]) : null;
                slots[i].sprite = t?.icon;
                slots[i].color = t != null ? Color.white : new Color(1f, 1f, 1f, 0f);
            }
            if (countText != null) countText.text = $"{picked.Count}/{need} chosen";
            if (join != null) join.interactable = picked.Count == need;
        }

        private static void Confirm()
        {
            if (picked.Count != need) return;
            var trees = picked.ToList();
            Close();
            Conjoined.CreateBody(trees);
        }

        internal static void Close()
        {
            SynergyTip.Hide();
            if (panel != null) Object.Destroy(panel.gameObject);
            panel = null;
            join = null;
            countText = null;
        }
    }
}
