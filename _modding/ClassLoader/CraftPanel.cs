using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// The crafting window: the recipes you know on the left, the picked one's output and ingredients on the right,
    /// and Craft / Craft ×5. Opened by the Apartment pad, by craft actions (field recipes, maybe only some), by the
    /// Rune Matrix's Transmute, or by the hotkey and the "Craft" tab beside the health panel. The hotkey crafts
    /// Apartment recipes anywhere in the Apartment; elsewhere it opens the recipe book, which shows but can't make.
    /// </summary>
    internal static class CraftPanel
    {
        internal static ConfigEntry<KeyCode> PanelKey;
        internal static string KeyName => PanelKey != null ? Keyboard.KeyName(PanelKey.Value) : "F10";

        internal static void Bind(ConfigFile cfg)
        {
            PanelKey = cfg.Bind("Crafting", "PanelKey", KeyCode.F10,
                "Opens the crafting window. In the Apartment it makes Apartment recipes; elsewhere it is a recipe book (craft with a crafting skill).");
        }

        private const int PerPage = 13;
        private static Window win;
        private static Transform holder;

        internal static bool IsOpen => win != null && win.gameObject.activeSelf;

        /// <summary>at: "apartment", "field" or null (browse); only: recipe ids to offer (null = all).</summary>
        internal static void Open(string at, List<string> only)
        {
            if (win == null) { Plugin.Log.LogWarning("Crafting window isn't built yet (no HUD)."); return; }
            win.Open(at, only);
        }

        internal static void Toggle()
        {
            if (win == null) return;
            if (IsOpen) win.Close(); else win.Open(Game.FLOOR == 100 ? "apartment" : null, null);
        }

        /// <summary>Recipes that pass their class requirement, whatever the station.</summary>
        internal static List<RecipeDef> Known() => Crafting.Recipes.Where(Crafting.Unlocked).ToList();

        private static Transform Holder()
        {
            if (holder != null) return holder;
            var go = new GameObject("SCCL_CraftPreviews");
            Object.DontDestroyOnLoad(go);
            go.transform.position = new Vector3(0f, -500f, 0f);
            return holder = go.transform;
        }

        private static Sprite IconOf(string id) => ItemRegistry.VanillaOrModItem(id)?.GetInventorySprite();

        // ---- hover tips -----------------------------------------------------------------------------------------

        internal class ItemTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Item item;
            public void OnPointerEnter(PointerEventData e) { if (item != null) Tooltip.instance?.Show_Item(item, false); }
            public void OnPointerExit(PointerEventData e) => Tooltip.instance?.Hide();
        }

        // ---- window ---------------------------------------------------------------------------------------------

        internal class Window : MonoBehaviour
        {
            public TMP_FontAsset font;
            private RectTransform body;
            private string station;
            private List<string> only;
            private RecipeDef sel;
            private Item preview;
            private int page;
            private string note;
            private bool noteBad;

            internal string Station => station;
            internal RecipeDef Selected => sel;

            internal void Open(string at, List<string> ids)
            {
                station = at; only = ids; note = null; page = 0;
                gameObject.SetActive(true);
                transform.SetAsLastSibling();
                AudioManager.PlayEvent("event:/UI/UI_Click");
                var list = Offered();
                if (sel == null || !list.Contains(sel)) Pick(list.FirstOrDefault(CanHere) ?? list.FirstOrDefault());
                else Rebuild();
                if (list.Count == 0) Say(at == null ? "You don't know any recipes yet." : "You don't know any recipes you can make here.");
            }

            internal void Close()
            {
                Tooltip.instance?.Hide();
                DropPreview();
                gameObject.SetActive(false);
            }

            private void Update()
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Player.instance == null || Game.Game_isLoading) Close();
            }

            private void Say(string s, bool bad = true)
            {
                note = s; noteBad = bad;
                if (bad) AudioManager.PlayEvent("event:/UI/UI_Error");
                Rebuild();
            }

            /// <summary>At a station: its recipes (and only the listed ones). Browsing: everything you know.</summary>
            internal List<RecipeDef> Offered() => Known()
                .Where(r => (station == null || Crafting.MadeAt(r, station)) && Crafting.Listed(r, only))
                .OrderByDescending(r => CanHere(r) && Crafting.CanCraft(r)).ThenBy(r => ItemRegistry.NameOf(r.outputId)).ToList();

            private bool CanHere(RecipeDef r) => Crafting.MadeAt(r, station);

            private void DropPreview()
            {
                if (preview != null && !preview.inPlayerInventory) Destroy(preview.gameObject);
                preview = null;
            }

            private void Pick(RecipeDef r)
            {
                DropPreview();
                sel = r;
                if (r != null) preview = Crafting.MakePreview(r, Holder());
                Rebuild();
            }

            /// <summary>Makes the picked recipe up to n times; stops at the first thing missing.</summary>
            internal void Craft(int n)
            {
                if (sel == null) return;
                if (!CanHere(sel)) { Say(station == null ? $"Make this at {Crafting.Where(sel)}." : "Can't be made here."); return; }
                int made = 0;
                string why = null;
                for (int i = 0; i < n; i++)
                {
                    if (preview == null) preview = Crafting.MakePreview(sel, Holder());
                    why = Crafting.Make(sel, preview);
                    if (why != null) break;
                    made++;
                    preview = Crafting.MakePreview(sel, Holder()); // the next one, freshly rolled
                }
                if (made > 0) { note = $"Made {ItemRegistry.NameOf(sel.outputId)}{(made * sel.amount > 1 ? $" ×{made * sel.amount}" : "")}." + (why != null && n > 1 ? " Out of ingredients." : ""); noteBad = false; Rebuild(); }
                else Say(why);
            }

            internal void Rebuild()
            {
                if (body != null) Destroy(body.gameObject);
                body = UiKit.Rect(transform, "Body", ((RectTransform)transform).sizeDelta);
                body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one; body.offsetMin = body.offsetMax = Vector2.zero;
                Vector2 size = ((RectTransform)transform).sizeDelta;
                float W = size.x, H = size.y;

                string title = station == "apartment" ? "Fabricator" : station == "field" ? "Field Crafting" : "Recipe Book";
                Text(body, title, 26, 44f, 12f, 400f, 34f, TextAlignmentOptions.TopLeft, new Color(1f, 0.78f, 0.45f));
                string hint = station == null
                    ? $"Browsing. Craft in the Apartment ({KeyName} there) or with a crafting skill."
                    : "Ingredients come from your pack, never from worn gear or the stash.";
                Text(body, note ?? hint, 14, 20f, 46f, W - 140f, 22f, TextAlignmentOptions.TopLeft,
                    note == null ? new Color(0.7f, 0.78f, 0.85f) : noteBad ? new Color(1f, 0.45f, 0.45f) : new Color(0.5f, 1f, 0.55f));
                Button(body, "Close", W - 96f, 12f, 80f, 30f, Close);

                // The recipe list.
                var list = Offered();
                int pages = Mathf.Max(1, (list.Count + PerPage - 1) / PerPage);
                page = Mathf.Clamp(page, 0, pages - 1);
                float y = 80f, lw = 360f;
                foreach (RecipeDef r in list.Skip(page * PerPage).Take(PerPage))
                {
                    RecipeDef rr = r;
                    bool rHere = CanHere(r), ok = rHere && Crafting.CanCraft(r);
                    Color c = r == sel ? UiKit.OnColor : new Color(0.06f, 0.1f, 0.13f, 0.95f);
                    Button b = Button(body, "", 16f, y, lw, 32f, () => { note = null; Pick(rr); AudioManager.PlayEvent("event:/UI/UI_Click"); }, c);
                    Icon(b.transform, IconOf(r.outputId), 4f, 2f, 28f);
                    string mark = ok ? "<color=#7CFC7C>●</color>" : rHere ? "<color=#FF7272>●</color>" : "<color=#6A7480>●</color>";
                    Text(b.transform, $"{mark} {ItemRegistry.NameOf(r.outputId)}{(r.amount > 1 ? $" ×{r.amount}" : "")}", 15, 38f, 5f, lw - 44f, 22f, TextAlignmentOptions.TopLeft, rHere ? Color.white : new Color(0.62f, 0.66f, 0.7f));
                    y += 35f;
                }
                if (pages > 1)
                {
                    float py = 80f + PerPage * 35f + 4f;
                    Button(body, "<", 16f, py, 40f, 26f, () => { page = (page + pages - 1) % pages; Rebuild(); });
                    Text(body, $"{page + 1} / {pages}", 14, 60f, py + 3f, 80f, 22f, TextAlignmentOptions.Top, new Color(0.7f, 0.78f, 0.85f));
                    Button(body, ">", 144f, py, 40f, 26f, () => { page = (page + 1) % pages; Rebuild(); });
                }

                // The picked recipe.
                float rx = 396f, rw = W - rx - 16f;
                if (sel == null) return;
                RectTransform card = UiKit.Panel(body, "Recipe", new Vector2(rw, H - 96f), new Color(0.05f, 0.09f, 0.12f, 0.9f));
                UiKit.At(card, rx, 80f);
                Image big = Icon(card, preview != null ? preview.GetInventorySprite() : IconOf(sel.outputId), 12f, 12f, 72f);
                if (big != null && preview != null) { big.raycastTarget = true; big.gameObject.AddComponent<ItemTip>().item = preview; }
                string name = ItemRegistry.NameOf(sel.outputId);
                Text(card, name + (sel.amount > 1 ? $" ×{sel.amount}" : ""), 20, 96f, 12f, rw - 108f, 28f, TextAlignmentOptions.TopLeft, Color.white);
                Text(card, $"Made at {Crafting.Where(sel)}. Hover the icon for the item's details.", 13, 96f, 44f, rw - 108f, 40f, TextAlignmentOptions.TopLeft, new Color(0.7f, 0.78f, 0.85f))
                    .enableWordWrapping = true;

                Text(card, "Needs", 17, 12f, 100f, 200f, 24f, TextAlignmentOptions.TopLeft, new Color(1f, 0.78f, 0.45f));
                float ny = 128f;
                foreach (CostSpec c in sel.inputs ?? new List<CostSpec>())
                {
                    int have = Crafting.Count(c);
                    Icon(card, c.resolved != null ? IconOf(c.resolved) : null, 12f, ny, 26f);
                    Text(card, $"{Crafting.Describe(c)}   <size=13>(have {have})</size>", 15, 46f, ny + 3f, rw - 60f, 22f, TextAlignmentOptions.TopLeft, have >= c.count ? new Color(0.5f, 1f, 0.55f) : new Color(1f, 0.45f, 0.45f));
                    ny += 30f;
                }
                foreach (string tool in sel.tools ?? new List<string>())
                {
                    bool has = (Menu_Inventory.GetItems() ?? new List<Item>()).Any(i => i != null && i.inPlayerInventory && i.HasTag(tool));
                    Text(card, $"Tool (kept): {tool.ToLowerInvariant().Replace('_', ' ')}", 15, 46f, ny + 3f, rw - 60f, 22f, TextAlignmentOptions.TopLeft, has ? new Color(0.5f, 1f, 0.55f) : new Color(1f, 0.45f, 0.45f));
                    ny += 30f;
                }
                if (sel.credits > 0)
                {
                    int cr = Menu_Inventory.instance.GetCredits();
                    Text(card, $"{sel.credits} credits   <size=13>(have {cr})</size>", 15, 46f, ny + 3f, rw - 60f, 22f, TextAlignmentOptions.TopLeft, cr >= sel.credits ? new Color(0.5f, 1f, 0.55f) : new Color(1f, 0.45f, 0.45f));
                    ny += 30f;
                }

                bool here = CanHere(sel), can = here && Crafting.CanCraft(sel);
                float by = H - 96f - 50f;
                Color go = can ? new Color(0.12f, 0.42f, 0.2f, 0.95f) : new Color(0.12f, 0.12f, 0.12f, 0.9f);
                Button(card, here ? "Craft" : "Not here", 12f, by, 140f, 38f, () => Craft(1), go);
                Button(card, "Craft ×5", 160f, by, 120f, 38f, () => Craft(5), go);
            }

            private Image Icon(Transform parent, Sprite s, float x, float y, float size)
            {
                if (s == null) return null;
                RectTransform r = UiKit.Rect(parent, "Icon", new Vector2(size, size));
                UiKit.At(r, x, y);
                var img = r.gameObject.AddComponent<Image>();
                img.sprite = s; img.preserveAspect = true; img.raycastTarget = false;
                return img;
            }

            private TextMeshProUGUI Text(Transform parent, string text, float size, float x, float y, float w, float h, TextAlignmentOptions align, Color color)
            {
                RectTransform box = UiKit.Rect(parent, "Text", new Vector2(w, h));
                UiKit.At(box, x, y);
                TextMeshProUGUI t = UiKit.Label(box, text, font, size, align);
                ((RectTransform)t.transform).offsetMin = ((RectTransform)t.transform).offsetMax = Vector2.zero;
                t.color = color;
                return t;
            }

            private Button Button(Transform parent, string label, float x, float y, float w, float h, Action click, Color? color = null)
            {
                Button b = UiKit.Button(parent, label, font, 15, new Vector2(w, h), click, color);
                UiKit.At((RectTransform)b.transform, x, y);
                return b;
            }
        }

        // ---- HUD hooks ------------------------------------------------------------------------------------------

        /// <summary>The "Craft" tab beside the health panel (above the Runes tab when that shows), and the hotkey.</summary>
        internal class HudTab : MonoBehaviour, IPointerClickHandler
        {
            public RectTransform runes;
            private float next;
            private bool have;

            public void OnPointerClick(PointerEventData e) => Toggle();

            private void Update()
            {
                if (Time.unscaledTime >= next)
                {
                    next = Time.unscaledTime + 0.5f;
                    have = Player.instance != null && !Game.Game_isLoading && Crafting.Recipes.Count > 0 && Known().Count > 0;
                    foreach (Transform c in transform) c.gameObject.SetActive(have);
                    GetComponent<Image>().enabled = have;
                    if (runes == null) runes = transform.parent.Find("SCCL_RuneTab") as RectTransform;
                    bool runesShown = runes != null && runes.GetComponent<Image>()?.enabled == true;
                    ((RectTransform)transform).anchoredPosition = new Vector2(-6f, runesShown ? 36f : 6f);
                }
                if (Input.GetKeyDown(PanelKey.Value) && Player.instance != null && !Game.Game_isLoading
                    && EventSystem.current?.currentSelectedGameObject?.GetComponent<TMP_InputField>() == null) Toggle();
            }
        }

        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.Awake))]
        private static class Patch_Awake
        {
            private static void Postfix(Menu_HUD __instance)
            {
                try
                {
                    Canvas canvas = __instance.GetComponentInParent<Canvas>();
                    if (canvas == null) return;
                    TMP_FontAsset font = UiKit.FontFrom(__instance);

                    RectTransform w = UiKit.Panel(canvas.transform, "SCCL_Crafting", new Vector2(900f, 600f), UiKit.PanelColor);
                    w.anchorMin = w.anchorMax = w.pivot = new Vector2(0.5f, 0.5f);
                    w.anchoredPosition = Vector2.zero;
                    win = w.gameObject.AddComponent<Window>();
                    UiLook.Attach(w, "Crafting");
                    UiLook.Collapsible(w, "Crafting", UiLook.Corner.InsideLeft, false);
                    win.font = font;
                    w.gameObject.SetActive(false);

                    var stats = __instance.stats != null ? (RectTransform)__instance.stats.transform : null;
                    Transform parent = stats != null ? stats : canvas.transform;
                    RectTransform tab = UiKit.Panel(parent, "SCCL_CraftTab", new Vector2(70f, 26f), UiKit.ButtonColor);
                    tab.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    tab.anchorMin = tab.anchorMax = new Vector2(0f, 1f);
                    tab.pivot = new Vector2(1f, 0f);
                    tab.anchoredPosition = new Vector2(-6f, 6f);
                    TextMeshProUGUI label = UiKit.Label(tab, "Craft", font, 15);
                    label.color = new Color(1f, 0.78f, 0.45f);
                    var hud = tab.gameObject.AddComponent<HudTab>();
                    UiLook.Attach(tab, "Crafting");
                    var tip = tab.gameObject.AddComponent<RunePanel.Tip>();
                    tip.title = "Crafting";
                    tip.body = $"Your recipes. In the Apartment you can make them here; elsewhere it's a recipe book. Hotkey: {KeyName}.";
                }
                catch (Exception e) { Plugin.Log.LogError("Crafting window: " + e); }
            }
        }

        // ---- test hooks ---------------------------------------------------------------------------------------

        internal static Window TestWindow => win;
    }
}
