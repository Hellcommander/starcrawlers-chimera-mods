// Free to use, copy and change for any purpose, like the example plugin.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Breachwork;
using HarmonyLib;
using UnityEngine;

namespace Threading
{
    /// <summary>
    /// Enemy steps worked out together. The game paths one enemy at a time on the main thread when its turn comes; here the
    /// map is copied as the enemy phase starts, and every awake enemy's path to you is solved on the workers. When that
    /// enemy's turn comes the answer is checked against the live map; if the step no longer holds the game paths as usual.
    /// The solver is the game's own search with its order of ties, on arrays instead of its lists.
    /// </summary>
    public static class BatchPaths
    {
        private const int MinCrowd = 12;
        private const int Empty = -1, IsPlayer = -2, NoTeam = -3;
        private const int Hostile = (int)AI.AI_TEAM.Hostile, Outsider = (int)AI.AI_TEAM.Outsider;
        private const double SpinMs = 3.0;
        private const float BatchEvery = 0.5f;
        private const int FramesToRead = 8;

        internal static int Hits, Misses, Solved;

        /// <summary>What doesn't change while a map is up.</summary>
        private sealed class Layout
        {
            public Map map;
            public NavNode[] nodes;
            public Dictionary<NavNode, int> index;
            public int[] linkStart, links, roomOf;
            public float[] x, y, z;
            public Room[] rooms;
            public int[][] roomNodes, roomLinks;
        }

        /// <summary>The map as it stood when the phase began. Read by the workers, never written after.</summary>
        private sealed class Snapshot
        {
            public Layout at;
            public bool[] blockMove, blockMob, blockBoss, blocked, doorShut, edgeBlocked;
            public int[] occupant;
        }

        private sealed class Job
        {
            public Snapshot map;
            public int phase, start, target, team;
            public bool canDoor, boss, hunter;
            public int state; // 0 waiting, 1 taken
            public volatile int[] path;
        }

        private static readonly AccessTools.FieldRef<AI_Manager, List<AI>> Ais = AccessTools.FieldRefAccess<AI_Manager, List<AI>>("ais");
        private static readonly AccessTools.FieldRef<AI_Manager, List<AI>> AisFast = AccessTools.FieldRefAccess<AI_Manager, List<AI>>("ais_fast");
        private static readonly Dictionary<AI, Job> waiting = new Dictionary<AI, Job>();
        private static Layout layout;
        private static int phase;
        private static float nextBatch;
        private static Snapshot reading;
        private static int read;

        private static Layout Current()
        {
            if (layout != null && layout.map == Map.instance && layout.nodes.Length > 0 && layout.nodes[0] != null) return layout;
            // room copies waiting for a later floor are in the scene too, under this root, with tiles that were never set up
            var found = System.Array.FindAll(UnityEngine.Object.FindObjectsOfType<NavNode>(true), n => n.transform.root.name != "SCCL_RoomCopies");
            var l = new Layout { map = Map.instance, nodes = found, index = new Dictionary<NavNode, int>(found.Length) };
            for (int i = 0; i < found.Length; i++) l.index[found[i]] = i;
            l.x = new float[found.Length]; l.y = new float[found.Length]; l.z = new float[found.Length];
            l.roomOf = new int[found.Length];
            l.linkStart = new int[found.Length + 1];
            var links = new List<int>(found.Length * 8);
            var rooms = new List<Room>();
            var roomIndex = new Dictionary<Room, int>();
            for (int i = 0; i < found.Length; i++)
            {
                Vector3 pos = found[i].transform.position;
                l.x[i] = pos.x; l.y[i] = pos.y; l.z[i] = pos.z;
                l.linkStart[i] = links.Count;
                foreach (NavNode n in found[i].GetLinks())
                    links.Add(n != null && l.index.TryGetValue(n, out int to) ? to : -1); // -1 keeps the game's link order
                Room room = found[i].GetRoom();
                l.roomOf[i] = -1;
                if (room == null) continue;
                if (!roomIndex.TryGetValue(room, out int r)) { r = rooms.Count; rooms.Add(room); roomIndex[room] = r; }
                l.roomOf[i] = r;
            }
            l.linkStart[found.Length] = links.Count;
            l.links = links.ToArray();
            l.rooms = rooms.ToArray();
            l.roomNodes = new int[rooms.Count][];
            l.roomLinks = new int[rooms.Count][];
            for (int r = 0; r < rooms.Count; r++)
            {
                var own = new List<int>(rooms[r].roomNodes.Count);
                foreach (NavNode n in rooms[r].roomNodes) if (n != null && l.index.TryGetValue(n, out int i)) own.Add(i);
                l.roomNodes[r] = own.ToArray();
                var next = new List<int>(rooms[r].linkedRooms.Count);
                foreach (Room o in rooms[r].linkedRooms) if (o != null && roomIndex.TryGetValue(o, out int i)) next.Add(i);
                l.roomLinks[r] = next.ToArray();
            }
            layout = l;
            return l;
        }

        private static Snapshot Blank()
        {
            Layout l = Current();
            int count = l.nodes.Length;
            return new Snapshot
            {
                at = l, blockMove = new bool[count], blockMob = new bool[count], blockBoss = new bool[count], blocked = new bool[count],
                doorShut = new bool[count], occupant = new int[count], edgeBlocked = new bool[l.links.Length]
            };
        }

        private static Snapshot Capture()
        {
            Snapshot s = Blank();
            Fill(s, 0, s.at.nodes.Length);
            return s;
        }

        private static void Fill(Snapshot s, int from, int end)
        {
            Layout l = s.at;
            for (int i = from; i < end; i++)
            {
                NavNode n = l.nodes[i];
                if (n == null) { s.blockMove[i] = s.blocked[i] = true; s.occupant[i] = Empty; continue; }
                Room room = n.GetRoom();
                s.blockMove[i] = n.Block_Move;
                s.blockMob[i] = n.Block_Mob;
                s.blockBoss[i] = n.Block_Boss;
                s.blocked[i] = n.Block_Move || room == null || room.BlockCheck(n);
                s.doorShut[i] = n.HasDoor() && !n.GetDoor().isOpen;
                Character who = n.GetOccupant();
                s.occupant[i] = who == null ? Empty : who == Player.instance ? IsPlayer : who.ai != null ? (int)who.ai.GetTeam() : NoTeam;
                for (int k = l.linkStart[i]; k < l.linkStart[i + 1]; k++)
                {
                    int to = l.links[k];
                    if (to < 0) continue;
                    NavNode next = l.nodes[to];
                    Room there = next != null ? next.GetRoom() : null;
                    s.edgeBlocked[k] = there == null || there.BlockCheck(next, n);
                }
            }
        }

        /// <summary>One enemy phase's paths, handed to the workers before its first enemy moves.</summary>
        private static void Prepare(List<AI> list, Snapshot ready = null)
        {
            waiting.Clear();
            int now = Interlocked.Increment(ref phase); // whatever is left of the last phase isn't worth solving now
            if (list == null || list.Count < MinCrowd || Map.instance == null || Player.instance == null || !Api.WorkersHealthy) return;
            NavNode goal = Player.instance.GetMover()?.GetCurrentNavNode();
            if (goal == null) return;
            Snapshot map = ready;
            var all = new List<Job>(list.Count);
            foreach (AI ai in list)
            {
                if (ai == null || !ai.gameObject.activeInHierarchy || ai.GetState() == AI.AI_STATE.Idle || ai.GetState() == AI.AI_STATE.Dead) continue;
                Character c = ai.GetCharacter();
                NavNode from = c != null && !c.GetIsDead() ? c.GetMover()?.GetCurrentNavNode() : null;
                Character wants = ai.GetTarget();
                NavNode to = wants != null ? wants.GetMover()?.GetCurrentNavNode() : null;
                if (to == null) to = goal;
                if (from == null || from == to) continue;
                map = map ?? Capture();
                if (!map.at.index.TryGetValue(from, out int start) || !map.at.index.TryGetValue(to, out int target)) continue;
                if (!InReach(map.at, start, target)) continue; // the game won't path past the next room, so nothing would ask for this
                var job = new Job
                {
                    map = map, phase = now, start = start, target = target, team = (int)ai.GetTeam(), hunter = ai.FLAG_isHunter,
                    canDoor = ai.FLAG_canOpenDoors || ai.FLAG_canBashDoors, boss = c.HasTag("BOSS")
                };
                waiting[ai] = job;
                all.Add(job);
            }
            if (all.Count == 0) return;
            int pieces = Api.WorkerPieces(all.Count);
            for (int p = 0, from = 0; p < pieces; p++)
            {
                int to = (int)((long)all.Count * (p + 1) / pieces);
                Send(all.GetRange(from, to - from));
                from = to;
            }
        }

        /// <summary>
        /// Everyone on their own timers has no phase to start from, so the paths are worked out every so often instead. Reading the
        /// whole map in a frame was a hitch twice a second, and each path is checked against the map on use, so the reading is spread out.
        /// </summary>
        internal static void Tick()
        {
            if (!Api.EventBusOn || !Api.SimultaneousTurns || AI_Manager.instance == null)
            {
                reading = null;
                return;
            }
            if (reading != null)
            {
                if (reading.at.map != Map.instance)
                {
                    reading = null;
                    return;
                }
                int count = reading.at.nodes.Length, end = Mathf.Min(read + Mathf.Max(1, count / FramesToRead), count);
                Fill(reading, read, end);
                read = end;
                if (read < count) return;
                Prepare(Ais(AI_Manager.instance), reading);
                reading = null;
                return;
            }
            if (Time.unscaledTime < nextBatch || Map.instance == null) return;
            nextBatch = Time.unscaledTime + BatchEvery;
            if (Ais(AI_Manager.instance).Count < MinCrowd) return;
            reading = Blank();
            read = 0;
        }

        private static bool InReach(Layout l, int from, int to)
        {
            int a = l.roomOf[from], b = l.roomOf[to];
            return a >= 0 && b >= 0 && (a == b || Array.IndexOf(l.roomLinks[a], b) >= 0);
        }

        // A refused chunk is still waiting: each of its enemies solves its own path on its turn.
        private static void Send(List<Job> chunk) => Api.RunOnWorker(() =>
        {
            foreach (Job j in chunk) if (j.phase == Volatile.Read(ref phase)) Take(j);
        });

        private static void Take(Job j)
        {
            if (Interlocked.CompareExchange(ref j.state, 1, 0) != 0) return;
            j.path = Solve(j);
            Interlocked.Increment(ref Solved);
        }

        // ---- the search

        private sealed class Scratch
        {
            public int[] g, f, parent, seen, closed, open, available;
            public int stamp;
            public List<int> list = new List<int>(64);
            public Scratch(int n) { g = new int[n]; f = new int[n]; parent = new int[n]; seen = new int[n]; closed = new int[n]; open = new int[n]; available = new int[n]; }
        }

        [ThreadStatic] private static Scratch scratch;

        private static int Distance(Layout l, int a, int b)
        {
            float dx = l.x[a] - l.x[b], dy = l.y[a] - l.y[b], dz = l.z[a] - l.z[b];
            return (int)(float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>The tiles an enemy will consider, as the game's pursuit lists them: its room and those linked, less what's closed to it.</summary>
        private static bool Allow(Job j, Scratch s, int stamp)
        {
            Snapshot m = j.map;
            Layout l = m.at;
            int home = l.roomOf[j.start];
            if (home < 0) return false;
            bool hasTarget = false;
            void Room(int r)
            {
                foreach (int i in l.roomNodes[r])
                {
                    if (i == j.target) hasTarget = true;
                    if (m.blockMob[i] || m.blockMove[i] || (m.blockBoss[i] && j.boss)) continue;
                    int who = m.occupant[i];
                    bool ok = who == Empty;
                    if (!ok && j.hunter) ok = who == IsPlayer || (who >= 0 && who != Hostile);
                    else if (!ok) ok = j.team == Hostile || (j.team == Outsider && (who == IsPlayer || who == j.team));
                    if (ok) s.available[i] = stamp;
                }
            }
            Room(home);
            foreach (int r in l.roomLinks[home]) Room(r);
            s.available[j.start] = stamp;
            s.available[j.target] = stamp;
            return hasTarget;
        }

        private static int[] Solve(Job j)
        {
            Snapshot m = j.map;
            Layout l = m.at;
            Scratch s = scratch;
            if (s == null || s.g.Length < l.nodes.Length) s = scratch = new Scratch(l.nodes.Length);
            int stamp = ++s.stamp;
            if (!Allow(j, s, stamp)) return new int[0];

            List<int> open = s.list;
            open.Clear();
            open.Add(j.start);
            s.open[j.start] = stamp;
            s.g[j.start] = 0;
            s.f[j.start] = 0;
            bool found = false;
            while (!found)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++) if (s.f[open[i]] < s.f[open[best]]) best = i;
                int cur = open[best];
                open.RemoveAt(best);
                s.open[cur] = 0;
                s.closed[cur] = stamp;
                for (int k = l.linkStart[cur]; k < l.linkStart[cur + 1]; k++)
                {
                    int to = l.links[k];
                    if (to < 0 || s.available[to] != stamp) continue;
                    if (m.blocked[to] || (m.doorShut[to] && !j.canDoor) || m.edgeBlocked[k]) continue;
                    if (to == j.target) { Record(l, s, cur, to, j.target, stamp); found = true; break; }
                    if (s.closed[to] == stamp) continue;
                    if (s.open[to] != stamp)
                    {
                        open.Add(to);
                        s.open[to] = stamp;
                        Record(l, s, cur, to, j.target, stamp);
                    }
                    if (s.g[cur] + 1 < s.g[to]) Record(l, s, cur, to, j.target, stamp);
                }
                if (open.Count == 0) break;
            }
            if (s.seen[j.target] != stamp) return new int[0];
            int steps = 0;
            for (int n = j.target; n != j.start && s.seen[n] == stamp; n = s.parent[n]) steps++;
            var path = new int[steps];
            for (int n = j.target, i = steps - 1; i >= 0; n = s.parent[n], i--) path[i] = n;
            return path;
        }

        private static void Record(Layout l, Scratch s, int from, int to, int target, int stamp)
        {
            s.parent[to] = from;
            s.seen[to] = stamp;
            s.g[to] = s.g[from] + 1;
            s.f[to] = s.g[to] + Distance(l, to, target);
        }

        // ---- into the game

        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.DoAiTurn))]
        private static class Patch_EnemyPhase
        {
            private static void Prefix(AI_Manager __instance, bool isFast)
            {
                if (!Api.EventBusOn) waiting.Clear();
                else if (!Api.SimultaneousTurns) Prepare(isFast ? AisFast(__instance) : Ais(__instance));
            }
        }

        [HarmonyPatch(typeof(Pathfinder), nameof(Pathfinder.FindPath))]
        private static class Patch_FindPath
        {
            private static bool Prefix(Pathfinder __instance, NavNode from, NavNode to, List<NavNode> availableNodes, AI ____ai, ref List<NavNode> __result)
            {
                if (!Api.EventBusOn || ____ai == null || __instance.ignoreBlocks || !(__instance is WalkingPathfinder)) return true;
                if (!waiting.TryGetValue(____ai, out Job j)) return true;
                waiting.Remove(____ai);
                Layout l = j.map.at;
                if (l.nodes[j.start] != from || l.nodes[j.target] != to) { Misses++; return true; }

                Take(j); // still waiting for a worker: solved here, not waited for
                int[] path = j.path;
                if (path == null)
                {
                    var clock = Stopwatch.StartNew();
                    while ((path = j.path) == null && clock.Elapsed.TotalMilliseconds < SpinMs) Thread.SpinWait(50);
                }
                if (path == null) { Misses++; return true; }

                if (path.Length > 0)
                {
                    NavNode step = l.nodes[path[0]];
                    if (step == null || !availableNodes.Contains(step) || step.CheckBlockedMovement_Enter(from, ____ai)) { Misses++; return true; }
                }
                var result = new List<NavNode>(path.Length);
                foreach (int n in path) result.Add(l.nodes[n]);
                ____ai.DEBUG_SetPath(result);
                __result = result;
                Hits++;
                return false;
            }
        }
    }
}
