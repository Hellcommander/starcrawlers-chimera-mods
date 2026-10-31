using System.IO;
using System.Linq;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// A character picture rendered by the game itself, on the GPU, with no AI model: your class's crawler body wearing the
    /// 3D models of what you have equipped, in a ready pose with both hands' gear, the stowed weapon on the back, armour in
    /// its real colour and tentacle arms if you have them, lit and shot by an off-screen camera far from the level, with the
    /// room you're standing in blurred behind. It takes one frame, and the picture lands in the Portraits folder like any other.
    /// </summary>
    internal static class PortraitRender
    {
        private const int Size = 768, Layer = 31;
        private const float Fov = 24f;
        private static readonly Vector3 Stage = new Vector3(0f, 5000f, 0f);

        private static Item_Equipment Equipped(EQUIP_TYPE slot) => Menu_Player.instance?.GetEquipment().FirstOrDefault(e => e != null && e.equipType == slot);

        /// <summary>Head items that don't fit a helmeted body yet: left out of the picture until they can sit on a face.</summary>
        private static bool LeftOut(Item it) => it != null && System.Text.RegularExpressions.Regex.IsMatch(
            it.GetName(false) + " " + it.GetDescription(), @"\b(glasses|goggles|specs|spectacles|shades|monocle|visor|lens|lenses|mask|headset|headphones|earpiece)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>What Wear puts on by itself, as a fresh copy it destroys when done: hats, gloves, boots. The rest is posed here.</summary>
        private static Item Worn(EQUIP_TYPE slot)
        {
            if (slot == EQUIP_TYPE.Weapon) return null;
            Item_Equipment on = Equipped(slot);
            return on == null || LeftOut(on) ? null : ItemDatabase.instance.GetItem(on.itemId);
        }

        /// <summary>The ready pose among the body's own animations, or the idle it already has.</summary>
        private static void Pose(GameObject body, Animator anim)
        {
            AnimationClip[] clips = anim.runtimeAnimatorController?.animationClips ?? new AnimationClip[0];
            foreach (string name in new[] { "Combat_Idle", "Alert", "Idle" })
            {
                AnimationClip clip = clips.FirstOrDefault(c => c.name.EndsWith(name));
                if (clip == null) continue;
                clip.SampleAnimation(anim.gameObject, clip.length * 0.3f);
                return;
            }
        }

        /// <summary>A weapon's length in the picture, as a share of the body: bigger than in hand-to-hand, so it reads at this size.</summary>
        private static float Length(Item_Weapon w) => w.weaponType == WEAPON_TYPE.Shield ? 0.15f
            : w.weaponType == WEAPON_TYPE.Ranged ? 0.17f : w.weaponType == WEAPON_TYPE.HeavyRanged ? 0.36f : w.weaponType == WEAPON_TYPE.Melee ? 0.32f : 0.4f;

        /// <summary>The model the game shows in your hands, the first-person one; the floor model only if a weapon has none.</summary>
        private static GameObject Held(Item_Weapon w) => w.weaponObject != null ? w.weaponObject : w.worldObject;

        /// <summary>
        /// A copy of a weapon at the end of its idle animation, not the stiff bind pose plants have in the file. Skinned parts
        /// are baked to plain meshes, since Wear copies meshes as they sit in the file. The copy stays inactive, so no script runs.
        /// </summary>
        private static GameObject Pose(GameObject model, Transform hidden)
        {
            GameObject copy = Object.Instantiate(model, hidden);
            foreach (Animator anim in copy.GetComponentsInChildren<Animator>(true))
            {
                AnimationClip clip = anim.runtimeAnimatorController?.animationClips.FirstOrDefault();
                if (clip != null) clip.SampleAnimation(anim.gameObject, clip.length * 0.98f);
            }
            foreach (SkinnedMeshRenderer skin in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                var mesh = new Mesh { name = "SCCL_PortraitBake" };
                skin.BakeMesh(mesh);
                Baked.Add(mesh);
                var part = new GameObject(skin.name);
                part.transform.SetParent(copy.transform, false);
                part.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                Vector3 up = copy.transform.lossyScale;
                part.transform.localScale = new Vector3(1f / up.x, 1f / up.y, 1f / up.z);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                Object.DestroyImmediate(skin);
            }
            return copy;
        }

        private static readonly System.Collections.Generic.List<Mesh> Baked = new System.Collections.Generic.List<Mesh>();

        /// <summary>Plants with a mouth aim it where you look, rather than standing up like a blade; vines stay upright, or they'd shrink to a stub.</summary>
        private static bool Plant(Item_Weapon w) => Held(w).GetComponentsInChildren<Animator>(true)
            .Any(a => a.runtimeAnimatorController != null && a.runtimeAnimatorController.animationClips.Any(c => c.name.StartsWith("Enemy_Plant")));

        /// <summary>Hangs a weapon from the spot the game holds it by: the item's origin sits on the first-person hand.</summary>
        private static void Hold(Item_Weapon w, Transform parent, Vector3 position, Quaternion rotation, float size)
        {
            GameObject model = Held(w);
            var hidden = new GameObject("SCCL_PortraitHold");
            hidden.SetActive(false);
            GameObject posed = Pose(model, hidden.transform);
            float share = w.weaponType == WEAPON_TYPE.Ranged || w.weaponType == WEAPON_TYPE.HeavyRanged ? 0.3f : 0.08f;
            Wear.Gripped(posed, model.transform.InverseTransformPoint(w.transform.position), share, parent, position, rotation, size);
            Object.DestroyImmediate(hidden);
        }

        private static void DressHands(GameObject body, float height)
        {
            Player p = Player.instance;
            Transform root = body.transform;
            foreach (Renderer own in Wear.OwnWeapons(root)) own.enabled = false;
            Transform right = Wear.Bone(root, "RightHand"), left = Wear.Bone(root, "LeftHand"), chest = Wear.Bone(root, "Chest");
            if (p.mainHandWeapon != null && right != null)
            {
                Quaternion tilt = root.rotation * (Plant(p.mainHandWeapon) ? Quaternion.Euler(70f, 0f, -12f) : Quaternion.Euler(0f, 0f, -12f));
                float size = height * Length(p.mainHandWeapon) * (Plant(p.mainHandWeapon) ? 0.75f : 1f); // nearer the camera, so it looks bigger
                Hold(p.mainHandWeapon, right, right.position, tilt, size);
            }
            if (p.offhandWeapon != null && p.offhandWeapon != p.mainHandWeapon && left != null)
                Wear.Placed(Held(p.offhandWeapon), left, left.position - root.right * height * 0.13f + root.forward * height * 0.03f, root.rotation * Quaternion.Euler(0f, -18f, 0f), height * Length(p.offhandWeapon));
            if (chest != null && typeof(Menu_Player).GetField("weaponSet_mainhand", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(Menu_Player.instance) is Item_Weapon stowed)
                Hold(stowed, chest, chest.position - root.forward * height * 0.1f - root.up * height * 0.02f + root.right * height * 0.1f, root.rotation * Quaternion.Euler(0f, 0f, 35f), height * (stowed.weaponType == WEAPON_TYPE.Ranged ? 0.4f : 0.55f));
        }

        /// <summary>The item's own colour: the mean of its icon's solid pixels.</summary>
        private static Color? IconColor(Item it)
        {
            Sprite sprite = it?.GetInventorySprite();
            if (sprite == null) return null;
            Rect r = sprite.textureRect;
            Texture t = sprite.texture;
            RenderTexture rt = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture was = RenderTexture.active;
            Graphics.Blit(t, rt, new Vector2(r.width / t.width, r.height / t.height), new Vector2(r.x / t.width, r.y / t.height));
            RenderTexture.active = rt;
            var small = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            small.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            Color[] solid = small.GetPixels().Where(c => c.a > 0.5f).ToArray();
            Object.Destroy(small);
            if (solid.Length == 0) return null;
            Color sum = solid.Aggregate(Color.clear, (x, y) => x + y) / solid.Length;
            sum.a = 1f;
            return sum;
        }

        /// <summary>A tapering tube along a curve, in world space: a tentacle.</summary>
        private static void Tentacle(Transform parent, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float radius, Material skin)
        {
            const int Rings = 36, Sides = 10;
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            Vector3 Point(float t) { float u = 1f - t; return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d; }
            for (int i = 0; i <= Rings; i++)
            {
                float t = i / (float)Rings;
                Vector3 p = Point(t), along = (Point(Mathf.Min(1f, t + 0.02f)) - Point(Mathf.Max(0f, t - 0.02f))).normalized;
                Vector3 x = Vector3.Cross(along, Vector3.forward).normalized, y = Vector3.Cross(along, x);
                float r = radius * (1f - t * 0.92f) * (1f + 0.07f * Mathf.Sin(t * 40f));
                for (int s = 0; s < Sides; s++)
                {
                    float angle = s * Mathf.PI * 2f / Sides;
                    verts.Add(p + (x * Mathf.Cos(angle) + y * Mathf.Sin(angle)) * r);
                    if (i == Rings) continue;
                    int n = (s + 1) % Sides;
                    tris.AddRange(new[] { i * Sides + s, (i + 1) * Sides + s, i * Sides + n, i * Sides + n, (i + 1) * Sides + s, (i + 1) * Sides + n });
                }
            }
            var mesh = new Mesh { name = "Tentacle" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            var go = new GameObject("Tentacle");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = skin;
        }

        private static void Tentacles(GameObject body, Transform set, float height)
        {
            if (!Player.instance.effects.GetEffects().Any(e => e != null && (e.GetName() ?? "").ToLowerInvariant().Contains("tentacle"))) return;
            Transform chest = Wear.Bone(body.transform, "Chest");
            if (chest == null) return;
            var skin = new Material(Shader.Find("Standard")) { color = new Color(0.42f, 0.2f, 0.34f) };
            skin.SetFloat("_Glossiness", 0.75f);
            float h = height * 0.72f;
            foreach (int side in new[] { -1, 1 })
                for (int i = 0; i < 2; i++)
                {
                    Vector3 from = chest.position + Vector3.right * side * h * 0.1f + Vector3.up * h * (0.09f - i * 0.05f);
                    Tentacle(set, from, from + new Vector3(side * h * 0.26f, -h * 0.1f, h * 0.05f), from + new Vector3(side * h * (0.28f + i * 0.08f), h * (0.26f - i * 0.2f), h * 0.1f),
                        from + new Vector3(side * h * (0.18f + i * 0.2f), h * (0.42f - i * 0.24f), h * 0.02f), h * 0.024f, skin);
                }
        }

        /// <summary>The room you're standing in, soft from being small, as a backdrop far enough behind to be out of the way.</summary>
        private static RenderTexture Room()
        {
            Camera eye = Player_Control.instance != null ? Player_Control.instance.playerCamera : null;
            if (eye == null) return null;
            var rt = new RenderTexture(96, 96, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = FilterMode.Bilinear };
            RenderTexture was = eye.targetTexture;
            eye.targetTexture = rt;
            eye.Render();
            eye.targetTexture = was;
            return rt;
        }

        private static GameObject Body(Character prefab)
        {
            // Built under an inactive parent so none of its scripts wake up.
            var hold = new GameObject("SCCL_PortraitHold");
            hold.SetActive(false);
            GameObject g = Object.Instantiate(prefab.gameObject, hold.transform);
            for (int pass = 0; pass < 3; pass++)
                foreach (Component c in g.GetComponentsInChildren<Component>(true))
                    if (c is MonoBehaviour || c is Collider || c is Rigidbody || c is Light || c is Canvas)
                        Object.DestroyImmediate(c);
            g.name = "SCCL_PortraitBody";
            g.transform.SetParent(null, false);
            Object.DestroyImmediate(hold);
            g.transform.SetPositionAndRotation(Stage, Quaternion.Euler(0f, -15f, 0f));
            g.SetActive(true);
            return g;
        }

        private static Light Lamp(Transform parent, Vector3 euler, Color color, float intensity)
        {
            var go = new GameObject("Lamp");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(euler);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = color;
            l.intensity = intensity;
            l.cullingMask = 1 << Layer;
            l.shadows = LightShadows.Soft;
            return l;
        }

        /// <summary>Renders the character to a file in the Portraits folder. Null when it worked, or why not.</summary>
        internal static string Render(string fileName)
        {
            if (Player.instance == null) return "Start a game first: the picture shows your character.";
            Character prefab = DashCam.CrawlerPrefab();
            if (prefab == null) return "No crawler body for your class.";

            GameObject body = Body(prefab), set = new GameObject("SCCL_PortraitSet");
            RenderTexture rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 8 }, room = null;
            bool fog = RenderSettings.fog;
            Color ambient = RenderSettings.ambientLight;
            var ambientMode = RenderSettings.ambientMode;
            try
            {
                Animator anim = body.GetComponentInChildren<Animator>(true);
                if (anim != null)
                {
                    anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    anim.applyRootMotion = false;
                    anim.Rebind();
                    anim.Update(0.5f);
                    Pose(body, anim);
                    anim.enabled = false;
                }
                foreach (ParticleSystem fx in body.GetComponentsInChildren<ParticleSystem>(true)) fx.gameObject.SetActive(false);
                float height = Mathf.Max(1.2f, (Wear.Bone(body.transform, "Head")?.position.y ?? Stage.y + 1.6f) - Stage.y + 0.2f);
                Wear.Dress(body.transform, Worn, prefab.internalName.Replace("Nemesis_", ""), Player.instance.playerName);
                DressHands(body, height);
                foreach (EQUIP_TYPE slot in new[] { EQUIP_TYPE.Torso, EQUIP_TYPE.Legs })
                    if (IconColor(Equipped(slot)) is Color worn) Wear.Paint(body.transform, slot, worn);
                Tentacles(body, set.transform, height);

                Transform head = Wear.Bone(body.transform, "Head"), hips = Wear.Bone(body.transform, "Hips");
                Vector3 top = head != null ? head.position : Stage + Vector3.up * 1.6f, low = hips != null ? hips.position : Stage + Vector3.up * 0.9f;
                float span = Mathf.Max(0.3f, top.y - low.y);
                Vector3 look = new Vector3(top.x, low.y + span * 0.72f, top.z);
                float distance = span * 0.9f / Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);

                var camGo = new GameObject("Camera");
                camGo.transform.SetParent(set.transform, false);
                camGo.transform.position = look + Vector3.forward * distance + Vector3.up * span * 0.08f;
                camGo.transform.LookAt(look);
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.cullingMask = 1 << Layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.035f, 0.05f, 0.075f);
                cam.fieldOfView = Fov;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = distance + 20f;
                cam.allowMSAA = true;
                cam.targetTexture = rt;

                room = Room();
                if (room != null && Shader.Find("Sprites/Default") is Shader flat)
                {
                    const float Behind = 4f;
                    GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Object.DestroyImmediate(wall.GetComponent<Collider>());
                    wall.transform.SetParent(set.transform, false);
                    wall.transform.position = look - Vector3.forward * Behind;
                    wall.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                    float side = 2.1f * (distance + Behind) * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
                    wall.transform.localScale = new Vector3(side, side, 1f);
                    wall.GetComponent<MeshRenderer>().sharedMaterial = new Material(flat) { mainTexture = room, color = new Color(1.9f, 2f, 2.2f) };
                }
                foreach (Transform t in body.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                foreach (Transform t in set.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;

                Lamp(set.transform, new Vector3(28f, 150f, 0f), new Color(1f, 0.93f, 0.82f), 1.25f);
                Lamp(set.transform, new Vector3(8f, 225f, 0f), new Color(0.55f, 0.72f, 1f), 0.45f).shadows = LightShadows.None;
                Lamp(set.transform, new Vector3(18f, 20f, 0f), new Color(0.2f, 0.95f, 0.9f), 1.5f).shadows = LightShadows.None; // rim

                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.2f, 0.22f, 0.26f);
                cam.Render();

                RenderTexture was = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = was;
                File.WriteAllBytes(Path.Combine(Portraits.Folder, fileName), tex.EncodeToPNG());
                Object.Destroy(tex);
                return null;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Portrait render: " + e);
                return "The render failed: " + e.Message;
            }
            finally
            {
                RenderSettings.fog = fog;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
                Object.DestroyImmediate(body);
                Object.DestroyImmediate(set);
                foreach (Mesh mesh in Baked) Object.Destroy(mesh);
                Baked.Clear();
                rt.Release();
                Object.Destroy(rt);
                if (room != null) { room.Release(); Object.Destroy(room); }
            }
        }
    }
}
