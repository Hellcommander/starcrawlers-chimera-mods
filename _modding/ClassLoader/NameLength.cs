using System;
using BepInEx.Configuration;
using HarmonyLib;

namespace SCClassLoader
{
    /// <summary>
    /// Longer character names. The creation screen's name box has a short limit set in the game's UI; [Creation]
    /// MaxNameLength raises it (never lowers it). The ChimeraLinq sign-up box, which starts out holding your name, gets
    /// the same room so it doesn't cut the name off. With [Creation] AutoCapitalizeNames off (the default) the box keeps
    /// the case you type ("Vex the Unbroken", "McAllister"): the game's Name validation capitalises each word and
    /// lowercases the rest; the replacement keeps its character rules (letters, single spaces, hyphens, one apostrophe, which keep the
    /// name safe as a save file name) and leaves the case alone.
    /// </summary>
    internal static class NameLength
    {
        internal static ConfigEntry<int> MaxNameLength;
        internal static ConfigEntry<bool> AutoCapitalizeNames;

        /// <summary>The name box's own limit, read the first time the creation screen opens.</summary>
        internal static int VanillaLimit = -1;

        internal static void Bind(ConfigFile cfg)
        {
            MaxNameLength = cfg.Bind("Creation", "MaxNameLength", 32,
                new ConfigDescription("Longest character name you can type on the creation screen. 0 (or anything under the game's own limit) keeps the game's limit.",
                    new AcceptableValueRange<int>(0, 64)));
            AutoCapitalizeNames = cfg.Bind("Creation", "AutoCapitalizeNames", false,
                "On: the name box capitalises each word and lowercases the rest, as in the game. Off: it keeps the case you type, for titles like \"Vex the Unbroken\".");
            // Changed in Mod Settings with the creation screen open: the name box follows at once.
            MaxNameLength.SettingChanged += (_, __) => Apply(Menu_Creation.instance);
            AutoCapitalizeNames.SettingChanged += (_, __) => Apply(Menu_Creation.instance);
        }

        /// <summary>The game's Name validation (TextMeshPro) without its case changes.</summary>
        internal static char KeepCase(string text, int pos, char ch)
        {
            if (char.IsLetter(ch)) return ch;
            char prev = text.Length > 0 ? text[UnityEngine.Mathf.Clamp(pos - 1, 0, text.Length - 1)] : ' ';
            char at = text.Length > 0 ? text[UnityEngine.Mathf.Clamp(pos, 0, text.Length - 1)] : ' ';
            char next = text.Length > 0 ? text[UnityEngine.Mathf.Clamp(pos + 1, 0, text.Length - 1)] : '\n';
            bool Sep(char c) => c == ' ' || c == '\'' || c == '-';
            if (ch == '\'' && at != ' ' && at != '\'' && next != '\'' && !text.Contains("'")) return ch;
            if (char.IsLetter(prev) && ch == '-' && at != '-') return ch;
            if ((ch == ' ' || ch == '-') && pos != 0 && !Sep(prev) && !Sep(at) && !Sep(next)) return ch;
            return '\0';
        }

        /// <summary>The limit to use for a box whose own limit is <paramref name="own"/>.</summary>
        internal static int Limit(int own) =>
            own <= 0 || MaxNameLength.Value <= 0 ? own : Math.Max(own, MaxNameLength.Value);

        /// <summary>Sets the creation screen's name box to the current limit and case setting.</summary>
        internal static void Apply(Menu_Creation mc)
        {
            if (mc == null || mc.nameInput == null) return;
            if (VanillaLimit < 0) VanillaLimit = mc.nameInput.characterLimit;
            mc.nameInput.characterLimit = Limit(VanillaLimit);
            // The game sets no validator of its own; one set here wins over the box's Name validation.
            mc.nameInput.onValidateInput = AutoCapitalizeNames.Value ? null : (TMPro.TMP_InputField.OnValidateInput)KeepCase;
        }

        [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.Show))]
        private static class Patch_Creation
        {
            private static void Postfix(Menu_Creation __instance) => Apply(__instance);
        }

        /// <summary>
        /// The character sheet's name has best fit on but overflows sideways, so best fit never shrinks it and a long
        /// name runs out of its header bar. A name too wide for the bar wraps and truncates instead, which lets best
        /// fit shrink it onto one line; names that fit keep the vanilla look.
        /// </summary>
        [HarmonyPatch(typeof(Menu_Player), nameof(Menu_Player.Show))]
        private static class Patch_Sheet
        {
            private static void Postfix(Menu_Player __instance)
            {
                UnityEngine.UI.Text label = __instance.playerName;
                if (label == null || label.preferredWidth <= label.rectTransform.rect.width) return;
                label.horizontalOverflow = UnityEngine.HorizontalWrapMode.Wrap;
                label.verticalOverflow = UnityEngine.VerticalWrapMode.Truncate;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = Math.Min(label.resizeTextMinSize, 10);
                label.resizeTextMaxSize = Math.Max(label.resizeTextMaxSize, label.fontSize);
            }
        }

        [HarmonyPatch(typeof(Menu_Event), nameof(Menu_Event.BTN_ChimeraLinq_NewUser))]
        private static class Patch_LinqName
        {
            private static int own = -1;

            private static void Prefix(Menu_Event __instance)
            {
                if (__instance.new_username == null) return;
                if (own < 0) own = __instance.new_username.characterLimit;
                __instance.new_username.characterLimit = Limit(own);
            }
        }
    }
}
