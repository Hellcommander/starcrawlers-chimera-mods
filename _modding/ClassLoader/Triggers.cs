using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// The extra proc triggers ("on") beyond hit / kill / hurt / dodge / turn / reaction: crit, miss, cast, move, wait,
    /// heal, lowHealth, allyDeath, effectGained, effectLost, combatStart, combatEnd, enterRoom, loot, levelUp,
    /// enemyDeath, allyKill / allyHurt (in ProcHub), summon, inflict, useItem, equip, unequip, search, newFloor, shieldBreak.
    /// Each one is a small patch that hands the event to ProcHub.
    /// </summary>
    internal static class Triggers
    {
        private static Room lastRoom;
        private static HashSet<string> lastEffects = new HashSet<string>();
        /// <summary>The allies you had last time we looked; null = not looked yet on this map (the next look only takes note).</summary>
        private static HashSet<Character> knownAllies;
        private static Player alliesOf;
        /// <summary>From the moment you act until your turn ends: what happens now is your doing (an effect your attack put on).</summary>
        internal static bool YourAction;
        /// <summary>Set while a mod action applies an effect; that action reports the inflict itself (stacks added count too).</summary>
        internal static bool ModApplying;

        /// <summary>Per player turn: the bookkeeping that ProcHub and conditions read, room changes and lost effects.</summary>
        internal static void Turn()
        {
            ProcHub.TurnCount++;
            Counters.Turn();
            Walls.Tick();
            Drifters.Tick();
            Horrors.Tick();
            Room room = Player.CURRENT_ROOM;
            if (room != null && room != lastRoom)
            {
                bool first = lastRoom == null;
                lastRoom = room;
                if (!first) ProcHub.Fire("enterroom", null);
            }
            CheckLostEffects();
            CheckNewAllies();
        }

        /// <summary>summon: an ally that wasn't with you last time (a summon, a pet, a charmed or hacked enemy, a hire).</summary>
        internal static void CheckNewAllies()
        {
            if (AI_Manager.instance == null || Player.instance == null) return;
            var now = new HashSet<Character>(AI_Manager.instance.GetPlayerMinions().Where(c => c != null && !c.GetIsDead()));
            bool fresh = knownAllies == null || alliesOf != Player.instance;
            var joined = fresh ? new List<Character>() : now.Where(c => !knownAllies.Contains(c)).ToList();
            knownAllies = now;
            alliesOf = Player.instance;
            foreach (Character c in joined) ProcHub.Fire("summon", c, null, c);
        }

        /// <summary>inflict: you put an effect on an enemy (your own effects only; a summon's don't count).</summary>
        internal static void Inflicted(Character caster, Character t, string id)
        {
            if (caster != Player.instance || t == null || t == Player.instance || t.ai == null || t.ai.GetTeam() == AI.AI_TEAM.Player || string.IsNullOrEmpty(id)) return;
            ProcHub.Fire("inflict", t, id);
        }

        internal static void CheckLostEffects()
        {
            Player p = Player.instance;
            if (p == null || p.effects == null) return;
            var now = new HashSet<string>(p.effects.GetEffects().Where(e => e != null && !string.IsNullOrEmpty(e.internalName)).Select(e => e.internalName));
            var gone = lastEffects.Where(id => !now.Contains(id)).ToList();
            lastEffects = now;
            foreach (string id in gone) ProcHub.Fire("effectlost", null, id);
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
        private static class Patch_Turn
        {
            private static void Prefix()
            {
                YourAction = false;
                try { Turn(); }
                catch (Exception e) { Plugin.Log.LogError("Triggers: " + e); }
            }
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.OnCrit))]
        private static class Patch_Crit
        {
            private static void Postfix(Character victim) { if (victim != null) ProcHub.Fire("crit", victim); }
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.OnMiss))]
        private static class Patch_Miss
        {
            private static void Postfix(Character victim) { if (victim != null) ProcHub.Fire("miss", victim); }
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.OnLootCredits))]
        private static class Patch_Loot
        {
            private static void Postfix(int amount) { if (amount > 0) ProcHub.Fire("loot", null, amount.ToString()); }
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.PlayerLevelUp))]
        private static class Patch_LevelUp
        {
            private static void Postfix() => ProcHub.Fire("levelup", null);
        }

        /// <summary>cast / wait: the action you just chose, before it resolves (move fires when the tile changes, below).</summary>
        [HarmonyPatch(typeof(Game), nameof(Game.PlayerAction))]
        private static class Patch_Action
        {
            private static void Prefix(PLAYER_ACTION action, bool __runOriginal)
            {
                if (!__runOriginal || Game.RESOLVING) return;
                YourAction = true;
                firedThisAction.Clear();
                // the Move action walks you right here (vanilla dashes walk you the same way, but from a skill)
                stepping = action == PLAYER_ACTION.Move;
                // a fresh game or load: take note of the allies you start with before this action can add one
                if (knownAllies == null || alliesOf != Player.instance) CheckNewAllies();
                switch (action)
                {
                    case PLAYER_ACTION.UseAbility:
                        Ability a = Game.PLAYER_ABILITY;
                        if (a == null || a.isPassive) return;
                        ProcHub.Fire("cast", Game.TARGET_C, $"{a.GetDisplayName()}|{a.internalName}");
                        break;
                    case PLAYER_ACTION.Wait: ProcHub.Fire("wait", null); break;
                }
            }

            private static void Finalizer() => stepping = false;
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Heal), new[] { typeof(int), typeof(bool), typeof(string) })]
        private static class Patch_Heal
        {
            private static void Prefix(Character __instance, out int __state) => __state = __instance != null ? __instance.health : 0;

            private static void Postfix(Character __instance, int __state)
            {
                if (__instance == Player.instance && __instance.health > __state) ProcHub.Fire("heal", null, (__instance.health - __state).ToString());
            }
        }

        /// <summary>lowHealth: your health crossing a threshold some proc listens for (once per crossing).</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.TakeDamage), new[] { typeof(Character), typeof(List<DmgObj>), typeof(string), typeof(Ability), typeof(float) })]
        private static class Patch_LowHealth
        {
            private static void Prefix(Player __instance, out int __state) => __state = __instance.health;

            private static void Postfix(Player __instance, Character caster, int __state)
            {
                if (__instance.health >= __state || __instance.GetIsDead()) return;
                float max = Mathf.Max(1, __instance.GetHealthMax());
                float before = 100f * __state / max, after = 100f * __instance.health / max;
                foreach (int t in ProcHub.LowHealthThresholds().Distinct().ToList())
                    if (before >= t && after < t) ProcHub.Fire("lowhealth", caster, t.ToString());
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_AllyDeath
        {
            private static void Prefix(Character __instance)
            {
                if (__instance == null || __instance == Player.instance || __instance.GetIsDead() || __instance.ai == null) return;
                if (__instance.ai.GetTeam() == AI.AI_TEAM.Player) ProcHub.Fire("allydeath", __instance);
            }
        }

        [HarmonyPatch(typeof(Character_Effect_Control), nameof(Character_Effect_Control.AddEffect))]
        private static class Patch_EffectGained
        {
            private static void Prefix(Character_Effect_Control __instance, Effect e, out bool __state) => __state = e != null && !__instance.effects.Contains(e);

            private static void Postfix(Character_Effect_Control __instance, Effect e, bool __state)
            {
                if (!__state || e == null || string.IsNullOrEmpty(e.internalName)) return;
                if (!__instance.isPlayer)
                {
                    // vanilla effects rarely name their caster, so one that lands while you act counts as yours
                    if (!ModApplying && (e.caster == Player.instance || e.caster == null && YourAction)) Inflicted(Player.instance, __instance.character, e.internalName);
                    return;
                }
                lastEffects.Add(e.internalName);
                ProcHub.Fire("effectgained", null, e.internalName);
            }
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.EnterCombat))]
        private static class Patch_CombatStart
        {
            private static void Prefix(LevelManager __instance, out bool __state) => __state = __instance.IsInCombat();
            private static void Postfix(LevelManager __instance, bool __state) { if (!__state && __instance.IsInCombat()) ProcHub.Fire("combatstart", null); }
        }

        [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.ExitCombat))]
        private static class Patch_CombatEnd
        {
            private static void Prefix(LevelManager __instance, out bool __state) => __state = __instance.IsInCombat();
            private static void Postfix(LevelManager __instance, bool __state) { if (__state && !__instance.IsInCombat()) ProcHub.Fire("combatend", null); }
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.MobDied))]
        private static class Patch_EnemyDeath
        {
            private static void Postfix(Character c)
            {
                if (c != null && c.ai != null && c.ai.GetTeam() != AI.AI_TEAM.Player) ProcHub.Fire("enemydeath", c);
            }
        }

        /// <summary>A new map: forget the old one's allies (pets come back as new characters) and anything still "yours".</summary>
        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.LevelChangeStart))]
        private static class Patch_LevelChangeStart
        {
            private static void Postfix() { YourAction = false; knownAllies = null; levelChanging = true; }
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.LevelChangeComplete))]
        private static class Patch_LevelChangeComplete
        {
            private static void Postfix()
            {
                levelChanging = false;
                try { CheckNewAllies(); }
                catch (Exception e) { Plugin.Log.LogError("Triggers: " + e); }
            }
        }

        // ---------------------------------------------------------------- move

        /// <summary>Set while the Move action walks you (a step or diagonal), so the tile change reads as a step.</summary>
        private static bool stepping;
        /// <summary>Between leaving one map and finishing the next: placing you there isn't a move.</summary>
        private static bool levelChanging;
        /// <summary>What a mod move of you is ("leap", "charge", "swap", "thrown"), set around it by the action doing it,
        /// so the tile change reads right; MoveOther is who it's about (the leap's target, who you swap with).</summary>
        internal static string MoveStyle;
        internal static Character MoveOther;
        /// <summary>charge / leap / teleport already fired by the action you're taking: a charge across five tiles is one charge.</summary>
        private static readonly HashSet<string> firedThisAction = new HashSet<string>();

        /// <summary>Runs a mod move under a style (an outer style wins: a charging chain's leaps are one charge).</summary>
        internal static void Styled(string style, Character other, Action move)
        {
            string prevStyle = MoveStyle;
            Character prevOther = MoveOther;
            MoveStyle = MoveStyle ?? style;
            MoveOther = MoveOther ?? other;
            try { move(); }
            finally { MoveStyle = prevStyle; MoveOther = prevOther; }
        }

        /// <summary>
        /// move: your tile changed. The detail says how: "step" (you walked), "ability" (your own skill, item or action
        /// moved you: a leap, dash, teleport or swap) or "forced" (something else did: a knockback, a pull, an enemy's swap).
        /// Then the kind of move, if it is one: charge, leap, teleport or swap.
        /// </summary>
        private static void Moved(string how, string style)
        {
            if (levelChanging || Game.Game_isLoading || Game.Game_isTransitioning) return;
            ProcHub.Fire("move", null, how);
            if (style == null || style == "thrown") return;
            if (style == "swap") { ProcHub.Fire("swap", MoveOther, "places"); return; }
            // once per action of yours; moves on someone else's turn (a teleport trap) each count
            if (YourAction && !firedThisAction.Add(style)) return;
            Character other = MoveOther ?? (YourAction && Game.TARGET_C != Player.instance ? Game.TARGET_C : null);
            ProcHub.Fire(style, style == "teleport" ? null : other);
        }

        /// <summary>The vanilla skills that don't read as what they are from how they move you.</summary>
        private static string SkillStyle()
        {
            Ability a = YourAction ? Game.PLAYER_ABILITY : null;
            if (a is PA_Barbarian_RecklessCharge || a is PA_Cyberninja_Dash || a is PA_Shieldbearer_Charge) return "charge";
            if (a is PA_Backflip || a is PA_Barbarian_FlyingSmash || a is PA_Smuggler_Vault) return "leap";
            return null;
        }

        /// <summary>Every walk starts here (yours, a dash or knockback, an ally's step); the tile is taken at once.</summary>
        [HarmonyPatch(typeof(Character_Mover), "StartMove")]
        private static class Patch_Walk
        {
            private static void Postfix(Character_Mover __instance, NavNode startNode, NavNode targetNode, Character ___character)
            {
                if (startNode == targetNode || targetNode == null || ___character == null) return;
                if (___character == Player.instance)
                {
                    if (stepping) { Moved("step", null); return; }
                    // a skill that walks you is a charge (dash, lunge, rush), or a leap if you jump
                    string style = YourAction ? MoveStyle ?? SkillStyle() ?? (__instance.isJumping ? "leap" : "charge") : MoveStyle;
                    Moved(YourAction ? "ability" : "forced", style);
                }
                // an ally's own effects: a summon that leaves something behind where it walks
                else if (___character.ai != null && ___character.ai.GetTeam() == AI.AI_TEAM.Player && !___character.GetIsDead())
                    ProcHub.FireOn(___character, "move", null, "step");
            }
        }

        /// <summary>Teleports, leaps and swaps set the tile outright.</summary>
        [HarmonyPatch(typeof(Character_Mover), nameof(Character_Mover.SetCurrentNavNode))]
        private static class Patch_Place
        {
            private static void Prefix(Character_Mover __instance, out NavNode __state) => __state = __instance.isPlayer ? __instance.currentNode : null;

            private static void Postfix(Character_Mover __instance, NavNode n, NavNode __state)
            {
                if (__state == null || n == null || n == __state || !__instance.isPlayer || Player.instance == null || __instance != Player.instance.GetMover()) return;
                // anything that sets you down somewhere else without a walk is a teleport (warps, pads, blink items)
                Moved(YourAction ? "ability" : "forced", MoveStyle ?? SkillStyle() ?? "teleport");
            }
        }

        // ---------------------------------------------------------------- knockback, weapon swap, chain

        /// <summary>knockback: you knock someone back and they really move.</summary>
        [HarmonyPatch(typeof(Ability), nameof(Ability.Knockback))]
        private static class Patch_Knockback
        {
            private static void Prefix(Character victim, out NavNode __state) => __state = victim?.GetMover()?.GetCurrentNavNode();

            private static void Postfix(Character victim, Character attacker, NavNode __state)
            {
                if (attacker != Player.instance || victim == null || __state == null || victim.GetMover()?.GetCurrentNavNode() == __state) return;
                ProcHub.Fire("knockback", victim);
            }
        }

        [HarmonyPatch(typeof(Menu_Player), nameof(Menu_Player.ChangeWeaponSet))]
        private static class Patch_WeaponSwap
        {
            private static void Prefix(Menu_Player __instance, out int __state) => __state = __instance.GetActiveWeaponSet();
            private static void Postfix(Menu_Player __instance, int __state) { if (__instance.GetActiveWeaponSet() != __state) ProcHub.Fire("swap", null, "weapon"); }
        }

        /// <summary>chain: a mod chain of yours jumps to its next link (hop 1 = the first jump after the target).</summary>
        internal static void ChainHop(Character caster, Character to, int hop)
        {
            if (caster == Player.instance && to != null && hop > 0) ProcHub.Fire("chain", to, hop.ToString());
        }

        /// <summary>Vanilla Chain Lightning: every strike past its first target is a jump.</summary>
        [HarmonyPatch(typeof(Ability), nameof(Ability.DealDamage), new[] { typeof(Character), typeof(Character), typeof(DmgObj), typeof(string), typeof(Ability), typeof(Item_Weapon) })]
        private static class Patch_ChainLightning
        {
            private static Ability cast;
            private static int hops;

            private static void Postfix(Ability __instance, Character attacker, Character victim)
            {
                if (!(__instance is PA_Elementalist_ChainLightning) || attacker != Player.instance || victim == null) return;
                if (victim == Game.TARGET_C) { cast = __instance; hops = 0; return; }
                if (cast != __instance) return;
                ProcHub.Fire("chain", victim, (++hops).ToString());
            }
        }

        /// <summary>newFloor: stairs that take you to another floor (side areas and the Apartment don't count).</summary>
        [HarmonyPatch(typeof(LevelManager), "DoStair")]
        private static class Patch_NewFloor
        {
            private static void Prefix(out int __state) => __state = Game.FLOOR;
            private static void Postfix(int __state) { if (Game.FLOOR != __state) ProcHub.Fire("newfloor", null, Game.FLOOR.ToString()); }
        }

        /// <summary>useItem: a consumable you used, just before it's spent ("match" = its name or id).</summary>
        [HarmonyPatch(typeof(Item_Consumable), nameof(Item_Consumable.UseComplete))]
        private static class Patch_UseItem
        {
            private static void Prefix(Item_Consumable __instance) => ProcHub.Fire("useitem", null, ItemDetail(__instance));
        }

        [HarmonyPatch(typeof(Item_Equipment), nameof(Item_Equipment.OnEquip))]
        private static class Patch_Equip
        {
            private static void Postfix(Item_Equipment __instance) => ProcHub.Fire("equip", null, ItemDetail(__instance));
        }

        [HarmonyPatch(typeof(Item_Equipment), nameof(Item_Equipment.OnUnequip))]
        private static class Patch_Unequip
        {
            private static void Postfix(Item_Equipment __instance) => ProcHub.Fire("unequip", null, ItemDetail(__instance));
        }

        private static string ItemDetail(Item i) => i == null ? null : $"{i.GetName(false)}|{i.itemId}";

        /// <summary>search: you open a container nobody searched yet (a mimic isn't one).</summary>
        [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
        private static class Patch_Search
        {
            private static void Prefix(Container __instance, out bool __state) => __state = !__instance.completed && !__instance.isMimic;
            private static void Postfix(bool __state) { if (__state) ProcHub.Fire("search", null); }
        }

        /// <summary>shieldBreak: a hit takes your shield from something to nothing.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.TakeDamage), new[] { typeof(Character), typeof(List<DmgObj>), typeof(string), typeof(Ability), typeof(float) })]
        private static class Patch_ShieldBreak
        {
            private static void Prefix(Player __instance, out int __state) => __state = __instance.shielding + __instance.oshielding;

            private static void Postfix(Player __instance, Character caster, int __state)
            {
                if (__state > 0 && __instance.shielding + __instance.oshielding <= 0 && !__instance.GetIsDead()) ProcHub.Fire("shieldbreak", caster);
            }
        }
    }
}
