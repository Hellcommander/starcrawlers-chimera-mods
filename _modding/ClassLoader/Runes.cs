using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>What a rune item does once it is set into a skill's rune socket (ItemDef.rune).</summary>
    public class RuneSpec
    {
        /// <summary>element | shape | power | payload. A skill takes one element rune, and any rune only once.</summary>
        public string kind = "power";
        /// <summary>1 = common, 2 = rare. Only shown and used by recipes/minting.</summary>
        public int tier = 1;
        /// <summary>The skill's damage becomes this type (Fire, Cryo, Shock, Bio, Psi, ...).</summary>
        public string element;
        /// <summary>Replaces the skill's reagents (effects that take part in a reaction) with this effect.</summary>
        public string reagent;
        /// <summary>The hit jumps on to this many more enemies nearby (enemy-target skills).</summary>
        public int jumps;
        /// <summary>Area added: a single-target skill becomes a burst around the target.</summary>
        public float radius;
        public float range;
        /// <summary>Damage multiplier: 1.4 = +40%.</summary>
        public float power = 1f;
        /// <summary>Added to the skill's resource cost (its stackCost, or stamina when it has none).</summary>
        public int cost;
        /// <summary>Added to the skill's stamina cost.</summary>
        public int stamina;
        public int cooldown;
        /// <summary>Stacks of the skill's runeMeter gained per cast (negative lowers what the other runes add).</summary>
        public int instability;
        /// <summary>Replaces the skill's impact particle.</summary>
        public string impactVfx;
        /// <summary>Extra actions on each target.</summary>
        public List<ActionSpec> actions;
        /// <summary>Extra actions on you, once per cast.</summary>
        public List<ActionSpec> selfActions;
        /// <summary>Added to the radius / turns of the skill's ground fields.</summary>
        public float fieldRadius;
        public int fieldTurns;
        /// <summary>Extra actions inside the skill's ground fields (run on whoever stands in them).</summary>
        public List<ActionSpec> fieldActions;
        /// <summary>Casts this rune lasts in a skill whose runes wear (AbilitySpec.runeWear); 0 = the skill's runeWear.</summary>
        public int wear;
        /// <summary>Extra casts of wear every rune in the skill takes per cast (negative takes off what others add).</summary>
        public int burn;
        /// <summary>One line of rules text for the rune panel; generated when empty.</summary>
        public string summary;

        // ---- augments: a new delivery, swapped types, behaviors added or taken away (SkillMods) ----
        /// <summary>trap: laid where you aim, goes off on the first enemy to step on it. grenade: thrown at a spot,
        /// bursts over everyone within its radius. monster: calls a creature there that bursts into the skill when it
        /// dies (or, burstOn "contact", when an enemy comes beside it). drifter: let loose to wander the deck, going
        /// off on whoever it meets. nova: a burst around you, no aiming. line / cone: that shape from you. area: a
        /// burst around the target. chain: hops on (with "jumps"). scatter: the aimed foe and random others in range.</summary>
        public string form;
        /// <summary>Where the rewritten skill is aimed: enemy | floor | self. Unset = what the form needs (trap, grenade
        /// and monster aim at a spot; a nova needs no aim), else the skill's own.</summary>
        public string target;
        public string shape;
        public float width;
        public float angle;
        public bool? instant;
        public string castVfx, travelVfx, areaVfx, sound;
        /// <summary>Effect swaps: what the skill applies -> what it applies instead ("@Singed": "@Rimed", vanilla ids too).</summary>
        public Dictionary<string, string> effects;
        /// <summary>Damage type swaps, one type at a time ("Fire": "Cryo"); "element" swaps them all.</summary>
        public Dictionary<string, string> damage;
        /// <summary>Behaviors given to the skill, by key: { "stun": 1, "knockback": 2, "lifesteal": 0.3, "trueDamage": true,
        /// "resetOnKill": true, "effect": "@Marked", "weapon": 0.5, "perRank": 1, "bonus": 2, "pierce": true, ... }. See SkillMods.Adds.</summary>
        public Dictionary<string, Newtonsoft.Json.Linq.JToken> add;
        /// <summary>Behaviors taken away: an action kind ("knockback", "stun", "effect", "effect:@Burning", "damage:Fire",
        /// "chain" keeps its hits but not the hops), a damage trait ("lifesteal", "trueDamage", "weapon", "scale") or a
        /// skill trait ("resetOnKill", "instant", "radius", "shape", "travel", "self" = its actions on you).</summary>
        public List<string> remove;

        // trap / monster / drifter details
        /// <summary>monster: the creature called ("PA_Engineer_Spidermine.characterPrefab", a mod creature...).</summary>
        public string creature;
        public string summonName;
        public ModelSpec model;
        /// <summary>trap: its look on the deck (a vanilla hazard id; default the mine); monster: see "model".</summary>
        public string look;
        /// <summary>trap: turns until it fizzles (default 8); monster and drifter: how long they last (default 4).</summary>
        public int turns;
        public int armTurns;
        public int charges = 1;
        public int fuse;
        public float triggerRadius;
        /// <summary>drifter: tiles a turn (default 2) and % chance to go off on each (default 50).</summary>
        public int steps, stepChance;
        /// <summary>monster: death (default) | contact (bursts as soon as an enemy stands beside it, at your turn).</summary>
        public string burstOn;
    }

    /// <summary>A signature combo: what one rune does in one particular skill (AbilitySpec.runeVariants).</summary>
    public class RuneVariant : RuneSpec
    {
        /// <summary>The rune item ("FsTetherRune", or "Mod:Id").</summary>
        public string rune;
        public string name;
        public string description;
        /// <summary>true: the rune's usual effect is dropped and only this applies.</summary>
        public bool replace;
    }

    /// <summary>
    /// Rune sockets. Runes are items; setting one into a socket takes it out of your bag, taking it out puts it back.
    /// Using the skill never uses them up, unless the skill sets runeWear: then each rune lasts that many casts (or its
    /// own "wear") and a matching rune from the bag takes its place when it burns out. Sockets are kept per ability in the
    /// save (SideAreas.Data "runes"), casts used per ability and rune in "runewear".
    /// A socketed skill is retuned when it is used and in its tooltip.
    /// </summary>
    internal static class Runes
    {
        internal const string Key = "runes";

        internal class Tuning
        {
            /// <summary>The runes set in open sockets.</summary>
            public readonly List<(string id, RuneSpec rune)> runes = new List<(string, RuneSpec)>();
            /// <summary>Signature combos in play.</summary>
            public readonly List<RuneVariant> variants = new List<RuneVariant>();
            /// <summary>Everything that contributes actions: runes (unless replaced) and combos.</summary>
            public readonly List<RuneSpec> parts = new List<RuneSpec>();
            public float power = 1f, radius, range, fieldRadius;
            public int cost, stamina, cooldown, instability, jumps, fieldTurns, burn;
            public string element, reagent, impactVfx;
            public string sig;
            public string Signature => sig;

            // ---- skill rewrites (SkillMods): the form and the overrides a rune can't make ----
            /// <summary>The rewrites in force, with their sources.</summary>
            public readonly List<SkillMods.Carried> mods = new List<SkillMods.Carried>();
            /// <summary>The skill as rewritten (a shallow copy of its spec with target, shape, vfx and the like swapped); null = untouched.</summary>
            public AbilitySpec spec;
            public string form, formName, target, shape, look, creature, summonName, burstOn, castVfx, travelVfx, areaVfx, sound;
            public float width, angle, triggerRadius, minRadius;
            public bool? instant;
            public int armTurns, charges = 1, turns, fuse, steps, stepChance;
            public ModelSpec model;
            public readonly Dictionary<string, string> effectSwaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> damageSwaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Behaviors given (key -> value; a later source wins) and taken away (see SkillMods).</summary>
            public readonly Dictionary<string, Newtonsoft.Json.Linq.JToken> adds = new Dictionary<string, Newtonsoft.Json.Linq.JToken>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> removes = new List<string>();
            private bool touched;

            internal void Add(SkillMods.Carried c)
            {
                mods.Add(c);
                Add(c.mod);
                if (!string.IsNullOrEmpty(c.mod.form)) formName = c.mod.name;
            }

            /// <summary>Works the form, the overrides and the skill-level adds/removes into a copy of the skill's spec
            /// (SkillMods.DefaultTarget decides where a form aims). Numbers (radius, range, cost) stay deltas on the Tuning.</summary>
            internal void Finish(AbilitySpec s)
            {
                if (!touched) return;
                spec = s.ShallowCopy();
                string own = s.target;
                spec.target = target ?? (form != null ? SkillMods.DefaultTarget(form, own) : own);
                if (form == "line" || form == "cone") spec.shape = form;
                else if (form == "trap" || form == "grenade" || form == "monster" || form == "nova" || form == "drifter" || form == "area" || form == "scatter") spec.shape = null;
                if (shape != null) spec.shape = Shapes.Is(shape) ? shape : null;
                if (width > 0f) spec.width = width;
                if (angle > 0f) spec.angle = angle;
                if (instant.HasValue) spec.instant = instant.Value;
                if (castVfx != null) spec.castVfx = castVfx;
                if (travelVfx != null && form != "nova" && form != "drifter") spec.travelVfx = travelVfx;
                if (areaVfx != null) spec.areaVfx = areaVfx;
                if (sound != null) spec.sound = sound;
                if (form == "trap" || form == "grenade" || form == "monster" || form == "nova" || form == "drifter") spec.dodgeable = false;
                if (form == "trap" || form == "monster") spec.melee = false;
                minRadius = SkillMods.DefaultRadius(form);
                if (form == "chain" && jumps <= 0) jumps = 1;
                SkillMods.FinishSpec(spec, this);
            }

            internal void Add(RuneSpec r)
            {
                parts.Add(r);
                if (!string.IsNullOrEmpty(r.form)) { form = r.form; touched = true; }
                if (!string.IsNullOrEmpty(r.target)) { target = r.target; touched = true; }
                if (!string.IsNullOrEmpty(r.shape)) { shape = r.shape; touched = true; }
                if (r.width > 0f) { width = r.width; touched = true; }
                if (r.angle > 0f) { angle = r.angle; touched = true; }
                if (r.instant.HasValue) { instant = r.instant; touched = true; }
                if (!string.IsNullOrEmpty(r.look)) look = r.look;
                if (!string.IsNullOrEmpty(r.creature)) creature = r.creature;
                if (!string.IsNullOrEmpty(r.summonName)) summonName = r.summonName;
                if (!string.IsNullOrEmpty(r.burstOn)) burstOn = r.burstOn;
                if (!string.IsNullOrEmpty(r.castVfx)) { castVfx = r.castVfx; touched = true; }
                if (!string.IsNullOrEmpty(r.travelVfx)) { travelVfx = r.travelVfx; touched = true; }
                if (!string.IsNullOrEmpty(r.areaVfx)) { areaVfx = r.areaVfx; touched = true; }
                if (!string.IsNullOrEmpty(r.sound)) { sound = r.sound; touched = true; }
                if (r.model != null) model = r.model;
                if (r.armTurns > 0) armTurns = r.armTurns;
                if (r.charges != 1) charges = r.charges;
                if (r.turns > 0) turns = r.turns;
                if (r.fuse > 0) fuse = r.fuse;
                if (r.triggerRadius > 0f) triggerRadius = r.triggerRadius;
                if (r.steps > 0) steps = r.steps;
                if (r.stepChance > 0) stepChance = r.stepChance;
                foreach (var kv in r.effects ?? new Dictionary<string, string>()) effectSwaps[kv.Key] = kv.Value;
                foreach (var kv in r.damage ?? new Dictionary<string, string>()) damageSwaps[kv.Key] = kv.Value;
                foreach (var kv in r.add ?? new Dictionary<string, Newtonsoft.Json.Linq.JToken>()) { adds[kv.Key] = kv.Value; touched = true; }
                foreach (string k in r.remove ?? new List<string>()) if (!string.IsNullOrEmpty(k)) { removes.Add(k.Trim()); touched = true; }
                power *= r.power <= 0f ? 1f : r.power;
                radius += r.radius; range += r.range; fieldRadius += r.fieldRadius;
                cost += r.cost; stamina += r.stamina; cooldown += r.cooldown;
                instability += r.instability; jumps += r.jumps; fieldTurns += r.fieldTurns; burn += r.burn;
                if (!string.IsNullOrEmpty(r.element)) element = r.element;
                if (!string.IsNullOrEmpty(r.reagent)) reagent = r.reagent;
                if (!string.IsNullOrEmpty(r.impactVfx)) impactVfx = r.impactVfx;
            }
        }

        /// <summary>This skill's signature combo for a rune, if it has one.</summary>
        internal static RuneVariant VariantOf(AbilitySpec s, string itemId) =>
            s?.runeVariants?.FirstOrDefault(v => v != null && !string.IsNullOrEmpty(v.rune) && Crafting.ResolveItem(v.rune, null) == itemId);

        /// <summary>How many of the ability's sockets are open at this class rank.</summary>
        internal static int OpenSlots(AbilitySpec s, int rank) =>
            s == null ? 0 : s.runeSlotRanks == null || s.runeSlotRanks.Count == 0 ? s.runeSlots
            : Mathf.Min(s.runeSlots, s.runeSlotRanks.Count(r => r <= rank) + Mathf.Max(0, s.runeSlots - s.runeSlotRanks.Count));

        internal static int OpensAt(AbilitySpec s, int slot) =>
            s?.runeSlotRanks != null && slot < s.runeSlotRanks.Count ? s.runeSlotRanks.OrderBy(x => x).ElementAt(slot) : 0;

        /// <summary>Internal item id -> rune.</summary>
        internal static RuneSpec Of(string itemId) =>
            itemId != null && ItemRegistry.Defs.TryGetValue(itemId, out ItemDef d) ? d.rune : null;

        internal static bool IsRune(Item i) => i != null && Of(i.itemId) != null;

        internal static IEnumerable<string> AllRuneIds => ItemRegistry.Defs.Where(kv => kv.Value?.rune != null).Select(kv => kv.Key);

        // ---- sockets --------------------------------------------------------------------------------------------

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

        /// <summary>The ability's sockets: item ids, null for an empty socket. Always runeSlots long.</summary>
        internal static string[] Sockets(string abilityId, int slots)
        {
            var s = new string[Mathf.Max(0, slots)];
            var store = Store;
            if (store != null && abilityId != null && store.TryGetValue(abilityId, out string[] have))
                for (int i = 0; i < s.Length && i < have.Length; i++) s[i] = have[i];
            return s;
        }

        private static void Save(string abilityId, string[] sockets)
        {
            var store = Store;
            if (store == null) return;
            if (sockets.All(x => x == null)) store.Remove(abilityId); else store[abilityId] = sockets;
            cache.Remove(abilityId);
        }

        /// <summary>Why this rune can't go into this ability right now, or null.</summary>
        internal static string CantSocket(AbilitySpec s, string[] sockets, string itemId)
        {
            RuneSpec r = Of(itemId);
            if (r == null) return "not a rune";
            if (s.runeKinds != null && s.runeKinds.Count > 0 && !s.runeKinds.Any(k => string.Equals(k, r.kind, StringComparison.OrdinalIgnoreCase)))
                return $"{s.name} doesn't take {r.kind} runes";
            if (sockets.Contains(itemId)) return "already set in this skill";
            if (r.kind == "element" && sockets.Any(x => Of(x)?.kind == "element")) return "only one element rune per skill";
            if (r.jumps > 0 && s.target != "enemy") return "jumps need a skill aimed at an enemy";
            return null;
        }

        internal static bool Socket(string abilityId, AbilitySpec s, int slot, string itemId, int open = int.MaxValue)
        {
            string[] sockets = Sockets(abilityId, s.runeSlots);
            if (slot < 0 || slot >= sockets.Length || slot >= open || sockets[slot] != null || CantSocket(s, sockets, itemId) != null) return false;
            var cost = new CostSpec { item = itemId, resolved = itemId, count = 1 };
            if (Crafting.Count(cost) < 1) return false;
            Crafting.Consume(cost);
            sockets[slot] = itemId;
            Save(abilityId, sockets);
            Menu_Inventory.instance?.Refresh();
            return true;
        }

        internal static bool Unsocket(string abilityId, AbilitySpec s, int slot)
        {
            string[] sockets = Sockets(abilityId, s.runeSlots);
            if (slot < 0 || slot >= sockets.Length || sockets[slot] == null) return false;
            if (Crafting.Give(sockets[slot], 1) == null) return false;
            sockets[slot] = null;
            Save(abilityId, sockets);
            Menu_Inventory.instance?.Refresh();
            return true;
        }

        // ---- wear -----------------------------------------------------------------------------------------------

        internal const string WearKey = "runewear";

        /// <summary>Casts used so far, per "abilityId|itemId". Kept when a rune is taken out, so swapping doesn't reset it.</summary>
        private static Dictionary<string, int> WearStore
        {
            get
            {
                var data = SideAreas.Data;
                if (data == null) return null;
                if (!(data.TryGetValue(WearKey, out object v) && v is Dictionary<string, int> d)) data[WearKey] = d = new Dictionary<string, int>();
                return d;
            }
        }

        /// <summary>Casts a rune lasts in this skill; 0 = it never wears.</summary>
        internal static int Life(AbilitySpec s, string itemId)
        {
            if (s == null || s.runeWear <= 0 || itemId == null) return 0;
            int own = Of(itemId)?.wear ?? 0;
            return own > 0 ? own : s.runeWear;
        }

        /// <summary>Casts left on the rune in this skill (0 when it doesn't wear).</summary>
        internal static int CastsLeft(string abilityId, AbilitySpec s, string itemId)
        {
            int life = Life(s, itemId);
            if (life <= 0) return 0;
            int left = life - Used(abilityId, itemId);
            return s.runeCharge > 0 ? Mathf.Max(0, left) : Mathf.Max(1, left);
        }

        private static int Used(string abilityId, string itemId) =>
            WearStore != null && WearStore.TryGetValue(abilityId + "|" + itemId, out int u) ? u : 0;

        /// <summary>A rechargeable rune with no casts left: it stays socketed and does nothing.</summary>
        internal static bool IsSpent(string abilityId, AbilitySpec s, string itemId) =>
            s != null && s.runeCharge > 0 && itemId != null && Life(s, itemId) > 0 && Used(abilityId, itemId) >= Life(s, itemId);

        /// <summary>Stamina to fill this rune back up; 0 when it is full or doesn't recharge.</summary>
        internal static int RechargeCost(string abilityId, AbilitySpec s, string itemId) =>
            s == null || s.runeCharge <= 0 || itemId == null ? 0 : Mathf.Min(Used(abilityId, itemId), Life(s, itemId)) * s.runeCharge;

        /// <summary>Spends your stamina on as many casts as it covers; returns how many came back.</summary>
        internal static int Recharge(string abilityId, AbilitySpec s, string itemId, Character who)
        {
            var store = WearStore;
            if (store == null || who == null || s == null || s.runeCharge <= 0) return 0;
            int used = Mathf.Min(Used(abilityId, itemId), Life(s, itemId));
            int casts = Mathf.Min(used, who.GetStamina() / s.runeCharge);
            if (casts <= 0) return 0;
            who.ModStamina(-casts * s.runeCharge);
            string k = abilityId + "|" + itemId;
            if (used - casts > 0) store[k] = used - casts; else store.Remove(k);
            return casts;
        }

        /// <summary>Stamina the rune's remaining casts are worth; 0 when it is empty or doesn't recharge.</summary>
        internal static int DischargeValue(string abilityId, AbilitySpec s, string itemId) =>
            s == null || s.runeCharge <= 0 || itemId == null ? 0 : Mathf.Max(0, Life(s, itemId) - Used(abilityId, itemId)) * s.runeCharge;

        /// <summary>Puts casts of charge back into every rechargeable rune, free of charge.</summary>
        internal static void Restore(int casts)
        {
            var store = WearStore;
            if (store == null || casts <= 0) return;
            foreach (string k in store.Keys.ToList())
            {
                if (store[k] > casts) store[k] -= casts; else store.Remove(k);
            }
            foreach (ModAbility m in RunePanel.Socketed()) m.Refresh();
        }

        /// <summary>What a discharge of this rune pays right now: nothing on cooldown, and no more than you have room for.</summary>
        internal static int DischargePays(string abilityId, AbilitySpec s, string itemId, Character who) =>
            DischargeWait > 0 || who == null ? 0 : Mathf.Min(DischargeValue(abilityId, s, itemId), who.GetStaminaMax() - who.GetStamina());

        private const string DischargeKey = "runedischarge_ready";

        /// <summary>Turns until a discharge gives stamina again; 0 = ready.</summary>
        internal static int DischargeWait =>
            Player.instance != null && SideAreas.Data != null && SideAreas.Data.TryGetValue(DischargeKey, out object v) && v is int at ? Mathf.Max(0, at - Player.instance.GetTurnCount()) : 0;

        /// <summary>Empties the rune, which deactivates it. Its casts become stamina unless a discharge gave some in the
        /// last runeDischarge turns; returns the stamina gained.</summary>
        internal static int Discharge(string abilityId, AbilitySpec s, string itemId, Character who)
        {
            var store = WearStore;
            if (store == null || who == null || s == null || s.runeCharge <= 0) return 0;
            int gain = DischargePays(abilityId, s, itemId, who);
            store[abilityId + "|" + itemId] = Life(s, itemId);
            if (gain <= 0) return 0;
            who.ModStamina(gain);
            if (s.runeDischarge > 0 && Player.instance != null && SideAreas.Data != null) SideAreas.Data[DischargeKey] = Player.instance.GetTurnCount() + s.runeDischarge;
            return gain;
        }

        /// <summary>One cast's wear on every rune in the open sockets. A rune that burns out is replaced by a matching
        /// one from the bag, or leaves its socket empty. Returns a log line per rune that burned out.</summary>
        internal static List<string> Wear(string abilityId, AbilitySpec s, int open = int.MaxValue, int burn = 0)
        {
            var burned = new List<string>();
            var store = WearStore;
            if (s == null || s.runeWear <= 0 || abilityId == null || store == null) return burned;
            string[] sockets = Sockets(abilityId, s.runeSlots);
            bool changed = false;
            int cast = 1 + Mathf.Max(0, burn);
            for (int i = 0; i < sockets.Length && i < open; i++)
            {
                string id = sockets[i];
                if (id == null) continue;
                string k = abilityId + "|" + id;
                if (s.runeCharge > 0)
                {
                    int have = Used(abilityId, id), life = Life(s, id);
                    if (have >= life) continue;
                    store[k] = Mathf.Min(life, have + cast);
                    if (have + cast >= life) burned.Add($"{ItemRegistry.NameOf(id)} is out of charge.");
                    continue;
                }
                int used = (store.TryGetValue(k, out int u) ? u : 0) + cast;
                if (used < Life(s, id)) { store[k] = used; continue; }
                store.Remove(k);
                string name = ItemRegistry.NameOf(id);
                var cost = new CostSpec { item = id, resolved = id, count = 1 };
                if (Crafting.Count(cost) >= 1) { Crafting.Consume(cost); burned.Add($"{name} burns out; a fresh one slots in."); }
                else { sockets[i] = null; changed = true; burned.Add($"{name} burns out, leaving its socket empty."); }
            }
            if (changed) { Save(abilityId, sockets); Menu_Inventory.instance?.Refresh(); }
            return burned;
        }

        // ---- tuning ---------------------------------------------------------------------------------------------

        private static readonly Dictionary<string, (string sig, Tuning t)> tunings = new Dictionary<string, (string, Tuning)>();
        private static readonly Dictionary<string, (string sig, List<ActionSpec> acts)> cache = new Dictionary<string, (string, List<ActionSpec>)>();

        /// <summary>What the runes in this ability's sockets and the rewrites on it (SkillMods) add up to; null when there are none.</summary>
        internal static Tuning For(string abilityId, AbilitySpec s, int open = int.MaxValue, List<SkillMods.Carried> mods = null)
        {
            if (s == null || abilityId == null) return null;
            // Runes left in sockets that are locked again (a lower rank) wait there and do nothing.
            var set = s.runeSlots > 0 ? Sockets(abilityId, s.runeSlots).Take(Mathf.Max(0, open)).Where(x => x != null && !IsSpent(abilityId, s, x)).ToList() : new List<string>();
            string sig = string.Join("|", set);
            if (mods != null && mods.Count > 0) sig += "||" + string.Join("|", mods.Select(m => m.key));
            if (sig.Length == 0) return null;
            if (tunings.TryGetValue(abilityId, out var c) && c.sig == sig) return c.t;
            var t = new Tuning { sig = sig };
            foreach (string id in set)
            {
                RuneSpec r = Of(id);
                if (r == null) continue;
                t.runes.Add((id, r));
                RuneVariant v = VariantOf(s, id);
                if (v == null || !v.replace) t.Add(r);
                if (v != null) { t.variants.Add(v); t.Add(v); }
            }
            foreach (SkillMods.Carried m in mods ?? new List<SkillMods.Carried>()) t.Add(m);
            t.Finish(s);
            tunings[abilityId] = (sig, t);
            return t;
        }

        /// <summary>The ability's actions with its runes worked in (a copy; the class file's actions are untouched).</summary>
        internal static List<ActionSpec> Apply(string abilityId, string variant, IEnumerable<ActionSpec> baseActs, AbilitySpec s, string classId, Tuning t)
        {
            var list = (baseActs ?? Enumerable.Empty<ActionSpec>()).ToList();
            if (t == null) return list;
            string key = abilityId + "#" + variant;
            if (cache.TryGetValue(key, out var hit) && hit.sig == t.Signature) return hit.acts;

            var acts = JsonConvert.DeserializeObject<List<ActionSpec>>(JsonConvert.SerializeObject(list)) ?? new List<ActionSpec>();
            foreach (ActionSpec a in acts) Retune(a, t, classId);
            bool IsSelf(ActionSpec a) => (a.to ?? "").ToLowerInvariant() == "self";
            if (t.removes.Count > 0 || t.adds.Count > 0) SkillMods.Rewrite(acts, t, s, classId);

            if (t.jumps > 0 && t.form != "scatter" && (s.target == "enemy" || t.form == "chain")) // scatter's "jumps" is how many it splits onto
            {
                ActionSpec chain = acts.FirstOrDefault(a => a.@do == "chain");
                if (chain != null) chain.jumps += t.jumps;
                else
                {
                    var hits = acts.Where(a => !IsSelf(a) && a.@do != "field" && a.@do != "hazard" && a.@do != "trap").ToList();
                    if (hits.Count > 0)
                    {
                        var wrap = new ActionSpec { @do = "chain", jumps = t.jumps, radius = 2.5f, falloff = 0.8f, actions = hits };
                        int at = acts.IndexOf(hits[0]);
                        acts.RemoveAll(hits.Contains);
                        acts.Insert(Mathf.Min(at, acts.Count), wrap);
                    }
                }
            }
            foreach (RuneSpec r in t.parts)
            {
                foreach (ActionSpec a in Clone(r.actions)) acts.Add(a);
                foreach (ActionSpec a in Clone(r.selfActions)) { a.to = "self"; acts.Add(a); }
            }
            if (t.instability > 0 && !string.IsNullOrEmpty(s.runeMeter))
                acts.Add(new ActionSpec { @do = "effect", to = "self", effect = s.runeMeter, stacks = t.instability });
            else if (t.instability < 0 && !string.IsNullOrEmpty(s.runeMeter))
            {
                // Coolant and the like soak up what the skill itself adds to the meter.
                int soak = -t.instability;
                foreach (ActionSpec a in acts.Where(a => a.@do == "effect" && IsSelf(a) && a.effect == s.runeMeter).ToList())
                {
                    int take = Mathf.Min(soak, Mathf.Max(1, a.stacks));
                    a.stacks = Mathf.Max(1, a.stacks) - take;
                    soak -= take;
                    if (a.stacks <= 0) acts.Remove(a);
                }
            }

            cache[key] = (t.Signature, acts);
            return acts;
        }

        private static List<ActionSpec> Clone(List<ActionSpec> l) =>
            l == null ? new List<ActionSpec>() : JsonConvert.DeserializeObject<List<ActionSpec>>(JsonConvert.SerializeObject(l)) ?? new List<ActionSpec>();

        /// <summary>Element and reagent swaps, through chains, fields and random options.</summary>
        private static void Retune(ActionSpec a, Tuning t, string classId)
        {
            if (a == null) return;
            if (a.@do == "damage" && !string.IsNullOrEmpty(t.element)) a.type = t.element;
            else if (a.@do == "damage" && t.damageSwaps.Count > 0 && a.type != null && t.damageSwaps.TryGetValue(a.type, out string swapped)) a.type = swapped;
            if (a.@do == "effect" && t.effectSwaps.Count > 0 && a.effect != null)
            {
                string id = Actions.EffectId(a.effect, classId);
                foreach (var kv in t.effectSwaps)
                    if (string.Equals(kv.Key, a.effect, StringComparison.OrdinalIgnoreCase) || string.Equals(Actions.EffectId(kv.Key, classId), id, StringComparison.OrdinalIgnoreCase)) { a.effect = kv.Value; break; }
            }
            if (a.@do == "field")
            {
                a.radius = Mathf.Max(0f, a.radius + t.fieldRadius);
                a.turns = Mathf.Max(1, a.turns + t.fieldTurns);
                if (a.actions == null) a.actions = new List<ActionSpec>();
                foreach (RuneSpec r in t.parts) a.actions.AddRange(Clone(r.fieldActions));
            }
            if (a.@do == "effect" && !string.IsNullOrEmpty(t.reagent) && Alchemy.IsReagent(Actions.EffectId(a.effect, classId)))
                a.effect = t.reagent;
            foreach (ActionSpec c in a.actions ?? new List<ActionSpec>()) Retune(c, t, classId);
            foreach (RandomOption o in a.options ?? new List<RandomOption>())
                foreach (ActionSpec c in o.actions ?? new List<ActionSpec>()) Retune(c, t, classId);
        }

        /// <summary>One line of rules text for a rune.</summary>
        internal static string Describe(RuneSpec r, string classId)
        {
            if (r == null) return "";
            if (!string.IsNullOrEmpty(r.summary)) return r.summary;
            var bits = new List<string>();
            if (!string.IsNullOrEmpty(r.element)) bits.Add($"{r.element} damage");
            if (!string.IsNullOrEmpty(r.reagent)) bits.Add($"applies {EffectName(r.reagent, classId)} instead of the skill's reagent");
            if (r.jumps > 0 && r.form != "scatter") bits.Add($"jumps to {r.jumps} more {(r.jumps > 1 ? "enemies" : "enemy")}");
            if (r.radius != 0 && (r.form == null || r.form == "chain" || r.form == "line" || r.form == "cone")) bits.Add($"area {r.radius:+0.#;-0.#}");
            if (r.range != 0) bits.Add($"range {r.range:+0.#;-0.#}");
            if (Math.Abs(r.power - 1f) > 0.001f) bits.Add($"{(r.power > 1f ? "+" : "")}{Mathf.RoundToInt((r.power - 1f) * 100f)}% damage");
            if (r.cost != 0) bits.Add($"cost {r.cost:+#;-#}");
            if (r.stamina != 0) bits.Add($"stamina {r.stamina:+#;-#}");
            if (r.cooldown != 0) bits.Add($"cooldown {r.cooldown:+#;-#}");
            if (r.fieldRadius != 0) bits.Add($"fields {r.fieldRadius:+0.#;-0.#} wider");
            if (r.fieldTurns != 0) bits.Add($"fields last {r.fieldTurns:+#;-#} turns");
            if (r.instability != 0) bits.Add($"<color=#FF9F43>{r.instability:+#;-#} Instability per cast</color>");
            if (r.burn != 0) bits.Add($"<color=#FF9F43>{BurnText(r.burn)}</color>");
            string shaped = SkillMods.DescribeShape(r, classId);
            if (shaped.Length > 0) bits.Add(shaped);
            string extra = Actions.Describe(r.actions, classId, "each target");
            if (extra.Length > 0) bits.Add(extra.Replace("\n", "; "));
            string self = Actions.Describe(r.selfActions, classId, "you");
            if (self.Length > 0) bits.Add(self.Replace("\n", "; "));
            string inField = Actions.Describe(r.fieldActions, classId, "whoever stands in its fields");
            if (inField.Length > 0) bits.Add("in its fields: " + inField.Replace("\n", "; "));
            return string.Join(", ", bits);
        }

        internal static string BurnText(int burn) => burn > 0
            ? $"every rune loses {1 + burn} casts of charge per cast"
            : $"runes lose {-burn} less per cast";

        /// <summary>"Tether: Gravity Well - ..." for tooltips and the rune panel.</summary>
        internal static string DescribeVariant(RuneVariant v, string classId, bool active)
        {
            string rune = ItemRegistry.NameOf(Crafting.ResolveItem(v.rune, null)).Replace(" Rune", "");
            string what = !string.IsNullOrEmpty(v.description) ? v.description : Describe(v, classId);
            return $"{(active ? "<color=#FFD15C>" : "<color=#8A96A3>")}{rune}: {v.name}</color> - {what}";
        }

        internal static string EffectName(string effect, string classId)
        {
            string id = Actions.EffectId(effect, classId);
            if (ModEffect.Specs.TryGetValue(id ?? "", out var rt)) return rt.spec.name;
            Effect e = EffectManager.instance?.GetEffect(id, false);
            return e != null ? e.GetName() : (effect ?? "").TrimStart('@', '#');
        }
    }
}
