using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Worn gear on crew crawlers. The game draws no gear on characters (you play in first person), but every item
    /// has a 3D model for when it lies on the floor. A crawler wearing an item gets a copy of that model on the right
    /// bone: helmets on the head, gloves and boots on both hands and feet. Floor models lie flat and come in odd sizes,
    /// so each copy is stood up (its thinnest side facing forward) and sized to the body part. Chestpiece and legplate
    /// floor models are flat folded props, so instead the body's own torso or leg mesh takes the item's rarity colour.
    /// A weapon replaces the crawler's own weapon model, in its hand, sized by weapon type.
    /// Mod items can give an exact "wear" model and "wearBone" for any slot instead.
    /// </summary>
    internal static class Wear
    {
        private const string Tag = "SCCL_Wear";

        /// <summary>Size of each slot's piece as a share of the body's height.</summary>
        private static float Share(EQUIP_TYPE slot) => slot switch
        {
            EQUIP_TYPE.Head => 0.17f,
            EQUIP_TYPE.Torso => 0.30f,
            EQUIP_TYPE.Legs => 0.42f,
            EQUIP_TYPE.Hands => 0.08f,
            EQUIP_TYPE.Feet => 0.11f,
            _ => 0.2f,
        };

        private static readonly Dictionary<string, (HumanBodyBones bone, string[] names)> Bones = new Dictionary<string, (HumanBodyBones, string[])>
        {
            { "Head", (HumanBodyBones.Head, new[] { " head", "_head", "head" }) },
            { "Chest", (HumanBodyBones.Chest, new[] { "spine2", "spine_03", "spine1", "spine" }) },
            { "Hips", (HumanBodyBones.Hips, new[] { "pelvis", "hips" }) },
            { "LeftHand", (HumanBodyBones.LeftHand, new[] { " l hand", "hand_l" }) },
            { "RightHand", (HumanBodyBones.RightHand, new[] { " r hand", "hand_r" }) },
            { "LeftFoot", (HumanBodyBones.LeftFoot, new[] { " l foot", "foot_l" }) },
            { "RightFoot", (HumanBodyBones.RightFoot, new[] { " r foot", "foot_r" }) },
        };

        private static string[] SlotBones(EQUIP_TYPE slot) => slot switch
        {
            EQUIP_TYPE.Head => new[] { "Head" },
            EQUIP_TYPE.Torso => new[] { "Chest" },
            EQUIP_TYPE.Legs => new[] { "Hips" },
            EQUIP_TYPE.Hands => new[] { "LeftHand", "RightHand" },
            EQUIP_TYPE.Feet => new[] { "LeftFoot", "RightFoot" },
            _ => new string[0],
        };

        internal static Transform Bone(Component c, string name)
        {
            if (!Bones.TryGetValue(name, out var b)) return null;
            Animator an = c.GetComponentInChildren<Animator>(true);
            if (an != null && an.isHuman)
            {
                Transform t = an.GetBoneTransform(b.bone);
                if (t == null && b.bone == HumanBodyBones.Chest) t = an.GetBoneTransform(HumanBodyBones.Spine);
                if (t != null) return t;
            }
            var all = c.GetComponentsInChildren<Transform>(true);
            foreach (string n in b.names)
            {
                Transform t = all.FirstOrDefault(x => x.name.ToLowerInvariant().EndsWith(n.Trim()) && (n.StartsWith(" ") ? x.name.ToLowerInvariant().Contains(n) : true));
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>The crawler's own weapon models (they go when an item weapon comes in).</summary>
        internal static List<Renderer> OwnWeapons(Component c)
        {
            var hands = new[] { Bone(c, "LeftHand"), Bone(c, "RightHand") }.Where(h => h != null).ToList();
            return c.GetComponentsInChildren<Renderer>(true)
                .Where(r => !(r is ParticleSystemRenderer) && !r.name.StartsWith(Tag) && r.GetComponentInParent<WearMark>() == null)
                .Where(r => hands.Any(h => r.transform.IsChildOf(h)) || r.name.ToLowerInvariant().Contains("weapon") || r.name.ToLowerInvariant().Contains("katana") || r.name.ToLowerInvariant().Contains("hammer"))
                .Where(r => !(r is SkinnedMeshRenderer s) || s.bones.Length <= 2)
                .ToList();
        }

        /// <summary>Marks our copies so they're never mistaken for the body's own parts.</summary>
        private class WearMark : MonoBehaviour { public bool hidOwn; }

        /// <summary>Re-dresses a crawler from its gear.</summary>
        internal static void Dress(Component c, Crew.Member m) => Dress(c, slot => Crew.Peek(m, slot), m.KindId, m.Name);

        /// <summary>
        /// Re-dresses any body from a gear lookup (crew, Conjoined bodies, ...). peek returns a fresh Item per slot or
        /// null; Dress destroys each one it gets. kindId matches a wear entry's "kinds".
        /// </summary>
        internal static void Dress(Component c, System.Func<EQUIP_TYPE, Item> peek, string kindId, string who)
        {
            if (c == null) return;
            foreach (WearMark old in c.GetComponentsInChildren<WearMark>(true).ToList()) Object.Destroy(old.gameObject);
            Untint(c);
            var own = OwnWeapons(c);
            foreach (Renderer r in own) r.enabled = true;

            float height = BodyHeight(c);
            foreach (EQUIP_TYPE slot in Crew.Slots)
            {
                Item it = peek(slot);
                if (it == null) continue;
                try
                {
                    ItemRegistry.Defs.TryGetValue(it.itemId, out ItemDef def);
                    if (slot == EQUIP_TYPE.Weapon) DressWeapon(c, it, def, own);
                    else if (def?.wear != null) Exact(c, def, slot);
                    else if (Find(it.itemId) is WearEntry we && (we.kinds.Count == 0 || we.kinds.Contains(kindId))) FitEntry(c, we, slot, height);
                    else if (slot == EQUIP_TYPE.Torso || slot == EQUIP_TYPE.Legs) Tint(c, slot, it);
                    else foreach (string bone in SlotBones(slot)) Fit(c, it.worldObject, Bone(c, bone), slot, height, bone.StartsWith("Left"));
                }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Wear {it.itemId} on {who}: {e.Message}"); }
                finally { Object.Destroy(it.gameObject); }
            }
        }

        private static float BodyHeight(Component c)
        {
            var rs = c.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.bones.Length > 2).ToList();
            if (rs.Count == 0) return 1.8f;
            Bounds b = rs[0].bounds;
            foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
            return Mathf.Clamp(b.size.y, 0.8f, 4f);
        }

        /// <summary>A bare copy of an item's model: meshes and materials only (no colliders, scripts, lights).</summary>
        private static GameObject Copy(GameObject src, Transform parent)
        {
            if (src == null) return null;
            var pivot = new GameObject(Tag);
            pivot.AddComponent<WearMark>();
            pivot.transform.SetParent(parent, false);
            foreach (MeshFilter mf in src.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;
                var part = new GameObject(Tag + "_part");
                part.transform.SetParent(pivot.transform, false);
                // keep each part where it sits relative to the model's root
                part.transform.localPosition = src.transform.InverseTransformPoint(mf.transform.position);
                part.transform.localRotation = Quaternion.Inverse(src.transform.rotation) * mf.transform.rotation;
                part.transform.localScale = Div(mf.transform.lossyScale, src.transform.lossyScale);
                part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = part.AddComponent<MeshRenderer>();
                r.sharedMaterials = mr.sharedMaterials;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            foreach (SkinnedMeshRenderer smr in src.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                var part = new GameObject(Tag + "_part");
                part.transform.SetParent(pivot.transform, false);
                part.transform.localPosition = src.transform.InverseTransformPoint(smr.transform.position);
                part.transform.localRotation = Quaternion.Inverse(src.transform.rotation) * smr.transform.rotation;
                part.transform.localScale = Div(smr.transform.lossyScale, src.transform.lossyScale);
                part.AddComponent<MeshFilter>().sharedMesh = smr.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
            }
            if (pivot.transform.childCount == 0) { Object.Destroy(pivot); return null; }
            return pivot;
        }

        private static Vector3 Div(Vector3 a, Vector3 b) => new Vector3(a.x / NZ(b.x), a.y / NZ(b.y), a.z / NZ(b.z));
        private static float NZ(float v) => Mathf.Abs(v) < 1e-5f ? 1f : v;

        /// <summary>Bounds of a pivot's parts in the pivot's own space.</summary>
        private static Bounds LocalBounds(GameObject pivot)
        {
            bool first = true;
            var b = new Bounds();
            foreach (MeshFilter mf in pivot.GetComponentsInChildren<MeshFilter>(true))
            {
                Bounds m = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3((i & 1) == 0 ? m.min.x : m.max.x, (i & 2) == 0 ? m.min.y : m.max.y, (i & 4) == 0 ? m.min.z : m.max.z);
                    Vector3 p = pivot.transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        private static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        /// <summary>Stands a floor model up on a bone: thinnest side forward, longest side up (along the foot for boots and gloves).</summary>
        private static GameObject Fit(Component c, GameObject src, Transform bone, EQUIP_TYPE slot, float height, bool mirror, float share = -1f, float turn = 0f, bool flip = false)
        {
            if (bone == null || src == null) return null;
            GameObject pivot = Copy(src, bone);
            if (pivot == null) return null;
            // measure unrotated, unscaled
            pivot.transform.localRotation = Quaternion.identity;
            pivot.transform.localScale = Vector3.one;
            Transform holder = new GameObject(Tag + "_fit").transform;
            holder.SetParent(pivot.transform, false);
            foreach (Transform part in pivot.transform.Cast<Transform>().Where(t => t != holder).ToList()) part.SetParent(holder, true);
            holder.gameObject.AddComponent<WearMark>();
            Bounds lb = LocalBounds(pivot);
            float[] ext = { lb.size.x, lb.size.y, lb.size.z };
            int[] order = new[] { 0, 1, 2 }.OrderBy(i => ext[i]).ToArray(); // thin, mid, long
            // near-square pieces (a chest with wide pauldrons) keep the model's own up instead of tipping over
            if (order[1] == 1 && ext[1] >= 0.9f * ext[order[2]]) { order[1] = order[2]; order[2] = 1; }
            Vector3 thin = Axis(order[0]), longest = Axis(order[2]);

            Transform body = c.transform;
            bool alongFoot = slot == EQUIP_TYPE.Feet || slot == EQUIP_TYPE.Hands;
            Vector3 fwd = alongFoot ? body.up : body.forward;
            Vector3 up = alongFoot ? body.forward : body.up;
            Quaternion local = Quaternion.Inverse(Quaternion.LookRotation(thin, longest));
            // the holder is centred, turned and sized; the pivot follows the bone
            holder.localPosition = -(local * lb.center);
            holder.localRotation = local;
            pivot.transform.rotation = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(0f, turn, flip ? 180f : 0f);
            float k = height * (share > 0f ? share : Share(slot)) / Mathf.Max(1e-4f, ext[order[2]]);
            Vector3 ls = bone.lossyScale;
            pivot.transform.localScale = new Vector3(k / NZ(ls.x), k / NZ(ls.y), k / NZ(ls.z)) * (mirror ? 1f : 1f);
            pivot.transform.position = bone.position + SlotNudge(slot, body, height);
            return pivot;
        }

        /// <summary>
        /// A floor model posed by hand for a picture: centred on position, its longest side at size. Its thinnest side faces
        /// rotation's forward, and its longest side runs along rotation's up, or its right when across (glasses).
        /// </summary>
        internal static GameObject Placed(GameObject src, Transform parent, Vector3 position, Quaternion rotation, float size, bool across = false)
        {
            GameObject pivot = Copy(src, parent);
            if (pivot == null) return null;
            pivot.transform.localRotation = Quaternion.identity;
            pivot.transform.localScale = Vector3.one;
            Transform holder = new GameObject(Tag + "_fit").transform;
            holder.SetParent(pivot.transform, false);
            foreach (Transform part in pivot.transform.Cast<Transform>().Where(t => t != holder).ToList()) part.SetParent(holder, true);
            Bounds lb = LocalBounds(pivot);
            float[] ext = { lb.size.x, lb.size.y, lb.size.z };
            int[] order = new[] { 0, 1, 2 }.OrderBy(i => ext[i]).ToArray();
            Quaternion local = Quaternion.Inverse(Quaternion.LookRotation(Axis(order[0]), Axis(across ? order[1] : order[2])));
            holder.localPosition = -(local * lb.center);
            holder.localRotation = local;
            pivot.transform.rotation = rotation;
            float k = size / Mathf.Max(1e-4f, ext[order[2]]);
            Vector3 ls = parent.lossyScale;
            pivot.transform.localScale = new Vector3(k / NZ(ls.x), k / NZ(ls.y), k / NZ(ls.z));
            pivot.transform.position = position;
            return pivot;
        }

        /// <summary>
        /// A held model with its grip on position and the rest running away from it along rotation's up: Placed, but hung from
        /// a point on the model (in src's space) rather than its middle, at least gripShare of the way along it.
        /// </summary>
        internal static GameObject Gripped(GameObject src, Vector3 grip, float gripShare, Transform parent, Vector3 position, Quaternion rotation, float size)
        {
            GameObject pivot = Copy(src, parent);
            if (pivot == null) return null;
            pivot.transform.localRotation = Quaternion.identity;
            pivot.transform.localScale = Vector3.one;
            Transform holder = new GameObject(Tag + "_fit").transform;
            holder.SetParent(pivot.transform, false);
            foreach (Transform part in pivot.transform.Cast<Transform>().Where(t => t != holder).ToList()) part.SetParent(holder, true);
            Bounds lb = LocalBounds(pivot);
            float[] ext = { lb.size.x, lb.size.y, lb.size.z };
            int[] order = new[] { 0, 1, 2 }.OrderBy(i => ext[i]).ToArray();
            Vector3 along = Axis(order[2]), hold = lb.ClosestPoint(grip);
            if (Vector3.Dot(lb.center - hold, along) < 0f) along = -along;
            hold += along * Mathf.Max(0f, ext[order[2]] * (gripShare - 0.5f) - Vector3.Dot(hold - lb.center, along)); // no holding a rifle by the butt
            Quaternion local = Quaternion.Inverse(Quaternion.LookRotation(Axis(order[0]), along));
            holder.localPosition = -(local * hold);
            holder.localRotation = local;
            pivot.transform.rotation = rotation;
            float k = size / Mathf.Max(1e-4f, ext[order[2]]);
            Vector3 ls = parent.lossyScale;
            pivot.transform.localScale = new Vector3(k / NZ(ls.x), k / NZ(ls.y), k / NZ(ls.z));
            pivot.transform.position = position;
            return pivot;
        }

        /// <summary>Where on the bone the piece sits (heads are above the neck joint, legplates below the hips).</summary>
        private static Vector3 SlotNudge(EQUIP_TYPE slot, Transform body, float h) => slot switch
        {
            EQUIP_TYPE.Head => body.up * (0.05f * h),
            EQUIP_TYPE.Torso => body.up * (0.07f * h) + body.forward * (0.015f * h),
            EQUIP_TYPE.Legs => -body.up * (0.2f * h),
            _ => Vector3.zero,
        };

        /// <summary>An item weapon in place of the crawler's own: same parent, same spot, same length.</summary>
        private static void DressWeapon(Component c, Item it, ItemDef def, List<Renderer> own)
        {
            Renderer main = own.OrderByDescending(r => r.bounds.size.magnitude).FirstOrDefault();
            Transform hand = main != null ? main.transform : Bone(c, "RightHand");
            if (hand == null) return;
            GameObject src = def?.wear != null ? null : it.worldObject;
            GameObject pivot = def?.wear != null ? ModelAssets.Spawn(def.wear, def.pack.folder, hand.position, hand) : Copy(src, hand.parent ?? hand);
            if (pivot == null) return;
            if (pivot.GetComponent<WearMark>() == null) pivot.AddComponent<WearMark>();
            foreach (Renderer r in own) r.enabled = false;
            if (def?.wear != null) return;

            pivot.transform.position = hand.position;
            pivot.transform.localScale = Vector3.one;
            pivot.transform.rotation = Quaternion.identity;
            Transform holder = new GameObject(Tag + "_fit").transform;
            holder.SetParent(pivot.transform, false);
            foreach (Transform part in pivot.transform.Cast<Transform>().Where(t => t != holder).ToList()) part.SetParent(holder, true);
            Bounds lb = LocalBounds(pivot);
            float[] ext = { lb.size.x, lb.size.y, lb.size.z };
            int longI = new[] { 0, 1, 2 }.OrderBy(i => ext[i]).Last();
            // the old weapon's length and long axis
            Bounds ob = main != null ? main.bounds : new Bounds(hand.position, Vector3.one * 0.8f);
            Vector3 oldLong = main is SkinnedMeshRenderer || main is MeshRenderer ? LongAxisWorld(main) : hand.forward;
            float oldLen = Mathf.Max(ob.size.x, Mathf.Max(ob.size.y, ob.size.z));
            holder.localPosition = -lb.center;
            pivot.transform.rotation = Quaternion.FromToRotation(Axis(longI), oldLong);
            // sized by the kind of weapon, not the body's old one (a pistol stays a pistol)
            float height = BodyHeight(c);
            var wt = (it as Item_Weapon)?.weaponType ?? WEAPON_TYPE.Ranged;
            float len = height * (wt == WEAPON_TYPE.Ranged ? 0.16f : wt == WEAPON_TYPE.HeavyRanged ? 0.48f : wt == WEAPON_TYPE.Melee ? 0.42f : wt == WEAPON_TYPE.HeavyMelee ? 0.55f : 0.25f);
            float k = len / Mathf.Max(1e-4f, ext[longI]);
            Vector3 ls = pivot.transform.parent.lossyScale;
            pivot.transform.localScale = new Vector3(k / NZ(ls.x), k / NZ(ls.y), k / NZ(ls.z));
            pivot.transform.position = main != null ? main.bounds.center : hand.position;
        }

        private static Vector3 LongAxisWorld(Renderer r)
        {
            Mesh mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return r.transform.forward;
            Vector3 e = mesh.bounds.size;
            Vector3 local = e.x >= e.y && e.x >= e.z ? Vector3.right : e.y >= e.z ? Vector3.up : Vector3.forward;
            return r.transform.TransformDirection(local).normalized;
        }

        // ---- chest and legs: the body's own parts take the item's colour ----------------------------------------

        /// <summary>Original colours of tinted body parts, to undo.</summary>
        private static readonly Dictionary<Renderer, Color[]> tinted = new Dictionary<Renderer, Color[]>();

        /// <summary>
        /// Floor models of chestpieces and legplates are flat, folded props that read as panels on a body, so they
        /// aren't hung on: where the body has its own torso or leg mesh, that part takes the item's rarity colour.
        /// </summary>
        private static void Tint(Component c, EQUIP_TYPE slot, Item it)
        {
            string[] keys = slot == EQUIP_TYPE.Torso ? new[] { "torso", "body", "chest", "shoulder" } : new[] { "leg" };
            Color col = ItemDatabase.instance.GetRarityColor(it.rarity);
            foreach (SkinnedMeshRenderer r in c.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string n = r.name.ToLowerInvariant();
                if (!keys.Any(n.Contains) || (slot == EQUIP_TYPE.Torso && n.Contains("leg"))) continue;
                Material[] mats = r.materials; // instances, this crawler only
                if (!tinted.ContainsKey(r)) tinted[r] = mats.Select(m => m.HasProperty("_Color") ? m.color : Color.white).ToArray();
                foreach (Material m in mats) if (m.HasProperty("_Color")) m.color = Color.Lerp(Color.white, col, 0.55f);
            }
        }

        /// <summary>Colours the body part as the item really looks, for a picture of a body that's about to go.</summary>
        internal static void Paint(Component c, EQUIP_TYPE slot, Color col)
        {
            string[] keys = slot == EQUIP_TYPE.Torso ? new[] { "torso", "body", "chest", "shoulder", "pauldron" } : new[] { "leg" };
            foreach (SkinnedMeshRenderer r in c.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string n = r.name.ToLowerInvariant();
                tinted.Remove(r);
                if (!keys.Any(n.Contains) || (slot == EQUIP_TYPE.Torso && n.Contains("leg"))) continue;
                foreach (Material m in r.materials) if (m.HasProperty("_Color")) m.color = Color.Lerp(Color.white, col, 0.85f);
            }
        }

        private static void Untint(Component c)
        {
            foreach (SkinnedMeshRenderer r in c.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!tinted.TryGetValue(r, out Color[] cols)) continue;
                Material[] mats = r.materials;
                for (int i = 0; i < mats.Length && i < cols.Length; i++) if (mats[i].HasProperty("_Color")) mats[i].color = cols[i];
                tinted.Remove(r);
            }
        }

        // ---- wear models for vanilla gear (a mod's "wear" list) -------------------------------------------------

        /// <summary>One entry of a mod's "wear" list: which items (ids or id prefixes) wear this mesh.</summary>
        public class WearEntry
        {
            public List<string> items = new List<string>();
            public string mesh;
            /// <summary>Its longest side as a share of the body's height (default by slot).</summary>
            public float share = -1f;
            /// <summary>Degrees around the up axis, and upside down, for meshes that come out facing the wrong way.</summary>
            public float turn;
            public bool flip;
            /// <summary>Crew kinds whose bodies take this piece well (empty: every kind); the rest keep the rarity tint.</summary>
            public List<string> kinds = new List<string>();
            internal string folder;
        }

        private static readonly List<WearEntry> entries = new List<WearEntry>();

        internal static void Load()
        {
            entries.Clear();
            foreach (ModPack p in ModPacks.Ordered)
                foreach (var (file, obj) in p.data)
                    foreach (var e in (obj["wear"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray()).OfType<Newtonsoft.Json.Linq.JObject>())
                    {
                        try { var w = e.ToObject<WearEntry>(); w.folder = p.folder; entries.Insert(0, w); } // later mods first
                        catch (System.Exception ex) { Plugin.Log.LogError($"{file}: a wear entry could not be read: {ex.Message}"); }
                    }
            if (entries.Count > 0) Plugin.Log.LogInfo($"Wear: {entries.Count} wearable models for {entries.Sum(x => x.items.Count)} item ids/prefixes");
        }

        /// <summary>The entry for an item: an exact id beats the longest matching prefix.</summary>
        internal static WearEntry Find(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            WearEntry best = null; int bestLen = -1;
            foreach (WearEntry e in entries)
                foreach (string k in e.items)
                {
                    int len = k == itemId ? 10000 : itemId.StartsWith(k, System.StringComparison.OrdinalIgnoreCase) ? k.Length : -1;
                    if (len > bestLen) { best = e; bestLen = len; }
                }
            return best;
        }

        private static void FitEntry(Component c, WearEntry e, EQUIP_TYPE slot, float height)
        {
            GameObject model = ModelAssets.Spawn(new ModelSpec { mesh = e.mesh, keepParticles = false }, e.folder, c.transform.position, null);
            if (model == null) return;
            try
            {
                foreach (string bone in SlotBones(slot))
                {
                    GameObject placed = Fit(c, model, Bone(c, bone), slot, height, false, e.share, e.turn, e.flip);
                    if (placed != null && Skin(c, placed, height)) Object.Destroy(placed);
                }
            }
            finally { Object.Destroy(model); }
        }

        private static bool warnedUnreadable;

        /// <summary>
        /// Turns a placed piece into part of the body: every point is pulled onto the crawler's own posed skin (a thin
        /// gap above it, keeping a little of the piece's thickness), then bound to the bones that move that skin, so the
        /// armour hugs this crawler's shape and bends with its animation. False (piece stays rigid) when the body mesh
        /// can't be read.
        /// </summary>
        private static bool Skin(Component c, GameObject placed, float height)
        {
            var bodies = c.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.enabled && r.bones.Length > 2 && r.sharedMesh != null && r.GetComponentInParent<WearMark>() == null).ToList();
            if (bodies.Any(r => !r.sharedMesh.isReadable))
            {
                if (!warnedUnreadable) Plugin.Log.LogInfo($"Wear: a body mesh of {c.name} can't be read; pieces skip it");
                warnedUnreadable = true;
                bodies = bodies.Where(r => r.sharedMesh.isReadable).ToList();
            }
            if (bodies.Count == 0) return false;

            // the posed skin in world space, and which bones move each point
            var pts = new List<Vector3>(); var nrm = new List<Vector3>(); var src = new List<(int body, BoneWeight w)>();
            var baked = new Mesh();
            for (int bi = 0; bi < bodies.Count; bi++)
            {
                SkinnedMeshRenderer r = bodies[bi];
                BoneWeight[] bw = r.sharedMesh.boneWeights;
                r.BakeMesh(baked, true);
                Vector3[] v = baked.vertices, n = baked.normals;
                if (bw.Length != v.Length || n.Length != v.Length) continue;
                Matrix4x4 M = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                for (int i = 0; i < v.Length; i++) { pts.Add(M.MultiplyPoint3x4(v[i])); nrm.Add(M.MultiplyVector(n[i]).normalized); src.Add((bi, bw[i])); }
            }
            Object.Destroy(baked);
            if (pts.Count == 0) return false;

            float cell = 0.04f * height;
            var grid = new Dictionary<Vector3Int, List<int>>();
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
            for (int i = 0; i < pts.Count; i++) { var k = Key(pts[i]); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>(); l.Add(i); }
            int Nearest(Vector3 p)
            {
                Vector3Int k0 = Key(p); int best = -1; float bd = float.MaxValue;
                for (int ring = 0; ring <= 8; ring++)
                {
                    for (int x = -ring; x <= ring; x++) for (int y = -ring; y <= ring; y++) for (int z = -ring; z <= ring; z++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) != ring) continue;
                        if (!grid.TryGetValue(new Vector3Int(k0.x + x, k0.y + y, k0.z + z), out var l)) continue;
                        foreach (int i in l) { float d = (pts[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
                    }
                    if (best >= 0 && Mathf.Sqrt(bd) < ring * cell) break;
                }
                return best;
            }

            var bones = new List<Transform>(); var boneIx = new Dictionary<Transform, int>();
            int Ix(Transform t) { if (!boneIx.TryGetValue(t, out int i)) { i = bones.Count; bones.Add(t); boneIx[t] = i; } return i; }
            float gap = 0.012f * height, keep = 0.15f; // a thin gap over the skin, and 15% of the piece's own thickness

            bool any = false;
            foreach (MeshFilter mf in placed.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh; MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (m == null || mr == null || !m.isReadable) continue;
                var go = new GameObject(Tag + "_skinned"); go.AddComponent<WearMark>();
                go.transform.SetParent(c.transform, false);
                Matrix4x4 W = mf.transform.localToWorldMatrix, R = go.transform.worldToLocalMatrix;
                Vector3[] v = m.vertices, n = m.normals;
                var outV = new Vector3[v.Length]; var outN = new Vector3[v.Length]; var outW = new BoneWeight[v.Length];
                Transform fallback = bodies[0].rootBone ?? bodies[0].bones[0];
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 p = W.MultiplyPoint3x4(v[i]);
                    if (n.Length == v.Length) outN[i] = R.MultiplyVector(W.MultiplyVector(n[i])).normalized;
                    int k = Nearest(p);
                    if (k < 0) { outV[i] = R.MultiplyPoint3x4(p); outW[i] = new BoneWeight { boneIndex0 = Ix(fallback), weight0 = 1f }; continue; }
                    // straight toward the nearest skin point (moving along its normal fans edge points out into fins)
                    Vector3 off = p - pts[k];
                    float d = off.magnitude;
                    Vector3 dir = Vector3.Dot(off, nrm[k]) > 0f && d > 1e-5f ? off / d : nrm[k];
                    if (Vector3.Dot(off, nrm[k]) <= 0f) d = 0f; // inside the body
                    float want = d < gap ? gap : gap + (d - gap) * keep;
                    outV[i] = R.MultiplyPoint3x4(pts[k] + dir * want);
                    var (bi, w) = src[k]; Transform[] bb = bodies[bi].bones;
                    outW[i] = new BoneWeight
                    {
                        boneIndex0 = Ix(bb[w.boneIndex0]), weight0 = w.weight0,
                        boneIndex1 = w.weight1 > 0f ? Ix(bb[w.boneIndex1]) : 0, weight1 = w.weight1,
                        boneIndex2 = w.weight2 > 0f ? Ix(bb[w.boneIndex2]) : 0, weight2 = w.weight2,
                        boneIndex3 = w.weight3 > 0f ? Ix(bb[w.boneIndex3]) : 0, weight3 = w.weight3,
                    };
                }
                var mesh = new Mesh { name = m.name + "_worn", indexFormat = m.indexFormat };
                mesh.vertices = outV;
                if (n.Length == v.Length) mesh.normals = outN;
                mesh.uv = m.uv;
                mesh.subMeshCount = m.subMeshCount;
                for (int s = 0; s < m.subMeshCount; s++) mesh.SetTriangles(m.GetTriangles(s), s);
                mesh.boneWeights = outW;
                Matrix4x4 G = go.transform.localToWorldMatrix;
                mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * G).ToArray();
                mesh.RecalculateBounds();
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.bones = bones.ToArray();
                smr.rootBone = bodies[0].rootBone;
                smr.sharedMesh = mesh;
                smr.sharedMaterials = mr.sharedMaterials;
                smr.updateWhenOffscreen = true;
                any = true;
            }
            if (any) Plugin.Log.LogInfo($"Wear: skinned a piece onto {c.name} ({pts.Count} skin points, {bones.Count} bones)");
            return any;
        }

        /// <summary>A mod item's own wearable model, exactly where the item says.</summary>
        private static void Exact(Component c, ItemDef def, EQUIP_TYPE slot)
        {
            var bones = string.IsNullOrEmpty(def.wearBone) ? SlotBones(slot) : new[] { def.wearBone };
            foreach (string name in bones)
            {
                Transform bone = Bone(c, name);
                if (bone == null) continue;
                GameObject go = ModelAssets.Spawn(def.wear, def.pack.folder, bone.position, bone);
                if (go == null) continue;
                go.AddComponent<WearMark>();
                go.transform.rotation = c.transform.rotation;
            }
        }
    }
}
