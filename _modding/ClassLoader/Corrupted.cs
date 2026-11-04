using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// A boss with "creature": "self" (the Corrupted Mirror): your class's crawler body in your gear, with your stats, on
    /// the enemy's side. Everything of yours comes back wrong. Its skills are yours in a shuffled order, each at another's
    /// rank and all dealing Void, with at least one swapped for a skill from a class you don't have. Vanilla skills have
    /// no spec to copy, so a vanilla build keeps the body's own Nemesis skills beside the copies. Your resistances guard
    /// against other damage types, and its weapon hits with Void. When it dies it leaves corrupted copies of some of your
    /// gear: the same item, every bonus stronger and moved to a different stat or damage type, any weapon damage Void, and
    /// its skill swapped for a corrupted version where it has one, and bonuses swapped for corrupted mods (see corruptedGear).
    /// Your minions and party come with it, so numbers don't settle the fight: each is the same creature with its stats as
    /// they stand now and more health, all its damage Void. They last as long as their originals would have, and fall when
    /// it does. All of them hit as hard as you and yours do, then "finalDamage" cuts what lands: the last step of the hit,
    /// after block, crits, shields, armour and resistance, so that share holds whatever the target wears. Status effects
    /// keep to "strength". Your people can be stronger than you are, a charmed elite above all, so they answer to a limit
    /// on what they take from you in a round ("escortDeadly"): checked when they step out and again each round, and any
    /// they go over comes off their hits and back as health, armour, dodge, block and resistance.
    /// One seed in a tag rebuilds the same copy after a load; a corrupted item is never corrupted again.
    /// </summary>
    internal static class Corrupted
    {
        /// <summary>Tag values: the seed, then strength, health share and what's left of a hit, in percent.</summary>
        internal const string Tag = "SCCLB_CORRUPT", StrengthTag = "SCCLB_CORRUPTSTR", HealthTag = "SCCLB_CORRUPTHP", FinalTag = "SCCLB_CORRUPTFINAL";
        /// <summary>On an item: this is a corrupted copy; the corrupted skill it has; a corrupted mod on it.</summary>
        internal const string ItemTag = "SCCL_CORRUPTED", SkillTag = "SCCL_CORRUPTSKILL_", ModTag = "SCCL_CORRUPTMOD_";
        /// <summary>On a copy of your minion: that it is one; the turns it has left, if timed; its boss.</summary>
        internal const string EscortTag = "SCCLB_ESCORT", EscortLifeTag = "SCCLB_ESCORTLIFE", EscortOfTag = "SCCLB_ESCORTOF_";
        /// <summary>On a copy of your minion: its base stats and resistances. A save keeps neither on a creature.</summary>
        private const string StatTag = "SCCLB_ESTAT_", GuardTag = "SCCLB_EGUARD_";

        private const DAMAGE_TYPE Void = DAMAGE_TYPE.Void;
        /// <summary>However deep it goes, it never hits as hard as you do.</summary>
        private const float MaxStrength = 0.9f;

        /// <summary>What landed in the fight by who dealt it, after the final cut (tests).</summary>
        internal static readonly Dictionary<string, int> Dealt = new Dictionary<string, int>();
        /// <summary>The last hit before and after the final cut (tests).</summary>
        internal static int CutFrom, CutTo;

        /// <summary>The last kit taught, and its skills from other classes (tests).</summary>
        internal static List<MobSkillSpec> LastKit = new List<MobSkillSpec>(), LastForeign = new List<MobSkillSpec>();
        internal static List<string> LastSwaps = new List<string>();

        /// <summary>A new copy: its share of you, raised by the depth in the Ichor Deep.</summary>
        internal static void Spawned(Character c, BossSpec b, NavNode at)
        {
            int depth = Endless.Depth;
            float strength = Mathf.Min(MaxStrength, b.mirror.strength * (1f + Endless.PowerPerDepth.Value / 300f * depth));
            float health = b.health * (1f + Endless.HealthPerDepth.Value / 100f * depth);
            c.SetTag(Tag, UnityEngine.Random.Range(1, int.MaxValue));
            c.SetTag(StrengthTag, Mathf.RoundToInt(strength * 100f));
            c.SetTag(HealthTag, Mathf.RoundToInt(health * 100f));
            float final = Mathf.Min(MaxStrength, b.mirror.finalDamage * (1f + Endless.PowerPerDepth.Value / 300f * depth));
            c.SetTag(FinalTag, Mathf.RoundToInt(final * 100f));
            int xp = c.xpValue * 5;
            Fit(c, b);
            c.xpValue = xp;
            SpawnEscort(b, at, final, b.mirror.escortHealth * health / Mathf.Max(0.01f, b.health));
        }

        /// <summary>A copy back from a save: rebuilt from its seed before it next acts.</summary>
        internal static void Refit(Character c)
        {
            BossSpec b = Bosses.SpecOf(c);
            if (b != null && c.HasTag(Tag) && c.GetComponent<ModMobAbility>() == null) Fit(c, b, c.GetHealth());
        }

        private static void Fit(Character c, BossSpec b, int keepHealth = -1)
        {
            Player me = Player.instance;
            if (c == null || me == null) return;
            MirrorSpec m = b.mirror;
            int seed = c.GetTag(Tag);
            var rng = new System.Random(seed);
            float strength = c.GetTag(StrengthTag) / 100f;
            Mirrors.Strip(c);
            c.AddTag("BOSS");
            c.SetTag("NOCORPSE", 1);
            Mirrors.CopyStats(c, me, c.GetTag(HealthTag) / 100f, keepHealth, r => Resists(r, rng));
            Teach(c, m, strength, seed, rng);
            string kind = c.internalName?.Replace("Nemesis_", "") ?? "";
            try { Wear.Dress(c, Mirrors.Worn, kind, me.playerName); }
            catch (Exception e) { Plugin.Log.LogWarning($"Corrupted copy: dressing: {e.Message}"); }
            Mirrors.Stain(c, Moves.Hex(m.look, Color.white), m.glow);
            if (c.ai != null)
            {
                c.ai.aiStyle = AI.AI_STYLE.Brawler;
                Traverse.Create(c.ai).Field("maneuverChance").SetValue(0);
            }
        }

        // ---- switched around ----------------------------------------------------------------------------------

        private static List<T> Shuffled<T>(IEnumerable<T> list, System.Random rng) => list.OrderBy(_ => rng.Next()).ToList();

        /// <summary>The damage types a bonus can move to.</summary>
        private static List<DAMAGE_TYPE> Types => ((DAMAGE_TYPE[])Enum.GetValues(typeof(DAMAGE_TYPE))).Where(t => t != DAMAGE_TYPE.Null && t != DAMAGE_TYPE.Meta).ToList();

        /// <summary>The same amounts, each on a damage type none of them guarded against.</summary>
        private static List<DmgObj> Resists(List<DmgObj> list, System.Random rng)
        {
            var open = Shuffled(Types.Where(t => list.All(r => r.dmgType != t)), rng);
            // too many to place: the rest take a type the first ones just left
            return list.Select((r, i) => new DmgObj(i < open.Count ? open[i] : list[(i - open.Count + (open.Count == 0 ? 1 : 0)) % list.Count].dmgType, r.damage)).ToList();
        }

        private static List<ActionSpec> Twist(List<ActionSpec> list) => list?.Select(a =>
        {
            ActionSpec t = a.ShallowCopy();
            string act = (a.@do ?? "").ToLowerInvariant();
            if (act == "damage" || act == "chain") t.type = Void.ToString();
            t.then = Twist(a.then);
            t.@else = Twist(a.@else);
            t.actions = Twist(a.actions);
            return t;
        }).ToList();

        private static MobSkillSpec Own(MobSkillSpec s, int seed, int rank, int priority, string name)
        {
            MobSkillSpec c = s.ShallowCopy();
            c.key = $"Corrupt_{seed}_{s.id}";
            c.fullDamage = true;
            c.name = name;
            c.copyRank = rank;
            c.priority = priority;
            c.actions = Twist(s.actions);
            EnemyAbilities.Specs[c.key] = c;
            return c;
        }

        /// <summary>A class no copy may take skills from: it says "mirror": false, or this mirror's blacklist names it.</summary>
        internal static bool Barred(string classId, MirrorSpec m) =>
            !string.IsNullOrEmpty(classId) && ((m?.blacklist?.Contains(classId, StringComparer.OrdinalIgnoreCase) ?? false)
                || ClassRegistry.AllDefs.Any(d => d.mirror == false && string.Equals(d.id, classId, StringComparison.OrdinalIgnoreCase)));

        /// <summary>Every copyable skill of each class you don't have (nor barred), grouped by class, classes shuffled.</summary>
        private static List<List<MobSkillSpec>> Strangers(MirrorSpec m, float strength, System.Random rng, int rank)
        {
            var mine = new HashSet<string>(Player_SkillControl.instance.skillTrees.Where(t => t != null).Select(t => t.internalName), StringComparer.OrdinalIgnoreCase);
            foreach (ModAbility a in Player_AbilityControl.instance.GetAbilities().OfType<ModAbility>()) mine.Add(a.ClassId);
            var classes = ClassRegistry.AllDefs.Where(d => d.enabled && !mine.Contains(d.id) && !Barred(d.id, m)).Select(d => d.id).ToList();
            return Shuffled(ModAbility.Specs.Values.Where(r => classes.Contains(r.classId) && r.spec.type != "passive"), rng)
                .Select(r => (r.classId, skill: Mirrors.Copy(r.spec, r.spec.actions ?? new List<ActionSpec>(), r.internalName, r.spec.name, r.spec.range, rank, r.classId, strength)))
                .Where(x => x.skill != null).GroupBy(x => x.classId).Select(g => g.Select(x => x.skill).ToList()).ToList();
        }

        internal static void Teach(Character c, MirrorSpec m, float strength, int seed, System.Random rng)
        {
            var all = Player_AbilityControl.instance.GetAbilities().OfType<ModAbility>().ToList();
            var barred = all.Where(a => Barred(a.ClassId, m)).Select(a => Mirrors.Copy(a, strength)).Where(s => s != null).ToList();
            var mine = Shuffled(all.Where(a => !Barred(a.ClassId, m)).Select(a => Mirrors.Copy(a, strength)).Where(s => s != null), rng);
            var strangers = Strangers(m, strength, rng, Player.instance.level / 3);
            // a barred class of yours is swapped for a stranger class: as many of its skills, at your ranks
            var swaps = new List<string>();
            foreach (var cls in barred.GroupBy(s => s.classId))
            {
                List<MobSkillSpec> with = strangers.FirstOrDefault(g => g.Count >= cls.Count()) ?? strangers.FirstOrDefault();
                if (with == null) break;
                strangers.Remove(with);
                var ranksOf = cls.Select(s => s.copyRank).ToList();
                foreach (var (s, i) in with.Take(ranksOf.Count).Select((s, i) => (s, i))) { s.copyRank = ranksOf[i]; mine.Add(s); }
                swaps.Add($"{cls.Key} -> {with[0].classId}");
            }
            if (swaps.Count > 0) { mine = Shuffled(mine, rng); Plugin.Log.LogInfo("Corrupted copy: barred classes swapped: " + string.Join(", ", swaps)); }
            LastSwaps = swaps;
            // the Nemesis skills of a vanilla class are the nearest thing to its own: they stay when there's little else
            bool vanilla = mine.Count < m.minSkills;
            foreach (Ability_Mob own in c.GetComponentsInChildren<Ability_Mob>(true).Where(a => !(a is ModMobAbility))) own.isAvailable = vanilla;

            int rank = mine.Count > 0 ? mine.Max(s => s.copyRank) : Player.instance.level / 3;
            int extra = Mathf.Max(Mathf.Max(1, m.foreign), m.minSkills - mine.Count);
            List<MobSkillSpec> foreign = strangers.Select(g => g[0]).Take(extra).ToList();
            foreach (MobSkillSpec f in foreign) f.copyRank = rank;
            var kit = mine.Take(Mathf.Max(0, m.skills - foreign.Count)).Concat(foreign).ToList();
            var ranks = Shuffled(kit.Select(s => s.copyRank), rng);
            var order = Shuffled(kit.Select(s => s.priority), rng);

            MobSkillSpec basic = Mirrors.Basic(strength);
            var taught = new List<MobSkillSpec> { Own(basic, seed, basic.copyRank, basic.priority, $"Corrupted {basic.name.Replace("Copied ", "")}") };
            taught.AddRange(kit.Select((s, i) => Own(s, seed, ranks[i], order[i], $"Corrupted {s.name}")));
            foreach (MobSkillSpec s in taught) EnemyAbilities.Attach(c.gameObject, s)?.Init(c);
            c.ai?.GetAbilities();
            LastKit = taught;
            LastForeign = taught.Where(s => foreign.Any(f => f.id == s.id)).ToList();
            Plugin.Log.LogInfo($"Corrupted copy: {string.Join(", ", taught.Select(s => $"{s.name} (rank {s.copyRank}, priority {s.priority})"))}; from other classes: {string.Join(", ", LastForeign.Select(s => s.classId))}");
        }

        // ---- your people, the same way --------------------------------------------------------------------------

        internal static IEnumerable<Character> Escort => Contracts.AllCharacters().Where(c => !c.GetIsDead() && c.HasTag(EscortTag));

        /// <summary>Who you have with you. A summoned copy of you is left out: this boss already is that.</summary>
        private static IEnumerable<Character> Party() =>
            AI_Manager.instance.GetPlayerMinions().Concat(Crew.Npcs()).Where(m => m != null && !m.GetIsDead() && !m.HasTag(Mirrors.Tag)).Distinct();

        private static void SpawnEscort(BossSpec b, NavNode at, float final, float health)
        {
            var rng = new System.Random();
            var copies = new List<Character>();
            foreach (Character src in Party().OrderByDescending(m => m.GetHealthMax()).Take(b.mirror.escort).ToList())
            {
                Character prefab = EnemyRegistry.Resolve(src.internalName);
                NavNode n = Summons.FreeNode(at.transform.position, 3.5f) ?? Summons.FreeNode(at.transform.position, 8f);
                if (prefab == null || n == null) { Plugin.Log.LogWarning($"Corrupted copy of {src.GetName()}: {(prefab == null ? "no creature" : "no free tile")}"); continue; }
                Character c = AI_Manager.instance.Spawn_Character(prefab, n.transform.position, AI.AI_TEAM.Hostile);
                if (c == null) continue;
                Mirrors.Strip(c);
                // a pet's prefab can carry these, and a load registers whoever has them as yours
                c.RemoveTag("MINION");
                c.RemoveTag("THOUGHTLEADER_MINION");
                c.SetTag(EscortTag, 1);
                c.SetTag(FinalTag, Mathf.RoundToInt(final * 100f));
                c.SetTag(EscortOfTag + b.id, 1);
                c.SetTag("NOCORPSE", 1);
                c.AddTag("NOXP");
                c.xpValue = 0;
                foreach (string model in src.tags.Where(t => t?.tagName != null && t.tagName.StartsWith(ModelAssets.TagPrefix)).Select(t => t.tagName).ToList()) ModelAssets.ApplyByTag(c, model);
                Mirrors.CopyStats(c, src, health, -1, r => Resists(r, rng));
                Remember(c);
                if (src.HasTag(Summons.LifeTag)) c.SetTag(EscortLifeTag, src.GetTag(Summons.LifeTag));
                if (src.HasTag(Summons.StayTag)) { c.SetTag(Summons.StayTag, 1); c.FLAG_canMove = false; }
                try
                {
                    if (Crew.IsCrew(src)) Crew.DressLike(c, src);
                    else if (Conjoined.IsBody(src)) Conjoined.DressLike(c, src);
                }
                catch (Exception e) { Plugin.Log.LogWarning($"Corrupted copy of {src.GetName()}: dressing: {e.Message}"); }
                c.SetName("Corrupted " + src.GetName());
                Mirrors.Stain(c, Moves.Hex(b.mirror.look, Color.white), b.mirror.glow);
                EnemyAbilities.FacePlayer(c);
                c.ai?.ChangeState(AI.AI_STATE.Idle);
                Plugin.Log.LogInfo($"Corrupted copy of {src.GetName()}: {c.internalName}, {c.GetHealthMax()} HP against {src.GetHealthMax()}, keeps {final:P0} of a hit");
                copies.Add(c);
            }
            float threat = copies.Sum(c => Threat(c) * c.GetTag(FinalTag) / 100f);
            float limit = Limit(b.mirror);
            Plugin.Log.LogInfo($"Corrupted copies: up to {threat:0} a round, limit {limit:0}");
            if (threat > limit) foreach (Character c in copies) Retune(c, limit / threat, b.mirror);
        }

        // ---- kept in line --------------------------------------------------------------------------------------

        /// <summary>What they may take from you in a round, all of them together.</summary>
        internal static float Limit(MirrorSpec m) => Mathf.Max(1f, Player.instance.GetHealthMax() * m.escortDeadly);

        /// <summary>The hardest thing a copy can do in a hit, before the final cut.</summary>
        internal static float Threat(Character c) => c.GetComponentsInChildren<Ability_Mob>(true)
            .Where(a => a.isAvailable).Select(a => a is ModMobAbility m ? Skill(m.Spec?.actions, c) : Attack(a, c)).DefaultIfEmpty(0f).Max();

        private static float Attack(Ability_Mob a, Character c)
        {
            var hits = a.damageList?.Where(d => d != null).ToList();
            if (hits == null || hits.Count == 0) return 0f;
            int power = Mathf.Max(c.GetStat(STAT_TYPE.MeleePower), c.GetStat(STAT_TYPE.RangedPower), c.GetStat(STAT_TYPE.PsiPower));
            return hits.Sum(d => (d.damage + Math.Max(d.damage, d.damageMax)) / 2f) + Mathf.Max(0, power);
        }

        private static float Skill(List<ActionSpec> list, Character c) => list == null ? 0f : list.Sum(a =>
            ((a.@do ?? "").ToLowerInvariant() == "damage" ? (a.min + Math.Max(a.min, a.max)) / 2f + Actions.StatBonus(a.scale, a.scaleMult, c) : 0f)
            + Skill(a.then, c) + Skill(a.actions, c));

        /// <summary>Its hits down to `keep` of what they are, never under the lowest share; what it lost comes back as toughness.</summary>
        internal static void Retune(Character c, float keep, MirrorSpec m)
        {
            int was = c.GetTag(FinalTag);
            int now = Mathf.Max(Mathf.RoundToInt(m.escortMinDamage * 100f), Mathf.RoundToInt(was * keep));
            if (now >= was) return;
            c.SetTag(FinalTag, now);
            Harden(c, (1f - now / (float)was) * m.escortDefense);
            Plugin.Log.LogInfo($"{c.GetName()}: keeps {now}% of a hit, was {was}%; {c.GetHealthMax()} HP, armour {c.GetStat(STAT_TYPE.Armor)}, dodge {c.GetStat(STAT_TYPE.Dodge)}, block {c.GetStat(STAT_TYPE.Block)}, Void resistance {c.GetResistance(Void)}");
        }

        /// <summary>Half of `gain` more health, and that much against everything. Goes on the base stats, under any effects on it.</summary>
        private static void Harden(Character c, float gain)
        {
            float share = c.GetHealth() / (float)Mathf.Max(1, c.GetHealthMax());
            int hp = Mathf.RoundToInt(c.GetHealthMax() * (1f + gain / 2f));
            int armour = Mathf.RoundToInt(gain * (2f + c.level / 4f)), points = Mathf.RoundToInt(gain * 25f);
            c.RestoreBaseStats();
            Traverse.Create(c).Method("RestoreBaseResistances").GetValue();
            foreach (StatObj st in c.secondaryAttributes.Where(st => st != null))
                if (st.statType == STAT_TYPE.Armor) st.amount += armour;
                else if (st.statType == STAT_TYPE.Dodge || st.statType == STAT_TYPE.Block) st.amount += points;
            foreach (DAMAGE_TYPE t in Types)
            {
                DmgObj guard = c.resistances.FirstOrDefault(r => r != null && r.dmgType == t);
                if (guard != null) guard.damage = Mathf.Min(100, guard.damage + points);
                else c.resistances.Add(new DmgObj(t, points));
            }
            if (c is Mob mob) mob.healthMax = hp;
            c.SetStat(STAT_TYPE.HealthMax, hp);
            Remember(c);
            c.CacheBaseStats();
            c.CalculateStats();
            int max = Mathf.Max(1, c.GetHealthMax());
            c.SetHealth(Mathf.Clamp(Mathf.RoundToInt(max * share), 1, max), max);
        }

        /// <summary>Its base stats as they stand, for after a load. Call with no effects counted in.</summary>
        private static void Remember(Character c)
        {
            foreach (StatObj st in c.primaryAttributes.Concat(c.secondaryAttributes).Where(st => st != null && st.statType != STAT_TYPE.HealthMax))
                c.SetTag(StatTag + st.statType, st.amount);
            foreach (DmgObj r in c.resistances.Where(r => r != null)) c.SetTag(GuardTag + r.dmgType, r.damage);
        }

        /// <summary>A save brings a creature back with its prefab's stats: these are the ones it was given.</summary>
        private static void Recall(Character c)
        {
            int left = c.GetHealth();
            c.RestoreBaseStats();
            foreach (StatObj st in c.primaryAttributes.Concat(c.secondaryAttributes).Where(st => st != null))
                if (c.HasTag(StatTag + st.statType)) st.amount = c.GetTag(StatTag + st.statType);
            c.resistances = Types.Where(t => c.HasTag(GuardTag + t)).Select(t => new DmgObj(t, c.GetTag(GuardTag + t))).ToList();
            c.CacheBaseStats();
            c.CalculateStats();
            int max = Mathf.Max(1, c.GetHealthMax());
            c.SetHealth(Mathf.Clamp(left, 1, max), max);
        }

        /// <summary>What they took from you since the last round.</summary>
        internal static int RoundTaken;

        /// <summary>The estimate only knows their attacks; this is what they really did. A round well over the limit tightens it.</summary>
        private static void WatchEscort()
        {
            int taken = RoundTaken;
            RoundTaken = 0;
            var copies = Escort.ToList();
            MirrorSpec m = SideAreaData.Bosses.FirstOrDefault(b => copies.Any(c => c.HasTag(EscortOfTag + b.id)))?.mirror;
            if (m == null || taken <= Limit(m) * 1.25f) return;
            Plugin.Log.LogInfo($"Corrupted copies: took {taken} in a round, limit {Limit(m):0}");
            foreach (Character c in copies) Retune(c, Limit(m) / taken, m);
        }

        /// <summary>A timed summon's copy runs out when the summon would have.</summary>
        internal static void TickEscort()
        {
            WatchEscort();
            foreach (Character c in Escort.Where(c => c.HasTag(EscortLifeTag)).ToList())
            {
                int left = c.GetTag(EscortLifeTag) - 1;
                if (left > 0) c.SetTag(EscortLifeTag, left);
                else c.Die_Silent();
            }
        }

        /// <summary>They only stood up because it did.</summary>
        internal static void EscortFalls()
        {
            foreach (Character c in Escort.ToList()) c.Die_Silent();
        }

        /// <summary>A reloaded side area: the glow and their stats aren't saved.</summary>
        internal static void RelightEscort()
        {
            foreach (Character c in Escort.ToList())
            {
                Recall(c);
                BossSpec b = SideAreaData.Bosses.FirstOrDefault(x => c.HasTag(EscortOfTag + x.id));
                if (b != null) Mirrors.Stain(c, Moves.Hex(b.mirror.look, Color.white), b.mirror.glow);
            }
        }

        /// <summary>The attacker behind the hit being worked out, while one of these is (a hit can set off another).</summary>
        private static Character hitter;

        /// <summary>Everything a copy of your people lands, its own skills and a vanilla creature's alike, is Void.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.TakeDamage), new[] { typeof(Character), typeof(List<DmgObj>), typeof(string), typeof(Ability), typeof(float) })]
        private static class Patch_Hits
        {
            private static void Prefix(Character attacker, ref List<DmgObj> dmgList, out Character __state)
            {
                __state = hitter;
                hitter = attacker != null && attacker.HasTag(FinalTag) ? attacker : null;
                if (hitter == null || dmgList == null || !hitter.HasTag(EscortTag)) return;
                dmgList = dmgList.Where(d => d != null).Select(d => new DmgObj(Void, d.damage) { damageMax = d.damageMax }).ToList();
            }

            private static void Postfix(Character __state) => hitter = __state;
        }

        /// <summary>Resistance is the last thing a hit goes through before it's added up and shown: the cut comes here.</summary>
        [HarmonyPatch(typeof(Character), "ApplyResistance")]
        private static class Patch_FinalCut
        {
            private static void Postfix(Character __instance, List<DmgObj> __result)
            {
                if (hitter == null || __result == null) return;
                float keep = hitter.GetTag(FinalTag) / 100f;
                foreach (DmgObj d in __result)
                    if (d != null && d.damage > 0)
                    {
                        CutFrom = d.damage;
                        d.damage = CutTo = Math.Max(1, Mathf.RoundToInt(d.damage * keep));
                        string who = hitter.GetName();
                        Dealt[who] = (Dealt.TryGetValue(who, out int was) ? was : 0) + d.damage;
                        if (hitter.HasTag(EscortTag) && __instance == Player.instance) RoundTaken += d.damage;
                    }
            }
        }

        // ---- what it leaves -----------------------------------------------------------------------------------

        private static bool Corruptible(Item i) =>
            i is Item_Equipment e && !e.HasTag("QUEST") && !e.HasTag(ItemTag) && !e.inPlayerStash
            && (e.stats.Any(s => s != null && s.amount != 0) || e.resistanceMods.Any(r => r != null) || e is Item_Weapon w && w.damageList.Any(d => d != null));

        internal static List<Item_Equipment> DropCandidates() =>
            Menu_Player.instance.GetEquipment().Cast<Item>().Concat(Menu_Inventory.GetItems()).Where(Corruptible).Cast<Item_Equipment>().Distinct().ToList();

        /// <summary>Corrupted copies of some of what you wear and carry. Yours stay yours.</summary>
        internal static List<Item> DropGear(BossSpec b, NavNode at)
        {
            var made = new List<Item>();
            foreach (Item_Equipment src in DropCandidates().OrderBy(_ => UnityEngine.Random.value).Take(b.mirror.gearDrops))
            {
                Item it = Copy(src, b.mirror.gearPower);
                if (it == null) continue;
                Echoes.Spew(it, at);
                made.Add(it);
                Game.LOG($"<color={b.color}>Your {src.GetName()} lies among the shards, changed.</color>");
            }
            return made;
        }

        private static int Stronger(int amount, float power) => amount > 0 ? Mathf.CeilToInt(amount * (1f + power)) : amount;

        /// <summary>The item as it stands, a new one in the world, with its bonuses stronger and switched around.</summary>
        internal static Item Copy(Item_Equipment src, float power)
        {
            var info = (ItemSaveInfo)src.Serialize();
            Item it = ItemDatabase.instance.GetItem(src.itemId);
            if (!(it is Item_Equipment eq)) { if (it != null) UnityEngine.Object.Destroy(it.gameObject); return null; }
            string id = it.itemInstanceId;
            Vector3 scale = it.transform.localScale;
            info.inPlayerStash = false;
            it.Deserialize(info);
            it.itemInstanceId = id;
            it.transform.localScale = scale;

            var stats = eq.stats.Where(s => s != null).ToList();
            var amounts = stats.Select(s => Stronger(s.amount, power)).ToList();
            eq.stats = stats.Select((s, i) => new StatObj(s.statType, amounts[(i + 1) % amounts.Count])).ToList();

            var guards = eq.resistanceMods.Where(r => r != null).ToList();
            var open = Types.Where(t => guards.All(r => r.dmgType != t)).OrderBy(_ => UnityEngine.Random.value).ToList();
            eq.resistanceMods = guards.Select((r, i) => new DmgObj(i < open.Count ? open[i] : r.dmgType, Stronger(r.damage, power))).ToList();

            if (eq is Item_Weapon w && w.damageList.Any(d => d != null))
            {
                var hits = w.damageList.Where(d => d != null).ToList();
                w.damageList = new List<DmgObj> { new DmgObj(Void, Stronger(hits.Sum(d => d.damage), power)) { damageMax = Stronger(hits.Sum(d => Math.Max(d.damage, d.damageMax)), power) } };
            }
            it.tags.Add(new Tag(ItemTag, 1));
            it.itemValue = Stronger(it.itemValue, power);
            TwistGrants(eq);
            return it;
        }

        private static List<Item_Equipment> granters;

        /// <summary>Every item in the game that grants a skill or an effect when worn. The Cursed Relic's only work for the Cursed.</summary>
        internal static List<Item_Equipment> Granters => granters ?? (granters = typeof(ItemDatabase).GetFields()
            .Where(f => f.FieldType == typeof(ItemDatabase.DatabaseItem[]))
            .SelectMany(f => (ItemDatabase.DatabaseItem[])f.GetValue(ItemDatabase.instance) ?? new ItemDatabase.DatabaseItem[0])
            .Select(d => d.item as Item_Equipment)
            .Where(e => e != null && !(e is Item_Cursed_Relic) && e.equipTrigger != null && (e.equipTrigger.equipment_ability != null || !string.IsNullOrEmpty(e.equipTrigger.equipment_effect)))
            .GroupBy(e => e.itemId).Select(g => g.First()).ToList());

        /// <summary>
        /// The corrupted version of its skill if it has one. Mods take the place of bonuses: some with a new skill, more
        /// without, where they replace what the item did when worn as well (it keeps its skill).
        /// </summary>
        private static void TwistGrants(Item_Equipment eq)
        {
            CorruptGearSpec gear = SideAreaData.CorruptGear;
            string skill = gear.AltFor(eq);
            if (skill != null) eq.tags.Add(new Tag(SkillTag + skill, 1));
            var lines = eq.stats.Where(st => st != null).OrderBy(_ => UnityEngine.Random.value).ToList();
            foreach (string mod in gear.modIds.OrderBy(_ => UnityEngine.Random.value).Take(skill != null ? gear.modsWithSkill : gear.mods))
            {
                eq.tags.Add(new Tag(ModTag + mod, 1));
                if (lines.Count <= 1) continue; // something of the item stays
                eq.stats.Remove(lines[0]);
                lines.RemoveAt(0);
            }
            Grant(eq, eq.tags);
        }

        /// <summary>Puts on the item what its tags say it has instead.</summary>
        private static void Grant(Item_Equipment eq, List<Tag> tags)
        {
            if (tags == null || !tags.Any(t => t?.tagName == ItemTag)) return;
            Item_OnUse_Trigger on = eq.equipTrigger;
            if (on == null) return;
            Tag skill = tags.FirstOrDefault(t => t?.tagName != null && t.tagName.StartsWith(SkillTag));
            if (skill != null) GiveSkill(on, skill.tagName.Substring(SkillTag.Length));
            else on.equipment_effect = "";
        }

        private static void GiveSkill(Item_OnUse_Trigger on, string abId)
        {
            if (on.equipment_ability == null || on.equipment_ability.internalName == abId || !ModAbility.Specs.TryGetValue(abId, out ModAbility.Runtime rt)) return;
            if (rt.icon == null) rt.icon = on.equipment_ability.GetSprite();
            UnityEngine.Object.Destroy(on.equipment_ability.gameObject);
            var go = new GameObject(ModAbility.Prefix + abId);
            go.transform.SetParent(on.transform, false);
            on.equipment_ability = go.AddComponent<ModAbility>();
        }

        private static IEnumerable<string> ModsOn(Item it) =>
            it.tags.Where(t => t?.tagName != null && t.tagName.StartsWith(ModTag)).Select(t => t.tagName.Substring(ModTag.Length));

        /// <summary>A mod works while the item is worn, and a load brings its effect back with the character's own.</summary>
        [HarmonyPatch(typeof(Item_Equipment), nameof(Item_Equipment.OnEquip))]
        private static class Patch_ModsOn
        {
            private static void Postfix(Item_Equipment __instance)
            {
                if (Game.Game_isLoading) return;
                foreach (string mod in ModsOn(__instance))
                    if (Player.instance.effects.GetEffect(mod) == null) EffectManager.instance.ApplyEffect(mod, Player.instance);
            }
        }

        [HarmonyPatch(typeof(Item_Equipment), nameof(Item_Equipment.OnUnequip))]
        private static class Patch_ModsOff
        {
            private static void Postfix(Item_Equipment __instance)
            {
                foreach (string mod in ModsOn(__instance))
                    if (!Menu_Player.instance.GetEquipment().Any(e => e != null && e != __instance && ModsOn(e).Contains(mod)))
                        Player.instance.effects.RemoveEffect(mod, isInstant: true);
            }
        }

        private static string ModLines(Item it) => string.Concat(ModsOn(it).Where(ModEffect.Specs.ContainsKey).Select(m =>
            $"\n<color=#B46CFF>{ModEffect.Specs[m].spec.name}</color>: {ModEffect.Specs[m].spec.description}"));

        [HarmonyPatch(typeof(Item_Equipment), nameof(Item_Equipment.GetStatString))]
        private static class Patch_ModText
        {
            private static void Postfix(Item_Equipment __instance, ref string __result) => __result += ModLines(__instance);
        }

        [HarmonyPatch(typeof(Item_Weapon), nameof(Item_Weapon.GetStatString))]
        private static class Patch_ModTextWeapon
        {
            private static void Postfix(Item_Weapon __instance, ref string __result) => __result += ModLines(__instance);
        }

        /// <summary>Before the game reads the saved skill's state back in: that state is the donor's skill's.</summary>
        [HarmonyPatch(typeof(Item_Equipment), nameof(Item_Equipment.Deserialize))]
        private static class Patch_GrantsBack
        {
            private static void Prefix(Item_Equipment __instance, object d)
            {
                if (d is ItemSaveInfo info) Grant(__instance, info.tags);
            }
        }

        /// <summary>The prefix goes on when the name is read: an epic's own prefix is put back on every load.</summary>
        [HarmonyPatch(typeof(Item), nameof(Item.GetName), typeof(bool))]
        private static class Patch_Name
        {
            private static void Postfix(Item __instance, ref string __result)
            {
                if (__instance.HasTag(ItemTag)) __result = "Corrupted " + __result;
            }
        }
    }
}
