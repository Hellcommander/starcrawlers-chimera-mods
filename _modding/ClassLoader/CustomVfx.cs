using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Mod-made particle effects. A "particles" data file lists effects by id; each is one or more layers of textured
    /// particles (built-in sprites or the mod's own PNGs, optionally a flipbook grid) plus an optional fading light.
    /// Anything that takes a vanilla particle name (castVfx, impactVfx, travelVfx, areaVfx, proc vfx, pulseVfx...)
    /// also takes one of these ids, with the same "@0.5" size suffix. Templates are built on first use and cloned.
    /// </summary>
    internal static class CustomVfx
    {
        internal class FxSpec
        {
            public string id;
            public List<LayerSpec> layers = new List<LayerSpec>();
            public LightSpec light;
            /// <summary>Instead of layers: play another particle under this id ("Enemy_BOSS_Cerberus/Cerberus_Warp.impactParticle@0.6"),
            /// so a later mod can point a class's own effect ids at something else without touching the class.</summary>
            public string use;
            /// <summary>With "use": put the borrowed effect on the floor under the spot (one made to play at its owner's feet).</summary>
            public bool ground;
            /// <summary>With "use": parts of the borrowed effect to leave out, by name ("Dballs").</summary>
            public List<string> without;
            internal string folder;
        }

        internal class LayerSpec
        {
            /// <summary>Built-in sprite (soft, spark, ring, star, smoke, shard, mote) or a PNG path relative to the file.</summary>
            public string texture = "soft";
            /// <summary>Instead of a texture: the look of a particle system inside another effect, read from the game when
            /// first played ("Enemy_BOSS_Cerberus/Cerberus_Warp.impactParticle:BloodGlobs"). Takes its material, flipbook
            /// and vertex streams, so lit goo stays lit; the layer's own numbers still shape what it does.</summary>
            public string borrow;
            /// <summary>Flipbook columns and rows: [4,4].</summary>
            public int[] grid;
            /// <summary>Each particle shows one random frame instead of playing the flipbook over its life.</summary>
            public bool randomFrame;
            /// <summary>"add" (glows, black is see-through) or "alpha" (smoke, solid shapes).</summary>
            public string blend = "add";
            public int count = 16;
            /// <summary>Particles per second on top of the burst, for as long as the layer runs.</summary>
            public float rate;
            public float duration = 0.4f;
            public float delay;
            public float[] life = { 0.4f, 0.8f };
            public float[] speed = { 1f, 2f };
            public float[] size = { 0.15f, 0.3f };
            /// <summary>shrink, grow, pop (swell then vanish), pulse or flat.</summary>
            public string sizeCurve = "shrink";
            public string color = "#FFFFFF";
            /// <summary>Colour at the end of each particle's life (default: same as color).</summary>
            public string color2;
            public float alpha = 1f;
            /// <summary>sphere, hemisphere (up), cone (up), ring (edge of a flat circle), disc (flat circle), point, line.</summary>
            public string shape = "sphere";
            public float radius = 0.2f;
            public float angle = 25f;
            public float gravity;
            public float drag;
            public float[] spin;
            /// <summary>Swirl around the centre (turns/sec-ish) and push away from it (negative pulls in).</summary>
            public float orbit, radial;
            /// <summary>Stretch along the direction of travel (sparks, streaks); the number is the stretch factor.</summary>
            public float stretch;
            /// <summary>Lie flat (rings, glyph circles); with ground, on the floor.</summary>
            public bool flat;
            /// <summary>Emit from the floor under the spawn point instead of the point itself (cast effects spawn at chest height).</summary>
            public bool ground;
            public float[] offset;
        }

        internal class LightSpec
        {
            public string color = "#FFFFFF";
            public float range = 3f;
            public float intensity = 1.5f;
            public float time = 0.5f;
        }

        private static readonly Dictionary<string, FxSpec> specs = new Dictionary<string, FxSpec>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private static GameObject holder;
        private static Shader addShader, alphaShader;
        private static bool shadersLooked;

        internal static bool Has(string id) => !string.IsNullOrEmpty(id) && specs.ContainsKey(id) && string.IsNullOrEmpty(specs[id].use);

        /// <summary>What an id set with "use" plays instead (with its own @size), or null.</summary>
        internal static bool AliasOnFloor(string id) => !string.IsNullOrEmpty(id) && specs.TryGetValue(id, out FxSpec s) && !string.IsNullOrEmpty(s.use) && s.ground;

        /// <summary>Takes the parts an alias leaves out off a fresh copy of what it borrows.</summary>
        internal static void Trim(string id, GameObject go)
        {
            if (string.IsNullOrEmpty(id) || !specs.TryGetValue(id, out FxSpec s) || s.without == null) return;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true).Where(t => s.without.Contains(t.name)).ToList())
                if (t != null && t.gameObject != go) UnityEngine.Object.DestroyImmediate(t.gameObject);
        }

        internal static string Alias(string id) => !string.IsNullOrEmpty(id) && specs.TryGetValue(id, out FxSpec s) && !string.IsNullOrEmpty(s.use) ? s.use : null;
        internal static IEnumerable<string> Ids => specs.Keys;

        internal static void Load()
        {
            specs.Clear();
            foreach (GameObject t in templates.Values) if (t != null) UnityEngine.Object.Destroy(t);
            templates.Clear();
            foreach (ModPack p in ModPacks.Ordered)
                foreach (var (file, obj) in p.data)
                    foreach (var e in (obj["particles"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray()).OfType<Newtonsoft.Json.Linq.JObject>())
                    {
                        try
                        {
                            FxSpec s = e.ToObject<FxSpec>();
                            if (string.IsNullOrEmpty(s.id) || (s.layers.Count == 0 && string.IsNullOrEmpty(s.use))) { Plugin.Log.LogWarning($"{file}: a particle effect needs an id and at least one layer (or \"use\")"); continue; }
                            s.folder = Path.GetDirectoryName(file);
                            specs[s.id] = s; // later mods replace earlier ones
                        }
                        catch (Exception ex) { Plugin.Log.LogError($"{file}: a particle effect could not be read: {ex.Message}"); }
                    }
            if (specs.Count > 0) Plugin.Log.LogInfo($"Particles: {specs.Count} mod effects");
        }

        /// <summary>The inactive template for a mod effect, built on first use; null if no mod defines the id.</summary>
        internal static GameObject Template(string id)
        {
            if (!specs.TryGetValue(id, out FxSpec s)) return null;
            if (templates.TryGetValue(id, out GameObject t) && t != null) return t;
            try { t = Build(s); }
            catch (Exception e) { Plugin.Log.LogError($"Particle effect '{id}' failed to build: {e}"); t = null; }
            templates[id] = t;
            return t;
        }

        private static GameObject Build(FxSpec s)
        {
            if (holder == null)
            {
                holder = new GameObject("SCCL_CustomVfx");
                holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holder);
            }
            var root = new GameObject("SCCL_Fx_" + s.id);
            root.transform.SetParent(holder.transform, false);
            foreach (LayerSpec l in s.layers) AddLayer(root, l, s.folder);
            if (s.light != null)
            {
                var lg = new GameObject("light");
                lg.transform.SetParent(root.transform, false);
                Light light = lg.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = Col(s.light.color, Color.white);
                light.range = s.light.range;
                light.intensity = s.light.intensity;
                light.shadows = LightShadows.None;
                lg.AddComponent<LightFade>().time = Mathf.Max(0.05f, s.light.time);
            }
            return root;
        }

        private static void AddLayer(GameObject root, LayerSpec l, string folder)
        {
            var go = new GameObject("layer");
            go.transform.SetParent(root.transform, false);
            if (l.offset != null && l.offset.Length >= 3) go.transform.localPosition = new Vector3(l.offset[0], l.offset[1], l.offset[2]);
            if (l.ground) go.AddComponent<GroundSnap>();
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = true;
            main.loop = false;
            main.duration = Mathf.Max(0.05f, l.duration);
            main.startDelay = Mathf.Max(0f, l.delay);
            main.startLifetime = Range(l.life, 0.6f);
            main.startSpeed = Range(l.speed, 1.5f);
            main.startSize = Range(l.size, 0.2f);
            main.startColor = Color.white;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = l.gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = Mathf.Clamp(l.count + Mathf.CeilToInt(l.rate * (l.duration + 1f)) + 8, 8, 1000);

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = Mathf.Max(0f, l.rate);
            em.SetBursts(l.count > 0 ? new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(l.count, 1, 999)) } : new ParticleSystem.Burst[0]);

            var sh = ps.shape;
            sh.enabled = true;
            sh.radius = Mathf.Max(0.001f, l.radius);
            switch ((l.shape ?? "sphere").ToLowerInvariant())
            {
                case "point": sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.001f; break;
                case "hemisphere": sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.rotation = new Vector3(-90f, 0f, 0f); break;
                case "cone": sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = l.angle; sh.rotation = new Vector3(-90f, 0f, 0f); break;
                case "ring": sh.shapeType = ParticleSystemShapeType.Circle; sh.radiusThickness = 0f; sh.rotation = new Vector3(-90f, 0f, 0f); break;
                case "disc": sh.shapeType = ParticleSystemShapeType.Circle; sh.radiusThickness = 1f; sh.rotation = new Vector3(-90f, 0f, 0f); break;
                case "line": sh.shapeType = ParticleSystemShapeType.SingleSidedEdge; break;
                default: sh.shapeType = ParticleSystemShapeType.Sphere; break;
            }

            var col = ps.colorOverLifetime;
            col.enabled = true;
            Color a = Col(l.color, Color.white), b = Col(l.color2, a);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(l.alpha, 0.08f), new GradientAlphaKey(l.alpha, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var sz = ps.sizeOverLifetime;
            AnimationCurve curve = SizeCurve(l.sizeCurve);
            if (curve != null) { sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, curve); }

            if (l.spin != null && l.spin.Length > 0)
            {
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(l.spin[0] * Mathf.Deg2Rad, (l.spin.Length > 1 ? l.spin[1] : l.spin[0]) * Mathf.Deg2Rad);
            }
            if (l.orbit != 0f || l.radial != 0f)
            {
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.Local;
                vel.orbitalY = l.orbit * Mathf.PI * 2f;
                vel.radial = l.radial;
            }
            if (l.drag > 0f)
            {
                var lim = ps.limitVelocityOverLifetime;
                lim.enabled = true;
                lim.drag = l.drag;
                lim.multiplyDragByParticleSize = false;
                lim.multiplyDragByParticleVelocity = true;
            }

            Texture2D tex = Texture(l.texture, folder);
            if (l.grid != null && l.grid.Length >= 2 && l.grid[0] * l.grid[1] > 1)
            {
                var ts = ps.textureSheetAnimation;
                ts.enabled = true;
                ts.numTilesX = l.grid[0];
                ts.numTilesY = l.grid[1];
                if (l.randomFrame)
                {
                    ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                    ts.startFrame = new ParticleSystem.MinMaxCurve(0f, l.grid[0] * l.grid[1] - 0.01f);
                }
                else ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = MaterialFor(tex, l.blend);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.maxParticleSize = 2f;
            if (l.stretch > 0f) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f * l.stretch; r.lengthScale = 1f + l.stretch * 0.5f; }
            else if (l.flat) r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            else r.renderMode = ParticleSystemRenderMode.Billboard;
            if (!string.IsNullOrEmpty(l.borrow)) Borrow(l.borrow, ps, r);
        }

        private static void Borrow(string spec, ParticleSystem ps, ParticleSystemRenderer r)
        {
            int colon = spec.LastIndexOf(':');
            GameObject from = Actions.ResolveVfx(colon > 0 ? spec.Substring(0, colon) : spec);
            string part = colon > 0 ? spec.Substring(colon + 1) : null;
            ParticleSystem src = from == null ? null : from.GetComponentsInChildren<ParticleSystem>(true).FirstOrDefault(x => part == null || x.name == part);
            ParticleSystemRenderer sr = src != null ? src.GetComponent<ParticleSystemRenderer>() : null;
            if (sr == null || sr.sharedMaterial == null) { Plugin.Log.LogWarning($"particle layer: nothing to borrow at '{spec}'"); return; }
            r.sharedMaterial = sr.sharedMaterial;
            var streams = new List<ParticleSystemVertexStream>();
            sr.GetActiveVertexStreams(streams);
            r.SetActiveVertexStreams(streams);
            var sheet = src.textureSheetAnimation;
            if (sheet.enabled)
            {
                var ts = ps.textureSheetAnimation;
                ts.enabled = true;
                ts.numTilesX = sheet.numTilesX;
                ts.numTilesY = sheet.numTilesY;
                ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                ts.startFrame = new ParticleSystem.MinMaxCurve(0f, sheet.numTilesX * sheet.numTilesY - 0.01f);
            }
        }

        private static ParticleSystem.MinMaxCurve Range(float[] v, float fallback)
        {
            if (v == null || v.Length == 0) return new ParticleSystem.MinMaxCurve(fallback);
            return v.Length == 1 ? new ParticleSystem.MinMaxCurve(v[0]) : new ParticleSystem.MinMaxCurve(v[0], v[1]);
        }

        private static AnimationCurve SizeCurve(string kind)
        {
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "flat": return null;
                case "grow": return new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 1f));
                case "pop": return new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f));
                case "pulse": return new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(0.6f, 0.7f), new Keyframe(1f, 0f));
                default: return new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            }
        }

        private static Color Col(string hex, Color fallback) => !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;

        // ---- materials and textures -------------------------------------------------------------------------------

        private static Material MaterialFor(Texture2D tex, string blend)
        {
            if (!shadersLooked)
            {
                shadersLooked = true;
                foreach (string n in new[] { "Legacy Shaders/Particles/Additive", "Particles/Additive", "Mobile/Particles/Additive" })
                    if ((addShader = Shader.Find(n)) != null) break;
                foreach (string n in new[] { "Legacy Shaders/Particles/Alpha Blended", "Particles/Alpha Blended", "Mobile/Particles/Alpha Blended", "Sprites/Default" })
                    if ((alphaShader = Shader.Find(n)) != null) break;
                Plugin.Log.LogInfo($"Particles use shaders '{addShader?.name}' (add) and '{alphaShader?.name}' (alpha)");
            }
            bool alpha = string.Equals(blend, "alpha", StringComparison.OrdinalIgnoreCase);
            Shader sh = alpha ? alphaShader ?? addShader : addShader ?? alphaShader;
            string key = (alpha ? "a:" : "+:") + tex.GetInstanceID();
            if (materials.TryGetValue(key, out Material m) && m != null) return m;
            m = new Material(sh) { name = "SCCL_Fx_" + tex.name, mainTexture = tex };
            // 0.5 is neutral for the legacy particle shaders; a little over makes glows hold up against lit rooms.
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", alpha ? new Color(0.5f, 0.5f, 0.5f, 0.5f) : new Color(0.62f, 0.62f, 0.62f, 0.5f));
            materials[key] = m;
            return m;
        }

        private static Texture2D Texture(string name, string folder)
        {
            name = string.IsNullOrEmpty(name) ? "soft" : name;
            string path = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? Path.GetFullPath(Path.Combine(folder ?? "", name)) : null;
            string key = path ?? name;
            if (textures.TryGetValue(key, out Texture2D t) && t != null) return t;
            t = path != null ? LoadPng(path) : null;
            if (t == null)
            {
                if (path != null) Plugin.Log.LogWarning($"Particle texture not found or unreadable: {path}; using a soft dot");
                t = Builtin(path != null ? "soft" : name.ToLowerInvariant());
            }
            textures[key] = t;
            return t;
        }

        /// <summary>A PNG without transparency (glow drawn on black) gets its brightness as alpha, so it also works alpha-blended.</summary>
        private static Texture2D LoadPng(string path)
        {
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = Path.GetFileNameWithoutExtension(path), wrapMode = TextureWrapMode.Clamp };
            if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
            Color32[] px = tex.GetPixels32();
            if (px.All(p => p.a == 255))
            {
                for (int i = 0; i < px.Length; i++) px[i].a = Math.Max(px[i].r, Math.Max(px[i].g, px[i].b));
                tex.SetPixels32(px);
                tex.Apply(true);
            }
            return tex;
        }

        /// <summary>White sprites drawn in code, so effects work without any art; the layer colour tints them.</summary>
        private static Texture2D Builtin(string kind)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "SCCL_" + kind, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            var rng = new System.Random(kind.GetHashCode());
            float[] noise = new float[16 * 16];
            for (int i = 0; i < noise.Length; i++) noise[i] = (float)rng.NextDouble();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v), a;
                    switch (kind)
                    {
                        case "spark": a = Mathf.Exp(-(u * u) / 0.004f - (v * v) / 0.35f); break;
                        case "ring": a = Mathf.Exp(-Mathf.Pow((r - 0.72f) / 0.09f, 2f)); break;
                        case "star":
                            a = Mathf.Max(Mathf.Exp(-(u * u) / 0.003f - (v * v) / 0.5f), Mathf.Exp(-(v * v) / 0.003f - (u * u) / 0.5f));
                            a = Mathf.Max(a, Mathf.Exp(-r * r / 0.03f));
                            break;
                        case "shard": a = Mathf.Clamp01((1f - (Mathf.Abs(u) / 0.32f + Mathf.Abs(v) / 0.95f)) * 6f); break;
                        case "smoke":
                            float nz = Noise(noise, (u + 1f) * 3.5f, (v + 1f) * 3.5f) * 0.6f + Noise(noise, (u + 1f) * 7f, (v + 1f) * 7f) * 0.4f;
                            a = Mathf.Clamp01((1f - r) * 1.6f) * Mathf.Clamp01(nz * 1.8f - 0.3f);
                            break;
                        case "mote": a = Mathf.Clamp01((0.35f - r) * 12f) + Mathf.Exp(-r * r / 0.12f) * 0.5f; break;
                        default: a = Mathf.Exp(-r * r / 0.16f) * Mathf.Clamp01((1f - r) * 4f); break; // soft
                    }
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        private static float Noise(float[] g, float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            float At(int i, int j) => g[((j & 15) * 16) + (i & 15)];
            float top = Mathf.Lerp(At(xi, yi), At(xi + 1, yi), fx), bot = Mathf.Lerp(At(xi, yi + 1), At(xi + 1, yi + 1), fx);
            return Mathf.Lerp(top, bot, fy);
        }

        // ---- runtime pieces ---------------------------------------------------------------------------------------

        /// <summary>Drops a layer onto the floor tile under it before its first particles come out.</summary>
        internal class GroundSnap : MonoBehaviour
        {
            /// <summary>Puts a spawned effect's root on the floor under it.</summary>
            internal static void Floor(GameObject go)
            {
                Vector3 p = go.transform.position;
                NavNode n = Map.instance != null ? Map.instance.GetNearestNavNodeAt(p, 2.5f) : null;
                if (n != null) go.transform.position = new Vector3(p.x, n.transform.position.y + 0.05f, p.z);
            }

            private void Awake()
            {
                Vector3 p = transform.position;
                float lift = transform.localPosition.y; // the layer's own offset now counts from the floor
                NavNode n = Map.instance != null ? Map.instance.GetNearestNavNodeAt(p, 2.5f) : null;
                float y = n != null ? n.transform.position.y : p.y - lift - 1f;
                transform.position = new Vector3(p.x, y + 0.05f + lift, p.z);
            }
        }

        /// <summary>Dims an effect's light to nothing over its time.</summary>
        internal class LightFade : MonoBehaviour
        {
            internal float time = 0.5f;
            private float born, start;
            private Light l;

            private void OnEnable() { l = GetComponent<Light>(); born = Time.time; start = l != null ? l.intensity : 0f; }

            private void Update()
            {
                if (l == null) return;
                float k = (Time.time - born) / time;
                if (k >= 1f) { l.enabled = false; enabled = false; return; }
                l.intensity = start * (1f - k * k);
            }
        }

        /// <summary>Carries an effect from caster to target on a shallow arc; on arrival its emitters stop and the trail fades.</summary>
        internal class Travel : MonoBehaviour
        {
            internal Vector3 from, to;
            internal float time = 0.3f, arc = 0.3f;
            private float born;

            /// <summary>Call right after Instantiate, before the first particle update.</summary>
            internal void Prepare()
            {
                foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.loop = true; // a travelling layer keeps emitting until it lands
                    // Burst-only layers are the projectile itself and ride along; streaming layers leave a trail.
                    if (ps.emission.rateOverTime.constant <= 0f) main.simulationSpace = ParticleSystemSimulationSpace.Local;
                }
            }

            private void Start() => born = Time.time;

            private void Update()
            {
                float k = Mathf.Clamp01((Time.time - born) / time);
                transform.position = Vector3.Lerp(from, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * arc);
                if (k < 1f) return;
                foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
                    if (ps.isEmitting) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                foreach (Light l in GetComponentsInChildren<Light>(true)) l.enabled = false;
                enabled = false;
            }
        }
    }
}
