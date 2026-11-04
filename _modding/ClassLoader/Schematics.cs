using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using HighlightPlus;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>A recipe with "found": you don't know it until you find its schematic (a laptop, a security terminal).</summary>
    public class FoundSpec
    {
        /// <summary>Floors it can turn up on, as the player counts them (1 = the first floor). Empty = any.</summary>
        public List<int> floors;
        /// <summary>How likely it is to be the one found, against the others that can turn up there.</summary>
        public int weight = 1;
    }

    /// <summary>
    /// Recipes you have to find. A recipe with "found" stays out of the crafting window until you learn it, from the
    /// holo-laptops on office desks or a security terminal's other functions. What you learn is kept as
    /// [Crafting] RecipeLearning says: with the character, with every character, or not at all (everything known).
    /// Recipes without "found" are known as before (a class's own, or ones a mod hands out).
    /// </summary>
    internal static class Schematics
    {
        internal enum Learning { PerCharacter, Shared, AllKnown }

        internal static ConfigEntry<Learning> Mode;
        internal static ConfigEntry<bool> Laptops;
        internal static ConfigEntry<int> LaptopBonusPerSmarts, LaptopsPerFloor;

        private const string KnownTag = "SCCL_SCHEMATIC_", LaptopTag = "SCCL_LAPTOP_";
        private static HashSet<string> shared;
        private static string SharedPath => Path.Combine(Paths.ConfigPath, "Breachwork.schematics.txt");

        internal static void Bind(ConfigFile cfg)
        {
            Mode = cfg.Bind("Crafting", "RecipeLearning", Learning.PerCharacter,
                "Recipes marked \"found\" must be learned from holo-laptops and security terminals. PerCharacter: each character learns its own. " +
                "Shared: once any character learns one, every character knows it. AllKnown: no learning, every recipe is known.");
            Laptops = cfg.Bind("Crafting", "LaptopSchematics", true, "Holo-laptops on office desks can be searched once for a schematic.");
            LaptopsPerFloor = cfg.Bind("Crafting", "LaptopsPerFloor", 3, "How many of a floor's holo-laptops hold schematics (the same ones every time you load). 0 = every laptop.");
            LaptopBonusPerSmarts = cfg.Bind("Crafting", "LaptopBonusPerSmarts", 2, "% chance per point of Smarts that a laptop holds a second schematic (at most 50%).");
        }

        private static string Key(RecipeDef r) => KnownTag + r.qualified.Replace(':', '_').Replace(' ', '_');

        private static HashSet<string> Shared
        {
            get
            {
                if (shared != null) return shared;
                shared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try { if (File.Exists(SharedPath)) foreach (string l in File.ReadAllLines(SharedPath)) if (!string.IsNullOrWhiteSpace(l)) shared.Add(l.Trim()); }
                catch (Exception e) { Plugin.Log.LogWarning("Schematics: could not read the shared list: " + e.Message); }
                return shared;
            }
        }

        internal static bool Known(RecipeDef r)
        {
            if (r.found == null || Mode.Value == Learning.AllKnown) return true;
            if (Player.instance != null && Player.instance.HasTag(Key(r))) return true;
            return Mode.Value == Learning.Shared && Shared.Contains(r.qualified);
        }

        internal static void Learn(RecipeDef r)
        {
            if (Player.instance != null && !Player.instance.HasTag(Key(r))) Player.instance.AddTag(Key(r));
            if (Mode.Value == Learning.Shared && Shared.Add(r.qualified))
                try { File.AppendAllText(SharedPath, r.qualified + Environment.NewLine); }
                catch (Exception e) { Plugin.Log.LogWarning("Schematics: could not save the shared list: " + e.Message); }
        }

        /// <summary>Forgets one (tests).</summary>
        internal static void Forget(RecipeDef r) => Player.instance?.RemoveTag(Key(r));

        /// <summary>The schematics this floor can still teach you, and that your classes could use.</summary>
        internal static List<RecipeDef> Findable(int floor) => Crafting.Recipes
            .Where(r => r.found != null && !Known(r) && Crafting.Unlocked(r)
                        && (r.found.floors == null || r.found.floors.Count == 0 || (floor >= r.found.floors[0] && floor <= r.found.floors[r.found.floors.Count - 1])))
            .ToList();

        /// <summary>Learns a random findable schematic for this floor (by weight); null when there is none left.</summary>
        internal static RecipeDef LearnRandom(string from)
        {
            var pool = Findable(Game.FLOOR + 1);
            if (pool.Count == 0) return null;
            int total = pool.Sum(r => Math.Max(1, r.found.weight)), roll = Random.Range(0, total);
            RecipeDef pick = pool.Last();
            foreach (RecipeDef r in pool) { roll -= Math.Max(1, r.found.weight); if (roll < 0) { pick = r; break; } }
            Learn(pick);
            string name = ItemRegistry.NameOf(pick.outputId);
            string where = string.Join(" or ", pick.stations.Select(s => s.ToLowerInvariant() == "apartment" ? "at the Apartment's Fabricator" : s.ToLowerInvariant() == "field" ? "with a crafting skill" : "at " + s));
            Game.LOG($"<color=#7FD8FF>Schematic learned: {name}</color> <color=#9FB7C9>(from {from}; make it {where})</color>");
            AudioManager.PlayEvent("event:/UI/Shing");
            Plugin.Log.LogInfo($"Schematics: learned {pick.qualified} from {from}");
            return pick;
        }

        internal static int StillToFind => Mode.Value == Learning.AllKnown ? 0 : Crafting.Recipes.Count(r => r.found != null && !Known(r) && Crafting.Unlocked(r));

        // ---- holo-laptops -------------------------------------------------------------------------------------------

        /// <summary>The laptop props: a "Laptop" group (desks, conference tables) or "Office_Laptop_Inactive" (workstations,
        /// shelves), each holding a Laptop_Base with its holo screens.</summary>
        private static bool IsLaptop(Transform t) =>
            (t.name.StartsWith("Laptop") && !t.name.StartsWith("Laptop_")) || t.name.StartsWith("Office_Laptop_Inactive")
                ? t.Find("Laptop_Base") != null : false;

        /// <summary>Shown when its room is: nothing between it and the room's geometry is switched off. (Culling switches
        /// whole rooms off, so the active flag alone says nothing; a decoration the room didn't pick is off below it.)</summary>
        private static bool Picked(Transform t)
        {
            Room room = t.GetComponentInParent<Room>(true);
            Transform stop = room != null && room.roomGeometry != null ? room.roomGeometry.transform : room != null ? room.transform : null;
            for (Transform u = t; u != null && u != stop; u = u.parent)
                if (!u.gameObject.activeSelf) return false;
            return room != null;
        }

        internal static List<Laptop> Wire()
        {
            var made = new List<Laptop>();
            if (!Laptops.Value || Map.instance == null || Game.FLOOR >= 100) return made;
            var all = Map.instance.GetComponentsInChildren<Transform>(true).Where(t => t != null && IsLaptop(t) && Picked(t)).ToList();
            // only a few hold anything, picked from the floor and the character so a reload picks the same ones
            var chosen = all.OrderBy(t => Mathf.RoundToInt(t.position.x * 10f)).ThenBy(t => Mathf.RoundToInt(t.position.z * 10f)).ThenBy(t => Mathf.RoundToInt(t.position.y * 10f)).ToList();
            if (LaptopsPerFloor.Value > 0 && chosen.Count > LaptopsPerFloor.Value)
            {
                uint h = 2166136261;
                foreach (char ch in (Player.instance?.playerName ?? "") + "|" + Game.FLOOR) h = (h ^ ch) * 16777619;
                var rng = new System.Random((int)(h & 0x7FFFFFFF));
                chosen = chosen.OrderBy(_ => rng.Next()).Take(LaptopsPerFloor.Value).ToList();
            }
            foreach (Transform t in chosen)
            {
                if (t.GetComponent<Laptop>() != null) continue;
                // its size from the meshes (a culled renderer has no bounds)
                bool any = false;
                Bounds b = default;
                foreach (MeshFilter mf in t.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    Bounds mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector3 w = mf.transform.TransformPoint(c);
                        if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w);
                    }
                }
                if (!any) continue;
                var box = t.gameObject.AddComponent<BoxCollider>();
                box.center = t.InverseTransformPoint(b.center);
                // at least a hand's width every way in the world, so a flat laptop is still easy to click
                Vector3 world = new Vector3(Mathf.Max(0.35f, b.size.x), Mathf.Max(0.35f, b.size.y), Mathf.Max(0.35f, b.size.z));
                Vector3 sc = t.lossyScale;
                box.size = new Vector3(world.x / Mathf.Max(0.0001f, Mathf.Abs(sc.x)), world.y / Mathf.Max(0.0001f, Mathf.Abs(sc.y)), world.z / Mathf.Max(0.0001f, Mathf.Abs(sc.z)));
                if (t.GetComponent<HighlightEffect>() == null) t.gameObject.AddComponent<HighlightEffect>();
                var tip = t.gameObject.AddComponent<Tooltip_Tiny>();
                tip.title = "Holo-laptop";
                var l = t.gameObject.AddComponent<Laptop>();
                tip.interactiveObject = l;
                l.internalName = "SCCL_Laptop";
                l.interactRange = 1.6f;
                l.room = t.GetComponentInParent<Room>(true);
                made.Add(l);
            }
            if (made.Count > 0) Plugin.Log.LogInfo($"Schematics: {made.Count} of {all.Count} holo-laptops on floor {Game.FLOOR + 1} hold schematics");
            return made;
        }

        private static IEnumerator WireWhenReady()
        {
            for (int i = 0; i < 120 && (Game.Game_isLoading || Player.instance == null || Map.instance == null); i++) yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(0.5f);
            try { Wire(); } catch (Exception e) { Plugin.Log.LogError("Schematics: laptops: " + e); }
        }

        internal class Laptop : InteractiveObject
        {
            protected override void Start() { }

            private string UsedKey => $"{LaptopTag}{Game.FLOOR}_{Mathf.RoundToInt(transform.position.x * 10f)}_{Mathf.RoundToInt(transform.position.z * 10f)}";

            internal bool Used => Player.instance != null && Player.instance.HasTag(UsedKey);

            public override void OnMouseDown()
            {
                base.OnMouseDown();
                if (Game.LOCK || Game.TARGETING || Game.RESOLVING || Choice.Open) return;
                if (RangeCheck()) Player_Control.instance.MouseDownObject(this);
                else Game.LOG("<color=#9FB7C9>Too far away to read the screen.</color>");
            }

            public override void Interact()
            {
                if (Used) { Game.LOG("<color=#9FB7C9>You've already been through this laptop's files.</color>"); return; }
                bool any = Findable(Game.FLOOR + 1).Count > 0;
                string body = Mode.Value == Learning.AllKnown
                    ? "Its holo screen cycles through someone's inbox and a fabrication queue. You already know everything it could teach you."
                    : any ? "Its holo screen cycles through an inbox and a fabrication queue. Someone left schematics open in the queue."
                          : "Its holo screen cycles through an inbox and a fabrication queue. Nothing in it is new to you.";
                var buttons = new List<(string, Action, Color?)>();
                if (any) buttons.Add(("Copy the schematics", Browse, null));
                buttons.Add(("Leave it", null, null));
                Choice.Show("SCCL_Laptop", "Holo-laptop", body, buttons);
            }

            internal void Browse()
            {
                if (Used) return;
                if (LearnRandom("a holo-laptop") == null) return;
                Player.instance.AddTag(UsedKey);
                int bonus = Mathf.Min(50, Player.instance.GetStat(STAT_TYPE.Smarts) * LaptopBonusPerSmarts.Value);
                if (Random.Range(0, 100) < bonus) LearnRandom("the same laptop's archive");
                DisableOutline();
            }
        }

        // ---- patches ------------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Map), nameof(Map.Init))]
        private static class Patch_New
        {
            private static void Postfix() { if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(WireWhenReady()); }
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.DeserializeLevel))]
        private static class Patch_Load
        {
            private static void Postfix() { if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(WireWhenReady()); }
        }
    }
}
