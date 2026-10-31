using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// The floor plan of a generated place, on a grid of 1 m tiles: rooms are sets of tiles, kept at least one solid
    /// tile apart, and every way between two rooms is a doorway tile in that gap with a room on each side of it. That
    /// is the shape the game's door tiles expect. Rooms are grown one at a time off rooms already placed, by the
    /// place's style. The same seed always gives the same plan, so a saved place can be rebuilt.
    /// </summary>
    internal class PlaceLayout
    {
        internal const int Solid = -1, Doorway = -2, N = 150;

        internal class Shape
        {
            public List<Vector2Int> cells = new List<Vector2Int>();
            public Dictionary<Vector2Int, char> marks = new Dictionary<Vector2Int, char>();
            /// <summary>Template doorways: a tile outside the shape and the way into it. Null = any edge tile.</summary>
            public List<(Vector2Int spot, Vector2Int inward)> spots;
            /// <summary>Tiles a corridor or tunnel may be entered by (its first tile). Null = any edge tile.</summary>
            public List<Vector2Int> entries;
            public PlaceTemplateSpec template;
            public Vector2Int axis;
        }

        internal class LRoom
        {
            public int id, depth;
            public string role;
            public bool hallway;
            public List<Vector2Int> cells;
            public HashSet<Vector2Int> set;
            public Dictionary<Vector2Int, char> marks;
            public List<(Vector2Int spot, Vector2Int inward)> spots;
            public PlaceTemplateSpec template;
            /// <summary>A corridor's direction, its last tile (tunnels are entered at their first).</summary>
            public Vector2Int axis, tip;
        }

        /// <summary>A doorway between rooms a and b; dir points from a toward b. State: door, arch (no door), open (jammed open).</summary>
        internal class LDoor { public Vector2Int cell, dir; public int a, b; public string state = "door"; }

        internal readonly int[,] grid = new int[N, N];
        internal readonly List<LRoom> rooms = new List<LRoom>();
        internal readonly List<LDoor> doors = new List<LDoor>();
        internal int arrival = -1;
        internal Vector2Int exitCell, exitWall;
        internal readonly System.Random rng;
        private readonly PlaceSpec p;
        private readonly int want;

        private static readonly Vector2Int[] Dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        private PlaceLayout(PlaceSpec spec, int seed, int roomsWanted)
        {
            p = spec;
            rng = new System.Random(seed);
            want = roomsWanted;
            for (int x = 0; x < N; x++) for (int y = 0; y < N; y++) grid[x, y] = Solid;
        }

        /// <summary>A plan for this place; retries a few derived seeds if one comes out too small.</summary>
        internal static PlaceLayout Make(PlaceSpec spec, int seed, int roomsWanted)
        {
            PlaceLayout best = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var l = new PlaceLayout(spec, seed + attempt * 7919, roomsWanted);
                l.Grow();
                if (best == null || l.Rooms > best.Rooms) best = l;
                if (l.Rooms >= Math.Max(3, roomsWanted * 2 / 3) && l.arrival >= 0) break;
            }
            return best;
        }

        internal int Rooms => rooms.Count(r => !r.hallway);

        // ---- small helpers ----------------------------------------------------------------------------------

        private int R(int min, int max) => rng.Next(min, Math.Max(min, max) + 1);
        private int R(int[] mm, int fallback) => mm != null && mm.Length >= 2 ? R(mm[0], mm[1]) : mm != null && mm.Length == 1 ? mm[0] : fallback;
        private bool Roll(float chance) => rng.NextDouble() < chance;
        private T Pick<T>(IList<T> list) => list[rng.Next(list.Count)];
        private void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        }

        internal static bool InB(Vector2Int v, int margin = 0) => v.x >= margin && v.y >= margin && v.x < N - margin && v.y < N - margin;
        internal int At(Vector2Int v) => InB(v) ? grid[v.x, v.y] : Solid;
        private static Vector2Int Perp(Vector2Int d) => new Vector2Int(d.y, d.x);
        private static Vector2Int Rot(Vector2Int v, int k)
        {
            for (int i = 0; i < (k & 3); i++) v = new Vector2Int(-v.y, v.x);
            return v;
        }

        // ---- shapes -----------------------------------------------------------------------------------------

        private static Shape Rect(int w, int h)
        {
            var s = new Shape();
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) s.cells.Add(new Vector2Int(x, y));
            return s;
        }

        /// <summary>A straight corridor running along dir, entered at its near end.</summary>
        private static Shape Corridor(int len, int width, Vector2Int dir)
        {
            var s = new Shape { axis = dir, entries = new List<Vector2Int>() };
            Vector2Int side = Perp(dir);
            for (int i = 0; i < len; i++)
                for (int w = 0; w < width; w++) s.cells.Add(dir * i + side * w);
            for (int w = 0; w < width; w++) s.entries.Add(side * w);
            return s;
        }

        /// <summary>A winding tunnel one tile wide, setting off along dir.</summary>
        private Shape Tunnel(int len, Vector2Int dir)
        {
            var s = new Shape { axis = dir, entries = new List<Vector2Int> { Vector2Int.zero } };
            var at = Vector2Int.zero;
            Vector2Int d = dir;
            s.cells.Add(at);
            for (int i = 1; i < len; i++)
            {
                if (i > 1 && Roll(0.3f)) d = Roll(0.5f) ? Perp(d) : -Perp(d);
                if (d == -dir) d = dir; // never doubles back toward where it came in
                Vector2Int next = at + d;
                // stays one tile wide: the new tile may only touch the tile it grows from
                if (s.cells.Any(c => c != at && Mathf.Abs(c.x - next.x) <= 1 && Mathf.Abs(c.y - next.y) <= 1)) { d = dir; next = at + d; }
                if (s.cells.Contains(next)) break;
                at = next;
                s.cells.Add(at);
            }
            s.axis = d;
            return s;
        }

        /// <summary>A cave blob: noise smoothed by a few rounds of cellular automaton, largest piece kept.</summary>
        private Shape Blob(int w, int h)
        {
            var m = new bool[w, h];
            for (int x = 1; x < w - 1; x++) for (int y = 1; y < h - 1; y++) m[x, y] = Roll(0.62f);
            for (int it = 0; it < 4; it++)
            {
                var n = new bool[w, h];
                for (int x = 1; x < w - 1; x++)
                    for (int y = 1; y < h - 1; y++)
                    {
                        int c = 0;
                        for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) if ((dx != 0 || dy != 0) && m[x + dx, y + dy]) c++;
                        n[x, y] = c >= 5 || (c >= 4 && m[x, y]);
                    }
                m = n;
            }
            var best = new List<Vector2Int>();
            var seen = new bool[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    if (!m[x, y] || seen[x, y]) continue;
                    var part = new List<Vector2Int>();
                    var q = new Queue<Vector2Int>();
                    q.Enqueue(new Vector2Int(x, y));
                    seen[x, y] = true;
                    while (q.Count > 0)
                    {
                        Vector2Int c = q.Dequeue();
                        part.Add(c);
                        foreach (Vector2Int d in Dirs)
                        {
                            Vector2Int nb = c + d;
                            if (nb.x < 0 || nb.y < 0 || nb.x >= w || nb.y >= h || seen[nb.x, nb.y] || !m[nb.x, nb.y]) continue;
                            seen[nb.x, nb.y] = true;
                            q.Enqueue(nb);
                        }
                    }
                    if (part.Count > best.Count) best = part;
                }
            if (best.Count < w * h / 3)
            {
                best.Clear();
                float rx = (w - 2) / 2f, ry = (h - 2) / 2f;
                for (int x = 0; x < w; x++)
                    for (int y = 0; y < h; y++)
                    {
                        float fx = (x - (w - 1) / 2f) / rx, fy = (y - (h - 1) / 2f) / ry;
                        if (fx * fx + fy * fy <= 1f) best.Add(new Vector2Int(x, y));
                    }
            }
            return new Shape { cells = best };
        }

        /// <summary>A template's map as a shape (row 0 at the top), turned k quarter turns.</summary>
        internal static Shape FromTemplate(PlaceTemplateSpec t, int k)
        {
            var s = new Shape { template = t, spots = new List<(Vector2Int, Vector2Int)>() };
            var floor = new HashSet<Vector2Int>();
            var doorSpots = new List<Vector2Int>();
            for (int row = 0; row < t.map.Count; row++)
                for (int col = 0; col < t.map[row].Length; col++)
                {
                    char ch = t.map[row][col];
                    var v = new Vector2Int(col, -row);
                    if (ch == '#' || ch == ' ') continue;
                    if (ch == 'D') { doorSpots.Add(v); continue; }
                    floor.Add(v);
                    if (ch != '.') s.marks[v] = ch;
                }
            foreach (Vector2Int v in floor) s.cells.Add(Rot(v, k));
            s.marks = s.marks.ToDictionary(kv => Rot(kv.Key, k), kv => kv.Value);
            foreach (Vector2Int d in doorSpots)
            {
                Vector2Int inward = Dirs.FirstOrDefault(dd => floor.Contains(d + dd));
                if (inward == Vector2Int.zero) continue;
                s.spots.Add((Rot(d, k), Rot(inward, k)));
            }
            if (s.spots.Count == 0) s.spots = null;
            return s;
        }

        // ---- placing ------------------------------------------------------------------------------------------

        private LRoom Commit(Shape s, Vector2Int off, string role, bool hallway)
        {
            var r = new LRoom
            {
                id = rooms.Count, role = role, hallway = hallway, template = s.template, axis = s.axis,
                cells = s.cells.Select(c => c + off).ToList(),
                marks = s.marks.ToDictionary(kv => kv.Key + off, kv => kv.Value),
                spots = s.spots?.Select(x => (x.spot + off, x.inward)).ToList(),
            };
            r.set = new HashSet<Vector2Int>(r.cells);
            r.tip = r.cells[r.cells.Count - 1];
            foreach (Vector2Int c in r.cells) grid[c.x, c.y] = r.id;
            rooms.Add(r);
            return r;
        }

        private bool Fits(Shape s, Vector2Int off, Vector2Int door)
        {
            foreach (Vector2Int c in s.cells)
            {
                Vector2Int w = c + off;
                if (!InB(w, 3) || grid[w.x, w.y] != Solid || w == door) return false;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (grid[w.x + dx, w.y + dy] != Solid) return false;
            }
            return true;
        }

        /// <summary>The first room, centred on the grid.</summary>
        private LRoom Place(Shape s, string role, bool hallway)
        {
            int minX = s.cells.Min(c => c.x), maxX = s.cells.Max(c => c.x), minY = s.cells.Min(c => c.y), maxY = s.cells.Max(c => c.y);
            var off = new Vector2Int(N / 2 - (minX + maxX) / 2, N / 2 - (minY + maxY) / 2);
            return Commit(s, off, role, hallway);
        }

        /// <summary>Where a doorway may go from this room: (inside tile, outward direction).</summary>
        private List<(Vector2Int c, Vector2Int dir)> Edges(LRoom from, Func<Vector2Int, Vector2Int, bool> ok)
        {
            var list = new List<(Vector2Int, Vector2Int)>();
            if (from.spots != null)
            {
                foreach (var (spot, inward) in from.spots)
                    if (At(spot) == Solid && (ok == null || ok(spot + inward, -inward))) list.Add((spot + inward, -inward));
                return list;
            }
            foreach (Vector2Int c in from.cells)
                foreach (Vector2Int d in Dirs)
                    if (!from.set.Contains(c + d) && (ok == null || ok(c, d))) list.Add((c, d));
            return list;
        }

        /// <summary>Grows a new room off "from" through a new doorway. make(dir) builds the shape for that direction.</summary>
        private LRoom Attach(LRoom from, Func<Vector2Int, Shape> make, string role, bool hallway, Func<Vector2Int, Vector2Int, bool> ok = null, int tries = 24)
        {
            var edges = Edges(from, ok);
            Shuffle(edges);
            foreach (var (c, dir) in edges.Take(tries))
            {
                Vector2Int d = c + dir;
                if (!InB(d, 3) || At(d) != Solid || At(d + Perp(dir)) != Solid || At(d - Perp(dir)) != Solid || At(d + dir) != Solid) continue;
                if (Dirs.Any(x => At(d + x) == Doorway)) continue;
                Shape s = make(dir);
                if (s == null || s.cells.Count == 0) continue;
                var offs = new List<Vector2Int>();
                if (s.spots != null)
                {
                    foreach (var (spot, inward) in s.spots) if (inward == dir) offs.Add(d - spot);
                }
                else
                {
                    var entries = s.entries ?? s.cells.Where(e => !s.cells.Contains(e - dir)).ToList();
                    foreach (Vector2Int e in entries.Where(e => !s.cells.Contains(e - dir))) offs.Add(d + dir - e);
                }
                Shuffle(offs);
                foreach (Vector2Int off in offs.Take(6))
                {
                    if (!Fits(s, off, d)) continue;
                    if (s.spots != null) s.spots = s.spots.Where(x => x.spot + off != d).ToList();
                    LRoom made = Commit(s, off, role, hallway);
                    grid[d.x, d.y] = Doorway;
                    doors.Add(new LDoor { cell = d, dir = dir, a = from.id, b = made.id });
                    return made;
                }
            }
            return null;
        }

        private PlaceTemplateSpec[] Templates(string at) =>
            (p.templates ?? new List<PlaceTemplateSpec>()).Where(t => t != null && t.map != null && t.map.Count > 0 && string.Equals(t.at ?? "any", at, StringComparison.OrdinalIgnoreCase)).ToArray();

        /// <summary>One of the place's templates for this spot, if it has any (turned to fit).</summary>
        private LRoom AttachTemplate(LRoom from, string at, Func<Vector2Int, Vector2Int, bool> ok = null)
        {
            var ts = Templates(at).Where(t => Roll(t.chance)).ToList();
            if (ts.Count == 0) return null;
            PlaceTemplateSpec t = Pick(ts);
            return Attach(from, dir => FromTemplate(t, rng.Next(4)), t.role ?? t.id ?? at, false, ok, 40);
        }

        // ---- styles -------------------------------------------------------------------------------------------

        private void Grow()
        {
            switch ((p.style ?? "spine").ToLowerInvariant())
            {
                case "hub": Hub(); break;
                case "grid": Grid(); break;
                case "caves": Caves(); break;
                default: Spine(); break;
            }
            ExtraTemplates();
            Loops();
            Depths();
            Exit();
            DoorStates();
        }

        private LRoom Room(LRoom from, string role, Func<Vector2Int, Vector2Int, bool> ok = null) =>
            Attach(from, _ => Rect(R(p.roomSize, 4), R(p.roomSize, 4)), role, false, ok);

        private void Spine()
        {
            LRoom spine = Place(Corridor(R(p.spine, 22), Math.Max(1, p.spineWidth), new Vector2Int(1, 0)), "spine", true);
            int maxX = spine.cells.Max(c => c.x), minX = spine.cells.Min(c => c.x);
            bool Side(Vector2Int c, Vector2Int d) => d.y != 0;
            LRoom bow = AttachTemplate(spine, "bow", (c, d) => d.x > 0 && c.x == maxX) ?? Attach(spine, _ => Rect(R(5, 7), R(5, 7)), "bridge", false, (c, d) => d.x > 0 && c.x == maxX);
            LRoom stern = AttachTemplate(spine, "stern", (c, d) => d.x < 0 && c.x == minX) ?? Attach(spine, _ => Rect(R(5, 6), R(6, 8)), "engine", false, (c, d) => d.x < 0 && c.x == minX);
            LRoom start = AttachTemplate(spine, "start", Side) ?? Attach(spine, _ => Rect(3, 3), "airlock", false, Side);
            if (start != null) arrival = start.id;
            var corridors = new List<LRoom> { spine };
            for (int tries = 0; Rooms < want && tries < want * 30; tries++)
            {
                double r = rng.NextDouble();
                if (r < 0.55) Room(Pick(corridors), "compartment", Side);
                else if (r < 0.7)
                {
                    LRoom side = Attach(spine, d => Corridor(R(p.corridor, 4), 1, d), "passage", true, Side);
                    if (side != null) corridors.Add(side);
                }
                else
                {
                    var rs = rooms.Where(x => !x.hallway && x.template == null && x.id != arrival).ToList();
                    if (rs.Count > 0) Attach(Pick(rs), _ => Rect(R(3, 4), R(3, 4)), "hold", false);
                }
            }
        }

        private void Hub()
        {
            LRoom hub = Place(Rect(R(7, 9), R(7, 9)), "hub", false);
            LRoom start = AttachTemplate(hub, "start") ?? Attach(hub, _ => Rect(3, 4), "airlock", false);
            if (start != null) arrival = start.id;
            var halls = new List<LRoom>();
            for (int i = 0; i < 4; i++)
            {
                LRoom c = Attach(hub, d => Corridor(R(p.corridor, 5), 1, d), "corridor", true);
                if (c != null) halls.Add(c);
            }
            for (int tries = 0; Rooms < want && tries < want * 30; tries++)
            {
                double r = rng.NextDouble();
                if (halls.Count > 0 && r < 0.6)
                {
                    LRoom h = Pick(halls);
                    // off the far end, or off a side
                    LRoom made = Roll(0.5f) ? Room(h, "wing", (c, d) => c == h.tip && d == h.axis) : Room(h, "wing", (c, d) => d != h.axis && d != -h.axis);
                    if (made != null && Roll(0.35f))
                    {
                        LRoom c2 = Attach(made, d => Corridor(R(p.corridor, 4), 1, d), "corridor", true);
                        if (c2 != null) halls.Add(c2);
                    }
                }
                else
                {
                    var rs = rooms.Where(x => !x.hallway && x.template == null && x.id != arrival).ToList();
                    if (rs.Count == 0) continue;
                    if (Roll(0.5f)) { LRoom c = Attach(Pick(rs), d => Corridor(R(p.corridor, 4), 1, d), "corridor", true); if (c != null) halls.Add(c); }
                    else Room(Pick(rs), "annex");
                }
            }
        }

        private void Grid()
        {
            int s = R(p.roomSize, 5), pitch = s + 1;
            LRoom main = Place(Corridor(R(p.spine, 24), 1, new Vector2Int(1, 0)), "corridor", true);
            int x0 = main.cells.Min(c => c.x), maxX = main.cells.Max(c => c.x);
            var halls = new List<LRoom> { main };
            LRoom cross = Attach(main, d => Corridor(R(p.corridor, 7) + 2, 1, d), "corridor", true, (c, d) => d.y != 0 && Mathf.Abs(c.x - (x0 + maxX) / 2) <= 1);
            if (cross != null) halls.Add(cross);
            LRoom start = AttachTemplate(main, "start", (c, d) => d.x < 0) ?? Attach(main, _ => Rect(3, 3), "airlock", false, (c, d) => d.x < 0);
            if (start != null) arrival = start.id;
            var labs = new List<LRoom>();
            for (int tries = 0; Rooms < want && tries < want * 30; tries++)
            {
                if (labs.Count > 0 && Roll(0.3f))
                {
                    Attach(Pick(labs), _ => Roll(0.5f) ? Rect(2, 2) : Rect(2, 3), "cell", false);
                    continue;
                }
                LRoom h = Pick(halls);
                Vector2Int ax = h.axis;
                // a steady rhythm of equal labs along the corridor
                LRoom lab = Attach(h, _ => Rect(s, s), "lab", false,
                    (c, d) => d != ax && d != -ax && ((ax.x != 0 ? c.x - x0 : c.y) % pitch + pitch) % pitch == 1);
                if (lab != null) labs.Add(lab);
            }
        }

        private void Caves()
        {
            LRoom first = Place(Blob(R(9, 12), R(9, 12)), "cavern", false);
            LRoom start = AttachTemplate(first, "start") ?? Attach(first, d => Tunnel(3, d), "landing", false);
            if (start != null) arrival = start.id;
            var tunnels = new List<LRoom>();
            for (int tries = 0; Rooms < want && tries < want * 30; tries++)
            {
                var caverns = rooms.Where(r => !r.hallway && r.template == null && r.id != arrival).ToList();
                if (tunnels.Count > 0 && Roll(0.55f))
                {
                    LRoom t = Pick(tunnels);
                    int big = R(p.roomSize, 6) * 2;
                    Attach(t, _ => Blob(Mathf.Clamp(big, 6, 14), Mathf.Clamp(big + R(-2, 2), 6, 14)), "cavern", false, (c, d) => c == t.tip && d == t.axis, 8);
                    if (Roll(0.2f)) { LRoom t2 = Attach(t, d => Tunnel(R(p.corridor, 5), d), "tunnel", true, (c, d) => c == t.tip && d == t.axis, 8); if (t2 != null) tunnels.Add(t2); }
                }
                else if (caverns.Count > 0)
                {
                    LRoom t = Attach(Pick(caverns), d => Tunnel(R(p.corridor, 5) + 2, d), "tunnel", true);
                    if (t != null) tunnels.Add(t);
                }
            }
        }

        /// <summary>Templates meant for anywhere or deep inside, each attached once per count.</summary>
        private void ExtraTemplates()
        {
            Depths();
            foreach (PlaceTemplateSpec t in Templates("far").Concat(Templates("any")))
                for (int i = 0; i < Math.Max(1, t.count); i++)
                {
                    if (!Roll(t.chance)) continue;
                    IEnumerable<LRoom> from = rooms.Where(r => r.id != arrival && (r.template == null || r.spots?.Count > 0));
                    from = string.Equals(t.at, "far", StringComparison.OrdinalIgnoreCase) ? from.OrderByDescending(r => r.depth) : from.OrderBy(_ => rng.Next());
                    foreach (LRoom r in from.ToList())
                        if (Attach(r, dir => FromTemplate(t, rng.Next(4)), t.role ?? t.id, false, null, 40) != null) break;
                }
        }

        /// <summary>A second way through where two rooms sit one wall tile apart.</summary>
        private void Loops()
        {
            if (p.loops <= 0) return;
            var linked = new HashSet<(int, int)>(doors.Select(d => (Math.Min(d.a, d.b), Math.Max(d.a, d.b))));
            for (int x = 3; x < N - 3; x++)
                for (int y = 3; y < N - 3; y++)
                {
                    if (grid[x, y] != Solid) continue;
                    var v = new Vector2Int(x, y);
                    foreach (Vector2Int d in new[] { new Vector2Int(1, 0), new Vector2Int(0, 1) })
                    {
                        int a = At(v - d), b = At(v + d);
                        if (a < 0 || b < 0 || a == b || At(v + Perp(d)) != Solid || At(v - Perp(d)) != Solid) continue;
                        if (Dirs.Any(dd => At(v + dd) == Doorway)) continue;
                        var key = (Math.Min(a, b), Math.Max(a, b));
                        if (linked.Contains(key) || a == arrival || b == arrival || !Roll(p.loops)) continue;
                        linked.Add(key);
                        grid[x, y] = Doorway;
                        doors.Add(new LDoor { cell = v, dir = d, a = a, b = b });
                    }
                }
        }

        /// <summary>How many doorways each room is from the arrival room.</summary>
        private void Depths()
        {
            foreach (LRoom r in rooms) r.depth = int.MaxValue;
            int start = arrival >= 0 ? arrival : 0;
            if (rooms.Count == 0) return;
            rooms[start].depth = 0;
            var q = new Queue<int>();
            q.Enqueue(start);
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                foreach (LDoor d in doors.Where(d => d.a == i || d.b == i))
                {
                    int o = d.a == i ? d.b : d.a;
                    if (rooms[o].depth != int.MaxValue) continue;
                    rooms[o].depth = rooms[i].depth + 1;
                    q.Enqueue(o);
                }
            }
        }

        /// <summary>Tiles next to a doorway (kept clear of props and spawns).</summary>
        internal bool NextToDoorway(Vector2Int c) => Dirs.Any(d => At(c + d) == Doorway);

        /// <summary>Outward directions from this tile that face solid wall.</summary>
        internal IEnumerable<Vector2Int> WallSides(Vector2Int c) => Dirs.Where(d => At(c + d) == Solid);

        private void Exit()
        {
            if (arrival < 0) arrival = 0;
            LRoom r = rooms[arrival];
            var marked = r.marks.Where(kv => kv.Value == 'E').Select(kv => kv.Key).ToList();
            var cands = marked.Count > 0 ? marked : r.cells.Where(c => WallSides(c).Count() == 1 && !NextToDoorway(c)).ToList();
            if (cands.Count == 0) cands = r.cells.Where(c => WallSides(c).Any()).ToList();
            if (cands.Count == 0) cands = r.cells;
            // farthest from the room's doorways, so you arrive facing into the room
            var ways = doors.Where(d => d.a == arrival || d.b == arrival).Select(d => d.cell).ToList();
            exitCell = cands.OrderByDescending(c => ways.Count == 0 ? 0 : ways.Min(w => Mathf.Abs(w.x - c.x) + Mathf.Abs(w.y - c.y))).First();
            exitWall = WallSides(exitCell).DefaultIfEmpty(new Vector2Int(0, -1)).First();
        }

        private void DoorStates()
        {
            foreach (LDoor d in doors)
            {
                if (Roll(p.archways)) d.state = "arch";
                else if (Roll(p.jammedOpen)) d.state = "open";
            }
        }

        internal Vector3 World(Vector2Int c) => new Vector3(c.x - N / 2, 0f, c.y - N / 2);

        internal string Describe() =>
            $"{rooms.Count} rooms ({Rooms} not corridors: {string.Join(", ", rooms.GroupBy(r => r.role).Select(g => g.Key + " x" + g.Count()))}), {doors.Count} doorways " +
            $"({doors.Count(d => d.state == "arch")} arches, {doors.Count(d => d.state == "open")} jammed open), {rooms.Sum(r => r.cells.Count)} tiles, arrival {arrival}, deepest {rooms.Where(r => r.depth != int.MaxValue).Max(r => r.depth)}";
    }
}
