using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// A player skill's "windup": the cast spends its costs and your turn and only begins charging. At the start of
    /// your turn after the last charging turn (Player.TurnBegin, after every enemy has acted) it goes off by itself
    /// at the target it was aimed at, through the skill's own cast (its shot, travel effect and all), for free and
    /// before you act. A stun on you, your death, or a target that's dead or gone breaks it off.
    /// </summary>
    internal static class WindUp
    {
        private class Charge
        {
            public ModAbility ability;
            public AbilitySpec spec;
            public string classId;
            public Character target;
            public NavNode node;
            public Item item; // an item-target skill's item
            public int left;
        }

        private static readonly List<Charge> charging = new List<Charge>();
        private static ModAbility releasing;

        internal static bool Releasing(ModAbility a) => a != null && releasing == a;
        internal static bool Charging(ModAbility a) => charging.Any(c => c.ability == a);

        internal static void Begin(ModAbility a, AbilitySpec spec, string classId, Character target, NavNode node, Item item = null)
        {
            charging.RemoveAll(c => c.ability == a);
            charging.Add(new Charge { ability = a, spec = spec, classId = classId, target = target, node = node, item = item, left = Mathf.Max(1, spec.windup) });
            Player p = Player.instance;
            if (!string.IsNullOrEmpty(spec.windupEffect))
                Actions.ApplyEffect(new ActionSpec { @do = "effect", effect = spec.windupEffect, stacks = 1, turns = spec.windup + 1 }, Context(a, classId, target), p);
            if (!string.IsNullOrEmpty(spec.windupSound)) ModAudio.Play(spec.windupSound, p.GetHitFx().position, classId);
            string what = spec.windupText ?? "starts to charge";
            Game.LOG($"<color=#FFD15C>{a.GetDisplayName()} {what}{(target != null ? $", aimed at {target.GetName()}" : "")} ({spec.windup}).</color>");
            if (HUD.instance != null) HUD.instance.MakeFct("<color=#FFD15C>" + a.GetDisplayName() + "...</color>", p.GetHitFx().position, false);
        }

        private static ActionContext Context(ModAbility a, string classId, Character target) =>
            new ActionContext { caster = Player.instance, classId = classId, source = a, sourceName = a.GetDisplayName(), target = target };

        private static void End(Charge c)
        {
            charging.Remove(c);
            if (!string.IsNullOrEmpty(c.spec.windupEffect) && Player.instance != null)
            {
                Player.instance.effects.RemoveEffect(Actions.EffectId(c.spec.windupEffect, c.classId), true);
                Player.instance.CalculateStats();
                if (Menu_HUD.instance != null) Menu_HUD.instance.UpdatePlayerEffects();
            }
        }

        private static void Break(Charge c, string why)
        {
            End(c);
            Game.LOG($"<color=#9FB7C9>{c.ability.GetDisplayName()} breaks off: {why}.</color>");
        }

        /// <summary>Drops this skill's charge without firing (a new floor).</summary>
        internal static void Drop(ModAbility a) { foreach (Charge c in charging.Where(c => c.ability == a).ToList()) End(c); }

        private static void OnTurnBegin(Player p)
        {
            foreach (Charge c in charging.ToList())
            {
                if (c.ability == null || p == null || p.GetIsDead()) { charging.Remove(c); continue; }
                if (p.FLAG_skipTurn) { Break(c, "you are stunned"); continue; }
                if (--c.left > 0)
                {
                    Game.LOG($"<color=#FFD15C>{c.ability.GetDisplayName()} charges ({c.left}).</color>");
                    continue;
                }
                bool aimed = c.spec.target == "enemy" || c.spec.target == "ally" || c.spec.target == "pet";
                if (aimed && (c.target == null || c.target.GetIsDead() || c.target.GetMover() == null)) { Break(c, "its target is gone"); continue; }
                if (c.spec.target == "item" && c.item == null) { Break(c, "its item is gone"); continue; }
                End(c);
                Plugin.Instance.StartCoroutine(Release(c));
            }
        }

        private static IEnumerator Release(Charge c)
        {
            // wait for the turn's own bookkeeping, then hold your input while it goes off
            yield return null;
            for (int i = 0; i < 300 && Game.RESOLVING; i++) yield return null;
            Game.SetResolve(true);
            releasing = c.ability;
            Character keepC = Game.TARGET_C;
            NavNode keepN = Game.TARGET_N;
            try
            {
                // aimed at a spot past an enemy that has since died: the spot it stood on, not the body
                Game.TARGET_C = c.target != null && c.target.GetIsDead() ? null : c.target;
                Game.TARGET_N = c.node;
                if (c.item != null) Game.TARGET_I = c.item;
                Game.LOG($"<color=#FFD15C>{c.ability.GetDisplayName()} goes off!</color>");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("WindUp: " + e.Message); }
            yield return c.ability.StartCoroutine(c.ability.Use());
            releasing = null;
            Game.TARGET_C = keepC;
            Game.TARGET_N = keepN;
            Game.SetResolve(false);
            if (Menu_HUD.instance != null) Menu_HUD.instance.Refresh_Cooldown();
        }

        [HarmonyPatch(typeof(Player), nameof(Player.TurnBegin))]
        private static class Patch_TurnBegin
        {
            private static void Postfix(Player __instance)
            {
                try { OnTurnBegin(__instance); }
                catch (System.Exception e) { Plugin.Log.LogError("WindUp: " + e); }
            }
        }
    }
}
