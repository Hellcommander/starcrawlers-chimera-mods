using System;
using System.Collections.Generic;
using HarmonyLib;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// "Who can be seen from here" fires a ray per character, up to five for an enemy, one after another on the main thread. A
    /// chain of lightning asks this per hop, and a radius of 20 or more is most of a crowd. With the bus on the rays of one
    /// question go out as one batch over Unity's job workers, which sit idle in a fight, and the question waits for the batch, so
    /// the game gets the same list in the same order at the same moment. Picking who to aim at and reading who was hit stay
    /// here: only the rays spread.
    ///
    /// Unity documents less about these batches than about a plain ray, so the first rays run both ways. Answers that don't
    /// match the game's turn the lane off for the session and the game does its own looking.
    /// </summary>
    internal static class SightLane
    {
        private const int FewestForJobs = 8, RaysPerJob = 16, ProofRays = 512, WrongAllowed = 5;

        internal static bool Bypass; // devtests
        internal static int Batches, Rays, Proven, Wrong;
        private static bool refused;

        private static readonly AccessTools.FieldRef<AI_Manager, List<AI>> Ais = AccessTools.FieldRefAccess<AI_Manager, List<AI>>("ais");

        private static NativeArray<RaycastCommand> commands;
        private static NativeArray<RaycastHit> results;
        private static readonly List<Vector3> aims = new List<Vector3>();
        private static readonly List<Character> who = new List<Character>();
        private static readonly List<CapsuleCollider> bodies = new List<CapsuleCollider>();
        private static readonly List<int> unseen = new List<int>();

        private static bool Ready => EventBus.On && !Bypass && !refused && Player.instance != null && !Game.Game_isLoading;

        private static void Fit(int rays)
        {
            if (commands.IsCreated && commands.Length >= rays) return;
            int size = Mathf.Max(256, rays * 2);
            if (commands.IsCreated) { commands.Dispose(); results.Dispose(); }
            commands = new NativeArray<RaycastCommand>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            results = new NativeArray<RaycastHit>(size, Allocator.Persistent);
        }

        /// <summary>A ray from <paramref name="from"/> to every aim, as the game fires them; each answer lands in results at the aim's place. False when the lane has stepped aside.</summary>
        private static bool Cast(Vector3 from)
        {
            int n = aims.Count;
            if (n == 0) return true;
            Fit(n);
            if (n < FewestForJobs)
            {
                for (int i = 0; i < n; i++)
                {
                    Physics.Raycast(from, aims[i] - from, out RaycastHit hit);
                    results[i] = hit;
                }
                return true;
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 way = aims[i] - from;
                float length = way.magnitude;
                // a plain ray divides by the length and Ray normalizes that again; one bit off flips a ray that grazes a body's edge.
                // Native code still parts the two on about 1 ray in 4000 that runs exactly along a body or wall, whatever is built
                // here: no way of making the direction matched a plain ray, and a plain ray parts from itself the same way.
                // It hits nothing along no direction, and a mask of nothing says the same to a batch.
                commands[i] = length > float.Epsilon ? new RaycastCommand(from, new Ray(from, way / length).direction) : new RaycastCommand(from, Vector3.up, 0f, 0);
            }
            if (Physics.autoSyncTransforms) Physics.SyncTransforms();
            RaycastCommand.ScheduleBatch(commands.GetSubArray(0, n), results.GetSubArray(0, n), RaysPerJob).Complete();
            Batches++;
            Rays += n;
            return Proved(from, n);
        }

        private static bool Proved(Vector3 from, int n)
        {
            if (Proven >= ProofRays) return true;
            bool same = true;
            for (int i = 0; i < n; i++)
            {
                Physics.Raycast(from, aims[i] - from, out RaycastHit hit);
                if (hit.collider == results[i].collider) continue;
                same = false;
                Wrong++;
            }
            Proven += n;
            if (Wrong > WrongAllowed) Refuse($"{Wrong} of {Proven} rays didn't match the game's");
            else if (Proven >= ProofRays) Plugin.Log.LogInfo($"Sight lane: {Proven} rays checked against the game's, {Wrong} differed; batches go over the job workers from here");
            return same; // this one question goes back to the game
        }

        private static void Refuse(string why)
        {
            refused = true;
            Plugin.Log.LogWarning($"Sight lane: {why}. The game does its own looking for the rest of this session.");
        }

        /// <summary>GetVisibleEnemiesAt: a character counts as seen when any of five points on its capsule is. The middle is nearly always one, so the others are only fired for those it missed.</summary>
        private static List<Character> Enemies(AI_Manager m, Vector3 pos, float distance, Character omit)
        {
            who.Clear();
            bodies.Clear();
            aims.Clear();
            foreach (AI ai in Ais(m))
            {
                if (ai == null) continue;
                Character c = ai.GetCharacter();
                if (c == null || c == omit || ai.GetIsDead()) continue;
                AI.AI_TEAM team = ai.GetTeam();
                if (team == AI.AI_TEAM.Player || team == AI.AI_TEAM.Passive || Vector3.Distance(ai.transform.position, pos) > distance) continue;
                CapsuleCollider body = c.gameObject.GetComponent<CapsuleCollider>();
                who.Add(c);
                bodies.Add(body);
                aims.Add(body != null ? Middle(c, body) : pos); // no capsule is never seen: an aim at the ray's own start hits nothing
            }
            if (!Cast(pos)) return null;
            var seen = new bool[who.Count];
            unseen.Clear();
            for (int i = 0; i < who.Count; i++)
            {
                seen[i] = Struck(i, i);
                if (!seen[i] && bodies[i] != null) unseen.Add(i);
            }
            aims.Clear();
            foreach (int i in unseen) AddEdges(who[i], bodies[i]);
            if (!Cast(pos)) return null;
            for (int u = 0; u < unseen.Count; u++)
                for (int ray = u * 4; ray < u * 4 + 4 && !seen[unseen[u]]; ray++)
                    seen[unseen[u]] = Struck(ray, unseen[u]);
            var list = new List<Character>();
            for (int i = 0; i < who.Count; i++)
                if (seen[i]) list.Add(who[i]);
            return list;
        }

        private static bool Struck(int ray, int character)
        {
            Collider hit = results[ray].collider;
            return hit != null && hit.GetComponent<Character>() == who[character];
        }

        private static Vector3 Middle(Character c, CapsuleCollider body) => c.transform.position + body.center * c.transform.localScale.y;

        private static void AddEdges(Character c, CapsuleCollider body)
        {
            Vector3 middle = Middle(c, body), scale = c.transform.localScale;
            float across = body.radius * scale.x, cap = body.radius * scale.y, tall = body.height > body.radius ? body.height * 0.5f * scale.y : 0f;
            // the game's sums in the game's order, so the same bits
            aims.Add(middle + new Vector3(0f, cap, 0f) + new Vector3(0f, tall, 0f));
            aims.Add(middle - new Vector3(0f, cap, 0f) - new Vector3(0f, tall, 0f));
            aims.Add(middle - new Vector3(across, 0f, 0f));
            aims.Add(middle + new Vector3(across, 0f, 0f));
        }

        /// <summary>GetVisibleCharactersAt and GetVisibleAlliesAt: one ray each, you first when you count. A team of null takes everyone.</summary>
        private static List<Character> Lines(AI_Manager m, Vector3 pos, float distance, bool you, AI.AI_TEAM? team)
        {
            who.Clear();
            aims.Clear();
            Player player = Player.instance;
            if (you)
            {
                who.Add(player);
                aims.Add(player.GetHitFx().position);
            }
            foreach (AI ai in Ais(m))
            {
                if (ai == null) continue;
                Character c = ai.GetCharacter();
                if (c == null || ai.GetIsDead() || team.HasValue && ai.GetTeam() != team.Value || Vector3.Distance(ai.transform.position, pos) > distance) continue;
                who.Add(c);
                aims.Add(ai.detection.transform.position);
            }
            if (!Cast(pos)) return null;
            var list = new List<Character>();
            for (int i = 0; i < who.Count; i++)
            {
                if (you && i == 0)
                {
                    Collider hit = results[0].collider;
                    if (hit != null && hit.GetComponent<Player>() != null && Vector3.Distance(hit.transform.position, pos) <= distance) list.Add(player);
                }
                else if (Struck(i, i)) list.Add(who[i]);
            }
            return list;
        }

        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.GetVisibleEnemiesAt), new[] { typeof(Vector3), typeof(float), typeof(Character) })]
        private static class Patch_Enemies
        {
            private static bool Prefix(AI_Manager __instance, Vector3 pos, float distance, Character omit, ref List<Character> __result)
            {
                if (!Ready) return true;
                try { __result = Enemies(__instance, pos, distance, omit); }
                catch (Exception e) { Refuse("it failed: " + e.Message); __result = null; }
                return __result == null;
            }
        }

        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.GetVisibleCharactersAt), new[] { typeof(Vector3), typeof(float), typeof(bool) })]
        private static class Patch_Characters
        {
            private static bool Prefix(AI_Manager __instance, Vector3 pos, float distance, bool omitPlayer, ref List<Character> __result)
            {
                if (!Ready) return true;
                try { __result = Lines(__instance, pos, distance, !omitPlayer, null); }
                catch (Exception e) { Refuse("it failed: " + e.Message); __result = null; }
                return __result == null;
            }
        }

        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.GetVisibleAlliesAt), new[] { typeof(Vector3), typeof(float), typeof(AI.AI_TEAM) })]
        private static class Patch_Allies
        {
            private static bool Prefix(AI_Manager __instance, Vector3 pos, float distance, AI.AI_TEAM alliedTeam, ref List<Character> __result)
            {
                if (!Ready || Ability.DEBUG_animTest) return true;
                try { __result = Lines(__instance, pos, distance, alliedTeam == AI.AI_TEAM.Player || alliedTeam == AI.AI_TEAM.Confused, alliedTeam); }
                catch (Exception e) { Refuse("it failed: " + e.Message); __result = null; }
                return __result == null;
            }
        }
    }
}
