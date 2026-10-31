using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SCClassLoader
{
    // ---- anomalies -------------------------------------------------------------------------------------------

    /// <summary>One anomaly from a mod's "sideAreas": { "anomalies": [...] }. Every part is optional and they combine.</summary>
    public class AnomalySpec
    {
        public string id, name, text;
        public bool enabled = true;
        /// <summary>Hard anomalies raise the vault grade by one; soft ones are shown in gold.</summary>
        public bool hard = true;
        public int weight = 10;
        /// <summary>Rolled on its own, on top of the normal ones (the Reliquary: weight 0, extraChance 0.1).</summary>
        public float extraChance;
        public int minFloor, maxFloor = 99;
        /// <summary>Only in these side area kinds (empty = any kind that doesn't exclude it).</summary>
        public List<string> kinds;
        /// <summary>Never rolled together with these anomalies.</summary>
        public List<string> notWith;
        /// <summary>Extra vault grades, on top of the one every hard anomaly gives.</summary>
        public int vaultGrade;

        /// <summary>Effects ("Name" = this mod's sideAreas effects or the built-in ones, or "Class@Effect").</summary>
        public string playerEffect, enemyEffect;
        public float enemyChance = 1f;
        public int enemyStacks = 1;
        /// <summary>Fraction of the echo's own enemies removed when it is built (0.5 = half are gone).</summary>
        public float thin;

        public LightsSpec lights;
        public HazardRoomsSpec hazards;
        public DoorsSpec doors;
        public SpawnSpec spawn;
        public CacheSpec caches;
        public TimerSpec timer;
        public PulseSpec pulse;
        public HunterSpec hunter;
        public AlarmSpec alarm;

        [JsonIgnore] public ModPack pack;
    }

    public class LightsSpec { public float off = 0.75f, ambient = 0.35f; }

    /// <summary>A hazard laid in a few rooms for the whole visit.</summary>
    public class HazardRoomsSpec
    {
        public string id = "HAZARD_Gas";
        public int[] rooms = { 3, 4 };
        /// <summary>Also cover the tiles around the centre tile.</summary>
        public bool spread = true;
        public int turns = 999;
    }

    public class DoorsSpec { public float @lock = 0.5f; public int pick = -20, bash = -15; }

    /// <summary>Extra enemies placed around the echo.</summary>
    public class SpawnSpec
    {
        /// <summary>Enemy ids per depth band: pools[floor / poolStep] (the last band for anything deeper).</summary>
        public List<List<string>> pools;
        public List<string> ids;
        public int poolStep = 2;
        public int count = 5, perFloor = 1, maxExtra = 6;
        public string name, effect;
        public int effectStacks = 1;
        /// <summary>Health multiplier for each spawned enemy.</summary>
        public float health = 1f;
        /// <summary>Copy the echo's own enemies instead of a list (turrets of the map's kind, guards of its floor).</summary>
        public bool fromEcho;
        /// <summary>A defeated spawn drops this ("Rare", "Epic", ...).</summary>
        public string drop;
    }

    public class CacheSpec { public int count = 2, items = 2; public string rarity = "Rare"; public string type; }

    /// <summary>A countdown: warnings, then the echo throws you out, hurt.</summary>
    public class TimerSpec
    {
        public int turns = 50;
        public List<int> warn = new List<int> { 20, 10, 5 };
        /// <summary>Fraction of your max health lost when it runs out.</summary>
        public float damage = 0.25f;
        public string warnText = "The echo shudders. {0} turns before it comes apart.";
        public string endText = "The echo comes apart around you and throws you back through the breach.";
        public string label = "collapse in";
    }

    /// <summary>Something that hits the tiles around you every so often.</summary>
    public class PulseSpec
    {
        public float chance = 0.25f;
        public string hazard = "HAZARD_Shock";
        public int turns = 2, tiles = 2;
        /// <summary>Chance that your own tile is hit too.</summary>
        public float onYou = 0.3f;
        public string text = "Static arcs across the floor.";
        public string color = "#7FE0FF";
    }

    /// <summary>After a while, one much tougher enemy comes for you.</summary>
    public class HunterSpec
    {
        public int after = 15;
        public float health = 3f;
        public string name = "Stalker", effect, text = "Something has picked up your trail.", drop = "Epic";
        public int effectStacks = 1;
    }

    /// <summary>Reinforcements arrive in waves at the far side of the echo and come for you.</summary>
    public class AlarmSpec
    {
        public int start = 12, every = 10, waves = 4, size = 2;
        /// <summary>Waves only start once something has spotted you.</summary>
        public bool whenSpotted = true;
        public string text = "Reinforcements are coming through.", name = "Responder", effect;
    }

    // ---- kinds -----------------------------------------------------------------------------------------------

    /// <summary>One kind of side area from "sideAreas": { "kinds": [...] }. "echo" is the classic one.</summary>
    public class KindSpec
    {
        public string id, name, text, arrival;
        public bool enabled = true;
        public int weight = 10, minFloor, maxFloor = 99;
        /// <summary>Breach pad colour (#RRGGBB).</summary>
        public string color = "#D970FF";
        /// <summary>near (next floor down, else neighbours), deeper (one or two down), shallower, any, same.</summary>
        public string theme = "near";
        /// <summary>Weights for rolling 0, 1, 2, 3... anomalies.</summary>
        public List<int> anomalyOdds = new List<int> { 0, 55, 35, 10 };
        public List<string> force, exclude, only;
        public bool vault = true;
        public int vaultGrade;
        /// <summary>Fraction of the echo's own enemies removed.</summary>
        public float thin;
        /// <summary>Remove every enemy of these kinds (Humanoid, Mechanical, Monster, Supernatural).</summary>
        public List<string> strip;
        /// <summary>Bonus cache items (-1 = the config's BonusCacheItems).</summary>
        public int cache = -1;
        public string cacheRarity;
        /// <summary>Faction jobs can be taken into this kind.</summary>
        public bool jobs = true;
        /// <summary>The kind's own goal; see SideKinds.</summary>
        public ObjectiveSpec objective;
        /// <summary>Enemy id of a passive vendor standing near the arrival point, and how good its stock is.</summary>
        public string vendor;
        public int vendorItems = 6;
        public string vendorRarity = "Rare";
        /// <summary>Health of the echo's enemies is multiplied by this.</summary>
        public float toughness = 1f;
        /// <summary>A second breach somewhere inside leads one layer deeper (at most this many layers).</summary>
        public int deeper;
        /// <summary>Stacked layers joined by lifts both ways, each kept as you left it; see SideLayers.</summary>
        public TowerSpec tower;
        public int scoreBonus;
        /// <summary>Roster ids this kind's population is drawn from (one is picked per breach; empty = any roster).</summary>
        public List<string> rosters;
        /// <summary>Fraction of the echo's own enemies swapped for the roster's (0 = keep them all).</summary>
        public float replace = 0.35f;
        /// <summary>Chance per enemy of an elite affix (-1 = the config's EliteChance).</summary>
        public float eliteChance = -1f;
        /// <summary>Chance of a rival crawler in here (-1 = the "nemesis" block's chance).</summary>
        public float nemesisChance = -1f;
        /// <summary>The generated place this kind leads into (see PlaceSpec), or one of several; none = an echo of a real floor.</summary>
        public string place;
        public List<string> places;
        /// <summary>Rooms to grow in its place: [min, max] (default: the place's own).</summary>
        public int[] size;

        [JsonIgnore] public ModPack pack;
    }

    /// <summary>A side area of stacked layers ("tower"): you arrive on the ground layer, whose stairs are the only way out.</summary>
    public class TowerSpec
    {
        /// <summary>How many layers, ground included: [min, max], rolled once per breach.</summary>
        public int[] layers = { 3, 4 };
        /// <summary>same (every layer borrows the ground's floor) or a theme rule applied layer to layer (deeper, shallower, any).</summary>
        public string layerTheme = "same";
        /// <summary>Each layer above the ground: enemy health up by this much, this much more elite chance, and one more hard anomaly.</summary>
        public float toughnessPerLayer = 0.15f, elitePerLayer = 0.05f;
        public bool extraAnomaly = true;
        /// <summary>Where the kind's objective is: top, ground or every (each layer gets its own).</summary>
        public string objectiveLayer = "top";
        /// <summary>The vault only on the top layer.</summary>
        public bool vaultOnTop = true;
        public LiftSpec lift = new LiftSpec();
        public TowerStairSpec stairs = new TowerStairSpec();
        /// <summary>Whoever carries a layer's override, when the lift or the stairs up have "unlock": "key".</summary>
        public OverseerSpec overseer = new OverseerSpec();
        /// <summary>Logged on arriving on a new layer ({0} = layer, {1} = layers) and on reaching the top.</summary>
        public string upText = "The lift lets you out on layer {0} of {1}.", downText = "The lift takes you back down to layer {0}.",
            topText = "This is the top. Whatever this place was guarding is up here.";
    }

    /// <summary>The lifts between a tower's layers.</summary>
    public class LiftSpec
    {
        public ModelSpec model;
        public string color = "#7FE0FF", lockedColor = "#FF5C5C";
        /// <summary>Metres from where you arrive to the lift up: rooms nearest this distance are picked.</summary>
        public float distance = 24f;
        /// <summary>Turns spent standing on a lift before it moves, and how far its noise carries (metres; 0 = silent).</summary>
        public int callTurns = 2;
        public float noise = 10f;
        /// <summary>Enemies already fighting you within this many metres ride along (at most "riders"; 0 = nobody follows).</summary>
        public float ride = 2.2f;
        public int riders = 3;
        /// <summary>open, or key: every lift up is locked until the layer's overseer is down.</summary>
        public string unlock = "open";
    }

    /// <summary>
    /// The borrowed maps' own stairs, used the way the game uses them: a stair that would lead to the next floor goes
    /// up a layer, one that would lead back goes down (out to the real floor from the ground layer).
    /// </summary>
    public class TowerStairSpec
    {
        /// <summary>false: stairs above the ground lead nowhere and only the lifts connect the layers.</summary>
        public bool link = true;
        /// <summary>open, or key: stairs up stay shut until the layer's overseer is down.</summary>
        public string unlock = "key";
        public string lockedText = "The stairwell door up is sealed. The {0} on this layer has the override.";
        public string blockedText = "The stairwell above is caved in. This is as high as it goes.";
    }

    public class OverseerSpec
    {
        public string name = "Shift Overseer", drop = "Rare";
        public float health = 1.6f;
    }

    // ---- who lives there -------------------------------------------------------------------------------------

    /// <summary>A themed population ("rosters"): enemy ids in depth bands, like the first game's per-theme mob charts.</summary>
    public class RosterSpec
    {
        public string id, name, text, color = "#C9D6E0";
        public bool enabled = true;
        public int weight = 10, minFloor, maxFloor = 99, poolStep = 2;
        /// <summary>tiers[floor / poolStep] (the last tier for anything deeper); a band mixes in the one below it.</summary>
        public List<List<string>> tiers = new List<List<string>>();

        [JsonIgnore] public ModPack pack;

        internal List<string> Band(int floor)
        {
            if (tiers == null || tiers.Count == 0) return new List<string>();
            int i = UnityEngine.Mathf.Clamp(floor / Math.Max(1, poolStep), 0, tiers.Count - 1);
            return i > 0 && UnityEngine.Random.value < 0.3f ? tiers[i - 1] : tiers[i];
        }
    }

    /// <summary>An elite affix ("affixes"): a name prefix, an effect, more health and a drop.</summary>
    public class AffixSpec
    {
        public string id, name, effect;
        public bool enabled = true;
        public int weight = 10, minFloor;
        public float health = 1f;
        public string drop = "Rare";
        public float dropChance = 0.5f;
    }

    /// <summary>Rival crawlers ("nemesis"): a named, much tougher crawler working the same echo.</summary>
    public class NemesisSpec
    {
        public float chance = 0.15f;
        public int minFloor = 1, affixes = 2;
        public float health = 3f;
        public string effect = "Rival", drop = "Epic";
        public List<string> bodies = new List<string>(), first = new List<string>(), last = new List<string>();
    }

    /// <summary>A side area's own objective. The way out stays open; finishing it pays out.</summary>
    public class ObjectiveSpec
    {
        /// <summary>holdout (survive waves at an uplink), uplinks (stand on N pads for a few turns each),
        /// rescue (free the captives and walk them to a stair), salvage (gather N crates), bounty (one named target).</summary>
        public string type;
        public int count = 3, turns = 3, waves = 3, every = 6, size = 2;
        public string text, done, name;
        /// <summary>A prop standing on each objective pad (uplink terminals, the siege beacon).</summary>
        public ModelSpec model;
        public int credits, xp;
        public string reward = "Rare";
        public int rewardItems = 1;
        /// <summary>Exits stay shut until it is done (holdout only, so you can't skip the fight).</summary>
        public bool sealExits;
    }

    // ---- vaults ----------------------------------------------------------------------------------------------

    public class VaultSpec
    {
        public int grade;
        public List<LootRoll> loot = new List<LootRoll>();
        public bool credits = true;
    }

    public class LootRoll
    {
        public string type = "Random";
        /// <summary>Pick one of these types instead.</summary>
        public List<string> types;
        public string rarity = "Rare";
        public float chance = 1f;
        public int count = 1;

        internal IEnumerable<(LOOT_TYPE, RARITY)> Roll()
        {
            for (int i = 0; i < Math.Max(1, count); i++)
            {
                if (UnityEngine.Random.value >= chance) continue;
                string t = types != null && types.Count > 0 ? types[UnityEngine.Random.Range(0, types.Count)] : type;
                yield return (SideAreaData.LootType(t), SideAreaData.Rarity(rarity));
            }
        }
    }

    /// <summary>
    /// The data behind side areas, read from every mod's "sideAreas" object. A later mod replaces an earlier one's
    /// anomaly, kind or vault grade with the same id. Mods/SideAreas holds the bundled set.
    /// </summary>
    internal static class SideAreaData
    {
        internal static readonly List<AnomalySpec> Anomalies = new List<AnomalySpec>();
        internal static readonly List<KindSpec> Kinds = new List<KindSpec>();
        internal static readonly Dictionary<int, VaultSpec> Vaults = new Dictionary<int, VaultSpec>();
        internal static readonly List<RosterSpec> Rosters = new List<RosterSpec>();
        internal static readonly List<AffixSpec> Affixes = new List<AffixSpec>();
        internal static NemesisSpec Nemesis;

        /// <summary>Lists in the json replace the defaults instead of being added to them.</summary>
        private static readonly JsonSerializer Json = new JsonSerializer { ObjectCreationHandling = ObjectCreationHandling.Replace };

        internal static RosterSpec Roster(string id) => Rosters.FirstOrDefault(r => string.Equals(r.id, id, StringComparison.OrdinalIgnoreCase));

        internal static AnomalySpec Anomaly(string id) => Anomalies.FirstOrDefault(a => string.Equals(a.id, id, StringComparison.OrdinalIgnoreCase));
        internal static KindSpec Kind(string id) => Kinds.FirstOrDefault(k => string.Equals(k.id, id, StringComparison.OrdinalIgnoreCase)) ?? Classic;

        /// <summary>What a breach is when no mod defines kinds (or a saved kind's mod is gone).</summary>
        internal static readonly KindSpec Classic = new KindSpec { id = "echo", name = "Echo", text = "An echo of a neighbouring floor." };

        internal static void Load()
        {
            Anomalies.Clear();
            Kinds.Clear();
            Vaults.Clear();
            Rosters.Clear();
            Affixes.Clear();
            Places.All.Clear();
            Nemesis = null;
            foreach (ModPack p in ModPacks.Ordered)
                foreach (var (file, obj) in p.data)
                {
                    if (!(obj["sideAreas"] is JObject sa)) continue;
                    try { RegisterEffects(p, sa["effects"] as JArray); }
                    catch (Exception e) { Plugin.Log.LogError($"{file}: side area effects: {e.Message}"); }
                    foreach (JObject e in (sa["anomalies"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        AnomalySpec a;
                        try { a = e.ToObject<AnomalySpec>(Json); }
                        catch (Exception ex) { Plugin.Log.LogError($"{file}: an anomaly could not be read: {ex.Message}"); continue; }
                        if (string.IsNullOrEmpty(a.id)) { Plugin.Log.LogError($"{file}: an anomaly is missing \"id\""); continue; }
                        Anomalies.RemoveAll(x => string.Equals(x.id, a.id, StringComparison.OrdinalIgnoreCase));
                        if (!a.enabled) continue;
                        a.pack = p;
                        a.name = a.name ?? a.id;
                        Anomalies.Add(a);
                    }
                    foreach (JObject e in (sa["kinds"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        KindSpec k;
                        try { k = e.ToObject<KindSpec>(Json); }
                        catch (Exception ex) { Plugin.Log.LogError($"{file}: a side area kind could not be read: {ex.Message}"); continue; }
                        if (string.IsNullOrEmpty(k.id)) { Plugin.Log.LogError($"{file}: a side area kind is missing \"id\""); continue; }
                        Kinds.RemoveAll(x => string.Equals(x.id, k.id, StringComparison.OrdinalIgnoreCase));
                        if (!k.enabled) continue;
                        k.pack = p;
                        k.name = k.name ?? k.id;
                        if (k.objective != null) k.objective.type = (k.objective.type ?? "").ToLowerInvariant();
                        Kinds.Add(k);
                    }
                    foreach (JObject e in (sa["places"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        PlaceSpec pl;
                        try { pl = e.ToObject<PlaceSpec>(Json); }
                        catch (Exception ex) { Plugin.Log.LogError($"{file}: a place could not be read: {ex.Message}"); continue; }
                        if (string.IsNullOrEmpty(pl.id) || pl.id.Contains("|")) { Plugin.Log.LogError($"{file}: a place needs an \"id\" (without '|')"); continue; }
                        Places.All.RemoveAll(x => string.Equals(x.id, pl.id, StringComparison.OrdinalIgnoreCase));
                        if (!pl.enabled) continue;
                        pl.pack = p;
                        pl.name = pl.name ?? pl.id;
                        Places.All.Add(pl);
                    }
                    foreach (JObject e in (sa["rosters"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        RosterSpec r;
                        try { r = e.ToObject<RosterSpec>(Json); }
                        catch (Exception ex) { Plugin.Log.LogError($"{file}: a roster could not be read: {ex.Message}"); continue; }
                        if (string.IsNullOrEmpty(r.id)) { Plugin.Log.LogError($"{file}: a roster is missing \"id\""); continue; }
                        Rosters.RemoveAll(x => string.Equals(x.id, r.id, StringComparison.OrdinalIgnoreCase));
                        if (!r.enabled) continue;
                        r.pack = p;
                        r.name = r.name ?? r.id;
                        Rosters.Add(r);
                    }
                    foreach (JObject e in (sa["affixes"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        AffixSpec a;
                        try { a = e.ToObject<AffixSpec>(Json); }
                        catch (Exception ex) { Plugin.Log.LogError($"{file}: an affix could not be read: {ex.Message}"); continue; }
                        if (string.IsNullOrEmpty(a.id)) { Plugin.Log.LogError($"{file}: an affix is missing \"id\""); continue; }
                        Affixes.RemoveAll(x => string.Equals(x.id, a.id, StringComparison.OrdinalIgnoreCase));
                        if (a.enabled) Affixes.Add(a);
                    }
                    if (sa["nemesis"] is JObject nem)
                    {
                        try { Nemesis = nem.ToObject<NemesisSpec>(Json); }
                        catch (Exception ex) { Plugin.Log.LogError($"{file}: \"nemesis\" could not be read: {ex.Message}"); }
                    }
                    foreach (JObject e in (sa["vaults"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        try { VaultSpec v = e.ToObject<VaultSpec>(Json); Vaults[UnityEngine.Mathf.Clamp(v.grade, 1, 5)] = v; }
                        catch (Exception ex) { Plugin.Log.LogError($"{file}: a vault could not be read: {ex.Message}"); }
                    }
                }
            foreach (AnomalySpec a in Anomalies)
                foreach (string id in a.notWith ?? new List<string>())
                    if (Anomaly(id) == null) Plugin.Log.LogWarning($"Anomaly {a.id}: notWith '{id}' isn't a loaded anomaly");
            foreach (RosterSpec r in Rosters)
                foreach (string id in r.tiers.SelectMany(t => t ?? new List<string>()).Distinct())
                    if (!id.Contains(":") && AI_Manager.instance != null && AI_Manager.instance.GetCharacter(id) == null) Plugin.Log.LogWarning($"Roster {r.id}: no enemy '{id}'");
            if (Anomalies.Count == 0) Plugin.Log.LogWarning("Side areas: no anomalies loaded (is Mods/SideAreas installed?); echoes will be quiet");
            Plugin.Log.LogInfo($"Side areas: {Kinds.Count} kind(s) [{string.Join(", ", Kinds.Select(k => k.id))}], {Anomalies.Count} anomalies, vault grades {string.Join("/", Vaults.Keys.OrderBy(g => g))}, {Rosters.Count} rosters, {Affixes.Count} affixes, places [{string.Join(", ", Places.All.Select(x => x.id))}], rivals {(Nemesis != null ? "on" : "off")}");
        }

        /// <summary>A mod's own side area effects, registered next to the built-in echo effects.</summary>
        private static void RegisterEffects(ModPack p, JArray arr)
        {
            if (arr == null || arr.Count == 0) return;
            var def = new ClassDef { id = Echoes.EffectClass, name = "Echoes", tiers = new List<TierDef>(), folder = p.folder,
                effects = arr.OfType<JObject>().Select(o => o.ToObject<EffectSpec>()).ToList() };
            ClassRegistry.RegisterEffects(def, null);
        }

        /// <summary>"Name" is a side area effect; "Class@Effect" any mod class's effect; ids already in full pass through.</summary>
        internal static string EffectId(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.Contains("@"))
            {
                string[] parts = name.Split('@');
                return ModEffect.MakeId(parts[0], parts[1]);
            }
            return EffectManager.instance != null && EffectManager.instance.GetEffect(name, false) != null ? name : Echoes.EffectId(name);
        }

        internal static RARITY Rarity(string s) => Enum.TryParse(s ?? "", true, out RARITY r) ? r : RARITY.Rare;
        internal static LOOT_TYPE LootType(string s) => Enum.TryParse(s ?? "", true, out LOOT_TYPE t) ? t : LOOT_TYPE.Random;
    }
}
