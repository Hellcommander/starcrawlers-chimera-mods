using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// "model" in mod JSON: a new look for a creature. Either a mesh (.glb or .obj next to the mod's files) that replaces
    /// the creature's body, or just a texture that repaints the body it has.
    /// </summary>
    public class ModelSpec
    {
        /// <summary>.glb (glTF 2.0 binary: positions, normals, UVs, embedded PNG base colour) or .obj.</summary>
        public string mesh;
        /// <summary>PNG. With a mesh: overrides its own texture. Without: repaints the creature's own body.</summary>
        public string texture;
        public float scale = 1f;
        /// <summary>Metres from the model's feet to its top, whatever the body under it is scaled to (overrides scale).</summary>
        public float height;
        /// <summary>Metres from the creature's feet: [x, y, z]. Floating things want y around 1.</summary>
        public float[] offset;
        /// <summary>Degrees [x, y, z].</summary>
        public float[] rotation;
        /// <summary>Glow colour (#RRGGBB) and strength.</summary>
        public string glow;
        public float glowStrength = 1f;
        /// <summary>Colour multiplied into the texture (#RRGGBB).</summary>
        public string tint;
        /// <summary>Bob up and down by this many metres, and turn this many degrees a second.</summary>
        public float bob;
        public float spin;
        /// <summary>Rocks back and forth by this many degrees (a knife quivering in the deck).</summary>
        public float wobble;
        /// <summary>Breathes in size by this fraction (0.1 = ±10%).</summary>
        public float pulse;
        /// <summary>Random turn around the up axis for each copy, so a scatter of them doesn't look stamped.</summary>
        public bool randomYaw;
        /// <summary>Keep the creature's own particle effects (glows, smoke) when its body is replaced.</summary>
        public bool keepParticles = true;
        /// <summary>The model keeps turning to face the camera (a floating grin, a ghost), whatever way its body faces.</summary>
        public bool faceViewer;
        /// <summary>For a skinned .glb with clips: which clip plays for each state (default: clips named idle, move,
        /// attack, hit, death, matched by name), and how fast they play.</summary>
        public Dictionary<string, string> clips;
        public float animSpeed = 1f;
        /// <summary>Particle effects that play on the creature for as long as it lives (a wisp's motes, halo, trail);
        /// see <see cref="ModelParticles"/>. Works with or without a mesh.</summary>
        public List<ModelParticleSpec> particles;
        /// <summary>A particle that plays once where the model was when the creature dies or is recalled.</summary>
        public string deathVfx;
    }

    internal static class ModelAssets
    {
        internal const string TagPrefix = "SCCLMDL_";
        private class Loaded { public Mesh[] meshes; public Material[] materials; public Rig rig; }

        /// <summary>A skinned, animated .glb: its node tree (in Unity's space), skinned parts and clips.</summary>
        private class Rig
        {
            public List<(string name, int parent, Vector3 pos, Quaternion rot, Vector3 scale)> nodes = new List<(string, int, Vector3, Quaternion, Vector3)>();
            public List<(Mesh mesh, Material material, int[] bones)> parts = new List<(Mesh, Material, int[])>();
            public List<AnimationClip> clips = new List<AnimationClip>();
        }
        private static readonly Dictionary<string, Loaded> cache = new Dictionary<string, Loaded>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, (ModelSpec spec, string folder)> registry = new Dictionary<string, (ModelSpec, string)>();
        private static Shader litShader;

        // ---- registry: summons are tagged with a key so the look comes back after a load -------------------------

        internal static string Key(ModelSpec spec, string folder)
        {
            string s = Path.GetFileName(folder ?? "") + "|" + JsonConvert.SerializeObject(spec);
            uint h = 2166136261;
            foreach (char ch in s) { h ^= ch; h *= 16777619; }
            return TagPrefix + h.ToString("x8");
        }

        internal static string Register(ModelSpec spec, string folder)
        {
            if (spec == null) return null;
            string key = Key(spec, folder);
            registry[key] = (spec, folder);
            return key;
        }

        internal static bool TryGet(string key, out ModelSpec spec, out string folder)
        {
            spec = null; folder = null;
            if (key == null || !registry.TryGetValue(key, out var m)) return false;
            spec = m.spec; folder = m.folder;
            return true;
        }

        /// <summary>Every "model" anywhere in a class's abilities and effects, so tagged summons can be restored after a restart.</summary>
        internal static void RegisterAll(JToken root, string folder)
        {
            if (root == null) return;
            foreach (JToken t in root.SelectTokens("$..model").ToList())
            {
                if (!(t is JObject o)) continue;
                try { Register(o.ToObject<ModelSpec>(), folder); }
                catch (Exception e) { Plugin.Log.LogWarning("Model spec: " + e.Message); }
            }
        }

        // ---- applying -------------------------------------------------------------------------------------------

        /// <summary>Gives a spawned character its look and tags it so a load restores it.</summary>
        internal static void ApplyTagged(Character c, ModelSpec spec, string folder)
        {
            if (c == null || spec == null) return;
            string key = Register(spec, folder);
            if (!c.HasTag(key)) c.SetTag(key, 1);
            Apply(c.gameObject, spec, folder);
        }

        /// <summary>Puts the look on a character object (a live one or a prefab clone).</summary>
        internal static bool Apply(GameObject go, ModelSpec spec, string folder)
        {
            if (go == null || spec == null) return false;
            try
            {
                if (go.transform.Find("SCCL_Model") != null) return true;
                Texture2D tex = string.IsNullOrEmpty(spec.texture) ? null : LoadTexture(Path.Combine(folder ?? "", spec.texture));
                if (string.IsNullOrEmpty(spec.mesh))
                {
                    if (tex == null)
                    {
                        // Only particles: they ride on the body the creature already has.
                        ModelParticles.Attach(go, spec, folder);
                        return spec.particles?.Count > 0;
                    }
                    foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r is ParticleSystemRenderer || r is SpriteRenderer) continue;
                        foreach (Material m in r.materials) { if (m.HasProperty("_MainTex")) m.mainTexture = tex; Tint(m, spec); }
                        Material glowPass = Glow(tex, spec);
                        if (glowPass != null) r.materials = r.materials.Concat(new[] { glowPass }).ToArray();
                    }
                    ModelParticles.Attach(go, spec, folder);
                    return true;
                }
                Loaded model = LoadModel(Path.Combine(folder ?? "", spec.mesh));
                if (model == null) return false;

                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer && spec.keepParticles) continue;
                    if (r is SpriteRenderer || r.GetComponentInParent<Canvas>() != null) continue;
                    r.enabled = false;
                }

                var root = new GameObject("SCCL_Model");
                root.transform.SetParent(go.transform, false);
                Vector3 off = V(spec.offset, Vector3.zero);
                root.transform.localPosition = off;
                root.transform.localRotation = Quaternion.Euler(V(spec.rotation, Vector3.zero));
                root.transform.localScale = Vector3.one * (spec.scale <= 0 ? 1f : spec.scale);
                if (model.rig != null) BuildRig(root, model.rig, tex, spec);
                for (int i = 0; i < model.meshes.Length; i++)
                {
                    var part = new GameObject("Part" + i);
                    part.transform.SetParent(root.transform, false);
                    part.AddComponent<MeshFilter>().sharedMesh = model.meshes[i];
                    var mr = part.AddComponent<MeshRenderer>();
                    Material m = new Material(model.materials[Math.Min(i, model.materials.Length - 1)]);
                    if (tex != null && m.HasProperty("_MainTex")) m.mainTexture = tex;
                    Tint(m, spec);
                    Material glowPass = Glow(m.HasProperty("_MainTex") ? m.mainTexture : null, spec);
                    mr.sharedMaterials = glowPass != null ? new[] { m, glowPass } : new[] { m };
                    mr.shadowCastingMode = ShadowCastingMode.On;
                }
                // The build strips Standard's emission variant, so a glow is a real light on the model.
                if (!string.IsNullOrEmpty(spec.glow) && ColorUtility.TryParseHtmlString(spec.glow, out Color gc))
                {
                    var lg = new GameObject("Glow");
                    lg.transform.SetParent(root.transform, false);
                    var light = lg.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = gc;
                    light.range = 2.5f * Mathf.Max(0.5f, spec.scale);
                    light.intensity = 1.5f * Mathf.Max(0f, spec.glowStrength);
                    light.shadows = LightShadows.None;
                }
                if (spec.randomYaw) root.transform.localRotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * root.transform.localRotation;
                if (model.rig != null && model.rig.clips.Count > 0)
                {
                    MobAnimDriver d = go.GetComponent<MobAnimDriver>() ?? go.AddComponent<MobAnimDriver>();
                    d.Bind(root.GetComponent<Animation>(), spec.clips, spec.animSpeed);
                }
                if (spec.bob != 0f || spec.spin != 0f || spec.wobble != 0f || spec.pulse != 0f)
                {
                    var f = root.AddComponent<Floater>();
                    f.bob = spec.bob; f.spin = spec.spin; f.wobble = spec.wobble; f.pulse = spec.pulse;
                    f.basePos = off; f.baseRot = root.transform.localRotation; f.baseScale = root.transform.localScale;
                }
                if (spec.height > 0f) FitHeight(root, go.transform, spec.height);
                if (spec.faceViewer) root.AddComponent<FaceViewer>();
                ModelParticles.Attach(go, spec, folder);
                return true;
            }
            catch (Exception e) { Plugin.Log.LogError($"Model '{spec.mesh ?? spec.texture}' on {go.name}: {e}"); return false; }
        }

        /// <summary>The node tree as transforms under the model root, a SkinnedMeshRenderer per part, and the clips.</summary>
        private static void BuildRig(GameObject root, Rig rig, Texture2D tex, ModelSpec spec)
        {
            var bones = new Transform[rig.nodes.Count];
            for (int i = 0; i < rig.nodes.Count; i++) bones[i] = new GameObject(rig.nodes[i].name).transform;
            for (int i = 0; i < rig.nodes.Count; i++)
            {
                var n = rig.nodes[i];
                bones[i].SetParent(n.parent >= 0 ? bones[n.parent] : root.transform, false);
                bones[i].localPosition = n.pos;
                bones[i].localRotation = n.rot;
                bones[i].localScale = n.scale;
            }
            int k = 0;
            foreach (var (mesh, material, boneIdx) in rig.parts)
            {
                var part = new GameObject("Skin" + k++);
                part.transform.SetParent(root.transform, false);
                var smr = part.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = boneIdx.Select(b => bones[b]).ToArray();
                smr.rootBone = smr.bones.Length > 0 ? smr.bones[0] : root.transform;
                smr.updateWhenOffscreen = true;
                Material m = new Material(material);
                if (tex != null && m.HasProperty("_MainTex")) m.mainTexture = tex;
                Tint(m, spec);
                Material glowPass = Glow(m.HasProperty("_MainTex") ? m.mainTexture : null, spec);
                smr.sharedMaterials = glowPass != null ? new[] { m, glowPass } : new[] { m };
                smr.shadowCastingMode = ShadowCastingMode.On;
            }
            if (rig.clips.Count == 0) return;
            var anim = root.AddComponent<Animation>();
            anim.playAutomatically = false;
            foreach (AnimationClip c in rig.clips) anim.AddClip(c, c.name);
            anim.clip = rig.clips[0];
        }

        private static void Tint(Material m, ModelSpec spec)
        {
            if (!string.IsNullOrEmpty(spec.tint) && ColorUtility.TryParseHtmlString(spec.tint, out Color t) && m.HasProperty("_Color")) m.color = m.color * t;
            if (!string.IsNullOrEmpty(spec.glow) && ColorUtility.TryParseHtmlString(spec.glow, out Color g) && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", g * Mathf.Max(0f, spec.glowStrength));
                if (m.HasProperty("_EmissionMap") && m.mainTexture != null) m.SetTexture("_EmissionMap", m.mainTexture);
            }
        }

        private static Shader glowShader;
        private static bool glowLooked;

        /// <summary>
        /// The build strips Standard's emission variant, but particle shaders always ship: an additive pass of the
        /// model's own texture in the glow colour, drawn over the lit one, makes it shine (and the game's bloom picks it up).
        /// </summary>
        private static Material Glow(Texture tex, ModelSpec spec)
        {
            if (string.IsNullOrEmpty(spec.glow) || !ColorUtility.TryParseHtmlString(spec.glow, out Color c) || spec.glowStrength <= 0f) return null;
            if (!glowLooked)
            {
                glowLooked = true;
                foreach (string n in new[] { "Legacy Shaders/Particles/Additive", "Particles/Additive", "Mobile/Particles/Additive", "Legacy Shaders/Particles/Additive (Soft)" })
                    if ((glowShader = Shader.Find(n)) != null) break;
                if (glowShader == null)
                    foreach (ParticleSystemRenderer r in Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>())
                        if (r?.sharedMaterial?.shader != null && r.sharedMaterial.shader.name.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0) { glowShader = r.sharedMaterial.shader; break; }
                Plugin.Log.LogInfo($"Model glow uses shader '{glowShader?.name}'");
            }
            if (glowShader == null) return null;
            var m = new Material(glowShader) { name = "SCCL_Glow" };
            if (tex != null && m.HasProperty("_MainTex")) m.mainTexture = tex;
            Color g = c * Mathf.Clamp(spec.glowStrength, 0f, 4f) * 0.5f;
            g.a = 1f;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", g);
            if (m.HasProperty("_Color")) m.SetColor("_Color", g);
            return m;
        }

        private static Vector3 V(float[] a, Vector3 d) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : d;

        /// <summary>Scales the model root so its meshes stand `height` metres tall in the world, allowing for the scale of the
        /// body it sits on (summon and enemy bodies are often scaled down).</summary>
        private static void FitHeight(GameObject root, Transform body, float height)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null) { lo = Mathf.Min(lo, f.sharedMesh.bounds.min.y); hi = Mathf.Max(hi, f.sharedMesh.bounds.max.y); }
            foreach (SkinnedMeshRenderer r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.sharedMesh != null) { lo = Mathf.Min(lo, r.sharedMesh.bounds.min.y); hi = Mathf.Max(hi, r.sharedMesh.bounds.max.y); }
            float tall = hi - lo, parent = Mathf.Abs(body.lossyScale.y);
            if (tall <= 0.0001f || parent <= 0.0001f) return;
            root.transform.localScale = Vector3.one * (height / (tall * parent));
            var f2 = root.GetComponent<Floater>();
            if (f2 != null) f2.baseScale = root.transform.localScale;
        }

        /// <summary>Turns the model about its vertical axis toward the camera each frame (after any Floater has moved it).</summary>
        internal class FaceViewer : MonoBehaviour
        {
            private void LateUpdate()
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                Vector3 d = cam.transform.position - transform.position; d.y = 0f;
                if (d.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(d);
            }
        }

        /// <summary>The game's culling switches a character's skinned mesh back on when it comes into view, which would
        /// bring the base body back under a mod model; keep it off while the character wears one.</summary>
        [HarmonyLib.HarmonyPatch(typeof(Character), nameof(Character.CullSkinnedMesh))]
        private static class Patch_KeepBaseHidden
        {
            private static void Postfix(Character __instance)
            {
                if (__instance == null || __instance.skinnedMesh == null || __instance.transform.Find("SCCL_Model") == null) return;
                if (!__instance.skinnedMesh.transform.IsChildOf(__instance.transform.Find("SCCL_Model"))) __instance.skinnedMesh.enabled = false;
            }
        }

        /// <summary>Procedural animation: bobbing, turning, rocking and breathing.</summary>
        internal class Floater : MonoBehaviour
        {
            public float bob, spin, wobble, pulse;
            public Vector3 basePos, baseScale = Vector3.one;
            public Quaternion baseRot = Quaternion.identity;
            private float phase, turned;
            private void Awake() => phase = UnityEngine.Random.value * 6.28f;
            private void Update()
            {
                float t = Time.time + phase;
                transform.localPosition = basePos + Vector3.up * Mathf.Sin(t * 1.6f) * bob;
                turned += spin * Time.deltaTime;
                // A rock that settles: strong just after the copy appears, then a faint idle sway.
                float rock = wobble == 0f ? 0f : wobble * Mathf.Sin(t * 9f) * (0.25f + 0.75f * Mathf.Exp(-Mathf.Max(0f, Time.time - born) * 2.5f));
                transform.localRotation = Quaternion.Euler(0f, turned, 0f) * baseRot * Quaternion.Euler(rock, 0f, rock * 0.5f);
                if (pulse != 0f) transform.localScale = baseScale * (1f + Mathf.Sin(t * 2.4f) * pulse);
            }
            private float born;
            private void Start() => born = Time.time;
        }

        /// <summary>
        /// An item's look on the floor: replaces its world object (the base item's mesh). Here "scale" is the model's
        /// longest side in metres and it rests on the floor, whatever scale and turn the base prefab's mesh had.
        /// </summary>
        internal static bool ApplyToItem(Item it, ModelSpec spec, string folder)
        {
            GameObject target = it.worldObject != null ? it.worldObject : it.gameObject;
            Transform old = target.transform.Find("SCCL_Model");
            if (old != null) Object.DestroyImmediate(old.gameObject); // a mod base's model; this item has its own
            if (!Apply(target, spec, folder)) return false;
            Transform root = target.transform.Find("SCCL_Model");
            if (root == null) return false;

            Quaternion turn = it.transform.rotation * Quaternion.Euler(V(spec.rotation, Vector3.zero));
            var parts = root.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null).ToList();
            if (parts.Count == 0) return true;
            // Bounds of the turned model at size 1, to size it and sit it on the floor.
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            foreach (MeshFilter mf in parts)
            {
                Bounds b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = turn * new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    lo = Vector3.Min(lo, c); hi = Vector3.Max(hi, c);
                }
            }
            Vector3 ext = hi - lo;
            float k = (spec.scale <= 0 ? 0.3f : spec.scale) / Mathf.Max(0.0001f, Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z)));
            Vector3 ls = target.transform.lossyScale;
            root.rotation = turn;
            root.localScale = new Vector3(k / NonZero(ls.x), k / NonZero(ls.y), k / NonZero(ls.z));
            // Centred over the world object (it may spin), bottom on the item's floor.
            Vector3 centre = (lo + hi) * 0.5f * k;
            Vector3 at = new Vector3(target.transform.position.x - centre.x, it.transform.position.y - lo.y * k, target.transform.position.z - centre.z);
            root.position = at + it.transform.rotation * V(spec.offset, Vector3.zero);
            Light glow = root.GetComponentInChildren<Light>(true);
            if (glow != null) glow.range = Mathf.Max(1f, 4f * k * Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z)));
            var f = root.GetComponent<Floater>();
            if (f != null) { f.basePos = root.localPosition; f.baseRot = root.localRotation; f.baseScale = root.localScale; }
            return true;
        }

        private static float NonZero(float v) => Mathf.Abs(v) < 0.0001f ? 1f : v;

        /// <summary>A free-standing copy of a model (no creature under it): the look of a field tile, a dropped prop.</summary>
        internal static GameObject Spawn(ModelSpec spec, string folder, Vector3 at, Transform parent)
        {
            var go = new GameObject("SCCL_Prop");
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            if (!Apply(go, spec, folder)) { Object.Destroy(go); return null; }
            return go;
        }

        // ---- loading --------------------------------------------------------------------------------------------

        private static Shader Lit()
        {
            if (litShader != null) return litShader;
            litShader = Shader.Find("Standard");
            if (litShader == null)
            {
                // The build may strip Standard: use a lit shader one of the game's characters uses.
                foreach (Renderer r in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>().Cast<Renderer>().Concat(Resources.FindObjectsOfTypeAll<MeshRenderer>()))
                    if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.HasProperty("_MainTex")) { litShader = r.sharedMaterial.shader; break; }
            }
            Plugin.Log.LogInfo($"Models use shader '{litShader?.name}'");
            return litShader;
        }

        private static Material NewMaterial(Texture2D tex, Color baseColor)
        {
            var m = new Material(Lit());
            if (tex != null && m.HasProperty("_MainTex")) m.mainTexture = tex;
            if (m.HasProperty("_Color")) m.color = baseColor;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.25f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            Object.DontDestroyOnLoad(m);
            return m;
        }

        /// <summary>A static model's meshes and materials (one per mesh), for geometry built from kit pieces.</summary>
        internal static bool LoadParts(string path, out Mesh[] meshes, out Material[] materials)
        {
            Loaded l = LoadModel(path);
            meshes = l?.meshes ?? new Mesh[0];
            materials = l?.materials ?? new Material[0];
            return l != null && meshes.Length > 0 && materials.Length > 0;
        }

        /// <summary>A plain lit material in one colour (stand-in surfaces).</summary>
        internal static Material LitMaterial(Color c) => NewMaterial(null, c);

        /// <summary>An additive glow material in this colour (light fixtures, screens, door lamps).</summary>
        internal static Material GlowMaterial(Color c, float strength)
        {
            Material m = Glow(null, new ModelSpec { glow = "#" + ColorUtility.ToHtmlStringRGB(c), glowStrength = strength });
            if (m != null) Object.DontDestroyOnLoad(m);
            return m;
        }

        internal static Texture2D LoadTexture(string path)
        {
            if (!File.Exists(path)) { Plugin.Log.LogWarning($"Texture not found: {path}"); return null; }
            return TextureFromBytes(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));
        }

        private static Texture2D TextureFromBytes(byte[] bytes, string name)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!tex.LoadImage(bytes)) return null;
            tex.name = name;
            tex.wrapMode = TextureWrapMode.Repeat;
            Object.DontDestroyOnLoad(tex);
            return tex;
        }

        private static Loaded LoadModel(string path)
        {
            if (cache.TryGetValue(path, out Loaded l)) return l;
            if (!File.Exists(path)) { Plugin.Log.LogWarning($"Model not found: {path}"); return null; }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            l = ext == ".glb" ? LoadGlb(path) : ext == ".obj" ? LoadObj(path) : null;
            if (l == null) Plugin.Log.LogWarning($"Model not loaded (only .glb and .obj): {path}");
            else Plugin.Log.LogInfo($"Model {Path.GetFileName(path)}: {l.meshes.Length} part(s), {l.meshes.Sum(m => m.vertexCount)} vertices"
                + (l.rig != null ? $", skinned: {l.rig.parts.Count} part(s), {l.rig.nodes.Count} nodes, clips [{string.Join(", ", l.rig.clips.Select(c => $"{c.name} {c.length:0.##}s"))}]" : ""));
            cache[path] = l;
            return l;
        }

        // glTF 2.0 binary. Static meshes: node transforms are baked in. Skinned meshes keep their bones, and the
        // animations become legacy clips (see Rig). glTF is right-handed: X is mirrored for Unity everywhere.
        private static Loaded LoadGlb(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 20 || BitConverter.ToUInt32(data, 0) != 0x46546C67) { Plugin.Log.LogWarning($"{path}: not a .glb"); return null; }
            int jsonLen = BitConverter.ToInt32(data, 12);
            JObject g = JObject.Parse(Encoding.UTF8.GetString(data, 20, jsonLen));
            int binStart = 20 + jsonLen + 8;
            if ((g["extensionsRequired"] as JArray)?.Count > 0) { Plugin.Log.LogWarning($"{path}: needs glTF extensions {g["extensionsRequired"]} (export without compression)"); return null; }

            var views = (JArray)g["bufferViews"];
            var accessors = (JArray)g["accessors"];
            ArraySegment<byte> View(int i)
            {
                var v = views[i];
                return new ArraySegment<byte>(data, binStart + (int?)v["byteOffset"] ?? binStart, (int)v["byteLength"]);
            }

            float[] ReadFloats(int acc, int comps)
            {
                var a = accessors[acc];
                int count = (int)a["count"];
                var v = views[(int)a["bufferView"]];
                int stride = (int?)v["byteStride"] ?? comps * 4;
                int start = binStart + ((int?)v["byteOffset"] ?? 0) + ((int?)a["byteOffset"] ?? 0);
                var r = new float[count * comps];
                for (int i = 0; i < count; i++)
                    for (int c = 0; c < comps; c++) r[i * comps + c] = BitConverter.ToSingle(data, start + i * stride + c * 4);
                return r;
            }

            int[] ReadIndices(int acc)
            {
                var a = accessors[acc];
                int count = (int)a["count"], type = (int)a["componentType"];
                var v = views[(int)a["bufferView"]];
                int start = binStart + ((int?)v["byteOffset"] ?? 0) + ((int?)a["byteOffset"] ?? 0);
                var r = new int[count];
                for (int i = 0; i < count; i++)
                    r[i] = type == 5125 ? (int)BitConverter.ToUInt32(data, start + i * 4) : type == 5123 ? BitConverter.ToUInt16(data, start + i * 2) : data[start + i];
                return r;
            }

            // Any accessor as floats (normalized integers scaled to 0..1, plain ones as they are).
            float[] ReadAny(int acc)
            {
                var a = accessors[acc];
                int count = (int)a["count"], ct = (int)a["componentType"];
                string type = (string)a["type"];
                int comps = type == "VEC2" ? 2 : type == "VEC3" ? 3 : type == "VEC4" ? 4 : type == "MAT4" ? 16 : 1;
                int size = ct == 5126 || ct == 5125 ? 4 : ct == 5123 || ct == 5122 ? 2 : 1;
                bool norm = (bool?)a["normalized"] ?? false;
                var v = views[(int)a["bufferView"]];
                int stride = (int?)v["byteStride"] ?? comps * size;
                int start = binStart + ((int?)v["byteOffset"] ?? 0) + ((int?)a["byteOffset"] ?? 0);
                var r = new float[count * comps];
                for (int i = 0; i < count; i++)
                    for (int c = 0; c < comps; c++)
                    {
                        int at = start + i * stride + c * size;
                        float f = ct == 5126 ? BitConverter.ToSingle(data, at) : ct == 5125 ? BitConverter.ToUInt32(data, at)
                            : ct == 5123 ? BitConverter.ToUInt16(data, at) : ct == 5122 ? BitConverter.ToInt16(data, at)
                            : ct == 5121 ? data[at] : (sbyte)data[at];
                        if (norm) f /= ct == 5123 ? 65535f : ct == 5122 ? 32767f : ct == 5121 ? 255f : 127f;
                        r[i * comps + c] = f;
                    }
                return r;
            }

            var textures = new Dictionary<int, Texture2D>();
            Texture2D Tex(int texIndex)
            {
                if (textures.TryGetValue(texIndex, out var t)) return t;
                int img = (int)g["textures"][texIndex]["source"];
                var image = g["images"][img];
                var seg = View((int)image["bufferView"]);
                var bytes = new byte[seg.Count];
                Buffer.BlockCopy(seg.Array, seg.Offset, bytes, 0, seg.Count);
                return textures[texIndex] = TextureFromBytes(bytes, (string)image["name"] ?? Path.GetFileNameWithoutExtension(path));
            }

            var mats = new List<Material>();
            foreach (JObject mt in (g["materials"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var pbr = mt["pbrMetallicRoughness"];
                Texture2D baseTex = pbr?["baseColorTexture"]?["index"] != null ? Tex((int)pbr["baseColorTexture"]["index"]) : null;
                var f = pbr?["baseColorFactor"] as JArray;
                Color col = f != null && f.Count >= 3 ? new Color((float)f[0], (float)f[1], (float)f[2], f.Count > 3 ? (float)f[3] : 1f) : Color.white;
                Material m = NewMaterial(baseTex, col);
                var em = mt["emissiveFactor"] as JArray;
                if (em != null && em.Count >= 3 && ((float)em[0] + (float)em[1] + (float)em[2]) > 0f && m.HasProperty("_EmissionColor"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", new Color((float)em[0], (float)em[1], (float)em[2]));
                    if (mt["emissiveTexture"]?["index"] != null) m.SetTexture("_EmissionMap", Tex((int)mt["emissiveTexture"]["index"]));
                }
                mats.Add(m);
            }
            if (mats.Count == 0) mats.Add(NewMaterial(null, Color.white));

            var meshes = new List<Mesh>();
            var partMats = new List<Material>();
            var nodes = g["nodes"] as JArray ?? new JArray();

            // ---- skins: the node tree (every node becomes a transform) and the skinned parts
            Rig rig = (g["skins"] as JArray)?.Count > 0 ? new Rig() : null;
            if (rig != null)
            {
                var parentOf = Enumerable.Repeat(-1, nodes.Count).ToArray();
                for (int i = 0; i < nodes.Count; i++)
                    foreach (JToken ch in nodes[i]["children"] as JArray ?? new JArray()) parentOf[(int)ch] = i;
                for (int i = 0; i < nodes.Count; i++)
                {
                    var n = nodes[i];
                    var t = n["translation"] as JArray; var q = n["rotation"] as JArray; var sc = n["scale"] as JArray;
                    Vector3 pos = t != null ? new Vector3(-(float)t[0], (float)t[1], (float)t[2]) : Vector3.zero;
                    Quaternion rot = q != null ? new Quaternion((float)q[0], -(float)q[1], -(float)q[2], (float)q[3]) : Quaternion.identity;
                    Vector3 scl = sc != null ? new Vector3((float)sc[0], (float)sc[1], (float)sc[2]) : Vector3.one;
                    rig.nodes.Add(($"n{i}_{(string)n["name"] ?? "node"}", parentOf[i], pos, rot, scl));
                }
            }

            void Skinned(JToken n, int skinIndex)
            {
                var skin = g["skins"][skinIndex];
                int[] joints = ((JArray)skin["joints"]).Select(j => (int)j).ToArray();
                float[] ibm = skin["inverseBindMatrices"] != null ? ReadAny((int)skin["inverseBindMatrices"]) : null;
                var bind = new Matrix4x4[joints.Length];
                for (int j = 0; j < joints.Length; j++)
                {
                    Matrix4x4 m = Matrix4x4.identity;
                    if (ibm != null) for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) m[r, c] = ibm[j * 16 + c * 4 + r];
                    for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) if ((r == 0) != (c == 0)) m[r, c] = -m[r, c]; // S * M * S, S mirrors X
                    bind[j] = m;
                }
                foreach (JObject prim in ((JArray)g["meshes"][(int)n["mesh"]]["primitives"]).OfType<JObject>())
                {
                    if (((int?)prim["mode"] ?? 4) != 4) continue;
                    var attrs = (JObject)prim["attributes"];
                    float[] pos = ReadFloats((int)attrs["POSITION"], 3);
                    float[] nrm = attrs["NORMAL"] != null ? ReadFloats((int)attrs["NORMAL"], 3) : null;
                    float[] uv = attrs["TEXCOORD_0"] != null ? ReadFloats((int)attrs["TEXCOORD_0"], 2) : null;
                    float[] jn = attrs["JOINTS_0"] != null ? ReadAny((int)attrs["JOINTS_0"]) : null;
                    float[] wt = attrs["WEIGHTS_0"] != null ? ReadAny((int)attrs["WEIGHTS_0"]) : null;
                    int vc = pos.Length / 3;
                    int[] idx = prim["indices"] != null ? ReadIndices((int)prim["indices"]) : Enumerable.Range(0, vc).ToArray();
                    var verts = new Vector3[vc]; var norms = nrm != null ? new Vector3[vc] : null; var uvs = uv != null ? new Vector2[vc] : null;
                    var bw = new BoneWeight[vc];
                    for (int i = 0; i < vc; i++)
                    {
                        verts[i] = new Vector3(-pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                        if (norms != null) norms[i] = new Vector3(-nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2]);
                        if (uvs != null) uvs[i] = new Vector2(uv[i * 2], 1f - uv[i * 2 + 1]);
                        if (jn == null || wt == null) { bw[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f }; continue; }
                        float w0 = wt[i * 4], w1 = wt[i * 4 + 1], w2 = wt[i * 4 + 2], w3 = wt[i * 4 + 3], sum = w0 + w1 + w2 + w3;
                        if (sum <= 0f) { w0 = 1f; sum = 1f; }
                        bw[i] = new BoneWeight
                        {
                            boneIndex0 = (int)jn[i * 4], weight0 = w0 / sum, boneIndex1 = (int)jn[i * 4 + 1], weight1 = w1 / sum,
                            boneIndex2 = (int)jn[i * 4 + 2], weight2 = w2 / sum, boneIndex3 = (int)jn[i * 4 + 3], weight3 = w3 / sum,
                        };
                    }
                    for (int i = 0; i + 2 < idx.Length; i += 3) { int t = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = t; }
                    var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) + "_skin" + rig.parts.Count };
                    if (vc > 65000) mesh.indexFormat = IndexFormat.UInt32;
                    mesh.vertices = verts;
                    if (norms != null) mesh.normals = norms;
                    if (uvs != null) mesh.uv = uvs;
                    mesh.triangles = idx;
                    mesh.boneWeights = bw;
                    mesh.bindposes = bind;
                    if (norms == null) mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    Object.DontDestroyOnLoad(mesh);
                    rig.parts.Add((mesh, mats[Math.Min((int?)prim["material"] ?? 0, mats.Count - 1)], joints));
                }
            }
            var scene = g["scenes"]?[(int?)g["scene"] ?? 0];
            void Walk(int ni, Matrix4x4 parent)
            {
                var n = nodes[ni];
                Matrix4x4 local = Matrix4x4.identity;
                if (n["matrix"] is JArray mx && mx.Count == 16)
                {
                    for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) local[r, c] = (float)mx[c * 4 + r];
                }
                else
                {
                    var t = n["translation"] as JArray; var q = n["rotation"] as JArray; var s = n["scale"] as JArray;
                    local = Matrix4x4.TRS(t != null ? new Vector3((float)t[0], (float)t[1], (float)t[2]) : Vector3.zero,
                        q != null ? new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]) : Quaternion.identity,
                        s != null ? new Vector3((float)s[0], (float)s[1], (float)s[2]) : Vector3.one);
                }
                Matrix4x4 world = parent * local;
                if (n["mesh"] != null && n["skin"] != null && rig != null) Skinned(n, (int)n["skin"]);
                else if (n["mesh"] != null)
                    foreach (JObject prim in ((JArray)g["meshes"][(int)n["mesh"]]["primitives"]).OfType<JObject>())
                    {
                        if (((int?)prim["mode"] ?? 4) != 4) continue;
                        var attrs = (JObject)prim["attributes"];
                        float[] pos = ReadFloats((int)attrs["POSITION"], 3);
                        float[] nrm = attrs["NORMAL"] != null ? ReadFloats((int)attrs["NORMAL"], 3) : null;
                        float[] uv = attrs["TEXCOORD_0"] != null ? ReadFloats((int)attrs["TEXCOORD_0"], 2) : null;
                        int vc = pos.Length / 3;
                        int[] idx = prim["indices"] != null ? ReadIndices((int)prim["indices"]) : Enumerable.Range(0, vc).ToArray();
                        var verts = new Vector3[vc]; var norms = nrm != null ? new Vector3[vc] : null; var uvs = uv != null ? new Vector2[vc] : null;
                        for (int i = 0; i < vc; i++)
                        {
                            // glTF is right-handed: mirror X for Unity, and flip V.
                            Vector3 p = world.MultiplyPoint3x4(new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]));
                            verts[i] = new Vector3(-p.x, p.y, p.z);
                            if (norms != null) { Vector3 q = world.MultiplyVector(new Vector3(nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2])).normalized; norms[i] = new Vector3(-q.x, q.y, q.z); }
                            if (uvs != null) uvs[i] = new Vector2(uv[i * 2], 1f - uv[i * 2 + 1]);
                        }
                        for (int i = 0; i + 2 < idx.Length; i += 3) { int t = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = t; }
                        var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) + "_" + meshes.Count };
                        if (vc > 65000) mesh.indexFormat = IndexFormat.UInt32;
                        mesh.vertices = verts;
                        if (norms != null) mesh.normals = norms;
                        if (uvs != null) mesh.uv = uvs;
                        mesh.triangles = idx;
                        if (norms == null) mesh.RecalculateNormals();
                        mesh.RecalculateBounds();
                        Object.DontDestroyOnLoad(mesh);
                        meshes.Add(mesh);
                        partMats.Add(mats[Math.Min((int?)prim["material"] ?? 0, mats.Count - 1)]);
                    }
                foreach (JToken ch in n["children"] as JArray ?? new JArray()) Walk((int)ch, world);
            }
            var roots = scene?["nodes"] as JArray ?? new JArray(Enumerable.Range(0, nodes.Count));
            foreach (JToken r in roots) Walk((int)r, Matrix4x4.identity);

            if (rig != null)
            {
                // Curves address nodes by their path from the model root.
                var paths = new string[nodes.Count];
                string PathOf(int i) => paths[i] ?? (paths[i] = (rig.nodes[i].parent >= 0 ? PathOf(rig.nodes[i].parent) + "/" : "") + rig.nodes[i].name);
                foreach (JObject an in (g["animations"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    var clip = new AnimationClip { legacy = true, name = (string)an["name"] ?? "clip" + rig.clips.Count };
                    var samplers = (JArray)an["samplers"];
                    foreach (JObject ch in ((JArray)an["channels"]).OfType<JObject>())
                    {
                        var tgt = ch["target"];
                        if (tgt?["node"] == null) continue;
                        string what = (string)tgt["path"];
                        int comps = what == "rotation" ? 4 : what == "translation" || what == "scale" ? 3 : 0;
                        if (comps == 0) continue;
                        var smp = samplers[(int)ch["sampler"]];
                        float[] times = ReadAny((int)smp["input"]);
                        float[] vals = ReadAny((int)smp["output"]);
                        string interp = (string)smp["interpolation"] ?? "LINEAR";
                        int stride = interp == "CUBICSPLINE" ? comps * 3 : comps, off = interp == "CUBICSPLINE" ? comps : 0;
                        string[] props = what == "translation" ? new[] { "localPosition.x", "localPosition.y", "localPosition.z" }
                            : what == "rotation" ? new[] { "localRotation.x", "localRotation.y", "localRotation.z", "localRotation.w" }
                            : new[] { "localScale.x", "localScale.y", "localScale.z" };
                        float[] sign = what == "translation" ? new[] { -1f, 1f, 1f } : what == "rotation" ? new[] { 1f, -1f, -1f, 1f } : new[] { 1f, 1f, 1f };
                        string nodePath = PathOf((int)tgt["node"]);
                        for (int c = 0; c < comps; c++)
                        {
                            var keys = new Keyframe[times.Length];
                            for (int i = 0; i < times.Length; i++) keys[i] = new Keyframe(times[i], vals[i * stride + off + c] * sign[c]);
                            for (int i = 0; i < keys.Length; i++)
                            {
                                if (interp == "STEP") { keys[i].inTangent = keys[i].outTangent = float.PositiveInfinity; continue; }
                                float before = i > 0 ? (keys[i].value - keys[i - 1].value) / Mathf.Max(1e-5f, keys[i].time - keys[i - 1].time) : 0f;
                                float after = i + 1 < keys.Length ? (keys[i + 1].value - keys[i].value) / Mathf.Max(1e-5f, keys[i + 1].time - keys[i].time) : 0f;
                                keys[i].inTangent = i > 0 ? before : after;
                                keys[i].outTangent = i + 1 < keys.Length ? after : before;
                            }
                            clip.SetCurve(nodePath, typeof(Transform), props[c], new AnimationCurve(keys));
                        }
                    }
                    clip.EnsureQuaternionContinuity();
                    string lname = clip.name.ToLowerInvariant();
                    clip.wrapMode = lname.Contains("death") || lname.Contains("die") ? WrapMode.ClampForever
                        : lname.Contains("idle") || lname.Contains("move") || lname.Contains("walk") || lname.Contains("run") ? WrapMode.Loop : WrapMode.Once;
                    Object.DontDestroyOnLoad(clip);
                    rig.clips.Add(clip);
                }
            }
            if (meshes.Count == 0 && (rig == null || rig.parts.Count == 0)) return null;
            return new Loaded { meshes = meshes.ToArray(), materials = partMats.ToArray(), rig = rig };
        }

        // Wavefront .obj: v / vt / vn / f (polygons fanned into triangles). The texture comes from "texture".
        private static Loaded LoadObj(string path)
        {
            var pv = new List<Vector3>(); var pt = new List<Vector2>(); var pn = new List<Vector3>();
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var norms = new List<Vector3>(); var tris = new List<int>();
            var seen = new Dictionary<string, int>();
            bool hasN = false;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length < 2 || line[0] == '#') continue;
                string[] a = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                float F(int i) => float.Parse(a[i], CultureInfo.InvariantCulture);
                switch (a[0])
                {
                    case "v": pv.Add(new Vector3(-F(1), F(2), F(3))); break;
                    case "vt": pt.Add(new Vector2(F(1), F(2))); break;
                    case "vn": pn.Add(new Vector3(-F(1), F(2), F(3))); break;
                    case "f":
                        var face = new List<int>();
                        for (int i = 1; i < a.Length; i++)
                        {
                            if (!seen.TryGetValue(a[i], out int vi))
                            {
                                string[] p = a[i].Split('/');
                                int Ix(string s, int n) => string.IsNullOrEmpty(s) ? -1 : (int.Parse(s, CultureInfo.InvariantCulture) is int k && k < 0 ? n + k : k - 1);
                                int v = Ix(p[0], pv.Count), t = p.Length > 1 ? Ix(p[1], pt.Count) : -1, n2 = p.Length > 2 ? Ix(p[2], pn.Count) : -1;
                                verts.Add(pv[v]); uvs.Add(t >= 0 ? pt[t] : Vector2.zero); norms.Add(n2 >= 0 ? pn[n2] : Vector3.up);
                                hasN |= n2 >= 0;
                                vi = verts.Count - 1; seen[a[i]] = vi;
                            }
                            face.Add(vi);
                        }
                        for (int i = 1; i + 1 < face.Count; i++) { tris.Add(face[0]); tris.Add(face[i + 1]); tris.Add(face[i]); }
                        break;
                }
            }
            if (tris.Count == 0) return null;
            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            if (hasN) mesh.SetNormals(norms); else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Object.DontDestroyOnLoad(mesh);
            return new Loaded { meshes = new[] { mesh }, materials = new[] { NewMaterial(null, Color.white) } };
        }

        // ---- loads ----------------------------------------------------------------------------------------------

        /// <summary>A loaded summon still carries its model tag; put the look back.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Deserialize))]
        private static class Patch_Restore
        {
            private static void Postfix(Character __instance)
            {
                if (__instance?.tags == null) return;
                foreach (Tag t in __instance.tags.ToList())
                    if (t?.tagName != null && t.tagName.StartsWith(TagPrefix) && registry.TryGetValue(t.tagName, out var m))
                        Apply(__instance.gameObject, m.spec, m.folder);
            }
        }
    }
}
