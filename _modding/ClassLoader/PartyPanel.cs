using System;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// StarCrawlers-style party cards for Conjoined bodies on the left of the screen: class icon, name, level, class
    /// and health; the body you play is highlighted, fallen ones are greyed. Click a card to switch to that body.
    /// The "Party" tab (or the hotkey) hides and shows the cards; the choice is remembered.
    /// Art comes from Mods/Conjoined/ui/*.png when present (card, card_active, card_down, tab, hp_fill).
    /// </summary>
    internal static class PartyPanel
    {
        internal static ConfigEntry<KeyCode> ToggleKey;
        private const string HiddenPref = "SCCL_PartyHidden";

        internal static void Bind(ConfigFile cfg) =>
            ToggleKey = cfg.Bind("Conjoined", "PartyPanelKey", KeyCode.F7, "Shows or hides the Conjoined party cards.");

        private static string ArtDir => Path.Combine(Plugin.GameDir, "Mods", "Conjoined", "ui");
        private static Sprite Art(string name) => File.Exists(Path.Combine(ArtDir, name + ".png")) ? ClassRegistry.LoadSprite(ArtDir, name + ".png") : null;

        internal static bool Hidden
        {
            get => PlayerPrefs.GetInt(HiddenPref, 0) == 1;
            set { PlayerPrefs.SetInt(HiddenPref, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        internal class Card : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public int bodyId;
            public Image back, icon, hpFill;
            public TextMeshProUGUI title, sub, hpText;
            public Sprite normal, active, down;

            internal Conjoined.Body Body => Conjoined.Bodies.FirstOrDefault(b => b.Id == bodyId);

            public void OnPointerClick(PointerEventData e)
            {
                var b = Body;
                if (b == null || b.Id == Conjoined.ActiveId) return;
                Tooltip.instance?.Hide();
                if (Conjoined.TrySwitch(b)) AudioManager.PlayEvent("event:/UI/UI_Click");
            }

            public void OnPointerEnter(PointerEventData e)
            {
                var b = Body;
                if (b == null) return;
                string why = b.Id == Conjoined.ActiveId ? "You are playing this body." : Conjoined.SwitchBlocker(b) ?? (Conjoined.InCombat ? "Click: switch (uses Trade Bodies)." : "Click: switch to this body.");
                Tooltip.instance?.Show_TinyTip(b.Name, $"Level {b.Level} · {ClassLine(b)}\n{why}", null);
            }

            public void OnPointerExit(PointerEventData e) => Tooltip.instance?.Hide();

            internal void Refresh()
            {
                var b = Body;
                if (b == null) return;
                bool me = b.Id == Conjoined.ActiveId;
                Character npc = me ? null : Conjoined.NpcOf(b);
                int hp = me ? Player.instance.GetHealth() : npc != null ? npc.GetHealth() : Math.Max(0, b.Hp);
                int max = me ? Player.instance.GetHealthMax() : npc != null ? npc.GetHealthMax() : Math.Max(1, b.HpMax);
                int lvl = me ? Player.instance.level : b.Level;
                title.text = (me ? "<color=#FFD15C>" : "") + b.Name + (me ? "</color>" : "");
                sub.text = b.Downed ? "<color=#FF7272>Down</color>" : $"Lv {lvl} · {ClassLine(b)}";
                hpText.text = b.Downed ? "" : $"{hp}/{max}";
                hpFill.fillAmount = b.Downed ? 0f : Mathf.Clamp01(hp / (float)Mathf.Max(1, max));
                hpFill.color = hp * 3 < max ? new Color(0.85f, 0.3f, 0.3f) : new Color(0.35f, 0.8f, 0.45f);
                Sprite s = b.Downed ? down : me ? active : normal;
                if (s != null) { back.sprite = s; back.type = Image.Type.Sliced; back.color = Color.white; }
                else back.color = b.Downed ? new Color(0.12f, 0.12f, 0.12f, 0.85f) : me ? new Color(0.22f, 0.18f, 0.06f, 0.92f) : new Color(0.04f, 0.08f, 0.12f, 0.88f);
                Sprite ic = MainTree(b)?.icon;
                if (ic != null) icon.sprite = ic;
                icon.color = b.Downed ? new Color(0.4f, 0.4f, 0.4f, 1f) : Color.white;
            }
        }

        private static Skill_Tree MainTree(Conjoined.Body b)
        {
            string[] trees = b.Id == Conjoined.ActiveId
                ? Player_SkillControl.instance.skillTrees.Where(t => t != null).Select(t => t.internalName).ToArray()
                : b.Save?.skillTrees;
            string main = trees?.FirstOrDefault(t => !string.IsNullOrEmpty(t) && t != Conjoined.ClassId);
            return main == null ? null : Game.instance.skills.FirstOrDefault(s => s != null && s.internalName == main);
        }

        private static string ClassLine(Conjoined.Body b) => MainTree(b)?.displayName.ToString() ?? "Conjoined";

        internal class Panel : MonoBehaviour, IPointerClickHandler
        {
            public RectTransform cards;
            public TextMeshProUGUI tabText;
            public TMP_FontAsset font;
            private float next;

            public void OnPointerClick(PointerEventData e) => Toggle();

            internal void Toggle()
            {
                Hidden = !Hidden;
                AudioManager.PlayEvent("event:/UI/UI_Click");
                next = 0f;
            }

            private void Update()
            {
                bool show = Conjoined.IsConjoined && Conjoined.Bodies.Count > 1;
                foreach (Transform c in transform) c.gameObject.SetActive(show);
                if (!show) return;
                if (Input.GetKeyDown(ToggleKey.Value) && !Game.Game_isLoading && EventSystem.current?.currentSelectedGameObject?.GetComponent<TMP_InputField>() == null) Toggle();
                cards.gameObject.SetActive(!Hidden);
                tabText.text = Hidden ? "Party +" : "Party -";
                if (Time.unscaledTime < next) return;
                next = Time.unscaledTime + 0.25f;
                var bodies = Conjoined.Bodies;
                var have = cards.GetComponentsInChildren<Card>(true).ToList();
                if (have.Count != bodies.Count || have.Select(c => c.bodyId).Except(bodies.Select(b => b.Id)).Any())
                {
                    foreach (Card c in have) Destroy(c.gameObject);
                    for (int i = 0; i < bodies.Count; i++) MakeCard(bodies[i].Id, i);
                    have = cards.GetComponentsInChildren<Card>(true).ToList();
                }
                foreach (Card c in have) c.Refresh();
            }

            private void MakeCard(int id, int i)
            {
                const float w = 250f, h = 74f;
                RectTransform rt = UiKit.Panel(cards, "SCCL_PartyCard", new Vector2(w, h), UiKit.PanelColor);
                UiKit.At(rt, 0f, i * (h + 8f));
                var card = rt.gameObject.AddComponent<Card>();
                card.bodyId = id;
                card.back = rt.GetComponent<Image>();
                card.normal = Art("card"); card.active = Art("card_active"); card.down = Art("card_down");
                RectTransform ic = UiKit.Rect(rt, "Icon", new Vector2(58f, 58f));
                UiKit.At(ic, 8f, 8f);
                card.icon = ic.gameObject.AddComponent<Image>();
                card.icon.raycastTarget = false;
                RectTransform nameBox = UiKit.Rect(rt, "Name", new Vector2(w - 80f, 26f));
                UiKit.At(nameBox, 74f, 5f);
                card.title = UiKit.Label(nameBox, "", font, 20, TextAlignmentOptions.TopLeft);
                Flush(card.title);
                RectTransform subBox = UiKit.Rect(rt, "Sub", new Vector2(w - 80f, 20f));
                UiKit.At(subBox, 74f, 30f);
                card.sub = UiKit.Label(subBox, "", font, 15, TextAlignmentOptions.TopLeft);
                Flush(card.sub);
                RectTransform bar = UiKit.Panel(rt, "HpBack", new Vector2(w - 82f, 15f), new Color(0f, 0f, 0f, 0.6f));
                UiKit.At(bar, 74f, 52f);
                RectTransform fill = UiKit.Rect(bar, "HpFill", Vector2.zero);
                fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.offsetMin = fill.offsetMax = Vector2.zero;
                card.hpFill = fill.gameObject.AddComponent<Image>();
                card.hpFill.sprite = Art("hp_fill") ?? Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
                card.hpFill.type = Image.Type.Filled;
                card.hpFill.fillMethod = Image.FillMethod.Horizontal;
                card.hpFill.raycastTarget = false;
                card.hpText = UiKit.Label(bar, "", font, 12);
                Flush(card.hpText);
                card.hpText.overflowMode = TextOverflowModes.Overflow;
            }

            /// <summary>Text fills its box exactly (UiKit labels are inset by default).</summary>
            private static void Flush(TMP_Text t) => ((RectTransform)t.transform).offsetMin = ((RectTransform)t.transform).offsetMax = Vector2.zero;
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
                    RectTransform root = UiKit.Rect(canvas.transform, "SCCL_PartyPanel", new Vector2(260f, 40f));
                    root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 0.5f);
                    root.anchoredPosition = new Vector2(16f, 220f);
                    var panel = root.gameObject.AddComponent<Panel>();
                    UiLook.Attach(root, "PartyPanel");
                    UiLook.Grip(root);
                    panel.font = font;
                    RectTransform tab = UiKit.Panel(root, "Tab", new Vector2(120f, 34f), UiKit.ButtonColor);
                    UiKit.At(tab, 0f, 0f);
                    Sprite tabArt = Art("tab");
                    if (tabArt != null) { var img = tab.GetComponent<Image>(); img.sprite = tabArt; img.type = Image.Type.Sliced; img.color = Color.white; }
                    tab.gameObject.AddComponent<TabClick>().panel = panel;
                    panel.tabText = UiKit.Label(tab, "Party -", font, 18);
                    panel.cards = UiKit.Rect(root, "Cards", new Vector2(260f, 10f));
                    UiKit.At(panel.cards, 0f, 42f);
                }
                catch (Exception e) { Plugin.Log.LogError("Party panel: " + e); }
            }
        }

        internal class TabClick : MonoBehaviour, IPointerClickHandler
        {
            public Panel panel;
            public void OnPointerClick(PointerEventData e) => panel?.Toggle();
        }
    }
}
