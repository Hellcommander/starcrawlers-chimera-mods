using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// "UH OH! Something went wrong during the turn ... use K to attempt recovery": a skill hit an error halfway, its
    /// coroutine died, and the turn waited for it forever until the game's watchdog gave up. Two layers now:
    /// <list type="bullet">
    /// <item>Every skill's Use() runs guarded: an error ends that skill there and the turn carries on as normal
    /// (enemies act, cooldowns tick). The game log names the skill; BepInEx/LogOutput.log has the full error.</item>
    /// <item>If a turn still gets stuck (an error deeper inside), the watchdog's abort is followed by a real recovery:
    /// the turn is released so you can act again, without pressing K, and the log says which skill it was.</item>
    /// </list>
    /// [Fixes] StuckTurnRecovery turns both off.
    /// </summary>
    internal static class StuckTurns
    {
        internal static ConfigEntry<bool> Fix;

        private static bool watchdogFired;
        private static string lastError = "";
        private static float lastErrorAt = -999f;
        /// <summary>Devtest: count of guarded errors and recoveries.</summary>
        internal static int Caught, Recovered;

        internal static void Bind(ConfigFile cfg)
        {
            Fix = cfg.Bind("Fixes", "StuckTurnRecovery", true,
                "A skill that hits an error stops there and your turn goes on, instead of freezing the turn ('UH OH! ... use K to attempt recovery'). A turn that still gets stuck is freed by itself. The log names the skill.");
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
            {
                lastError = message + (string.IsNullOrEmpty(stack) ? "" : "\n" + stack);
                lastErrorAt = Time.realtimeSinceStartup;
            }
            else if (message != null && message.StartsWith("Aborting player action", StringComparison.Ordinal)) watchdogFired = true;
        }

        /// <summary>From Plugin.Update: the frame after the watchdog aborted a turn, release it.</summary>
        internal static void Tick()
        {
            if (!watchdogFired) return;
            watchdogFired = false;
            if (!Fix.Value || !Game.RESOLVING || Game.Game_isLoading || Player.instance == null || Player.instance.GetIsDead()) return;
            try { Recover(); }
            catch (Exception e) { Plugin.Log.LogError("Stuck turn recovery failed: " + e); }
        }

        private static void Recover()
        {
            Ability stuck = Game.PLAYER_ABILITY;
            string what = stuck != null ? stuck.GetDisplayName() : Traverse.Create(Game.instance).Field("resolvingAction").GetValue()?.ToString() ?? "an action";
            bool recentError = Time.realtimeSinceStartup - lastErrorAt < Game.instance.resolveTimeMax + 5f;
            Plugin.Log.LogWarning($"STUCK TURN: {what} ({stuck?.internalName ?? "-"}) never finished; releasing the turn." +
                                  (recentError ? "\nLast error before it:\n" + lastError : "\nNo error was logged; it was waiting on something that never came."));
            Game.ClearPlayerAbility();
            Game.CancelAbilityTargeting();
            Game.USING_INSTANT_ABILITY = false;
            Game.SetResolve(flag: false);
            Map.instance?.TurnOver();
            CullManager.instance?.UpdateCulling();
            Tooltip.instance?.Refresh();
            Menu_HUD.instance?.Refresh_Hotbar();
            Recovered++;
            Game.LOG($"<color=#9FD8FF>{what} got stuck{(recentError ? " on a game error" : "")}. The turn has been freed; you can act again.</color>");
        }

        // ---- guarded skills ------------------------------------------------------------------------------------

        /// <summary>Every Use() a skill class declares (player, enemy and mod skills): an error ends it cleanly.</summary>
        [HarmonyPatch]
        private static class Patch_Use
        {
            private static IEnumerable<MethodBase> TargetMethods() =>
                typeof(Ability).Assembly.GetTypes().Concat(typeof(StuckTurns).Assembly.GetTypes())
                    .Where(t => typeof(Ability).IsAssignableFrom(t))
                    .Select(t => t.GetMethod("Use", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null))
                    .Where(m => m != null && m.ReturnType == typeof(IEnumerator) && !m.IsAbstract)
                    .Distinct();

            private static void Postfix(Ability __instance, ref IEnumerator __result)
            {
                if (Fix.Value && __result != null) __result = Guard(__result, __instance);
            }
        }

        private static IEnumerator Guard(IEnumerator inner, Ability a)
        {
            while (true)
            {
                object current = null;
                bool more;
                try
                {
                    more = inner.MoveNext();
                    if (more) current = inner.Current;
                }
                catch (Exception e)
                {
                    more = false;
                    Report(a, e);
                }
                if (!more) yield break;
                yield return current;
            }
        }

        private static void Report(Ability a, Exception e)
        {
            Caught++;
            Character by = a != null ? Traverse.Create(a).Field("caster").GetValue<Character>() : null;
            string name = a == null ? "A skill" : a.GetDisplayName();
            Plugin.Log.LogWarning($"SKILL ERROR: {name} ({a?.internalName}, used by {(by != null ? by.GetName() : "?")}) stopped early: {e}");
            if (by != null && by == Player.instance) Game.LOG($"<color=#9FD8FF>{name} hit a game error and stopped early; your turn goes on.</color>");
        }

        /// <summary>The watchdog's "press K and report it" lines: with the fix on the mod frees the turn itself.</summary>
        [HarmonyPatch(typeof(Game), nameof(Game.LOG), typeof(string))]
        private static class Patch_Log
        {
            private static bool Prefix(string s) =>
                !(Fix.Value && s != null && (s.Contains("(Skill Menu hotkey) to attempt recovery") || s.Contains("Please report this bug using the Feedback tool")));
        }
    }
}
