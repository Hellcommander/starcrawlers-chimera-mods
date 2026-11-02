using System;
using BepInEx.Configuration;

namespace SCClassLoader
{
    internal enum BusTier { Low, Mid, High }

    /// <summary>
    /// How much the event bus asks of this machine. Told from the CPU unless the player picks a tier: a small CPU keeps most of
    /// itself for the game and shows less at once, a big one gives its threads to the workers and shows more.
    /// </summary>
    internal static class BusProfile
    {
        private const int LowUpTo = 6, MidUpTo = 16;

        internal static ConfigEntry<string> Choice;

        internal static void Init(ConfigFile cfg)
        {
            Choice = cfg.Bind("Performance", "HardwareTier", "Auto",
                new ConfigDescription("Needs EventBus on. How much the event bus asks of your computer: Low, Mid, High, or Auto to go by your CPU's threads (up to 6 is Low, up to 16 is Mid). Low uses fewer worker threads and shows fewer effects at once; High uses more of both.",
                    new AcceptableValueList<string>("Auto", "Low", "Mid", "High")));
        }

        internal static BusTier Tier
        {
            get
            {
                if (Enum.TryParse(Choice.Value, true, out BusTier picked)) return picked;
                int threads = Environment.ProcessorCount;
                return threads <= LowUpTo ? BusTier.Low : threads <= MidUpTo ? BusTier.Mid : BusTier.High;
            }
        }

        /// <summary>Two threads stay with the game and its drawing; a small CPU keeps half.</summary>
        internal static int Workers
        {
            get
            {
                int threads = Environment.ProcessorCount;
                return Math.Max(1, Tier == BusTier.Low ? threads / 2 : threads - 2);
            }
        }

        /// <summary>The bus's share of a frame on the main thread.</summary>
        internal static double PumpMs => Tier == BusTier.Low ? 1.5 : Tier == BusTier.Mid ? 2.0 : 3.0;

        /// <summary>How long enemies whose turns need no waiting may keep acting within one frame.</summary>
        internal static double TurnMs => Tier == BusTier.Low ? 4.0 : Tier == BusTier.Mid ? 6.0 : 8.0;

        /// <summary>A shown-at-once limit from the settings, for this machine.</summary>
        internal static int Shown(int limit) => Tier == BusTier.Low ? (limit + 1) / 2 : Tier == BusTier.High ? limit + limit / 2 : limit;
    }
}
