using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// The hover tip over a character follows the mouse; with several long mod effects it grew tall enough for the game
    /// to slide it under the cursor, where it swallowed clicks on the world. Now it shows only while a key is held
    /// (Left Alt by default; the cursor still turns red over enemies), mod effects get a compact row in it (the effect
    /// icon's own tip still has the full text), and character and targeting tips let clicks through.
    /// </summary>
    internal static class HoverTip
    {
        internal static ConfigEntry<bool> NeedsKey;
        internal static ConfigEntry<KeyCode> Key;
        /// <summary>Devtest: stands in for the key.</summary>
        internal static bool? TestHeld;

        private static Character hovered;
        /// <summary>Whether the tip on screen is the character tip (and not a targeting, item or ability tip).</summary>
        private static bool characterTip;

        private static bool Held => TestHeld ?? Input.GetKey(Key.Value);
        private static bool Gated => NeedsKey != null && NeedsKey.Value;

        internal static void Bind(ConfigFile cfg)
        {
            NeedsKey = cfg.Bind("Interface", "CharacterTipNeedsKey", true, "The tip over a character (name, health, effects) shows only while you hold Character Tip Key, so it never covers what you're clicking. Off: it shows on every mouse-over, as in the base game. Targeting tips always show.");
            Key = cfg.Bind("Interface", "CharacterTipKey", KeyCode.LeftAlt, "Hold this while pointing at a character to see its tip.");
        }

        internal static void Tick()
        {
            Tooltip tip = Tooltip.instance;
            if (!Gated || hovered == null || tip == null || tip.content == null) return;
            if (hovered.GetIsDead() || !tip.IsShowing() || !characterTip) { hovered = null; return; }
            GameObject content = tip.content.gameObject;
            if (Held && !content.activeSelf) tip.Show_CharacterTip(hovered);
            else if (!Held && content.activeSelf) content.SetActive(false);
        }

        private static void Clicks(Tooltip t, bool block)
        {
            if (t?.content == null) return;
            CanvasGroup g = t.content.GetComponent<CanvasGroup>() ?? t.content.gameObject.AddComponent<CanvasGroup>();
            g.blocksRaycasts = block;
        }

        // every tip starts here (items can be interacted with, so they keep blocking)
        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.Show))]
        private static class Patch_Show
        {
            private static void Postfix(Tooltip __instance) { Clicks(__instance, true); characterTip = false; }
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.Show_CharacterTip))]
        private static class Patch_Character
        {
            private static void Postfix(Tooltip __instance, Character c)
            {
                Clicks(__instance, false);
                if (c == null || !__instance.IsShowing()) return;
                hovered = c;
                characterTip = true;
                if (Gated && !Held && __instance.content != null) __instance.content.gameObject.SetActive(false);
            }
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.Clear_CharacterTip))]
        private static class Patch_Clear
        {
            private static void Postfix(Character c) { if (c == hovered) hovered = null; }
        }

        [HarmonyPatch(typeof(Tooltip), nameof(Tooltip.Show_TargetingTip))]
        private static class Patch_Targeting
        {
            private static void Postfix(Tooltip __instance) => Clicks(__instance, false);
        }

        [HarmonyPatch(typeof(Tooltip_Effect), "SetText")]
        private static class Patch_EffectRow
        {
            private static void Postfix(Tooltip_Effect __instance, Effect e)
            {
                try
                {
                    if (!(e is ModEffect me) || __instance.text == null) return;
                    string full = "\n" + me.GetDescription(), t = __instance.text.text;
                    if (!t.EndsWith(full, StringComparison.Ordinal)) return;
                    string brief = me.ShortDescription();
                    __instance.text.text = t.Substring(0, t.Length - full.Length) + (brief.Length > 0 ? "\n" + brief : "");
                }
                catch (Exception ex) { Plugin.Log.LogWarning("Hover tip: " + ex.Message); }
            }
        }
    }
}
