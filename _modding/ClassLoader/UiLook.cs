using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// Size and see-through settings for every panel the loader draws ("Interface" in Mod Settings). Each panel's
    /// root gets a Look that scales it about its own pivot and fades its backgrounds (sprite-less or framed images);
    /// text and icons stay solid unless FadeText is on. "All" settings multiply into each panel's own. Panels made
    /// Collapsible also get a small tab that folds them down to "+ Name" and back, and collapsible or Gripped panels
    /// a corner grip that drags their size setting (the layouts are fixed, so it scales them rather than reflowing).
    /// </summary>
    internal static class UiLook
    {
        private const string Section = "Interface";

        /// <summary>Every panel by its setting name, with what it is.</summary>
        private static readonly (string key, string about)[] Panels =
        {
            ("BodyPicker", "the Conjoined new-body picker"),
            ("BonusSkills", "the bonus skill menu on a hotbar icon"),
            ("BuildList", "the saved-builds list on the character creation screen"),
            ("Crafting", "the crafting window and its Craft tab"),
            ("CombatBadge", "the combat round badge and the combat banner at the top of the screen"),
            ("CrewScreen", "the crew screen"),
            ("Dialogs", "the pop-up choices such as the Ichor Deep's and the ones after the credits"),
            ("FactionScreens", "the job board, auction, standing, contact and encounter screens"),
            ("Graveyard", "the graveyard of fallen crawlers"),
            ("HotbarPage", "the hotbar page label"),
            ("JobTracker", "the job tracker in the top left corner"),
            ("MinionBar", "the minion strip beside the hotbar"),
            ("MinionMenu", "the right-click menu on your companions"),
            ("ModsPage", "the Mods page on the title screen"),
            ("ModSettings", "this Mod Settings page"),
            ("OrderBar", "the order strip above the hotbar"),
            ("PartyPanel", "the party panel"),
            ("PortraitPicker", "the character picture picker"),
            ("Reactions", "the reactions strip by the hotbar"),
            ("RuneMatrix", "the Rune Matrix window and its Runes tab"),
            ("SaveTrees", "the class icons on the load screen"),
            ("StashTabs", "the stash tab bar"),
            ("SynergyTab", "the text and controls on the character screen's Synergies tab"),
            ("SynergyTip", "the synergy tooltip on the creation screen and the body picker"),
        };

        private static readonly Dictionary<string, (ConfigEntry<int> size, ConfigEntry<int> opacity)> settings = new Dictionary<string, (ConfigEntry<int>, ConfigEntry<int>)>();
        private static ConfigEntry<int> allSize, allOpacity;
        private static ConfigEntry<bool> fadeText, collapseButtons, resizeGrips;

        /// <summary>Where a panel's collapse tab sits: just above its top edge (left or right end), or inside its top-left corner.</summary>
        internal enum Corner { AboveLeft, AboveRight, InsideLeft }

        internal static void Bind(ConfigFile cfg)
        {
            allSize = cfg.Bind(Section, "AllSize", 100, new ConfigDescription("Size of every loader panel, in percent (each panel's own size multiplies into this).", new AcceptableValueRange<int>(50, 200)));
            allOpacity = cfg.Bind(Section, "AllOpacity", 100, new ConfigDescription("Opacity of every loader panel's background, in percent; lower lets the game show through.", new AcceptableValueRange<int>(10, 100)));
            fadeText = cfg.Bind(Section, "FadeText", false, "Opacity fades the text and icons too. Off keeps them solid and fades only the backgrounds.");
            collapseButtons = cfg.Bind(Section, "CollapseButtons", true, "Panels have a small - tab that folds them down to a + tab and back. Off hides the tabs and shows every panel whole.");
            resizeGrips = cfg.Bind(Section, "ResizeGrips", true, "Panels have a small grip in their corner: drag it to change the panel's size setting. Off hides the grips.");
            foreach (var (key, about) in Panels)
                settings[key] = (
                    cfg.Bind(Section, key + "Size", 100, new ConfigDescription($"Size of {about}, in percent.", new AcceptableValueRange<int>(50, 200))),
                    cfg.Bind(Section, key + "Opacity", 100, new ConfigDescription($"Background opacity of {about}, in percent.", new AcceptableValueRange<int>(10, 100))));
        }

        internal static float Scale(string key) =>
            (allSize?.Value ?? 100) / 100f * (settings.TryGetValue(key, out var s) ? s.size.Value : 100) / 100f;

        internal static float Opacity(string key) =>
            Mathf.Clamp01((allOpacity?.Value ?? 100) / 100f * (settings.TryGetValue(key, out var s) ? s.opacity.Value : 100) / 100f);

        /// <summary>Gives `root` the panel's size and opacity; `scaled` (default: root) is what grows or shrinks.</summary>
        internal static void Attach(Component root, string key, RectTransform scaled = null)
        {
            if (root == null) return;
            Look l = root.GetComponent<Look>() ?? root.gameObject.AddComponent<Look>();
            l.key = key;
            l.scaled = scaled != null ? scaled : root.transform as RectTransform;
            l.Apply(true);
        }

        /// <summary>
        /// Gives a panel (already Attached) a collapse tab titled `title`. HUD strips `remember` being folded between
        /// sessions; screens open whole every time.
        /// </summary>
        internal static void Collapsible(Component root, string title, Corner corner, bool remember)
        {
            Look l = root != null ? root.GetComponent<Look>() : null;
            if (l == null) return;
            l.title = title;
            l.corner = corner;
            l.remember = remember;
            l.collapsed = remember && PlayerPrefs.GetInt(Pref(l.key), 0) == 1;
            l.Fold();
        }

        /// <summary>Gives a panel (already Attached) a resize grip; `home` is where it sits (default: what scales), `lift`
        /// raises it off the bottom edge (for a panel that ends behind something else).</summary>
        internal static void Grip(Component root, RectTransform home = null, float lift = 0f)
        {
            Look l = root != null ? root.GetComponent<Look>() : null;
            if (l == null) return;
            l.gripped = true;
            l.gripHome = home;
            l.gripLift = lift;
        }

        private static string Pref(string key) => "SCCL_Collapsed_" + key;

        /// <summary>Dragging away from the panel's pivot grows it, towards it shrinks it; the size setting is saved on release.</summary>
        private class ResizeGrip : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            internal Look look;
            private Vector2 pivot;
            private float startDistance;
            private int startSize;

            public void OnBeginDrag(PointerEventData e)
            {
                if (look == null || !settings.ContainsKey(look.key)) return;
                pivot = RectTransformUtility.WorldToScreenPoint(e.pressEventCamera, look.Pivot);
                startDistance = Mathf.Max(1f, Vector2.Distance(e.position, pivot));
                startSize = settings[look.key].size.Value;
                Plugin.Instance.Config.SaveOnConfigSet = false; // not a file write per pixel
            }

            public void OnDrag(PointerEventData e)
            {
                if (startSize == 0) return;
                settings[look.key].size.Value = Mathf.Clamp(Mathf.RoundToInt(startSize * Vector2.Distance(e.position, pivot) / startDistance), 50, 200);
            }

            public void OnEndDrag(PointerEventData e)
            {
                if (startSize == 0) return;
                startSize = 0;
                Plugin.Instance.Config.SaveOnConfigSet = true;
                Plugin.Instance.Config.Save();
            }
        }

        internal class Look : MonoBehaviour
        {
            public string key;
            public RectTransform scaled;
            public string title;
            public Corner corner;
            public bool remember, collapsed, gripped;
            public RectTransform gripHome;
            public float gripLift;
            private RectTransform tab, grip;
            private TMPro.TextMeshProUGUI tabText;
            private CanvasGroup group, tabGroup;
            private readonly List<Graphic> faded = new List<Graphic>();
            /// <summary>Outlines and shadows switched off while faded: they draw whole copies of the panel behind it.</summary>
            private readonly List<Shadow> muted = new List<Shadow>();
            private float shownAlpha = 1f, nextScan;
            private bool shownText;
            private int shownCount = -1;

            private void OnEnable() { if (key != null) Apply(true); } // AddComponent gets here before Attach has set the key
            private void LateUpdate() { Apply(false); Fold(); KeepGrip(); }

            internal Vector3 Pivot => (scaled != null ? scaled : transform).position;

            /// <summary>Panels that clear their children take the grip with them, so it comes back here.</summary>
            private void KeepGrip()
            {
                if (!gripped && title == null) return;
                if (grip == null) MakeGrip();
                bool show = (resizeGrips == null || resizeGrips.Value) && !collapsed && HasContent();
                if (grip.gameObject.activeSelf != show) grip.gameObject.SetActive(show);
                if (show && grip.GetSiblingIndex() != grip.parent.childCount - 1) grip.SetAsLastSibling();
            }

            private void MakeGrip()
            {
                Transform home = gripHome != null ? gripHome : scaled != null ? scaled : transform;
                grip = UiKit.Panel(home, "SCCL_ResizeGrip", new Vector2(20f, 20f), UiKit.ButtonColor);
                grip.anchorMin = grip.anchorMax = grip.pivot = new Vector2(1f, 0f);
                grip.anchoredPosition = new Vector2(0f, gripLift);
                grip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                grip.gameObject.AddComponent<ResizeGrip>().look = this;
                var lines = UiKit.Label(grip, "=", UiKit.FontFrom(this), 16f); // the game font has no corner glyph
                lines.overflowMode = TMPro.TextOverflowModes.Overflow;
                lines.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }

            internal void Toggle()
            {
                collapsed = !collapsed;
                if (remember) { PlayerPrefs.SetInt(Pref(key), collapsed ? 1 : 0); PlayerPrefs.Save(); }
                AudioManager.PlayEvent("event:/UI/UI_Click");
                Fold();
            }

            /// <summary>Shows the tab while the panel has something in it, and hides the panel's insides while folded.</summary>
            internal void Fold()
            {
                if (title == null) return;
                bool on = collapseButtons == null || collapseButtons.Value;
                if (tab == null) MakeTab(); // panels that clear their children take the tab with them
                bool show = on && HasContent();
                if (tab.gameObject.activeSelf != show) tab.gameObject.SetActive(show);
                bool hide = on && collapsed;
                if (group == null) group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
                if (group.alpha != (hide ? 0f : 1f)) { group.alpha = hide ? 0f : 1f; group.blocksRaycasts = group.interactable = !hide; }
                if (!show) return;
                // the tab ignores the panel's group (so it stays while folded) but not whatever hides the whole HUD
                float outer = 1f; bool blocks = true;
                for (Transform t = transform.parent; t != null; t = t.parent)
                {
                    CanvasGroup g = t.GetComponent<CanvasGroup>();
                    if (g == null || !g.enabled) continue;
                    outer *= g.alpha; blocks &= g.blocksRaycasts;
                    if (g.ignoreParentGroups) break;
                }
                tabGroup.alpha = outer;
                tabGroup.blocksRaycasts = tabGroup.interactable = blocks && outer > 0.01f;
                string label = collapsed ? "+ " + title : "-";
                if (tabText.text != label)
                {
                    tabText.text = label;
                    tab.sizeDelta = new Vector2(collapsed ? tabText.GetPreferredValues(label).x + 18f : 24f, 22f);
                }
                if (tab.GetSiblingIndex() != tab.parent.childCount - 1) tab.SetAsLastSibling();
            }

            private bool HasContent()
            {
                Graphic own = GetComponent<Graphic>();
                if (own != null && own.enabled) return true;
                foreach (Transform t in transform)
                    if (t != tab && t != grip && t.gameObject.activeSelf) return true;
                return false;
            }

            private void MakeTab()
            {
                Transform home = scaled != null ? scaled : transform;
                tab = UiKit.Panel(home, "SCCL_CollapseTab", new Vector2(24f, 22f), UiKit.ButtonColor);
                switch (corner)
                {
                    case Corner.AboveRight: tab.anchorMin = tab.anchorMax = new Vector2(1f, 1f); tab.pivot = new Vector2(1f, 0f); tab.anchoredPosition = new Vector2(0f, 2f); break;
                    case Corner.InsideLeft: tab.anchorMin = tab.anchorMax = tab.pivot = new Vector2(0f, 1f); tab.anchoredPosition = new Vector2(8f, -8f); break;
                    default: tab.anchorMin = tab.anchorMax = new Vector2(0f, 1f); tab.pivot = new Vector2(0f, 0f); tab.anchoredPosition = new Vector2(0f, 2f); break;
                }
                var le = tab.gameObject.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
                tabGroup = tab.gameObject.AddComponent<CanvasGroup>();
                tabGroup.ignoreParentGroups = true;
                var b = tab.gameObject.AddComponent<Button>();
                b.targetGraphic = tab.GetComponent<Image>();
                b.onClick.AddListener(Toggle);
                tabText = UiKit.Label(tab, "", UiKit.FontFrom(this), 15f);
                tabText.overflowMode = TMPro.TextOverflowModes.Overflow;
                tabText.rectTransform.offsetMin = new Vector2(4f, 0f);
                tabText.rectTransform.offsetMax = new Vector2(-4f, 0f);
            }

            internal void Apply(bool rescan)
            {
                float s = Scale(key);
                if (scaled != null && Mathf.Abs(scaled.localScale.x - s) > 0.001f) scaled.localScale = new Vector3(s, s, 1f);

                float a = Opacity(key);
                bool text = fadeText != null && fadeText.Value;
                if (a >= 0.999f && shownAlpha >= 0.999f) return; // nothing faded, nothing to undo
                // panels rebuild their insides: look again when the hierarchy changes, and now and then anyway
                int count = transform.hierarchyCount;
                if (rescan || text != shownText || count != shownCount || Time.unscaledTime >= nextScan) Collect(text, a < 0.999f);
                shownCount = count;
                foreach (Graphic g in faded)
                    if (g != null && Mathf.Abs(g.canvasRenderer.GetAlpha() - a) > 0.002f) g.canvasRenderer.SetAlpha(a);
                if (a >= 0.999f)
                {
                    foreach (Shadow sh in muted) if (sh != null) sh.enabled = true;
                    muted.Clear();
                }
                shownAlpha = a;
                shownText = text;
            }

            private void Collect(bool text, bool fading)
            {
                nextScan = Time.unscaledTime + 0.5f;
                var now = GetComponentsInChildren<Graphic>(true).Where(g => text || Background(g)).ToList();
                // anything no longer faded (FadeText turned off) goes back to solid
                foreach (Graphic g in faded.Except(now)) if (g != null) g.canvasRenderer.SetAlpha(1f);
                faded.Clear();
                faded.AddRange(now);
                if (!fading) return;
                foreach (Graphic g in now)
                    foreach (Shadow sh in g.GetComponents<Shadow>())
                        if (sh.enabled) { sh.enabled = false; muted.Add(sh); }
            }

            /// <summary>Plain fills and framed (sliced or tiled) images; icons and text are left alone.</summary>
            private static bool Background(Graphic g) =>
                g is Image i ? i.sprite == null || i.type == Image.Type.Sliced || i.type == Image.Type.Tiled
                : g is RawImage r && r.texture == null;
        }
    }
}
