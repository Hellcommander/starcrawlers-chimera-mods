using System;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Classes that wait to be earned. A class with <c>"unlock": "win"</c> stays off the creation screen (the vanilla
    /// grid, its random picks and the Conjoined body picker) until this computer has seen the game won: a win recorded
    /// by the loader (a PlayerPrefs flag set when the victory screen shows), a won run in the graveyard, or the game's
    /// own Steam achievement for escaping. The class is still registered, so a save that already has it loads as usual.
    /// [Unlocks] UnlockAll shows every class anyway.
    /// </summary>
    internal static class Unlocks
    {
        internal static ConfigEntry<bool> UnlockAll;
        private const string WonPref = "SCCL_HasWon";
        private static bool? won;

        internal static void Bind(ConfigFile cfg)
        {
            UnlockAll = cfg.Bind("Unlocks", "UnlockAll", false,
                "On: classes that are earned by winning the game show on the creation screen before you have won.");
        }

        /// <summary>Has this computer seen the game won? Read once, then remembered (a win sets it straight away).</summary>
        internal static bool HasWon
        {
            get
            {
                if (won == null)
                {
                    bool w = PlayerPrefs.GetInt(WonPref, 0) == 1;
                    if (!w) { try { w = Graveyard.Load().Any(e => e.won); } catch { } }
                    if (!w) w = SteamWin();
                    won = w;
                }
                return won.Value;
            }
        }

        private static bool SteamWin()
        {
            try { return SteamManager.Initialized && Steamworks.SteamUserStats.GetAchievement("ACH_WIN_2020", out bool got) && got; }
            catch { return false; }
        }

        /// <summary>Is this tree a class still waiting to be earned?</summary>
        internal static bool IsLocked(string treeId)
        {
            if (UnlockAll != null && UnlockAll.Value) return false;
            ClassDef def = ClassRegistry.AllDefs.FirstOrDefault(d => d.id == treeId);
            if (def == null || string.IsNullOrEmpty(def.unlock)) return false;
            if (def.unlock.Equals("win", StringComparison.OrdinalIgnoreCase)) return !HasWon;
            Plugin.Log.LogWarning($"{def.id}: unknown \"unlock\" '{def.unlock}' (only \"win\"); left unlocked");
            return false;
        }

        /// <summary>Devtests: pretend this computer has (true) or hasn't (false) seen a win; null reads it again.</summary>
        internal static void ForTest(bool? w) => won = w;

        internal static void MarkWon()
        {
            won = true;
            PlayerPrefs.SetInt(WonPref, 1);
            PlayerPrefs.Save();
        }

        [HarmonyPatch(typeof(HUD), nameof(HUD.WinGame))]
        private static class Patch_Won
        {
            private static void Postfix()
            {
                try { MarkWon(); }
                catch (Exception ex) { Plugin.Log.LogError("Unlocks: " + ex); }
            }
        }

        /// <summary>Takes locked classes out of the vanilla creation grid before CreationLayout sorts it. Mod trees come
        /// after the 36 vanilla ones, so the built-in presets' grid indices are untouched.</summary>
        [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.Show))]
        private static class Patch_Show
        {
            [HarmonyPriority(Priority.High)]
            private static void Postfix(Menu_Creation __instance)
            {
                for (int i = __instance.madSkills.Count - 1; i >= 0; i--)
                {
                    SkillSelect s = __instance.madSkills[i];
                    if (s == null || s.skillTree == null || !IsLocked(s.skillTree.internalName)) continue;
                    __instance.madSkills.RemoveAt(i);
                    s.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(s.gameObject);
                }
            }
        }
    }
}
