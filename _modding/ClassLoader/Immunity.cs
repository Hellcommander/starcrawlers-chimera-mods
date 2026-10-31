using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>
    /// Effect fields that shield their bearer from its own side's output: "immune" lists effects it can't get ("@Gloomrot",
    /// "#Poisoned", vanilla ids like "EFFECT_Poison" or "CONFUSE"), and "immuneToAllies" drops every hit that comes from
    /// your side (you, your summons and pets, and their bursts and damage over time). A void caster's own spores and
    /// bursts can't touch them.
    /// </summary>
    internal static class Immunity
    {
        internal static bool Blocks(Character t, string effectId)
        {
            if (t == null || t.effects == null || string.IsNullOrEmpty(effectId)) return false;
            foreach (Effect e in t.effects.GetEffects())
            {
                if (!(e is ModEffect me) || !ModEffect.Specs.TryGetValue(me.internalName, out var rt) || rt.spec.immune == null) continue;
                foreach (string x in rt.spec.immune)
                    if (string.Equals(Actions.EffectId(x, rt.classId), effectId, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool AlliesCantHurt(Character t) =>
            t != null && t.effects != null && t.effects.GetEffects().Any(e => e is ModEffect me && ModEffect.Specs.TryGetValue(me.internalName, out var rt) && rt.spec.immuneToAllies);

        private static bool OnYourSide(Character c) => c != null && (c == Player.instance || (c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Player));

        [HarmonyPatch(typeof(EffectManager), nameof(EffectManager.CanUseOn), typeof(string), typeof(Character))]
        private static class Patch_CanUseOn
        {
            private static void Postfix(string internalName, Character target, ref bool __result)
            {
                if (__result && Blocks(target, internalName)) __result = false;
            }
        }

        [HarmonyPatch(typeof(EffectManager), nameof(EffectManager.ApplyEffect), typeof(string), typeof(Character))]
        private static class Patch_Apply
        {
            [HarmonyPriority(Priority.High)]
            private static bool Prefix(string internalName, Character target, ref Effect __result)
            {
                if (!Blocks(target, internalName)) return true;
                __result = null;
                return false;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.TakeDamage), new[] { typeof(Character), typeof(List<DmgObj>), typeof(string), typeof(Ability), typeof(float) })]
        private static class Patch_TakeDamage
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Character __instance, Character attacker) =>
                !(attacker != null && attacker != __instance && OnYourSide(__instance) && OnYourSide(attacker) && AlliesCantHurt(__instance));
        }
    }
}
