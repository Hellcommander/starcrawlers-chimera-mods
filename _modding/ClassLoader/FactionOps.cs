using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// Operations, after the first game: once a faction's favour has run out (and your standing with it is low
    /// enough), each of its operations is queued, a warning reaches you, and it goes off a few steps later (every
    /// side area you enter is a step; Chimera's own floors never see them). Ambushes turn people near you into a hit team, hunters come looking for
    /// you, squeezes take credits. Each operation fires once; it re-arms when favour bottoms out at -10 (which also
    /// resets favour to 0, as in the first game) or when your standing recovers past its threshold.
    /// Contacts can call operations off, and sell what they know about their rivals' plans.
    /// </summary>
    internal static class FactionOps
    {
        private static Dictionary<string, object> D => SideAreas.Data;

        private static string Key(FactionSpec f, OperationSpec op) => $"op_{f.id}_{op.id}";

        private static int Left(FactionSpec f, OperationSpec op) => D != null && D.TryGetValue(Key(f, op), out object v) && v is int i ? i : 0;
        private static bool Done(FactionSpec f, OperationSpec op) => D != null && D.TryGetValue(Key(f, op) + "_done", out object v) && v is bool b && b;
        internal static bool Known(FactionSpec f, OperationSpec op) => D != null && D.TryGetValue(Key(f, op) + "_known", out object v) && v is bool b && b;

        /// <summary>Operations queued against you.</summary>
        internal static IEnumerable<(FactionSpec f, OperationSpec op, int left, bool known)> Pending() =>
            Factions.All.SelectMany(f => f.operations.Select(op => (f, op, left: Left(f, op), known: Known(f, op)))).Where(x => x.left > 0);

        /// <summary>A new floor or echo: queue what's due, count down, fire what's ready.</summary>
        internal static void Step(string where)
        {
            if (!Factions.Active || D == null || Player.instance == null) return;
            foreach (FactionSpec f in Factions.All)
            {
                int rep = Factions.Rep(f), fav = Factions.Favour(f);
                if (fav <= -10)
                {
                    // the first game's reset: favour back to 0, every operation armed again
                    Factions.AdjustFavour(f, -fav);
                    foreach (OperationSpec op in f.operations) D.Remove(Key(f, op) + "_done");
                    Game.LOG($"{f.Tinted()} have run out of patience with you.");
                }
                foreach (OperationSpec op in f.operations)
                {
                    int left = Left(f, op);
                    if (left > 0)
                    {
                        left--;
                        D[Key(f, op)] = left;
                        if (left == 0) Fire(f, op);
                        continue;
                    }
                    if (rep > op.repAtMost) { D.Remove(Key(f, op) + "_done"); continue; } // standing recovered: re-armed
                    if (Done(f, op) || fav > op.favourAtMost) continue;
                    int delay = op.delay != null && op.delay.Length >= 2 ? Random.Range(Math.Max(1, op.delay[0]), Math.Max(op.delay[0], op.delay[1]) + 1) : 2;
                    D[Key(f, op)] = delay;
                    D[Key(f, op) + "_done"] = true;
                    Factions.Discover(f, "they made themselves known");
                    Game.LOG($"<color=#FF8F5C>{op.warning ?? $"{f.Short} are planning something."}</color>");
                }
            }
        }

        private static void Fire(FactionSpec f, OperationSpec op)
        {
            D.Remove(Key(f, op) + "_known");
            try
            {
                switch ((op.type ?? "").ToLowerInvariant())
                {
                    case "hunter": Hunter(f, op); break;
                    case "squeeze":
                        {
                            int have = Menu_Inventory.instance.GetCredits();
                            int take = Math.Min(have, Math.Max(10, have * Mathf.Clamp(op.strength, 1, 50) / 100));
                            if (take > 0) Menu_Inventory.instance.AdjustCredits(-take);
                            Game.LOG($"<color=#FF8F5C>{op.text ?? f.Short + " squeeze you."}</color> <color=#FF8F5C>-{take} credits</color>.");
                            break;
                        }
                    default:
                        if (Contracts.HitTeam(f, Player.instance.GetCurrentRoom(), Math.Max(1, op.strength), nearPlayer: true) > 0)
                            Game.LOG($"<color=#FF5C5C>{op.text ?? f.Short + " hit team."}</color>");
                        break;
                }
            }
            catch (Exception e) { Plugin.Log.LogError($"Faction operation {f.id}/{op.id}: {e}"); }
        }

        /// <summary>A copy of the toughest enemy on the map, tougher still, spawned a room away and already looking for you.</summary>
        private static void Hunter(FactionSpec f, OperationSpec op)
        {
            Room here = Player.instance.GetCurrentRoom();
            Character template = Map.instance.GetRooms().Where(r => r != null).SelectMany(r => AI_Manager.instance.GetMobsInRoom(r) ?? new List<Character>())
                .Where(c => c != null && !c.GetIsDead() && c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Hostile && !c.HasTag("BOSS"))
                .OrderByDescending(c => c.GetHealthMax()).FirstOrDefault();
            Character prefab = template != null ? AI_Manager.instance.GetCharacter(template.internalName) : null;
            prefab = prefab ?? AI_Manager.instance.GetCharacter("CORPSEC_COMMANDO");
            NavNode node = Map.instance.GetRooms().Where(r => r != null && r != here && r.RandomNodeCheck())
                .OrderBy(r => Vector3.Distance(r.transform.position, Player.instance.transform.position)).Take(3)
                .Select(r => r.GetRandomNode()).FirstOrDefault(n => n != null && !n.IsOccupied() && !n.HasDoor());
            if (prefab == null || node == null) return;
            Character h = AI_Manager.instance.Spawn_Character(prefab, node.transform.position, AI.AI_TEAM.Hostile);
            if (h == null) return;
            int hp = h.GetHealthMax() * Mathf.Clamp(op.strength, 1, 6);
            h.SetHealth(hp, hp);
            h.SetName($"{f.Short} bounty hunter");
            h.AddTag("SCCLF_HUNTER");
            h.AddTag("SCCLF_HUNTER_" + f.id);
            Actions.ApplyEffect(new ActionSpec { @do = "effect", effect = Factions.EffectId("Hunter") },
                new ActionContext { caster = Player.instance, classId = Factions.EffectClass, sourceName = "Bounty" }, h);
            if (h.ai != null) { h.ai.ChangeState(AI.AI_STATE.Active); h.ai.SetTarget(Player.instance); }
            Game.LOG($"<color=#FF5C5C>{op.text ?? f.Short + " sent someone to collect on you."}</color>");
        }

        /// <summary>Called off: a faction's pending operations go away (and stay away until they're re-armed).</summary>
        internal static void CallOff(FactionSpec f)
        {
            if (D == null) return;
            int n = 0;
            foreach (OperationSpec op in f.operations)
                if (Left(f, op) > 0) { D.Remove(Key(f, op)); D.Remove(Key(f, op) + "_known"); n++; }
            if (n > 0) Game.LOG($"{f.Tinted()} call off what they had planned for you.");
        }

        /// <summary>What a contact knows: its rivals' pending operations against you become visible.</summary>
        internal static int LearnRivalPlans(FactionSpec f)
        {
            int n = 0;
            foreach (FactionSpec r in Factions.EnemiesOf(f))
                foreach (OperationSpec op in r.operations)
                    if (Left(r, op) > 0 && !Known(r, op)) { D[Key(r, op) + "_known"] = true; n++; }
            return n;
        }

        internal static string Describe((FactionSpec f, OperationSpec op, int left, bool known) p) =>
            p.known ? $"{p.f.Tinted()}: {p.op.name ?? p.op.type} in {p.left} step{(p.left == 1 ? "" : "s")}" : $"{p.f.Tinted()}: something, soon";

        // ---- hooks --------------------------------------------------------------------------------------------

        /// <summary>A dead bounty hunter leaves its pay behind.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_HunterDown
        {
            private static void Prefix(Character __instance)
            {
                if (__instance == null || __instance.GetIsDead() || !__instance.HasTag("SCCLF_HUNTER")) return;
                __instance.RemoveTag("SCCLF_HUNTER");
                Item it = LootManager.instance.Generate_Loot(LOOT_TYPE.Random, RARITY.Epic);
                if (it == null) return;
                Map.instance.AddItem(it);
                NavNode n = __instance.GetMover()?.GetCurrentNavNode();
                Vector3 at = n != null ? n.transform.position : __instance.transform.position;
                it.transform.position = at;
                LootManager.instance.DoLootSpew(at, it, 1f, n != null ? n.GetRoom() : null, true, null, 0.3f);
                Game.LOG("The bounty hunter's pay spills out of their kit.");
            }
        }
    }
}
