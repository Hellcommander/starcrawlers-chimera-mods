using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Shared with the desktop mod manager (_modding/ModManager links this file), so both find, own and order mods by the
// same rules. Nothing here may use BepInEx or Unity.
namespace SCClassLoader
{
    /// <summary>One mod: a folder under Mods/ with an optional mod.json manifest.</summary>
    public class ModPack
    {
        public string id;
        public string name;
        public string author;
        public string version;
        public string license;
        public string description;
        public bool enabled = true;
        /// <summary>Mods that must be installed; this mod is skipped (with an error) if one is missing.</summary>
        public List<string> requires = new List<string>();
        /// <summary>Mods to load before this one when they are present (their entries can then be patched).</summary>
        public List<string> loadAfter = new List<string>();
        /// <summary>The plugin API level this mod's code needs; a loader with a lower <see cref="ModPacks.ApiLevel"/> skips the mod.</summary>
        public int apiLevel;
        /// <summary>False: this mod's sound archive is read on demand, one sound at a time, instead of preloaded at start (for very large sound sets).</summary>
        public bool soundsPreload = true;

        [JsonIgnore] public string folder;
        [JsonIgnore] public bool hasManifest;
        /// <summary>The mod.json's own "enabled" (true without one). <see cref="enabled"/> is this or the player's choice.</summary>
        [JsonIgnore] public bool manifestEnabled = true;
        /// <summary>Why the mod.json could not be read; such a mod is not loaded until it is fixed.</summary>
        [JsonIgnore] public string manifestError;
        /// <summary>Data objects (items, tweaks, ...) from this mod's files, in file order.</summary>
        [JsonIgnore] public readonly List<(string file, JObject obj)> data = new List<(string, JObject)>();
        /// <summary>Library objects from this mod's files, in file order.</summary>
        [JsonIgnore] public readonly List<(string file, JObject obj)> libraryObjs = new List<(string, JObject)>();
        /// <summary>Class objects (new classes and patches) from this mod's files, in file order.</summary>
        [JsonIgnore] public readonly List<(string file, JObject obj)> classObjs = new List<(string, JObject)>();
        /// <summary>Classes (id, name) in this mod's files, read whether or not it loads.</summary>
        [JsonIgnore] public readonly List<(string id, string name)> classes = new List<(string, string)>();
        /// <summary>Code plugins: the .dll files directly in this mod's plugins/ folder, read whether or not it loads.</summary>
        [JsonIgnore] public readonly List<string> pluginFiles = new List<string>();
        /// <summary>The .lua files in the mod's scripts/ folder, in name order. Sandboxed, so they need no OK (see Scripting.cs).</summary>
        [JsonIgnore] public readonly List<string> scriptFiles = new List<string>();
        /// <summary>The <see cref="ModPacks.CodeHash"/> of <see cref="pluginFiles"/>, once asked for.</summary>
        [JsonIgnore] internal string codeHash;

        /// <summary>What can be turned off one by one, by kind (classes, items, ...); read whether or not the mod loads.</summary>
        [JsonIgnore] internal readonly List<ContentGroup> content = new List<ContentGroup>();

        [JsonIgnore] public int libraries => libraryObjs.Count;
        [JsonIgnore] public string Title => string.IsNullOrEmpty(name) ? id : name;
    }

    /// <summary>One kind of thing in a mod (its classes, its items, ...) that can be turned off as a whole.</summary>
    internal class ContentGroup
    {
        /// <summary>"ModId/kind", e.g. "ArendethClasses/classes" or "SideAreas/sideAreas/kinds".</summary>
        internal string key;
        internal string label;
        internal readonly List<ContentEntry> entries = new List<ContentEntry>();
    }

    /// <summary>One class, item, synergy, tweak, ... that can be turned off on its own.</summary>
    internal class ContentEntry
    {
        /// <summary>"ModId/kind/id", e.g. "ArendethClasses/classes/MOD_Runeforger".</summary>
        internal string key;
        internal string id, name;
        /// <summary>Where it sits in the mod's files (a class or list entry, or a tweak's property), so the loader can take it out.</summary>
        internal JToken token;
        internal bool isClass;
    }

    /// <summary>Which mods load and in what order, and why each of the others doesn't.</summary>
    internal class ModPlan
    {
        internal readonly List<ModPack> Ordered = new List<ModPack>();
        internal readonly Dictionary<ModPack, string> Skipped = new Dictionary<ModPack, string>();
        internal readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// Finds mods, decides which mod owns each json file, and orders mods by requires/loadAfter and the player's chosen
    /// order. A folder with a mod.json is a mod (with everything under it); other top-level folders under Mods/ are
    /// implicit mods named after the folder, so existing class folders keep working unchanged.
    /// </summary>
    internal static partial class ModPacks
    {
        /// <summary>Top-level keys that make a json object mod data rather than a class.</summary>
        internal static readonly string[] DataKeys = { "items", "tweaks", "enemies", "creatures", "recipes", "factions", "synergies", "wear", "sideAreas", "enemyPatches", "mobEffects", "particles", "pluginData", "soundFallbacks" };

        /// <summary>The <see cref="ModPlan.Skipped"/> reason for a mod that is simply switched off.</summary>
        internal const string Off = "turned off";
        /// <summary>The <see cref="ModPlan.Skipped"/> reason for a mod whose plugin code the player hasn't allowed yet.</summary>
        internal const string NeedsOk = "waiting for your OK to run its code";

        /// <summary>The level of the plugin API (the Breachwork namespace); goes up when plugins can do more ("apiLevel" in mod.json).</summary>
        public const int ApiLevel = 4;

        private static readonly Dictionary<string, ModPack> byFolder = new Dictionary<string, ModPack>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every mod found, loaded or not.</summary>
        internal static IEnumerable<ModPack> All => byFolder.Values;

        internal static bool IsManifest(string file) => Path.GetFileName(file).Equals("mod.json", StringComparison.OrdinalIgnoreCase);

        internal static bool IsData(JObject o) => o["tiers"] == null && DataKeys.Any(k => o[k] != null);

        internal static ModPack Find(string id) => byFolder.Values.FirstOrDefault(p => string.Equals(p.id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Folders inside a mod that hold code, not mod files: a plugin's own files, a plugin project's build output, scripts.</summary>
        private static readonly string[] CodeFolders = { "plugins", "bin", "obj", "scripts", "tools" };

        private static bool InCodeFolder(string file, string modsDir) =>
            file.Substring(modsDir.Length).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(d => CodeFolders.Contains(d, StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// Finds every mod and hands each json/xml object under <paramref name="modsDir"/> to its mod as data, a library or a
        /// class. Returns the files that could not be read.
        /// </summary>
        internal static List<(string file, string error)> Scan(string modsDir, Func<string, JToken> readXml)
        {
            Discover(modsDir);
            var errors = new List<(string, string)>();
            // *.meta.json are audio/art pipeline sidecars, not mod files.
            var files = Directory.GetFiles(modsDir, "*.json", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
                .Concat(Directory.GetFiles(modsDir, "*.xml", SearchOption.AllDirectories))
                .Where(f => !InCodeFolder(f, modsDir))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                if (IsManifest(file)) continue;
                try
                {
                    JToken root = file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? readXml(file) : JToken.Parse(File.ReadAllText(file));
                    var objs = root is JArray arr ? arr.OfType<JObject>().ToList() : new List<JObject> { (JObject)root };
                    ModPack owner = Owner(file, modsDir);
                    foreach (JObject o in objs)
                    {
                        if (IsData(o)) owner.data.Add((file, o));
                        else if (o["library"] != null) owner.libraryObjs.Add((file, o));
                        else
                        {
                            owner.classObjs.Add((file, o));
                            if (o["id"] != null) owner.classes.Add(((string)o["id"], (string)o["name"] ?? (string)o["id"]));
                        }
                    }
                }
                catch (Exception e) { errors.Add((file, e.Message)); }
            }
            foreach (string dll in Directory.GetFiles(modsDir, "*.dll", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                if (string.Equals(Path.GetFileName(Path.GetDirectoryName(dll)), "plugins", StringComparison.OrdinalIgnoreCase))
                    Owner(dll, modsDir).pluginFiles.Add(dll);
            foreach (string lua in Directory.GetFiles(modsDir, "*.lua", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                if (string.Equals(Path.GetFileName(Path.GetDirectoryName(lua)), "scripts", StringComparison.OrdinalIgnoreCase))
                    Owner(lua, modsDir).scriptFiles.Add(lua);
            foreach (ModPack p in byFolder.Values) Catalog(p);
            return errors;
        }

        /// <summary>
        /// Data lists whose entries can be turned off one by one. Particles, wear models, effects, rosters and vaults are
        /// left out: they are parts other content points at, not things a player picks.
        /// </summary>
        private static readonly (string path, string label)[] Groups =
        {
            ("items", "Items"), ("enemies", "Enemies"), ("creatures", "Creatures"), ("enemyPatches", "Enemy changes"), ("recipes", "Recipes"),
            ("synergies", "Synergies"), ("factions", "Factions"), ("sideAreas/kinds", "Side area kinds"),
            ("sideAreas/places", "Side area places"), ("sideAreas/anomalies", "Anomalies"), ("sideAreas/affixes", "Affixes"),
        };

        /// <summary>Lists what in <paramref name="p"/> can be turned off on its own, by kind: classes first, tweaks last.</summary>
        private static void Catalog(ModPack p)
        {
            p.content.Clear();
            ContentGroup Group(string path, string label)
            {
                string key = p.id + "/" + path;
                ContentGroup g = p.content.FirstOrDefault(x => x.key == key);
                if (g == null) p.content.Add(g = new ContentGroup { key = key, label = label });
                return g;
            }
            void Add(ContentGroup g, string id, string name, JToken token, bool isClass = false) =>
                g.entries.Add(new ContentEntry { key = g.key + "/" + id, id = id, name = name, token = token, isClass = isClass });

            foreach (var (file, obj) in p.classObjs)
            {
                string id = (string)obj["id"];
                if (string.IsNullOrEmpty(id)) continue;
                ModPack target = PatchTarget(p, id);
                string name = (string)obj["name"] ?? target?.classes.FirstOrDefault(c => c.id == id).name ?? id;
                Add(Group("classes", "Classes"), id, target != null ? $"{name} (changes to {target.Title}'s class)" : name, obj, true);
            }
            foreach (var (path, label) in Groups)
                foreach (var (file, obj) in p.data)
                {
                    JToken list = obj;
                    foreach (string k in path.Split('/')) list = list is JObject o ? o[k] : null;
                    foreach (JObject e in (list as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                    {
                        string id = (string)e["id"];
                        if (!string.IsNullOrEmpty(id)) Add(Group(path, label), id, (string)e["name"] ?? id, e);
                    }
                }
            foreach (var (file, obj) in p.data)
                if (obj["tweaks"] is JObject t)
                    foreach (JProperty prop in t.Properties()) Add(Group("tweaks", "Tweaks"), prop.Name, $"{prop.Name} = {prop.Value}", prop);
        }

        /// <summary>Whether an entry will load at the next start: its mod, its kind and itself all on.</summary>
        internal static bool IsOn(ModPack p, ContentGroup g, ContentEntry e) => ModChoices.IsOn(p) && ModChoices.PartOn(g.key) && ModChoices.PartOn(e.key);

        private static void Discover(string modsDir)
        {
            byFolder.Clear();
            ModChoices.Load();
            foreach (string mf in Directory.GetFiles(modsDir, "mod.json", SearchOption.AllDirectories))
            {
                ModPack p;
                try { p = JsonConvert.DeserializeObject<ModPack>(File.ReadAllText(mf)) ?? new ModPack(); }
                catch (Exception e) { p = new ModPack { manifestError = e.Message }; }
                p.folder = Path.GetDirectoryName(mf);
                p.hasManifest = true;
                if (string.IsNullOrEmpty(p.id)) p.id = Path.GetFileName(p.folder);
                p.id = Sanitize(p.id);
                Choose(p);
                byFolder[p.folder] = p;
            }
        }

        /// <summary>The player's choice wins over the mod's own "enabled".</summary>
        private static void Choose(ModPack p)
        {
            p.manifestEnabled = p.enabled;
            if (ModChoices.TryGet(p.id, out bool on)) p.enabled = on;
        }

        /// <summary>A fingerprint of a mod's plugin code, so an allowed mod is asked about again when its code changes.</summary>
        internal static string CodeHash(ModPack p)
        {
            if (p.codeHash != null) return p.codeHash;
            using (var sha = SHA256.Create())
            {
                foreach (string f in p.pluginFiles)
                {
                    byte[] name = System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(f).ToLowerInvariant());
                    sha.TransformBlock(name, 0, name.Length, null, 0);
                    byte[] body = File.ReadAllBytes(f);
                    sha.TransformBlock(body, 0, body.Length, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return p.codeHash = BitConverter.ToString(sha.Hash).Replace("-", "");
            }
        }

        /// <summary>The mod that owns a file: the nearest folder with a mod.json, else the top-level folder under Mods/.</summary>
        internal static ModPack Owner(string file, string modsDir)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(file));
            string root = Path.GetFullPath(modsDir).TrimEnd(Path.DirectorySeparatorChar);
            for (string d = dir; d != null && d.Length > root.Length; d = Path.GetDirectoryName(d))
                if (byFolder.TryGetValue(d, out ModPack p)) return p;

            string rel = dir.Length > root.Length ? dir.Substring(root.Length + 1) : "";
            string top = rel.Split(Path.DirectorySeparatorChar)[0];
            string folder = string.IsNullOrEmpty(top) ? root : Path.Combine(root, top);
            if (!byFolder.TryGetValue(folder, out ModPack implicitPack))
            {
                implicitPack = new ModPack { id = Sanitize(string.IsNullOrEmpty(top) ? "Mods" : top), name = top, folder = folder };
                Choose(implicitPack);
                byFolder[folder] = implicitPack;
            }
            return implicitPack;
        }

        /// <summary>The mods this one must load after: its requires and loadAfter.</summary>
        internal static IEnumerable<string> After(ModPack p) => (p.requires ?? new List<string>()).Concat(p.loadAfter ?? new List<string>());

        /// <summary>
        /// Orders the mods <paramref name="on"/> picks so requires/loadAfter come first and, within that, as the player's
        /// <see cref="ModChoices.Order"/> lists them (unlisted mods by folder name), and gives the reason for every mod left
        /// out: off, an unreadable mod.json, a taken id, or a requirement that is missing or itself left out.
        /// </summary>
        internal static ModPlan Resolve(Func<ModPack, bool> on)
        {
            var plan = new ModPlan();
            var all = new Dictionary<string, ModPack>(StringComparer.OrdinalIgnoreCase);
            foreach (ModPack p in byFolder.Values.OrderBy(p => p.folder, StringComparer.OrdinalIgnoreCase))
            {
                if (p.manifestError != null) plan.Skipped[p] = "its mod.json could not be read: " + p.manifestError;
                else if (!on(p)) plan.Skipped[p] = Off;
                else if (all.TryGetValue(p.id, out ModPack first)) plan.Skipped[p] = $"the id '{p.id}' is already used by {first.folder}";
                else if (p.apiLevel > ApiLevel) plan.Skipped[p] = $"it needs a newer Breachwork (plugin API level {p.apiLevel}, this one has {ApiLevel})";
                else if (p.pluginFiles.Count > 0 && !ModChoices.CodeOk(p)) plan.Skipped[p] = NeedsOk;
                else all[p.id] = p;
            }

            // A mod dropped for a missing requirement can take its own dependants with it, so repeat until nothing changes.
            string WhyNot(string id)
            {
                ModPack p = Find(id);
                return p == null ? "not installed" : plan.Skipped.TryGetValue(p, out string why) && (why == Off || why == NeedsOk) ? why : "not loaded";
            }
            for (bool dropped = true; dropped;)
            {
                dropped = false;
                foreach (ModPack p in all.Values.ToList())
                {
                    var missing = (p.requires ?? new List<string>()).Where(r => !all.ContainsKey(r)).ToList();
                    if (missing.Count == 0) continue;
                    plan.Skipped[p] = "needs " + string.Join(", ", missing.Select(r => $"{Find(r)?.Title ?? r} ({WhyNot(r)})"));
                    all.Remove(p.id);
                    dropped = true;
                }
            }

            // Depth first in the chosen order: a mod pulls what it loads after ahead of itself, so a chosen order that
            // already respects requires/loadAfter comes out exactly as chosen.
            int Rank(ModPack p) { int i = ModChoices.Order.FindIndex(x => string.Equals(x, p.id, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; }
            IEnumerable<ModPack> Sorted(IEnumerable<ModPack> ps) => ps.OrderBy(Rank).ThenBy(p => p.folder, StringComparer.OrdinalIgnoreCase);
            var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 1 = visiting, 2 = done
            void Visit(ModPack p)
            {
                if (state.TryGetValue(p.id, out int s))
                {
                    if (s == 1) plan.Warnings.Add($"Mod '{p.id}' is part of a requires/loadAfter loop; loading it in folder order.");
                    return;
                }
                state[p.id] = 1;
                foreach (ModPack d in Sorted(After(p).Where(all.ContainsKey).Select(id => all[id]).Distinct())) Visit(d);
                state[p.id] = 2;
                plan.Ordered.Add(p);
            }
            foreach (ModPack p in Sorted(all.Values)) Visit(p);
            return plan;
        }

        /// <summary>
        /// The mod whose class a class file in <paramref name="p"/> patches: one it requires or loads after that has a class
        /// with the same id. Null when the file is a class of its own.
        /// </summary>
        internal static ModPack PatchTarget(ModPack p, string classId) =>
            After(p).Select(id => byFolder.Values.FirstOrDefault(x => x != p && string.Equals(x.id, id, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(x => x != null && x.classes.Any(c => string.Equals(c.id, classId, StringComparison.OrdinalIgnoreCase)));

        private static readonly (string key, string one, string many)[] Kinds =
        {
            ("items", "item", "items"), ("enemies", "enemy", "enemies"), ("creatures", "creature", "creatures"), ("enemyPatches", "enemy patch", "enemy patches"),
            ("mobEffects", "enemy effect", "enemy effects"), ("recipes", "recipe", "recipes"), ("synergies", "synergy", "synergies"),
            ("factions", "faction", "factions"), ("sideAreas", "side area set", "side area sets"), ("wear", "wear model", "wear models"),
            ("particles", "particle effect", "particle effects"), ("tweaks", "tweak set", "tweak sets"), ("pluginData", "plugin data set", "plugin data sets"),
        };

        internal static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

        /// <summary>What a mod holds, e.g. "14 classes, 13 items, 12 recipes".</summary>
        internal static string Contents(ModPack p)
        {
            var parts = new List<string>();
            if (p.pluginFiles.Count > 0) parts.Add(Count(p.pluginFiles.Count, "code plugin", "code plugins"));
            if (p.scriptFiles.Count > 0) parts.Add(Count(p.scriptFiles.Count, "script", "scripts"));
            int patches = p.classes.Count(c => PatchTarget(p, c.id) != null);
            if (p.classes.Count > patches) parts.Add(Count(p.classes.Count - patches, "class", "classes"));
            if (patches > 0) parts.Add($"patches to {Count(patches, "class", "classes")}");
            if (p.libraries > 0) parts.Add(Count(p.libraries, "library", "libraries"));
            foreach (var (key, one, many) in Kinds)
            {
                int n = p.data.Sum(d => d.obj[key] is JArray a ? a.Count : d.obj[key] != null ? 1 : 0);
                if (n > 0) parts.Add(Count(n, one, many));
            }
            return parts.Count == 0 ? "no class or data files" : string.Join(", ", parts);
        }

        /// <summary>A log line is about a mod when it names a file in its folder, or the mod by id ('Id', (Id) or Id:Thing).</summary>
        internal static bool Mentions(ModPack p, string text, string modsDir)
        {
            string root = Path.GetFullPath(modsDir).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(p.folder, root, StringComparison.OrdinalIgnoreCase))
            {
                string f = p.folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (text.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf(f.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return text.Contains($"'{p.id}'") || text.Contains($"({p.id})") || text.Contains(p.id + ":");
        }

        /// <summary>"Id" -> "Pack:Id"; "Other:Id" stays as written.</summary>
        internal static string Qualify(ModPack p, string id) =>
            string.IsNullOrEmpty(id) ? id : id.Contains(":") ? id : p.id + ":" + id;

        internal static string Sanitize(string s) =>
            new string((s ?? "").Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
    }

    /// <summary>
    /// The player's mod choices from the mod manager and the title screen's Mods page, kept in BepInEx/config so they
    /// outlive mod updates: mods turned on or off (winning over each mod.json's "enabled"; a choice that matches it is
    /// dropped), a load order and which mods may run code. Saved at once on every change.
    /// </summary>
    internal static class ModChoices
    {
        internal const string FileName = "arendeth.starcrawlers.classloader.mods.json";
        /// <summary>BepInEx/config; set by whoever uses this (the loader, the manager).</summary>
        internal static string ConfigDir;
        internal static string FilePath => Path.Combine(ConfigDir, FileName);
        /// <summary>Why the file could not be read on the last <see cref="Load"/>, else null.</summary>
        internal static string LoadError;

        private static readonly Dictionary<string, bool> choices = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Kinds and entries turned off inside mods ("Mod/kind" or "Mod/kind/id"); everything else is on.</summary>
        private static readonly HashSet<string> off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal static IReadOnlyCollection<string> OffParts => off;
        /// <summary>Mod ids in the chosen load order (first loads first); empty = automatic (folder names).</summary>
        internal static readonly List<string> Order = new List<string>();
        /// <summary>Mod id -> the <see cref="ModPacks.CodeHash"/> the player allowed, or <see cref="AnyCode"/>.</summary>
        private static readonly Dictionary<string, string> code = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Allowed whatever its code is, even after it changes (for a plugin's own author, who rebuilds it all day).</summary>
        internal const string AnyCode = "*";

        internal static void Load()
        {
            choices.Clear();
            Order.Clear();
            off.Clear();
            code.Clear();
            LoadError = null;
            if (!File.Exists(FilePath)) return;
            try
            {
                JObject o = JObject.Parse(File.ReadAllText(FilePath));
                foreach (JProperty p in (o["mods"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    if (p.Value.Type == JTokenType.Boolean) choices[p.Name] = (bool)p.Value;
                foreach (JToken t in (o["off"] as JArray) ?? new JArray())
                    if (t.Type == JTokenType.String) off.Add((string)t);
                foreach (JToken t in (o["order"] as JArray) ?? new JArray())
                    if (t.Type == JTokenType.String && !Order.Contains((string)t, StringComparer.OrdinalIgnoreCase)) Order.Add((string)t);
                foreach (JProperty p in (o["code"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    if (p.Value.Type == JTokenType.String) code[p.Name] = (string)p.Value;
            }
            catch (Exception e) { LoadError = e.Message; }
        }

        internal static bool TryGet(string id, out bool on) => choices.TryGetValue(id, out on);

        /// <summary>Whether a kind ("Mod/kind") or entry ("Mod/kind/id") is on in itself (its parents aside).</summary>
        internal static bool PartOn(string key) => !off.Contains(key);

        /// <summary>Turns a kind or entry on or off and saves the file at once.</summary>
        internal static void SetPart(string key, bool on)
        {
            if (on) off.Remove(key);
            else off.Add(key);
            Save();
        }

        /// <summary>Whether the mod will be on at the next start.</summary>
        internal static bool IsOn(ModPack p) => choices.TryGetValue(p.id, out bool on) ? on : p.manifestEnabled;

        /// <summary>Records a choice and saves the file at once.</summary>
        internal static void Set(ModPack p, bool on)
        {
            if (on == p.manifestEnabled) choices.Remove(p.id);
            else choices[p.id] = on;
            Save();
        }

        /// <summary>Whether the player has allowed this mod's plugin code as it is now.</summary>
        internal static bool CodeOk(ModPack p) => code.TryGetValue(p.id, out string h) && (h == AnyCode || h == ModPacks.CodeHash(p));

        /// <summary>Whether the mod is allowed whatever its code becomes.</summary>
        internal static bool AnyCodeOk(ModPack p) => code.TryGetValue(p.id, out string h) && h == AnyCode;

        /// <summary>Allows this mod's code as it is now, or whatever it becomes, and saves the file at once.</summary>
        internal static void AllowCode(ModPack p, bool always = false)
        {
            code[p.id] = always ? AnyCode : ModPacks.CodeHash(p);
            Save();
        }

        /// <summary>Takes back <see cref="AllowCode"/> and saves the file at once.</summary>
        internal static void ForbidCode(ModPack p)
        {
            code.Remove(p.id);
            Save();
        }

        /// <summary>Sets the load order (mod ids, first loads first; empty = automatic) and saves the file at once.</summary>
        internal static void SetOrder(IEnumerable<string> ids)
        {
            Order.Clear();
            Order.AddRange(ids);
            Save();
        }

        private static void Save()
        {
            if (choices.Count == 0 && Order.Count == 0 && off.Count == 0 && code.Count == 0) { if (File.Exists(FilePath)) File.Delete(FilePath); return; }
            var o = new JObject
            {
                ["about"] = "Mod choices from the Breachwork Mod Manager, the title screen's Mods page and Mod Settings > Mod Content: mods turned on or off (each wins over that mod's own mod.json), classes and other content turned off inside mods, the load order, and which mods may run code. Delete this file to go back to the mods' own settings and the automatic order.",
                ["mods"] = new JObject(choices.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => new JProperty(kv.Key, kv.Value))),
                ["off"] = new JArray(off.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)),
                ["order"] = new JArray(Order),
                ["code"] = new JObject(code.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => new JProperty(kv.Key, kv.Value))),
            };
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(FilePath, o.ToString());
        }
    }
}
