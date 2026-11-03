using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// The bodies the game leaves lying on a floor, as things a skill can use: "corpses" spends them (an explosion, a feast,
    /// spare parts) and the "corpses:R" value counts them. The game keeps them in AI_Manager's own list until they decay.
    /// </summary>
    internal static class Corpses
    {
        internal static List<Corpse> All() =>
            AI_Manager.instance == null ? new List<Corpse>() : Traverse.Create(AI_Manager.instance).Field("corpses").GetValue<List<Corpse>>() ?? new List<Corpse>();

        /// <summary>Corpses within r of a point, nearest first; ones already melting away don't count.</summary>
        internal static List<Corpse> Near(Vector3 at, float r) =>
            All().Where(c => c != null && !c.isDissolving && Flat(c.transform.position - at) <= r + 0.25f)
                 .OrderBy(c => Flat(c.transform.position - at)).ToList();

        private static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }

        /// <summary>
        /// "corpses": up to "amount" corpses (0 = every one) within "radius" of you (default 3). For each, the nested actions
        /// with "to": "self" run on you, and the rest on the enemies within "blastRadius" of that corpse, or on the skill's
        /// target when there's no blast. The corpses are used up unless "keepCorpses" is set; "vfx" plays on each.
        /// </summary>
        internal static void Use(ActionSpec a, ActionContext ctx, Character t)
        {
            Character me = ctx.caster;
            if (me == null || a.actions == null || AI_Manager.instance == null) return;
            var found = Near(me.transform.position, a.radius > 0f ? a.radius : 3f);
            if (a.amount > 0) found = found.Take(a.amount).ToList();
            if (found.Count == 0) return;
            var self = a.actions.Where(x => (x.to ?? "").ToLowerInvariant() == "self").ToList();
            var rest = a.actions.Where(x => (x.to ?? "").ToLowerInvariant() != "self").ToList();
            foreach (Corpse c in found)
            {
                Vector3 at = c.transform.position;
                var sub = Actions.Clone(ctx);
                sub.center = at;
                if (!string.IsNullOrEmpty(a.vfx)) Actions.SpawnVfx(a.vfx, at + Vector3.up * 0.4f);
                Actions.Run(self, sub, me);
                if (rest.Count > 0)
                {
                    var victims = a.blastRadius > 0f
                        ? AI_Manager.instance.GetEnemiesAt(at, a.blastRadius).Where(v => v != null && !v.GetIsDead()).ToList()
                        : t != null && t != me && !t.GetIsDead() ? new List<Character> { t } : new List<Character>();
                    HitLane.Each(victims, rest, sub);
                }
                if (a.keepCorpses) continue;
                AI_Manager.instance.RemoveCorpse(c);
                Object.Destroy(c.gameObject);
            }
            Game.LOG($"{ctx.sourceName} {(a.keepCorpses ? "draws on" : "uses up")} {found.Count} {(found.Count == 1 ? "corpse" : "corpses")}.");
        }
    }
}
