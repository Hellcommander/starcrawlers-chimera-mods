using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Diagonals for everyone else, and reach across a corner.
    ///
    /// Steps: the game's pathfinding walks the four tile links, so a path that turns goes A → B → C. Where C is across
    /// the corner from A and the corner is open by the same rule as yours (no wall, door or barrier on either tile beside
    /// it, not both of them occupied), an enemy or ally steps straight to C. Each diagonal adds 0.414 to that
    /// character's own stride debt; when it reaches a whole turn, its next move is spent catching up.
    ///
    /// Reach: a diagonal neighbour counts as next to you, for every side. Reach-1 abilities (melee) can be used on it,
    /// and the AI treats it as adjacent when it decides to strike, shoot point-blank or back off. Longer reaches already
    /// cover diagonals by distance. The corner rule applies: nobody reaches around a wall corner.
    /// </summary>
    internal static class DiagonalAI
    {
        internal static ConfigEntry<bool> Enemies, Allies, Reach;

        private const float Extra = 0.41421356f;

        internal static void Bind(ConfigFile cfg)
        {
            Enemies = cfg.Bind("Movement", "EnemyDiagonals", false, "Enemies step diagonally too: where their path turns a corner they cut across it, by the same corner rule as you, and each diagonal costs them 1.41 turns (now and then they lose a moment catching up).");
            Allies = cfg.Bind("Movement", "AllyDiagonals", false, "Your allies (companions, summons, pets) step diagonally too, by the same rules.");
            Reach = cfg.Bind("Movement", "DiagonalReach", false, "Anyone diagonally next to you is next to you: melee reaches across the corner, for you, your allies and enemies, and the AI counts it as adjacent when it decides to strike, shoot point-blank or back off. Not around a wall corner.");
        }

        // ---- stride debt, one per character ------------------------------------------------------------------

        private class Stride { public float debt; public bool caughtUp; }
        private static readonly ConditionalWeakTable<Character, Stride> strides = new ConditionalWeakTable<Character, Stride>();

        /// <summary>Tests: diagonal steps taken by the AI, and moments spent catching up.</summary>
        internal static int Steps, CatchUps;
        internal static float DebtOf(Character c) => c != null && strides.TryGetValue(c, out Stride s) ? s.debt : 0f;

        /// <summary>True once if this character just spent its move catching up (so an ally's order doesn't step anyway).</summary>
        internal static bool ConsumeCatchUp(Character c)
        {
            if (c == null || !strides.TryGetValue(c, out Stride s) || !s.caughtUp) return false;
            s.caughtUp = false;
            return true;
        }

        private static bool Allowed(AI ai) =>
            ai != null && (ai.GetTeam() == AI.AI_TEAM.Player ? Allies.Value : Enemies.Value);

        // ---- geometry ----------------------------------------------------------------------------------------

        /// <summary>The two tiles beside the corner between two diagonal neighbours, or false if they aren't diagonal neighbours.</summary>
        private static bool Corner(NavNode from, NavNode to, out NavNode a, out NavNode b, out Vector3 z, out Vector3 x)
        {
            a = b = null;
            z = x = Vector3.zero;
            if (from == null || to == null) return false;
            Vector3 d = to.transform.position - from.transform.position;
            if (Mathf.Abs(Mathf.Abs(d.x) - 1f) > 0.2f || Mathf.Abs(Mathf.Abs(d.z) - 1f) > 0.2f) return false;
            z = new Vector3(0f, 0f, Mathf.Sign(d.z));
            x = new Vector3(Mathf.Sign(d.x), 0f, 0f);
            a = Diagonal.Link(from, z);
            b = Diagonal.Link(from, x);
            NavNode viaA = Diagonal.Link(a, x), viaB = Diagonal.Link(b, z);
            return (viaA ?? viaB) == to && (viaA == null || viaB == null || viaA == viaB);
        }

        private static bool Clear(NavNode n) => n != null && !n.Block_Move && !n.BlockCheck() && !n.HasDoor();

        /// <summary>Can someone on `from` reach someone on `to` across the corner (a diagonal neighbour with no wall in the corner)?</summary>
        internal static bool Reaches(NavNode from, NavNode to) =>
            Reach.Value && Corner(from, to, out NavNode a, out NavNode b, out _, out _) && Clear(a) && Clear(b);

        /// <summary>A tile's four links, and its diagonal neighbours in reach when DiagonalReach is on.</summary>
        internal static List<NavNode> ReachLinks(NavNode n)
        {
            List<NavNode> l = n.GetLinks();
            if (!Reach.Value) return l;
            foreach (float zs in new[] { 1f, -1f })
                foreach (float xs in new[] { 1f, -1f })
                {
                    Vector3 z = new Vector3(0f, 0f, zs), x = new Vector3(xs, 0f, 0f);
                    NavNode d = Diagonal.Link(Diagonal.Link(n, z), x) ?? Diagonal.Link(Diagonal.Link(n, x), z);
                    if (d != null && !l.Contains(d) && Reaches(n, d)) l.Add(d);
                }
            return l;
        }

        /// <summary>May this character step from `from` straight to `to` across the corner?</summary>
        internal static bool CanCut(NavNode from, NavNode to, Character c)
        {
            if (!Corner(from, to, out NavNode a, out NavNode b, out Vector3 z, out Vector3 x)) return false;
            if (Diagonal.Target(from, z, x, out _) != to) return false;
            bool boss = c != null && c.HasTag("BOSS");
            foreach (NavNode side in new[] { a, b })
                if (side.Block_Mob || (boss && side.Block_Boss)) return false;
            return !to.IsOccupied();
        }

        // ---- into the game -----------------------------------------------------------------------------------

        // After Threading's batch paths (they serve FindPath) and before CompanionTraps (its postfix still checks our step).
        [HarmonyPatch(typeof(Pathfinder), nameof(Pathfinder.FindStep))]
        private static class Patch_Step
        {
            private static bool Prefix(Pathfinder __instance, NavNode from, NavNode to, List<NavNode> availableNodes, AI ____ai, ref NavNode __result)
            {
                if (!(__instance is WalkingPathfinder) || !Allowed(____ai)) return true;
                Character c = ____ai.GetCharacter();
                if (c == null || from == null) return true;
                Stride s = strides.GetOrCreateValue(c);
                s.caughtUp = false;
                if (s.debt >= 1f)
                {
                    s.debt -= 1f;
                    s.caughtUp = true;
                    CatchUps++;
                    __result = null;
                    return false;
                }
                List<NavNode> path = __instance.FindPath(from, to, availableNodes);
                __result = path.Count > 0 ? path[0] : null;
                if (path.Count >= 2 && path[0] != null && path[1] != null && !path[0].HasDoor() && CanCut(from, path[1], c))
                {
                    __result = path[1];
                    s.debt += Extra;
                    Steps++;
                }
                return false;
            }
        }

        /// <summary>Tests: how many adjacency checks in the AI's turn now see diagonal neighbours.</summary>
        internal static int ReachSites;

        // Every "is my target next to me" in the AI's turn (strike, shoot point-blank, back off) asks GetLinks().Contains.
        [HarmonyPatch]
        private static class Patch_TurnReach
        {
            private static MethodBase TargetMethod() => AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(AI), nameof(AI.TakeTurn)));

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
            {
                MethodInfo links = AccessTools.Method(typeof(NavNode), nameof(NavNode.GetLinks));
                MethodInfo reach = AccessTools.Method(typeof(DiagonalAI), nameof(ReachLinks));
                int n = 0;
                foreach (CodeInstruction ci in code)
                {
                    if (ci.Calls(links)) { ci.opcode = OpCodes.Call; ci.operand = reach; n++; }
                    yield return ci;
                }
                ReachSites = n;
            }
        }

        // Reach-1 abilities (melee) on a diagonal neighbour. Longer reaches already include it by distance (1.41).
        private static bool ShortReach(Ability a) => a != null && a.range > 0.5f && a.range < 1.4f;

        [HarmonyPatch(typeof(Ability), nameof(Ability.CanUseOn), new[] { typeof(Character) })]
        private static class Patch_CanUseOn
        {
            private static void Postfix(Ability __instance, Character c, Character ___caster, ref bool __result)
            {
                if (__result || !ShortReach(__instance) || c == null || c.GetMover() == null || ___caster == null || ___caster.GetMover() == null) return;
                if (Reaches(___caster.GetMover().GetCurrentNavNode(), c.GetMover().GetCurrentNavNode())) __result = true;
            }
        }

        [HarmonyPatch(typeof(Ability), nameof(Ability.RangeCheck), new[] { typeof(NavNode) })]
        private static class Patch_RangeCheck
        {
            private static void Postfix(Ability __instance, NavNode n, Character ___caster, ref bool __result)
            {
                if (__result || !ShortReach(__instance) || n == null || ___caster == null || ___caster.GetMover() == null) return;
                if (Reaches(___caster.GetMover().GetCurrentNavNode(), n)) __result = true;
            }
        }
    }
}
