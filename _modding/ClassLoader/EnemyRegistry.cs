using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>One enemy from a mod's "enemies" list.</summary>
    public class EnemyDef
    {
        public string id;
        /// <summary>Movement habits after its own turn: kite, retreat, guard, hitAndRun (see Behaviors).</summary>
        public List<BehaviorSpec> behaviors;
        /// <summary>Vanilla enemy internal name (see vanilla_reference.txt), or another mod enemy.</summary>
        [JsonProperty("base")] public string baseId;
        public string name;
        public string subtitle;
        public string description;
        /// <summary>Mechanical | Humanoid | Monster | Supernatural.</summary>
        public string type;
        public Dictionary<string, int> tags;
        public List<string> removeTags;

        /// <summary>Multipliers on the base enemy's per-floor stats.</summary>
        public float health = 1f;
        public float damage = 1f;
        public float xp = 1f;
        /// <summary>Added to the per-floor stats, e.g. {"Dodge": 10, "Armor": 2}.</summary>
        public Dictionary<string, int> stats;
        /// <summary>Replaces the resistances.</summary>
        public Dictionary<string, int> resists;
        /// <summary>Brawler | Skirmisher | Sniper.</summary>
        public string ai;
        /// <summary>Model size (1 = normal).</summary>
        public float scale = 1f;
        /// <summary>For enemies with looks variants: always use this one (0-based).</summary>
        public int? variant;
        /// <summary>A new look: a mesh (.glb/.obj) or a texture next to the mod's files (see ModelSpec).</summary>
        public ModelSpec model;

        public List<MobAbilityDef> abilities;
        /// <summary>Brand-new skills (see MobSkillSpec): the class action system, used by this enemy.</summary>
        public List<MobSkillSpec> skills;
        public List<string> removeAbilities;
        public EnemyLootDef loot;
        public List<EnemySpawnDef> spawn;
        public bool enabled = true;

        /// <summary>Creatures only: how it fights (brawler, skirmisher, sniper, hover). The loader picks and strips a body
        /// for it; a creature never names a base.</summary>
        public string role;
        /// <summary>Creatures only, as a summon: max health gained per level, and points of scaleStat per point of pet bonus.</summary>
        public float healthPerLevel = 12f;
        public int smartsPerBonus = 6;
        /// <summary>Creatures only, as a summon: the owner stat it grows with (default Smarts). Any other stat is also
        /// lent to the summon in full, so its skills with that "power" hit harder as the owner's stat grows.</summary>
        public string scaleStat;

        [JsonIgnore] public bool creature;
        [JsonIgnore] public ModPack pack;
        [JsonIgnore] public string file;
        [JsonIgnore] public string qualified;
    }

    public class MobAbilityDef
    {
        /// <summary>"ENEMY_INTERNALNAME/ABILITY_INTERNALNAME": copy that enemy's ability; or just an ability name already on this enemy, to retune it.</summary>
        public string from;
        public int? cooldown;
        public float? range;
        public int? priority;
        public int? staminaCost;
        public List<DamageDef> damage;
    }

    public class EnemyLootDef
    {
        /// <summary>Always dropped: mod items ("Mod:Id" / "Id") or vanilla ids.</summary>
        public List<string> items;
        /// <summary>Rolled drops: [{"type": "Gear", "rarity": "Rare"}].</summary>
        public List<RandomLootDef> random;
        /// <summary>true: drop only these; false (default): add to the base enemy's drops.</summary>
        public bool replace;
    }

    public class RandomLootDef
    {
        public string type = "Random";
        public string rarity = "None";
    }

    public class EnemySpawnDef
    {
        /// <summary>[min, max] floors (0 = first). Default: all.</summary>
        public int[] floors;
        /// <summary>Without "replaces": % of ordinary hostile spawns on those floors that become this enemy.
        /// With "replaces": % of that vanilla enemy's spawns that become this one instead.</summary>
        public float chance = 10f;
        public string replaces;
    }

    /// <summary>
    /// Mod enemies: clones of vanilla enemy prefabs, found by saves through AI_Manager.GetCharacter, and mixed into
    /// the hostile spawn points of the floors they are set for.
    /// </summary>
    internal static class EnemyRegistry
    {
        internal const string Prefix = "SCCLMOB_";
        internal static readonly Dictionary<string, Character> Templates = new Dictionary<string, Character>();
        internal static readonly Dictionary<string, EnemyDef> Defs = new Dictionary<string, EnemyDef>();
        internal static readonly Dictionary<string, string> Ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static GameObject holder;
        private static List<EnemyDef> pending;

        internal static void Register()
        {
            if (holder != null) Object.Destroy(holder);
            holder = new GameObject("SCClassLoader_Enemies");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            Templates.Clear();
            Defs.Clear();
            Ids.Clear();
            if (AI_Manager.instance?.allCharacters == null) { Plugin.Log.LogError("Enemies: no AI_Manager yet"); return; }

            var merged = new Dictionary<string, (JObject obj, ModPack pack, string file)>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (ModPack p in ModPacks.Ordered)
                foreach (var (file, obj) in p.data)
                    foreach (JObject raw in (obj["enemies"] as JArray ?? new JArray()).OfType<JObject>()
                                 .Concat((obj["creatures"] as JArray ?? new JArray()).OfType<JObject>().Select(AsCreature)))
                    {
                        JObject e = raw;
                        string id = (string)e["id"];
                        if (string.IsNullOrEmpty(id)) { Plugin.Log.LogError($"{file}: an enemy has no \"id\""); continue; }
                        string q = ModPacks.Qualify(p, id);
                        if (merged.TryGetValue(q, out var prev))
                        {
                            var patched = (JObject)prev.obj.DeepClone();
                            patched.Merge(e, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                            merged[q] = (patched, prev.pack, prev.file);
                        }
                        else { merged[q] = (e, p, file); order.Add(q); }
                    }

            pending = new List<EnemyDef>();
            foreach (string q in order)
            {
                var (obj, pack, file) = merged[q];
                try
                {
                    EnemyDef d = obj.ToObject<EnemyDef>(new JsonSerializer { ObjectCreationHandling = ObjectCreationHandling.Replace });
                    if (!d.enabled) continue;
                    d.creature = (bool?)obj["_creature"] == true;
                    d.pack = pack; d.file = file; d.qualified = q;
                    pending.Add(d);
                }
                catch (Exception e) { Plugin.Log.LogError($"{file}: enemy '{q}' could not be read: {e.Message}"); }
            }
            foreach (EnemyDef d in pending.ToList())
            {
                try { Build(d, new HashSet<string>()); }
                catch (Exception e) { Plugin.Log.LogError($"{d.file}: enemy '{d.qualified}' failed: {e}"); }
            }
            pending = null;
            if (Templates.Count > 0) Plugin.Log.LogInfo($"Enemies: registered {Templates.Count} mod enemy type(s)");
        }

        /// <summary>The loader's own bodies, one per role: a creature is stamped from one of these with every borrowed
        /// look, ability and drop stripped off, so it is only what its data says.</summary>
        private static readonly Dictionary<string, (string body, string ai)> Roles = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["brawler"] = ("CORPSEC_GUARD_MELEE", "Brawler"),
            ["skirmisher"] = ("CORPSEC_GUARD_PISTOL", "Skirmisher"),
            ["sniper"] = ("CORPSEC_SHARPSHOOTER", "Sniper"),
            ["hover"] = ("CORPSEC_DRONE", "Skirmisher"),
        };

        /// <summary>A "creatures" entry as an enemy entry: its role's body, no borrowed abilities or drops, the model's
        /// own particles only.</summary>
        private static JObject AsCreature(JObject src)
        {
            var e = (JObject)src.DeepClone();
            string role = (string)e["role"] ?? "brawler";
            if (!Roles.TryGetValue(role, out var r)) { Plugin.Log.LogError($"creature '{(string)e["id"]}': unknown role '{role}' (brawler, skirmisher, sniper, hover)"); r = Roles["brawler"]; }
            e["base"] = r.body;
            if (e["ai"] == null) e["ai"] = r.ai;
            e["removeAbilities"] = new JArray("*");
            if (e["loot"] == null) e["loot"] = new JObject { ["replace"] = true };
            if (e["model"] is JObject m && m["keepParticles"] == null) m["keepParticles"] = false;
            e["_creature"] = true;
            return e;
        }

        /// <summary>A creature by its id ("PaintedGuard" or "Pack:PaintedGuard"); null if none.</summary>
        internal static Character Creature(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var kv in Ids)
                if ((string.Equals(kv.Key, id, StringComparison.OrdinalIgnoreCase) || kv.Key.EndsWith(":" + id, StringComparison.OrdinalIgnoreCase))
                    && Defs.TryGetValue(kv.Value, out EnemyDef d) && d.creature && Templates.TryGetValue(kv.Value, out Character t)) return t;
            return null;
        }

        internal static EnemyDef DefOf(Character c) => c != null && Defs.TryGetValue(c.internalName ?? "", out EnemyDef d) ? d : null;

        /// <summary>A vanilla enemy id, a mod enemy's internal name, or a mod enemy as "Pack:Id".</summary>
        internal static Character Resolve(string id) =>
            string.IsNullOrEmpty(id) ? null : Ids.TryGetValue(id, out string mid) && Templates.TryGetValue(mid, out Character t) ? t
            : Templates.TryGetValue(id, out Character byName) ? byName : AI_Manager.instance?.GetCharacter(id);

        internal static Character VanillaEnemy(string internalName) =>
            AI_Manager.instance.allCharacters.FirstOrDefault(c => c != null && c.internalName == internalName);

        private static Character Build(EnemyDef d, HashSet<string> visiting)
        {
            if (Ids.TryGetValue(d.qualified, out string done)) return Templates[done];
            if (!visiting.Add(d.qualified)) { Plugin.Log.LogError($"{d.file}: enemy '{d.qualified}' has a base loop"); return null; }

            Character src = null;
            if (!string.IsNullOrEmpty(d.baseId))
            {
                string bq = ModPacks.Qualify(d.pack, d.baseId);
                EnemyDef baseDef = pending?.FirstOrDefault(x => string.Equals(x.qualified, bq, StringComparison.OrdinalIgnoreCase))
                                   ?? pending?.FirstOrDefault(x => x.qualified.EndsWith(":" + d.baseId, StringComparison.OrdinalIgnoreCase));
                src = baseDef != null ? Build(baseDef, visiting) : VanillaEnemy(d.baseId);
            }
            if (src == null) { Plugin.Log.LogError($"{d.file}: enemy '{d.qualified}': base '{d.baseId}' not found (see vanilla_reference.txt)"); return null; }

            string internalName = Prefix + d.pack.id + "_" + ModPacks.Sanitize(d.id);
            GameObject go = Object.Instantiate(src.gameObject, holder.transform, false);
            go.name = internalName;
            Character c = go.GetComponent<Character>();
            c.internalName = internalName;
            string ctx = d.qualified;
            string termId = "MOB_" + internalName;

            if (d.name != null) { c.displayName = ClassRegistry.Term(termId, "Name", d.name); c.nameOverride = ""; }
            if (d.subtitle != null) c.subTitle = ClassRegistry.Term(termId, "Sub", d.subtitle);
            if (d.description != null) c.displayDescription = ClassRegistry.Term(termId, "Desc", d.description);
            if (d.type != null) c.characterType = ClassRegistry.ParseEnum(d.type, c.characterType, ctx);
            if (d.ai != null && c.ai != null) c.ai.aiStyle = ClassRegistry.ParseEnum(d.ai, c.ai.aiStyle, ctx);

            // Plain enemies only: a rare/elite tag would send them down the rare-spawn path, which looks them up by name.
            c.tags.RemoveAll(t => t != null && (t.tagName == "RARE" || t.tagName == "RARE_ELITE"));
            foreach (string t in d.removeTags ?? new List<string>()) c.tags.RemoveAll(x => x != null && x.tagName == t);
            foreach (var kv in d.tags ?? new Dictionary<string, int>()) { c.tags.RemoveAll(x => x != null && x.tagName == kv.Key); c.tags.Add(new Tag(kv.Key, kv.Value)); }
            if (d.variant.HasValue) { c.tags.RemoveAll(x => x != null && x.tagName == "VARIANT"); c.tags.Add(new Tag("VARIANT", d.variant.Value)); }
            // Built into the clone, so every spawn and every load of this enemy has it.
            if (d.model != null && ModelAssets.Apply(go, d.model, System.IO.Path.GetDirectoryName(d.file) ?? d.pack.folder))
            {
                Plugin.Log.LogInfo($"{ctx}: model {d.model.mesh ?? d.model.texture}");
                if (!string.IsNullOrEmpty(d.model.mesh)) c.ragdoll = null; // the base body's ragdoll would fall out of the new one; it plays its own death
            }

            ScaleStats(c, d, ctx);
            if (d.resists != null)
                c.resistances = d.resists.Select(kv => new DmgObj(ClassRegistry.ParseEnum(kv.Key, DAMAGE_TYPE.Null, ctx), kv.Value)).Where(r => r.dmgType != DAMAGE_TYPE.Null).ToList();
            if (d.scale != 1f && c.ai != null && c.ai.model != null) c.ai.model.localScale *= d.scale;

            Abilities(go, c, d, ctx);
            Loot(go, d, ctx);

            Templates[internalName] = c;
            Defs[internalName] = d;
            Ids[d.qualified] = internalName;
            visiting.Remove(d.qualified);
            if (d.creature && go.GetComponents<Ability>().Length == 0) Plugin.Log.LogError($"{ctx}: a creature needs at least one skill to fight with");
            Plugin.Log.LogInfo($"{(d.creature ? "Creatures" : "Enemies")}: {d.qualified} -> {internalName} ({(d.creature ? "role " + (d.role ?? "brawler") : "base " + src.internalName)}), abilities [{string.Join(", ", go.GetComponents<Ability>().Select(a => a.internalName))}]");
            return c;
        }

        private static void ScaleStats(Character c, EnemyDef d, string ctx)
        {
            var add = (d.stats ?? new Dictionary<string, int>()).Select(kv => (ClassRegistry.ParseEnum(kv.Key, STAT_TYPE.Null, ctx), kv.Value)).Where(x => x.Item1 != STAT_TYPE.Null).ToList();
            void AddStats(StatObj[] arr)
            {
                foreach (var (type, amount) in add)
                    foreach (StatObj s in arr ?? new StatObj[0])
                        if (s != null && s.statType == type) s.amount += amount;
            }
            c.healthMax = Mathf.Max(1, Mathf.RoundToInt(c.healthMax * d.health));
            c.xpValue = Mathf.RoundToInt(c.xpValue * d.xp);
            AddStats(c.primaryAttributes);
            AddStats(c.secondaryAttributes);
            if (c is Mob m)
                foreach (MobStatProfile p in m.mobStatProfiles ?? new MobStatProfile[0])
                {
                    if (p == null) continue;
                    p.healthMax = Mathf.Max(1, Mathf.RoundToInt(p.healthMax * d.health));
                    p.xpValue = Mathf.RoundToInt(p.xpValue * d.xp);
                    AddStats(p.primaryAttributes);
                    AddStats(p.secondaryAttributes);
                }
            if (add.Count > 0 && !add.Any(a => Has(c.primaryAttributes, a.Item1) || Has(c.secondaryAttributes, a.Item1)))
                Plugin.Log.LogWarning($"{ctx}: none of the \"stats\" exist on this enemy");
        }

        private static bool Has(StatObj[] arr, STAT_TYPE t) => arr != null && arr.Any(s => s != null && s.statType == t);

        private static void Abilities(GameObject go, Character c, EnemyDef d, string ctx)
        {
            foreach (string name in d.removeAbilities ?? new List<string>())
            {
                var gone = go.GetComponents<Ability>().Where(a => name == "*" || a.internalName == name).ToList();
                if (gone.Count == 0 && name != "*") Plugin.Log.LogWarning($"{ctx}: removeAbilities: '{name}' is not on the base enemy");
                foreach (Ability a in gone) Object.DestroyImmediate(a);
            }
            foreach (MobAbilityDef ad in d.abilities ?? new List<MobAbilityDef>())
            {
                if (string.IsNullOrEmpty(ad.from)) continue;
                Ability ab;
                int slash = ad.from.IndexOf('/');
                if (slash > 0)
                {
                    Character donor = VanillaEnemy(ad.from.Substring(0, slash)) ?? (Ids.TryGetValue(ModPacks.Qualify(d.pack, ad.from.Substring(0, slash)), out string mid) ? Templates[mid] : null);
                    string an = ad.from.Substring(slash + 1);
                    Ability srcAb = donor?.GetComponents<Ability>().FirstOrDefault(a => a.internalName == an);
                    if (srcAb == null) { Plugin.Log.LogError($"{ctx}: ability '{ad.from}' not found (see vanilla_reference.txt)"); continue; }
                    if (go.GetComponents<Ability>().Any(a => a.internalName == an)) { Plugin.Log.LogWarning($"{ctx}: already has '{an}'"); ab = go.GetComponents<Ability>().First(a => a.internalName == an); }
                    else ab = Copy(srcAb, go);
                    if (ab is Ability_Mob am) am.isAvailable = true;
                }
                else
                {
                    ab = go.GetComponents<Ability>().FirstOrDefault(a => a.internalName == ad.from);
                    if (ab == null) { Plugin.Log.LogError($"{ctx}: ability '{ad.from}' is not on this enemy; use \"ENEMY/ABILITY\" to copy one"); continue; }
                }
                if (ad.cooldown.HasValue) ab.cooldown = ad.cooldown.Value;
                if (ad.range.HasValue) ab.range = ad.range.Value;
                if (ad.priority.HasValue) ab.priority = ad.priority.Value;
                if (ad.staminaCost.HasValue) ab.staminaCost = ad.staminaCost.Value;
                if (ad.damage != null)
                    ab.damageList = ad.damage.Select(x => new DmgObj(ClassRegistry.ParseEnum(x.type, DAMAGE_TYPE.Crushing, ctx), x.min) { damageMax = Math.Max(x.max, x.min) }).ToList();
            }
            foreach (MobSkillSpec s in d.skills ?? new List<MobSkillSpec>())
            {
                try
                {
                    EnemyAbilities.Prepare(s, d.pack, d.pack.id + "_" + ModPacks.Sanitize(d.id));
                    EnemyAbilities.Attach(go, s);
                }
                catch (Exception e) { Plugin.Log.LogError($"{ctx}: skill '{s.id ?? s.name}' failed: {e.Message}"); }
            }
            if (d.damage != 1f)
                foreach (Ability a in go.GetComponents<Ability>())
                    foreach (DmgObj dm in a.damageList ?? new List<DmgObj>())
                    {
                        if (dm == null) continue;
                        dm.damage = Mathf.Max(dm.damage > 0 ? 1 : 0, Mathf.RoundToInt(dm.damage * d.damage));
                        dm.damageMax = Mathf.Max(dm.damage, Mathf.RoundToInt(dm.damageMax * d.damage));
                    }
        }

        /// <summary>Same field copy the game uses to give rare enemies extra abilities (RareSpawn.CopyComponent).</summary>
        private static Ability Copy(Ability original, GameObject destination)
        {
            Type type = original.GetType();
            var copy = (Ability)destination.AddComponent(type);
            foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                f.SetValue(copy, f.GetValue(original));
            copy.damageList = (original.damageList ?? new List<DmgObj>()).Select(x => new DmgObj(x.dmgType, x.damage) { damageMax = x.damageMax }).ToList();
            return copy;
        }

        private static void Loot(GameObject go, EnemyDef d, string ctx)
        {
            if (d.loot == null) return;
            OnDeath_LootDrop drop = go.GetComponentInChildren<OnDeath_LootDrop>(true);
            if (drop == null)
            {
                var child = new GameObject("SCCL_LootDrop");
                child.transform.SetParent(go.transform, false);
                drop = child.AddComponent<OnDeath_LootDrop>();
                drop.dropThis = new List<Item>();
                drop.alsoThis = new List<ItemTemplate>();
            }
            if (d.loot.replace) { drop.dropThis = new List<Item>(); drop.alsoThis = new List<ItemTemplate>(); }
            foreach (string id in d.loot.items ?? new List<string>())
            {
                Item it = ItemRegistry.VanillaOrModItem(Crafting.ResolveItem(id, d.pack));
                if (it == null) Plugin.Log.LogWarning($"{ctx}: loot item '{id}' not found");
                else drop.dropThis.Add(it);
            }
            foreach (RandomLootDef r in d.loot.random ?? new List<RandomLootDef>())
                drop.alsoThis.Add(new ItemTemplate
                {
                    lootType = new[] { ClassRegistry.ParseEnum(r.type, LOOT_TYPE.Random, ctx) },
                    rarity = ClassRegistry.ParseEnum(r.rarity, RARITY.None, ctx),
                });
        }

        // ---- spawning ---------------------------------------------------------------------------------------

        private static bool HostilePoint(Spawn_Point sp, Room r)
        {
            if (r == null || r is Room_Event || r.Properties.IsBossRoom) return false;
            if (sp.spawns == null || sp.spawns.Length == 0 || sp.spawns.Any(s => s == null)) return false;
            if (sp.makeRare || sp.cinematicPosition != null) return false;
            if (sp.forceTeam && sp.setTeamTo != AI.AI_TEAM.Hostile) return false;
            return sp.spawns.All(s => s.ai != null && s.ai.aiTeam == AI.AI_TEAM.Hostile && !s.HasTag("BOSS") && !s.HasTag("BOSSHEALTHBAR")
                                      && !s.HasTag("RARE") && !s.HasTag("RARE_ELITE"));
        }

        private static bool OnFloor(EnemySpawnDef s, int floor) =>
            (s.floors == null || s.floors.Length == 0 || floor >= s.floors[0]) && (s.floors == null || s.floors.Length < 2 || floor <= s.floors[1]);

        /// <summary>Picks a mod enemy for this spawn point, or null to keep the vanilla draw.</summary>
        internal static Character Pick(Character[] spawns, int floor)
        {
            // Replacements first: a vanilla enemy that is in this point's draw is swapped with "chance" %.
            foreach (var kv in Defs)
                foreach (EnemySpawnDef s in kv.Value.spawn ?? new List<EnemySpawnDef>())
                {
                    if (string.IsNullOrEmpty(s.replaces) || !OnFloor(s, floor)) continue;
                    int hits = spawns.Count(x => x.internalName == s.replaces);
                    if (hits == 0) continue;
                    float p = s.chance / 100f * hits / spawns.Length;
                    if (UnityEngine.Random.value < p) return Templates[kv.Key];
                }
            // Then the additions: one roll over all of them, each taking its "chance" % slice (capped at 100% total).
            float roll = UnityEngine.Random.value * 100f;
            foreach (var kv in Defs)
                foreach (EnemySpawnDef s in kv.Value.spawn ?? new List<EnemySpawnDef>())
                {
                    if (!string.IsNullOrEmpty(s.replaces) || s.chance <= 0f || !OnFloor(s, floor)) continue;
                    if (roll < s.chance) return Templates[kv.Key];
                    roll -= s.chance;
                }
            return null;
        }

        [HarmonyPatch(typeof(Spawn_Point), nameof(Spawn_Point.Spawn))]
        private static class Patch_Spawn
        {
            private static void Prefix(Spawn_Point __instance, Room r, out Character[] __state)
            {
                __state = null;
                if (Defs.Count == 0 || Game.Game_isLoading) return;
                try
                {
                    if (!HostilePoint(__instance, r)) return;
                    Character pick = Pick(__instance.spawns, Game.FLOOR);
                    if (pick == null) return;
                    __state = __instance.spawns;
                    __instance.spawns = new[] { pick }; // vanilla then places it exactly like the enemy it stands in for
                }
                catch (Exception e) { Plugin.Log.LogError("Enemies: spawn pick failed: " + e); }
            }

            private static void Postfix(Spawn_Point __instance, Character[] __state)
            {
                if (__state != null) __instance.spawns = __state;
            }
        }

        /// <summary>Saves store enemies by internal name; mod enemies resolve to their template.</summary>
        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.GetCharacter))]
        private static class Patch_GetCharacter
        {
            private static void Postfix(string internalName, ref Character __result)
            {
                if (__result != null || internalName == null || !internalName.StartsWith(Prefix)) return;
                if (Templates.TryGetValue(internalName, out Character t)) __result = t;
                else Plugin.Log.LogWarning($"Enemies: '{internalName}' is from a mod that is not installed; it is left out of the level");
            }
        }

        /// <summary>Looks variants set their own name and resistances in Start; mod enemies keep what the json says.</summary>
        [HarmonyPatch(typeof(Mob_Variant), nameof(Mob_Variant.ApplyVariant))]
        private static class Patch_Variant
        {
            private static void Postfix(Mob_Variant __instance)
            {
                Character c = __instance.GetComponent<Character>();
                if (c == null || c.internalName == null || !Defs.TryGetValue(c.internalName, out EnemyDef d)) return;
                if (d.name != null && !c.HasTagWith("RARESPAWN") && !c.HasTagWith("ELITESPAWN")) c.nameOverride = "";
                if (d.resists != null && Templates.TryGetValue(c.internalName, out Character t))
                    c.resistances = t.resistances.Select(x => new DmgObj(x.dmgType, x.damage)).ToList();
            }
        }
    }
}
