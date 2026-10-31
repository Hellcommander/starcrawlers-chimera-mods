using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// Turns a PlaceLayout into rooms the game can play: each room becomes a template object (a Room with a node per
    /// tile, door nodes on its doorways, enemy and loot spots, its floor, walls, ceiling and lights) parked under an
    /// inactive holder. The Map gets one packed entry per template, so the game's own map setup instantiates them,
    /// pairs the door nodes into doors, links the tiles and spawns, exactly as it does for its own floors.
    /// </summary>
    internal static class PlaceBuilder
    {
        internal class Built { public Map map; public GameObject holder; public PlaceLayout lay; public PlaceSpec spec; public List<Chest> chests; }

        /// <summary>Where a searchable container stands: its tile, which way its wall is, and the tile you search it from.</summary>
        internal struct Chest { public Vector3 at, wall, front; }

        private static readonly Vector2Int[] Dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        internal static Built Make(PlaceSpec p, PlaceKit kit, string mapId, int seed, int rooms, int floor, Map like, Door doorPrefab, List<Character> pool)
        {
            PlaceLayout lay = PlaceLayout.Make(p, seed, rooms);
            var deco = new System.Random(seed ^ 0x5bd1e995);
            var holder = new GameObject("SCCL_PlaceTemplates");
            holder.SetActive(false);

            var go = new GameObject(mapId);
            Map map = go.AddComponent<Map>();
            map.internalName = mapId;
            map.floor = floor;
            map.packed = true;
            if (like != null)
            {
                map.stairPrefab = like.stairPrefab;
                map.secretWallPrefab = like.secretWallPrefab;
                map.uberDoorPrefab = like.uberDoorPrefab;
                map.guards = like.guards;
                map.guardMinMax = like.guardMinMax;
                map.cameraFarClipPlane = like.cameraFarClipPlane;
                map.naniteCloudsMin = Mathf.Min(like.naniteCloudsMin, 1);
                map.naniteCloudsMax = Mathf.Min(like.naniteCloudsMax, 2);
                map.rareSpawnMinMax = new Vector2Int(0, Mathf.Min(1, like.rareSpawnMinMax.y));
            }
            map.secretRoomMin = map.secretRoomMax = 0;
            map.MinorDoorInitialLockPercentage = Mathf.Clamp01(p.lockedDoors);
            map.lorePlaced = true; // lore belongs to the real floors
            map._packedRooms = new List<PackedRoom>();
            var chests = new List<Chest>();

            foreach (PlaceLayout.LRoom r in lay.rooms)
            {
                GameObject t = Room(p, lay, r, holder.transform, kit, deco, doorPrefab, pool, chests);
                Room room = t.GetComponent<Room>();
                map._packedRooms.Add(new PackedRoom(room.PrefabGroupName, room.Properties, t.transform.position, Quaternion.identity));
                room.Properties.UseSpecificRoomVariant = t;
            }
            return new Built { map = map, holder = holder, lay = lay, spec = p, chests = chests };
        }

        private static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        private static GameObject Room(PlaceSpec p, PlaceLayout lay, PlaceLayout.LRoom r, Transform holder, PlaceKit kit, System.Random rng, Door doorPrefab, List<Character> pool, List<Chest> chests)
        {
            float H = Mathf.Max(1.6f, p.height);
            int minX = r.cells.Min(c => c.x), maxX = r.cells.Max(c => c.x), minY = r.cells.Min(c => c.y), maxY = r.cells.Max(c => c.y);
            Vector3 lo = lay.World(new Vector2Int(minX, minY)), hi = lay.World(new Vector2Int(maxX, maxY));
            Vector3 centre = (lo + hi) * 0.5f;

            var go = new GameObject("Place_" + r.role);
            go.transform.SetParent(holder, false);
            go.transform.position = centre;
            Room room = go.AddComponent<Room>();
            room.PrefabGroupName = "Place_" + r.role;
            Transform geo = Child(go.transform, "Geo"), meshes = Child(geo, "Meshes"), nodesT = Child(geo, "NavNodes");
            Child(geo, "Props");
            Transform traps = Child(geo, "TrapControl");
            Child(traps, "Triggers");
            Child(traps, "Traps");
            Transform col = Child(go.transform, "Collision"), lootT = Child(go.transform, "LootLocators"), ways = Child(go.transform, "Doorways");
            Child(go.transform, "InteractiveLocators");
            Child(go.transform, "StairLocators");
            Child(go.transform, "QuestLocators");
            Transform spawnT = Child(go.transform, "Spawn_Control");

            bool isArrival = r.id == lay.arrival;
            room.roomGeometry = meshes.gameObject;
            room.lootLocatorContainer = lootT;
            room.interactiveLocators = new InteractiveLocator[0];
            room.questLocators = new QuestLocator[0];
            room.stairLocators = new Stair_Locator[0];
            room.randomWallMaterials = new Material[0];
            room.roomRadius = Mathf.Max(maxX - minX + 1, maxY - minY + 1) * 0.5f;
            room.Properties = new RoomProperties
            {
                trapMinMax = Vector2Int.zero,
                NoStairSpawn = true,
                IsHallway = r.hallway,
                NoLockedDoors = isArrival,
                InteractiveObjectMax = 0,
                bounds = new Bounds(new Vector3(0f, H * 0.5f, 0f), new Vector3(maxX - minX + 1, H, maxY - minY + 1) * 2f),
            };

            // ---- which tiles hold props (blocked), loot and enemy spots ----
            var blocked = new Dictionary<Vector2Int, PlacePropSpec>();
            var free = new HashSet<Vector2Int>(r.cells);
            free.Remove(lay.exitCell);
            var filler = new HashSet<Vector2Int>(); // 'x': taken up by a big prop on a tile next to it
            foreach (var kv in r.marks)
            {
                if (kv.Value == 'x') { filler.Add(kv.Key); free.Remove(kv.Key); continue; }
                PlacePropSpec prop = kit.Prop(kv.Value, r.template);
                if (prop != null) { if (prop.blocks) blocked[kv.Key] = prop; free.Remove(kv.Key); }
            }
            if (!r.hallway && r.template == null)
            {
                int want = r.cells.Count / 8;
                var spots = r.cells.Where(c => free.Contains(c) && lay.WallSides(c).Any() && !lay.NextToDoorway(c) && !NearExit(lay, c)).ToList();
                Shuffle(spots, rng);
                foreach (Vector2Int c in spots)
                {
                    if (want <= 0) break;
                    bool corner = lay.WallSides(c).Count() >= 2;
                    PlacePropSpec prop = kit.PickProp(rng, r.role, corner ? "corner" : "wall");
                    if (prop == null) break;
                    if (prop.blocks && !StaysConnected(r, lay, blocked.Keys.Concat(filler).Append(c))) continue;
                    if (prop.blocks) blocked[c] = prop;
                    else kit.Scenery(c, prop);
                    free.Remove(c);
                    want--;
                }
                // something in the middle of bigger rooms
                if (r.cells.Count >= 25)
                {
                    var mid = r.cells.Where(c => free.Contains(c) && !lay.WallSides(c).Any() && Dirs.All(d => r.set.Contains(c + d) && !lay.NextToDoorway(c + d))).ToList();
                    Shuffle(mid, rng);
                    PlacePropSpec centreProp = kit.PickProp(rng, r.role, "centre");
                    foreach (Vector2Int c in mid.Take(r.cells.Count / 30))
                        if (centreProp != null && StaysConnected(r, lay, blocked.Keys.Concat(filler).Append(c))) { blocked[c] = centreProp; free.Remove(c); }
                }
            }

            // ---- nodes ----
            var nodes = new List<NavNode>();
            foreach (Vector2Int c in r.cells)
            {
                NavNode n = Node(nodesT, lay.World(c), room);
                if (blocked.ContainsKey(c) || filler.Contains(c)) n.Block_Move = n.Block_Item = n.Block_Mob = n.Block_Hazard = true;
                nodes.Add(n);
            }
            foreach (PlaceLayout.LDoor d in lay.doors.Where(d => d.a == r.id || d.b == r.id))
            {
                NavNode n = Node(nodesT, lay.World(d.cell), room);
                n.transform.rotation = Quaternion.LookRotation(new Vector3(d.dir.x, 0f, d.dir.y));
                var dn = n.gameObject.AddComponent<DoorNode>();
                dn.doorPrefab = doorPrefab;
                dn.doorPriority = d.a == r.id ? 1 : 0; // a's node survives the pairing
                dn.destroyIfUnusedDoor = true;
                Transform ind = Child(n.transform, d.dir.x != 0 ? "EW" : "NS");
                dn.doorDirectionIndicator = ind;
                n.doorNode = dn;
                nodes.Add(n);
            }
            room.roomNodes = nodes;

            // ---- surfaces ----
            var mb = new MeshBatch();
            foreach (Vector2Int c in r.cells)
            {
                Vector3 w = lay.World(c);
                kit.Floor(mb, w, rng);
                kit.Ceiling(mb, w, H, rng);
                foreach (Vector2Int d in Dirs)
                    if (lay.At(c + d) == PlaceLayout.Solid)
                    {
                        kit.Wall(mb, w, d, H, rng);
                        WallCollider(col, w, d, H);
                    }
            }
            FloorColliders(col, lay, r);
            foreach (var kv in blocked)
            {
                Vector2Int wall = lay.WallSides(kv.Key).FirstOrDefault();
                Vector2Int inward = -wall;
                if (kv.Value.search && wall != Vector2Int.zero && r.set.Contains(kv.Key + inward) && !blocked.ContainsKey(kv.Key + inward))
                    chests.Add(new Chest { at = lay.World(kv.Key), wall = new Vector3(wall.x, 0f, wall.y), front = lay.World(kv.Key + inward) });
                else kit.Prop(mb, lay, kv.Key, kv.Value, rng, meshes);
            }
            kit.PlaceScenery(mb, lay, r, rng, meshes);
            mb.Flush(meshes, "Surfaces");

            // the doorways this room owns (each is built once, by room a, and never hidden with a room)
            var wb = new MeshBatch();
            foreach (PlaceLayout.LDoor d in lay.doors.Where(d => d.a == r.id))
            {
                Vector3 w = lay.World(d.cell);
                kit.Floor(wb, w, rng);
                kit.Ceiling(wb, w, H, rng);
                Vector2Int side = new Vector2Int(d.dir.y, d.dir.x);
                foreach (Vector2Int s in new[] { side, -side })
                {
                    kit.Wall(wb, w, s, H, rng);
                    WallCollider(col, w, s, H);
                }
                kit.Lintel(wb, w, d.dir, H, d.state == "arch");
            }
            wb.Flush(ways, "Doorways");

            Lights(p, lay, r, meshes, rng, H);

            // ---- enemies and loot ----
            var spawnPoints = new List<Spawn_Point>();
            bool quiet = isArrival || (r.template?.noEnemies ?? false);
            if (!quiet && pool != null && pool.Count > 0)
            {
                var marked = r.marks.Where(kv => kv.Value == 'S').Select(kv => kv.Key).ToList();
                int count = r.hallway ? (rng.NextDouble() < 0.35 ? 1 : 0) : Mathf.Clamp(Mathf.RoundToInt(r.cells.Count / (float)Mathf.Max(4, p.tilesPerEnemy)), 1, 4);
                var spots = r.cells.Where(c => free.Contains(c) && !lay.NextToDoorway(c) && !marked.Contains(c)).ToList();
                Shuffle(spots, rng);
                foreach (Vector2Int c in marked.Concat(spots.Take(Mathf.Max(0, count - marked.Count))))
                {
                    var sp = new GameObject("Spawn_Point").AddComponent<Spawn_Point>();
                    sp.transform.SetParent(spawnT, false);
                    sp.transform.position = lay.World(c);
                    sp.transform.rotation = Quaternion.Euler(0f, 90f * rng.Next(4), 0f);
                    sp.useExactPosition = true;
                    sp.emptyChance = marked.Contains(c) ? 0 : Mathf.Clamp(p.emptyChance, 0, 90);
                    sp.spawns = Enumerable.Range(0, 4).Select(_ => pool[rng.Next(pool.Count)]).Distinct().ToArray();
                    spawnPoints.Add(sp);
                    free.Remove(c);
                }
            }
            Spawn_Control ctl = spawnT.gameObject.AddComponent<Spawn_Control>();
            ctl.floorSpawnRange = new Vector2Int(0, 999);
            ctl.spawnPoints = spawnPoints.ToArray();
            room.spawners = new[] { ctl };

            var lootMarks = r.marks.Where(kv => kv.Value == 'L').Select(kv => kv.Key).ToList();
            int loot = r.hallway ? 0 : Mathf.RoundToInt(r.cells.Count / (float)Mathf.Max(6, p.tilesPerLoot) + (float)rng.NextDouble() * 0.6f);
            var lootSpots = r.cells.Where(c => free.Contains(c) && !lay.NextToDoorway(c)).OrderBy(c => lay.WallSides(c).Any() ? 0 : 1).ThenBy(_ => rng.Next()).ToList();
            foreach (Vector2Int c in lootMarks.Concat(lootSpots.Take(Mathf.Max(0, loot - lootMarks.Count))))
            {
                var ll = new GameObject("LootLocator").AddComponent<LootLocator>();
                ll.transform.SetParent(lootT, false);
                ll.transform.position = lay.World(c) + Vector3.up * 0.02f;
                ll.spawnTypes = new[] { LOOT_TYPE.Random };
                ll.rarityOverride = RARITY.None;
                ll.randomizeRotation = true;
                ll.emptyChance = lootMarks.Contains(c) ? 0 : 30;
            }
            return go;
        }

        private static bool NearExit(PlaceLayout lay, Vector2Int c) => Mathf.Abs(c.x - lay.exitCell.x) + Mathf.Abs(c.y - lay.exitCell.y) <= 1;

        private static NavNode Node(Transform parent, Vector3 at, Room room)
        {
            var g = new GameObject("NavNode");
            g.transform.SetParent(parent, false);
            g.transform.position = at;
            NavNode n = g.AddComponent<NavNode>();
            n.room = room;
            n.links = new NavNode[4];
            return n;
        }

        /// <summary>Blocking these tiles keeps every free tile of the room reachable, doorways included.</summary>
        private static bool StaysConnected(PlaceLayout.LRoom r, PlaceLayout lay, IEnumerable<Vector2Int> blockedTiles)
        {
            var blocked = new HashSet<Vector2Int>(blockedTiles);
            var open = r.cells.Where(c => !blocked.Contains(c)).ToList();
            if (open.Count == 0) return false;
            var seen = new HashSet<Vector2Int> { open[0] };
            var q = new Queue<Vector2Int>();
            q.Enqueue(open[0]);
            while (q.Count > 0)
            {
                Vector2Int c = q.Dequeue();
                foreach (Vector2Int d in Dirs)
                {
                    Vector2Int n = c + d;
                    if (r.set.Contains(n) && !blocked.Contains(n) && seen.Add(n)) q.Enqueue(n);
                }
            }
            return seen.Count == open.Count;
        }

        private static void WallCollider(Transform col, Vector3 w, Vector2Int d, float H)
        {
            var g = new GameObject("Wall");
            g.transform.SetParent(col, false);
            g.transform.position = w + new Vector3(d.x, 0f, d.y) * 0.55f + Vector3.up * H * 0.5f;
            var bc = g.AddComponent<BoxCollider>();
            bc.size = d.x != 0 ? new Vector3(0.1f, H, 1.02f) : new Vector3(1.02f, H, 0.1f);
        }

        /// <summary>A thin slab under each row of tiles, just below the tiles' own pick boxes.</summary>
        private static void FloorColliders(Transform col, PlaceLayout lay, PlaceLayout.LRoom r)
        {
            foreach (var row in r.cells.GroupBy(c => c.y))
            {
                var xs = row.Select(c => c.x).OrderBy(x => x).ToList();
                int start = xs[0], prev = xs[0];
                foreach (int x in xs.Skip(1).Append(int.MaxValue))
                {
                    if (x == prev + 1) { prev = x; continue; }
                    Vector3 a = lay.World(new Vector2Int(start, row.Key)), b = lay.World(new Vector2Int(prev, row.Key));
                    var g = new GameObject("Floor");
                    g.transform.SetParent(col, false);
                    g.transform.position = (a + b) * 0.5f + Vector3.down * 0.07f;
                    g.AddComponent<BoxCollider>().size = new Vector3(prev - start + 1, 0.1f, 1f);
                    start = prev = x;
                }
            }
        }

        private static void Lights(PlaceSpec p, PlaceLayout lay, PlaceLayout.LRoom r, Transform parent, System.Random rng, float H)
        {
            PlaceLookSpec look = p.look ?? new PlaceLookSpec();
            int n = Mathf.Clamp(Mathf.RoundToInt(r.cells.Count / (float)Mathf.Max(4, look.tilesPerLight)), 1, r.hallway ? 3 : 4);
            // spread out: each next light on the tile farthest from the ones already placed
            var picks = new List<Vector2Int> { r.cells.OrderBy(c => (c - r.cells[r.cells.Count / 2]).sqrMagnitude).First() };
            while (picks.Count < n)
                picks.Add(r.cells.OrderByDescending(c => picks.Min(q => (c - q).sqrMagnitude)).First());
            bool accent = !string.IsNullOrEmpty(look.accent) && rng.NextDouble() < look.accentChance;
            Color c0 = ColorUtility.TryParseHtmlString(accent ? look.accent : look.light, out Color lc) ? lc : Color.white;
            foreach (Vector2Int c in picks)
            {
                var g = new GameObject("Light");
                g.transform.SetParent(parent, false);
                g.transform.position = lay.World(c) + Vector3.up * (H - 0.35f);
                Light l = g.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = c0;
                l.range = look.lightRange;
                l.intensity = look.lightIntensity * SideAreas.BrightnessScale;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
                if (look.flicker > 0f && rng.NextDouble() < look.flicker)
                {
                    var f = g.AddComponent<PlaceFlicker>();
                    f.baseIntensity = l.intensity;
                    f.seed = (float)rng.NextDouble() * 100f;
                }
                PlaceKit.Fixture(g.transform, c0);
            }
        }

        private static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        }
    }

    /// <summary>A failing ceiling light: mostly steady, now and then it stutters or drops out.</summary>
    internal class PlaceFlicker : MonoBehaviour
    {
        public float baseIntensity = 1f, seed;
        private Light l;
        private void Awake() => l = GetComponent<Light>();
        private void Update()
        {
            if (l == null) return;
            float t = Time.time + seed;
            float n = Mathf.PerlinNoise(t * 0.7f, seed);
            l.intensity = n < 0.22f ? baseIntensity * Mathf.PerlinNoise(t * 25f, seed) * 0.6f : baseIntensity * (0.85f + 0.15f * Mathf.PerlinNoise(t * 3f, seed + 7f));
        }
    }

    /// <summary>Static geometry gathered per material, then merged into a few meshes.</summary>
    internal class MeshBatch
    {
        private readonly Dictionary<Material, List<CombineInstance>> parts = new Dictionary<Material, List<CombineInstance>>();

        internal void Add(Mesh mesh, Material mat, Matrix4x4 m)
        {
            if (mesh == null || mat == null) return;
            if (!parts.TryGetValue(mat, out var list)) parts[mat] = list = new List<CombineInstance>();
            for (int i = 0; i < mesh.subMeshCount; i++) list.Add(new CombineInstance { mesh = mesh, subMeshIndex = i, transform = m });
        }

        internal void Flush(Transform parent, string name)
        {
            foreach (var kv in parts)
            {
                // a mesh holds at most ~4 billion indices with 32-bit indexing, but keep chunks moderate for culling
                for (int start = 0; start < kv.Value.Count; start += 400)
                {
                    var chunk = kv.Value.Skip(start).Take(400).ToArray();
                    var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                    // positions are world space: the room template sits at its own spot, so undo that
                    Matrix4x4 inv = parent.worldToLocalMatrix;
                    for (int i = 0; i < chunk.Length; i++) chunk[i].transform = inv * chunk[i].transform;
                    mesh.CombineMeshes(chunk, true, true);
                    mesh.RecalculateBounds();
                    var g = new GameObject(name);
                    g.transform.SetParent(parent, false);
                    g.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = g.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = kv.Key;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
            }
            parts.Clear();
        }
    }
}
