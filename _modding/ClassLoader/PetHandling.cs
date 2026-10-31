using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Pet toggles. RaiseCap lifts the vanilla 3-pet limit. SwapWithPets lets you walk into one of your own pets and
    /// trade tiles with it: vanilla only asks the pet to step aside, so a pet with nowhere to go blocks corridors.
    /// </summary>
    internal static class PetHandling
    {
        internal static ConfigEntry<bool> RaiseCap;
        internal static ConfigEntry<int> Cap;
        internal static ConfigEntry<bool> SwapWithPets;
        internal static ConfigEntry<bool> GigamawEatsMinions;
        internal static ConfigEntry<bool> CommandBar;

        private static Character pendingPet;
        private static NavNode pendingNode;

        internal static void Bind(ConfigFile cfg)
        {
            RaiseCap = cfg.Bind("Minions", "RaiseCap", true, "Allow more pets than the game's 3 (the number is Cap). A mod's minionCap tweak overrides this.");
            Cap = cfg.Bind("Minions", "Cap", 6, "How many pets you can keep when RaiseCap is on.");
            SwapWithPets = cfg.Bind("Minions", "SwapWithPets", true, "Walking into one of your pets swaps places with it, even when it has nowhere to step aside to.");
            GigamawEatsMinions = cfg.Bind("Minions", "GigamawEatsMinions", true,
                "The BioLabs plant's lashers devour converted minions and organic (humanoid or monster) pets standing in reach, and each one counts toward feeding it. Off = they only get nicked for 5%, as in vanilla. Mechanical pets are never eaten.");
            CommandBar = cfg.Bind("Minions", "CommandBar", true,
                "A strip on top of the hotbar with a button per pet: click twice to unsummon it, right-click twice to break it down for the scrap it cost.");
        }

        internal static bool IsPet(Character c) =>
            c != null && c != Player.instance && !c.GetIsDead() && c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Player && !c.HasTag("BOSS");

        /// <summary>Won over from the enemy (Thought Leader and the like) rather than built or summoned.</summary>
        internal static bool IsConverted(Character c) => c.HasTag("THOUGHTLEADER_MINION") || c.ai.GetBaseTeam() != AI.AI_TEAM.Player;

        internal static bool IsEdible(Character c) =>
            IsPet(c) && (IsConverted(c) || c.characterType == CHARACTER_TYPE.Humanoid || c.characterType == CHARACTER_TYPE.Monster);

        // ---- unsummon and breakdown -----------------------------------------------------------------------------

        private const string PartsTag = "SCCL_PARTS|";

        /// <summary>The scrap a pet cost to build, if it was paid for with MECHPARTS: (item id, count).</summary>
        internal static (string id, int count) PartsOf(Character c)
        {
            Tag t = c?.tags?.Find(x => x != null && x.tagName != null && x.tagName.StartsWith(PartsTag) && x.tagAmount > 0);
            return t == null ? (null, 0) : (t.tagName.Substring(PartsTag.Length), t.tagAmount);
        }

        /// <summary>
        /// Sends a pet away without a fight (no loot, no kill procs). With keepRemains (the command bar's unsummon) you
        /// get something back: the scrap a built pet cost, otherwise its body as a corpse (summons whose corpses
        /// dissolve anyway just vanish). Without it (a skill's "dismiss") the pet is simply gone, unless broken down.
        /// </summary>
        internal static void Unsummon(Character t, string source, bool breakdown, bool keepRemains = false)
        {
            if (!IsPet(t) || AI_Manager.instance == null) return;
            var (id, n) = PartsOf(t);
            Actions.SpawnVfx("PA_Engineer_Boltron.PS_summon", t.GetHitFx().position);
            ModelParticles.Release(t.gameObject); // a wisp's particles fade and its death burst plays
            bool refund = n > 0 && (breakdown || keepRemains);
            bool corpse = keepRemains && !refund && !t.HasTag("NOCORPSE") && t.FLAG_corpseDissolveSpeed <= 0f;
            if (refund)
            {
                Crafting.Give(id, n);
                Game.LOG($"{source}: {t.GetName()} is broken down for {n}× {ItemRegistry.NameOf(id)}.");
            }
            else if (corpse) Game.LOG($"{source}: {t.GetName()} is released; its body stays where it stood.");
            else Game.LOG($"{source}: {t.GetName()} is recalled.");
            try { AI_Manager.instance.MinionDied(t); } // turrets and junkers tell their ability they're gone
            catch (System.Exception e) { Plugin.Log.LogWarning("Unsummon: " + e.Message); }
            AI_Manager.instance.RemoveCharacter(t);
            t.GetMover()?.GetCurrentNavNode()?.ClearOccupant();
            if (corpse)
            {
                t.xpValue = 0;
                t.AddTag("NOXP");
                try { t.Corpsify(); } // the save-load path: a corpse with no death, loot or kill procs
                catch (System.Exception e) { Plugin.Log.LogWarning("Unsummon corpse: " + e.Message); Object.Destroy(t.gameObject); }
            }
            else Object.Destroy(t.gameObject);
            AI_Manager.instance.RefreshPlayerMinionList();
        }

        private static int spentParts;
        private static string spentPartsId;
        private static float spentPartsAt;

        /// <summary>Scrap leaving your inventory; a pet registered right after is tagged with it so it can be refunded.</summary>
        [HarmonyPatch(typeof(Item_Consumable), nameof(Item_Consumable.ReduceStack))]
        private static class Patch_SpendParts
        {
            private static void Prefix(Item_Consumable __instance, int amount)
            {
                if (amount <= 0 || !__instance.inPlayerInventory || !__instance.HasTag("MECHPARTS")) return;
                if (Time.realtimeSinceStartup - spentPartsAt > 3f || spentPartsId != __instance.itemId) spentParts = 0;
                spentParts += Mathf.Min(amount, __instance.stack);
                spentPartsId = __instance.itemId;
                spentPartsAt = Time.realtimeSinceStartup;
            }
        }

        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.RegisterMinion))]
        private static class Patch_TagParts
        {
            private static void Postfix(Character minion)
            {
                if (minion == null || spentParts <= 0 || Time.realtimeSinceStartup - spentPartsAt > 3f) return;
                minion.SetTag(PartsTag + spentPartsId, spentParts);
                spentParts = 0;
            }
        }

        /// <summary>
        /// A contract's target won over for good (Command, Join Us...) kept its red skull on the map and could never
        /// finish the contract, since only its death counted. Taming it now counts as beating it, and the skull goes.
        /// </summary>
        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.RegisterMinion))]
        private static class Patch_TamedTarget
        {
            private static void Postfix(Character minion) => SettleTamed(minion);
        }

        /// <summary>A tamed contract target: the contract counts it as beaten, and its skull or nemesis mark goes.</summary>
        internal static void SettleTamed(Character minion)
        {
            if (minion == null || minion == Player.instance) return;
            Quest_Target qt = minion.questTarget;
            if (qt != null)
            {
                minion.questTarget = null; // its later death mustn't count a second time
                try { qt.Killed(); }
                catch (System.Exception e) { Plugin.Log.LogWarning("Tamed contract target: " + e.Message); }
                Object.Destroy(qt);
                Game.LOG($"<color=#FFD966>{minion.GetName()} is yours now; that settles the contract.</color>");
            }
            minion.RemoveTag("MINIMAP_NEMESIS_MARKER");
            minion.ClearMiniMapMarker();
        }

        /// <summary>
        /// A save with a contract target tamed before this fix: loading the contract put the skull back on it (and it
        /// still waited for a death that will never come). Settle it as the contract loads.
        /// </summary>
        [HarmonyPatch]
        private static class Patch_TamedOnLoad
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
                new[] { typeof(Quest_KillMob), typeof(Quest_KillMob_Multi) }.Select(t => (System.Reflection.MethodBase)AccessTools.DeclaredMethod(t, "Deserialize"));

            private static void Postfix(Quest __instance)
            {
                if (AI_Manager.instance == null) return;
                foreach (Character c in Contracts.AllCharacters().Where(c => c != null && !c.GetIsDead() && c.questTarget != null && c.questTarget.quest == __instance).ToList())
                    if (c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Player && (c.HasTag("MINION") || c.HasTag("HACKER_CHARM") || c.HasTag("THOUGHTLEADER_MINION")))
                        SettleTamed(c);
            }
        }

        // ---- Hacker Command keeps its old bot -----------------------------------------------------------------------

        /// <summary>Your turns so far this session (the fallback watch counts them).</summary>
        private static int turns;

        /// <summary>
        /// Command holds one bot. Its roll can succeed on a target that then throws the override off (a robot boss):
        /// vanilla had already let go of your old bot, which went back to the enemy, so a "failed" Command cost you
        /// it. Now the new bot is watched for two of your turns; if it sheds the charm while still alive, Command
        /// falls back to the old bot (if that one still lives) and the new one is let go.
        /// </summary>
        [HarmonyPatch(typeof(PA_Hacker_Command), nameof(PA_Hacker_Command.Use))]
        private static class Patch_CommandKeepsOld
        {
            private static void Postfix(PA_Hacker_Command __instance, ref IEnumerator __result) => __result = Watched(__instance, __result);
        }

        private static IEnumerator Watched(PA_Hacker_Command cmd, IEnumerator use)
        {
            Character old = cmd.victim != null && !cmd.victim.GetIsDead() && cmd.victim.effects.GetEffect("CHARM") != null ? cmd.victim : null;
            while (use.MoveNext()) yield return use.Current;
            Character fresh = cmd.victim;
            if (old != null && fresh != null && fresh != old) Plugin.Instance.StartCoroutine(WatchNewBot(cmd, old, fresh));
        }

        private static bool Holds(Character c) =>
            c != null && !c.GetIsDead() && c.effects.GetEffect("CHARM") != null && c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Player;

        private static IEnumerator WatchNewBot(PA_Hacker_Command cmd, Character old, Character fresh)
        {
            int until = turns + 2;
            while (turns <= until && cmd != null && cmd.victim == fresh)
            {
                if (fresh == null || fresh.GetIsDead()) yield break; // it died yours: the swap stands
                if (!Holds(fresh))
                {
                    if (old == null || old.GetIsDead()) yield break;
                    FallBack(cmd, old, fresh);
                    yield break;
                }
                yield return new WaitForSeconds(0.25f);
            }
        }

        internal static void FallBack(PA_Hacker_Command cmd, Character old, Character fresh)
        {
            if (fresh != null)
            {
                fresh.RemoveTag("HACKER_CHARM");
                fresh.RemoveTag("MINION");
                fresh.effects.GetEffect("CHARM")?.RemoveInstant();
                AI_Manager.instance.RefreshPlayerMinionList();
            }
            Effect_Charm charm = EffectManager.instance.ApplyCharm(old, Player.instance);
            if (charm == null) return;
            charm.displayName = cmd.charm_effect_name;
            charm.description = cmd.charm_effect_description;
            charm.duration = -1;
            charm.icon = cmd.charm_effect_icon;
            old.AddTag("HACKER_CHARM");
            old.SetTag("MINION", 1);
            AI_Manager.instance.RegisterMinion(old);
            cmd.victim = old;
            cmd.charm_effect = charm;
            Plugin.Instance.StartCoroutine(Retag(cmd, old));
            Game.LOG($"<color=#FF9F5C>{fresh?.GetName() ?? "The target"} throws off the override.</color> <color=#9FE08A>Your link to {old.GetName()} holds.</color>");
        }

        /// <summary>
        /// The bot's earlier charm effect is destroyed at the end of the frame, and its OnDestroy takes the
        /// HACKER_CHARM tag (how Command finds its bot after a load) and puts its team back: set them again after.
        /// </summary>
        private static IEnumerator Retag(PA_Hacker_Command cmd, Character old)
        {
            // vanilla let the old charm go with a delayed Remove(): the effect lingers until it expires, then undoes
            // the bot. Cut those leftovers loose from it (their OnDestroy skips a null target), and keep the tag
            // and team set while any of them might still be on their way out.
            for (float t = 0f; t < 4f; t += 0.25f)
            {
                if (cmd == null || old == null || old.GetIsDead() || cmd.victim != old) yield break;
                foreach (Effect_Charm e in Object.FindObjectsOfType<Effect_Charm>())
                    if (e != null && e != cmd.charm_effect && e.target == old) e.target = null;
                if (!old.HasTag("HACKER_CHARM")) old.AddTag("HACKER_CHARM");
                old.SetTag("MINION", 1);
                old.ai?.SetTeam(AI.AI_TEAM.Player);
                yield return new WaitForSeconds(0.25f);
            }
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
        private static class Patch_CountTurns
        {
            private static void Postfix() => turns++;
        }

        // ---- Gigamaw ----------------------------------------------------------------------------------------------

        /// <summary>
        /// The lashers kill enemies outright (200% health) and the plant eats the corpse; that feeding is what opens the
        /// fight. Your pets only took 5%, so they could never feed it. Converted and organic pets are now devoured too.
        /// </summary>
        [HarmonyPatch(typeof(Character), nameof(Character.TakeDamage), new[] { typeof(Character), typeof(DmgObj), typeof(string), typeof(Ability), typeof(float) })]
        private static class Patch_Lasher
        {
            private static bool Prefix(Character __instance, Character attacker, string attackString)
            {
                if (!GigamawEatsMinions.Value || attacker == null || !attacker.HasTag("BIOLABS_MINIBOSS_GIGAMAW") || !IsEdible(__instance)) return true;
                if (attackString != Ability.T("Events - BioLabs/EventRoom_0_LasherStrike")) return true;
                __instance.RemoveTag("NOCORPSE"); // the corpse is what gets eaten and counted
                __instance.killedBy = attacker;
                Game.LOG($"{attackString} {__instance.GetName()} is dragged into the plant's maw.");
                __instance.Die();
                return false;
            }
        }

        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.GetPlayerMinionMax))]
        private static class Patch_Cap
        {
            private static void Postfix(ref int __result)
            {
                if (RaiseCap.Value && !Tweaks.Has("minionCap")) __result += Mathf.Max(1, Cap.Value) - 3;
            }
        }

        internal static bool IsSwappablePet(Character c) => IsPet(c) && c.GetMover() != null;

        /// <summary>Frees the pet's tile so your move goes through; the pet takes your old tile once you set off.</summary>
        private static bool FreePetTile(Player_Control pc, Vector3 direction)
        {
            if (!SwapWithPets.Value || pendingPet != null || Game.RESOLVING || Game.TARGETING) return false;
            NavNode node = pc.GetNodeInDirection(new Vector3(Mathf.Round(direction.x), 0f, Mathf.Round(direction.z)));
            Character pet = node?.GetOccupant();
            if (!IsSwappablePet(pet)) return false;
            pet.GetMover().AbortMove();
            pendingPet = pet;
            pendingNode = node;
            node.ClearOccupant();
            Plugin.Instance.StartCoroutine(Expire(pet, node));
            return true;
        }

        /// <summary>Backing up and strafing.</summary>
        [HarmonyPatch(typeof(Player_Control), "MoveCheck")]
        private static class Patch_MoveCheck
        {
            private static bool Prefix(Player_Control __instance, Vector3 direction, ref bool __result)
            {
                if (!TileStacks.PlayerWalksOnto(__instance, direction) && !FreePetTile(__instance, direction)) return true;
                __result = false; // "not handled": the game goes on to move you into the now free tile
                return false;
            }
        }

        /// <summary>Walking forward (bumping attacks enemies and nudges pets).</summary>
        [HarmonyPatch(typeof(Player_Control), "AttackMove")]
        private static class Patch_AttackMove
        {
            private static bool Prefix(Player_Control __instance, ref bool __result)
            {
                if (!TileStacks.PlayerWalksOnto(__instance, __instance.transform.forward) && !FreePetTile(__instance, __instance.transform.forward)) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Character_Mover), "StartMove")]
        private static class Patch_StartMove
        {
            private static void Postfix(Character_Mover __instance, NavNode startNode, NavNode targetNode)
            {
                if (!__instance.isPlayer || pendingPet == null || targetNode != pendingNode) return;
                Character pet = pendingPet;
                pendingPet = null;
                pendingNode = null;
                if (pet == null || pet.GetIsDead()) return;
                pet.GetMover().MoveTo(targetNode, startNode); // leaving clears targetNode, which you now stand on
                targetNode.SetOccupant(Player.instance);
                startNode.SetOccupant(pet);
            }
        }

        /// <summary>If the move never happened (a locked door, a cancelled turn), the pet keeps its tile.</summary>
        private static IEnumerator Expire(Character pet, NavNode node)
        {
            yield return new WaitForSecondsRealtime(1f);
            if (pendingPet != pet) yield break;
            pendingPet = null;
            pendingNode = null;
            if (pet != null && !pet.GetIsDead() && node.GetOccupant() == null) node.SetOccupant(pet);
        }
    }
}
