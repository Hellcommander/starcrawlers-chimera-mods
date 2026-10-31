using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Stats for a companion from a character sheet instead of the floor it stands on: the body's weakest stat
    /// profile, plus a baseline for its level, the tier bonuses of its trees at their ranks, and the stats and
    /// resistances of its gear; its basic shot or strike uses its weapon's damage. Used by hired crew and by
    /// Conjoined bodies walking beside you (their own level, trees and equipped gear).
    /// </summary>
    internal static class Loadout
    {
        internal class Spec
        {
            public int level = 1;
            public List<(Skill_Tree tree, int rank)> trees = new List<(Skill_Tree, int)>();
            /// <summary>Gear as the game's item save data (ItemSaveInfo).</summary>
            public List<object> gear = new List<object>();
            /// <summary>&gt;0: the exact max health (gear and trees already in it). 0: your health scaled by the body's profile.</summary>
            public int healthMax;
            /// <summary>Which basic attack a weapon feeds: melee or ranged; null = whichever the weapon is.</summary>
            public bool? melee;
        }

        /// <summary>An item rebuilt from save data (not in the world: read it, then Destroy it).</summary>
        internal static Item Rebuild(object saved)
        {
            if (!(saved is ItemSaveInfo info) || ItemDatabase.instance == null) return null;
            Item it = ItemDatabase.instance.GetItem(info.itemId);
            if (it == null) return null;
            it.Deserialize(saved);
            return it;
        }

        /// <summary>Every bonus on top of the body's weakest profile.</summary>
        internal static (Dictionary<STAT_TYPE, int> stats, Dictionary<DAMAGE_TYPE, int> resists) Bonuses(Spec s)
        {
            var stats = new Dictionary<STAT_TYPE, int>();
            var res = new Dictionary<DAMAGE_TYPE, int>();
            void S(STAT_TYPE t, int n) { stats.TryGetValue(t, out int v); stats[t] = v + n; }
            void R(DAMAGE_TYPE t, int n) { res.TryGetValue(t, out int v); res[t] = v + n; }

            // levels: a little of everything
            foreach (STAT_TYPE t in new[] { STAT_TYPE.Strength, STAT_TYPE.Toughness, STAT_TYPE.Agility, STAT_TYPE.Recognition, STAT_TYPE.Smarts, STAT_TYPE.Savvy })
                S(t, (Math.Max(1, s.level) - 1) / 2);

            foreach (var (tree, rank) in s.trees)
                for (int i = 0; tree != null && i <= rank && i < tree.skillTiers.Count; i++)
                {
                    Skill_Tier tier = tree.skillTiers[i];
                    if (tier == null) continue;
                    foreach (StatObj st in tier.attributeChanges ?? new StatObj[0]) if (st != null) S(st.statType, st.amount);
                    foreach (DmgObj r in tier.resistanceChanges ?? new DmgObj[0]) if (r != null) R(r.dmgType, r.damage);
                }

            foreach (object saved in s.gear)
            {
                Item it = Rebuild(saved);
                if (it is Item_Equipment eq)
                {
                    foreach (StatObj st in eq.stats ?? new List<StatObj>()) if (st != null) S(st.statType, st.amount);
                    foreach (DmgObj r in eq.resistanceMods ?? new List<DmgObj>()) if (r != null) R(r.dmgType, r.damage);
                }
                if (it != null) UnityEngine.Object.Destroy(it.gameObject);
            }
            // an exact health figure already has every health bonus in it
            if (s.healthMax > 0) stats.Remove(STAT_TYPE.HealthMax);
            return (stats, res);
        }

        /// <summary>Rebuilds a companion's base stats and weapon from its sheet; `hp` &lt; 0 = full health.</summary>
        internal static void Apply(Character c, Spec s, int hp, Func<int> playerHealth)
        {
            if (!(c is Mob mob)) return;
            MobStatProfile p0 = mob.mobStatProfiles != null && mob.mobStatProfiles.Length > 0 ? mob.mobStatProfiles[0] : null;
            if (p0 != null)
            {
                for (int i = 0; i < p0.primaryAttributes.Length && i < c.primaryAttributes.Length; i++) c.primaryAttributes[i].amount = p0.primaryAttributes[i].amount;
                for (int i = 0; i < p0.secondaryAttributes.Length && i < c.secondaryAttributes.Length; i++) c.secondaryAttributes[i].amount = p0.secondaryAttributes[i].amount;
            }
            // health: exact, or yours scaled by the body's own toughness (a Nemesis profile's 100 = your health)
            int baseHp = s.healthMax > 0 ? s.healthMax
                : Player.instance != null && p0 != null ? Mathf.Max(10, Mathf.RoundToInt(playerHealth() * p0.healthMax / 100f))
                : p0?.healthMax ?? 40;
            mob.healthMax = baseHp;
            c.SetStat(STAT_TYPE.HealthMax, baseHp);
            c.resistances = c.resistances?.Where(r => r != null).Select(r => new DmgObj(r.dmgType, 0)).ToList() ?? new List<DmgObj>();

            var (stats, res) = Bonuses(s);
            foreach (var kv in stats)
            {
                StatObj st = c.primaryAttributes.FirstOrDefault(x => x != null && x.statType == kv.Key) ?? c.secondaryAttributes.FirstOrDefault(x => x != null && x.statType == kv.Key);
                if (st != null) st.amount += kv.Value;
            }
            foreach (var kv in res)
            {
                DmgObj r = c.resistances.FirstOrDefault(x => x.dmgType == kv.Key);
                if (r == null) c.resistances.Add(new DmgObj(kv.Key, kv.Value)); else r.damage += kv.Value;
            }
            mob.healthMax = Math.Max(1, c.GetStat(STAT_TYPE.HealthMax));
            c.CacheBaseStats();
            c.CalculateStats();
            c.level = Math.Max(1, s.level);
            ApplyWeapon(c, s);
            int max = Math.Max(1, c.GetHealthMax());
            c.SetHealth(Mathf.Clamp(hp < 0 ? max : hp, 1, max), max);
        }

        /// <summary>The body's basic attacks as they came, before any weapon (kept per ability so gear can change).</summary>
        private static readonly Dictionary<Ability, List<DmgObj>> stock = new Dictionary<Ability, List<DmgObj>>();

        /// <summary>The basic attack: a melee strike or a ranged shot, the one with the shortest cooldown.</summary>
        internal static Ability BasicAttack(Character c, bool melee) =>
            c.GetComponentsInChildren<Ability>(true)
             .Where(a => a != null && a.damageList != null && a.damageList.Count > 0)
             .Where(a => melee ? a.GetType().Name.Contains("Melee") : (a.GetType().Name.Contains("Ranged") || a.GetType().Name.Contains("RunAndGun")))
             .OrderBy(a => a.cooldown).FirstOrDefault();

        internal static bool IsMelee(Item_Weapon w) => w.weaponType == WEAPON_TYPE.Melee || w.weaponType == WEAPON_TYPE.HeavyMelee;
        internal static bool IsRanged(Item_Weapon w) => w.weaponType == WEAPON_TYPE.Ranged || w.weaponType == WEAPON_TYPE.HeavyRanged;

        private static void ApplyWeapon(Character c, Spec s)
        {
            // put both basic attacks back first, so a weapon taken off leaves no trace
            foreach (bool kind in new[] { true, false })
            {
                Ability a = BasicAttack(c, kind);
                if (a == null) continue;
                if (!stock.ContainsKey(a)) stock[a] = a.damageList.Select(Copy).ToList();
                a.damageList = stock[a].Select(Copy).ToList();
            }
            foreach (object saved in s.gear)
            {
                Item it = Rebuild(saved);
                try
                {
                    if (!(it is Item_Weapon w) || w.GetStatDamageList().Count == 0) continue;
                    bool melee = IsMelee(w);
                    if (!melee && !IsRanged(w)) continue;
                    if (s.melee.HasValue && s.melee.Value != melee) continue;
                    Ability basic = BasicAttack(c, melee);
                    if (basic == null) continue;
                    basic.damageList = w.GetStatDamageList();
                    return; // the first usable weapon (the main hand comes first)
                }
                finally { if (it != null) UnityEngine.Object.Destroy(it.gameObject); }
            }
        }

        private static DmgObj Copy(DmgObj d) => new DmgObj(d.dmgType, d.damage) { damageMax = d.damageMax };
    }
}
