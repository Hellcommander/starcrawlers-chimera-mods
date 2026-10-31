using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// Generated side areas: places outside Chimera (wrecks, bases, labs, mines) built at runtime from a PlaceSpec
    /// and a seed instead of borrowing one of the game's floors. The map's id carries the place, seed and room count
    /// ("SCCL_Place|derelict|12345|12"), so a save made inside one rebuilds the same layout before its state goes back
    /// on. Kinds choose them with "place" / "places"; a kind without either stays an echo of a real floor.
    /// </summary>
    internal static class Places
    {
        internal const string Prefix = "SCCL_Place|";
        /// <summary>A light with this name stays on through a blackout (the lamp over the way out).</summary>
        internal const string KeepLit = "SCCL_KeepLit";
        internal static readonly List<PlaceSpec> All = new List<PlaceSpec>();

        private class Pending { public PlaceBuilder.Built built; public PlaceKit kit; }
        private static Pending pending;
        private static PlaceLayout layHere;
        private static PlaceKit kitHere;

        internal static PlaceSpec Get(string id) => All.FirstOrDefault(p => string.Equals(p.id, id, StringComparison.OrdinalIgnoreCase));

        internal static bool IsPlace(string mapId) => mapId != null && mapId.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>The map now loaded is a generated place.</summary>
        internal static bool Here => Map.instance != null && IsPlace(Map.instance.internalName);

        internal static PlaceSpec Current => Here ? Get(Parse(Map.instance.internalName).id) : null;

        private static (string id, int seed, int rooms) Parse(string mapId)
        {
            string[] parts = mapId.Substring(Prefix.Length).Split('|');
            int seed = parts.Length > 1 && int.TryParse(parts[1], out int s) ? s : 1;
            int rooms = parts.Length > 2 && int.TryParse(parts[2], out int r) ? r : 12;
            return (parts[0], seed, rooms);
        }

        // ---- choosing ---------------------------------------------------------------------------------------

        private static List<string> Ids(KindSpec k) =>
            (k?.places ?? new List<string>()).Concat(string.IsNullOrEmpty(k?.place) ? new string[0] : new[] { k.place })
                .Where(id => string.Equals(id, "echo", StringComparison.OrdinalIgnoreCase) || Get(id) != null).ToList();

        /// <summary>Which place this floor's side area is (picked once for the breach, fresh for each deeper layer); null = an echo.</summary>
        internal static PlaceSpec Pick(KindSpec k, int floor, bool firstLayer)
        {
            var ids = Ids(k);
            if (ids.Count == 0) return null;
            var d = SideAreas.Data;
            string key = "place_" + floor;
            string id = firstLayer && d != null && d.TryGetValue(key, out object v) && v is string s && ids.Contains(s) ? s : ids[Random.Range(0, ids.Count)];
            if (firstLayer && d != null) d[key] = id;
            return Get(id);
        }

        /// <summary>The place this floor's breach leads into, for the board and the breach finder (null = an echo).</summary>
        internal static PlaceSpec Of(int floor) => Pick(SideKinds.KindOf(floor), floor, true);

        // ---- making -----------------------------------------------------------------------------------------

        /// <summary>A new place for this kind, un-initialised like a map fresh from the Atlas: map.Init() builds it.</summary>
        internal static Map Create(PlaceSpec p, KindSpec k, int origin, int theme)
        {
            int seed = Random.Range(1, int.MaxValue);
            int[] size = k?.size != null && k.size.Length > 0 ? k.size : p.rooms;
            int rooms = size != null && size.Length >= 2 ? Random.Range(size[0], size[1] + 1) : size != null && size.Length == 1 ? size[0] : 12;
            return Make(p, $"{Prefix}{p.id}|{seed}|{rooms}", seed, rooms, origin, theme, spawns: true);
        }

        /// <summary>For a save: the same place rebuilt from its map id (null if the id isn't a place).</summary>
        internal static Map LoadMap(string mapId)
        {
            if (!IsPlace(mapId)) return null;
            var (id, seed, rooms) = Parse(mapId);
            PlaceSpec p = Get(id);
            if (p == null)
            {
                Plugin.Log.LogWarning($"Places: '{id}' is no longer loaded; rebuilding the saved side area from the first place's look");
                p = All.FirstOrDefault() ?? new PlaceSpec { id = id };
            }
            return Make(p, mapId, seed, rooms, Game.FLOOR, Game.FLOOR, spawns: false);
        }

        private static Map Make(PlaceSpec p, string mapId, int seed, int rooms, int origin, int theme, bool spawns)
        {
            Drop();
            var t0 = DateTime.Now;
            Map like = LikeMap(origin) ?? LikeMap(theme);
            Door door = DoorPrefab(origin) ?? DoorPrefab(theme);
            if (door == null) throw new InvalidOperationException("no door prefab found for a generated place");
            var kit = new PlaceKit(p);
            PlaceBuilder.Built b = PlaceBuilder.Make(p, kit, mapId, seed, rooms, origin, like, door, spawns ? Pool(origin) : null);
            pending = new Pending { built = b, kit = kit };
            Plugin.Log.LogInfo($"Places: {p.id} seed {seed}: {b.lay.Describe()} ({(DateTime.Now - t0).TotalMilliseconds:0} ms)");
            return b.map;
        }

        private static void Drop()
        {
            if (pending?.built?.holder != null) Object.Destroy(pending.built.holder);
            pending = null;
        }

        /// <summary>
        /// After the map's own setup (map.Init on a new visit, map.Deserialize on a load): the templates go, the way
        /// out goes in, and the doors get their look (and, on a new visit, their jammed or archway state).
        /// </summary>
        internal static void Finish(bool fresh)
        {
            if (pending == null || Map.instance != pending.built.map) return;
            layHere = pending.built.lay;
            kitHere = pending.kit;
            PlaceSpec p = pending.built.spec;
            var chests = pending.built.chests;
            Object.Destroy(pending.built.holder);
            pending = null;
            try { Exit(p); }
            catch (Exception e) { Plugin.Log.LogError("Places: the way out: " + e); }
            try { Doors(fresh); }
            catch (Exception e) { Plugin.Log.LogError("Places: doors: " + e); }
            // a reload gets its containers back from the save, opened or not
            if (fresh && chests != null)
                try { Chests(chests); }
                catch (Exception e) { Plugin.Log.LogError("Places: containers: " + e); }
        }

        // ---- containers ---------------------------------------------------------------------------------------

        private static readonly System.Reflection.MethodInfo GetContainer = HarmonyLib.AccessTools.Method(typeof(InteractiveManager), "GetContainer");

        /// <summary>
        /// The searchable props become the real floor's own containers, set up the way the game sets up a floor's:
        /// some locked (lockpick or bash them), and on deeper floors a chance that one of them is a mimic.
        /// </summary>
        private static void Chests(List<PlaceBuilder.Chest> spots)
        {
            InteractiveManager im = InteractiveManager.instance;
            if (im == null || GetContainer == null || spots.Count == 0) return;
            // about a floor's worth to search; the rest stand open and empty, already picked clean
            int full = Mathf.Clamp(spots.Count / 3, Math.Min(4, spots.Count), 8);
            int locks = Mathf.RoundToInt(full * Random.Range(0.3f, 0.5f));
            bool mimic = Random.Range(1, 101) <= (Game.FLOOR + 1) * 10;
            int made = 0;
            foreach (PlaceBuilder.Chest s in spots.OrderBy(_ => Random.value))
            {
                Room room = Map.instance.GetRoomAt(s.at) ?? Map.instance.GetRoomContaining(s.at);
                if (room == null || !(GetContainer.Invoke(im, new object[] { Game.FLOOR }) is Container c)) continue;
                c.transform.position = s.at + s.wall * 0.18f;
                c.transform.rotation = Quaternion.LookRotation(-s.wall); // its front to the room
                if (c.nodeSnapPoint == null)
                {
                    c.nodeSnapPoint = new GameObject(c.internalName + "_NODESNAP").transform;
                    c.nodeSnapPoint.SetParent(c.transform);
                }
                c.nodeSnapPoint.position = s.front;
                c.room = room;
                c.Init();
                c.gameObject.name += "_SCCL" + made;
                im.RegisterInteractiveObject(c);
                if (made >= full) Emptied(c);
                else if (mimic) { mimic = false; c.isMimic = true; }
                else if (locks > 0) { locks--; c.Lock(); }
                made++;
            }
            Plugin.Log.LogInfo($"Places: {made} container(s), {Math.Min(made, full)} to search");
        }

        private static readonly System.Reflection.MethodInfo AnimOpen = HarmonyLib.AccessTools.Method(typeof(Container), "Anim_Open");
        private static readonly System.Reflection.MethodInfo OpenTip = HarmonyLib.AccessTools.Method(typeof(Container), "UpdateTooltip");

        /// <summary>Open and empty, the way a searched one is left (and saved).</summary>
        private static void Emptied(Container c)
        {
            c.completed = true;
            Collider col = c.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            c.ClearMiniMapMarker();
            AnimOpen?.Invoke(c, null);
            OpenTip?.Invoke(c, null);
        }

        /// <summary>A side area has a lot of them, so each holds one or two things instead of a floor container's three to six.</summary>
        [HarmonyLib.HarmonyPatch(typeof(Container), nameof(Container.Interact))]
        private static class Patch_ContainerHaul
        {
            private static void Prefix(Container __instance)
            {
                if (!Here || __instance.completed || __instance.contents.Count > 0 || __instance.isBossLootContainer) return;
                __instance.minItems = 1;
                __instance.maxItems = 2;
            }
        }

        // ---- what the place is built from -------------------------------------------------------------------

        private static Map LikeMap(int floor) =>
            floor >= 0 && floor < Atlas.instance.maps.Length ? Atlas.instance.maps[floor]?.maps?.FirstOrDefault(m => m != null && m.stairPrefab != null) : null;

        private static Door doorPrefab;

        /// <summary>The game's ordinary door, for its logic (locks, bashing, picking, saving); its look is replaced.</summary>
        private static Door DoorPrefab(int floor)
        {
            if (doorPrefab != null) return doorPrefab;
            if (floor < 0 || floor >= Atlas.instance.maps.Length) return null;
            foreach (Map m in Atlas.instance.maps[floor]?.maps ?? new Map[0])
                foreach (PackedRoom pr in m?._packedRooms ?? new List<PackedRoom>())
                {
                    GameObject v = null;
                    try { v = pr?.Properties?.UseSpecificRoomVariant ?? Atlas.instance.GetRoomVariant(pr.PrefabGroupName); }
                    catch { }
                    if (v == null) continue;
                    foreach (DoorNode dn in v.GetComponentsInChildren<DoorNode>(true))
                        if (dn.doorPrefab != null && !dn.doorPrefab.FLAG_isUberDoor && !dn.isInternalDoor && dn.doorPrefab.collider_closed != null)
                        {
                            Plugin.Log.LogInfo($"Places: doors use the logic of '{dn.doorPrefab.name}'");
                            return doorPrefab = dn.doorPrefab;
                        }
                }
            return null;
        }

        private static readonly Dictionary<int, List<Character>> pools = new Dictionary<int, List<Character>>();

        /// <summary>Who lives on this floor: every enemy its own rooms can spawn at this depth (rosters swap some later).</summary>
        private static List<Character> Pool(int floor)
        {
            if (pools.TryGetValue(floor, out var list)) return list;
            list = new List<Character>();
            if (floor >= 0 && floor < Atlas.instance.maps.Length)
                foreach (Map m in Atlas.instance.maps[floor]?.maps ?? new Map[0])
                    foreach (PackedRoom pr in m?._packedRooms ?? new List<PackedRoom>())
                    {
                        GameObject v = null;
                        try { v = pr?.Properties?.UseSpecificRoomVariant ?? Atlas.instance.GetRoomVariant(pr.PrefabGroupName); }
                        catch { }
                        if (v == null) continue;
                        foreach (Spawn_Control sc in v.GetComponentsInChildren<Spawn_Control>(true))
                        {
                            if (sc.secretRoomOnly || floor < sc.floorSpawnRange.x || floor > sc.floorSpawnRange.y) continue;
                            foreach (Spawn_Point sp in sc.GetComponentsInChildren<Spawn_Point>(true))
                            {
                                if (sp.GetType() != typeof(Spawn_Point) || sp.spawns == null) continue;
                                foreach (Character c in sp.spawns)
                                {
                                    try
                                    {
                                        if (c == null || c.ai == null || c.ai.aiTeam != AI.AI_TEAM.Hostile || c.HasTag("BOSS") || c.HasTag("RARE") || c.HasTag("RARE_ELITE")) continue;
                                        list.Add(c);
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
            if (list.Count == 0)
                foreach (string id in new[] { "CORPSEC_GUARD_MELEE", "CORPSEC_COMMANDO" })
                {
                    Character c = AI_Manager.instance.GetCharacter(id);
                    if (c != null) list.Add(c);
                }
            Plugin.Log.LogInfo($"Places: floor {floor} enemies: {string.Join(", ", list.GroupBy(c => c.GetInternalName()).Select(g => g.Key + " x" + g.Count()))}");
            return pools[floor] = list;
        }

        // ---- the way out --------------------------------------------------------------------------------------

        /// <summary>The game's stair object (so taking it, and every side-area rule about stairs, works as usual) in the place's own look.</summary>
        private static void Exit(PlaceSpec p)
        {
            Map map = Map.instance;
            if (map.stairPrefab == null) return;
            Vector3 at = layHere.World(layHere.exitCell);
            Vector3 wall = new Vector3(layHere.exitWall.x, 0f, layHere.exitWall.y);
            Stair s = Object.Instantiate(map.stairPrefab);
            s.transform.rotation = Quaternion.LookRotation(-wall);
            Vector3 nl = s.nodeLocator != null ? s.nodeLocator.position - s.transform.position : Vector3.zero;
            s.transform.position = at - nl;
            s.isUpStair = false;
            s.gameObject.name = "Stair_Exit";
            Hide(s.gameObject);
            map.RegisterStair(s);
            s.Init();
            var look = new GameObject("SCCL_ExitLook").transform;
            look.SetParent(Unscaled(s.transform), false);
            look.position = at;
            look.rotation = Quaternion.LookRotation(-wall);
            kitHere.ExitLook(look);
            // the way out always has a lamp over it, blackout or not
            var lamp = new GameObject(KeepLit).AddComponent<Light>();
            lamp.transform.SetParent(look, false);
            lamp.transform.localPosition = new Vector3(0f, 1.3f, 0.1f);
            lamp.type = LightType.Point;
            lamp.color = new Color(0.55f, 1f, 0.78f);
            lamp.range = 2.6f;
            lamp.intensity = 1.1f;
            lamp.shadows = LightShadows.None;
            // clicking the exit: a box over the new look, on the stair itself (the old one sits in the wall now)
            var bc = s.gameObject.AddComponent<BoxCollider>();
            Vector3 c = s.transform.InverseTransformPoint(at + wall * 0.3f + Vector3.up * 1.1f);
            bc.center = c;
            bc.size = new Vector3(1.4f, 2.2f, 0.4f);
            var rs = look.GetComponentsInChildren<Renderer>(true);
            Bounds lb = rs.Length > 0 ? rs.Select(r => r.bounds).Aggregate((a, x) => { a.Encapsulate(x); return a; }) : new Bounds(at, Vector3.zero);
            Plugin.Log.LogInfo($"Places: way out at {at} (wall side {wall}), stair node {(s.myNode != null ? s.myNode.name : "MISSING")}; its look: {rs.Length} renderers " +
                               $"({rs.Count(r => r.enabled && r.gameObject.activeInHierarchy)} showing), bounds {lb.min}..{lb.max}");
        }

        /// <summary>The game's own meshes on an object we keep for its logic: gone, so only our look shows.</summary>
        private static void Hide(GameObject g)
        {
            foreach (MeshFilter mf in g.GetComponentsInChildren<MeshFilter>(true)) mf.sharedMesh = null;
            foreach (SkinnedMeshRenderer smr in g.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { smr.sharedMesh = null; smr.enabled = false; }
            foreach (MeshRenderer mr in g.GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = false;
        }

        // ---- doors ----------------------------------------------------------------------------------------------

        private static void Doors(bool fresh)
        {
            int made = 0, arches = 0, jammed = 0;
            foreach (Door d in Map.instance.mapDoors.Where(x => x != null).ToList())
                try { Door(d, fresh, ref made, ref arches, ref jammed); }
                catch (Exception e) { Plugin.Log.LogError($"Places: door {d.name}: {e}"); }
            Plugin.Log.LogInfo($"Places: {made} doors, {arches} archways, {jammed} jammed open{(fresh ? "" : " (restored)")}" +
                               (doorPrefab != null ? $"; door prefab scale {doorPrefab.transform.localScale}" : ""));
        }

        private static void Door(Door d, bool fresh, ref int made, ref int arches, ref int jammed)
        {
            {
                PlaceLayout.LDoor ld = d.node == null ? null : layHere.doors.OrderBy(x => (layHere.World(x.cell) - d.node.transform.position).sqrMagnitude).FirstOrDefault();
                string state = ld?.state ?? "door";
                Hide(d.gameObject);
                if (state != "arch")
                {
                    var look = d.gameObject.AddComponent<PlaceDoor>();
                    look.door = d;
                    look.leaf = kitHere.DoorLeaf(Unscaled(d.transform), out Renderer lamp);
                    look.lamp = lamp;
                    look.slide = kitHere.Slide;
                    look.height = kitHere.DoorHeight;
                    made++;
                }
                else arches++;
                if (fresh && state != "door")
                {
                    bool was = Game.Game_isLoading;
                    Game.Game_isLoading = true; // no door sounds while the place is being made
                    try { d.Break(null); }
                    finally { Game.Game_isLoading = was; }
                    if (state == "open") jammed++;
                }
            }
        }

        /// <summary>A child of t that undoes t's scale, so our looks come out in metres whatever the game's prefab was scaled to.</summary>
        private static Transform Unscaled(Transform t)
        {
            var g = new GameObject("SCCL_Look").transform;
            g.SetParent(t, false);
            Vector3 s = t.lossyScale;
            g.localScale = new Vector3(1f / NonZero(s.x), 1f / NonZero(s.y), 1f / NonZero(s.z));
            return g;
        }

        private static float NonZero(float v) => Mathf.Abs(v) < 0.0001f ? 1f : v;

        // ---- look ---------------------------------------------------------------------------------------------

        private static bool saved;
        private static AmbientMode oldMode;
        private static Color oldAmbient, oldFog;
        private static bool oldFogOn;
        private static FogMode oldFogMode;

        /// <summary>Called whenever a level's render settings load: a place sets its own light and fog, anywhere else gets the old ones back.</summary>
        internal static void ApplyLook()
        {
            PlaceSpec p = SideAreas.InSide ? Current : null;
            if (p == null) { RestoreLook(); return; }
            PlaceLookSpec look = p.look ?? new PlaceLookSpec();
            if (!saved)
            {
                saved = true;
                oldMode = RenderSettings.ambientMode;
                oldAmbient = RenderSettings.ambientLight;
                oldFog = RenderSettings.fogColor;
                oldFogOn = RenderSettings.fog;
                oldFogMode = RenderSettings.fogMode;
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            if (ColorUtility.TryParseHtmlString(look.ambient ?? "", out Color a)) RenderSettings.ambientLight = a * SideAreas.BrightnessScale;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            if (ColorUtility.TryParseHtmlString(look.fog ?? "", out Color f)) RenderSettings.fogColor = f;
            RenderSettings.fogStartDistance = look.fogStart;
            RenderSettings.fogEndDistance = look.fogEnd;
        }

        internal static void RestoreLook()
        {
            if (!saved) return;
            saved = false;
            RenderSettings.ambientMode = oldMode;
            RenderSettings.ambientLight = oldAmbient;
            RenderSettings.fogColor = oldFog;
            RenderSettings.fog = oldFogOn;
            RenderSettings.fogMode = oldFogMode;
        }

        /// <summary>The minimap's map name: the place's own.</summary>
        internal static void NameOnMap()
        {
            PlaceSpec p = Current;
            Menu_MiniMap mm = Menu_MiniMap.instance;
            if (p == null || mm == null || mm.mapName == null) return;
            mm.mapName.text = p.name;
            mm.mapName.transform.parent.gameObject.SetActive(true);
            if (mm.bigMapName != null) { mm.bigMapName.text = p.name; mm.bigMapName.transform.parent.gameObject.SetActive(true); }
        }
    }

    /// <summary>A saved place has no map in the Atlas: its id rebuilds it (every other id is the Atlas's own).</summary>
    [HarmonyLib.HarmonyPatch(typeof(Atlas), nameof(Atlas.LoadMap))]
    internal static class Patch_PlaceLoadMap
    {
        private static bool Prefix(string mapId, ref Map __result)
        {
            if (!Places.IsPlace(mapId)) return true;
            try { __result = Places.LoadMap(mapId); }
            catch (Exception e) { Plugin.Log.LogError("Places: rebuilding a saved place: " + e); __result = null; }
            return __result == null;
        }
    }

    /// <summary>A place's door leaf: slides open and shut with the game's door, and its lamp shows locked or sealed.</summary>
    internal class PlaceDoor : MonoBehaviour
    {
        public Door door;
        public Transform leaf;
        public Renderer lamp;
        public string slide = "side";
        public float height = 2.3f;
        private Vector3 shut;
        private int lampState = -1;

        private void Start()
        {
            if (leaf == null) return;
            shut = leaf.localPosition;
            if (door != null && door.isOpen) leaf.localPosition = Target();
        }

        private Vector3 Target() => !door.isOpen ? shut : slide == "up" ? shut + Vector3.up * (height - 0.12f) : shut + Vector3.right * 0.95f;

        private void Update()
        {
            if (door == null || leaf == null) return;
            leaf.localPosition = Vector3.MoveTowards(leaf.localPosition, Target(), Time.deltaTime * 4f);
            int s = door.isSealed ? 2 : door.isLocked ? 1 : 0;
            if (s == lampState || lamp == null) return;
            lampState = s;
            lamp.sharedMaterial = PlaceKit.Lamp(s == 2 ? "#FFB040" : s == 1 ? "#FF3B3B" : "#40FF90");
        }
    }
}
