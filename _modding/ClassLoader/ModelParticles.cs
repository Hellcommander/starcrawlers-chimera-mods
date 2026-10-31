using System.Collections.Generic;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>One entry of a model's "particles": an effect that rides on the creature for as long as it lives.</summary>
    public class ModelParticleSpec
    {
        /// <summary>A mod particle id or a vanilla particle name, with the usual "@0.5" size suffix.</summary>
        public string id;
        /// <summary>From the creature's feet, in the same units as the model's own "offset" (so the same numbers sit on
        /// the model, however the body under it is scaled); default: the model's offset.</summary>
        public float[] offset;
        /// <summary>The particles move with the body (halos, orbiting motes). False leaves them where they were made,
        /// so a moving creature draws a trail.</summary>
        public bool follow = true;
        /// <summary>The emitter bobs along with the model's "bob".</summary>
        public bool bob = true;
    }

    /// <summary>
    /// The live half of a wisp: a model's "particles" play on the creature, looping, until it dies or is recalled;
    /// then they stop emitting, fade out where they are, and "deathVfx" plays once. It is an OnDeath, so the game's
    /// own Die tells it. Built in Start so a mod enemy's prefab (and a loaded summon) builds its own copy.
    /// </summary>
    internal class ModelParticles : OnDeath
    {
        /// <summary>The model's registry key (serialised, so clones of an enemy prefab keep it).</summary>
        public string key;

        private Transform holder, model;
        private ModelAssets.Floater floater;
        private readonly List<(Transform t, Vector3 basePos, bool bob)> parts = new List<(Transform, Vector3, bool)>();
        private Character c;
        private bool hadCharacter, ended;
        private ModelSpec spec;

        internal static void Attach(GameObject go, ModelSpec spec, string folder)
        {
            if (go == null || spec == null || ((spec.particles == null || spec.particles.Count == 0) && string.IsNullOrEmpty(spec.deathVfx))) return;
            var mp = go.GetComponent<ModelParticles>() ?? go.AddComponent<ModelParticles>();
            mp.key = ModelAssets.Register(spec, folder);
        }

        /// <summary>The creature leaves without dying (a spent or recalled summon): let its particles go the same way.</summary>
        internal static void Release(GameObject go)
        {
            ModelParticles mp = go != null ? go.GetComponent<ModelParticles>() : null;
            if (mp != null) mp.End(true);
        }

        private void Start()
        {
            c = GetComponent<Character>();
            hadCharacter = c != null;
            if (!ModelAssets.TryGet(key, out spec, out _) || (c != null && c.GetIsDead())) { ended = true; return; }
            model = transform.Find("SCCL_Model");
            floater = model != null ? model.GetComponent<ModelAssets.Floater>() : null;
            if (spec.particles == null || spec.particles.Count == 0) return;

            // Built inactive so every layer is set to loop before its first particle; scaled so sizes and offsets are metres.
            var go = new GameObject("SCCL_Particles");
            go.SetActive(false);
            holder = go.transform;
            holder.SetParent(transform, false);
            Vector3 ls = transform.lossyScale;
            holder.localScale = new Vector3(1f / NonZero(ls.x), 1f / NonZero(ls.y), 1f / NonZero(ls.z));
            foreach (ModelParticleSpec p in spec.particles)
            {
                if (p == null || string.IsNullOrEmpty(p.id)) continue;
                GameObject prefab = Actions.ResolveVfx(p.id);
                if (prefab == null) continue;
                GameObject fx = Instantiate(prefab, holder, false);
                fx.name = "fx_" + p.id;
                fx.SetActive(true);
                float[] o = p.offset ?? spec.offset;
                Vector3 at = Vector3.Scale(o != null && o.Length >= 3 ? new Vector3(o[0], o[1], o[2]) : Vector3.up, ls);
                fx.transform.localPosition = at;
                fx.transform.localRotation = Quaternion.identity;
                Actions.Rescale(fx, Actions.SplitScale(p.id, out _));
                foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.loop = true; // a one-shot's burst now repeats every duration; its stream runs on
                    main.playOnAwake = true;
                    main.simulationSpace = p.follow ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
                    float life = main.startLifetime.constantMax, rate = ps.emission.rateOverTime.constantMax;
                    int burst = 0;
                    for (int i = 0; i < ps.emission.burstCount; i++) burst += (int)ps.emission.GetBurst(i).count.constantMax;
                    int need = Mathf.CeilToInt(rate * life + burst * (life / Mathf.Max(0.05f, main.duration) + 1f)) + 8;
                    if (main.maxParticles < need) main.maxParticles = Mathf.Min(need, 1000);
                }
                // A mod effect's light fades like a flash; on a wisp it is a steady glow.
                foreach (CustomVfx.LightFade f in fx.GetComponentsInChildren<CustomVfx.LightFade>(true)) Destroy(f);
                parts.Add((fx.transform, at, p.bob));
            }
            go.SetActive(true);
        }

        private void LateUpdate()
        {
            if (ended) return;
            if (hadCharacter && (c == null || c.GetIsDead())) { End(true); return; }
            if (holder == null || floater == null || model == null) return;
            // The model's bob, carried into the holder's metres.
            Vector3 d = Vector3.Scale(model.localPosition - floater.basePos, transform.lossyScale);
            foreach (var (t, basePos, bob) in parts) if (t != null && bob) t.localPosition = basePos + d;
        }

        public override void Trigger(Character who) => End(true);

        /// <summary>A corpse coming back from a save: no particles and no death burst.</summary>
        public override void Loaded() => End(false);

        private void End(bool burst)
        {
            if (ended) return;
            ended = true;
            Vector3 at = model != null ? model.position : transform.position + Vector3.up;
            if (holder != null)
            {
                float fade = 0.5f;
                foreach (ParticleSystem ps in holder.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    fade = Mathf.Max(fade, ps.main.startLifetime.constantMax);
                }
                foreach (Light l in holder.GetComponentsInChildren<Light>(true)) l.enabled = false;
                // Off the body, so what is already in the air fades where it is even if the body goes now.
                holder.SetParent(null, true);
                Destroy(holder.gameObject, fade + 0.5f);
                holder = null;
            }
            if (burst && spec != null && !string.IsNullOrEmpty(spec.deathVfx)) Actions.SpawnVfx(spec.deathVfx, at);
        }

        private void OnDestroy()
        {
            if (holder != null) Destroy(holder.gameObject);
        }

        private static float NonZero(float v) => Mathf.Abs(v) < 0.0001f ? 1f : v;
    }
}
