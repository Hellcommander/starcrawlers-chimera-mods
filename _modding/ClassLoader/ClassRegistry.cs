using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>Loads class json files and turns them into Skill_Tree templates inside Game.instance.skills.</summary>
    public static class ClassRegistry
    {
        /// <summary>Custom localization terms -> text. Served by the TryGetTranslation patch.</summary>
        public static readonly Dictionary<string, string> Strings = new Dictionary<string, string>();

        /// <summary>Custom tree id -> vanilla tree ids it borrows tiers from.</summary>
        public static readonly Dictionary<string, HashSet<string>> Borrowed = new Dictionary<string, HashSet<string>>();

        public static readonly List<Skill_Tree> Templates = new List<Skill_Tree>();

        public static bool IsModTree(string id) => id != null && Borrowed.ContainsKey(id);

        private static List<ClassDef> defs;
        internal static IReadOnlyList<ClassDef> AllDefs => defs ?? new List<ClassDef>();
        private static GameObject holder;

        public static string ModsDir => Path.Combine(Plugin.GameDir, "Mods");

        /// <summary>Shared ability templates from library files, by name.</summary>
        private static readonly Dictionary<string, Newtonsoft.Json.Linq.JObject> libAbilities = new Dictionary<string, Newtonsoft.Json.Linq.JObject>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Library effects, registered as class "LIB" and referenced as "#Name".</summary>
        internal static ClassDef LibraryDef;
        internal const string LibId = "LIB";

        public static void LoadDefinitions()
        {
            defs = new List<ClassDef>();
            libAbilities.Clear();
            When.Named.Clear();
            LibraryDef = new ClassDef { id = LibId, name = "Library", tiers = new List<TierDef>(), effects = new List<EffectSpec>() };
            Directory.CreateDirectory(ModsDir);
            // Pass 1 (shared with the mod manager): every json/xml object to its mod, then the mods in load order.
            foreach (var (file, error) in ModPacks.Scan(ModsDir, XmlMods.Load)) Plugin.Log.LogError($"Could not read {file}: {error}");
            ModPacks.Order();
            ModPacks.PruneOff();
            var libTokens = ModPacks.Ordered.SelectMany(p => p.libraryObjs.Select(t => (t.file, pack: p, t.obj))).ToList();
            var classTokens = ModPacks.Ordered.SelectMany(p => p.classObjs.Select(t => (t.file, pack: p, t.obj))).ToList();
            int Rank(ModPack p) => ModPacks.Ordered.IndexOf(p);

            // Pass 2: libraries of loaded mods, in load order (a later library's template or effect replaces an earlier one).
            foreach (var (file, pack, o) in libTokens.Where(t => ModPacks.IsLoaded(t.pack)).OrderBy(t => Rank(t.pack)))
            {
                LibraryDef.folder = Path.GetDirectoryName(file);
                foreach (var e in (o["effects"] as Newtonsoft.Json.Linq.JArray) ?? new Newtonsoft.Json.Linq.JArray())
                {
                    EffectSpec spec = e.ToObject<EffectSpec>();
                    LibraryDef.effects.RemoveAll(x => x.id == spec.id);
                    LibraryDef.effects.Add(spec);
                }
                foreach (var p in ((o["abilities"] as Newtonsoft.Json.Linq.JObject) ?? new Newtonsoft.Json.Linq.JObject()).Properties())
                    libAbilities[p.Name] = (Newtonsoft.Json.Linq.JObject)p.Value;
                ReadConditions(o, file);
                Plugin.Log.LogInfo($"Library {o["library"]} ({pack.id}): {LibraryDef.effects.Count} effects, {libAbilities.Count} ability templates so far");
            }

            // Pass 3: classes in load order, with "base" templates merged into newAbility. A later mod that uses an
            // existing class id patches that class instead of adding a new one.
            var merged = new Dictionary<string, (Newtonsoft.Json.Linq.JObject obj, string file, ModPack pack)>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var (file, pack, obj) in classTokens.Where(t => ModPacks.IsLoaded(t.pack)).OrderBy(t => Rank(t.pack)))
            {
                try
                {
                    ReadConditions(obj, file);
                    foreach (var tier in (obj["tiers"] as Newtonsoft.Json.Linq.JArray) ?? new Newtonsoft.Json.Linq.JArray())
                        if (tier is Newtonsoft.Json.Linq.JObject t && t["newAbility"] is Newtonsoft.Json.Linq.JObject na) t["newAbility"] = ResolveBase(na, file, 0);
                    foreach (var fx in ((obj["effects"] as Newtonsoft.Json.Linq.JArray) ?? new Newtonsoft.Json.Linq.JArray()).OfType<Newtonsoft.Json.Linq.JObject>())
                        if (fx["kit"] is Newtonsoft.Json.Linq.JArray kit)
                            for (int k = 0; k < kit.Count; k++)
                                if (kit[k] is Newtonsoft.Json.Linq.JObject ka) kit[k] = ResolveBase(ka, file, 0);
                    string id = (string)obj["id"];
                    if (string.IsNullOrEmpty(id) || !merged.TryGetValue(id, out var prev))
                    {
                        // A patch for a class whose mod is off, or that is turned off itself, would otherwise become a
                        // half-made class of its own.
                        ModPack target = string.IsNullOrEmpty(id) ? null : ModPacks.PatchTarget(pack, id);
                        if (target != null && !ModPacks.IsLoaded(target))
                        {
                            Plugin.Log.LogInfo($"Class {id}: patch from {pack.id} skipped, {target.id} is not loaded");
                            continue;
                        }
                        if (target != null && !target.classObjs.Any(t => string.Equals((string)t.obj["id"], id, StringComparison.OrdinalIgnoreCase)))
                        {
                            Plugin.Log.LogInfo($"Class {id}: patch from {pack.id} skipped, the class is turned off");
                            continue;
                        }
                        string key = string.IsNullOrEmpty(id) ? "?" + file : id;
                        merged[key] = (obj, file, pack);
                        order.Add(key);
                        continue;
                    }
                    if (prev.pack == pack) { Plugin.Log.LogError($"{file}: class id '{id}' is already used by {prev.file}"); continue; }
                    // Paths in the patch are relative to the patch's own folder.
                    AbsolutizeAssets(obj, Path.GetDirectoryName(file));
                    var patched = (Newtonsoft.Json.Linq.JObject)prev.obj.DeepClone();
                    // Effects match by id (patch it, or add it); tiers match by position, so {} skips a tier.
                    var patchEffects = obj["effects"] as Newtonsoft.Json.Linq.JArray;
                    obj.Remove("effects");
                    patched.Merge(obj, new Newtonsoft.Json.Linq.JsonMergeSettings { MergeArrayHandling = Newtonsoft.Json.Linq.MergeArrayHandling.Merge });
                    if (patchEffects != null)
                    {
                        var effects = patched["effects"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray();
                        foreach (var pe in patchEffects.OfType<Newtonsoft.Json.Linq.JObject>())
                        {
                            var hit = effects.OfType<Newtonsoft.Json.Linq.JObject>().FirstOrDefault(e => (string)e["id"] == (string)pe["id"]);
                            if (hit != null) hit.Merge(pe, new Newtonsoft.Json.Linq.JsonMergeSettings { MergeArrayHandling = Newtonsoft.Json.Linq.MergeArrayHandling.Replace });
                            else effects.Add(pe);
                            Plugin.Log.LogInfo($"Class {id}: effect {(string)pe["id"]} {(hit != null ? "patched" : "added")} -> {(hit ?? pe).ToString(Formatting.None)}");
                        }
                        patched["effects"] = effects;
                    }
                    merged[id] = (patched, prev.file, prev.pack);
                    Plugin.Log.LogInfo($"Class {id}: patched by {pack.id} ({Path.GetFileName(file)})");
                }
                catch (Exception e) { Plugin.Log.LogError($"Could not read class file {file}: {e.Message}"); }
            }

            foreach (string key in order)
            {
                var (obj, file, pack) = merged[key];
                try
                {
                    ClassDef def = obj.ToObject<ClassDef>();
                    if (def == null || !def.enabled) continue;
                    def.sourceFile = file;
                    def.folder = Path.GetDirectoryName(file);
                    def.pack = pack;
                    if (!string.IsNullOrEmpty(def.id)) folders[def.id] = def.folder;
                    if (!string.IsNullOrEmpty(def.id)) { if (def.ignoresAllies) IgnoresAllies.Add(def.id); else IgnoresAllies.Remove(def.id); }
                    ModelAssets.RegisterAll(obj, def.folder);
                    Alchemy.Register(def);
                    defs.Add(def);
                }
                catch (Exception e) { Plugin.Log.LogError($"Could not read class file {file}: {e.Message}"); }
            }
            Plugin.Log.LogInfo($"Found {defs.Count} custom class definition(s) in {ModsDir}");
        }

        /// <summary>Class id -> qualified mod item ids still to add to its starter gear.</summary>
        private static readonly Dictionary<string, List<string>> pendingStarter = new Dictionary<string, List<string>>();

        /// <summary>Adds mod items to class starter gear. Call after ItemRegistry.Register.</summary>
        internal static void ResolveStarterItems()
        {
            foreach (var kv in pendingStarter)
            {
                Skill_Tree tree = Templates.FirstOrDefault(t => t.internalName == kv.Key);
                if (tree == null) continue;
                var items = (tree.starterGear_items ?? new Item[0]).ToList();
                foreach (string q in kv.Value)
                {
                    Item item = ItemRegistry.Ids.TryGetValue(q, out string internalId) ? ItemDatabase.instance.GetItem(internalId, false, RARITY.None) : null;
                    if (item != null) { items.Add(item); Plugin.Log.LogInfo($"{kv.Key}: starter item {q} -> {item.itemId}"); }
                    else Plugin.Log.LogWarning($"{kv.Key}: starter item '{q}' not found (not a vanilla id or a loaded mod item)");
                }
                tree.starterGear_items = items.ToArray();
            }
        }

        private static readonly string[] AssetKeys = { "icon", "sound", "burstSound" };

        /// <summary>Turns relative png/wav/ogg/mp3 paths in a class patch into absolute ones, so they resolve from the patch's folder.</summary>
        private static void AbsolutizeAssets(Newtonsoft.Json.Linq.JToken token, string folder)
        {
            if (token is Newtonsoft.Json.Linq.JObject o)
                foreach (var p in o.Properties().ToList())
                {
                    if (AssetKeys.Contains(p.Name) && p.Value.Type == Newtonsoft.Json.Linq.JTokenType.String)
                    {
                        string v = (string)p.Value;
                        string ext = Path.GetExtension(v ?? "").ToLowerInvariant();
                        if (!Path.IsPathRooted(v) && (ext == ".png" || ext == ".wav" || ext == ".ogg" || ext == ".mp3"))
                            p.Value = Path.GetFullPath(Path.Combine(folder, v));
                    }
                    else AbsolutizeAssets(p.Value, folder);
                }
            else if (token is Newtonsoft.Json.Linq.JArray a)
                foreach (var c in a) AbsolutizeAssets(c, folder);
        }

        /// <summary>Deep-merges a library template under the ability: the class's own fields win, arrays replace.</summary>
        private static readonly Dictionary<string, string> folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Folder of the class file that defines classId (for its icons and sounds).</summary>
        internal static void SetFolder(string classId, string folder) => folders[classId] = folder;

        /// <summary>Classes whose hostile actions never land on your own side (ClassDef.ignoresAllies).</summary>
        internal static readonly HashSet<string> IgnoresAllies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static string FolderOf(string classId) =>
            classId != null && folders.TryGetValue(classId, out string f) ? f : (classId == LibId ? LibraryDef?.folder : null);

        /// <summary>Named conditions ("conditions": {"Bloodied": {...}}) from a library or class, usable everywhere by name.</summary>
        private static void ReadConditions(Newtonsoft.Json.Linq.JObject o, string file)
        {
            if (!(o["conditions"] is Newtonsoft.Json.Linq.JObject conds)) return;
            foreach (var p in conds.Properties())
            {
                try
                {
                    if (When.Named.ContainsKey(p.Name)) Plugin.Log.LogInfo($"{file}: condition '{p.Name}' replaces an earlier one");
                    When.Named[p.Name] = p.Value.ToObject<WhenSpec>();
                }
                catch (Exception e) { Plugin.Log.LogError($"{file}: condition '{p.Name}' could not be read: {e.Message}"); }
            }
        }

        internal static Newtonsoft.Json.Linq.JObject ResolveBase(Newtonsoft.Json.Linq.JObject ability, string file, int depth)
        {
            string baseName = (string)ability["base"];
            if (string.IsNullOrEmpty(baseName)) return ability;
            if (depth > 5 || !libAbilities.TryGetValue(baseName, out var template))
            {
                Plugin.Log.LogError($"{file}: unknown ability base '{baseName}'");
                return ability;
            }
            var merged = (Newtonsoft.Json.Linq.JObject)ResolveBase((Newtonsoft.Json.Linq.JObject)template.DeepClone(), file, depth + 1).DeepClone();
            merged.Merge(ability, new Newtonsoft.Json.Linq.JsonMergeSettings { MergeArrayHandling = Newtonsoft.Json.Linq.MergeArrayHandling.Replace });
            merged.Remove("base");
            return merged;
        }

        /// <summary>Builds every template and appends it to the game's master skill list. Safe to call per Game instance.</summary>
        public static void Register(Game game)
        {
            if (defs == null) LoadDefinitions();

            if (holder != null) Object.Destroy(holder);
            holder = new GameObject("SCClassLoader_Templates");
            holder.SetActive(false); // keeps Awake from running on templates; clones made by the game run it normally
            Object.DontDestroyOnLoad(holder);
            Templates.Clear();
            pendingStarter.Clear();
            Borrowed.Clear();
            Strings.Clear();
            ModAbility.Specs.Clear();
            ModEffect.Specs.Clear();
            Forms.Clear();

            game.skills.RemoveAll(s => s == null);
            var vanilla = game.skills.Where(s => s.isLive).ToDictionary(s => s.internalName, s => s);
            if (LibraryDef != null && LibraryDef.effects.Count > 0)
            {
                Sprite libIcon = vanilla.Values.First().icon;
                RegisterEffects(LibraryDef, libIcon);
                FillEffectIcons(LibraryDef, libIcon);
            }

            foreach (ClassDef def in defs)
            {
                try
                {
                    Skill_Tree tree = Build(def, vanilla);
                    if (tree == null) continue;
                    game.skills.Add(tree);
                    Templates.Add(tree);
                    Plugin.Log.LogInfo($"Registered class '{def.name}' ({def.id}) with {tree.skillTiers.Count} tiers");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Failed to build class '{def.id}' from {def.sourceFile}: {e}");
                }
            }
        }

        private static Skill_Tree Build(ClassDef def, Dictionary<string, Skill_Tree> vanilla)
        {
            if (string.IsNullOrEmpty(def.id)) { Plugin.Log.LogError($"{def.sourceFile}: missing \"id\""); return null; }
            if (vanilla.ContainsKey(def.id) || Borrowed.ContainsKey(def.id)) { Plugin.Log.LogError($"{def.sourceFile}: id '{def.id}' is already used"); return null; }
            if (def.tiers == null || def.tiers.Count == 0) { Plugin.Log.LogError($"{def.id}: needs at least one tier"); return null; }
            int maxTiers = Plugin.MaxTiers;
            if (def.tiers.Count > maxTiers)
            {
                Plugin.Log.LogWarning($"{def.id}: {def.tiers.Count} tiers given but the skills screen only has room for {maxTiers}; extra tiers dropped");
                def.tiers = def.tiers.Take(maxTiers).ToList();
            }

            var go = new GameObject("SKILLTREE_MOD_" + def.id);
            go.transform.SetParent(holder.transform, false);
            Skill_Tree tree = go.AddComponent<Skill_Tree>();
            tree.internalName = def.id;
            tree.isLive = true;
            tree.rank = -1;
            tree.skillClass = ParseEnum(def.archetype, SKILL_CLASS.Muscle, def.id);
            tree.displayName = Term(def.id, "Name", def.name ?? def.id);
            tree.description = Term(def.id, "Desc", def.description ?? "");
            tree.skillTiers = new List<Skill_Tier>();

            // Icon (needed first: it is the fallback icon for new abilities and effects)
            Sprite icon = LoadSprite(def.folder, def.icon);
            if (icon == null && def.iconFrom != null) icon = VanillaIcon(def.iconFrom, vanilla, def.id);
            if (icon == null)
            {
                TierDef borrowed = def.tiers.FirstOrDefault(t => t.newAbility == null && t.from != null && vanilla.ContainsKey(t.from));
                icon = borrowed != null ? vanilla[borrowed.from].icon : vanilla.Values.First().icon;
            }
            tree.icon = icon;

            RegisterEffects(def, icon);

            var sources = new HashSet<string>();
            for (int i = 0; i < def.tiers.Count; i++)
            {
                TierDef td = def.tiers[i];
                if (td.newAbility != null)
                {
                    Skill_Tier made = NewTier(def, i, td, tree, icon);
                    if (made == null) { Object.Destroy(go); return null; }
                    tree.skillTiers.Add(made);
                    continue;
                }
                if (td.from == null || !vanilla.TryGetValue(td.from, out Skill_Tree src))
                {
                    Plugin.Log.LogError($"{def.id} tier {i}: unknown source tree '{td.from}' (see vanilla_reference.txt)");
                    Object.Destroy(go);
                    return null;
                }
                if (td.tier < 0 || td.tier >= src.skillTiers.Count || src.skillTiers[td.tier] == null)
                {
                    Plugin.Log.LogError($"{def.id} tier {i}: '{td.from}' has no tier {td.tier}");
                    Object.Destroy(go);
                    return null;
                }
                sources.Add(src.internalName);
                Skill_Tier tier = CloneTier(src.skillTiers[td.tier], tree, i);
                ApplyTier(def, i, td, tier);
                tree.skillTiers.Add(tier);
            }
            Borrowed[def.id] = sources;
            FillEffectIcons(def, icon);
            WildMagic.Register(def);
            Augments.Register(def);

            // Starter gear
            var items = new List<Item>();
            foreach (string id in def.starterGearFrom ?? new List<string>())
            {
                if (vanilla.TryGetValue(id, out Skill_Tree t) && t.starterGear_items != null) items.AddRange(t.starterGear_items.Where(x => x != null));
                else Plugin.Log.LogWarning($"{def.id}: starterGearFrom '{id}' not found");
            }
            var modItems = new List<string>();
            foreach (string id in def.starterItems ?? new List<string>())
            {
                // Mod items register after classes; ResolveStarterItems adds them.
                Item item = ItemRegistry.VanillaItem(id);
                if (item != null) items.Add(item);
                else modItems.Add(def.pack != null ? ModPacks.Qualify(def.pack, id) : id);
            }
            if (modItems.Count > 0) pendingStarter[def.id] = modItems;
            tree.starterGear_items = items.ToArray();
            tree.starterGear_credits = def.starterCredits;

            // Priorities shown in the preview summary
            if (def.attributePriorities != null)
            {
                tree.attributePriorities = def.attributePriorities.Select(kv => new StatObj(ParseEnum(kv.Key, STAT_TYPE.Null, def.id), kv.Value)).ToArray();
            }
            else
            {
                tree.attributePriorities = sources.SelectMany(s => vanilla[s].attributePriorities ?? new StatObj[0])
                    .GroupBy(s => s.statType)
                    .Select(g => new StatObj(g.Key, g.Max(s => s.amount)))
                    .ToArray();
            }

            // Player tags: explicit ones plus the borrowed trees' tags (abilities often check them)
            var tags = new Dictionary<string, int>();
            if (def.inheritTags)
                foreach (string s in sources)
                    foreach (Tag t in vanilla[s].playerTags ?? new Tag[0])
                        if (t != null && !string.IsNullOrEmpty(t.tagName)) tags[t.tagName] = t.tagAmount;
            foreach (var kv in def.playerTags ?? new Dictionary<string, int>()) tags[kv.Key] = kv.Value;
            tree.playerTags = tags.Select(kv => new Tag(kv.Key, kv.Value)).ToArray();

            return tree;
        }

        /// <summary>Builds a tier around a brand-new ModAbility.</summary>
        private static Skill_Tier NewTier(ClassDef def, int i, TierDef td, Skill_Tree tree, Sprite classIcon)
        {
            AbilitySpec s = td.newAbility;
            if (string.IsNullOrEmpty(s.name)) { Plugin.Log.LogError($"{def.id} tier {i}: newAbility needs a \"name\""); return null; }
            Normalize(s, def.id);

            string abId = $"{def.id}_T{i}";
            var rt = new ModAbility.Runtime
            {
                spec = s,
                classId = def.id,
                internalName = abId,
                icon = BorrowedIcon(s.icon, def.id) ?? LoadSprite(def.folder, s.icon) ?? classIcon,
                name = Term(def.id, $"T{i}AbilName", s.name),
                desc = Term(def.id, $"T{i}AbilDesc", s.description ?? ""),
                perkName = Term(def.id, $"T{i}PerkName", s.perk?.name ?? s.name + "+"),
                perkDesc = Term(def.id, $"T{i}PerkDesc", s.perk?.description ?? ""),
            };
            ModAbility.Specs[abId] = rt;

            var tierGo = new GameObject($"T{i}_{abId}");
            tierGo.transform.SetParent(tree.transform, false);
            Skill_Tier tier = tierGo.AddComponent<Skill_Tier>();
            tier.internalName = abId;
            tier.skillTree = tree;
            tier.displayName = rt.name;
            tier.descrption = Term(def.id, $"T{i}Desc", td.description ?? "");
            tier.icon = rt.icon;
            tier.effect = "";
            tier.attributeChanges = Stats(td.attributes ?? td.addAttributes ?? new Dictionary<string, int>(), def.id);
            tier.resistanceChanges = Resists(td.resistances ?? td.addResistances ?? new Dictionary<string, int>(), def.id);

            var abGo = new GameObject(ModAbility.Prefix + abId);
            abGo.transform.SetParent(tierGo.transform, false);
            ModAbility ab = abGo.AddComponent<ModAbility>();
            ab.Bind(); // template is inactive, so Awake never runs on it; bind now for conflict checks and tooltips
            if (s.type == "passive") tier.passive = ab; else tier.ability = ab;
            return tier;
        }

        internal static void Normalize(AbilitySpec s, string classId = null)
        {
            s.type = (s.type ?? "active").ToLowerInvariant();
            s.target = (s.target ?? "enemy").ToLowerInvariant();
            foreach (ActionSpec a in AllActionsOf(s)) a.@do = (a.@do ?? "").ToLowerInvariant();
            foreach (ProcSpec p in s.procs ?? new List<ProcSpec>()) p.on = (p.on ?? "").ToLowerInvariant();
            SkillMods.Normalize(s.modifies, classId);
        }

        /// <summary>A transformation's abilities: registered like tier abilities, built on the player when the form starts.</summary>
        private static void RegisterKit(ClassDef def, EffectSpec es, string effectId, Sprite classIcon)
        {
            var names = new List<string>();
            string tag = new string(es.id.Where(char.IsLetterOrDigit).ToArray());
            for (int k = 0; k < es.kit.Count; k++)
            {
                AbilitySpec s = es.kit[k];
                if (s == null || string.IsNullOrEmpty(s.name)) { Plugin.Log.LogError($"{def.id}: effect '{es.id}' kit ability {k} needs a \"name\""); continue; }
                Normalize(s, def.id);
                if (s.type == "passive") { Plugin.Log.LogWarning($"{def.id}: kit ability '{s.name}' is passive; put passive parts in the effect itself"); continue; }
                string abId = $"{def.id}_F{tag}_{k}";
                ModAbility.Specs[abId] = new ModAbility.Runtime
                {
                    spec = s,
                    classId = def.id,
                    internalName = abId,
                    icon = LoadSprite(def.folder, s.icon) ?? LoadSprite(def.folder, es.icon) ?? classIcon,
                    name = Term(def.id, $"F{tag}_{k}Name", s.name),
                    desc = Term(def.id, $"F{tag}_{k}Desc", s.description ?? ""),
                    perkName = Term(def.id, $"F{tag}_{k}PerkName", s.perk?.name ?? s.name + "+"),
                    perkDesc = Term(def.id, $"F{tag}_{k}PerkDesc", s.perk?.description ?? ""),
                };
                names.Add(abId);
            }
            Forms.Register(effectId, def.id, names, es.keep, es.keepOtherClasses);
        }

        private static IEnumerable<ActionSpec> AllActionsOf(AbilitySpec s) =>
            (s.actions ?? new List<ActionSpec>())
            .Concat(s.perk?.addActions ?? new List<ActionSpec>())
            .Concat((s.procs ?? new List<ProcSpec>()).SelectMany(p => p.actions ?? new List<ActionSpec>()));

        internal static void RegisterEffects(ClassDef def, Sprite classIcon)
        {
            if (def.effects == null || def.effects.Count == 0 || EffectManager.instance == null) return;
            var dict = HarmonyLib.Traverse.Create(EffectManager.instance).Field("iEffects").GetValue<Dictionary<string, Effect>>();
            if (dict == null) { Plugin.Log.LogError("EffectManager lookup not found; custom effects disabled"); return; }
            foreach (EffectSpec es in def.effects)
            {
                if (string.IsNullOrEmpty(es.id)) { Plugin.Log.LogError($"{def.id}: an effect is missing \"id\""); continue; }
                string id = ModEffect.MakeId(def.id, es.id);
                foreach (string k in (es.stats ?? new Dictionary<string, int>()).Keys)
                    if (!Enum.TryParse(k, true, out STAT_TYPE _)) Plugin.Log.LogWarning($"{def.id}: effect '{es.id}' has unknown stat '{k}' (ignored). Stats: {string.Join(", ", Enum.GetNames(typeof(STAT_TYPE)).Skip(1))}");
                foreach (string k in (es.resists ?? new Dictionary<string, int>()).Keys)
                    if (!Enum.TryParse(k, true, out DAMAGE_TYPE _)) Plugin.Log.LogWarning($"{def.id}: effect '{es.id}' has unknown resist '{k}' (ignored)");
                foreach (ProcSpec p in es.procs ?? new List<ProcSpec>())
                {
                    p.on = (p.on ?? "").ToLowerInvariant();
                    foreach (ActionSpec a in p.actions ?? new List<ActionSpec>()) a.@do = (a.@do ?? "").ToLowerInvariant();
                }
                foreach (ActionSpec a in (es.burstActions ?? new List<ActionSpec>()).Concat(es.onApplyActions ?? new List<ActionSpec>())
                    .Concat(es.onExpireActions ?? new List<ActionSpec>()).Concat(es.onSaveActions ?? new List<ActionSpec>())
                    .Concat(es.onDeathActions ?? new List<ActionSpec>())
                    .Concat((es.thresholds ?? new List<ThresholdSpec>()).SelectMany(th => th.actions ?? new List<ActionSpec>())))
                    a.@do = (a.@do ?? "").ToLowerInvariant();
                SkillMods.Normalize(es.modifies, def.id);
                if (es.capScale != null && (es.burstAt > 0 || es.maxStacks <= 1 || !Actions.ScaleStats(es.capScale.scale).Any()))
                {
                    // a burst point is a danger set by hand, and a cap needs stats that exist
                    Plugin.Log.LogWarning($"{def.id}: {es.id}'s capScale ignored (needs maxStacks above 1, no burstAt, and a known stat; got \"{es.capScale.scale}\").");
                    es.capScale = null;
                }
                Effect vfxSrc = string.IsNullOrEmpty(es.vfx) ? null : EffectManager.instance.GetEffect(es.vfx, false);
                ModEffect.Specs[id] = new ModEffect.Runtime
                {
                    spec = es,
                    classId = def.id,
                    id = id,
                    icon = LoadSprite(def.folder, es.icon),
                    name = Term(def.id, $"FX_{es.id}_Name", es.name ?? es.id),
                    desc = Term(def.id, $"FX_{es.id}_Desc", es.description ?? ""),
                    particle = vfxSrc != null ? vfxSrc.spawnParticle : null,
                };
                var go = new GameObject(id);
                go.transform.SetParent(holder.transform, false);
                ModEffect fx = go.AddComponent<ModEffect>();
                fx.Bind();
                dict[id] = fx;
                if (es.kit != null && es.kit.Count > 0) RegisterKit(def, es, id, classIcon);
            }
        }

        /// <summary>Effects without their own png use the icon of the first ability that applies them, else the class icon.</summary>
        private static void FillEffectIcons(ClassDef def, Sprite classIcon)
        {
            foreach (EffectSpec es in def.effects ?? new List<EffectSpec>())
            {
                string id = ModEffect.MakeId(def.id, es.id);
                if (!ModEffect.Specs.TryGetValue(id, out var rt) || rt.icon != null) continue;
                var user = ModAbility.Specs.Values.FirstOrDefault(a => a.classId == def.id && AllActionsOf(a.spec).Any(x => x.effect == "@" + es.id));
                rt.icon = user?.icon ?? classIcon;
            }
        }

        /// <summary>An ability "icon" written as "SKILLTREE_X/3": borrow that vanilla tier's icon (no png needed).</summary>
        private static Sprite BorrowedIcon(string spec, string id)
        {
            if (string.IsNullOrEmpty(spec) || !spec.StartsWith("SKILLTREE_") || Game.instance?.skills == null) return null;
            var vanilla = Game.instance.skills.Where(t => t != null && !IsModTree(t.internalName))
                .GroupBy(t => t.internalName).ToDictionary(g => g.Key, g => g.First());
            return VanillaIcon(spec, vanilla, id);
        }

        /// <summary>"SKILLTREE_X" gives that tree's icon, "SKILLTREE_X/3" the icon of its tier 3 ability.</summary>
        private static Sprite VanillaIcon(string spec, Dictionary<string, Skill_Tree> vanilla, string id)
        {
            string[] parts = spec.Split('/');
            if (!vanilla.TryGetValue(parts[0], out Skill_Tree t)) { Plugin.Log.LogWarning($"{id}: iconFrom tree '{parts[0]}' not found"); return null; }
            if (parts.Length < 2) return t.icon;
            if (!int.TryParse(parts[1], out int i) || i < 0 || i >= t.skillTiers.Count || t.skillTiers[i] == null)
            {
                Plugin.Log.LogWarning($"{id}: iconFrom '{spec}' has no such tier");
                return t.icon;
            }
            Ability a = t.skillTiers[i].ability != null ? t.skillTiers[i].ability : t.skillTiers[i].passive;
            return a != null && a.displaySprite != null ? a.displaySprite : t.icon;
        }

        /// <summary>Copies a vanilla tier (and the ability/passive it owns) under the new tree.</summary>
        private static Skill_Tier CloneTier(Skill_Tier src, Skill_Tree tree, int index)
        {
            if (src.GetComponent<Skill_Tree>() != null)
                throw new InvalidOperationException($"tier '{src.internalName}' shares a GameObject with its tree; cannot clone it on its own");

            GameObject copy = Object.Instantiate(src.gameObject, tree.transform, false);
            copy.name = $"T{index}_{src.gameObject.name}";
            Skill_Tier tier = copy.GetComponent<Skill_Tier>();
            tier.ability = Own(tier.ability, copy.transform, tree.transform);
            tier.passive = Own(tier.passive, copy.transform, tree.transform);
            tier.skillTree = tree;
            tier.skillSelect = null;
            tier.isUnlocked = false;
            return tier;
        }

        /// <summary>Makes sure an ability referenced by a tier lives inside our tree so per-tree overrides do not leak into vanilla.</summary>
        private static Ability Own(Ability a, Transform tierT, Transform treeT)
        {
            if (a == null || a.transform.IsChildOf(treeT)) return a;
            GameObject copy = Object.Instantiate(a.gameObject, tierT, false);
            copy.name = a.gameObject.name;
            return (Ability)copy.GetComponent(a.GetType());
        }

        private static void ApplyTier(ClassDef def, int i, TierDef td, Skill_Tier tier)
        {
            string key = "T" + i;
            if (td.name != null) tier.displayName = Term(def.id, key + "Name", td.name);
            if (td.description != null) tier.descrption = Term(def.id, key + "Desc", td.description);

            if (td.attributes != null) tier.attributeChanges = Stats(td.attributes, def.id);
            if (td.addAttributes != null) tier.attributeChanges = (tier.attributeChanges ?? new StatObj[0]).Concat(Stats(td.addAttributes, def.id)).ToArray();
            if (td.resistances != null) tier.resistanceChanges = Resists(td.resistances, def.id);
            if (td.addResistances != null) tier.resistanceChanges = (tier.resistanceChanges ?? new DmgObj[0]).Concat(Resists(td.addResistances, def.id)).ToArray();
            if (tier.attributeChanges == null) tier.attributeChanges = new StatObj[0];
            if (tier.resistanceChanges == null) tier.resistanceChanges = new DmgObj[0];

            AbilityDef ad = td.ability;
            Ability a = tier.ability != null ? tier.ability : tier.passive;
            if (ad == null) return;
            if (a == null) { Plugin.Log.LogWarning($"{def.id} tier {i}: has ability overrides but the borrowed tier has no ability"); return; }

            if (ad.name != null) a.displayName = Term(def.id, key + "AbilName", ad.name);
            if (ad.description != null) a.description = Term(def.id, key + "AbilDesc", ad.description);
            if (ad.staminaCost.HasValue) a.staminaCost = ad.staminaCost.Value;
            if (ad.cooldown.HasValue) a.cooldown = ad.cooldown.Value;
            if (ad.hitChance.HasValue) a.hitChance = ad.hitChance.Value;
            if (ad.range.HasValue) a.range = ad.range.Value;
            if (ad.damage != null)
            {
                a.damageList = ad.damage.Select(d => new DmgObj(ParseEnum(d.type, DAMAGE_TYPE.Crushing, def.id), d.min) { damageMax = Math.Max(d.max, d.min) }).ToList();
            }
            Sprite s = LoadSprite(def.folder, ad.icon);
            if (s != null) { a.displaySprite = s; a.perkSprite = null; }
        }

        private static StatObj[] Stats(Dictionary<string, int> d, string id) =>
            d.Select(kv => new StatObj(ParseEnum(kv.Key, STAT_TYPE.Null, id), kv.Value)).Where(s => s.statType != STAT_TYPE.Null).ToArray();

        private static DmgObj[] Resists(Dictionary<string, int> d, string id) =>
            d.Select(kv => new DmgObj(ParseEnum(kv.Key, DAMAGE_TYPE.Null, id), kv.Value)).Where(r => r.dmgType != DAMAGE_TYPE.Null).ToArray();

        internal static string Term(string id, string key, string text)
        {
            string term = $"SCCL/{id}/{key}";
            Strings[term] = text;
            return term;
        }

        internal static T ParseEnum<T>(string value, T fallback, string ctx) where T : struct
        {
            if (value != null && Enum.TryParse(value, true, out T r)) return r;
            Plugin.Log.LogWarning($"{ctx}: '{value}' is not a valid {typeof(T).Name}; using {fallback}");
            return fallback;
        }

        private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        internal static Sprite LoadSprite(string folder, string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            string path = Path.GetFullPath(Path.Combine(folder, file));
            if (spriteCache.TryGetValue(path, out Sprite cached) && cached != null) return cached;
            if (!File.Exists(path)) { Plugin.Log.LogWarning($"Icon not found: {path}"); return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(path))) { Plugin.Log.LogWarning($"Icon is not a valid png: {path}"); return null; }
            tex.wrapMode = TextureWrapMode.Clamp;
            Object.DontDestroyOnLoad(tex);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = Path.GetFileNameWithoutExtension(path);
            Object.DontDestroyOnLoad(sprite);
            spriteCache[path] = sprite;
            return sprite;
        }

        /// <summary>Ability/passive internal names granted by a tree.</summary>
        public static HashSet<string> AbilityNames(Skill_Tree t)
        {
            var set = new HashSet<string>();
            if (t?.skillTiers == null) return set;
            foreach (Skill_Tier tier in t.skillTiers)
            {
                if (tier == null) continue;
                if (tier.ability != null) set.Add(tier.ability.internalName);
                if (tier.passive != null) set.Add(tier.passive.internalName);
            }
            return set;
        }

        /// <summary>Why two trees cannot be taken together, or null if they can.</summary>
        public static string Conflict(Skill_Tree a, Skill_Tree b)
        {
            if (a == null || b == null || a.internalName == b.internalName) return null;
            if (!IsModTree(a.internalName) && !IsModTree(b.internalName)) return null;
            if (Borrowed.TryGetValue(a.internalName, out var sa) && sa.Contains(b.internalName)) return "borrows from it";
            if (Borrowed.TryGetValue(b.internalName, out var sb) && sb.Contains(a.internalName)) return "borrows from it";
            var shared = AbilityNames(a);
            shared.IntersectWith(AbilityNames(b));
            return shared.Count > 0 ? "shares abilities with it" : null;
        }
    }
}
