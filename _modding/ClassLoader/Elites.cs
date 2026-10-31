using BepInEx.Configuration;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>
    /// Bosses count as elites for skills and perks that reward elites. Vanilla only looks for the ELITESPAWN tag, so
    /// a boss (a far harder kill) got nothing: Xeno's Render Ooze perk made unique ooze from rare and elite humanoids
    /// but a random plain jar from a boss. Mod conditions (<c>"tag": "elite"</c>) and execute use the same check.
    /// [Fixes] BossesAreElites in the config file turns the vanilla part off.
    /// </summary>
    internal static class Elites
    {
        internal static ConfigEntry<bool> BossesAreElites;

        internal static void Bind(ConfigFile cfg)
        {
            BossesAreElites = cfg.Bind("Fixes", "BossesAreElites", true,
                "Bosses, minibosses, nemeses and kill squads count as elites for vanilla perks that reward elites (Xeno's Render Ooze makes the elite ooze from a boss).");
        }

        /// <summary>A boss fight: bosses, minibosses with a boss health bar, nemeses and kill squads.</summary>
        internal static bool IsBoss(Character c) =>
            c != null && (c.HasTag("BOSS") || c.HasTag("BOSSHEALTHBAR") || c.HasTag("NEMESIS") || c.HasTag("KILLSQUAD"));

        /// <summary>Elite for anything that asks: the game's elites and rares, side-area elites, and every boss.</summary>
        internal static bool IsElite(Character c) =>
            c != null && (IsBoss(c) || c.HasTag("ELITE") || c.HasTag("RARE") || c.HasTag("RARE_ELITE")
                          || c.HasTagWith("ELITESPAWN") || c.HasTagWith("RARESPAWN") || c.HasTag(SideKinds.EliteTag));

        private static bool IsBoss(Corpse c) =>
            c.HasTag("BOSS") || c.HasTag("BOSSHEALTHBAR") || c.HasTag("NEMESIS") || c.HasTag("KILLSQUAD");

        /// <summary>
        /// Render Ooze picks the jar by the corpse's ELITESPAWN tag (corpses copy their character's tags). A boss corpse
        /// gets that tag first; nothing else reads it from a corpse, and the corpse dissolves into the jar.
        /// </summary>
        [HarmonyPatch(typeof(PA_Xeno_OozeJar), "RenderOoze")]
        private static class Patch_RenderOoze
        {
            private static void Prefix(Corpse corpse)
            {
                if (BossesAreElites.Value && corpse != null && IsBoss(corpse) && !corpse.HasTag("ELITESPAWN"))
                    corpse.AddTag("ELITESPAWN");
            }
        }
    }
}
