using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>One recipe from a mod's "recipes" list.</summary>
    public class RecipeDef
    {
        public string id;
        /// <summary>Item made: a mod item ("Id" in this mod, "Mod:Id") or a vanilla id.</summary>
        public string output;
        public int amount = 1;
        public List<CostSpec> inputs = new List<CostSpec>();
        public int credits;
        /// <summary>Tags of items you must carry (not used up), e.g. "LOCKPICK".</summary>
        public List<string> tools = new List<string>();
        public RequireSpec requires;
        /// <summary>Where it can be made: "apartment" (the Fabricator pad) and/or "field" (craft abilities). Default both.</summary>
        public List<string> stations;
        public bool enabled = true;

        [JsonIgnore] public ModPack pack;
        [JsonIgnore] public string qualified;
        [JsonIgnore] public string outputId;
    }

    /// <summary>An amount of an item ("item": id) or of anything carrying a tag ("tag": "MECHPARTS").</summary>
    public class CostSpec
    {
        public string item;
        public string tag;
        public int count = 1;
        [JsonIgnore] public string resolved;
    }

    public class RequireSpec
    {
        /// <summary>Skill tree id (vanilla "SKILLTREE_Engineer" or a mod class "MOD_Scrapper").</summary>
        [JsonProperty("class")] public string tree;
        /// <summary>Minimum rank in that tree (0 = first tier).</summary>
        public int rank;
    }

    /// <summary>
    /// Recipes, the Apartment's Fabricator pad, and the shared inventory helpers that abilities use for item costs,
    /// giving and salvaging. The crafting window itself is CraftPanel.
    /// </summary>
    internal static class Crafting
    {
        internal static readonly List<RecipeDef> Recipes = new List<RecipeDef>();

        // ---- loading ----------------------------------------------------------------------------------------

        internal static void Load()
        {
            Recipes.Clear();
            var seen = new Dictionary<string, RecipeDef>(StringComparer.OrdinalIgnoreCase);
            foreach (ModPack p in ModPacks.Ordered)
                foreach (var (file, obj) in p.data)
                    foreach (JObject e in (obj["recipes"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        RecipeDef r;
                        try { r = e.ToObject<RecipeDef>(); }
                        catch (Exception ex) { Plugin.Log.LogError($"{file}: a recipe could not be read: {ex.Message}"); continue; }
                        if (!r.enabled) continue;
                        r.pack = p;
                        r.qualified = ModPacks.Qualify(p, string.IsNullOrEmpty(r.id) ? r.output : r.id);
                        r.outputId = ResolveItem(r.output, p);
                        if (ItemRegistry.VanillaOrModItem(r.outputId) == null) { Plugin.Log.LogError($"{file}: recipe '{r.qualified}': output '{r.output}' not found"); continue; }
                        foreach (CostSpec c in r.inputs ?? new List<CostSpec>()) Resolve(c, p, file, r.qualified);
                        if (r.stations == null || r.stations.Count == 0) r.stations = new List<string> { "apartment", "field" };
                        if (seen.TryGetValue(r.qualified, out RecipeDef prev)) { Recipes.Remove(prev); Plugin.Log.LogInfo($"Recipes: {p.id} replaces {r.qualified}"); }
                        seen[r.qualified] = r;
                        Recipes.Add(r);
                    }
            if (Recipes.Count > 0) Plugin.Log.LogInfo($"Recipes: {Recipes.Count} loaded");
        }

        internal static void Resolve(CostSpec c, ModPack p, string file, string ctx)
        {
            if (!string.IsNullOrEmpty(c.item))
            {
                c.resolved = ResolveItem(c.item, p);
                if (ItemRegistry.VanillaOrModItem(c.resolved) == null) Plugin.Log.LogWarning($"{file}: {ctx}: item '{c.item}' not found");
            }
            else if (string.IsNullOrEmpty(c.tag)) Plugin.Log.LogWarning($"{file}: {ctx}: a cost needs \"item\" or \"tag\"");
            if (c.count < 1) c.count = 1;
        }

        /// <summary>"Mod:Id" or an id from this mod -> its in-game id; anything else is taken as a vanilla id.</summary>
        internal static string ResolveItem(string id, ModPack p)
        {
            if (string.IsNullOrEmpty(id)) return id;
            if (ItemRegistry.Ids.TryGetValue(id, out string a)) return a;
            if (p != null && ItemRegistry.Ids.TryGetValue(ModPacks.Qualify(p, id), out string b)) return b;
            var hits = ItemRegistry.Ids.Where(kv => kv.Key.EndsWith(":" + id, StringComparison.OrdinalIgnoreCase)).ToList();
            return hits.Count == 1 ? hits[0].Value : id;
        }

        // ---- inventory helpers -------------------------------------------------------------------------------

        private static bool Matches(Item i, CostSpec c) =>
            i != null && (c.resolved != null ? i.itemId == c.resolved : i.HasTag(c.tag));

        private static IEnumerable<Item> Carried() =>
            (Menu_Inventory.GetItems() ?? new List<Item>()).Where(i => i != null && i.inPlayerInventory);

        internal static int Count(CostSpec c) => Carried().Where(i => Matches(i, c)).Sum(i => Math.Max(1, i.stack));

        internal static bool HasAll(IEnumerable<CostSpec> costs) => (costs ?? Enumerable.Empty<CostSpec>()).All(c => Count(c) >= c.count);

        /// <summary>Takes the items out of the inventory, newest stacks last. Never touches equipped gear or the stash.</summary>
        internal static void Consume(CostSpec c)
        {
            int left = c.count;
            foreach (Item i in Carried().Where(i => Matches(i, c)).Reverse().ToList())
            {
                if (left <= 0) break;
                int have = Math.Max(1, i.stack);
                if (have > left)
                {
                    i.SetStack(have - left);
                    i.CalculateValue();
                    left = 0;
                }
                else
                {
                    left -= have;
                    Menu_Inventory.instance.RemoveItem(i, null, isSilent: true);
                    Object.Destroy(i.gameObject);
                }
            }
            Menu_Inventory.instance.Refresh();
        }

        /// <summary>Puts an item in the inventory, or at your feet when there is no room.</summary>
        internal static Item Give(string id, int count)
        {
            Item item = ItemDatabase.instance.GetItem(id);
            if (item == null) return null;
            if (item.isStackable || item is Item_Ammo) item.SetStack(Math.Max(1, count));
            if (!Menu_Inventory.Take(item))
                Plugin.Log.LogInfo($"Crafting: inventory full, {id} dropped");
            AudioManager.PlayEvent("event:/UI/Shing");
            return item;
        }

        internal static string Describe(CostSpec c)
        {
            string name = c.resolved != null ? ItemRegistry.NameOf(c.resolved) : (c.tag ?? "?").ToLowerInvariant().Replace('_', ' ');
            return $"{c.count}× {name}";
        }

        // ---- recipes ------------------------------------------------------------------------------------------

        internal static bool Unlocked(RecipeDef r)
        {
            if (r.requires?.tree == null) return true;
            int rank = Player_SkillControl.instance != null ? Player_SkillControl.instance.GetRank(r.requires.tree) : -1;
            return rank >= r.requires.rank;
        }

        private static bool HasTools(RecipeDef r) => (r.tools ?? new List<string>()).All(t => Carried().Any(i => i.HasTag(t)));

        internal static bool CanCraft(RecipeDef r) =>
            HasAll(r.inputs) && HasTools(r) && Menu_Inventory.instance.GetCredits() >= r.credits;

        /// <summary>Ingredient list with have/need colours, for the tooltip.</summary>
        internal static string Needs(RecipeDef r)
        {
            var parts = new List<string>();
            foreach (CostSpec c in r.inputs ?? new List<CostSpec>())
            {
                int have = Count(c);
                parts.Add($"<color={(have >= c.count ? "#7CFC7C" : "#FF7272")}>{Describe(c)} ({have})</color>");
            }
            foreach (string t in r.tools ?? new List<string>())
                parts.Add($"<color={(Carried().Any(i => i.HasTag(t)) ? "#7CFC7C" : "#FF7272")}>tool: {t.ToLowerInvariant().Replace('_', ' ')}</color>");
            if (r.credits > 0)
                parts.Add($"<color={(Menu_Inventory.instance.GetCredits() >= r.credits ? "#7CFC7C" : "#FF7272")}>{r.credits} credits</color>"); // the prompt line can't show sprite icons
            return string.Join(", ", parts);
        }

        /// <summary>Where a recipe can be made, for the window: "Apartment, field".</summary>
        internal static string Where(RecipeDef r) =>
            string.Join(", ", r.stations.Select(s => s.ToLowerInvariant() == "apartment" ? "the Apartment" : s.ToLowerInvariant() == "field" ? "crafting skills" : s));

        internal static bool MadeAt(RecipeDef r, string at) =>
            at != null && r.stations.Any(s => string.Equals(s, at, StringComparison.OrdinalIgnoreCase));

        /// <summary>Recipes a craft action or the Rune Matrix asks for: ids as written in any mod, "Mod:Id", or qualified.</summary>
        internal static bool Listed(RecipeDef r, List<string> only) =>
            only == null || only.Any(id => string.Equals(ModPacks.Qualify(r.pack, id), r.qualified, StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(id, r.qualified, StringComparison.OrdinalIgnoreCase)
                                           || r.qualified.EndsWith(":" + id, StringComparison.OrdinalIgnoreCase));

        /// <summary>A fresh copy of the output, rolled, kept out of the world; what the window shows and what you get.</summary>
        internal static Item MakePreview(RecipeDef r, Transform holder)
        {
            Item it = ItemDatabase.instance.GetItem(r.outputId);
            if (it == null) return null;
            if (r.amount > 1 && (it.isStackable || it is Item_Ammo)) it.SetStack(r.amount);
            it.transform.SetParent(holder);
            it.transform.localPosition = Vector3.zero;
            it.inPlayerInventory = it.inPlayerStash = it.inVendorInventory = false;
            it.OnEnterInventory(); // hides its world model
            it.Rarify();
            return it;
        }

        /// <summary>Makes the recipe, handing over the previewed item. Returns why not, or null when it was made.</summary>
        internal static string Make(RecipeDef r, Item made)
        {
            if (made == null) return "That item can't be made.";
            if (!CanCraft(r)) return $"Can't make {ItemRegistry.NameOf(r.outputId)} yet. Needs: {Needs(r)}";
            if (!Menu_Inventory.TakeCheck(made)) return "No room in your pack.";
            string name = ItemRegistry.NameOf(r.outputId); // the item's own name carries its stack size
            foreach (CostSpec c in r.inputs ?? new List<CostSpec>()) Consume(c);
            if (r.credits > 0) Menu_Inventory.instance.AdjustCredits(-r.credits);
            made.transform.SetParent(null);
            Menu_Inventory.instance.AddToInventory(made, doStack: true, isSilent: true, null);
            var s = Menu_Inventory.instance.GetItemSlot(Menu_Inventory.instance.GetLastItem());
            if (s != null) s.DoShing();
            AudioManager.PlayEvent("event:/UI/Shing");
            Game.LOG($"You fabricate {name}{(r.amount > 1 ? $" ×{r.amount}" : "")}.");
            Menu_Inventory.instance.Refresh();
            return null;
        }

        /// <summary>Opens the crafting window. at: "apartment" or "field" (null = browse only); only = recipe ids to offer (null = all).</summary>
        internal static void Open(string at, List<string> only = null) => CraftPanel.Open(at, only);

        // ---- the Apartment station ---------------------------------------------------------------------------

        internal class StationPad : MonoBehaviour { }

        /// <summary>An amber pad near the Apartment's arrival point opens the Fabricator.</summary>
        private static void PlaceApartmentPad()
        {
            if (Game.FLOOR != 100 || Map.instance == null || Recipes.Count == 0) return;
            if (Map.instance.GetComponentInChildren<StationPad>(true) != null) return;
            NavNode arrival = Map.instance.GetNavNodeAt(new Vector3(5f, 0.2f, -3f));
            if (arrival == null) return;
            NavNode spot = arrival.GetLinks().FirstOrDefault(n => n != null && !n.IsOccupied() && !n.HasDoor() && !n.Block_Move && n.GetRoom() == arrival.GetRoom())
                           ?? Map.instance.GetClosestNode(arrival, 3f, validNodesOnly: true);
            if (spot == null || spot == arrival) return;
            TeleportPad pad = Object.Instantiate(Game.instance.TELEPORTPAD_PREFAB, spot.transform.position, Quaternion.identity);
            pad.name = "SCCL_FabricatorPad";
            pad.transform.SetParent(Map.instance.transform);
            pad.color_clear = new Color(1f, 0.65f, 0.15f);
            if (pad.lineRenderer != null) Object.Destroy(pad.lineRenderer);
            foreach (ParticleSystem ps in pad.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = pad.color_clear;
            }
            pad.gameObject.AddComponent<StationPad>();
            pad.Init();
            Plugin.Instance.StartCoroutine(Tint(pad));
        }

        private static IEnumerator Tint(TeleportPad pad)
        {
            yield return null;
            if (pad != null) pad.SetClear();
        }

        private static IEnumerator OpenWhenStill(NavNode node)
        {
            for (float t = 0; t < 5f && (Game.RESOLVING || Game.LOCK || Player.instance.GetMover().isMoving); t += Time.unscaledDeltaTime) yield return null;
            if (Player.instance.GetMover().GetCurrentNavNode() == node && !Game.VENDOR && !CraftPanel.IsOpen) Open("apartment");
        }

        // ---- patches ------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(TeleportPad), nameof(TeleportPad.OnEnterNode))]
        private static class Patch_Pad
        {
            private static bool Prefix(TeleportPad __instance, Character c)
            {
                if (__instance.GetComponent<StationPad>() == null) return true;
                if (c == Player.instance && !Game.Game_isLoading) Plugin.Instance.StartCoroutine(OpenWhenStill(__instance.myNodes[0]));
                return false;
            }
        }

        [HarmonyPatch(typeof(Map), nameof(Map.Init))]
        private static class Patch_ApartmentNew
        {
            private static void Postfix() { try { PlaceApartmentPad(); } catch (Exception e) { Plugin.Log.LogError("Fabricator pad: " + e); } }
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.DeserializeLevel))]
        private static class Patch_ApartmentLoad
        {
            private static void Postfix(int id) { if (id == 100) try { PlaceApartmentPad(); } catch (Exception e) { Plugin.Log.LogError("Fabricator pad: " + e); } }
        }
    }
}
