using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>
    /// Skills and perks that build up stacks say where they stop. Vanilla tooltips mostly give the per-stack amount
    /// and leave the cap out (Combo Points stop at 5, Tempt Fate at 15, Rolling Thunder at 10), so players guessed,
    /// and got it wrong both ways (Better Living was reported to stop at 10; it has no limit). The numbers are read
    /// from the game's own code and effect data; ones that depend on skill rank show the value for your rank.
    /// [Interface] ShowStackCaps turns it off.
    /// </summary>
    internal static class StackCaps
    {
        internal static ConfigEntry<bool> Show;

        internal static void Bind(ConfigFile cfg)
        {
            Show = cfg.Bind("Interface", "ShowStackCaps", true,
                "Skill and perk tooltips say how many stacks they build up to (or that there is no limit).");
        }

        private static int Rank(string tree) => Math.Max(0, Player_SkillControl.instance?.GetRank(tree) ?? 0) + 1;

        private const string VoidEnergy = "Void Energy goes up to 100.";
        private const string TemptFate = "Tempt Fate goes up to 15 stacks.";
        private const string Widgets = "Up to 5 widgets per item.";
        private const string Mutation = "Each use shifts that mutation's odds: Hold It In bottoms out after 9 uses, Unleash tops out after 10.";

        /// <summary>By ability internalName, for classes several skills share.</summary>
        private static readonly Dictionary<string, Func<Ability, string>> ByName = new Dictionary<string, Func<Ability, string>>
        {
            ["PA_PASSIVE_KonomoCode"] = a => "Perk: 30 extra lives per character in all; Extra Life charges themselves have no limit.",
            ["PA_PASSIVE_OldFaithful"] = a => $"Up to +{Math.Min(30, Rank("SKILLTREE_BigGuns") * 10)}% damage (10% per Big Guns rank, at most 30%).",
            ["PA_PASSIVE_Pistolier_Marksman"] = a => "Ranged Accuracy builds with every ranged attack in a turn, with no limit; it resets each turn.",
            ["PA_Xeno_CraftOozeJar"] = a => "Every ooze jar fed to your pet slime raises its stats for good, with no limit.",
            ["PA_Hacker_Virus_Mindmelt"] = a => "Mindmelt lowers resistance every turn it runs, with no limit.",
            ["PA_Hacker_Virus_Overheat"] = a => "Perk: the spread range grows every turn, with no limit.",
            ["PA_PASSIVE_Pistolier_Deadeye"] = a => "Up to 3 chained shots per turn.",
            ["PA_PASSIVE_Mark"] = a => $"Grows by up to one stat point per character level (now {Player.instance?.level ?? 1}).",
        };

        /// <summary>By ability class.</summary>
        private static readonly Dictionary<string, Func<Ability, string>> ByType = new Dictionary<string, Func<Ability, string>>
        {
            ["PA_PASSIVE_ComboBuilder"] = a => "Combo Points go up to 5.",
            ["PA_Cyberninja_Rolling_Thunder"] = a => "Up to 10 charges. While it's running, the bonus from kills has no limit.",
            ["PA_Force_Psyker_Judgement"] = a => "Up to 10 charges (one pulse each).",
            ["PA_PASSIVE_Force_Psyker_Barrier"] = a =>
                $"The chance is 1% + 1% per 3 levels, up to 20% (now {UnityEngine.Mathf.Clamp(1 + (Player.instance?.level ?? 1) / 3, 1, 20)}%). " +
                "Perk: +1% for each turn without a barrier, up to 100%; it resets when a barrier forms.",
            ["PA_PASSIVE_Force_Psyker_Inner_Peace"] = a =>
                $"Overshield builds up to 30% of your Psi Power (now {(int)((Player.instance?.GetStat(STAT_TYPE.PsiPower) ?? 0) * 0.3f)}).",
            ["PA_Void_Psyker_Ignite"] = a => VoidEnergy,
            ["PA_Void_Psyker_Shadowcoil"] = a => VoidEnergy,
            ["PA_Void_Psyker_Blast"] = a => VoidEnergy,
            ["PA_Void_Psyker_Ascendance"] = a => VoidEnergy + " Ascendance raises Psi Power 30% more each turn with no limit, but its Void Energy drain doubles each turn.",
            ["PA_PASSIVE_Void_Psyker_Vigour"] = a => VoidEnergy,
            ["PA_PASSIVE_Void_Psyker_Drain"] = a => VoidEnergy,
            ["PA_PASSIVE_Bodybuilding_Rage"] = a => $"Up to +{Rank("SKILLTREE_Bodybuilding") * 6} Strength (6 per Bodybuilding rank).",
            ["PA_Pirate_DaringManeuver"] = a => TemptFate,
            ["PA_Pirate_BlackSpot"] = a => TemptFate,
            ["PA_Pirate_Avast"] = a => TemptFate,
            ["PA_Pirate_GrogBottle"] = a => "No set limit, but from 2 stacks each swig has a 10% chance per stack to make you spew and start over, so the 10th always does.",
            ["PA_PASSIVE_Chemistry_Better_Living"] = a =>
                $"Perk: no limit. Every Weirdest Science proc adds 1 Smarts for good (now +{Player.instance?.GetTag("Chemistry_Better_Living_Perk") ?? 0}).",
            ["PA_PASSIVE_Loot_Hunter_Magic"] = a => "Perk: +10% per search that finds nothing, with no limit; it resets when it pays off.",
            ["PA_PASSIVE_Carapace"] = a => "Carapace goes up to 5 charges.",
            ["PA_PASSIVE_ThoughtLeader_PyramidScheme"] = a => "Counts up to 3 minions (the most you can have).",
            ["PA_PASSIVE_Pyromaniac_FeelTheBurn"] = a => "Counts up to 10 (being on fire yourself counts as 10).",
            ["PA_PASSIVE_Mutant_Hangry"] = a => $"Up to +{Rank("SKILLTREE_ChaoticMutant") * 5}% damage (5% per Chaotic Mutant rank).",
            ["PA_PASSIVE_Gunslinger"] = a => a.internalName == "PA_PASSIVE_Gunslinger_3" ? "One For Each of Ya: +Ranged Power for every enemy within 10, with no limit." : null,
            ["PA_Gadgeteer_Widget"] = a => Widgets,
            ["PA_Gadgeteer_ApplyWidget"] = a => Widgets,
            ["PA_Modder_ModItBetter"] = a => ModderBalance.Rebalance.Value
                ? $"Up to {ModderBalance.MaxMods(Rank("SKILLTREE_Modder"))} mods per item at your rank (1/1/2/2/3), each {ModderBalance.ModAmount(Rank("SKILLTREE_Modder")):+0}. " +
                  "The 1st, 2nd and 3rd mod fail 10%, 30% and 50% of the time, and a failure takes that much off instead. Item (Re)Designer doesn't reset mods."
                : "Up to 3 mods per item.",
            ["PA_Modder_IWM"] = a => "Up to 10 times per weapon; the malfunction chance stops at 100%.",
            ["PA_Modder_ItemRedesigner"] = a => "The redesign count stops at 10 per item.",
            ["PA_Security_Boobytrap"] = a => "Up to 3 traps; placing a 4th removes the oldest.",
            ["PA_PASSIVE_Force_Psyker_Bastion"] = a => "Perk: Fortress adds armor every turn it lasts, with no limit.",
            ["PA_PASSIVE_VoidPsyker_Tentacles"] = a => "Perk: the bonus counts every living tentacle, with no limit (each lasts 6 turns).",
            ["PA_Bodybuilding_Pump"] = a => "No limit: each use adds 5% of your Strength, counting what Pump Up already gave.",
            ["PA_PASSIVE_Cybertechnician_Chrome"] = a => "Up to 3 hits (+30% Toughness, and the perk's armor stops with it).",
            ["PA_PASSIVE_Cybertechnician_Synapses"] = a => "+1 per weapon hit that doesn't crit, with no limit; it resets on a crit or after 4 turns without one.",
            ["PA_PASSIVE_Elementalist_Stormbringer"] = a => "Perk: +20% per charge, with no limit; your next Elementalist ability uses them all.",
            ["PA_Smuggler_Mug"] = a => "Perk: the bonus has no limit (it can pass 100%); it wears off 5% per hit and over quiet turns.",
            ["PA_Engineer_MechPart"] = a => "Every upgrade adds a stat to the mech for good, with no limit.",
            ["PA_Engineer_Drone"] = a => "Perk: resistances keep dropping every turn, with no limit.",
            ["PA_Hacker_Scramble"] = a => "Perk: each trigger adds a turn to the virus, with no limit.",
            ["PA_Cursed_Avatar"] = a => "Absorbed damage piles up with no limit, and all of it comes due as Blood Debt when Avatar ends.",
            ["PA_PASSIVE_Acrobatics_CheatDeath"] = a => "Each cheated death adds a charge, with no limit.",
            ["PA_PASSIVE_Cursed"] = a => $"Patience runs from 100 down to 0 (now {(Player.instance != null && Player.instance.HasTag("CURSED") ? Player.instance.GetTag("CURSED") : 100)}); it starts over after it runs out.",
            ["PA_Mutant_HoldItIn"] = a => Mutation,
            ["PA_Mutant_Unleash"] = a => Mutation,
            ["PA_Shieldbearer_Brace"] = a => $"Each use sets {Rank("SKILLTREE_Shieldbearer") + 2} block charges (rank + 3); they don't add up.",
        };

        internal static string LineFor(Ability a)
        {
            if (a == null) return null;
            if (a.internalName != null && ByName.TryGetValue(a.internalName, out var byName)) return byName(a);
            return ByType.TryGetValue(a.GetType().Name, out var byType) ? byType(a) : null;
        }

        private static void Append(Tooltip tip, Ability a)
        {
            if (!Show.Value || tip?.ability_description == null) return;
            string line;
            try { line = LineFor(a); }
            catch (Exception e) { Plugin.Log.LogWarning("Stack caps: " + e.Message); return; }
            if (string.IsNullOrEmpty(line) || tip.ability_description.text.EndsWith(line + "</color>", StringComparison.Ordinal)) return;
            tip.ability_description.text += "\n\n<color=#9FD8FF>Stacks: " + line + "</color>";
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.Show_TierTip), typeof(Skill_Tier), typeof(UnityEngine.Vector2))]
        private static class Patch_Tier
        {
            private static void Postfix(Tooltip __instance, Skill_Tier st) { if (st != null) Append(__instance, st.ability ?? st.passive); }
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.Show_AbilityTip))]
        private static class Patch_Ability
        {
            private static void Postfix(Tooltip __instance, Ability ability) => Append(__instance, ability);
        }
    }
}
