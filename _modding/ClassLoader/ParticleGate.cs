using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// The game starts its own effects from hundreds of places, and every enemy cast does, whether anyone can see it or not.
    /// They all pass through Unity's one Instantiate, so this is where they're judged. A copy nobody can see, or one too many of
    /// the same effect, is made as asked and goes quiet at once: whoever asked still gets the object they expect, and the
    /// game's own timers clear it away. Only effects made of nothing but looks are touched, and never right beside you.
    /// </summary>
    internal static class ParticleGate
    {
        internal static ConfigEntry<int> MaxOfOne;

        private const float AlwaysWithin = 6f, LoopCounts = 3f, MostLife = 4f;
        private const int ReportTop = 8;

        private sealed class Prefab
        {
            public string name, refusedFor;
            public bool lookOnly, looping;
            public float life;
            public int silenced;
            public readonly Queue<float> ends = new Queue<float>();
        }

        internal static bool Own; // the mod's effects have been judged already

        private static readonly Dictionary<int, Prefab> prefabs = new Dictionary<int, Prefab>();
        private static readonly List<ParticleSystem> systems = new List<ParticleSystem>();
        private static readonly List<Light> lights = new List<Light>();

        internal static void Init(ConfigFile cfg)
        {
            MaxOfOne = cfg.Bind("Performance", "MaxCopiesOfGameEffect", 12,
                "Needs EventBus on. Most copies of one of the game's own effects playing at once, such as the same impact on a whole crowd. Close to you they always play. Only the look: what a skill does is unchanged.");
        }

        internal static GameObject Make(GameObject prefab, Vector3 pos)
        {
            Own = true;
            try { return Object.Instantiate(prefab, pos, Quaternion.identity); }
            finally { Own = false; }
        }

        internal static void Forget()
        {
            if (prefabs.Count > 0) prefabs.Clear();
        }

        // these answer collisions or raise events, so someone may be waiting on them
        private static readonly HashSet<string> Answering = new HashSet<string> { "RFX4_EffectEvent", "RFX4_PhysicsMotion", "RFX4_RaycastCollision", "RFX4_ParticleCollisionGameObject", "ME_ParticleCollisionDecal" };

        private static bool LooksOnly(Component c)
        {
            if (c == null || c is Transform || c is ParticleSystem || c is ParticleSystemRenderer || c is Light || c is ParticleControl || c is DestroyAfterTime) return true;
            string type = c.GetType().Name;
            return (type.StartsWith("ME_") || type.StartsWith("RFX4_")) && !Answering.Contains(type);
        }

        private static Prefab Read(GameObject from)
        {
            var p = new Prefab { name = from.name };
            ParticleSystem[] all = from.GetComponentsInChildren<ParticleSystem>(true);
            if (all.Length == 0) return p;
            foreach (Component c in from.GetComponentsInChildren<Component>(true))
            {
                if (LooksOnly(c)) continue;
                p.refusedFor = c.GetType().Name;
                ParticleLoad.Lighten(from);
                return p;
            }
            float life = 0f;
            foreach (ParticleSystem ps in all)
            {
                var main = ps.main;
                if (main.stopAction != ParticleSystemStopAction.None)
                {
                    p.refusedFor = "stop action"; // going quiet would destroy it under its owner
                    ParticleLoad.Lighten(from);
                    return p;
                }
                if (main.loop) p.looping = true;
                life = Mathf.Max(life, main.duration + main.startLifetime.constantMax);
            }
            p.lookOnly = true;
            p.life = p.looping ? LoopCounts : Mathf.Min(life, MostLife);
            ParticleLoad.Lighten(from); // the sweep takes seconds to reach a copy, and an impact is over by then; this holds until the game closes
            return p;
        }

        private static bool Quiet(Prefab p, Vector3 pos)
        {
            Player you = Player.instance;
            if (you != null && (pos - you.transform.position).sqrMagnitude <= AlwaysWithin * AlwaysWithin) return false;
            if (EventBus.Unseen(pos)) return true;
            if (p.looping) return false; // a status on something in plain view is what it looks like, not noise
            float now = Time.time;
            while (p.ends.Count > 0 && p.ends.Peek() <= now) p.ends.Dequeue();
            if (p.ends.Count >= Mathf.Max(1, BusProfile.Shown(MaxOfOne.Value))) return true;
            p.ends.Enqueue(now + p.life);
            return false;
        }

        private static void Silence(Prefab p, GameObject made)
        {
            p.silenced++;
            made.GetComponentsInChildren(true, systems);
            foreach (ParticleSystem ps in systems)
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (ps.main.loop) ParticleLoad.Park(ps); // it may wrap something that walks into view later
            }
            if (p.looping) return;
            made.GetComponentsInChildren(true, lights);
            foreach (Light l in lights) l.enabled = false;
        }

        /// <summary>The nearest copy above a system, which is the effect itself or the body it came with.</summary>
        private static string Owner(ParticleSystem ps)
        {
            for (Transform t = ps.transform; t != null; t = t.parent)
                if (t.name.EndsWith("(Clone)")) return t.name.Substring(0, t.name.Length - 7);
            return "(in the level) " + ps.transform.root.name;
        }

        /// <summary>For the stress test: what's playing, what went quiet, and what this wouldn't touch.</summary>
        internal static string Report()
        {
            var playing = Object.FindObjectsOfType<ParticleSystem>().Where(ps => ps.isPlaying).GroupBy(Owner).OrderByDescending(g => g.Count()).Take(ReportTop * 2).Select(g => $"{g.Key} {g.Count()}");
            var quiet = prefabs.Values.Where(p => p.silenced > 0).OrderByDescending(p => p.silenced).Take(ReportTop).Select(p => $"{p.name} {p.silenced}");
            var refused = prefabs.Values.Where(p => p.refusedFor != null).GroupBy(p => p.refusedFor).OrderByDescending(g => g.Count()).Take(ReportTop).Select(g => $"{g.Key} {g.Count()} ({g.First().name})");
            string line = $"gate: {prefabs.Values.Sum(p => p.silenced)} copies quiet of {prefabs.Count} prefabs seen, {prefabs.Values.Count(p => p.lookOnly)} look-only, {StatusLook.Waiting} status looks waiting; quiet:{string.Join(", ", quiet)}; left alone for: {string.Join(", ", refused)}; playing: {string.Join(", ", playing)}";
            foreach (Prefab p in prefabs.Values) p.silenced = 0;
            return line;
        }

        [HarmonyPatch(typeof(Object), nameof(Object.Instantiate), new[] { typeof(Object), typeof(Vector3), typeof(Quaternion) })]
        private static class Patch_Instantiate
        {
            private static void Postfix(Object original, Vector3 position, Object __result)
            {
                if (Own || !EventBus.On || Game.Game_isLoading || Game.Game_isTransitioning || __result == null) return; // a new floor is built with only the second one set
                GameObject from = original as GameObject ?? (original as Component)?.gameObject;
                GameObject made = __result as GameObject ?? (__result as Component)?.gameObject;
                if (from == null || made == null) return;
                if (!prefabs.TryGetValue(original.GetInstanceID(), out Prefab p)) prefabs[original.GetInstanceID()] = p = Read(from);
                if (p.lookOnly && Quiet(p, position)) Silence(p, made);
            }
        }
    }
}
