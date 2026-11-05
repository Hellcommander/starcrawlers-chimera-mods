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
    /// Ollama. All of it is kept per character in SCCL_titles.bwt, a title archive (TitleArchive.cs).
    /// </summary>
    internal static class ClassTitles
    {
        internal enum Style { Full, Short, Epithet }
        internal enum Source { Mastery, Words }

        internal static ConfigEntry<bool> Enabled, OnCreation, ShowRenown, AiTitles;
        internal static ConfigEntry<Style> TitleStyle;
        internal static ConfigEntry<Source> TitleSource;
        internal static ConfigEntry<string> OllamaUrl, OllamaModel, OllamaKeepAlive;

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
            OllamaKeepAlive = cfg.Bind("Titles", "OllamaKeepAlive", "1m", "How long Ollama keeps the model in video memory after writing (Ollama durations: 30s, 5m, 0 to unload at once). Short keeps VRAM free for the game.");
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
        /// <summary>Per theme: adjectives and "of" epithets that let the themes past the top two into a full title.</summary>
        private static Dictionary<string, TitleSpec> themeWords;

        internal static void Load()
        {
            specs = new Dictionary<string, TitleSpec>(StringComparer.OrdinalIgnoreCase);
            renown = new List<RenownSpec>();
            combos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            themeTitles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            themeWords = new Dictionary<string, TitleSpec>(StringComparer.OrdinalIgnoreCase);
            derived.Clear();
            foreach (ClassDef d in ClassRegistry.AllDefs)
                if (d?.title != null && !string.IsNullOrEmpty(d.id)) specs[d.id] = d.title;
            foreach (ModPack p in ModPacks.Ordered)
            {
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
                            int n = key.Split('+').Length;
                            if (n >= 2 && n <= 7 && !string.IsNullOrWhiteSpace(text)) combos[key] = text.Trim();
                            else Plugin.Log.LogError($"{file}: title combo '{kv.Key}' needs two to seven class ids joined with + and a title");
                        }
                    if (obj["themeTitles"] is JObject tt)
                        foreach (var kv in tt)
                        {
                            var names = kv.Value is JArray a ? a.Select(x => (string)x).Where(x => !string.IsNullOrWhiteSpace(x)).ToList() : new List<string> { (string)kv.Value };
                            themeTitles[ThemeKey(kv.Key.Split('+'))] = names;
                        }
                    if (obj["themeWords"] is JObject tw)
                        foreach (var kv in tw)
                        {
                            try { if (kv.Value is JObject o) themeWords[kv.Key.Trim().ToLowerInvariant()] = o.ToObject<TitleSpec>(); }
                            catch (Exception e) { Plugin.Log.LogError($"{file}: theme words for '{kv.Key}' could not be read: {e.Message}"); }
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
                // title archives (*.bwt, the Class Titler writes one): titleCombos in their own compressed form, after the mod's json
                foreach (string bwt in p.folder != null && Directory.Exists(p.folder) ? Directory.GetFiles(p.folder, "*.bwt", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() : new string[0])
                {
                    try
                    {
                        var arc = Breachwork.Titles.TitleArchive.Load(bwt);
                        int n = 0;
                        foreach (var kv in arc.Sets)
                        {
                            int classes = kv.Key.Split('+').Length;
                            string text = kv.Value.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                            if (classes >= 2 && classes <= 7 && text != null) { combos[kv.Key] = text.Trim(); n++; }
                        }
                        Plugin.Log.LogInfo($"Class titles: {n} saved titles from {Path.GetFileName(bwt)} ({p.id}, {new FileInfo(bwt).Length} bytes)");
                    }
                    catch (Exception e) { Plugin.Log.LogError($"{bwt}: title archive could not be read: {e.Message}"); }
                }
            }
            Plugin.Log.LogInfo($"Class titles: words for {specs.Count} classes, {combos.Count} hand-written combos, {themeTitles.Count} mastery names, {renown.Count} renown lines");
        }

        private static TitleSpec Spec(string id)
        {
            if (specs == null) Load();
            if (id == null) return null;
            if (specs.TryGetValue(id, out TitleSpec s)) return s;
            return Learned(id);
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

        /// <summary>The mod folder the Class Titler saves titles into.</summary>
        internal const string YourTitlesMod = "YourClassTitles";

        /// <summary>For the class catalog: the data titles are built from, so the Class Titler shows the in-game title.</summary>
        internal static object CatalogData()
        {
            if (themeTitles == null) Load();
            return new { themeTitles, themeWords, combos };
        }

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
        internal static string Mastery(List<Pick> ordered, string charName, int roll) => Mastery(ordered, charName, roll, Style.Full, out _);

        internal static string Mastery(List<Pick> ordered, string charName, int roll, out string scoreText) => Mastery(ordered, charName, roll, Style.Full, out scoreText);

        /// <summary>The theme totals over all your classes, strongest first (ties alphabetical).</summary>
        internal static List<KeyValuePair<string, int>> ThemeScores(List<Pick> ordered)
        {
            var score = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Pick p in ordered)
            {
                var themes = ThemesOf(p.id);
                for (int i = 0; i < themes.Count && i < 3; i++)
                {
                    string t = themes[i].Trim().ToLowerInvariant();
                    score[t] = (score.TryGetValue(t, out int v) ? v : 0) + (3 - i);
                }
            }
            return score.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        }

        internal static string Mastery(List<Pick> ordered, string charName, int roll, Style style, out string scoreText)
        {
            scoreText = "";
            if (themeTitles == null) Load();
            if (themeTitles.Count == 0) return null;
            // every class counts the same, whatever its rank; ties go alphabetically, so order never matters
            var top = ThemeScores(ordered);
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
            // seeded by the whole class set, not the character: the same seven classes always make the same title,
            // and changing any one of them reseeds every word
            string sig = Signature(ordered);
            int turn = roll / found.Count;
            string name = Choose(themeTitles[key], sig + key, turn, 3);
            if (style == Style.Short) return name;
            // the next strongest themes outside the name: the first gives the "of" epithet, the second the adjective
            var inName = new HashSet<string>(key.Split('+'));
            var rest = top.Select(kv => kv.Key).Where(t => !inName.Contains(t)).ToList();
            string of = rest.Count > 0 ? Choose(ThemeWord(rest[0])?.of, sig + "of" + rest[0], turn, 4) : null;
            string adj = rest.Count > 1 ? Choose(ThemeWord(rest[1])?.adjectives, sig + "adj" + rest[1], turn, 5) : null;
            return (adj != null ? adj + " " : "") + name + (of != null ? " of " + of : "");
        }

        private static TitleSpec ThemeWord(string theme)
        {
            if (themeWords == null) Load();
            return themeWords.TryGetValue(theme, out TitleSpec w) ? w : null;
        }

        /// <summary>What the title is made of: each class with what it adds, then the theme totals and the controls.</summary>
        internal static string Explain(List<Pick> ordered)
        {
            var sb = new StringBuilder();
            sb.Append("Made from all your classes, rank aside:\n");
            foreach (Pick p in ordered)
                sb.Append("<color=#FEBF59>").Append(Clean(p.name)).Append("</color>  ").Append(string.Join(", ", ThemesOf(p.id).DefaultIfEmpty("-"))).Append('\n');
            var scores = ThemeScores(ordered);
            if (scores.Count > 0) sb.Append("\nThemes: ").Append(string.Join(", ", scores.Select(kv => kv.Key + " " + kv.Value))).Append('\n');
            sb.Append("\n<color=#9FB7C9>Click: the next name.");
            if (TitleSource.Value == Source.Words) sb.Append("  Shift+click: the next class leads.");
            if (AiTitles.Value) sb.Append("  Ctrl+click: Ollama writes five for these classes.");
            sb.Append("</color>");
            return sb.ToString();
        }

        internal static string Make(List<Pick> ordered, string charName, int roll, Style style, out string renownLine)
        {
            renownLine = "";
            if (ordered == null || ordered.Count == 0) return "";
            string mastery = TitleSource != null && TitleSource.Value == Source.Mastery ? Mastery(ordered, charName, roll, style, out _) : null;
            Pick lead = ordered[0];
            TitleSpec ls = Spec(lead.id);
            if (combos == null) Load();
            string combo = null;
            // a title saved for exactly these classes (the Class Titler writes them) wins over everything
            if (ordered.Count > 1) combos.TryGetValue(Signature(ordered), out combo);
            // else a hand-written title for your first three, or first two, wins over the generated words
            if (combo == null && ordered.Count > 2 && style != Style.Short) combos.TryGetValue(Signature(ordered.Take(3)), out combo);
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
            /// <summary>The classes Ollama wrote for (sorted ids), and which of the titles for them is shown (-1: none).</summary>
            public string customFor;
            public int pick = -1;
            /// <summary>All the titles Ollama offered for customFor; a click moves to the next, past the last back to the generated one.</summary>
            public List<string> options => customFor != null && asked.TryGetValue(customFor, out List<string> o) ? o : null;
            /// <summary>The Ollama title shown, or null.</summary>
            public string custom => options is List<string> o && pick >= 0 && pick < o.Count ? o[pick] : null;
            internal bool Kept => roll != 0 || lead != null || custom != null;
        }

        private static Dictionary<string, Choice> choices;
        /// <summary>Ollama's titles per class set (sorted ids), kept once however many characters have that set.</summary>
        private static Dictionary<string, List<string>> asked = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The most titles kept for one class set; asking again adds five and drops the oldest past this.</summary>
        internal const int MaxAsked = 20;
        private static string ChoicesPath => Path.Combine(Application.persistentDataPath, "SCCL_titles.bwt");
        /// <summary>The first format (2.1.0): JSON, every character with its full class list and all of Ollama's titles.</summary>
        private static string OldChoicesPath => Path.Combine(Application.persistentDataPath, "SCCL_titles.json");

        internal static Choice ChoiceFor(string charName)
        {
            if (choices == null) ReadChoices();
            if (!choices.TryGetValue(charName ?? "", out Choice c)) choices[charName ?? ""] = c = new Choice();
            return c;
        }

        /// <summary>
        /// SCCL_titles.bwt, a title archive (Breachwork.Titles.TitleArchive): Ollama's titles once per class set, and each
        /// character's roll, lead, set and pick as a record. Without one, an SCCL_titles.json from 2.1.0 is read, written
        /// as the archive, read back, and only then deleted.
        /// </summary>
        private static void ReadChoices()
        {
            choices = new Dictionary<string, Choice>(StringComparer.OrdinalIgnoreCase);
            asked = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(ChoicesPath))
            {
                try
                {
                    var arc = Breachwork.Titles.TitleArchive.Load(ChoicesPath);
                    foreach (var kv in arc.Sets) asked[kv.Key] = kv.Value;
                    foreach (var r in arc.Records)
                        choices[r.name] = new Choice { roll = r.roll, lead = r.lead, customFor = r.set, pick = r.set != null ? r.pick : -1 };
                }
                catch (Exception e) { Plugin.Log.LogWarning("SCCL_titles.bwt could not be read: " + e.Message); }
                return;
            }
            if (!File.Exists(OldChoicesPath)) return;
            try { ReadOldChoices(JObject.Parse(File.ReadAllText(OldChoicesPath))); }
            catch (Exception e) { Plugin.Log.LogWarning("SCCL_titles.json could not be read: " + e.Message); return; }
            long before = new FileInfo(OldChoicesPath).Length;
            if (!SaveChoices()) return; // the json stays until the archive is safely written
            File.Delete(OldChoicesPath);
            Plugin.Log.LogInfo($"SCCL_titles.json moved into the title archive SCCL_titles.bwt: {before} bytes to {new FileInfo(ChoicesPath).Length}");
        }

        /// <summary>The first SCCL_titles.json: a character's name to {roll, lead, custom, customFor, options}.</summary>
        private static void ReadOldChoices(JObject root)
        {
            foreach (var kv in root)
            {
                if (!(kv.Value is JObject o)) continue;
                var c = new Choice { roll = (int?)o["roll"] ?? 0, lead = (string)o["lead"] };
                string custom = (string)o["custom"], set = (string)o["customFor"];
                if (custom != null && set != null)
                {
                    var offered = (o["options"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
                    if (!offered.Contains(custom)) offered.Insert(0, custom);
                    c.customFor = set;
                    c.pick = AddAsked(set, offered, custom);
                }
                choices[kv.Key] = c;
            }
        }

        /// <summary>
        /// Adds Ollama's titles to the ones kept for a set (a title it wrote before isn't kept twice), dropping the oldest
        /// past <see cref="MaxAsked"/> and moving every character's pick with them. Returns where <paramref name="show"/> is.
        /// </summary>
        private static int AddAsked(string set, List<string> titles, string show)
        {
            if (!asked.TryGetValue(set, out List<string> list)) asked[set] = list = new List<string>();
            var shown = choices.Values.Where(c => c.customFor == set).Select(c => (c, c.custom)).ToList();
            foreach (string t in titles)
                if (!string.IsNullOrWhiteSpace(t) && !list.Contains(t, StringComparer.OrdinalIgnoreCase)) list.Add(t);
            // drop the oldest, sparing the titles a character shows now and the new one
            for (int i = 0; list.Count > MaxAsked && i < list.Count; )
            {
                string t = list[i];
                if (t == show || shown.Any(s => s.custom == t)) i++;
                else list.RemoveAt(i);
            }
            foreach (var (c, title) in shown) c.pick = title == null ? -1 : list.IndexOf(title);
            return list.FindIndex(x => string.Equals(x, show, StringComparison.OrdinalIgnoreCase));
        }

        private static bool SaveChoices()
        {
            try
            {
                // titles for a set no character shows any more can't be reached: forget them
                var inUse = new HashSet<string>(choices.Values.Where(c => c.custom != null).Select(c => c.customFor), StringComparer.OrdinalIgnoreCase);
                foreach (string gone in asked.Keys.Where(k => !inUse.Contains(k)).ToList()) asked.Remove(gone);
                var arc = new Breachwork.Titles.TitleArchive();
                foreach (var kv in choices.Where(kv => kv.Key != "" && kv.Value.Kept))
                {
                    Choice c = kv.Value;
                    bool shows = c.custom != null;
                    if (shows) arc.Sets[c.customFor] = asked[c.customFor];
                    arc.Records.Add(new Breachwork.Titles.TitleArchive.Record { name = kv.Key, roll = c.roll, lead = c.lead, set = shows ? c.customFor : null, pick = shows ? c.pick : -1 });
                }
                arc.Save(ChoicesPath);
                return true;
            }
            catch (Exception e) { Plugin.Log.LogWarning("SCCL_titles.bwt could not be written: " + e.Message); return false; }
        }

        /// <summary>Devtest: reads SCCL_titles again, as a new launch would.</summary>
        internal static void ReloadChoices() => ReadChoices();
        internal static string ChoicesFile => ChoicesPath;
        internal static string OldChoicesFile => OldChoicesPath;
        internal static int AskedSets => asked.Count;

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
            else if (c.custom != null && c.customFor == Signature(picks))
            {
                if (c.pick + 1 < c.options.Count) c.pick++;
                else { c.customFor = null; c.pick = -1; } // past the last: back to the generated title
            }
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
            public string forName, forClasses, error;
            public List<string> results;
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
            // flavour and what the class does (the second paragraph says how it plays), then its skills
            text = Regex.Replace(text, @"\s+", " ");
            if (text.Length > 600) text = text.Substring(0, 600) + "...";
            var skills = Skills(id);
            if (skills.Count > 0) text += " Skills: " + string.Join(", ", skills) + ".";
            return text;
        }

        /// <summary>The names of a class's skills and passives, in tier order.</summary>
        internal static List<string> Skills(string id)
        {
            var names = new List<string>();
            foreach (Skill_Tier tier in FindTree(id)?.skillTiers ?? new List<Skill_Tier>())
                foreach (Ability a in new[] { tier?.ability, tier?.passive })
                {
                    string n = a != null ? Regex.Replace((string)a.displayName ?? "", "<[^>]*>", "").Trim() : "";
                    if (n != "" && !n.Contains("NYI") && !names.Contains(n)) names.Add(n); // "* NYI *": a tier the game never finished
                }
            return names;
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
            sb.Append("Each title 2 to 5 words, Title Case. Do not include a personal name, do not start with \"The\", and do not just list the class names. ");
            sb.Append("Write 5 different titles, one per line, and nothing else: no numbering, no quotes, no explanation.");
            return sb.ToString();
        }

        /// <summary>A model's answer made into titles, one per line: reasoning, numbering, quotes, labels and odd characters dropped.</summary>
        internal static List<string> TidyAll(string answer)
        {
            string s = Regex.Replace(answer ?? "", "<think>.*?</think>", "", RegexOptions.Singleline);
            return s.Split('\n').Select(Tidy).Where(x => x.Length >= 3 && x.Split(' ').Length <= 7)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
        }

        /// <summary>One line of a model's answer made into a title.</summary>
        internal static string Tidy(string line)
        {
            string s = (line ?? "").Trim();
            s = Regex.Replace(s, @"^\s*(\d+\s*[.):-]|[-*\u2022])\s*", "");
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
            // seven classes with their descriptions and skills fit in 4k; Ollama's default can be huge (and spill the model off the GPU)
            string body = JsonConvert.SerializeObject(new { model = OllamaModel.Value, prompt = Prompt(Order(picks, ChoiceFor(charName).lead)), stream = false, keep_alive = OllamaKeepAlive.Value, options = new { temperature = 0.9, num_ctx = 4096 } });
            // Mono resolves localhost to IPv6 first, where Ollama doesn't listen, and may send it through the system proxy
            string url = GenerateUrl;
            ai = job;
            Plugin.Log.LogInfo($"Class title: asking {OllamaModel.Value} at {url} for {job.forClasses}");
            new System.Threading.Thread(() =>
            {
                try
                {
                    job.results = TidyAll(Post(url, body));
                    if (job.results == null || job.results.Count == 0) job.error = "the answer had no usable title";
                }
                catch (Exception e) { job.error = e.Message; }
                job.done = true;
            }) { IsBackground = true, Name = "SCCL class title" }.Start();
            RefreshSheet();
            RefreshCreation();
        }

        // Mono resolves localhost to IPv6 first, where Ollama doesn't listen, and may send it through the system proxy
        private static string GenerateUrl => Regex.Replace(OllamaUrl.Value.TrimEnd('/'), @"^(https?://)localhost\b", "${1}127.0.0.1", RegexOptions.IgnoreCase) + "/api/generate";

        /// <summary>One request to Ollama's generate endpoint (worker thread); returns its "response" text.</summary>
        private static string Post(string url, string body)
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
                    return (string)JObject.Parse(r.ReadToEnd())["response"];
            }
            catch (System.Net.WebException e) when (e.Response is System.Net.HttpWebResponse hr)
            {
                string detail = "";
                try { using (var r = new StreamReader(hr.GetResponseStream())) detail = r.ReadToEnd(); } catch { }
                throw new Exception($"{(int)hr.StatusCode} {detail}");
            }
        }

        // ---- class words Ollama writes from a class's description -------------------------------------------------

        internal static readonly string[] Themes = { "fire", "frost", "storm", "rot", "blood", "void", "mind", "gun", "blade", "brawn", "guard", "tech", "swarm", "guile", "arcane" };

        /// <summary>Title words for classes no mod gave any (SCCL_classwords.json), each with the text they were written from.</summary>
        internal class LearnedWords { public string from; public TitleSpec words; }

        private static Dictionary<string, LearnedWords> learned;
        private static readonly Queue<string> learnQueue = new Queue<string>();
        private static readonly HashSet<string> learnTried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> learnRetried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Title Case: every word capitalised, small joining words lower case except at the start (or always, for "of" phrases).</summary>
        internal static string Cap(string s, bool phrase = false)
        {
            var small = new HashSet<string> { "the", "of", "a", "an", "and", "in", "on" };
            var words = (s ?? "").Split(' ').Where(w => w != "").ToArray();
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                bool keepSmall = small.Contains(w.ToLowerInvariant()) && (i > 0 || phrase);
                words[i] = keepSmall ? w.ToLowerInvariant() : char.ToUpperInvariant(w[0]) + w.Substring(1);
            }
            return string.Join(" ", words);
        }
        private class LearnJob { public string id, from, error; public TitleSpec words; public volatile bool done; }
        private static LearnJob learning;
        private static string LearnedPath => Path.Combine(Application.persistentDataPath, "SCCL_classwords.json");

        internal static bool Learning => learning != null || learnQueue.Count > 0;

        /// <summary>Words Ollama wrote for this class, if its description hasn't changed since; else queues it when Ollama titles are on.</summary>
        private static TitleSpec Learned(string id)
        {
            if (learned == null)
            {
                try { learned = File.Exists(LearnedPath) ? JsonConvert.DeserializeObject<Dictionary<string, LearnedWords>>(File.ReadAllText(LearnedPath)) : null; }
                catch (Exception e) { Plugin.Log.LogWarning("SCCL_classwords.json could not be read: " + e.Message); }
                learned = new Dictionary<string, LearnedWords>(learned ?? new Dictionary<string, LearnedWords>(), StringComparer.OrdinalIgnoreCase);
            }
            if (FindTree(id) == null) return null; // not a class in this game (or not loaded yet)
            string from = Describe(id);
            if (learned.TryGetValue(id, out LearnedWords w) && w.words != null && w.from == from) return w.words;
            if (AiTitles != null && AiTitles.Value && from != "" && learnTried.Add(id) && learning?.id != id && !learnQueue.Contains(id)) learnQueue.Enqueue(id);
            return null;
        }

        internal static string ClassPrompt(string id, string name, string from)
        {
            var sb = new StringBuilder();
            sb.Append("Here is a character class from a grim, darkly funny sci-fi dungeon crawler set on a corporate space station.\n");
            sb.Append("Class: ").Append(Clean(name)).Append('\n');
            sb.Append("What it is and does: ").Append(from).Append('\n');
            sb.Append("Reply with JSON only, with these fields:\n");
            sb.Append("\"themes\": 1 to 3 words for what this class does in a fight, strongest first, chosen only from: ").Append(string.Join(", ", Themes)).Append(".\n");
            sb.Append("\"adjectives\": 2 single adjectives for someone of this class, used like \"Kindled Gunslinger\".\n");
            sb.Append("\"nouns\": 2 short nouns for a person of this class, like \"Gunslinger\".\n");
            sb.Append("\"of\": 2 short phrases used after \"of\" in a title, like \"the Ember\".\n");
            sb.Append("\"master\": 1 short noun for someone who has mastered this class.\n");
            sb.Append("Do not use the word \"The\" at the start of nouns. Title Case.");
            return sb.ToString();
        }

        /// <summary>Main thread: starts the next queued class, saves a finished one. One request at a time.</summary>
        private static void TickLearn()
        {
            LearnJob job = learning;
            if (job != null)
            {
                if (!job.done) return;
                learning = null;
                if (job.error != null)
                {
                    // one more try: a small model sometimes invents a theme outside the list
                    bool retry = learnRetried.Add(job.id);
                    Plugin.Log.LogWarning($"Class title: Ollama gave no words for {job.id} ({job.error}){(retry ? "; asking once more" : "")}");
                    if (retry) learnQueue.Enqueue(job.id);
                }
                else
                {
                    learned[job.id] = new LearnedWords { from = job.from, words = job.words };
                    derived.Remove(job.id);
                    try { File.WriteAllText(LearnedPath, JsonConvert.SerializeObject(learned, Formatting.Indented)); }
                    catch (Exception e) { Plugin.Log.LogWarning("SCCL_classwords.json could not be written: " + e.Message); }
                    Plugin.Log.LogInfo($"Class title: Ollama wrote words for {job.id}: themes {string.Join(", ", job.words.themes)}; {string.Join(", ", job.words.adjectives)}; {string.Join(", ", job.words.nouns)}");
                    try { RefreshSheet(); RefreshCreation(); } catch (Exception e) { Plugin.Log.LogError("Class title refresh: " + e); }
                }
            }
            if (ai != null || learnQueue.Count == 0) return; // a title request goes first
            string id = learnQueue.Dequeue();
            Skill_Tree tree = FindTree(id);
            var next = new LearnJob { id = id, from = Describe(id) };
            string body = JsonConvert.SerializeObject(new { model = OllamaModel.Value, prompt = ClassPrompt(id, tree != null ? (string)tree.displayName : id, next.from), stream = false, format = "json", keep_alive = OllamaKeepAlive.Value, options = new { temperature = 0.6, num_ctx = 4096 } });
            string url = GenerateUrl;
            learning = next;
            new System.Threading.Thread(() =>
            {
                try
                {
                    string answer = Regex.Replace(Post(url, body) ?? "", "<think>.*?</think>", "", RegexOptions.Singleline);
                    JObject o = JObject.Parse(answer);
                    List<string> Words(string key) => (o[key] as JArray)?.Select(x => Cap(Tidy((string)x))).Where(x => x.Length >= 2).Take(3).ToList() ?? new List<string>();
                    var w = new TitleSpec
                    {
                        adjectives = Words("adjectives"),
                        nouns = Words("nouns"),
                        of = (o["of"] as JArray)?.Select(x => Cap(Regex.Replace(Tidy((string)x), @"^of\s+", "", RegexOptions.IgnoreCase), true)).Where(x => x.Length >= 2).Take(3).ToList() ?? new List<string>(),
                        master = Cap(Tidy((string)o["master"] ?? "")),
                    };
                    var said = o["themes"] is JArray ta ? ta.Select(x => (string)x ?? "").ToList() : new List<string> { (string)o["themes"] ?? "" };
                    w.themes = ThemesFrom(said);
                    if (w.themes.Count == 0) next.error = "no theme from the list in '" + string.Join(", ", said) + "'";
                    else if (w.nouns.Count == 0) next.error = "no nouns";
                    else next.words = w;
                }
                catch (Exception e) { next.error = e.Message; }
                next.done = true;
            }) { IsBackground = true, Name = "SCCL class words" }.Start();
        }

        /// <summary>Words a small model often uses for a theme instead of the theme's own name.</summary>
        private static readonly Dictionary<string, string> ThemeSynonyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["flame"] = "fire", ["flames"] = "fire", ["burn"] = "fire", ["burning"] = "fire", ["pyro"] = "fire", ["heat"] = "fire", ["explosive"] = "fire", ["explosives"] = "fire",
            ["ice"] = "frost", ["cold"] = "frost", ["freeze"] = "frost", ["cryo"] = "frost", ["water"] = "frost",
            ["lightning"] = "storm", ["electric"] = "storm", ["electricity"] = "storm", ["shock"] = "storm", ["thunder"] = "storm", ["wind"] = "storm", ["weather"] = "storm",
            ["poison"] = "rot", ["toxic"] = "rot", ["toxin"] = "rot", ["acid"] = "rot", ["disease"] = "rot", ["decay"] = "rot", ["chemical"] = "rot", ["chemistry"] = "rot",
            ["bleed"] = "blood", ["bleeding"] = "blood", ["vampiric"] = "blood", ["flesh"] = "blood",
            ["shadow"] = "void", ["dark"] = "void", ["darkness"] = "void", ["gravity"] = "void", ["space"] = "void", ["cosmic"] = "void", ["astral"] = "void",
            ["psychic"] = "mind", ["psi"] = "mind", ["mental"] = "mind", ["telepathy"] = "mind", ["control"] = "mind",
            ["guns"] = "gun", ["ranged"] = "gun", ["shooting"] = "gun", ["firearms"] = "gun", ["sniper"] = "gun",
            ["melee"] = "blade", ["sword"] = "blade", ["blades"] = "blade", ["knife"] = "blade",
            ["strength"] = "brawn", ["brute"] = "brawn", ["force"] = "brawn", ["tank"] = "brawn", ["physical"] = "brawn",
            ["defense"] = "guard", ["defence"] = "guard", ["shield"] = "guard", ["protection"] = "guard", ["support"] = "guard", ["healing"] = "guard",
            ["technology"] = "tech", ["hacking"] = "tech", ["machines"] = "tech", ["drones"] = "tech", ["engineering"] = "tech", ["crafting"] = "tech",
            ["summon"] = "swarm", ["summons"] = "swarm", ["summoning"] = "swarm", ["minions"] = "swarm", ["pets"] = "swarm", ["allies"] = "swarm",
            ["stealth"] = "guile", ["trickery"] = "guile", ["tricks"] = "guile", ["luck"] = "guile", ["cunning"] = "guile", ["deception"] = "guile",
            ["magic"] = "arcane", ["spells"] = "arcane", ["spell"] = "arcane", ["elemental"] = "arcane", ["mana"] = "arcane", ["runes"] = "arcane", ["sorcery"] = "arcane",
        };

        /// <summary>Themes from what a model said: the theme names themselves, or common words for them, in its order.</summary>
        internal static List<string> ThemesFrom(IEnumerable<string> said)
        {
            var found = new List<string>();
            foreach (string phrase in said)
                foreach (string word in Regex.Split((phrase ?? "").ToLowerInvariant(), "[^a-z]+").Where(x => x != ""))
                {
                    string t = Themes.Contains(word) ? word : ThemeSynonyms.TryGetValue(word, out string m) ? m : null;
                    if (t != null && !found.Contains(t)) found.Add(t);
                }
            return found.Take(3).ToList();
        }

        /// <summary>Devtest: act as if no mod gave this class title words.</summary>
        internal static void ForgetSpec(string id) { if (specs == null) Load(); specs.Remove(id); derived.Remove(id); }

        internal static string DescribeFor(string id) => Describe(id);

        /// <summary>Devtest: forget what was learned for a class and ask again.</summary>
        internal static void Relearn(string id)
        {
            Learned(id);
            learned.Remove(id);
            learnTried.Remove(id);
            learnRetried.Remove(id);
            derived.Remove(id);
            Learned(id);
        }

        /// <summary>Main thread, every frame: saves a finished Ollama title or class words.</summary>
        internal static void Tick()
        {
            TickLearn();
            AiJob job = ai;
            if (job == null || !job.done) return;
            ai = null;
            if (job.error != null) Plugin.Log.LogWarning($"Class title: Ollama gave no title ({job.error}). Is it running at {OllamaUrl.Value}, with {OllamaModel.Value} pulled?");
            else
            {
                Choice c = ChoiceFor(job.forName);
                c.customFor = job.forClasses;
                c.pick = AddAsked(job.forClasses, job.results, job.results[0]);
                SaveChoices();
                Plugin.Log.LogInfo($"Class title: Ollama wrote {string.Join(" / ", job.results)} for {job.forName}");
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
            t.fontSize = Mathf.Max(10, Mathf.RoundToInt(level.fontSize * 0.85f));
            t.fontStyle = FontStyle.Italic;
            t.color = new Color(0.99f, 0.75f, 0.35f, 1f);
            // one line, sized by FitSheet to the sheet's left column
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = false;
            sheetFont = t.fontSize;
            t.raycastTarget = true;
            AddTip(go, () => Player.instance == null ? null : (sheetRenown != "" ? "<color=#FEBF59>" + sheetRenown + "</color>\n\n" : "") + Explain(Order(FromPlayer(), ChoiceFor(Player.instance.GetName()).lead)));
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

        /// <summary>Shows <paramref name="text"/> (worked out on hover) in the game's small tooltip while the pointer is over <paramref name="go"/>.</summary>
        private static void AddTip(GameObject go, Func<string> text)
        {
            var trigger = go.GetComponent<UnityEngine.EventSystems.EventTrigger>() ?? go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var enter = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                try { string t = text(); if (!string.IsNullOrEmpty(t)) Tooltip.instance?.Show_TinyTip("Class title", t, null); }
                catch (Exception e) { Plugin.Log.LogWarning("Class title tip: " + e.Message); }
            });
            var exit = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => Tooltip.instance?.Hide());
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }

        // ---- the load list ----------------------------------------------------------------------------------------

        private const string SaveName = "SCCL_ClassTitleSave";

        /// <summary>The save's title beside its name on the load list, from the classes in the save file (SaveTrees reads them).</summary>
        internal static void OnSaveWidget(SaveWidget w, string[] trees, int[] ranks, Dictionary<string, Skill_Tree> byId)
        {
            TMP_Text nameText = w?.saveName;
            if (nameText == null) return;
            Transform old = nameText.transform.Find(SaveName);
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            if (!Enabled.Value) return;
            var picks = new List<Pick>();
            for (int i = 0; i < trees.Length; i++)
            {
                if (string.IsNullOrEmpty(trees[i])) continue;
                byId.TryGetValue(trees[i], out Skill_Tree t);
                int rank = ranks != null && i < ranks.Length ? ranks[i] : 0;
                picks.Add(new Pick { id = trees[i], name = t != null ? (string)t.displayName : trees[i], rank = rank, order = i,
                    maxed = t?.skillTiers != null && t.skillTiers.Count > 0 && rank == t.skillTiers.Count - 1 });
            }
            string title = For(nameText.text, picks, out _);
            if (title == "" || AiBusy && title.StartsWith("asking")) return;
            nameText.ForceMeshUpdate();
            RectTransform rt = UiKit.Rect(nameText.rectTransform, SaveName, new Vector2(520f, nameText.rectTransform.rect.height));
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            float textLeft = nameText.alignment == TextAlignmentOptions.Center || nameText.alignment == TextAlignmentOptions.Midline
                ? (nameText.rectTransform.rect.width - nameText.preferredWidth) / 2f : 0f;
            rt.anchoredPosition = new Vector2(textLeft + nameText.preferredWidth + 14f, 0f);
            TextMeshProUGUI label = UiKit.Label(rt, title, nameText.font, nameText.fontSize * 0.62f, TextAlignmentOptions.Left);
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            label.fontStyle = FontStyles.Italic;
            label.color = new Color(0.99f, 0.75f, 0.35f, 1f);
        }

        internal static string SaveTitle(SaveWidget w)
        {
            Transform t = w?.saveName?.transform.Find(SaveName);
            return t != null ? t.GetComponentInChildren<TMP_Text>()?.text ?? "" : "";
        }

        private static int sheetFont = 20;
        private static string sheetRenown = "";
        internal static string SheetRenown => sheetRenown;

        /// <summary>
        /// Puts the first of <paramref name="lines"/> that fits the sheet's left column on one line at no less than
        /// 70% of the title's font size, shrinking the font as needed; the last one is used (smaller still) if none do.
        /// </summary>
        private static void FitSheet(Menu_Player mp, Text t, params string[] lines)
        {
            var header = (RectTransform)mp.playerName.transform.parent;
            var c = new Vector3[4];
            header.GetWorldCorners(c);
            float scale = Mathf.Max(0.0001f, t.rectTransform.lossyScale.x);
            float width = (c[2].x - c[0].x) * 0.46f / scale; // the portrait column, left of the attributes
            var rt = t.rectTransform;
            rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
            foreach (string line in lines.Where(x => !string.IsNullOrEmpty(x)).Distinct())
            {
                t.text = line;
                bool last = line == lines.Last(x => !string.IsNullOrEmpty(x));
                int min = last ? Mathf.Max(8, sheetFont / 2) : Mathf.CeilToInt(sheetFont * 0.7f);
                for (int size = sheetFont; size >= min; size--)
                {
                    t.fontSize = size;
                    if (t.preferredWidth <= width) return;
                }
            }
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
            sheetRenown = renownLine;
            label.gameObject.SetActive(line != "");
            // with the renown if it fits; otherwise the title alone (the renown stays in the hover tip)
            if (line != "") FitSheet(mp, label, line, epithet ? renownLine : title);
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
            AddTip(preview.gameObject, () => Menu_Creation.instance == null ? null : Explain(Order(FromCreation(Menu_Creation.instance), ChoiceFor(Menu_Creation.instance.nameInput?.text ?? "").lead)));
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
