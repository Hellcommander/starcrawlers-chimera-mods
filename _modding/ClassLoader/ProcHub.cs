using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>Dredmor-style triggers: routes game events to every ModAbility the player knows and every ModEffect on the player.</summary>
    internal static class ProcHub
    {
        /// <summary>What the current event is about, for "match" (an ability name, an effect id).</summary>
        private static string detail;
        /// <summary>The ally in the current event (allyHurt / allyKill / summon / allyDeath), for "to"/"of": "ally".</summary>
        private static Character ally;

        /// <summary>Per-proc bookkeeping for cooldown / every / maxPerTurn (per bearer, so two summons don't share).</summary>
        private class ProcState { public int seen, readyAt, firedThisTurn, turnStamp; }
        private static readonly Dictionary<(ProcSpec, Character), ProcState> states = new Dictionary<(ProcSpec, Character), ProcState>();
        internal static int TurnCount;

        public static void Fire(string on, Character other, string what, Character withAlly = null)
        {
            string prev = detail;
            Character prevAlly = ally;
            detail = what;
            ally = withAlly;
            try { Fire(on, other); }
            finally { detail = prev; ally = prevAlly; }
        }

        public static void Fire(string on, Character other)
        {
            if (Actions.ProcDepth > 0 || Player.instance == null || Player_AbilityControl.instance == null || Game.Game_isLoading) return;
            PluginHost.Fire(on, Player.instance, other, detail, ally); // first, so procs see what a plugin keeps up to date
            try
            {
                foreach (Ability a in Player_AbilityControl.instance.GetAbilities())
                    if (a is ModAbility m) m.HandleProc(on, other);
                foreach (Effect e in Player.instance.effects.GetEffects().ToList())
                    if (e is ModEffect me) me.HandleProc(on, other);
            }
            catch (Exception e) { Plugin.Log.LogError($"Proc '{on}' failed: {e}"); }
        }

        /// <summary>Procs of effects carried by an ally that isn't you (a summon, a pet): its own hits, kills and hurts.</summary>
        public static void FireOn(Character bearer, string on, Character other, string what)
        {
            string prev = detail;
            detail = what;
            try { FireOn(bearer, on, other); }
            finally { detail = prev; }
        }

        public static void FireOn(Character bearer, string on, Character other)
        {
            if (Actions.ProcDepth > 0 || bearer == null || bearer == Player.instance || bearer.effects == null || Game.Game_isLoading) return;
            PluginHost.Fire(on, bearer, other, detail, null);
            try
            {
                foreach (Effect e in bearer.effects.GetEffects().ToList())
                    if (e is ModEffect me) me.HandleProc(on, other);
            }
            catch (Exception e) { Plugin.Log.LogError($"Proc '{on}' on {bearer.name} failed: {e}"); }
        }

        public static void RunProc(ProcSpec p, ActionContext ctx, string on, Character other)
        {
            on = (on ?? "").ToLowerInvariant();
            if (!Matches(p, on, ctx)) return;
            bool pluginEvent = on.Contains(":"); // a plugin's own event: "other" is whoever it fired it about
            bool aimsAtOther = pluginEvent || on == "hit" || on == "kill" || on == "hurt" || on == "dodge" || on == "reaction" || on == "crit" || on == "miss" || on == "cast" || on == "allydeath"
                || on == "enemydeath" || on == "allykill" || on == "allyhurt" || on == "summon" || on == "inflict" || on == "shieldbreak" || on == "trap"
                || on == "charge" || on == "leap" || on == "knockback" || on == "swap" || on == "chain";
            Character focus = aimsAtOther && other != null ? other : ctx.caster;
            if (pluginEvent || on == "hit" || on == "kill" || on == "crit" || on == "miss" || on == "cast" || on == "enemydeath" || on == "allykill" || on == "inflict" || on == "trap"
                || on == "charge" || on == "leap" || on == "knockback" || on == "chain") ctx.victim = other;
            else if (on == "hurt" || on == "dodge" || on == "allyhurt" || on == "shieldbreak") ctx.attacker = other;
            else if (on == "allydeath") ctx.victim = other;
            ctx.ally = on == "allydeath" || on == "summon" ? other : ally;
            ctx.target = focus;

            // limits; "every" counts the events that passed the condition (every third hit on a bleeding enemy)
            ProcState st = State(p, ctx.caster);
            if (st.turnStamp != TurnCount) { st.turnStamp = TurnCount; st.firedThisTurn = 0; }
            if (p.cooldown > 0 && TurnCount < st.readyAt) return;
            if (p.maxPerTurn > 0 && st.firedThisTurn >= p.maxPerTurn) return;
            if (p.when != null && !When.Check(p.when, ctx, focus)) return;
            if (p.every > 1 && ++st.seen % p.every != 0) return;
            if (Random.Range(1, 101) > p.chance + ctx.chanceBonus) return;
            st.firedThisTurn++;
            if (p.cooldown > 0) st.readyAt = TurnCount + p.cooldown;

            Actions.ProcDepth++;
            try
            {
                Vector3 at = focus != null ? focus.GetHitFx().position : ctx.caster.GetHitFx().position;
                ModAudio.Play(p.sound, at, ctx.classId);
                Actions.SpawnVfx(p.vfx, at);

                var self = p.actions.Where(a => (a.to ?? "").ToLowerInvariant() == "self").ToList();
                var rest = p.actions.Except(self).ToList();
                if (p.radius > 0f && AI_Manager.instance != null)
                {
                    Vector3 center = focus != null && focus.GetMover() != null && focus.GetMover().GetCurrentNavNode() != null
                        ? focus.GetMover().GetCurrentNavNode().transform.position : focus != null ? focus.transform.position : ctx.caster.transform.position;
                    var near = AI_Manager.instance.GetEnemiesAt(center, p.radius).Where(c => c != null && !c.GetIsDead() && c != ctx.caster).ToList();
                    if (p.pick > 0) near = near.OrderBy(_ => Random.value).Take(p.pick).ToList();
                    foreach (Character c in near)
                        Actions.Run(rest, ctx, c);
                }
                else Actions.Run(rest, ctx, focus);
                Actions.Run(self, ctx, ctx.caster);
            }
            finally { Actions.ProcDepth--; }
        }

        private static ProcState State(ProcSpec p, Character bearer)
        {
            var key = (p, bearer);
            if (!states.TryGetValue(key, out ProcState s))
            {
                if (states.Count > 500) foreach (var k in states.Keys.Where(k => k.Item2 == null || k.Item2.GetIsDead()).ToList()) states.Remove(k);
                states[key] = s = new ProcState();
            }
            return s;
        }

        /// <summary>The event's own filters: "match" (ability / effect) and lowHealth's threshold.</summary>
        private static bool Matches(ProcSpec p, string on, ActionContext ctx)
        {
            if (on == "lowhealth") return detail != null && int.TryParse(detail, out int pct) && pct == p.threshold;
            // move: how you moved; no match = your own steps, as before
            if (on == "move")
            {
                string how = string.IsNullOrEmpty(p.match) ? "step" : p.match.ToLowerInvariant();
                return how == "any" || how == detail;
            }
            if (string.IsNullOrEmpty(p.match)) return true;
            if (detail == null) return false;
            if (on == "effectgained" || on == "effectlost" || on == "inflict")
                return string.Equals(detail, Actions.EffectId(p.match, ctx.classId), StringComparison.OrdinalIgnoreCase);
            // cast: "name|id" of the ability used; useItem / equip / unequip: "name|id" of the item
            return detail.Split('|').Any(d => string.Equals(d, p.match, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Every lowHealth threshold any of your procs or effects listens for.</summary>
        internal static IEnumerable<int> LowHealthThresholds()
        {
            if (Player_AbilityControl.instance == null || Player.instance == null) yield break;
            foreach (Ability a in Player_AbilityControl.instance.GetAbilities())
                if (a is ModAbility m && m.Procs != null)
                    foreach (ProcSpec p in m.Procs) if (string.Equals(p.on, "lowhealth", StringComparison.OrdinalIgnoreCase)) yield return p.threshold;
            foreach (Effect e in Player.instance.effects.GetEffects())
                if (e is ModEffect me && me.Procs != null)
                    foreach (ProcSpec p in me.Procs) if (string.Equals(p.on, "lowhealth", StringComparison.OrdinalIgnoreCase)) yield return p.threshold;
        }
    }

    [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.OnDealDamage))]
    internal static class Patch_ProcHit
    {
        private static void Postfix(Character victim, int totalDamage)
        {
            if (victim == null || totalDamage <= 0) return;
            ProcHub.Fire("hit", victim);
            if (victim.GetIsDead() || victim.GetHealth() <= 0) ProcHub.Fire("kill", victim);
        }
    }

    /// <summary>Allies' own triggers: a summon hitting or killing something, or getting hurt, fires the effects it carries.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.TakeDamage), new[] { typeof(Character), typeof(List<DmgObj>), typeof(string), typeof(Ability), typeof(float) })]
    internal static class Patch_AllyProcs
    {
        private static void Prefix(Character __instance, out int __state) => __state = __instance.health;

        private static void Postfix(Character __instance, Character attacker, int __state)
        {
            if (__instance == null || __instance.health >= __state) return;
            if (attacker != null && attacker != Player.instance && attacker.ai != null && attacker.ai.GetTeam() == AI.AI_TEAM.Player)
            {
                ProcHub.FireOn(attacker, "hit", __instance);
                if (__instance.GetIsDead() || __instance.GetHealth() <= 0)
                {
                    ProcHub.FireOn(attacker, "kill", __instance);
                    // and yours: one of your allies made a kill
                    if (__instance.ai != null && __instance.ai.GetTeam() != AI.AI_TEAM.Player) ProcHub.Fire("allykill", __instance, null, attacker);
                }
            }
            if (__instance != Player.instance && !__instance.GetIsDead() && __instance.ai != null && __instance.ai.GetTeam() == AI.AI_TEAM.Player && attacker != __instance)
            {
                ProcHub.FireOn(__instance, "hurt", attacker);
                ProcHub.Fire("allyhurt", attacker, null, __instance);
                Reactions.Hurt(attacker, __instance, __state);
            }
        }
    }

    [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
    internal static class Patch_ProcTurn
    {
        private static void Postfix()
        {
            ProcHub.Fire("turn", null);
            // Allies' own "turn" triggers (a summon running its own program each turn), as that ally.
            if (AI_Manager.instance == null) return;
            foreach (Character c in AI_Manager.instance.GetPlayerMinions())
                if (c != null && !c.GetIsDead()) ProcHub.FireOn(c, "turn", null);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TakeDamage), new[] { typeof(Character), typeof(List<DmgObj>), typeof(string), typeof(Ability), typeof(float) })]
    internal static class Patch_ProcHurt
    {
        private static void Prefix(Player __instance, out int __state) => __state = __instance.health;

        private static void Postfix(Player __instance, Character caster, int __state)
        {
            if (__instance.health < __state && !__instance.GetIsDead() && caster != __instance)
            {
                ProcHub.Fire("hurt", caster);
                Reactions.Hurt(caster, __instance, __state);
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnDodge))]
    internal static class Patch_ProcDodge
    {
        private static void Postfix(Character c)
        {
            ProcHub.Fire("dodge", c);
            Reactions.Fire("dodge", c, Player.instance);
        }
    }
}

namespace SCClassLoader
{
    /// <summary>Cheat-death: ModEffects with preventDeath intercept lethal damage on their bearer.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.DamageHealth), new[] { typeof(int), typeof(Ability), typeof(bool) })]
    internal static class Patch_PreventDeath
    {
        private static void Prefix(Character __instance, ref int dmg)
        {
            if (__instance == null || __instance.isDead || __instance.health - dmg > 0) return;
            foreach (Effect e in __instance.effects.GetEffects().ToList())
                if (e is ModEffect me && me.TrySave(ref dmg)) return;
        }
    }

    /// <summary>The player's damage never reaches the three-argument overload: Player overrides DamageHealth(int, Ability)
    /// with its own death-blow checks and health change, so cheat-death has to look there too.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.DamageHealth), new[] { typeof(int), typeof(Ability) })]
    internal static class Patch_PreventDeathPlayer
    {
        private static void Prefix(Player __instance, ref int dmg)
        {
            if (__instance == null || __instance.isDead || __instance.health - dmg > 0) return;
            foreach (Effect e in __instance.effects.GetEffects().ToList())
                if (e is ModEffect me && me.TrySave(ref dmg)) return;
        }
    }
}
