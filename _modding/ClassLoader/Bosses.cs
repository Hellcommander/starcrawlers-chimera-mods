using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// Bosses in side areas and the Ichor Deep, from any mod's "sideAreas": { "bosses": [...] } (see BossSpec). Each new
    /// layer rolls for one (a tower only on its top layer). It waits in a far room, marked with a skull, until you find it
    /// or it notices you, then its health bar comes up; one with a "challenge" stays out of the level until you call it
    /// (see Challenge). Tags carry it through saves and kept side areas: which boss it is, and that it has already shown
    /// itself.
    /// </summary>
    internal static class Bosses
    {
        internal static ConfigEntry<int> Chance;

        internal const string IdTag = "SCCLB_ID_", AwakeTag = "SCCLB_AWAKE";

        /// <summary>Self-tests only: every layer gets this boss.</summary>
        internal static string Force;

        /// <summary>Bosses whose health bar is up. A map change or reload takes the widgets away, and this goes with them.</summary>
        private static readonly HashSet<Character> Barred = new HashSet<Character>();

        internal static void Bind(ConfigFile cfg)
        {
            Chance = cfg.Bind("SideAreas", "BossChance", 15, new ConfigDescription(
                "Percent chance that a side area layer holds a boss (0 turns them off). Kinds can set their own.", new AcceptableValueRange<int>(0, 100)));
        }

        internal static BossSpec SpecOf(Character c)
        {
            Tag t = c?.tags?.FirstOrDefault(x => x?.tagName != null && x.tagName.StartsWith(IdTag));
            return t == null ? null : SideAreaData.Boss(t.tagName.Substring(IdTag.Length));
        }

        internal static IEnumerable<Character> Alive =>
            Contracts.AllCharacters().Where(c => !c.GetIsDead() && c.tags != null && c.tags.Any(t => t?.tagName != null && t.tagName.StartsWith(IdTag)));

        /// <summary>Boss skills (after EnemyAbilities has its effects and the mod enemies exist).</summary>
        internal static void PrepareSkills()
        {
            foreach (BossSpec b in SideAreaData.Bosses)
                foreach (MobSkillSpec s in b.skills) EnemyAbilities.Prepare(s, b.pack, $"boss_{b.id}");
        }

        // ---- rolling and placing ------------------------------------------------------------------------------

        private static bool Allowed(BossSpec b, KindSpec k) =>
            Game.FLOOR >= b.minFloor && Game.FLOOR <= b.maxFloor && Endless.Depth >= b.minDepth
            && (b.kinds == null || b.kinds.Count == 0 || b.kinds.Contains(k.id, StringComparer.OrdinalIgnoreCase));

        private static BossSpec Roll(KindSpec k)
        {
            var pool = SideAreaData.Bosses.Where(b => b.weight > 0 && Allowed(b, k)).ToList();
            if (pool.Count == 0) return null;
            int r = Random.Range(0, pool.Sum(b => b.weight));
            return pool.First(b => (r -= b.weight) < 0);
        }

        /// <summary>From Echoes.OnEchoBuilt, once the layer's other people are in place.</summary>
        internal static void OnEchoBuilt(KindSpec k, Room arrival)
        {
            Barred.Clear();
            if (!SideLayers.VaultHere) return;
            BossSpec b = Force != null ? SideAreaData.Boss(Force) : Random.value < (k.bossChance >= 0f ? k.bossChance : Chance.Value / 100f) ? Roll(k) : null;
            if (b == null) return;
            if (b.challenge?.model != null) Challenge.Build(b, arrival);
            else Spawn(b, arrival);
        }

        internal static NavNode Lair(Room arrival)
        {
            Room room = Echoes.Rooms(arrival).Where(r => r != Echoes.VaultRoom && !r.Properties.IsHallway)
                .OrderByDescending(r => Vector3.Distance(r.transform.position, Player.instance.transform.position)).Take(3).OrderBy(_ => Random.value).FirstOrDefault();
            for (int i = 0; i < 12 && room != null; i++)
            {
                NavNode n = room.GetRandomNode();
                if (n != null && !n.IsOccupied() && !n.HasDoor()) return n;
            }
            return SideAreas.RandomFreeNode(arrival);
        }

        internal static Character Spawn(BossSpec b, Room arrival, NavNode at = null)
        {
            bool self = Mirrors.IsSelf(b.creature);
            Character prefab = self ? Mirrors.Body() : EnemyRegistry.Resolve(b.creature);
            NavNode spot = at ?? Lair(arrival);
            if (prefab == null || spot == null) { Plugin.Log.LogWarning($"Boss {b.id}: {(prefab == null ? $"no enemy '{b.creature}'" : "no free tile")}"); return null; }
            Character c = AI_Manager.instance.Spawn_Character(prefab, spot.transform.position, AI.AI_TEAM.Hostile);
            if (c == null) return null;
            EnemyAbilities.FacePlayer(c);
            c.SetTag(IdTag + b.id, 1);
            if (self) Corrupted.Spawned(c, b, spot);
            else
            {
                c.AddTag("BOSS");
                int hp = Mathf.Max(1, Mathf.RoundToInt(c.GetHealthMax() * b.health));
                c.SetHealth(hp, hp);
                Teach(c, b);
            }
            c.SetName(b.name);
            if (!string.IsNullOrEmpty(b.effect)) Echoes.Fx(c, b.effect, b.effectStacks);
            c.ai?.ChangeState(AI.AI_STATE.Idle);
            Menu_MiniMap.instance?.CreateSkullMarkerFor(c);
            if (at == null && !string.IsNullOrEmpty(b.arrival)) Game.LOG($"<color={b.color}>{b.arrival}</color>");
            Plugin.Log.LogInfo($"Boss: {b.id} ({c.internalName}) on {spot.transform.position}, {c.GetHealthMax()} HP");
            return c;
        }

        private static void Teach(Character c, BossSpec b)
        {
            foreach (MobSkillSpec s in b.skills) EnemyAbilities.Attach(c.gameObject, s)?.Init(c);
            if (b.skills.Count > 0) c.ai?.GetAbilities();
        }

        // ---- finding it -----------------------------------------------------------------------------------------

        internal static void Turn()
        {
            Corrupted.TickEscort();
            Room here = Player.instance.GetCurrentRoom();
            foreach (Character c in Alive.ToList())
            {
                if (c.ai == null || (c.ai.GetState() == AI.AI_STATE.Idle && c.GetMover()?.GetCurrentNavNode()?.GetRoom() != here)) continue;
                if (Barred.Add(c)) BossHealth.GetInstance()?.AddBoss(c);
                if (c.HasTag(AwakeTag)) continue;
                c.AddTag(AwakeTag);
                BossSpec b = SpecOf(c);
                if (!string.IsNullOrEmpty(b?.text)) Game.LOG($"<color={b.color}>{b.text}</color>");
            }
        }

        /// <summary>A reloaded side area: the skull comes back, and a corrupted copy gets its skills and looks back.</summary>
        internal static IEnumerator RestoreSoon()
        {
            yield return new WaitForSeconds(0.5f);
            Barred.Clear();
            if (!SideAreas.InSide) yield break;
            Challenge.Restore();
            foreach (Character c in Alive.ToList())
            {
                Menu_MiniMap.instance?.CreateSkullMarkerFor(c);
                try { Corrupted.Refit(c); }
                catch (Exception e) { Plugin.Log.LogError("Corrupted copy, after a load: " + e); }
            }
            Corrupted.RelightEscort();
        }

        internal static string TrackerLine()
        {
            var alive = SideAreas.InSide ? Alive.Select(SpecOf).Where(b => b != null).ToList() : null;
            string waiting = SideAreas.InSide ? Challenge.TrackerLine() : null;
            if (alive == null || alive.Count == 0) return waiting;
            return string.Join("  |  ", alive.Select(b => $"<color={b.color}>{b.name}</color> is in here").Concat(waiting != null ? new[] { waiting } : new string[0]));
        }

        // ---- its end --------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_Down
        {
            private static void Prefix(Character __instance)
            {
                if (__instance == null || __instance.GetIsDead() || __instance.tags == null) return;
                BossSpec b = SpecOf(__instance);
                if (b == null) return;
                __instance.RemoveTag(IdTag + b.id);
                try
                {
                    NavNode at = __instance.GetMover()?.GetCurrentNavNode();
                    foreach (LootRoll l in b.loot)
                        foreach (var (type, rarity) in l.Roll()) Echoes.Spew(type, rarity, at);
                    if (Mirrors.IsSelf(b.creature))
                    {
                        Corrupted.DropGear(b, at);
                        Corrupted.EscortFalls();
                    }
                    if (!string.IsNullOrEmpty(b.deathText)) Game.LOG($"<color={b.color}>{b.deathText}</color>");
                    Plugin.Log.LogInfo($"Boss: {b.id} down");
                }
                catch (Exception e) { Plugin.Log.LogError($"Boss {b.id}: dropping its loot: {e}"); }
            }
        }

        /// <summary>A save keeps a boss's tags and stats, not skills it was taught: teach them again before it next acts.</summary>
        [HarmonyPatch(typeof(AI), "SelectAbility")]
        private static class Patch_BackFromSave
        {
            private static void Prefix(AI __instance)
            {
                Character c = __instance.GetCharacter();
                BossSpec b = SpecOf(c);
                if (b == null) return;
                try
                {
                    if (Mirrors.IsSelf(b.creature)) Corrupted.Refit(c);
                    else if (b.skills.Any(s => !c.GetComponents<ModMobAbility>().Any(m => m.specKey == s.key))) Teach(c, b);
                }
                catch (Exception e) { Plugin.Log.LogError($"Boss {b.id}, after a load: {e}"); }
            }
        }
    }
}
