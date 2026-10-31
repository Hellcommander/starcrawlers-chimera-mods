using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>One finished run in the local graveyard.</summary>
    public class GraveEntry
    {
        public string date;
        public string name;
        public List<string> classes = new List<string>();
        public int level;
        public int floor;
        public int deepest;
        public int turns;
        public string killer;
        public string skill;
        public int lastHit;
        public string difficulty;
        public bool ironmode;
        public int sideAreas;
        public bool won;
        /// <summary>Deepest depth of the Ichor Deep reached after the win (0 = never went in).</summary>
        public int deep;
        public int score;
    }

    /// <summary>
    /// Death recap and a local Nethack-style graveyard. The game remembers who dealt the killing blow; this also keeps
    /// the last few hits, spells the cause out on the death screen, and appends every finished run to
    /// SCCL_Graveyard.json next to Game.scc. F8 (configurable) shows the list, anywhere including the title screen.
    /// </summary>
    internal static class Graveyard
    {
        internal static ConfigEntry<KeyCode> ViewKey;
        internal static ConfigEntry<int> Shown;

        internal class Hit
        {
            public int turn, amount, hpBefore;
            public string who, skill, types;
        }

        internal static readonly List<Hit> LastHits = new List<Hit>();
        private static string pendingTypes;

        private static string FilePath => Path.Combine(Application.persistentDataPath, "SCCL_Graveyard.json");

        internal static void Bind(ConfigFile cfg)
        {
            ViewKey = cfg.Bind("Graveyard", "ViewKey", KeyCode.F8, "Shows the local list of past runs.");
            Shown = cfg.Bind("Graveyard", "Lines", 15, "How many runs the list shows.");
            var go = new GameObject("SCCL_Graveyard");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<View>();
        }

        // ---- recording hits -----------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Character), nameof(Character.TakeDamage), typeof(Character), typeof(List<DmgObj>), typeof(string), typeof(Ability), typeof(float))]
        private static class Patch_TakeDamage
        {
            private static void Prefix(Character __instance, List<DmgObj> dmgList)
            {
                if (__instance != Player.instance || dmgList == null) return;
                pendingTypes = string.Join("/", dmgList.Where(d => d != null && d.damage > 0).Select(d => d.dmgType.ToString()).Distinct());
            }
        }

        /// <summary>Every bit of health the player loses passes through here, with its source.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.DamageHealth))]
        private static class Patch_DamageHealth
        {
            private static void Prefix(Player __instance, int dmg, Ability dmgSource)
            {
                if (dmg <= 0) return;
                Character caster = null;
                string skill = null;
                try { caster = dmgSource?.GetCaster(); skill = dmgSource?.GetDisplayName(); } catch { }
                LastHits.Add(new Hit
                {
                    turn = __instance.GetTurnCount(),
                    amount = dmg,
                    hpBefore = __instance.GetHealth(),
                    who = caster != null && caster != __instance ? Plain(caster.GetName()) : null,
                    skill = string.IsNullOrEmpty(skill) ? null : Plain(skill),
                    types = pendingTypes,
                });
                pendingTypes = null;
                if (LastHits.Count > 6) LastHits.RemoveAt(0);
            }
        }

        private static string Plain(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<.*?>", "").Trim();

        private static string Describe(Hit h)
        {
            string src = h.who != null ? (h.skill != null && h.skill != h.who ? $"{h.who}'s {h.skill}" : h.who) : h.skill ?? "something unseen";
            return $"{src}: {h.amount}{(string.IsNullOrEmpty(h.types) ? "" : " " + h.types)}";
        }

        // ---- death and victory --------------------------------------------------------------------------------

        [HarmonyPatch(typeof(HUD), nameof(HUD.PlayerDied))]
        private static class Patch_Died
        {
            private static void Postfix(HUD __instance)
            {
                try
                {
                    Hit last = LastHits.LastOrDefault();
                    GraveEntry e = Record(won: false, last);
                    var lines = new List<string>();
                    if (last != null)
                        lines.Add($"Killing blow: <color=#FF7272>{Describe(last)}</color> damage with {last.hpBefore} health left.");
                    var before = LastHits.Take(Math.Max(0, LastHits.Count - 1)).Reverse().Take(3).Select(Describe).ToList();
                    if (before.Count > 0) lines.Add("Before that: " + string.Join("; ", before) + ".");
                    lines.Add($"Score {e.score:N0}, #{Rank(e)} of {Load().Count} in your graveyard ({ViewKey.Value} to view).");
                    if (__instance.deathReason != null)
                        __instance.deathReason.text += "\n\n<size=65%>" + string.Join("\n", lines) + "</size>";
                }
                catch (Exception ex) { Plugin.Log.LogError("Graveyard: " + ex); }
                LastHits.Clear();
            }
        }

        [HarmonyPatch(typeof(HUD), nameof(HUD.WinGame))]
        private static class Patch_Won
        {
            private static void Postfix()
            {
                try { Record(won: true, null); }
                catch (Exception ex) { Plugin.Log.LogError("Graveyard: " + ex); }
            }
        }

        internal static GraveEntry Record(bool won, Hit last)
        {
            Player p = Player.instance;
            var e = new GraveEntry
            {
                date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                name = p != null ? Plain(p.GetName()) : "?",
                classes = Player_SkillControl.instance?.skillTrees?.Where(t => t != null).Select(t => Plain(t.displayName)).ToList() ?? new List<string>(),
                level = p != null ? p.level : 0,
                floor = Game.FLOOR + 1,
                deepest = Math.Max(Game.FLOOR, Game.FLOOR_HIGHEST) + 1,
                turns = p != null ? p.GetTurnCount() : 0,
                killer = last?.who ?? last?.skill,
                skill = last?.who != null ? last.skill : null,
                lastHit = last?.amount ?? 0,
                difficulty = new[] { "Easy", "Normal", "Hard", "Brutal" }.ElementAtOrDefault(Game.GetDifficulty()) ?? Game.GetDifficulty().ToString(),
                ironmode = Game.GetIronmode() == 1,
                sideAreas = SideAreas.Visits,
                won = won || Endless.Won,
                deep = Endless.Best,
            };
            e.score = Score(e);
            var all = Load();
            // a winner who stayed and fell later in the Ichor Deep: that ending replaces the run's win entry
            var d = SideAreas.Data;
            if (d != null && d.TryGetValue("won_entry", out object w) && w is string wonDate && Endless.Won)
                all.RemoveAll(x => x.won && x.date == wonDate && x.name == e.name);
            if (won && d != null) d["won_entry"] = e.date;
            all.Add(e);
            try { File.WriteAllText(FilePath, JsonConvert.SerializeObject(all, Formatting.Indented)); }
            catch (Exception ex) { Plugin.Log.LogError("Graveyard: could not save: " + ex.Message); }
            Plugin.Log.LogInfo($"Graveyard: {Line(e, 0)}");
            return e;
        }

        /// <summary>Depth matters most, then level; escaping alive doubles it, Ironmode and side trips add a bonus.</summary>
        internal static int Score(GraveEntry e)
        {
            int s = e.deepest * 1000 + e.level * 100 + e.sideAreas * 250 + e.deep * 1500;
            if (e.ironmode) s = s * 5 / 4;
            if (e.won) s *= 2;
            return s;
        }

        internal static List<GraveEntry> Load()
        {
            try { return File.Exists(FilePath) ? JsonConvert.DeserializeObject<List<GraveEntry>>(File.ReadAllText(FilePath)) ?? new List<GraveEntry>() : new List<GraveEntry>(); }
            catch (Exception ex) { Plugin.Log.LogError("Graveyard: could not read: " + ex.Message); return new List<GraveEntry>(); }
        }

        private static int Rank(GraveEntry e) => Load().OrderByDescending(x => x.score).ToList().FindIndex(x => x.date == e.date && x.name == e.name && x.score == e.score) + 1;

        internal static string Line(GraveEntry e, int rank)
        {
            string who = $"{e.name}{(e.classes.Count > 0 ? " the " + string.Join("/", e.classes.Take(3)) + (e.classes.Count > 3 ? "/…" : "") : "")}";
            string how = e.won && e.deep > 0 ? $"<color=#7CFC7C>escaped the Chimera building</color>, then went down to depth {e.deep} of the <color={Endless.Color}>Ichor Deep</color>{(e.killer != null ? $" and was killed there by {e.killer}" : "")}"
                : e.won ? "<color=#7CFC7C>escaped the Chimera building</color>"
                : $"killed on floor {e.floor} by {e.killer ?? "unknown causes"}{(e.skill != null && e.skill != e.killer ? $"'s {e.skill}" : "")}";
            string extra = $"lvl {e.level}, {e.turns} turns, {e.difficulty}{(e.ironmode ? ", Ironmode" : "")}{(e.sideAreas > 0 ? $", {e.sideAreas} side area{(e.sideAreas > 1 ? "s" : "")}" : "")}";
            return $"{(rank > 0 ? $"{rank,3}. " : "")}{e.score,7:N0}  {who}, {how} ({extra}), {e.date}";
        }

        // ---- the list -------------------------------------------------------------------------------------------

        internal class View : MonoBehaviour
        {
            private GameObject panel;
            private TextMeshProUGUI text;

            private void Update()
            {
                if (!Input.GetKeyDown(ViewKey.Value)) return;
                if (panel != null && panel.activeSelf) { panel.SetActive(false); return; }
                Show();
            }

            internal void Show()
            {
                if (panel == null) Build();
                var all = Load();
                var ranked = all.OrderByDescending(e => e.score).ToList();
                var lines = new List<string> { "<size=130%><color=#FFD966>The Graveyard</color></size>", $"<color=#9FB7C9>{all.Count} run{(all.Count == 1 ? "" : "s")} recorded on this computer. {ViewKey.Value} to close.</color>", "" };
                if (all.Count == 0) lines.Add("Nobody has died yet. Give it time.");
                for (int i = 0; i < Math.Min(Shown.Value, ranked.Count); i++) lines.Add(Line(ranked[i], i + 1));
                GraveEntry latest = all.LastOrDefault();
                if (latest != null && ranked.IndexOf(latest) >= Shown.Value) lines.Add($"\n<color=#9FB7C9>Latest:</color>\n{Line(latest, ranked.IndexOf(latest) + 1)}");
                text.text = string.Join("\n", lines);
                panel.SetActive(true);
            }

            private void Build()
            {
                panel = new GameObject("SCCL_GraveyardPanel", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                panel.transform.SetParent(transform, false);
                var canvas = panel.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 5000;
                var scaler = panel.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);

                var bg = new GameObject("bg", typeof(RectTransform), typeof(Image));
                bg.transform.SetParent(panel.transform, false);
                var br = (RectTransform)bg.transform;
                br.anchorMin = new Vector2(0.08f, 0.1f); br.anchorMax = new Vector2(0.92f, 0.9f);
                br.offsetMin = br.offsetMax = Vector2.zero;
                bg.GetComponent<Image>().color = new Color(0.02f, 0.05f, 0.08f, 0.97f);
                UiLook.Attach(br, "Graveyard");
                UiLook.Collapsible(br, "Graveyard", UiLook.Corner.InsideLeft, false);

                var t = new GameObject("text", typeof(RectTransform), typeof(TextMeshProUGUI));
                t.transform.SetParent(bg.transform, false);
                var tr = (RectTransform)t.transform;
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
                tr.offsetMin = new Vector2(30, 24); tr.offsetMax = new Vector2(-30, -24);
                text = t.GetComponent<TextMeshProUGUI>();
                // The combat log's font reads well in mixed case; the first font found may be an all-caps title face.
                TMP_FontAsset font = (Menu_HUD.instance != null && Menu_HUD.instance.log != null ? Menu_HUD.instance.log.font : null)
                                     ?? Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f != null && Menu_HUD.instance == null);
                if (font != null) text.font = font;
                text.fontSize = 22;
                text.alignment = TextAlignmentOptions.TopLeft;
                text.color = Color.white;
                panel.SetActive(false);
            }
        }
    }
}
