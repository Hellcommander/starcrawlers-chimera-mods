using System;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>
    /// Every damage object spells its type out three times over when it's made and once more each time its colour is read, and a
    /// single hit makes several. With the bus on those strings come from a table, so a hit's garbage goes on what it logs.
    /// </summary>
    internal static class DmgText
    {
        private struct Words { public string name, term, color; }

        private static readonly Words[] table = Build();

        private static Words[] Build()
        {
            var values = (DAMAGE_TYPE[])Enum.GetValues(typeof(DAMAGE_TYPE));
            int most = 0;
            foreach (DAMAGE_TYPE d in values) most = Math.Max(most, (int)d);
            var words = new Words[most + 1];
            foreach (DAMAGE_TYPE d in values)
                words[(int)d] = new Words { name = d.ToString(), term = "General/" + d, color = DmgObj.GetDamageColor(d) };
            return words;
        }

        private static bool Known(DAMAGE_TYPE d) => d >= 0 && (int)d < table.Length && table[(int)d].name != null;

        [HarmonyPatch(typeof(DmgObj), MethodType.Constructor, new[] { typeof(DAMAGE_TYPE), typeof(int) })]
        private static class Patch_New
        {
            // skipping the constructor skips its field defaults too, and this sets every field they cover
            private static bool Prefix(DmgObj __instance, DAMAGE_TYPE dt, int dam)
            {
                if (!EventBus.On || !Known(dt)) return true;
                Words w = table[(int)dt];
                __instance.name = w.name;
                __instance.dmgType = dt;
                __instance.damage = dam;
                __instance.damageMax = 0;
                __instance.text = w.term;
                __instance.textColor = w.color;
                return false;
            }
        }

        [HarmonyPatch(typeof(DmgObj), nameof(DmgObj.GetTextColor))]
        private static class Patch_Color
        {
            private static bool Prefix(DmgObj __instance, ref string __result)
            {
                if (!EventBus.On || !Known(__instance.dmgType)) return true;
                Words w = table[(int)__instance.dmgType];
                __instance.text.mTerm = w.term;
                if (__instance.textColor == "") __instance.textColor = w.color;
                __result = __instance.textColor;
                return false;
            }
        }
    }
}
