using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// Inside a side area: minimap markers on its way out, the sealed vault, its objectives, and the tracker line
    /// listing breaches you haven't used yet. On Chimera's own floors it does nothing: the breach pad is the only
    /// trace of the side areas there.
    /// </summary>
    internal static class BreachFinder
    {
        internal static ConfigEntry<bool> Markers;

        private static readonly List<GameObject> made = new List<GameObject>();

        internal static void Bind(ConfigFile cfg)
        {
            Markers = cfg.Bind("SideAreas", "MapMarkers", true, "Inside a side area, mark its way out, the sealed vault and its objectives on the minimap.");
        }

        private static Dictionary<string, object> D => SideAreas.Data;

        /// <summary>Floors (0-based) whose breach is still open (a collapse closes one for good).</summary>
        internal static List<int> OpenBreaches()
        {
            var d = D;
            if (d == null) return new List<int>();
            return d.Keys.Where(k => k.StartsWith("breach_")).Select(k => int.TryParse(k.Substring(7), out int f) ? f : -1)
                .Where(f => f >= 0 && d["breach_" + f] is float[])
                .OrderBy(f => f).ToList();
        }

        internal static string OpenBreachesText(bool skipCurrent)
        {
            var floors = OpenBreaches().Where(f => !skipCurrent || f != Game.FLOOR || SideAreas.InSide).ToList();
            if (floors.Count == 0) return null;
            return "Open breaches: " + string.Join(", ", floors.Select(f => $"floor {f + 1} ({SideKinds.Tinted(SideKinds.KindOf(f))}{(SideAreas.HasKept(f) ? ", been in" : "")})"));
        }

        /// <summary>Inside a side area: the floors whose breach you haven't used yet. Nothing on the real floors.</summary>
        internal static string TrackerLine()
        {
            if (!SideAreas.InSide || D == null) return null;
            string others = OpenBreachesText(skipCurrent: true);
            return others == null ? null : $"<size=85%><color=#9FB7C9>{others}</color></size>";
        }

        // ---- markers ------------------------------------------------------------------------------------------

        internal static void Refresh()
        {
            foreach (GameObject g in made) if (g != null) Object.Destroy(g);
            made.Clear();
            if (!Markers.Value || !SideAreas.InSide || Menu_MiniMap.instance == null || Map.instance == null) return;
            foreach (SideAreas.Breach b in SideAreas.Pads.Where(x => x != null)) // the green way out and rifts deeper
                Mark(b.isReturn ? "star" : "quest", b.transform.position);
            Room vault = Echoes.VaultRoom;
            if (vault != null && !Echoes.VaultOpen) Mark("star", vault.transform.position);
            foreach (Vector3 p in SideKinds.MarkerSpots()) Mark("quest", p);
            foreach (var (pos, up) in SideLayers.LiftSpots()) Mark(up ? "quest" : "star", pos); // the way on, and the way back down
        }

        private static void Mark(string id, Vector3 pos)
        {
            try
            {
                MiniMap_Marker m = Menu_MiniMap.instance.CreateMarkerAt(id, pos);
                if (m == null) return;
                m.transform.SetParent(Map.instance.transform, worldPositionStays: true); // goes when the map does
                made.Add(m.gameObject);
            }
            catch (Exception e) { Plugin.Log.LogError("Breach markers: " + e.Message); }
        }

        private static IEnumerator AfterArrival()
        {
            yield return new WaitForSeconds(0.6f);
            if (Player.instance == null || Map.instance == null || Game.Game_isLoading) yield break;
            try { Refresh(); }
            catch (Exception e) { Plugin.Log.LogError("Breach markers: " + e); }
        }

        [HarmonyPatch(typeof(Menu_MiniMap), nameof(Menu_MiniMap.Init))]
        private static class Patch_MiniMapInit
        {
            private static void Postfix()
            {
                if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(AfterArrival());
            }
        }
    }
}
