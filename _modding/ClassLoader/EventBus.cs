using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using BepInEx.Configuration;
using HarmonyLib;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Optional built-in plugin, off until the player sets [Performance] EventBus in the config (or Mod Settings). It lives in
    /// the loader because what it speeds up is internal. It changes when things are checked, never what they do:
    ///
    /// Game events stop walking every ability and effect when nothing on that character listens for them, and HUD panels
    /// stop rebuilding on their timers when nothing they show has changed. A panel still looks every few seconds, so a change
    /// that came by a path with no hook is late, not lost.
    ///
    /// Everything game-side stays on the main thread: nearly all the cost is reading live game objects, which workers can't
    /// touch. Plugins may publish from their own threads; messages wait in a bounded queue, and one pump drains them from the
    /// loader's Update, on the main thread, with a single time budget shared by every message.
    /// </summary>
    internal static class EventBus
    {
        /// <summary>What changed, as bits, so a panel can watch several at once.</summary>
        internal const int Turn = 1, Effects = 2, Abilities = 4, Allies = 8, Health = 16, Orders = 32, Reactions = 64, Settings = 128, Synergies = 256, Items = 512, Hazards = 1024;
        private static readonly string[] Names = { "turn", "effects", "abilities", "allies", "health", "orders", "reactions", "settings", "synergies", "items", "hazards" };
        private const int Everything = 2047;

        /// <summary>How late a change with no hook can show on a panel.</summary>
        private const float ForcedLookSeconds = 2f;
        internal const int MaxQueued = 4096;
        private const double SlowHandlerMs = 10.0;
        private static double PumpBudgetMs => BusProfile.PumpMs;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> JobWorkers, MaxDamageNumbers, MaxLiveEffects;
        internal static bool On => RunByPlugin || (Enabled != null && Enabled.Value);
        internal static bool RunByPlugin;
        private static int gameWorkers;
        internal static ConfigEntry<bool> GraphicsJobs, IncrementalGC;
        private const string GfxJobsLine = "gfx-enable-gfx-jobs=1";
        private const string IncrementalGcLine = "gc-max-time-slice=3";

        private static readonly long[] stamps = new long[Names.Length];
        private static readonly long[] delivered = new long[Names.Length];

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Performance", "EventBus", false,
                "Optional. Game events skip characters with nothing listening, and HUD panels only refresh when what they show changes. Helps with very large crowds of enemies, summons and effects. Off = everything polls as usual.");
            JobWorkers = cfg.Bind("Performance", "JobWorkers", 0,
                "Needs EventBus on. Threads Unity may use for particles, skinning and culling. 0 = the game's own number, at most 8, since a big CPU spends more waking more of them than they save; anything above your CPU's limit is lowered to it.");
            MaxDamageNumbers = cfg.Bind("Performance", "MaxDamageNumbers", 40,
                "Needs EventBus on. Most floating damage numbers at once; crits may go to twice this. Off-screen ones are skipped. Only the numbers: the damage and the combat log are unchanged.");
            MaxLiveEffects = cfg.Bind("Performance", "MaxLiveEffects", 300,
                "Needs EventBus on. Most mod particle effects at once. Off-screen ones are skipped. Only the look: what a skill does is unchanged.");
            GraphicsJobs = cfg.Bind("Performance", "GraphicsJobs", false,
                "Spreads the game's rendering over your CPU's threads instead of one. Takes effect the next time the game starts. Turn it off again if you see flicker or crashes.");
            IncrementalGC = cfg.Bind("Performance", "IncrementalGC", false,
                "Spreads memory cleanup over many frames instead of stopping the game for it, which is the hitch you feel about once a second in a big fight. Takes effect the next time the game starts. Turn it off again if you see crashes.");
            SyncBootConfig();
            BusProfile.Init(cfg);
            BusTune.Init(cfg);
            BusWorkers.Init(cfg);
            AssetStream.Init(cfg);
            ParticleLoad.Init(cfg);
            StatusLook.Init(cfg);
            gameWorkers = JobsUtility.JobWorkerCount;
            Enabled.SettingChanged += (s, e) => { listening.Clear(); SkillLane.NewTurn(); Mark(Everything); ApplyWorkers(); };
            JobWorkers.SettingChanged += (s, e) => ApplyWorkers();
            ApplyWorkers();
            cfg.SettingChanged += (s, e) => Mark(Settings);
        }

        /// <summary>Unity's own worker threads: these only ever run Unity's jobs, so more of them can't touch game state.</summary>
        /// <summary>Unity reads this before any mod runs, so a change here counts from the next launch. Off leaves the file as the game shipped it.</summary>
        private static void SyncBootConfig()
        {
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath, "boot.config");
                var lines = new List<string>(System.IO.File.ReadAllLines(path));
                bool changed = false;
                foreach (var (line, on) in new[] { (GfxJobsLine, GraphicsJobs.Value), (IncrementalGcLine, IncrementalGC.Value) })
                {
                    if (lines.Contains(line) == on) continue;
                    if (on) lines.Add(line); else lines.RemoveAll(l => l == line);
                    changed = true;
                    Plugin.Log.LogInfo($"Library: {line} {(on ? "on" : "off")} from the next launch");
                }
                if (changed) System.IO.File.WriteAllLines(path, lines);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Library: couldn't set boot options: " + e.Message); }
        }

        internal static void ApplyWorkers()
        {
            int want = !On ? gameWorkers : Trial > 0 ? Trial : SettledWorkers;
            if (JobsUtility.JobWorkerCount != want) JobsUtility.JobWorkerCount = want;
        }

        /// <summary>The number <see cref="BusTune"/> is trying right now, 0 when it isn't.</summary>
        internal static int Trial;
        internal static int GameWorkers => gameWorkers;
        internal static int SettledWorkers => JobWorkers.Value > 0 ? Mathf.Clamp(JobWorkers.Value, 1, JobsUtility.JobWorkerMaximumCount) : Mathf.Min(gameWorkers, BusProfile.MostJobWorkers);

        // ---- what's only there to look at

        private static int numbers, numbersFrame = -1;
        internal static int NumbersSkipped, EffectsSkipped;

        private static bool OffScreen(Vector3 pos)
        {
            Camera eye = Camera.main;
            if (eye == null) return false;
            Vector3 v = eye.WorldToViewportPoint(pos);
            if (v.z <= 0f || v.x < -0.1f || v.x > 1.1f || v.y < -0.1f || v.y > 1.1f) return true;
            return Unseen(pos);
        }

        /// <summary>In view isn't seen: the game draws no bodies far from you or in rooms it has hidden, so an effect there floats with nobody under it.</summary>
        internal static bool Unseen(Vector3 pos)
        {
            Player you = Player.instance;
            if (you == null || Map.instance == null || CullManager.instance == null) return false;
            if (Vector3.Distance(pos, you.transform.position) > CullManager.instance.characterCullDistance) return true;
            return NodeIndex.Hidden(Map.instance, pos);
        }

        /// <summary>Every live number moves itself each frame, which rebuilds its whole canvas, so the cost grows with how many are up.</summary>
        [HarmonyPatch(typeof(HUD), nameof(HUD.MakeFct), new[] { typeof(string), typeof(Vector3), typeof(bool), typeof(bool) })]
        private static class Patch_DamageNumbers
        {
            private static bool Prefix(HUD __instance, Vector3 pos, bool isCrit, bool is2d)
            {
                if (!On) return true;
                if (!is2d && OffScreen(pos)) { NumbersSkipped++; return false; }
                if (numbersFrame != Time.frameCount)
                {
                    numbersFrame = Time.frameCount;
                    numbers = 0;
                    foreach (FloatingCombatText f in __instance.fctPool)
                        if (f != null && f.gameObject.activeSelf) numbers++;
                }
                int cap = Mathf.Max(1, BusProfile.Shown(MaxDamageNumbers.Value));
                if (numbers >= (isCrit ? cap * 2 : cap)) { NumbersSkipped++; return false; }
                numbers++;
                return true;
            }
        }

        /// <summary>True for a mod particle not worth making: nobody would see it, or the screen is already full of them.</summary>
        internal static bool SkipEffect(Vector3 pos)
        {
            if (!On) return false;
            if (Actions.VfxLife.Live.Count < Mathf.Max(1, BusProfile.Shown(MaxLiveEffects.Value)) && !OffScreen(pos)) return false;
            EffectsSkipped++;
            return true;
        }

        // ---- what changed

        internal static void Mark(int what)
        {
            if (!On) return;
            for (int i = 0; i < stamps.Length; i++)
                if ((what & 1 << i) != 0) stamps[i]++;
        }

        /// <summary>One panel's place in the stamps.</summary>
        internal struct Watch { internal long seen; internal float forceAt; }

        /// <summary>True when a panel can skip this look: the bus is on and nothing it shows has changed. Settings always count.</summary>
        internal static bool Unchanged(ref Watch w, int what)
        {
            if (!On) return false;
            what |= Settings;
            long sum = 0;
            for (int i = 0; i < stamps.Length; i++)
                if ((what & 1 << i) != 0) sum += stamps[i];
            if (sum == w.seen && Time.unscaledTime < w.forceAt) return true;
            w.seen = sum;
            w.forceAt = Time.unscaledTime + ForcedLookSeconds;
            return false;
        }

        // ---- who listens for what

        /// <summary>
        /// The "on" names each character's procs answer to, each with the effects that do, in the order they sit on the character.
        /// Built when first asked and dropped whole when its kit changes, never edited, so a proc that changes the kit can't disturb a walk over a list.
        /// </summary>
        private static readonly Dictionary<Character, Dictionary<string, List<ModEffect>>> listening = new Dictionary<Character, Dictionary<string, List<ModEffect>>>();
        private static readonly List<ModEffect> NoEffects = new List<ModEffect>();

        private static Dictionary<string, List<ModEffect>> Listeners(Character bearer)
        {
            if (!listening.TryGetValue(bearer, out var names)) listening[bearer] = names = Collect(bearer);
            return names;
        }

        /// <summary>False only when no ability or effect on this character has a proc for the event.</summary>
        internal static bool Listens(Character bearer, string on) => bearer == null || Listeners(bearer).ContainsKey(on);

        /// <summary>The effects on this character with a proc for the event: a few synergies among all it carries.</summary>
        internal static List<ModEffect> EffectsFor(Character bearer, string on) => Listeners(bearer).TryGetValue(on, out List<ModEffect> found) ? found : NoEffects;

        private static Dictionary<string, List<ModEffect>> Collect(Character bearer)
        {
            var names = new Dictionary<string, List<ModEffect>>(StringComparer.OrdinalIgnoreCase);
            if (bearer == Player.instance && Player_AbilityControl.instance != null)
                foreach (Ability a in Player_AbilityControl.instance.GetAbilities())
                    if (a is ModAbility m) AddAll(names, m.Procs, null);
            if (bearer.effects != null)
                foreach (Effect e in bearer.effects.effects)
                    if (e != null && e is ModEffect me) AddAll(names, me.Procs, me);
            return names;
        }

        private static void AddAll(Dictionary<string, List<ModEffect>> names, IEnumerable<ProcSpec> procs, ModEffect from)
        {
            if (procs == null) return;
            foreach (ProcSpec p in procs)
            {
                if (p.on == null) continue;
                if (!names.TryGetValue(p.on, out List<ModEffect> list)) names[p.on] = list = new List<ModEffect>();
                if (from != null && !list.Contains(from)) list.Add(from);
            }
        }

        /// <summary>Your abilities with a proc for this event, kept so a hit needn't walk everything you know to find them.</summary>
        internal static List<ModAbility> AbilitiesFor(string on)
        {
            if (abilitiesFor.TryGetValue(on, out List<ModAbility> found)) return found;
            found = new List<ModAbility>();
            foreach (Ability a in Player_AbilityControl.instance.GetAbilities())
                if (a is ModAbility m && m.Procs != null && m.Procs.Any(p => string.Equals(p.on, on, StringComparison.OrdinalIgnoreCase))) found.Add(m);
            return abilitiesFor[on] = found;
        }

        private static readonly Dictionary<string, List<ModAbility>> abilitiesFor = new Dictionary<string, List<ModAbility>>(StringComparer.OrdinalIgnoreCase);

        private static void KitChanged(Character who)
        {
            if (who != null) listening.Remove(who);
            if (who == Player.instance) abilitiesFor.Clear();
            SkillLane.KitChanged(who);
        }

        /// <summary>Each player turn: start the index over, which also lets go of the dead and of anything no hook saw.</summary>
        internal static void NewTurn()
        {
            Alarm.NewTurn();
            SkillLane.NewTurn();
            if (!On) return;
            listening.Clear();
            abilitiesFor.Clear();
            Mark(Turn);
        }

        internal static void EffectsChanged(Character who)
        {
            if (!On) return;
            KitChanged(who);
            if (who == Player.instance) Mark(Effects);
        }

        private static bool OnOurSide(Character c) => c != null && (c == Player.instance || c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Player);

        [HarmonyPatch(typeof(Character_Effect_Control), nameof(Character_Effect_Control.AddEffect))]
        private static class Patch_EffectAdded
        {
            private static void Postfix(Character_Effect_Control __instance) => EffectsChanged(__instance.character);
        }

        [HarmonyPatch(typeof(Character_Effect_Control), nameof(Character_Effect_Control.ClearEffect))]
        private static class Patch_EffectCleared
        {
            private static void Postfix(Character_Effect_Control __instance) => EffectsChanged(__instance.character);
        }

        [HarmonyPatch(typeof(Character_Effect_Control), nameof(Character_Effect_Control.CharacterDied), new[] { typeof(NavNode) })]
        private static class Patch_EffectsDied
        {
            private static void Postfix(Character_Effect_Control __instance)
            {
                EffectsChanged(__instance.character);
                if (OnOurSide(__instance.character)) Mark(Allies);
            }
        }

        [HarmonyPatch]
        private static class Patch_AbilitiesChanged
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                foreach (var m in AccessTools.GetDeclaredMethods(typeof(Player_AbilityControl)))
                    if (m.Name.EndsWith("Ability") && (m.Name.StartsWith("Add") || m.Name.StartsWith("Load") || m.Name.StartsWith("Remove"))) yield return m;
            }

            private static void Postfix()
            {
                if (!On) return;
                KitChanged(Player.instance);
                Mark(Abilities);
            }
        }

        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.Spawn_Character), new[] { typeof(Character), typeof(Vector3), typeof(Quaternion), typeof(AI.AI_TEAM) })]
        private static class Patch_Spawned
        {
            private static void Postfix(AI.AI_TEAM team)
            {
                Contracts.ForgetCrowd();
                if (team == AI.AI_TEAM.Player) Mark(Allies);
            }
        }

        /// <summary>Hits on your side show on the pet strip and party cards; hits on enemies change nothing there.</summary>
        internal static void Hurt(Character who)
        {
            if (On && OnOurSide(who)) Mark(Health);
        }

        // ---- messages, for plugins

        private class Sub { internal PluginHost.Loaded owner; internal Action<object> run; internal bool warned; }
        private static readonly Dictionary<string, List<Sub>> subs = new Dictionary<string, List<Sub>>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentQueue<KeyValuePair<string, object>> queue = new ConcurrentQueue<KeyValuePair<string, object>>();
        private static readonly Stopwatch clock = new Stopwatch();
        private static int queued, dropped, mainThread = -1;
        private static bool pumping;

        internal static int Queued => Volatile.Read(ref queued);
        internal static int Dropped => Volatile.Read(ref dropped);
        internal static int Subscribers(string topic) => subs.TryGetValue(topic, out var l) ? l.Count : 0;

        /// <summary>Main thread only, like everything a handler does.</summary>
        internal static Action Subscribe(PluginHost.Loaded owner, string topic, Action<object> run)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("A topic is needed", nameof(topic));
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (Thread.CurrentThread.ManagedThreadId != mainThread && mainThread != -1) throw new InvalidOperationException("Subscribe from the main thread");
            if (!subs.TryGetValue(topic, out var list)) subs[topic] = list = new List<Sub>();
            var sub = new Sub { owner = owner, run = run };
            // a new list, so unsubscribing inside a handler can't disturb the delivery running over the old one
            subs[topic] = new List<Sub>(list) { sub };
            return () =>
            {
                if (!subs.TryGetValue(topic, out var now)) return;
                var less = new List<Sub>(now);
                less.Remove(sub);
                subs[topic] = less;
            };
        }

        /// <summary>Any thread. False when the bus is off or the queue is full; a full queue drops rather than grow without end.</summary>
        internal static bool Publish(string topic, object data)
        {
            if (!On || string.IsNullOrEmpty(topic)) return false;
            if (Interlocked.Increment(ref queued) > MaxQueued)
            {
                Interlocked.Decrement(ref queued);
                if (Interlocked.Increment(ref dropped) == 1) Plugin.Log.LogWarning($"Event bus: queue full at {MaxQueued}, dropping messages (first on '{topic}')");
                return false;
            }
            queue.Enqueue(new KeyValuePair<string, object>(topic, data));
            return true;
        }

        private const float EasedStep = 0.05f, SlowFrame = 0.04f, FineFrame = 0.025f;
        private static float gameStep;

        /// <summary>A slow frame runs physics and every fixed script up to five times to catch up, which slows the next frame too. In a heavy fight they step less often, and go back once it passes.</summary>
        private static void PaceFixedSteps()
        {
            if (gameStep == 0f && On && Time.smoothDeltaTime > SlowFrame)
            {
                gameStep = Time.fixedDeltaTime;
                Time.fixedDeltaTime = EasedStep;
            }
            else if (gameStep != 0f && (!On || Time.smoothDeltaTime < FineFrame))
            {
                Time.fixedDeltaTime = gameStep;
                gameStep = 0f;
            }
        }

        /// <summary>The one consumer: the loader's Update. A handler that publishes is heard next frame, never inside this loop's call to it.</summary>
        internal static void Pump()
        {
            mainThread = Thread.CurrentThread.ManagedThreadId;
            // the game writes a line to disk for every hit a shield takes; warnings and errors still get through
            UnityEngine.Debug.unityLogger.filterLogType = On ? LogType.Warning : LogType.Log;
            PaceFixedSteps();
            if (pumping) return;
            if (!On || Game.Game_isLoading)
            {
                // what's waiting may name things the load has destroyed
                while (queue.TryDequeue(out _)) Interlocked.Decrement(ref queued);
                if (Game.Game_isLoading) HitLane.Forget();
                else { HitLane.Flush(); XpBatch.Flush(); BuffLane.Flush(); ItemLane.Flush(); } // turned off mid-burst: a hit still lands
                BusWorkers.Forget();
                ParticleLoad.Forget();
                BuffLane.Forget();
                ItemLane.Forget();
                return;
            }
            pumping = true;
            try
            {
                LogBatch.Flush();
                XpBatch.Flush();
                BuffLane.Flush();
                ItemLane.Flush();
                ParticleLoad.Tick();
                for (int i = 0; i < stamps.Length; i++)
                {
                    if (stamps[i] == delivered[i]) continue;
                    delivered[i] = stamps[i];
                    Deliver("bus:" + Names[i], null); // however many changes this frame, once
                }
                clock.Restart();
                HitLane.Drain(clock, PumpBudgetMs); // the fight first, so a noisy plugin can't hold a hit back
                while (clock.Elapsed.TotalMilliseconds < PumpBudgetMs && queue.TryDequeue(out var m))
                {
                    Interlocked.Decrement(ref queued);
                    Deliver(m.Key, m.Value);
                }
                BusWorkers.Drain(clock, PumpBudgetMs); // the same allowance: two budgets would stack into a stutter
            }
            finally { pumping = false; }
        }

        private static void Deliver(string topic, object data)
        {
            if (!subs.TryGetValue(topic, out var list)) return;
            foreach (Sub s in list)
            {
                long began = Stopwatch.GetTimestamp();
                try { s.run(data); }
                catch (Exception e)
                {
                    if (s.owner != null) PluginHost.Failed(s.owner, $"event bus topic '{topic}'", e);
                    else Plugin.Log.LogError($"Event bus topic '{topic}' failed: {e}"); // the loader's own handler
                }
                double ms = (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency;
                if (ms > SlowHandlerMs && !s.warned)
                {
                    s.warned = true; // once, or a slow handler would add a log line to every slow frame
                    Plugin.Log.LogWarning($"{s.owner?.pack.id ?? "loader"}: handler for '{topic}' took {ms:0.0} ms on the main thread; the bus's whole budget is {PumpBudgetMs:0} ms a frame.");
                }
            }
        }
    }
}
