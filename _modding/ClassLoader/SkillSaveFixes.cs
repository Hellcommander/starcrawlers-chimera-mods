using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>
    /// Skill state that loading a save threw away. [Fixes] SkillStateOnLoad turns it off.
    /// <list type="bullet">
    /// <item><b>Combo Builder</b> (Combology): it saves its state in its own record type, but loading only reads the
    /// standard ability record, so the combo counter, last skill class, Button Masher progress and the cooldown that
    /// gates Konomo Code all reset on every load (and the Combo Point icon went back to neutral). The record is now
    /// read, and the cooldown and Combo Point class ride along in it.</item>
    /// </list>
    /// </summary>
    internal static class SkillSaveFixes
    {
        internal static ConfigEntry<bool> Fix;

        internal static void Bind(ConfigFile cfg)
        {
            Fix = cfg.Bind("Fixes", "SkillStateOnLoad", true,
                "Combo Builder's combo, Button Masher progress and Konomo Code cooldown survive saving and loading.");
        }

        // ---- Combo Builder ------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(PA_PASSIVE_ComboBuilder), nameof(Ability.Serialize))]
        private static class Patch_ComboSave
        {
            private static void Postfix(PA_PASSIVE_ComboBuilder __instance, object __result)
            {
                if (!Fix.Value || !(__result is ComboBuilderSaveInfo info)) return;
                info.cooldown_amount = __instance.cooldown_amount;
                if (__instance.comboPoint != null) info.string_a = __instance.comboPoint.skillClass.ToString();
            }
        }

        /// <summary>After the player's effects are back (the Combo Point among them), read the record vanilla skipped.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.Deserialize))]
        private static class Patch_ComboLoad
        {
            private static void Postfix(object d)
            {
                if (!Fix.Value || !((d as PlayerSaveInfo)?.abilities is AbilityControlSaveInfo saved) || saved.abilitySaveInfo == null) return;
                ComboBuilderSaveInfo info = saved.abilitySaveInfo.OfType<ComboBuilderSaveInfo>().FirstOrDefault();
                var cb = Player_AbilityControl.instance?.GetAbilities().OfType<PA_PASSIVE_ComboBuilder>().FirstOrDefault();
                if (info == null || cb == null) return;
                cb.Deserialize(info);
                cb.cooldown_amount = info.cooldown_amount;
                if (cb.comboPoint != null && System.Enum.TryParse(info.string_a, out SKILL_CLASS cls))
                {
                    cb.comboPoint.skillClass = cls;
                    cb.comboPoint.UpdateEffect();
                    Menu_HUD.instance?.UpdatePlayerEffects();
                }
            }
        }
    }
}
