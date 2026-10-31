using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// Faction encounters in the echoes (the first game's standing-gated event options) and the services a contact
    /// offers when you call them: smoothing things over for credits, asking for work, what they know about their
    /// rivals' plans.
    /// </summary>
    internal static class FactionEvents
    {
        private static Dictionary<string, object> D => SideAreas.Data;

        // ---- encounters ---------------------------------------------------------------------------------------

        internal class EncounterPad : MonoBehaviour { public string faction, encounter; }

        /// <summary>
        /// Most echoes (60%) hold one encounter: usually the occupier's, otherwise any faction's whose standing
        /// window you're in. It waits on a pad in the faction's colour.
        /// </summary>
        internal static void PlaceEncounter(Offer job, Room arrival)
        {
            if (Random.value > 0.6f) return;
            var candidates = Factions.All.SelectMany(f => f.encounters.Where(e => Factions.Rep(f) >= e.minRep && Factions.Rep(f) <= e.maxRep).Select(e => (f, e))).ToList();
            FactionSpec owner = Contracts.Occupier;
            var preferred = candidates.Where(x => x.f == owner).ToList();
            var pool = preferred.Count > 0 && Random.value < 0.6f ? preferred : candidates;
            if (pool.Count == 0) return;
            var (fac, enc) = pool[Random.Range(0, pool.Count)];
            NavNode n = SideAreas.RandomFreeNode(arrival);
            if (n == null) return;
            Vector3 p = n.transform.position;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            D["enc"] = $"{fac.id}|{enc.id}|{p.x.ToString(inv)}|{p.y.ToString(inv)}|{p.z.ToString(inv)}";
            Put(fac, enc, p);
        }

        private static void Put(FactionSpec f, EncounterSpec e, Vector3 p)
        {
            TeleportPad pad = Contracts.PlacePad(p, f.color, "SCCL_Encounter");
            var c = pad.gameObject.AddComponent<EncounterPad>();
            c.faction = f.id;
            c.encounter = e.id;
        }

        internal static void RestoreEncounter()
        {
            if (D == null || !D.TryGetValue("enc", out object v) || !(v is string s)) return;
            string[] p = s.Split('|');
            FactionSpec f = Factions.Get(p[0]);
            EncounterSpec e = f?.encounters.FirstOrDefault(x => x.id == p[1]);
            if (e == null || p.Length < 5) return;
            Put(f, e, new Vector3(float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture)));
        }

        internal static void ClearEncounter() => D?.Remove("enc");

        [HarmonyPatch(typeof(TeleportPad), nameof(TeleportPad.OnEnterNode))]
        private static class Patch_EncounterPad
        {
            private static bool Prefix(TeleportPad __instance, Character c)
            {
                var pad = __instance.GetComponent<EncounterPad>();
                if (pad == null) return true;
                if (c == Player.instance && !Game.Game_isLoading && !FactionUi.Open)
                {
                    FactionSpec f = Factions.Get(pad.faction);
                    EncounterSpec e = f?.encounters.FirstOrDefault(x => x.id == pad.encounter);
                    if (e != null) { Factions.Discover(f, "you ran into their people"); FactionUi.ShowEncounter(f, e, __instance); }
                }
                return false;
            }
        }

        /// <summary>Why an option is locked, or null when you can take it.</summary>
        internal static string Locked(FactionSpec f, ConditionSpec r)
        {
            if (r == null) return null;
            int rep = Factions.Rep(f);
            if (r.repAtLeast.HasValue && rep < r.repAtLeast.Value) return $"needs {Factions.Category(r.repAtLeast.Value)} standing";
            if (r.repAtMost.HasValue && rep > r.repAtMost.Value) return $"only if they're {Factions.Category(r.repAtMost.Value)} or worse toward you";
            if (r.favour > 0 && Factions.Favour(f) < r.favour) return $"needs {r.favour} favour";
            if (r.credits > 0 && Menu_Inventory.instance.GetCredits() < r.credits) return $"needs {r.credits} credits";
            if (!string.IsNullOrEmpty(r.flag) && !Factions.Flag(f, r.flag)) return $"only if {r.flag}";
            if (!string.IsNullOrEmpty(r.notFlag) && Factions.Flag(f, r.notFlag)) return $"not while {r.notFlag}";
            if (!string.IsNullOrEmpty(r.skill) && !(Player_SkillControl.instance?.skillTrees?.Any(t => t != null && t.internalName == r.skill) ?? false))
                return $"needs {Game.instance.skills.FirstOrDefault(s => s != null && s.internalName == r.skill)?.displayName.ToString() ?? r.skill}";
            return null;
        }

        internal static string RequireText(ConditionSpec r)
        {
            if (r == null) return "";
            var parts = new List<string>();
            if (r.repAtLeast.HasValue) parts.Add(Factions.Category(r.repAtLeast.Value) + "+");
            if (r.favour > 0) parts.Add($"{r.favour} favour{(r.spendFavour ? ", spent" : "")}");
            if (r.credits > 0) parts.Add($"{r.credits} credits");
            if (!string.IsNullOrEmpty(r.skill)) parts.Add(Game.instance.skills.FirstOrDefault(s => s != null && s.internalName == r.skill)?.displayName.ToString() ?? r.skill);
            return parts.Count > 0 ? $"[{string.Join(", ", parts)}] " : "";
        }

        /// <summary>Takes an option: pays its costs and applies its outcome. Returns the result text.</summary>
        internal static string Choose(FactionSpec f, OptionSpec o, TeleportPad pad)
        {
            ConditionSpec r = o.requires;
            if (Locked(f, r) != null) return null;
            if (r != null && r.credits > 0) Menu_Inventory.instance.AdjustCredits(-r.credits);
            if (r != null && r.spendFavour && r.favour > 0) Factions.AdjustFavour(f, -r.favour);
            foreach (OutcomeSpec x in o.outcome ?? new List<OutcomeSpec>())
            {
                FactionSpec who = string.IsNullOrEmpty(x.faction) ? f : Factions.Get(x.faction) ?? f;
                if (x.rep != 0) Factions.AdjustRep(who, x.rep, "what you did");
                if (x.favour != 0) Factions.AdjustFavour(who, x.favour);
                if (x.credits != 0) Menu_Inventory.instance.AdjustCredits(x.credits);
                if (x.xp > 0) Factions.GiveXp(x.xp);
                Factions.GiveLoot(x.loot, x.rarity);
                if (x.heal > 0f) Player.instance.Heal(Mathf.RoundToInt(Player.instance.GetHealthMax() * x.heal), false);
                if (x.goods) Contracts.GiveGoods(who, 1 + Game.FLOOR / 2);
                if (!string.IsNullOrEmpty(x.discover)) Factions.Discover(Factions.Get(x.discover), "word from " + f.Short);
                if (x.fight) Contracts.HitTeam(who, null, 3, nearPlayer: true);
            }
            if (pad != null) Object.Destroy(pad.gameObject);
            ClearEncounter();
            string res = o.result ?? "";
            if (res.Length > 0) Game.LOG($"{f.Tinted()}: {res}");
            return res;
        }

        // ---- contact services ---------------------------------------------------------------------------------

        internal static int SmoothCost(FactionSpec f) => 50 + 25 * Math.Max(0, Game.FLOOR) + 2 * Math.Max(0, -Factions.Rep(f));

        /// <summary>Why you can't pay to smooth things over, or null.</summary>
        internal static string CantSmooth(FactionSpec f)
        {
            if (Factions.Flag(f, "Betrayed") || Factions.Flag(f, "Deceitful")) return "they won't take your money";
            if (Factions.Rep(f) > 10) return "nothing to smooth over";
            if (Factions.Rep(f) < -75) return "too far gone for money";
            if (D != null && D.TryGetValue("smooth_" + f.id, out object v) && v is int fl && fl == Game.FLOOR) return "once per floor";
            if (Menu_Inventory.instance.GetCredits() < SmoothCost(f)) return $"needs {SmoothCost(f)} credits";
            return null;
        }

        internal static void Smooth(FactionSpec f)
        {
            if (CantSmooth(f) != null) return;
            Menu_Inventory.instance.AdjustCredits(-SmoothCost(f));
            D["smooth_" + f.id] = Game.FLOOR;
            Factions.AdjustRep(f, 5, "a donation to the cause");
        }

        internal static string CantAskForWork(FactionSpec f)
        {
            if (Factions.Rep(f) < -10) return "they don't want you working for them";
            if (f.runsFor.Count == 0) return "they don't hire out";
            if (D != null && D.ContainsKey("req_" + f.id)) return "already promised";
            return null;
        }

        internal static void AskForWork(FactionSpec f)
        {
            if (CantAskForWork(f) != null) return;
            D["req_" + f.id] = true;
            Game.LOG($"{f.Tinted()} will have a job for you on the next breach board.");
        }

        internal static int IntelCost(FactionSpec f) => 30 + 15 * Math.Max(0, Game.FLOOR);

        internal static string CantBuyIntel(FactionSpec f)
        {
            if (Factions.Rep(f) < 11) return "needs Valued standing";
            if (!Factions.EnemiesOf(f).Any(r => r.operations.Count > 0)) return "nothing to tell";
            if (Menu_Inventory.instance.GetCredits() < IntelCost(f)) return $"needs {IntelCost(f)} credits";
            return null;
        }

        internal static void BuyIntel(FactionSpec f)
        {
            if (CantBuyIntel(f) != null) return;
            Menu_Inventory.instance.AdjustCredits(-IntelCost(f));
            int n = FactionOps.LearnRivalPlans(f);
            foreach (FactionSpec r in Factions.EnemiesOf(f)) Factions.Discover(r, "from " + f.Short);
            Game.LOG(n > 0 ? $"{f.Tinted()}: \"Here's what I'm hearing.\" ({n} operation{(n > 1 ? "s" : "")} against you, now on your standing screen.)"
                           : $"{f.Tinted()}: \"Nobody's coming for you. Yet.\"");
        }
    }
}
