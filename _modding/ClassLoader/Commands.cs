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
    ///   Kite       - keep their distance: fight, then step back from an enemy within two tiles
    ///   Shield     - stand beside you on the side of the nearest enemy; fight only from there
    ///   Flank      - go around one enemy to its far side from you, then attack it from there
    ///   Draw fire  - go to the enemy after you and taunt it onto themselves while they stay close
    ///   Scatter    - fight, then step away from anyone next to them, so one blast can't catch them all
    /// Given from the order strip above the hotbar (or Alt+1..9) to the group it shows (everyone, crew or minions;
    /// Alt+0 switches), to one crawler on the crew screen, or to any one companion from its right-click menu
    /// (MinionMenu). Orders are saved with the run (SideAreas.Data["orders"]); minions' orders last as long as the minion.
    /// </summary>
    internal static class Commands
    {
        internal enum Order { Engage, Focus, Guard, Hold, Follow, StandDown, MoveTo, FallBack, Unleash, Kite, Shield, Flank, DrawFire, Scatter }
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
            (Order.Kite, "Kite", "Keep their distance: they shoot or strike, then step back from any enemy within two tiles. They close in no nearer than about four."),
            (Order.Shield, "Shield", "Stand beside you between you and the nearest enemy, and fight only from there."),
            (Order.Flank, "Flank", "Go around one enemy to its far side from you, then attack it from there. Click, then click the enemy; the key uses your target."),
            (Order.DrawFire, "Draw fire", "Each goes to an enemy after you (one each while there are enough) and taunts it onto themselves for as long as they stay close."),
            (Order.Scatter, "Scatter", "After each attack they step away from anyone next to them, so one blast can't catch them all. In melee they spread round the enemy."),
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
            // Alt+1..9 for the first nine; the rest start unbound
            Keys = All.Select((o, i) => cfg.Bind("Orders", o.name.Replace(" ", "") + "Key", i < 9 ? new KeyboardShortcut(KeyCode.Alpha1 + i, KeyCode.LeftAlt) : KeyboardShortcut.Empty,
                $"Gives every companion the order '{o.name}': {o.tip}")).ToArray();
            // the first defaults were Ctrl+number, which is also the game's action bar 2 (on by default): move them once
            if (PlayerPrefs.GetInt("SCCL_OrderKeysMoved", 0) == 0)
            {
                foreach (var e in Keys.Append(GroupKey))
                {
                    KeyboardShortcut v = e.Value;
                    if (v.MainKey != KeyCode.None && v.Modifiers.SequenceEqual(new[] { KeyCode.LeftControl }) && ((KeyboardShortcut)e.DefaultValue).MainKey == v.MainKey)
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

        /// <summary>The focus (or flank) target per companion (not saved: it's a moment in a fight).</summary>
        private static readonly Dictionary<string, Character> focus = new Dictionary<string, Character>();
        /// <summary>Move here: the tile each is headed for. Unleash: the target for the one big move.</summary>
        private static readonly Dictionary<string, NavNode> moveSpot = new Dictionary<string, NavNode>();
        private static readonly Dictionary<string, Character> unleash = new Dictionary<string, Character>();
        private static readonly Dictionary<string, int> unleashSince = new Dictionary<string, int>();
        /// <summary>Draw fire: the enemy each one is pulling off you.</summary>
        private static readonly Dictionary<string, Character> decoy = new Dictionary<string, Character>();

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

        /// <summary>Orders aimed at one enemy: Focus, Unleash and Flank.</summary>
        internal static bool NeedsTarget(Order o) => o == Order.Focus || o == Order.Unleash || o == Order.Flank;

        internal static string GroupName(Group g) => g == Group.Everyone ? "everyone" : g == Group.Crew ? "crew" : "minions";

        /// <summary>Orders every companion in the current group; returns how many heard it.</summary>
        internal static int GiveAll(Order o) => GiveAll(o, Current);

        /// <summary>`target` and `spot` are what you clicked when picking in the world; without them, your target and the aimed tile.</summary>
        internal static int GiveAll(Order o, Group g, Character target = null, NavNode spot = null)
        {
            var list = Companions(g);
            if (list.Count == 0) { Game.LOG($"No {(g == Group.Everyone ? "companions" : GroupName(g))} with you to give orders to."); return 0; }
            bool needsTarget = NeedsTarget(o);
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
            if (c != null) { Give(c, o, NeedsTarget(o) ? YourTarget() : null, quiet: false, spot: o == Order.MoveTo ? AimedTile() : null); return; }
            if (Saved != null && !NeedsTarget(o) && o != Order.MoveTo) Saved[key] = o.ToString();
        }

        /// <summary>One companion, from its right-click menu. A minion joins the ones you command; back on Engage it's let go.</summary>
        internal static void GiveOne(Character c, Order o, Character target = null, NavNode spot = null)
        {
            if (c == null) return;
            bool pick = Pick(c);
            Give(c, o, NeedsTarget(o) ? target ?? YourTarget() : null, quiet: false, spot: o == Order.MoveTo ? spot ?? AimedTile() : null);
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
            EventBus.Mark(EventBus.Orders);
            if (NeedsTarget(o) && !Hostile(target)) { if (!quiet) Game.LOG($"{Name(o)} on what? Aim at an enemy, or hit one first."); return; }
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
            decoy.Remove(key);
            if (o == Order.MoveTo) moveSpot[key] = spot;
            if (c.ai == null) return;
            c.ai.SetLockTarget(false, null);
            switch (o)
            {
                case Order.Focus:
                case Order.Flank:
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

        /// <summary>Kite: step back from a foe this close (m), and don't chase one closer than KiteClose.</summary>
        private const float KiteKeep = 2.5f, KiteClose = 4.5f;
        /// <summary>Draw fire: close enough (m) for the enemy to be taunted onto it.</summary>
        private const float DecoyReach = 2.5f;

        private static IEnumerable<Character> SeenFoes(Character c) =>
            Contracts.AllCharacters().Where(f => Hostile(f) && (c.ai == null || c.ai.CanSee(f)));

        private static Character Nearest(Character c, out float dist)
        {
            dist = float.MaxValue;
            Character best = null;
            foreach (Character f in SeenFoes(c))
            {
                float d = Vector3.Distance(f.transform.position, c.transform.position);
                if (d < dist) { dist = d; best = f; }
            }
            return best;
        }

        private static bool Open(NavNode n, Character c) =>
            n != null && (!n.IsOccupied() || n == c.GetMover().GetCurrentNavNode()) && !n.BlockCheck() && !n.Block_Move && !n.HasDoor() && !Claimed(n, c);

        /// <summary>Where each companion is walking to, so two don't pick the same tile and end up sharing it.</summary>
        private static readonly Dictionary<Character, NavNode> heading = new Dictionary<Character, NavNode>();

        /// <summary>Another companion stands on it or is on its way there.</summary>
        private static bool Claimed(NavNode n, Character c)
        {
            foreach (var kv in heading.ToList())
            {
                Character o = kv.Key;
                if (o == null || o.GetIsDead() || kv.Value == null || o.GetMover()?.GetCurrentNavNode() == kv.Value) { heading.Remove(o); continue; }
                if (o != c && kv.Value == n) return true;
            }
            return TileStacks.On(n).Any(o => o != c && o != null && !o.GetIsDead() && o != Player.instance && TileStacks.IsAlly(o));
        }

        /// <summary>How far a tile is from the nearest foe it can see.</summary>
        private static float FoeRoom(Character c, NavNode n)
        {
            float best = float.MaxValue;
            foreach (Character f in SeenFoes(c)) best = Mathf.Min(best, Vector3.Distance(f.transform.position, n.transform.position));
            return best;
        }

        /// <summary>How far a tile is from you and the other companions (not counting itself).</summary>
        private static float AllyRoom(Character c, NavNode n)
        {
            float best = float.MaxValue;
            foreach (Character a in Companions().Append(Player.instance))
                if (a != null && a != c && !a.GetIsDead()) best = Mathf.Min(best, Vector3.Distance(a.transform.position, n.transform.position));
            return best;
        }

        /// <summary>The free tile beside it that scores best on `room`, if that's a real gain over staying put.</summary>
        private static NavNode Roomiest(Character c, Func<NavNode, float> room, Func<NavNode, bool> keep)
        {
            NavNode here = c.GetMover().GetCurrentNavNode();
            if (here == null) return null;
            NavNode best = here.GetLinks().Where(n => n != here && Open(n, c) && (keep == null || keep(n))).OrderByDescending(room).FirstOrDefault();
            return best != null && room(best) > room(here) + 0.2f ? best : null;
        }

        /// <summary>Shield: the free tile beside you nearest the closest enemy (null with none within 10 m).</summary>
        private static NavNode CoverBesideYou(Character c)
        {
            Player p = Player.instance;
            NavNode me = p?.GetMover()?.GetCurrentNavNode();
            if (me == null) return null;
            Character foe = Contracts.AllCharacters().Where(f => Hostile(f) && Vector3.Distance(f.transform.position, p.transform.position) <= 10f)
                .OrderBy(f => Vector3.Distance(f.transform.position, p.transform.position)).FirstOrDefault();
            if (foe == null) return null;
            return me.GetLinks().Where(n => Open(n, c)).OrderBy(n => Vector3.Distance(n.transform.position, foe.transform.position)).FirstOrDefault();
        }

        /// <summary>Flank: the free tile beside the enemy farthest from you, so it stands between you (its own tile when already there).</summary>
        private static NavNode FarSide(Character c, Character prey)
        {
            NavNode at = prey.GetMover()?.GetCurrentNavNode(), here = c.GetMover().GetCurrentNavNode();
            if (at == null || here == null) return null;
            Vector3 you = Player.instance.transform.position;
            float preyToYou = Vector3.Distance(prey.transform.position, you);
            if (at.GetLinks().Contains(here) && Vector3.Distance(here.transform.position, you) > preyToYou + 0.3f) return here;
            return at.GetLinks().Where(n => Open(n, c) && Vector3.Distance(n.transform.position, you) > preyToYou + 0.3f && FirstStep(here, n) != null)
                .OrderByDescending(n => Vector3.Distance(n.transform.position, you)).FirstOrDefault();
        }

        /// <summary>Draw fire: the free tile beside the enemy nearest to it that it can walk to (the enemy's own tile can't be walked onto).</summary>
        private static NavNode BesideFoe(Character c, Character foe)
        {
            NavNode at = foe.GetMover()?.GetCurrentNavNode(), here = c.GetMover().GetCurrentNavNode();
            if (at == null || here == null) return null;
            if (at.GetLinks().Contains(here)) return null;
            return at.GetLinks().Where(n => Open(n, c) && FirstStep(here, n) != null)
                .OrderBy(n => Vector3.Distance(n.transform.position, c.transform.position)).FirstOrDefault();
        }

        /// <summary>The open tile it can walk to that is nearest <paramref name="foe"/>, if that is a real step closer.</summary>
        private static NavNode Closest(Character c, Character foe)
        {
            NavNode here = c.GetMover().GetCurrentNavNode();
            if (here == null) return null;
            Vector3 at = foe.transform.position;
            float now = Vector3.Distance(here.transform.position, at);
            NavNode best = Reachable(here).Where(n => n != here && Open(n, c)).OrderBy(n => Vector3.Distance(n.transform.position, at)).FirstOrDefault();
            return best != null && Vector3.Distance(best.transform.position, at) < now - 0.5f ? best : null;
        }

        /// <summary>Every tile a companion could walk to from <paramref name="from"/>, by the same rules as <see cref="FirstStep"/>.</summary>
        private static List<NavNode> Reachable(NavNode from)
        {
            var seen = new HashSet<NavNode> { from };
            var queue = new Queue<NavNode>();
            queue.Enqueue(from);
            while (queue.Count > 0 && seen.Count < 2000)
                foreach (NavNode m in queue.Dequeue().GetLinks())
                {
                    if (m == null || seen.Contains(m) || !Passable(m, null)) continue;
                    seen.Add(m);
                    queue.Enqueue(m);
                }
            return seen.ToList();
        }

        /// <summary>Trace: what rules out each tile round an enemy.</summary>
        private static string Why(Character c, Character foe)
        {
            NavNode at = foe.GetMover()?.GetCurrentNavNode(), here = c.GetMover().GetCurrentNavNode();
            if (at == null || here == null) return "no tile";
            return string.Join(", ", at.GetLinks().Select(n => n == null ? "null" : $"{n.name}:" +
                (n == here ? "here" : n.IsOccupied() ? "occupied" : n.BlockCheck() ? "blocked" : n.Block_Move ? "no-move" : n.HasDoor() ? "door" : Claimed(n, c) ? "claimed" : FirstStep(here, n) == null ? "no path" : "ok")));
        }

        /// <summary>Draw fire: the enemy it's pulling, kept until it drops or gets far off; else one nobody else is pulling, the one after you first, then the nearest to you.</summary>
        private static Character PullTarget(Character c, string key)
        {
            // kept while it lives and stays near, even out of sight for a moment (round a corner, behind a crate)
            if (decoy.TryGetValue(key, out Character d) && Hostile(d) && Vector3.Distance(d.transform.position, c.transform.position) <= 12f) return d;
            Player p = Player.instance;
            // one each while there are enough to go round: an enemy another is already pulling comes last
            var taken = new HashSet<Character>(decoy.Where(kv => kv.Key != key && OrderOf(kv.Key) == Order.DrawFire && Companions().Any(x => KeyOf(x) == kv.Key)).Select(kv => kv.Value));
            // you see the ones after you, so they count even before it has seen them itself
            var after = Contracts.AllCharacters().Where(f => Hostile(f) && f.ai.GetTarget() == p && Vector3.Distance(f.transform.position, p.transform.position) <= 12f);
            d = SeenFoes(c).Union(after).OrderBy(f => taken.Contains(f) ? 1 : 0).ThenBy(f => f.ai.GetTarget() == p ? 0 : 1)
                .ThenBy(f => Vector3.Distance(f.transform.position, p.transform.position)).FirstOrDefault();
            if (d != null) decoy[key] = d; else decoy.Remove(key);
            return d;
        }

        private static IEnumerator WalkTo(AI ai, Character c, NavNode spot)
        {
            NavNode from = c.GetMover().GetCurrentNavNode();
            if (Trace) Plugin.Log.LogInfo($"Orders: {c.GetName()} walks ({ToYou(c):0.0} m from you) to spot {(spot != null ? spot.name : "none")}, same room {(spot != null && from != null && spot.GetRoom() == from.GetRoom())}, canMove {c.FLAG_canMove}, rooted {c.FLAG_isRooted}");
            if (spot == null || spot == from) yield break;
            heading[c] = spot;
            // our own walk is allowed past the "don't chase" rule below, so the game's animated step does the work
            walkingFor = ai;
            IEnumerator step;
            try { step = pursue(ai, spot); }
            finally { walkingFor = null; }
            yield return ai.StartCoroutine(step);
            Character_Mover mover = c.GetMover();
            // the game's pursuit spent this move catching up after diagonal steps: that is the move, not a failure
            if (!mover.isMoving && mover.GetCurrentNavNode() == from && !DiagonalAI.ConsumeCatchUp(c))
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
                    if (m == null || came.ContainsKey(m) || m.CheckBlockedMovement_Enter(n) || !Passable(m, to)) continue;
                    came[m] = n;
                    queue.Enqueue(m);
                }
            }
            return null;
        }

        /// <summary>A tile a companion walks through: open floor, no shut door, nobody on it (but the goal), no trap it knows of.</summary>
        private static bool Passable(NavNode m, NavNode to)
        {
            if (m.Block_Move || m.Block_Mob) return false;
            if (m.HasDoor() && !m.GetDoor().isOpen) return false;
            if (m != to && m.IsOccupied()) return false;
            return !(CompanionTraps.Avoid.Value && (CompanionTraps.VisibleTrap(m) != null || Traps.HurtsAllies(m)));
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
                    case Order.Shield:
                        {
                            NavNode cover = CoverBesideYou(c);
                            if (cover != null && c.GetMover().GetCurrentNavNode() != cover) { __result = WalkTo(__instance, c, cover); return false; }
                            if (cover == null && ToYou(c) > 2.2f) { __result = WalkToYou(__instance, c); return false; }
                            return true;
                        }
                    case Order.Flank:
                        {
                            if (!focus.TryGetValue(key, out Character prey) || !Hostile(prey))
                            {
                                if (Saved != null) Saved[key] = Order.Engage.ToString();
                                focus.Remove(key);
                                __instance.SetLockTarget(false, null);
                                return true;
                            }
                            __instance.SetTarget(prey);
                            __instance.SetLockTarget(true, prey);
                            if (__instance.GetState() != AI.AI_STATE.Active) __instance.ChangeState(AI.AI_STATE.Active);
                            // beside it already: strike, then slip round (Postfix); otherwise walk round to its far side
                            if (Vector3.Distance(c.transform.position, prey.transform.position) <= 1.6f) return true;
                            NavNode far = FarSide(c, prey);
                            if (far != null && far != c.GetMover().GetCurrentNavNode()) { __result = WalkTo(__instance, c, far); return false; }
                            return true; // no way round: attack
                        }
                    case Order.DrawFire:
                        {
                            Character foe = PullTarget(c, key);
                            if (Trace) Plugin.Log.LogInfo($"Orders: {c.GetName()} draws fire: {(foe != null ? $"{foe.GetName()} {Vector3.Distance(c.transform.position, foe.transform.position):0.0} m off, sees it {foe.ai.CanSee(c)}, taunted by {(Afflictions.TaunterOf(foe.ai)?.GetName() ?? "nobody")}" : "no enemy to pull")}");
                            if (foe == null) { if (ToYou(c) > 2.2f) { __result = WalkToYou(__instance, c); return false; } return true; }
                            __instance.SetTarget(foe);
                            __instance.SetLockTarget(true, foe);
                            if (__instance.GetState() != AI.AI_STATE.Active) __instance.ChangeState(AI.AI_STATE.Active);
                            if (Vector3.Distance(c.transform.position, foe.transform.position) <= DecoyReach && foe.ai.CanSee(c))
                            {
                                // close enough to be the one it fights: taunted for as long as it stays that way
                                bool fresh = Afflictions.TaunterOf(foe.ai) != c;
                                Afflictions.Taunt(new ActionSpec { @do = "taunt", turns = 2 }, new ActionContext { caster = c, sourceName = Name(Order.DrawFire) }, foe);
                                if (fresh && HUD.instance != null) HUD.instance.MakeFct("<color=#FF9F5C>Taunted</color>", foe.GetHitFx().position, false);
                                return true;
                            }
                            NavNode up = BesideFoe(c, foe);
                            if (Trace && up == null) Plugin.Log.LogInfo($"Orders: {c.GetName()} finds no tile beside {foe.GetName()}: {Why(c, foe)}");
                            if (up == null) up = Closest(c, foe); // no way to its side (you stand in the gap, say): as near as it gets
                            if (up == null) return true; // nowhere nearer: fight from here
                            __result = WalkTo(__instance, c, up);
                            return false;
                        }
                    default:
                        return true; // Kite and Scatter act first and move after (Postfix)
                }
            }

            /// <summary>Kite, Scatter and Flank: the turn as the AI plays it (a shot, a swing), then a step.</summary>
            private static void Postfix(AI __instance, ref IEnumerator __result)
            {
                Character c = __instance.GetCharacter();
                if (__result == null || c == null || c.GetIsDead() || !IsCompanion(c) || Player.instance == null) return;
                string key = KeyOf(c);
                Order o = OrderOf(key);
                if ((o == Order.Kite || o == Order.Scatter || o == Order.Flank) && !errands.ContainsKey(key)) __result = ThenStep(__instance, c, __result, o);
            }
        }

        private static IEnumerator ThenStep(AI ai, Character c, IEnumerator turn, Order o)
        {
            yield return turn;
            // a walk this turn still under way: let it land first
            for (float t = 0f; t < 1.5f && c != null && c.GetMover() != null && c.GetMover().isMoving; t += Time.deltaTime) yield return null;
            if (c == null || c.GetIsDead() || c.GetMover() == null || !c.FLAG_canMove || c.FLAG_isRooted) yield break;
            NavNode step = null;
            Character foe = Nearest(c, out float d);
            if (o == Order.Kite)
            {
                // shoot and scoot: back off anyone this close, up to two tiles (a sprint, so a charger doesn't just follow)
                if (foe != null && d <= KiteKeep) step = Roomiest(c, n => FoeRoom(c, n), null);
                if (step != null)
                {
                    yield return WalkTo(ai, c, step);
                    for (float t = 0f; t < 1.5f && c != null && c.GetMover() != null && c.GetMover().isMoving; t += Time.deltaTime) yield return null;
                    if (c == null || c.GetIsDead()) yield break;
                    step = Nearest(c, out d) != null && d <= KiteKeep ? Roomiest(c, n => FoeRoom(c, n), null) : null;
                }
            }
            else if (o == Order.Flank)
            {
                // after a strike, one step round it toward its far side, staying beside it
                Character prey = focus.TryGetValue(KeyOf(c), out Character f) && Hostile(f) ? f : null;
                NavNode here = c.GetMover().GetCurrentNavNode(), at = prey?.GetMover()?.GetCurrentNavNode();
                if (here != null && at != null && at.GetLinks().Contains(here))
                {
                    Vector3 you = Player.instance.transform.position;
                    float mine = Vector3.Distance(here.transform.position, you);
                    // tiles link straight, not across corners, so going round takes a step or two along the path there
                    NavNode far = mine <= Vector3.Distance(prey.transform.position, you) + 0.3f ? FarSide(c, prey) : null;
                    if (far != null && far != here) step = FirstStep(here, far);
                    if (Trace && step == null) Plugin.Log.LogInfo($"Orders: {c.GetName()} can't step round {prey.GetName()} (it {Vector3.Distance(prey.transform.position, you):0.0} m from you, this {mine:0.0}): {Why(c, prey)}");
                }
            }
            else if (AllyRoom(c, c.GetMover().GetCurrentNavNode()) < 1.6f)
            {
                // in melee it keeps its foe in reach and spreads round it; otherwise anywhere with room
                Func<NavNode, bool> keep = foe != null && d <= 1.6f ? (Func<NavNode, bool>)(n => Vector3.Distance(n.transform.position, foe.transform.position) <= 1.6f) : null;
                step = Roomiest(c, n => AllyRoom(c, n), keep);
            }
            if (Trace) Plugin.Log.LogInfo($"Orders: {c.GetName()} after its turn ({o}): nearest foe {(foe != null ? d.ToString("0.0") + " m" : "none")}, closest ally {AllyRoom(c, c.GetMover().GetCurrentNavNode()):0.0} m, step {(step != null ? step.name : "none")}");
            if (step != null) yield return WalkTo(ai, c, step);
        }

        /// <summary>The AI whose walk to its ordered spot is starting right now (exempt from the chase rule).</summary>
        private static AI walkingFor;

        /// <summary>Held companions (and, mid-fight, followers) don't chase: they fight from where they stand.</summary>
        private static bool Stay(AI ai)
        {
            Character c = ai.GetCharacter();
            if (c == null || !IsCompanion(c)) return false;
            Order o = OrderOf(c);
            return o == Order.Hold || o == Order.MoveTo || o == Order.FallBack || o == Order.Shield || (o == Order.Follow && ToYou(c) >= 2.2f)
                || (o == Order.Kite && Nearest(c, out float d) != null && d <= KiteClose);
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
                Order o = OrderOf(__instance.GetCharacter());
                if ((o == Order.Follow || o == Order.Shield) && me != null && (targetNode == me || me.GetLinks().Contains(targetNode))) return true;
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
            private EventBus.Watch watch;
            private bool wasShown;
            private RectTransform row;
            private TextMeshProUGUI groupLabel;

            internal static void Refresh() { EventBus.Mark(EventBus.Orders); if (bar != null) bar.nextRefresh = 0f; }

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
                // rows of seven, the first on top
                int rows = (All.Length + PerRow - 1) / PerRow;
                float widest = 0f;
                for (int i = 0; i < All.Length; i++)
                {
                    var (o, name, tip) = All[i];
                    if (i % PerRow == 0) x = 108f;
                    float w = name.Length > 7 ? 112f : name.Length > 6 ? 94f : 86f;
                    RectTransform b = UiKit.Panel(row, name, new Vector2(w, 34f), UiKit.ButtonColor);
                    b.anchorMin = b.anchorMax = b.pivot = new Vector2(0f, 0f);
                    b.anchoredPosition = new Vector2(x, (rows - 1 - i / PerRow) * 38f);
                    x += w + 4f;
                    widest = Mathf.Max(widest, x);
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
                row.sizeDelta = new Vector2(widest, rows * 38f - 4f);
                gb.anchoredPosition = new Vector2(0f, (rows - 1) * 38f);
            }

            private const int PerRow = 7;

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
                bool same = BarShown == wasShown;
                wasShown = BarShown;
                if (EventBus.Unchanged(ref watch, EventBus.Allies | EventBus.Orders | EventBus.Turn) && same) return;
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
                    KeyboardShortcut ks = Keys[key].Value;
                    string text = ks.MainKey == KeyCode.None ? name : $"{name} <size=70%><color=#9FB7C9>{Keyboard.ShortcutName(ks).Replace(" + ", "+")}</color></size>";
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
