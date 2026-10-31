using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>One movement habit of a creature or mod enemy ("behaviors" in its data).</summary>
    public class BehaviorSpec
    {
        /// <summary>kite | retreat | guard | hitAndRun</summary>
        public string kind;
        /// <summary>kite: step away when a foe is this close (tiles).</summary>
        public float keep = 3f;
        /// <summary>retreat: back off while health is below this percent.</summary>
        public int below = 40;
        /// <summary>guard: walk back toward its owner when farther than this.</summary>
        public float leash = 4f;
        /// <summary>Its turns before the habit can kick in again (0 = every turn).</summary>
        public int cooldown;
    }

    /// <summary>
    /// Movement habits that run after a creature's own turn, so a summon or enemy can fight and then reposition:
    /// kite (drift away from foes that come close), retreat (back off when badly hurt), guard (stay near its owner) and
    /// hitAndRun (dart in, strike, and spring straight back to where it started the turn).
    /// </summary>
    internal static class Behaviors
    {
        private static readonly Dictionary<(int, string), int> readyAt = new Dictionary<(int, string), int>();

        [HarmonyPatch(typeof(AI), nameof(AI.TakeTurn))]
        private static class Patch_Turn
        {
            [HarmonyPriority(Priority.Low)]
            private static void Postfix(AI __instance, ref IEnumerator __result)
            {
                Character c = __instance != null ? __instance.GetCharacter() : null;
                List<BehaviorSpec> bs = EnemyRegistry.DefOf(c)?.behaviors;
                if (bs == null || bs.Count == 0 || __result == null) return;
                __result = Wrap(__instance, c, __result, bs);
            }
        }

        private static IEnumerator Wrap(AI ai, Character c, IEnumerator turn, List<BehaviorSpec> bs)
        {
            NavNode start = c.GetMover()?.GetCurrentNavNode();
            yield return turn;
            if (c == null || c.GetIsDead() || c.GetMover() == null || !c.FLAG_canMove || c.FLAG_isRooted) yield break;
            Ability used = Traverse.Create(ai).Field("chosenAbility").GetValue<Ability>();
            foreach (BehaviorSpec b in bs)
            {
                string kind = (b.kind ?? "").ToLowerInvariant();
                var key = (c.GetInstanceID(), kind);
                if (readyAt.TryGetValue(key, out int at) && ProcHub.TurnCount < at) continue;
                bool acted = false;
                NavNode step = null; // where kite, retreat and guard walk to
                switch (kind)
                {
                    case "kite":
                        if (Nearest(c, out float d) != null && d <= b.keep) step = Away(c);
                        acted = step != null;
                        break;
                    case "retreat":
                        if (c.GetHealth() * 100 < b.below * Mathf.Max(1, c.GetHealthMax())) step = Away(c);
                        acted = step != null;
                        break;
                    case "guard":
                        {
                            Character owner = c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Player ? Player.instance : null;
                            NavNode home = owner?.GetMover()?.GetCurrentNavNode();
                            if (home != null && Vector3.Distance(c.transform.position, owner.transform.position) > b.leash) step = home;
                            acted = step != null;
                            break;
                        }
                    case "hitandrun":
                        {
                            NavNode here = c.GetMover().GetCurrentNavNode();
                            if (used != null && start != null && here != null && here != start)
                            {
                                // its own tile, or the free one beside it nearest to it when something stepped in
                                NavNode home = Free(start) ? start : start.GetLinks().Where(Free).OrderBy(n => (n.transform.position - start.transform.position).sqrMagnitude).FirstOrDefault();
                                if (home != null && home != here) { Snap(c, here, home); acted = true; }
                            }
                            break;
                        }
                    default: Plugin.Log.LogWarning($"{c.GetName()}: unknown behavior '{b.kind}' (kite, retreat, guard, hitAndRun)"); break;
                }
                if (step != null)
                {
                    IEnumerator walk = Traverse.Create(ai).Method("DoPursuit", step).GetValue<IEnumerator>();
                    if (walk != null) yield return ai.StartCoroutine(walk);
                }
                if (acted)
                {
                    if (b.cooldown > 0) readyAt[key] = ProcHub.TurnCount + b.cooldown;
                    break; // one habit a turn
                }
            }
        }

        private static List<Character> Foes(Character c) => EnemyAbilities.FoesOf(c).Where(f => c.ai == null || c.ai.CanSee(f)).ToList();

        private static Character Nearest(Character c, out float dist)
        {
            dist = float.MaxValue;
            Character best = null;
            foreach (Character f in Foes(c))
            {
                float d = Vector3.Distance(f.transform.position, c.transform.position);
                if (d < dist) { dist = d; best = f; }
            }
            return best;
        }

        /// <summary>The free tile beside it that puts the most room between it and the nearest foe, if that helps.</summary>
        private static NavNode Away(Character c)
        {
            NavNode here = c.GetMover().GetCurrentNavNode();
            List<Character> foes = Foes(c);
            if (here == null || foes.Count == 0) return null;
            float Room(Vector3 p) => foes.Min(f => Vector3.Distance(f.transform.position, p));
            float now = Room(here.transform.position);
            NavNode best = here.GetLinks().Where(n => n != null && !n.IsOccupied() && !n.BlockCheck() && !n.Block_Move && !n.HasDoor())
                .OrderByDescending(n => Room(n.transform.position)).FirstOrDefault();
            return best != null && Room(best.transform.position) > now + 0.2f ? best : null;
        }

        private static bool Free(NavNode n) => n != null && !n.IsOccupied() && !n.BlockCheck() && !n.Block_Move && !n.HasDoor();

        /// <summary>Back to `to` at once with a dash streak, like a wisp darting home after a strike.</summary>
        private static void Snap(Character c, NavNode from, NavNode to)
        {
            Actions.SpawnVfx("CYBERNINJA_DASH.particle_dash", c.GetHitFx().position);
            from.ClearOccupant();
            c.GetMover().transform.position = to.transform.position;
            c.GetMover().SetCurrentNavNode(to);
            to.OnEnterNode(c);
            to.SetOccupant(c);
        }

        internal static string Describe(List<BehaviorSpec> bs) => bs == null ? "" : string.Join(", ", bs.Select(b => b.kind));
    }
}
