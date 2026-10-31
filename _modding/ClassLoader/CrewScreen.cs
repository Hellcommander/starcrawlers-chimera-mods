using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SCClassLoader
{
    /// <summary>
    /// The crew screen ([Crew] Key, F6 by default): your crew's cards (class, level, stats, six gear slots) on the
    /// left, hiring while there's room, and the gear in your pack on the right. Click a crew card to select it,
    /// click gear in your pack to put it on them, click a slot on a card to take that item back.
    /// </summary>
    internal static class CrewScreen
    {
        private const float W = 1240f, H = 800f, M = 18f, Card = 210f, SlotS = 58f, GearS = 58f;
        private static RectTransform panel;
        private static TMP_FontAsset font;
        private static int selectedId;
        private static TextMeshProUGUI info;

        internal static bool Open => panel != null;

        internal static void Tick()
        {
            if (Crew.Key == null || !Input.GetKeyDown(Crew.Key.Value)) return;
            if (Open) Close();
            else if (Player.instance != null && !Game.Game_isLoading && Crew.On) Show();
        }

        internal static void Show()
        {
            Close();
            if (Menu_HUD.instance == null) return;
            Canvas canvas = Menu_HUD.instance.GetComponentInParent<Canvas>()?.rootCanvas;
            if (canvas == null) return;
            font = UiKit.FontFrom(Menu_HUD.instance);
            var members = Crew.Members;
            if (!members.Any(m => m.Id == selectedId)) selectedId = members.FirstOrDefault()?.Id ?? 0;

            panel = UiKit.Panel(canvas.transform, "SCCL_Crew", new Vector2(W, H), UiKit.PanelColor);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            UiLook.Attach(panel, "CrewScreen");
            UiLook.Collapsible(panel, "Crew", UiLook.Corner.InsideLeft, false);
            var ol = panel.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0.3f, 0.7f, 0.85f, 0.8f);
            ol.effectDistance = new Vector2(2f, -2f);

            Text(panel, "<color=#FFD15C>Crew</color>", 32, new Vector2(W, 46f), 0f, 10f, TextAlignmentOptions.Center);
            Text(panel, $"They level with you and fight with the gear you give them. Wages: <color=#FFD966>{Crew.Wage}</color> each per floor.   Credits: <color=#FFD966>{Menu_Inventory.instance.GetCredits()}</color>   ({Crew.Key.Value} to close)",
                18, new Vector2(W, 28f), 0f, 56f, TextAlignmentOptions.Center);
            var close = UiKit.Button(panel, "X", font, 22, new Vector2(44f, 40f), Close);
            UiKit.At((RectTransform)close.transform, W - 44f - M, 12f);

            // ---- left: crew cards, then hiring
            float leftW = 740f, y = 100f;
            foreach (Crew.Member m in members) { MemberCard(m, M, y, leftW); y += Card + 12f; }
            int max = Mathf.Clamp(Crew.MaxCrew.Value, 1, 3);
            if (members.Count < max)
            {
                string why = Crew.CantHire();
                Text(panel, $"<color=#9FB7C9>Hire a crawler</color>  <color=#FFD966>{Crew.HireCost} credits</color> + {Crew.HireFavour} favour{(why != null && !why.StartsWith("hiring costs") ? $"  <color=#FF7272>({why})</color>" : "")}",
                    20, new Vector2(leftW, 30f), M, y, TextAlignmentOptions.Left);
                y += 36f;
                float bw = (leftW - 3 * 10f) / 4f;
                for (int i = 0; i < Crew.Kinds.Length; i++)
                {
                    Crew.Kind k = Crew.Kinds[i];
                    Skill_Tree tree = Game.instance.skills.FirstOrDefault(s => s != null && s.internalName == k.tree);
                    RectTransform box = UiKit.Panel(panel, "Hire" + k.id, new Vector2(bw, 120f), UiKit.ButtonColor);
                    UiKit.At(box, M + i * (bw + 10f), y);
                    var b = box.gameObject.AddComponent<Button>();
                    b.targetGraphic = box.GetComponent<Image>();
                    string kwhy = Crew.CantHire(k);
                    b.interactable = kwhy == null;
                    FactionSpec fac = Crew.Faction(k);
                    Hover(box.gameObject, () => info.text = $"<b>{k.name}</b>{(fac != null ? $" from {fac.Tinted(fac.name)}" : "")}\n{k.blurb}"
                        + (kwhy != null ? $"\n<color=#FF7272>{kwhy}</color>" : "\n<color=#5CFF6B>Click to hire.</color>"));
                    b.onClick.AddListener(() => { if (Crew.Hire(k.id) != null) { selectedId = Crew.Members.Last().Id; Show(); } });
                    Icon(box, tree?.icon, 52f, 8f, 8f);
                    Text(box, k.name, 20, new Vector2(bw - 68f, 30f), 66f, 18f, TextAlignmentOptions.Left);
                    var blurb = Text(box, fac != null ? $"from {fac.Tinted(fac.name)}" + (kwhy != null && why == null ? $"\n<color=#FF7272>{kwhy}</color>" : "") : $"<color=#9FB7C9>{k.blurb}</color>", 14, new Vector2(bw - 16f, 50f), 8f, 64f, TextAlignmentOptions.TopLeft);
                    blurb.enableWordWrapping = true;
                }
            }
            else if (members.Count == 0) Text(panel, "Nobody to hire.", 18, new Vector2(leftW, 30f), M, y, TextAlignmentOptions.Left);

            // ---- right: your gear
            float gx = M + leftW + 24f, gw = W - gx - M;
            Crew.Member sel = members.FirstOrDefault(m => m.Id == selectedId);
            Text(panel, sel != null ? $"<color=#9FB7C9>Your gear</color>  (click to give to {sel.Name})" : "<color=#9FB7C9>Your gear</color>  (hire someone first)", 18, new Vector2(gw, 30f), gx, 100f, TextAlignmentOptions.Left);
            var gear = Menu_Inventory.instance.itemSlots.Where(s => s != null && s.item is Item_Equipment eq && Crew.Slots.Contains(eq.equipType)).Select(s => s.item).ToList();
            int cols = Mathf.Max(1, (int)((gw + 8f) / (GearS + 8f)));
            for (int i = 0; i < gear.Count; i++)
            {
                Item it = gear[i];
                string cant = sel == null ? "hire someone first" : Crew.CantEquip(sel, it);
                RectTransform cell = UiKit.Panel(panel, "Gear", new Vector2(GearS, GearS), cant == null ? UiKit.ButtonColor : new Color(0.08f, 0.08f, 0.1f, 0.9f));
                UiKit.At(cell, gx + (i % cols) * (GearS + 8f), 136f + (i / cols) * (GearS + 8f));
                Image pic = Icon(cell, it.inventorySprite, GearS - 8f, 4f, 4f);
                if (cant != null) pic.color = new Color(1f, 1f, 1f, 0.35f);
                var b = cell.gameObject.AddComponent<Button>();
                b.targetGraphic = cell.GetComponent<Image>();
                b.onClick.AddListener(() =>
                {
                    if (sel == null) return;
                    string why = Crew.CantEquip(sel, it);
                    if (why != null) { info.text = $"<color=#FF7272>{why}</color>"; AudioManager.PlayEvent("event:/UI/UI_Error"); return; }
                    Crew.Equip(sel, it);
                    Show();
                });
                Hover(cell.gameObject, () => info.text = Describe(it) + (cant != null ? $"\n<color=#FF7272>{cant}</color>" : ""));
            }
            if (gear.Count == 0) Text(panel, "<color=#6F8794>No gear in your pack.</color>", 16, new Vector2(gw, 30f), gx, 136f, TextAlignmentOptions.Left);

            info = Text(panel, "<color=#6F8794>Hover gear or a slot to see what it does.</color>", 17, new Vector2(gw, 170f), gx, H - 190f, TextAlignmentOptions.TopLeft);
            info.enableWordWrapping = true;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static void MemberCard(Crew.Member m, float x, float y, float w)
        {
            bool sel = m.Id == selectedId;
            RectTransform card = UiKit.Panel(panel, "Card" + m.Id, new Vector2(w, Card), sel ? new Color(0.07f, 0.2f, 0.25f, 0.95f) : new Color(0.04f, 0.09f, 0.12f, 0.95f));
            UiKit.At(card, x, y);
            var cb = card.gameObject.AddComponent<Button>();
            cb.targetGraphic = card.GetComponent<Image>();
            cb.onClick.AddListener(() => { selectedId = m.Id; Show(); });
            if (sel) { var o = card.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.45f, 0.9f, 1f, 0.9f); o.effectDistance = new Vector2(2f, -2f); }

            Skill_Tree tree = Crew.Tree(m);
            Icon(card, tree?.icon, 70f, 10f, 10f);
            int lvl = Player.instance.level;
            Character c = Crew.NpcOf(m);
            string state = m.Downed ? "<color=#FF8F5C>down until the next floor</color>" : c == null ? "<color=#9FB7C9>joins you on arrival</color>" : $"<color=#5CFF6B>{c.GetHealth()}/{c.GetHealthMax()} health</color>";
            Text(card, $"<b>{m.Name}</b>  <color=#9FB7C9>{m.Kind.name}, level {lvl}, {tree?.displayName} rank {Crew.TreeRank(m) + 1}</color>", 20, new Vector2(w - 200f, 30f), 92f, 10f, TextAlignmentOptions.Left);
            string pay = m.Missed == 0 ? "<color=#9FB7C9>paid up</color>" : $"<color=#FFC35C>owed a payday: pay up next floor or they go</color>";
            Text(card, state + "   " + pay, 16, new Vector2(w - 200f, 24f), 92f, 40f, TextAlignmentOptions.Left);
            Text(card, $"<color=#C9D6E0>{StatLine(m, c)}</color>", 15, new Vector2(w - 230f, 44f), 92f, 64f, TextAlignmentOptions.TopLeft).enableWordWrapping = true;

            var dismiss = UiKit.Button(card, "Dismiss", font, 16, new Vector2(100f, 30f), () => { Crew.Dismiss(m); Show(); }, UiKit.DangerColor);
            UiKit.At((RectTransform)dismiss.transform, w - 110f, 10f);

            // this crawler's standing order (the strip above the hotbar orders everyone at once)
            string key = "crew:" + m.Id;
            Commands.Order now = Commands.OrderOf(key);
            // standing orders only: Move here and Unleash are given from the strip, aimed at the moment
            var standing = Commands.All.Where(x => x.order != Commands.Order.MoveTo && x.order != Commands.Order.Unleash).ToArray();
            for (int i = 0; i < standing.Length; i++)
            {
                var (o, name, tip) = standing[i];
                var ob = UiKit.Button(card, name, font, 14, new Vector2(100f, 21f), () => { Commands.Give(key, o); Show(); }, o == now ? UiKit.OnColor : UiKit.ButtonColor);
                UiKit.At((RectTransform)ob.transform, w - 110f, 44f + i * 23f);
                Hover(ob.gameObject, () => info.text = $"<b>{name}</b>\n{tip}" + (o == Commands.Order.Focus ? "\n<color=#9FB7C9>Uses the last enemy you hit.</color>" : ""));
            }

            for (int i = 0; i < Crew.Slots.Length; i++)
            {
                EQUIP_TYPE slot = Crew.Slots[i];
                float sx = 92f + i * (SlotS + 34f), sy = Card - SlotS - 28f;
                Text(card, $"<color=#9FB7C9>{slot}</color>", 16, new Vector2(SlotS + 30f, 22f), sx - 15f, sy - 24f, TextAlignmentOptions.Center);
                RectTransform s = UiKit.Panel(card, "Slot" + slot, new Vector2(SlotS, SlotS), new Color(0f, 0f, 0f, 0.5f));
                UiKit.At(s, sx, sy);
                Item it = Crew.Peek(m, slot);
                if (it != null)
                {
                    Icon(s, it.inventorySprite, SlotS - 8f, 4f, 4f);
                    string desc = Describe(it);
                    Object.Destroy(it.gameObject);
                    var b = s.gameObject.AddComponent<Button>();
                    b.targetGraphic = s.GetComponent<Image>();
                    b.onClick.AddListener(() => { Crew.Unequip(m, slot); Show(); });
                    Hover(s.gameObject, () => info.text = desc + "\n<color=#9FB7C9>Click to take it back.</color>");
                }
            }
        }

        private static string StatLine(Crew.Member m, Character c)
        {
            if (c == null)
            {
                var (st, _) = Crew.Bonuses(m);
                return "Bonuses: " + string.Join(", ", st.Where(kv => kv.Value != 0).Select(kv => $"{(kv.Value > 0 ? "+" : "")}{kv.Value} {StatObj.GetStatString(kv.Key)}"));
            }
            var show = new[] { STAT_TYPE.Strength, STAT_TYPE.Toughness, STAT_TYPE.Agility, STAT_TYPE.MeleePower, STAT_TYPE.RangedPower, STAT_TYPE.Armor, STAT_TYPE.Dodge, STAT_TYPE.Crit };
            string stats = string.Join("   ", show.Select(t => $"{StatObj.GetStatString(t)} {c.GetStat(t)}"));
            Ability basic = Crew.BasicAttack(c, m.Kind.melee);
            string atk = basic != null ? "   Attack: " + string.Join(" ", basic.damageList.Where(d => d != null).Select(d => $"{d.damage}{(d.damageMax > d.damage ? "-" + d.damageMax : "")} {Game.GetSpriteString(d.dmgType)}")) : "";
            return stats + atk;
        }

        private static string Describe(Item it)
        {
            var lines = new List<string> { $"<b>{it.GetName()}</b>" };
            if (it is Item_Weapon w && w.GetStatDamageList().Count > 0)
                lines.Add($"{w.weaponType}: " + string.Join(" ", w.GetStatDamageList().Where(d => d != null).Select(d => $"{d.damage}{(d.damageMax > d.damage ? "-" + d.damageMax : "")} {Game.GetSpriteString(d.dmgType)}")));
            if (it is Item_Equipment eq)
            {
                foreach (StatObj s in eq.stats ?? new List<StatObj>()) if (s != null && s.amount != 0) lines.Add($"{(s.amount > 0 ? "+" : "")}{s.amount} {StatObj.GetStatString(s.statType)}");
                foreach (DmgObj r in eq.resistanceMods ?? new List<DmgObj>()) if (r != null && r.damage != 0) lines.Add($"{(r.damage > 0 ? "+" : "")}{r.damage} {Game.GetSpriteString(r.dmgType, isResist: true)} resistance");
            }
            return string.Join("\n", lines);
        }

        private static TextMeshProUGUI Text(Transform parent, string text, float size, Vector2 box, float x, float y, TextAlignmentOptions align)
        {
            RectTransform r = UiKit.Rect(parent, "Text", box);
            UiKit.At(r, x, y);
            var t = UiKit.Label(r, text, font, size, align);
            t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Overflow; // a box a hair short of a line would otherwise show nothing
            return t;
        }

        private static Image Icon(Transform parent, Sprite sprite, float size, float x, float y)
        {
            RectTransform r = UiKit.Rect(parent, "Icon", new Vector2(size, size));
            UiKit.At(r, x, y);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            if (sprite == null) img.color = new Color(1f, 1f, 1f, 0f);
            return img;
        }

        private static void Hover(GameObject go, System.Action enter)
        {
            var trig = go.AddComponent<EventTrigger>();
            var e = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            e.callback.AddListener(_ => enter());
            trig.triggers.Add(e);
        }

        internal static void Close()
        {
            if (panel != null) Object.Destroy(panel.gameObject);
            panel = null;
        }
    }
}
