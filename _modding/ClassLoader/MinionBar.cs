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
    /// The pet command strip on top of the hotbar ([Minions] CommandBar): one button per living pet showing its name
    /// and health. Click twice to unsummon it (its scrap back, or its corpse); right-click twice to break it down for the scrap it cost.
    /// </summary>
    internal static class MinionBar
    {
        private const float ArmSeconds = 2.5f;
        private static Strip strip;

        internal static bool CanCommand => Player.instance != null && !Game.RESOLVING && !Game.TARGETING && !Game.Game_isLoading;

        internal class PetButton : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public Character pet;
            public TextMeshProUGUI text;
            public Image back;
            private bool armedBreakdown;
            private float armedUntil;

            public void OnPointerClick(PointerEventData e)
            {
                if (pet == null || !CanCommand) return;
                bool breakdown = e.button == PointerEventData.InputButton.Right;
                if (breakdown && PetHandling.PartsOf(pet).count == 0) { AudioManager.PlayEvent("event:/UI/UI_Error"); return; }
                if (Time.unscaledTime < armedUntil && armedBreakdown == breakdown)
                {
                    armedUntil = 0f;
                    Tooltip.instance?.Hide();
                    PetHandling.Unsummon(pet, "Command", breakdown, keepRemains: true);
                    AudioManager.PlayEvent("event:/UI/UI_Click");
                    strip?.Rebuild();
                    return;
                }
                armedBreakdown = breakdown;
                armedUntil = Time.unscaledTime + ArmSeconds;
                AudioManager.PlayEvent("event:/UI/UI_Click");
                Refresh();
            }

            public void OnPointerEnter(PointerEventData e)
            {
                if (pet == null) return;
                var (id, n) = PetHandling.PartsOf(pet);
                string kind = PetHandling.IsConverted(pet) ? "Converted" : pet.characterType.ToString();
                Tooltip.instance?.Show_TinyTip($"{pet.GetName()}  {pet.GetHealth()}/{pet.GetHealthMax()}",
                    $"{kind}. Click twice: unsummon ({(n > 0 ? "you get its scrap back" : "it leaves its body as a corpse")}).\n" +
                    (n > 0 ? $"Right-click twice: break down for {n}× {ItemRegistry.NameOf(id)}." : "Nothing to break down: it cost no scrap."), null);
            }

            public void OnPointerExit(PointerEventData e) => Tooltip.instance?.Hide();

            internal void Refresh()
            {
                if (pet == null) return;
                bool armed = Time.unscaledTime < armedUntil;
                string name = pet.GetName();
                if (name.Length > 14) name = name.Substring(0, 13) + "…";
                text.text = armed
                    ? $"<color=#FF7272>{(armedBreakdown ? "Break down?" : "Unsummon?")}</color>\n<size=80%>click again</size>"
                    : $"{name}\n<size=80%><color={(pet.GetHealth() * 3 < pet.GetHealthMax() ? "#FF7272" : "#9FD99F")}>{pet.GetHealth()}/{pet.GetHealthMax()}</color>{(PetHandling.PartsOf(pet).count > 0 ? " <color=#C79750>scrap</color>" : "")}</size>";
                back.color = armed ? new Color(0.35f, 0.08f, 0.08f, 0.85f) : new Color(0.04f, 0.08f, 0.12f, 0.8f);
            }
        }

        internal class Strip : MonoBehaviour
        {
            public RectTransform panel;
            public TMP_FontAsset font;
            private readonly List<PetButton> buttons = new List<PetButton>();

            /// <summary>Pets only: Conjoined bodies have the party panel.</summary>
            private static List<Character> Pets() =>
                AI_Manager.instance.GetPlayerMinions().Where(c => PetHandling.IsPet(c) && !Conjoined.IsBody(c)).ToList();
            private float nextRefresh;

            internal void Rebuild()
            {
                foreach (PetButton b in buttons) if (b != null) Destroy(b.gameObject);
                buttons.Clear();
                if (!PetHandling.CommandBar.Value || AI_Manager.instance == null || Player.instance == null) return;
                foreach (Character pet in Pets()) buttons.Add(MakeButton(pet));
                // Shrink to fit beside the page label on the hotbar's left (about 170 wide).
                float room = panel != null && panel.rect.width > 0f ? panel.rect.width - 240f : 600f;
                float w = buttons.Count == 0 ? 150f : Mathf.Clamp(room / buttons.Count - 6f, 70f, 150f);
                for (int i = 0; i < buttons.Count; i++)
                {
                    var rt = (RectTransform)buttons[i].transform;
                    rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
                    rt.anchoredPosition = new Vector2(-i * (w + 6f), 0f);
                    buttons[i].text.fontSize = w < 110f ? 16 : 20;
                }
            }

            private PetButton MakeButton(Character pet)
            {
                var go = new GameObject("SCCL_Pet", typeof(RectTransform), typeof(Image), typeof(PetButton));
                var rt = (RectTransform)go.transform;
                rt.SetParent(transform, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
                rt.sizeDelta = new Vector2(150f, 52f);
                var b = go.GetComponent<PetButton>();
                b.pet = pet;
                b.back = go.GetComponent<Image>();
                var t = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                var trt = (RectTransform)t.transform;
                trt.SetParent(rt, false);
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(6f, 2f); trt.offsetMax = new Vector2(-6f, -2f);
                b.text = t.GetComponent<TextMeshProUGUI>();
                if (font != null) b.text.font = font;
                b.text.fontSize = 20;
                b.text.alignment = TextAlignmentOptions.Center;
                b.text.enableWordWrapping = false;
                b.text.raycastTarget = false;
                b.Refresh();
                return b;
            }

            private void LateUpdate()
            {
                if (panel == null || panel.rect.width <= 0f) return;
                var corners = new Vector3[4];
                panel.GetWorldCorners(corners);
                transform.position = corners[2]; // top-right, opposite the page label
                ((RectTransform)transform).anchoredPosition += new Vector2(-6f, 4f);
            }

            private void Update()
            {
                if (Time.unscaledTime < nextRefresh) return;
                nextRefresh = Time.unscaledTime + 0.25f;
                var pets = PetHandling.CommandBar.Value && AI_Manager.instance != null && Player.instance != null ? Pets() : new List<Character>();
                if (pets.Count != buttons.Count || buttons.Any(b => b == null || b.pet == null || !pets.Contains(b.pet))) Rebuild();
                else foreach (PetButton b in buttons) b.Refresh();
            }
        }

        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.Awake))]
        private static class Patch_Awake
        {
            private static void Postfix(Menu_HUD __instance)
            {
                strip = null;
                try
                {
                    if (__instance.actionIconSlots == null || __instance.actionIconSlots.Length == 0 || __instance.actionIconSlots[0] == null) return;
                    RectTransform row = (RectTransform)__instance.actionIconSlots[0].transform.parent;
                    var go = new GameObject("SCCL_PetBar", typeof(RectTransform), typeof(Strip));
                    var rt = (RectTransform)go.transform;
                    rt.SetParent(row.parent, false);
                    go.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true; // placed by hand each frame: stay out of the hotbar's own layout, which otherwise grew by our height
                    rt.sizeDelta = new Vector2(10f, 52f);
                    rt.pivot = new Vector2(1f, 0f);
                    strip = go.GetComponent<Strip>();
                    strip.panel = (RectTransform)__instance.hotbar.transform;
                    UiLook.Attach(rt, "MinionBar");
                    UiLook.Collapsible(rt, "Minions", UiLook.Corner.AboveRight, true);
                    strip.font = __instance.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.font != null)?.font;
                }
                catch (Exception e) { Plugin.Log.LogError("Pet command bar: " + e); }
            }
        }
    }
}
