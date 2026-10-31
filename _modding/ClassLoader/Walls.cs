using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Walls as a weapon. A tile's wall sides are the directions with no floor beside it (the room's edge), a blocked
    /// tile, or a wall you raised; doorways don't count.
    ///   "wallSpikes": a spike bursts from every wall side of the target (up to "amount", default all) and runs the
    ///                 nested actions on it once per spike, so an enemy in a corner is hit twice. "model" = the spike.
    ///   "raiseWall":  a wall "amount" tiles wide (default 1) across your line of aim at the aim point (or under the
    ///                 target), on free tiles only, for "turns" of your turns (default 4). It blocks movement like any
    ///                 wall and counts as one for spikes, throws and the wallsNear condition. "model" = one wall piece.
    ///   condition value "wallsNear": how many wall sides touch whoever is checked (0-4).
    /// </summary>
    internal static class Walls
    {
        private static readonly Vector3[] Dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

        private class Raised { public NavNode node; public int turns; public GameObject visual; public string map; }
        private static readonly List<Raised> raised = new List<Raised>();

        private static string MapKey => Map.instance != null ? Map.instance.GetInstanceID().ToString() : "";

        internal static bool IsRaised(NavNode n) => n != null && raised.Any(r => r.node == n);

        /// <summary>The directions from this tile that are walls.</summary>
        internal static List<Vector3> Sides(NavNode n)
        {
            var sides = new List<Vector3>();
            if (n == null) return sides;
            var links = n.GetLinks();
            for (int i = 0; i < Dirs.Length; i++)
            {
                NavNode next = links != null && links.Count > i ? links[i] : null;
                if (next == null || IsRaised(next) || (next.Block_Move && !next.HasDoor())) sides.Add(Dirs[i]);
            }
            return sides;
        }

        internal static int Count(Character c) => c?.GetMover()?.GetCurrentNavNode() == null ? 0 : Sides(c.GetMover().GetCurrentNavNode()).Count;

        // ---- spikes -------------------------------------------------------------------------------------------

        internal static void Spikes(ActionSpec a, ActionContext ctx, Character t)
        {
            NavNode n = t?.GetMover()?.GetCurrentNavNode();
            if (n == null || t.GetIsDead()) return;
            var sides = Sides(n);
            if (a.amount > 0) sides = sides.Take(a.amount).ToList();
            if (sides.Count == 0) return;
            string folder = ClassRegistry.FolderOf(ctx.classId);
            foreach (Vector3 dir in sides)
            {
                if (t.GetIsDead()) break;
                Vector3 face = n.transform.position + dir * 0.5f + Vector3.up * 0.55f;
                if (a.model != null)
                {
                    GameObject go = ModelAssets.Spawn(a.model, folder, face, null);
                    if (go != null)
                    {
                        go.transform.rotation = Quaternion.LookRotation(-dir) * go.transform.rotation; // points out of the wall at them
                        var anim = go.AddComponent<Moves.ShowAnim>();
                        anim.life = a.showTime > 0f ? a.showTime : 0.7f;
                    }
                }
                Actions.SpawnVfx(a.vfx ?? "PA_Shieldbearer_Charge.impactParticle@0.35", face);
                Actions.Run(a.actions, ctx, t);
            }
            Game.LOG($"{ctx.sourceName}: {(sides.Count > 1 ? $"{sides.Count} spikes" : "a spike")} burst from the wall into {t.GetName()}.");
        }

        // ---- raised walls -------------------------------------------------------------------------------------

        internal static void Raise(ActionSpec a, ActionContext ctx, Character t)
        {
            if (Map.instance == null || ctx.caster == null) return;
            Vector3 at = ctx.center ?? (t != null ? t.transform.position : ctx.caster.transform.position);
            NavNode mid = Map.instance.GetNearestNavNodeAt(at, 1f);
            if (mid == null) return;
            // across the line from you to the aim point
            Vector3 aim = mid.transform.position - ctx.caster.transform.position; aim.y = 0f;
            Vector3 across = Mathf.Abs(aim.x) >= Mathf.Abs(aim.z) ? Vector3.forward : Vector3.right;
            int width = Mathf.Max(1, a.amount);
            var tiles = new List<NavNode> { mid };
            NavNode l = mid, r = mid;
            for (int i = 1; tiles.Count < width && i <= width; i++)
            {
                l = l != null ? Diagonal.Link(l, -across) : null;
                if (l != null && tiles.Count < width) tiles.Add(l);
                r = r != null ? Diagonal.Link(r, across) : null;
                if (r != null && tiles.Count < width) tiles.Add(r);
            }
            int turns = a.turns > 0 ? a.turns : 4;
            string folder = ClassRegistry.FolderOf(ctx.classId);
            int made = 0;
            foreach (NavNode n in tiles)
            {
                if (n == null || n.IsOccupied() || n.Block_Move || n.HasDoor() || IsRaised(n) || TileStacks.On(n).Any()) continue;
                n.Block_Move = true;
                GameObject vis = a.model != null ? ModelAssets.Spawn(a.model, folder, n.transform.position, null) : null;
                if (vis != null) vis.transform.rotation = Quaternion.LookRotation(aim.sqrMagnitude > 0.01f ? -aim : Vector3.forward) * vis.transform.rotation;
                Actions.SpawnVfx(a.vfx ?? "PA_Shieldbearer_Charge.impactParticle@0.4", n.transform.position + Vector3.up * 0.3f);
                raised.Add(new Raised { node = n, turns = turns, visual = vis, map = MapKey });
                made++;
            }
            if (made > 0) Game.LOG($"{ctx.sourceName}: a wall {made} wide rises from the deck.");
        }

        /// <summary>Per player turn: raised walls crumble when their time is up (or are forgotten off this map).</summary>
        internal static void Tick()
        {
            if (raised.Count == 0) return;
            string map = MapKey;
            foreach (Raised r in raised.ToList())
            {
                if (r.node == null || r.map != map) { Drop(r, unblock: r.node != null && r.map == map); continue; }
                if (--r.turns <= 0) Drop(r, unblock: true);
            }
        }

        private static void Drop(Raised r, bool unblock)
        {
            if (unblock && r.node != null) r.node.Block_Move = false;
            if (r.visual != null) Object.Destroy(r.visual);
            raised.Remove(r);
        }

        /// <summary>Off a floor or on load: no raised wall survives the map it was raised on.</summary>
        internal static void ClearAll()
        {
            foreach (Raised r in raised.ToList()) Drop(r, unblock: r.node != null && r.map == MapKey);
            raised.Clear();
        }

        internal static int RaisedCount => raised.Count;
    }
}
