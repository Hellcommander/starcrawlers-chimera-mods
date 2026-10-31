using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// "Mod Settings" in the game's Options menu: every loader setting from the BepInEx config, by section, with
    /// toggles, steppers and key capture (single keys and combos like Alt+1, with a note when the key is taken). Changes save to the config file right away (the file and the page always
    /// agree); almost all of them apply at once, the few that need a restart say so.
    /// </summary>
    internal static class ModSettings
    {
        /// <summary>Settings only read while the game starts.</summary>
        private static readonly HashSet<string> RestartOnly = new HashSet<string> { "Classes/TierSlots", "Audio/MixBlock" };

        private static Window win;
        internal static Window Win => win;

        private static string Key(ConfigEntryBase e) => e.Definition.Section + "/" + e.Definition.Key;

        /// <summary>"MouseWheelOverHotbar" -> "Mouse Wheel Over Hotbar".</summary>
        private static string Nice(string key) =>
            System.Text.RegularExpressions.Regex.Replace(key ?? "", "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");

        internal class Window : MonoBehaviour
        {
            public TMP_FontAsset font;
            /// <summary>The full-screen backdrop the panel sits on; shown and hidden with it.</summary>
            public GameObject root;
            private RectTransform body;
            private string section;
            private ConfigEntryBase capturing;
            private float scroll;
            private float listScroll;

            private static ConfigFile Cfg => Plugin.Instance.Config;

            private List<ConfigEntryBase> Entries() =>
                Cfg.Keys.Select(k => Cfg[k]).Where(e => e != null).ToList();

            internal void Open()
            {
                capturing = null;
                ModContent.Forget();
                scroll = 0f;
                root.SetActive(true);
                root.transform.SetAsLastSibling();
                AudioManager.PlayEvent("event:/UI/UI_Click");
                Rebuild();
            }

            /// <summary>Opens on one section (devtest).</summary>
            internal void OpenAt(string sec)
            {
                Open();
                section = sec;
                Rebuild();
            }

            internal void Close()
            {
                capturing = null;
                root.SetActive(false);
            }

            private void Update()
            {
                // The Options menu closing (Escape, its own Back button) takes this page with it.
                if (Menu_Options.instance == null || !Menu_Options.instance.GetIsShowing()) { Close(); return; }
                if (capturing != null)
                {
                    if (Input.GetKeyDown(KeyCode.Escape)) { capturing = null; Rebuild(); return; }
                    bool combo = capturing.SettingType == typeof(KeyboardShortcut);
                    foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
                    {
                        if (k == KeyCode.None || k == KeyCode.Mouse0 || k == KeyCode.Mouse1 || !Input.GetKeyDown(k)) continue;
                        if (combo && Keyboard.Modifiers.Contains(k)) continue; // held for the combo; wait for its key
                        Set(capturing, combo ? (object)new KeyboardShortcut(k, Keyboard.HeldModifiers()) : k);
                        capturing = null;
                        Rebuild();
                        return;
                    }
                }
            }

            private void Set(ConfigEntryBase e, object value)
            {
                e.BoxedValue = value;
                if (!Cfg.SaveOnConfigSet) Cfg.Save();
                AudioManager.PlayEvent("event:/UI/UI_Click");
            }

            internal void Rebuild()
            {
                if (body != null) Destroy(body.gameObject);
                var size = ((RectTransform)transform).sizeDelta;
                body = UiKit.Rect(transform, "Body", size);
                body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one; body.offsetMin = body.offsetMax = Vector2.zero;
                float W = size.x, H = size.y;

                var all = Entries();
                var sections = all.Select(e => e.Definition.Section).Distinct().OrderBy(s => s).ToList();
                sections.Insert(0, ModContent.Section);
                if (section == null || !sections.Contains(section)) section = sections.FirstOrDefault();

                Text(body, "Mod Settings", 26, 20f, 12f, 400f, 34f, TextAlignmentOptions.TopLeft, new Color(0.75f, 0.9f, 1f));
                Text(body, "Breachwork Loader. Changes are saved at once and apply right away unless marked.", 14, 20f, 46f, W - 140f, 22f,
                    TextAlignmentOptions.TopLeft, new Color(0.7f, 0.78f, 0.85f));
                Button(body, "Close", W - 96f, 12f, 80f, 30f, Close);

                // Sections down the left, scrollable too: there are more than the page is tall.
                float listH = H - 84f - 16f, sy = 0f;
                RectTransform list = UiKit.Rect(body, "Sections", new Vector2(150f, listH));
                UiKit.At(list, 16f, 84f);
                list.gameObject.AddComponent<RectMask2D>();
                list.gameObject.AddComponent<Image>().color = Color.clear; // something for the wheel to land on
                RectTransform items = UiKit.Rect(list, "Content", new Vector2(150f, listH));
                UiKit.At(items, 0f, 0f);
                foreach (string s in sections)
                {
                    string sec = s;
                    Button(items, Nice(s), 0f, sy, 150f, 30f, () => { section = sec; scroll = 0f; capturing = null; Rebuild(); },
                        s == section ? UiKit.OnColor : UiKit.ButtonColor);
                    sy += 34f;
                }
                items.sizeDelta = new Vector2(150f, Mathf.Max(listH, sy - 4f));
                var lsr = list.gameObject.AddComponent<ScrollRect>();
                lsr.content = items;
                lsr.horizontal = false;
                lsr.movementType = ScrollRect.MovementType.Clamped;
                lsr.scrollSensitivity = 30f;
                lsr.verticalNormalizedPosition = 1f - listScroll;
                lsr.onValueChanged.AddListener(v => listScroll = 1f - v.y);

                // The section's settings, scrollable.
                float lx = 182f, lw = W - lx - 16f, top = 84f, viewH = H - top - 16f;
                RectTransform view = UiKit.Panel(body, "View", new Vector2(lw, viewH), new Color(0f, 0f, 0f, 0.2f));
                UiKit.At(view, lx, top);
                view.gameObject.AddComponent<RectMask2D>();
                RectTransform content = UiKit.Rect(view, "Content", new Vector2(lw, viewH));
                UiKit.At(content, 0f, 0f);

                float y = 8f, nameW = lw - 330f;
                if (section == ModContent.Section) y = ModContent.Draw(this, content, lw);
                else foreach (ConfigEntryBase e in all.Where(x => x.Definition.Section == section).OrderBy(x => x.Definition.Key))
                {
                    string desc = e.Description?.Description ?? "";
                    if (RestartOnly.Contains(Key(e))) desc += " <color=#FFB347>(applies after a restart)</color>";
                    string clash = Keyboard.Clashes(e);
                    if (clash != "") desc += $" <color=#FFB347>Also bound to: {clash}.</color>";
                    TextMeshProUGUI name = Text(content, Nice(e.Definition.Key), 18, 12f, y, nameW, 24f, TextAlignmentOptions.TopLeft, Color.white);
                    TextMeshProUGUI d = Text(content, desc, 14, 12f, y + 24f, nameW, 20f, TextAlignmentOptions.TopLeft, new Color(0.68f, 0.75f, 0.82f));
                    d.enableWordWrapping = true;
                    d.overflowMode = TextOverflowModes.Overflow;
                    float dh = Mathf.Max(18f, d.GetPreferredValues(desc, nameW, 0f).y);
                    ((RectTransform)d.transform.parent).sizeDelta = new Vector2(nameW, dh);
                    Control(content, e, lw - 310f, y);
                    y += Mathf.Max(52f, 30f + dh) + 12f;
                }
                content.sizeDelta = new Vector2(lw, Mathf.Max(viewH, y));

                var sr = view.gameObject.AddComponent<ScrollRect>();
                sr.content = content;
                sr.horizontal = false;
                sr.movementType = ScrollRect.MovementType.Clamped;
                sr.scrollSensitivity = 30f;
                sr.verticalNormalizedPosition = 1f - scroll;
                sr.onValueChanged.AddListener(v => scroll = 1f - v.y);
            }

            /// <summary>The control for one setting: toggle, stepper, key capture or list cycle.</summary>
            private void Control(RectTransform parent, ConfigEntryBase e, float x, float y)
            {
                Type t = e.SettingType;
                object v = e.BoxedValue;
                bool changed = !Equals(v, e.DefaultValue);
                if (changed)
                    Button(parent, "Reset", x + 236f, y, 62f, 30f, () => { Set(e, e.DefaultValue); Rebuild(); }, new Color(0.18f, 0.14f, 0.08f, 0.95f));

                if (t == typeof(bool))
                {
                    bool on = (bool)v;
                    Button(parent, on ? "On" : "Off", x, y, 226f, 30f, () => { Set(e, !on); Rebuild(); }, on ? UiKit.OnColor : UiKit.ButtonColor);
                    return;
                }
                if (t == typeof(KeyCode) || t == typeof(KeyboardShortcut))
                {
                    bool cap = capturing == e;
                    string name = v is KeyboardShortcut ks ? Keyboard.ShortcutName(ks) : Keyboard.KeyName((KeyCode)v);
                    string ask = t == typeof(KeyboardShortcut) ? "Press a key or combo (Esc cancels)" : "Press a key (Esc cancels)";
                    Button(parent, cap ? ask : name, x, y, 226f, 30f,
                        () => { capturing = cap ? null : e; Rebuild(); }, cap ? UiKit.OnColor : UiKit.ButtonColor);
                    return;
                }
                if (t == typeof(int) || t == typeof(float))
                {
                    bool isInt = t == typeof(int);
                    double cur = Convert.ToDouble(v), min = double.MinValue, max = double.MaxValue;
                    if (e.Description?.AcceptableValues is AcceptableValueRange<int> ri) { min = ri.MinValue; max = ri.MaxValue; }
                    else if (e.Description?.AcceptableValues is AcceptableValueRange<float> rf) { min = rf.MinValue; max = rf.MaxValue; }
                    double step = Step(isInt, Convert.ToDouble(e.DefaultValue), min, max);
                    object Make(double d) { d = Math.Max(min, Math.Min(max, d)); return isInt ? (object)(int)Math.Round(d) : (float)Math.Round(d, 3); }
                    Button(parent, "-", x, y, 40f, 30f, () => { Set(e, Make(cur - step)); Rebuild(); }, cur - step < min - 1e-9 ? new Color(0.1f, 0.1f, 0.1f, 0.9f) : (Color?)null);
                    Text(parent, isInt ? ((int)cur).ToString() : cur.ToString("0.###"), 17, x + 44f, y + 3f, 138f, 26f, TextAlignmentOptions.Center, Color.white);
                    Button(parent, "+", x + 186f, y, 40f, 30f, () => { Set(e, Make(cur + step)); Rebuild(); }, cur + step > max + 1e-9 ? new Color(0.1f, 0.1f, 0.1f, 0.9f) : (Color?)null);
                    return;
                }
                if (t.IsEnum || e.Description?.AcceptableValues?.GetType().GetProperty("AcceptableValues") != null)
                {
                    object[] opts = t.IsEnum ? Enum.GetValues(t).Cast<object>().ToArray()
                        : ((Array)e.Description.AcceptableValues.GetType().GetProperty("AcceptableValues").GetValue(e.Description.AcceptableValues)).Cast<object>().ToArray();
                    int i = Array.FindIndex(opts, o => Equals(o, v));
                    Button(parent, v + "  >", x, y, 226f, 30f, () => { Set(e, opts[(i + 1) % opts.Length]); Rebuild(); });
                    return;
                }
                Text(parent, v + "  (edit in the config file)", 14, x, y + 4f, 226f, 26f, TextAlignmentOptions.Left, new Color(0.7f, 0.7f, 0.7f));
            }

            /// <summary>Step size: 1 for whole numbers (5 on wide ranges), finer for small decimals.</summary>
            private static double Step(bool isInt, double def, double min, double max)
            {
                bool ranged = min > double.MinValue && max < double.MaxValue;
                if (isInt) return ranged && max - min >= 50 ? 5 : 1;
                if (ranged) return Math.Max(0.01, Math.Round((max - min) / 20, 2));
                double a = Math.Abs(def);
                return a == 0 || a >= 2 ? 1 : a < 0.2 ? 0.01 : 0.05;
            }

            internal TextMeshProUGUI Text(Transform parent, string text, float size, float x, float y, float w, float h, TextAlignmentOptions align, Color color)
            {
                RectTransform box = UiKit.Rect(parent, "Text", new Vector2(w, h));
                UiKit.At(box, x, y);
                TextMeshProUGUI t = UiKit.Label(box, text, font, size, align);
                ((RectTransform)t.transform).offsetMin = ((RectTransform)t.transform).offsetMax = Vector2.zero;
                t.color = color;
                return t;
            }

            internal Button Button(Transform parent, string label, float x, float y, float w, float h, Action click, Color? color = null)
            {
                Button b = UiKit.Button(parent, label, font, 14, new Vector2(w, h), click, color);
                UiKit.At((RectTransform)b.transform, x, y);
                return b;
            }
        }

        /// <summary>Adds the "Mod Settings" button beside the Options menu's keybindings button.</summary>
        [HarmonyPatch(typeof(Menu_Options), nameof(Menu_Options.Start))]
        private static class Patch_Start
        {
            private static void Postfix(Menu_Options __instance)
            {
                try
                {
                    Canvas canvas = __instance.GetComponentInParent<Canvas>();
                    if (canvas == null) return;
                    canvas = canvas.rootCanvas;
                    TMP_FontAsset font = UiKit.FontFrom(__instance);

                    // A dark full-screen backdrop (it also keeps clicks off the Options menu) with an opaque panel on it.
                    RectTransform back = UiKit.Panel(canvas.transform, "SCCL_ModSettings", Vector2.zero, new Color(0f, 0f, 0f, 0.82f));
                    back.anchorMin = Vector2.zero; back.anchorMax = Vector2.one; back.offsetMin = back.offsetMax = Vector2.zero;
                    RectTransform w = UiKit.Panel(back, "Panel", new Vector2(1000f, 660f), new Color(0.03f, 0.06f, 0.09f, 1f));
                    w.anchorMin = w.anchorMax = w.pivot = new Vector2(0.5f, 0.5f);
                    w.anchoredPosition = Vector2.zero;
                    w.gameObject.AddComponent<Outline>().effectColor = new Color(0.3f, 0.6f, 0.7f, 0.8f);
                    win = w.gameObject.AddComponent<Window>();
                    win.font = font;
                    win.root = back.gameObject;
                    UiLook.Attach(back, "ModSettings", w);
                    UiLook.Grip(back);
                    back.gameObject.SetActive(false);

                    GameObject src = __instance.btn_keybindings;
                    if (src == null) return;
                    GameObject copy = UnityEngine.Object.Instantiate(src, src.transform.parent, false);
                    copy.name = "SCCL_ModSettingsButton";
                    copy.SetActive(true);
                    // The original's label is localized; drop that so ours keeps its own text.
                    foreach (Component c in copy.GetComponentsInChildren<Component>(true))
                        if (c != null && c.GetType().Name.Contains("Localize")) UnityEngine.Object.Destroy(c);
                    foreach (TMP_Text t in copy.GetComponentsInChildren<TMP_Text>(true)) t.text = "Mod Settings";
                    Button b = copy.GetComponentInChildren<Button>(true);
                    if (b != null)
                    {
                        b.onClick = new Button.ButtonClickedEvent();
                        b.onClick.AddListener(() => win?.Open());
                    }
                    if (src.transform.parent.GetComponent<LayoutGroup>() != null) copy.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);
                    else
                    {
                        var rt = (RectTransform)copy.transform;
                        rt.anchoredPosition = ((RectTransform)src.transform).anchoredPosition - new Vector2(0f, rt.rect.height + 8f);
                    }
                }
                catch (Exception e) { Plugin.Log.LogError("Mod settings: " + e); }
            }
        }
    }
}
