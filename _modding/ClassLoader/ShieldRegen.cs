using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>
    /// Shield Regen past every turn counts. Shield Regen is turns per shield point and every source adds into one total,
    /// so once it reaches 1 more did nothing, and going past it was worse: a total of 0 fell back to every 8 turns and
    /// one below 0 never refilled. Now the total stops at every turn and every 5 points past it refill 1 more per turn,
    /// the way HealthRegenPastCap works. A pool with no Shield Regen of its own (only reductions) starts from vanilla's 8.
    /// [Fixes] ShieldRegenPastCap in the config file turns it off.
    /// </summary>
    internal static class ShieldRegen
    {
        internal static ConfigEntry<bool> PastCap;

        /// <summary>Vanilla refills a pool with no Shield Regen 1 point every Default turns.</summary>
        internal const int Default = 8, PointsPerShield = 5;

        /// <summary>How far your Shield Regen went past every turn, from the last time your stats were worked out.</summary>
        internal static int Over;

        internal static void Bind(ConfigFile cfg)
        {
            PastCap = cfg.Bind("Fixes", "ShieldRegenPastCap", true,
                "Shield Regen better than every turn still counts: every 5 points past it refill 1 more shield per turn (vanilla stops at 1 per turn, and a total that went past it refilled every 8 turns or never).");
        }

        /// <summary>Shield points refilled per turn on top of vanilla's 1.</summary>
        internal static int Extra => PastCap.Value ? Over / PointsPerShield : 0;

        /// <summary>
        /// Called once your stats are worked out: settles the Shield Regen total. "sourced" = gear or a skill set a
        /// Shield Regen of its own, so the total isn't just reductions.
        /// </summary>
        internal static void Settle(Player p, bool sourced)
        {
            Over = 0;
            if (!PastCap.Value) return;
            StatObj slot = p.secondaryAttributes[15];
            sourced |= Menu_Player.instance.GetEquipment().Any(e => e != null && e.stats.Any(s => s != null && s.statType == STAT_TYPE.ShieldRegen && s.amount > 0));
            int turns = slot.amount;
            if (!sourced)
            {
                if (turns >= 0) return; // nothing set it: vanilla's 8
                turns += Default;
            }
            if (turns < 1) { Over = 1 - turns; turns = 1; }
            slot.amount = turns;
        }

        private static readonly AccessTools.FieldRef<Character, int> Tick = AccessTools.FieldRefAccess<Character, int>("shieldRegenTick");

        /// <summary>
        /// Every hit restarts vanilla's Shield Regen count. An effect with "steadyShield" keeps the count through hits, so
        /// its bearer's shield refills at its normal rate under fire.
        /// </summary>
        [HarmonyPatch(typeof(Character), nameof(Character.TakeDamage), typeof(Character), typeof(System.Collections.Generic.List<DmgObj>), typeof(string), typeof(Ability), typeof(float))]
        private static class Patch_Hit
        {
            private static void Prefix(Character __instance, out int __state) => __state = Tick(__instance);

            private static void Postfix(Character __instance, int __state)
            {
                if (__instance.effects != null && __instance.effects.GetEffects().Any(e => e is ModEffect me && ModEffect.Specs.TryGetValue(me.internalName, out var rt) && rt.spec.steadyShield))
                    Tick(__instance) = __state;
            }
        }

        /// <summary>Vanilla has just refilled its 1 point (it does every turn at 1, while the pool isn't full); this adds the rest.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.Turn))]
        private static class Patch_Turn
        {
            private static void Postfix(Player __instance)
            {
                if (Game.STASIS || __instance != Player.instance || __instance.GetIsDead() || __instance.shieldRegen != 1) return;
                int extra = System.Math.Min(Extra, __instance.shieldingMax - __instance.shielding);
                if (extra > 0) Menu_Player.instance.ModShield(extra);
            }
        }
    }
}
