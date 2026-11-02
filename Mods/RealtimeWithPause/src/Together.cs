// An example to start your own plugin from: use, copy and change it for any purpose, with no conditions and no credit needed.

using System.Collections;
using System.Collections.Generic;
using Breachwork;
using HarmonyLib;
using UnityEngine;

namespace RealtimeWithPause
{
    /// <summary>
    /// Nobody waits for anybody. Vanilla plays a round: your action, then every AI down the list. While the clock runs, the
    /// game's own AI phase is left with an empty list, so a turn takes only as long as your action, and every AI gets a timer of
    /// its own instead, spread at random over the tick. When its timer is up it takes its usual turn through DoAiTurn, alone
    /// and in the background, whatever you or anyone else is doing. Its cooldowns and effects count its own turns; yours count
    /// yours, and the world's (hazards, corpses, the countdown out of combat) go on the plugin's tick.
    /// </summary>
    internal static class Together
    {
        private sealed class Running
        {
            internal AI ai;
            internal bool done;
            internal float began;
        }

        // a turn this old lost its coroutine (its AI was switched off or stopped), so it can't be waited for
        private const float LostAfter = 30f;

        // the crowd's timers come up faster than a frame can start them, so a big fight stretches rather than stops
        private const double StartingMsPerFrame = 8.0;
        private static readonly System.Diagnostics.Stopwatch starting = new System.Diagnostics.Stopwatch();

        private static readonly AccessTools.FieldRef<AI_Manager, List<AI>> Ais = AccessTools.FieldRefAccess<AI_Manager, List<AI>>("ais");
        private static readonly AccessTools.FieldRef<AI_Manager, List<AI>> AisFast = AccessTools.FieldRefAccess<AI_Manager, List<AI>>("ais_fast");
        private static readonly AccessTools.FieldRef<AI_Manager, List<Corpse>> Corpses = AccessTools.FieldRefAccess<AI_Manager, List<Corpse>>("corpses");

        private static readonly Dictionary<AI, Running> running = new Dictionary<AI, Running>();
        private static readonly List<AI> everyone = new List<AI>(), justThisOne = new List<AI>();
        private static readonly List<Corpse> noCorpses = new List<Corpse>();
        private static readonly Dictionary<AI, float> due = new Dictionary<AI, float>();
        private static float clock;
        private static bool oneAi, sawYou, fightBegins = true;

        internal static bool On => Api.SimultaneousTurns;

        /// <summary>Whether any AI is still in the middle of its turn.</summary>
        internal static bool Busy
        {
            get
            {
                foreach (Running r in running.Values)
                    if (!r.done && r.ai != null && r.ai.gameObject.activeInHierarchy) return true;
                return false;
            }
        }

        internal static void Reset()
        {
            due.Clear();
            running.Clear();
            sawYou = false;
            FightOver();
        }

        /// <summary>
        /// The next run is a new fight. Whatever started it noticed you, and not every AI will have had its turn by the world's
        /// first tick, as they have in a round, so that tick mustn't find nobody looking and call it off.
        /// </summary>
        internal static void FightOver() => fightBegins = true;

        /// <summary>Called every frame the world is running: starts the turn of every AI whose time has come.</summary>
        internal static void Run(float deltaTime, float tick)
        {
            AI_Manager am = AI_Manager.instance;
            if (am == null || Player.instance == null) return;
            if (fightBegins) sawYou = true;
            fightBegins = false;
            clock += deltaTime;
            everyone.Clear();
            everyone.AddRange(Ais(am));
            starting.Restart();
            foreach (AI ai in everyone)
            {
                // those left over keep their place: a timer that's up stays up until there's room
                if (starting.Elapsed.TotalMilliseconds >= StartingMsPerFrame) break;
                if (ai == null || ai.GetCharacter() == null || ai.GetCharacter().GetIsDead()) continue;
                if (!due.TryGetValue(ai, out float at))
                {
                    due[ai] = clock + Random.Range(0f, tick);
                    continue;
                }
                if (clock < at || Player.instance.GetIsDead()) continue;
                bool quick = ai.GetCharacter().HasTag("FAST") || ai.GetCharacter().HasTag("TEMP_FAST");
                due[ai] = clock + tick * Random.Range(0.8f, 1.2f) * (quick ? 0.5f : 1f);
                if (running.TryGetValue(ai, out Running r) && !r.done && clock - r.began < LostAfter) continue; // still busy: its next one waits a tick
                TakeTurnAlone(am, ai);
            }
        }

        /// <summary>
        /// The game's own AI phase with a list of one, so everything it does for an AI (effects, the slow and fast tags, minions
        /// catching up, spotting you) still happens. The turn itself leaves through <see cref="Launch"/>, so this takes no time.
        /// </summary>
        private static void TakeTurnAlone(AI_Manager am, AI ai)
        {
            List<AI> all = Ais(am);
            List<Corpse> corpses = Corpses(am);
            int countdown = am.outOfCombatTimer;
            justThisOne.Clear();
            justThisOne.Add(ai);
            noCorpses.Clear();
            Ais(am) = justThisOne;
            Corpses(am) = noCorpses;
            // The phase counts down when nobody saw you and ends the fight at zero. That's the world's to do: one far-off AI mustn't, so it's given a step to spare.
            am.outOfCombatTimer = Mathf.Max(countdown, 1) + 1;
            oneAi = true;
            try
            {
                IEnumerator phase = am.DoAiTurn(isFast: false);
                while (phase.MoveNext()) { }
            }
            finally
            {
                oneAi = false;
                Ais(am) = all;
                Corpses(am) = corpses;
                AisFast(am).Clear();
                if (am.sawPlayerThisTurn) sawYou = true;
                else am.outOfCombatTimer = countdown;
            }
        }

        /// <summary>The world's part of the game's AI phase: corpses and the countdown, with no AI in it.</summary>
        private static IEnumerator WorldOnly(AI_Manager am, IEnumerator phase, bool isFast)
        {
            List<AI> all = Ais(am), fast = AisFast(am);
            Ais(am) = new List<AI>();
            AisFast(am) = new List<AI>();
            if (sawYou) am.outOfCombatTimer = 8;
            oneAi = true;
            try { while (phase.MoveNext()) { } }
            finally
            {
                oneAi = false;
                Ais(am) = all;
                AisFast(am) = fast;
            }
            if (sawYou && isFast)
            {
                am.outOfCombatTimer = 8;
                sawYou = false;
            }
            yield break;
        }

        /// <summary>Starts one AI's turn beside the others. A target still stepping would hold the AI up until it lands, so it isn't told.</summary>
        private static IEnumerator Launch(AI ai, IEnumerator turn)
        {
            Character target = ai.GetTarget();
            Character_Mover mover = target != null ? target.GetMover() : null;
            bool moving = mover != null && mover.isMoving;
            if (moving) mover.isMoving = false;
            var r = new Running { ai = ai, began = clock };
            running[ai] = r;
            ai.StartCoroutine(Drive(r, turn));
            if (moving) mover.isMoving = true;
            yield break;
        }

        private static IEnumerator Drive(Running r, IEnumerator turn)
        {
            while (true)
            {
                if (r.ai != null) Game.LAST_CHARACTER = r.ai.GetCharacter(); // who gets the credit for what lands now
                if (!turn.MoveNext()) break;
                yield return turn.Current;
            }
            r.done = true;
            if (r.ai != null && running.TryGetValue(r.ai, out Running now) && now == r) running.Remove(r.ai);
        }

        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.DoAiTurn))]
        private static class Patch_DoAiTurn
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(AI_Manager __instance, bool isFast, ref IEnumerator __result)
            {
                if (On && !oneAi) __result = WorldOnly(__instance, __result, isFast);
            }
        }

        [HarmonyPatch(typeof(AI), nameof(AI.TakeTurn))]
        private static class Patch_TakeTurn
        {
            // Outermost, around the loader's pacing wrapper.
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(AI __instance, ref IEnumerator __result)
            {
                if (On) __result = Launch(__instance, __result);
            }
        }
    }
}
