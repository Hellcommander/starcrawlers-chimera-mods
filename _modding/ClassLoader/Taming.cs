using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// "tame": bind a hostile creature to you for good, the way the Hacker's Command Override takes a robot, as a pet of
    /// your own (it doesn't use up the vanilla pet limit). The odds are the action's amount plus its stat scaling, better
    /// the more wounded the creature is; the log shows them. On success the "then" actions run on the new pet, on a
    /// failure the "else" ones (a backlash). Bosses and elites can't be tamed. With "summonGroup" + "maxSummons" the
    /// oldest pet of the group goes wild again when a new one is tamed past the cap.
    /// Effects with "ownerShare" (0.5 = half) give their bearer that share of its owner's attributes, kept current
    /// every turn: a tamed beast fights with half your Strength, Smarts and the rest. Tamed creatures regenerate health
    /// by their own Health Regen the way you do, and heals reach them like any pet. Taming counts as the kill: you get its
    /// experience, and a quest target or contract mark is counted as down.
    /// </summary>
    internal static class Taming
    {
        internal const string Tag = "SCCL_TAMED";
        private static readonly STAT_TYPE[] Shared =
            { STAT_TYPE.Strength, STAT_TYPE.Toughness, STAT_TYPE.Agility, STAT_TYPE.Recognition, STAT_TYPE.Smarts, STAT_TYPE.Savvy, STAT_TYPE.PsiPower };

        /// <summary>The odds before the roll: base (amount + scaling) plus up to half again for a badly wounded target.</summary>
        internal static int Odds(int baseChance, Character t)
        {
            float missing = 1f - t.GetHealth() / (float)Math.Max(1, t.GetHealthMax());
            return Mathf.Clamp(Mathf.RoundToInt(baseChance + 50f * missing), 5, 95);
        }

        internal static void Tame(ActionSpec a, ActionContext ctx, Character t, int baseChance)
        {
            Character me = ctx.caster;
            if (me == null || me.GetIsDead() || t == null || t.GetIsDead() || t.ai == null || EffectManager.instance == null)
            { Plugin.Log.LogInfo($"{ctx.sourceName}: no tame (caster {(me == null ? "missing" : me.GetName())}, target {(t == null ? "missing" : t.GetName())})"); return; }
            if (t.ai.GetBaseTeam() == AI.AI_TEAM.Player || t.HasTag(Tag)) { Plugin.Log.LogInfo($"{ctx.sourceName}: {t.GetName()} is already on your side"); return; }
            if (t.HasTag("BOSS") || Elites.IsElite(t))
            {
                Game.LOG($"{ctx.sourceName}: {t.GetName()} is far too strong-willed to be bound.");
                Plugin.Log.LogInfo($"{ctx.sourceName}: {t.GetName()} can't be tamed (boss or elite)");
                Actions.Run(a.@else, ctx, t);
                return;
            }
            int odds = Odds(baseChance, t);
            if (Random.Range(0, 100) >= odds)
            {
                Game.LOG($"<color=#FF9F5C>[{odds}%] {t.GetName()} throws off {ctx.sourceName}!</color>");
                Plugin.Log.LogInfo($"{ctx.sourceName}: {t.GetName()} threw it off ({odds}%)");
                Actions.Run(a.@else, ctx, t);
                return;
            }

            // past the cap, the oldest of the group goes wild again
            string group = string.IsNullOrEmpty(a.summonGroup) ? null : Summons.GroupTag(a.summonGroup);
            if (group != null && a.maxSummons > 0)
                foreach (Character old in Summons.Mine(group).Where(m => m.HasTag(Tag)).Skip(Math.Max(0, a.maxSummons - 1)).ToList())
                    Release(old);

            // it is out of the fight for good: you get what its kill was worth, and quests and contracts count it as down
            int xp = t.HasTag("NOXP") ? 0 : t.xpValue;
            if (t.questTarget != null) { t.questTarget.Killed(); t.questTarget = null; }
            Contracts.MarkDown(t);
            if (xp > 0 && me == Player.instance) Player.instance.AddXp(xp, t.level, "enemy");
            t.xpValue = 0;
            t.AddTag("NOXP"); // its experience is spent: if it slips the bond and you kill it later, it gives none

            Effect_Charm charm = EffectManager.instance.ApplyCharm(t, me);
            if (charm != null)
            {
                charm.duration = -1;
                charm.displayName = a.summonName ?? "Bound";
                charm.description = $"Bound to {me.GetName()} by {ctx.sourceName}.";
            }
            t.AddTag(Tag);
            t.SetTag("MINION", 1);
            t.SetTag(Summons.ModTag, 1);
            if (group != null) t.SetTag(group, 1);
            AI_Manager.instance.RegisterMinion(t);
            t.FLAG_canMove = true;
            if (When.InCombat()) t.ai.ChangeState(AI.AI_STATE.Active); // it fights for you straight away
            t.SetHealth(t.GetHealthMax());
            if (!string.IsNullOrEmpty(a.effect)) Actions.Run(new[] { new ActionSpec { @do = "effect", effect = a.effect } }, ctx, t);
            Refresh(t);
            Actions.SpawnVfx(string.IsNullOrEmpty(a.summonVfx) ? "PA_Engineer_Boltron.PS_summon" : a.summonVfx, t.GetHitFx().position);
            Game.LOG($"<color=#9FE8FF>[{odds}%] {t.GetName()} is bound to you!</color>");
            Plugin.Log.LogInfo($"{ctx.sourceName}: tamed {t.name} ({odds}%, {xp} xp)");
            Actions.Run(a.then, ctx, t);
        }

        /// <summary>Lets a tamed creature go: it turns on you again.</summary>
        internal static void Release(Character c)
        {
            if (c == null) return;
            c.effects.GetEffect("CHARM")?.RemoveInstant();
            c.RemoveTag(Tag);
            c.RemoveTag("MINION");
            c.RemoveTag(Summons.ModTag);
            // NOXP stays: it was already paid out when it was tamed
            foreach (Tag tg in c.tags.Where(x => x?.tagName != null && x.tagName.StartsWith("SCCLS_")).ToList()) c.RemoveTag(tg.tagName);
            if (c.ai != null) c.ai.SetTeam(c.ai.GetBaseTeam());
            Game.LOG($"<color=#FF9F5C>{c.GetName()} slips its bond and turns on you!</color>");
        }

        /// <summary>Re-reads the owner's attributes into every "ownerShare" effect on this creature.</summary>
        internal static void Refresh(Character c)
        {
            if (c == null || c.GetIsDead()) return;
            bool changed = false;
            foreach (Effect e in c.effects.GetEffects())
            {
                if (!(e is ModEffect me) || !ModEffect.Specs.TryGetValue(me.internalName, out var rt) || rt.spec.ownerShare <= 0f) continue;
                Character owner = e.caster ?? Player.instance;
                if (owner == null) continue;
                var mods = (rt.spec.stats ?? new Dictionary<string, int>())
                    .Select(kv => Enum.TryParse(kv.Key, true, out STAT_TYPE st) ? new StatObj(st, kv.Value) : null).Where(x => x != null).ToList();
                foreach (STAT_TYPE st in Shared)
                {
                    int add = Mathf.RoundToInt(owner.GetStat(st) * rt.spec.ownerShare);
                    if (add == 0) continue;
                    StatObj have = mods.FirstOrDefault(m => m.statType == st);
                    if (have != null) have.amount += add; else mods.Add(new StatObj(st, add));
                }
                me.attributeMods = mods;
                changed = true;
            }
            if (changed) c.CalculateStats();
        }

        private static readonly Dictionary<int, int> regenClock = new Dictionary<int, int>();

        /// <summary>A tamed creature heals the way you do: 1 HP every (8 - Health Regen) turns, and past 7 more per turn.</summary>
        private static void Regen(Character c)
        {
            if (c.GetHealth() >= c.GetHealthMax() || c.HasTag("NOHEAL")) return;
            int regen = c.GetStat(STAT_TYPE.HealthRegen);
            int id = c.GetInstanceID();
            if (regen < HealthRegen.Cap)
            {
                int every = HealthRegen.Base - Mathf.Max(0, regen);
                regenClock[id] = (regenClock.TryGetValue(id, out int k) ? k : 0) + 1;
                if (regenClock[id] < every) return;
                regenClock[id] = 0;
                c.Heal(1, false, "TURN");
                return;
            }
            for (int i = 0; i < 1 + HealthRegen.Extra(regen) && c.GetHealth() < c.GetHealthMax(); i++) c.Heal(1, false, "TURN");
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
        private static class Patch_Turn
        {
            private static void Postfix()
            {
                if (AI_Manager.instance == null || Game.STASIS) return;
                foreach (Character m in AI_Manager.instance.GetPlayerMinions())
                {
                    if (m == null || m.GetIsDead() || m.effects == null) continue;
                    if (m.effects.GetEffects().Any(e => e is ModEffect me && ModEffect.Specs.TryGetValue(me.internalName, out var rt) && rt.spec.ownerShare > 0f))
                        Refresh(m);
                    if (m.HasTag(Tag)) Regen(m);
                }
            }
        }
    }
}
