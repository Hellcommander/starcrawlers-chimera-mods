using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// A place's art kit: its floor tiles, wall panels, ceiling tiles, door, exit and props as loaded models, fitted
    /// to the 1 m grid. Anything the kit lacks is a plain stand-in shape in the place's colours.
    /// </summary>
    internal class PlaceKit
    {
        private class Piece { public Mesh[] meshes; public Material[] mats; public Bounds b; public ModelSpec spec; }

        private readonly PlaceSpec p;
        private readonly List<Piece> floors, walls, ceilings;
        private readonly Piece door, lintel, exit;
        private readonly Dictionary<string, Piece> propPieces = new Dictionary<string, Piece>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PlacePropSpec> props;
        private readonly List<(Vector2Int c, PlacePropSpec prop)> scenery = new List<(Vector2Int, PlacePropSpec)>();
        private static readonly Dictionary<string, Material> plain = new Dictionary<string, Material>();
        private static Mesh quadUp, quadWall, cube;

        internal PlaceKit(PlaceSpec spec)
        {
            p = spec;
            PlaceKitSpec k = p.kit ?? new PlaceKitSpec();
            string folder = p.pack?.folder;
            floors = Load(k.floor, folder);
            walls = Load(k.wall, folder);
            ceilings = Load(k.ceiling, folder);
            door = Solid(Load(k.door, folder));
            lintel = Solid(Load(k.lintel, folder));
            exit = Solid(Load(k.exit, folder));
            props = (k.props ?? new List<PlacePropSpec>()).Where(x => x != null && !string.IsNullOrEmpty(x.id)).ToList();
            if (props.Count == 0) props = StandInProps();
            foreach (PlacePropSpec pr in props)
            {
                Piece pc = Solid(Load(pr.model, folder));
                if (pc != null) propPieces[pr.id] = pc;
            }
        }

        /// <summary>Doors, exits and props need a model; a bare texture for one of them is ignored.</summary>
        private static Piece Solid(Piece p) => p?.meshes != null ? p : null;

        private static List<Piece> Load(List<ModelSpec> specs, string folder) =>
            (specs ?? new List<ModelSpec>()).Select(s => Load(s, folder)).Where(x => x != null).ToList();

        private static Piece Load(ModelSpec spec, string folder)
        {
            if (spec == null) return null;
            if (string.IsNullOrEmpty(spec.mesh))
            {
                // a surface given as a texture: drawn on the grid's own flat tiles and panels
                Texture2D surface = string.IsNullOrEmpty(spec.texture) ? null : ModelAssets.LoadTexture(Path.Combine(folder ?? "", spec.texture));
                if (surface == null) return null;
                surface.anisoLevel = 4;
                Color tc = !string.IsNullOrEmpty(spec.tint) && ColorUtility.TryParseHtmlString(spec.tint, out Color st) ? st : Color.white;
                Material sm = ModelAssets.LitMaterial(tc);
                sm.mainTexture = surface;
                return new Piece { mats = new[] { sm }, spec = spec };
            }
            if (!ModelAssets.LoadParts(Path.Combine(folder ?? "", spec.mesh), out Mesh[] meshes, out Material[] mats) || meshes.Length == 0) return null;
            var tex = string.IsNullOrEmpty(spec.texture) ? null : ModelAssets.LoadTexture(Path.Combine(folder ?? "", spec.texture));
            if (tex != null) mats = mats.Select(m => { var c = new Material(m) { mainTexture = tex }; Object.DontDestroyOnLoad(c); return c; }).ToArray();
            if (!string.IsNullOrEmpty(spec.tint) && ColorUtility.TryParseHtmlString(spec.tint, out Color t))
                mats = mats.Select(m => { var c = new Material(m); if (c.HasProperty("_Color")) c.color *= t; Object.DontDestroyOnLoad(c); return c; }).ToArray();
            Bounds b = meshes[0].bounds;
            foreach (Mesh m in meshes.Skip(1)) b.Encapsulate(m.bounds);
            return new Piece { meshes = meshes, mats = mats, b = b, spec = spec };
        }

        // ---- stand-ins -------------------------------------------------------------------------------------

        private static List<PlacePropSpec> StandInProps() => new List<PlacePropSpec>
        {
            new PlacePropSpec { id = "crate", where = "wall", size = 0.48f, loot = true, search = true, weight = 12 },
            new PlacePropSpec { id = "console", where = "wall", size = 0.66f, glow = "#5FD0FF", weight = 8 },
            new PlacePropSpec { id = "locker", where = "corner", size = 1.1f, search = true, weight = 6 },
            new PlacePropSpec { id = "pillar", where = "centre", size = 2f, weight = 10 },
        };

        internal static Material Plain(string hex, Color fallback)
        {
            string key = hex ?? ColorUtility.ToHtmlStringRGB(fallback);
            if (plain.TryGetValue(key, out Material m) && m != null) return m;
            Color c = ColorUtility.TryParseHtmlString(hex ?? "", out Color pc) ? pc : fallback;
            return plain[key] = ModelAssets.LitMaterial(c);
        }

        private static Mesh QuadUp => quadUp != null ? quadUp : quadUp = Quad(new[] { new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f), new Vector3(.5f, 0, -.5f) }, Vector3.up);
        /// <summary>1 x 1, standing on its bottom edge, facing -Z.</summary>
        private static Mesh QuadWall => quadWall != null ? quadWall : quadWall = Quad(new[] { new Vector3(-.5f, 0, 0), new Vector3(-.5f, 1, 0), new Vector3(.5f, 1, 0), new Vector3(.5f, 0, 0) }, Vector3.back);

        private static Mesh Quad(Vector3[] v, Vector3 normal)
        {
            var m = new Mesh { vertices = v, normals = Enumerable.Repeat(normal, 4).ToArray(), uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) }, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            Object.DontDestroyOnLoad(m);
            return m;
        }

        /// <summary>A unit cube centred on the origin.</summary>
        private static Mesh Cube
        {
            get
            {
                if (cube != null) return cube;
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
                foreach (Vector3 f in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                {
                    Vector3 a = f.y != 0 ? Vector3.right : Vector3.up, b = Vector3.Cross(f, a);
                    int i = v.Count;
                    v.Add((f - a - b) * 0.5f); v.Add((f - a + b) * 0.5f); v.Add((f + a + b) * 0.5f); v.Add((f + a - b) * 0.5f);
                    for (int k = 0; k < 4; k++) n.Add(f);
                    uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
                    t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                }
                cube = new Mesh { vertices = v.ToArray(), normals = n.ToArray(), uv = uv.ToArray(), triangles = t.ToArray() };
                // the faces above wind the wrong way for half the sides: fix by checking each against its normal
                var tris = cube.triangles;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 a = v[tris[i]], b = v[tris[i + 1]], c = v[tris[i + 2]];
                    if (Vector3.Dot(Vector3.Cross(b - a, c - a), n[tris[i]]) < 0) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
                }
                cube.triangles = tris;
                Object.DontDestroyOnLoad(cube);
                return cube;
            }
        }

        // ---- fitting ---------------------------------------------------------------------------------------

        /// <summary>Maps a piece's bounds into a box of this size, centred at centre, turned by rot (then the spec's own turn).</summary>
        private static Matrix4x4 Fit(Piece pc, Vector3 size, Vector3 centre, Quaternion rot)
        {
            Quaternion own = pc.spec?.rotation != null && pc.spec.rotation.Length >= 3 ? Quaternion.Euler(pc.spec.rotation[0], pc.spec.rotation[1], pc.spec.rotation[2]) : Quaternion.identity;
            // the spec's turn is applied first, so the bounds that get fitted are the turned ones
            Bounds tb = Turned(pc.b, own);
            Vector3 s = new Vector3(size.x / Mathf.Max(0.0001f, tb.size.x), size.y / Mathf.Max(0.0001f, tb.size.y), size.z / Mathf.Max(0.0001f, tb.size.z));
            return Matrix4x4.TRS(centre, rot, Vector3.one) * Matrix4x4.Scale(s) * Matrix4x4.Translate(-tb.center) * Matrix4x4.Rotate(own);
        }

        private static Bounds Turned(Bounds b, Quaternion q)
        {
            var r = new Bounds(q * b.center, Vector3.zero);
            for (int i = 0; i < 8; i++)
                r.Encapsulate(q * new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
            return r;
        }

        private static void Add(MeshBatch mb, Piece pc, Matrix4x4 m)
        {
            for (int i = 0; i < pc.meshes.Length; i++) mb.Add(pc.meshes[i], pc.mats[Math.Min(i, pc.mats.Length - 1)], m);
        }

        private static float Depth(Piece pc, float across)
        {
            Quaternion own = pc.spec?.rotation != null && pc.spec.rotation.Length >= 3 ? Quaternion.Euler(pc.spec.rotation[0], pc.spec.rotation[1], pc.spec.rotation[2]) : Quaternion.identity;
            Bounds tb = Turned(pc.b, own);
            return tb.size.z / Mathf.Max(0.0001f, tb.size.x) * across;
        }

        private static Quaternion Yaw(System.Random rng) => Quaternion.Euler(0f, 90f * rng.Next(4), 0f);
        private static Quaternion Facing(Vector2Int outward) => Quaternion.LookRotation(new Vector3(outward.x, 0f, outward.y));

        // ---- surfaces --------------------------------------------------------------------------------------

        internal void Floor(MeshBatch mb, Vector3 w, System.Random rng)
        {
            PlaceLookSpec look = p.look ?? new PlaceLookSpec();
            if (floors.Count == 0) { mb.Add(QuadUp, Plain(look.floor, new Color(0.23f, 0.25f, 0.28f)), Matrix4x4.Translate(w)); return; }
            Piece pc = floors[rng.Next(floors.Count)];
            if (pc.meshes == null) { mb.Add(QuadUp, pc.mats[0], Matrix4x4.TRS(w, Yaw(rng), Vector3.one)); return; }
            float t = Mathf.Clamp(pc.b.size.y / Mathf.Max(0.0001f, Mathf.Max(pc.b.size.x, pc.b.size.z)), 0.04f, 0.25f);
            Add(mb, pc, Fit(pc, new Vector3(1.002f, t, 1.002f), w + Vector3.down * (t * 0.5f), Yaw(rng)));
        }

        internal void Ceiling(MeshBatch mb, Vector3 w, float H, System.Random rng)
        {
            PlaceLookSpec look = p.look ?? new PlaceLookSpec();
            if (ceilings.Count == 0) { mb.Add(QuadUp, Plain(look.ceiling, new Color(0.16f, 0.18f, 0.2f)), Matrix4x4.TRS(w + Vector3.up * H, Quaternion.Euler(180f, 0f, 0f), Vector3.one)); return; }
            Piece pc = ceilings[rng.Next(ceilings.Count)];
            if (pc.meshes == null) { mb.Add(QuadUp, pc.mats[0], Matrix4x4.TRS(w + Vector3.up * H, Quaternion.Euler(180f, 0f, 0f) * Yaw(rng), Vector3.one)); return; }
            float t = Mathf.Clamp(pc.b.size.y / Mathf.Max(pc.b.size.x, pc.b.size.z), 0.04f, 0.3f);
            Add(mb, pc, Fit(pc, new Vector3(1.002f, t, 1.002f), w + Vector3.up * (H + t * 0.5f), Quaternion.Euler(180f, 0f, 0f) * Yaw(rng)));
        }

        /// <summary>A wall panel on the edge of tile w toward d, facing back into the tile.</summary>
        internal void Wall(MeshBatch mb, Vector3 w, Vector2Int d, float H, System.Random rng)
        {
            PlaceLookSpec look = p.look ?? new PlaceLookSpec();
            Vector3 out3 = new Vector3(d.x, 0f, d.y);
            if (walls.Count == 0) { mb.Add(QuadWall, Plain(look.wall, new Color(0.3f, 0.32f, 0.36f)), Matrix4x4.TRS(w + out3 * 0.5f, Facing(d), new Vector3(1f, H, 1f))); return; }
            Piece pc = walls[rng.Next(walls.Count)];
            if (pc.meshes == null) { mb.Add(QuadWall, pc.mats[0], Matrix4x4.TRS(w + out3 * 0.5f, Facing(d), new Vector3(1f, H, 1f))); return; }
            float depth = Mathf.Clamp(Depth(pc, 1f), 0.04f, 0.35f);
            Add(mb, pc, Fit(pc, new Vector3(1.002f, H, depth), w + out3 * (0.5f - depth * 0.5f + 0.02f) + Vector3.up * H * 0.5f, Facing(d)));
        }

        /// <summary>The wall above a doorway, on both faces of the doorway's middle.</summary>
        internal void Lintel(MeshBatch mb, Vector3 w, Vector2Int dir, float H, bool arch)
        {
            PlaceKitSpec k = p.kit ?? new PlaceKitSpec();
            PlaceLookSpec look = p.look ?? new PlaceLookSpec();
            float dh = Mathf.Clamp(k.doorHeight, 1.1f, H - 0.1f);
            float h = H - dh;
            Vector3 c = w + Vector3.up * (dh + h * 0.5f);
            if (lintel != null) { Add(mb, lintel, Fit(lintel, new Vector3(1f, h, 0.3f), c, Facing(dir))); return; }
            Material lm = walls.FirstOrDefault(x => x.meshes == null)?.mats[0] ?? Plain(look.wall, new Color(0.3f, 0.32f, 0.36f));
            mb.Add(Cube, lm, Matrix4x4.TRS(c, Facing(dir), new Vector3(1f, h, 0.3f)));
            if (!arch)
            {
                // the frame round the opening
                foreach (float s in new[] { -0.47f, 0.47f })
                    mb.Add(Cube, Plain(look.door, new Color(0.4f, 0.44f, 0.5f)), Matrix4x4.TRS(w + Facing(dir) * new Vector3(s, dh * 0.5f, 0f), Facing(dir), new Vector3(0.06f, dh, 0.34f)));
            }
        }

        // ---- props -----------------------------------------------------------------------------------------

        internal PlacePropSpec Prop(char mark, PlaceTemplateSpec t)
        {
            if ("ELSV.".IndexOf(mark) >= 0) return null;
            string id = t?.legend != null && t.legend.TryGetValue(mark.ToString(), out string s) ? s : null;
            return id == null ? null : props.FirstOrDefault(x => string.Equals(x.id, id, StringComparison.OrdinalIgnoreCase));
        }

        internal PlacePropSpec PickProp(System.Random rng, string role, string where)
        {
            var pool = props.Where(x => x.weight > 0 && (x.where == where || x.where == "any" || (where == "corner" && x.where == "wall"))
                                        && (x.roles == null || x.roles.Count == 0 || x.roles.Contains(role, StringComparer.OrdinalIgnoreCase))).ToList();
            if (pool.Count == 0) return null;
            int r = rng.Next(pool.Sum(x => x.weight));
            return pool.First(x => (r -= x.weight) < 0);
        }

        internal void Scenery(Vector2Int c, PlacePropSpec prop) => scenery.Add((c, prop));

        internal void PlaceScenery(MeshBatch mb, PlaceLayout lay, PlaceLayout.LRoom r, System.Random rng, Transform parent)
        {
            foreach (var (c, prop) in scenery.Where(x => r.set.Contains(x.c)).ToList()) Prop(mb, lay, c, prop, rng, parent);
            scenery.RemoveAll(x => r.set.Contains(x.c));
        }

        /// <summary>A prop on tile c: against its wall (facing in), or standing free in the middle.</summary>
        internal void Prop(MeshBatch mb, PlaceLayout lay, Vector2Int c, PlacePropSpec prop, System.Random rng, Transform parent)
        {
            Vector3 w = lay.World(c);
            Vector2Int wall = lay.WallSides(c).FirstOrDefault();
            bool against = prop.where != "centre" && wall != Vector2Int.zero;
            Quaternion rot = against ? Facing(wall) : Yaw(rng);
            Vector3 out3 = new Vector3(wall.x, 0f, wall.y);
            float H = Mathf.Max(1.6f, p.height);
            if (propPieces.TryGetValue(prop.id, out Piece pc))
            {
                Quaternion own = pc.spec?.rotation != null && pc.spec.rotation.Length >= 3 ? Quaternion.Euler(pc.spec.rotation[0], pc.spec.rotation[1], pc.spec.rotation[2]) : Quaternion.identity;
                Bounds tb = Turned(pc.b, own);
                float k = Mathf.Min(prop.size, H - 0.05f) / Mathf.Max(0.0001f, Mathf.Max(tb.size.x, Mathf.Max(tb.size.y, tb.size.z)));
                Vector3 size = tb.size * k;
                Vector3 centre = w + Vector3.up * size.y * 0.5f + (against ? out3 * (0.5f - size.z * 0.5f - 0.03f) : Vector3.zero);
                Add(mb, pc, Fit(pc, size, centre, rot));
            }
            else StandIn(mb, w, prop, against, out3, rot, H);
            if (!string.IsNullOrEmpty(prop.glow) && ColorUtility.TryParseHtmlString(prop.glow, out Color gc))
            {
                var g = new GameObject("PropGlow");
                g.transform.SetParent(parent, false);
                g.transform.position = w + Vector3.up * Mathf.Min(1.2f, prop.size) - (against ? out3 * 0.1f : Vector3.zero);
                Light l = g.AddComponent<Light>();
                l.type = LightType.Point; l.color = gc; l.range = 2.2f; l.intensity = 1.2f; l.shadows = LightShadows.None;
            }
        }

        private void StandIn(MeshBatch mb, Vector3 w, PlacePropSpec prop, bool against, Vector3 out3, Quaternion rot, float H)
        {
            PlaceLookSpec look = p.look ?? new PlaceLookSpec();
            Vector3 size;
            string colour;
            // plain blocks in roughly the prop's proportions, its longest side "size" long
            float k = Mathf.Clamp(prop.size, 0.2f, H - 0.05f);
            switch (prop.id)
            {
                case "console": size = new Vector3(0.8f, 1f, 0.45f) * k; colour = "#4F5966"; break;
                case "locker": case "cabinet": case "rack": case "server": case "cryopod": case "tank": size = new Vector3(0.42f, 1f, 0.3f) * k; colour = "#5E6670"; break;
                case "pillar": size = new Vector3(0.35f, H, 0.35f); colour = look.wall; break;
                default: size = new Vector3(1f, 0.8f, 0.9f) * k; colour = "#7A6A4E"; break;
            }
            Vector3 centre = w + Vector3.up * size.y * 0.5f + (against ? out3 * (0.5f - size.z * 0.5f - 0.03f) : Vector3.zero);
            mb.Add(Cube, Plain(colour, Color.gray), Matrix4x4.TRS(centre, rot, size));
            if (prop.id == "console")
                mb.Add(Cube, Glow(prop.glow ?? "#5FD0FF"), Matrix4x4.TRS(centre + Vector3.up * 0.3f - out3 * (size.z * 0.5f + 0.01f), rot, new Vector3(0.7f, 0.35f, 0.02f)));
        }

        private static readonly Dictionary<string, Material> glows = new Dictionary<string, Material>();

        private static Material Glow(string hex)
        {
            if (glows.TryGetValue(hex, out Material m) && m != null) return m;
            Color c = ColorUtility.TryParseHtmlString(hex, out Color pc) ? pc : Color.cyan;
            return glows[hex] = ModelAssets.GlowMaterial(c, 2f) ?? Plain(hex, c);
        }

        /// <summary>The glowing panel a ceiling light hangs from.</summary>
        internal static void Fixture(Transform light, Color c)
        {
            var g = new GameObject("Fixture");
            g.transform.SetParent(light, false);
            g.transform.localPosition = Vector3.up * 0.3f;
            g.transform.localScale = new Vector3(0.6f, 0.04f, 0.25f);
            g.AddComponent<MeshFilter>().sharedMesh = Cube;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Glow("#" + ColorUtility.ToHtmlStringRGB(c));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---- door and exit (live objects, made after the map is set up) ------------------------------------

        /// <summary>The leaf of a door, under the game's door object (its local Z runs through the doorway).</summary>
        internal Transform DoorLeaf(Transform parent, out Renderer lamp)
        {
            PlaceKitSpec k = p.kit ?? new PlaceKitSpec();
            PlaceLookSpec look = p.look ?? new PlaceLookSpec();
            float dh = Mathf.Clamp(k.doorHeight, 1.1f, Mathf.Max(1.6f, p.height) - 0.1f);
            var leaf = new GameObject("SCCL_DoorLeaf").transform;
            leaf.SetParent(parent, false);
            if (door != null) Instance(leaf, door, Fit(door, new Vector3(0.94f, dh, Mathf.Clamp(Depth(door, 0.94f), 0.06f, 0.25f)), Vector3.up * dh * 0.5f, Quaternion.identity));
            else Instance(leaf, Cube, Plain(look.door, new Color(0.42f, 0.46f, 0.52f)), Matrix4x4.TRS(Vector3.up * dh * 0.5f, Quaternion.identity, new Vector3(0.94f, dh, 0.1f)));
            var lampGo = new GameObject("Lamp");
            lampGo.transform.SetParent(leaf, false);
            lampGo.transform.localPosition = new Vector3(0f, dh * 0.62f, 0f);
            lampGo.transform.localScale = new Vector3(0.16f, 0.06f, 0.14f);
            lampGo.AddComponent<MeshFilter>().sharedMesh = Cube;
            lamp = lampGo.AddComponent<MeshRenderer>();
            lamp.sharedMaterial = Glow("#40FF90");
            return leaf;
        }

        internal string Slide => (p.kit?.doorSlide ?? "side").ToLowerInvariant();
        internal float DoorHeight => Mathf.Clamp(p.kit?.doorHeight ?? 1.55f, 1.1f, Mathf.Max(1.6f, p.height) - 0.1f);
        internal static Material Lamp(string hex) => Glow(hex);

        /// <summary>The way out against the arrival room's wall; parent faces into the room (its -Z is the wall side).</summary>
        internal void ExitLook(Transform parent)
        {
            PlaceKitSpec k = p.kit ?? new PlaceKitSpec();
            float s = Mathf.Clamp(k.exitSize, 1f, Mathf.Max(1.6f, p.height) - 0.1f);
            if (exit != null)
            {
                Bounds tb = Turned(exit.b, exit.spec?.rotation != null && exit.spec.rotation.Length >= 3 ? Quaternion.Euler(exit.spec.rotation[0], exit.spec.rotation[1], exit.spec.rotation[2]) : Quaternion.identity);
                float kk = s / Mathf.Max(0.0001f, Mathf.Max(tb.size.x, tb.size.y));
                Vector3 size = tb.size * kk;
                // flat against the wall: the player stands on the exit tile, so a deep model would swallow the camera
                size.z = Mathf.Min(size.z, 0.22f);
                Instance(parent, exit, Fit(exit, size, new Vector3(0f, size.y * 0.5f, -0.5f + size.z * 0.5f + 0.02f), Quaternion.Euler(0f, 180f, 0f)));
                return;
            }
            // stand-in: a heavy hatch with a glowing seam
            Instance(parent, Cube, Plain("#3E4A55", Color.gray), Matrix4x4.TRS(new Vector3(0f, s * 0.5f, -0.42f), Quaternion.identity, new Vector3(1.3f, s, 0.16f)));
            Instance(parent, Cube, Glow("#7FFFC0"), Matrix4x4.TRS(new Vector3(0f, s * 0.5f, -0.33f), Quaternion.identity, new Vector3(0.05f, s * 0.9f, 0.02f)));
            Instance(parent, Cube, Glow("#7FFFC0"), Matrix4x4.TRS(new Vector3(0f, s + 0.08f, -0.33f), Quaternion.identity, new Vector3(1.1f, 0.06f, 0.02f)));
        }

        private static void Instance(Transform parent, Piece pc, Matrix4x4 m)
        {
            for (int i = 0; i < pc.meshes.Length; i++) Instance(parent, pc.meshes[i], pc.mats[Math.Min(i, pc.mats.Length - 1)], m);
        }

        private static void Instance(Transform parent, Mesh mesh, Material mat, Matrix4x4 m)
        {
            var g = new GameObject("Part");
            g.transform.SetParent(parent, false);
            // a fitted matrix can scale unevenly after a turn, which a transform can't hold: bake it into a copy of the mesh
            var baked = Object.Instantiate(mesh);
            var v = baked.vertices;
            var n = baked.normals;
            Matrix4x4 nm = m.inverse.transpose;
            for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
            for (int i = 0; i < n.Length; i++) n[i] = nm.MultiplyVector(n[i]).normalized;
            baked.vertices = v;
            if (n.Length == v.Length) baked.normals = n;
            baked.RecalculateBounds();
            g.AddComponent<MeshFilter>().sharedMesh = baked;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
