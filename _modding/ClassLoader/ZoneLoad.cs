using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// A zone change runs inside one coroutine step, with Game_isLoading set for only part of it, so the bus's pump never sees
    /// stairs, a breach or the apartment happen: this is where the bus hears of them. Every way between zones clears the level
    /// through <see cref="LevelManager.ClearLevel"/>, which makes it the one moment what's waiting stops making sense.
    /// </summary>
    internal static class ZoneLoad
    {
        /// <summary>A zone is being taken down or put up: new builds don't set Game_isLoading, reloads of a visited zone do.</summary>
        internal static bool Busy => Game.Game_isLoading || Game.Game_isTransitioning;

        /// <summary>
        /// A destroyed object's script stays in memory for as long as anything points at it, and its fields point at its room and
        /// from there at the whole map. Quests and enemies' effects die with their map, but these two lists outlive every map and
        /// never dropped them: each visit to a floor kept the last visit's rooms, tiles and materials, and effects ending scanned it all.
        /// </summary>
        internal static void DropTheDead()
        {
            if (!BugFixes.ZoneLeftovers.Value) return;
            Menu_Quest.instance.activeQuests.RemoveAll(q => q == null);
            EffectManager.instance.activeEffects.RemoveAll(e => e == null);
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.ClearLevel))]
        private static class Patch_ClearLevel
        {
            private static void Prefix()
            {
                DropTheDead();
                if (!EventBus.On) return;
                HitLane.Forget();
                BusWorkers.Forget();
                ParticleLoad.Forget();
            }
        }

        // ---- rooms copied ahead

        // Most of rebuilding a floor is Unity copying the room prefabs: about 14 ms a room, where waking a copy takes under 2.
        // Under an inactive parent a copy runs no game code, so it can be made whenever: the floor that was left for a side area
        // or the apartment, and the floors either side of this one, a room at a time, and Unpack takes the copy where it would
        // have made one. The game still draws every room itself, so all this guesses is which prefabs a floor used last time,
        // and a wrong guess only means a plain copy.

        private const int MostCopies = 110, FramesBetween = 6, SettleFrames = 60;
        private const float SlowFrame = 0.04f;

        private static readonly Dictionary<int, List<GameObject>> floorRooms = new Dictionary<int, List<GameObject>>();
        private static readonly Dictionary<GameObject, Stack<GameObject>> copies = new Dictionary<GameObject, Stack<GameObject>>();
        private static readonly List<GameObject> todo = new List<GameObject>(), unwanted = new List<GameObject>();
        private static List<GameObject> building;
        private static Transform parked;
        private static Map plannedFor, lastMap;
        private static int lastFloor, waited;

        internal const string ParkedName = "SCCL_RoomCopies";
        internal static int CopiesUsed, CopiesMissed, CopiesReady;
        internal static double SlowestCopyMs;
        internal static int CopiesQueued => todo.Count;

        private static bool Copying => EventBus.On;

        [HarmonyPatch(typeof(Map), nameof(Map.Unpack))]
        private static class Patch_Unpack
        {
            private static void Prefix() => building = Copying ? new List<GameObject>() : null;

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
            {
                MethodInfo plain = typeof(Object).GetMethods().First(m => m.Name == nameof(Object.Instantiate) && m.IsGenericMethod && m.GetParameters().Length == 1).MakeGenericMethod(typeof(GameObject));
                MethodInfo ours = AccessTools.Method(typeof(ZoneLoad), nameof(Copied));
                foreach (CodeInstruction c in code)
                {
                    if (c.Calls(plain)) c.operand = ours;
                    yield return c;
                }
            }

            private static void Finalizer(Map __instance)
            {
                if (building != null && building.Count > 1)
                {
                    floorRooms[__instance.floor] = building;
                    lastFloor = __instance.floor;
                    lastMap = __instance;
                }
                building = null;
            }
        }

        private static GameObject Copied(GameObject prefab)
        {
            // a place's rooms are templates in the scene, gone once it's built
            if (!Copying || prefab == null || prefab.scene.IsValid()) return Object.Instantiate(prefab);
            building?.Add(prefab);
            if (Bypass || parked == null || !copies.TryGetValue(prefab, out Stack<GameObject> spare)) return Missed(prefab);
            while (spare.Count > 0)
            {
                GameObject room = spare.Pop();
                CopiesReady--;
                if (room == null) continue;
                room.transform.SetParent(null, true); // wakes it, as making it here would have
                CopiesUsed++;
                return room;
            }
            return Missed(prefab);
        }

        private static GameObject Missed(GameObject prefab)
        {
            CopiesMissed++;
            return Object.Instantiate(prefab);
        }

        /// <summary>What's worth having copied from here, nearest first, up to the cap; whatever's parked beyond that goes.</summary>
        private static void Plan()
        {
            plannedFor = Map.instance;
            waited = -SettleFrames;
            todo.Clear();
            unwanted.Clear();
            if (parked == null)
            {
                // a title screen load takes the scene, and the copies with it
                copies.Clear();
                CopiesReady = 0;
            }
            var left = copies.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
            int room = MostCopies;
            void Want(int floor)
            {
                if (!floorRooms.TryGetValue(floor, out List<GameObject> rooms)) return;
                foreach (GameObject prefab in rooms)
                {
                    if (room == 0 || prefab == null) continue;
                    room--;
                    if (left.TryGetValue(prefab, out int n) && n > 0) left[prefab] = n - 1;
                    else todo.Add(prefab);
                }
            }
            if (lastMap != Map.instance) Want(lastFloor);
            Want(lastFloor + 1);
            Want(lastFloor - 1);
            foreach (var kv in left)
                for (int i = 0; i < kv.Value; i++) unwanted.Add(kv.Key);
        }

        /// <summary>One room at most, and not during a load, a fight or a slow frame: a copy can't be cut in half.</summary>
        internal static void Tick()
        {
            if (!Copying || Map.instance == null || Busy) return;
            if (Map.instance != plannedFor) Plan();
            if (todo.Count == 0 && unwanted.Count == 0) return;
            if (Time.unscaledDeltaTime > SlowFrame || ++waited < FramesBetween || When.InCombat()) return;
            waited = 0;
            if (unwanted.Count > 0)
            {
                GameObject prefab = unwanted[unwanted.Count - 1];
                unwanted.RemoveAt(unwanted.Count - 1);
                if (copies.TryGetValue(prefab, out Stack<GameObject> spare) && spare.Count > 0)
                {
                    GameObject room = spare.Pop();
                    CopiesReady--;
                    if (room != null) Object.Destroy(room);
                }
                return;
            }
            GameObject next = todo[todo.Count - 1];
            todo.RemoveAt(todo.Count - 1);
            if (next == null) return;
            if (parked == null)
            {
                var holder = new GameObject(ParkedName);
                holder.SetActive(false);
                parked = holder.transform;
            }
            int collections = GC.CollectionCount(0);
            var clock = Stopwatch.StartNew();
            GameObject copy = Object.Instantiate(next, parked, false);
            // a collection landing here says nothing about the copy
            if (GC.CollectionCount(0) == collections) SlowestCopyMs = Math.Max(SlowestCopyMs, clock.Elapsed.TotalMilliseconds);
            if (!copies.TryGetValue(next, out Stack<GameObject> ready)) copies[next] = ready = new Stack<GameObject>();
            ready.Push(copy);
            CopiesReady++;
        }

        // ---- joining rooms

        // Each door tile looks for its twin, and each hallway door for its neighbours, by copying and de-duplicating every tile on
        // the map, so the work grows with doors times tiles. Both only want other door tiles within 1.2, and both still check
        // everything themselves: handed the door tiles near them, in the map's order, they reach the game's own answer.

        private const int Reach = 2;
        private struct DoorTile { public NavNode node; public int order; }
        private static readonly Dictionary<long, List<DoorTile>> doorCells = new Dictionary<long, List<DoorTile>>();
        private static readonly List<DoorTile> nearby = new List<DoorTile>();
        private static Map doorsOf;
        private static int doorsAt;
        private static NavNode asking;

        internal static bool Bypass;
        internal static int Shortlists;

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        private static void IndexDoors(Map m)
        {
            if (m == doorsOf && m.mapNodes.Count == doorsAt) return;
            doorsOf = m;
            doorsAt = m.mapNodes.Count;
            doorCells.Clear();
            var seen = new HashSet<NavNode>();
            for (int i = 0; i < m.mapNodes.Count; i++)
            {
                NavNode n = m.mapNodes[i];
                if (n == null || n.doorNode == null || !seen.Add(n)) continue;
                Vector3 p = n.transform.position;
                long key = Key(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z));
                if (!doorCells.TryGetValue(key, out var list)) doorCells[key] = list = new List<DoorTile>(2);
                list.Add(new DoorTile { node = n, order = i });
            }
        }

        private static List<NavNode> DoorsNear(NavNode node)
        {
            IndexDoors(Map.instance);
            nearby.Clear();
            Vector3 p = node.transform.position;
            int cx = Mathf.FloorToInt(p.x), cz = Mathf.FloorToInt(p.z);
            for (int x = cx - Reach; x <= cx + Reach; x++)
                for (int z = cz - Reach; z <= cz + Reach; z++)
                    if (doorCells.TryGetValue(Key(x, z), out var list)) nearby.AddRange(list);
            nearby.Sort((a, b) => a.order.CompareTo(b.order));
            var near = new List<NavNode>(nearby.Count);
            foreach (DoorTile d in nearby) near.Add(d.node);
            return near;
        }

        private static bool Shortlisting => EventBus.On && !Bypass;

        /// <summary>An internal door never asks for the map's tiles: left set, the question would be answered for whatever asks next.</summary>
        [HarmonyPatch(typeof(NavNode), nameof(NavNode.DoorCheck))]
        private static class Patch_DoorCheck
        {
            private static void Prefix(NavNode __instance)
            {
                if (Shortlisting && !__instance.doorNode.isInternalDoor) asking = __instance;
            }

            private static void Finalizer() => asking = null;
        }

        [HarmonyPatch(typeof(NavNode), nameof(NavNode.HallwayDoorCheck))]
        private static class Patch_HallwayDoorCheck
        {
            private static void Prefix(NavNode __instance)
            {
                if (Shortlisting) asking = __instance;
            }

            private static void Finalizer() => asking = null;
        }

        [HarmonyPatch(typeof(Map), nameof(Map.GetNavNodes))]
        private static class Patch_GetNavNodes
        {
            private static bool Prefix(ref List<NavNode> __result)
            {
                if (asking == null) return true;
                __result = DoorsNear(asking);
                asking = null; // only the one question: making a door can ask again, and that's about something else
                Shortlists++;
                return false;
            }
        }
    }
}
