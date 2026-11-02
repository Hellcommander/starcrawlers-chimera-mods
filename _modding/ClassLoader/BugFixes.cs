using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Vanilla bugs the event bus stress runs turned up. None of them needs the bus; [Fixes] in the config file turns each
    /// one off. SCCL_DEVTEST=bugfixes shows each bug with its switch off and gone with it on.
    /// <list type="bullet">
    /// <item>An effect that ended during a walk over a character's effects took its place in the list from the next one,
    /// which then got skipped: it missed a turn's countdown, or sat out a hit it should have changed.</item>
    /// <item>Warp Jump waits while it travels. An enemy killed and destroyed in that wait, or a target destroyed in it,
    /// threw an error halfway through the jump.</item>
    /// <item>The player's damage path never read the IMMORTAL tag, so a poison ticking through the end of the Cerberus
    /// fight, whose cutscene relies on that tag, could still kill you after you had won.</item>
    /// <item>An event that spawns a mob threw an error when no tile near it was free, and the event stopped there.</item>
    /// <item>Levelling past the end of the xp table (100) threw inside the level-up, which skipped the rest of it.</item>
    /// <item>Looking an item up by name threw on a destroyed item still in the bag's list.</item>
    /// <item>Aiming at a character with no skill selected threw; the item and tile versions of that call check for it.</item>
    /// <item>Arm Cannon's and Thunderclap's Ready effect assumed it sits on a player who has that skill, so giving it to
    /// anyone else, or losing the skill while it runs, threw during the turn.</item>
    /// <item>The game has a flag for an enemy that already called for help, and nothing sets it (see <see cref="Alarm"/>).</item>
    /// <item>Destroyed quests and effects stayed in two lists for good, keeping the old floor in memory
    /// (see ZoneLoad.DropTheDead).</item>
    /// </list>
    /// </summary>
    internal static class BugFixes
    {
        internal static ConfigEntry<bool> EffectTurns, WarpMidCast, ImmortalPlayer, EventSpawn, LevelTable, DeadBagItems, TargetNoSkill, ArmCannon, AlarmOnce, ZoneLeftovers;

        internal static void Bind(ConfigFile cfg)
        {
            EffectTurns = cfg.Bind("Fixes", "EffectsMissNoTurn", true,
                "When one effect on a character ends, the next one in the list is no longer skipped (vanilla: it missed that turn's countdown, or that hit).");
            WarpMidCast = cfg.Bind("Fixes", "WarpJumpMidCast", true,
                "An enemy's Warp Jump survives its caster or target being destroyed while it travels (vanilla threw an error).");
            ImmortalPlayer = cfg.Bind("Fixes", "PlayerImmortalTag", true,
                "The player's health respects the game's IMMORTAL tag, as enemies' does. Damage over time can't kill you in the cutscene after beating Cerberus.");
            EventSpawn = cfg.Bind("Fixes", "EventSpawnWithNoRoom", true,
                "An event that spawns a mob looks further out when no tile near it is free, and goes on without the mob if none is (vanilla threw an error and the event stopped).");
            LevelTable = cfg.Bind("Fixes", "LevelPastTheTable", true,
                "Levels past 100 continue the xp table (vanilla threw an error in the level-up).");
            DeadBagItems = cfg.Bind("Fixes", "DeadBagItems", true,
                "Finding an item in your bag by name ignores destroyed items still in its list (vanilla threw an error).");
            TargetNoSkill = cfg.Bind("Fixes", "TargetWithNoSkill", true,
                "Aiming at a character with no skill selected no longer throws an error.");
            ArmCannon = cfg.Bind("Fixes", "ArmCannonWithoutSkill", true,
                "Arm Cannon's and Thunderclap's Ready effect can start and end on someone without the skill (vanilla threw an error during the turn).");
            AlarmOnce = cfg.Bind("Fixes", "EnemyAlarmOncePerTurn", true,
                "An enemy under fire answers an attacker and calls for help once a turn, not on every hit. On with EventBus whatever this says.");
            ZoneLeftovers = cfg.Bind("Fixes", "ZoneLeftovers", true,
                "Destroyed quests and effects leave the game's lists when a zone changes, so old floors don't stay in memory.");
        }

        // ---- walking a character's effects ------------------------------------------------------------------------

        private static readonly Stack<List<Effect>> spareLists = new Stack<List<Effect>>();

        /// <summary>Each effect once, in order, however the list changes on the way. One added on the way gets its visit, as in the game's loops.</summary>
        internal static void Each(Character_Effect_Control control, Action<Effect> visit)
        {
            List<Effect> all = control.effects;
            List<Effect> seen = spareLists.Count > 0 ? spareLists.Pop() : new List<Effect>();
            bool shifted = false;
            try
            {
                for (int i = 0; i < all.Count; i++)
                {
                    Effect e = all[i];
                    if (e == null || (shifted && seen.Contains(e))) continue;
                    seen.Add(e);
                    visit(e);
                    if (i < all.Count && all[i] == e) continue;
                    shifted = true;
                    i = -1;
                }
            }
            finally
            {
                seen.Clear();
                spareLists.Push(seen);
            }
        }

        private static bool Skip(Character_Effect_Control c) => !EffectTurns.Value || c.effects.Count == 0;

        [HarmonyPatch(typeof(Character_Effect_Control), nameof(Character_Effect_Control.TurnBegin))]
        private static class Patch_TurnBegin
        {
            private static bool Prefix(Character_Effect_Control __instance)
            {
                if (Skip(__instance)) return true;
                Each(__instance, e => e.TurnBegin());
                return false;
            }
        }

        [HarmonyPatch(typeof(Character_Effect_Control), nameof(Character_Effect_Control.TurnOver))]
        private static class Patch_TurnOver
        {
            private static bool Prefix(Character_Effect_Control __instance)
            {
                if (Skip(__instance)) return true;
                Each(__instance, e => e.TurnOver_Instant());
                return false;
            }
        }

        [HarmonyPatch(typeof(Character_Effect_Control), nameof(Character_Effect_Control.EndTurn))]
        private static class Patch_EndTurn
        {
            private static bool Prefix(Character_Effect_Control __instance)
            {
                if (Skip(__instance)) return true;
                Each(__instance, e => e.TurnOver_End());
                return false;
            }
        }

        [HarmonyPatch(typeof(Character_Effect_Control), nameof(Character_Effect_Control.OnGetHit))]
        private static class Patch_OnGetHit
        {
            private static bool Prefix(Character_Effect_Control __instance)
            {
                if (Skip(__instance)) return true;
                Each(__instance, e => e.OnGetHit());
                return false;
            }
        }

        [HarmonyPatch(typeof(Character_Effect_Control), nameof(Character_Effect_Control.IncomingRangedAttack))]
        private static class Patch_IncomingRanged
        {
            private static bool Prefix(Character_Effect_Control __instance, ref int __result)
            {
                if (Skip(__instance)) return true;
                int total = 0;
                Each(__instance, e => total += e.IncomingRangedAttack());
                __result = total;
                return false;
            }
        }

        [HarmonyPatch(typeof(Character_Effect_Control), nameof(Character_Effect_Control.CriticalCheck))]
        private static class Patch_CriticalCheck
        {
            private static bool Prefix(Character_Effect_Control __instance, Character attacker, Character victim, Item attackerWeapon)
            {
                if (Skip(__instance)) return true;
                Each(__instance, e => e.CriticalCheck(attacker, victim, attackerWeapon));
                return false;
            }
        }

        [HarmonyPatch(typeof(Character_Effect_Control), nameof(Character_Effect_Control.IncomingDamage))]
        private static class Patch_IncomingDamage
        {
            private static bool Prefix(Character_Effect_Control __instance, Character attacker, List<DmgObj> dmgList, string attackString, Ability dmgSource, float critDmgMod, ref List<DmgObj> __result)
            {
                if (Skip(__instance)) return true;
                List<DmgObj> list = dmgList;
                Each(__instance, e => { if (!e.bypass) list = e.OnTakeDamage(attacker, list, attackString, dmgSource, critDmgMod); });
                __result = list;
                return false;
            }
        }

        [HarmonyPatch(typeof(Character_Effect_Control), nameof(Character_Effect_Control.OnBlock))]
        private static class Patch_OnBlock
        {
            private static bool Prefix(Character_Effect_Control __instance, Character attacker, Ability dmgSource, List<DmgObj> dmgList, ref List<DmgObj> __result)
            {
                if (Skip(__instance) || (attacker != null && attacker == __instance.character)) return true;
                List<DmgObj> list = dmgList;
                Each(__instance, e => { if (!e.bypass) list = e.OnBlock(attacker, dmgSource, list); });
                __result = list;
                return false;
            }
        }

        [HarmonyPatch(typeof(Character_Effect_Control), nameof(Character_Effect_Control.OnDodge))]
        private static class Patch_OnDodge
        {
            private static bool Prefix(Character_Effect_Control __instance, Character attacker)
            {
                if (Skip(__instance) || (attacker != null && attacker == __instance.character)) return true;
                Each(__instance, e => { if (!e.bypass) e.OnDodge(attacker); });
                return false;
            }
        }

        [HarmonyPatch(typeof(Character_Effect_Control), nameof(Character_Effect_Control.OnDealDamage))]
        private static class Patch_OnDealDamage
        {
            private static bool Prefix(Character_Effect_Control __instance, Ability ability, Character victim, List<DmgObj> dmgList, Item_Weapon attackerWeapon, float critDmgMod, ref List<DmgObj> __result)
            {
                if (Skip(__instance)) return true;
                List<DmgObj> list = dmgList;
                Each(__instance, e => { if (!e.bypass) list = e.OnDealDamage(ability, victim, list, attackerWeapon, critDmgMod); });
                __result = list;
                return false;
            }
        }

        // ---- Warp Jump ------------------------------------------------------------------------------------------

        private static readonly AccessTools.FieldRef<Ability, Character> CasterOf = AccessTools.FieldRefAccess<Ability, Character>("caster");
        private static readonly AccessTools.FieldRef<Ability, Character> TargetOf = AccessTools.FieldRefAccess<Ability, Character>("target");
        private static readonly AccessTools.FieldRef<MA_Teleport, Ability_Mob> StrikeOf = AccessTools.FieldRefAccess<MA_Teleport, Ability_Mob>("blinkStrikeAbility");

        /// <summary>Runs first, so the error guard in StuckTurns stays the outer layer.</summary>
        [HarmonyPatch(typeof(MA_Teleport), nameof(MA_Teleport.Use))]
        private static class Patch_Warp
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(MA_Teleport __instance, ref IEnumerator __result)
            {
                if (WarpMidCast.Value && __result != null) __result = Warp(__result, __instance);
            }
        }

        /// <summary>
        /// Between steps of the jump: with the caster gone nothing is left to land. With the target gone the jump still ends
        /// on its tile, since the caster has already left the old one, and it looks at nothing and strikes no one.
        /// </summary>
        private static IEnumerator Warp(IEnumerator jump, MA_Teleport warp)
        {
            bool underway = false;
            while (true)
            {
                if (underway)
                {
                    Character caster = CasterOf(warp);
                    if (caster == null) yield break;
                    if (TargetOf(warp) == null)
                    {
                        TargetOf(warp) = caster;
                        StrikeOf(warp) = null;
                    }
                }
                if (!jump.MoveNext()) yield break;
                underway = true;
                yield return jump.Current;
            }
        }

        // ---- the IMMORTAL tag -----------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Player), nameof(Player.DamageHealth), new[] { typeof(int), typeof(Ability) })]
        private static class Patch_ImmortalPlayer
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Player __instance, int dmg)
            {
                if (!ImmortalPlayer.Value || __instance.GetHealth() - dmg > 0 || !__instance.HasTag("IMMORTAL")) return true;
                __instance.health = 1;
                Menu_HUD.instance?.Refresh();
                return false;
            }
        }

        // ---- events that spawn a mob --------------------------------------------------------------------------------

        private const float FurthestSpawn = 24f;

        private sealed class SpawnWas
        {
            public float radius;
            public Character mob;
            public bool rare;
        }

        private static int SpawnTiles(MiniEvent_SpecialEffect_Mob ev, float radius)
        {
            // the game's lookup assumes the spot is on a tile and throws if not, at any radius
            if (ev.spawnMobAt != null)
                return Map.instance.GetNavNodeAt(ev.spawnMobAt.position) == null ? 0 : Map.instance.GetNavNodesAt(ev.spawnMobAt.position, radius, validNodesOnly: true).Count;
            NavNode here = Player.instance.GetMover().GetCurrentNavNode();
            return here.GetRoom().GetMobSpawnNodesAt(here.transform.position, radius, ev.allowLinkedRooms).Count;
        }

        [HarmonyPatch(typeof(MiniEvent_SpecialEffect_Mob), nameof(MiniEvent_SpecialEffect_Mob.Trigger))]
        private static class Patch_EventSpawn
        {
            private static void Prefix(MiniEvent_SpecialEffect_Mob __instance, out SpawnWas __state)
            {
                __state = null;
                if (!EventSpawn.Value || (__instance.spawnMob == null && !__instance.spawnRare) || SpawnTiles(__instance, __instance.spawnRadius) > 0) return;
                __state = new SpawnWas { radius = __instance.spawnRadius, mob = __instance.spawnMob, rare = __instance.spawnRare };
                for (float r = Mathf.Max(1f, __instance.spawnRadius) * 2f; r <= FurthestSpawn; r *= 2f)
                {
                    if (SpawnTiles(__instance, r) == 0) continue;
                    __instance.spawnRadius = r;
                    return;
                }
                Plugin.Log.LogWarning($"Event spawn fix: {__instance.name} has no free tile within {FurthestSpawn}; going on without its mob.");
                __instance.spawnMob = null;
                __instance.spawnRare = false;
            }

            private static void Finalizer(MiniEvent_SpecialEffect_Mob __instance, SpawnWas __state)
            {
                if (__state == null) return;
                __instance.spawnRadius = __state.radius;
                __instance.spawnMob = __state.mob;
                __instance.spawnRare = __state.rare;
            }
        }

        // ---- the end of the xp table --------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Menu_Skills), nameof(Menu_Skills.Ding))]
        private static class Patch_LevelTable
        {
            private static void Prefix(Menu_Skills __instance)
            {
                int level = Player.instance.level, had = __instance.xpLevels.Length;
                if (!LevelTable.Value || level < had) return;
                Array.Resize(ref __instance.xpLevels, level + 1);
                for (int i = had; i <= level; i++) __instance.xpLevels[i] = 50 + 50 * i;
            }
        }

        // ---- destroyed items in the bag -----------------------------------------------------------------------------

        private static readonly Predicate<Item> Gone = i => i == null;

        [HarmonyPatch]
        private static class Patch_ItemsByName
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Menu_Inventory), nameof(Menu_Inventory.HasItemNamed));
                yield return AccessTools.Method(typeof(Menu_Inventory), nameof(Menu_Inventory.GetItemNamed));
                yield return AccessTools.Method(typeof(Menu_Inventory), nameof(Menu_Inventory.GetItemsNamed));
            }

            private static void Prefix(Menu_Inventory __instance)
            {
                if (DeadBagItems.Value) __instance.items.RemoveAll(Gone);
            }
        }

        // ---- aiming with no skill -----------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Game), nameof(Game.SetTarget), new[] { typeof(Character) })]
        private static class Patch_TargetNoSkill
        {
            private static bool Prefix(Character c)
            {
                if (!TargetNoSkill.Value || Game.GetPlayerAbility() != null) return true;
                if (Game.TARGET_C != null && Game.TARGET_C != c) Game.TARGET_C.SetOutline(flag: false, 0);
                Game.TARGET_C = c;
                return false;
            }
        }

        // ---- Arm Cannon's Ready effect ------------------------------------------------------------------------------

        private static readonly Action<Effect, Character> PlainInit = AccessTools.MethodDelegate<Action<Effect, Character>>(
            AccessTools.Method(typeof(Effect), nameof(Effect.Init)), null, false);
        private static readonly Action<Effect> PlainExpiry = AccessTools.MethodDelegate<Action<Effect>>(
            AccessTools.Method(typeof(Effect), "Expired_Duration"), null, false);

        /// <summary>What the effect goes on to call: the skill behind it, on the player it sits on.</summary>
        private static bool HasCannon(Effect_Prototype_ArmCannon_Ready ready, Character on) =>
            on != null && on == Player.instance && Player_AbilityControl.instance != null
            && Player_AbilityControl.instance.GetAbility(ready.isThunderclap ? "PA_Prototype_Thunderclap" : "PA_Prototype_ArmCannon") != null;

        [HarmonyPatch(typeof(Effect_Prototype_ArmCannon_Ready), nameof(Effect.Init))]
        private static class Patch_CannonInit
        {
            private static bool Prefix(Effect_Prototype_ArmCannon_Ready __instance, Character c)
            {
                if (!ArmCannon.Value || HasCannon(__instance, c)) return true;
                PlainInit(__instance, c);
                return false;
            }
        }

        [HarmonyPatch(typeof(Effect_Prototype_ArmCannon_Ready), "Expired_Duration")]
        private static class Patch_CannonExpiry
        {
            private static bool Prefix(Effect_Prototype_ArmCannon_Ready __instance)
            {
                if (!ArmCannon.Value || HasCannon(__instance, __instance.target)) return true;
                PlainExpiry(__instance);
                return false;
            }
        }
    }
}
