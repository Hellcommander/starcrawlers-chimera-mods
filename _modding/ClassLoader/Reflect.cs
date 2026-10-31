using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// An effect's "reflect" (a % chance): a projectile or energy attack on its bearer is turned aside and thrown back at
    /// whoever made it, at "reflectShare" of its strength (default all of it). Projectile = a ranged attack or skill;
    /// energy = any of its damage is Energy. Melee blows, damage over time and hazards are never reflected.
    /// </summary>
    internal static class Reflect
    {
        private static bool busy; // a reflected shot isn't reflected again

        [HarmonyPatch(typeof(Character), nameof(Character.TakeDamage), new[] { typeof(Character), typeof(List<DmgObj>), typeof(string), typeof(Ability), typeof(float) })]
        private static class Patch_TakeDamage
        {
            [HarmonyPriority(Priority.High)]
            private static bool Prefix(Character __instance, Character attacker, List<DmgObj> __1, Ability __3)
            {
                if (busy || __instance == null || attacker == null || attacker == __instance || __1 == null || __1.Count == 0 || __3 == null) return true;
                if (__instance.GetIsDead() || attacker.GetIsDead() || __instance.effects == null) return true;
                bool projectile = __3.isRanged && !__3.isMelee;
                bool energy = __1.Any(d => d != null && d.dmgType == DAMAGE_TYPE.Energy);
                if (!projectile && !energy) return true;
                foreach (Effect e in __instance.effects.GetEffects())
                {
                    if (!(e is ModEffect me) || !ModEffect.Specs.TryGetValue(me.internalName, out var rt) || rt.spec.reflect <= 0) continue;
                    if (Random.Range(0, 100) >= rt.spec.reflect) continue;
                    float share = rt.spec.reflectShare > 0f ? rt.spec.reflectShare : 1f;
                    var back = __1.Where(d => d != null).Select(d => new DmgObj(d.dmgType, Mathf.Max(1, Mathf.RoundToInt(d.damage * share)))).ToList();
                    Game.LOG($"<color=#9FE8FF>{__instance.GetName()}'s {rt.spec.name} turns {attacker.GetName()}'s {__3.GetDisplayName()} back on {(attacker == Player.instance ? "you" : "it")}!</color>");
                    Actions.SpawnVfx("PA_Elementalist_FrigidBlast.impactParticle@0.5", __instance.GetHitFx().position);
                    busy = true;
                    try { attacker.TakeDamage(__instance, back, $"{rt.spec.name} reflects", null, 0f); }
                    finally { busy = false; }
                    if (HUD.instance != null) HUD.instance.MakeFct("Reflected", __instance.GetHitFx().position, false);
                    return false; // the shot never lands
                }
                return true;
            }
        }
    }
}
