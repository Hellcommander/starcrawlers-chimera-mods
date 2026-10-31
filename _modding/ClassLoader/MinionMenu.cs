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
    /// Right-click one of your own companions in the world (a bot, pet, summon, crawler or Conjoined body) for a small
    /// menu at the cursor: the Orders (Commands) for that one alone, jobs to do for you (Errands: use, unlock or bash a
    /// door or container, fetch an item), and for pets Unsummon and Break down (PetHandling). Only a quick click opens
    /// it: holding the right button still looks around, as in the base game, and a click on a hotbar or other panel is
    /// left to that panel. A minion ordered this way takes orders even with [Orders] CommandMinions off, until it's
    /// told to Engage again.
    /// Focus, Unleash, Move here and the jobs then ask for a click in the world: the enemy (outlined), the floor tile
    /// (marked) or the thing to deal with; right-click or Esc calls it off. The order strip's Focus, Unleash and Move here
    /// buttons pick the same way for the whole group. While picking, and over the menu, clicks don't also reach the
    /// game (no swing at what's behind, no door opening, no lockpick dialog).
    /// </summary>
    internal static class MinionMenu
    {
        internal static ConfigEntry<bool> Enabled;

        /// <summary>The longest press, and the farthest the mouse may move during it, that still count as a click and not a look.</summary>
        private const float ClickSeconds = 0.35f, ClickPixels = 8f;
        private const float ArmSeconds = 2.5f;

        private static Character pressed;
        private static Vector2 pressAt;
        private static float pressTime;
        private static int pressFrame = -1;
        private static bool dragged;
        private static Menu menu;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Orders", "MinionMenu", true,
                "Right-click one of your companions (bots, pets, summons, crew) for a menu of orders for that one alone, jobs it can do for you (open, unlock or bash doors and containers, fetch items), and to unsummon or break down a pet. A minion ordered this way takes orders even with CommandMinions off. Holding the right button still looks around.");
        }

        /// <summary>On your side and one you could order: not you, not a shopkeeper or a talking NPC.</summary>
        internal static bool Mine(Character c) =>
            PetHandling.IsPet(c) && c.GetComponent<Vendor>() == null && string.IsNullOrEmpty(c.linkedMiniEventTrigger);

        internal static bool IsOpen => menu != null && menu.gameObject.activeSelf;
        internal static Character Showing => IsOpen ? menu.who : null;

        private static bool CanOpen()
        {
            Player_Control pc = Player_Control.instance;
            if (Enabled == null || !Enabled.Value || Player.instance == null || pc == null || Game.Game_isLoading) return false;
            return !(Game.TARGETING || Game.LOCK || Game.VENDOR || Game.EVENT || pc.PREF_PermaFreeLook || pc.FreeLookToggleActive);
        }

        private static readonly List<RaycastResult> uiHits = new List<RaycastResult>();

        /// <summary>A hotbar button, the minion strip, a menu or any other panel under the point: the click is theirs.</summary>
        private static bool OverPanel(Vector2 at)
        {
            if (EventSystem.current == null) return false;
            uiHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = at }, uiHits);
            // the picking prompt follows the cursor but takes no clicks
            return uiHits.Any(h => h.gameObject != null && (prompt == null || !h.gameObject.transform.IsChildOf(prompt)));
        }

        private static readonly Dictionary<int, bool> seeThrough = new Dictionary<int, bool>();

        /// <summary>
        /// Lattices, grates, mesh fences and glass: you see what's behind them, so a click goes through. Told apart by
        /// how they're drawn (alpha-tested or transparent materials); walls, crates and machines stop it.
        /// </summary>
        internal static bool SeeThrough(Collider col)
        {
            int id = col.GetInstanceID();
            if (seeThrough.TryGetValue(id, out bool v)) return v;
            Renderer r = col.GetComponent<Renderer>() ?? col.GetComponentInParent<Renderer>() ?? col.GetComponentInChildren<Renderer>();
            v = r != null && r.sharedMaterials.Length > 0 && r.sharedMaterials.All(m => m != null && m.renderQueue >= 2450);
            seeThrough[id] = v;
            return v;
        }

        /// <summary>The camera's ray at a screen point, nearest first, past the triggers and see-through things.</summary>
        internal static IEnumerable<RaycastHit> Ray(Vector2 screen, List<string> trace = null)
        {
            Camera cam = Camera.main;
            if (cam == null) yield break;
            foreach (RaycastHit h in Physics.RaycastAll(cam.ScreenPointToRay(screen), 60f, cam.cullingMask & cam.eventMask).OrderBy(h => h.distance))
            {
                bool skip = h.collider.isTrigger || (h.collider.GetComponentInParent<Character>() == null && h.collider.GetComponentInParent<InteractiveObject>() == null
                    && h.collider.GetComponentInParent<Item>() == null && h.collider.GetComponentInParent<NavNode>() == null && SeeThrough(h.collider));
                trace?.Add($"{h.collider.name} {h.distance:0.0} m{(h.collider.isTrigger ? " (trigger)" : skip ? " (see-through)" : "")}");
                if (!skip) yield return h;
            }
        }

        /// <summary>The character under a screen point: the nearest thing the camera's ray hits, looking past floor tiles.</summary>
        internal static Character PickAt(Vector2 screen, List<string> trace = null)
        {
            foreach (RaycastHit h in Ray(screen, trace))
            {
                Character c = h.collider.GetComponentInParent<Character>();
                if (c == Player.instance) continue;
                if (c != null) { trace?.Add("-> " + c.GetName()); return c; }
                if (h.collider.GetComponentInParent<NavNode>() != null) continue;
                return null; // a wall or a prop in the way
            }
            return null;
        }

        /// <summary>The floor tile under a screen point (a character standing there stands for its tile).</summary>
        internal static NavNode TileAt(Vector2 screen)
        {
            if (Map.instance == null) return null;
            foreach (RaycastHit h in Ray(screen))
            {
                Character c = h.collider.GetComponentInParent<Character>();
                if (c == Player.instance) continue;
                NavNode n = c != null ? c.GetMover()?.GetCurrentNavNode() : h.collider.GetComponentInParent<NavNode>();
                if (n == null) n = Map.instance.GetNearestNavNodeAt(h.point, 1f, validNodesOnly: true);
                if (n == null) return null;
                return n.Block_Move || n.BlockCheck() || n.HasDoor() ? null : n;
            }
            return null;
        }

        /// <summary>A right press: remembered when it lands on one of yours. Run from Update and from the game's mouse-look,
        /// whichever comes first in the frame, so it happens before the look grabs the cursor.</summary>
        private static void Press(Vector2 at)
        {
            pressFrame = Time.frameCount;
            pressed = null;
            dragged = false;
            if (IsOpen && !menu.Over(at)) menu.Close();
            if (!CanOpen() || OverPanel(at)) return;
            Character c = PickAt(at);
            if (!Mine(c)) return;
            pressed = c;
            pressAt = at;
            pressTime = Time.unscaledTime;
        }

        private static void Release()
        {
            Character c = pressed;
            pressed = null;
            if (c == null || dragged || Time.unscaledTime - pressTime > ClickSeconds || !Mine(c) || !CanOpen()) return;
            Open(c, pressAt);
        }

        internal static void Tick()
        {
            if (picking)
            {
                bool cancel = Input.GetMouseButtonDown(1);
                if (cancel) pressFrame = Time.frameCount; // the cancelling right-click doesn't also open a menu
                PickFrame(TestPointer ?? (Vector2)Input.mousePosition, Input.GetMouseButtonDown(0), cancel);
                return;
            }
            if (Input.GetMouseButtonDown(1) && pressFrame != Time.frameCount) Press(Input.mousePosition);
            if (pressed != null && Input.GetMouseButtonUp(1))
            {
                if (Vector2.Distance(Input.mousePosition, pressAt) > ClickPixels) dragged = true;
                Release();
            }
        }

        /// <summary>Devtest: why a right-click at a point wouldn't open the menu (null: it would).</summary>
        internal static string WhyNot(Vector2 at)
        {
            Player_Control pc = Player_Control.instance;
            var why = new List<string>();
            if (Enabled == null || !Enabled.Value) why.Add("switched off");
            if (Game.Game_isLoading) why.Add("loading");
            if (Game.TARGETING) why.Add("targeting");
            if (Game.LOCK) why.Add("game locked");
            if (Game.VENDOR) why.Add("vendor/dialog");
            if (Game.EVENT) why.Add("event");
            if (pc != null && (pc.PREF_PermaFreeLook || pc.FreeLookToggleActive)) why.Add("free look");
            if (OverPanel(at)) why.Add("panel under the pointer: " + string.Join(", ", uiHits.Take(3).Select(h => h.gameObject.name)));
            if (!Mine(PickAt(at))) why.Add("no companion under the pointer");
            return why.Count == 0 ? null : string.Join("; ", why);
        }

        /// <summary>Devtest: a right-click at a screen point, through the same raycast and checks as the mouse.</summary>
        internal static void TestClick(Vector2 at)
        {
            Press(at);
            Release();
        }

        /// <summary>Devtest: where the pointer is while picking (in place of the real mouse, which is wherever the player left it).</summary>
        internal static Vector2? TestPointer;

        /// <summary>Devtest: the pointer over a screen point while picking, and optionally a left-click there.</summary>
        internal static void TestPick(Vector2 at, bool click)
        {
            TestPointer = at;
            PickFrame(at, click, false);
        }

        /// <summary>Devtest: the right-click that calls picking off.</summary>
        internal static void TestCancel() { if (picking) PickFrame(TestPointer ?? Vector2.zero, false, true); }

        /// <summary>While a right press on one of yours might still be a click, the look waits (and the cursor stays free).</summary>
        [HarmonyPatch(typeof(Player_Control), "Look")]
        private static class Patch_Look
        {
            private static bool Prefix()
            {
                if (picking) return true;
                if (Input.GetMouseButtonDown(1) && pressFrame != Time.frameCount) Press(Input.mousePosition);
                if (pressed == null || dragged || !Input.GetMouseButton(1)) return true;
                if (Time.unscaledTime - pressTime <= ClickSeconds && Vector2.Distance(Input.mousePosition, pressAt) <= ClickPixels) return false;
                dragged = true; // held or dragged: it's a look after all
                return true;
            }
        }

        /// <summary>Over the menu, while picking and just after: the world's clicks are ours.</summary>
        internal static bool Swallowing => picking || Time.frameCount <= swallowUntil || (IsOpen && menu.Over(Input.mousePosition));

        /// <summary>The game's own "pointer is over a menu" check, which keeps clicks from attacking, opening or taking things.</summary>
        [HarmonyPatch(typeof(HUD), nameof(HUD.OverMenuCheck))]
        private static class Patch_OverMenu
        {
            private static void Postfix(ref bool __result)
            {
                if (!__result && Swallowing) __result = true;
            }
        }

        private static int escFrame = -1;

        /// <summary>Esc (or the pad's Cancel) calls picking off or closes the menu, and that's all the press does.</summary>
        internal static bool Escape()
        {
            if (!(picking || IsOpen)) return false;
            escFrame = Time.frameCount;
            if (picking) Stop(true);
            else menu.Close();
            AudioManager.PlayEvent("event:/UI/UI_Click");
            return true;
        }

        /// <summary>Before the HUD reads Esc (open the system menu, or close a panel).</summary>
        [HarmonyPatch(typeof(HUD), "Update")]
        private static class Patch_Escape
        {
            private static void Prefix(HUD __instance)
            {
                if (!(picking || IsOpen) || escFrame == Time.frameCount) return;
                if ((!string.IsNullOrEmpty(__instance.ESCKey) && Input.GetKeyDown(__instance.ESCKey)) || Keybindings.GetInstance().GetButtonDown("Cancel")) Escape();
            }
        }

        /// <summary>The same press doesn't also open the system menu or close one of your panels.</summary>
        [HarmonyPatch]
        private static class Patch_EscapeTaken
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => new[]
            {
                AccessTools.Method(typeof(Menu_System), nameof(Menu_System.Show)),
                AccessTools.Method(typeof(HUD), "CloseAMenu"),
            };

            private static bool Prefix() => escFrame != Time.frameCount;
        }

        /// <summary>Doors and containers go straight to the lockpick dialog without that check.</summary>
        [HarmonyPatch]
        private static class Patch_ObjectClick
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => new[]
            {
                AccessTools.Method(typeof(InteractiveObject), nameof(InteractiveObject.OnMouseDown)),
                AccessTools.Method(typeof(Door), nameof(Door.OnMouseDown)),
                AccessTools.Method(typeof(Container), nameof(Container.OnMouseDown)),
            };

            private static bool Prefix() => !Swallowing;
        }

        // ---- picking in the world -------------------------------------------------------------------------------

        private enum Want { Enemy, Tile, Thing }

        private static bool picking;
        private static Want want;
        private static Commands.Order pickOrder;
        private static Errands.Job pickJob;
        /// <summary>The one companion it's for, or null for the whole group.</summary>
        private static Character pickOne;
        private static Commands.Group pickGroup;
        private static int swallowUntil = -1;
        private static Character outlined;
        private static InteractiveObject outlinedThing;
        private static bool tileMarked;
        private static RectTransform prompt;
        private static TextMeshProUGUI promptText;

        internal static bool Picking => picking;
        internal static Character PickingFor => picking ? pickOne : null;

        private static bool NeedsPick(Commands.Order o) => o == Commands.Order.Focus || o == Commands.Order.Unleash || o == Commands.Order.MoveTo;

        /// <summary>Focus, Unleash or Move here for one companion (or, with `one` null, a group): click the enemy or tile next.
        /// False when the order needs no pick (or there's nobody to give it to): give it straight away.</summary>
        internal static bool StartPicking(Commands.Order o, Character one, Commands.Group g)
        {
            if (!NeedsPick(o) || (one == null && Commands.Companions(g).Count == 0)) return false;
            pickOrder = o;
            Begin(o == Commands.Order.MoveTo ? Want.Tile : Want.Enemy, one, g);
            return true;
        }

        internal static void StartJob(Errands.Job job, Character one)
        {
            pickJob = job;
            Begin(Want.Thing, one, Commands.Group.Everyone);
        }

        private static void Begin(Want w, Character one, Commands.Group g)
        {
            if (IsOpen) menu.Close();
            pressed = null;
            want = w;
            pickOne = one;
            pickGroup = g;
            picking = true;
            if (prompt == null) MakePrompt();
            if (prompt != null) prompt.gameObject.SetActive(true);
            Game.LOG($"<color=#7FE0C8>{Who()}:</color> {Ask()} <color=#9FB7C9>({CancelHint})</color>");
            AudioManager.PlayEvent("event:/UI/UI_Click");
        }

        private static string Who() => pickOne != null ? pickOne.GetName() : "Orders (" + Commands.GroupName(pickGroup) + ")";

        private static string Ask() =>
            want == Want.Enemy ? $"{Commands.Name(pickOrder)}: click the enemy."
            : want == Want.Tile ? "Move here: click the floor."
            : $"{Errands.All.First(j => j.job == pickJob).name}: click a door, container or item.";

        private const string CancelHint = "right-click or Esc: cancel";

        private static void Stop(bool cancelled)
        {
            if (cancelled && picking) Game.LOG($"<color=#7FE0C8>{Who()}:</color> never mind.");
            picking = false;
            pickOne = null;
            Highlight(null, null, null);
            if (prompt != null) prompt.gameObject.SetActive(false);
            swallowUntil = Time.frameCount + 1;
        }

        private static bool StillOn() =>
            Player.instance != null && !Game.Game_isLoading && !Game.TARGETING && !Game.LOCK
            && (pickOne != null ? Mine(pickOne) : Commands.Companions(pickGroup).Count > 0);

        private static void PickFrame(Vector2 at, bool click, bool cancel)
        {
            if (!StillOn() || cancel) { Stop(true); return; }
            bool panel = OverPanel(at);
            Character foe = null;
            NavNode tile = null;
            Component thing = null;
            if (!panel)
            {
                if (want == Want.Enemy) { Character c = PickAt(at); if (Commands.Hostile(c)) foe = c; }
                else if (want == Want.Tile) tile = TileAt(at);
                else thing = Errands.ThingAt(at);
            }
            Highlight(foe, tile, thing as InteractiveObject);
            string over = foe != null ? foe.GetName() : tile != null ? "here" : thing != null ? Errands.NameOf(thing) : null;
            ShowPrompt(at, over);
            if (click && !panel) Choose(foe, tile, thing);
        }

        private static void Fail(string why)
        {
            Game.LOG(why);
            AudioManager.PlayEvent("event:/UI/UI_Error");
        }

        /// <summary>The click: give the order (or send on the job), or say why not and keep picking.</summary>
        private static void Choose(Character foe, NavNode tile, Component thing)
        {
            Character one = pickOne;
            switch (want)
            {
                case Want.Enemy:
                    if (foe == null) { Fail($"Click an enemy ({CancelHint})."); return; }
                    Stop(false);
                    if (one != null) Commands.GiveOne(one, pickOrder, foe);
                    else Commands.GiveAll(pickOrder, pickGroup, foe);
                    break;
                case Want.Tile:
                    if (tile == null) { Fail($"Click open floor ({CancelHint})."); return; }
                    NavNode from = one?.GetMover()?.GetCurrentNavNode();
                    if (one != null && from != tile && Commands.FirstStep(from, tile) == null) { Fail($"{one.GetName()} can't get there."); return; }
                    Stop(false);
                    if (one != null) Commands.GiveOne(one, Commands.Order.MoveTo, null, tile);
                    else Commands.GiveAll(Commands.Order.MoveTo, pickGroup, null, tile);
                    break;
                default:
                    if (thing == null) { Fail($"Click a door, container or item ({CancelHint})."); return; }
                    string why = Errands.Plan(one, pickJob, thing, out Commands.Errand e);
                    if (why != null) { Fail(why); return; }
                    Stop(false);
                    Commands.Send(one, e);
                    break;
            }
            AudioManager.PlayEvent("event:/UI/UI_Click");
            Commands.OrderBar.Refresh();
        }

        /// <summary>Outlines the enemy, marks the tile (the game's own target marker) or outlines the door or container.</summary>
        private static void Highlight(Character foe, NavNode tile, InteractiveObject thing)
        {
            if (outlined != foe)
            {
                if (outlined != null) outlined.SetOutline(false, 0);
                outlined = foe;
                if (foe != null) foe.SetOutline(true, 1);
            }
            if (outlinedThing != thing)
            {
                if (outlinedThing != null) outlinedThing.DisableOutline();
                outlinedThing = thing;
                if (thing != null) thing.EnableOutline();
            }
            Player_AbilityControl pac = Player_AbilityControl.instance;
            if (pac == null || pac.targetNode == null) return;
            if (tile != null)
            {
                pac.Toggle_TargetNode(true);
                pac.targetNode.transform.position = tile.transform.position;
                tileMarked = true;
            }
            else if (tileMarked)
            {
                pac.Toggle_TargetNode(false);
                tileMarked = false;
            }
        }

        private static Canvas Root() => Menu_HUD.instance != null ? Menu_HUD.instance.GetComponentInParent<Canvas>()?.rootCanvas : null;

        private static Camera CamOf(Canvas c) => c == null || c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;

        private static void MakePrompt()
        {
            Canvas root = Root();
            if (root == null) return;
            prompt = UiKit.Panel(root.transform, "SCCL_MinionPick", new Vector2(300f, 48f), UiKit.PanelColor);
            prompt.GetComponent<Image>().raycastTarget = false;
            prompt.anchorMin = prompt.anchorMax = new Vector2(0.5f, 0.5f);
            prompt.pivot = new Vector2(0f, 1f);
            promptText = UiKit.Label(prompt, "", UiKit.FontFrom(Menu_HUD.instance), 17f, TextAlignmentOptions.Left);
            promptText.overflowMode = TextOverflowModes.Overflow;
            UiLook.Attach(prompt, "MinionMenu");
        }

        /// <summary>A note by the cursor: who it's for, what to click, and what's under the cursor now.</summary>
        private static void ShowPrompt(Vector2 at, string over)
        {
            if (prompt == null) return;
            string text = $"<color=#7FE0C8>{Who()}</color>  {Ask()}\n<size=85%>{(over != null ? $"<color=#FFD15C>{over}</color>" : "<color=#9FB7C9>nothing to pick here</color>")}  <color=#9FB7C9>· {CancelHint}</color></size>";
            if (promptText.text != text)
            {
                promptText.text = text;
                Vector2 size = promptText.GetPreferredValues(text);
                prompt.sizeDelta = new Vector2(size.x + 24f, Mathf.Max(48f, size.y + 10f));
            }
            prompt.SetAsLastSibling();
            Place(prompt, at + new Vector2(18f, -18f));
        }

        /// <summary>Puts a panel's top-left corner at a screen point, kept on screen.</summary>
        private static void Place(RectTransform rt, Vector2 at)
        {
            var parent = (RectTransform)rt.parent;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, at, CamOf(rt.GetComponentInParent<Canvas>()?.rootCanvas), out Vector2 local)) return;
            Vector2 size = rt.sizeDelta * rt.localScale.x;
            Rect r = parent.rect;
            local.x = Mathf.Clamp(local.x, r.xMin, r.xMax - size.x);
            local.y = Mathf.Clamp(local.y, r.yMin + size.y, r.yMax);
            rt.anchoredPosition = local;
        }

        // ---- the menu -------------------------------------------------------------------------------------------

        private static void Open(Character c, Vector2 at)
        {
            if (menu == null && !Make()) return;
            menu.Show(c, at);
            AudioManager.PlayEvent("event:/UI/UI_Click");
        }

        private static bool Make()
        {
            Canvas root = Root();
            if (root == null) return false;
            RectTransform rt = UiKit.Panel(root.transform, "SCCL_MinionMenu", new Vector2(220f, 100f), UiKit.PanelColor);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            menu = rt.gameObject.AddComponent<Menu>();
            menu.font = UiKit.FontFrom(Menu_HUD.instance);
            UiLook.Attach(rt, "MinionMenu");
            return true;
        }

        internal class Menu : MonoBehaviour
        {
            internal Character who;
            internal TMP_FontAsset font;
            private const float Width = 230f, Row = 30f, Pad = 6f;
            private TextMeshProUGUI header;
            private readonly List<(Commands.Order o, Image back)> orders = new List<(Commands.Order, Image)>();
            private TextMeshProUGUI unlockLabel;
            private (bool breakdown, TextMeshProUGUI label, string text)? armed;
            private float armedUntil, nextRefresh;
            private readonly List<(bool breakdown, TextMeshProUGUI label, string text)> petRows = new List<(bool, TextMeshProUGUI, string)>();

            private Camera Cam => CamOf(GetComponentInParent<Canvas>()?.rootCanvas);

            internal bool Over(Vector2 screen) => RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, screen, Cam);

            /// <summary>Where a button sits on the screen (devtests click it through the UI raycast).</summary>
            internal Vector2 ScreenPointOf(string label)
            {
                RectTransform b = GetComponentsInChildren<Button>().Select(x => (RectTransform)x.transform)
                    .FirstOrDefault(x => (x.GetComponentInChildren<TMP_Text>()?.text ?? "").Contains(label));
                return b == null ? new Vector2(-1f, -1f) : RectTransformUtility.WorldToScreenPoint(Cam, b.TransformPoint(b.rect.center));
            }

            internal void Show(Character c, Vector2 at)
            {
                who = c;
                armed = null;
                Build();
                gameObject.SetActive(true);
                transform.SetAsLastSibling();
                Place((RectTransform)transform, at + new Vector2(6f, -6f));
                Refresh();
            }

            internal void Close()
            {
                who = null;
                armed = null;
                Tooltip.instance?.Hide();
                gameObject.SetActive(false);
            }

            private void Build()
            {
                var rt = (RectTransform)transform;
                // the old rows go at the end of the frame: hidden now, so they can't be clicked or found meanwhile
                foreach (Transform t in rt) { t.gameObject.SetActive(false); Destroy(t.gameObject); }
                orders.Clear();
                petRows.Clear();
                float y = Pad;
                RectTransform head = UiKit.Rect(rt, "Header", new Vector2(Width - 2f * Pad, 44f));
                UiKit.At(head, Pad, y);
                header = UiKit.Label(head, "", font, 17f, TextAlignmentOptions.Left);
                header.rectTransform.offsetMin = new Vector2(4f, 0f);
                header.rectTransform.offsetMax = new Vector2(-4f, 0f);
                y += 48f;
                foreach (var (o, name, tip) in Commands.All)
                {
                    bool pick = NeedsPick(o);
                    Button b = Item(rt, pick ? name + "…" : name, tip, ref y, UiKit.ButtonColor, () => Pick(o));
                    orders.Add((o, b.GetComponent<Image>()));
                }
                y += 4f;
                foreach (var (job, name, tip) in Errands.All)
                {
                    Button b = Item(rt, name + "…", tip, ref y, UiKit.ButtonColor, () => Job(job));
                    if (job == Errands.Job.Unlock) unlockLabel = b.GetComponentInChildren<TextMeshProUGUI>();
                }
                // pets (not crawlers or bodies) can also be sent away
                if (!Commands.IsCrewLike(who))
                {
                    y += 4f;
                    var (id, n) = PetHandling.PartsOf(who);
                    string back = n > 0 ? $"you get the {n}× {ItemRegistry.NameOf(id)} it cost back" : "its body stays behind as a corpse";
                    Button u = Item(rt, "Unsummon", $"Send it away: {back}, no loot. Click twice.", ref y, UiKit.DangerColor, () => Dismiss(false));
                    petRows.Add((false, u.GetComponentInChildren<TextMeshProUGUI>(), "Unsummon"));
                    if (n > 0)
                    {
                        string text = $"Break down ({n}× scrap)";
                        Button d = Item(rt, text, $"Take it apart for the {n}× {ItemRegistry.NameOf(id)} it cost. Click twice.", ref y, UiKit.DangerColor, () => Dismiss(true));
                        petRows.Add((true, d.GetComponentInChildren<TextMeshProUGUI>(), text));
                    }
                }
                rt.sizeDelta = new Vector2(Width, y + Pad - 2f);
            }

            private Button Item(RectTransform rt, string name, string tip, ref float y, Color color, Action click)
            {
                Button b = UiKit.Button(rt, name, font, 17f, new Vector2(Width - 2f * Pad, Row - 2f), click, color);
                var brt = (RectTransform)b.transform;
                UiKit.At(brt, Pad, y);
                y += Row;
                TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.Left;
                var trig = b.gameObject.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => Tooltip.instance?.Show_TinyTip(name.TrimEnd('…'), tip, null));
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => Tooltip.instance?.Hide());
                trig.triggers.Add(enter); trig.triggers.Add(exit);
                return b;
            }

            private void Pick(Commands.Order o)
            {
                if (who == null || !MinionBar.CanCommand) { AudioManager.PlayEvent("event:/UI/UI_Error"); return; }
                if (StartPicking(o, who, Commands.Group.Everyone)) return;
                Commands.GiveOne(who, o);
                AudioManager.PlayEvent("event:/UI/UI_Click");
                Commands.OrderBar.Refresh();
                Close();
            }

            private void Job(Errands.Job job)
            {
                if (who == null || !MinionBar.CanCommand) { AudioManager.PlayEvent("event:/UI/UI_Error"); return; }
                if (job == Errands.Job.Unlock && Errands.Lockpick() == null) { Fail("You have no lockpicks to give it."); return; }
                StartJob(job, who);
            }

            private void Dismiss(bool breakdown)
            {
                if (who == null || !MinionBar.CanCommand) { AudioManager.PlayEvent("event:/UI/UI_Error"); return; }
                if (armed.HasValue && armed.Value.breakdown == breakdown && Time.unscaledTime < armedUntil)
                {
                    PetHandling.Unsummon(who, "Command", breakdown, keepRemains: true);
                    AudioManager.PlayEvent("event:/UI/UI_Click");
                    Close();
                    return;
                }
                armed = petRows.First(r => r.breakdown == breakdown);
                armedUntil = Time.unscaledTime + ArmSeconds;
                AudioManager.PlayEvent("event:/UI/UI_Click");
                Refresh();
            }

            private void Refresh()
            {
                if (who == null) return;
                Commands.Order now = Commands.OrderOf(who);
                string hp = who.GetHealth() * 3 < who.GetHealthMax() ? "#FF7272" : "#9FD99F";
                string kind = Crew.IsCrew(who) ? "Crew" : Conjoined.IsBody(who) ? "Body" : PetHandling.IsConverted(who) ? "Converted" : "Minion";
                // what it's up to: a job, else its order (and its target when it's on one)
                Character target = who.ai?.GetTarget();
                string doing = Commands.ErrandOf(who) ?? Commands.Name(now) + (Commands.Hostile(target) ? " → " + target.GetName() : "");
                header.text = $"{who.GetName()}\n<size=80%><color={hp}>{who.GetHealth()}/{who.GetHealthMax()}</color>  <color=#9FB7C9>{kind} · {doing}</color></size>";
                foreach (var (o, back) in orders) back.color = o == now ? UiKit.OnColor : UiKit.ButtonColor;
                if (unlockLabel != null)
                {
                    int picks = Menu_Inventory.instance != null ? Menu_Inventory.instance.GetStacksOf("LOCKPICK") : 0;
                    unlockLabel.text = picks > 0 ? $"Unlock… <size=80%><color=#9FB7C9>{picks} lockpick{(picks == 1 ? "" : "s")}</color></size>" : "<color=#8C99A6>Unlock… <size=80%>no lockpicks</size></color>";
                }
                bool live = armed.HasValue && Time.unscaledTime < armedUntil;
                foreach (var (breakdown, label, text) in petRows)
                    label.text = live && armed.Value.breakdown == breakdown ? $"<color=#FF7272>{text}?</color> <size=80%>click again</size>" : text;
            }

            private void Update()
            {
                if (who == null || who.GetIsDead() || !Mine(who) || Player.instance == null || Game.Game_isLoading || Game.TARGETING) { Close(); return; }
                // a click anywhere else closes it (and goes on to do whatever it does there)
                if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(2)) && !Over(Input.mousePosition)) { Close(); return; }
                if (Time.unscaledTime < nextRefresh) return;
                nextRefresh = Time.unscaledTime + 0.25f;
                Refresh();
            }
        }
    }
}
