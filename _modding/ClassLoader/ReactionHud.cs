using System;
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
    /// What's armed: a small panel above the hotbar's left end (over the page label) with one row per armed reaction
    /// (name, trigger, charges, turns left) and the slots in use. Hover a row for the reaction's full rules. Shown only
    /// for characters who can arm reactions.
    /// </summary>
    internal static class ReactionHud
    {
        private const float RowHeight = 26f, Width = 430f;

        internal class Row : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Reactions.Armed r;
            public TextMeshProUGUI text;

            public void OnPointerEnter(PointerEventData e)
            {
                if (r == null) return;
                Tooltip.instance?.Show_TinyTip(r.spec.name,
                    $"{Reactions.Describe(r.spec, r.classId)}.\n\n{r.charges} charge{(r.charges == 1 ? "" : "s")} left, disarms in {r.turns} turn{(r.turns == 1 ? "" : "s")}.", null);
            }

            public void OnPointerExit(PointerEventData e) => Tooltip.instance?.Hide();
        }

        internal class Panel : MonoBehaviour
        {
            public RectTransform hotbar;
            public TMP_FontAsset font;
            private TextMeshProUGUI header;
            private Image back;
            private readonly List<Row> rows = new List<Row>();
            private float nextRefresh;
            private string shown;

            internal void Build()
            {
                back = gameObject.AddComponent<Image>();
                back.color = new Color(0.03f, 0.06f, 0.09f, 0.72f);
                back.raycastTarget = false;
                header = Text(transform, 20f);
                ((RectTransform)header.transform).anchoredPosition = new Vector2(10f, 0f);
            }

            private TextMeshProUGUI Text(Transform parent, float size)
            {
                var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                var rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(Width - 12f, RowHeight);
                var t = go.GetComponent<TextMeshProUGUI>();
                if (font != null) t.font = font;
                t.fontSize = size;
                t.alignment = TextAlignmentOptions.MidlineLeft;
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Ellipsis;
                t.raycastTarget = false;
                return t;
            }

            private static string Short(ReactionSpec s)
            {
                bool allies = string.Equals(s.who, "allies", StringComparison.OrdinalIgnoreCase);
                switch ((s.on ?? "").ToLowerInvariant())
                {
                    case "approach": return allies ? "enemy steps up to your side" : "enemy steps next to you";
                    case "special": return s.interrupt ? "enemy special: cut off" : "enemy uses a special";
                    case "hurt": return allies ? "enemy hits your side" : "enemy hits you";
                    case "retreat": return allies ? "enemy backs off your side" : "enemy backs off you";
                    case "ranged": return allies ? "enemy shoots your side" : "enemy shoots you";
                    case "lowhealth": return $"a hit takes {(allies ? "your side" : "you")} under {Mathf.Clamp(s.threshold, 1, 99)}%";
                    case "debuff": return allies ? "enemy debuffs your side" : "enemy debuffs you";
                    case "dodge": return "you dodge";
                    default: return s.on;
                }
            }

            private void Update()
            {
                if (Time.unscaledTime < nextRefresh) return;
                nextRefresh = Time.unscaledTime + 0.25f;
                bool can = Player.instance != null && !Game.Game_isLoading && Player_AbilityControl.instance != null && CanArm();
                var list = can ? Reactions.Current : (IReadOnlyList<Reactions.Armed>)new List<Reactions.Armed>();
                string key = can ? $"{Reactions.Slots}:" + string.Join(";", list.Select(r => $"{r.spec.name}{r.charges}{r.turns}")) : "";
                if (key == shown) return;
                shown = key;
                foreach (Row row in rows) if (row != null) Destroy(row.gameObject);
                rows.Clear();
                gameObject.GetComponent<Image>().enabled = can;
                header.gameObject.SetActive(can);
                if (!can) { ((RectTransform)transform).sizeDelta = Vector2.zero; return; }

                int slots = Reactions.Slots;
                header.text = $"<color=#7FE0FF>Reactions</color> <color=#9FB7C9>{list.Count}/{slots}</color>"
                              + (list.Count == 0 ? "  <size=80%><color=#9FB7C9>none armed</color></size>" : "");
                for (int i = 0; i < list.Count; i++)
                {
                    Reactions.Armed r = list[i];
                    var t = Text(transform, 18f);
                    ((RectTransform)t.transform).anchoredPosition = new Vector2(10f, -RowHeight * (i + 1));
                    t.raycastTarget = true;
                    t.text = $"<color=#7FE0FF>{r.spec.name}</color>  <color=#C9D6E0>{Short(r.spec)}</color>  <color=#FFD966>x{r.charges}</color><color=#9FB7C9>, {r.turns} turn{(r.turns == 1 ? "" : "s")}</color>";
                    var row = t.gameObject.AddComponent<Row>();
                    row.r = r;
                    row.text = t;
                    rows.Add(row);
                }
                ((RectTransform)transform).sizeDelta = new Vector2(Width, RowHeight * (list.Count + 1) + 6f);
            }

            /// <summary>Only characters with an ability that arms reactions get the panel.</summary>
            private static bool CanArm() => Player_AbilityControl.instance.GetAbilities().Any(a => a is ModAbility m && m.Spec != null
                && (m.Spec.actions ?? new List<ActionSpec>()).Any(x => string.Equals(x.@do, "arm", StringComparison.OrdinalIgnoreCase)));

            private void LateUpdate()
            {
                if (hotbar == null || hotbar.rect.width <= 0f) return;
                var corners = new Vector3[4];
                hotbar.GetWorldCorners(corners);
                transform.position = corners[1]; // top-left, stacked above the page label and pet buttons
                ((RectTransform)transform).anchoredPosition += new Vector2(6f, 64f);
            }
        }

        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.Awake))]
        private static class Patch_Awake
        {
            private static void Postfix(Menu_HUD __instance)
            {
                try
                {
                    if (__instance.actionIconSlots == null || __instance.actionIconSlots.Length == 0 || __instance.actionIconSlots[0] == null) return;
                    RectTransform row = (RectTransform)__instance.actionIconSlots[0].transform.parent;
                    var go = new GameObject("SCCL_Reactions", typeof(RectTransform));
                    var rt = (RectTransform)go.transform;
                    rt.SetParent(row.parent, false);
                    go.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true; // placed by hand each frame: stay out of the hotbar's own layout, which otherwise grew by our height
                    rt.pivot = new Vector2(0f, 0f);
                    rt.sizeDelta = Vector2.zero;
                    var p = go.AddComponent<Panel>();
                    p.hotbar = (RectTransform)__instance.hotbar.transform;
                    p.font = __instance.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.font != null)?.font;
                    p.Build();
                    UiLook.Attach(rt, "Reactions");
                    UiLook.Collapsible(rt, "Reactions", UiLook.Corner.AboveLeft, true);
                }
                catch (Exception e) { Plugin.Log.LogError("Reactions panel: " + e); }
            }
        }
    }
}
