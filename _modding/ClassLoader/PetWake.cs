using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Pets join the fight. A vanilla pet left idle does look around, but only a hostile mob acts on what it spots, so
    /// a pet (or hired help, or a converted enemy) stands beside a fight until something hits it. Now, during combat, an
    /// idle companion on its own judgement (Engage) goes for the nearest enemy it can see, or one that is fighting you
    /// or your side within WakeRange. Out of combat they still leave sleeping mobs alone, so sneaking past works.
    /// </summary>
    internal static class PetWake
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Range;
        internal static bool Trace;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Minions", "JoinFights", true,
                "During combat your idle pets, summons and hired help go for the nearest enemy they can see, or one fighting you or your side within JoinRange, instead of waiting to be hit first.");
            Range = cfg.Bind("Minions", "JoinRange", 10, new ConfigDescription(
                "How far (in tiles) an idle companion looks for an enemy fighting your side.", new AcceptableValueRange<int>(3, 30)));
        }

        /// <summary>Not you, alive, on your side and waiting on its own judgement.</summary>
        private static bool Waiting(AI ai, Character c) =>
            c != null && c != Player.instance && !c.GetIsDead() && ai.GetTeam() == AI.AI_TEAM.Player && c.FLAG_canAttack
            && (ai.GetState() == AI.AI_STATE.Idle || (ai.GetState() == AI.AI_STATE.Search && ai.GetTarget() == null && ai.targetCharacterLastNode == null))
            && Commands.OrderOf(c) == Commands.Order.Engage && !Commands.OnErrand(c);

        /// <summary>Fighting you or someone on your side (or right next to you).</summary>
        private static bool Fighting(Character e)
        {
            if (e.ai.GetState() != AI.AI_STATE.Active || Player.instance == null) return false;
            Character t = e.ai.GetTarget();
            if (t == Player.instance || (t != null && t.ai != null && t.ai.GetTeam() == AI.AI_TEAM.Player)) return true;
            return Vector3.Distance(e.transform.position, Player.instance.transform.position) <= 3f;
        }

        private static bool SameArea(Character a, Character b)
        {
            Room ra = a.GetMover()?.GetCurrentNavNode()?.GetRoom();
            Room rb = b.GetMover()?.GetCurrentNavNode()?.GetRoom();
            return ra != null && rb != null && (ra == rb || ra.GetLinkedRoomsContains(b));
        }

        /// <summary>The enemy to go for: the nearest one it sees, else the nearest one fighting your side in reach.</summary>
        internal static Character Pick(AI ai, Character c)
        {
            List<Character> all = AI_Manager.instance.GetPotentialTargets(c, AI.AI_TEAM.Player);
            if (all == null) return null;
            Character seen = null, heard = null;
            float seenD = float.PositiveInfinity, heardD = float.PositiveInfinity, reach = Range.Value;
            foreach (Character e in all)
            {
                if (!Commands.Hostile(e) || e.ai.isNeutral) continue; // neutral: quest mobs you can't target either
                float d = Vector3.Distance(c.transform.position, e.transform.position);
                if (d < seenD && ai.CanSee(e)) { seen = e; seenD = d; }
                else if (d < heardD && d <= reach && Fighting(e) && SameArea(c, e)) { heard = e; heardD = d; }
            }
            return seen ?? heard;
        }

        [HarmonyPatch(typeof(AI), nameof(AI.TakeTurn))]
        private static class Patch_Turn
        {
            [HarmonyPriority(Priority.Low)]
            private static void Prefix(AI __instance)
            {
                if (!Enabled.Value || !When.InCombat() || AI_Manager.instance == null) return;
                Character c = __instance.GetCharacter();
                if (!Waiting(__instance, c)) return;
                Character e = Pick(__instance, c);
                if (e == null) return;
                __instance.SetTarget(e);
                // out of sight it goes looking where the enemy is, not back to you
                __instance.targetCharacterLastNode = e.GetMover()?.GetCurrentNavNode();
                __instance.ChangeState(AI.AI_STATE.Active);
                if (Trace) Plugin.Log.LogInfo($"PetWake: {c.GetName()} joins in against {e.GetName()} #{e.GetInstanceID()} neutral={e.ai.isNeutral} ({(__instance.CanSee(e) ? "seen" : "heard")})");
            }
        }
    }
}
