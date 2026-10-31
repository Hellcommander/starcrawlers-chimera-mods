using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// The whole keyboard for bindings. The game's Keybindings screen only took keys on its own name list
    /// (Keybindings.ParseConvention), so Enter, Backspace, Delete, the lock keys, Pause, Print Screen and the numpad
    /// operators couldn't be bound; every key now gets its Unity input name (each one checked against Unity first).
    /// Also: readable key names for Mod Settings, the clashes between a mod key and the game's or another mod key's,
    /// and mod key combos win over the hotbar slot on the same key (Alt+1 gives an order, not bar 3's first slot).
    /// </summary>
    internal static class Keyboard
    {
        // ---- every key bindable in the game's own keybinding screen ------------------------------------------

        /// <summary>Unity input names to try for keys the game's list leaves out, by lower-case KeyCode name.</summary>
        private static readonly Dictionary<string, string[]> Candidates = new Dictionary<string, string[]>
        {
            ["return"] = new[] { "return" }, ["backspace"] = new[] { "backspace" }, ["delete"] = new[] { "delete" },
            ["clear"] = new[] { "clear" }, ["pause"] = new[] { "pause" }, ["capslock"] = new[] { "caps lock" },
            ["numlock"] = new[] { "numlock", "num lock" }, ["scrolllock"] = new[] { "scroll lock" },
            ["keypadperiod"] = new[] { "[.]" }, ["keypaddivide"] = new[] { "[/]" }, ["keypadmultiply"] = new[] { "[*]" },
            ["keypadminus"] = new[] { "[-]" }, ["keypadplus"] = new[] { "[+]" }, ["keypadenter"] = new[] { "enter" },
            ["keypadequals"] = new[] { "[equals]", "equals" }, ["altgr"] = new[] { "alt gr" },
            ["leftwindows"] = new[] { "left super", "left windows" }, ["rightwindows"] = new[] { "right super", "right windows" },
            ["print"] = new[] { "print screen", "print" }, ["sysreq"] = new[] { "sys req" }, ["break"] = new[] { "break" },
            ["menu"] = new[] { "menu" }, ["help"] = new[] { "help" }, ["doublequote"] = new[] { "\"" },
            ["underscore"] = new[] { "_" }, ["leftparen"] = new[] { "(" }, ["rightparen"] = new[] { ")" },
        };

        private static Dictionary<string, string> extra;
        private static bool building;

        private static bool Valid(string name)
        {
            try { Input.GetKey(name); return true; }
            catch { return false; }
        }

        private static void Build()
        {
            building = true;
            extra = new Dictionary<string, string>();
            try
            {
                foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
                {
                    if (k == KeyCode.None || k == KeyCode.Mouse0 || k == KeyCode.Mouse1 || (int)k >= (int)KeyCode.JoystickButton0) continue;
                    string code = k.ToString().ToLowerInvariant();
                    if (extra.ContainsKey(code) || Keybindings.ParseConvention(code) != "") continue;
                    var tries = (Candidates.TryGetValue(code, out string[] c) ? c : new string[0]).Concat(new[] { code });
                    string name = tries.FirstOrDefault(Valid);
                    if (name != null) extra[code] = name;
                }
                Plugin.Log.LogInfo($"Keyboard: {extra.Count} more keys bindable ({string.Join(", ", extra.Keys)})");
            }
            finally { building = false; }
        }

        [HarmonyPatch(typeof(Keybindings), nameof(Keybindings.ParseConvention))]
        private static class Patch_Parse
        {
            private static void Postfix(string kcode, ref string __result)
            {
                if (__result != "" || building || string.IsNullOrEmpty(kcode)) return;
                if (extra == null) Build();
                if (extra.TryGetValue(kcode, out string name)) __result = name;
            }
        }

        /// <summary>The keybinding screen drops "keypad" from names, so Num 1 read as 1; keep it apart.</summary>
        [HarmonyPatch(typeof(Menu_Options), "ParseKeybindDisplay")]
        private static class Patch_Display
        {
            // the method strips "keypad" from its own argument, so keep the name as it came in
            private static void Prefix(string kcode, out string __state) => __state = kcode;

            private static void Postfix(TMP_Text t, string __state)
            {
                string kcode = __state;
                if (t == null || string.IsNullOrEmpty(kcode)) return;
                if (Enum.TryParse(kcode, true, out KeyCode k) && (kcode.StartsWith("keypad") || extra != null && extra.ContainsKey(kcode)))
                    t.text = KeyName(k).ToUpperInvariant();
            }
        }

        // ---- mod key combos come first --------------------------------------------------------------------

        /// <summary>
        /// The hotbar reads Ctrl and Alt as "use bar 2 / bar 3" for any slot key. A mod combo pressed this frame
        /// (Alt+1 for an order, say) isn't also a hotbar press.
        /// </summary>
        [HarmonyPatch(typeof(Menu_HUD), "Update")]
        private static class Patch_Hotbar
        {
            private static bool Prefix()
            {
                if (!Input.anyKeyDown || Keybindings.GetInstance() == null || Keybindings.GetInstance().CONTROLLERMODE) return true;
                foreach (ConfigEntryBase e in Entries())
                    if (e.BoxedValue is KeyboardShortcut ks && ks.Modifiers.Any() && ks.IsDown()) return false;
                return true;
            }
        }

        /// <summary>
        /// Bars 2 and 3 reuse bar 1's slot keys with a modifier held, but every row was labelled 1, 2, 3...; their
        /// labels now carry the modifier ("Ctrl+1", "Alt+1").
        /// </summary>
        [HarmonyPatch(typeof(Action_Icon_Slot), nameof(Action_Icon_Slot.GetHotkey))]
        private static class Patch_SlotLabel
        {
            private static void Postfix(Action_Icon_Slot __instance, ref string __result)
            {
                if (string.IsNullOrEmpty(__result) || __instance.id < 7 || Keybindings.GetInstance() == null || Keybindings.GetInstance().CONTROLLERMODE) return;
                KeyCode mod = Pref(__instance.id < 14 ? "ActionBar2Mod" : "ActionBar3Mod");
                if (mod != KeyCode.None) __result = ModName(mod) + "+" + __result.ToUpperInvariant();
            }
        }

        /// <summary>The longer labels run past the slot's small text box instead of being cut off.</summary>
        [HarmonyPatch(typeof(Action_Icon), nameof(Action_Icon.Refresh))]
        private static class Patch_SlotLabelFit
        {
            private static void Postfix(Action_Icon __instance)
            {
                UnityEngine.UI.Text t = __instance.hotkeyDisplay;
                if (t == null || __instance.slot == null || t.text == null || t.text.IndexOf('+') < 0) return;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        /// <summary>
        /// Moves a setting still on a key it used to default to (one that clashed) to its new default, once: a player
        /// who picks the old key again later keeps it.
        /// </summary>
        internal static void MoveOnce(ConfigEntry<KeyCode> e, KeyCode old, string flag)
        {
            if (PlayerPrefs.GetInt(flag, 0) == 1) return;
            if (e.Value == old) e.Value = (KeyCode)e.DefaultValue;
            PlayerPrefs.SetInt(flag, 1);
            PlayerPrefs.Save();
        }

        // ---- names --------------------------------------------------------------------------------------------

        internal static string KeyName(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.None: return "none";
                case KeyCode.LeftControl: return "Left Ctrl";
                case KeyCode.RightControl: return "Right Ctrl";
                case KeyCode.Return: return "Enter";
                case KeyCode.KeypadEnter: return "Num Enter";
                case KeyCode.KeypadPlus: return "Num +";
                case KeyCode.KeypadMinus: return "Num -";
                case KeyCode.KeypadMultiply: return "Num *";
                case KeyCode.KeypadDivide: return "Num /";
                case KeyCode.KeypadPeriod: return "Num .";
                case KeyCode.KeypadEquals: return "Num =";
                case KeyCode.Escape: return "Esc";
                case KeyCode.BackQuote: return "`";
                case KeyCode.Minus: return "-";
                case KeyCode.Equals: return "=";
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.Semicolon: return ";";
                case KeyCode.Quote: return "'";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                case KeyCode.Slash: return "/";
                case KeyCode.Backslash: return "\\";
                case KeyCode.Print: return "Print Screen";
                case KeyCode.Mouse2: return "Middle Mouse";
            }
            string s = k.ToString();
            if (s.StartsWith("Alpha")) return s.Substring(5);
            if (s.StartsWith("Keypad")) return "Num " + s.Substring(6);
            if (s.StartsWith("Mouse")) return "Mouse " + (int.Parse(s.Substring(5)) + 1);
            return System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z0-9])(?=[A-Z0-9])", " ");
        }

        private static string ModName(KeyCode m)
        {
            switch (Side(m))
            {
                case KeyCode.LeftControl: return "Ctrl";
                case KeyCode.LeftShift: return "Shift";
                case KeyCode.LeftAlt: return "Alt";
                default: return KeyName(m);
            }
        }

        /// <summary>The modifiers held right now, as left-side keys.</summary>
        internal static KeyCode[] HeldModifiers() => Modifiers.Where(Input.GetKey).Select(Side).Distinct().ToArray();

        internal static string ShortcutName(KeyboardShortcut ks) =>
            ks.MainKey == KeyCode.None ? "none" : string.Join(" + ", ks.Modifiers.Select(ModName).Concat(new[] { KeyName(ks.MainKey) }));

        internal static readonly KeyCode[] Modifiers =
            { KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.LeftCommand, KeyCode.RightCommand };

        /// <summary>Right-hand modifiers count as the left ones (a binding of Ctrl is either Ctrl).</summary>
        private static KeyCode Side(KeyCode k) =>
            k == KeyCode.RightControl ? KeyCode.LeftControl : k == KeyCode.RightShift ? KeyCode.LeftShift
            : k == KeyCode.RightAlt ? KeyCode.LeftAlt : k == KeyCode.RightCommand ? KeyCode.LeftCommand : k;

        // ---- clashes -----------------------------------------------------------------------------------------

        private struct Combo
        {
            public KeyCode key;
            public string mods; // sorted, left-side modifier names
            public static Combo Of(KeyCode key, IEnumerable<KeyCode> mods) =>
                new Combo { key = key, mods = string.Join("+", mods.Select(Side).Distinct().OrderBy(m => m)) };
            public bool Same(Combo o) => key == o.key && mods == o.mods;
        }

        private static IEnumerable<ConfigEntryBase> Entries()
        {
            ConfigFile cfg = Plugin.Instance?.Config;
            return cfg == null ? Enumerable.Empty<ConfigEntryBase>() : cfg.Keys.Select(k => cfg[k]).Where(e => e != null);
        }

        private static Combo? ComboOf(ConfigEntryBase e)
        {
            if (e.BoxedValue is KeyCode k && k != KeyCode.None) return Combo.Of(k, new KeyCode[0]);
            if (e.BoxedValue is KeyboardShortcut ks && ks.MainKey != KeyCode.None) return Combo.Of(ks.MainKey, ks.Modifiers);
            return null;
        }

        /// <summary>The game's keyboard bindings (PlayerPrefs name, what it does).</summary>
        private static readonly (string pref, string name)[] GameKeys =
        {
            ("Wait", "Wait"), ("MoveForward", "Move forward"), ("MoveBackward", "Move back"), ("TurnLeft", "Turn left"),
            ("TurnRight", "Turn right"), ("StrafeRight", "Strafe right"), ("StrafeLeft", "Strafe left"), ("RecenterCam", "Recenter camera"),
            ("MapUp", "Map up"), ("MapDown", "Map down"), ("MapLeft", "Map left"), ("MapRight", "Map right"), ("ESCKey", "Menu"),
            ("MenuCharacter", "Character"), ("MenuSkills", "Skills"), ("MenuInventory", "Inventory"), ("MenuContracts", "Contracts"),
            ("MenuMap", "Map"), ("MenuSystem", "System menu"), ("MenuQuicksave", "Quicksave"), ("Mod", "Force action"),
            ("SpecialAction0", "Special action 1"), ("SpecialAction1", "Special action 2"), ("SpecialAction2", "Special action 3"),
            ("SpecialAction3", "Special action 4"), ("ActionBar2Mod", "Action bar 2 key"), ("ActionBar3Mod", "Action bar 3 key"),
            ("FreeLookToggle", "Free look"), ("WeaponSwap", "Weapon swap"), ("ItemCompare", "Item compare"),
            ("HighlightItems", "Highlight items"), ("Flashlight", "Flashlight"),
        };

        private static KeyCode Pref(string name) =>
            Enum.TryParse(PlayerPrefs.GetString(name, ""), true, out KeyCode k) ? k : KeyCode.None;

        private static IEnumerable<(Combo combo, string name)> GameCombos()
        {
            foreach (var (pref, name) in GameKeys)
            {
                KeyCode k = Pref(pref);
                if (k != KeyCode.None) yield return (Combo.Of(k, new KeyCode[0]), "the game's " + name);
            }
            KeyCode bar2 = Pref("ActionBar2Mod"), bar3 = Pref("ActionBar3Mod");
            for (int i = 0; i < 21; i++)
            {
                KeyCode k = Pref("Action" + (i + 1));
                if (k == KeyCode.None) continue;
                if (i < 7) yield return (Combo.Of(k, new KeyCode[0]), $"hotbar slot {i + 1}");
                else if (i < 14 && bar2 != KeyCode.None && PlayerPrefs.GetInt("Pref_actionBar2", 1) == 1) yield return (Combo.Of(k, new[] { bar2 }), $"action bar 2 slot {i - 6}");
                else if (i >= 14 && bar3 != KeyCode.None && PlayerPrefs.GetInt("Pref_actionBar3", 0) == 1) yield return (Combo.Of(k, new[] { bar3 }), $"action bar 3 slot {i - 13}");
            }
        }

        /// <summary>What else is bound to this setting's key ("" when nothing).</summary>
        internal static string Clashes(ConfigEntryBase e)
        {
            Combo? mine = ComboOf(e);
            if (mine == null) return "";
            var hits = new List<string>();
            foreach (ConfigEntryBase o in Entries())
                if (o != e && ComboOf(o) is Combo c && c.Same(mine.Value))
                    hits.Add($"{o.Definition.Section} / {o.Definition.Key}");
            foreach (var (c, name) in GameCombos())
                if (c.Same(mine.Value)) hits.Add(name);
            return string.Join(", ", hits.Distinct());
        }
    }
}
