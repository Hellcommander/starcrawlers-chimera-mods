using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// "Which tiles are near here" is asked by every blast, proc radius and area spell, and the game answers by asking Unity
    /// for the position of every tile on the floor. With the bus on the answer comes from a grid of the tiles instead. It gives
    /// the game's own answer, quirks and order included; anything it can't be sure of goes to the game's own search.
    /// </summary>
    internal static class NodeIndex
    {
        private const float CloseEnough = 0.5f, SureWithin = 1.5f, MostRadius = 12f;

        private struct Entry { public NavNode node; public Room room; public int order, at; }

        private static readonly Dictionary<long, List<Entry>> cells = new Dictionary<long, List<Entry>>();
        private static readonly HashSet<Room> rooms = new HashSet<Room>();
        private static Map map;
        private static int stamp, sure = -1;

        internal static bool Bypass;
        internal static int Answered;

        private static int Cell(float v) => Mathf.FloorToInt(v);
        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        internal static void Forget()
        {
            map = null;
            cells.Clear();
            rooms.Clear();
        }

        private static bool Ready(Map m)
        {
            if (!EventBus.On || Bypass || Game.Game_isLoading || m == null) return false;
            int now = m.mapRooms.Count;
            foreach (Room r in m.mapRooms) if (r != null) now = now * 31 + r.roomNodes.Count;
            if (m == map && now == stamp) return true;
            Forget();
            map = m;
            stamp = now;
            int order = 0;
            foreach (Room r in m.mapRooms)
            {
                if (r == null) continue;
                rooms.Add(r);
                for (int i = 0; i < r.roomNodes.Count; i++)
                {
                    NavNode n = r.roomNodes[i];
                    if (n == null) continue;
                    Vector3 p = n.transform.position;
                    long key = Key(Cell(p.x), Cell(p.z));
                    if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<Entry>(1);
                    list.Add(new Entry { node = n, room = r, order = order++, at = i });
                }
            }
            return true;
        }

        /// <summary>The first tile on the game's walk within half a tile, else the nearest; null when only the game can say.</summary>
        internal static NavNode Nearest(Map m, Vector3 pos)
        {
            if (!Ready(m)) return null;
            NavNode first = null, best = null;
            int firstOrder = int.MaxValue, bestOrder = int.MaxValue;
            float bestDistance = float.PositiveInfinity;
            int cx = Cell(pos.x), cz = Cell(pos.z);
            for (int x = cx - 2; x <= cx + 2; x++)
                for (int z = cz - 2; z <= cz + 2; z++)
                {
                    if (!cells.TryGetValue(Key(x, z), out var list)) continue;
                    foreach (Entry e in list)
                    {
                        if (e.node == null) return null;
                        float d = Vector3.Distance(pos, e.node.transform.position);
                        if (d <= CloseEnough && e.order < firstOrder) { first = e.node; firstOrder = e.order; }
                        if (d < bestDistance || (d == bestDistance && e.order < bestOrder)) { best = e.node; bestDistance = d; bestOrder = e.order; }
                    }
                }
            if (first != null) return first;
            return bestDistance <= SureWithin ? best : null; // a closer tile can't be outside the cells searched
        }

        /// <summary>
        /// True when every tile under this spot is in a room the game has hidden. Asked for thousands of particles a frame, so
        /// the map is only checked once a frame and no tile's position is read; a spot off the tiles counts as seen.
        /// </summary>
        internal static bool Hidden(Map m, Vector3 pos)
        {
            if (m != map || sure != Time.frameCount)
            {
                if (!Ready(m)) return false;
                sure = Time.frameCount;
            }
            if (!cells.TryGetValue(Key(Cell(pos.x), Cell(pos.z)), out var list) || list.Count == 0) return false;
            foreach (Entry e in list)
                if (e.room == null || e.room.IsShowing()) return false;
            return true;
        }

        /// <summary>The room's and its linked rooms' tiles in reach, in the game's order; null when only the game can say.</summary>
        internal static List<NavNode> Within(Room room, Vector3 pos, float radius, bool validOnly, bool linked)
        {
            if (radius > MostRadius || !Ready(Map.instance) || !rooms.Contains(room)) return null;
            if (linked)
                foreach (Room r in room.linkedRooms)
                    if (r != null && !rooms.Contains(r)) return null;
            var found = new List<(int rank, int at, NavNode node)>();
            int span = Mathf.CeilToInt(radius) + 1, cx = Cell(pos.x), cz = Cell(pos.z); // a tile on the very edge can sit just over a cell line
            for (int x = cx - span; x <= cx + span; x++)
                for (int z = cz - span; z <= cz + span; z++)
                {
                    if (!cells.TryGetValue(Key(x, z), out var list)) continue;
                    foreach (Entry e in list)
                    {
                        int rank = e.room == room ? 0 : linked ? room.linkedRooms.IndexOf(e.room) + 1 : 0;
                        if (rank == 0 && e.room != room) continue;
                        NavNode n = e.node;
                        if (n == null || Vector3.Distance(pos, n.transform.position) > radius) continue;
                        if (validOnly && (n.IsOccupied() || n.BlockCheck() || n.Block_Mob || n.Block_Boss)) continue;
                        found.Add((rank, e.at, n));
                    }
                }
            found.Sort((a, b) => a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.at.CompareTo(b.at));
            return found.Select(f => f.node).Distinct().ToList();
        }

        [HarmonyPatch(typeof(Map), nameof(Map.GetNearestNavNodeAt), new[] { typeof(Vector3), typeof(float), typeof(bool) })]
        private static class Patch_Nearest
        {
            private static bool Prefix(Map __instance, Vector3 pos, bool validNodesOnly, ref NavNode __result)
            {
                if (validNodesOnly) return true;
                NavNode n = Nearest(__instance, pos);
                if (n == null) return true;
                Answered++;
                __result = n;
                return false;
            }
        }

        [HarmonyPatch(typeof(Room), nameof(Room.GetNavNodesAt), new[] { typeof(Vector3), typeof(float), typeof(bool), typeof(bool) })]
        private static class Patch_Within
        {
            private static bool Prefix(Room __instance, Vector3 pos, float radius, bool validNodesOnly, bool useLinkedRooms, ref List<NavNode> __result)
            {
                List<NavNode> near = Within(__instance, pos, radius, validNodesOnly, useLinkedRooms);
                if (near == null) return true;
                Answered++;
                __result = near;
                return false;
            }
        }

        /// <summary>Swaps a tile without changing any count.</summary>
        [HarmonyPatch(typeof(Room), nameof(Room.ReplaceNavNode))]
        private static class Patch_Replaced
        {
            private static void Postfix() => Forget();
        }
    }
}
