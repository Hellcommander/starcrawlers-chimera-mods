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
    /// </summary>
    internal static class Alchemy
    {
        internal class Entry
        {
            public ReagentReaction spec;
            public string classId;
            public string[] ids;
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
                .Select(r => new Entry { spec = r, classId = def.id, ids = r.needs.Select(n => Actions.EffectId(n, def.id)).ToArray() }).ToList();
            if (list.Count > 0) byClass[def.id] = list;
            reagents = new HashSet<string>(byClass.Values.SelectMany(l => l).SelectMany(e => e.ids));
        }

        /// <summary>The effect takes part in some class's reaction (rune element swaps replace these).</summary>
        internal static bool IsReagent(string effectId) => effectId != null && reagents.Contains(effectId);

        /// <summary>Reactions of the classes the player has now.</summary>
        internal static List<Entry> Active()
        {
            var trees = Player_SkillControl.instance?.skillTrees;
            if (trees == null) return new List<Entry>();
            return trees.Where(t => t != null && byClass.ContainsKey(t.internalName)).SelectMany(t => byClass[t.internalName]).ToList();
        }

        internal static List<Entry> OfClass(string classId) => classId != null && byClass.TryGetValue(classId, out var l) ? l : new List<Entry>();

        private static bool IsEnemy(Character c) =>
            c != null && !c.GetIsDead() && c != Player.instance && c.ai != null && c.ai.GetTeam() != AI.AI_TEAM.Player && c.ai.GetTeam() != AI.AI_TEAM.Passive;

        private static int depth;

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
                    Entry hit = Active().FirstOrDefault(e => e.ids.Contains(internalName)
                        && e.ids.Where(id => id != internalName).All(id => target.effects.GetEffect(id) != null));
                    if (hit == null) return true;
                    if (Time.time > burstReset) { burst = 0; burstReset = Time.time + 1f; }
                    if (++burst > MaxBurst) return true;
                    depth++;
                    try { React(hit, target, internalName); }
                    finally { depth--; }
                    __result = null;
                    return !hit.spec.consume; // a reaction that keeps its reagents lets the new one land too
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
            if (r.consume)
            {
                foreach (string id in e.ids.Where(id => id != incoming)) t.effects.GetEffect(id)?.RemoveInstant();
                t.CalculateStats();
            }
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
            foreach (Character v in victims) if (v != null && !v.GetIsDead()) { ctx.target = v; Actions.Run(rest, ctx, v); }
            if (Player.instance != null) Actions.Run(self, ctx, Player.instance);
            Actions.FlushDeferred(ctx);
        }

        /// <summary>"Fire + Shock: Plasma Flash" lines for the rune panel and the README.</summary>
        internal static string Describe(Entry e) =>
            $"{string.Join(" + ", e.spec.needs.Select(n => Runes.EffectName(n, e.classId)))}: <color=#7FE3FF>{e.spec.name}</color>" +
            (string.IsNullOrEmpty(e.spec.description) ? "" : " - " + e.spec.description);
    }
}
