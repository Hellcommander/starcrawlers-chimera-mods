using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ClassTitler
{
    /// <summary>One class from the game's class catalog.</summary>
    internal class ClassInfo
    {
        public string id, name, archetype, mod, description, icon;
        public List<string> skills = new List<string>(), themes = new List<string>();
        /// <summary>The wordsmith's identity words for this class, as the game uses them (its own or the fallbacks).</summary>
        public List<string> traits = new List<string>(), roots = new List<string>(), heads = new List<string>();

        public Breachwork.Titles.WordsmithClass Smith() =>
            new Breachwork.Titles.WordsmithClass { id = id, name = name, traits = traits, roots = roots, heads = heads, themes = themes };
    }

    /// <summary>
    /// The catalog the game writes each launch (BepInEx/cache/BreachworkClassCatalog/classes.json) and the title rules
    /// from the loader's ClassTitles, kept the same here so the titler shows exactly the title the game would.
    /// </summary>
    internal class Catalog
    {
        public string Folder, SaveTo, OllamaUrl = "http://localhost:11434", OllamaModel = "llama3.1:8b";
        /// <summary>The game's WordsmithLength: the longest wordsmith title, in letters.</summary>
        public int WordsmithLength = 32;
        public DateTime Written;
        public List<ClassInfo> Classes = new List<ClassInfo>();
        public Dictionary<string, List<string>> ThemeTitles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, (List<string> adjectives, List<string> of)> ThemeWords = new Dictionary<string, (List<string>, List<string>)>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Combos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static List<string> Strings(object o) => (o as System.Collections.IEnumerable)?.Cast<object>().Select(x => x as string).Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>();
        private static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object> ?? new Dictionary<string, object>();
        private static string Str(Dictionary<string, object> d, string k) => d.TryGetValue(k, out object v) ? v as string : null;

        public static Catalog Load(string folder)
        {
            var c = new Catalog { Folder = folder };
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = Obj(json.DeserializeObject(File.ReadAllText(Path.Combine(folder, "classes.json"))));
            DateTime.TryParse(Str(root, "written"), out c.Written);
            c.SaveTo = Str(root, "saveTo");
            var ollama = Obj(root.TryGetValue("ollama", out object ol) ? ol : null);
            c.OllamaUrl = Str(ollama, "url") ?? c.OllamaUrl;
            c.OllamaModel = Str(ollama, "model") ?? c.OllamaModel;
            if (root.TryGetValue("wordsmithLength", out object wl) && wl is int wli) c.WordsmithLength = wli;
            foreach (object o in (root.TryGetValue("classes", out object cl) ? cl as object[] : null) ?? new object[0])
            {
                var d = Obj(o);
                var smith = Obj(d.TryGetValue("smith", out object sm) ? sm : null);
                c.Classes.Add(new ClassInfo
                {
                    id = Str(d, "id"), name = Str(d, "name"), archetype = Str(d, "archetype"), mod = Str(d, "mod"),
                    description = Str(d, "description") ?? "", icon = Str(d, "icon"),
                    skills = Strings(d.TryGetValue("skills", out object sk) ? sk : null).Where(s => !s.Contains("NYI")).ToList(),
                    themes = Strings(d.TryGetValue("themes", out object th) ? th : null),
                    traits = Strings(smith.TryGetValue("traits", out object tr) ? tr : null),
                    roots = Strings(smith.TryGetValue("roots", out object ro) ? ro : null),
                    heads = Strings(smith.TryGetValue("heads", out object he) ? he : null),
                });
            }
            var titles = Obj(root.TryGetValue("titles", out object tt) ? tt : null);
            foreach (var kv in Obj(titles.TryGetValue("themeTitles", out object a) ? a : null)) c.ThemeTitles[kv.Key] = Strings(kv.Value);
            foreach (var kv in Obj(titles.TryGetValue("themeWords", out object b) ? b : null))
            {
                var w = Obj(kv.Value);
                c.ThemeWords[kv.Key] = (Strings(w.TryGetValue("adjectives", out object x) ? x : null), Strings(w.TryGetValue("of", out object y) ? y : null));
            }
            foreach (var kv in Obj(titles.TryGetValue("combos", out object cb) ? cb : null)) if (kv.Value is string s) c.Combos[kv.Key] = s;
            c.Classes = c.Classes.Where(x => !string.IsNullOrEmpty(x.id)).OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase).ToList();
            return c;
        }

        // ---- the game's title rules (ClassTitles.Mastery, Full style) --------------------------------------------------

        private static readonly Dictionary<string, string> Stats = new Dictionary<string, string>
        {
            ["AGL"] = "Agility", ["PSI"] = "Psi", ["REC"] = "Recognition", ["SAV"] = "Savvy", ["SMT"] = "Smarts", ["STR"] = "Strength", ["TGH"] = "Toughness",
        };

        /// <summary>A description with the game's stat tokens ([STR]) written out.</summary>
        public static string StatNames(string text) =>
            Regex.Replace(text ?? "", @"\[([A-Z]+)\]", m => Stats.TryGetValue(m.Groups[1].Value, out string s) ? s : m.Groups[1].Value).Trim();

        public static string Signature(IEnumerable<string> ids) => string.Join("+", ids.Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        private static string ThemeKey(IEnumerable<string> themes) =>
            string.Join("+", themes.Select(t => t.Trim().ToLowerInvariant()).Where(t => t != "").Distinct().OrderBy(t => t, StringComparer.Ordinal));

        private static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (char ch in s ?? "") { h ^= ch; h *= 16777619; }
            return h;
        }

        private static string Choose(List<string> words, string seed, int roll, int slot)
        {
            var ok = words?.Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
            if (ok == null || ok.Count == 0) return null;
            uint h = Hash(seed + "|" + slot) + (uint)roll * 2654435761u;
            return ok[(int)(h % (uint)ok.Count)].Trim();
        }

        public static List<KeyValuePair<string, int>> ThemeScores(IEnumerable<ClassInfo> classes)
        {
            var score = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (ClassInfo c in classes)
                for (int i = 0; i < c.themes.Count && i < 3; i++)
                {
                    string t = c.themes[i].Trim().ToLowerInvariant();
                    score[t] = (score.TryGetValue(t, out int v) ? v : 0) + (3 - i);
                }
            return score.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        }

        /// <summary>The wordsmith's titles for these classes, best first: what the game shows with TitleSource = Wordsmith.</summary>
        public List<Breachwork.Titles.WordsmithTitle> Forge(List<ClassInfo> picked, int count = 8) =>
            Breachwork.Titles.Wordsmith.Titles(picked.Select(p => p.Smith()), WordsmithLength, count);

        /// <summary>The classes a title names through their identity words.</summary>
        public static List<ClassInfo> Named(string title, List<ClassInfo> picked) =>
            Breachwork.Titles.Wordsmith.Names(title, picked.Select(p => p.Smith()).ToList()).Select(i => picked[i]).ToList();

        /// <summary>The title the game gives these classes: a saved one for exactly this set, else the mastery title.</summary>
        public string GameTitle(List<ClassInfo> picked, out bool saved)
        {
            saved = false;
            if (picked.Count == 0) return "";
            if (picked.Count > 1 && Combos.TryGetValue(Signature(picked.Select(p => p.id)), out string combo)) { saved = true; return combo; }
            var top = ThemeScores(picked);
            if (top.Count == 0) return "";
            var keys = new List<string>();
            bool alone = top.Count == 1 || top[0].Value >= top[1].Value * 2;
            if (alone) keys.Add(ThemeKey(new[] { top[0].Key }));
            for (int a = 0; a < Math.Min(3, top.Count); a++)
                for (int b = a + 1; b < Math.Min(3, top.Count); b++)
                    keys.Add(ThemeKey(new[] { top[a].Key, top[b].Key }));
            if (!alone) keys.Add(ThemeKey(new[] { top[0].Key }));
            var found = keys.Distinct().Where(k => ThemeTitles.TryGetValue(k, out List<string> n) && n.Count > 0).ToList();
            if (found.Count == 0) return "";
            string key = found[0];
            string sig = Signature(picked.Select(p => p.id));
            string name = Choose(ThemeTitles[key], sig + key, 0, 3);
            var inName = new HashSet<string>(key.Split('+'));
            var rest = top.Select(kv => kv.Key).Where(t => !inName.Contains(t)).ToList();
            string of = rest.Count > 0 && ThemeWords.TryGetValue(rest[0], out var w0) ? Choose(w0.of, sig + "of" + rest[0], 0, 4) : null;
            string adj = rest.Count > 1 && ThemeWords.TryGetValue(rest[1], out var w1) ? Choose(w1.adjectives, sig + "adj" + rest[1], 0, 5) : null;
            return (adj != null ? adj + " " : "") + name + (of != null ? " of " + of : "");
        }
    }

    /// <summary>
    /// Your saved titles: Mods/YourClassTitles/titles.bwt, a title archive (Breachwork.Titles.TitleArchive, the same
    /// compressed format the game reads from any mod), loaded like any other mod. A titles.json from 2.1.0
    /// ("titleCombos") is read too; the first save moves it into the archive and, once that reads back, deletes it
    /// (a json with anything besides titleCombos is left in place).
    /// </summary>
    internal class SavedTitles
    {
        public readonly string Folder;
        public readonly SortedDictionary<string, string> Combos = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string Archive => Path.Combine(Folder, "titles.bwt");
        private string OldJson => Path.Combine(Folder, "titles.json");

        public SavedTitles(string folder)
        {
            Folder = folder;
            if (File.Exists(OldJson)
                && new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(File.ReadAllText(OldJson)) is Dictionary<string, object> root
                && root.TryGetValue("titleCombos", out object c) && c is Dictionary<string, object> d)
                foreach (var kv in d) if (kv.Value is string s) Combos[Catalog.Signature(kv.Key.Split('+').Select(x => x.Trim()))] = s;
            if (File.Exists(Archive))
                foreach (var kv in Breachwork.Titles.TitleArchive.Load(Archive).Sets)
                    if (kv.Value.Count > 0) Combos[kv.Key] = kv.Value[0];
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            string manifest = Path.Combine(Folder, "mod.json");
            if (!File.Exists(manifest))
                File.WriteAllText(manifest, "{\n  \"id\": \"YourClassTitles\",\n  \"name\": \"Your Class Titles\",\n  \"author\": \"You\",\n  \"version\": \"1.0.0\",\n" +
                    "  \"description\": \"Titles you picked in the Class Titler for exact class combinations. They win over the generated titles.\"\n}\n");
            var arc = new Breachwork.Titles.TitleArchive();
            foreach (var kv in Combos) arc.Sets[kv.Key] = new List<string> { kv.Value };
            arc.Save(Archive); // writes, reads back, then replaces
            if (File.Exists(OldJson)
                && new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(File.ReadAllText(OldJson)) is Dictionary<string, object> root
                && root.Keys.All(k => k == "titleCombos"))
                File.Delete(OldJson);
        }
    }

    /// <summary>Talks to a local Ollama.</summary>
    internal static class Ollama
    {
        private static string Url(string baseUrl, string path) =>
            Regex.Replace(baseUrl.TrimEnd('/'), @"^(https?://)localhost\b", "${1}127.0.0.1", RegexOptions.IgnoreCase) + path;

        private static string Send(string url, string body)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Proxy = null;
            req.ServicePoint.Expect100Continue = false;
            req.Timeout = req.ReadWriteTimeout = 300000; // the first call loads the model
            if (body != null)
            {
                req.Method = "POST";
                req.ContentType = "application/json";
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                using (Stream s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
            }
            try
            {
                using (var resp = req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return r.ReadToEnd();
            }
            catch (WebException e) when (e.Response is HttpWebResponse hr)
            {
                using (var r = new StreamReader(hr.GetResponseStream())) throw new Exception($"Ollama answered {(int)hr.StatusCode}: {r.ReadToEnd()}");
            }
        }

        public static List<string> Models(string baseUrl)
        {
            var root = new JavaScriptSerializer().DeserializeObject(Send(Url(baseUrl, "/api/tags"), null)) as Dictionary<string, object>;
            return ((root?["models"] as object[]) ?? new object[0]).OfType<Dictionary<string, object>>().Select(m => m["name"] as string).Where(n => n != null).OrderBy(n => n).ToList();
        }

        public static string Prompt(List<ClassInfo> classes, int count)
        {
            var sb = new StringBuilder();
            sb.Append("You name characters in a grim, darkly funny sci-fi dungeon crawler set on a corporate space station. ");
            sb.Append("Write titles for a character who has all of these classes:\n");
            foreach (ClassInfo c in classes)
            {
                sb.Append("- ").Append(c.name.Replace("!", ""));
                string d = Regex.Replace(c.description ?? "", @"\[[A-Z]+\]|\s+", " ").Trim();
                if (d.Length > 600) d = d.Substring(0, 600) + "...";
                if (d != "") sb.Append(": ").Append(d);
                if (c.skills.Count > 0) sb.Append(" Skills: ").Append(string.Join(", ", c.skills)).Append('.');
                if (c.themes.Count > 0) sb.Append(" [plays as: ").Append(string.Join(", ", c.themes)).Append(']');
                sb.Append('\n');
            }
            sb.Append("Each title should name what this whole combination does in a fight, as one identity, the way a crew would nickname a veteran; let every class colour it. ");
            sb.Append("Each title 2 to 5 words, Title Case. Do not include a personal name, do not start with \"The\", and do not just list the class names. ");
            sb.Append($"Write {count} different titles, one per line, and nothing else: no numbering, no quotes, no explanation.");
            return sb.ToString();
        }

        /// <summary>
        /// The wordsmith prompt: each class's identity words, and titles that fuse them into one name ("Gloom-Spored
        /// Runesmith") naming as many of the classes as fit in <paramref name="maxLength"/> letters. Titles in
        /// <paramref name="avoid"/> (earlier rolls) are not to be repeated.
        /// </summary>
        public static string SmithPrompt(List<ClassInfo> classes, int count, int maxLength, IEnumerable<string> avoid, int min = 0, string example = null, IEnumerable<string> missing = null)
        {
            var sb = new StringBuilder();
            sb.Append("You name characters in a grim, darkly funny sci-fi dungeon crawler set on a corporate space station. ");
            sb.Append("A character has all of these classes. Each comes with its identity words: traits (adjectives), stems (they join onto other words) and title words (a title can end on one; a leading - joins it onto a stem, so -smith makes Runesmith).\n");
            foreach (ClassInfo c in classes)
            {
                sb.Append("- ").Append(c.name.Replace("!", ""));
                string d = Regex.Replace(c.description ?? "", @"\[[A-Z]+\]|\s+", " ").Trim();
                int dot = d.IndexOf(". ", StringComparison.Ordinal);
                if (dot > 0 && dot < 220) d = d.Substring(0, dot + 1); else if (d.Length > 220) d = d.Substring(0, 220) + "...";
                if (d != "") sb.Append(" (").Append(d).Append(')');
                sb.Append(": traits ").Append(string.Join(", ", c.traits.DefaultIfEmpty("-")));
                sb.Append("; stems ").Append(string.Join(", ", c.roots.DefaultIfEmpty("-")));
                sb.Append("; title words ").Append(string.Join(", ", c.heads.DefaultIfEmpty("-"))).Append('\n');
            }
            sb.Append($"Write titles of 2 to 4 words and at most {maxLength} letters that fuse these identity words, or close forms of them, into one name. ");
            sb.Append("Join a stem onto a trait or a title word with a hyphen or into one word (Psi-Spored, Hive-Guard, Runesmith). ");
            sb.Append("Every word should point at one of the classes, and a broad word that fits two or more of them at once is best, so the title names as many classes as it can. ");
            if (min > 0) sb.Append($"Each title must name at least {min} of the {classes.Count} classes through their words; 3 or 4 words, each carrying one or two classes, get there. ");
            if (!string.IsNullOrWhiteSpace(example)) sb.Append($"For example, \"{example}\" is built this way; write new ones, not that one. ");
            var left = (missing ?? Enumerable.Empty<string>()).ToList();
            if (left.Count > 0) sb.Append("Your last titles left out ").Append(string.Join(", ", left)).Append(": use their words this time. ");
            sb.Append("Only join words whose meanings belong together. Title Case, no personal name, do not start with \"The\". ");
            var old = (avoid ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToList();
            if (old.Count > 0) sb.Append("Do not repeat any of these: ").Append(string.Join("; ", old)).Append(". ");
            sb.Append($"Write {count} different titles, one per line, and nothing else: no numbering, no quotes, no explanation.");
            return sb.ToString();
        }

        public static List<string> Titles(string baseUrl, string model, List<ClassInfo> classes, int count = 8) => Generate(baseUrl, model, Prompt(classes, count), count, null);

        /// <summary>Titles from the wordsmith prompt; <paramref name="seed"/> differs per roll so a reroll writes new ones.</summary>
        public static List<string> SmithTitles(string baseUrl, string model, List<ClassInfo> classes, int maxLength, IEnumerable<string> avoid, int seed,
            int min = 0, string example = null, IEnumerable<string> missing = null, int count = 8) =>
            Generate(baseUrl, model, SmithPrompt(classes, count, maxLength, avoid, min, example, missing), count, seed);

        private static List<string> Generate(string baseUrl, string model, string prompt, int count, int? seed)
        {
            var json = new JavaScriptSerializer();
            var options = new Dictionary<string, object> { ["temperature"] = 0.95, ["num_ctx"] = 4096 };
            if (seed.HasValue) options["seed"] = seed.Value;
            string body = json.Serialize(new Dictionary<string, object>
            {
                ["model"] = model, ["prompt"] = prompt, ["stream"] = false, ["options"] = options,
            });
            var root = json.DeserializeObject(Send(Url(baseUrl, "/api/generate"), body)) as Dictionary<string, object>;
            string answer = Regex.Replace(root?["response"] as string ?? "", "<think>.*?</think>", "", RegexOptions.Singleline);
            return answer.Split('\n').Select(l => Cap(Tidy(l))).Where(x => x.Length >= 3 && x.Split(' ').Length <= 7).Distinct(StringComparer.OrdinalIgnoreCase).Take(count).ToList();
        }

        /// <summary>Title case, small words kept small past the first (the loader's ClassTitles.Cap).</summary>
        public static string Cap(string s)
        {
            var small = new HashSet<string> { "the", "of", "a", "an", "and", "in", "on" };
            var words = (s ?? "").Split(' ').Where(w => w != "").ToArray();
            for (int i = 0; i < words.Length; i++)
                words[i] = i > 0 && small.Contains(words[i].ToLowerInvariant()) ? words[i].ToLowerInvariant() : char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }

        /// <summary>One line of an answer made into a title (the loader's ClassTitles.Tidy).</summary>
        public static string Tidy(string line)
        {
            string s = (line ?? "").Trim();
            s = Regex.Replace(s, @"^\s*(\d+\s*[.):-]|[-*•])\s*", "");
            s = Regex.Replace(s, @"^(title|answer)\s*:\s*", "", RegexOptions.IgnoreCase);
            s = new string(s.Where(ch => char.IsLetter(ch) || ch == ' ' || ch == '-' || ch == '\'').ToArray());
            s = Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '\'');
            if (s.StartsWith("The ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4);
            if (s.Length > 48) s = s.Substring(0, 48).Trim();
            return s;
        }
    }
}
