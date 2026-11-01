using System;
using System.Linq;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>"statsFrom" on an ability: while you know it, one of your stats is raised by a share of another.</summary>
    public class StatFromSpec
    {
        /// <summary>The stat raised ("PsiPower").</summary>
        public string stat;
        /// <summary>The stat it is worked out from ("Smarts"), with all your gear and effects counted.</summary>
        public string from;
        /// <summary>One point of "stat" for every this many of "from", rounded down.</summary>
        public int per = 1;
    }

    /// <summary>
    /// Stats worked out from other stats, the way Force Psyker's Bastion turns Strength and Toughness into a bonus, but
    /// kept current whenever your stats are calculated instead of once a turn, so gear changes count at once.
    /// </summary>
    internal static class StatsFrom
    {
        internal static bool Off; // devtests

        [HarmonyPatch(typeof(Player), nameof(Player.CalculateStats))]
        private static class Patch_Calculate
        {
            private static void Postfix(Player __instance)
            {
                var pac = Player_AbilityControl.instance;
                if (Off || __instance != Player.instance || pac == null || __instance.effects == null || Game.Game_isLoading) return;
                bool raised = false;
                foreach (ModAbility a in pac.GetAbilities().OfType<ModAbility>())
                    foreach (StatFromSpec s in a.Spec?.statsFrom ?? Enumerable.Empty<StatFromSpec>())
                    {
                        if (s == null || !Enum.TryParse(s.stat, true, out STAT_TYPE to) || !Enum.TryParse(s.from, true, out STAT_TYPE from)) continue;
                        int amount = __instance.GetStat(from) / Math.Max(1, s.per);
                        StatObj[] group = __instance.secondaryAttributes.Any(x => x != null && x.statType == to) ? __instance.secondaryAttributes : __instance.primaryAttributes;
                        StatObj slot = group.FirstOrDefault(x => x != null && x.statType == to);
                        if (slot != null) slot.amount += amount;
                        raised |= slot != null && to == STAT_TYPE.ShieldMax;
                    }
                __instance.shieldingMax = __instance.secondaryAttributes[14].amount; // the base method read it, and drew the bar, before this ran
                if (raised && Menu_Player.instance.playerShieldItem == null && __instance.shielding > __instance.shieldingMax) __instance.shielding = __instance.shieldingMax; // its buffs ended
                if (Menu_HUD.instance != null) Menu_HUD.instance.Refresh_Shield();
            }
        }
    }
}
