using System.Collections;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// "horror": brings in The Horror, the Cursed class's stalker, for a few turns. A coin toss (or "anySide": false, always
    /// yours) decides whether it hunts you or your enemies. It gets its own body rather than the game's single Horror, so
    /// a Cursed player's own one is left alone, and it is never saved: it can't outlive its turns.
    ///   { "do": "horror", "turns": 6, "anySide": true, "actions": [...] }
    /// Each turn it drifts two tiles at its quarry, through anything, and runs its actions on the quarry when it reaches it.
    /// </summary>
    internal static class Horrors
    {
        private const float Reach = 1.2f, Stride = 2f;

        private class Horror
        {
            public ActionSpec spec;
            public ActionContext ctx;
            public GameObject body;
            public bool huntsYou;
            public int life;
            public Map map;
        }

        private static Horror live;

        internal static bool Out => live != null;
        internal static bool HuntsYou => live != null && live.huntsYou;
        internal static int Strikes; // devtests
        internal static GameObject Body => live?.body;

        internal static void Launch(ActionSpec a, ActionContext ctx, Character t)
        {
            HazardManager hm = HazardManager.instance;
            if (live != null || hm == null || hm.horrorPrefab == null || ctx.caster == null || Map.instance == null) return;
            bool huntsYou = a.anySide && (Summons.ForceLoose ?? Random.value < 0.5f);
            Vector3 near = (t != null ? t : ctx.caster).transform.position;
            var spots = Map.instance.GetNavNodesAt(near, 3.5f, validNodesOnly: true).Where(n => n != null && Vector3.Distance(n.transform.position, near) > 1.5f).ToList();
            Vector3 at = spots.Count > 0 ? spots[Random.Range(0, spots.Count)].transform.position : near;
            Hazard_Cursed_Horror body = Object.Instantiate(hm.horrorPrefab, at, Quaternion.identity);
            body.enabled = false; // the game's own version only ever walks at you
            live = new Horror
            {
                spec = a, body = body.gameObject, huntsYou = huntsYou, life = System.Math.Max(1, a.turns), map = Map.instance,
                ctx = new ActionContext
                {
                    caster = ctx.caster, source = ctx.source, classId = ctx.classId, sourceName = ctx.sourceName, rank = ctx.rank,
                    damageMult = ctx.damageMult, chanceBonus = ctx.chanceBonus, backfire = true,
                },
            };
            Game.LOG(huntsYou ? "<color=#FF6B6B>The Horror</color> comes through, and it has noticed you." : "<color=#7CFFD0>The Horror</color> comes through, and it turns on your enemies.");
            Plugin.Instance.StartCoroutine(Step(live));
        }

        /// <summary>Each player turn: it closes in, or goes back where it came from.</summary>
        internal static void Tick()
        {
            if (live == null) return;
            if (live.body == null || live.map != Map.instance || --live.life <= 0) { Kill(); return; }
            Plugin.Instance.StartCoroutine(Step(live));
        }

        private static Character Quarry(Horror h)
        {
            if (h.huntsYou) return h.ctx.caster;
            Vector3 at = h.body.transform.position;
            return AI_Manager.instance?.GetEnemiesAt(at, 30f).Where(c => c != null && !c.GetIsDead() && c != h.ctx.caster && !TileStacks.IsAlly(c))
                .OrderBy(c => Vector3.Distance(c.transform.position, at)).FirstOrDefault();
        }

        private static IEnumerator Step(Horror h)
        {
            Character q = Quarry(h);
            if (q == null) yield break;
            Vector3 from = h.body.transform.position, to = Vector3.MoveTowards(from, q.transform.position, Stride);
            for (float k = 0f; k < 1f && h.body != null; k += Time.deltaTime)
            {
                h.body.transform.position = Vector3.Lerp(from, to, k);
                yield return null;
            }
            if (h.body == null || q == null || q.GetIsDead()) yield break;
            h.body.transform.position = to;
            if (Vector3.Distance(to, q.transform.position) > Reach) yield break;
            Strikes++;
            ScreenShake.GetInstance().Shake(0.1f, 3f);
            Actions.ProcDepth++;
            try { h.ctx.target = q; h.ctx.center = q.transform.position; Actions.Run(h.spec.actions, h.ctx, q); }
            finally { Actions.ProcDepth--; }
        }

        private static void Kill()
        {
            if (live?.body != null)
            {
                Actions.SpawnVfx("PA_Psykonics_Temporal.castParticle@0.6", live.body.transform.position + Vector3.up);
                Object.Destroy(live.body);
                Game.LOG("The Horror is gone.");
            }
            live = null;
        }

        internal static string Describe(ActionSpec a, string classId) =>
            $"The Horror comes through for {System.Math.Max(1, a.turns)} turns{(a.anySide ? ", hunting either you or your enemies" : " and hunts your enemies")}; when it reaches its quarry:\n" +
            Actions.Describe(a.actions, classId, "it");
    }
}
