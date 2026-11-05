using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>An alchemical reaction from a class's "reactions" list.</summary>
    public class ReagentReaction
    {
        public string name;
        public string description;
        /// <summary>The effects that react: "@Residue" (this class), "#X" (library), vanilla ids such as "EFFECT_Fire".</summary>
        public List<string> needs = new List<string>();
        /// <summary>The reacting effects are used up (default).</summary>
        public bool consume = true;
        /// <summary>Reagents that are not used up (catalysts): the reaction happens and these stay on, or land anyway.</summary>
        public List<string> keep;
        /// <summary>The class rank it is known from (1 = just taken; 0 or 1 = always). Earlier, the reagents simply sit side by side.</summary>
        public int rank;
        /// <summary>% odds the contact reacts at all (default 100); a failed roll lets the reagent land like any other effect.</summary>
        public int chance = 100;
        /// <summary>Added to chance for every class rank past the first.</summary>
        public int chancePerRank;
        /// <summary>The actions hit every enemy within this radius of the target (0 = the target only).</summary>
        public float radius;
        public string vfx;
        public string sound;
        public List<ActionSpec> actions = new List<ActionSpec>();
    }

    /// <summary>
    /// Alchemical reactions: when an effect lands on an enemy that already carries the effect it reacts with, the pair
    /// reacts on contact: both are used up and the reaction's actions run, with you as the caster. Reactions
    /// belong to a class and only happen while you have that class, whoever applied the effects (your weapons, other
    /// classes, vanilla fire and shock all count). A reaction can set off another; a short burst limit stops loops.
    /// A reaction with a "rank" is known once the class reaches it; "keep" names reagents the reaction leaves alone;
    /// "chance" makes the contact a roll.
    /// </summary>
    internal static class Alchemy
    {
        internal class Entry
        {
            public ReagentReaction spec;
            public string classId;
            public string[] ids;
            public string[] keepIds;
        }

        private static readonly Dictionary<string, List<Entry>> byClass = new Dictionary<string, List<Entry>>();
        private static HashSet<string> reagents = new HashSet<string>();
        private static int burst;
        private static float burstReset;
        private const int MaxBurst = 12;

        internal static void Register(ClassDef def)
        {
            if (def?.id == null) return;
            byClass.Remove(def.id);
            var list = (def.reactions ?? new List<ReagentReaction>())
                .Where(r => r?.needs != null && r.needs.Count >= 2)
                .Select(r => new Entry
                {
                    spec = r, classId = def.id,
                    ids = r.needs.Select(n => Actions.EffectId(n, def.id)).ToArray(),
                    keepIds = r.consume ? (r.keep ?? new List<string>()).Select(n => Actions.EffectId(n, def.id)).ToArray()
                                        : r.needs.Select(n => Actions.EffectId(n, def.id)).ToArray()
                }).ToList();
            if (list.Count > 0) byClass[def.id] = list;
            reagents = new HashSet<string>(byClass.Values.SelectMany(l => l).SelectMany(e => e.ids));
        }

        /// <summary>The effect takes part in some class's reaction (rune element swaps replace these).</summary>
        internal static bool IsReagent(string effectId) => effectId != null && reagents.Contains(effectId);

        /// <summary>Reactions of the classes the player has now, known or not yet (see Known).</summary>
        internal static List<Entry> Active()
        {
            var trees = Player_SkillControl.instance?.skillTrees;
            if (trees == null) return new List<Entry>();
            return trees.Where(t => t != null && byClass.ContainsKey(t.internalName)).SelectMany(t => byClass[t.internalName]).ToList();
        }

        internal static List<Entry> OfClass(string classId) => classId != null && byClass.TryGetValue(classId, out var l) ? l : new List<Entry>();

        /// <summary>The class's level (rank + 1) for the player, 0 without the class.</summary>
        private static int Level(string classId)
        {
            var trees = Player_SkillControl.instance?.skillTrees;
            if (trees == null) return 0;
            foreach (Skill_Tree t in trees) if (t != null && t.internalName == classId) return t.GetRank() + 1;
            return 0;
        }

        /// <summary>The class has reached the reaction's rank.</summary>
        internal static bool Known(Entry e) => e.spec.rank <= 1 || Level(e.classId) >= e.spec.rank;

        private static bool Rolls(Entry e)
        {
            int odds = e.spec.chance + e.spec.chancePerRank * Math.Max(0, Level(e.classId) - 1);
            return odds >= 100 || UnityEngine.Random.Range(1, 101) <= odds;
        }

        private static bool IsEnemy(Character c) =>
            c != null && !c.GetIsDead() && c != Player.instance && c.ai != null && c.ai.GetTeam() != AI.AI_TEAM.Player && c.ai.GetTeam() != AI.AI_TEAM.Passive;

        private static int depth;

        private static bool PartnersOn(Entry e, string incoming, Character target)
        {
            foreach (string id in e.ids)
                if (id != incoming && target.effects.GetEffect(id) == null) return false;
            return true;
        }

        /// <summary>The first known reaction, in class and file order, this reagent sets off on the target. Every hit of a fire or shock skill asks, whoever casts it, so it walks the lists instead of building them.</summary>
        private static Entry ReactionFor(string incoming, Character target)
        {
            Skill_Tree[] trees = Player_SkillControl.instance?.skillTrees;
            if (trees == null) return null;
            foreach (Skill_Tree t in trees)
            {
                if (t == null || !byClass.TryGetValue(t.internalName, out List<Entry> list)) continue;
                int level = t.GetRank() + 1;
                foreach (Entry e in list)
                    if ((e.spec.rank <= 1 || level >= e.spec.rank) && Array.IndexOf(e.ids, incoming) >= 0 && PartnersOn(e, incoming, target)) return e;
            }
            return null;
        }

        /// <summary>
        /// On contact: a reagent landing on an enemy that carries its partner reacts instead of being applied (the
        /// partner is used up). Checked before the game applies it, because some vanilla statuses cancel each other
        /// (chill puts out fire) and would never sit side by side. The first matching reaction in file order wins.
        /// </summary>
        [HarmonyPatch(typeof(EffectManager), nameof(EffectManager.ApplyEffect), new[] { typeof(string), typeof(Character) })]
        private static class Patch_Apply
        {
            private static bool Prefix(string internalName, Character target, ref Effect __result)
            {
                if (byClass.Count == 0 || depth > 3 || !reagents.Contains(internalName) || !IsEnemy(target) || Game.Game_isLoading) return true;
                try
                {
                    Entry hit = ReactionFor(internalName, target);
                    if (hit == null || !Rolls(hit)) return true;
                    if (Time.time > burstReset) { burst = 0; burstReset = Time.time + 1f; }
                    if (++burst > MaxBurst) return true;
                    void Go()
                    {
                        if (target.GetIsDead()) return;
                        depth++;
                        try { React(hit, target, internalName); }
                        finally { depth--; }
                    }
                    if (!TileStackAreas.AfterSweep(Go)) Go();
                    // a reaction that keeps this reagent lets it land too
                    if (Array.IndexOf(hit.keepIds, internalName) >= 0) return true;
                    __result = null;
                    return false;
                }
                catch (Exception ex) { Plugin.Log.LogError("Reactions: " + ex); return true; }
            }
        }

        /// <summary>The last few reactions ("name on target"), for tests.</summary>
        internal static readonly List<string> Recent = new List<string>();

        /// <summary>Runs a reaction on t. "incoming" is the reagent that set it off (not on t yet, so not removed).</summary>
        internal static void React(Entry e, Character t, string incoming = null)
        {
            ReagentReaction r = e.spec;
            Recent.Add(r.name);
            if (Recent.Count > 40) Recent.RemoveAt(0);
            bool removed = false;
            foreach (string id in e.ids)
            {
                if (id == incoming || Array.IndexOf(e.keepIds, id) >= 0) continue;
                Effect fx = t.effects.GetEffect(id);
                if (fx == null) continue;
                fx.RemoveInstant();
                removed = true;
            }
            if (removed) t.CalculateStats();
            Vector3 at = t.GetHitFx().position;
            Actions.SpawnVfx(r.vfx, at);
            ModAudio.Play(r.sound, at, e.classId);
            Game.LOG($"<color=#7FE3FF>{r.name}!</color> {t.GetName()} reacts.");
            var ctx = new ActionContext { caster = Player.instance, classId = e.classId, sourceName = r.name, center = t.transform.position };
            var victims = new List<Character> { t };
            NavNode n = t.GetMover()?.GetCurrentNavNode();
            if (r.radius > 0f && n != null)
                victims.AddRange(AI_Manager.instance.GetEnemiesAt(n.transform.position, r.radius).Where(v => v != t));
            var self = r.actions.Where(a => (a.to ?? "").ToLowerInvariant() == "self").ToList();
            var rest = r.actions.Except(self).ToList();
            if (self.Count == 0) HitLane.Each(victims, rest, ctx); // a wide reaction in a crowd rolls over it like any other area hit
            else foreach (Character v in victims) if (v != null && !v.GetIsDead()) { ctx.target = v; Actions.Run(rest, ctx, v); }
            if (Player.instance != null) Actions.Run(self, ctx, Player.instance);
            Actions.FlushDeferred(ctx);
        }

        /// <summary>"Fire + Shock: Plasma Flash" lines for the rune panel and the README.</summary>
        internal static string Describe(Entry e) =>
            $"{string.Join(" + ", e.spec.needs.Select(n => Runes.EffectName(n, e.classId)))}: <color=#7FE3FF>{e.spec.name}</color>" +
            (string.IsNullOrEmpty(e.spec.description) ? "" : " - " + e.spec.description);

        /// <summary>What a known reaction costs or spares: "(kept)" reagents, odds, rank.</summary>
        internal static string Terms(Entry e)
        {
            var bits = new List<string>();
            if (!e.spec.consume) bits.Add("nothing used up");
            else if (e.spec.keep != null && e.spec.keep.Count > 0) bits.Add(string.Join(" and ", e.spec.keep.Select(k => Runes.EffectName(k, e.classId))) + " stays");
            int odds = e.spec.chance + e.spec.chancePerRank * Math.Max(0, Level(e.classId) - 1);
            if (odds < 100) bits.Add($"{odds}% odds");
            if (e.spec.radius > 0f) bits.Add($"area {e.spec.radius:0.#}");
            return bits.Count == 0 ? "" : " (" + string.Join(", ", bits) + ")";
        }

        /// <summary>For an effect's tooltip: the known reactions it takes part in, for the classes the player has.</summary>
        internal static IEnumerable<string> TooltipLines(string effectId)
        {
            if (string.IsNullOrEmpty(effectId) || !reagents.Contains(effectId) || Player_SkillControl.instance == null) yield break;
            foreach (Entry e in Active())
            {
                if (Array.IndexOf(e.ids, effectId) < 0) continue;
                string others = string.Join(" + ", e.spec.needs.Where((n, i) => e.ids[i] != effectId).Select(n => Runes.EffectName(n, e.classId)));
                yield return Known(e)
                    ? $"<color=#7FE3FF>Reacts with {others}: {e.spec.name}</color>{Terms(e)}"
                    : $"<color=#8A96A3>Reacts with {others} at rank {e.spec.rank}: {e.spec.name}</color>";
            }
        }
    }
}
