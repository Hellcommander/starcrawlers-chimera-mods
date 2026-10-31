using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Shared tiles, part two: the game code that looks at "whoever stands on this tile" (audited: ~50 methods across
    /// the mob and player abilities, hazards, traps and boss rooms) only ever sees a tile's primary occupant.
    ///  - Area hits pass on: when an explosion, beam, blast, splash, damage field or trap hits the primary of a shared
    ///    tile, everyone else on that tile takes the same damage and any effect it applies in the same moment.
    ///  - Targeting: "is this character in these rooms" counts characters sharing a tile, so enemies target them and
    ///    they pick targets.
    ///  - Room-wide boss attacks list everyone in the room; channel blasts gather everyone on their impact tiles.
    ///  - Events that clear a tile (room locks, boss anchors, loot drops, summoning circles) first move the extra
    ///    characters on shared tiles to free tiles next to them.
    /// </summary>
    internal static class TileStackAreas
    {
        /// <summary>Abilities whose hits land on tiles (the audit's inline area attacks). Ones that use AI_Manager's area queries already see stacks.</summary>
        private static readonly HashSet<string> AreaTypes = new HashSet<string>
        {
            "MA_BOSS_Ughstein_GigaSlam", "MA_BOSS_Ughstein_Thundercrunch", "MA_BOSS_Cerberus_Annihilation", "MA_Channel", "MA_Chroma_Warp",
            "MA_Xeno_Warp", "MA_Geotechnician_Bomb_Pulse", "MA_Nemesis_Soldier_Grenade", "MA_Spidermine_Explode", "MA_Throw_Grenade",
            "MA_Throw_Experiment", "MA_Ray", "PA_Barbarian_FlyingSmash", "PA_Barbarian_SweepingStrike", "PA_Bomb", "PA_Grenade",
            "PA_Soldier_Grenade", "PA_Elementalist_MagmaBurst", "PA_Magnetronc", "PA_Pirate_Cacafuego", "PA_Prototype_Thunderclap",
            "PA_Psykonics_Blast", "PA_Pyromaniac_MovotolCocktail", "PA_Smuggler_Flashbang", "PA_Suppression", "PA_Bomblets",
        };

        private static int areaDepth;      // inside a synchronous area method (damage fields, trap bombs, channel blasts)
        private static bool passing;       // re-applying to riders right now
        private static Character lastAreaPrimary;
        private static int lastAreaFrame = -1;
        private static readonly HashSet<string> effectPassed = new HashSet<string>();
        private static int effectPassedFrame = -1;

        private static bool IsAreaSource(Ability a, Character attacker, string attackString)
        {
            if (areaDepth > 0) return true;
            for (Type t = a?.GetType(); t != null && t != typeof(Ability); t = t.BaseType)
                if (AreaTypes.Contains(t.Name)) return true;
            // The BioLabs lasher vines hit whoever stands on their trigger tiles.
            return attacker != null && attacker.HasTag("BIOLABS_MINIBOSS_GIGAMAW") && attackString == Ability.T("Events - BioLabs/EventRoom_0_LasherStrike");
        }

        private static NavNode StackOfPrimary(Character c)
        {
            if (c == null || c.GetMover() == null) return null;
            NavNode n = c.GetMover().GetCurrentNavNode();
            return n != null && n.GetOccupant() == c && TileStacks.Riders(n).Any() ? n : null;
        }

        private static List<DmgObj> Clone(List<DmgObj> l) =>
            l?.Where(d => d != null).Select(d => new DmgObj(d.dmgType, d.damage) { damageMax = d.damageMax }).ToList() ?? new List<DmgObj>();

        // ---- damage and effects pass on to everyone on the tile -----------------------------------------------

        [HarmonyPatch(typeof(Character), nameof(Character.TakeDamage), new[] { typeof(Character), typeof(List<DmgObj>), typeof(string), typeof(Ability), typeof(float) })]
        private static class Patch_TakeDamage
        {
            private static void Prefix(Character __instance, Character attacker, List<DmgObj> dmgList, string attackString, Ability dmgSource, out List<DmgObj> __state)
            {
                __state = null;
                if (passing || !TileStacks.ShareTiles.Value || StackOfPrimary(__instance) == null || !IsAreaSource(dmgSource, attacker, attackString)) return;
                __state = Clone(dmgList);
            }

            private static void Postfix(Character __instance, Character attacker, string attackString, Ability dmgSource, float critDmgMod, List<DmgObj> __state)
            {
                if (__state == null) return;
                PassOn(__instance, r => r.TakeDamage(attacker, Clone(__state), attackString, dmgSource, critDmgMod));
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.DamageHealth), new[] { typeof(List<DmgObj>), typeof(string), typeof(Ability) })]
        private static class Patch_DamageHealth
        {
            private static void Prefix(Character __instance, List<DmgObj> dmgList, string attackString, Ability dmgSource, out List<DmgObj> __state)
            {
                __state = null;
                if (passing || !TileStacks.ShareTiles.Value || StackOfPrimary(__instance) == null || !IsAreaSource(dmgSource, null, attackString)) return;
                __state = Clone(dmgList);
            }

            private static void Postfix(Character __instance, string attackString, Ability dmgSource, List<DmgObj> __state)
            {
                if (__state == null) return;
                PassOn(__instance, r => r.DamageHealth(Clone(__state), attackString, dmgSource));
            }
        }

        private static void PassOn(Character primary, Action<Character> hit)
        {
            NavNode n = primary.GetMover()?.GetCurrentNavNode();
            if (n == null) return;
            lastAreaPrimary = primary;
            lastAreaFrame = Time.frameCount;
            passing = true;
            try
            {
                foreach (Character r in TileStacks.Riders(n).ToList())
                    if (r != null && !r.GetIsDead()) { r.isOmniDirectionalDamage = true; hit(r); }
            }
            catch (Exception e) { Plugin.Log.LogError("Shared tile hit: " + e); }
            finally { passing = false; }
        }

        /// <summary>For the self-test: an area hit on <paramref name="c"/> (as a damage field would deal it).</summary>
        internal static void TestAreaHit(Character c, int dmg, string effect)
        {
            areaDepth++;
            try
            {
                c.TakeDamage(null, new List<DmgObj> { new DmgObj(DAMAGE_TYPE.Fire, dmg) }, "Test blast", null, 0f);
                if (effect != null) EffectManager.instance.ApplyEffect(effect, c);
            }
            finally { areaDepth--; }
        }

        /// <summary>An effect put on the primary by the same area hit (same frame) goes on the others too.</summary>
        private static void PassEffect(string id, Character target, int charges)
        {
            if (passing || !TileStacks.ShareTiles.Value || target == null || string.IsNullOrEmpty(id)) return;
            NavNode n = StackOfPrimary(target);
            if (n == null) return;
            if (areaDepth <= 0 && !(target == lastAreaPrimary && Time.frameCount == lastAreaFrame)) return;
            if (effectPassedFrame != Time.frameCount) { effectPassed.Clear(); effectPassedFrame = Time.frameCount; }
            if (!effectPassed.Add(id + "|" + target.GetInstanceID())) return;
            passing = true;
            try
            {
                foreach (Character r in TileStacks.Riders(n).ToList())
                    if (r != null && !r.GetIsDead() && EffectManager.instance.CanUseOn(id, r))
                    {
                        if (charges > 0) EffectManager.instance.ApplyEffect(id, r, charges);
                        else EffectManager.instance.ApplyEffect(id, r);
                    }
            }
            catch (Exception e) { Plugin.Log.LogError("Shared tile effect: " + e); }
            finally { passing = false; }
        }

        [HarmonyPatch(typeof(EffectManager), nameof(EffectManager.ApplyEffect), new[] { typeof(string), typeof(Character) })]
        private static class Patch_Apply2
        {
            private static void Postfix(string internalName, Character target) => PassEffect(internalName, target, 0);
        }

        [HarmonyPatch(typeof(EffectManager), nameof(EffectManager.ApplyEffect), new[] { typeof(string), typeof(Character), typeof(int) })]
        private static class Patch_Apply3
        {
            private static void Postfix(string internalName, Character target, int charges) => PassEffect(internalName, target, charges);
        }

        /// <summary>Synchronous area methods: everything they hit counts as an area hit.</summary>
        [HarmonyPatch]
        private static class Patch_AreaScope
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                foreach (var (type, method) in new[]
                {
                    ("Hazard_DamageField", "DealDamage"), ("Hazard_DamageField_Mobile", "DealDamage"), ("Trap_Bomb", "Trigger"),
                    ("MA_Channel", "ChannelComplete_Blue"), ("MA_Channel", "ChannelComplete_Prototype"), ("MA_BOSS_Cerberus_Annihilation", "DamageCheck"),
                    ("Hazard_Mine", "Explode"), // mines and bomblets blow up on whoever set them off; the whole tile takes it
                })
                {
                    var m = AccessTools.Method(AccessTools.TypeByName(type), method);
                    if (m != null && m.DeclaringType.Name == type) yield return m;
                    else Plugin.Log.LogWarning($"Shared tiles: {type}.{method} not found");
                }
            }

            private static void Prefix() => areaDepth++;
            private static Exception Finalizer(Exception __exception) { areaDepth = Math.Max(0, areaDepth - 1); return __exception; }
        }

        // ---- targeting and room lists -------------------------------------------------------------------------

        /// <summary>Enemies pick targets (and allies pick theirs) among characters in linked rooms; riders count.</summary>
        [HarmonyPatch(typeof(Room), nameof(Room.GetLinkedRoomsContains))]
        private static class Patch_RoomContains
        {
            private static void Postfix(Room __instance, Character c, ref bool __result)
            {
                if (__result || c == null || c.GetIsDead() || c.GetMover() == null) return;
                NavNode n = c.GetMover().GetCurrentNavNode();
                if (n == null || !TileStacks.Riders(n).Contains(c)) return;
                Room r = n.GetRoom();
                __result = r == __instance || (__instance.linkedRooms != null && __instance.linkedRooms.Contains(r));
            }
        }

        [HarmonyPatch]
        private static class Patch_RoomCharacters
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                foreach (string type in new[] { "Room_Event_Cerberus", "Room_Event_SpecialProjects_Boss" })
                {
                    var m = AccessTools.Method(AccessTools.TypeByName(type), "GetRoomCharacters");
                    if (m != null) yield return m;
                }
            }

            private static void Postfix(object __instance, Character exclude, List<Character> __result)
            {
                var nodes = Traverse.Create(__instance).Field("roomNodes").GetValue<List<NavNode>>();
                if (nodes == null || __result == null) return;
                foreach (NavNode n in nodes)
                    foreach (Character r in TileStacks.Riders(n))
                        if (r != exclude && !__result.Contains(r)) __result.Add(r);
            }
        }

        [HarmonyPatch(typeof(MA_Channel), "GatherVictims")]
        private static class Patch_ChannelVictims
        {
            private static void Postfix(MA_Channel __instance)
            {
                var impacts = Traverse.Create(__instance).Field("impacts").GetValue() as System.Collections.IList;
                var targets = Traverse.Create(__instance).Field("targets").GetValue<List<Character>>();
                if (impacts == null || targets == null) return;
                foreach (object imp in impacts)
                {
                    NavNode n = imp == null ? null : Traverse.Create(imp).Field("node").GetValue<NavNode>();
                    foreach (Character r in TileStacks.Riders(n))
                        if (r != Traverse.Create(__instance).Field("caster").GetValue<Character>() && !targets.Contains(r)) targets.Add(r);
                }
            }
        }

        /// <summary>The Cerberus boss picks a random target among characters it can see in the room; riders are candidates.</summary>
        [HarmonyPatch(typeof(MA_BOSS_Cerberus), nameof(MA_BOSS_Cerberus.GetRandomTarget))]
        private static class Patch_CerberusTarget
        {
            private static void Postfix(MA_BOSS_Cerberus __instance, ref Character __result)
            {
                if (!TileStacks.ShareTiles.Value) return;
                Character caster = __instance.GetCaster();
                Room room = Traverse.Create(__instance).Field("room").GetValue<Room>();
                if (room == null || caster == null || caster.ai == null) return;
                var pool = room.GetNavNodes().Where(n => n != null).SelectMany(TileStacks.On)
                    .Where(c => c != null && !c.GetIsDead() && c != caster && (c == Player.instance || (c.ai != null && c.ai.GetTeam() != AI.AI_TEAM.Hostile))
                                && caster.ai.detection.CanDetect(c))
                    .Distinct().ToList();
                // Only re-roll when someone sharing a tile is a candidate; otherwise vanilla's pick stands.
                if (pool.Any(c => c.GetMover()?.GetCurrentNavNode()?.GetOccupant() != c))
                    __result = pool[UnityEngine.Random.Range(0, pool.Count)];
            }
        }

        /// <summary>A Nanite Jar capture looks for the cloud as its tile's occupant; a cloud sharing a tile takes that place for the capture.</summary>
        [HarmonyPatch(typeof(PA_NaniteJar), nameof(PA_NaniteJar.DoImpact))]
        private static class Patch_NaniteCapture
        {
            private static void Prefix(PA_NaniteJar __instance)
            {
                if (!TileStacks.ShareTiles.Value || !__instance.isCaptureAbility) return;
                var nodes = new List<NavNode>(__instance.targetNodes ?? new List<NavNode>());
                NavNode main = Traverse.Create(__instance).Field("targetNode").GetValue<NavNode>();
                if (main != null) nodes.Add(main);
                foreach (NavNode n in nodes.Where(x => x != null).Distinct())
                {
                    if (n.GetOccupant() != null && n.GetOccupant().GetInternalName() == "NPC_Nanite_Cloud") continue;
                    Character cloud = TileStacks.Riders(n).FirstOrDefault(r => r.GetInternalName() == "NPC_Nanite_Cloud");
                    if (cloud != null) TileStacks.Promote(n, cloud);
                }
            }
        }

        [HarmonyPatch(typeof(NavNode), nameof(NavNode.Discover))]
        private static class Patch_Discover
        {
            private static void Postfix(NavNode __instance) { foreach (Character r in TileStacks.Riders(__instance)) r.Discover(); }
        }

        // ---- tiles that get cleared -----------------------------------------------------------------------------

        /// <summary>Moves the extra characters off every shared tile to free neighbours (before a tile gets cleared or locked).</summary>
        internal static void Unstack()
        {
            foreach (var kv in TileStacks.All.ToList())
            {
                foreach (Character r in TileStacks.Riders(kv.Key).ToList())
                {
                    NavNode to = kv.Key.GetLinks().FirstOrDefault(l => l != null && !l.IsOccupied() && !l.HasDoor() && !l.BlockCheck())
                                 ?? Map.instance.GetClosestNode(kv.Key, 3f, validNodesOnly: true);
                    if (to == null || to.IsOccupied()) continue;
                    r.GetMover().AbortMove();
                    r.GetMover().SetCurrentNavNode(to);
                    r.transform.position = to.transform.position;
                }
            }
        }

        [HarmonyPatch]
        private static class Patch_Clearers
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                foreach (var (type, method) in new[]
                {
                    ("MA_BOSS_Cerberus", "PreTurnCheck"), ("MA_BOSS_Ooze", "CasterDied"), ("MA_BOSS_Widow", "CasterDied"), ("MA_BOSS_Widow", "Use"),
                    ("MiniEvent_SpecialEffect_SetRoomLock", "Trigger"), ("Room_Event_BioLabs_1", "TriggerNextCell"), ("Room_Event_BioLabs_1", "LockRoom"),
                    ("Room_Event_BioLabs_Boss", "SetDoorLocks"), ("Room_Event_ProdBoss", "DoLootSpawn"), ("Hazard_Summoning", "GatherNode"),
                })
                {
                    var m = AccessTools.Method(AccessTools.TypeByName(type), method);
                    if (m != null && m.DeclaringType.Name == type) yield return m;
                    else Plugin.Log.LogWarning($"Shared tiles: {type}.{method} not found");
                }
            }

            private static void Prefix()
            {
                if (!TileStacks.ShareTiles.Value) return;
                try { Unstack(); }
                catch (Exception e) { Plugin.Log.LogError("Shared tiles unstack: " + e); }
            }
        }
    }
}
