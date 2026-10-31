using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// "summon" action: spawns a player minion the way vanilla Engineer pets are made (Player team, MINION tag,
    /// RegisterMinion, pet scaling). Lifetime lives in a character tag so it survives save/load.
    /// creature: "ABILITY_ID.field" (a vanilla ability's Character prefab field), "nanite_cloud", "nanite_plague",
    /// or part of a creature prefab name.
    /// </summary>
    internal static class Summons
    {
        internal const string LifeTag = "SCCL_SUMMON";
        /// <summary>Marks every mod summon, timed or not, for the vanilla minion patches below.</summary>
        internal const string ModTag = "SCCL_MODSUMMON";
        /// <summary>A stationary summon: it never walks off the tile it appeared on (kept through loads).</summary>
        internal const string StayTag = "SCCL_STAY";
        /// <summary>The owner stat (a STAT_TYPE number) a summon grows with when it isn't Smarts (kept through loads).</summary>
        internal const string StatTag = "SCCL_PETSTAT";
        private const string OwnerTagPrefix = "SCCLS_";
        private static readonly Dictionary<string, Character> prefabCache = new Dictionary<string, Character>(StringComparer.OrdinalIgnoreCase);

        /// <param name="howMany">How many to make (amount, or amount scaled by stacks); null = amount, at least 1.</param>
        public static void Summon(ActionSpec a, ActionContext ctx, Character near, int? howMany = null, Vector3? at = null)
        {
            Character caster = ctx.caster ?? Player.instance;
            if (caster == null || AI_Manager.instance == null || Map.instance == null) return;
            Character prefab = Resolve(a.creature);
            if (prefab == null) { Plugin.Log.LogWarning($"{ctx.sourceName}: summon creature '{a.creature}' not found"); return; }
            if (a.anySide && (ForceLoose ?? UnityEngine.Random.value < 0.5f)) { Loose(a, ctx, near ?? caster, howMany ?? Math.Max(1, a.amount)); return; }

            string ownerTag = !string.IsNullOrEmpty(a.summonGroup) ? GroupTag(a.summonGroup) : OwnerTagPrefix + (ctx.source != null ? ctx.source.internalName : ctx.classId);
            int count = howMany ?? Math.Max(1, a.amount);
            if (count <= 0) return;
            int cap = a.maxSummons > 0 ? a.maxSummons : 99;
            int existing = Mine(ownerTag).Count;
            Vector3 center = at ?? Pos(near ?? caster);

            string step = "start";
            try
            {
            for (int i = 0; i < count && existing < cap; i++)
            {
                step = "node" + i;
                NavNode node = FreeNode(center, 2.6f) ?? FreeNode(Pos(caster), 3.5f);
                if (node == null) break;
                Character c;
                step = "spawn" + i;
                c = AI_Manager.instance.Spawn_Character(prefab, node.transform.position, Quaternion.identity, AI.AI_TEAM.Player);
                if (c == null) return;
                step = "place" + i;
                if (c.ai != null) c.ai.ForceMove = true;
                if (c.ai != null) c.ai.SetForceFacing(caster.transform.position);
                // Spawn_Character already places it; SetPosition would re-look the node up and can hit null.
                if (c.GetMover() != null && c.GetMover().GetCurrentNavNode() != node) c.GetMover().SetCurrentNavNode(node);
                c.level = caster.level;
                if (!string.IsNullOrEmpty(a.summonName)) c.nameOverride = a.summonName;
                if (a.model != null) ModelAssets.ApplyTagged(c, a.model, ClassRegistry.FolderOf(ctx.classId));
                c.SetTag("MINION", 1);
                c.SetTag(ModTag, 1);
                c.SetTag(ownerTag, 1);
                if (a.turns > 0) c.SetTag(LifeTag, a.turns);
                if (a.stationary) { c.SetTag(StayTag, 1); c.FLAG_canMove = false; }
                EnemyDef cd = EnemyRegistry.DefOf(prefab);
                if (Enum.TryParse(a.scaleStat ?? (cd != null && cd.creature ? cd.scaleStat : null) ?? "", true, out STAT_TYPE growStat) && growStat != STAT_TYPE.Smarts)
                    c.SetTag(StatTag, (int)growStat);
                AI_Manager.instance.RegisterMinion(c);
                // Mid-fight it joins in at once: an idle pet only wakes when it spots someone itself or a cast
                // alerts the spot, and self-cast summons (a veil, a stance) alert nothing.
                if (c.ai != null && When.InCombat()) c.ai.ChangeState(AI.AI_STATE.Active);
                try
                {
                    Effect scalar = EffectManager.instance.ApplyEffect("Effect_PASSIVE_Pet_Scalar", c);
                    if (scalar != null)
                    {
                        // Same divisor (Smarts per bonus point) and health-per-level vanilla gives each pet.
                        var (div, hpPerLevel) = cd != null && cd.creature ? (Math.Max(1, cd.smartsPerBonus), cd.healthPerLevel) : ScalarFor(a.creature);
                        scalar.caster = caster;
                        scalar.amountA = div;
                        scalar.amountC = hpPerLevel;
                        if (c.HasTag(StatTag)) (scalar as Effect_Pet_Scalar)?.UpdateStats();
                    }
                }
                catch { }
                step = "fx" + i;
                if (!string.IsNullOrEmpty(a.effect))
                {
                    Actions.Run(new[] { new ActionSpec { @do = "effect", effect = a.effect } }, ctx, c);
                    c.CalculateStats();
                    c.SetHealth(c.GetHealthMax()); // a fresh summon starts full, whatever its effect adds
                }
                if (!string.Equals(a.summonVfx, "none", StringComparison.OrdinalIgnoreCase))
                    Actions.SpawnVfx(string.IsNullOrEmpty(a.summonVfx) ? "PA_Engineer_Boltron.PS_summon" : a.summonVfx, c.GetHitFx().position);
                existing++;
                Plugin.Log.LogInfo($"{ctx.sourceName}: summoned {c.name} ({existing}/{(a.maxSummons > 0 ? a.maxSummons.ToString() : "-")}, {a.turns} turns)");
            }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"{ctx.sourceName}: summon failed at {step}: {e}"); }
        }

        internal const int DefaultScalarDiv = 6;
        internal const float DefaultScalarHp = 8f;

        private static (int, float) ScalarFor(string creature)
        {
            string s = creature ?? "";
            if (s.IndexOf("Junker", StringComparison.OrdinalIgnoreCase) >= 0) return (12, 15f);
            if (s.IndexOf("Turret", StringComparison.OrdinalIgnoreCase) >= 0) return (12, 8f);
            if (s.IndexOf("Boltron", StringComparison.OrdinalIgnoreCase) >= 0) return (8, 10f);
            if (s.IndexOf("Tentacle", StringComparison.OrdinalIgnoreCase) >= 0) return (3, 4f);
            return (DefaultScalarDiv, DefaultScalarHp);
        }

        /// <summary>"a Spidermine": what a summon action calls up, for rules text (the shown name when one is set).</summary>
        internal static string NameOf(string creature, string summonName)
        {
            if (!string.IsNullOrEmpty(summonName)) return "a " + summonName;
            Character prefab = null;
            try { prefab = Resolve(creature); } catch { }
            string n = prefab != null ? prefab.GetName() : (creature ?? "creature");
            if (prefab == null && n.Contains(".")) n = n.Split('.')[0].Split('_').Last();
            n = n.Replace("NPC_", "").Replace("_", " ");
            return (n.Length > 0 && "AEIOUaeiou".IndexOf(n[0]) >= 0 ? "an " : "a ") + n;
        }

        /// <summary>Living minions summoned by one ability (or all mod summons when ownerTag is null).</summary>
        public static List<Character> Mine(string ownerTag)
        {
            if (AI_Manager.instance == null) return new List<Character>();
            return AI_Manager.instance.GetPlayerMinions()
                .Where(m => m != null && !m.GetIsDead() && (ownerTag == null ? m.HasTag(LifeTag) || m.effects != null && m.HasTag("MINION") : m.HasTag(ownerTag)))
                .ToList();
        }

        /// <summary>The tag shared by summons of one summonGroup, whichever ability made them.</summary>
        internal static string GroupTag(string group) => OwnerTagPrefix + "G_" + new string((group ?? "").Where(char.IsLetterOrDigit).ToArray());

        /// <summary>The tag that marks summons made by the ability (or class) behind this context.</summary>
        internal static string OwnerTag(ActionContext ctx) => OwnerTagPrefix + (ctx.source != null ? ctx.source.internalName : ctx.classId);

        /// <summary>Living summons matching "self" (this ability's), "any", or a creature name.</summary>
        internal static List<Character> Matching(string spec, ActionContext ctx)
        {
            if (AI_Manager.instance == null || string.IsNullOrEmpty(spec)) return new List<Character>();
            if (spec.StartsWith("group:", StringComparison.OrdinalIgnoreCase)) return Mine(GroupTag(spec.Substring(6)));
            switch (spec.ToLowerInvariant())
            {
                case "self": return Mine(OwnerTag(ctx));
                case "any": return AI_Manager.instance.GetPlayerMinions().Where(m => m != null && !m.GetIsDead()).ToList();
                default:
                    return AI_Manager.instance.GetPlayerMinions().Where(m => m != null && !m.GetIsDead() &&
                        ((m.internalName ?? "").IndexOf(spec, StringComparison.OrdinalIgnoreCase) >= 0 || m.name.IndexOf(spec, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            }
        }

        /// <summary>Count of your living minions (any source), for "stacksOf": "summons".</summary>
        public static int Count() =>
            AI_Manager.instance == null ? 0 : AI_Manager.instance.GetPlayerMinions().Count(m => m != null && !m.GetIsDead());

        // ---- "anySide" summons that came in against you ------------------------------------------------------------

        internal static bool? ForceLoose; // devtests
        internal static int LooseSpawned; // devtests
        private static readonly Dictionary<Character, int> looseFoes = new Dictionary<Character, int>();
        internal static int LooseCount => looseFoes.Keys.Count(c => c != null && !c.GetIsDead());

        private static void Loose(ActionSpec a, ActionContext ctx, Character near, int count)
        {
            Vector3 center = Pos(near);
            for (int i = 0; i < count && LooseCount < Math.Max(1, a.maxSummons > 0 ? a.maxSummons : 3); i++)
            {
                Character prefab = Resolve(a.creature); // a boss's minions: a fresh pick each time
                NavNode node = FreeNode(center, 2.6f);
                if (prefab == null || node == null) return;
                Character c = AI_Manager.instance.Spawn_Character(prefab, node.transform.position, Quaternion.identity, AI.AI_TEAM.Hostile);
                if (c == null) return;
                c.level = ctx.caster.level;
                c.SetTag("NOXP", 1);
                if (!string.IsNullOrEmpty(a.summonName)) c.nameOverride = a.summonName;
                if (!string.IsNullOrEmpty(a.effect))
                {
                    Actions.Run(new[] { new ActionSpec { @do = "effect", effect = a.effect } }, ctx, c);
                    c.CalculateStats();
                    c.SetHealth(c.GetHealthMax());
                }
                looseFoes[c] = Math.Max(1, a.turns);
                LooseSpawned++;
                if (c.ai != null && When.InCombat()) c.ai.ChangeState(AI.AI_STATE.Active);
                Actions.SpawnVfx(string.IsNullOrEmpty(a.summonVfx) ? "PA_Engineer_Boltron.PS_summon" : a.summonVfx, c.GetHitFx().position);
                Game.LOG($"<color=#FF6B6B>{c.GetName()}</color> comes through against you!");
            }
        }

        internal static void ClearLoose() // devtests
        {
            foreach (Character c in looseFoes.Keys.ToList()) if (c != null && !c.GetIsDead()) c.Die_Silent();
            looseFoes.Clear();
        }

        private static void TickLoose()
        {
            foreach (Character c in looseFoes.Keys.ToList())
            {
                if (c == null || c.GetIsDead()) { looseFoes.Remove(c); continue; }
                if (--looseFoes[c] > 0) continue;
                looseFoes.Remove(c);
                Actions.SpawnVfx("PA_Engineer_Boltron.PS_summon", c.GetHitFx().position);
                c.Die_Silent();
            }
        }

        /// <summary>"boss:Enemy_BOSS_Smorn": a creature that boss really summons, read from its abilities.</summary>
        private static Character BossMinion(string bossId)
        {
            if (!bossMinions.TryGetValue(bossId, out List<Character> minions))
            {
                minions = new List<Character>();
                Character boss = AI_Manager.instance.GetCharacter(bossId);
                foreach (Ability ab in boss != null ? boss.GetComponentsInChildren<Ability>(true) : new Ability[0])
                    foreach (var f in ab.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        object v = f.GetValue(ab);
                        if (v is Character one && one != boss) minions.Add(one);
                        else if (v is IEnumerable<Character> many) minions.AddRange(many.Where(m => m != null && m != boss));
                    }
                minions = minions.Distinct().ToList();
                Plugin.Log.LogInfo($"Minions of {bossId}: {string.Join(", ", minions.Select(m => m.name))}");
                bossMinions[bossId] = minions;
            }
            return minions.Count > 0 ? minions[UnityEngine.Random.Range(0, minions.Count)] : null;
        }

        private static readonly Dictionary<string, List<Character>> bossMinions = new Dictionary<string, List<Character>>();

        /// <summary>Called once per player turn: ages timed summons and dismisses expired ones.</summary>
        public static void Tick()
        {
            if (AI_Manager.instance == null) return;
            TickLoose();
            foreach (Character m in AI_Manager.instance.GetPlayerMinions())
            {
                if (m == null || m.GetIsDead() || !m.HasTag(LifeTag)) continue;
                int left = m.GetTag(LifeTag) - 1;
                if (left > 0) { m.SetTag(LifeTag, left); continue; }
                Actions.SpawnVfx("PA_Engineer_Boltron.PS_summon", m.GetHitFx().position);
                m.RemoveTag(LifeTag);
                Plugin.Log.LogInfo($"Summon {m.name} expired");
                m.Die();
            }
        }

        private static Vector3 Pos(Character c) =>
            c.GetMover() != null && c.GetMover().GetCurrentNavNode() != null ? c.GetMover().GetCurrentNavNode().transform.position : c.transform.position;

        private static NavNode FreeNode(Vector3 pos, float r) =>
            Map.instance.GetNavNodesAt(pos, r, validNodesOnly: true)
                .Where(n => n != null && !n.IsOccupied() && !n.BlockCheck() && !n.Block_Move)
                .OrderBy(n => Vector3.Distance(n.transform.position, pos)).FirstOrDefault();

        private static Character Resolve(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return null;
            if (spec.StartsWith("boss:")) return BossMinion(spec.Substring(5));
            if (prefabCache.TryGetValue(spec, out Character hit) && hit != null) return hit;
            Character found = EnemyRegistry.Creature(spec); // a mod's own creature ("creatures" data)
            if (found != null) { }
            else if (spec.Equals("nanite_cloud", StringComparison.OrdinalIgnoreCase)) found = AI_Manager.instance.naniteCloudPrefab;
            else if (spec.Equals("nanite_plague", StringComparison.OrdinalIgnoreCase)) found = AI_Manager.instance.nanitePlaguePrefab;
            else if (spec.Contains("."))
            {
                string[] p = spec.Split('.');
                Ability src = FindAbility(p[0]);
                var f = src?.GetType().GetField(p[1], System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                found = f?.GetValue(src) as Character;
            }
            else if (AI_Manager.instance != null && AI_Manager.instance.GetCharacter(spec) is Character byId && byId != null)
                found = byId; // an enemy internal name (vanilla_reference.txt ENEMIES), including mod enemies
            else
            {
                found = Resources.FindObjectsOfTypeAll<Character>()
                    .Where(c => c != null && c.gameObject.scene.name == null && c.GetComponent<AI>() != null && !(c is Player))
                    .FirstOrDefault(c => c.name.IndexOf(spec, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            prefabCache[spec] = found;
            return found;
        }

        private static Ability FindAbility(string id)
        {
            if (Game.instance == null) return null;
            foreach (Skill_Tree t in Game.instance.skills)
                foreach (Skill_Tier tier in t?.skillTiers ?? new List<Skill_Tier>())
                {
                    if (tier?.ability != null && tier.ability.internalName == id) return tier.ability;
                    if (tier?.passive != null && tier.passive.internalName == id) return tier.passive;
                }
            return null;
        }

        /// <summary>For the reference dump: summonable prefab fields on vanilla abilities.</summary>
        internal static IEnumerable<string> ListPrefabs()
        {
            if (Game.instance == null) yield break;
            foreach (Skill_Tree t in Game.instance.skills)
                foreach (Skill_Tier tier in t?.skillTiers ?? new List<Skill_Tier>())
                {
                    Ability a = tier?.ability ?? tier?.passive;
                    if (a == null) continue;
                    foreach (var f in a.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                        if (f.GetValue(a) is Character c && c != null) yield return $"{a.internalName}.{f.Name}  ({c.name})";
                }
        }
    }

    /// <summary>Vanilla calls the Engineer ability when a Junker or Turret dies; mod summons may have no Engineer.</summary>
    [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.MinionDied))]
    internal static class Patch_ModMinionDied
    {
        private static bool Prefix(Character c) => c == null || !c.HasTag(Summons.ModTag);
    }

    /// <summary>Mod summons are capped per ability (maxSummons), so they don't use up the vanilla 3-pet limit.</summary>
    [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.GetPlayerMinionMax))]
    internal static class Patch_ModMinionMax
    {
        private static void Postfix(AI_Manager __instance, ref int __result)
        {
            var list = Traverse.Create(__instance).Field("playerMinions").GetValue<List<Character>>();
            if (list != null) __result += list.Count(m => m != null && m.HasTag(Summons.ModTag));
        }
    }

    /// <summary>A pet scalar with no divisor (e.g. restored from a save) would divide by zero on level-up.</summary>
    [HarmonyPatch(typeof(Effect_Pet_Scalar), nameof(Effect_Pet_Scalar.UpdateStats))]
    internal static class Patch_PetScalarGuard
    {
        private static void Prefix(Effect_Pet_Scalar __instance)
        {
            if (__instance.amountA == 0) __instance.amountA = Summons.DefaultScalarDiv;
            if (__instance.amountC == 0f) __instance.amountC = Summons.DefaultScalarHp;
        }
    }

    /// <summary>
    /// A summon with a scaleStat (Psi Power for astral and void summons) takes its pet bonus from that owner stat instead
    /// of Smarts, and is lent the stat itself, so skills with that "power" grow with the owner. Runs on every update.
    /// </summary>
    [HarmonyPatch(typeof(Effect_Pet_Scalar), nameof(Effect_Pet_Scalar.UpdateStats))]
    internal static class Patch_PetScalarStat
    {
        private static void Postfix(Effect_Pet_Scalar __instance)
        {
            Character t = __instance.target, owner = __instance.caster;
            if (t == null || owner == null || t.GetIsDead() || owner.GetIsDead() || !t.HasTag(Summons.StatTag)) return;
            var mods = __instance.attributeMods;
            if (mods == null || mods.Count < 2) return;
            STAT_TYPE stat = (STAT_TYPE)t.GetTag(Summons.StatTag);
            int have = owner.GetStat(stat);
            mods[0].amount = mods[1].amount = Math.Max(1, have / Math.Max(1, __instance.amountA));
            StatObj lent = mods.Skip(2).FirstOrDefault(m => m.statType == stat);
            if (lent == null) mods.Add(lent = new StatObj(stat, 0));
            lent.amount = have;
            t.CalculateStats();
        }
    }

    [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
    internal static class Patch_SummonTick
    {
        private static void Postfix()
        {
            try { Summons.Tick(); } catch (Exception e) { Plugin.Log.LogWarning("Summon tick: " + e.Message); }
        }
    }

    /// <summary>
    /// Item summons (vanilla PA_Summon skills on gear, like Summon Prodling) never said what the summon is, how strong
    /// it gets or how long it stays. The tooltip now ends with that: a mob's stats come from the floor it is summoned
    /// on (or the fixed profile the skill forces), not from your stats, and a timed effect on it sets how long it lasts.
    /// </summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.GetTooltipDescription))]
    internal static class Patch_ItemSummonTip
    {
        private static void Postfix(Ability __instance, ref string __result)
        {
            // class trees' own summons (Engineer pets, Smuggler goons, nanite clouds) explain themselves
            if (!(__instance is PA_Summon s)) return;
            string type = __instance.GetType().Name;
            if (type.StartsWith("PA_Engineer") || type.StartsWith("PA_Smuggler") || type.StartsWith("PA_Nanomancy")) return;
            try
            {
                Character c = s.characterPrefab ?? s.randomCharacterPrefabs?.FirstOrDefault(x => x != null);
                string who = s.randomCharacterPrefabs != null && s.randomCharacterPrefabs.Length > 1 ? "one of several creatures" : c != null ? c.GetName() : "a creature";
                string scale;
                if (c is Mob m && m.mobStatProfiles != null && m.mobStatProfiles.Length > 0 && !c.HasTag("NOPROFILE"))
                    scale = s.forceSpawnProfile >= 0
                        ? $"Its strength is fixed (a floor {s.forceSpawnProfile + 1} creature's), whatever your stats."
                        : $"Its health and stats grow with the floor you call it on (here: a floor {Mathf.Min(Game.FLOOR, m.mobStatProfiles.Length - 1) + 1} creature's), not with your stats.";
                else scale = "Its strength is fixed: it doesn't grow with the floor or your stats.";
                int turns = s.setEffectDurationTo;
                if (turns == 0 && !string.IsNullOrEmpty(s.applyEffect))
                {
                    Effect fx = EffectManager.instance?.GetEffect(s.applyEffect, doInstantiate: false);
                    if (fx != null) turns = fx.duration;
                }
                string stay = turns > 0 ? $"It stays for {turns} turn{(turns == 1 ? "" : "s")}." : "It stays until it is killed.";
                string team = s.setTeamTo == AI.AI_TEAM.Player ? "to fight for you" : "";
                __result = (__result ?? "") + $"\n<color=#9FB7C9>Summons {who} {team}. {scale} {stay}</color>".Replace("  ", " ").Replace(" .", ".");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Item summon tip: " + e.Message); }
        }
    }

    /// <summary>The dodge line used the body's own name ("Nanite Cloud dodges"); a named summon goes by its name.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.GetDodgeString))]
    internal static class Patch_SummonDodgeName
    {
        private static void Postfix(Character __instance, ref string __result)
        {
            if (__instance == null || __instance is Player || string.IsNullOrEmpty(__instance.nameOverride) || __result == null) return;
            string body = __instance.displayName;
            if (!string.IsNullOrEmpty(body) && __result.StartsWith(body)) __result = __instance.nameOverride + __result.Substring(body.Length);
        }
    }

    /// <summary>
    /// A body with looks variants names itself in Start, after the spawn and after a load have set the summon's name;
    /// a mod summon keeps the name it was given.
    /// </summary>
    [HarmonyPatch(typeof(Mob_Variant), nameof(Mob_Variant.ApplyVariant))]
    [HarmonyPriority(Priority.Last)]
    internal static class Patch_SummonVariantName
    {
        private static void Prefix(Mob_Variant __instance, out string __state)
        {
            Character c = __instance != null ? __instance.GetComponent<Character>() : null;
            __state = c != null && c.HasTag(Summons.ModTag) ? c.nameOverride : null;
        }

        private static void Postfix(Mob_Variant __instance, string __state)
        {
            if (string.IsNullOrEmpty(__state)) return;
            Character c = __instance.GetComponent<Character>();
            if (c != null && c.nameOverride != __state) c.SetName(__state);
        }
    }

    /// <summary>
    /// A save keeps a creature's health max with its effects' bonuses in it, and the load sets that as the body's base
    /// (Mob.SetHealth writes the cached base too) before putting the effects back on top: a summon with a HealthMax
    /// effect gained it again on every load. The base goes back to the saved max less what its effects add.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Deserialize))]
    internal static class Patch_SummonLoadHealth
    {
        private static readonly AccessTools.FieldRef<Mob, StatObj[]> cache = AccessTools.FieldRefAccess<Mob, StatObj[]>("cache_secondaryAttributes");

        private static void Postfix(Character __instance, object d)
        {
            if (!(__instance is Mob m) || !m.HasTag(Summons.ModTag) || !(d is CharacterSaveInfo info) || m.GetIsDead()) return;
            try
            {
                StatObj baseMax = cache(m)?.FirstOrDefault(s => s != null && s.statType == STAT_TYPE.HealthMax);
                if (baseMax == null) return;
                int fromEffects = m.GetHealthMax() - baseMax.amount;
                if (fromEffects == 0) return;
                int max = Math.Max(1, info.healthMax - fromEffects);
                m.SetHealth(Math.Min(info.health, max), max);
                m.CalculateStats();
                m.SetHealth(Mathf.Clamp(info.health, 1, m.GetHealthMax()));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Summon load health ({m.name}): {e.Message}"); }
        }
    }

    /// <summary>
    /// A nanite cloud heals an ally or infests a foe by dying into it and bursting back out later as a new, nameless
    /// cloud on the host's enemy team. A mod summon on a cloud body would be lost that way and come back hostile, so it
    /// never merges: it doesn't use the heal, and its infesting attack always lands as a plain strike.
    /// </summary>
    [HarmonyPatch(typeof(MA_Nanite_Heal), nameof(MA_Nanite_Heal.CanUse))]
    internal static class Patch_SummonNaniteHeal
    {
        private static readonly AccessTools.FieldRef<Ability, Character> caster = AccessTools.FieldRefAccess<Ability, Character>("caster");

        private static void Postfix(MA_Nanite_Heal __instance, ref bool __result)
        {
            if (__result && caster(__instance) is Character c && c.HasTag(Summons.ModTag)) __result = false;
        }
    }

    [HarmonyPatch(typeof(MA_Nanite_Infest), nameof(MA_Nanite_Infest.Use))]
    internal static class Patch_SummonNaniteInfest
    {
        private static readonly AccessTools.FieldRef<Ability, Character> caster = AccessTools.FieldRefAccess<Ability, Character>("caster");
        private static readonly AccessTools.FieldRef<MA_Nanite_Infest, int> chance = AccessTools.FieldRefAccess<MA_Nanite_Infest, int>("infestChance");

        private static void Prefix(MA_Nanite_Infest __instance)
        {
            if (!__instance.isPlague && caster(__instance) is Character c && c.HasTag(Summons.ModTag)) chance(__instance) = int.MinValue / 2;
        }
    }

    /// <summary>A loaded stationary summon stays put again.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Deserialize))]
    internal static class Patch_StationarySummon
    {
        private static void Postfix(Character __instance)
        {
            if (__instance != null && __instance.HasTag(Summons.StayTag)) __instance.FLAG_canMove = false;
        }
    }

}
