using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// "drifter": a loose spell that leaves the caster toward the target and then wanders the deck on its own, a few
    /// tiles a turn, mostly keeping its heading and now and then veering at random. On every tile it enters it may go
    /// off (stepChance %), running its actions on everyone on or beside that tile: foes, allies and you alike.
    ///   { "do": "drifter", "fieldName": "Loose Spell", "vfx": "fx_x_drifter", "turns": 4, "steps": 2,
    ///     "stepChance": 50, "radius": 1.1, "actions": [...] }
    /// Its look is a mod particle effect played on a loop, so give it steady layers (streams, no flashes).
    /// Drifters fizzle when you leave the map; they are not saved.
    /// </summary>
    internal static class Drifters
    {
        private class Drifter
        {
            public ActionSpec spec;
            public ActionContext ctx;
            public NavNode node;
            public Vector3 heading;
            public int life;
            public GameObject look;
            public Map map;
            public bool busy;
        }

        private static readonly List<Drifter> live = new List<Drifter>();

        /// <summary>The last tiles a drifter went off on, with who it caught ("Loose Spell: 2"), for tests.</summary>
        internal static readonly List<string> Bursts = new List<string>();

        internal static int Count => live.Count(d => d.map == Map.instance);
        internal static int CountNamed(string name) => live.Count(d => d.map == Map.instance && d.ctx.sourceName == name);

        internal static void Launch(ActionSpec a, ActionContext ctx, Character t)
        {
            Character caster = ctx.caster;
            NavNode start = caster?.GetMover()?.GetCurrentNavNode();
            if (start == null || Map.instance == null) return;
            Vector3 heading = t != null && t != caster ? t.transform.position - caster.transform.position : Random.insideUnitSphere;
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.01f) heading = Vector3.forward;
            var d = new Drifter
            {
                spec = a, node = start, heading = heading.normalized, life = Mathf.Max(1, a.turns), map = Map.instance,
                ctx = new ActionContext
                {
                    caster = caster, source = ctx.source, classId = ctx.classId, sourceName = a.fieldName ?? ctx.sourceName,
                    rank = ctx.rank, damageMult = ctx.damageMult * Actions.LevelMult(a.levelCap), chanceBonus = ctx.chanceBonus, backfire = true,
                },
            };
            d.look = Look(a.vfx, start.transform.position + Vector3.up * 0.9f);
            live.Add(d);
            Game.LOG($"<color=#E59CFF>{d.ctx.sourceName}</color> spins loose and starts to wander.");
            Plugin.Instance.StartCoroutine(Walk(d, Steps(a)));
        }

        private static int Steps(ActionSpec a) => Mathf.Max(1, a.steps > 0 ? a.steps : 2);

        /// <summary>Each player turn: every drifter on this map moves on; spent ones fizzle.</summary>
        internal static void Tick()
        {
            foreach (Drifter d in live.ToList())
            {
                if (d.map != Map.instance || d.look == null) { Kill(d); continue; }
                if (--d.life <= 0) { Kill(d); continue; }
                if (!d.busy) Plugin.Instance.StartCoroutine(Walk(d, Steps(d.spec)));
            }
        }

        private static IEnumerator Walk(Drifter d, int steps)
        {
            d.busy = true;
            for (int i = 0; i < steps && live.Contains(d); i++)
            {
                NavNode next = Next(d);
                if (next == null) break;
                Vector3 from = d.node.transform.position, to = next.transform.position;
                d.heading = Vector3.Slerp(d.heading, (to - from).normalized, 0.6f).normalized;
                d.node = next;
                for (float k = 0f; k < 1f && d.look != null; k += Time.deltaTime / 0.28f)
                {
                    d.look.transform.position = Vector3.Lerp(from, to, k) + Vector3.up * (0.9f + Mathf.Sin(k * Mathf.PI) * 0.15f);
                    yield return null;
                }
                if (d.look == null) break;
                d.look.transform.position = to + Vector3.up * 0.9f;
                if (Random.Range(1, 101) <= (d.spec.stepChance > 0 ? d.spec.stepChance : 50)) GoOff(d, to);
                yield return new WaitForSeconds(0.08f);
            }
            d.busy = false;
        }

        /// <summary>The next tile: mostly the open neighbour most along its heading, sometimes any open neighbour.</summary>
        private static NavNode Next(Drifter d)
        {
            var open = d.node.GetLinks().Where(n => n != null && !n.Block_Move && !n.HasDoor()).ToList();
            if (open.Count == 0) return null;
            if (Random.value < 0.3f) return open[Random.Range(0, open.Count)];
            Vector3 here = d.node.transform.position;
            return open.OrderByDescending(n => Vector3.Dot((n.transform.position - here).normalized, d.heading) + Random.Range(0f, 0.35f)).First();
        }

        private static void GoOff(Drifter d, Vector3 at)
        {
            float r = d.spec.radius > 0f ? d.spec.radius : 1.1f;
            var caught = new List<Character>();
            foreach (NavNode n in Map.instance.GetNavNodesAt(at, r))
            {
                Character c = n?.GetOccupant();
                if (c != null && !c.GetIsDead() && !caught.Contains(c)) caught.Add(c);
            }
            Bursts.Add($"{d.ctx.sourceName}: {caught.Count}" + (caught.Contains(d.ctx.caster) ? " (you)" : ""));
            if (Bursts.Count > 60) Bursts.RemoveAt(0);
            if (caught.Count == 0) return;
            Actions.ProcDepth++;
            try
            {
                d.ctx.center = at;
                foreach (Character c in caught)
                    if (c != null && !c.GetIsDead()) { d.ctx.target = c; Actions.Run(d.spec.actions, d.ctx, c); }
            }
            finally { Actions.ProcDepth--; }
        }

        private static void Kill(Drifter d)
        {
            live.Remove(d);
            if (d.look == null) return;
            foreach (ParticleSystem ps in d.look.GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            Object.Destroy(d.look, 1.5f);
        }

        /// <summary>The effect, kept playing: every layer loops, the trail stays in the world, and no light comes with it.</summary>
        private static GameObject Look(string spec, Vector3 at)
        {
            if (string.IsNullOrEmpty(spec)) spec = "PA_Psykonics_Temporal.castParticle@0.4";
            float scale = Actions.SplitScale(spec, out string name);
            GameObject prefab = Actions.ResolveVfx(name);
            if (prefab == null) return new GameObject("SCCL_Drifter");
            GameObject go = Object.Instantiate(prefab, at, Quaternion.identity);
            Actions.Rescale(go, scale);
            foreach (Light l in go.GetComponentsInChildren<Light>(true)) Object.Destroy(l.gameObject);
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = true;
                if (ps.emission.rateOverTime.constant <= 0f) main.simulationSpace = ParticleSystemSimulationSpace.Local;
            }
            go.SetActive(true);
            return go;
        }

        internal static string Describe(ActionSpec a, string classId) =>
            $"Sends {a.fieldName ?? "a loose spell"} wandering from you toward {(a.to == "self" ? "nowhere in particular" : "the target")} " +
            $"for {Mathf.Max(1, a.turns)} turns, {Steps(a)} tiles a turn; on each tile, {(a.stepChance > 0 ? a.stepChance : 50)}% it goes off on " +
            $"everyone within {(a.radius > 0f ? a.radius : 1.1f):0.#}, friend or foe, you included{Actions.LevelNote(a.levelCap)}:\n" + Actions.Describe(a.actions, classId, "each");
    }
}
