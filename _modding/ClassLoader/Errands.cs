using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// Jobs a companion does for you, picked from its right-click menu (MinionMenu) and then clicked in the world:
    ///   Use    - open or close a door, open a container where it stands, fetch an item off the floor into your pack
    ///   Unlock - pick a door's or container's lock with one of your lockpicks: the game's own odds, with the better
    ///            of your and its Savvy and Recognition (and your Lockpopper); picks break as they do for you
    ///   Bash   - smash it open with its own Strength and Toughness, the roll the game's AI uses on doors; each miss
    ///            makes the next one likelier, and the noise draws attention as usual
    /// It walks over (Commands' errands), has a go as its turn (a few goes for Unlock and Bash), then carries on with
    /// its standing order. Quest objects and other contraptions stay yours to use.
    /// </summary>
    internal static class Errands
    {
        internal enum Job { Use, Unlock, Bash }

        internal static readonly (Job job, string name, string tip)[] All =
        {
            (Job.Use, "Use", "Then click a door (open or close it), a container (open it) or an item on the floor (fetch it into your pack)."),
            (Job.Unlock, "Unlock", "Then click a locked door or container: it picks the lock with one of your lockpicks, at your odds or its own, whichever is better."),
            (Job.Bash, "Bash", "Then click a locked door or container: it smashes it open with its own Strength and Toughness. Noisy."),
        };

        private static readonly Func<AI, Animator> animator =
            AccessTools.MethodDelegate<Func<AI, Animator>>(AccessTools.Method(typeof(AI), "GetAnimator"));

        /// <summary>A door, container or loose item under the screen point (the nearest thing the ray hits; your own
        /// companions standing in front don't count, so the bot by the crate doesn't hide it).</summary>
        internal static Component ThingAt(Vector2 screen, List<string> trace = null)
        {
            foreach (RaycastHit h in MinionMenu.Ray(screen, trace))
            {
                Character ch = h.collider.GetComponentInParent<Character>();
                if (ch == Player.instance || MinionMenu.Mine(ch)) continue;
                if (ch != null) return null;
                InteractiveObject io = h.collider.GetComponentInParent<InteractiveObject>();
                if (io != null) return io;
                Item item = h.collider.GetComponentInParent<Item>();
                if (item != null && !item.inPlayerInventory) return item;
                if (h.collider.isTrigger || h.collider.GetComponentInParent<NavNode>() != null) continue;
                return null;
            }
            return null;
        }

        internal static string NameOf(Component thing) =>
            thing is Door ? "the door" : thing is Container ? "the container" : thing is Item i ? i.GetName() : "that";

        internal static Item_Consumable Lockpick() => Menu_Inventory.instance?.GetItemWithTag("LOCKPICK") as Item_Consumable;

        /// <summary>The errand for a job on a thing, or why it can't be done.</summary>
        internal static string Plan(Character c, Job job, Component thing, out Commands.Errand e)
        {
            e = null;
            if (c == null || thing == null) return "There's nothing there to deal with.";
            NavNode from = c.GetMover()?.GetCurrentNavNode();
            if (from == null) return $"{c.GetName()} can't move.";
            string name = NameOf(thing);
            if (thing is Item item)
            {
                if (job != Job.Use) return $"There's nothing to {job.ToString().ToLowerInvariant()} on {name}.";
                if (!Menu_Inventory.TakeCheck(item)) return $"There's no room in your pack for {name}.";
                e = new Commands.Errand
                {
                    what = $"Fetch {name}",
                    // its own tile or any of the eight round it (diagonals are ~1.41 tiles off)
                    spot = NearestWhere(from, n => Flat(n.transform.position, item.transform.position) <= 1.6f),
                    there = x => item != null && Flat(x.transform.position, item.transform.position) <= 1.7f,
                    done = () => item == null || item.inPlayerInventory,
                    act = (ai, x) => Fetch(ai, x, item),
                };
                return e.spot == null ? $"{c.GetName()} can't get to {name}." : null;
            }
            if (!(thing is InteractiveObject io) || !(io is Door || io is Container)) return "Only you can use that.";
            Door door = io as Door;
            Func<NavNode, bool> reach;
            if (door != null) reach = n => door.node != null && door.node.GetLinks().Contains(n);
            else
            {
                float range = io.interactRange + (io.nodeSnapPoint != null ? Vector3.Distance(io.nodeSnapPoint.position, io.transform.position) : 0f);
                reach = n => Flat(n.transform.position, io.transform.position) <= range;
            }
            NavNode spot = NearestWhere(from, reach);
            if (spot == null) return $"{c.GetName()} can't get to {name}.";
            bool sealedShut = door != null && (door.isSealed || door.FLAG_isUberDoor);
            e = new Commands.Errand { spot = spot, there = x => reach(x.GetMover().GetCurrentNavNode()) };
            switch (job)
            {
                case Job.Use:
                    if (io.isLocked || sealedShut) { e = null; return $"{Cap(name)} is locked: Unlock or Bash it."; }
                    if (door != null)
                    {
                        if (door.isBroken) { e = null; return "That door is broken open."; }
                        bool wasOpen = door.isOpen;
                        e.what = wasOpen ? "Close the door" : "Open the door";
                        e.done = () => door == null || door.isOpen != wasOpen;
                        e.act = (ai, x) => UseDoor(ai, x, door, wasOpen);
                        e.tries = 2;
                    }
                    else
                    {
                        if (io.completed) { e = null; return $"{Cap(name)} has been opened already."; }
                        e.what = "Open the container";
                        e.done = () => io == null || io.completed;
                        e.act = (ai, x) => Open(ai, x, io);
                    }
                    return null;
                case Job.Unlock:
                    if (!io.isLocked) { e = null; return $"{Cap(name)} isn't locked."; }
                    if (sealedShut || io.lockpickMod <= -999) { e = null; return "That lock can't be picked."; }
                    if (Lockpick() == null) { e = null; return "You have no lockpicks to give it."; }
                    e.what = $"Pick the lock on {name}";
                    e.done = () => io == null || !io.isLocked || Lockpick() == null;
                    e.act = (ai, x) => PickLock(ai, x, io);
                    e.tries = 3;
                    return null;
                default:
                    if (!io.isLocked && (door == null || door.isOpen || door.isBroken)) { e = null; return door != null ? "That door isn't shut." : $"{Cap(name)} isn't locked."; }
                    if (io.bashMod <= -999 || (door != null && !door.CanBeBashed())) { e = null; return $"{Cap(name)} won't give."; }
                    e.what = $"Bash {name}";
                    e.done = () => io == null || (door != null ? door.isBroken || door.isOpen : !io.isLocked);
                    e.act = (ai, x) => Smash(ai, x, io);
                    e.tries = 4;
                    return null;
            }
        }

        private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        /// <summary>The closest free tile (by walking) that passes `ok`, over the same floor Commands walks on.</summary>
        private static NavNode NearestWhere(NavNode from, Func<NavNode, bool> ok)
        {
            if (ok(from)) return from;
            var seen = new HashSet<NavNode> { from };
            var queue = new Queue<NavNode>();
            queue.Enqueue(from);
            while (queue.Count > 0 && seen.Count < 2000)
            {
                NavNode n = queue.Dequeue();
                foreach (NavNode m in n.GetLinks())
                {
                    if (m == null || !seen.Add(m) || m.Block_Move || m.Block_Mob || m.CheckBlockedMovement_Enter(n)) continue;
                    if ((m.HasDoor() && !m.GetDoor().isOpen) || m.IsOccupied()) continue;
                    if (ok(m)) return m;
                    queue.Enqueue(m);
                }
            }
            return null;
        }

        private static void Say(Character c, string what, string fct = null)
        {
            Game.LOG($"<color=#7FE0C8>{c.GetName()}:</color> {what}");
            if (Commands.Trace) Plugin.Log.LogInfo($"Errands: {c.GetName()} {what}");
            if (fct != null && HUD.instance != null) HUD.instance.MakeFct(fct, c.GetHitFx().position, false);
        }

        private static void Face(AI ai, Component thing)
        {
            ai.SetFacing(thing.transform.position);
            ai.SetTarget(null);
        }

        private static IEnumerator Fetch(AI ai, Character c, Item item)
        {
            if (item == null || item.inPlayerInventory) yield break;
            Face(ai, item);
            string name = item.GetName();
            if (!Menu_Inventory.TakeCheck(item)) { Say(c, $"there's no room in your pack for {name}."); yield break; }
            Menu_Inventory.Take(item);
            AudioManager.PlayEvent("event:/UI/UI_Click");
            Say(c, $"fetches {name} for you.", $"<color=#9FD99F>{name}</color>");
            yield return new WaitForSeconds(0.2f);
        }

        private static IEnumerator UseDoor(AI ai, Character c, Door door, bool wasOpen)
        {
            if (door == null) yield break;
            Face(ai, door);
            if (wasOpen)
            {
                if (door.node.IsOccupied()) { Say(c, "something is standing in the doorway."); yield break; }
                door.Close(c);
            }
            else door.Open(c);
            Say(c, wasOpen ? "closes the door." : "opens the door.");
            yield return new WaitForSeconds(0.3f);
        }

        private static IEnumerator Open(AI ai, Character c, InteractiveObject io)
        {
            if (io == null) yield break;
            Face(ai, io);
            io.Interact();
            Say(c, "opens the container.");
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>Menu_Event's lockpicking, with the companion's hands (and your lockpick).</summary>
        private static IEnumerator PickLock(AI ai, Character c, InteractiveObject io)
        {
            Item_Consumable pick = Lockpick();
            if (io == null || pick == null) yield break;
            Face(ai, io);
            Player p = Player.instance;
            int diff = Mathf.Max(1, Game.DIFFICULTY);
            int savvy = Mathf.Max(p.GetStat(STAT_TYPE.Savvy), c.GetStat(STAT_TYPE.Savvy));
            int recog = Mathf.Max(p.GetStat(STAT_TYPE.Recognition), c.GetStat(STAT_TYPE.Recognition));
            int popper = 0;
            if (Player_AbilityControl.instance.HasAbility("PA_PASSIVE_Security_Lockpopper"))
                popper = Mathf.Max(0, Player_AbilityControl.instance.GetAbility("PA_PASSIVE_Security_Lockpopper").CalculateHitChance(null));
            int chance = Mathf.Clamp(40 + io.lockpickMod + savvy / diff + recog / diff, 0, 100) + popper;
            Vector3 at = io.GetLookPoint().position;
            int breaks;
            if (Random.Range(1, 101) <= chance)
            {
                AudioManager.PlayEvent("event:/Environment/Lockpick_Success", at);
                io.Unlock();
                Say(c, $"picks the lock on {NameOf(io)}.", "<color=#9FD99F>Unlocked</color>");
                breaks = 50 - (savvy + recog) - popper;
            }
            else
            {
                AudioManager.PlayEvent("event:/Environment/Lockpick_Fail", at);
                Say(c, $"fails to pick the lock on {NameOf(io)}.", "<color=#FF7272>Lock holds</color>");
                int d2 = Mathf.Max(1, Game.DIFFICULTY * 2);
                breaks = 150 - (savvy / d2 + recog / d2) - popper;
            }
            if (breaks > 0 && Random.Range(1, 101) <= breaks)
            {
                AudioManager.PlayEvent("event:/Environment/Lockpick_Break", at);
                Say(c, "a lockpick breaks.");
                pick.UseComplete();
            }
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>Doors: the game's own AI bash (AI.iAttemptBash). Containers: the same odds, Strength + Toughness on bashMod.</summary>
        private static IEnumerator Smash(AI ai, Character c, InteractiveObject io)
        {
            if (io == null) yield break;
            Face(ai, io);
            // iAttemptBash plays its swing on AI.anim, which is only filled in by GetAnimator: no animator, same roll by hand below
            if (io is Door door && animator(ai) != null)
            {
                yield return ai.iAttemptBash(door);
                if (door.isBroken) Say(c, $"smashes {NameOf(io)} open.", "<color=#9FD99F>Smashed</color>");
                else Say(c, $"bashes {NameOf(io)}, but it holds.", "<color=#FF7272>It holds</color>");
                yield return new WaitForSeconds(0.4f);
                yield break;
            }
            animator(ai)?.Play("Melee");
            AudioManager.PlayEvent("event:/Environment/Door_Office_Bash", io.transform.position);
            int muscle = c.GetStat(STAT_TYPE.Strength) + c.GetStat(STAT_TYPE.Toughness);
            if (Random.Range(1, 101) <= Mathf.Clamp(io.bashMod + muscle, 0, 100))
            {
                io.Bashed();
                io.Break(c);
                Say(c, $"smashes {NameOf(io)} open.", "<color=#9FD99F>Smashed</color>");
            }
            else
            {
                io.Bashed();
                io.bashMod += muscle / 2;
                Say(c, $"bashes {NameOf(io)}, but it holds.", "<color=#FF7272>It holds</color>");
            }
            Ability.AlertInRadius(io.transform.position, 3.5f);
            yield return new WaitForSeconds(0.4f);
        }
    }
}
