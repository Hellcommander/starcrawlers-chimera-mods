using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Breachwork;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Runs code plugins: the .dll files in the plugins/ folder of every mod that loads (a mod with code only loads once the
    /// player has allowed it, see <see cref="ModChoices"/>). Holds what they register and is the only way the framework calls
    /// into them, so a plugin that throws costs its own feature and never the game. The public side is PluginApi.cs.
    /// </summary>
    internal static class PluginHost
    {
        /// <summary>One running plugin.</summary>
        internal class Loaded
        {
            internal ModPack pack;
            internal BreachworkPlugin plugin;
            internal PluginContext ctx;
            internal string file;
            internal int errors, updateErrors;
            internal readonly List<Action<bool>> portraitButtons = new List<Action<bool>>();
        }

        private class ActionEntry { internal Loaded owner; internal Action<ActionCall> run; internal Func<ActionSpec, string, string> text; internal bool hostile; }
        private class ConditionEntry { internal Loaded owner; internal Func<ActionCall, Character, bool> check; internal string text; }
        private class ValueEntry { internal Loaded owner; internal Func<ActionCall, Character, float?> read; internal string text; }
        private class HandlerEntry { internal Loaded owner; internal Action<GameEvent> run; }
        private class DataEntry { internal Loaded owner; internal Action<PluginData> read; }

        /// <summary>Every plugin running this session, in load order.</summary>
        internal static readonly List<Loaded> All = new List<Loaded>();
        private static readonly List<Loaded> updaters = new List<Loaded>();

        private static readonly Dictionary<string, ActionEntry> actions = new Dictionary<string, ActionEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ConditionEntry> conditions = new Dictionary<string, ConditionEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ValueEntry> values = new Dictionary<string, ValueEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<HandlerEntry>> handlers = new Dictionary<string, List<HandlerEntry>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DataEntry> readers = new Dictionary<string, DataEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> eventTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Errors logged per plugin before the rest are only counted; OnUpdate gives up sooner, it would fill the log in a second.</summary>
        private const int MaxLoggedErrors = 25, MaxUpdateErrors = 10;

        // ---- loading

        /// <summary>
        /// Loads the plugins and scripts of this session's mods in load order (a mod's dlls, then its scripts), then hands
        /// them their "pluginData".
        /// </summary>
        internal static void LoadAll()
        {
            var mods = ModPacks.Ordered.Where(p => p.pluginFiles.Count > 0 || p.scriptFiles.Count > 0).ToList();
            if (mods.Count == 0) return;
            if (mods.Any(p => p.pluginFiles.Count > 0)) AppDomain.CurrentDomain.AssemblyResolve += FindAssembly;
            var seen = new HashSet<Assembly>();
            foreach (ModPack p in mods)
            {
                foreach (string file in p.pluginFiles) Load(p, file, seen);
                if (p.scriptFiles.Count > 0)
                {
                    // The interpreter is its own dll next to the loader's; a missing one shows up when this call is compiled.
                    try { LoadScripts(p); }
                    catch (Exception e) when (e is FileNotFoundException || e is TypeLoadException || e is BadImageFormatException)
                    {
                        Plugin.Log.LogError($"{p.id}: its scripts can't run: MoonSharp.Interpreter.dll is missing or broken in BepInEx/plugins/StarCrawlersClassLoader ({e.Message}). Reinstall Breachwork.");
                    }
                }
            }
            ReadData();
            Plugin.Log.LogInfo($"Code plugins running: {All.Count}" + (All.Count > 0 ? $" ({string.Join(", ", All.Select(l => l.pack.id + "/" + Name(l)))})" : ""));
        }

        private static string Name(Loaded l) => l.plugin is ScriptPlugin s ? $"{s.filesLoaded} script{(s.filesLoaded == 1 ? "" : "s")}" : l.plugin.GetType().Name;

        /// <summary>Starts a mod's scripts as one plugin.</summary>
        private static void LoadScripts(ModPack p) => Attach(new ScriptPlugin(p), p, "scripts");

        private static void Load(ModPack pack, string file, HashSet<Assembly> seen)
        {
            string name = Path.GetFileName(file);
            Assembly asm;
            Type[] types;
            try
            {
                asm = Assembly.LoadFrom(file);
                if (!seen.Add(asm)) { Plugin.Log.LogWarning($"{pack.id}: {name} is the same assembly as another plugin's, skipped"); return; }
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                string why = string.Join("; ", e.LoaderExceptions.Where(x => x != null).Select(x => x.Message).Distinct().Take(5));
                Plugin.Log.LogError($"{pack.id}: could not load {name}: {why}");
                return;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"{pack.id}: could not load {name}: {e.Message}");
                return;
            }
            foreach (Type t in types.Where(t => typeof(BreachworkPlugin).IsAssignableFrom(t) && !t.IsAbstract))
            {
                try { Attach((BreachworkPlugin)Activator.CreateInstance(t), pack, name); }
                catch (Exception e) { Plugin.Log.LogError($"{pack.id}: could not start {t.Name}: {e.InnerException?.Message ?? e.Message}"); }
            }
        }

        /// <summary>Starts a plugin. One whose OnLoad throws is taken out again, with everything it had registered.</summary>
        internal static Loaded Attach(BreachworkPlugin plugin, ModPack pack, string file)
        {
            var l = new Loaded { pack = pack, plugin = plugin, file = file };
            l.ctx = new PluginContext(l);
            plugin.Context = l.ctx;
            try { plugin.OnLoad(); }
            catch (Exception e)
            {
                Plugin.Log.LogError($"{pack.id}: {plugin.GetType().Name} failed to start and is not running: {e}");
                Remove(l);
                return null;
            }
            All.Add(l);
            if (plugin.GetType().GetMethod(nameof(BreachworkPlugin.OnUpdate)).DeclaringType != typeof(BreachworkPlugin)) updaters.Add(l);
            return l;
        }

        private static void Remove(Loaded l)
        {
            All.Remove(l);
            updaters.Remove(l);
            foreach (string k in actions.Where(kv => kv.Value.owner == l).Select(kv => kv.Key).ToList()) actions.Remove(k);
            foreach (string k in conditions.Where(kv => kv.Value.owner == l).Select(kv => kv.Key).ToList()) conditions.Remove(k);
            foreach (string k in values.Where(kv => kv.Value.owner == l).Select(kv => kv.Key).ToList()) values.Remove(k);
            foreach (string k in readers.Where(kv => kv.Value.owner == l).Select(kv => kv.Key).ToList()) readers.Remove(k);
            foreach (var list in handlers.Values) list.RemoveAll(h => h.owner == l);
            foreach (Action<bool> b in l.portraitButtons) Portraits.RemoveButton(b);
            try { l.ctx.Unpatch(); }
            catch (Exception e) { Plugin.Log.LogError($"{l.pack.id}: could not undo its patches: {e.Message}"); }
        }

        /// <summary>Plugins find the loader and each other's libraries by name, wherever the game has them loaded from.</summary>
        private static Assembly FindAssembly(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            Assembly have = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
            if (have != null) return have;
            string file = ModPacks.Ordered.SelectMany(p => p.pluginFiles)
                .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
            return file != null ? Assembly.LoadFrom(file) : null;
        }

        private static void ReadData()
        {
            foreach (ModPack p in ModPacks.Ordered)
                foreach (var (file, obj) in p.data)
                    foreach (JProperty entry in (obj["pluginData"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    {
                        if (!readers.TryGetValue(entry.Name, out DataEntry r)) { Plugin.Log.LogWarning($"{p.id}: nothing reads pluginData '{entry.Name}' in {file}"); continue; }
                        try { r.read(new PluginData { ModId = p.id, File = file, Value = entry.Value }); }
                        catch (Exception e) { Failed(r.owner, $"reading '{entry.Name}' from {file}", e); }
                    }
        }

        // ---- calls into plugins

        private static void Failed(Loaded l, string what, Exception e)
        {
            if (++l.errors < MaxLoggedErrors) Plugin.Log.LogError($"{l.pack.id}: {what} failed: {e}");
            else if (l.errors == MaxLoggedErrors) Plugin.Log.LogError($"{l.pack.id}: {what} failed: {e}\n{l.pack.id}: that makes {MaxLoggedErrors} errors from this plugin; the rest aren't logged.");
        }

        internal static void GameReady()
        {
            foreach (Loaded l in All.ToList())
            {
                try { l.plugin.OnGameReady(); }
                catch (Exception e) { Failed(l, "OnGameReady", e); }
            }
        }

        internal static void Tick()
        {
            for (int i = updaters.Count - 1; i >= 0; i--)
            {
                Loaded l = updaters[i];
                try { l.plugin.OnUpdate(); }
                catch (Exception e)
                {
                    Failed(l, "OnUpdate", e);
                    if (++l.updateErrors < MaxUpdateErrors) continue;
                    updaters.RemoveAt(i);
                    Plugin.Log.LogError($"{l.pack.id}: OnUpdate has thrown {MaxUpdateErrors} times and won't be called again this session.");
                }
            }
        }

        /// <summary>Runs a plugin's action; false when no plugin has one by that name.</summary>
        internal static bool RunAction(string name, ActionSpec a, ActionContext ctx, Character target)
        {
            if (!actions.TryGetValue(name, out ActionEntry e)) return false;
            try { e.run(new ActionCall(ctx, a, target)); }
            catch (Exception x) { Failed(e.owner, $"action '{name}' from {ctx.sourceName}", x); }
            return true;
        }

        internal static bool IsHostile(string name) => actions.TryGetValue(name, out ActionEntry e) && e.hostile;

        /// <summary>A plugin action's rules text; "" for none.</summary>
        internal static string ActionText(ActionSpec a, string who)
        {
            if (!actions.TryGetValue(a.@do ?? "", out ActionEntry e) || e.text == null) return "";
            try { return e.text(a, who) ?? ""; }
            catch (Exception x) { Failed(e.owner, $"describing action '{a.@do}'", x); return ""; }
        }

        /// <summary>A plugin condition's answer (a condition that throws doesn't hold); null when no plugin has one by that name.</summary>
        internal static bool? Condition(string name, ActionContext ctx, Character who)
        {
            if (!conditions.TryGetValue(name, out ConditionEntry e)) return null;
            try { return e.check(new ActionCall(ctx, null, who), who); }
            catch (Exception x) { Failed(e.owner, $"condition '{name}'", x); return false; }
        }

        internal static string ConditionText(string name) => conditions.TryGetValue(name, out ConditionEntry e) ? e.text : null;

        /// <summary>Reads a plugin's value into <paramref name="value"/>; false when no plugin has one by that name.</summary>
        internal static bool Value(string name, ActionContext ctx, Character who, out float? value)
        {
            value = null;
            if (!values.TryGetValue(name, out ValueEntry e)) return false;
            try { value = e.read(new ActionCall(ctx, null, who), who); }
            catch (Exception x) { Failed(e.owner, $"value '{name}'", x); }
            return true;
        }

        internal static bool HasValue(string name) => values.ContainsKey(name);

        internal static string ValueText(string name) => values.TryGetValue(name, out ValueEntry e) ? e.text ?? name : null;

        internal static string EventText(string name) => eventTexts.TryGetValue(name ?? "", out string t) ? t : null;

        /// <summary>
        /// Hands a game event to the plugins listening for it. Like a proc's actions, whatever they do can't set off more
        /// procs or events.
        /// </summary>
        internal static void Fire(string on, Character bearer, Character other, string detail, Character ally)
        {
            if (!handlers.TryGetValue(on, out var list) || list.Count == 0) return;
            var e = new GameEvent { Name = on.ToLowerInvariant(), Bearer = bearer, Other = other, Detail = detail, Ally = ally };
            Actions.ProcDepth++;
            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    try { list[i].run(e); }
                    catch (Exception x) { Failed(list[i].owner, $"event '{on}'", x); }
                }
            }
            finally { Actions.ProcDepth--; }
        }

        // ---- what plugins register

        /// <summary>"name" -> "ModId:name", the way json files refer to it.</summary>
        internal static string Qualify(Loaded l, string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A name is needed", nameof(name));
            return name.Contains(":") ? name : l.pack.id + ":" + name;
        }

        private static string Mine(Loaded l, string name)
        {
            string q = Qualify(l, name);
            if (!q.StartsWith(l.pack.id + ":", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"'{name}' isn't one of {l.pack.id}'s names", nameof(name));
            return q;
        }

        internal static void AddAction(Loaded l, string name, Action<ActionCall> run, Func<ActionSpec, string, string> text, bool hostile) =>
            actions[Mine(l, name)] = new ActionEntry { owner = l, run = run ?? throw new ArgumentNullException(nameof(run)), text = text, hostile = hostile };

        internal static void AddCondition(Loaded l, string name, Func<ActionCall, Character, bool> check, string text) =>
            conditions[Mine(l, name)] = new ConditionEntry { owner = l, check = check ?? throw new ArgumentNullException(nameof(check)), text = text };

        internal static void AddValue(Loaded l, string name, Func<ActionCall, Character, float?> read, string text) =>
            values[Mine(l, name)] = new ValueEntry { owner = l, read = read ?? throw new ArgumentNullException(nameof(read)), text = text };

        internal static void AddPortraitButton(Loaded l, string label, Action<bool> click)
        {
            if (click == null) throw new ArgumentNullException(nameof(click));
            Action<bool> guarded = creation =>
            {
                try { click(creation); }
                catch (Exception e) { Failed(l, $"portrait button '{label}'", e); }
            };
            l.portraitButtons.Add(guarded);
            Portraits.AddButton(label, guarded);
        }

        internal static void AddEventText(Loaded l, string name, string text) => eventTexts[Mine(l, name)] = text;

        internal static void AddDataReader(Loaded l, string name, Action<PluginData> read) =>
            readers[Mine(l, name)] = new DataEntry { owner = l, read = read ?? throw new ArgumentNullException(nameof(read)) };

        internal static void AddHandler(Loaded l, string on, Action<GameEvent> run)
        {
            if (string.IsNullOrEmpty(on)) throw new ArgumentException("An event name is needed", nameof(on));
            if (!handlers.TryGetValue(on, out var list)) handlers[on] = list = new List<HandlerEntry>();
            list.Add(new HandlerEntry { owner = l, run = run ?? throw new ArgumentNullException(nameof(run)) });
        }
    }
}
