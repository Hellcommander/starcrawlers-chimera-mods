using HarmonyLib;
using LiquidVolumeFX;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Every flask on every body writes its slosh speed into its material, by name, on every physics step: 40,000 writes a second
    /// in a big crowd, seen or not, and each step a slow frame repeats pays for them again. The shader only reads that number
    /// when the flask is drawn. With the bus on the speed still counts up each step, and goes to the material as the flask is
    /// about to draw, so it looks the same.
    /// </summary>
    internal static class LiquidLook
    {
        private static readonly int SpeedId = Shader.PropertyToID("_TurbulenceSpeed");
        private static readonly AccessTools.FieldRef<LiquidVolume, Material> Mat = AccessTools.FieldRefAccess<LiquidVolume, Material>("liqMat");
        private static readonly AccessTools.FieldRef<LiquidVolume, float> Slosh = AccessTools.FieldRefAccess<LiquidVolume, float>("turbulenceSpeed");
        private static readonly AccessTools.FieldRef<LiquidVolume, float> Rate = AccessTools.FieldRefAccess<LiquidVolume, float>("_speed");

        [HarmonyPatch(typeof(LiquidVolume), "FixedUpdate")]
        private static class Patch_Step
        {
            private static bool Prefix(LiquidVolume __instance)
            {
                if (!EventBus.On) return true;
                Slosh(__instance) += Time.fixedDeltaTime * 3f * Rate(__instance);
                return false;
            }
        }

        [HarmonyPatch(typeof(LiquidVolume), nameof(LiquidVolume.OnWillRenderObject))]
        private static class Patch_Drawn
        {
            private static void Postfix(LiquidVolume __instance)
            {
                Material m = Mat(__instance);
                if (EventBus.On && m != null) m.SetFloat(SpeedId, Slosh(__instance) * 4f);
            }
        }
    }
}
