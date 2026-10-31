using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Health Regen past 7 counts. Vanilla heals 1 HP every (8 - Health Regen) turns, so at 7 it heals every turn
    /// and anything higher did nothing (players stacked Toughness and Health Regen gear for no gain). Up to 7 nothing
    /// changes; past it every 5 points heal 1 more HP per turn (12 = 2 per turn, 32 = 6). The extra HP comes 1 at a
    /// time like vanilla regen, so robots don't get it halved the way they do bigger heals. The Health Regen tip on the
    /// character sheet says what yours heals. [Fixes] HealthRegenPastCap in the config file turns it off.
    /// </summary>
    internal static class HealthRegen
    {
        internal static ConfigEntry<bool> PastCap;

        /// <summary>Vanilla: 1 HP every (Base - regen) turns, so Base - 1 is the regen that heals every turn.</summary>
        internal const int Base = 8, Cap = Base - 1, PointsPerHp = 5;

        /// <summary>The character sheet's invisible hover box over the Health Regen icon and number.</summary>
        internal const string TipObject = "tip_health_regen";

        internal static void Bind(ConfigFile cfg)
        {
            PastCap = cfg.Bind("Fixes", "HealthRegenPastCap", true,
                "Health Regen above 7 still counts: every 5 points over 7 heal 1 more HP per turn (vanilla stops at 1 HP per turn, so 7 and 50 healed the same).");
        }

        /// <summary>HP healed per turn on top of vanilla's 1, for this much Health Regen.</summary>
        internal static int Extra(int regen) => PastCap.Value && regen > Cap ? (regen - Cap) / PointsPerHp : 0;

        internal static string Rate(int regen) =>
            regen >= Cap ? $"{1 + Extra(regen)} HP per turn" : $"1 HP every {Base - regen} turns";

        internal static string Describe(int regen)
        {
            string hint = PastCap.Value ? $"Every {PointsPerHp} past {Cap} heal 1 more HP per turn." : $"More than {Cap} does nothing.";
            if (regen < Cap) hint = $"At {Cap} it heals every turn. " + hint;
            return $"<color=#72FFAC>Yours ({regen}) heals {Rate(regen)}.</color>\n{hint}";
        }

        /// <summary>Vanilla has just healed its 1 HP (it always does past the cap, while you're hurt); this adds the rest.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.Turn))]
        private static class Patch_Turn
        {
            private static void Postfix(Player __instance)
            {
                if (Game.STASIS || __instance.healBlock || __instance.GetIsDead()) return;
                int extra = Extra(__instance.GetStat(STAT_TYPE.HealthRegen));
                for (int i = 0; i < extra && __instance.GetHealth() < __instance.GetHealthMax(); i++)
                    __instance.Heal(1, doLog: false, "TURN");
            }
        }

        /// <summary>The Health Regen tip on the character sheet gets what your regen heals under its own text.</summary>
        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.Show_TinyTip), typeof(string), typeof(string), typeof(Tooltip_Tiny), typeof(Vector2))]
        private static class Patch_Tip
        {
            private static void Postfix(Tooltip __instance, Tooltip_Tiny tinyTip)
            {
                if (tinyTip == null || tinyTip.name != TipObject || Player.instance == null || __instance.itemDescription == null) return;
                if (__instance.rank == null || __instance.rank.text != (string)tinyTip.title) return; // the tip didn't show
                var box = __instance.itemDescription;
                string line = Describe(Player.instance.GetStat(STAT_TYPE.HealthRegen));
                box.text = box.gameObject.activeSelf && !string.IsNullOrEmpty(box.text) ? box.text + "\n\n" + line : line;
                box.gameObject.SetActive(true);
            }
        }
    }
}
