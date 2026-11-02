using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using BepInEx.Configuration;
using HarmonyLib;
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
        internal const int Turn = 1, Effects = 2, Abilities = 4, Allies = 8, Health = 16, Orders = 32, Reactions = 64, Settings = 128;
        private static readonly string[] Names = { "turn", "effects", "abilities", "allies", "health", "orders", "reactions", "settings" };
        private const int Everything = 255;

        /// <summary>How late a change with no hook can show on a panel.</summary>
        private const float ForcedLookSeconds = 2f;
        internal const int MaxQueued = 4096;
        private const double PumpBudgetMs = 2.0;

        internal static ConfigEntry<bool> Enabled;
        internal static bool On => Enabled != null && Enabled.Value;

        private static readonly long[] stamps = new long[Names.Length];
        private static readonly long[] delivered = new long[Names.Length];

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Performance", "EventBus", false,
                "Optional. Game events skip characters with nothing listening, and HUD panels only refresh when what they show changes. Helps with very large crowds of enemies, summons and effects. Off = everything polls as usual.");
            Enabled.SettingChanged += (s, e) => { listening.Clear(); Mark(Everything); };
            cfg.SettingChanged += (s, e) => Mark(Settings);
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

        /// <summary>The "on" names each character's procs answer to, built when first asked and dropped when its kit changes.</summary>
        private static readonly Dictionary<Character, HashSet<string>> listening = new Dictionary<Character, HashSet<string>>();

        /// <summary>False only when no ability or effect on this character has a proc for the event.</summary>
        internal static bool Listens(Character bearer, string on)
        {
            if (bearer == null) return true;
            if (!listening.TryGetValue(bearer, out HashSet<string> names)) listening[bearer] = names = Collect(bearer);
            return names.Contains(on);
        }

        private static HashSet<string> Collect(Character bearer)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (bearer == Player.instance && Player_AbilityControl.instance != null)
                foreach (Ability a in Player_AbilityControl.instance.GetAbilities())
                    if (a is ModAbility m) AddAll(names, m.Procs);
            if (bearer.effects != null)
                foreach (Effect e in bearer.effects.effects)
                    if (e is ModEffect me) AddAll(names, me.Procs);
            return names;
        }

        private static void AddAll(HashSet<string> names, IEnumerable<ProcSpec> procs)
        {
            if (procs == null) return;
            foreach (ProcSpec p in procs)
                if (p.on != null) names.Add(p.on);
        }

        private static void KitChanged(Character who)
        {
            if (who != null) listening.Remove(who);
        }

        /// <summary>Each player turn: start the index over, which also lets go of the dead and of anything no hook saw.</summary>
        internal static void NewTurn()
        {
            if (!On) return;
            listening.Clear();
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
            private static void Postfix(AI.AI_TEAM team) { if (team == AI.AI_TEAM.Player) Mark(Allies); }
        }

        /// <summary>Hits on your side show on the pet strip and party cards; hits on enemies change nothing there.</summary>
        internal static void Hurt(Character who)
        {
            if (On && OnOurSide(who)) Mark(Health);
        }

        // ---- messages, for plugins

        private class Sub { internal PluginHost.Loaded owner; internal Action<object> run; }
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

        /// <summary>The one consumer: the loader's Update. A handler that publishes is heard next frame, never inside this loop's call to it.</summary>
        internal static void Pump()
        {
            mainThread = Thread.CurrentThread.ManagedThreadId;
            if (pumping) return;
            if (!On || Game.Game_isLoading)
            {
                // what's waiting may name things the load has destroyed
                while (queue.TryDequeue(out _)) Interlocked.Decrement(ref queued);
                if (!Game.Game_isLoading) BuffLane.Flush(); // turned off with a redraw still owed
                BuffLane.Forget();
                return;
            }
            pumping = true;
            try
            {
                BuffLane.Flush();
                for (int i = 0; i < stamps.Length; i++)
                {
                    if (stamps[i] == delivered[i]) continue;
                    delivered[i] = stamps[i];
                    Deliver("bus:" + Names[i], null); // however many changes this frame, once
                }
                clock.Restart();
                while (clock.Elapsed.TotalMilliseconds < PumpBudgetMs && queue.TryDequeue(out var m))
                {
                    Interlocked.Decrement(ref queued);
                    Deliver(m.Key, m.Value);
                }
            }
            finally { pumping = false; }
        }

        private static void Deliver(string topic, object data)
        {
            if (!subs.TryGetValue(topic, out var list)) return;
            foreach (Sub s in list)
            {
                try { s.run(data); }
                catch (Exception e) { PluginHost.Failed(s.owner, $"event bus topic '{topic}'", e); }
            }
        }
    }
}
