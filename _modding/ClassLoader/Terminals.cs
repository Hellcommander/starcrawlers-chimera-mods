using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// The CorpSec and Special Projects wall terminals get a third choice beside the game's own: "Access its other
    /// functions". Each function rolls one stat the way the terminal's own check does (better with the stat, worse the
    /// deeper you are), and using one spends the terminal, so it is that or the security shutdown. Backing out of the
    /// list leaves the terminal as it was. A failed roll trips an alarm: guards arrive beside you.
    ///   Download schematics (Smarts)  learn a recipe you haven't found (see Schematics)
    ///   Override locks (Agility)      unlock this floor's locked doors and lockers (never sealed, secret or boss doors)
    ///   Pull the floor plan (Recognition)  every room on the floor shows on the map
    ///   Requisition supplies          no roll: pay credits (less with Savvy) for a drop of ammo, medkits and tools
    /// </summary>
    internal static class Terminals
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> BaseChance, PerStat, PerFloor;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Terminals", "OtherFunctions", true, "CorpSec and Special Projects wall terminals offer other functions beside the security shutdown.");
            BaseChance = cfg.Bind("Terminals", "BaseChance", 35, "Success % of a terminal function before stats and depth.");
            PerStat = cfg.Bind("Terminals", "ChancePerStat", 3, "Success % added per point of the function's stat.");
            PerFloor = cfg.Bind("Terminals", "ChancePerFloor", 5, "Success % taken off per floor down.");
        }

        internal static bool IsTerminal(MiniEvent_Trigger t) =>
            t != null && (t.name.Contains("MiniEvent_Wall_CorpSec") || t.name.Contains("MiniEvent_Wall_SpecialProjects"));

        internal static int Chance(STAT_TYPE stat) =>
            Mathf.Clamp(BaseChance.Value + Player.instance.GetStat(stat) * PerStat.Value - PerFloor.Value * Game.FLOOR, 5, 95);

        internal static int SupplyCost() =>
            Mathf.Max(10, Mathf.RoundToInt((40 + 20 * Game.FLOOR) * (1f - Mathf.Min(0.5f, Player.instance.GetStat(STAT_TYPE.Savvy) * 0.02f))));

        /// <summary>Forced result of the next roll (tests): true, false, or null for a real roll.</summary>
        internal static bool? Force;

        private static bool Roll(STAT_TYPE stat, string what)
        {
            int chance = Chance(stat), roll = Random.Range(1, 101);
            bool ok = Force ?? roll <= chance;
            Force = null;
            Plugin.Log.LogInfo($"Terminals: {what} {chance}% ({stat}) rolled {roll}: {(ok ? "success" : "alarm")}");
            return ok;
        }

        /// <summary>The menu behind the third button. Sits on the terminal, so SendMessage("Selected") finds it.</summary>
        internal class Functions : MonoBehaviour
        {
            internal MiniEvent_Trigger trigger;

            public void Selected() => Open(trigger);
        }

        internal static void Open(MiniEvent_Trigger trigger)
        {
            if (trigger == null || trigger.completed) return;
            bool fighting = LevelManager.instance != null && LevelManager.instance.IsInCombat();
            bool schematics = Schematics.Findable(Game.FLOOR + 1).Count > 0;
            int cost = SupplyCost();
            var lines = new List<string> { "The security shell gives way to a maintenance menu. It will take one request before it locks you out." };
            var buttons = new List<(string, Action, Color?)>();
            if (schematics) { buttons.Add(($"Schematics {Chance(STAT_TYPE.Smarts)}%", () => Use(trigger, Download), null)); lines.Add($"<b>Download schematics</b> (Smarts): learn a recipe you haven't found."); }
            if (!fighting) { buttons.Add(($"Locks {Chance(STAT_TYPE.Agility)}%", () => Use(trigger, Locks), null)); lines.Add("<b>Override locks</b> (Agility): this floor's locked doors and lockers open."); }
            else lines.Add("<color=#9FB7C9>Override locks: not while the floor is in lockdown for a fight.</color>");
            buttons.Add(($"Floor plan {Chance(STAT_TYPE.Recognition)}%", () => Use(trigger, Plan), null));
            lines.Add("<b>Pull the floor plan</b> (Recognition): every room on this floor shows on your map.");
            bool afford = Menu_Inventory.instance.GetCredits() >= cost;
            buttons.Add(($"Supplies {cost}c", afford ? () => Use(trigger, () => Supplies(cost)) : (Action)(() => Game.LOG("<color=#FF7272>Not enough credits.</color>")), afford ? (Color?)null : UiKit.DangerColor));
            lines.Add($"<b>Requisition supplies</b> ({cost} credits, less with Savvy): ammo, medkits and tools drop at your feet.");
            lines.Add("<color=#FF9F72>A failed request trips the alarm.</color>");
            buttons.Add(("Back out", null, null));
            Choice.Show("SCCL_TerminalFunctions", "CorpSec Terminal", string.Join("\n", lines), buttons);
        }

        private static void Use(MiniEvent_Trigger trigger, Func<bool> act)
        {
            if (trigger == null || trigger.completed) return;
            trigger.Complete();
            if (!act()) Alarm();
        }

        internal static bool Download()
        {
            if (!Roll(STAT_TYPE.Smarts, "schematics")) { Game.LOG("<color=#FF9F72>The download stalls halfway and the terminal flags your session.</color>"); return false; }
            if (Schematics.LearnRandom("a security terminal") == null) Game.LOG("<color=#9FB7C9>The archive holds nothing new to you.</color>");
            return true;
        }

        internal static bool Locks()
        {
            if (!Roll(STAT_TYPE.Agility, "locks")) { Game.LOG("<color=#FF9F72>The override is rejected and every lock on the floor reports you.</color>"); return false; }
            int doors = 0, lockers = 0;
            foreach (Door d in Object.FindObjectsOfType<Door>())
                if (d != null && d.isLocked && !d.isSealed && !d.isSecret && !d.isBroken && !d.FLAG_isUberDoor) { d.Unlock(); doors++; }
            foreach (Container c in Object.FindObjectsOfType<Container>())
                if (c != null && c.isLocked) { c.Unlock(); lockers++; }
            Game.LOG($"<color=#7FD8FF>Locks released: {doors} door{(doors == 1 ? "" : "s")}, {lockers} locker{(lockers == 1 ? "" : "s")}.</color>");
            return true;
        }

        internal static bool Plan()
        {
            if (!Roll(STAT_TYPE.Recognition, "floor plan")) { Game.LOG("<color=#FF9F72>The floor plan is behind a clearance you don't have, and asking was noticed.</color>"); return false; }
            int n = 0;
            foreach (Room r in Map.instance.GetRooms().Where(r => r != null && !r.Properties.IsSecret)) { r.Discover(); n++; }
            Game.LOG($"<color=#7FD8FF>Floor plan downloaded: {n} rooms on your map.</color>");
            return true;
        }

        private static readonly (string id, int min, int max)[] Drop =
        {
            ("WEAPON_AMMO_Generic", 30, 60), ("CONSUME_Small_Medkit", 1, 2), ("CONSUME_Small_Repair_Kit", 1, 1),
            ("CONSUME_ATK_Grenade", 1, 2), ("CONSUME_LOCKPICK_Omnipick", 1, 1), ("CONSUME_Medium_Medkit", 1, 1),
        };

        internal static bool Supplies(int cost)
        {
            if (Menu_Inventory.instance.GetCredits() < cost) return true;
            Menu_Inventory.instance.AdjustCredits(-cost);
            var got = new List<string>();
            foreach (var (id, min, max) in Drop.OrderBy(_ => Random.value).Take(3))
            {
                int n = Random.Range(min, max + 1);
                if (Crafting.Give(id, n) != null) got.Add($"{ItemRegistry.NameOf(id)}{(n > 1 ? " ×" + n : "")}");
            }
            Game.LOG($"<color=#7FD8FF>A supply hatch opens: {string.Join(", ", got)}.</color>");
            return true;
        }

        /// <summary>Guards for the depth step out beside you, already after you.</summary>
        internal static void Alarm()
        {
            string[] pool = Game.FLOOR < 2 ? new[] { "CORPSEC_GUARD_MELEE", "CORPSEC_GUARD_PISTOL" }
                : Game.FLOOR < 4 ? new[] { "CORPSEC_GUARD_PISTOL", "CORPSEC_TROOPER", "CORPSEC_DRONE" }
                : new[] { "CORPSEC_TROOPER", "CORPSEC_COMMANDO", "CORPSEC_SHARPSHOOTER" };
            int count = 1 + (Game.FLOOR >= 3 ? 1 : 0);
            Vector3 at = Player.instance.transform.position;
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                Character prefab = EnemyRegistry.Resolve(pool[Random.Range(0, pool.Length)]);
                NavNode n = Summons.FreeNode(at, 3.5f) ?? Summons.FreeNode(at, 7f);
                if (prefab == null || n == null) continue;
                Character c = AI_Manager.instance.Spawn_Character(prefab, n.transform.position, AI.AI_TEAM.Hostile);
                if (c == null) continue;
                EnemyAbilities.FacePlayer(c);
                Echoes.Hunt(c);
                spawned++;
            }
            Game.LOG($"<color=#FF7272>ALARM. {(spawned == 1 ? "A guard teleports" : "Guards teleport")} in beside the terminal.</color>");
            if (spawned > 0)
            {
                AI_Manager.instance.outOfCombatTimer = 8;
                LevelManager.instance.EnterCombat();
            }
        }

        // ---- the third button --------------------------------------------------------------------------------------

        /// <summary>The terminal's opening screen (the first of its events): add the button before the screen shows.</summary>
        private static void AddButton(MiniEvent ev)
        {
            if (!Enabled.Value || ev == null || !ev.name.EndsWith("_Intro") || Player.instance == null) return;
            MiniEvent_Trigger trigger = ev.GetComponentInParent<MiniEvent_Trigger>();
            if (!IsTerminal(trigger) || trigger.completed) return;
            if (ev.option_c != "NULL") return; // its own third choice comes first
            var f = trigger.GetComponent<Functions>() ?? trigger.gameObject.AddComponent<Functions>();
            f.trigger = trigger;
            Menu_Event.instance.RegisterButton("Access its other functions", trigger.gameObject, "Selected");
        }

        [HarmonyPatch(typeof(MiniEvent), "ApplyEventText")]
        private static class Patch_Event
        {
            private static void Prefix(MiniEvent __instance) { try { AddButton(__instance); } catch (Exception e) { Plugin.Log.LogError("Terminals: " + e); } }
        }

        [HarmonyPatch(typeof(MiniEvent_Attempt), "ApplyEventText")]
        private static class Patch_Attempt
        {
            private static void Prefix(MiniEvent_Attempt __instance) { try { AddButton(__instance); } catch (Exception e) { Plugin.Log.LogError("Terminals: " + e); } }
        }
    }
}
