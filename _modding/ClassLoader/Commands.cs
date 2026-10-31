using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SCClassLoader
{
    /// <summary>
    /// Orders for your companions: crew crawlers, Conjoined bodies walking beside you, and (with [Orders]
    /// CommandMinions, off by default) your summons and pets too.
    ///   Engage     - their own judgement (the default)
    ///   Focus      - everyone on your target (the enemy under your crosshair, else the last one you hit) until it drops
    ///   Guard      - go after whoever last hurt you, and stay near you
    ///   Hold       - don't move; fight whatever can be reached from here
    ///   Follow     - stay at your side; fight only from there
    ///   Stand down - don't fight at all; stay with you
    ///   Move here  - go to the tile you're aiming at and hold it
    ///   Fall back  - regroup beside you on the side away from the enemy; fight only what comes adjacent
    ///   Unleash    - one-off: next turn each uses its strongest ready special move on your target, then carries on
    /// Given from the order strip above the hotbar (or Alt+1..9) to the group it shows (everyone, crew or minions;
    /// Alt+0 switches), to one crawler on the crew screen, or to any one companion from its right-click menu
    /// (MinionMenu). Orders are saved with the run (SideAreas.Data["orders"]); minions' orders last as long as the minion.
    /// </summary>
    internal static class Commands
    {
        internal enum Order { Engage, Focus, Guard, Hold, Follow, StandDown, MoveTo, FallBack, Unleash }
        internal enum Group { Everyone, Crew, Minions }

        internal static readonly (Order order, string name, string tip)[] All =
        {
            (Order.Engage, "Engage", "Fight as they see fit."),
            (Order.Focus, "Focus", "Everyone on one enemy until it drops. Click, then click the enemy; the key uses the one under your crosshair, else the last one you hit."),
            (Order.Guard, "Guard", "Go after whoever last hurt you, and stay near you."),
            (Order.Hold, "Hold", "Stay put: fight whatever can be reached from where they stand."),
            (Order.Follow, "Follow", "Stay at your side and fight only from there."),
            (Order.StandDown, "Stand down", "Don't fight at all; just stay with you."),
            (Order.MoveTo, "Move here", "Go to a tile and hold it. Click, then click the floor; the key uses the tile you're aiming at (or a few steps ahead of you)."),
            (Order.FallBack, "Fall back", "Regroup beside you on the side away from the enemy, and only fight what comes adjacent."),
            (Order.Unleash, "Unleash", "Next turn, each uses its strongest ready special move on one enemy (click, then click it; the key uses your target), then carries on."),
        };

        internal static Group Current = Group.Everyone;

        internal static ConfigEntry<bool> ShowBar, CommandMinions;
        internal static ConfigEntry<KeyboardShortcut>[] Keys;
        internal static ConfigEntry<KeyboardShortcut> GroupKey;
        internal static ConfigEntry<KeyCode> BarKey;

        private const string ShownPref = "SCCL_OrderBarShown";
        private static bool? barShown;
        /// <summary>The order strip is out of the way until you call it up (remembered between sessions).</summary>
        internal static bool BarShown
        {
            get => barShown ?? (barShown = PlayerPrefs.GetInt(ShownPref, 0) == 1).Value;
            set { barShown = value; PlayerPrefs.SetInt(ShownPref, value ? 1 : 0); PlayerPrefs.Save(); OrderBar.Refresh(); }
        }

        internal static void Bind(ConfigFile cfg)
        {
            ShowBar = cfg.Bind("Orders", "OrderBar", true, "Allow the order strip above the hotbar (called up with Order Bar Key while you have companions to order). Off: never show it; the order keys still work.");
            BarKey = cfg.Bind("Orders", "OrderBarKey", KeyCode.O, "Shows or hides the order strip. It starts hidden, so companions get on with their own AI until you want to give orders; the order keys (Alt+1..9) work either way.");
            CommandMinions = cfg.Bind("Orders", "CommandMinions", false, "Your summons and pets take orders too, so a minion build plays as tactics instead of on autopilot.");
            GroupKey = cfg.Bind("Orders", "GroupKey", new KeyboardShortcut(KeyCode.Alpha0, KeyCode.LeftAlt), "Switches who the order strip commands: everyone, your crew (and Conjoined bodies), or your minions.");
            Keys = All.Select((o, i) => cfg.Bind("Orders", o.name.Replace(" ", "") + "Key", new KeyboardShortcut(KeyCode.Alpha1 + i, KeyCode.LeftAlt),
                $"Gives every companion the order '{o.name}': {o.tip}")).ToArray();
            // the first defaults were Ctrl+number, which is also the game's action bar 2 (on by default): move them once
            if (PlayerPrefs.GetInt("SCCL_OrderKeysMoved", 0) == 0)
            {
                foreach (var e in Keys.Append(GroupKey))
                {
                    KeyboardShortcut v = e.Value;
                    if (v.Modifiers.SequenceEqual(new[] { KeyCode.LeftControl }) && ((KeyboardShortcut)e.DefaultValue).MainKey == v.MainKey)
                        e.Value = (KeyboardShortcut)e.DefaultValue;
                }
                PlayerPrefs.SetInt("SCCL_OrderKeysMoved", 1);
                PlayerPrefs.Save();
            }
        }

        // ---- who and what -------------------------------------------------------------------------------------

        internal static bool IsCrewLike(Character c) => Crew.IsCrew(c) || Conjoined.IsBody(c);

        /// <summary>Tests: minions take orders without touching the player's config.</summary>
        internal static bool ForceMinions;

        internal static bool MinionsOn => ForceMinions || (CommandMinions != null && CommandMinions.Value);

        /// <summary>Minions given an order one by one (their right-click menu): they take orders even with CommandMinions off.</summary>
        private static readonly HashSet<int> picked = new HashSet<int>();

        internal static bool IsMinion(Character c) =>
            c != null && (MinionsOn || picked.Contains(c.GetInstanceID())) && c != Player.instance && !c.GetIsDead() && !IsCrewLike(c)
            && c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Player && !c.HasTag("BOSS");

        internal static bool IsCompanion(Character c) => IsCrewLike(c) || IsMinion(c);

        /// <summary>Everyone you can order right now, in a group.</summary>
        internal static List<Character> Companions(Group g = Group.Everyone)
        {
            var list = new List<Character>();
            if (g != Group.Minions) list.AddRange(Crew.Npcs().Concat(Conjoined.Npcs()));
            if (g != Group.Crew && AI_Manager.instance != null) list.AddRange(AI_Manager.instance.GetPlayerMinions().Where(IsMinion));
            return list.Where(c => c != null && !c.GetIsDead()).Distinct().ToList();
        }

        /// <summary>A key that survives respawns: crew and bodies by id; minions by instance (they don't outlive the map anyway).</summary>
        internal static string KeyOf(Character c) =>
            Crew.IsCrew(c) ? "crew:" + c.GetTag(Crew.CrewTag) : Conjoined.IsBody(c) ? "body:" + c.GetTag(Conjoined.BodyTag) : IsMinion(c) ? "pet:" + c.GetInstanceID() : null;

        private static Dictionary<string, object> Saved
        {
            get
            {
                var data = SideAreas.Data;
                if (data == null) return null;
                if (!(data.TryGetValue("orders", out object v) && v is Dictionary<string, object> d)) { d = new Dictionary<string, object>(); data["orders"] = d; }
                return d;
            }
        }

        internal static Order OrderOf(string key) =>
            key != null && Saved != null && Saved.TryGetValue(key, out object v) && v is string s && Enum.TryParse(s, out Order o) ? o : Order.Engage;

        internal static Order OrderOf(Character c) => OrderOf(KeyOf(c));

        /// <summary>The focus target per companion (not saved: it's a moment in a fight).</summary>
        private static readonly Dictionary<string, Character> focus = new Dictionary<string, Character>();
        /// <summary>Move here: the tile each is headed for. Unleash: the target for the one big move.</summary>
        private static readonly Dictionary<string, NavNode> moveSpot = new Dictionary<string, NavNode>();
        private static readonly Dictionary<string, Character> unleash = new Dictionary<string, Character>();
        private static readonly Dictionary<string, int> unleashSince = new Dictionary<string, int>();

        /// <summary>The floor tile you're aiming at, else three steps ahead of you (as far as the floor goes).</summary>
        internal static NavNode AimedTile()
        {
            if (Game.TARGET_N != null && !Game.TARGET_N.Block_Move) return Game.TARGET_N;
            if (Player.instance == null || Player_Control.instance == null) return null;
            NavNode n = Player.instance.GetMover().GetCurrentNavNode(), best = null;
            for (int i = 0; i < 3 && n != null; i++)
            {
                n = Diagonal.Link(n, Player_Control.instance.transform.forward);
                if (n != null && !n.Block_Move && !n.BlockCheck() && !n.HasDoor()) best = n; else break;
            }
            return best;
        }

        private static Character lastHit, lastAttacker;
        private static int lastAttackTurn = -99;

        /// <summary>Who "your target" is: the hostile under your crosshair, else the last enemy you hit.</summary>
        internal static Character YourTarget()
        {
            Character c = Game.TARGET_C;
            if (Hostile(c)) return c;
            return Hostile(lastHit) ? lastHit : null;
        }

        internal static bool Hostile(Character c) =>
            c != null && !c.GetIsDead() && c != Player.instance && c.ai != null && c.ai.GetTeam() != AI.AI_TEAM.Player && c.ai.GetTeam() != AI.AI_TEAM.Passive;

        // ---- giving orders ------------------------------------------------------------------------------------

        internal static string GroupName(Group g) => g == Group.Everyone ? "everyone" : g == Group.Crew ? "crew" : "minions";

        /// <summary>Orders every companion in the current group; returns how many heard it.</summary>
        internal static int GiveAll(Order o) => GiveAll(o, Current);

        /// <summary>`target` and `spot` are what you clicked when picking in the world; without them, your target and the aimed tile.</summary>
        internal static int GiveAll(Order o, Group g, Character target = null, NavNode spot = null)
        {
            var list = Companions(g);
            if (list.Count == 0) { Game.LOG($"No {(g == Group.Everyone ? "companions" : GroupName(g))} with you to give orders to."); return 0; }
            bool needsTarget = o == Order.Focus || o == Order.Unleash;
            target = needsTarget ? target ?? YourTarget() : null;
            if (needsTarget && target == null) { Game.LOG($"{Name(o)} on what? Aim at an enemy, or hit one first."); AudioManager.PlayEvent("event:/UI/UI_Error"); return 0; }
            spot = o == Order.MoveTo ? spot ?? AimedTile() : null;
            if (o == Order.MoveTo && spot == null) { Game.LOG("Move where? Aim at the floor."); AudioManager.PlayEvent("event:/UI/UI_Error"); return 0; }
            foreach (Character c in list) Give(c, o, target, quiet: true, spot: spot);
            Game.LOG($"<color=#7FE0C8>Orders ({GroupName(g)}):</color> {Name(o)}{(target != null ? $" ({target.GetName()})" : "")}.");
            AudioManager.PlayEvent("event:/UI/UI_Click");
            return list.Count;
        }

        /// <summary>Orders one companion (by its saved key, so it also works for one that isn't on this map).</summary>
        internal static void Give(string key, Order o)
        {
            Character c = Companions().FirstOrDefault(x => KeyOf(x) == key);
            if (c != null) { Give(c, o, o == Order.Focus || o == Order.Unleash ? YourTarget() : null, quiet: false, spot: o == Order.MoveTo ? AimedTile() : null); return; }
            if (Saved != null && o != Order.Focus && o != Order.Unleash && o != Order.MoveTo) Saved[key] = o.ToString();
        }

        /// <summary>One companion, from its right-click menu. A minion joins the ones you command; back on Engage it's let go.</summary>
        internal static void GiveOne(Character c, Order o, Character target = null, NavNode spot = null)
        {
            if (c == null) return;
            bool pick = Pick(c);
            Give(c, o, o == Order.Focus || o == Order.Unleash ? target ?? YourTarget() : null, quiet: false, spot: o == Order.MoveTo ? spot ?? AimedTile() : null);
            if (pick && OrderOf(c) == Order.Engage && !unleash.ContainsKey(KeyOf(c)) && !errands.ContainsKey(KeyOf(c))) picked.Remove(c.GetInstanceID());
        }

        /// <summary>A minion you single out joins the ones you command (true when it wasn't already).</summary>
        private static bool Pick(Character c) => !IsCrewLike(c) && !MinionsOn && picked.Add(c.GetInstanceID());

        // ---- errands: one job somewhere (open, unlock or bash something, fetch an item), then back to the order ---

        internal class Errand
        {
            /// <summary>What it's doing, for the log and the menu ("Bash the door").</summary>
            internal string what;
            /// <summary>Where it goes to do it.</summary>
            internal NavNode spot;
            /// <summary>Close enough to do it from where it stands.</summary>
            internal Func<Character, bool> there;
            /// <summary>Done (or no longer possible): the errand ends before its turn.</summary>
            internal Func<bool> done;
            /// <summary>One go at it, as the companion's turn.</summary>
            internal Func<AI, Character, IEnumerator> act;
            /// <summary>Goes before it gives up (a bash or a lockpick can fail).</summary>
            internal int tries = 1;
            internal int since;
        }

        private static readonly Dictionary<string, Errand> errands = new Dictionary<string, Errand>();

        /// <summary>Out on a job for you (PetWake leaves it be).</summary>
        internal static bool OnErrand(Character c) { string k = KeyOf(c); return k != null && errands.ContainsKey(k); }

        internal static bool Send(Character c, Errand e)
        {
            if (c == null || e == null) return false;
            Pick(c);
            string key = KeyOf(c);
            if (key == null) return false;
            e.since = ProcHub.TurnCount;
            errands[key] = e;
            unleash.Remove(key);
            if (HUD.instance != null) HUD.instance.MakeFct($"<color=#7FE0C8>{e.what}</color>", c.GetHitFx().position, false);
            Game.LOG($"<color=#7FE0C8>{c.GetName()}:</color> {e.what}.");
            return true;
        }

        internal static string ErrandOf(Character c)
        {
            string key = KeyOf(c);
            return key != null && errands.TryGetValue(key, out Errand e) ? e.what : null;
        }

        /// <summary>The errand's part of the turn: walk there, have a go, or (done, hopeless) let the standing order take over.</summary>
        private static bool RunErrand(AI ai, Character c, string key, ref IEnumerator result)
        {
            if (!errands.TryGetValue(key, out Errand e)) return false;
            if (e.done()) { errands.Remove(key); return false; }
            if (e.there(c))
            {
                if (e.tries-- <= 0) { errands.Remove(key); Game.LOG($"<color=#7FE0C8>{c.GetName()}:</color> gives up ({e.what.ToLowerInvariant()})."); if (Trace) Plugin.Log.LogInfo($"Orders: {c.GetName()} gives up: {e.what}"); return false; }
                ai.SetTarget(null);
                result = e.act(ai, c);
                return true;
            }
            if (ProcHub.TurnCount - e.since > 15 || e.spot == null)
            {
                errands.Remove(key);
                Game.LOG($"<color=#7FE0C8>{c.GetName()}:</color> can't get there ({e.what.ToLowerInvariant()}).");
                if (Trace) Plugin.Log.LogInfo($"Orders: {c.GetName()} can't get there: {e.what}");
                return false;
            }
            result = WalkTo(ai, c, e.spot);
            return true;
        }

        internal static void Give(Character c, Order o, Character target, bool quiet, NavNode spot = null)
        {
            string key = KeyOf(c);
            if (key == null || Saved == null) return;
            if ((o == Order.Focus || o == Order.Unleash) && !Hostile(target)) { if (!quiet) Game.LOG($"{Name(o)} on what? Aim at an enemy, or hit one first."); return; }
            if (o == Order.MoveTo && spot == null) { if (!quiet) Game.LOG("Move where? Aim at the floor."); return; }
            if (o == Order.Unleash)
            {
                // a one-off on top of whatever they were doing
                unleash[key] = target;
                unleashSince[key] = ProcHub.TurnCount;
                if (c.ai != null) { c.ai.SetTarget(target); c.ai.ChangeState(AI.AI_STATE.Active); }
                if (HUD.instance != null) HUD.instance.MakeFct("<color=#FFD15C>Unleash</color>", c.GetHitFx().position, false);
                if (!quiet) Game.LOG($"<color=#7FE0C8>{c.GetName()}:</color> Unleash ({target.GetName()}).");
                return;
            }
            Saved[key] = o.ToString();
            errands.Remove(key); // a fresh order calls off the job it was on
            focus.Remove(key);
            moveSpot.Remove(key);
            if (o == Order.MoveTo) moveSpot[key] = spot;
            if (c.ai == null) return;
            c.ai.SetLockTarget(false, null);
            switch (o)
            {
                case Order.Focus:
                    focus[key] = target;
                    c.ai.SetTarget(target);
                    c.ai.SetLockTarget(true, target);
                    c.ai.ChangeState(AI.AI_STATE.Active);
                    break;
                case Order.StandDown:
                    c.ai.SetTarget(null);
                    break;
            }
            if (HUD.instance != null) HUD.instance.MakeFct($"<color=#7FE0C8>{Name(o)}</color>", c.GetHitFx().position, false);
            if (!quiet) Game.LOG($"<color=#7FE0C8>{c.GetName()}:</color> {Name(o)}{(target != null ? $" ({target.GetName()})" : "")}.");
        }

        internal static string Name(Order o) => All.First(x => x.order == o).name;

        // ---- carrying them out --------------------------------------------------------------------------------

        private static readonly Func<AI, NavNode, IEnumerator> pursue =
            AccessTools.MethodDelegate<Func<AI, NavNode, IEnumerator>>(AccessTools.Method(typeof(AI), "DoPursuit"));

        private static readonly Func<AI, Animator> animator =
            AccessTools.MethodDelegate<Func<AI, Animator>>(AccessTools.Method(typeof(AI), "GetAnimator"));

        private static IEnumerator Nothing() { yield break; }

        private static float ToYou(Character c) => Player.instance == null ? 0f : Vector3.Distance(c.transform.position, Player.instance.transform.position);

        /// <summary>A free tile beside you to walk to (or null when already close).</summary>
        private static NavNode BesideYou(Character c)
        {
            NavNode me = Player.instance?.GetMover()?.GetCurrentNavNode();
            if (me == null) return null;
            return me.GetLinks().Where(n => n != null && (!n.IsOccupied() || n == c.GetMover().GetCurrentNavNode()) && !n.BlockCheck())
                     .OrderBy(n => Vector3.Distance(n.transform.position, c.transform.position)).FirstOrDefault()
                   ?? Map.instance?.GetClosestNode(me, 2.5f, validNodesOnly: true);
        }

        /// <summary>Tests: log every walk back to you.</summary>
        internal static bool Trace;

        private static IEnumerator WalkToYou(AI ai, Character c) => WalkTo(ai, c, BesideYou(c));

        /// <summary>Fall back: the free tile beside you that's farthest from the nearest enemy.</summary>
        private static NavNode SafeBesideYou(Character c)
        {
            NavNode me = Player.instance?.GetMover()?.GetCurrentNavNode();
            if (me == null) return null;
            var foes = Contracts.AllCharacters().Where(Hostile).Select(x => x.transform.position).ToList();
            return me.GetLinks().Where(n => n != null && (!n.IsOccupied() || n == c.GetMover().GetCurrentNavNode()) && !n.BlockCheck() && !n.Block_Move)
                     .OrderByDescending(n => foes.Count == 0 ? 0f : foes.Min(f => Vector3.Distance(f, n.transform.position)))
                     .FirstOrDefault() ?? BesideYou(c);
        }

        private static IEnumerator WalkTo(AI ai, Character c, NavNode spot)
        {
            NavNode from = c.GetMover().GetCurrentNavNode();
            if (Trace) Plugin.Log.LogInfo($"Orders: {c.GetName()} walks to you: {ToYou(c):0.0} m, spot {(spot != null ? spot.name : "none")}, same room {(spot != null && from != null && spot.GetRoom() == from.GetRoom())}, canMove {c.FLAG_canMove}, rooted {c.FLAG_isRooted}");
            if (spot == null || spot == from) yield break;
            // our own walk is allowed past the "don't chase" rule below, so the game's animated step does the work
            walkingFor = ai;
            IEnumerator step;
            try { step = pursue(ai, spot); }
            finally { walkingFor = null; }
            yield return ai.StartCoroutine(step);
            Character_Mover mover = c.GetMover();
            if (!mover.isMoving && mover.GetCurrentNavNode() == from)
            {
                // the game's pursuit gave up (it only searches so far): take one step along our own path
                NavNode next = FirstStep(from, spot);
                if (Trace && next == null) Plugin.Log.LogInfo($"Orders: no walkable path from {from.name} to {spot.name}");
                if (next != null && mover.MoveCheck(from, next))
                {
                    // the same as the game's own step (AI.DoPursuit): face it, move, walk animation and footsteps
                    ai.SetFacing(next.transform.position);
                    mover.MoveTo(from, next);
                    if (c is Mob mob && !string.IsNullOrEmpty(mob.FMOD_move)) AudioManager.PlayEvent(mob.FMOD_move, c.transform.position);
                    animator(ai)?.SetBool("isMoving", true);
                }
            }
            if (Trace) Plugin.Log.LogInfo($"Orders: {c.GetName()} moved {(mover.isMoving || mover.GetCurrentNavNode() != from)}");
        }

        /// <summary>The first tile of the shortest walk over free floor from one tile to another (breadth-first).</summary>
        internal static NavNode FirstStep(NavNode from, NavNode to)
        {
            var came = new Dictionary<NavNode, NavNode> { [from] = null };
            var queue = new Queue<NavNode>();
            queue.Enqueue(from);
            while (queue.Count > 0 && came.Count < 2000)
            {
                NavNode n = queue.Dequeue();
                if (n == to)
                {
                    while (came[n] != null && came[n] != from) n = came[n];
                    return n == from ? null : n;
                }
                foreach (NavNode m in n.GetLinks())
                {
                    if (m == null || came.ContainsKey(m) || m.Block_Move || m.Block_Mob || m.CheckBlockedMovement_Enter(n)) continue;
                    if (m.HasDoor() && !m.GetDoor().isOpen) continue;
                    if (m != to && m.IsOccupied()) continue;
                    came[m] = n;
                    queue.Enqueue(m);
                }
            }
            return null;
        }

        /// <summary>The order decides the turn before the AI does: close the gap, stand down, or pick the target.</summary>
        [HarmonyPatch(typeof(AI), nameof(AI.TakeTurn))]
        private static class Patch_Turn
        {
            [HarmonyPriority(Priority.High)]
            private static bool Prefix(AI __instance, ref IEnumerator __result)
            {
                Character c = __instance.GetCharacter();
                if (c == null || c.GetIsDead() || !IsCompanion(c) || Player.instance == null) return true;
                string key = KeyOf(c);
                if (RunErrand(__instance, c, key, ref __result)) return false;
                // a big move that can't be made within three turns (no sight of the target) is called off
                if (unleash.ContainsKey(key) && unleashSince.TryGetValue(key, out int since) && ProcHub.TurnCount - since > 3) unleash.Remove(key);
                if (unleash.TryGetValue(key, out Character big) && Hostile(big))
                {
                    if (Trace) Plugin.Log.LogInfo($"Orders: {c.GetName()} turn with unleash pending, state {__instance.GetState()}, sees target {__instance.CanSee(big)}");
                    __instance.SetTarget(big);
                    __instance.SetLockTarget(true, big);
                    return true; // SelectAbility picks the big move this turn
                }
                unleash.Remove(key);
                switch (OrderOf(key))
                {
                    case Order.Focus:
                        if (!focus.TryGetValue(key, out Character t) || !Hostile(t))
                        {
                            // the target is down (or lost with a reload): back to their own judgement
                            if (Saved != null) Saved[key] = Order.Engage.ToString();
                            focus.Remove(key);
                            __instance.SetLockTarget(false, null);
                            return true;
                        }
                        if (__instance.GetTarget() != t) __instance.SetTarget(t);
                        __instance.SetLockTarget(true, t);
                        return true;
                    case Order.Guard:
                        if (Hostile(lastAttacker) && ProcHub.TurnCount - lastAttackTurn <= 6 && __instance.CanSee(lastAttacker))
                        {
                            __instance.SetTarget(lastAttacker);
                            __instance.SetLockTarget(true, lastAttacker);
                            if (__instance.GetState() != AI.AI_STATE.Active) __instance.ChangeState(AI.AI_STATE.Active);
                            return true;
                        }
                        __instance.SetLockTarget(false, null);
                        if (ToYou(c) > 3.5f) { __result = WalkToYou(__instance, c); return false; }
                        return true;
                    case Order.Follow:
                        if (ToYou(c) > 2.2f) { __result = WalkToYou(__instance, c); return false; }
                        return true;
                    case Order.StandDown:
                        __instance.SetTarget(null);
                        __instance.SetLockTarget(false, null);
                        __result = ToYou(c) > 2.2f ? WalkToYou(__instance, c) : Nothing();
                        return false;
                    case Order.MoveTo:
                        if (!moveSpot.TryGetValue(key, out NavNode spot) || spot == null) { if (Saved != null) Saved[key] = Order.Hold.ToString(); return true; }
                        if (c.GetMover().GetCurrentNavNode() != spot && !(spot.IsOccupied() && spot.GetLinks().Contains(c.GetMover().GetCurrentNavNode())))
                        {
                            __result = WalkTo(__instance, c, spot);
                            return false;
                        }
                        return true; // there: hold it
                    case Order.FallBack:
                        NavNode safe = SafeBesideYou(c);
                        if (safe != null && c.GetMover().GetCurrentNavNode() != safe) { __result = WalkTo(__instance, c, safe); return false; }
                        return true;
                    default:
                        return true;
                }
            }
        }

        /// <summary>The AI whose walk to its ordered spot is starting right now (exempt from the chase rule).</summary>
        private static AI walkingFor;

        /// <summary>Held companions (and, mid-fight, followers) don't chase: they fight from where they stand.</summary>
        private static bool Stay(AI ai)
        {
            Character c = ai.GetCharacter();
            if (c == null || !IsCompanion(c)) return false;
            Order o = OrderOf(c);
            return o == Order.Hold || o == Order.MoveTo || o == Order.FallBack || (o == Order.Follow && ToYou(c) >= 2.2f);
        }

        /// <summary>Unleash: this turn, the ready special with the longest cooldown (their biggest move).</summary>
        [HarmonyPatch(typeof(AI), "SelectAbility")]
        private static class Patch_Unleash
        {
            private static void Postfix(AI __instance)
            {
                Character c = __instance.GetCharacter();
                if (c == null || !IsCompanion(c)) return;
                string key = KeyOf(c);
                if (key == null || !unleash.TryGetValue(key, out Character big)) return;
                unleash.Remove(key);
                if (!Hostile(big)) return;
                Ability best = (__instance.GetAbilities() ?? new Ability[0])
                    .Where(a => a != null && a.cooldown > 0 && a.CanUse() && a.CanUseOn(big))
                    .OrderByDescending(a => a.cooldown).FirstOrDefault();
                if (Trace) Plugin.Log.LogInfo($"Orders: unleash {c.GetName()}: " + string.Join(", ", (__instance.GetAbilities() ?? new Ability[0]).Where(a => a != null)
                    .Select(a => $"{a.GetDisplayName()} cd {a.cooldown}/{a.cooldown_amount} use {a.CanUse()} on {a.CanUseOn(big)}")) + $" -> {best?.GetDisplayName() ?? "none"}");
                if (best != null)
                {
                    __instance.SetChosenAbility(best);
                    if (HUD.instance != null) HUD.instance.MakeFct($"<color=#FFD15C>{best.GetDisplayName()}!</color>", c.GetHitFx().position, false);
                }
                if (OrderOf(key) != Order.Focus) __instance.SetLockTarget(false, null);
            }
        }

        [HarmonyPatch(typeof(AI), "DoPursuit")]
        private static class Patch_Pursuit
        {
            private static bool Prefix(AI __instance, NavNode targetNode, ref IEnumerator __result)
            {
                if (__instance == walkingFor || !Stay(__instance)) return true;
                // following you back is fine; chasing anything else isn't
                NavNode me = Player.instance?.GetMover()?.GetCurrentNavNode();
                if (OrderOf(__instance.GetCharacter()) == Order.Follow && me != null && (targetNode == me || me.GetLinks().Contains(targetNode))) return true;
                __result = Nothing();
                return false;
            }
        }

        [HarmonyPatch(typeof(AI), "DoWander")]
        private static class Patch_Wander
        {
            private static bool Prefix(AI __instance, ref IEnumerator __result) { if (!Stay(__instance)) return true; __result = Nothing(); return false; }
        }

        [HarmonyPatch(typeof(AI), "DoManeuver")]
        private static class Patch_Maneuver
        {
            private static bool Prefix(AI __instance, ref IEnumerator __result)
            {
                Character c = __instance.GetCharacter();
                if (c == null || !IsCompanion(c) || OrderOf(c) != Order.Hold) return true;
                __result = Nothing();
                return false;
            }
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.OnDealDamage))]
        private static class Patch_LastHit
        {
            private static void Postfix(Character victim) { if (Hostile(victim)) lastHit = victim; }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.TakeDamage), new[] { typeof(Character), typeof(List<DmgObj>), typeof(string), typeof(Ability), typeof(float) })]
        private static class Patch_YouHurt
        {
            private static void Postfix(Character caster)
            {
                if (!Hostile(caster)) return;
                lastAttacker = caster;
                lastAttackTurn = ProcHub.TurnCount;
            }
        }

        internal static void Tick()
        {
            if (Keys == null || Player.instance == null || Game.Game_isLoading) return;
            for (int i = 0; i < Keys.Length; i++)
                if (Keys[i].Value.IsDown()) { GiveAll(All[i].order); OrderBar.Refresh(); }
            if (GroupKey.Value.IsDown()) NextGroup();
            if (Input.GetKeyDown(BarKey.Value) && UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject?.GetComponent<TMP_InputField>() == null)
            {
                BarShown = !BarShown;
                AudioManager.PlayEvent("event:/UI/UI_Click");
            }
        }

        internal static void NextGroup()
        {
            Current = Current == Group.Everyone ? Group.Crew : Current == Group.Crew && MinionsOn ? Group.Minions : Group.Everyone;
            Game.LOG($"<color=#7FE0C8>Orders now go to:</color> {GroupName(Current)}.");
            OrderBar.Refresh();
        }

        // ---- the strip above the hotbar -----------------------------------------------------------------------

        internal class OrderBar : MonoBehaviour
        {
            private static OrderBar bar;
            internal RectTransform hotbar;
            internal TMP_FontAsset font;
            private readonly List<(Order o, Image back)> buttons = new List<(Order, Image)>();
            /// <summary>Each button's label with its key, kept current when keys are rebound.</summary>
            private readonly List<(TextMeshProUGUI label, string name, int key)> keyLabels = new List<(TextMeshProUGUI, string, int)>();
            private float nextRefresh;
            private RectTransform row;
            private TextMeshProUGUI groupLabel;

            internal static void Refresh() { if (bar != null) bar.nextRefresh = 0f; }

            private void Build()
            {
                row = (RectTransform)transform;
                foreach (Transform t in row) Destroy(t.gameObject);
                buttons.Clear();
                keyLabels.Clear();
                float x = 0f;
                RectTransform gb = UiKit.Panel(row, "Group", new Vector2(104f, 34f), new Color(0.12f, 0.22f, 0.3f, 0.95f));
                gb.anchorMin = gb.anchorMax = gb.pivot = new Vector2(0f, 0f);
                gb.anchoredPosition = new Vector2(0f, 0f);
                var gbtn = gb.gameObject.AddComponent<Button>();
                gbtn.targetGraphic = gb.GetComponent<Image>();
                gbtn.onClick.AddListener(() => { if (MinionBar.CanCommand) NextGroup(); });
                groupLabel = UiKit.Label(gb, "", font, 16f);
                groupLabel.overflowMode = TextOverflowModes.Overflow;
                var gtrig = gb.gameObject.AddComponent<EventTrigger>();
                var genter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                genter.callback.AddListener(_ => Tooltip.instance?.Show_TinyTip("Who takes orders", $"Click (or {Keyboard.ShortcutName(GroupKey.Value)}) to switch between everyone, your crew and your minions.", null));
                var gexit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                gexit.callback.AddListener(_ => Tooltip.instance?.Hide());
                gtrig.triggers.Add(genter); gtrig.triggers.Add(gexit);
                x += 108f;
                for (int i = 0; i < All.Length; i++)
                {
                    var (o, name, tip) = All[i];
                    float w = name.Length > 7 ? 112f : name.Length > 6 ? 94f : 86f;
                    RectTransform b = UiKit.Panel(row, name, new Vector2(w, 34f), UiKit.ButtonColor);
                    b.anchorMin = b.anchorMax = b.pivot = new Vector2(0f, 0f);
                    b.anchoredPosition = new Vector2(x, 0f);
                    x += w + 4f;
                    var btn = b.gameObject.AddComponent<Button>();
                    btn.targetGraphic = b.GetComponent<Image>();
                    // Focus, Unleash and Move here by mouse: click the enemy or the tile next (the keys use your aim)
                    btn.onClick.AddListener(() => { if (MinionBar.CanCommand && !MinionMenu.StartPicking(o, null, Current)) GiveAll(o); Refresh(); });
                    var lbl = UiKit.Label(b, "", font, 17f);
                    keyLabels.Add((lbl, name, i));
                    lbl.overflowMode = TextOverflowModes.Overflow;
                    var trig = b.gameObject.AddComponent<EventTrigger>();
                    var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    enter.callback.AddListener(_ => Tooltip.instance?.Show_TinyTip(name, $"{tip}\n<color=#9FB7C9>{Keyboard.ShortcutName(Keys[i].Value)}</color>", null));
                    var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    exit.callback.AddListener(_ => Tooltip.instance?.Hide());
                    trig.triggers.Add(enter); trig.triggers.Add(exit);
                    buttons.Add((o, b.GetComponent<Image>()));
                }
                row.sizeDelta = new Vector2(x, 34f);
            }

            private void Awake() => bar = this;

            private void LateUpdate()
            {
                if (hotbar == null || hotbar.rect.width <= 0f) return;
                // centred on the screen, a little above the hotbar's top (clear of the pet strip on its right)
                var corners = new Vector3[4];
                hotbar.GetWorldCorners(corners);
                var rt = (RectTransform)transform;
                Canvas root = rt.GetComponentInParent<Canvas>()?.rootCanvas;
                if (root == null) return;
                var rootRt = (RectTransform)root.transform;
                Vector2 top = rootRt.InverseTransformPoint(corners[1]);
                float scale = rt.lossyScale.x / Mathf.Max(0.0001f, rootRt.lossyScale.x);
                float w = rt.sizeDelta.x * scale;
                transform.position = rootRt.TransformPoint(new Vector3(rootRt.rect.center.x - w / 2f, top.y + 64f * scale, 0f));
            }

            private void Update()
            {
                if (Time.unscaledTime < nextRefresh) return;
                nextRefresh = Time.unscaledTime + 0.3f;
                var list = ShowBar.Value && BarShown && Player.instance != null && !Game.Game_isLoading ? Companions() : new List<Character>();
                bool show = list.Count > 0;
                if (show && buttons.Count == 0) Build();
                foreach (Transform t in transform) t.gameObject.SetActive(show);
                if (!show) return;
                if (Current == Group.Minions && !MinionsOn) Current = Group.Everyone;
                if (groupLabel != null) groupLabel.text = $"<color=#9FB7C9>To:</color> {GroupName(Current)}";
                list = Companions(Current);
                // lit: the order everyone has (when they agree)
                var orders = list.Select(OrderOf).Distinct().ToList();
                foreach (var (o, back) in buttons) back.color = orders.Count == 1 && orders[0] == o ? UiKit.OnColor : UiKit.ButtonColor;
                foreach (var (label, name, key) in keyLabels)
                {
                    string text = $"{name} <size=70%><color=#9FB7C9>{Keyboard.ShortcutName(Keys[key].Value).Replace(" + ", "+")}</color></size>";
                    if (label != null && label.text != text) label.text = text;
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
                        var go = new GameObject("SCCL_OrderBar", typeof(RectTransform));
                        var rt = (RectTransform)go.transform;
                        rt.SetParent(row.parent, false);
                        go.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true; // placed by hand each frame: stay out of the hotbar's own layout, which otherwise grew by our height
                        rt.pivot = new Vector2(0f, 0f);
                        rt.sizeDelta = new Vector2(10f, 34f);
                        var ob = go.AddComponent<OrderBar>();
                        ob.hotbar = (RectTransform)__instance.hotbar.transform;
                        UiLook.Attach(rt, "OrderBar");
                        UiLook.Collapsible(rt, "Orders", UiLook.Corner.AboveLeft, true);
                        ob.font = __instance.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.font != null)?.font;
                    }
                    catch (Exception e) { Plugin.Log.LogError("Order bar: " + e); }
                }
            }
        }
    }
}
