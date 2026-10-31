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
    /// The rune matrix: every skill of yours with rune sockets, the runes in your bag, and the reactions you know.
    /// Click an empty socket and then a rune to set it; click a set rune to take it back out. Locked in combat, but a
    /// skill with the "runepanel" action opens it for recharging runes there too.
    /// Opened with the hotkey or the "Runes" tab beside the health panel (shown when you have socketed skills).
    /// "Transmute" opens the Fabricator with the rune recipes you know.
    /// </summary>
    internal static class RunePanel
    {
        internal static ConfigEntry<KeyCode> PanelKey;
        internal static string KeyName => PanelKey != null ? Keyboard.KeyName(PanelKey.Value) : "F4";

        internal static void Bind(ConfigFile cfg)
        {
            PanelKey = cfg.Bind("Runes", "PanelKey", KeyCode.F4, "Opens the rune matrix: set runes into skills that have rune sockets.");
            Keyboard.MoveOnce(PanelKey, KeyCode.F5, "SCCL_RuneKeyMoved"); // F5 was the first default, and is the game's quicksave
        }

        private static Window win;

        private static readonly Color KindElement = new Color(0.95f, 0.55f, 0.25f), KindShape = new Color(0.4f, 0.75f, 1f),
            KindPower = new Color(1f, 0.82f, 0.35f), KindPayload = new Color(0.72f, 0.55f, 1f);

        internal static Color KindColor(string kind) => (kind ?? "").ToLowerInvariant() switch
        {
            "element" => KindElement,
            "shape" => KindShape,
            "payload" => KindPayload,
            _ => KindPower,
        };

        internal static List<ModAbility> Socketed() =>
            Player_AbilityControl.instance?.GetAbilities()?.OfType<ModAbility>().Where(m => m != null && (m.Spec?.runeSlots ?? 0) > 0).ToList()
            ?? new List<ModAbility>();

        internal static void Toggle()
        {
            if (win == null) return;
            if (win.gameObject.activeSelf) win.Close(); else win.Open();
        }

        /// <summary>Opened by a skill: recharging also works in combat.</summary>
        internal static void OpenCharging()
        {
            if (win == null) return;
            win.Open(true);
        }

        // ---- hover tips -----------------------------------------------------------------------------------------

        internal class Tip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public string title, body;
            public void OnPointerEnter(PointerEventData e) { if (!string.IsNullOrEmpty(title)) Tooltip.instance?.Show_TinyTip(title, body ?? "", null); }
            public void OnPointerExit(PointerEventData e) => Tooltip.instance?.Hide();
        }

        private static void AddTip(Component c, string title, string body)
        {
            var t = c.gameObject.AddComponent<Tip>();
            t.title = title; t.body = body;
        }

        // ---- window ---------------------------------------------------------------------------------------------

        internal class Window : MonoBehaviour
        {
            public TMP_FontAsset font;
            private RectTransform body;
            private string selAbility;
            private int selSlot = -1;
            private string note;
            private bool charging;
            private string bench; // the skill whose augments are open in the right column

            internal void Open(bool bySkill = false)
            {
                selAbility = null; selSlot = -1; note = null; charging = bySkill; bench = null;
                gameObject.SetActive(true);
                transform.SetAsLastSibling();
                AudioManager.PlayEvent("event:/UI/UI_Click");
                Rebuild();
            }

            internal void Close()
            {
                Tooltip.instance?.Hide();
                gameObject.SetActive(false);
            }

            private void Update()
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Player.instance == null || Game.Game_isLoading) Close();
            }

            private void Say(string s, bool error = true)
            {
                note = s;
                if (error) AudioManager.PlayEvent("event:/UI/UI_Error");
            }

            internal void Rebuild()
            {
                Tooltip.instance?.Hide(); // the row under the pointer is about to be replaced
                if (body != null) Destroy(body.gameObject);
                body = UiKit.Rect(transform, "Body", ((RectTransform)transform).sizeDelta);
                body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one; body.offsetMin = body.offsetMax = Vector2.zero;
                float W = ((RectTransform)transform).sizeDelta.x;
                bool combat = Conjoined.InCombat;
                bool canCharge = Socketed().Any(m => m.Spec.runeCharge > 0);

                Text(body, "Rune Matrix", 26, 20f, 12f, 400f, 34f, TextAlignmentOptions.TopLeft, new Color(0.75f, 0.9f, 1f));
                string hint = bench != null ? "Spend the pool on how this spell behaves and lands. Hover for each one's gain and price; runes can resonate with them."
                    : combat ? (charging ? "Charge runes with stamina, or discharge them back into it. They can't be swapped in combat." : "<color=#FF7272>Runes can't be changed in combat.</color>")
                    : "Click an empty socket, then a rune from your bag. Click a set rune to take it back out." +
                      (canCharge ? " Spent runes do nothing until you charge them; discharging turns charge back into stamina." : Socketed().Any(m => m.Spec.runeWear > 0) ? " Some skills wear their runes out; spares in your bag slot in." : " Runes are never used up.");
                Text(body, note ?? hint, 14, 20f, 46f, W - 220f, 22f, TextAlignmentOptions.TopLeft, note != null ? new Color(1f, 0.75f, 0.45f) : new Color(0.7f, 0.78f, 0.85f));
                Button(body, "Close", W - 96f, 12f, 80f, 30f, Close);
                Button(body, "Transmute", W - 214f, 12f, 110f, 30f, Transmute);

                // Skills and their sockets.
                var skills = Socketed();
                float y = 80f;
                if (skills.Count == 0) Text(body, "None of your skills has rune sockets.", 16, 20f, y, 500f, 24f, TextAlignmentOptions.TopLeft, Color.white);
                foreach (ModAbility m in skills)
                {
                    AbilitySpec s = m.Spec;
                    string id = m.internalName;
                    bool augments = Augments.Has(m);
                    float rowH = s.runeCharge > 0 || augments ? 112f : 60f;
                    RectTransform row = UiKit.Panel(body, "Skill", new Vector2(540f, rowH), new Color(0.05f, 0.1f, 0.14f, 0.9f));
                    UiKit.At(row, 16f, y);
                    RectTransform ic = UiKit.Rect(row, "Icon", new Vector2(44f, 44f));
                    UiKit.At(ic, 8f, 8f);
                    var img = ic.gameObject.AddComponent<Image>(); img.sprite = m.displaySprite; img.raycastTarget = true;
                    AddTip(img, m.GetDisplayName(), StripRules(m.GetTooltipDescription()));
                    Text(row, m.GetDisplayName(), 16, 60f, 6f, 160f, 22f, TextAlignmentOptions.TopLeft, Color.white);
                    int open = m.OpenRuneSlots;
                    Runes.Tuning t = m.Tuning;
                    string meter = t != null && t.instability > 0 && s.runeMeter != null ? $"<color=#FF9F43>+{t.instability} {Runes.EffectName(s.runeMeter, m.ClassId)} per cast</color>" : "";
                    Text(row, meter, 12, 60f, 30f, 160f, 20f, TextAlignmentOptions.TopLeft, Color.white);
                    if (augments)
                    {
                        var set = Augments.Set(m);
                        bool dark = Augments.IsDark(m);
                        Button(row, $"Augments ({Augments.Bill(m, set)})", 8f, 56f, 212f, 24f, () =>
                        {
                            bench = bench == id ? null : id;
                            selAbility = null; selSlot = -1; note = null;
                            AudioManager.PlayEvent("event:/UI/UI_Click");
                            Rebuild();
                        }, bench == id ? UiKit.OnColor : (Color?)null);
                        Text(row, string.Join(", ", set.Select(a => a.name)), 12, 10f, 84f, 210f, 22f, TextAlignmentOptions.TopLeft, dark ? new Color(1f, 0.45f, 0.45f) : new Color(0.9f, 0.75f, 1f));
                    }
                    string[] sockets = Runes.Sockets(id, s.runeSlots);
                    for (int i = 0; i < sockets.Length; i++)
                    {
                        int slot = i;
                        string rune = sockets[i];
                        bool sel = selAbility == id && selSlot == i;
                        RuneSpec r = Runes.Of(rune);
                        if (i >= open && i < m.RankRuneSlots)
                        {
                            Button sb = Button(row, rune == null ? "sealed" : Short(ItemRegistry.NameOf(rune)) + " (sealed)", 228f + i * 104f, 8f, 98f, 44f,
                                () => { Say("An augment on this skill keeps this socket sealed."); Rebuild(); }, new Color(0.22f, 0.11f, 0.05f, 0.95f));
                            AddTip(sb, "Sealed socket", "One of this skill's augments keeps it shut." + (rune != null ? " The rune in it does nothing and wears no charge until you take that augment off." : ""));
                            continue;
                        }
                        if (i >= open)
                        {
                            int at = Runes.OpensAt(s, i);
                            Button lb = Button(row, rune == null ? $"rank {at}" : Short(ItemRegistry.NameOf(rune)) + " (off)", 228f + i * 104f, 8f, 98f, 44f,
                                () => { Say($"This socket opens at class rank {at}."); Rebuild(); }, new Color(0.06f, 0.06f, 0.07f, 0.95f));
                            AddTip(lb, "Locked socket", $"Opens when the class reaches rank {at}." + (rune != null ? " The rune in it does nothing until then." : ""));
                            continue;
                        }
                        int left = rune != null ? Runes.CastsLeft(id, s, rune) : 0;
                        bool spent = Runes.IsSpent(id, s, rune);
                        string label = rune == null ? (sel ? "choose..." : "+ empty") : Short(ItemRegistry.NameOf(rune)) + (spent ? " (spent)" : left > 0 ? $" ({left})" : "");
                        Color c = rune == null ? (sel ? UiKit.OnColor : new Color(0.08f, 0.13f, 0.17f, 0.95f)) : spent ? new Color(0.06f, 0.06f, 0.07f, 0.95f) : Darker(KindColor(r?.kind));
                        int bill = Runes.RechargeCost(id, s, rune);
                        if (bill > 0)
                            Button(row, $"Charge {bill}", 228f + i * 104f, 56f, 98f, 24f, () =>
                            {
                                if (Conjoined.InCombat && !charging) { Say("In combat, open this with your charging skill."); Rebuild(); return; }
                                int back = Runes.Recharge(id, s, rune, Player.instance);
                                if (back > 0) { note = $"{ItemRegistry.NameOf(rune)} takes {back} cast{(back > 1 ? "s" : "")} of charge."; m.Refresh(); AudioManager.PlayEvent("event:/UI/Shing"); }
                                else Say("Not enough stamina.");
                                Rebuild();
                            }, new Color(0.1f, 0.3f, 0.36f, 0.95f));
                        int worth = Runes.DischargeValue(id, s, rune);
                        if (worth > 0)
                        {
                            int wait = Runes.DischargeWait;
                            int pays = Runes.DischargePays(id, s, rune, Player.instance);
                            Button db = Button(row, wait > 0 ? $"Discharge ({wait})" : $"Discharge {pays}", 228f + i * 104f, 84f, 98f, 24f, () =>
                            {
                                if (Conjoined.InCombat && !charging) { Say("In combat, open this with your charging skill."); Rebuild(); return; }
                                int back = Runes.Discharge(id, s, rune, Player.instance);
                                note = back > 0 ? $"{ItemRegistry.NameOf(rune)} gives {back} stamina and goes dark." : $"{ItemRegistry.NameOf(rune)} goes dark. No stamina came back.";
                                m.Refresh();
                                AudioManager.PlayEvent("event:/UI/UI_Click");
                                Rebuild();
                            }, new Color(0.34f, 0.2f, 0.08f, 0.95f));
                            AddTip(db, "Discharge", (wait > 0 ? $"Deactivates this rune. Discharging gives stamina again in {wait} turn{(wait > 1 ? "s" : "")}."
                                : $"Empties this rune's charge into your stamina: {pays} of the {worth} it holds" + (pays < worth ? ", all you have room for; the rest is lost." : ".")
                                  + (pays > 0 && s.runeDischarge > 0 ? $" After that, discharging gives no stamina for {s.runeDischarge} turns." : ""))
                                + " A spent rune does nothing until you charge it again.");
                        }
                        Button b = Button(row, label, 228f + i * 104f, 8f, 98f, 44f, () =>
                        {
                            if (Conjoined.InCombat) { Say("Not in combat."); Rebuild(); return; }
                            if (rune != null)
                            {
                                if (Runes.Unsocket(id, s, slot)) { note = $"{ItemRegistry.NameOf(rune)} is back in your bag."; m.Refresh(); AudioManager.PlayEvent("event:/UI/UI_Click"); }
                                else Say("No room in your bag.");
                            }
                            else { selAbility = id; selSlot = slot; note = null; bench = null; AudioManager.PlayEvent("event:/UI/UI_Click"); }
                            Rebuild();
                        }, c);
                        if (r != null)
                        {
                            RuneVariant v = Runes.VariantOf(s, rune);
                            AddTip(b, ItemRegistry.NameOf(rune), $"<color=#B58CFF>{Cap(r.kind)} rune</color>\n" +
                                (v != null && v.replace ? "" : Runes.Describe(r, m.ClassId) + "\n") +
                                (v != null ? "\n" + Runes.DescribeVariant(v, m.ClassId, true) + "\n" : "") +
                                (s.runeCharge > 0 ? $"\n<color=#FF9F43>{left} of {Runes.Life(s, rune)} casts of charge left; {s.runeCharge} stamina per cast to recharge.</color>\n"
                                    : left > 0 ? $"\n<color=#FF9F43>{left} of {Runes.Life(s, rune)} casts left; then a matching rune from your bag replaces it.</color>\n" : "") +
                                "\nClick to take it out" + (left > 0 ? " (the casts it has used stay used)." : "."));
                        }
                    }
                    y += rowH + 6f;
                }

                float rx = 576f, rw = W - rx - 16f;
                ModAbility benchSkill = skills.FirstOrDefault(m => m.internalName == bench);
                if (benchSkill != null) { Bench(benchSkill, rx, rw); return; }

                // The bag.
                Text(body, "Runes in your bag", 18, rx, 80f, rw, 24f, TextAlignmentOptions.TopLeft, new Color(0.75f, 0.9f, 1f));
                var bag = (Menu_Inventory.GetItems() ?? new List<Item>()).Where(i => i != null && i.inPlayerInventory && Runes.IsRune(i))
                    .GroupBy(i => i.itemId).Select(g => (id: g.Key, n: g.Sum(i => Math.Max(1, i.stack)))).OrderBy(x => Runes.Of(x.id).kind).ThenBy(x => x.id).ToList();
                ModAbility selM = skills.FirstOrDefault(m => m.internalName == selAbility);
                float by = 108f;
                if (bag.Count == 0) Text(body, "No runes. Rune Press mints them; Transmute turns spares into the ones you want.", 13, rx, by, rw, 40f, TextAlignmentOptions.TopLeft, new Color(0.7f, 0.78f, 0.85f));
                foreach (var (rid, n) in bag.Take(14))
                {
                    RuneSpec r = Runes.Of(rid);
                    string why = selM != null ? Runes.CantSocket(selM.Spec, Runes.Sockets(selM.internalName, selM.Spec.runeSlots), rid) : null;
                    Color c = selM == null ? Darker(KindColor(r.kind)) : why == null ? KindColor(r.kind) * 0.7f + new Color(0, 0, 0, 0.3f) : new Color(0.12f, 0.12f, 0.12f, 0.9f);
                    Button b = Button(body, $"{Short(ItemRegistry.NameOf(rid))}  x{n}", rx, by, rw, 28f, () =>
                    {
                        if (selM == null) { Say("Pick an empty socket first.", false); Rebuild(); return; }
                        if (Conjoined.InCombat) { Say("Not in combat."); Rebuild(); return; }
                        if (why != null) { Say(why); Rebuild(); return; }
                        if (Runes.Socket(selM.internalName, selM.Spec, selSlot, rid, selM.OpenRuneSlots))
                        {
                            note = $"{ItemRegistry.NameOf(rid)} set into {selM.GetDisplayName()}.";
                            selM.Refresh();
                            AudioManager.PlayEvent("event:/UI/Shing");
                            selAbility = null; selSlot = -1;
                        }
                        else Say("That didn't fit.");
                        Rebuild();
                    }, c);
                    var combos = skills.Select(sk => (sk, v: Runes.VariantOf(sk.Spec, rid))).Where(x => x.v != null).ToList();
                    AddTip(b, ItemRegistry.NameOf(rid), $"<color=#B58CFF>{Cap(r.kind)} rune, tier {r.tier}</color>\n{Runes.Describe(r, null)}" +
                        string.Concat(combos.Select(x => $"\n\nIn {x.sk.GetDisplayName()}: " + Runes.DescribeVariant(x.v, x.sk.ClassId, true))) +
                        (why != null && selM != null ? $"\n\n<color=#FF7272>{why}</color>" : ""));
                    by += 32f;
                }

                // Reactions.
                var known = Alchemy.Active();
                if (known.Count > 0)
                {
                    float ry = Mathf.Max(by + 10f, 108f + 32f * 7f);
                    Text(body, "Reactions you know", 18, rx, ry, rw, 24f, TextAlignmentOptions.TopLeft, new Color(0.75f, 0.9f, 1f));
                    string lines = string.Join("\n", known.Select(e =>
                        $"{string.Join(" + ", e.spec.needs.Select(x => Runes.EffectName(x, e.classId)))} = <color=#7FE3FF>{e.spec.name}</color>"));
                    TextMeshProUGUI lt = Text(body, lines, 13, rx, ry + 26f, rw, ((RectTransform)transform).sizeDelta.y - ry - 36f, TextAlignmentOptions.TopLeft, new Color(0.82f, 0.88f, 0.92f));
                    lt.enableWordWrapping = true;
                    lt.overflowMode = TextOverflowModes.Truncate;
                }
            }

            /// <summary>The right column while a skill's augments are open: the class's pool, what this skill can take, what it adds up to.</summary>
            private void Bench(ModAbility m, float x, float w)
            {
                string cls = m.ClassId;
                var set = Augments.Set(m);
                bool dark = Augments.IsDark(m);
                int pool = Augments.Pool(cls), spent = Augments.Spent(cls), mine = dark ? 0 : Augments.Bill(m, set);
                Text(body, m.GetDisplayName(), 18, x, 80f, w - 76f, 24f, TextAlignmentOptions.TopLeft, new Color(0.75f, 0.9f, 1f));
                Button(body, "Runes", x + w - 70f, 78f, 70f, 24f, () => { bench = null; note = null; AudioManager.PlayEvent("event:/UI/UI_Click"); Rebuild(); });

                // a pip per point: the class's other skills, this one, then what's free
                float gap = 3f, pip = Mathf.Min(24f, (w - gap * (pool - 1)) / Mathf.Max(1, pool));
                for (int i = 0; i < pool; i++)
                {
                    Color c = i < spent - mine ? new Color(0.3f, 0.26f, 0.42f, 0.95f) : i < spent ? KindPayload : new Color(0.08f, 0.13f, 0.17f, 0.95f);
                    UiKit.At(UiKit.Panel(body, "Pip", new Vector2(pip, 12f), c), x + i * (pip + gap), 108f);
                }
                RectTransform over = UiKit.Panel(body, "Pool", new Vector2(w, 16f), new Color(0f, 0f, 0f, 0f));
                UiKit.At(over, x, 106f);
                int perks = Augments.Skills(cls).Count(s => s.Perked);
                AddTip(over.GetComponent<Image>(), "Augment pool", $"Class rank {m.ClassRank}, plus {perks} for your perked rune skills. Every rune skill of this class draws on it.");
                Text(body, $"{pool - spent} of {pool} points free", 13, x, 124f, w, 20f, TextAlignmentOptions.TopLeft, new Color(0.7f, 0.78f, 0.85f));

                float y = 148f, cw = (w - 6f) / 2f;
                foreach (var (kind, title, tint) in new[]
                {
                    (Augments.Delivery, "Delivery: one per skill", KindShape),
                    (Augments.Behavior, "Behavior", KindPayload),
                    (Augments.Flaw, "Flaw: one per skill, discounts it", new Color(1f, 0.45f, 0.35f)),
                })
                {
                    var list = Augments.Catalog(cls).Where(a => a.kind == kind && Augments.Fits(m, a)).ToList();
                    if (list.Count == 0) continue;
                    Text(body, title, 14, x, y, w, 20f, TextAlignmentOptions.TopLeft, tint);
                    y += 22f;
                    for (int i = 0; i < list.Count; i++)
                    {
                        AugmentSpec a = list[i];
                        bool on = set.Contains(a), locked = a.rank > m.ClassRank;
                        string why = on ? null : Augments.CantTake(m, a);
                        Color c = on ? tint * 0.7f + new Color(0f, 0f, 0f, 0.3f) : why == null ? Darker(tint) : new Color(0.12f, 0.12f, 0.12f, 0.9f);
                        string label = locked ? $"{a.name} (rank {a.rank})" : $"{a.name}  {(kind == Augments.Flaw ? "-" : "")}{a.points}";
                        Button b = Button(body, label, x + i % 2 * (cw + 6f), y + i / 2 * 28f, cw, 25f, () =>
                        {
                            if (Conjoined.InCombat) { Say("Not in combat."); Rebuild(); return; }
                            string no = Augments.Toggle(m, a);
                            if (no != null) Say(Cap(no));
                            else { note = $"{a.name} {(on ? "taken off" : "set on")} {m.GetDisplayName()}."; AudioManager.PlayEvent(on ? "event:/UI/UI_Click" : "event:/UI/Shing"); }
                            Rebuild();
                        }, c);
                        var (gain, price) = Augments.Rules(a, cls);
                        string res = string.Concat((a.resonance ?? new List<AugmentResonance>()).Where(r => r != null).Select(r => $"\n<color=#FFD15C>{Augments.ResonanceLine(r, cls)}</color>"));
                        string earns = a.seal > 0 ? ", while every open socket holds a rune" : a.burn > 0 ? ", while a rune is set" : "";
                        AddTip(b, a.name, $"<color=#B58CFF>{Cap(a.kind)}, {(kind == Augments.Flaw ? "takes " + a.points + " off this skill's bill" + earns : a.points + " point" + (a.points == 1 ? "" : "s"))}</color>" +
                            (gain.Length > 0 ? "\n" + gain : "") + (price.Length > 0 ? $"\n<color=#FF9C7A>Price: {price}</color>" : "") + res +
                            (why != null ? $"\n\n<color=#FF7272>{Cap(why)}</color>" : ""));
                    }
                    y += (list.Count + 1) / 2 * 28f + 6f;
                }

                string sums = Augments.Preview(m);
                var sang = (m.Tuning?.mods ?? new List<SkillMods.Carried>()).Where(c => c.source != "Augment" && c.key.StartsWith("aug:")).Select(c => c.mod.name).ToList();
                string lines = dark ? $"<color=#FF7272>Dark: this skill's {Augments.Bill(m, set)} points don't fit your pool. It works unaugmented until there's room.</color>"
                    : $"<color=#9FB7C9>As set: {sums}.</color>" + (sang.Count > 0 ? $"\n<color=#FFD15C>Resonating: {string.Join(", ", sang)}.</color>" : "");
                TextMeshProUGUI lt = Text(body, lines, 13, x, y + 4f, w, Mathf.Max(40f, ((RectTransform)transform).sizeDelta.y - y - 14f), TextAlignmentOptions.TopLeft, Color.white);
                lt.enableWordWrapping = true;
            }

            private void Transmute()
            {
                var ids = Crafting.Recipes.Where(r => r.enabled && Runes.Of(r.outputId) != null).Select(r => r.qualified).ToList();
                if (ids.Count == 0) { Say("You don't know any rune recipes."); Rebuild(); return; }
                Close();
                Crafting.Open("field", ids);
            }

            private static string Short(string name) => (name ?? "").Replace(" Rune", "");
            private static string Cap(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1);
            private static Color Darker(Color c) => new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 0.95f);
            private static string StripRules(string s) => s ?? "";

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
                Button b = UiKit.Button(parent, label, font, 14, new Vector2(w, h), click, color);
                UiKit.At((RectTransform)b.transform, x, y);
                return b;
            }
        }

        // ---- HUD hooks ------------------------------------------------------------------------------------------

        /// <summary>The "Runes" tab beside the health panel, and the hotkey; shown only when you have socketed skills.</summary>
        internal class HudTab : MonoBehaviour, IPointerClickHandler
        {
            private float next;
            private bool have;

            public void OnPointerClick(PointerEventData e) => Toggle();

            private void Update()
            {
                if (Time.unscaledTime >= next)
                {
                    next = Time.unscaledTime + 0.5f;
                    have = Player.instance != null && !Game.Game_isLoading && Socketed().Count > 0;
                    foreach (Transform c in transform) c.gameObject.SetActive(have);
                    GetComponent<Image>().enabled = have;
                }
                if (have && Input.GetKeyDown(PanelKey.Value) && EventSystem.current?.currentSelectedGameObject?.GetComponent<TMP_InputField>() == null) Toggle();
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

                    RectTransform w = UiKit.Panel(canvas.transform, "SCCL_RuneMatrix", new Vector2(900f, 700f), UiKit.PanelColor);
                    w.anchorMin = w.anchorMax = w.pivot = new Vector2(0.5f, 0.5f);
                    w.anchoredPosition = Vector2.zero;
                    win = w.gameObject.AddComponent<Window>();
                    UiLook.Attach(w, "RuneMatrix");
                    UiLook.Collapsible(w, "Rune Matrix", UiLook.Corner.InsideLeft, false);
                    win.font = font;
                    w.gameObject.SetActive(false);

                    var stats = __instance.stats != null ? (RectTransform)__instance.stats.transform : null;
                    Transform parent = stats != null ? stats : canvas.transform;
                    RectTransform tab = UiKit.Panel(parent, "SCCL_RuneTab", new Vector2(70f, 26f), UiKit.ButtonColor);
                    tab.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    tab.anchorMin = tab.anchorMax = new Vector2(0f, 1f);
                    tab.pivot = new Vector2(1f, 0f);
                    tab.anchoredPosition = new Vector2(-6f, 6f);
                    TextMeshProUGUI label = UiKit.Label(tab, "Runes", font, 15);
                    label.color = new Color(0.8f, 0.7f, 1f);
                    tab.gameObject.AddComponent<HudTab>();
                    UiLook.Attach(tab, "RuneMatrix");
                    AddTip(tab, "Rune Matrix", $"Set runes into your skills' sockets. Hotkey: {KeyName}.");
                }
                catch (Exception e) { Plugin.Log.LogError("Rune matrix: " + e); }
            }
        }
    }
}
