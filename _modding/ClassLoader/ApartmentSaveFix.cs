using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>
    /// Vanilla PA_ApartmentPortal.Use() quicksaves every time you warp to or from the apartment, even with autosave
    /// turned off. This makes that save follow the autosave toggle. Ironmode keeps the forced save, since there it
    /// stops save-scumming.
    /// </summary>
    [HarmonyPatch]
    internal static class ApartmentSaveFix
    {
        /// <summary>Replaces the portal's <c>Game.instance.Quicksave()</c>.</summary>
        public static void PortalQuicksave(Game game)
        {
            if (!Plugin.ApartmentSaveFollowsAutosave.Value || SaveGating.AutosaveOn) game.Quicksave();
            else Plugin.Log.LogInfo("Apartment portal: autosave is off, skipping the forced save");
        }

        private static MethodBase TargetMethod() => AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(PA_ApartmentPortal), nameof(PA_ApartmentPortal.Use)));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) =>
            SwapQuicksave(code, AccessTools.Method(typeof(ApartmentSaveFix), nameof(PortalQuicksave)), "Apartment portal");

        /// <summary>Points the one <c>Game.Quicksave()</c> call in a coroutine at <paramref name="helper"/>.</summary>
        internal static List<CodeInstruction> SwapQuicksave(IEnumerable<CodeInstruction> code, MethodInfo helper, string what)
        {
            var list = code.ToList();
            MethodInfo quicksave = AccessTools.Method(typeof(Game), nameof(Game.Quicksave));
            int n = 0;
            foreach (CodeInstruction ci in list.Where(ci => ci.Calls(quicksave)))
            {
                ci.opcode = OpCodes.Call;
                ci.operand = helper;
                n++;
            }
            if (n != 1) Plugin.Log.LogWarning($"{what} save fix: found {n} places to patch (expected 1)");
            return list;
        }
    }
}
