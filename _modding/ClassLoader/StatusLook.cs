using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// A status on an enemy is one looping particle at its chest and, for the glowing kinds, another copy for every mesh on
    /// its body, each set up by hand. An area spell made all of that inside the hit. Now the status lands at once and its look
    /// follows a few a frame, once the enemy is near enough to see, so most of a crowd out of reach costs nothing. Only the
    /// look waits: the effect itself, its stats and its turns are the game's own.
    /// </summary>
    internal static class StatusLook
    {
        internal static ConfigEntry<int> MaxOnBodies, MaxOfOne;
        internal static int Waiting => queue.Count;

        private const int MostPerFrame = 3, MostLooked = 64, MaxWaiting = 512;

        internal static int Delayed, Bare;

        private static readonly AccessTools.FieldRef<Effect, Character> Target = AccessTools.FieldRefAccess<Effect, Character>("target");
        private static readonly AccessTools.FieldRef<Effect, ParticleControl> Particle = AccessTools.FieldRefAccess<Effect, ParticleControl>("particle");
        private static readonly Action<Effect, Character> DressBody =
            AccessTools.MethodDelegate<Action<Effect, Character>>(AccessTools.Method(typeof(Effect), "ApplySpawnMeshEffect"));

        private static readonly Queue<Effect> queue = new Queue<Effect>();
        private static readonly List<Effect> worn = new List<Effect>();
        private static readonly Dictionary<ParticleControl, List<Effect>> wearing = new Dictionary<ParticleControl, List<Effect>>();
        private static readonly HashSet<ParticleControl> full = new HashSet<ParticleControl>();
        private static readonly Predicate<Effect> Gone = w => w == null;

        private struct Held { public ParticleControl particle; public PSMeshRendererUpdater mesh; }

        internal static void Init(ConfigFile cfg)
        {
            MaxOnBodies = cfg.Bind("Performance", "MaxStatusGlows", 40,
                "Needs EventBus on. Most statuses at once that wrap a glow around an enemy's whole body. Past this they still show their smaller effect. Only the look: the status is unchanged.");
            MaxOfOne = cfg.Bind("Performance", "MaxCopiesOfStatusLook", 40,
                "Needs EventBus on. Most enemies showing the same status's particle at once. Past this the rest wait for a place. Only the look: the status is unchanged.");
        }

        /// <summary>Once a frame, from the bus's pump.</summary>
        internal static void Tick(Vector3 you, float far)
        {
            int made = 0;
            full.Clear();
            for (int look = Mathf.Min(queue.Count, MostLooked); look > 0 && made < MostPerFrame; look--)
            {
                Effect e = queue.Dequeue();
                Character on = e != null ? Target(e) : null;
                if (on == null || on.GetIsDead() || on.effects == null || !on.effects.effects.Contains(e)) continue; // over before it showed
                if (ParticleLoad.OutOfSight(on.transform.position, you, far)) { queue.Enqueue(e); continue; }
                if (e.spawnParticle != null && Particle(e) == null && !HasRoom(e.spawnParticle)) { queue.Enqueue(e); continue; }
                made++;
                try { Dress(e, on); }
                catch (Exception x) { Plugin.Log.LogWarning($"Status look for {e.internalName} failed: {x.Message}"); }
            }
        }

        /// <summary>A whole crowd wearing one status reads the same with fewer of them shown. One past the limit waits for a place, it isn't lost.</summary>
        private static bool HasRoom(ParticleControl kind)
        {
            if (full.Contains(kind)) return false;
            if (!wearing.TryGetValue(kind, out var list)) wearing[kind] = list = new List<Effect>();
            list.RemoveAll(Gone);
            if (list.Count < Mathf.Max(1, BusProfile.Shown(MaxOfOne.Value))) return true;
            full.Add(kind);
            return false;
        }

        private static void Dress(Effect e, Character on)
        {
            if (e.spawnParticle != null && Particle(e) == null)
            {
                Transform at = on.GetHitFx();
                wearing[e.spawnParticle].Add(e);
                ParticleControl p = UnityEngine.Object.Instantiate(e.spawnParticle, at.position, Quaternion.identity);
                Particle(e) = p;
                p.transform.SetParent(at);
                p.transform.localScale = Vector3.one;
                p.transform.localPosition = Vector3.zero;
            }
            if (e.spawnMeshEffect == null) return;
            worn.RemoveAll(Gone);
            if (worn.Count >= Mathf.Max(0, BusProfile.Shown(MaxOnBodies.Value))) { Bare++; return; }
            DressBody(e, on);
            worn.Add(e);
        }

        internal static void Forget()
        {
            queue.Clear();
            worn.Clear();
            wearing.Clear();
        }

        [HarmonyPatch(typeof(Effect), nameof(Effect.Init), new[] { typeof(Character) })]
        private static class Patch_Init
        {
            private static void Prefix(Effect __instance, Character c, out Held __state)
            {
                __state = default;
                if (!EventBus.On || Game.Game_isLoading || c == null || c == Player.instance) return;
                // these reach for their particle or swap their glow straight after this
                if (__instance is Effect_Blind || __instance is Effect_Smuggler_Bounty || __instance is Effect_Mod_Damage_Gold) return;
                __state = new Held { particle = __instance.spawnParticle, mesh = __instance.spawnMeshEffect };
                __instance.spawnParticle = null;
                __instance.spawnMeshEffect = null;
            }

            private static void Postfix(Effect __instance, Held __state)
            {
                if (__state.particle == null && __state.mesh == null) return;
                __instance.spawnParticle = __state.particle;
                __instance.spawnMeshEffect = __state.mesh;
                if (queue.Count >= MaxWaiting) queue.Dequeue();
                queue.Enqueue(__instance);
                Delayed++;
            }
        }
    }
}
