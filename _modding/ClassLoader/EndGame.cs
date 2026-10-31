using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// The end-game stretch (floor 6's Excavation tunnel and fake apartment, then 7_TheEnd), three player reports:
    /// <list type="bullet">
    /// <item><b>Minions:</b> the tunnel and the fake apartment switch your minions off (SetActive false) for the
    /// cutscene and nothing ever switches them back on. Engineer bots came through the portal invisible and dead to
    /// the touch: their hotbar buttons stuck, no new bot could be built, and their skills hung the turn. They now come
    /// back once you're past those two rooms.</item>
    /// <item><b>Apartment lock:</b> those rooms set Game.LOCK_APARTMENT and nothing ever cleared it, so after the
    /// end-game the portal home (and turn autosave) stayed off for every character until the game was restarted. It is
    /// cleared whenever a level unloads; the rooms that need it set it again. With PortalHomeInEndGame the portal also
    /// works on the end-game floors (to restock before the last fight), except in the fake apartment, boss rooms, on the
    /// lift and once the final fight has begun.</item>
    /// <item><b>Draw distance:</b> 7_TheEnd raises the character draw distance from 20 to 100 m and the reset meant
    /// for other maps reset the door distance instead, so characters drew out to 100 m on every map afterwards.</item>
    /// </list>
    /// </summary>
    internal static class EndGame
    {
        internal static ConfigEntry<bool> MinionsComeBack, ClearApartmentLock, PortalHome, CullReset;
        internal const int VanillaCull = 20;

        internal static void Bind(ConfigFile cfg)
        {
            MinionsComeBack = cfg.Bind("Fixes", "EndGameMinionsComeBack", true,
                "Your minions (Engineer bots and the rest) come back after the end-game tunnel and fake apartment hide them for the cutscene. Vanilla left them hidden for good, with their skills and hotbar buttons broken.");
            ClearApartmentLock = cfg.Bind("Fixes", "ApartmentLockClears", true,
                "The end-game's 'no apartment' lock is cleared when you leave the level. Vanilla kept it until you restarted the game, so the portal home and autosave stayed off for every character afterwards.");
            PortalHome = cfg.Bind("Gameplay", "PortalHomeInEndGame", true,
                "The Apartment Portal works on the end-game floors too, to restock before the final fight. Not in the fake apartment, boss rooms or on the lift, and not once the final fight has begun.");
            CullReset = cfg.Bind("Fixes", "DrawDistanceResets", true,
                "Characters draw out to the normal 20 m again after the final map (vanilla left them at the final map's 100 m on every map afterwards).");
        }

        private static Room PlayerRoom() => Player.instance?.GetMover()?.GetCurrentNavNode()?.GetRoom();

        /// <summary>The two rooms that hide minions for their cutscene (and hide them again every turn you're there).</summary>
        internal static bool InHidingRoom(Room r) => r is Room_Event_Excavation_Final_Room || r is Room_Event_Excavation_TunnelOfDoom;

        // ---- minions ------------------------------------------------------------------------------------------

        /// <summary>Hidden-but-alive minions come back. Returns how many.</summary>
        internal static int ShowMinions()
        {
            if (!MinionsComeBack.Value || Player.instance == null || AI_Manager.instance == null || Game.Game_isLoading) return 0;
            if (InHidingRoom(PlayerRoom())) return 0;
            List<Character> minions = AI_Manager.instance.GetPlayerMinions();
            int shown = 0;
            foreach (Character c in minions)
            {
                if (c == null || c.GetIsDead() || c.gameObject.activeSelf) continue;
                c.gameObject.SetActive(true);
                CullManager.instance?.UpdateCulling(c);
                shown++;
                Plugin.Log.LogInfo($"End-game: {c.GetName()} was still hidden from the cutscene; it's back");
            }
            if (shown > 0)
            {
                AI_Manager.instance.HealthBarCheck();
                Menu_HUD.instance?.Refresh_Hotbar();
            }
            return shown;
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.LevelChangeComplete))]
        private static class Patch_LevelChange
        {
            private static void Postfix() => ShowMinions();
        }

        /// <summary>Also after any turn, for walking back out of the tunnel.</summary>
        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
        private static class Patch_Turn
        {
            private static void Postfix() => ShowMinions();
        }

        // ---- apartment lock and the portal home ------------------------------------------------------------------

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.ClearLevel))]
        private static class Patch_ClearLevel
        {
            private static void Prefix() { if (ClearApartmentLock.Value) Game.LOCK_APARTMENT = false; }
        }

        /// <summary>Why the portal can't be used here, or null if the end-game exception applies.</summary>
        internal static string EndGameBlock()
        {
            Player p = Player.instance;
            if (p == null) return "no player";
            if (Game.FLOOR != 6 && Game.FLOOR != 7) return "not an end-game floor";
            if (p.HasTag("FINALFIGHT")) return "the final fight has begun";
            if (p.transform.parent != null && p.transform.parent.GetComponentInParent<Room_Event_TheEnd>() != null) return "riding the lift";
            if (Endless.Won) return null; // after the win the arena is just a room
            Room r = PlayerRoom();
            if (r == null) return "no room";
            if (r is Room_Event_Excavation_Final_Room) return "in the fake apartment";
            if (r.Properties != null && r.Properties.IsBossRoom) return "in a boss room";
            return null;
        }

        [HarmonyPatch(typeof(PA_ApartmentPortal), nameof(PA_ApartmentPortal.CanUse))]
        private static class Patch_PortalCanUse
        {
            private static void Prefix(out bool __state)
            {
                __state = false;
                if (!PortalHome.Value || !Game.LOCK_APARTMENT || Game.STASIS || EndGameBlock() != null) return;
                __state = true;
                Game.LOCK_APARTMENT = false;
            }

            private static void Postfix(bool __state) { if (__state) Game.LOCK_APARTMENT = true; }
        }

        // ---- draw distance --------------------------------------------------------------------------------------

        private static void ResetCull(CullManager cm)
        {
            if (!CullReset.Value || cm == null) return;
            if (Map.instance == null || Map.instance.internalName != "7_TheEnd") cm.characterCullDistance = VanillaCull;
        }

        [HarmonyPatch(typeof(CullManager), nameof(CullManager.UpdateCulling), new System.Type[0])]
        private static class Patch_Cull
        {
            private static void Prefix(CullManager __instance) => ResetCull(__instance);
        }

        [HarmonyPatch(typeof(CullManager), nameof(CullManager.UpdateCulling), typeof(Character))]
        private static class Patch_CullOne
        {
            private static void Prefix(CullManager __instance) => ResetCull(__instance);
        }
    }
}
