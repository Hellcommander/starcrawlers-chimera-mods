// An example to start your own plugin from: use, copy and change it for any purpose, with no conditions and no credit needed.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        }

        private static readonly AccessTools.FieldRef<AI_Manager, List<AI>> Ais = AccessTools.FieldRefAccess<AI_Manager, List<AI>>("ais");
        private static readonly AccessTools.FieldRef<AI_Manager, List<AI>> AisFast = AccessTools.FieldRefAccess<AI_Manager, List<AI>>("ais_fast");
        private static readonly AccessTools.FieldRef<AI_Manager, List<Corpse>> Corpses = AccessTools.FieldRefAccess<AI_Manager, List<Corpse>>("corpses");

        private static readonly List<Running> running = new List<Running>();
        private static readonly Dictionary<AI, float> due = new Dictionary<AI, float>();
        private static float clock;
        private static bool oneAi, sawYou;

        internal static bool On => Api.SimultaneousTurns;

        /// <summary>Whether any AI is still in the middle of its turn.</summary>
        internal static bool Busy
        {
            get
            {
                running.RemoveAll(r => r.done || r.ai == null || !r.ai.gameObject.activeInHierarchy);
                return running.Count > 0;
            }
        }

        internal static void Reset()
        {
            due.Clear();
            running.Clear();
            sawYou = false;
        }

        /// <summary>Called every frame the world is running: starts the turn of every AI whose time has come.</summary>
        internal static void Run(float deltaTime, float tick)
        {
            AI_Manager am = AI_Manager.instance;
            if (am == null || Player.instance == null) return;
            clock += deltaTime;
            foreach (AI ai in Ais(am).ToList())
            {
                if (ai == null || ai.GetCharacter() == null || ai.GetCharacter().GetIsDead()) continue;
                if (!due.TryGetValue(ai, out float at))
                {
                    due[ai] = clock + Random.Range(0f, tick);
                    continue;
                }
                if (clock < at || Player.instance.GetIsDead()) continue;
                bool quick = ai.GetCharacter().HasTag("FAST") || ai.GetCharacter().HasTag("TEMP_FAST");
                due[ai] = clock + tick * Random.Range(0.8f, 1.2f) * (quick ? 0.5f : 1f);
                if (running.Any(r => r.ai == ai && !r.done)) continue; // still busy: its next one waits a tick
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
            Ais(am) = new List<AI> { ai };
            Corpses(am) = new List<Corpse>();
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
            }
            if (am.sawPlayerThisTurn) sawYou = true;
            else am.outOfCombatTimer = countdown;
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
            var r = new Running { ai = ai };
            running.Add(r);
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
