using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditionalWeakTable = System.Runtime.CompilerServices.ConditionalWeakTable<Character, SCClassLoader.Actor>;
using BepInEx.Configuration;
using Breachwork;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Scripts: the Lua files in a mod's scripts/ folder (see "Scripts" in Mods/README.md). A mod's scripts run as one code
    /// plugin, so everything they register goes through <see cref="PluginHost"/> like a dll's would, with the same error
    /// isolation. Unlike a dll, a script is sandboxed: it reaches nothing but the tables this file hands it (no files, no
    /// network, no reflection), and a call that runs too long is cut off. So script mods load without the player's OK.
    /// </summary>
    internal sealed class ScriptPlugin : BreachworkPlugin
    {
        internal readonly ModPack pack;
        internal Script lua;
        private readonly string folder;
        private readonly List<Closure> ready = new List<Closure>(), update = new List<Closure>();
        private readonly List<(KeyCode key, Closure fn)> keys = new List<(KeyCode, Closure)>();
        private readonly List<LuaTimer> timers = new List<LuaTimer>();
        /// <summary>Script calls under way (a handler calling an action calling a handler); only the outermost is time-limited.</summary>
        private int depth;
        /// <summary>Files loaded so far this session, so a hot reload can tell what changed.</summary>
        internal int filesLoaded;

        /// <summary>Lua instructions a top-level call may run before it is stopped (a few million is well under a second).</summary>
        internal const long MaxInstructions = 5_000_000;

        private static readonly CoreModules Sandbox = CoreModules.Basic | CoreModules.GlobalConsts | CoreModules.TableIterators | CoreModules.Metatables
            | CoreModules.String | CoreModules.Table | CoreModules.Math | CoreModules.Bit32 | CoreModules.ErrorHandling | CoreModules.Coroutine | CoreModules.OS_Time | CoreModules.Json
            | CoreModules.LoadMethods; // for require; the file-loading ones are taken out again below

        private static bool registered;

        internal ScriptPlugin(ModPack pack)
        {
            this.pack = pack;
            folder = Path.Combine(pack.folder, "scripts");
        }

        internal static void RegisterTypes()
        {
            if (registered) return;
            registered = true;
            UserData.RegistrationPolicy = MoonSharp.Interpreter.Interop.InteropRegistrationPolicy.Explicit;
            foreach (Type t in new[] { typeof(LuaMod), typeof(LuaGame), typeof(Actor), typeof(LuaCall), typeof(LuaEvent), typeof(LuaEntry), typeof(LuaSetting), typeof(LuaTimer), typeof(Vec) })
                UserData.RegisterType(t);
        }

        public override void OnLoad()
        {
            RegisterTypes();
            lua = new Script(Sandbox);
            lua.Options.DebugPrint = s => Context.Log.Info(s);
            lua.Options.ScriptLoader = new FolderLoader(folder);
            lua.Options.CheckThreadAccess = false;
            // require() stays (it only reads this mod's scripts folder, see FolderLoader); loading files by path does not.
            foreach (string gone in new[] { "dofile", "loadfile", "loadfilesafe" }) lua.Globals[gone] = DynValue.Nil;
            lua.Globals["mod"] = new LuaMod(this);
            lua.Globals["game"] = new LuaGame(this);
            lua.Globals["vec"] = (Func<double, double, double, Vec>)((x, y, z) => new Vec((float)x, (float)y, (float)z));
            foreach (string file in pack.scriptFiles) RunFile(file);
        }

        /// <summary>Runs one script file. A syntax or runtime error costs that file only; the mod's other scripts still run.</summary>
        private void RunFile(string file)
        {
            try
            {
                Guarded(() => lua.DoString(File.ReadAllText(file), null, Path.GetFileName(file)));
                filesLoaded++;
            }
            catch (Exception e) { Context.Log.Error($"{Path.GetFileName(file)} did not load: {Message(e)}"); }
        }

        public override void OnGameReady()
        {
            foreach (Closure fn in ready.ToList()) Call(fn);
        }

        public override void OnUpdate()
        {
            if (keys.Count > 0 && !Typing())
                foreach (var (key, fn) in keys)
                    if (Input.GetKeyDown(key)) Call(fn);
            for (int i = timers.Count - 1; i >= 0; i--)
            {
                LuaTimer t = timers[i];
                if (t.cancelled) { timers.RemoveAt(i); continue; }
                if (Time.unscaledTime < t.due) continue;
                if (t.every > 0f) t.due = Time.unscaledTime + t.every; else timers.RemoveAt(i);
                Call(t.fn);
            }
            foreach (Closure fn in update) Call(fn);
        }

        private static bool Typing()
        {
            GameObject sel = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            return sel != null && (sel.GetComponent<TMPro.TMP_InputField>() != null || sel.GetComponent<UnityEngine.UI.InputField>() != null);
        }

        // ---- calling into Lua

        /// <summary>
        /// Calls a script function. The outermost call runs as a coroutine with an instruction budget, so a script that never
        /// returns is stopped instead of hanging the game; nested calls (a handler running an action that fires a handler)
        /// share the outer budget's protection and run plainly. Errors come back as a <see cref="ScriptError"/> so the host
        /// logs them against the mod without a .NET stack trace.
        /// </summary>
        internal DynValue Call(Closure fn, params DynValue[] args)
        {
            if (fn == null) return DynValue.Nil;
            depth++;
            try
            {
                if (depth > 1) return fn.Call(args);
                DynValue co = lua.CreateCoroutine(fn);
                co.Coroutine.AutoYieldCounter = MaxInstructions;
                DynValue result = co.Coroutine.Resume(args);
                if (co.Coroutine.State == CoroutineState.ForceSuspended)
                    throw new ScriptError($"a script function ran for over {MaxInstructions:N0} instructions and was stopped (an endless loop?)");
                return result;
            }
            catch (ScriptError) { throw; }
            catch (InterpreterException e) { throw new ScriptError(e.DecoratedMessage ?? e.Message); }
            finally { depth--; }
        }

        /// <summary>Runs host code so that any error reads as a script error.</summary>
        private void Guarded(Action a)
        {
            try { a(); }
            catch (InterpreterException e) { throw new ScriptError(e.DecoratedMessage ?? e.Message); }
        }

        internal static string Message(Exception e) => e is ScriptError || e is InterpreterException ? ((e as InterpreterException)?.DecoratedMessage ?? e.Message) : e.ToString();

        /// <summary>A Lua function from an argument, or an error the script can read.</summary>
        internal static Closure Fn(DynValue v, string what)
        {
            if (v == null || v.Type != DataType.Function) throw new ScriptRuntimeException($"{what} needs a function, not {(v == null ? "nothing" : v.Type.ToString().ToLowerInvariant())}");
            return v.Function;
        }

        internal void OnReady(Closure fn) => ready.Add(fn);
        internal void OnUpdateHook(Closure fn) => update.Add(fn);

        internal void OnKey(string key, Closure fn)
        {
            if (!Enum.TryParse(key, true, out KeyCode code)) throw new ScriptRuntimeException($"'{key}' is not a key name (use Unity KeyCode names: F7, Alpha1, LeftBracket, ...)");
            keys.Add((code, fn));
        }

        internal LuaTimer Timer(float seconds, Closure fn, bool repeat)
        {
            var t = new LuaTimer { fn = fn, due = Time.unscaledTime + Mathf.Max(0f, seconds), every = repeat ? Mathf.Max(0.02f, seconds) : 0f };
            timers.Add(t);
            return t;
        }

        // ---- conversions

        internal static DynValue ToLua(Script s, JToken j)
        {
            switch (j)
            {
                case null: return DynValue.Nil;
                case JObject o:
                {
                    var t = new Table(s);
                    foreach (JProperty p in o.Properties()) t[p.Name] = ToLua(s, p.Value);
                    return DynValue.NewTable(t);
                }
                case JArray a:
                {
                    var t = new Table(s);
                    for (int i = 0; i < a.Count; i++) t[i + 1] = ToLua(s, a[i]);
                    return DynValue.NewTable(t);
                }
                case JValue v:
                    switch (v.Type)
                    {
                        case JTokenType.Integer: return DynValue.NewNumber(Convert.ToDouble(v.Value));
                        case JTokenType.Float: return DynValue.NewNumber(Convert.ToDouble(v.Value));
                        case JTokenType.Boolean: return DynValue.NewBoolean((bool)v.Value);
                        case JTokenType.Null: case JTokenType.Undefined: return DynValue.Nil;
                        default: return DynValue.NewString(v.ToString());
                    }
                default: return DynValue.NewString(j.ToString());
            }
        }

        /// <summary>
        /// Actions written as Lua tables (one, or a list) in the class-file format. "do" and "else" are Lua words, so tables
        /// write them as "act" and "orelse": { act = "damage", min = 3, max = 5 }.
        /// </summary>
        internal static List<ActionSpec> ToActions(DynValue v, string what)
        {
            if (v == null || v.IsNil()) return new List<ActionSpec>();
            if (v.Type != DataType.Table) throw new ScriptRuntimeException($"{what} needs a table of actions, like {{ act = \"damage\", min = 3, max = 5 }}");
            JToken j = ToJson(v);
            try
            {
                return j is JArray a ? a.ToObject<List<ActionSpec>>() : new List<ActionSpec> { j.ToObject<ActionSpec>() };
            }
            catch (Exception e) { throw new ScriptRuntimeException($"{what}: not an action: {e.Message}"); }
        }

        /// <summary>A Lua value as json: a table with keys 1..n is a list, any other table an object.</summary>
        internal static JToken ToJson(DynValue v)
        {
            switch (v.Type)
            {
                case DataType.Nil: case DataType.Void: return JValue.CreateNull();
                case DataType.Boolean: return new JValue(v.Boolean);
                case DataType.Number: return v.Number == Math.Floor(v.Number) && Math.Abs(v.Number) < long.MaxValue ? new JValue((long)v.Number) : new JValue(v.Number);
                case DataType.String: return new JValue(v.String);
                case DataType.UserData: return new JValue(v.UserData.Object?.ToString());
                case DataType.Table:
                {
                    Table t = v.Table;
                    int n = t.Length;
                    if (n > 0 && t.Keys.All(k => k.Type == DataType.Number && k.Number >= 1 && k.Number <= n && k.Number == Math.Floor(k.Number)))
                    {
                        var a = new JArray();
                        for (int i = 1; i <= n; i++) a.Add(ToJson(t.Get(i)));
                        return a;
                    }
                    var o = new JObject();
                    foreach (TablePair kv in t.Pairs)
                    {
                        string key = kv.Key.Type == DataType.String ? kv.Key.String : kv.Key.ToPrintString();
                        if (key == "act") key = "do"; else if (key == "orelse") key = "else";
                        o[key] = ToJson(kv.Value);
                    }
                    return o;
                }
                default: return new JValue(v.ToPrintString());
            }
        }

        internal static object ToSaved(DynValue v, string what)
        {
            switch (v.Type)
            {
                case DataType.Number: return v.Number == Math.Floor(v.Number) && Math.Abs(v.Number) < int.MaxValue ? (object)(int)v.Number : (float)v.Number;
                case DataType.Boolean: return v.Boolean;
                case DataType.String: return v.String;
                default: throw new ScriptRuntimeException($"{what} can save a number, a string or true/false, not {v.Type.ToString().ToLowerInvariant()}");
            }
        }

        internal static DynValue FromSaved(object o)
        {
            switch (o)
            {
                case null: return DynValue.Nil;
                case int i: return DynValue.NewNumber(i);
                case long l: return DynValue.NewNumber(l);
                case float f: return DynValue.NewNumber(f);
                case double d: return DynValue.NewNumber(d);
                case bool b: return DynValue.NewBoolean(b);
                case string s: return DynValue.NewString(s);
                default: return DynValue.NewString(o.ToString());
            }
        }

        internal static Vector3 ToVector(DynValue v, string what)
        {
            if (v.Type == DataType.UserData)
            {
                if (v.UserData.Object is Vec p) return p.V;
                if (v.UserData.Object is Actor a) return a.C != null ? a.C.transform.position : Vector3.zero;
            }
            if (v.Type == DataType.Table) return new Vector3((float)v.Table.Get("x").Number, (float)v.Table.Get("y").Number, (float)v.Table.Get("z").Number);
            throw new ScriptRuntimeException($"{what} needs a position (vec(x, y, z) or an actor)");
        }

        internal static Character ToCharacter(DynValue v) => v != null && v.Type == DataType.UserData && v.UserData.Object is Actor a ? a.C : null;

        /// <summary>Loads require()'d files from the mod's scripts folder only.</summary>
        private sealed class FolderLoader : ScriptLoaderBase
        {
            private readonly string folder;
            internal FolderLoader(string folder)
            {
                this.folder = Path.GetFullPath(folder);
                ModulePaths = new[] { Path.Combine(this.folder, "?.lua") };
            }
            /// <summary>Only files under the folder; a path that can't even be resolved (a rooted or malformed one) is outside too.</summary>
            private bool Inside(string file)
            {
                try
                {
                    string full = Path.GetFullPath(file);
                    return full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && full.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception) { return false; }
            }
            public override bool ScriptFileExists(string name)
            {
                try { return Inside(name) && File.Exists(name); }
                catch (Exception) { return false; }
            }
            public override object LoadFile(string file, Table globalContext)
            {
                if (!Inside(file)) throw new ScriptRuntimeException("scripts can only require files from their own mod's scripts folder");
                return File.ReadAllText(file);
            }
        }
    }

    /// <summary>A script's error, as the host logs it: the Lua message with file and line, and no .NET stack.</summary>
    internal sealed class ScriptError : Exception
    {
        internal ScriptError(string message) : base(message) { }
        public override string ToString() => Message;
    }

    /// <summary>A timer made by mod.after or mod.every; cancel() stops it.</summary>
    [MoonSharpUserData]
    internal sealed class LuaTimer
    {
        internal Closure fn;
        internal float due, every;
        internal bool cancelled;
        public void cancel() => cancelled = true;
        public bool active => !cancelled;
    }

    /// <summary>The "mod" table: the script's own mod, and everything it can register with the loader.</summary>
    [MoonSharpUserData]
    internal sealed class LuaMod
    {
        private readonly ScriptPlugin host;
        private PluginContext Ctx => host.Context;
        internal LuaMod(ScriptPlugin host) => this.host = host;

        public string id => Ctx.ModId;
        public string name => Ctx.ModName;
        public string version => Ctx.ModVersion;
        public string folder => Ctx.ModFolder;

        public void log(string text) => Ctx.Log.Info(text ?? "");
        public void warn(string text) => Ctx.Log.Warn(text ?? "");
        public void error(string text) => Ctx.Log.Error(text ?? "");

        // ---- registering

        /// <summary>mod.action("name", function(call) ... end, { describe = "text" | function(spec, who), hostile = true })</summary>
        public void action(string name, DynValue fn, Table options = null)
        {
            Closure run = ScriptPlugin.Fn(fn, "mod.action");
            DynValue describe = options?.Get("describe");
            bool hostile = options != null && options.Get("hostile").CastToBool();
            Func<ActionSpec, string, string> text = null;
            if (describe != null && describe.Type == DataType.String) text = (s, who) => describe.String.Replace("{who}", who).Replace("{amount}", s.amount.ToString());
            else if (describe != null && describe.Type == DataType.Function)
                text = (s, who) => host.Call(describe.Function, ScriptPlugin.ToLua(host.lua, JObject.FromObject(s)), DynValue.NewString(who)).CastToString();
            Ctx.AddAction(name, call => host.Call(run, UserData.Create(new LuaCall(call, host.lua))), text, hostile);
        }

        /// <summary>mod.condition("name", function(call, who) return true end, "describe")</summary>
        public void condition(string name, DynValue fn, string describe = null)
        {
            Closure check = ScriptPlugin.Fn(fn, "mod.condition");
            Ctx.AddCondition(name, (call, who) => host.Call(check, UserData.Create(new LuaCall(call, host.lua)), Actor.Dyn(who)).CastToBool(), describe);
        }

        /// <summary>mod.value("name", function(call, who) return 3 end, "describe")</summary>
        public void value(string name, DynValue fn, string describe = null)
        {
            Closure read = ScriptPlugin.Fn(fn, "mod.value");
            Ctx.AddValue(name, (call, who) =>
            {
                DynValue r = host.Call(read, UserData.Create(new LuaCall(call, host.lua)), Actor.Dyn(who));
                return r.Type == DataType.Number ? (float?)r.Number : null;
            }, describe);
        }

        /// <summary>mod.event("name", "when you ...") declares a trigger of your own; mod.fire sets it off.</summary>
        public void @event(string name, string describe) => Ctx.AddEvent(name, describe);
        public void fire(string name, Actor other = null, string detail = null) => Ctx.FireEvent(name, other?.C, detail);

        /// <summary>
        /// mod.on("kill", function(e) ... end): a game event, your own ("OtherMod:name" works too), or "ready" (the game and
        /// every mod are loaded) and "update" (every frame).
        /// </summary>
        public void on(string what, DynValue fn)
        {
            Closure run = ScriptPlugin.Fn(fn, "mod.on");
            switch ((what ?? "").ToLowerInvariant())
            {
                case "ready": host.OnReady(run); return;
                case "update": host.OnUpdateHook(run); return;
                case "": throw new ScriptRuntimeException("mod.on needs an event name");
            }
            Ctx.On(what, e => host.Call(run, UserData.Create(new LuaEvent(e))));
        }

        /// <summary>mod.data("name", function(entry) ... end): reads { "pluginData": { "MyMod:name": ... } } from any mod's json.</summary>
        public void data(string name, DynValue fn)
        {
            Closure read = ScriptPlugin.Fn(fn, "mod.data");
            Ctx.OnData(name, d => host.Call(read, UserData.Create(new LuaEntry(d, host.lua))));
        }

        /// <summary>mod.key("F7", function() ... end): runs when the key is pressed outside a text field.</summary>
        public void key(string keyName, DynValue fn) => host.OnKey(keyName, ScriptPlugin.Fn(fn, "mod.key"));

        /// <summary>mod.after(seconds, fn): once. mod.every(seconds, fn): until the returned timer's cancel().</summary>
        public LuaTimer after(double seconds, DynValue fn) => host.Timer((float)seconds, ScriptPlugin.Fn(fn, "mod.after"), false);
        public LuaTimer every(double seconds, DynValue fn) => host.Timer((float)seconds, ScriptPlugin.Fn(fn, "mod.every"), true);

        // ---- settings and saved data

        /// <summary>mod.setting("Payout", 1, "What it does") -> a setting in Options > Mod Settings; read and write its .value.</summary>
        public LuaSetting setting(string key, DynValue defaultValue, string description = "")
        {
            switch (defaultValue.Type)
            {
                case DataType.Number: return new LuaSetting(Ctx.Bind(key, (float)defaultValue.Number, description));
                case DataType.Boolean: return new LuaSetting(Ctx.Bind(key, defaultValue.Boolean, description));
                case DataType.String: return new LuaSetting(Ctx.Bind(key, defaultValue.String, description));
                default: throw new ScriptRuntimeException("mod.setting's default must be a number, a string or true/false");
            }
        }

        /// <summary>mod.saved("key", fallback): a value saved with the current run.</summary>
        public DynValue saved(string key, DynValue fallback = null)
        {
            object v = Ctx.GetSaved<object>(key);
            return v == null ? fallback ?? DynValue.Nil : ScriptPlugin.FromSaved(v);
        }

        /// <summary>mod.save("key", value): saves a number, string or true/false with the run. False when no run is going.</summary>
        public bool save(string key, DynValue value) => Ctx.SetSaved(key, ScriptPlugin.ToSaved(value, "mod.save"));
    }

    /// <summary>A setting from mod.setting: .value reads and writes it.</summary>
    [MoonSharpUserData]
    internal sealed class LuaSetting
    {
        private readonly ConfigEntryBase entry;
        internal LuaSetting(ConfigEntryBase entry) => this.entry = entry;
        public string key => entry.Definition.Key;
        public DynValue value
        {
            get => ScriptPlugin.FromSaved(entry.BoxedValue);
            set
            {
                if (entry.SettingType == typeof(float)) entry.BoxedValue = (float)value.CastToNumber().GetValueOrDefault();
                else if (entry.SettingType == typeof(bool)) entry.BoxedValue = value.CastToBool();
                else entry.BoxedValue = value.CastToString();
            }
        }
    }

    /// <summary>The "game" table: the world outside any one call.</summary>
    [MoonSharpUserData]
    internal sealed class LuaGame
    {
        private readonly ScriptPlugin host;
        internal LuaGame(ScriptPlugin host) => this.host = host;

        public int api_level => Api.Level;
        public string loader_version => Api.LoaderVersion;
        public bool mod_loaded(string id) => Api.ModLoaded(id);

        // ---- who is where

        public Actor player() => Actor.Of(Player.instance);
        public bool loading => Game.Game_isLoading || Player.instance == null;
        public bool in_combat => LevelManager.instance != null && LevelManager.instance.IsInCombat();
        public int floor => Game.FLOOR;
        public int turn => ProcHub.TurnCount;
        public double time => Time.time;

        private static IEnumerable<Character> Active() => AI_Manager.instance == null ? Enumerable.Empty<Character>()
            : AI_Manager.instance.GetActiveCharacters().Where(c => c != null && !c.GetIsDead());

        /// <summary>You, your summons, pets, charmed and hired help.</summary>
        public Table allies() => Actor.List(host.lua, Active().Where(TileStacks.IsAlly).Concat(Player.instance != null && !Player.instance.GetIsDead() ? new[] { Player.instance } : new Character[0]).Distinct());
        /// <summary>Every living hostile on the floor.</summary>
        public Table enemies() => Actor.List(host.lua, Active().Where(c => c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Hostile));
        public Table enemies_near(DynValue at, double radius) => Actor.List(host.lua, Api.EnemiesNear(ScriptPlugin.ToVector(at, "game.enemies_near"), (float)radius));
        public Table near(DynValue at, double radius)
        {
            Vector3 p = ScriptPlugin.ToVector(at, "game.near");
            return Actor.List(host.lua, AI_Manager.instance == null ? new List<Character>() : AI_Manager.instance.GetCharactersAt(p, (float)radius, false).Where(c => c != null && !c.GetIsDead()));
        }
        public double distance(DynValue a, DynValue b) => Vector3.Distance(ScriptPlugin.ToVector(a, "game.distance"), ScriptPlugin.ToVector(b, "game.distance"));

        /// <summary>game.spawn("CORPSEC_GUARD_MELEE", at, "enemy" | "ally"): a vanilla or mod creature, as an actor.</summary>
        public Actor spawn(string creature, DynValue at, string team = "enemy")
        {
            Character prefab = EnemyRegistry.Resolve(creature);
            if (prefab == null) throw new ScriptRuntimeException($"no creature '{creature}'");
            if (AI_Manager.instance == null) return null;
            AI.AI_TEAM side = string.Equals(team, "ally", StringComparison.OrdinalIgnoreCase) ? AI.AI_TEAM.Player : AI.AI_TEAM.Hostile;
            return Actor.Of(AI_Manager.instance.Spawn_Character(prefab, ScriptPlugin.ToVector(at, "game.spawn"), side));
        }

        // ---- credits and items

        public int credits => Menu_Inventory.instance != null ? Menu_Inventory.instance.GetCredits() : 0;
        public void add_credits(int amount) { if (Menu_Inventory.instance != null) Menu_Inventory.instance.AdjustCredits(amount); }

        private CostSpec Cost(string item, int count)
        {
            var c = new CostSpec { item = item, count = Math.Max(1, count) };
            c.resolved = Crafting.ResolveItem(item, host.pack);
            return c;
        }
        /// <summary>game.give_item("MyMod:ScrapHelm", 2): a vanilla or mod item id. True if it went in.</summary>
        public bool give_item(string item, int count = 1) => Player.instance != null && Crafting.Give(Crafting.ResolveItem(item, host.pack), Math.Max(1, count)) != null;
        public int count_item(string item) => Player.instance == null ? 0 : Crafting.Count(Cost(item, 1));
        public bool has_item(string item, int count = 1) => count_item(item) >= Math.Max(1, count);
        /// <summary>Takes items from your bag; false (and nothing taken) when you don't have that many.</summary>
        public bool take_item(string item, int count = 1)
        {
            CostSpec c = Cost(item, count);
            if (Player.instance == null || Crafting.Count(c) < c.count) return false;
            Crafting.Consume(c);
            return true;
        }
        public string item_name(string item) => ItemRegistry.NameOf(Crafting.ResolveItem(item, host.pack));

        // ---- showing and telling

        public void vfx(string name, DynValue at, double scale = 1.0) => Actions.SpawnVfx(scale != 1.0 ? $"{name}@{scale}" : name, ScriptPlugin.ToVector(at, "game.vfx"));
        /// <summary>Floating text over a point (rich text works).</summary>
        public void text(string text, DynValue at) { if (HUD.instance != null) HUD.instance.MakeFct(text ?? "", ScriptPlugin.ToVector(at, "game.text"), false); }
        /// <summary>Floating text in the middle of the screen.</summary>
        public void notify(string text) { if (HUD.instance != null) HUD.instance.MakeFct(text ?? ""); }
        /// <summary>A line in the game's message log.</summary>
        public void message(string text) => Game.LOG(text ?? "");
        /// <summary>A vanilla FMOD event ("event:/...") or a sound file in your mod ("sfx/ping.wav").</summary>
        public void sound(string spec, DynValue at = null, double volume = 1.0)
        {
            Vector3 p = at != null && !at.IsNil() ? ScriptPlugin.ToVector(at, "game.sound") : Player.instance != null ? Player.instance.transform.position : Vector3.zero;
            if (!spec.StartsWith("event:", StringComparison.OrdinalIgnoreCase) && !Path.IsPathRooted(spec)) spec = Path.Combine(host.pack.folder, spec);
            ModAudio.Play(spec, p, null, (float)volume);
        }

        // ---- the framework's actions, from anywhere

        /// <summary>
        /// game.run(actions, caster, target, "Source Name", "MOD_ClassId"): runs class-file actions outside any call. The
        /// class id lets "@Effect" names resolve to that class's effects.
        /// </summary>
        public void run(DynValue actions, Actor caster, Actor target = null, string sourceName = null, string classId = null)
        {
            Character c = caster?.C ?? Player.instance;
            Character t = target?.C ?? c;
            Actions.Run(ScriptPlugin.ToActions(actions, "game.run"), new ActionContext { caster = c, target = t, sourceName = sourceName ?? host.pack.id, classId = classId }, t);
        }

        public double random(DynValue min = null, DynValue max = null)
        {
            if (min == null || min.IsNil()) return UnityEngine.Random.value;
            if (max == null || max.IsNil()) return UnityEngine.Random.Range(1, (int)min.Number + 1);
            return UnityEngine.Random.Range((int)min.Number, (int)max.Number + 1);
        }
        public bool chance(double percent) => UnityEngine.Random.Range(0f, 100f) < percent;
    }

    /// <summary>A position.</summary>
    [MoonSharpUserData]
    internal sealed class Vec
    {
        internal Vector3 V;
        internal Vec(Vector3 v) => V = v;
        internal Vec(float x, float y, float z) => V = new Vector3(x, y, z);
        public double x { get => V.x; set => V.x = (float)value; }
        public double y { get => V.y; set => V.y = (float)value; }
        public double z { get => V.z; set => V.z = (float)value; }
        public double dist(DynValue other) => Vector3.Distance(V, ScriptPlugin.ToVector(other, "dist"));
        public Vec add(double dx, double dy, double dz) => new Vec(V + new Vector3((float)dx, (float)dy, (float)dz));
        public override string ToString() => $"({V.x:0.##}, {V.y:0.##}, {V.z:0.##})";
    }

    /// <summary>A character as a script sees it: you, an enemy, a summon. The same character is always the same actor.</summary>
    [MoonSharpUserData]
    internal sealed class Actor
    {
        private static readonly ConditionalWeakTable cache = new ConditionalWeakTable();
        internal readonly Character C;
        private Actor(Character c) => C = c;

        internal static Actor Of(Character c) => c == null ? null : cache.GetValue(c, x => new Actor(x));
        internal static DynValue Dyn(Character c) => c == null ? DynValue.Nil : UserData.Create(Of(c));
        internal static Table List(Script s, IEnumerable<Character> who)
        {
            var t = new Table(s);
            int i = 1;
            foreach (Character c in who) if (c != null) t[i++] = UserData.Create(Of(c));
            return t;
        }

        private Character Live => C != null && C.gameObject != null ? C : throw new ScriptRuntimeException("that actor is gone");

        // ---- who

        public string name => C != null ? C.GetName() : "";
        /// <summary>The creature's internal name, as creature files refer to it.</summary>
        public string id => C != null ? C.GetInternalName() : "";
        /// <summary>Player, Humanoid, Mechanical, Monster, Supernatural, ...</summary>
        public string kind => C != null ? C.characterType.ToString() : "";
        public int level => C != null ? C.level : 0;
        public bool is_player => C != null && C == Player.instance;
        public bool is_ally => TileStacks.IsAlly(C);
        public bool is_enemy => C != null && !TileStacks.IsAlly(C) && C.ai != null && C.ai.GetTeam() == AI.AI_TEAM.Hostile;
        public bool is_dead => C == null || C.GetIsDead();
        public bool alive => !is_dead;
        public bool exists => C != null && C.gameObject != null;
        public Vec pos => new Vec(Live.transform.position);
        public double distance(DynValue other) => Vector3.Distance(Live.transform.position, ScriptPlugin.ToVector(other, "distance"));

        // ---- numbers

        public int health { get => C != null ? C.GetHealth() : 0; set => Live.SetHealth(Mathf.Clamp(value, 0, Live.GetHealthMax())); }
        public int max_health => C != null ? C.GetHealthMax() : 0;
        public int stamina { get => C != null ? C.GetStamina() : 0; set => Live.SetStamina(value); }
        public int max_stamina => C != null ? C.GetStaminaMax() : 0;
        public int shield => C != null ? C.oshielding : 0;
        /// <summary>A stat by name: "Smarts", "Toughness", "Crit", "Armor", ...</summary>
        public int stat(string name) => Enum.TryParse(name, true, out STAT_TYPE t) ? Live.GetStat(t) : throw new ScriptRuntimeException($"no stat '{name}'");
        public int resist(string type) => Enum.TryParse(type, true, out DAMAGE_TYPE t) ? Live.GetResistance(t) : throw new ScriptRuntimeException($"no damage type '{type}'");
        public int tag(string name) => Live.GetTag(name);
        public bool has_tag(string name) => C != null && C.HasTag(name);
        public void add_tag(string name, int amount = 1) => Live.AddTag(name, amount);
        public void remove_tag(string name) => Live.RemoveTag(name);

        // ---- effects and counters (ids as the class-file format writes them: "#Shocked" (library), "SCCLFX_..." or a vanilla id)

        public int stacks(string effect) => Actions.StacksOn(C, Actions.EffectId(effect, null));
        public bool has_effect(string effect) => stacks(effect) > 0;
        public int counter(string name) => Counters.Get(C, name);
        public void set_counter(string name, int value) => Counters.Set(Live, name, value);
        public void add_counter(string name, int delta = 1) => Counters.Set(Live, name, Counters.Get(Live, name) + delta);

        // ---- doing things to them, through the framework's own actions so every rule applies

        private void Do(ActionSpec a, Actor by, string source)
        {
            Character caster = by?.C ?? Player.instance ?? Live;
            Actions.Run(new[] { a }, new ActionContext { caster = caster, target = Live, sourceName = source ?? "Script" }, Live);
        }
        /// <summary>a.damage(5, "Energy", by): "Physical", "Energy", "Fire", "Shock", "Chemical", "Void", ...</summary>
        public void damage(int amount, string type = "Physical", Actor by = null, string source = null) => Do(new ActionSpec { @do = "damage", type = type, min = amount, max = amount }, by, source);
        public void heal(int amount, string source = null) => Do(new ActionSpec { @do = "heal", amount = amount }, null, source);
        public void add_shield(int amount, string source = null) => Do(new ActionSpec { @do = "shield", amount = amount }, null, source);
        public void add_stamina(int amount) => Live.ModStamina(amount);
        /// <summary>a.effect("#Shocked", stacks, turns)</summary>
        public void effect(string effect, int stacks = 1, int turns = 0, Actor by = null, string source = null) => Do(new ActionSpec { @do = "effect", effect = effect, stacks = stacks, turns = turns }, by, source);
        public void purge(string effect) => Do(new ActionSpec { @do = "purge", effect = effect }, null, null);
        public void cleanse(int count = 0) => Do(new ActionSpec { @do = "cleanse", amount = count }, null, null);
        public void dispel(int count = 0) => Do(new ActionSpec { @do = "dispel", amount = count }, null, null);
        public void stun(int turns = 1) => Ability.ApplyStun(Live, Math.Max(1, turns));
        public void knockback(Actor from, int tiles = 1) => Ability.Knockback(Live, from?.C ?? Player.instance, Math.Max(1, tiles));
        public void kill() { if (C != null && !C.GetIsDead()) C.Die(); }
        /// <summary>Any action in the class-file format, on this actor: a.act({ do = "effect", effect = "#Shocked" }, by)</summary>
        public void act(DynValue action, Actor by = null, string source = null, string classId = null)
        {
            Character caster = by?.C ?? Player.instance ?? Live;
            Actions.Run(ScriptPlugin.ToActions(action, "act"), new ActionContext { caster = caster, target = Live, sourceName = source ?? "Script", classId = classId }, Live);
        }

        public override string ToString() => C == null ? "nobody" : $"{C.GetName()} ({C.GetHealth()}/{C.GetHealthMax()})";
        public override bool Equals(object o) => o is Actor a && a.C == C;
        public override int GetHashCode() => C != null ? C.GetHashCode() : 0;
    }

    /// <summary>One run of an action, condition or value, as the script sees it.</summary>
    [MoonSharpUserData]
    internal sealed class LuaCall
    {
        private readonly ActionCall call;
        private readonly Script s;
        internal LuaCall(ActionCall call, Script s) { this.call = call; this.s = s; }

        public Actor caster => Actor.Of(call.Caster);
        public Actor target => Actor.Of(call.Target);
        public Actor attacker => Actor.Of(call.Attacker);
        public Actor victim => Actor.Of(call.Victim);
        public Actor ally => Actor.Of(call.Ally);
        public int rank => call.Rank;
        public string class_id => call.ClassId;
        public string source_name => call.SourceName;
        public double damage_mult => call.DamageMult;
        public Vec center => call.Center.HasValue ? new Vec(call.Center.Value) : null;
        /// <summary>The action as written, as a table ("amount", "chance", your own fields).</summary>
        public DynValue spec => call.Spec == null ? DynValue.Nil : ScriptPlugin.ToLua(s, JObject.FromObject(call.Spec));
        /// <summary>The action's "amount" with "perRank" and stat "scale" worked in.</summary>
        public int amount() => call.Spec != null ? call.Amount() : 0;
        /// <summary>A custom field from the action's json: call.arg("reach", 2)</summary>
        public DynValue arg(string name, DynValue fallback = null)
        {
            JToken v = call.Arg<JToken>(name);
            return v == null ? fallback ?? DynValue.Nil : ScriptPlugin.ToLua(s, v);
        }
        /// <summary>"@Hexed" -> this class's effect id, "#Shocked" -> the library's.</summary>
        public string effect_id(string name) => call.EffectId(name);
        public int stacks(Actor who, string effect) => who?.C != null ? call.Stacks(who.C, effect) : 0;
        /// <summary>Runs class-file actions in this call's context (so "@Effect" and "to" work): call.run({ do = "damage", min = 2, max = 4 }, who)</summary>
        public void run(DynValue actions, Actor on = null) => call.Run(ScriptPlugin.ToActions(actions, "call.run"), on?.C);
    }

    /// <summary>A game event, as a proc would see it.</summary>
    [MoonSharpUserData]
    internal sealed class LuaEvent
    {
        private readonly GameEvent e;
        internal LuaEvent(GameEvent e) => this.e = e;
        public string name => e.Name;
        public Actor bearer => Actor.Of(e.Bearer);
        public Actor other => Actor.Of(e.Other);
        public Actor ally => Actor.Of(e.Ally);
        public string detail => e.Detail;
    }

    /// <summary>One "pluginData" entry from a mod's json.</summary>
    [MoonSharpUserData]
    internal sealed class LuaEntry
    {
        private readonly PluginData d;
        private readonly Script s;
        internal LuaEntry(PluginData d, Script s) { this.d = d; this.s = s; }
        public string mod => d.ModId;
        public string file => d.File;
        public DynValue value => ScriptPlugin.ToLua(s, d.Value);
    }
}
