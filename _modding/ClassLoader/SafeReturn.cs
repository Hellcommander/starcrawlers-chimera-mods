using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Leaving the apartment drops you back on the exact tile you left, with the floor frozen as it was, and the
    /// portal or door saves first. Portal out of a fight on low health and every reload starts in the apartment with
    /// the same enemies standing next to the door: one save, no way out. On the way back, enemies next to you are now
    /// moved to a free spot elsewhere in the room, the way the game already moves anything standing on a staircase
    /// you arrive on. Bosses stay where they are. [Fixes] SafeApartmentReturn turns it off.
    /// </summary>
    internal static class SafeReturn
    {
        internal static ConfigEntry<bool> Fix;
        /// <summary>Within this of you counts as next to you (diagonals are about 1.41).</summary>
        internal const float Adjacent = 1.6f, MinAway = 3f;

        internal static void Bind(ConfigFile cfg)
        {
            Fix = cfg.Bind("Fixes", "SafeApartmentReturn", true,
                "Coming back from the apartment, enemies standing next to you are moved elsewhere in the room (not bosses), so a save made mid-fight can't trap you between the apartment and a death on the doorstep.");
        }

        /// <summary>Moves hostile non-bosses off the tiles around the player. Returns how many moved.</summary>
        internal static int ClearDoorstep()
        {
            Player p = Player.instance;
            NavNode here = p?.GetMover()?.GetCurrentNavNode();
            if (here == null || AI_Manager.instance == null || Map.instance == null) return 0;
            Room room = here.GetRoom();
            int moved = 0;
            foreach (Character c in AI_Manager.instance.GetEnemiesAt(p.transform.position, Adjacent))
            {
                if (c == null || c == p || c.GetIsDead() || Elites.IsBoss(c)) continue;
                NavNode to = FreeSpot(room, p.transform.position);
                if (to == null) { Plugin.Log.LogInfo($"Safe return: no free spot to move {c.GetName()} to"); continue; }
                c.GetMover().AbortMove();
                c.GetMover().GetCurrentNavNode()?.ClearOccupant();
                c.GetMover().SetCurrentNavNode(to);
                c.transform.position = to.transform.position;
                CullManager.instance?.UpdateCulling(c);
                moved++;
                Plugin.Log.LogInfo($"Safe return: moved {c.GetName()} off your doorstep, {Vector3.Distance(to.transform.position, p.transform.position):0.0} m away");
            }
            if (moved > 0) Game.LOG($"<color=#9FD8FF>{(moved == 1 ? "An enemy waiting at the door was" : $"{moved} enemies waiting at the door were")} pushed back.</color>");
            return moved;
        }

        private static NavNode FreeSpot(Room room, Vector3 from)
        {
            IEnumerable<NavNode> pool = room != null ? room.GetMobSpawnNodesAt(from, 12f) : Enumerable.Empty<NavNode>();
            var spots = pool.Where(n => Open(n, from)).ToList();
            if (spots.Count == 0) spots = Map.instance.GetNavNodesAt(from, 10f, validNodesOnly: true).Where(n => Open(n, from)).ToList();
            return spots.Count == 0 ? null : spots[Random.Range(0, spots.Count)];
        }

        private static bool Open(NavNode n, Vector3 from) =>
            n != null && n.GetOccupant() == null && Vector3.Distance(n.transform.position, from) >= MinAway;

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.ReturnToMap))]
        private static class Patch_Return
        {
            private static void Postfix()
            {
                if (!Fix.Value) return;
                try { ClearDoorstep(); }
                catch (System.Exception e) { Plugin.Log.LogWarning("Safe return: " + e.Message); }
            }
        }
    }
}
