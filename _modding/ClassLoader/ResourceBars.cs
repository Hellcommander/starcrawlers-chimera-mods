using System;
using System.Collections.Generic;
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
    /// Class resources (Reagent, mana-style pools, meters like Strain) as small see-through bars just above
    /// your health and stamina. They brighten while a resource changes or under the mouse and fade back when idle;
    /// they move with the stats panel. The hotkey hides and shows them (remembered). Pools regenerate each turn and
    /// start filled the first time you have the class, whether or not the bars are shown.
    /// </summary>
    internal static class ResourceBars
    {
        internal static ConfigEntry<bool> Show;
        internal static ConfigEntry<float> IdleOpacity, Scale;
        internal static ConfigEntry<KeyCode> ToggleKey;
        private const string HiddenPref = "SCCL_ResourcesHidden";

        private static readonly string[] Palette = { "#4FA3FF", "#4FD1C5", "#B58CFF", "#FFC857" };
        private static readonly Color Back = new Color(0.02f, 0.05f, 0.08f, 0.62f);

        internal static void Bind(ConfigFile cfg)
        {
            Show = cfg.Bind("Resources", "ShowOverlay", true, "Shows class resources (Reagent, mana pools, ...) as bars above your health and stamina.");
            IdleOpacity = cfg.Bind("Resources", "IdleOpacity", 0.55f, "How visible the bars are while nothing changes (0 = invisible, 1 = solid). They brighten when a resource changes or under the mouse.");
            Scale = cfg.Bind("Resources", "Scale", 1f, "Size of the bars.");
            ToggleKey = cfg.Bind("Resources", "ToggleKey", KeyCode.F3, "Shows or hides the resource bars.");
            Keyboard.MoveOnce(ToggleKey, KeyCode.F6, "SCCL_ResourceKeyMoved"); // F6 was the first default, and opens the crew screen
        }

        internal static bool Hidden
        {
            get => PlayerPrefs.GetInt(HiddenPref, 0) == 1;
            set { PlayerPrefs.SetInt(HiddenPref, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // ---- which effects are resources ------------------------------------------------------------------------

        private static HashSet<string> Taken() => new HashSet<string>(
            Player_SkillControl.instance?.skillTrees?.Where(t => t != null).Select(t => t.internalName) ?? Enumerable.Empty<string>());

        /// <summary>The player's resources, in class then file order.</summary>
        internal static List<ModEffect.Runtime> Current()
        {
            var taken = Taken();
            if (taken.Count == 0) return new List<ModEffect.Runtime>();
            var paid = new HashSet<string>(ModAbility.Specs.Values.Where(r => r?.spec?.stackCost?.effect != null)
                .Select(r => Actions.EffectId(r.spec.stackCost.effect, r.classId)));
            return ModEffect.Specs.Values.Where(r => r?.spec != null && r.spec.maxStacks > 1 && taken.Contains(r.classId)
                && (r.spec.resource ?? (paid.Contains(r.id) || IsPool(r.spec)))).ToList();
        }

        private static bool IsPool(EffectSpec s) => s.regenPerTurn > 0 || s.startStacks > 0;

        // ---- pools ----------------------------------------------------------------------------------------------

        private static void Give(ModEffect.Runtime r, int n)
        {
            Player p = Player.instance;
            if (p == null || p.GetIsDead() || n <= 0) return;
            var ctx = new ActionContext { caster = p, classId = r.classId, sourceName = r.spec.name };
            if (p.effects.GetEffect(r.id) is ModEffect me) me.AddStacks(n, ctx);
            else Actions.ApplyEffect(new ActionSpec { @do = "effect", effect = r.id, stacks = n }, ctx, p);
        }

        /// <summary>Fills each pool once, the first time the player has its class (kept in the save).</summary>
        internal static void StartPools()
        {
            var data = SideAreas.Data;
            if (data == null || Player.instance == null || Game.Game_isLoading) return;
            SweepGonePools(data);
            foreach (var r in Current().Where(r => r.spec.startStacks > 0))
            {
                string key = "res_started_" + r.id;
                if (data.ContainsKey(key)) continue;
                data[key] = true;
                int have = Actions.StacksOn(Player.instance, r.id);
                // a pool that starts full starts full at its real cap, even when the class is taken late
                int start = r.spec.startStacks >= r.spec.maxStacks ? r.CapOn(Player.instance) : r.spec.startStacks;
                if (have < start) Give(r, start - have);
            }
        }

        private static Dictionary<string, object> swept;

        /// <summary>
        /// Once per loaded save: forgets the "started" mark of pools nothing defines any more, so one that comes back
        /// (its mod re-enabled) starts filled. The stacks themselves the game already drops: it skips a saved effect it
        /// can't find, and skills that paid with it move to stamina (ModAbility.PoolGone).
        /// </summary>
        private static void SweepGonePools(Dictionary<string, object> data)
        {
            if (ReferenceEquals(swept, data)) return;
            swept = data;
            foreach (string key in data.Keys.Where(k => k.StartsWith("res_started_")).ToList())
            {
                string id = key.Substring("res_started_".Length);
                if (ModEffect.Specs.ContainsKey(id)) continue;
                data.Remove(key);
                Plugin.Log.LogInfo($"Resource pool {id} no longer exists; its stacks are gone and skills that used it cost stamina instead.");
            }
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
        private static class Patch_Turn
        {
            private static void Postfix()
            {
                try
                {
                    StartPools();
                    foreach (var r in Current().Where(r => r.spec.regenPerTurn > 0)) Give(r, r.spec.regenPerTurn);
                }
                catch (Exception e) { Plugin.Log.LogError("Resource pools: " + e); }
            }
        }

        // ---- overlay --------------------------------------------------------------------------------------------

        private static Sprite round;
        private static Sprite Round => round != null ? round : round = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");

        /// <summary>The set colour, else red for a debuff meter, else a palette colour that stays with the resource.</summary>
        internal static Color ColorOf(ModEffect.Runtime r)
        {
            string hex = !string.IsNullOrEmpty(r.spec.resourceColor) ? r.spec.resourceColor
                       : r.spec.debuff ? "#E06A5A" : Palette[(r.id ?? "").Sum(ch => ch) % Palette.Length];
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.white;
        }

        internal class Row : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public ModEffect.Runtime rt;
            public Overlay owner;
            public TextMeshProUGUI name, value;
            public RectTransform track;
            public Image fill;
            public readonly List<Image> pips = new List<Image>();
            public Color color;
            public int shown = -1, shownMax = -1;

            public void OnPointerEnter(PointerEventData e)
            {
                owner.hovered = true;
                string extra = rt.spec.regenPerTurn > 0 ? $"\n<color=#9FD8FF>+{rt.spec.regenPerTurn} each turn</color>" : "";
                if (rt.spec.capScale != null) extra += "\n" + rt.CapLine();
                if (rt.spec.burstAt > 0) extra += $"\n<color=#FF9A7A>At {rt.spec.burstAt} it goes off.</color>";
                Tooltip.instance?.Show_TinyTip(rt.spec.name, (rt.spec.description ?? "") + extra, null);
            }

            public void OnPointerExit(PointerEventData e)
            {
                owner.hovered = false;
                Tooltip.instance?.Hide();
            }

            internal bool Refresh()
            {
                int max = rt.CapOn(Player.instance);
                int n = Mathf.Max(0, Actions.StacksOn(Player.instance, rt.id)); // can sit above a cap that fell (shown as is)
                if (n == shown && max == shownMax) return false;
                bool changed = shown >= 0;
                shown = n; shownMax = max;
                value.text = $"{n}/{max}";
                if (pips.Count > 0)
                    for (int i = 0; i < pips.Count; i++)
                    {
                        bool warn = rt.spec.burstAt > 0 && i + 1 >= rt.spec.burstAt;
                        Color c = warn ? Color.Lerp(color, new Color(1f, 0.35f, 0.3f), 0.6f) : color;
                        pips[i].color = i < n ? c : new Color(c.r * 0.25f, c.g * 0.25f, c.b * 0.25f, 0.55f);
                    }
                else fill.fillAmount = Mathf.Clamp01(n / (float)max);
                return changed;
            }
        }

        internal class Overlay : MonoBehaviour
        {
            public TMP_FontAsset font;
            public CanvasGroup group;
            public RectTransform rows;
            public bool hovered;
            private float lastChange = -10f, next;
            private string layout = "";
            private readonly List<Row> made = new List<Row>();

            private void Update()
            {
                bool want = Show.Value && Player.instance != null && !Game.Game_isLoading;
                if (want && Input.GetKeyDown(ToggleKey.Value) && EventSystem.current?.currentSelectedGameObject?.GetComponent<TMP_InputField>() == null)
                {
                    Hidden = !Hidden;
                    AudioManager.PlayEvent("event:/UI/UI_Click");
                    lastChange = Time.unscaledTime;
                }
                rows.gameObject.SetActive(want && !Hidden && made.Count > 0);
                if (!want) return;

                float target = hovered || Time.unscaledTime - lastChange < 2.5f ? 1f : Mathf.Clamp01(IdleOpacity.Value);
                group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime * (target > group.alpha ? 6f : 1.2f));

                if (Time.unscaledTime < next) return;
                next = Time.unscaledTime + 0.15f;
                try
                {
                    StartPools();
                    var res = Current();
                    // the cap is in the key: pips are laid out for it, so a cap that moves rebuilds the row
                    string key = string.Join("|", res.Select(r => r.id + ":" + r.CapOn(Player.instance))) + "@" + Scale.Value;
                    if (key != layout) { Rebuild(res); layout = key; }
                    bool changed = false;
                    foreach (Row r in made) changed |= r.Refresh();
                    if (changed) lastChange = Time.unscaledTime;
                    rows.gameObject.SetActive(!Hidden && made.Count > 0);
                }
                catch (Exception e) { Plugin.Log.LogError("Resource bars: " + e); next = Time.unscaledTime + 5f; }
            }

            private void Rebuild(List<ModEffect.Runtime> res)
            {
                foreach (Row r in made) if (r != null) Destroy(r.gameObject);
                made.Clear();
                hovered = false;
                float s = Mathf.Clamp(Scale.Value, 0.5f, 3f);
                float w = rows.rect.width > 10f ? rows.rect.width : 260f, h = 20f * s, gap = 4f * s;
                foreach (var r in res) made.Add(MakeRow(r, w, h, s));
                // Bottom row sits right on the stats panel; more stack upwards.
                for (int i = 0; i < made.Count; i++)
                {
                    var rt = (RectTransform)made[i].transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                    rt.pivot = new Vector2(0.5f, 0f);
                    rt.anchoredPosition = new Vector2(0f, i * (h + gap));
                }
                lastChange = Time.unscaledTime;
            }

            private Row MakeRow(ModEffect.Runtime r, float w, float h, float s)
            {
                RectTransform rt = UiKit.Panel(rows, "SCCL_Resource_" + r.id, new Vector2(w, h), Back);
                var bg = rt.GetComponent<Image>();
                bg.sprite = Round; bg.type = Image.Type.Sliced;
                var row = rt.gameObject.AddComponent<Row>();
                row.rt = r; row.owner = this; row.color = ColorOf(r);

                float nameW = Mathf.Round(w * 0.32f), valueW = 42f * s, pad = 6f * s;
                RectTransform nameBox = UiKit.Rect(rt, "Name", new Vector2(nameW, h));
                UiKit.At(nameBox, pad, 0f);
                row.name = UiKit.Label(nameBox, r.spec.name, font, 13f * s, TextAlignmentOptions.MidlineLeft);
                Flush(row.name);
                row.name.color = new Color(0.86f, 0.92f, 0.96f, 1f);

                float trackX = pad + nameW + pad, trackW = w - trackX - valueW - pad;
                row.track = UiKit.Rect(rt, "Track", new Vector2(trackW, h * 0.5f));
                UiKit.At(row.track, trackX, h * 0.25f);
                int max = r.CapOn(Player.instance);
                if (max <= 12)
                {
                    float pg = 2f * s, pw = (trackW - pg * (max - 1)) / max;
                    for (int i = 0; i < max; i++)
                    {
                        RectTransform pip = UiKit.Panel(row.track, "Pip", new Vector2(pw, h * 0.5f), row.color);
                        UiKit.At(pip, i * (pw + pg), 0f);
                        var img = pip.GetComponent<Image>();
                        img.sprite = Round; img.type = Image.Type.Sliced; img.raycastTarget = false;
                        row.pips.Add(img);
                    }
                }
                else
                {
                    RectTransform under = UiKit.Panel(row.track, "Under", Vector2.zero, new Color(0f, 0f, 0f, 0.45f));
                    under.anchorMin = Vector2.zero; under.anchorMax = Vector2.one; under.offsetMin = under.offsetMax = Vector2.zero;
                    var ui = under.GetComponent<Image>(); ui.sprite = Round; ui.type = Image.Type.Sliced; ui.raycastTarget = false;
                    RectTransform fill = UiKit.Rect(row.track, "Fill", Vector2.zero);
                    fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.offsetMin = fill.offsetMax = Vector2.zero;
                    row.fill = fill.gameObject.AddComponent<Image>();
                    row.fill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
                    row.fill.type = Image.Type.Filled; row.fill.fillMethod = Image.FillMethod.Horizontal;
                    row.fill.color = row.color; row.fill.raycastTarget = false;
                }

                RectTransform valueBox = UiKit.Rect(rt, "Value", new Vector2(valueW, h));
                UiKit.At(valueBox, w - valueW - pad * 0.5f, 0f);
                row.value = UiKit.Label(valueBox, "", font, 13f * s, TextAlignmentOptions.MidlineRight);
                Flush(row.value);
                row.value.overflowMode = TextOverflowModes.Overflow;
                return row;
            }

            private static void Flush(TMP_Text t) => ((RectTransform)t.transform).offsetMin = ((RectTransform)t.transform).offsetMax = Vector2.zero;
        }

        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.Awake))]
        private static class Patch_Awake
        {
            private static void Postfix(Menu_HUD __instance)
            {
                try
                {
                    // Hangs off the stats panel (health and stamina) so it follows when that panel is dragged.
                    var stats = __instance.stats != null ? (RectTransform)__instance.stats.transform : null;
                    Transform parent = stats != null ? stats : __instance.GetComponentInParent<Canvas>()?.transform;
                    if (parent == null) return;
                    RectTransform root = UiKit.Rect(parent, "SCCL_ResourceBars", new Vector2(stats != null && stats.rect.width > 10f ? stats.rect.width : 260f, 10f));
                    root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    root.anchorMin = root.anchorMax = new Vector2(0.5f, stats != null ? 1f : 0f);
                    root.pivot = new Vector2(0.5f, 0f);
                    root.anchoredPosition = new Vector2(0f, stats != null ? 6f : 150f);
                    var overlay = root.gameObject.AddComponent<Overlay>();
                    overlay.font = UiKit.FontFrom(__instance);
                    overlay.group = root.gameObject.AddComponent<CanvasGroup>();
                    overlay.group.alpha = Mathf.Clamp01(IdleOpacity.Value);
                    overlay.rows = UiKit.Rect(root, "Rows", root.sizeDelta);
                    overlay.rows.anchorMin = Vector2.zero; overlay.rows.anchorMax = Vector2.one;
                    overlay.rows.offsetMin = overlay.rows.offsetMax = Vector2.zero;
                }
                catch (Exception e) { Plugin.Log.LogError("Resource bars: " + e); }
            }
        }
    }
}
