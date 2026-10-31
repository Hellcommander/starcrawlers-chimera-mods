using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Makes the game's hidden forced saves follow the Options autosave toggle (Ironmode always saves), and adds an
    /// optional save on arriving at a new floor. Save &amp; Quit, the quicksave button and the new-game save are left alone.
    /// The Apartment portal's forced save is handled by ApartmentSaveFix.
    /// </summary>
    internal static class SaveGating
    {
        internal static ConfigEntry<bool> FollowAutosave;
        internal static ConfigEntry<bool> SaveOnNewFloor;
        internal static bool inLorePlacement; // internal for the self-test

        internal static void Bind(ConfigFile cfg)
        {
            FollowAutosave = cfg.Bind("Saving", "ForcedSavesFollowAutosave", true,
                "The silent save when a floor's lore dataslate is placed and the save on leaving the tutorial only happen if autosave is on (Ironmode always saves).");
            SaveOnNewFloor = cfg.Bind("Saving", "AutosaveOnFloorChange", false,
                "With autosave on, also save every time you take the stairs to another floor.");
        }

        internal static bool AutosaveOn => PlayerPrefs.GetInt("Pref_Autosave", 1) == 1 || Game.GetIronmode() == 1;

        /// <summary>Lore_Control saves the whole game each time it drops a dataslate on a new floor.</summary>
        [HarmonyPatch(typeof(Lore_Control), nameof(Lore_Control.PlaceLegacyLore))]
        private static class Patch_Lore
        {
            private static void Prefix() => inLorePlacement = true;
            private static void Finalizer() => inLorePlacement = false;
        }

        [HarmonyPatch(typeof(SaveLoad), nameof(SaveLoad.Save))]
        private static class Patch_Save
        {
            private static bool Prefix()
            {
                if (!inLorePlacement || !FollowAutosave.Value || AutosaveOn) return true;
                Plugin.Log.LogInfo("Saving: autosave is off, skipping the save after placing a lore dataslate");
                return false;
            }
        }

        /// <summary>Replaces the tutorial exit's <c>Game.instance.Quicksave()</c>.</summary>
        public static void TutorialQuicksave(Game game)
        {
            if (!FollowAutosave.Value || AutosaveOn) game.Quicksave();
            else Plugin.Log.LogInfo("Saving: autosave is off, skipping the save on leaving the tutorial");
        }

        [HarmonyPatch]
        private static class Patch_TutorialExit
        {
            private static MethodBase TargetMethod() => AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(InteractiveObject_Tutorial_ExitDoor), "Exit"));

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) =>
                ApartmentSaveFix.SwapQuicksave(code, AccessTools.Method(typeof(SaveGating), nameof(TutorialQuicksave)), "Tutorial exit");
        }

        [HarmonyPatch(typeof(LevelManager), "DoStair")]
        private static class Patch_Stairs
        {
            private static void Postfix()
            {
                if (SaveOnNewFloor.Value && PlayerPrefs.GetInt("Pref_Autosave", 1) == 1 && Game.GetIronmode() != 1)
                    Plugin.Instance.StartCoroutine(SaveSoon());
            }

            private static IEnumerator SaveSoon()
            {
                yield return null;
                yield return null; // let the new floor finish settling (end of turn, culling) first
                if (Player.instance != null && !Player.instance.GetIsDead() && !Game.RESOLVING) Game.instance.DoAutosave();
            }
        }
    }
}
