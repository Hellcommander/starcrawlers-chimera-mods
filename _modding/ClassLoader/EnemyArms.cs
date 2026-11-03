using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// Enemy weapons ([EnemyWeapons], off by default): the weapon in a dead enemy's hand falls to the floor and stays
    /// there as the item, in the pose it landed and with its own mesh. The game already lets most of them fall (and
    /// cleans them up after a few seconds); the ones it keeps welded to the body (Smorn's blade, the Armorer's flamer)
    /// are let go here too. The vanilla weapon it looks like (a baton, a rifle) decides how it is used; its damage types
    /// and on-hit effects come from the attack the enemy made with it, and its strength from the floor's loot. Other
    /// loot still comes from the inventory everyone carries, as in the base game. Each enemy-and-weapon pair is a
    /// "recipe" kept in BepInEx/config so a saved weapon rebuilds after a restart.
    /// The first drop comes with a Scrap Bot Kit (see <see cref="ScrapBot"/>).
    /// </summary>
    internal static class EnemyArms
    {
        internal static ConfigEntry<bool> Enabled, OnHitEffects, BotOn, ScrapBossWeapons;
        internal static ConfigEntry<int> DamagePercent, MeleeSaving, HarvestRange, BossScrapBonus, RebuildCost, RebuildRange, KeepScrap, ScrapPrice,
            RepairCost, RepairPercent, BotArmor, BotHealth;
        internal static ConfigEntry<string> HarvestUpTo, ScrapParts;

        internal const string Mark = "SCCL_EW_";
        internal const string KitId = "CONSUME_" + Mark + "ScrapBotKit";
        internal const string BossTag = "SCCL_EW_BOSS";
        internal const string BotTag = "SCCL_SCRAPBOT";
        internal const string OwnedTag = "SCCL_SCRAPBOT_OWNED", KitGivenTag = "SCCL_SCRAPBOT_KIT";
        private const string FileName = "arendeth.starcrawlers.classloader.enemyweapons.json";

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("EnemyWeapons", "Enabled", false,
                "The weapon that falls from a dead enemy's hand stays where it lands, and you can pick it up and use it: its attack's damage types and on-hit effects, as strong as the floor's loot. A boss's is Epic or better.");
            DamagePercent = cfg.Bind("EnemyWeapons", "LowPowerDamage", 75, new ConfigDescription(
                "Enemies run their weapons in a low-power mode: weaker than the floor's loot, but a gun needs no ammo and a melee weapon draws less of your stamina. Its damage, in percent of a normal weapon from the same floor.",
                new AcceptableValueRange<int>(25, 300)));
            MeleeSaving = cfg.Bind("EnemyWeapons", "MeleeStaminaSaving", 25, new ConfigDescription("Percent less stamina your skills cost while you swing an enemy melee weapon.", new AcceptableValueRange<int>(0, 100)));
            OnHitEffects = cfg.Bind("EnemyWeapons", "OnHitEffects", true, "Enemy weapons keep what their attack did on a hit (burning, shock, hazards, a buff for the wielder...).");
            BotOn = cfg.Bind("EnemyWeapons", "ScrapBot", true,
                "Needs [Performance] EventBus on. The first enemy weapon comes with a Scrap Bot Kit: a bot that follows you, turns enemy weapons into scrap, rebuilds the floor's broken props with it and sells the rest to vendors.");
            HarvestUpTo = cfg.Bind("EnemyWeapons", "ScrapBotHarvestUpTo", "Rare", new ConfigDescription(
                "The scrap bot strips enemy weapons lying near it up to this rarity, without asking. None = it only scraps what you hand it from its menu.",
                new AcceptableValueList<string>("None", "Common", "Rare", "Epic", "Legendary")));
            HarvestRange = cfg.Bind("EnemyWeapons", "ScrapBotRange", 3, new ConfigDescription("Tiles the scrap bot reaches for a weapon on the floor.", new AcceptableValueRange<int>(1, 8)));
            ScrapParts = cfg.Bind("EnemyWeapons", "ScrapPerWeapon", "1,2,3,5", "Scrap the bot gets from one enemy weapon: Common, Rare, Epic, Legendary.");
            BossScrapBonus = cfg.Bind("EnemyWeapons", "BossScrapBonus", 2, "Extra scrap from a boss weapon.");
            ScrapBossWeapons = cfg.Bind("EnemyWeapons", "ScrapBossWeapons", false, "The scrap bot may take boss weapons apart too (off keeps them as trophies).");
            RebuildCost = cfg.Bind("EnemyWeapons", "RebuildCost", 2, new ConfigDescription("Scrap the bot spends to rebuild one broken prop (crates, consoles, furniture). 0 = it never rebuilds.", new AcceptableValueRange<int>(0, 50)));
            RebuildRange = cfg.Bind("EnemyWeapons", "RebuildRange", 4, new ConfigDescription("Tiles the scrap bot reaches for a broken prop.", new AcceptableValueRange<int>(1, 10)));
            KeepScrap = cfg.Bind("EnemyWeapons", "KeepScrap", 6, new ConfigDescription("Scrap the bot holds back for rebuilding when you open a vendor; the rest it sells.", new AcceptableValueRange<int>(0, 999)));
            ScrapPrice = cfg.Bind("EnemyWeapons", "ScrapPrice", 5, new ConfigDescription("Credits a vendor pays per scrap, times the floor number.", new AcceptableValueRange<int>(0, 500)));
            RepairCost = cfg.Bind("EnemyWeapons", "RepairCost", 1, new ConfigDescription("Scrap the bot welds onto itself when it drops below 60% health. 0 = it never repairs.", new AcceptableValueRange<int>(0, 50)));
            RepairPercent = cfg.Bind("EnemyWeapons", "RepairPercent", 25, new ConfigDescription("Percent of its health one repair restores.", new AcceptableValueRange<int>(1, 100)));
            BotArmor = cfg.Bind("EnemyWeapons", "BotArmor", 8, new ConfigDescription("The scrap bot's extra armour (plus 2 per floor).", new AcceptableValueRange<int>(0, 200)));
            BotHealth = cfg.Bind("EnemyWeapons", "BotHealth", 60, new ConfigDescription("The scrap bot's extra health (plus 10 per level of yours).", new AcceptableValueRange<int>(0, 2000)));
        }

        // ---- recipes ----------------------------------------------------------------------------------------

        internal class Hit
        {
            public string kind;   // debuff, hazard, buff, random, cleave
            public string id;
            public string[] pool;
            public int chance = 100, min, max;
            public float mod;
        }

        internal class Share { public string type; public float share; }

        internal class Recipe
        {
            public string id, baseId, enemy, name, description;
            public string look; // the weapon's path under the enemy's prefab: its mesh on the floor after a reload
            public bool boss;
            public List<Share> shares = new List<Share>();
            public List<Hit> hits = new List<Hit>();
        }

        private static Dictionary<string, Recipe> recipes;
        private static readonly Dictionary<string, Item> built = new Dictionary<string, Item>();
        private static GameObject holder;

        internal static string FilePath => Path.Combine(Paths.ConfigPath, FileName);

        private static Dictionary<string, Recipe> Recipes()
        {
            if (recipes != null) return recipes;
            try { recipes = File.Exists(FilePath) ? JsonConvert.DeserializeObject<Dictionary<string, Recipe>>(File.ReadAllText(FilePath)) : null; }
            catch (Exception e) { Plugin.Log.LogWarning("Enemy weapons: recipe file unreadable, starting fresh: " + e.Message); }
            return recipes = recipes ?? new Dictionary<string, Recipe>();
        }

        private static void Keep(Recipe r)
        {
            Recipes()[r.id] = r;
            try { File.WriteAllText(FilePath, JsonConvert.SerializeObject(recipes, Formatting.Indented)); }
            catch (Exception e) { Plugin.Log.LogWarning("Enemy weapons: could not save recipes: " + e.Message); }
        }

        internal static bool IsArm(Item i) => i is Item_Weapon && i.itemId != null && i.itemId.Contains(Mark);

        /// <summary>The template for an enemy-weapon or kit id (ItemRegistry asks for every SCCL_ id it doesn't know).</summary>
        internal static bool TryTemplate(string id, out Item tpl)
        {
            tpl = null;
            if (string.IsNullOrEmpty(id) || !id.Contains(Mark)) return false;
            if (built.TryGetValue(id, out tpl) && tpl != null) { ItemRegistry.Templates[id] = tpl; return true; }
            try
            {
                tpl = id == KitId ? ScrapBot.BuildKit(Holder()) :Recipes().TryGetValue(id, out Recipe r) ? Build(r) : null;
            }
            catch (Exception e) { Plugin.Log.LogError($"Enemy weapons: building {id}: {e}"); tpl = null; }
            if (tpl == null) return false;
            built[id] = tpl;
            ItemRegistry.Templates[id] = tpl;
            return true;
        }

        /// <summary>For tests: as after a restart, templates are rebuilt from the recipe file on the next ask.</summary>
        internal static void ForgetTemplates()
        {
            foreach (string k in built.Keys) ItemRegistry.Templates.Remove(k);
            built.Clear();
            recipes = null;
        }

        private static Transform Holder()
        {
            if (holder == null)
            {
                holder = new GameObject("SCClassLoader_EnemyWeapons");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
            }
            return holder.transform;
        }

        private static Item Build(Recipe r)
        {
            if (!(ItemRegistry.VanillaItem(r.baseId) is Item_Weapon src)) { Plugin.Log.LogWarning($"Enemy weapons: base {r.baseId} is gone"); return null; }
            GameObject go = Object.Instantiate(src.gameObject, Holder(), false);
            go.name = r.id;
            foreach (Item_AltInfo alt in go.GetComponentsInChildren<Item_AltInfo>(true)) Object.DestroyImmediate(alt);
            var w = go.GetComponent<Item_Weapon>();
            w.itemId = r.id;
            w.itemInstanceId = "";
            w.itemName = ClassRegistry.Term("ITEM_" + r.id, "Name", r.name);
            w.itemDescription = ClassRegistry.Term("ITEM_" + r.id, "Desc", r.description);
            w.name_prefix = default;
            w.name_affix = default;
            w.rarityApplied = false;
            w.damageList = Spread(Sum(src.damageList, false), Sum(src.damageList, true), r.shares, 1f);
            w.onHit = (w.onHit ?? new List<OnHit>()).Where(o => o != null).ToList();
            if (OnHitEffects.Value)
                foreach (Hit h in r.hits) { OnHit o = MakeHit(go, h); if (o != null) w.onHit.Add(o); }
            Transform piece = string.IsNullOrEmpty(r.look) ? null : AI_Manager.instance?.GetCharacter(r.enemy)?.transform.Find(r.look);
            if (piece != null) Dress(w, piece, false);
            return w;
        }

        private static int Sum(List<DmgObj> l, bool high) =>
            (l ?? new List<DmgObj>()).Where(d => d != null && d.damage > 0).Sum(d => high ? Math.Max(d.damage, d.damageMax) : d.damage);

        private static List<DmgObj> Spread(int lo, int hi, List<Share> shares, float pct) =>
            shares.Select(s =>
            {
                int a = Math.Max(1, Mathf.RoundToInt(lo * s.share * pct));
                int b = Mathf.RoundToInt(hi * s.share * pct);
                return new DmgObj(ClassRegistry.ParseEnum(s.type, DAMAGE_TYPE.Crushing, "enemy weapon"), a) { damageMax = b > a ? b : 0 };
            }).ToList();

        private static OnHit MakeHit(GameObject go, Hit h)
        {
            switch (h.kind)
            {
                case "debuff": var d = go.AddComponent<OnHit_Debuff>(); d.applyDebuff = h.id; d.applyChance = h.chance; d.chargeMinMax = new Vector2Int(h.min, h.max); return d;
                case "hazard": var z = go.AddComponent<OnHit_Hazard>(); z.hazard = h.id; z.hazardChance = h.chance; return z;
                case "buff": var b = go.AddComponent<OnHit_WielderBuff>(); b.effect = h.id; b.charges = h.max; b.chance = h.chance; return b;
                case "cleave": var c = go.AddComponent<OnHit_Cleave>(); c.triggerChance = h.chance; c.dmgMod = h.mod; return c;
                case "random":
                    Effect[] fx = (h.pool ?? new string[0]).Select(n => EffectManager.instance.GetEffect(n, false)).Where(e => e != null).ToArray();
                    if (fx.Length == 0) return null;
                    var rnd = go.AddComponent<OnHit_RandomEffect>(); rnd.effects = fx; rnd.applyChance = h.chance; rnd.chargeMinMax = new Vector2Int(h.min, h.max); return rnd;
            }
            return null;
        }

        /// <summary>An enemy's self-buff on hit, now for whoever holds the weapon (a held weapon has no Character above it).</summary>
        internal class OnHit_WielderBuff : OnHit
        {
            public string effect;
            public int charges = 1, chance = 100;

            public override void Trigger(Character c)
            {
                base.Trigger(c);
                Player p = Player.instance;
                if (p == null || p.GetIsDead() || string.IsNullOrEmpty(effect) || UnityEngine.Random.Range(1, 101) > chance) return;
                Effect have = p.effects.GetEffect(effect);
                if (have != null) { if (charges != 0) have.ModCharge(charges); }
                else EffectManager.instance.ApplyEffect(effect, p, Math.Max(1, charges));
            }
        }

        // ---- reading an enemy's attack ------------------------------------------------------------------------

        private static readonly string[] Generic = { "", "ability", "attack", "melee", "ranged", "shoot", "strike", "bite", "slash" };

        /// <summary>Its signature attack: the hardest-hitting active ability with real damage (a basic attack wins ties for regulars).</summary>
        private static Ability Signature(Character c, bool boss, bool? ranged)
        {
            var list = c.GetComponentsInChildren<Ability>(true)
                .Where(a => a != null && !a.isPassive && !(a is Ability_Player) && a.damageList != null && a.damageList.Any(d => d != null && d.damage > 0)).ToList();
            if (list.Count == 0) return null;
            float Avg(Ability a) => a.damageList.Where(d => d != null && d.damage > 0).Sum(d => (d.damage + Math.Max(d.damage, d.damageMax)) / 2f);
            if (ranged != null)
            {
                // the attack made with this weapon: a gun's shots, a blade's swings
                var made = list.Where(a => IsMelee(a) != ranged.Value).ToList();
                if (made.Count > 0) list = made;
            }
            if (!boss)
            {
                // what it carries: a gunman's gun, not the pistol-whip it falls back on up close
                var basic = list.Where(a => a is MA_Ranged).ToList();
                if (basic.Count == 0) basic = list.Where(a => a is MA_Melee).ToList();
                if (basic.Count > 0) list = basic;
            }
            return list.OrderByDescending(Avg).First();
        }

        private static bool IsMelee(Ability a) =>
            a is MA_Melee || a is MA_Melee_Flurry || a is MA_Charge || (a.isMelee && !a.isRanged) || (!a.isRanged && a.range > 0f && a.range <= 1.6f);

        private static List<Hit> HitsOf(Ability a)
        {
            var hits = new List<Hit>();
            Traverse t = Traverse.Create(a);
            string S(string f) => t.Field(f).FieldExists() ? t.Field(f).GetValue() as string : null;
            int I(string f, int def) => t.Field(f).FieldExists() && t.Field(f).GetValue() is int v ? v : def;
            void AddOnHit(OnHit o)
            {
                switch (o)
                {
                    case OnHit_Debuff d when !string.IsNullOrEmpty(d.applyDebuff): hits.Add(new Hit { kind = "debuff", id = d.applyDebuff, chance = d.applyChance, min = d.chargeMinMax.x, max = d.chargeMinMax.y }); break;
                    case OnHit_Hazard z when !string.IsNullOrEmpty(z.hazard): hits.Add(new Hit { kind = "hazard", id = z.hazard, chance = z.hazardChance }); break;
                    case OnHit_Buff b when b.applyEffect != null: hits.Add(new Hit { kind = "buff", id = b.applyEffect.internalName, max = b.chargeCount }); break;
                    case OnHit_Cleave c: hits.Add(new Hit { kind = "cleave", chance = c.triggerChance, mod = c.dmgMod }); break;
                    case OnHit_RandomEffect r when r.effects != null && r.effects.Length > 0:
                        hits.Add(new Hit { kind = "random", pool = r.effects.Where(e => e != null).Select(e => e.internalName).ToArray(), chance = r.applyChance, min = r.chargeMinMax.x, max = r.chargeMinMax.y }); break;
                }
            }
            foreach (FieldInfo f in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = f.GetValue(a);
                if (v is OnHit one && one != null) AddOnHit(one);
                else if (v is IEnumerable<OnHit> many) foreach (OnHit o in many) if (o != null) AddOnHit(o);
                else if (v is Hazard hz && hz != null && !string.IsNullOrEmpty(hz.internalName)) hits.Add(new Hit { kind = "hazard", id = hz.internalName, chance = 35 });
            }
            foreach (var (eff, ch) in new[] { ("applyEffect", "effectChance"), ("applyDebuff", "applyChance") })
            {
                string e = S(eff);
                if (!string.IsNullOrEmpty(e) && e.StartsWith("EFFECT")) hits.Add(new Hit { kind = "debuff", id = e, chance = I(ch, 100) });
            }
            string hzName = S("spawnHazard");
            if (!string.IsNullOrEmpty(hzName)) hits.Add(new Hit { kind = "hazard", id = hzName, chance = I("hazardChance", 100) });
            return hits.GroupBy(h => h.kind + "|" + h.id + "|" + string.Join(",", h.pool ?? new string[0])).Select(g => g.First()).ToList();
        }

        private static List<Share> SharesOf(Ability a)
        {
            var by = a.damageList.Where(d => d != null && d.damage > 0).GroupBy(d => d.dmgType)
                .Select(g => (type: g.Key, avg: g.Sum(d => (d.damage + Math.Max(d.damage, d.damageMax)) / 2f)))
                .OrderByDescending(x => x.avg).Take(2).ToList();
            float total = by.Sum(x => x.avg);
            return by.Select(x => new Share { type = x.type.ToString(), share = x.avg / total }).ToList();
        }

        /// <summary>A weapon from this floor's loot of the right kind; one with the attack's main damage type if there is one.</summary>
        private static Item_Weapon PickBase(bool melee, bool boss, DAMAGE_TYPE main)
        {
            LootManager lm = LootManager.instance;
            IEnumerable<Item_Weapon> Floor(int f) => (lm?.floorLootTables != null && f >= 0 && f < lm.floorLootTables.Length && lm.floorLootTables[f]?.tables != null
                ? lm.floorLootTables[f].tables.Where(t => t != null && t.tableType == LOOT_TYPE.Weapon && t.items != null).SelectMany(t => t.items)
                : Enumerable.Empty<Item>()).OfType<Item_Weapon>().Where(w => w != null && w.itemId != null && !w.itemId.Contains(ItemRegistry.Marker));
            int floor = lm?.floorLootTables == null ? 0 : Mathf.Clamp(Game.FLOOR, 0, lm.floorLootTables.Length - 1);
            var pool = Floor(floor).ToList();
            if (pool.Count == 0 && lm?.floorLootTables != null) pool = Enumerable.Range(0, lm.floorLootTables.Length).SelectMany(Floor).ToList();
            WEAPON_TYPE[] order = melee ? (boss ? new[] { WEAPON_TYPE.HeavyMelee, WEAPON_TYPE.Melee } : new[] { WEAPON_TYPE.Melee, WEAPON_TYPE.HeavyMelee })
                                        : (boss ? new[] { WEAPON_TYPE.HeavyRanged, WEAPON_TYPE.Ranged } : new[] { WEAPON_TYPE.Ranged, WEAPON_TYPE.HeavyRanged });
            foreach (WEAPON_TYPE wt in order)
            {
                var kind = pool.Where(w => w.weaponType == wt).Distinct().ToList();
                if (kind.Count == 0) continue;
                var typed = kind.Where(w => w.HasDamageType(main)).ToList();
                var from = typed.Count > 0 ? typed : kind;
                return from[UnityEngine.Random.Range(0, from.Count)];
            }
            return null;
        }

        private static string Strip(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<.*?>", "").Trim();

        private static string Describe(Recipe r, string enemy, string attack, bool ranged)
        {
            string types = string.Join(" and ", r.shares.Select(s => s.type));
            string text = $"Dropped by {(r.boss ? enemy : "a " + enemy)} as it died. It deals the same {types} damage";
            text += string.IsNullOrEmpty(attack) ? " it did." : $" as its {attack}.";
            var lines = new List<string>();
            foreach (Hit h in r.hits)
                switch (h.kind)
                {
                    case "debuff": lines.Add($"{h.chance}% to inflict {Runes.EffectName(h.id, null)}"); break;
                    case "hazard": lines.Add($"{h.chance}% to leave {h.id.Replace("HAZARD_", "").Replace('_', ' ')} under the target"); break;
                    case "buff": lines.Add($"gives you {Runes.EffectName(h.id, null)}"); break;
                    case "cleave": lines.Add($"{h.chance}% to cleave for {Mathf.RoundToInt(h.mod * 100f)}% damage"); break;
                    case "random": lines.Add($"{h.chance}% to inflict one of {string.Join(", ", h.pool.Select(p => Runes.EffectName(p, null)))}"); break;
                }
            if (lines.Count > 0) text += " On a hit: " + string.Join("; ", lines) + ".";
            text += ranged
                ? " It is still in the low-power mode its owner fought with: weaker than a tuned gun, but it needs no ammo."
                : $" It is still in the low-power mode its owner fought with: weaker than a tuned weapon, but your skills cost {MeleeSaving.Value}% less stamina while you swing it.";
            return text;
        }

        /// <summary>The recipe for this weapon of this enemy, saved for later loads.</summary>
        private static Recipe RecipeFor(Character c, bool boss, Arm arm)
        {
            Ability a = Signature(c, boss, arm.ranged);
            if (a == null) return null;
            var shares = SharesOf(a);
            if (shares.Count == 0) return null;
            bool ranged = arm.ranged ?? !IsMelee(a);
            Item_Weapon b = (arm.baseId != null ? ItemRegistry.VanillaItem(arm.baseId) as Item_Weapon : null)
                ?? PickBase(!ranged, boss, ClassRegistry.ParseEnum(shares[0].type, DAMAGE_TYPE.Crushing, "enemy weapon"));
            if (b == null) return null;
            string id = Prefix(b.itemId) + Mark + ModPacks.Sanitize(b.itemId) + "__" + ModPacks.Sanitize(c.internalName);
            if (Recipes().TryGetValue(id, out Recipe known) && known.look == arm.path) return known;
            string enemy = Strip(c.displayName);
            if (enemy.Length == 0) enemy = c.internalName;
            bool beast = c.characterType == CHARACTER_TYPE.Monster || c.characterType == CHARACTER_TYPE.Supernatural;
            string baseName = ItemRegistry.NameOf(b.itemId);
            string attack = Strip(a.displayName);
            if (Generic.Contains(attack.ToLowerInvariant()) || attack.Equals(baseName, StringComparison.OrdinalIgnoreCase)) attack = "";
            var r = new Recipe
            {
                id = id, baseId = b.itemId, enemy = c.internalName, boss = boss, shares = shares, hits = HitsOf(a), look = arm.path,
                name = beast ? $"{baseName} of {enemy}" : $"{enemy}'s {baseName}",
            };
            r.description = Describe(r, enemy, attack, b.weaponType == WEAPON_TYPE.Ranged || b.weaponType == WEAPON_TYPE.HeavyRanged);
            Keep(r);
            Plugin.Log.LogInfo($"Enemy weapons: new recipe {id} ({r.name}; {string.Join("+", shares.Select(s => s.type))}; {r.hits.Count} on-hit) from {a.GetType().Name}");
            return r;
        }

        /// <summary>"WEAPON_M_" from "WEAPON_M_Energon_Blade": the family the game sorts weapons by.</summary>
        private static string Prefix(string baseId)
        {
            var fam = new List<string>();
            foreach (string part in baseId.Split('_')) { if (part.Length == 0 || part.Any(char.IsLower)) break; fam.Add(part); }
            return fam.Count > 0 ? string.Join("_", fam) + "_" : "WEAPON_";
        }

        /// <summary>
        /// A rolled drop: a weapon of the same kind from this floor's loot is rolled like floor loot (level, rarity,
        /// rarity mods), then poured into the enemy-weapon template through the same save data a reload uses, and its
        /// damage turned into the enemy's types at the low-power share.
        /// </summary>
        internal static Item_Weapon Make(Recipe r)
        {
            if (!TryTemplate(r.id, out Item tpl) || !(ItemRegistry.VanillaItem(r.baseId) is Item_Weapon look)) return null;
            bool melee = look.weaponType == WEAPON_TYPE.Melee || look.weaponType == WEAPON_TYPE.HeavyMelee;
            Item_Weapon src = PickBase(melee, look.weaponType == WEAPON_TYPE.HeavyMelee || look.weaponType == WEAPON_TYPE.HeavyRanged,
                ClassRegistry.ParseEnum(r.shares[0].type, DAMAGE_TYPE.Crushing, "enemy weapon")) ?? look;
            Item_Weapon roll = Object.Instantiate(src);
            try
            {
                LootManager.instance.RarityCheck(roll, r.boss ? 1f : 0f, Game.FLOOR + 1);
                if (r.boss && roll.rarity < RARITY.Epic) roll.OverrideRarity(RARITY.Epic);
                roll.Rarify();
                var save = (ItemSaveInfo)roll.Serialize();
                save.itemId = r.id;
                save.itemName = tpl.itemName;
                var w = (Item_Weapon)Object.Instantiate(tpl);
                w.Deserialize(save);
                w.damageList = Spread(Sum(roll.damageList, false), Sum(roll.damageList, true), r.shares, DamagePercent.Value / 100f);
                if (r.boss) w.SetTag(BossTag, 1);
                w.GenerateItemValue();
                return w;
            }
            finally { Object.Destroy(roll.gameObject); }
        }

        // ---- dropping ---------------------------------------------------------------------------------------

        private static bool Eligible(Character c, out bool boss)
        {
            boss = false;
            if (c == null || c == Player.instance || c.ai == null || c.GetIsDead() || c.FLAG_silentDeath || Game.Game_isLoading) return false;
            if (c.ai.GetTeam() != AI.AI_TEAM.Hostile || c.HasTag("MINION") || c.HasTag("CLONE") || c.HasTag("NOXP") || c.HasTag(BotTag)) return false;
            boss = Elites.IsBoss(c);
            return true;
        }

        // ---- what it holds ----------------------------------------------------------------------------------

        internal class Arm
        {
            public Transform root;
            public string path, baseId;
            public bool? ranged; // null: a bare "Weapon_L" mount; its attack decides
            public bool loose;   // the game lets this one fall by itself (OnDeath_Detach)
            public bool stowed;  // carried but not in hand when it died
        }

        // what a held thing is, from its node and mesh names; first match wins, so the specific words come first
        private static readonly (string word, string baseId, bool ranged)[] Kinds =
        {
            ("injector", "WEAPON_R_ChemInjector", true), ("skorp", "WEAPON_R_Skorpio", true), ("flamer", "WEAPON_HR_Incineron_Thrower", true),
            ("pistol", "WEAPON_R_Street_Special", true), ("shotgun", "WEAPON_HR_Shotgun", true), ("sharpshooter", "WEAPON_HR_Salty", true),
            ("rifle", "WEAPON_HR_Merc_Rifle", true), ("warhammer", "WEAPON_HM_Warhammer", false), ("hammer", "WEAPON_HM_Kinetic_Hammer", false),
            ("wrench", "WEAPON_HM_Pneumatowrench", false), ("baton", "WEAPON_M_Carbonite_Baton", false), ("cleaver", "WEAPON_HM_CarboniteCleaver", false),
            ("usamad", "WEAPON_HM_Usamad", false), ("chainsaw", "WEAPON_HM_Usamad", false), ("katana", "WEAPON_HM_DaiKatana", false),
            ("smornblade", "WEAPON_HM_DaiKatana", false), ("knife", "WEAPON_M_Concealed_Shiv", false), ("blade", "WEAPON_M_Carbonite_Blade", false),
        };
        // things that fall or are held but are no weapon: heads, mugs, briefcases, a bot's own claws, thrown grenades
        private static readonly string[] NotArms = { "head", "mug", "cup", "briefcase", "claw", "loaderarm", "grenade", "bomb", "mop", "drill", "scancone", "hologram", "ps_" };

        private static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static bool Shown(Transform t) => t.GetComponentsInChildren<Renderer>(false).Any(r => r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer));

        private static bool Kind(Transform t, out string baseId, out bool? ranged, bool self = false)
        {
            baseId = null; ranged = null;
            string own = t.name.ToLowerInvariant();
            if (NotArms.Any(own.Contains)) return false;
            if (self)
            {
                // a held weapon is named for what it is (the bone that holds it is not): Smornblade, not clavicle_r
                string me = (own + " " + (t.GetComponent<MeshFilter>()?.sharedMesh?.name ?? "")).ToLowerInvariant();
                if (!own.StartsWith("weapon") && !Kinds.Any(k => me.Contains(k.word))) return false;
            }
            for (Transform p = t.parent; p != null; p = p.parent) if (p.name.ToLowerInvariant().Contains("wing")) return false; // a flyer's wing blades
            string all = string.Join(" ", t.GetComponentsInChildren<Transform>(true).Select(x => x.name)
                .Concat(t.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null).Select(m => m.sharedMesh.name))).ToLowerInvariant();
            foreach (var k in Kinds)
                if (all.Contains(k.word)) { baseId = k.baseId; ranged = k.ranged; return true; }
            return own.StartsWith("weapon");
        }

        /// <summary>The weapons in its hands (at most two): the ones the game drops, then the ones it keeps welded on.</summary>
        internal static List<Arm> ArmsOf(Character c)
        {
            var arms = new List<Arm>();
            var detach = c.GetComponentsInChildren<OnDeath_Detach>(true).Where(d => d != null && d.detach && d.GetComponent<Rigidbody>() != null).ToList();
            foreach (OnDeath_Detach d in detach)
                if (Kind(d.transform, out string b, out bool? rg) && d.GetComponentsInChildren<Renderer>(true).Any(r => r is MeshRenderer || r is SkinnedMeshRenderer))
                    arms.Add(new Arm { root = d.transform, path = PathOf(d.transform, c.transform), baseId = b, ranged = rg, loose = true });
            // in hand first, then stowed: a weapon it only draws to attack (a ChefBot's cleaver) is still in its kit, and
            // drops all the same; a stowed copy of one it already holds (a holstered pose of the same gun) does not
            var all = c.GetComponentsInChildren<Transform>(true).Where(t => t != c.transform).ToList();
            foreach (bool inHand in new[] { true, false })
                foreach (Transform t in all)
                {
                    if (arms.Count >= 2) break;
                    if (Shown(t) != inHand || arms.Any(a => t.IsChildOf(a.root) || a.root.IsChildOf(t))) continue;
                    if (detach.Any(d => d.transform.IsChildOf(t) || t.IsChildOf(d.transform))) continue;
                    if (!Kind(t, out string b, out bool? rg, self: true) || !t.GetComponentsInChildren<Renderer>(true).Any(r => r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                    if (!inHand && arms.Any(a => a.baseId == b)) continue;
                    arms.Add(new Arm { root = t, path = PathOf(t, c.transform), baseId = b, ranged = rg, stowed = !inHand });
                }
            return arms.Take(2).ToList();
        }

        /// <summary>A weapon the body would keep: it slips out of the hand and falls like the ones the game drops.</summary>
        private static void LetGo(Transform t)
        {
            t.SetParent(Map.instance.transform, true);
            t.gameObject.SetActive(true); // a stowed one comes out of the kit
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true)) if (r is MeshRenderer || r is SkinnedMeshRenderer) r.enabled = true;
            foreach (Animator an in t.GetComponentsInChildren<Animator>(true)) an.enabled = false;
            Rigidbody rb = t.GetComponent<Rigidbody>() ?? t.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = true;
            if (t.GetComponent<Collider>() == null)
            {
                var box = t.gameObject.AddComponent<BoxCollider>();
                if (LocalBounds(t, out Bounds b)) { box.center = b.center; box.size = Vector3.Max(b.size, Vector3.one * 0.03f); }
            }
            foreach (Collider col in t.GetComponentsInChildren<Collider>(true)) col.enabled = true;
            t.gameObject.layer = 0;
        }

        private static bool LocalBounds(Transform t, out Bounds b)
        {
            b = default;
            bool any = false;
            Matrix4x4 inv = t.worldToLocalMatrix;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                Mesh m = MeshOf(r);
                if (m == null) continue;
                Matrix4x4 to = inv * r.transform.localToWorldMatrix;
                foreach (Vector3 p in Corners(m.bounds)) { Vector3 q = to.MultiplyPoint3x4(p); if (!any) { b = new Bounds(q, Vector3.zero); any = true; } else b.Encapsulate(q); }
            }
            return any;
        }

        private static Mesh MeshOf(Renderer r) => r is SkinnedMeshRenderer s ? s.sharedMesh : r is MeshRenderer ? r.GetComponent<MeshFilter>()?.sharedMesh : null;

        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++) yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
        }

        private static float NZ(float v) => Mathf.Abs(v) < 0.0001f ? 1f : v;

        /// <summary>
        /// The item's look on the floor: the enemy's own weapon mesh in place of the base weapon's. Fallen = in the exact
        /// pose it landed in; otherwise (a template, after a reload) lying flat with its longest side along the floor.
        /// </summary>
        internal static void Dress(Item it, Transform src, bool fallen)
        {
            GameObject target = it.worldObject != null ? it.worldObject : it.gameObject;
            var parts = new List<(Mesh mesh, Material[] mats, Matrix4x4 m)>();
            Matrix4x4 inv = src.worldToLocalMatrix;
            foreach (Renderer rd in src.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = MeshOf(rd);
                if (mesh == null || (fallen && !rd.enabled)) continue;
                parts.Add((mesh, rd.sharedMaterials, inv * rd.transform.localToWorldMatrix));
            }
            if (parts.Count == 0) return;
            Transform old = target.transform.Find("SCCL_Model");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            foreach (Renderer rd in target.GetComponentsInChildren<Renderer>(true)) if (!(rd is ParticleSystemRenderer)) rd.enabled = false;
            var root = new GameObject("SCCL_Model").transform;
            root.gameObject.layer = target.layer;
            root.SetParent(target.transform, false);
            foreach (var (mesh, mats, m) in parts)
            {
                var go = new GameObject(mesh.name) { layer = target.layer };
                go.transform.SetParent(root, false);
                go.transform.localPosition = m.GetColumn(3);
                go.transform.localRotation = m.rotation;
                go.transform.localScale = m.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
            Vector3 ls = target.transform.lossyScale, ss = src.lossyScale;
            root.localScale = new Vector3(ss.x / NZ(ls.x), ss.y / NZ(ls.y), ss.z / NZ(ls.z));
            if (fallen) { root.SetPositionAndRotation(src.position, src.rotation); return; }
            // flat on the floor, centred on the item
            Bounds World()
            {
                var w = new Bounds(root.position, Vector3.zero);
                bool first = true;
                foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                    foreach (Vector3 p in Corners(mf.sharedMesh.bounds)) { Vector3 q = mf.transform.localToWorldMatrix.MultiplyPoint3x4(p); if (first) { w = new Bounds(q, Vector3.zero); first = false; } else w.Encapsulate(q); }
                return w;
            }
            root.rotation = Quaternion.identity;
            Bounds b = World();
            if (b.size.y > b.size.x && b.size.y > b.size.z) { root.rotation = Quaternion.Euler(0f, 0f, 90f); b = World(); }
            Vector3 at = it.transform.position;
            root.position += new Vector3(at.x - b.center.x, at.y + 0.01f - b.min.y, at.z - b.center.z);
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_Die
        {
            private static void Prefix(Character __instance)
            {
                if (!Enabled.Value) return;
                try
                {
                    if (!Eligible(__instance, out bool boss)) return;
                    NavNode n = __instance.GetMover()?.GetCurrentNavNode();
                    if (n == null) return;
                    foreach (Arm arm in ArmsOf(__instance))
                    {
                        Recipe r = RecipeFor(__instance, boss, arm);
                        if (r == null) continue;
                        if (!arm.loose) LetGo(arm.root);
                        Plugin.Instance.StartCoroutine(Follow(r, arm.root, n));
                    }
                }
                catch (Exception e) { Plugin.Log.LogError("Enemy weapons: " + e); }
            }
        }

        /// <summary>For tests: the last weapon that hit the floor.</summary>
        internal static Item_Weapon LastDrop;

        /// <summary>Waits for the falling weapon to come to rest, then it becomes the item, right where it lies.</summary>
        private static IEnumerator Follow(Recipe r, Transform piece, NavNode died)
        {
            yield return new WaitForSeconds(0.5f);
            Rigidbody rb = piece != null ? piece.GetComponent<Rigidbody>() : null;
            for (float t = 0f; t < 3f && piece != null && rb != null && !rb.isKinematic && rb.velocity.sqrMagnitude > 0.0025f; t += Time.deltaTime) yield return null;
            if (Map.instance == null || died == null) yield break;
            Vector3 at = piece != null && LocalBounds(piece, out Bounds lb) ? piece.TransformPoint(lb.center) : died.transform.position;
            NavNode n = Map.instance.GetNearestNavNodeAt(at, 1.2f, true);
            bool lost = piece == null || n == null || at.y < died.transform.position.y - 0.5f; // rolled off or fell through: it lies where its owner died
            if (n == null) n = died;
            Item_Weapon w = null;
            try { w = Make(r); }
            catch (Exception e) { Plugin.Log.LogError($"Enemy weapons: making {r.id}: {e}"); }
            if (w == null) yield break;
            w.OnEnterWorld();
            LootManager.instance.SprinkleLootDust(w);
            Map.instance.AddItem(w);
            w.transform.position = lost ? n.transform.position : new Vector3(at.x, n.transform.position.y, at.z);
            if (!lost) Dress(w, piece, true);
            if (piece != null) Object.Destroy(piece.gameObject);
            LastDrop = w;
            Plugin.Instance.StartCoroutine(SCClassLoader.ScrapBot.Settled(w));
            if (r.boss) Game.LOG($"<color=#C79750>{Strip(w.GetName())}</color> clatters to the floor.");
            if (BotOn.Value && EventBus.On && Player.instance != null && !Player.instance.HasTag(OwnedTag) && !Player.instance.HasTag(KitGivenTag)
                && TryTemplate(KitId, out Item kitTpl))
            {
                Player.instance.SetTag(KitGivenTag, 1);
                yield return new WaitForSeconds(0.1f);
                Spew(Object.Instantiate(kitTpl), died.transform.position, died.GetRoom());
                Game.LOG("Among its gear: a <color=#C79750>Scrap Bot Kit</color>.");
            }
        }

        // ---- the low-power mode -------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Item_Weapon), nameof(Item_Weapon.GetAmmoUse))]
        private static class Patch_Ammo
        {
            private static void Postfix(Item_Weapon __instance, ref int __result) { if (IsArm(__instance)) __result = 0; }
        }

        private static bool SwingsArm()
        {
            Player p = Player.instance;
            bool M(Item_Weapon w) => w != null && IsArm(w) && (w.weaponType == WEAPON_TYPE.Melee || w.weaponType == WEAPON_TYPE.HeavyMelee);
            return p != null && (M(p.GetMainhandWeapon()) || M(p.GetOffhandWeapon()));
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ModStamina))]
        private static class Patch_Stamina
        {
            private static void Prefix(ref int s)
            {
                if (s >= 0 || MeleeSaving.Value <= 0 || !Game.RESOLVING || Game.PLAYER_ABILITY == null || Game.PLAYER_ABILITY is PA_AutoAttack || !SwingsArm()) return;
                s = -Mathf.RoundToInt(-s * (100 - MeleeSaving.Value) / 100f);
            }
        }

        private static void Spew(Item it, Vector3 pos, Room room)
        {
            it.OnEnterWorld();
            LootManager.instance.SprinkleLootDust(it);
            Map.instance.AddItem(it);
            it.transform.position = pos;
            LootManager.instance.DoLootSpew(it.transform.position, it, 0.5f, room, animateRotation: true, null, 0.3f);
        }
    }
}
