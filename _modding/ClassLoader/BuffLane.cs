using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// What a buff costs beyond its own numbers, with [Performance] EventBus on. Every change to an effect on you recalculated
    /// your stats and, through that, redrew the HUD and the character sheet (open or not, with eleven full rebuilds of your
    /// resistances), and walked your icons, so a skill that stacks something per hit paid for all of it per hit.
    ///
    /// The numbers stay exact: stats are still worked out at once whenever they can move, and only the recalculations that
    /// provably change nothing go (an effect with no stats, or the game doing it twice). Drawing them is what batches: the
    /// first change in a frame draws at once, the rest of that frame draw together from the bus's pump. Nothing here may skip
    /// Player.CalculateStats itself: StatsFrom's postfix adds to the stats it leaves, so a skipped call would add twice.
    /// </summary>
    internal static class BuffLane
    {
        internal static int Spared;

        /// <summary>One piece of drawing: at most the first call of a frame and one more from the pump.</summary>
        private sealed class Gate
        {
            private int frame = -1;
            internal bool owed;
            internal int held;

            internal bool Now()
            {
                if (flushing || !EventBus.On || Game.Game_isLoading) return true;
                if (frame != Time.frameCount)
                {
                    frame = Time.frameCount;
                    owed = false;
                    return true;
                }
                owed = true;
                held++;
                return false;
            }
        }

        private static readonly Gate hud = new Gate(), sheet = new Gate(), icons = new Gate();
        private static bool flushing;

        internal static int HudHeld => hud.held;
        internal static int SheetHeld => sheet.held;
        internal static int IconsHeld => icons.held;

        /// <summary>From the bus's pump, once a frame.</summary>
        internal static void Flush()
        {
            if (!hud.owed && !sheet.owed && !icons.owed) return;
            flushing = true;
            try
            {
                if (Player.instance != null && Menu_HUD.instance != null)
                {
                    if (icons.owed) Menu_HUD.instance.UpdatePlayerEffects();
                    if (hud.owed) Menu_HUD.instance.Refresh();
                }
                if (sheet.owed && Menu_Player.instance != null && Menu_Player.instance.GetIsShowing()) Menu_Player.instance.Refresh();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Buff lane: " + e.Message); }
            finally
            {
                flushing = false;
                hud.owed = sheet.owed = icons.owed = false;
            }
        }

        internal static void Forget()
        {
            hud.owed = sheet.owed = icons.owed = false;
            if (where.Count == 0 && indexed == 0) return;
            where.Clear();
            indexed = 0;
            indexedList = null;
            // the game never takes out what a load destroyed, so these are all still in the list
            EffectManager.instance?.activeEffects.RemoveAll(Destroyed);
        }

        private static readonly Predicate<Effect> Destroyed = e => e == null;

        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.Refresh))]
        private static class Patch_Hud
        {
            private static bool Prefix() => hud.Now();
        }

        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.UpdatePlayerEffects))]
        private static class Patch_Icons
        {
            private static bool Prefix() => icons.Now();
        }

        /// <summary>
        /// Shown, it's another piece of drawing. Closed, nobody can read it, and Show refreshes it anyway. Only the end of it does
        /// anything for the game: Player.CalculateStats clears FLAG_canBlock and counts on this to set it back, in the same call.
        /// </summary>
        [HarmonyPatch(typeof(Menu_Player), nameof(Menu_Player.Refresh))]
        private static class Patch_Sheet
        {
            private static bool Prefix(Menu_Player __instance, ref Player ___player)
            {
                if (flushing || !EventBus.On || Game.Game_isLoading) return true;
                if (__instance.GetIsShowing() && sheet.Now()) return true;
                if (___player == null) ___player = Player.instance; // ModShield reads it, and only Show and Refresh set it
                if (__instance.slot_offhand.item != null && __instance.slot_offhand.item.BlockCheck()) Player.instance.FLAG_canBlock = true;
                else if (__instance.slot_mainhand.item != null && __instance.slot_mainhand.item.BlockCheck()) Player.instance.FLAG_canBlock = true;
                if (Player_Control.instance.weapon_l != null && __instance.playerShieldItem != null) Player_Control.instance.weapon_l.Refresh_Shield(__instance.playerShieldItem.charge);
                return false;
            }
        }

        // ---- stats

        /// <summary>True for a mod effect that moves no stat at any stack count, so it needs no recalculation coming or going.</summary>
        internal static bool MovesNothing(Effect e) => EventBus.On && e is ModEffect m && m.attributeMods.Count == 0 && m.resistanceMods.Count == 0;

        /// <summary>What a mod effect calls after it is gone: the game already recalculated as it left.</summary>
        internal static void AfterRemoved(Character c)
        {
            if (!EventBus.On) c.CalculateStats();
        }

        private static void Recalculate(Character c, Effect e)
        {
            if (MovesNothing(e)) Spared++;
            else c.CalculateStats();
        }

        [HarmonyPatch]
        private static class Patch_AddedAndCleared
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Character_Effect_Control), nameof(Character_Effect_Control.AddEffect));
                yield return AccessTools.Method(typeof(Character_Effect_Control), nameof(Character_Effect_Control.ClearEffect));
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
            {
                MethodInfo calculate = AccessTools.Method(typeof(Character), nameof(Character.CalculateStats));
                foreach (CodeInstruction i in code)
                {
                    if (!i.Calls(calculate)) { yield return i; continue; }
                    yield return new CodeInstruction(OpCodes.Ldarg_1).WithLabels(i.ExtractLabels());
                    yield return CodeInstruction.Call(typeof(BuffLane), nameof(Recalculate));
                }
            }
        }

        // ---- lists

        /// <summary>The game checks each effect against all the others for duplicates, but AddEffect is the one way in and already refuses them.</summary>
        [HarmonyPatch(typeof(Character_Effect_Control), nameof(Character_Effect_Control.GetEffects), new[] { typeof(bool) })]
        private static class Patch_GetEffects
        {
            private static bool Prefix(Character_Effect_Control __instance, bool includeHidden, ref List<Effect> __result)
            {
                if (!EventBus.On) return true;
                List<Effect> all = __instance.effects;
                var found = new List<Effect>(all.Count);
                for (int i = 0; i < all.Count; i++)
                {
                    Effect e = all[i];
                    if (e != null && (includeHidden || !e.isHidden)) found.Add(e);
                }
                __result = found;
                return false;
            }
        }

        // activeEffects is only there to be searched when an effect ends, a walk over every effect on every character. Nothing
        // reads its order, so one is found by index and the last takes its place. The game only ever adds at the end of it.
        private static readonly Dictionary<Effect, int> where = new Dictionary<Effect, int>();
        private static List<Effect> indexedList;
        private static int indexed;

        private static bool Take(EffectManager manager, Effect e)
        {
            List<Effect> list = manager.activeEffects;
            if (list != indexedList || indexed > list.Count)
            {
                where.Clear();
                indexed = 0;
                indexedList = list;
            }
            for (; indexed < list.Count; indexed++)
                if ((object)list[indexed] != null) where[list[indexed]] = indexed;
            if (!where.TryGetValue(e, out int at)) return false;
            where.Remove(e);
            int last = list.Count - 1;
            Effect moved = list[last];
            list[at] = moved;
            list.RemoveAt(last);
            indexed = list.Count;
            if (at != last && (object)moved != null) where[moved] = at;
            return true;
        }

        [HarmonyPatch(typeof(EffectManager), nameof(EffectManager.EffectExpired))]
        private static class Patch_Expired
        {
            private static bool Prefix(EffectManager __instance, Effect effect)
            {
                if (!EventBus.On) return true;
                if (effect != null && Take(__instance, effect)) __instance.clearEffects.Add(effect);
                return false;
            }
        }

        [HarmonyPatch(typeof(EffectManager), nameof(EffectManager.EffectExpiredInstant))]
        private static class Patch_ExpiredInstant
        {
            private static bool Prefix(EffectManager __instance, Effect effect)
            {
                if (!EventBus.On) return true;
                if (effect == null) return false;
                Take(__instance, effect);
                UnityEngine.Object.Destroy(effect.gameObject);
                return false;
            }
        }
    }
}
