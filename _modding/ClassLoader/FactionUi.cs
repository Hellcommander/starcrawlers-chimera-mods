using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// The faction screens: the job board that opens on a breach (with an auction view for stolen goods), the
    /// standing screen (F9 by default) with each contact's line ("the Wire"), the contact you call, the encounters
    /// in the echoes, and the job tracker shown while you're in an echo.
    /// </summary>
    internal static class FactionUi
    {
        private static RectTransform panel;
        private static NavNode boardPad;
        internal static bool Open => panel != null;

        private static Canvas Canvas() =>
            Menu_HUD.instance != null ? (Menu_HUD.instance.GetComponentInParent<Canvas>() ?? Object.FindObjectsOfType<Canvas>().FirstOrDefault(c => c.isRootCanvas)) : null;

        private static RectTransform NewPanel(string name, float w, float h)
        {
            Close();
            Canvas canvas = Canvas();
            if (canvas == null) return null;
            panel = UiKit.Panel(canvas.transform, name, new Vector2(w, h), UiKit.PanelColor);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            UiLook.Attach(panel, "FactionScreens");
            UiLook.Collapsible(panel, System.Text.RegularExpressions.Regex.Replace(name.Replace("SCCL_", ""), "(?<=[a-z])(?=[A-Z])", " "), UiLook.Corner.InsideLeft, false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Game.SetLock(flag: true);
            return panel;
        }

        internal static void Close()
        {
            if (panel != null)
            {
                Object.Destroy(panel.gameObject);
                Game.SetLock(flag: false);
                Tooltip.instance?.Hide();
            }
            panel = null;
        }

        private static TextMeshProUGUI Text(RectTransform parent, string text, TMP_FontAsset font, float size, float x, float y, float w, float h, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            RectTransform box = UiKit.Rect(parent, "Line", new Vector2(w, h));
            UiKit.At(box, x, y);
            var t = UiKit.Label(box, text, font, size, align);
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        private static Button Btn(RectTransform parent, string label, TMP_FontAsset font, float x, float y, float w, Action click, Color? color = null, float h = 40f)
        {
            Button b = UiKit.Button(parent, label, font, 18, new Vector2(w, h), click, color);
            UiKit.At((RectTransform)b.transform, x, y);
            return b;
        }

        private static RectTransform Row(RectTransform parent, float x, float y, float w, float h)
        {
            RectTransform row = UiKit.Panel(parent, "Row", new Vector2(w, h), new Color(0.06f, 0.11f, 0.15f, 0.95f));
            UiKit.At(row, x, y);
            return row;
        }

        private static TMP_FontAsset Font => UiKit.FontFrom(Menu_HUD.instance);

        // ---- the job board ------------------------------------------------------------------------------------

        internal static void ShowBoard(NavNode pad)
        {
            boardPad = pad;
            List<Offer> offers = Contracts.Board(Game.FLOOR);
            var goods = Contracts.GoodsCarried();
            string[] anoms = Echoes.Rolled(Game.FLOOR);
            const float w = 1040f, rh = 92f;
            float h = 156f + offers.Count * (rh + 8f) + (goods.Count > 0 ? 64f : 0f) + 66f;
            RectTransform p = NewPanel("SCCL_JobBoard", w, h);
            if (p == null) return;
            TMP_FontAsset font = Font;
            Text(p, "<color=#D070FF>Breach</color>: who's hiring", font, 28, 0f, 8f, w, 40f, TextAlignmentOptions.Center);
            Text(p, "One job per danger level. Leave by the way you came in to turn it in; bonuses pay even if the job fails.", font, 17, 20f, 50f, w - 40f, 28f, TextAlignmentOptions.Center);
            KindSpec kind = SideKinds.KindOf(Game.FLOOR);
            PlaceSpec place = Places.Of(Game.FLOOR);
            string where = place != null ? $"{SideKinds.Article(kind)}: {place.name}" : $"{SideKinds.Article(kind)} of floor {SideAreas.ThemeOf(Game.FLOOR) + 1}";
            var echoTip = Text(p, $"Beyond this breach: {where}, with {Echoes.AnomalyNames(anoms)}.  Going in freelance, the vault is {Echoes.GradeText(Echoes.VaultGrade(anoms, 0, kind))}; harder jobs raise it.",
                font, 17, 20f, 82f, w - 40f, 30f, TextAlignmentOptions.Center);

            float y = 124f;
            foreach (Offer o in offers)
            {
                RectTransform row = Row(p, 20f, y, w - 40f, rh);
                FactionSpec emp = o.Employer, tgt = o.Target;
                Text(row, $"{o.TierName}  {emp.Tinted(emp.name)}  <size=85%><color=#9FB7C9>you're {Factions.Tinted(Factions.Rep(emp))}</color>   vault {Echoes.GradeText(Echoes.VaultGrade(anoms, o.tier, kind))}</size>", font, 19, 12f, 4f, 820f, 26f);
                Text(row, o.Goal + ".", font, 17, 12f, 30f, 780f, 24f);
                string tline = tgt != null ? $"   {tgt.Tinted()} <color=#FF8F5C>-5</color>" : "";
                string bonus = o.bonus != "" ? $"   <color=#7FE0FF>{o.BonusText} (+{o.BonusCredits} cr, +2 {emp.Short})</color>" : "";
                Text(row, $"<size=90%><color=#FFD966>{o.credits} cr</color>, {o.xp} XP   {emp.Tinted()} <color=#9FE08A>+5</color>{tline}{bonus}</size>", font, 16, 12f, 56f, 820f, 30f);
                Offer pick = o;
                Btn(row, "Take job", font, w - 40f - 150f, 24f, 136f, () => Go(pick), UiKit.OnColor);
                y += rh + 8f;
            }

            if (goods.Count > 0)
            {
                Text(p, $"You're carrying <color=#FFD966>{goods.Count}</color> piece{(goods.Count > 1 ? "s" : "")} of stolen goods. Their owners' rivals are bidding.", font, 17, 20f, y + 8f, w - 260f, 44f);
                Btn(p, "Auction goods", font, w - 220f, y + 8f, 200f, () => ShowAuction(goods[0]));
                y += 64f;
            }

            Btn(p, "Go in freelance", font, w - 420f, h - 54f, 200f, () => Go(null));
            Btn(p, "Not now", font, w - 210f, h - 54f, 190f, Close);
            Btn(p, $"Contacts ({Factions.StandingKey.Value})", font, 20f, h - 54f, 220f, ShowStanding);
        }

        private static void Go(Offer o)
        {
            NavNode pad = boardPad;
            Close();
            boardPad = null;
            if (o != null) Contracts.Take(o);
            if (pad != null) Plugin.Instance.StartCoroutine(SideAreas.Enter(pad));
        }

        // ---- auctions -----------------------------------------------------------------------------------------

        private static void ShowAuction(Item goods)
        {
            if (goods == null) { ShowBoard(boardPad); return; }
            List<Contracts.Bid> bids = Contracts.Bids(goods);
            var all = Contracts.GoodsCarried();
            const float w = 900f;
            float h = 170f + bids.Count * 56f + 70f;
            RectTransform p = NewPanel("SCCL_Auction", w, h);
            if (p == null) return;
            TMP_FontAsset font = Font;
            FactionSpec owner = Contracts.OwnerOf(goods);
            int g = Contracts.Stars(goods);
            Text(p, $"Auction: <color=#FFD966>{goods.GetName(false)}</color> ({g}-star)", font, 28, 0f, 10f, w, 40f, TextAlignmentOptions.Center);
            Text(p, owner != null
                ? $"Stolen from {owner.Tinted(owner.name)} (you're {Factions.Tinted(Factions.Rep(owner))}). Selling it to a rival costs another <color=#FF8F5C>{Math.Max(1, g)}</color> standing with them; the fence doesn't."
                : "Nobody's claiming it.", font, 18, 20f, 54f, w - 40f, 50f);
            float y = 110f;
            if (bids.Count == 1) { Text(p, "<color=#9FB7C9>No faction wants it right now.</color>", font, 18, 20f, y, w - 40f, 30f); y += 40f; }
            foreach (Contracts.Bid b in bids)
            {
                Text(p, Contracts.Describe(b), font, 18, 20f, y + 8f, w - 220f, 40f);
                Contracts.Bid bid = b;
                Btn(p, b.fence ? "Fence it" : "Sell", font, w - 190f, y + 4f, 170f, () => { Contracts.Accept(goods, bid); ShowBoard(boardPad); }, b.fence ? (Color?)null : UiKit.OnColor);
                y += 56f;
            }
            if (all.Count > 1)
            {
                Item next = all[(all.IndexOf(goods) + 1) % all.Count];
                Btn(p, "Next item", font, 20f, h - 56f, 180f, () => ShowAuction(next));
            }
            Btn(p, "Back", font, w - 210f, h - 56f, 190f, () => ShowBoard(boardPad));
        }

        // ---- standing: the Wire -------------------------------------------------------------------------------

        internal static void ShowStanding()
        {
            var known = Factions.All.Where(Factions.Known).ToList();
            int unknown = Factions.All.Count - known.Count;
            var plans = FactionOps.Pending().ToList();
            var knownPlans = plans.Where(x => x.known).ToList();
            const float w = 1100f, rh = 104f;
            float h = 110f + known.Count * (rh + 8f) + 34f + (SideAreas.InSide ? 30f : 0f) + (plans.Count > 0 ? 30f + knownPlans.Count * 24f : 0f) + 66f;
            RectTransform p = NewPanel("SCCL_Standing", w, h);
            if (p == null) return;
            TMP_FontAsset font = Font;
            Text(p, "The Wire", font, 30, 0f, 8f, w, 42f, TextAlignmentOptions.Center);
            Offer job = Contracts.Job;
            Text(p, job != null ? $"Current job: {job.Employer.Tinted()} - {job.Goal}." : "No job running. Breaches in Chimera's floors are where they hire.", font, 17, 20f, 52f, w - 40f, 28f, TextAlignmentOptions.Center);
            float y = 88f;
            foreach (FactionSpec f in known)
            {
                RectTransform row = Row(p, 20f, y, w - 40f, rh);
                int rep = Factions.Rep(f), fav = Factions.Favour(f), tier = Factions.BoonTier(rep);
                string flags = string.Join(" ", new[] { "Trusted", "Hated", "Betrayed", "Deceitful" }.Where(x => Factions.Flag(f, x))
                    .Select(x => $"<color={(x == "Trusted" ? "#5CFFB0" : "#FF5C5C")}>[{x}]</color>"));
                string who = f.contact != null ? $"  <size=80%><color=#9FB7C9>{f.contact.name}, {f.contact.title}</color></size>" : "";
                Text(row, $"{f.Tinted(f.name)}  {Factions.Tinted(rep)} <color=#9FB7C9>{rep:+0;-0;0}</color>  favour <color=#7FE0FF>{fav:+0;-0;0}</color>  {flags}{who}", font, 20, 12f, 4f, 860f, 28f);
                string line = Factions.MoodLine(f);
                Text(row, $"<size=90%><i><color=#C9D6E0>{(line.Length > 0 ? "\"" + line + "\"" : f.description)}</color></i></size>", font, 16, 12f, 34f, 860f, 40f);
                string boon = f.boon == null ? "" : tier > 0 ? $"<color=#9FE08A>{f.boon.name} {new string('I', tier)}</color>" : $"<color=#9FB7C9>{f.boon.name} at Valued</color>";
                Text(row, $"<size=85%>Rivals: {Factions.RivalsText(f)}.   {boon}</size>", font, 15, 12f, 76f, 860f, 24f);
                FactionSpec ff = f;
                Btn(row, "Call", font, w - 40f - 150f, 30f, 136f, () => ShowContact(ff), UiKit.OnColor);
                y += rh + 8f;
            }
            Text(p, unknown > 0 ? $"<color=#9FB7C9>{unknown} more out there you haven't dealt with yet.</color>" : "", font, 16, 20f, y + 2f, w - 40f, 28f);
            y += 34f;
            if (SideAreas.InSide)
            {
                Text(p, BreachFinder.OpenBreachesText(skipCurrent: false) ?? "<color=#9FB7C9>No other open breaches on the floors you've seen.</color>", font, 16, 20f, y, w - 40f, 28f);
                y += 30f;
            }
            if (plans.Count > 0)
            {
                int hidden = plans.Count - knownPlans.Count;
                Text(p, $"<color=#FF8F5C>Trouble coming:</color> {plans.Count} operation{(plans.Count > 1 ? "s" : "")} against you{(hidden > 0 ? $" ({hidden} you only hear rumours of; a contact who knows the people behind it can tell you more)" : "")}.", font, 16, 20f, y, w - 40f, 28f);
                y += 30f;
                foreach (var pl in knownPlans) { Text(p, "   " + FactionOps.Describe(pl), font, 16, 20f, y, w - 40f, 24f); y += 24f; }
            }
            if (boardPad != null) Btn(p, "Back to the board", font, 20f, h - 54f, 240f, () => ShowBoard(boardPad));
            Btn(p, "Close", font, w - 210f, h - 54f, 190f, () => { boardPad = null; Close(); });
        }

        // ---- a contact ----------------------------------------------------------------------------------------

        internal static void ShowContact(FactionSpec f)
        {
            int rep = Factions.Rep(f), fav = Factions.Favour(f);
            var rewards = (f.rewards ?? new List<RewardSpec>()).Where(r => r.minRep <= rep + 40).ToList(); // don't dangle what's far out of reach
            const float w = 980f;
            float h = 246f + 3 * 50f + 38f + rewards.Count * 50f + 66f;
            RectTransform p = NewPanel("SCCL_Contact", w, h);
            if (p == null) return;
            TMP_FontAsset font = Font;
            ContactSpec c = f.contact;
            Text(p, $"{f.Tinted(c?.name ?? f.name)}", font, 30, 24f, 10f, w - 48f, 40f);
            Text(p, $"<color=#9FB7C9>{c?.title ?? ""}{(c != null ? ", " : "")}{f.name}</color>", font, 17, 24f, 50f, w - 48f, 26f);
            Text(p, $"<size=90%><color=#C9D6E0>{c?.bio ?? f.description}</color></size>", font, 16, 24f, 78f, w - 48f, 50f);
            string mood = Factions.MoodLine(f), favl = Factions.FavourLine(f);
            Text(p, $"<i>\"{mood}\"</i>{(favl.Length > 0 ? $"  <i><color=#7FE0FF>\"{favl}\"</color></i>" : "")}", font, 18, 24f, 132f, w - 48f, 60f);
            Text(p, $"You're {Factions.Tinted(rep)} ({rep:+0;-0;0}), favour <color=#7FE0FF>{fav:+0;-0;0}</color>. "
                    + string.Join(" ", FactionOps.Pending().Where(x => x.f == f).Select(x => $"<color=#FF8F5C>They have something planned for you ({x.op.name ?? x.op.type}, {x.left} step{(x.left == 1 ? "" : "s")}).</color>")),
                font, 16, 24f, 196f, w - 48f, 44f);

            float y = 246f;
            void Service(string label, string why, Action act)
            {
                Text(p, why == null ? label : $"<color=#7A8590>{label}  ({why})</color>", font, 17, 24f, y + 8f, w - 260f, 34f);
                Button b = Btn(p, why == null ? "Do it" : "-", font, w - 210f, y + 4f, 186f, () => { act(); ShowContact(f); }, why == null ? UiKit.OnColor : (Color?)null);
                b.interactable = why == null;
                y += 50f;
            }
            Service($"Smooth things over: <color=#FFD966>{FactionEvents.SmoothCost(f)} credits</color> for +5 standing", FactionEvents.CantSmooth(f), () => FactionEvents.Smooth(f));
            Service("Ask for work: a job of theirs on the next breach board", FactionEvents.CantAskForWork(f), () => FactionEvents.AskForWork(f));
            Service($"Ask what their rivals are planning against you: <color=#FFD966>{FactionEvents.IntelCost(f)} credits</color>", FactionEvents.CantBuyIntel(f), () => FactionEvents.BuyIntel(f));
            y += 6f;
            Text(p, "<color=#FFD966>Favours</color>", font, 19, 24f, y, 300f, 28f);
            y += 32f;
            foreach (RewardSpec r in rewards)
            {
                string why = Factions.CantCallIn(f, r);
                RewardSpec rr = r;
                Service($"{r.name} <color=#7FE0FF>({r.favour} favour)</color>{(r.callOff ? " - also calls off what they have planned" : "")}", why, () => Factions.CallIn(f, rr));
            }
            Btn(p, "Back", font, w - 210f, h - 54f, 190f, ShowStanding);
        }

        // ---- encounters ---------------------------------------------------------------------------------------

        internal static void ShowEncounter(FactionSpec f, EncounterSpec e, TeleportPad pad)
        {
            const float w = 900f;
            float h = 220f + e.options.Count * 54f + 60f;
            RectTransform p = NewPanel("SCCL_Encounter", w, h);
            if (p == null) return;
            TMP_FontAsset font = Font;
            Text(p, $"{f.Tinted(f.name)}: {e.title}", font, 26, 24f, 10f, w - 48f, 38f);
            Text(p, $"<color=#9FB7C9>You're {Factions.Tinted(Factions.Rep(f))} with them, favour {Factions.Favour(f):+0;-0;0}.</color>", font, 16, 24f, 50f, w - 48f, 24f);
            Text(p, e.text, font, 18, 24f, 80f, w - 48f, 120f);
            float y = 206f;
            foreach (OptionSpec o in e.options)
            {
                string why = FactionEvents.Locked(f, o.requires);
                OptionSpec oo = o;
                Button b = Btn(p, "", font, 24f, y, w - 48f, () => ShowResult(f, e, FactionEvents.Choose(f, oo, pad)), why == null ? UiKit.OnColor : (Color?)null, 46f);
                var label = b.GetComponentInChildren<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.text = why == null ? $"{FactionEvents.RequireText(o.requires)}{o.text}" : $"<color=#7A8590>{FactionEvents.RequireText(o.requires)}{o.text}  ({why})</color>";
                b.interactable = why == null;
                y += 54f;
            }
        }

        private static void ShowResult(FactionSpec f, EncounterSpec e, string result)
        {
            const float w = 760f, h = 220f;
            RectTransform p = NewPanel("SCCL_EncounterResult", w, h);
            if (p == null) return;
            TMP_FontAsset font = Font;
            Text(p, $"{f.Tinted(f.name)}: {e.title}", font, 24, 24f, 10f, w - 48f, 36f);
            Text(p, string.IsNullOrEmpty(result) ? "That's that." : result, font, 18, 24f, 56f, w - 48f, 110f);
            Btn(p, "Continue", font, w - 210f, h - 54f, 190f, Close, UiKit.OnColor);
        }

        // ---- tracker and hotkey -------------------------------------------------------------------------------

        internal class Tracker : MonoBehaviour
        {
            public TextMeshProUGUI text;
            private float next;

            private void Update()
            {
                if (Factions.Active && Input.GetKeyDown(Factions.StandingKey.Value) && Player.instance != null && !Game.Game_isLoading)
                {
                    if (Open) { boardPad = null; Close(); }
                    else if (!Game.LOCK && !Game.TARGETING && !Game.RESOLVING) { boardPad = null; ShowStanding(); }
                }
                if (Time.unscaledTime < next) return;
                next = Time.unscaledTime + 0.5f;
                string job = Factions.Active ? Contracts.TrackerText() : null, echo = Echoes.TrackerLine(), kind = SideKinds.TrackerLine(), tower = SideLayers.TrackerLine(), breach = BreachFinder.TrackerLine();
                string s = string.Join("\n", new[] { job, tower, kind, echo, breach }.Where(x => !string.IsNullOrEmpty(x)));
                text.gameObject.SetActive(!string.IsNullOrEmpty(s));
                if (!string.IsNullOrEmpty(s)) text.text = s;
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
                    var go = new GameObject("SCCL_JobTracker", typeof(RectTransform), typeof(Tracker));
                    var rt = (RectTransform)go.transform;
                    rt.SetParent(canvas.transform, false);
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
                    rt.anchoredPosition = new Vector2(24f, -24f);
                    rt.sizeDelta = new Vector2(900f, 70f);
                    UiLook.Attach(rt, "JobTracker");
                    UiLook.Collapsible(rt, "Job", UiLook.Corner.AboveLeft, true);
                    // the text is a child, so hiding it doesn't stop the tracker (or the hotkey)
                    var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                    var trt = (RectTransform)textGo.transform;
                    trt.SetParent(rt, false);
                    trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
                    var t = textGo.GetComponent<TextMeshProUGUI>();
                    t.font = UiKit.FontFrom(__instance);
                    t.fontSize = 20;
                    t.alignment = TextAlignmentOptions.TopLeft;
                    t.enableWordWrapping = true;
                    t.raycastTarget = false;
                    var tr = go.GetComponent<Tracker>();
                    tr.text = t;
                    go.SetActive(true);
                    t.gameObject.SetActive(false);
                }
                catch (Exception e) { Plugin.Log.LogError("Job tracker: " + e); }
            }
        }
    }
}
