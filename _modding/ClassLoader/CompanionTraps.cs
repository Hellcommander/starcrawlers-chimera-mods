using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// Your allies set off traps (only hostile and passive creatures are spared), and walked straight onto them. Now
    /// an ally's path treats a tile with a live trap or tripwire you can see (its room is discovered) as blocked, so
    /// it stops short or goes round. A crawler-like companion (hired crew, a Conjoined body) next to one sizes it up
    /// as a player would: when its own odds (the trap's base chance plus its Smarts, as on the disarm screen) are at
    /// least [Companions] DisarmAtChance, it tries once; failing sets the trap off on it, as it does on you.
    /// </summary>
    internal static class CompanionTraps
    {
        internal static ConfigEntry<bool> Avoid;
        internal static ConfigEntry<int> DisarmAt;

        /// <summary>Traps a companion has already tried this floor (trap instance id, companion unique id).</summary>
        private static readonly HashSet<(int, int)> tried = new HashSet<(int, int)>();

        internal static void Bind(ConfigFile cfg)
        {
            Avoid = cfg.Bind("Companions", "AvoidTraps", true, "Allies don't path onto live traps and tripwires you can see.");
            DisarmAt = cfg.Bind("Companions", "DisarmAtChance", 60,
                "Crew and Conjoined bodies next to a trap try to disarm it when their own chance is at least this (0-100; above 100 = never).");
        }

        internal static bool IsAlly(Character c) =>
            c != null && c != Player.instance && !c.GetIsDead() && c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Player;

        /// <summary>A trap that would go off on an ally stepping here, which you can see.</summary>
        internal static Trap_Trigger VisibleTrap(NavNode n)
        {
            if (n == null || n.hazards == null || n.hazards.Count == 0) return null;
            Room r = n.GetRoom();
            if (r != null && !r.discovered) return null;
            foreach (Hazard h in n.hazards)
                if (h is Trap_Trigger t && t != null && !t.GetTripped() && !(t is Trap_Trigger_Camera)) return t;
            return null;
        }

        [HarmonyPatch(typeof(WalkingPathfinder), "GetICost")]
        private static class Patch_Cost
        {
            private static void Postfix(WalkingPathfinder __instance, NavNode to, ref int __result)
            {
                if (!Avoid.Value || __result > 0 || to == null) return;
                AI ai = Traverse.Create(__instance).Field("_ai").GetValue<AI>();
                if (ai == null || !IsAlly(ai.GetCharacter())) return;
                if (VisibleTrap(to) != null || Traps.HurtsAllies(to)) __result = 1; // an extra cost of 1 ends the path before this tile
            }
        }

        /// <summary>The ally's own disarm odds: what the disarm screen would give you, with its stats instead of yours.</summary>
        internal static int Chance(Hazard h, Character c)
        {
            int chance = h.GetDisarmBaseChance();
            foreach (StatObj m in h.attributeMods ?? new StatObj[0])
                if (m != null && m.GetStatType() == STAT_TYPE.Smarts && m.amount > 1)
                    chance += c.GetStat(STAT_TYPE.Smarts) / (m.amount / 2);
            return Mathf.Clamp(chance, 0, 100);
        }

        /// <summary>Each crawler-like companion beside a seen trap weighs it up once; returns how many tried.</summary>
        internal static int Consider()
        {
            if (DisarmAt.Value > 100 || AI_Manager.instance == null || Game.Game_isLoading) return 0;
            int n = 0;
            foreach (Character c in AI_Manager.instance.GetPlayerMinions().Concat(Contracts.AllCharacters()).Distinct().Where(c => IsAlly(c) && Commands.IsCrewLike(c)).ToList())
            {
                NavNode at = c.GetMover()?.GetCurrentNavNode();
                if (at == null) continue;
                foreach (NavNode next in at.GetLinks())
                {
                    Trap_Trigger t = VisibleTrap(next);
                    if (t == null || !t.CanDisarm() || !tried.Add((t.GetInstanceID(), c.uniqueId))) continue;
                    int chance = Chance(t, c);
                    string trap = (string)t.hazardName;
                    if (chance < DisarmAt.Value)
                    {
                        Game.LOG($"{c.GetName()} eyes the {trap}: {chance}% to disarm it. Not worth the risk; it keeps clear.");
                        continue;
                    }
                    n++;
                    if (Random.Range(1, 101) <= chance)
                    {
                        Game.LOG($"<color=#9FE08A>{c.GetName()} disarms the {trap}</color> ({chance}%).");
                        AudioManager.PlayEvent("event:/Environment/Lockpick_Success", t.transform.position);
                        t.Disarm(giveParts: true);
                    }
                    else
                    {
                        Game.LOG($"<color=#FF9F5C>{c.GetName()} fumbles the {trap}</color> ({chance}%) and sets it off.");
                        t.Trigger(c, true); // as a failed disarm does to you
                    }
                    break; // one attempt per companion per turn
                }
            }
            return n;
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
        private static class Patch_Turn
        {
            private static void Postfix()
            {
                try { Consider(); }
                catch (Exception e) { Plugin.Log.LogWarning("Companion traps: " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(Map), nameof(Map.Init))]
        private static class Patch_NewMap
        {
            private static void Postfix() => tried.Clear();
        }
    }
}
