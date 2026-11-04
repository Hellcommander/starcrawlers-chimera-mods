using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>Title words for one class: "title" in a class json, or an entry under "titles" in a mod's data file.</summary>
    public class TitleSpec
    {
        /// <summary>Words used when this class is your second: "Kindled" in "Kindled Gunslinger".</summary>
        public List<string> adjectives;
        /// <summary>Words used when this class leads: "Gunslinger".</summary>
        public List<string> nouns;
        /// <summary>Phrases used when this class is your third: "the Ember" in "Gunslinger of the Ember".</summary>
        public List<string> of;
        /// <summary>The noun once this class is maxed and leads, in place of <see cref="nouns"/>.</summary>
        public string master;
        /// <summary>What the class does, strongest first (up to three of the themes in themeTitles: fire, gun, swarm, ...).
        /// Without it the class's themes come from its skills' damage types.</summary>
        public List<string> themes;
    }

    /// <summary>A line added once enough of your classes are maxed: "renown" in a mod's data file.</summary>
    public class RenownSpec
    {
        /// <summary>How many maxed classes it takes.</summary>
        public int maxed;
        /// <summary>True: only when every class you have is maxed (at least two), whatever the count.</summary>
        public bool all;
        public string text;
    }

    /// <summary>
    /// Class titles: an optional title made from the classes you have, shown under your name on the character sheet
    /// and, while you pick classes, on the creation screen. Rank never counts: the title is about which classes you have.
    /// Mastery (the default): every class adds its themes (what it does: fire, guns, summons, ...) and the two strongest
    /// themes pick one name for the whole build ("Powder Saint"). Words: your first class gives the noun, the second an
    /// adjective, the third an "of" phrase ("Kindled Gunslinger of the Void"). Hand-written titleCombos beat both, and
    /// Ollama can write one for your exact combination. Words come from data (a class's "title", or "titles",
    /// "themeTitles" and "titleCombos" in any mod's data file, later mods winning), so any class gets a title.
    ///
    /// The title is display only: the game uses your name as the save file name and key, so <see cref="Player.playerName"/>
    /// never changes. Click the title for the next name; Shift+click makes the next class lead (Words); Ctrl+click asks
    /// Ollama. All of it is kept per character in SCCL_titles.json.
    /// </summary>
    internal static class ClassTitles
    {
        internal enum Style { Full, Short, Epithet }
        internal enum Source { Mastery, Words }

        internal static ConfigEntry<bool> Enabled, OnCreation, ShowRenown, AiTitles;
        internal static ConfigEntry<Style> TitleStyle;
        internal static ConfigEntry<Source> TitleSource;
        internal static ConfigEntry<string> OllamaUrl, OllamaModel;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Titles", "ClassTitles", true, "Show a title made from your classes under your name (character sheet and creation screen). Display only: your name and saves are unchanged. Off = the vanilla screens.");
            TitleStyle = cfg.Bind("Titles", "TitleStyle", Style.Full, "Full: \"Kindled Gunslinger of the Void\" (up to three classes). Short: \"Kindled Gunslinger\" (two). Epithet: the full title joined to your name on the character sheet, \"Vex the Kindled Gunslinger of the Void\".");
            TitleSource = cfg.Bind("Titles", "TitleSource", Source.Mastery, "Mastery: one name for what all your classes do together; every class counts the same, and the two strongest themes (fire, guns, summons, ...) pick the name (\"Powder Saint\" for fire and guns). Click it for the other pairs your classes make. Words: built from your first three classes (\"Kindled Gunslinger of the Void\"). Rank never counts in either.");
            OnCreation = cfg.Bind("Titles", "TitleOnCreation", true, "Preview the title under the name box on the creation screen as you pick classes.");
            ShowRenown = cfg.Bind("Titles", "ShowRenown", true, "Add a renown line once several of your classes are maxed (\"Paragon of Seven Paths\" with all of them).");
            AiTitles = cfg.Bind("Titles", "OllamaTitles", false, "Ctrl+click your title to have a locally running Ollama write one from your class combination. It is saved for that character and kept while you have the same classes; a plain click goes back to the generated title. Sends only your name, your classes and their descriptions to OllamaUrl.");
            OllamaUrl = cfg.Bind("Titles", "OllamaUrl", "http://localhost:11434", "Where Ollama runs (its default port is 11434).");
            OllamaModel = cfg.Bind("Titles", "OllamaModel", "llama3.1:8b", "The Ollama model that writes titles (one you have pulled: ollama list shows them). Thinking models work too; their reasoning is dropped.");
            EventHandler changed = (_, __) => { RefreshSheet(); RefreshCreation(); };
            Enabled.SettingChanged += changed;
            TitleStyle.SettingChanged += changed;
            OnCreation.SettingChanged += changed;
            ShowRenown.SettingChanged += changed;
            TitleSource.SettingChanged += changed;
        }

        // ---- words ----------------------------------------------------------------------------------------------

        private static Dictionary<string, TitleSpec> specs;
        private static List<RenownSpec> renown;
        /// <summary>Hand-written titles for class combinations, keyed by sorted ids joined with '+'.</summary>
        private static Dictionary<string, string> combos;
        /// <summary>Mastery names by theme ("fire") or theme pair (sorted, "fire+gun").</summary>
        private static Dictionary<string, List<string>> themeTitles;

        internal static void Load()
        {
            specs = new Dictionary<string, TitleSpec>(StringComparer.OrdinalIgnoreCase);
            renown = new List<RenownSpec>();
            combos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            themeTitles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            derived.Clear();
            foreach (ClassDef d in ClassRegistry.AllDefs)
                if (d?.title != null && !string.IsNullOrEmpty(d.id)) specs[d.id] = d.title;
            foreach (ModPack p in ModPacks.Ordered)
                foreach (var (file, obj) in p.data)
                {
                    if (obj["titles"] is JObject t)
                        foreach (var kv in t)
                        {
                            try { if (kv.Value is JObject o) specs[kv.Key] = o.ToObject<TitleSpec>(); }
                            catch (Exception e) { Plugin.Log.LogError($"{file}: title for '{kv.Key}' could not be read: {e.Message}"); }
                        }
                    if (obj["titleCombos"] is JObject cb)
                        foreach (var kv in cb)
                        {
                            string key = Signature(kv.Key.Split('+').Select(x => new Pick { id = x.Trim() }));
                            string text = (string)kv.Value;
                            if (key.Contains("+") && !string.IsNullOrWhiteSpace(text)) combos[key] = text.Trim();
                            else Plugin.Log.LogError($"{file}: title combo '{kv.Key}' needs two or three class ids joined with + and a title");
                        }
                    if (obj["themeTitles"] is JObject tt)
                        foreach (var kv in tt)
                        {
                            var names = kv.Value is JArray a ? a.Select(x => (string)x).Where(x => !string.IsNullOrWhiteSpace(x)).ToList() : new List<string> { (string)kv.Value };
                            themeTitles[ThemeKey(kv.Key.Split('+'))] = names;
                        }
                    if (obj["renown"] is JArray r)
                    {
                        // a later mod's list replaces the earlier one, so a mod can rewrite the whole ladder
                        var list = new List<RenownSpec>();
                        foreach (JObject o in r.OfType<JObject>())
                        {
                            try { var s = o.ToObject<RenownSpec>(); if (!string.IsNullOrEmpty(s.text)) list.Add(s); }
                            catch (Exception e) { Plugin.Log.LogError($"{file}: a renown entry could not be read: {e.Message}"); }
                        }
                        renown = list;
                    }
                }
            Plugin.Log.LogInfo($"Class titles: words for {specs.Count} classes, {combos.Count} hand-written combos, {themeTitles.Count} mastery names, {renown.Count} renown lines");
        }

        private static TitleSpec Spec(string id)
        {
            if (specs == null) Load();
            return id != null && specs.TryGetValue(id, out TitleSpec s) ? s : null;
        }

        // ---- the title ------------------------------------------------------------------------------------------

        internal class Pick
        {
            public string id, name;
            public int rank, order;
            public bool maxed;
        }

        /// <summary>The classes in title order: the order you have them in (picked, or class slots), the chosen lead in front. Rank never counts.</summary>
        internal static List<Pick> Order(IEnumerable<Pick> picks, string lead)
        {
            var list = picks.Where(p => p != null && !string.IsNullOrEmpty(p.id)).OrderBy(p => p.order).ToList();
            int i = lead == null ? -1 : list.FindIndex(p => string.Equals(p.id, lead, StringComparison.OrdinalIgnoreCase));
            if (i > 0) { Pick p = list[i]; list.RemoveAt(i); list.Insert(0, p); }
            return list;
        }

        private static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s ?? "") { h ^= c; h *= 16777619; }
            return h;
        }

        private static string Choose(List<string> words, string seed, int roll, int slot)
        {
            var ok = words?.Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
            if (ok == null || ok.Count == 0) return null;
            uint h = Hash(seed + "|" + slot) + (uint)roll * 2654435761u;
            return ok[(int)(h % (uint)ok.Count)].Trim();
        }

        private static string Clean(string name) => (name ?? "").Replace("!", "").Trim();

        /// <summary>
        /// The title for these classes, or "" for none. <paramref name="renownLine"/> gets the renown line ("" if none).
        /// Words are seeded from the character's name, so the same character keeps the same title until it rolls.
        /// </summary>
        // ---- mastery names: what all your classes do together -----------------------------------------------------

        internal static string ThemeKey(IEnumerable<string> themes) =>
            string.Join("+", themes.Select(t => t.Trim().ToLowerInvariant()).Where(t => t != "").Distinct().OrderBy(t => t, StringComparer.Ordinal));

        /// <summary>Themes worked out from a class's skills, for classes whose title words list none.</summary>
        private static readonly Dictionary<string, List<string>> derived = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private static string ThemeOf(DAMAGE_TYPE t)
        {
            switch (t)
            {
                case DAMAGE_TYPE.Slashing: case DAMAGE_TYPE.Piercing: return "blade";
                case DAMAGE_TYPE.Crushing: return "brawn";
                case DAMAGE_TYPE.Ballistic: return "gun";
                case DAMAGE_TYPE.Fire: return "fire";
                case DAMAGE_TYPE.Cryo: return "frost";
                case DAMAGE_TYPE.Shock: case DAMAGE_TYPE.Energy: return "storm";
                case DAMAGE_TYPE.Void: return "void";
                case DAMAGE_TYPE.Psi: return "mind";
                case DAMAGE_TYPE.Meta: return "arcane";
                case DAMAGE_TYPE.Bio: return "rot";
                default: return null;
            }
        }

        private static Skill_Tree FindTree(string id) =>
            Player_SkillControl.instance?.skillTrees?.FirstOrDefault(x => x != null && x.internalName == id)
            ?? Menu_Creation.instance?.selectedSkills?.Select(x => x?.skillTree).FirstOrDefault(x => x != null && x.internalName == id)
            ?? Game.instance?.skills?.FirstOrDefault(x => x != null && x.internalName == id);

        /// <summary>A class's themes: its title words' "themes", else what its skills deal (damage types, summons).</summary>
        internal static List<string> ThemesOf(string id)
        {
            List<string> listed = Spec(id)?.themes;
            if (listed != null && listed.Count > 0) return listed;
            if (derived.TryGetValue(id, out List<string> d)) return d;
            var score = new Dictionary<string, int>();
            void Add(string t, int n) { if (t != null) score[t] = (score.TryGetValue(t, out int v) ? v : 0) + n; }
            Skill_Tree tree = FindTree(id);
            foreach (Skill_Tier tier in tree?.skillTiers ?? new List<Skill_Tier>())
                foreach (Ability a in new[] { tier?.ability, tier?.passive })
                {
                    if (a == null) continue;
                    foreach (DmgObj dmg in a._damageList ?? new List<DmgObj>()) Add(ThemeOf(dmg.dmgType), 2);
                    string type = a.GetType().Name;
                    if (type.Contains("Summon") || type.Contains("Minion") || type.Contains("Drone") || type.Contains("Pet")) Add("swarm", 3);
                }
            d = score.OrderByDescending(kv => kv.Value).Take(2).Select(kv => kv.Key).ToList();
            if (tree != null) derived[id] = d; // not cached until the tree is known
            return d;
        }

        /// <summary>
        /// One name for everything you have: every class adds its themes (3, 2, 1 for its first, second, third), all
        /// classes alike whatever their rank, and the two strongest themes pick a name from themeTitles. A theme far ahead
        /// of the rest (twice the next) uses its own name. A click (roll) moves on to the other pairs among your top three
        /// themes. It is a fixed function of the class set (order and character don't matter), so every combination
        /// of classes has exactly one name, as if looked up in a list of them all. Null when nothing fits, so the words take over.
        /// </summary>
        internal static string Mastery(List<Pick> ordered, string charName, int roll) => Mastery(ordered, charName, roll, out _);

        internal static string Mastery(List<Pick> ordered, string charName, int roll, out string scoreText)
        {
            scoreText = "";
            if (themeTitles == null) Load();
            if (themeTitles.Count == 0) return null;
            var score = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Pick p in ordered)
            {
                const int weight = 1; // which classes you have, not how far you've taken them
                var themes = ThemesOf(p.id);
                for (int i = 0; i < themes.Count && i < 3; i++)
                {
                    string t = themes[i].Trim().ToLowerInvariant();
                    score[t] = (score.TryGetValue(t, out int v) ? v : 0) + weight * (3 - i);
                }
            }
            // ties go alphabetically: the name depends only on which classes you have, never their order
            var top = score.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            scoreText = string.Join(", ", top.Select(kv => kv.Key + " " + kv.Value));
            if (top.Count == 0) return null;
            // the best fit first (one theme far ahead uses its own name), then the other pairs among your top three themes
            var keys = new List<string>();
            bool alone = top.Count == 1 || top[0].Value >= top[1].Value * 2;
            if (alone) keys.Add(ThemeKey(new[] { top[0].Key }));
            for (int a = 0; a < Math.Min(3, top.Count); a++)
                for (int b = a + 1; b < Math.Min(3, top.Count); b++)
                    keys.Add(ThemeKey(new[] { top[a].Key, top[b].Key }));
            if (!alone) keys.Add(ThemeKey(new[] { top[0].Key }));
            var found = keys.Distinct().Where(k => themeTitles.TryGetValue(k, out List<string> n) && n.Count > 0).ToList();
            if (found.Count == 0) return null;
            string key = found[((roll % found.Count) + found.Count) % found.Count];
            // seeded by the class set, not the character: the same classes always make the same name
            return Choose(themeTitles[key], Signature(ordered) + key, roll / found.Count, 3);
        }

        internal static string Make(List<Pick> ordered, string charName, int roll, Style style, out string renownLine)
        {
            renownLine = "";
            if (ordered == null || ordered.Count == 0) return "";
            string mastery = TitleSource != null && TitleSource.Value == Source.Mastery ? Mastery(ordered, charName, roll) : null;
            Pick lead = ordered[0];
            TitleSpec ls = Spec(lead.id);
            if (combos == null) Load();
            string combo = null;
            // a hand-written title for your top three, else your top two, wins over the generated words
            if (ordered.Count > 2 && style != Style.Short) combos.TryGetValue(Signature(ordered.Take(3)), out combo);
            if (combo == null && ordered.Count > 1) combos.TryGetValue(Signature(ordered.Take(2)), out combo);
            string noun = lead.maxed && !string.IsNullOrWhiteSpace(ls?.master) ? ls.master.Trim() : Choose(ls?.nouns, charName + lead.id, roll, 0) ?? Clean(lead.name);
            var sb = new StringBuilder();
            if (ordered.Count > 1)
            {
                Pick second = ordered[1];
                string adj = Choose(Spec(second.id)?.adjectives, charName + second.id, roll, 1);
                if (adj != null && !string.Equals(adj, noun, StringComparison.OrdinalIgnoreCase)) sb.Append(adj).Append(' ');
                else if (adj == null) sb.Append(Clean(second.name)).Append('-'); // no words: "Smuggler-Gunslinger"
            }
            sb.Append(noun);
            if (mastery != null) { sb.Clear(); sb.Append(mastery); }
            if (combo != null) { sb.Clear(); sb.Append(combo); } // hand-named classes beat everything generated
            else if (mastery == null && style != Style.Short && ordered.Count > 2)
            {
                Pick third = ordered[2];
                string of = Choose(Spec(third.id)?.of, charName + third.id, roll, 2);
                if (of != null) sb.Append(" of ").Append(of);
            }

            if (ShowRenown == null || ShowRenown.Value)
            {
                if (renown == null) Load();
                int maxed = ordered.Count(p => p.maxed);
                RenownSpec r = null;
                if (ordered.Count >= 2 && maxed == ordered.Count) r = renown.FirstOrDefault(x => x.all);
                if (r == null) r = renown.Where(x => !x.all && x.maxed > 0 && x.maxed <= maxed).OrderByDescending(x => x.maxed).FirstOrDefault();
                if (r != null) renownLine = r.text.Trim();
            }
            return sb.ToString();
        }

        // ---- who you are --------------------------------------------------------------------------------------

        internal static List<Pick> FromPlayer()
        {
            var trees = Player_SkillControl.instance?.skillTrees ?? Player.instance?.playerSkillControl?.skillTrees;
            var list = new List<Pick>();
            if (trees == null) return list;
            for (int i = 0; i < trees.Length; i++)
            {
                Skill_Tree t = trees[i];
                if (t == null || string.IsNullOrEmpty(t.internalName)) continue;
                list.Add(new Pick { id = t.internalName, name = t.displayName, rank = t.rank, order = i, maxed = t.skillTiers != null && t.skillTiers.Count > 0 && t.Maxed() });
            }
            return list;
        }

        internal static List<Pick> FromCreation(Menu_Creation mc)
        {
            var list = new List<Pick>();
            if (mc?.selectedSkills == null) return list;
            for (int i = 0; i < mc.selectedSkills.Count; i++)
            {
                Skill_Tree t = mc.selectedSkills[i]?.skillTree;
                if (t == null || string.IsNullOrEmpty(t.internalName) || t.internalName == "NYI") continue;
                list.Add(new Pick { id = t.internalName, name = t.displayName, rank = 0, order = i });
            }
            return list;
        }

        // ---- per-character choices (SCCL_titles.json, beside the game's data) -------------------------------------

        internal class Choice
        {
            public int roll;
            public string lead;
            /// <summary>A title Ollama wrote, and the classes it was written for (sorted ids).</summary>
            public string custom, customFor;
        }

        private static Dictionary<string, Choice> choices;
        private static string ChoicesPath => Path.Combine(Application.persistentDataPath, "SCCL_titles.json");

        internal static Choice ChoiceFor(string charName)
        {
            if (choices == null)
            {
                try { choices = File.Exists(ChoicesPath) ? JsonConvert.DeserializeObject<Dictionary<string, Choice>>(File.ReadAllText(ChoicesPath)) : null; }
                catch (Exception e) { Plugin.Log.LogWarning("SCCL_titles.json could not be read: " + e.Message); }
                choices = new Dictionary<string, Choice>(choices ?? new Dictionary<string, Choice>(), StringComparer.OrdinalIgnoreCase);
            }
            if (!choices.TryGetValue(charName ?? "", out Choice c)) choices[charName ?? ""] = c = new Choice();
            return c;
        }

        private static void SaveChoices()
        {
            try { File.WriteAllText(ChoicesPath, JsonConvert.SerializeObject(choices.Where(kv => kv.Key != "" && (kv.Value.roll != 0 || kv.Value.lead != null || kv.Value.custom != null)).ToDictionary(kv => kv.Key, kv => kv.Value), Formatting.Indented)); }
            catch (Exception e) { Plugin.Log.LogWarning("SCCL_titles.json could not be written: " + e.Message); }
        }

        /// <summary>Click: other words. Shift+click: the next class leads.</summary>
        internal static void Cycle(string charName, List<Pick> picks, bool nextLead)
        {
            Choice c = ChoiceFor(charName);
            if (nextLead)
            {
                var ordered = Order(picks, c.lead);
                if (ordered.Count < 2) return;
                c.lead = ordered[1].id;
                // every class has led once: back to the natural order
                if (string.Equals(c.lead, Order(picks, null)[0].id, StringComparison.OrdinalIgnoreCase)) c.lead = null;
            }
            else if (c.custom != null && c.customFor == Signature(picks)) c.custom = c.customFor = null; // back to the generated title
            else c.roll++;
            SaveChoices();
        }

        internal static string For(string charName, List<Pick> picks, out string renownLine)
        {
            Choice c = ChoiceFor(charName);
            string made = Make(Order(picks, c.lead), charName, c.roll, TitleStyle.Value, out renownLine);
            if (ai != null && ai.forName == charName && ai.forClasses == Signature(picks)) return "asking Ollama...";
            // an Ollama title belongs to the class combination it was written for
            if (c.custom != null && c.customFor == Signature(picks)) return c.custom;
            return made;
        }

        /// <summary>The class combination, rank and order aside.</summary>
        internal static string Signature(IEnumerable<Pick> picks) =>
            string.Join("+", picks.Where(p => p != null && !string.IsNullOrEmpty(p.id)).Select(p => p.id).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        private static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        /// <summary>What a click on the title does: Ctrl asks Ollama (when on), Shift moves the lead, a plain click rolls words.</summary>
        internal static void Clicked(string charName, List<Pick> picks)
        {
            if (Ctrl && AiTitles.Value) Ask(charName, picks);
            else Cycle(charName, picks, Shift);
        }

        // ---- Ollama ---------------------------------------------------------------------------------------------

        private class AiJob
        {
            public string forName, forClasses, result, error;
            public volatile bool done;
        }
        private static AiJob ai;

        internal static bool AiBusy => ai != null;

        private static string Describe(string id)
        {
            ClassDef d = ClassRegistry.AllDefs.FirstOrDefault(x => string.Equals(x.id, id, StringComparison.OrdinalIgnoreCase));
            string text = d?.description;
            if (string.IsNullOrEmpty(text))
            {
                Skill_Tree t = Player_SkillControl.instance?.skillTrees?.FirstOrDefault(x => x != null && x.internalName == id)
                    ?? Menu_Creation.instance?.selectedSkills?.Select(x => x?.skillTree).FirstOrDefault(x => x != null && x.internalName == id);
                text = t != null ? (string)t.description : "";
            }
            text = Regex.Replace(text ?? "", @"<[^>]*>|\[[A-Z]+\]", "").Replace('\n', ' ').Trim();
            // flavour and what the class does (the second paragraph says how it plays)
            text = Regex.Replace(text, @"\s+", " ");
            return text.Length > 400 ? text.Substring(0, 400) + "..." : text;
        }

        internal static string Prompt(List<Pick> ordered)
        {
            var sb = new StringBuilder();
            sb.Append("You name characters in a grim, darkly funny sci-fi dungeon crawler set on a corporate space station. ");
            sb.Append("Write one title for a character who has these classes, strongest first:\n");
            foreach (Pick p in ordered)
            {
                sb.Append("- ").Append(Clean(p.name));
                if (p.maxed) sb.Append(" (mastered)");
                string d = Describe(p.id);
                if (d != "") sb.Append(": ").Append(d);
                var themes = ThemesOf(p.id);
                if (themes.Count > 0) sb.Append(" [plays as: ").Append(string.Join(", ", themes)).Append(']');
                sb.Append('\n');
            }
            sb.Append("The title should name what this whole combination does in a fight, as one identity, the way a crew would nickname a veteran; ");
            sb.Append("weigh the strongest classes most but let every class colour it. ");
            sb.Append("2 to 5 words, Title Case. Do not include a personal name, do not start with \"The\", and do not just list the class names. ");
            sb.Append("Reply with only the title: no quotes, no explanation.");
            return sb.ToString();
        }

        /// <summary>The first line of a model's answer made into a title: reasoning, quotes, labels and odd characters dropped.</summary>
        internal static string Tidy(string answer)
        {
            string s = Regex.Replace(answer ?? "", "<think>.*?</think>", "", RegexOptions.Singleline);
            s = s.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x != "") ?? "";
            s = Regex.Replace(s, @"^(title|answer)\s*:\s*", "", RegexOptions.IgnoreCase);
            s = new string(s.Where(ch => char.IsLetter(ch) || ch == ' ' || ch == '-' || ch == '\'').ToArray());
            s = Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '\'');
            if (s.StartsWith("The ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4);
            if (s.Length > 48) s = s.Substring(0, 48).Trim();
            return s;
        }

        /// <summary>Asks Ollama for a title on a worker thread; <see cref="Tick"/> saves it when it comes back.</summary>
        internal static void Ask(string charName, List<Pick> picks)
        {
            if (ai != null || picks.Count == 0) return;
            var job = new AiJob { forName = charName, forClasses = Signature(picks) };
            // a title needs a small context; Ollama's default can be huge (and spill the model off the GPU)
            string body = JsonConvert.SerializeObject(new { model = OllamaModel.Value, prompt = Prompt(Order(picks, ChoiceFor(charName).lead)), stream = false, options = new { temperature = 0.9, num_ctx = 2048 } });
            // Mono resolves localhost to IPv6 first, where Ollama doesn't listen, and may send it through the system proxy
            string url = Regex.Replace(OllamaUrl.Value.TrimEnd('/'), @"^(https?://)localhost\b", "${1}127.0.0.1", RegexOptions.IgnoreCase) + "/api/generate";
            ai = job;
            Plugin.Log.LogInfo($"Class title: asking {OllamaModel.Value} at {url} for {job.forClasses}");
            new System.Threading.Thread(() =>
            {
                try
                {
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                    var host = new Uri(url).Host;
                    if (host == "127.0.0.1" || host == "::1" || host == "[::1]") req.Proxy = null;
                    req.ServicePoint.Expect100Continue = false;
                    req.KeepAlive = false;
                    req.Method = "POST";
                    req.ContentType = "application/json";
                    req.Timeout = req.ReadWriteTimeout = 180000; // the first call loads the model
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    using (Stream st = req.GetRequestStream()) st.Write(bytes, 0, bytes.Length);
                    using (var resp = req.GetResponse())
                    using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        job.result = Tidy((string)JObject.Parse(r.ReadToEnd())["response"]);
                    if (string.IsNullOrEmpty(job.result)) job.error = "the answer had no usable title";
                }
                catch (System.Net.WebException e) when (e.Response is System.Net.HttpWebResponse hr)
                {
                    string detail = "";
                    try { using (var r = new StreamReader(hr.GetResponseStream())) detail = r.ReadToEnd(); } catch { }
                    job.error = $"{(int)hr.StatusCode} {detail}";
                }
                catch (Exception e) { job.error = e.Message; }
                job.done = true;
            }) { IsBackground = true, Name = "SCCL class title" }.Start();
            RefreshSheet();
            RefreshCreation();
        }

        /// <summary>Main thread, every frame: saves a finished Ollama title.</summary>
        internal static void Tick()
        {
            AiJob job = ai;
            if (job == null || !job.done) return;
            ai = null;
            if (job.error != null) Plugin.Log.LogWarning($"Class title: Ollama gave no title ({job.error}). Is it running at {OllamaUrl.Value}, with {OllamaModel.Value} pulled?");
            else
            {
                Choice c = ChoiceFor(job.forName);
                c.custom = job.result;
                c.customFor = job.forClasses;
                SaveChoices();
                Plugin.Log.LogInfo($"Class title: Ollama wrote '{job.result}' for {job.forName}");
            }
            try { RefreshSheet(); RefreshCreation(); }
            catch (Exception e) { Plugin.Log.LogError("Class title refresh: " + e); }
        }

        // ---- the character sheet ----------------------------------------------------------------------------------

        private const string SheetName = "SCCL_ClassTitle";

        internal static Text SheetLabel(Menu_Player mp)
        {
            Text level = mp?.playerLevel;
            if (level == null) return null;
            Transform found = level.transform.parent.Find(SheetName);
            if (found != null) return found.GetComponent<Text>();
            GameObject go = UnityEngine.Object.Instantiate(level.gameObject, level.transform.parent);
            go.name = SheetName;
            foreach (Component extra in go.GetComponents<Component>().Where(x => !(x is Transform) && !(x is Text) && !(x is CanvasRenderer)).ToList())
                UnityEngine.Object.Destroy(extra);
            var t = go.GetComponent<Text>();
            var rt = (RectTransform)go.transform;
            var lr = (RectTransform)level.transform;
            rt.anchoredPosition = lr.anchoredPosition - new Vector2(0f, lr.rect.height * 0.9f);
            rt.sizeDelta = new Vector2(Mathf.Max(lr.sizeDelta.x, 0f) + 160f, lr.sizeDelta.y);
            t.fontSize = Mathf.Max(10, Mathf.RoundToInt(level.fontSize * 0.85f));
            t.fontStyle = FontStyle.Italic;
            t.color = new Color(0.99f, 0.75f, 0.35f, 1f);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = true;
            var b = go.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() =>
            {
                if (Player.instance == null) return;
                Clicked(Player.instance.GetName(), FromPlayer());
                RefreshSheet();
            });
            return t;
        }

        internal static void RefreshSheet()
        {
            Menu_Player mp = Menu_Player.instance;
            if (mp == null || Player.instance == null || mp.playerName == null) return;
            string name = Player.instance.GetName();
            Text label = SheetLabel(mp);
            if (Enabled == null || !Enabled.Value)
            {
                mp.playerName.text = name;
                if (label != null) label.gameObject.SetActive(false);
                return;
            }
            string title = For(name, FromPlayer(), out string renownLine);
            if (AiBusy) renownLine = ""; // just "asking Ollama..." while it writes
            bool epithet = TitleStyle.Value == Style.Epithet && title != "";
            mp.playerName.text = epithet ? name + " the " + title : name;
            if (label == null) return;
            string line = epithet ? renownLine : (renownLine == "" ? title : title + ", " + renownLine);
            label.text = line;
            label.gameObject.SetActive(line != "");
        }

        [HarmonyPatch(typeof(Menu_Player), nameof(Menu_Player.Show))]
        private static class Patch_Sheet
        {
            private static void Postfix()
            {
                try { RefreshSheet(); }
                catch (Exception e) { Plugin.Log.LogError("Class title on the character sheet: " + e); }
            }
        }

        // ---- the creation screen ----------------------------------------------------------------------------------

        private const string CreationName = "SCCL_ClassTitlePreview";
        private static TextMeshProUGUI preview;

        private static TextMeshProUGUI PreviewLabel(Menu_Creation mc)
        {
            if (preview != null) return preview;
            TMP_InputField box = mc?.nameInput;
            if (box == null) return null;
            var boxRt = (RectTransform)box.transform;
            RectTransform rt = UiKit.Rect(boxRt, CreationName, new Vector2(Mathf.Max(boxRt.rect.width, 300f) + 120f, 30f));
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -4f);
            TMP_Text boxText = box.textComponent;
            preview = UiKit.Label(rt, "", boxText != null ? boxText.font : UiKit.FontFrom(mc), boxText != null ? boxText.fontSize * 0.7f : 18f);
            preview.fontStyle = FontStyles.Italic;
            preview.color = new Color(0.99f, 0.75f, 0.35f, 1f);
            preview.raycastTarget = true;
            var b = preview.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() =>
            {
                Menu_Creation m = Menu_Creation.instance;
                if (m == null) return;
                Clicked(m.nameInput != null ? m.nameInput.text : "", FromCreation(m));
                RefreshCreation();
            });
            box.onValueChanged.AddListener(_ => RefreshCreation());
            return preview;
        }

        internal static void RefreshCreation()
        {
            Menu_Creation mc = Menu_Creation.instance;
            if (mc == null || mc.nameInput == null) return;
            TextMeshProUGUI label = PreviewLabel(mc);
            if (label == null) return;
            bool on = Enabled.Value && OnCreation.Value;
            string name = mc.nameInput.text ?? "";
            string title = on ? For(name, FromCreation(mc), out _) : "";
            if (title != "" && TitleStyle.Value == Style.Epithet) title = (name.Trim() == "" ? "" : name.Trim() + " ") + "the " + title;
            label.text = title;
            label.gameObject.SetActive(title != "");
        }

        internal static string CreationPreview => preview != null && preview.gameObject.activeSelf ? preview.text : "";

        [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.Skill_Select))]
        private static class Patch_Select
        {
            private static void Postfix()
            {
                try { RefreshCreation(); }
                catch (Exception e) { Plugin.Log.LogError("Class title on the creation screen: " + e); }
            }
        }

        // presets, Random and the previous character fill the picks without Skill_Select; each ends in DoSkillCheck
        [HarmonyPatch(typeof(Menu_Creation), "DoSkillCheck")]
        internal static class Patch_Check
        {
            private static void Postfix()
            {
                try { RefreshCreation(); }
                catch (Exception e) { Plugin.Log.LogError("Class title on the creation screen: " + e); }
            }
        }

        [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.Show))]
        private static class Patch_CreationShow
        {
            private static void Postfix()
            {
                try { RefreshCreation(); }
                catch (Exception e) { Plugin.Log.LogError("Class title on the creation screen: " + e); }
            }
        }
    }
}
