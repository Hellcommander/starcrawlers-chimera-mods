using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// "hazardRules" on an effect: while it is on the crawler, the floor's damaging hazards (fire, acid, gas, ...) follow
    /// other rules, whoever laid them, as the game's Pyromaniac makes all fire spread:
    ///   { "hazard": "HAZARD_Fire", "spread": 20, "damage": 0.25 }
    /// </summary>
    public class HazardRuleSpec
    {
        /// <summary>The hazard id ("HAZARD_Fire"), or the start of several; "all" = every damaging hazard.</summary>
        public string hazard;
        /// <summary>% chance, per stack, that each such hazard creeps onto a free tile beside it each turn, on top of any
        /// spreading of its own. Hazards that never spread start to. Only hazards that run out do; the new tile gets
        /// half to all of the turns the old one had left, as the game's own spread does.</summary>
        public int spread;
        /// <summary>Added to the share of damage the hazard deals to your enemies, per stack (0.25 = a quarter more).
        /// You and your summons take its usual damage.</summary>
        public float damage;
    }

    internal static class HazardRules
    {
        /// <summary>The summed rules for one hazard from every effect on the crawler: spread %, damage share.</summary>
        internal static (int spread, float damage) For(Hazard h)
        {
            int spread = 0;
            float damage = 0f;
            Player p = Player.instance;
            if (p == null || p.effects == null || h == null) return (0, 0f);
            string id = Id(h);
            foreach (Effect e in p.effects.GetEffects())
                if (e is ModEffect m)
                    foreach (var (r, stacks) in m.HazardRules())
                        if (Matches(r.hazard, id)) { spread += r.spread * stacks; damage += r.damage * stacks; }
            return (spread, damage);
        }

        internal static string Id(Hazard h) => string.IsNullOrEmpty(h.internalName) ? h.name.Replace("(Clone)", "").Trim() : h.internalName;

        internal static bool Matches(string rule, string id) =>
            string.IsNullOrEmpty(rule) || rule.Equals("all", StringComparison.OrdinalIgnoreCase) || id.StartsWith(rule, StringComparison.OrdinalIgnoreCase);

        /// <summary>"hazardRules" in words, for an effect's description.</summary>
        internal static string Describe(HazardRuleSpec r, int stacks)
        {
            string name = string.IsNullOrEmpty(r.hazard) || "all".Equals(r.hazard, StringComparison.OrdinalIgnoreCase) ? "damaging hazard" : GroundFields.HazardName(r.hazard).ToLowerInvariant();
            var bits = new List<string>();
            if (r.spread > 0) bits.Add($"{Mathf.Min(100, r.spread * stacks)}% chance each turn to spread to a tile beside it");
            if (r.damage != 0f) bits.Add($"{Mathf.RoundToInt(r.damage * stacks * 100):+#;-#}% damage to your enemies");
            return bits.Count == 0 ? "" : $"Any {name} on the floor, whoever laid it: {string.Join("; ", bits)}";
        }

        // ---- "clearHazards" ---------------------------------------------------------------------------------------

        /// <summary>
        /// "clearHazards": puts out the damaging hazards (fire, acid, gas, ...) on the tiles around the cast's aim point (or
        /// the target, when there is none), or under the target without a radius; "hazard" narrows it to one kind. Traps and mines are left alone. "value:cleared" counts the
        /// tiles cleared, for the actions after it.
        /// </summary>
        internal static void Clear(ActionSpec a, ActionContext ctx, Character t)
        {
            if (Map.instance == null || HazardManager.instance == null) return;
            // as ground is laid: a wide clear centres on the cast's aim point when it has one, a tile-sized one under the target
            Vector3 at = (a.radius > 0f || ctx.groundAtCenter) && ctx.center.HasValue ? ctx.center.Value
                       : t != null ? t.transform.position : ctx.center ?? ctx.caster.transform.position;
            List<NavNode> tiles = a.radius > 0f ? Map.instance.GetNavNodesAt(at, a.radius).Where(n => n != null).ToList()
                : new List<NavNode> { Map.instance.GetNearestNavNodeAt(at, 1f) };
            int cleared = 0;
            foreach (NavNode n in tiles)
            {
                if (n == null || n.hazards == null) continue;
                bool any = false;
                foreach (Hazard h in n.hazards.ToList())
                    // a put-out field stays on its tile a moment while its flames die down; the manager has let go of it
                    if (h is Hazard_DamageField && HazardManager.instance.hazards.Contains(h) && Matches(a.hazard, Id(h))) { h.RemoveHazard(); any = true; }
                if (any) cleared++;
            }
            ctx.cleared += cleared;
            if (cleared > 0) Plugin.Log.LogInfo($"{ctx.sourceName}: cleared hazards from {cleared} tiles");
        }

        // ---- the patches ------------------------------------------------------------------------------------------

        private static readonly AccessTools.FieldRef<Hazard, bool> initialSetup = AccessTools.FieldRefAccess<Hazard, bool>("initialSetup");

        /// <summary>"spread": after the game's own turn for the hazard, one more chance to creep to a free tile beside it,
        /// by the same rules the game's spread keeps (no hidden tiles, walls, closed doors or tiles that keep hazards out).</summary>
        [HarmonyPatch(typeof(Hazard_DamageField), nameof(Hazard_DamageField.DoUpdate_Instant))]
        private static class Patch_Spread
        {
            private static void Postfix(Hazard_DamageField __instance)
            {
                try
                {
                    Hazard_DamageField h = __instance;
                    if (h == null || !h.gameObject.activeInHierarchy || initialSetup(h) || h.duration <= 1 || h.myNodes.Count == 0 || h.myNodes[0] == null) return;
                    int odds = For(h).spread;
                    if (odds <= 0 || UnityEngine.Random.Range(1, 101) > odds) return;
                    NavNode from = h.myNodes[0];
                    string id = Id(h);
                    var free = from.links.Where(n => n != null && !n.HasHazard(id) && !n.Flag_isHidden && !n.Block_Hazard && !n.BlockCheck(from)
                        && (!n.HasDoor() || n.GetDoor().isOpen)).ToList();
                    if (free.Count == 0) return;
                    NavNode to = free[UnityEngine.Random.Range(0, free.Count)];
                    Hazard made = HazardManager.instance.CreateHazard(id, to.transform.position);
                    if (made != null) made.duration = UnityEngine.Random.Range(h.duration / 2, h.duration + 1);
                    Spread++;
                }
                catch (Exception e) { Plugin.Log.LogWarning("Hazard spread: " + e.Message); }
            }
        }

        internal static int Spread; // devtests

        /// <summary>"damage": while a field deals its turn's damage, its damage is raised on tiles where only your enemies
        /// stand, and put back after.</summary>
        [HarmonyPatch(typeof(Hazard_DamageField), "DealDamage")]
        private static class Patch_Damage
        {
            private static void Prefix(Hazard_DamageField __instance, out List<(DmgObj d, int dmg, int max)> __state)
            {
                __state = null;
                try
                {
                    Hazard_DamageField h = __instance;
                    if (h == null || h.damageList == null || h.damageList.Count == 0) return;
                    List<Character> on = h.myNodes.Where(n => n != null && n.GetOccupant() != null && !n.GetOccupant().GetIsDead()).Select(n => n.GetOccupant()).ToList();
                    if (on.Count == 0 || on.Any(c => c == Player.instance || TileStacks.IsAlly(c))) return;
                    float more = For(h).damage;
                    if (more == 0f) return;
                    __state = h.damageList.Where(d => d != null).Select(d => (d, d.damage, d.damageMax)).ToList();
                    foreach (var (d, dmg, max) in __state)
                    {
                        d.damage = Mathf.Max(0, Mathf.RoundToInt(dmg * (1f + more)));
                        d.damageMax = Mathf.Max(0, Mathf.RoundToInt(max * (1f + more)));
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("Hazard damage: " + e.Message); }
            }

            private static void Finalizer(List<(DmgObj d, int dmg, int max)> __state)
            {
                if (__state == null) return;
                foreach (var (d, dmg, max) in __state) { d.damage = dmg; d.damageMax = max; }
            }
        }
    }
}
