using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Diagonal steps for the player. Hold forward or back together with a strafe key (W+Q, W+E, S+Q, S+E by default),
    /// use numpad 7 / 9 / 1 / 3 (with Num Lock on or off), or push the controller stick diagonally. A diagonal step is a normal move to the tile
    /// across the corner, so it animates, occupies tiles and sets off what's on the floor like any step.
    ///
    /// No corner cutting: both tiles beside the diagonal must be open floor (no wall piece, no door) and not both
    /// occupied, and the tile you step onto can't be a door. A diagonal is √2 tiles long, so it costs √2 turns: each
    /// one adds 0.414 of a turn to a stride debt, and whenever that adds up to a whole turn, the enemies get an extra
    /// turn (you lose a moment). Over many steps that is exactly the extra distance, with no dice involved.
    /// </summary>
    internal static class Diagonal
    {
        internal static ConfigEntry<bool> Enabled, Numpad;
        internal static ConfigEntry<float> ComboWindow;

        private const float Extra = 0.41421356f;
        private static float debt;
        private static bool owesTurn;
        private static float freshSince = -1f;

        internal static float Debt => debt;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Movement", "DiagonalSteps", true, "Step diagonally: forward/back + a strafe key together, numpad 7/9/1/3, or the stick on a diagonal. Costs 1.41 turns (the extra shows up as an occasional lost moment).");
            Numpad = cfg.Bind("Movement", "NumpadDiagonals", true, "Numpad 7, 9, 1 and 3 step diagonally (forward-left, forward-right, back-left, back-right), with Num Lock on or off. With it off they arrive as Home, Page Up, End and Page Down, so those keys step too.");
            ComboWindow = cfg.Bind("Movement", "ComboWindow", 0.06f, "Seconds a fresh forward/back or strafe press waits for its partner key before moving straight (so two keys pressed together make a diagonal).");
        }

        // ---- the grid -----------------------------------------------------------------------------------------

        /// <summary>A node's neighbour in a world direction: the game links each tile right, left, forward, back.</summary>
        internal static NavNode Link(NavNode n, Vector3 dir)
        {
            if (n == null) return null;
            var links = n.GetLinks();
            if (links == null || links.Count < 4) return null;
            dir = new Vector3(Mathf.Round(dir.x), 0f, Mathf.Round(dir.z));
            if (dir == Vector3.right) return links[0];
            if (dir == Vector3.left) return links[1];
            if (dir == Vector3.forward) return links[2];
            if (dir == Vector3.back) return links[3];
            return null;
        }

        private static bool Open(NavNode n, NavNode from) =>
            n != null && !n.HasDoor() && !n.Block_Move && !n.BlockCheck() && !n.CheckBlockedMovement_Enter(from);

        /// <summary>The tile across the corner, or null with the reason it can't be stepped to.</summary>
        internal static NavNode Target(NavNode from, Vector3 a, Vector3 b, out string why)
        {
            why = null;
            NavNode sideA = Link(from, a), sideB = Link(from, b);
            NavNode viaA = Link(sideA, b), viaB = Link(sideB, a);
            NavNode target = viaA ?? viaB;
            if (target == null || (viaA != null && viaB != null && viaA != viaB)) { why = "no tile there"; return null; }
            if (!Open(sideA, from) || !Open(sideB, from)) { why = "a corner is in the way"; return null; }
            if (sideA.IsOccupied() && sideB.IsOccupied()) { why = "no room to squeeze past"; return null; }
            if (target.HasDoor()) { why = "doors are taken straight on"; return null; }
            if (target.CheckBlockedMovement_Enter(sideA) && target.CheckBlockedMovement_Enter(sideB)) { why = "blocked"; return null; }
            return target;
        }

        // ---- stepping -----------------------------------------------------------------------------------------

        /// <summary>Steps toward forward/back (f = +1/-1) and right/left (r = +1/-1) relative to where you face.</summary>
        internal static bool Step(Player_Control pc, int f, int r)
        {
            if (pc == null || Player.instance == null) return false;
            var tr = Traverse.Create(pc);
            if (tr.Field("isTurning").GetValue<bool>()) return false;
            Character_Mover mover = tr.Field("mover").GetValue<Character_Mover>();
            NavNode here = pc.GetCurrentNavNode();
            if (mover == null || here == null) return false;
            Vector3 a = pc.transform.forward * f, b = pc.transform.right * r;
            NavNode target = Target(here, a, b, out string why);
            if (target == null) return false;
            if (Player.instance.FLAG_isRooted) { Game.instance.PlayerAction(PLAYER_ACTION.Wait); return true; }
            if (!mover.MoveCheck(here, target)) return false;
            mover.playerStartNode = here;
            mover.playerTargetNode = target;
            debt += Extra;
            if (debt >= 1f) { debt -= 1f; owesTurn = true; }
            Game.instance.PlayerAction(PLAYER_ACTION.Move);
            return true;
        }

        /// <summary>The stride debt came due: the next moment is spent catching up.</summary>
        private static bool PayDebt()
        {
            if (!owesTurn || Game.RESOLVING || Game.LOCK) return false;
            owesTurn = false;
            if (HUD.instance != null) HUD.instance.MakeFct("<color=#9FB7C9>catching up</color>", Player.instance.GetHitFx().position, false);
            Game.instance.PlayerAction(PLAYER_ACTION.Wait);
            return true;
        }

        /// <summary>Page keys need their combo, so alone they're free to step; with no combo set they stay page keys.</summary>
        private static bool NavFree => HotbarPages.Key2Combo.Value != KeyCode.None && !HotbarPages.Key2Held
            && !(Menu_Stash.instance_stash != null && Menu_Stash.instance_stash.GetIsShowing());

        private static bool Held(string key) => !string.IsNullOrEmpty(key) && Input.GetKey(key);
        private static bool Pressed(string key) => !string.IsNullOrEmpty(key) && Input.GetKeyDown(key);

        [HarmonyPatch(typeof(Player_Control), "DoKeyboardMovement")]
        private static class Patch_Keyboard
        {
            private static bool Prefix(Player_Control __instance, bool __runOriginal)
            {
                if (!__runOriginal) return false; // another patch (combat clarity's spotted halt) stopped movement this frame
                if (!Enabled.Value || Game.RESOLVING || Game.TARGETING) return true;
                if (PayDebt()) return false;
                int f = 0, r = 0;
                if (Numpad.Value)
                {
                    if (Input.GetKey(KeyCode.Keypad7)) { f = 1; r = -1; }
                    else if (Input.GetKey(KeyCode.Keypad9)) { f = 1; r = 1; }
                    else if (Input.GetKey(KeyCode.Keypad1)) { f = -1; r = -1; }
                    else if (Input.GetKey(KeyCode.Keypad3)) { f = -1; r = 1; }
                    else if (NavFree)
                    {
                        // Num Lock off: the same four keys arrive as Home, Page Up, End and Page Down
                        if (Input.GetKey(KeyCode.Home)) { f = 1; r = -1; }
                        else if (Input.GetKey(KeyCode.PageUp)) { f = 1; r = 1; }
                        else if (Input.GetKey(KeyCode.End)) { f = -1; r = -1; }
                        else if (Input.GetKey(KeyCode.PageDown)) { f = -1; r = 1; }
                    }
                }
                if (f == 0)
                {
                    bool fw = Held(__instance.MoveForward), bk = Held(__instance.MoveBackward);
                    bool sl = Held(__instance.StrafeLeft), sr = Held(__instance.StrafeRight);
                    f = fw && !bk ? 1 : bk && !fw ? -1 : 0;
                    r = sr && !sl ? 1 : sl && !sr ? -1 : 0;
                    if (f == 0 || r == 0)
                    {
                        // one key of a pair just went down: give its partner a moment to arrive
                        bool fresh = Pressed(__instance.MoveForward) || Pressed(__instance.MoveBackward) || Pressed(__instance.StrafeLeft) || Pressed(__instance.StrafeRight);
                        if (fresh) freshSince = Time.unscaledTime;
                        if ((f != 0 || r != 0) && freshSince >= 0f && Time.unscaledTime - freshSince < ComboWindow.Value) return false;
                        return true;
                    }
                }
                freshSince = -1f;
                return !Step(__instance, f, r);
            }
        }

        [HarmonyPatch(typeof(Player_Control), "DoControllerMovement")]
        private static class Patch_Controller
        {
            private static bool Prefix(Player_Control __instance, bool __runOriginal)
            {
                if (!__runOriginal) return false;
                if (!Enabled.Value || Game.RESOLVING || Game.LOCK) return true;
                if (PayDebt()) return false;
                // Keybindings.input is a Rewired player; read it without referencing Rewired
                var input = Traverse.Create(typeof(Keybindings)).Field("input");
                float v = input.Method("GetAxis", new[] { typeof(string) }).GetValue<float>("Vertical");
                float h = input.Method("GetAxis", new[] { typeof(string) }).GetValue<float>("Horizontal");
                if (Mathf.Abs(v) < 0.5f || Mathf.Abs(h) < 0.5f) return true;
                return !Step(__instance, v > 0f ? 1 : -1, h > 0f ? 1 : -1);
            }
        }
    }
}
