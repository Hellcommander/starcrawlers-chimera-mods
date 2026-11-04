using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Effects' consumable rules on the crawler: "itemTurns" (what your consumables put on you lasts longer),
    /// "shareItems" (it goes on your summons too) and "keepCost" (a skill's itemCost may not be spent).
    /// </summary>
    internal static class Consumables
    {
        /// <summary>The summed rules from every effect on the crawler.</summary>
        private static (float turns, bool share, int keepCost) Rules()
        {
            float turns = 0f;
            bool share = false;
            int keep = 0;
            Player p = Player.instance;
            if (p?.effects == null) return (0f, false, 0);
            foreach (Effect e in p.effects.GetEffects())
                if (e is ModEffect m)
                {
                    var (t, s, k) = m.ItemRules();
                    turns += t; share |= s; keep += k;
                }
            return (turns, share, keep);
        }

        /// <summary>"keepCost": one roll for a skill's whole itemCost; true = nothing is spent this time.</summary>
        internal static bool KeepsCost(string skillName)
        {
            int odds = Rules().keepCost;
            if (odds <= 0 || UnityEngine.Random.Range(1, 101) > odds) return false;
            Game.LOG($"<color=#FFD15C>{skillName} costs you nothing this time.</color>");
            return true;
        }

        // ---- what a consumable puts on you ---------------------------------------------------------------------

        private static int pendingFrame = -1;
        private static Character pendingTarget;
        private static readonly List<string> pendingIds = new List<string>();

        /// <summary>A consumable's use applies its effects right after this, in the same frame.</summary>
        [HarmonyPatch(typeof(Item_OnUse_Trigger), nameof(Item_OnUse_Trigger.Use))]
        private static class Patch_Use
        {
            private static void Prefix(Item_OnUse_Trigger __instance)
            {
                pendingIds.Clear();
                pendingTarget = null;
                if (__instance == null || __instance.target != Player.instance || string.IsNullOrEmpty(__instance.use_effect)) return;
                pendingFrame = Time.frameCount;
                pendingTarget = __instance.target;
                pendingIds.Add(__instance.use_effect);
                foreach (var b in __instance.bonus_effects ?? new Item_OnUse_Trigger.BonusEffect[0])
                    if (!string.IsNullOrEmpty(b.effect)) pendingIds.Add(b.effect);
            }
        }

        [HarmonyPatch(typeof(EffectManager), nameof(EffectManager.ApplyEffect), new[] { typeof(string), typeof(Character) })]
        private static class Patch_Apply
        {
            private static void Postfix(string internalName, Character target, Effect __result)
            {
                if (__result == null || target != pendingTarget || Time.frameCount != pendingFrame || !pendingIds.Remove(internalName)) return;
                var (turns, share, _) = Rules();
                if (turns > 0f || share) Plugin.Instance.StartCoroutine(Later(__result, internalName, turns, share));
            }
        }

        internal static int Lengthened, Shared; // devtests

        /// <summary>A frame on, once the item has set the effect's own length and strength.</summary>
        private static IEnumerator Later(Effect fx, string id, float turns, bool share)
        {
            yield return null;
            if (fx == null || fx.target == null || fx.target.GetIsDead()) yield break;
            try
            {
                if (turns > 0f && fx.duration > 0)
                {
                    fx.duration = Mathf.RoundToInt(fx.duration * (1f + turns));
                    Lengthened++;
                }
                if (share && AI_Manager.instance != null)
                    foreach (Character m in AI_Manager.instance.GetPlayerMinions().ToList())
                    {
                        if (m == null || m.GetIsDead() || !EffectManager.instance.CanUseOn(id, m)) continue;
                        Effect copy = EffectManager.instance.ApplyEffect(id, m);
                        if (copy == null) continue;
                        if (fx.duration > 0) copy.duration = fx.duration;
                        copy.amountA = fx.amountA;
                        copy.caster = fx.caster != null ? fx.caster : Player.instance;
                        copy.Activate();
                        // as Actions.ApplyEffect sets one up: its on-apply actions, group and shares
                        if (copy is ModEffect mod)
                            mod.OnApplied(new ActionContext { caster = copy.caster, target = m, sourceName = fx.GetName() });
                        m.CalculateStats();
                        Shared++;
                    }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Item effect {id}: {e.Message}"); }
            if (Menu_HUD.instance != null) Menu_HUD.instance.UpdatePlayerEffects();
        }
    }
}
