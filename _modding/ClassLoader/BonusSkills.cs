using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// Bonus skills that ride on another skill instead of taking a hotbar slot: a granted skill with "attachTo" (a
    /// synergy's grant) is reached by a quick right-click on its parent's hotbar icon, which opens a small menu at the
    /// cursor; clicking an entry selects it exactly like a hotbar click. If the parent isn't on the hotbar at all, the
    /// bonus skill goes on the hotbar itself so it is never out of reach. A right-click on a class's icon on the skills
    /// screen opens the same menu with every skill your synergies give that class.
    /// </summary>
    internal static class BonusSkills
    {
        private const float ClickSeconds = 0.35f, ClickPixels = 8f;

        /// <summary>Does this attachTo name this ability (display name or internal name)?</summary>
        private static bool Names(string attachTo, Ability parent) =>
            !string.IsNullOrEmpty(attachTo) && parent != null &&
            (string.Equals(attachTo, parent.GetDisplayName(), System.StringComparison.OrdinalIgnoreCase) || string.Equals(attachTo, parent.internalName, System.StringComparison.OrdinalIgnoreCase));

        /// <summary>The bonus skills on this parent that you have right now.</summary>
        internal static List<ModAbility> For(Ability parent)
        {
            var pac = Player_AbilityControl.instance;
            if (pac == null || parent == null) return new List<ModAbility>();
            return pac.GetAbilities().OfType<ModAbility>().Where(m => m != parent && Names(m.Spec?.attachTo, parent)).ToList();
        }

        /// <summary>The parent skill this bonus skill rides on, if you have it.</summary>
        internal static Ability ParentOf(ModAbility bonus)
        {
            string at = bonus?.Spec?.attachTo;
            if (string.IsNullOrEmpty(at) || Player_AbilityControl.instance == null) return null;
            return Player_AbilityControl.instance.GetAbilities().FirstOrDefault(a => a != null && a != bonus && Names(at, a));
        }

        /// <summary>Is the parent on the hotbar (so the bonus skill can stay off it)?</summary>
        internal static bool ParentOnBar(ModAbility bonus)
        {
            Ability parent = ParentOf(bonus);
            Menu_HUD hud = Menu_HUD.instance;
            return parent != null && hud != null && hud.actionIconSlots.Any(s => s != null && s.actionIcon != null && s.actionIcon.ability == parent);
        }

        // ---- the right-click ---------------------------------------------------------------------------------

        private static Vector2 pressAt;
        private static float pressTime;
        private static Action_Icon pressed;
        private static Popup popup;
        private static readonly List<RaycastResult> hits = new List<RaycastResult>();

        private static Action_Icon IconAt(Vector2 at)
        {
            if (EventSystem.current == null) return null;
            hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = at }, hits);
            return hits.Select(h => h.gameObject != null ? h.gameObject.GetComponentInParent<Action_Icon>() : null).FirstOrDefault(i => i != null && i.ability != null);
        }

        /// <summary>The click point raised to the icon's top edge, so the menu opens above the hotbar, not over it.</summary>
        private static Vector2 Above(Action_Icon icon, Vector2 at)
        {
            var corners = new Vector3[4];
            ((RectTransform)icon.transform).GetWorldCorners(corners);
            Canvas root = icon.GetComponentInParent<Canvas>()?.rootCanvas;
            Camera cam = root == null || root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            float top = RectTransformUtility.WorldToScreenPoint(cam, corners[1]).y;
            return new Vector2(at.x, Mathf.Max(at.y, top));
        }

        internal static void Tick()
        {
            if (Player.instance == null || Menu_HUD.instance == null || Game.Game_isLoading) return;
            if (Input.GetMouseButtonDown(1))
            {
                pressed = IconAt(Input.mousePosition);
                pressAt = Input.mousePosition;
                pressTime = Time.unscaledTime;
            }
            if (pressed != null && Input.GetMouseButtonUp(1))
            {
                Action_Icon icon = pressed;
                pressed = null;
                if (Time.unscaledTime - pressTime > ClickSeconds || Vector2.Distance(Input.mousePosition, pressAt) > ClickPixels) return;
                if (IconAt(Input.mousePosition) != icon) return;
                Open(icon.ability, Above(icon, Input.mousePosition));
            }
        }

        /// <summary>A row of the menu that isn't a skill: an item a loaded skill can take (Loads).</summary>
        internal class Choice
        {
            internal Sprite icon;
            internal System.Func<string> label, title, tip;
            internal System.Action pick;
        }

        /// <summary>Opens the menu for a skill at a screen point: the items it can be loaded with (Loads), then its bonus
        /// skills; false when it has neither.</summary>
        internal static bool Open(Ability parent, Vector2 at)
        {
            var list = For(parent);
            var loads = parent is ModAbility m && m.Spec?.loads != null && m.Spec.loads.Count > 0 ? Loads.Choices(m) : new List<Choice>();
            if (list.Count == 0 && loads.Count == 0) return false;
            if (popup == null && !Make()) return false;
            popup.Show(loads.Count > 0 ? $"{parent.GetDisplayName()}: load with" : $"{parent.GetDisplayName()}: bonus skills", list, at, false, loads);
            AudioManager.PlayEvent("event:/UI/UI_Click");
            return true;
        }

        /// <summary>Opens the menu for a class on the skills screen; one with no synergy skills yet says so.</summary>
        internal static bool OpenClass(Skill_Tree tree, Vector2 at)
        {
            if (popup == null && !Make()) return false;
            popup.Show($"{tree.displayName}: synergy skills", Synergies.GrantedTo(tree), at, true);
            AudioManager.PlayEvent("event:/UI/UI_Click");
            return true;
        }

        // the class icon has no right-click of its own; on a skill's icon the right-click stays the perk
        [HarmonyPatch(typeof(SkillSelect), nameof(SkillSelect.OnPointerClick))]
        private static class ClassIconClick
        {
            private static void Postfix(SkillSelect __instance, PointerEventData eventData)
            {
                if (eventData == null || eventData.button != PointerEventData.InputButton.Right || Game.RESOLVING) return;
                if (__instance.skillTree == null || __instance.skillTier != null || __instance.mc != null || Menu_Skills.instance == null) return;
                if (Menu_Skills.instance.skillTrees.Contains(__instance)) OpenClass(__instance.skillTree, eventData.position);
            }
        }

        /// <summary>A right-click at a screen point, through the same UI raycast a mouse click uses (devtests).</summary>
        internal static bool RightClickAt(Vector2 at)
        {
            Action_Icon icon = IconAt(at);
            return icon != null && Open(icon.ability, Above(icon, at));
        }

        internal static void Close() { if (popup != null) popup.Close(); }

        internal static bool IsOpen => popup != null && popup.gameObject.activeSelf;

        /// <summary>Devtest: picks an entry of the open menu by name, as a click would.</summary>
        internal static bool Pick(string name) => popup != null && popup.Pick(name);

        private static Canvas Root() => Menu_HUD.instance != null ? Menu_HUD.instance.GetComponentInParent<Canvas>()?.rootCanvas : null;

        private static bool Make()
        {
            Canvas root = Root();
            if (root == null) return false;
            RectTransform rt = UiKit.Panel(root.transform, "SCCL_BonusSkills", new Vector2(240f, 60f), UiKit.PanelColor);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0f);
            popup = rt.gameObject.AddComponent<Popup>();
            popup.font = UiKit.FontFrom(Menu_HUD.instance);
            rt.gameObject.AddComponent<Canvas>(); // its own layer, so it also sits above the skills screen
            rt.gameObject.AddComponent<GraphicRaycaster>();
            UiLook.Attach(rt, "BonusSkills");
            return true;
        }

        internal class Popup : MonoBehaviour
        {
            internal TMP_FontAsset font;
            private const float Width = 250f, Row = 34f, Pad = 6f;
            private readonly List<(ModAbility ab, TextMeshProUGUI label)> rows = new List<(ModAbility, TextMeshProUGUI)>();
            private readonly List<(Choice c, TextMeshProUGUI label)> choiceRows = new List<(Choice, TextMeshProUGUI)>();
            private float nextRefresh;
            private bool onSkillsScreen;

            private Button AddRow(RectTransform rt, float y, Sprite sp, System.Action click, System.Action enter)
            {
                Button b = UiKit.Button(rt, "", font, 16f, new Vector2(Width - 2f * Pad, Row - 2f), click, UiKit.ButtonColor);
                var brt = (RectTransform)b.transform;
                UiKit.At(brt, Pad, y);
                TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.Left;
                label.margin = new Vector4(36f, 0f, 4f, 0f);
                if (sp != null)
                {
                    var ic = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                    ic.transform.SetParent(brt, false);
                    var irt = (RectTransform)ic.transform;
                    irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
                    irt.pivot = new Vector2(0f, 0.5f);
                    irt.anchoredPosition = new Vector2(3f, 0f);
                    irt.sizeDelta = new Vector2(Row - 6f, Row - 6f);
                    var img = ic.GetComponent<Image>();
                    img.sprite = sp;
                    img.raycastTarget = false;
                }
                var trig = b.gameObject.AddComponent<EventTrigger>();
                var on = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                on.callback.AddListener(_ => enter());
                var off = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                off.callback.AddListener(_ => Tooltip.instance?.Hide());
                trig.triggers.Add(on); trig.triggers.Add(off);
                return b;
            }

            private Camera Cam
            {
                get { Canvas c = GetComponentInParent<Canvas>()?.rootCanvas; return c == null || c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera; }
            }

            private bool Over(Vector2 screen) => RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, screen, Cam);

            internal void Show(string title, List<ModAbility> list, Vector2 at, bool skillsScreen, List<Choice> choices = null)
            {
                foreach (Transform ch in transform) Destroy(ch.gameObject);
                rows.Clear();
                choiceRows.Clear();
                onSkillsScreen = skillsScreen;
                choices = choices ?? new List<Choice>();
                var rt = (RectTransform)transform;
                float y = Pad;
                RectTransform head = UiKit.Rect(rt, "Header", new Vector2(Width - 2f * Pad, 22f));
                UiKit.At(head, Pad, y);
                UiKit.Label(head, $"<color=#9FB7C9>{title}</color>", font, 14f, TextAlignmentOptions.Left);
                y += 24f;
                foreach (Choice c in choices)
                {
                    Choice pick = c;
                    Button b = AddRow(rt, y, c.icon, () => Choose(pick), () => Tooltip.instance?.Show_TinyTip(pick.title?.Invoke() ?? "", pick.tip?.Invoke() ?? "", null));
                    choiceRows.Add((c, b.GetComponentInChildren<TextMeshProUGUI>()));
                    y += Row;
                }
                if (list.Count == 0 && choices.Count == 0)
                {
                    RectTransform none = UiKit.Rect(rt, "None", new Vector2(Width - 2f * Pad, 22f));
                    UiKit.At(none, Pad, y);
                    UiKit.Label(none, "<color=#8C99A6>none yet</color>", font, 14f, TextAlignmentOptions.Left);
                    y += 24f;
                }
                if (list.Count > 0 && choices.Count > 0)
                {
                    RectTransform sub = UiKit.Rect(rt, "Bonus", new Vector2(Width - 2f * Pad, 22f));
                    UiKit.At(sub, Pad, y);
                    UiKit.Label(sub, "<color=#9FB7C9>bonus skills</color>", font, 14f, TextAlignmentOptions.Left);
                    y += 24f;
                }
                foreach (ModAbility ab in list)
                {
                    ModAbility pick = ab;
                    Button b = AddRow(rt, y, ab.displaySprite, () => Use(pick), () => Tooltip.instance?.Show_AbilityTip(pick));
                    rows.Add((ab, b.GetComponentInChildren<TextMeshProUGUI>()));
                    y += Row;
                }
                rt.sizeDelta = new Vector2(Width, y + Pad - 2f);
                gameObject.SetActive(true);
                transform.SetAsLastSibling();
                Canvas layer = GetComponent<Canvas>();
                layer.overrideSorting = true;
                layer.sortingOrder = 600;
                Place(rt, at + new Vector2(4f, 8f));
                Refresh();
            }

            /// <summary>Puts the menu's bottom-left corner just above the cursor, kept on screen.</summary>
            private void Place(RectTransform rt, Vector2 at)
            {
                var parent = (RectTransform)rt.parent;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, at, Cam, out Vector2 local)) return;
                Vector2 size = rt.sizeDelta * rt.localScale.x;
                Rect r = parent.rect;
                local.x = Mathf.Clamp(local.x, r.xMin, r.xMax - size.x);
                local.y = Mathf.Clamp(local.y, r.yMin, r.yMax - size.y);
                rt.anchoredPosition = local;
            }

            private void Refresh()
            {
                foreach (var (ab, label) in rows)
                {
                    int cd = ab.GetCooldownAmount();
                    bool ok = cd <= 0 && ab.CanUse();
                    string state = cd > 0 ? $" <size=80%><color=#FF9F43>{cd}</color></size>" : ok ? "" : " <size=80%><color=#8C99A6>can't now</color></size>";
                    label.text = (ok ? ab.GetDisplayName() : $"<color=#8C99A6>{ab.GetDisplayName()}</color>") + state;
                }
                foreach (var (c, label) in choiceRows) if (label != null) label.text = c.label?.Invoke() ?? "";
            }

            internal bool Pick(string name)
            {
                var choice = choiceRows.FirstOrDefault(r => r.label != null && StripTags(r.label.text).StartsWith(name));
                if (choice.c != null) { Choose(choice.c); return true; }
                var row = rows.FirstOrDefault(r => r.ab.GetDisplayName() == name);
                if (row.ab == null) return false;
                Use(row.ab);
                return true;
            }

            private static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<[^>]*>", "");

            private void Choose(Choice c)
            {
                Close();
                AudioManager.PlayEvent("event:/UI/UI_Click");
                c.pick?.Invoke();
            }

            private void Use(ModAbility ab)
            {
                Close();
                if (Game.RESOLVING || Game.TARGETING) { AudioManager.PlayEvent("event:/UI/UI_Error"); return; }
                if (onSkillsScreen) Menu_Skills.instance?.Hide();
                Player_AbilityControl.instance?.AbilitySelected(null, ab);
            }

            internal void Close()
            {
                Tooltip.instance?.Hide();
                gameObject.SetActive(false);
            }

            private void Update()
            {
                if (Player.instance == null || Game.Game_isLoading || Player.instance.GetIsDead()) { Close(); return; }
                if (onSkillsScreen && (Menu_Skills.instance == null || !Menu_Skills.instance.GetIsShowing())) { Close(); return; }
                if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(2)) && !Over(Input.mousePosition)) { Close(); return; }
                if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
                if (Time.unscaledTime < nextRefresh) return;
                nextRefresh = Time.unscaledTime + 0.25f;
                Refresh();
            }
        }
    }
}
