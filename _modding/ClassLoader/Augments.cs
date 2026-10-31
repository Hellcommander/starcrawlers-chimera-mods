using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// A rewrite you choose for one of your rune-socketed skills in the rune matrix (ClassDef.augments), bought from the
    /// class's pool. Everything a skill rewrite takes works here (SkillModSpec), including "when" and "roll".
    /// </summary>
    public class AugmentSpec : SkillModSpec
    {
        public string id;
        /// <summary>delivery (a skill takes one) | behavior | flaw (a skill takes one; it only makes that skill's other augments cheaper).</summary>
        public string kind = "behavior";
        /// <summary>What it costs from the pool; on a flaw, what it takes off the skill's bill.</summary>
        public int points = 1;
        /// <summary>The class rank it opens at (1 = just taken).</summary>
        public int rank = 1;
        /// <summary>Rune sockets of the skill it shuts while it is set, from the last one open. A rune in one waits and does nothing.</summary>
        public int seal;
        /// <summary>The price in words, for one the numbers don't show (a "when", a form that gives up aiming...).</summary>
        public string drawback;
        /// <summary>Extra rewrites that hold only while a rune they name is set and has charge.</summary>
        public List<AugmentResonance> resonance;
    }

    /// <summary>What an augment gains from a particular rune in the same skill.</summary>
    public class AugmentResonance : SkillModSpec
    {
        /// <summary>A rune kind (element | shape | power | payload) or a rune item ("FsLensRune", or "Mod:Id").</summary>
        public string rune;
    }

    /// <summary>
    /// Spell augments: each class with "augments" has one pool for all its rune-socketed skills. The pool is the class's
    /// rank plus one for every one of those skills you have perked. Set augments go in the save per ability
    /// (SideAreas.Data "augments"), are worked into the skill as rewrites (SkillMods) and can only be changed out of
    /// combat. A skill that no longer fits a smaller pool goes dark until there is room again.
    /// </summary>
    internal static class Augments
    {
        internal const string Key = "augments";
        internal const string Delivery = "delivery", Behavior = "behavior", Flaw = "flaw";

        private static readonly Dictionary<string, List<AugmentSpec>> catalogs = new Dictionary<string, List<AugmentSpec>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<AugmentSpec, (string gain, string price)> rules = new Dictionary<AugmentSpec, (string, string)>();

        internal static void Register(ClassDef def)
        {
            if (def.augments == null || def.augments.Count == 0) return;
            var list = new List<AugmentSpec>();
            foreach (AugmentSpec a in def.augments)
            {
                if (a == null || string.IsNullOrEmpty(a.id)) { Plugin.Log.LogWarning($"{def.id}: an augment needs an \"id\""); continue; }
                if (list.Any(x => x.id == a.id)) { Plugin.Log.LogWarning($"{def.id}: augment '{a.id}' is listed twice"); continue; }
                a.kind = (a.kind ?? Behavior).ToLowerInvariant();
                if (a.kind != Delivery && a.kind != Behavior && a.kind != Flaw) Plugin.Log.LogWarning($"{def.id}: augment '{a.id}' has unknown kind '{a.kind}' ({Delivery}, {Behavior}, {Flaw})");
                if (string.IsNullOrEmpty(a.name)) a.name = a.id;
                list.Add(a);
            }
            SkillMods.Normalize(list.Cast<SkillModSpec>().ToList(), def.id);
            foreach (AugmentSpec a in list) SkillMods.Normalize(a.resonance?.Cast<SkillModSpec>().ToList(), def.id);
            catalogs[def.id] = list;
        }

        internal static IReadOnlyList<AugmentSpec> Catalog(string classId) =>
            classId != null && catalogs.TryGetValue(classId, out var l) ? l : (IReadOnlyList<AugmentSpec>)Array.Empty<AugmentSpec>();

        /// <summary>Only skills with rune sockets of a class that lists augments can take them.</summary>
        internal static bool Has(ModAbility m) => m?.Spec != null && m.Spec.runeSlots > 0 && Catalog(m.ClassId).Count > 0;

        /// <summary>The class's skills that take augments, in a fixed order: what comes first keeps its place in a smaller pool.</summary>
        internal static List<ModAbility> Skills(string classId) =>
            Player_AbilityControl.instance?.GetAbilities()?.OfType<ModAbility>().Where(m => m != null && m.ClassId == classId && Has(m))
                .OrderBy(m => m.internalName, StringComparer.Ordinal).ToList() ?? new List<ModAbility>();

        // ---- what is set -------------------------------------------------------------------------------------------

        private static Dictionary<string, string[]> Store
        {
            get
            {
                var data = SideAreas.Data;
                if (data == null) return null;
                if (!(data.TryGetValue(Key, out object v) && v is Dictionary<string, string[]> d)) data[Key] = d = new Dictionary<string, string[]>();
                return d;
            }
        }

        /// <summary>The augments set on the skill. One its mod no longer lists is skipped.</summary>
        internal static List<AugmentSpec> Set(ModAbility m)
        {
            var store = Store;
            if (store == null || m == null || !store.TryGetValue(m.internalName, out string[] ids)) return new List<AugmentSpec>();
            var all = Catalog(m.ClassId);
            return ids.Select(id => all.FirstOrDefault(a => a.id == id)).Where(a => a != null).ToList();
        }

        private static void Save(ModAbility m, List<AugmentSpec> set)
        {
            var store = Store;
            if (store == null) return;
            if (set.Count == 0) store.Remove(m.internalName); else store[m.internalName] = set.Select(a => a.id).ToArray();
            foreach (ModAbility s in Skills(m.ClassId)) s.Refresh(); // a change here can light or darken the class's other skills
        }

        // ---- the pool ----------------------------------------------------------------------------------------------

        internal static int Pool(string classId)
        {
            var skills = Skills(classId);
            return skills.Count == 0 ? 0 : skills[0].ClassRank + skills.Count(s => s.Perked);
        }

        /// <summary>A flaw only pays while it costs you something: burning runes takes a rune to burn, sealing a socket
        /// takes every open socket full, so that what it shuts is a rune and not an empty hole.</summary>
        internal static bool Bites(ModAbility m, AugmentSpec a)
        {
            if (a.burn <= 0 && a.seal <= 0) return true;
            var open = Runes.Sockets(m.internalName, m.Spec.runeSlots).Take(m.RankRuneSlots).ToList();
            return a.seal > 0 ? open.Count > 0 && open.All(x => x != null) : open.Any(x => x != null);
        }

        /// <summary>A flaw only discounts the skill it is on, and never all the way: what is set costs at least 1.</summary>
        internal static int Bill(ModAbility m, IEnumerable<AugmentSpec> set)
        {
            int full = 0, off = 0;
            foreach (AugmentSpec a in set)
            {
                if (a.kind != Flaw) full += a.points;
                else if (Bites(m, a)) off += a.points;
            }
            return full <= 0 ? 0 : Mathf.Max(1, full - off);
        }

        /// <summary>Skills in order, paying from the pool; those it can't cover are dark.</summary>
        private static (int spent, HashSet<string> dark) Ledger(string classId)
        {
            var dark = new HashSet<string>();
            int pool = Pool(classId), spent = 0;
            foreach (ModAbility s in Skills(classId))
            {
                int bill = Bill(s, Set(s));
                if (spent + bill <= pool) spent += bill; else dark.Add(s.internalName);
            }
            return (spent, dark);
        }

        internal static int Spent(string classId) => Ledger(classId).spent;

        internal static bool IsDark(ModAbility m) => m != null && Ledger(m.ClassId).dark.Contains(m.internalName);

        /// <summary>What's working on the skill right now.</summary>
        internal static List<AugmentSpec> Live(ModAbility m) => Has(m) && !IsDark(m) ? Set(m) : new List<AugmentSpec>();

        /// <summary>Sockets the skill's augments keep shut.</summary>
        internal static int Sealed(ModAbility m) => Live(m).Sum(a => a.seal);

        // ---- into the skill ----------------------------------------------------------------------------------------

        /// <summary>Adds the skill's augments, and what they gain from the runes that are set and charged, to the rewrites in force.</summary>
        internal static void Carry(ModAbility ab, List<SkillMods.Carried> list)
        {
            var set = Live(ab);
            if (set.Count == 0) return;
            var runes = Runes.Sockets(ab.internalName, ab.Spec.runeSlots).Take(ab.OpenRuneSlots).Where(x => x != null && !Runes.IsSpent(ab.internalName, ab.Spec, x)).ToList();
            foreach (AugmentSpec a in set)
            {
                string key = "aug:" + a.id;
                SkillMods.Take(list, a, ab, ab.ClassId, "Augment", key, null);
                int i = 0;
                foreach (AugmentResonance r in a.resonance ?? new List<AugmentResonance>())
                {
                    i++;
                    if (r != null && Resonates(r, runes)) SkillMods.Take(list, r, ab, ab.ClassId, a.name, $"{key}~{i}", null);
                }
            }
        }

        private static bool Resonates(AugmentResonance r, List<string> runes) =>
            runes.Any(id => string.Equals(Runes.Of(id)?.kind, r.rune, StringComparison.OrdinalIgnoreCase) || id == Crafting.ResolveItem(r.rune, null));

        // ---- choosing ----------------------------------------------------------------------------------------------

        /// <summary>Would it do anything on this skill? One that wouldn't isn't offered: a form that would run a laid field
        /// once per victim, a burst on what already bursts, a reset on a skill with no cooldown.</summary>
        internal static bool Fits(ModAbility m, AugmentSpec a)
        {
            AbilitySpec s = m.Spec;
            if (!SkillMods.Matches(a, m, m.ClassId) || !SkillMods.Changes(a, s, m.ClassId)) return false;
            bool Adds(string k) => a.add != null && a.add.Keys.Any(x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase));
            if (a.form != null)
            {
                if (s.target == "self" || (s.actions ?? new List<ActionSpec>()).Any(x => x.@do == "field" || x.@do == "hazard" || x.@do == "trap")) return false;
                if (a.form == "area" && s.radius > 0f) return false;
                if (a.form == s.shape) return false;
            }
            if (Adds("resetOnKill") && s.cooldown + a.cooldown <= 0) return false;
            if (Adds("pierce") && (s.radius > 0f || Shapes.Is(s.shape))) return false; // it would trade the burst for a line
            if (a.jumps > 0 && a.form != "scatter" && s.target != "enemy") return false;
            return true;
        }

        /// <summary>The set with this one in: a skill's delivery and flaw swap rather than stack.</summary>
        private static List<AugmentSpec> With(ModAbility m, AugmentSpec a)
        {
            var set = Set(m);
            if (a.kind != Behavior) set.RemoveAll(x => x.kind == a.kind);
            set.Add(a);
            return set;
        }

        private static List<AugmentSpec> Without(ModAbility m, AugmentSpec a)
        {
            var set = Set(m);
            set.Remove(a);
            if (set.All(x => x.kind == Flaw)) set.Clear(); // a flaw with nothing left to discount goes too
            return set;
        }

        private static bool Pierces(AugmentSpec a) => a.add != null && a.add.Keys.Any(x => string.Equals(x, "pierce", StringComparison.OrdinalIgnoreCase));

        /// <summary>Why this set can't stand on the skill, or null.</summary>
        private static string Refuse(ModAbility m, List<AugmentSpec> set)
        {
            int free = Pool(m.ClassId) - (Spent(m.ClassId) - (IsDark(m) ? 0 : Bill(m, Set(m))));
            int bill = Bill(m, set);
            if (bill > free) return $"that would cost {bill} points and {free} are free";
            if (set.Sum(a => a.seal) > m.RankRuneSlots) return "it has no open socket left to seal";
            return null;
        }

        /// <summary>Why this can't be set on the skill right now, or null.</summary>
        internal static string CantTake(ModAbility m, AugmentSpec a)
        {
            if (a.rank > m.ClassRank) return $"opens at class rank {a.rank}";
            if (!Fits(m, a)) return $"does nothing on {m.GetDisplayName()}";
            var set = With(m, a);
            if (a.kind == Flaw && set.All(x => x.kind == Flaw)) return "a flaw only discounts the skill's other augments: set one first";
            if (set.Any(x => x.form != null) && set.Any(Pierces)) return "a shot that flies on can't also take a new delivery";
            return Refuse(m, set);
        }

        /// <summary>Sets it, or takes it out when it is set. Returns why not, or null.</summary>
        internal static string Toggle(ModAbility m, AugmentSpec a)
        {
            bool on = Set(m).Contains(a);
            string why = on ? Refuse(m, Without(m, a)) : CantTake(m, a);
            if (why != null) return why;
            Save(m, on ? Without(m, a) : With(m, a));
            return null;
        }

        // ---- rules text --------------------------------------------------------------------------------------------

        /// <summary>What it gives and what it costs, apart, for the rune matrix.</summary>
        internal static (string gain, string price) Rules(AugmentSpec a, string classId)
        {
            if (rules.TryGetValue(a, out var hit)) return hit;
            var price = new List<string>();
            var good = JsonConvert.DeserializeObject<RuneSpec>(JsonConvert.SerializeObject(a));
            if (a.power > 0f && a.power < 1f) { price.Add($"{Mathf.RoundToInt((1f - a.power) * 100f)}% less damage"); good.power = 1f; }
            if (a.stamina > 0) { price.Add($"+{a.stamina} stamina"); good.stamina = 0; }
            if (a.cost > 0) { price.Add($"+{a.cost} cost"); good.cost = 0; }
            if (a.cooldown > 0) { price.Add($"+{a.cooldown} cooldown"); good.cooldown = 0; }
            if (a.range < 0f) { price.Add($"{a.range:0.#} range"); good.range = 0f; }
            if (a.instability > 0) { price.Add($"+{a.instability} Instability per cast"); good.instability = 0; }
            if (a.burn > 0) { price.Add(Runes.BurnText(a.burn)); good.burn = 0; }
            if (a.seal > 0) price.Add($"seals {a.seal} rune socket{(a.seal > 1 ? "s" : "")}");
            if (!string.IsNullOrEmpty(a.drawback)) price.Add(a.drawback);
            string gain = Runes.Describe(good, classId);
            if (a.when != null) gain += (gain.Length > 0 ? ", " : "") + $"only while {When.Describe(a.when, classId)}";
            return rules[a] = (gain, string.Join(", ", price));
        }

        internal static string ResonanceLine(AugmentResonance r, string classId)
        {
            string rune = r.rune;
            if (rune != null && !new[] { "element", "shape", "power", "payload" }.Contains(rune.ToLowerInvariant()))
                rune = ItemRegistry.NameOf(Crafting.ResolveItem(rune, null)).Replace(" Rune", "");
            else rune = "any " + rune;
            return $"With {rune}: {(string.IsNullOrEmpty(r.name) ? "" : r.name + " - ")}{Runes.Describe(r, classId)}";
        }

        /// <summary>The skill's sums with what is set on it, for the rune matrix.</summary>
        internal static string Preview(ModAbility m)
        {
            Runes.Tuning t = m.Tuning;
            AbilitySpec s = m.Spec;
            var bits = new List<string>();
            if (m.GetStaminaCost() != s.staminaCost) bits.Add($"stamina {s.staminaCost} to {m.GetStaminaCost()}");
            if (m.cooldown != s.cooldown) bits.Add($"cooldown {s.cooldown} to {m.cooldown}");
            if (t != null && Mathf.Abs(t.power - 1f) > 0.001f) bits.Add($"{Mathf.RoundToInt(t.power * 100f)}% damage");
            if (t != null && t.instability != 0) bits.Add($"{t.instability:+#;-#} {Runes.EffectName(s.runeMeter, m.ClassId)} per cast");
            if (t != null && t.burn > 0) bits.Add(Runes.BurnText(t.burn));
            int shut = Sealed(m);
            if (shut > 0) bits.Add($"{shut} socket{(shut > 1 ? "s" : "")} sealed");
            foreach (AugmentSpec a in Set(m).Where(a => a.kind == Flaw && !Bites(m, a)))
                bits.Add($"<color=#FF7272>{a.name} gives no discount until {(a.seal > 0 ? "every open socket holds a rune" : "a rune is set")}</color>");
            return bits.Count > 0 ? string.Join(", ", bits) : "its sums don't change";
        }

        /// <summary>The tooltip line of a skill that can take augments.</summary>
        internal static string TipLine(ModAbility m)
        {
            int free = Pool(m.ClassId) - Spent(m.ClassId);
            var set = Set(m);
            if (set.Count == 0) return $"<color=#E59CFF>Augments: none set, {free} point{(free == 1 ? "" : "s")} free.</color>";
            return IsDark(m) ? $"<color=#FF7272>Augments dark: {Bill(m, set)} points, more than your pool has left.</color>"
                : $"<color=#E59CFF>Augments: {string.Join(", ", set.Select(a => a.name))} ({Bill(m, set)} points, {free} free).</color>";
        }
    }
}
