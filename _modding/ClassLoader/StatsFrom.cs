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
        /// <summary>The stat it is worked out from ("Smarts", or "Smarts+PsiPower"), with all your gear and effects counted.</summary>
        public string from;
        /// <summary>One point of "stat" for every this many of "from", rounded down.</summary>
        public int per = 1;
        /// <summary>One more point for every this many ranks in the ability's class. 0 = rank doesn't count.</summary>
        public int rankPer;
        /// <summary>
        /// For a stat where lower is better (ShieldRegen is turns per point): the stat is this minus the share, at least 1,
        /// and it replaces your gear's value only when it beats it. 0 = the share is simply added.
        /// </summary>
        public int start;
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
                bool raised = false, sourced = false;
                foreach (ModAbility a in pac.GetAbilities().OfType<ModAbility>())
                    foreach (StatFromSpec s in a.Spec?.statsFrom ?? Enumerable.Empty<StatFromSpec>())
                    {
                        if (s == null || s.from == null || !Enum.TryParse(s.stat, true, out STAT_TYPE to)) continue;
                        int total = 0;
                        foreach (string name in s.from.Split('+'))
                            if (Enum.TryParse(name.Trim(), true, out STAT_TYPE from)) total += __instance.GetStat(from);
                        int amount = total / Math.Max(1, s.per) + (s.rankPer > 0 ? a.ClassRank / s.rankPer : 0);
                        StatObj[] group = __instance.secondaryAttributes.Any(x => x != null && x.statType == to) ? __instance.secondaryAttributes : __instance.primaryAttributes;
                        StatObj slot = group.FirstOrDefault(x => x != null && x.statType == to);
                        if (slot == null) continue;
                        if (s.start <= 0) slot.amount += amount;
                        else
                        {
                            // with ShieldRegenPastCap on, what goes past every turn is kept for it to count
                            // and effects that shorten it still do, on top of whichever of gear and this is better
                            int value = s.start - amount;
                            int mods = __instance.secondaryAttributes_mods.Where(x => x != null && x.statType == to).Sum(x => x.amount);
                            int gear = slot.amount - mods;
                            slot.amount = (gear > 0 ? Math.Min(gear, value) : value + gear) + mods;
                            if (!ShieldRegen.PastCap.Value) slot.amount = Math.Max(1, slot.amount);
                            sourced |= to == STAT_TYPE.ShieldRegen;
                        }
                        raised |= to == STAT_TYPE.ShieldMax;
                    }
                ShieldRegen.Settle(__instance, sourced);
                __instance.shieldingMax = __instance.secondaryAttributes[14].amount; // the base method read it, and drew the bar, before this ran
                __instance.shieldRegen = __instance.secondaryAttributes[15].amount;
                if (raised && Menu_Player.instance.playerShieldItem == null && __instance.shielding > __instance.shieldingMax) __instance.shielding = __instance.shieldingMax; // its buffs ended
                if (Menu_HUD.instance != null) Menu_HUD.instance.Refresh_Shield();
            }
        }
    }
}
