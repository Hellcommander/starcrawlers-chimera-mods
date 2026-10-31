using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SCClassLoader;
using UnityEngine;

// The public face of the loader for code plugins (see "Code plugins" in Mods/README.md). Everything else in the loader is
// internal and free to change; what is public here stays, or the API level goes up.
namespace Breachwork
{
    using Actions = SCClassLoader.Actions; // the game has a global Actions of its own

    /// <summary>
    /// A code plugin: subclass this in a .dll in your mod's plugins/ folder. The loader makes one instance per class, in
    /// load order, for mods that are on and allowed to run code.
    /// </summary>
    public abstract class BreachworkPlugin
    {
        /// <summary>Your mod and everything you can register with the loader. Set before <see cref="OnLoad"/>.</summary>
        public PluginContext Context { get; internal set; }

        /// <summary>Early in game start, with no level or player yet. Register actions, conditions, events and settings here.</summary>
        public virtual void OnLoad() { }

        /// <summary>After the game and every mod's content are registered. Can run more than once in a session, so don't count on once.</summary>
        public virtual void OnGameReady() { }

        /// <summary>Every frame, title screen included. Stops for good after it has thrown 10 times.</summary>
        public virtual void OnUpdate() { }
    }

    /// <summary>What a plugin gets from the loader: its mod, logging, settings, saved data and every way to extend the framework.</summary>
    public sealed class PluginContext
    {
        internal readonly PluginHost.Loaded Owner;
        private Harmony harmony;

        internal PluginContext(PluginHost.Loaded owner)
        {
            Owner = owner;
            Log = new PluginLog(owner.pack.id);
        }

        public string ModId => Owner.pack.id;
        public string ModName => Owner.pack.Title;
        public string ModVersion => Owner.pack.version;
        /// <summary>The mod's folder. Anything in its plugins/ folder is left alone by the loader, so keep your own files there.</summary>
        public string ModFolder => Owner.pack.folder;
        public PluginLog Log { get; }
        /// <summary>A Harmony instance of your own. Patch with Harmony.PatchAll(GetType().Assembly) in OnLoad.</summary>
        public Harmony Harmony => harmony ?? (harmony = new Harmony("breachwork.plugin." + ModId));

        internal void Unpatch() => harmony?.UnpatchSelf();

        /// <summary>A path inside the mod's folder.</summary>
        public string PathTo(string relative) => Path.Combine(ModFolder, relative);

        // ---- extending the framework

        /// <summary>
        /// A new action, used in any class, item or enemy file as "do": "YourMod:name". Custom fields in the json are read
        /// with <see cref="ActionCall.Arg{T}"/>; chance, to, when and the other shared fields work as on any action.
        /// </summary>
        /// <param name="run">Does the action.</param>
        /// <param name="describe">Rules text for tooltips, from the action and who it lands on ("the target", "you", ...). Null = none.</param>
        /// <param name="hostile">Harmful: an ignoresAllies class then never lands it on its own side.</param>
        public void AddAction(string name, Action<ActionCall> run, Func<ActionSpec, string, string> describe = null, bool hostile = false) =>
            PluginHost.AddAction(Owner, name, run, describe, hostile);

        /// <summary>A new condition, used as "when": "YourMod:name". It gets the call and whoever is checked.</summary>
        /// <param name="describe">Rules text after "if" ("you stand alone").</param>
        public void AddCondition(string name, Func<ActionCall, Character, bool> check, string describe = null) =>
            PluginHost.AddCondition(Owner, name, check, describe);

        /// <summary>A new number for value checks: "value": "YourMod:name". Return null when it can't be read (the check fails).</summary>
        /// <param name="describe">Rules text ("your bounty tally").</param>
        public void AddValue(string name, Func<ActionCall, Character, float?> read, string describe = null) =>
            PluginHost.AddValue(Owner, name, read, describe);

        /// <summary>Rules text for an event of your own, so a proc's tooltip reads "When you collect a bounty:".</summary>
        public void AddEvent(string name, string describe) => PluginHost.AddEventText(Owner, name, describe);

        /// <summary>
        /// Runs whenever a proc with this "on" would fire: "hit", "kill", "hurt", "turn", ... (see "Triggers" in the README),
        /// or "OtherMod:name" for another plugin's event. Whatever it does can't set off more procs or events.
        /// </summary>
        public void On(string gameEvent, Action<GameEvent> handler) => PluginHost.AddHandler(Owner, gameEvent, handler);

        /// <summary>Fires your own event; procs listen with "on": "YourMod:name" (and "match" against <paramref name="detail"/>).</summary>
        public void FireEvent(string name, Character other = null, string detail = null) => ProcHub.Fire(PluginHost.Qualify(Owner, name), other, detail);

        /// <summary>
        /// Takes a new kind of content: any mod's json can then say { "pluginData": { "YourMod:name": ... } }. Runs once per
        /// entry, in load order, right after every plugin's OnLoad.
        /// </summary>
        public void OnData(string name, Action<PluginData> read) => PluginHost.AddDataReader(Owner, name, read);

        /// <summary>A button on the character picture picker (needs apiLevel 2). Gets true on the creation screen, false on the character sheet.</summary>
        public void AddPortraitButton(string label, Action<bool> click) => PluginHost.AddPortraitButton(Owner, label, click);

        // ---- settings and saved data

        /// <summary>A setting in Options > Mod Settings, in a section named after your mod. Read its .Value when you need it.</summary>
        public ConfigEntry<T> Bind<T>(string key, T defaultValue, string description) =>
            Plugin.Instance.Config.Bind(ModId, key, defaultValue, description);

        /// <summary>A number or text saved with the current run; <paramref name="fallback"/> when unset or no run is going.</summary>
        public T GetSaved<T>(string key, T fallback = default)
        {
            var d = SideAreas.Data;
            if (d == null || !d.TryGetValue(SavedKey(key), out object v)) return fallback;
            return v is T t ? t : (T)Convert.ChangeType(v, typeof(T));
        }

        /// <summary>
        /// Saves an int, float, bool or string with the current run. Only these, so a save still opens without your plugin.
        /// Returns false when no run is going.
        /// </summary>
        public bool SetSaved<T>(string key, T value)
        {
            if (!(value is int || value is float || value is bool || value is string))
                throw new ArgumentException($"Only int, float, bool and string can be saved, not {typeof(T).Name}", nameof(value));
            var d = SideAreas.Data;
            if (d == null) return false;
            d[SavedKey(key)] = value;
            return true;
        }

        private string SavedKey(string key) => "plugin_" + ModId + "_" + key;

        /// <summary>Starts a coroutine on the loader, which lives as long as the game.</summary>
        public Coroutine StartCoroutine(IEnumerator routine) => Plugin.Instance.StartCoroutine(routine);
    }

    /// <summary>Writes to the game's log as your mod, so errors and warnings also show on your mod's page in the Mods screen and the mod manager.</summary>
    public sealed class PluginLog
    {
        private readonly string id;
        internal PluginLog(string id) => this.id = id;
        public void Info(string text) => Plugin.Log.LogInfo($"{id}: {text}");
        public void Warn(string text) => Plugin.Log.LogWarning($"{id}: {text}");
        public void Error(string text) => Plugin.Log.LogError($"{id}: {text}");
    }

    /// <summary>One run of an action, condition or value: who is involved, what set it off, and the json it came from.</summary>
    public sealed class ActionCall
    {
        private readonly ActionContext ctx;

        internal ActionCall(ActionContext ctx, ActionSpec spec, Character target)
        {
            this.ctx = ctx;
            Spec = spec;
            Target = target;
        }

        /// <summary>The action as written; null in conditions and values.</summary>
        public ActionSpec Spec { get; }
        /// <summary>Who this lands on ("to" applied).</summary>
        public Character Target { get; }
        /// <summary>Whoever is acting: you, or the enemy or summon using the skill.</summary>
        public Character Caster => ctx.caster;
        /// <summary>Who hit the caster, in hurt, dodge and shieldBreak procs.</summary>
        public Character Attacker => ctx.attacker;
        /// <summary>Who the caster hit or killed, in hit, kill and crit procs.</summary>
        public Character Victim => ctx.victim;
        /// <summary>The ally in an ally event.</summary>
        public Character Ally => ctx.ally;
        /// <summary>The ability behind this, if any.</summary>
        public Ability Source => ctx.source;
        public string ClassId => ctx.classId;
        /// <summary>The skill or effect's name, for log lines.</summary>
        public string SourceName => ctx.sourceName;
        public int Rank => ctx.rank;
        /// <summary>The perk and falloff multiplier on this cast's damage.</summary>
        public float DamageMult => ctx.damageMult;
        /// <summary>The aim point: the target's or floor tile's position.</summary>
        public Vector3? Center => ctx.center;

        /// <summary>A custom field from the action's json, or <paramref name="fallback"/>.</summary>
        public T Arg<T>(string name, T fallback = default)
        {
            if (Spec?.extra == null) return fallback;
            foreach (var kv in Spec.extra)
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value.ToObject<T>();
            return fallback;
        }

        /// <summary>The action's "amount" with its "perRank" and stat "scale" added, as "heal" and "shield" work it out.</summary>
        public int Amount() => Actions.Amount(Spec, ctx);

        /// <summary>An effect's full id: "@Hexed" for this class's effect, "#Shocked" for the library's, or a vanilla id.</summary>
        public string EffectId(string name) => Actions.EffectId(name, ctx.classId);

        /// <summary>How many stacks of an effect (written as in <see cref="EffectId"/>) someone has.</summary>
        public int Stacks(Character who, string effect) => Actions.StacksOn(who, EffectId(effect));

        /// <summary>Runs the framework's own actions in this call's context; they land on <paramref name="on"/> (default <see cref="Target"/>).</summary>
        public void Run(IEnumerable<ActionSpec> actions, Character on = null) => Actions.Run(actions, ctx, on ?? Target);

        /// <summary>Runs one of the framework's own actions in this call's context.</summary>
        public void Run(ActionSpec action, Character on = null) => Actions.Run(new[] { action }, ctx, on ?? Target);
    }

    /// <summary>A game event, as a proc would see it.</summary>
    public sealed class GameEvent
    {
        /// <summary>The event's "on" name, in lower case.</summary>
        public string Name { get; internal set; }
        /// <summary>Who it happened to: you, or the ally whose own hit, kill, hurt or turn it was.</summary>
        public Character Bearer { get; internal set; }
        /// <summary>The other one: who you hit or killed, who hit you. Null for turn and the like.</summary>
        public Character Other { get; internal set; }
        /// <summary>The ally in an ally event (allyHurt, allyKill).</summary>
        public Character Ally { get; internal set; }
        /// <summary>What "match" compares against: an ability or item as "name|id", an effect id, a low-health threshold.</summary>
        public string Detail { get; internal set; }
    }

    /// <summary>One "pluginData" entry from a mod's json.</summary>
    public sealed class PluginData
    {
        /// <summary>The mod whose file it is.</summary>
        public string ModId { get; internal set; }
        public string File { get; internal set; }
        public JToken Value { get; internal set; }
        /// <summary>The entry as your own type.</summary>
        public T As<T>() => Value.ToObject<T>();
    }

    /// <summary>Helpers for plugins, for things not tied to one action.</summary>
    public static class Api
    {
        /// <summary>The plugin API level of the running loader, to compare with "apiLevel" in mod.json.</summary>
        public static int Level => ModPacks.ApiLevel;
        /// <summary>The running loader's version.</summary>
        public static string LoaderVersion => Plugin.Version;

        /// <summary>Whether a mod is loaded this session.</summary>
        public static bool ModLoaded(string id)
        {
            ModPack p = ModPacks.Find(id);
            return p != null && ModPacks.IsLoaded(p);
        }

        /// <summary>
        /// Stacks of an effect by full id (1 for an effect that doesn't stack), or "counter:NAME", "summons", as "stacksOf"
        /// reads them. Class effects are "@Name" only inside an action: use <see cref="ActionCall.Stacks"/> there.
        /// </summary>
        public static int Stacks(Character who, string effectId) => Actions.StacksOn(who, effectId);

        /// <summary>A named counter on a character ("do": "counter"); yours is saved with the run.</summary>
        public static int Counter(Character who, string name) => Counters.Get(who, name);
        public static void SetCounter(Character who, string name, int value) => Counters.Set(who, name, value);

        /// <summary>You, your summons, pets and crew.</summary>
        public static bool IsAlly(Character who) => TileStacks.IsAlly(who);

        /// <summary>Living enemies within a radius of a point.</summary>
        public static List<Character> EnemiesNear(Vector3 at, float radius) => AI_Manager.instance == null ? new List<Character>()
            : AI_Manager.instance.GetEnemiesAt(at, radius).Where(c => c != null && !c.GetIsDead()).ToList();

        /// <summary>The effect behind the transformation you're in, or null when you're in your own shape (needs apiLevel 2).</summary>
        public static Effect CurrentForm => Forms.Active && Player.instance != null ? Player.instance.effects.GetEffect(Forms.ActiveEffectId) : null;

        /// <summary>The folder character pictures live in; it exists after this is read (needs apiLevel 2).</summary>
        public static string PortraitFolder
        {
            get
            {
                Portraits.Files();
                return Portraits.Folder;
            }
        }

        /// <summary>The file name of the current character's picture, or null (needs apiLevel 2).</summary>
        public static string CurrentPortrait => Portraits.Current;

        /// <summary>Gives the current character a picture from the folder, by file name; or the one being made, on the creation screen (needs apiLevel 2).</summary>
        public static void SetPortrait(string fileName, bool onCreationScreen = false) => Portraits.Choose(onCreationScreen, fileName);

        /// <summary>Renders the character in their real gear to a file in the portrait folder, on the GPU with no AI model. Null when it worked, or why not (needs apiLevel 4).</summary>
        public static string RenderPortrait(string fileName) => PortraitRender.Render(fileName);

        /// <summary>
        /// True while a plugin has everyone act at the same time: the loader's combat pacing then stops making enemies wait
        /// for your step and for each other, and your turn for them (needs apiLevel 3). A plugin that sets it clears it when it's done.
        /// </summary>
        public static bool SimultaneousTurns { get; set; }

        /// <summary>Plays a particle at a point: a name from vanilla_reference.txt ("name@0.5" = half size) or a mod's own.</summary>
        public static void Vfx(string name, Vector3 at) => Actions.SpawnVfx(name, at);

        /// <summary>Floating combat text at a point (rich text works).</summary>
        public static void FloatText(string text, Vector3 at)
        {
            if (HUD.instance != null) HUD.instance.MakeFct(text, at, false);
        }

        /// <summary>Reads actions written in the class-file format: "[ { \"do\": \"damage\", \"min\": 3, \"max\": 5 } ]".</summary>
        public static List<ActionSpec> ParseActions(string json) => JsonConvert.DeserializeObject<List<ActionSpec>>(json);

        /// <summary>Runs actions outside any call (from an event or a Harmony patch), as <paramref name="caster"/>.</summary>
        public static void RunActions(IEnumerable<ActionSpec> actions, Character caster, Character target, string sourceName = "Plugin") =>
            Actions.Run(actions, new ActionContext { caster = caster, target = target, sourceName = sourceName }, target);
    }
}
