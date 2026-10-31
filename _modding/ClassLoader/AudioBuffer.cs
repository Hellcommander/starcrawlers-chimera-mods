using System;
using BepInEx.Configuration;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>
    /// FMOD mixes sound in blocks and keeps four of them queued for the sound card, so every sound in the game (the game's
    /// own as much as a mod's) reaches the speakers one block after it starts, on average half a block later, plus the four
    /// queued: 1024 samples at 48 kHz, FMOD's default and what the game leaves it at, is about 95 ms before the device adds
    /// its own. The block size can only be set before FMOD starts, so this steps in as the Studio system initializes.
    /// Smaller blocks mean less delay and a mixer that wakes more often; a block too small for a machine crackles. 512 is
    /// FMOD's own advice for lower latency on PC.
    /// </summary>
    [HarmonyPatch(typeof(FMOD.Studio.System), nameof(FMOD.Studio.System.initialize))]
    internal static class AudioBuffer
    {
        internal const string Game = "game";
        internal static ConfigEntry<string> Block;
        /// <summary>What was asked of FMOD, for the log and the self-test.</summary>
        internal static string Applied = "the game's own";

        internal static void Bind(ConfigFile cfg)
        {
            Block = cfg.Bind("Audio", "MixBlock", "512", new ConfigDescription(
                "Samples per FMOD mix block, for every sound in the game: smaller means less delay between a hit and its sound (1024, the game's own, is about 95 ms; 512 about 50; 256 about 30) but a greater chance of crackling on a slow machine. 'game' leaves FMOD's default.",
                new AcceptableValueList<string>(Game, "256", "512", "1024")));
        }

        private static void Prefix(FMOD.Studio.System __instance)
        {
            if (Block == null || Block.Value == Game || !uint.TryParse(Block.Value, out uint samples) || samples < 64) return;
            try
            {
                if (__instance.getCoreSystem(out FMOD.System core) != FMOD.RESULT.OK) return;
                FMOD.RESULT r = core.setDSPBufferSize(samples, 4);
                Applied = r == FMOD.RESULT.OK ? $"{samples} x 4" : $"refused ({r})";
                Plugin.Log.LogInfo($"FMOD mix block: {Applied}");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"FMOD mix block could not be set: {e.Message}"); }
        }
    }
}
