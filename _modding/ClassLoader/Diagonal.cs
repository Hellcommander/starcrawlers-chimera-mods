using System;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Diagonal steps for the player. Hold forward or back together with a strafe key (W+Q, W+E, S+Q, S+E by default),
    /// use numpad 7 / 9 / 1 / 3 (with Num Lock on or off; 8 / 2 / 4 / 6 move straight and 5 waits), or push the controller stick diagonally. A diagonal step is a normal move to the tile
    /// across the corner, so it animates, occupies tiles and sets off what's on the floor like any step.
    ///
    /// No corner cutting: both tiles beside the diagonal must be open floor (no wall piece, no door) and not both
    /// occupied, and the tile you step onto can't be a door. A diagonal is √2 tiles long, so it costs √2 turns: each
    /// one adds 0.414 of a turn to a stride debt, and whenever that adds up to a whole turn, the enemies get an extra
    /// turn (you lose a moment). Over many steps that is exactly the extra distance, with no dice involved.
    /// </summary>
    internal static class Diagonal
    {
        internal static ConfigEntry<bool> Enabled, Numpad, TurnBy45;
        internal static ConfigEntry<float> ComboWindow;
        internal enum SideKeys { Strafe, Turn }
        internal static ConfigEntry<SideKeys> NumpadSides;

        private const float Extra = 0.41421356f;
        private static float debt;
        private static bool owesTurn;
        private static float freshSince = -1f;
        private static int pendF, pendR;

        internal static float Debt => debt;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Movement", "DiagonalSteps", true, "Step diagonally: forward/back + a strafe key together, numpad 7/9/1/3, or the stick on a diagonal. Costs 1.41 turns (the extra shows up as an occasional lost moment).");
            Numpad = cfg.Bind("Movement", "NumpadDiagonals", true, "Move with the number pad, Num Lock on or off: 8 forward, 2 back, 4 and 6 to the sides, 5 waits, and 7, 9, 1 and 3 step diagonally (forward-left, forward-right, back-left, back-right). With Num Lock off the keys arrive as the arrows, Home, Page Up, End and Page Down, so those keys move too.");
            NumpadSides = cfg.Bind("Movement", "NumpadSideKeys", SideKeys.Strafe, "What numpad 4 and 6 do: Strafe steps left/right, Turn turns 90 degrees.");
            TurnBy45 = cfg.Bind("Movement", "TurnBy45", false, "Turn 45 degrees at a time instead of 90 (needs DiagonalSteps). Facing a diagonal, forward and back step along it, the strafe keys step across it, walking into an enemy attacks it, and facing skills (Dash, Charge, Backflip, Vault, ...) run along the diagonal, all by the same corner rule.");
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

        // ---- 45° facing -----------------------------------------------------------------------------------------

        /// <summary>Is the player facing a diagonal (45° off the grid)?</summary>
        internal static bool FacingDiagonal(Player_Control pc) =>
            pc != null && Mathf.Abs(pc.transform.forward.x) > 0.38f && Mathf.Abs(pc.transform.forward.z) > 0.38f;

        /// <summary>A direction snapped to the eight grid directions (components -1, 0 or 1).</summary>
        internal static Vector3 Grid(Vector3 v)
        {
            v.y = 0f;
            float m = v.magnitude;
            if (m < 0.01f) return Vector3.zero;
            return new Vector3(Mathf.Abs(v.x) > 0.38f * m ? Mathf.Sign(v.x) : 0f, 0f, Mathf.Abs(v.z) > 0.38f * m ? Mathf.Sign(v.z) : 0f);
        }

        /// <summary>The tile across the corner in a diagonal grid direction, by the corner rule, whoever stands on it.</summary>
        internal static NavNode Across(NavNode from, Vector3 dir, out string why) =>
            Target(from, new Vector3(0f, 0f, dir.z), new Vector3(dir.x, 0f, 0f), out why);

        /// <summary>
        /// Moves forward/back (f) and right/left (r) relative to where you face, at any facing: the sum is snapped to the
        /// eight grid directions, so facing a diagonal W steps along it and W+E steps straight. bump: walking into an
        /// enemy attacks it, as the game's forward key does.
        /// </summary>
        internal static bool Go(Player_Control pc, int f, int r, bool bump)
        {
            Vector3 dir = Grid(pc.transform.forward * f + pc.transform.right * r);
            if (dir == Vector3.zero) return false;
            var tr = Traverse.Create(pc);
            NavNode here = pc.GetCurrentNavNode();
            if (here == null || tr.Field("isTurning").GetValue<bool>()) return true;
            if (dir.x != 0f && dir.z != 0f)
            {
                if (bump && Bump(pc, here, dir)) return true;
                return StepWorld(pc, new Vector3(0f, 0f, dir.z), new Vector3(dir.x, 0f, 0f)) || true;
            }
            if (bump && f == 1 && r == 0 && !FacingDiagonal(pc) && tr.Method("AttackMove", new[] { typeof(NavNode) }).GetValue<bool>(here)) return true;
            // MoveCheck swaps places with an ally in the way, as the game's own keys do
            if (!tr.Method("MoveCheck", new[] { typeof(NavNode), typeof(Vector3) }).GetValue<bool>(here, dir)) pc.Move(dir);
            return true;
        }

        /// <summary>Walking into an enemy across the corner attacks it (not around a wall corner).</summary>
        private static bool Bump(Player_Control pc, NavNode here, Vector3 dir)
        {
            NavNode a = Link(here, new Vector3(0f, 0f, dir.z)), b = Link(here, new Vector3(dir.x, 0f, 0f));
            NavNode there = Link(a, new Vector3(dir.x, 0f, 0f)) ?? Link(b, new Vector3(0f, 0f, dir.z));
            if (there == null || !there.IsOccupied()) return false;
            if (a == null || b == null || a.Block_Move || b.Block_Move || a.BlockCheck() || b.BlockCheck() || a.HasDoor() || b.HasDoor()) return false;
            Character c = there.GetOccupant();
            if (c == null || c.ai == null || c.ai.GetTeam() == AI.AI_TEAM.Passive || c.ai.GetTeam() == AI.AI_TEAM.Player || c.ai.isNeutral) return false;
            if (!Player_AbilityControl.instance.ability_autoAttack.CanUseOn(c)) return false;
            Game.TARGET_C = c;
            Game.instance.PlayerAction(PLAYER_ACTION.AutoAttack);
            return true;
        }

        /// <summary>Steps toward forward/back (f = +1/-1) and right/left (r = +1/-1) relative to where you face.</summary>
        internal static bool Step(Player_Control pc, int f, int r)
        {
            if (pc == null || Player.instance == null) return false;
            if (FacingDiagonal(pc)) return Go(pc, f, r, bump: false);
            return StepWorld(pc, pc.transform.forward * f, pc.transform.right * r);
        }

        /// <summary>A diagonal step across the corner between world directions a and b.</summary>
        private static bool StepWorld(Player_Control pc, Vector3 a, Vector3 b)
        {
            if (pc == null || Player.instance == null) return false;
            var tr = Traverse.Create(pc);
            if (tr.Field("isTurning").GetValue<bool>()) return false;
            Character_Mover mover = tr.Field("mover").GetValue<Character_Mover>();
            NavNode here = pc.GetCurrentNavNode();
            if (mover == null || here == null) return false;
            NavNode target = Target(here, a, b, out string why);
            if (target == null) { Blocked(why); return false; }
            if (Player.instance.FLAG_isRooted) { Game.instance.PlayerAction(PLAYER_ACTION.Wait); return true; }
            if (!mover.MoveCheck(here, target)) { if (!mover.isMoving && target.IsOccupied()) Blocked("someone is in the way"); return false; }
            mover.playerStartNode = here;
            mover.playerTargetNode = target;
            debt += Extra;
            if (debt >= 1f) { debt -= 1f; owesTurn = true; }
            Game.instance.PlayerAction(PLAYER_ACTION.Move);
            return true;
        }

        private static float blockedAt = -10f;
        private static string blockedWhy;
        internal static string LastBlocked => blockedWhy;

        /// <summary>Why a diagonal didn't step, over your head; once per press, not every frame the key is held.</summary>
        private static void Blocked(string why)
        {
            if (string.IsNullOrEmpty(why) || HUD.instance == null || Player.instance == null) return;
            if (why == blockedWhy && Time.unscaledTime - blockedAt < 0.8f) { blockedAt = Time.unscaledTime; return; }
            blockedAt = Time.unscaledTime;
            blockedWhy = why;
            HUD.instance.MakeFct($"<color=#9FB7C9>{why}</color>", Player.instance.GetHitFx().position, false);
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

        private static bool Key(KeyCode a, KeyCode b) => Input.GetKey(a) || Input.GetKey(b);

        /// <summary>Every key the numpad moves with, Num Lock on or off (Home/End/Page keys count only when they're free to step).</summary>
        internal static System.Collections.Generic.IEnumerable<KeyCode> NumpadKeys
        {
            get
            {
                foreach (KeyCode k in new[] { KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4, KeyCode.Keypad5, KeyCode.Keypad6,
                    KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.Clear }) yield return k;
                if (NavFree) foreach (KeyCode k in new[] { KeyCode.Home, KeyCode.End, KeyCode.PageUp, KeyCode.PageDown }) yield return k;
            }
        }

        /// <summary>Numpad 8/2/4/6/5 (arrows and Clear with Num Lock off), handled the way the game handles W/S/Q/E/A/D/Space.</summary>
        private static bool Straight(Player_Control pc)
        {
            if (Key(KeyCode.Keypad5, KeyCode.Clear)) { Game.instance.PlayerAction(PLAYER_ACTION.Wait); return true; }
            int f = 0, r = 0;
            if (Key(KeyCode.Keypad8, KeyCode.UpArrow)) f = 1;
            else if (Key(KeyCode.Keypad2, KeyCode.DownArrow)) f = -1;
            else if (Key(KeyCode.Keypad6, KeyCode.RightArrow)) r = 1;
            else if (Key(KeyCode.Keypad4, KeyCode.LeftArrow)) r = -1;
            else return false;
            if (r != 0 && NumpadSides.Value == SideKeys.Turn)
            {
                Traverse.Create(pc).Method("Turn", new[] { typeof(float) }).GetValue(r > 0 ? 90f : -90f);
                return true;
            }
            // the same as W / S / E / Q at any facing (forward walks into an enemy to attack it)
            Go(pc, f, r, bump: f == 1);
            return true;
        }

        [HarmonyPatch(typeof(Player_Control), "DoKeyboardMovement")]
        private static class Patch_Keyboard
        {
            private static bool Prefix(Player_Control __instance, bool __runOriginal)
            {
                if (!__runOriginal) return false; // another patch (combat clarity's spotted halt) stopped movement this frame
                if (Game.RESOLVING) return true;
                if (Enabled.Value && PayDebt()) return false;
                // straight numpad keys work whenever the game's own keys do, diagonals on or off
                if (Numpad.Value && Straight(__instance)) { freshSince = -1f; return false; }
                if (!Enabled.Value || Game.TARGETING) return true;
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
                bool single = false;
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
                        if (fresh) { freshSince = Time.unscaledTime; pendF = f; pendR = r; }
                        if (f == 0 && r == 0)
                        {
                            // a quick tap let go while it waited for a partner: still a move
                            int pf = pendF, pr = pendR;
                            pendF = pendR = 0;
                            if ((pf != 0 || pr != 0) && freshSince >= 0f && Time.unscaledTime - freshSince < ComboWindow.Value + 0.25f)
                            {
                                freshSince = -1f;
                                Go(__instance, pf, pr, bump: pf == 1 && pr == 0);
                                return false;
                            }
                            return true;
                        }
                        if (freshSince >= 0f && Time.unscaledTime - freshSince < ComboWindow.Value) return false;
                        pendF = pendR = 0;
                        // facing a diagonal the game's own keys would find no tile, so every move is ours; turns and waits stay the game's
                        if (f == 0 && r == 0 || !FacingDiagonal(__instance)) return true;
                        single = true;
                    }
                }
                freshSince = -1f;
                pendF = pendR = 0;
                if (single) { Go(__instance, f, r, bump: f == 1 && r == 0); return false; }
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
                int f = Mathf.Abs(v) >= 0.5f ? (v > 0f ? 1 : -1) : 0, r = Mathf.Abs(h) >= 0.5f ? (h > 0f ? 1 : -1) : 0;
                if (f == 0 && r == 0) return true;
                if (FacingDiagonal(__instance)) { Go(__instance, f, r, bump: f == 1 && r == 0); return false; }
                if (f == 0 || r == 0) return true;
                return !Step(__instance, f, r);
            }
        }

        // ---- turning by 45° ----------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Player_Control), "Turn")]
        private static class Patch_Turn
        {
            private static void Prefix(Player_Control __instance, ref float turn)
            {
                if (Mathf.Abs(turn) < 1f) return;
                bool on = TurnBy45.Value && Enabled.Value;
                // with it off, a diagonal facing (it was on a moment ago) turns back onto the grid
                if (on || FacingDiagonal(__instance)) turn = Mathf.Sign(turn) * 45f;
            }
        }

        /// <summary>The game's "tile in this direction" knows only the four sides; facing a diagonal, it finds the tile across the corner.</summary>
        [HarmonyPatch(typeof(Player_Control), nameof(Player_Control.GetNodeInDirection))]
        private static class Patch_NodeInDirection
        {
            private static bool Prefix(Player_Control __instance, Vector3 direction, ref NavNode __result)
            {
                if (Mathf.Abs(direction.x) < 0.5f || Mathf.Abs(direction.z) < 0.5f || !FacingDiagonal(__instance)) return true;
                NavNode here = __instance.GetCurrentNavNode();
                __result = here == null ? null : Across(here, Grid(direction), out _);
                return false;
            }
        }

        /// <summary>A tile's links, plus the tiles across its corners in the player's facing while it faces a diagonal.</summary>
        internal static System.Collections.Generic.List<NavNode> FacingLinks(NavNode n)
        {
            var l = n.GetLinks();
            Player_Control pc = Player_Control.instance;
            if (!FacingDiagonal(pc)) return l;
            Vector3 f = Grid(pc.transform.forward);
            foreach (Vector3 d in new[] { f, -f })
            {
                NavNode across = Across(n, d, out _);
                if (across != null && !l.Contains(across)) l.Add(across);
            }
            return l;
        }

        // Skills that run along your facing check each next tile is linked to the last; facing a diagonal, the tile
        // across the corner counts (by the corner rule), so they run along it instead of fizzling.
        [HarmonyPatch]
        private static class Patch_FacingSkills
        {
            internal static int Sites;
            private static readonly string[] Skills =
            {
                "PA_Dash", "PA_Backflip", "PA_Acrobatics_Backflip", "PA_Barbarian_RecklessCharge", "PA_BigGuns_RunGun", "PA_Blademaster_Lunge",
                "PA_Cyberninja_Dash", "PA_MUTATION_MassiveHorns", "PA_Shieldbearer_Charge", "PA_Smashmaster_BeatRush", "PA_Smuggler_Vault",
            };

            private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                System.Reflection.MethodInfo links = AccessTools.Method(typeof(NavNode), nameof(NavNode.GetLinks));
                foreach (string name in Skills)
                {
                    Type t = AccessTools.TypeByName(name);
                    if (t == null) continue;
                    var types = new System.Collections.Generic.List<Type> { t };
                    types.AddRange(t.GetNestedTypes(AccessTools.all));
                    foreach (Type x in types)
                        foreach (var m in x.GetMethods(AccessTools.all | System.Reflection.BindingFlags.DeclaredOnly))
                            if (!m.IsAbstract && m.GetMethodBody() != null && PatchProcessor.GetOriginalInstructions(m).Any(ci => ci.Calls(links))) yield return m;
                }
            }

            private static System.Collections.Generic.IEnumerable<CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<CodeInstruction> code)
            {
                System.Reflection.MethodInfo links = AccessTools.Method(typeof(NavNode), nameof(NavNode.GetLinks));
                System.Reflection.MethodInfo mine = AccessTools.Method(typeof(Diagonal), nameof(FacingLinks));
                foreach (CodeInstruction ci in code)
                {
                    if (ci.Calls(links)) { ci.opcode = System.Reflection.Emit.OpCodes.Call; ci.operand = mine; Sites++; }
                    yield return ci;
                }
            }
        }

        internal static int FacingSkillSites => Patch_FacingSkills.Sites;
    }
}
