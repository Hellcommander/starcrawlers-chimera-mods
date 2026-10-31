using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using I2.Loc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>One item from a mod's "items" list.</summary>
    public class ItemDef
    {
        public string id;
        /// <summary>weapon | ammo | gear | consumable. Optional: taken from the base item.</summary>
        public string kind;
        /// <summary>Vanilla item id, or another mod item ("Mod:Id", or "Id" in the same mod).</summary>
        [JsonProperty("base")] public string baseId;
        public string name;
        public string description;
        /// <summary>Consumables: the type line in the tooltip ("Stimulant", "Medkit", ...).</summary>
        public string category;
        public string icon;
        /// <summary>The look on the floor and on vendor shelves (.glb/.obj); "scale" is its size in metres.</summary>
        public ModelSpec model;
        /// <summary>Gear: the look when a crew member wears it (mesh, offset/rotation in metres and degrees from the
        /// bone, "scale" = its size in metres). Without it, the floor model is stood up and fitted to the body part.</summary>
        public ModelSpec wear;
        /// <summary>Bone for "wear": Head, Chest, Hips, LeftHand, RightHand, LeftFoot, RightFoot (default by slot).</summary>
        public string wearBone;
        public int? level;
        public int? value;
        public string rarity;
        /// <summary>true (default): exactly as written. false: rolls rarity and random stat bonuses like vanilla loot.</summary>
        [JsonProperty("fixed")] public bool isFixed = true;
        /// <summary>How many come in one drop (ammo, consumables).</summary>
        public int? stack;
        public int? stackMax;
        public Dictionary<string, int> tags;

        public Dictionary<string, int> stats;
        public Dictionary<string, int> resists;
        /// <summary>Gear: extra damage added to your attacks.</summary>
        public List<DamageDef> bonusDamage;
        /// <summary>Effect kept on you while equipped: "@Id" (this mod), "#Id" (library) or a vanilla id. Use "duration": -1.</summary>
        public string equipEffect;

        /// <summary>Weapons: base damage. Ammo: damage added per shot.</summary>
        public List<DamageDef> damage;
        public int? ammoUse;
        /// <summary>Weapons: hit bonus by range band, e.g. {"close": 10, "medium": 5, "long": -10}.</summary>
        public Dictionary<string, int> accuracy;
        public OnHitDef onHit;
        /// <summary>Ammo: emissive colour of the dropped magazine, "#RRGGBB".</summary>
        public string glow;

        public UseDef use;
        /// <summary>Medkits: fraction of max health healed (0.25 = 25%).</summary>
        public float? heal;

        public List<LootDef> loot;
        /// <summary>Makes the item a rune that sets into a skill's rune sockets (see Runes.cs).</summary>
        public RuneSpec rune;
        /// <summary>Class ids the item is for: it only drops as loot when a body in your party has one of them
        /// (ClassLoot.cs). Runes default to the classes of their own mod whose sockets take them.</summary>
        public List<string> forClasses;
        public bool enabled = true;

        [JsonIgnore] public ModPack pack;
        [JsonIgnore] public string file;
        [JsonIgnore] public string qualified;
    }

    public class OnHitDef
    {
        public string effect;
        public int chance;
    }

    public class UseDef
    {
        public string effect;
        public int duration;
        public int amount;
        public bool? consume;
    }

    public class LootDef
    {
        /// <summary>Weapon | Gear | Consumable | Ammo | Food | Luxury. Defaults from the item kind.</summary>
        public string type;
        /// <summary>[min, max] floor numbers (0 = first floor). Default: every floor.</summary>
        public int[] floors;
        /// <summary>How many lots the item gets in the table; vanilla items have 1 each.</summary>
        public int weight = 1;
    }

    /// <summary>
    /// Builds mod items as clones of vanilla prefabs and makes the game find them by id: in saves, loot, vendors and
    /// anywhere else that calls ItemDatabase.GetItem. Ids look like "GEAR_HEAD_SCCL_MyMod_ScrapHelm": the vanilla
    /// family prefix is kept because some game code checks it (e.g. "WEAPON", "Grenade").
    /// </summary>
    internal static class ItemRegistry
    {
        internal const string Marker = "SCCL_";

        /// <summary>Internal id -> template (inactive, never Awake'd).</summary>
        internal static readonly Dictionary<string, Item> Templates = new Dictionary<string, Item>();
        /// <summary>"Mod:Id" -> internal id.</summary>
        internal static readonly Dictionary<string, string> Ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, ItemDef> Defs = new Dictionary<string, ItemDef>();

        private static GameObject holder;
        private static readonly HashSet<string> warnedMissing = new HashSet<string>();

        internal static string PackClassId(ModPack p) => "PK_" + p.id;

        internal static void Register()
        {
            if (holder != null) Object.Destroy(holder);
            holder = new GameObject("SCClassLoader_Items");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            Templates.Clear();
            Ids.Clear();
            Defs.Clear();
            if (ItemDatabase.instance == null) { Plugin.Log.LogError("Items: no ItemDatabase yet"); return; }

            // Merge entries by qualified id, in load order: a later entry with the same id patches the earlier one.
            var merged = new Dictionary<string, (JObject obj, ModPack pack, string file)>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (ModPack p in ModPacks.Ordered)
            {
                RegisterPackEffects(p);
                foreach (var (file, obj) in p.data)
                {
                    foreach (JObject e in (obj["items"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        string id = (string)e["id"];
                        if (string.IsNullOrEmpty(id)) { Plugin.Log.LogError($"{file}: an item has no \"id\""); continue; }
                        string q = ModPacks.Qualify(p, id);
                        if (merged.TryGetValue(q, out var prev))
                        {
                            var patched = (JObject)prev.obj.DeepClone();
                            patched.Merge(e, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                            merged[q] = (patched, prev.pack, prev.file);
                            Plugin.Log.LogInfo($"Items: {p.id} patches {q}");
                        }
                        else { merged[q] = (e, p, file); order.Add(q); }
                    }
                }
            }

            var defs = new List<ItemDef>();
            foreach (string q in order)
            {
                var (obj, pack, file) = merged[q];
                try
                {
                    ItemDef def = obj.ToObject<ItemDef>();
                    if (!def.enabled) continue;
                    def.pack = pack;
                    def.file = file;
                    def.qualified = q;
                    defs.Add(def);
                }
                catch (Exception e) { Plugin.Log.LogError($"{file}: item '{q}' could not be read: {e.Message}"); }
            }
            pendingDefs = defs; // lets an item use a base defined further down
            foreach (ItemDef def in defs)
            {
                try { Build(def, new HashSet<string>()); }
                catch (Exception e) { Plugin.Log.LogError($"{def.file}: item '{def.qualified}' failed: {e}"); }
            }
            pendingDefs = null;
            FillPackEffectIcons();
            InjectLoot();
            if (Templates.Count > 0) Plugin.Log.LogInfo($"Items: registered {Templates.Count} mod item(s)");
        }

        /// <summary>A data file's "effects" list works like a class's: referenced as "@Id" from that mod's items.</summary>
        private static void RegisterPackEffects(ModPack p)
        {
            var effects = p.data.SelectMany(d => (d.obj["effects"] as JArray ?? new JArray()).OfType<JObject>())
                .Select(e => e.ToObject<EffectSpec>()).ToList();
            if (effects.Count == 0) return;
            var def = new ClassDef { id = PackClassId(p), name = p.name ?? p.id, folder = p.folder, effects = effects, tiers = new List<TierDef>() };
            ClassRegistry.SetFolder(def.id, p.folder);
            ClassRegistry.RegisterEffects(def, null);
        }

        /// <summary>Pack effects without their own png take the icon of the first item that uses them.</summary>
        private static void FillPackEffectIcons()
        {
            Sprite fallback = Game.instance?.skills?.FirstOrDefault(s => s != null && s.icon != null)?.icon;
            foreach (var rt in ModEffect.Specs.Values.Where(r => r.classId != null && r.classId.StartsWith("PK_") && r.icon == null).ToList())
            {
                var user = Defs.FirstOrDefault(kv => PackClassId(kv.Value.pack) == rt.classId &&
                    new[] { kv.Value.equipEffect, kv.Value.use?.effect, kv.Value.onHit?.effect }.Any(e => e != null && Actions.EffectId(e, rt.classId) == rt.id));
                rt.icon = user.Key != null ? Templates[user.Key].inventorySprite : fallback;
                Effect tpl = EffectManager.instance.GetEffect(rt.id, false);
                if (tpl != null) tpl.icon = rt.icon; // the template copied the (empty) icon when it was registered
            }
        }

        // ---- building ---------------------------------------------------------------------------------------

        private static Item Build(ItemDef def, HashSet<string> visiting)
        {
            if (Ids.TryGetValue(def.qualified, out string done)) return Templates[done];
            if (!visiting.Add(def.qualified)) { Plugin.Log.LogError($"{def.file}: item '{def.qualified}' has a base loop"); return null; }

            Item src = ResolveBase(def, visiting);
            if (src == null) return null;

            string internalId = MakeId(src.itemId, def);
            GameObject go = Object.Instantiate(src.gameObject, holder.transform, false);
            go.name = internalId;
            foreach (Item_AltInfo alt in go.GetComponentsInChildren<Item_AltInfo>(true)) Object.DestroyImmediate(alt); // hidden-chem disguise
            Item it = go.GetComponent<Item>();
            it.itemId = internalId;
            it.itemInstanceId = "";
            string ctx = def.qualified;
            string cls = PackClassId(def.pack);

            // Common fields
            if (def.name != null)
            {
                it.itemName = ClassRegistry.Term("ITEM_" + internalId, "Name", def.name);
                it.name_prefix = default(LocalizedString);
                it.name_affix = default(LocalizedString);
            }
            if (def.description != null) it.itemDescription = ClassRegistry.Term("ITEM_" + internalId, "Desc", def.description);
            Sprite icon = ClassRegistry.LoadSprite(def.pack.folder, def.icon);
            if (icon != null) it.inventorySprite = icon;
            if (def.model != null && !ModelAssets.ApplyToItem(it, def.model, def.pack.folder)) Plugin.Log.LogWarning($"{ctx}: model not applied; it keeps its base's look");
            if (def.level.HasValue) it.itemLevel = def.level.Value;
            if (def.rarity != null) it.rarity = ClassRegistry.ParseEnum(def.rarity, RARITY.Common, ctx);
            it.rarityApplied = def.isFixed;
            if (def.value.HasValue) { it.itemValue = def.value.Value; it.stackValue = def.value.Value; }
            if (def.stack.HasValue) it.stack = def.stack.Value;
            foreach (var kv in def.tags ?? new Dictionary<string, int>()) SetTag(it, kv.Key, kv.Value);

            string kind = (def.kind ?? KindOf(it)).ToLowerInvariant();
            if (kind != KindOf(it)) Plugin.Log.LogWarning($"{ctx}: kind '{kind}' does not match its base ({KindOf(it)}); using the base's");
            kind = KindOf(it);

            if (it is Item_Equipment eq)
            {
                if (def.stats != null) eq.stats = def.stats.Select(kv => new StatObj(ClassRegistry.ParseEnum(kv.Key, STAT_TYPE.Null, ctx), kv.Value)).Where(s => s.statType != STAT_TYPE.Null).ToList();
                if (def.resists != null) eq.resistanceMods = def.resists.Select(kv => new DmgObj(ClassRegistry.ParseEnum(kv.Key, DAMAGE_TYPE.Null, ctx), kv.Value)).Where(r => r.dmgType != DAMAGE_TYPE.Null).ToList();
                if (def.bonusDamage != null && !(it is Item_Ammo)) eq.damageMods = Damage(def.bonusDamage, ctx);
                if (def.equipEffect != null)
                {
                    if (eq.equipTrigger == null) eq.equipTrigger = go.AddComponent<Item_OnUse_Trigger>();
                    eq.equipTrigger.equipment_effect = Actions.EffectId(def.equipEffect, cls);
                    CheckEffect(eq.equipTrigger.equipment_effect, ctx);
                }
            }
            if (it is Item_Weapon w)
            {
                if (def.damage != null) w.damageList = Damage(def.damage, ctx);
                if (def.ammoUse.HasValue) w.ammoUse = def.ammoUse.Value;
                foreach (var kv in def.accuracy ?? new Dictionary<string, int>())
                {
                    string band = kv.Key.ToUpperInvariant();
                    if (band != "LONG" && band != "MEDIUM" && band != "CLOSE") { Plugin.Log.LogWarning($"{ctx}: accuracy band '{kv.Key}' should be close, medium or long"); continue; }
                    SetTag(it, "ACCURACY_" + band, kv.Value);
                }
            }
            if (it is Item_Ammo am)
            {
                if (def.damage != null)
                {
                    am.damageMods = Damage(def.damage, ctx);
                    am.damageMinMax = Vector2Int.zero; // keep the written damage instead of a per-level roll
                    if (!am.damageMods.Any(d => d.damage > 0)) Plugin.Log.LogWarning($"{ctx}: ammo needs at least one damage entry above 0 or weapon tooltips break");
                }
                if (def.onHit != null)
                {
                    am.onHitEffect = Actions.EffectId(def.onHit.effect, cls) ?? "";
                    am.onHitChance = def.onHit.chance;
                    am.onHitChanceLevelMod = 1;
                    am.onHitChanceMinMax = new Vector2Int(def.onHit.chance, def.onHit.chance); // flat chance, not level-scaled
                    CheckEffect(am.onHitEffect, ctx);
                }
                if (def.glow != null && ColorUtility.TryParseHtmlString(def.glow, out Color c)) am.ammoEmissiveGlow = c;
                if (def.stackMax.HasValue) am.ammoMax = def.stackMax.Value;
            }
            if (it is Item_Consumable con)
            {
                if (def.category != null) con.consumableCategory = ClassRegistry.Term("ITEM_" + internalId, "Cat", def.category);
                if (def.stackMax.HasValue) { con.override_stackMax = def.stackMax.Value; con.isStackable = def.stackMax.Value > 1; }
                if (def.use != null)
                {
                    if (con.useTrigger == null) Plugin.Log.LogError($"{ctx}: base '{src.itemId}' has no use trigger; pick a consumable that applies an effect");
                    else
                    {
                        if (def.use.effect != null) { con.useTrigger.use_effect = Actions.EffectId(def.use.effect, cls); CheckEffect(con.useTrigger.use_effect, ctx); }
                        if (def.use.duration > 0) con.useTrigger.effect_setDuration = def.use.duration;
                        if (def.use.amount > 0) con.useTrigger.effect_setAmountA = def.use.amount;
                        con.useTrigger.bonus_effects = new Item_OnUse_Trigger.BonusEffect[0];
                    }
                    if (def.use.consume.HasValue) con.onUse_consume = def.use.consume.Value;
                }
                if (def.heal.HasValue)
                {
                    if (it is Item_Consumable_Medkit mk) mk.healPercent = def.heal.Value;
                    else Plugin.Log.LogWarning($"{ctx}: \"heal\" needs a medkit as base");
                }
            }

            Templates[internalId] = it;
            Ids[def.qualified] = internalId;
            if (def.rune != null) SkillMods.Normalize(def.rune, ctx);
            Defs[internalId] = def;
            visiting.Remove(def.qualified);
            Plugin.Log.LogInfo($"Items: {def.qualified} -> {internalId} ({kind}, base {src.itemId})");
            return it;
        }

        private static Item ResolveBase(ItemDef def, HashSet<string> visiting)
        {
            if (string.IsNullOrEmpty(def.baseId)) { Plugin.Log.LogError($"{def.file}: item '{def.qualified}' needs a \"base\" item (see vanilla_reference.txt)"); return null; }
            // A mod item first ("Other:Id", or a plain id defined in this mod), then a vanilla id.
            string q = ModPacks.Qualify(def.pack, def.baseId);
            if (Ids.TryGetValue(q, out string built)) return Templates[built];
            ItemDef pending = PendingDefs.FirstOrDefault(d => string.Equals(d.qualified, q, StringComparison.OrdinalIgnoreCase));
            if (pending != null) return Build(pending, visiting);
            Item v = VanillaItem(def.baseId);
            if (v == null) Plugin.Log.LogError($"{def.file}: item '{def.qualified}': base '{def.baseId}' not found (see vanilla_reference.txt)");
            return v;
        }

        /// <summary>Set while Register runs, so a base defined later in the files can still be built first.</summary>
        private static IEnumerable<ItemDef> PendingDefs => pendingDefs ?? Enumerable.Empty<ItemDef>();
        private static List<ItemDef> pendingDefs;

        /// <summary>The prefab/template for any item id, without instantiating it.</summary>
        internal static Item VanillaOrModItem(string id) =>
            string.IsNullOrEmpty(id) ? null : Templates.TryGetValue(id, out Item t) ? t : VanillaItem(id);

        /// <summary>Plain display name of an item id (no rarity colour), or the id itself.</summary>
        internal static string NameOf(string id)
        {
            try
            {
                Item it = VanillaOrModItem(id);
                return it == null ? id : System.Text.RegularExpressions.Regex.Replace((string)it.itemName ?? id, "<.*?>", "");
            }
            catch { return id; }
        }

        internal static Item VanillaItem(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Contains(Marker)) return null;
            try { return ItemDatabase.instance.GetItem(id, false, RARITY.None); }
            catch (KeyNotFoundException) { return null; } // some GetItem branches index their dictionary directly
        }

        /// <summary>Keeps the vanilla family prefix (WEAPON_R_, GEAR_HEAD_, CONSUME_ATK_..., plus "Grenade").</summary>
        private static string MakeId(string baseId, ItemDef def)
        {
            var family = new List<string>();
            foreach (string part in baseId.Split('_'))
            {
                if (part.Length == 0 || part.Any(char.IsLower) || part == "SCCL") break;
                family.Add(part);
            }
            if (baseId.Contains("Grenade")) family.Add("Grenade");
            string prefix = family.Count > 0 ? string.Join("_", family) + "_" : "";
            return prefix + Marker + def.pack.id + "_" + ModPacks.Sanitize(def.id);
        }

        private static string KindOf(Item it) =>
            it is Item_Ammo ? "ammo" : it is Item_Weapon ? "weapon" : it is Item_Equipment ? "gear" : it is Item_Consumable ? "consumable" : "other";

        private static List<DmgObj> Damage(List<DamageDef> list, string ctx) =>
            list.Select(d => new DmgObj(ClassRegistry.ParseEnum(d.type, DAMAGE_TYPE.Crushing, ctx), d.min) { damageMax = Math.Max(d.max, d.min) }).ToList();

        private static void SetTag(Item it, string name, int amount)
        {
            it.tags.RemoveAll(t => t != null && t.tagName == name);
            it.tags.Add(new Tag(name, amount));
        }

        private static void CheckEffect(string id, string ctx)
        {
            if (string.IsNullOrEmpty(id) || EffectManager.instance == null) return;
            if (EffectManager.instance.GetEffect(id, false) == null) Plugin.Log.LogWarning($"{ctx}: effect '{id}' not found");
        }

        // ---- loot -------------------------------------------------------------------------------------------

        private static void InjectLoot()
        {
            LootManager lm = LootManager.instance;
            if (lm?.floorLootTables == null) return;
            foreach (LootTable lt in lm.floorLootTables)
                foreach (LootTableObject t in lt?.tables ?? new LootTableObject[0])
                    t?.items.RemoveAll(i => i == null || (i.itemId != null && i.itemId.Contains(Marker)));

            int added = 0;
            foreach (var kv in Defs)
            {
                ItemDef def = kv.Value;
                Item tpl = Templates[kv.Key];
                foreach (LootDef ld in def.loot ?? new List<LootDef>())
                {
                    LOOT_TYPE type = ClassRegistry.ParseEnum(ld.type ?? DefaultLootType(tpl), LOOT_TYPE.Random, def.qualified);
                    if (type == LOOT_TYPE.Random || type == LOOT_TYPE.Null || type == LOOT_TYPE.Credits) { Plugin.Log.LogWarning($"{def.qualified}: loot type must be Weapon, Gear, Consumable, Ammo, Food or Luxury"); continue; }
                    int lo = ld.floors != null && ld.floors.Length > 0 ? ld.floors[0] : 0;
                    int hi = ld.floors != null && ld.floors.Length > 1 ? ld.floors[1] : lm.floorLootTables.Length - 1;
                    for (int f = Math.Max(0, lo); f <= Math.Min(hi, lm.floorLootTables.Length - 1); f++)
                    {
                        LootTable lt = lm.floorLootTables[f];
                        if (lt == null) continue;
                        LootTableObject table = (lt.tables ?? new LootTableObject[0]).FirstOrDefault(t => t != null && t.tableType == type);
                        if (table == null)
                        {
                            table = new LootTableObject { tableName = "SCCL_" + type, tableType = type, items = new List<Item>() };
                            lt.tables = (lt.tables ?? new LootTableObject[0]).Concat(new[] { table }).ToArray();
                        }
                        for (int n = 0; n < Math.Max(1, ld.weight); n++) table.items.Add(tpl);
                        added++;
                    }
                }
            }
            lm.ClearScratchLists();
            if (added > 0) Plugin.Log.LogInfo($"Items: added mod items to {added} floor loot table(s)");
        }

        private static string DefaultLootType(Item it) =>
            it is Item_Ammo ? "Ammo" : it is Item_Weapon ? "Weapon" : it is Item_Equipment ? "Gear" : "Consumable";

        // ---- patches ----------------------------------------------------------------------------------------

        /// <summary>Mod ids resolve to their template; a mod id that is no longer installed becomes a broken item.</summary>
        [HarmonyPatch(typeof(ItemDatabase), nameof(ItemDatabase.GetItem), typeof(string), typeof(bool), typeof(RARITY))]
        private static class Patch_GetItem
        {
            private static bool Prefix(ItemDatabase __instance, string internalName, bool doInstantiate, ref Item __result)
            {
                if (internalName == null || !internalName.Contains(Marker)) return true;
                if (Templates.TryGetValue(internalName, out Item tpl))
                {
                    __result = doInstantiate ? Object.Instantiate(tpl) : tpl;
                    return false;
                }
                if (warnedMissing.Add(internalName))
                    Plugin.Log.LogWarning($"Items: '{internalName}' is from a mod that is not installed; it becomes a broken item");
                __result = __instance.GetItem("MISC_Broken_Item", doInstantiate, RARITY.None);
                return false;
            }
        }

        /// <summary>Loot sets every item's level to the floor; fixed mod items keep the level they were given.</summary>
        [HarmonyPatch(typeof(LootManager), nameof(LootManager.RarityCheck))]
        private static class Patch_RarityCheck
        {
            private static void Postfix(Item item)
            {
                if (item != null && item.itemId != null && Defs.TryGetValue(item.itemId, out ItemDef def) && def.isFixed && def.level.HasValue)
                    item.itemLevel = def.level.Value;
            }
        }
    }
}
