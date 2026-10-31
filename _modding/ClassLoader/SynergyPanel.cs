using System;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// A third view on the character screen, beside Attributes and Resistances: Synergies. Synergy bonuses stay off
    /// the buff bar (Synergies.Keep hides their effects); this view lists the active ones with their level and how much of each you use,
    /// and the ones your trees could still make with the tree each one needs.
    /// The game's stats box is two small containers (attributes, resistances) whose content spills out of them, each
    /// holding one swap button to the other. Each gets a Synergies button beside its swap button (both at half width);
    /// the synergies view is a third container like them, with Attributes and Resistances buttons of its own.
    /// </summary>
    internal static class SynergyPanel
    {
        private static Button tab, tab2;
        private static RectTransform container, panel;
        private static TextMeshProUGUI body;
        private static float shownScale = 1f;

        internal static TextMeshProUGUI Body => body;
        internal static bool Showing => container != null && container.gameObject.activeSelf;

        /// <summary>A copy of a swap button with its own click (the copy's vanilla call is a persistent listener).</summary>
        private static Button Clone(Button src, Transform parent, string label, Action click)
        {
            Button b = Object.Instantiate(src, parent);
            b.name = "SCCL_" + label + "Tab";
            b.onClick = new Button.ButtonClickedEvent();
            b.onClick.AddListener(() => click());
            foreach (Text t in b.GetComponentsInChildren<Text>(true))
            {
                t.text = label;
                // best fit only shrinks text that wraps or truncates
                t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 8; t.resizeTextMaxSize = t.fontSize;
            }
            foreach (TMP_Text t in b.GetComponentsInChildren<TMP_Text>(true)) { t.text = label; t.enableAutoSizing = true; t.fontSizeMax = t.fontSize; t.fontSizeMin = 8f; }
            b.gameObject.SetActive(true);
            return b;
        }

        /// <summary>Puts a button in the left or right half of where `like` sits (same parent frame).</summary>
        private static void Half(RectTransform me, RectTransform like, bool right)
        {
            me.anchorMin = like.anchorMin; me.anchorMax = like.anchorMax; me.pivot = like.pivot;
            float w = like.sizeDelta.x, half = w / 2f - 2f, left = like.anchoredPosition.x - like.pivot.x * w;
            me.sizeDelta = new Vector2(half, like.sizeDelta.y);
            me.anchoredPosition = new Vector2(left + (right ? w / 2f + 2f : 0f) + like.pivot.x * half, like.anchoredPosition.y);
        }

        private static void HalfLabel(Button b)
        {
            foreach (Text t in b.GetComponentsInChildren<Text>(true))
            {
                t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 8; t.resizeTextMaxSize = Mathf.Max(t.resizeTextMaxSize, t.fontSize);
            }
            foreach (TMP_Text t in b.GetComponentsInChildren<TMP_Text>(true)) { t.enableAutoSizing = true; t.fontSizeMax = Mathf.Max(t.fontSizeMax, t.fontSize); t.fontSizeMin = 8f; }
        }

        private static void Build(Menu_Player mp)
        {
            if (tab != null || mp.btn_resistances == null || mp.btn_attributes == null || mp.attributeContainer == null || mp.resistanceContainer == null) return;
            var toRes = (RectTransform)mp.btn_resistances.transform;   // lives in the attributes container
            var toAttr = (RectTransform)mp.btn_attributes.transform;   // lives in the resistances container
            // the box the stats are drawn in, before any button moves: everything under the attributes container but its button
            var corners = new Vector3[4];
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            var home = (RectTransform)mp.attributeContainer.transform;
            foreach (RectTransform r in home.GetComponentsInChildren<RectTransform>(true))
            {
                if (r == home || r.IsChildOf(toRes)) continue;
                r.GetWorldCorners(corners);
                foreach (Vector3 c in corners)
                {
                    Vector3 l = home.InverseTransformPoint(c);
                    min = Vector2.Min(min, l); max = Vector2.Max(max, l);
                }
            }
            toRes.GetWorldCorners(corners);
            float buttonsTop = home.InverseTransformPoint(corners[1]).y;
            // the button row spans the box's inside: the text keeps to that width (some stat rows reach past it)
            float boxLeft = home.InverseTransformPoint(corners[0]).x, boxRight = home.InverseTransformPoint(corners[2]).x;

            // a Synergies button beside each swap button
            tab = Clone(mp.btn_resistances, toRes.parent, "Synergies", Show);
            Half((RectTransform)tab.transform, toRes, right: true);
            tab2 = Clone(mp.btn_resistances, toAttr.parent, "Synergies", Show);
            Half((RectTransform)tab2.transform, toAttr, right: true);
            // the swap buttons take the left halves (Half works from a stand-in with their original rect)
            Vector2 resPos = toRes.anchoredPosition, resSize = toRes.sizeDelta, attrPos = toAttr.anchoredPosition, attrSize = toAttr.sizeDelta;
            var probe = UiKit.Rect(toRes.parent, "probe", resSize); probe.anchorMin = toRes.anchorMin; probe.anchorMax = toRes.anchorMax; probe.pivot = toRes.pivot; probe.anchoredPosition = resPos;
            Half(toRes, probe, right: false);
            Object.Destroy(probe.gameObject);
            probe = UiKit.Rect(toAttr.parent, "probe", attrSize); probe.anchorMin = toAttr.anchorMin; probe.anchorMax = toAttr.anchorMax; probe.pivot = toAttr.pivot; probe.anchoredPosition = attrPos;
            Half(toAttr, probe, right: false);
            Object.Destroy(probe.gameObject);
            HalfLabel(mp.btn_resistances); HalfLabel(mp.btn_attributes);

            // the synergies view: a third container in the same frame as the other two
            container = UiKit.Rect(home.parent, "SCCL_SynergiesView", home.sizeDelta);
            container.anchorMin = home.anchorMin; container.anchorMax = home.anchorMax; container.pivot = home.pivot;
            container.anchoredPosition = home.anchoredPosition; container.localScale = home.localScale;
            container.SetSiblingIndex(home.GetSiblingIndex() + 1);
            Button back = Clone(mp.btn_attributes, container, "Attributes", () => Menu_Player.instance?.BTN_ShowAttributes());
            probe = UiKit.Rect(container, "probe", resSize); probe.anchorMin = toRes.anchorMin; probe.anchorMax = toRes.anchorMax; probe.pivot = toRes.pivot; probe.anchoredPosition = resPos;
            Half((RectTransform)back.transform, probe, right: false);
            Button res = Clone(mp.btn_resistances, container, "Resistances", () => Menu_Player.instance?.BTN_ShowResistance());
            Half((RectTransform)res.transform, probe, right: true);
            Object.Destroy(probe.gameObject);

            // the text area: the stats box above the buttons
            panel = UiKit.Rect(container, "SCCL_SynergiesPanel", Vector2.zero);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            float top = max.y, bottom = Mathf.Max(min.y, buttonsTop + 4f);
            if (top - bottom < 60f) { top = bottom + 300f; }
            panel.localPosition = new Vector3((boxLeft + boxRight) / 2f, (top + bottom) / 2f, 0f);
            panel.sizeDelta = new Vector2(Mathf.Max(120f, boxRight - boxLeft), top - bottom);

            // a scrolling text column: a long list of synergies mustn't spill over the gear slots
            RectTransform view = UiKit.Rect(panel, "View", Vector2.zero);
            view.anchorMin = Vector2.zero; view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(6f, 6f); view.offsetMax = new Vector2(-6f, -6f);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f); // catches the wheel
            RectTransform content = UiKit.Rect(view, "Content", Vector2.zero);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0f, 1f);
            content.sizeDelta = new Vector2(100f, 100f);
            var sr = panel.gameObject.AddComponent<ScrollRect>();
            sr.viewport = view; sr.content = content; sr.horizontal = false; sr.scrollSensitivity = 30f;
            sr.movementType = ScrollRect.MovementType.Clamped;

            float size = 18f;
            Text like = mp.resistances?.FirstOrDefault(t => t != null);
            if (like != null) size = Mathf.Max(14f, like.fontSize * 0.9f);
            body = UiKit.Label(content, "", UiKit.FontFrom(Menu_HUD.instance != null ? (Component)Menu_HUD.instance : mp), size, TextAlignmentOptions.TopLeft);
            body.rectTransform.offsetMin = new Vector2(4f, 0f); body.rectTransform.offsetMax = new Vector2(-4f, 0f);
            body.enableWordWrapping = true;
            body.overflowMode = TextOverflowModes.Overflow;
            body.raycastTarget = true;
            body.gameObject.AddComponent<Clicks>();
            // the column scales and rewraps to the box (see Fill); the box itself stays inside the character screen
            UiLook.Attach(panel, "SynergyTab", content);
            UiLook.Grip(panel, panel, 28f); // the combat log covers the box's bottom corner
            container.gameObject.SetActive(false);
        }

        private static void Show()
        {
            Menu_Player mp = Menu_Player.instance;
            if (mp == null || container == null) return;
            mp.attributeContainer?.SetActive(false);
            mp.resistanceContainer?.SetActive(false);
            container.gameObject.SetActive(true);
            Fill();
            AudioManager.PlayEvent("event:/UI/UI_Click");
        }

        internal static void Refill() { if (Showing) Fill(false); }

        internal static void Hide()
        {
            if (container != null) container.gameObject.SetActive(false);
        }

        /// <summary>What the tab shows: active synergies first, then the ones a tree you have is half of.</summary>
        internal static string Text()
        {
            var active = Synergies.All.Select(s => (s, lvl: Synergies.Level(s))).Where(x => x.lvl > 0).ToList();
            var lines = active.Select(x =>
                $"<color=#7FE0C8>{x.s.name ?? x.s.id}</color>  <color=#FFD966>level {x.lvl}/{x.s.maxLevel}</color>\n" +
                Controls(x.s, x.lvl) + "\n" + PricedLine(x.s) +
                $"<size=85%><color=#9FB7C9>{string.Join(" + ", x.s.trees.Select(Synergies.TreeNames))}</color></size>\n{x.s.description}").ToList();
            string strain = Synergies.StrainText();
            if (strain != "") lines.Add(strain);
            if (lines.Count == 0) lines.Add("<color=#9FB7C9>No synergies yet. Two trees that work well together give a bonus that grows with the lower of their ranks.</color>");

            var half = Synergies.All.Where(s => Synergies.Level(s) == 0 && s.trees.Any(t => Synergies.TreeLevel(t) > 0)).ToList();
            if (half.Count > 0)
            {
                lines.Add("<b>Within reach</b>");
                foreach (SynergySpec s in half)
                {
                    string need = string.Join(" + ", s.trees.Where(t => Synergies.TreeLevel(t) == 0).Select(Synergies.TreeNames));
                    lines.Add($"<color=#FFD15C>{s.name ?? s.id}</color> <size=85%><color=#9FB7C9>(needs {need})</color></size>\n{s.description}");
                }
            }
            return "<b>Synergies</b>\n" + string.Join("\n<size=40%>\n</size>", lines);
        }

        /// <summary>The row that sets how much of a synergy you use: its [ - ] and [ + ] are links the click handler reads.</summary>
        private static string Controls(SynergySpec s, int level)
        {
            int used = Synergies.Applied(s);
            string Step(string dir, string glyph, bool can) =>
                can && !Synergies.ChoiceLocked ? $"<link=\"{dir}:{s.id}\"><color=#FFD966>[ {glyph} ]</color></link>" : $"<color=#5F6B75>[ {glyph} ]</color>";
            return $"{Step("down", "-", used > 0)} <color=#FFD966>using {used} of {level}</color> {Step("up", "+", used < level)}";
        }

        private static string PricedLine(SynergySpec s)
        {
            var skills = Synergies.PricedBy(s);
            return skills.Count == 0 ? "" : $"<size=85%><color=#FF9C7A>No strain: {string.Join(", ", skills.Select(a => a.GetDisplayName()))} pays for it</color></size>\n";
        }

        /// <summary>Clicks on the [ - ] and [ + ] links in the text column.</summary>
        private class Clicks : MonoBehaviour, IPointerClickHandler
        {
            private void LateUpdate()
            {
                if (Mathf.Abs(UiLook.Scale("SynergyTab") - shownScale) > 0.001f) Fill(false);
            }

            public void OnPointerClick(PointerEventData e)
            {
                int i = TMP_TextUtilities.FindIntersectingLink(body, e.position, e.pressEventCamera);
                if (i < 0) return;
                string[] link = body.textInfo.linkInfo[i].GetLinkID().Split(new[] { ':' }, 2);
                SynergySpec s = Synergies.All.FirstOrDefault(x => x.id == link[1]);
                if (s == null) return;
                Synergies.Choose(s, Synergies.Applied(s) + (link[0] == "up" ? 1 : -1));
                AudioManager.PlayEvent("event:/UI/UI_Click");
                Fill(false);
            }
        }

        private static void Fill(bool toTop = true)
        {
            if (body == null) return;
            body.text = Text();
            var content = (RectTransform)body.transform.parent;
            shownScale = UiLook.Scale("SynergyTab");
            float w = ((RectTransform)content.parent).rect.width / shownScale;
            float h = body.GetPreferredValues(body.text, w - 8f, 0f).y;
            content.sizeDelta = new Vector2(w, h + 8f);
            if (toTop) content.anchoredPosition = Vector2.zero;
        }

        [HarmonyPatch(typeof(Menu_Player), nameof(Menu_Player.Show))]
        private static class Patch_Show
        {
            private static void Postfix(Menu_Player __instance)
            {
                try
                {
                    Build(__instance);
                    if (tab != null) tab.gameObject.SetActive(Synergies.All.Count > 0);
                    if (tab2 != null) tab2.gameObject.SetActive(Synergies.All.Count > 0);
                    if (Showing) Fill();
                }
                catch (Exception e) { Plugin.Log.LogError("Synergies tab: " + e); }
            }
        }

        [HarmonyPatch(typeof(Menu_Player), nameof(Menu_Player.BTN_ShowAttributes))]
        private static class Patch_Attributes
        {
            private static void Postfix() => Hide();
        }

        [HarmonyPatch(typeof(Menu_Player), nameof(Menu_Player.BTN_ShowResistance))]
        private static class Patch_Resistances
        {
            private static void Postfix() => Hide();
        }
    }
}
