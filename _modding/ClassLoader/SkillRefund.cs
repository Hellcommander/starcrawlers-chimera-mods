using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>
    /// A save can hold more skill and perk points than the installed trees have room for: a class mod was removed or
    /// turned off, or a tree lost tiers or perks in an update. Vanilla quietly drops what it can't place, and the
    /// points are gone. On load they come back instead, as unspent points. [Fixes] RefundMissingSkills turns it off.
    /// <list type="bullet">
    /// <item><b>A tree that isn't installed</b>: its bought ranks (the first tier is free) become skill points.</item>
    /// <item><b>A tree with fewer tiers than the save's rank</b>: the extra ranks become skill points and the rank is
    /// capped at the top tier (left over, the tree never counted as maxed and kept asking for a point it couldn't take).</item>
    /// <item><b>A perk on a skill that's gone</b> (its tree, its tier or the skill itself), or on a skill that no
    /// longer has a perk: the perk point goes back to the pool.</item>
    /// <item><b>An effect that can't load</b>: a missing tree's lingering effects (Force Psyker's Judgement, say) look
    /// up their skill while loading, find nothing and throw, and vanilla's load stops there and never finishes. Such
    /// an effect is dropped instead and the load goes on.</item>
    /// </list>
    /// Health and stamina saved above the new maximum (the lost tree gave some of it) are capped there.
    /// Saving afterwards writes the trimmed build, so nothing is refunded twice.
    /// </summary>
    internal static class SkillRefund
    {
        internal static ConfigEntry<bool> Fix;

        private static int perksFromDroppedPerks;

        internal static void Bind(ConfigFile cfg)
        {
            Fix = cfg.Bind("Fixes", "RefundMissingSkills", true,
                "Loading a save with ranks or perks in skills that aren't installed any more gives those points back.");
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Deserialize))]
        private static class Patch_PlayerLoad
        {
            private static void Prefix() { perksFromDroppedPerks = 0; broken.Clear(); }

            private static void Postfix(object d)
            {
                if (!Fix.Value || !(d is PlayerSaveInfo ps) || Player_SkillControl.instance == null || Player_AbilityControl.instance == null) return;
                var notes = new List<string>();

                int skill = 0;
                bool lost = false;
                Skill_Tree[] trees = Player_SkillControl.instance.skillTrees;
                for (int i = 0; ps.skillTrees != null && i < ps.skillTrees.Length && i < trees.Length; i++)
                {
                    string id = ps.skillTrees[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    int rank = ps.skillTreeRanks != null && i < ps.skillTreeRanks.Length ? System.Math.Max(0, ps.skillTreeRanks[i]) : 0;
                    Skill_Tree t = trees[i];
                    if (t == null)
                    {
                        lost = true;
                        if (rank > 0) notes.Add($"tree {id} (not installed): {rank} skill points");
                        skill += rank;
                        continue;
                    }
                    int top = System.Math.Max(0, t.skillTiers.Count - 1);
                    if (t.rank <= top) continue;
                    lost = true;
                    notes.Add($"tree {id} rank {t.rank + 1} of {top + 1}: {t.rank - top} skill points");
                    skill += t.rank - top;
                    t.rank = top;
                }

                // what vanilla matched it set to null; whatever is left belongs to a skill the player doesn't have now
                int perk = perksFromDroppedPerks;
                if (perk > 0) notes.Add($"{perk} perk points on skills that no longer have a perk");
                if (ps.abilities is AbilityControlSaveInfo ac && ac.abilitySaveInfo != null)
                {
                    var have = new HashSet<string>(Player_AbilityControl.instance.GetAbilities().Where(a => a != null).Select(a => a.internalName));
                    foreach (AbilitySaveInfo rec in ac.abilitySaveInfo.OfType<AbilitySaveInfo>())
                    {
                        if (rec.perkPoints < 1 || have.Contains(rec.internalName)) continue;
                        notes.Add($"perk on {rec.internalName} (skill gone): {rec.perkPoints} perk points");
                        perk += rec.perkPoints;
                    }
                }

                // the save's health and stamina were taken with the old trees' bonuses: don't load them over the new maximum
                Player p = Player.instance;
                if (lost && p != null && (p.GetHealth() > p.GetHealthMax() || p.GetStamina() > p.GetStaminaMax()))
                {
                    notes.Add($"health {p.GetHealth()} and stamina {p.GetStamina()} capped at {p.GetHealthMax()} and {p.GetStaminaMax()}");
                    p.SetHealth(System.Math.Min(p.GetHealth(), p.GetHealthMax()));
                    p.SetStamina(p.GetStamina());
                    Menu_HUD.instance?.Refresh_Health();
                    Menu_HUD.instance?.Refresh_Stamina();
                }

                if (skill == 0 && perk == 0) { if (notes.Count > 0) Plugin.Log.LogInfo("Skills not installed any more: " + string.Join("; ", notes)); return; }
                Player_AbilityControl.instance.skillPoints += skill;
                Player_AbilityControl.instance.perkPoints_available += perk;
                Plugin.Log.LogInfo($"Refunded {skill} skill and {perk} perk points: {string.Join("; ", notes)}");
                Plugin.Instance.StartCoroutine(Tell(skill, perk));
            }
        }

        /// <summary>Remembers the skill's own perk count before the save's goes over it: -1 means it has no perk.</summary>
        [HarmonyPatch(typeof(Ability), nameof(Ability.Deserialize))]
        private static class Patch_AbilityLoad
        {
            private static void Prefix(Ability __instance, out int __state) => __state = __instance.perkPoints;

            private static void Postfix(Ability __instance, object d, int __state)
            {
                if (!Fix.Value || __state >= 0 || !(d is AbilitySaveInfo rec) || rec.perkPoints < 1) return;
                if (Player_SkillControl.instance?.GetAbility(__instance.internalName) != __instance) return; // only the player's tree skills take perks
                perksFromDroppedPerks += rec.perkPoints;
                __instance.perkPoints = -1;
            }
        }

        // ---- effects that throw while loading -------------------------------------------------------------------

        private static readonly HashSet<Effect> broken = new HashSet<Effect>();

        private static IEnumerable<MethodBase> Declared(string name, params System.Type[] args) =>
            typeof(Effect).Assembly.GetTypes().Concat(typeof(Plugin).Assembly.GetTypes())
                .Where(t => typeof(Effect).IsAssignableFrom(t))
                .Select(t => AccessTools.DeclaredMethod(t, name, args))
                .Where(m => m != null && !m.IsAbstract);

        [HarmonyPatch]
        private static class Patch_EffectLoad
        {
            private static IEnumerable<MethodBase> TargetMethods() => Declared(nameof(Effect.Deserialize), typeof(object));

            private static System.Exception Finalizer(Effect __instance, System.Exception __exception)
            {
                if (__exception == null || !Fix.Value || !Game.Game_isLoading) return __exception;
                Plugin.Log.LogWarning($"Effect {__instance?.internalName} couldn't load ({__exception.GetType().Name}: {__exception.Message}); dropped so the save still loads.");
                if (__instance != null) broken.Add(__instance);
                return null;
            }
        }

        /// <summary>Player.Deserialize activates each effect right after loading it: a broken one is taken off instead.</summary>
        [HarmonyPatch]
        private static class Patch_EffectActivate
        {
            private static IEnumerable<MethodBase> TargetMethods() => Declared(nameof(Effect.Activate), System.Type.EmptyTypes);

            private static bool Prefix(Effect __instance)
            {
                if (broken.Count == 0 || !broken.Remove(__instance)) return true;
                __instance.target?.effects?.ClearEffect(__instance); // not Remove(): its on-expire effects would fire
                EffectManager.instance?.EffectExpiredInstant(__instance);
                return false;
            }
        }

        /// <summary>Said once the load is over, so it lands in the log the player sees.</summary>
        private static IEnumerator Tell(int skill, int perk)
        {
            while (Game.Game_isLoading || Player.instance == null) yield return null;
            var parts = new List<string>();
            if (skill > 0) parts.Add(skill == 1 ? "1 skill point" : $"{skill} skill points");
            if (perk > 0) parts.Add(perk == 1 ? "1 perk point" : $"{perk} perk points");
            Game.LOG($"<color=#FFD966>Skills in this save aren't installed any more: {string.Join(" and ", parts)} refunded.</color>");
            Menu_Skills.instance?.CheckPointAlerts();
        }
    }
}
