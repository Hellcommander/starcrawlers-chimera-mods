using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// "Mod Content" in Mod Settings: a tree of every mod, the kinds of content in it (classes, items, synergies, tweaks,
    /// ...) and each entry, with On/Off at every level. Off on a mod or kind takes everything under it with it, while the
    /// entries keep their own setting for when it comes back on. Choices save at once (<see cref="ModChoices"/>); content
    /// is read while the game starts, so they apply after a restart.
    /// </summary>
    internal static class ModContent
    {
        internal const string Section = "Mod Content";

        private static readonly Color Grey = new Color(0.55f, 0.6f, 0.66f), Amber = new Color(1f, 0.72f, 0.3f), Red = new Color(1f, 0.45f, 0.45f),
            Dim = new Color(0.68f, 0.75f, 0.82f);

        /// <summary>Expanded rows (mod ids and kind keys), for this session.</summary>
        private static readonly HashSet<string> open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, List<string>> saves;

        /// <summary>Reads the saves again on the next draw (each time Mod Settings opens).</summary>
        internal static void Forget() => saves = null;

        internal static float Draw(ModSettings.Window w, RectTransform c, float width)
        {
            saves = saves ?? ModsPage.SavesByClass();
            float y = Para(w, c, "Turn off whole mods, a kind of content in a mod, or single classes, items, synergies and tweaks. " +
                "Turning one off turns off everything under it. Content is read while the game starts, so changes here apply after a restart.",
                14, 12f, 8f, width - 24f, Dim);

            if (ModPacks.ChangedSinceStart())
            {
                bool title = Player.instance == null;
                w.Text(c, title ? "Changes apply after a restart." : "Changes apply after a restart: save and quit, then start the game again.",
                    15, 12f, y + 6f, width - 300f, 22f, TextAlignmentOptions.TopLeft, Amber);
                w.Button(c, "Undo", width - (title ? 276f : 110f), y, 96f, 30f, () => { Undo(); w.Rebuild(); });
                if (title) w.Button(c, "Restart Now", width - 170f, y, 156f, 30f, ModsPage.Restart, UiKit.OnColor).gameObject.name = "SCCL_ContentRestart";
                y += 40f;
            }
            y += 6f;

            ModPlan next = ModPacks.Resolve(ModChoices.IsOn);
            foreach (ModPack p in ModPacks.All.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase))
            {
                bool modOn = ModChoices.IsOn(p);
                int offInside = p.content.Sum(g => g.entries.Count(e => !ModChoices.PartOn(g.key) || !ModChoices.PartOn(e.key)));
                string note; Color noteColor = Grey;
                if (modOn && next.Skipped.TryGetValue(p, out string why) && why != ModPacks.Off) { note = why; noteColor = Red; }
                else note = offInside > 0 ? $"{offInside} off inside" : p.content.Count == 0 ? "nothing to turn off inside" : "";
                if (modOn && offInside > 0) noteColor = Amber;
                y = Row(w, c, width, 0, p.id, p.Title + (string.IsNullOrEmpty(p.version) ? "" : $"  <color=#8A96A3><size=13>v{p.version}</size></color>"),
                    note, noteColor, p.content.Count > 0, modOn, true, () => ModChoices.Set(p, !modOn), y);
                if (!open.Contains(p.id)) continue;

                foreach (ContentGroup g in p.content)
                {
                    bool gOn = ModChoices.PartOn(g.key);
                    int on = g.entries.Count(e => ModChoices.PartOn(e.key));
                    y = Row(w, c, width, 1, g.key, $"{g.label} ({g.entries.Count})", $"{on} of {g.entries.Count} on", on < g.entries.Count ? Amber : Grey,
                        true, gOn, modOn, () => ModChoices.SetPart(g.key, !gOn), y);
                    if (!open.Contains(g.key)) continue;

                    foreach (ContentEntry e in g.entries)
                    {
                        bool eOn = ModChoices.PartOn(e.key);
                        string eNote = ""; Color eColor = Grey;
                        // A class turned off leaves the skill slot of characters that have it empty.
                        if (e.isClass && saves.TryGetValue(e.id, out var used))
                        {
                            bool loads = ModPacks.IsOn(p, g, e);
                            eNote = (loads ? "in saves: " : "slot empty in saves: ") + string.Join(", ", used.Distinct());
                            eColor = loads ? Grey : Amber;
                        }
                        y = Row(w, c, width, 2, e.key, e.name, eNote, eColor, false, eOn, modOn && gOn, () => ModChoices.SetPart(e.key, !eOn), y);
                    }
                }
            }
            return y + 8f;
        }

        /// <summary>Puts every choice back to what this session loaded with.</summary>
        private static void Undo()
        {
            foreach (ModPack p in ModPacks.All) if (ModChoices.IsOn(p) != p.enabled) ModChoices.Set(p, p.enabled);
            foreach (string k in ModChoices.OffParts.Where(k => !ModPacks.StartOff.Contains(k)).ToList()) ModChoices.SetPart(k, true);
            foreach (string k in ModPacks.StartOff.Where(k => ModChoices.PartOn(k)).ToList()) ModChoices.SetPart(k, false);
            AudioManager.PlayEvent("event:/UI/UI_Click");
        }

        /// <summary>One row: expander, label, note and its On/Off. A row whose parent is off is greyed but keeps its own setting.</summary>
        private static float Row(ModSettings.Window w, RectTransform c, float width, int depth, string key, string label, string note, Color noteColor,
            bool expandable, bool on, bool parentOn, Action toggle, float y)
        {
            float x = 10f + depth * 30f;
            RectTransform bg = UiKit.Panel(c, "Row", new Vector2(width - x - 4f, 32f),
                depth == 0 ? new Color(0.07f, 0.13f, 0.17f, 0.95f) : depth == 1 ? new Color(0.05f, 0.1f, 0.13f, 0.8f) : new Color(0.03f, 0.07f, 0.09f, 0.6f));
            UiKit.At(bg, x - 4f, y);
            if (expandable)
                w.Button(c, open.Contains(key) ? "-" : "+", x, y + 3f, 26f, 26f, () => { if (!open.Remove(key)) open.Add(key); w.Rebuild(); })
                    .gameObject.name = "SCCL_ContentOpen_" + key;
            float lx = x + 34f, noteW = 250f;
            w.Text(c, label, depth == 0 ? 17 : 15, lx, y + 5f, width - lx - noteW - 120f, 24f, TextAlignmentOptions.TopLeft, on && parentOn ? Color.white : Grey);
            if (!string.IsNullOrEmpty(note))
                w.Text(c, note, 13, width - noteW - 116f, y + 8f, noteW, 20f, TextAlignmentOptions.TopRight, parentOn ? noteColor : Grey);
            w.Button(c, on ? "On" : "Off", width - 106f, y + 3f, 96f, 26f, () => { toggle(); AudioManager.PlayEvent("event:/UI/UI_Click"); w.Rebuild(); },
                !on ? UiKit.ButtonColor : parentOn ? UiKit.OnColor : new Color(0.1f, 0.2f, 0.22f, 0.9f)).gameObject.name = "SCCL_Content_" + key;
            return y + 36f;
        }

        private static float Para(ModSettings.Window w, RectTransform c, string text, float size, float x, float y, float width, Color color)
        {
            TextMeshProUGUI t = w.Text(c, text, size, x, y, width, 20f, TextAlignmentOptions.TopLeft, color);
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            float h = Mathf.Max(size + 6f, t.GetPreferredValues(text, width, 0f).y);
            ((RectTransform)t.transform.parent).sizeDelta = new Vector2(width, h);
            return y + h + 6f;
        }
    }
}
