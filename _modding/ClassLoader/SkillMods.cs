using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// A rewrite of other skills, carried by a passive ("modifies" on an ability: on while you know it) or by an
    /// effect ("modifies" on an effect: on while it is on you - a stance, or a charge an active skill puts on you).
    /// Everything a rune block takes works here (RuneSpec: element, power, reach, cost, extra actions, a new form,
    /// swapped effects, behaviors added or taken away); this adds which skills it applies to and when.
    /// </summary>
    public class SkillModSpec : RuneSpec
    {
        /// <summary>Which of your skills it rewrites: names ("Ember Lance|Frost Bolt", display or internal), "*" (every
        /// other active mod skill you know, any class), "class" (the carrying class's own actives), "class:MOD_X",
        /// "others" (attacks from your other classes), "attacks" (every attack you aim, any class).</summary>
        public string skills = "class";
        /// <summary>Shown in the skill's tooltip and the log ("Buried").</summary>
        public string name;
        /// <summary>Only while this holds about you (below half health, in combat, a stance...).</summary>
        public WhenSpec when;
        /// <summary>On an effect: one stack of it (or the effect) is spent when a rewritten skill is cast.</summary>
        public bool consume;
        /// <summary>A gamble instead of a fixed rewrite: one of these is drawn for each skill (by "weight") and shown in
        /// its tooltip, so the aim always fits what comes out; casting the skill draws again. An option that would change
        /// nothing about the skill isn't drawn. "skills", "when" and "chance" sit on the entry that holds the list.</summary>
        public List<SkillModSpec> roll;
        /// <summary>"roll": % chance that anything is drawn at all.</summary>
        public int chance = 100;
        /// <summary>An option's share of the draw.</summary>
        public int weight = 1;
    }

    /// <summary>
    /// Skill rewrites and augments. A rune in a socket, a passive you know or an effect on you can change how one of
    /// your skills is delivered (its form), what it is made of (element, effects) and what it does (behaviors added
    /// or taken away, Soulash-style amplifiers), all worked into a copy of the skill at cast time and in its tooltip.
    /// </summary>
    internal static class SkillMods
    {
        /// <summary>One rewrite in force on a skill, and where it comes from.</summary>
        internal class Carried
        {
            public SkillModSpec mod;
            public string source, classId, key;
            public ModEffect carrier;
            public bool rolled;
        }

        private static readonly string[] Forms = { "trap", "grenade", "monster", "drifter", "nova", "line", "cone", "area", "chain", "scatter" };

        internal static bool IsForm(string f) => f != null && Forms.Contains(f);

        /// <summary>Lower-cases what the loader matches on; called when a class or item registers.</summary>
        internal static void Normalize(RuneSpec m, string owner)
        {
            if (m == null) return;
            m.form = m.form?.ToLowerInvariant();
            m.target = m.target?.ToLowerInvariant();
            m.shape = m.shape?.ToLowerInvariant();
            m.burstOn = m.burstOn?.ToLowerInvariant();
            foreach (ActionSpec a in (m.actions ?? new List<ActionSpec>()).Concat(m.selfActions ?? new List<ActionSpec>()).Concat(m.fieldActions ?? new List<ActionSpec>()))
                if (a != null) a.@do = (a.@do ?? "").ToLowerInvariant();
            if (!string.IsNullOrEmpty(m.form) && !IsForm(m.form)) Plugin.Log.LogWarning($"{owner}: unknown form '{m.form}' ({string.Join(", ", Forms)})");
            foreach (string k in (m.add ?? new Dictionary<string, JToken>()).Keys)
                if (!AddKeys.Contains(k)) Plugin.Log.LogWarning($"{owner}: unknown behavior to add '{k}' ({string.Join(", ", AddKeys)})");
        }

        /// <param name="classId">The class carrying them: its "@X" effects stay its own on another class's skill.</param>
        internal static void Normalize(List<SkillModSpec> mods, string classId)
        {
            foreach (SkillModSpec m in mods ?? new List<SkillModSpec>())
            {
                Normalize(m, $"skill rewrite '{m?.name ?? m?.skills}'");
                if (m == null) continue;
                OwnEffects(m, classId);
                Normalize(m.roll, classId);
            }
        }

        private static void OwnEffects(RuneSpec m, string classId)
        {
            if (string.IsNullOrEmpty(classId)) return;
            string Own(string fx) => fx != null && fx.StartsWith("@") ? classId + fx : fx;
            void Walk(List<ActionSpec> acts)
            {
                foreach (ActionSpec a in acts ?? new List<ActionSpec>())
                {
                    if (a == null) continue;
                    a.effect = Own(a.effect);
                    foreach (List<ActionSpec> inner in Nested(a)) Walk(inner);
                }
            }
            Walk(m.actions); Walk(m.selfActions); Walk(m.fieldActions);
            foreach (string k in (m.effects ?? new Dictionary<string, string>()).Keys.ToList()) m.effects[k] = Own(m.effects[k]);
            foreach (string k in new[] { "effect", "selfEffect", "purge" })
                if (m.add != null && m.add.TryGetValue(k, out JToken v))
                    m.add[k] = v.Type == JTokenType.Array ? new JArray(v.Select(x => Own((string)x))) : (JToken)Own(S(v));
        }

        private static bool resolving;

        /// <summary>Every rewrite in force on this skill right now: from the passives you know and the effects on you.</summary>
        internal static List<Carried> Active(ModAbility ab)
        {
            var list = new List<Carried>();
            Player p = Player.instance;
            var pac = Player_AbilityControl.instance;
            if (ab == null || p == null || pac == null || Game.Game_isLoading || resolving) return list;
            if (ab.Caster != null && ab.Caster != p) return list;
            if (p.effects == null) return list; // the player is still being put together
            AbilitySpec spec = ab.Spec;
            if (spec == null || spec.type == "passive") return list;
            resolving = true;
            try
            {
                int i;
                Augments.Carry(ab, list); // first, so a stance or a charge on top of them has the last word
                foreach (Ability a in pac.GetAbilities())
                {
                    if (!(a is ModAbility m) || m == ab) continue;
                    AbilitySpec ms = m.Spec;
                    if (ms?.modifies == null || ms.modifies.Count == 0) continue;
                    if (!Equipped.Check(ms.equipped, p)) continue;
                    i = 0;
                    foreach (SkillModSpec mod in ms.modifies)
                        Take(list, mod, ab, m.ClassId, m.GetDisplayName(), $"ab:{m.internalName}#{++i}", null);
                }
                foreach (Effect e in p.effects.GetEffects().ToList())
                {
                    if (!(e is ModEffect me)) continue;
                    EffectSpec es = me.Spec;
                    if (es?.modifies == null || es.modifies.Count == 0) continue;
                    i = 0;
                    foreach (SkillModSpec mod in es.modifies)
                        Take(list, mod, ab, me.ClassId, me.GetName(), $"fx:{me.internalName}#{++i}", me);
                }
            }
            catch (Exception e) { Plugin.Log.LogError($"skill rewrites for {ab.GetDisplayName()}: {e}"); }
            finally { resolving = false; }
            return list;
        }

        internal static void Take(List<Carried> list, SkillModSpec mod, ModAbility ab, string classId, string source, string key, ModEffect carrier)
        {
            if (mod == null || !Matches(mod, ab, classId) || !Holds(mod, classId, source)) return;
            bool rolled = mod.roll != null;
            if (rolled)
            {
                int drawn = Drawn(mod, ab, classId, key);
                if (drawn < 0 || !Holds(mod.roll[drawn], classId, source)) return;
                mod = mod.roll[drawn];
                key += ":" + drawn;
            }
            list.Add(new Carried { mod = mod, source = source, classId = classId, key = key, carrier = carrier, rolled = rolled });
        }

        // ---- gambles ("roll"): what each skill drew, until it is cast. Not saved: a loaded game draws again -----------

        private static readonly Dictionary<string, Dictionary<string, int>> draws = new Dictionary<string, Dictionary<string, int>>();

        internal static Func<SkillModSpec, bool> ForceDraw; // devtests

        private static int Drawn(SkillModSpec mod, ModAbility ab, string classId, string key)
        {
            if (!draws.TryGetValue(ab.internalName, out var mine)) draws[ab.internalName] = mine = new Dictionary<string, int>();
            if (mine.TryGetValue(key, out int at)) return at;
            at = -1;
            var options = Enumerable.Range(0, mod.roll.Count).Where(i => mod.roll[i] != null && Changes(mod.roll[i], ab.Spec, classId)).ToList();
            if (ForceDraw != null) options = options.Where(i => ForceDraw(mod.roll[i])).ToList();
            if (options.Count > 0 && (ForceDraw != null || UnityEngine.Random.Range(1, 101) <= mod.chance))
            {
                int pick = UnityEngine.Random.Range(0, options.Sum(i => Math.Max(1, mod.roll[i].weight)));
                foreach (int i in options) { pick -= Math.Max(1, mod.roll[i].weight); if (pick < 0) { at = i; break; } }
            }
            return mine[key] = at;
        }

        /// <summary>The skill was cast (or dodged): its next cast draws again.</summary>
        internal static void Redraw(ModAbility ab) => draws.Remove(ab.internalName);

        internal static void ClearDraws() => draws.Clear(); // devtests

        private static readonly string[] DamageTraits = { "lifesteal", "truedamage", "weapon", "scale", "scalemult", "perrank", "bonus", "chance", "levelcap" };

        private static readonly string[] SkillTraits = { "resetonkill", "instant", "dodgeable", "melee", "radius", "area", "shape", "travel" };

        /// <summary>A rewrite that would do nothing to this skill (life steal on a spell with no damage of its own, taking
        /// away a stun it doesn't have) must not be drawn, or its tooltip would promise something that never happens.</summary>
        internal static bool Changes(SkillModSpec o, AbilitySpec s, string classId)
        {
            bool hurts = Any(s.actions, a => Is(a, "damage"));
            if (s.target != "enemy" && (o.add?.Keys.Any(k => k.ToLowerInvariant() == "pierce") ?? false)) return false; // nothing to fly on from
            if (!hurts && (o.element != null || o.damage != null || (o.add?.Keys.Any(k => DamageTraits.Contains(k.ToLowerInvariant())) ?? false))) return false;
            foreach (string r in o.remove ?? new List<string>())
            {
                if (SkillTraits.Contains(r.ToLowerInvariant())) continue;
                string was = Newtonsoft.Json.JsonConvert.SerializeObject(s.actions);
                var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ActionSpec>>(was);
                Remove(copy, r, classId, 0);
                if (Newtonsoft.Json.JsonConvert.SerializeObject(copy) == was) return false;
            }
            return true;
        }

        private static bool Any(List<ActionSpec> acts, Func<ActionSpec, bool> what, int depth = 0) =>
            acts != null && depth <= 6 && acts.Any(a => a != null && (what(a) || Nested(a).Any(inner => Any(inner, what, depth + 1))));

        private static bool Holds(SkillModSpec mod, string classId, string source) =>
            mod.when == null || When.Check(mod.when, new ActionContext { caster = Player.instance, classId = classId, sourceName = source }, Player.instance);

        /// <summary>Does this rewrite name that skill?</summary>
        internal static bool Matches(SkillModSpec mod, ModAbility ab, string carrierClass)
        {
            string skills = string.IsNullOrEmpty(mod.skills) ? "class" : mod.skills;
            foreach (string raw in skills.Split('|'))
            {
                string t = raw.Trim();
                if (t.Length == 0) continue;
                if (t == "*") return true;
                if (t.Equals("class", StringComparison.OrdinalIgnoreCase)) { if (ab.ClassId == carrierClass) return true; continue; }
                if (t.Equals("others", StringComparison.OrdinalIgnoreCase)) { if (ab.ClassId != carrierClass && IsAttack(ab.Spec?.actions)) return true; continue; }
                if (t.Equals("attacks", StringComparison.OrdinalIgnoreCase)) { if ((ab.Spec?.target == "enemy" || ab.Spec?.target == "floor") && IsAttack(ab.Spec.actions)) return true; continue; }
                if (t.StartsWith("class:", StringComparison.OrdinalIgnoreCase)) { if (string.Equals(ab.ClassId, t.Substring(6), StringComparison.OrdinalIgnoreCase)) return true; continue; }
                if (string.Equals(t, ab.GetDisplayName(), StringComparison.OrdinalIgnoreCase) || string.Equals(t, ab.internalName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t, ab.Spec?.name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Does it strike anyone, directly, through a chain or a random outcome? "others" and "attacks" leave buffs and stances alone.</summary>
        private static bool IsAttack(List<ActionSpec> actions) =>
            Any(actions, a => Is(a, "damage") || Is(a, "wildcast") || Is(a, "surge"));

        /// <summary>Only a skill's top-level actions are lower-cased at load; nested ones keep the file's spelling.</summary>
        private static bool Is(ActionSpec a, string what) => string.Equals(a.@do, what, StringComparison.OrdinalIgnoreCase);

        /// <summary>Burst radius the form falls back to when the skill and its rewrites give none.</summary>
        internal static float DefaultRadius(string form) => form switch
        {
            "grenade" => 1.5f, "nova" => 2.5f, "monster" => 1.5f, "area" => 1.5f, "drifter" => 1.1f, _ => 0f,
        };

        /// <summary>Where a form aims when the rewrite doesn't say.</summary>
        internal static string DefaultTarget(string form, string own) => form switch
        {
            "trap" => "floor", "grenade" => "floor", "monster" => "floor", "nova" => "self",
            "drifter" => own == "self" ? "floor" : own, "line" => own == "self" ? "floor" : own, "cone" => own == "self" ? "floor" : own,
            "scatter" => own == "self" ? "enemy" : own, _ => own,
        };

        // ---- behaviors in and out (augments) -----------------------------------------------------------------------

        /// <summary>The keys "add" understands.</summary>
        internal static readonly string[] AddKeys =
        {
            // skill traits
            "resetOnKill", "instant", "dodgeable", "melee", "command", "reactionSlots", "pierce", "jumps",
            // damage traits (every damage action)
            "lifesteal", "trueDamage", "weapon", "scale", "scaleMult", "perRank", "bonus", "chance", "levelCap",
            // on each target
            "stun", "knockback", "pull", "blind", "silence", "taunt", "exhaust", "purge", "effect", "effectTurns", "execute", "hazard", "swap",
            // on you, once per cast
            "heal", "shield", "stamina", "cooldowns", "selfEffect",
        };

        private static int I(JToken t) => t == null ? 0 : t.Type == JTokenType.Boolean ? ((bool)t ? 1 : 0) : (int)Math.Round((double)t);
        private static float F(JToken t) => t == null ? 0f : t.Type == JTokenType.Boolean ? ((bool)t ? 1f : 0f) : (float)(double)t;
        private static bool B(JToken t) => t != null && (t.Type == JTokenType.Boolean ? (bool)t : t.Type == JTokenType.String ? !string.IsNullOrEmpty((string)t) : (double)t != 0);
        private static string S(JToken t) => t?.Type == JTokenType.String ? (string)t : t?.ToString();
        private static IEnumerable<string> Many(JToken t) => t == null ? new string[0] : t.Type == JTokenType.Array ? t.Select(x => (string)x) : new[] { S(t) };

        /// <summary>"pierce" as a number: how many it flies on through after the first (0 = no limit, or no piercing).</summary>
        internal static int Pierces(Runes.Tuning t) =>
            t != null && t.form == null && t.adds.TryGetValue("pierce", out JToken v) && v.Type != JTokenType.Boolean ? Math.Max(0, I(v)) : 0;

        /// <summary>Skill-level adds and removes, on the rewritten copy of the spec.</summary>
        internal static void FinishSpec(AbilitySpec spec, Runes.Tuning t)
        {
            foreach (var kv in t.adds)
                switch (kv.Key.ToLowerInvariant())
                {
                    case "resetonkill": spec.resetOnKill = B(kv.Value); break;
                    case "instant": spec.instant = B(kv.Value); break;
                    case "dodgeable": spec.dodgeable = B(kv.Value); break;
                    case "melee": spec.melee = B(kv.Value); break;
                    case "command": spec.command = B(kv.Value); break;
                    case "reactionslots": spec.reactionSlots = Math.Max(spec.reactionSlots, I(kv.Value)); break;
                    case "pierce": if (B(kv.Value) && t.form == null && spec.target != "self") spec.shape = "line"; break;
                }
            foreach (string r in t.removes)
                switch (r.ToLowerInvariant())
                {
                    case "resetonkill": spec.resetOnKill = false; break;
                    case "instant": spec.instant = false; break;
                    case "dodgeable": spec.dodgeable = false; break;
                    case "melee": spec.melee = false; break;
                    case "radius": case "area": spec.radius = 0f; if (t.form == null) t.minRadius = 0f; break;
                    case "shape": spec.shape = null; break;
                    case "travel": spec.travelVfx = null; break;
                }
        }

        /// <summary>Behaviors taken away and given, through chains, branches and random outcomes.</summary>
        internal static void Rewrite(List<ActionSpec> acts, Runes.Tuning t, AbilitySpec s, string classId)
        {
            foreach (string r in t.removes) Remove(acts, r, classId, 0);
            foreach (var kv in t.adds) AddTrait(acts, kv.Key, kv.Value, 0);
            string who = null;
            int fxTurns = t.adds.TryGetValue("effectTurns", out JToken ft) ? I(ft) : 0;
            foreach (var kv in t.adds)
            {
                ActionSpec a = null;
                switch (kv.Key.ToLowerInvariant())
                {
                    case "stun": a = new ActionSpec { @do = "stun", turns = Math.Max(1, I(kv.Value)) }; break;
                    case "knockback": a = new ActionSpec { @do = "knockback", amount = Math.Max(1, I(kv.Value)) }; break;
                    case "pull": a = new ActionSpec { @do = "pull", amount = Math.Max(1, I(kv.Value)) }; break;
                    case "blind": a = new ActionSpec { @do = "blind", turns = Math.Max(1, I(kv.Value)) }; break;
                    case "silence": a = new ActionSpec { @do = "silence", turns = Math.Max(1, I(kv.Value)) }; break;
                    case "taunt": a = new ActionSpec { @do = "taunt", turns = Math.Max(1, I(kv.Value)) }; break;
                    case "exhaust": a = new ActionSpec { @do = "exhaust", amount = Math.Max(1, I(kv.Value)) }; break;
                    case "purge": a = new ActionSpec { @do = "purge", effect = S(kv.Value) }; break;
                    case "execute": a = new ActionSpec { @do = "execute", amount = Math.Max(1, I(kv.Value)) }; break;
                    case "hazard": a = new ActionSpec { @do = "hazard", hazard = S(kv.Value) }; break;
                    case "swap": if (B(kv.Value)) a = new ActionSpec { @do = "swap" }; break;
                    case "effect": foreach (string fx in Many(kv.Value)) acts.Add(new ActionSpec { @do = "effect", effect = fx, turns = fxTurns }); break;
                    case "heal": a = new ActionSpec { @do = "heal", to = "self", amount = I(kv.Value) }; break;
                    case "shield": a = new ActionSpec { @do = "shield", to = "self", amount = I(kv.Value) }; break;
                    case "stamina": a = new ActionSpec { @do = "stamina", to = "self", amount = I(kv.Value) }; break;
                    case "cooldowns": a = new ActionSpec { @do = "cooldowns", to = "self", amount = I(kv.Value) }; break;
                    case "selfeffect": foreach (string fx in Many(kv.Value)) acts.Add(new ActionSpec { @do = "effect", to = "self", effect = fx, turns = fxTurns }); break;
                }
                if (a != null) { a.to = a.to ?? who; acts.Add(a); }
            }
        }

        private static IEnumerable<List<ActionSpec>> Nested(ActionSpec a)
        {
            if (a.actions != null) yield return a.actions;
            if (a.then != null) yield return a.then;
            if (a.@else != null) yield return a.@else;
            foreach (RandomOption o in a.options ?? new List<RandomOption>()) if (o?.actions != null) yield return o.actions;
        }

        private static void Remove(List<ActionSpec> acts, string key, string classId, int depth)
        {
            if (acts == null || depth > 6) return;
            string k = key.ToLowerInvariant();
            string arg = null;
            int colon = k.IndexOf(':');
            if (colon > 0) { arg = key.Substring(colon + 1).Trim(); k = k.Substring(0, colon); }
            foreach (ActionSpec a in acts.ToList())
            {
                if (a == null) continue;
                string act = (a.@do ?? "").ToLowerInvariant();
                switch (k)
                {
                    case "lifesteal": if (act == "damage") a.lifesteal = 0f; break;
                    case "truedamage": if (act == "damage") a.trueDamage = false; break;
                    case "weapon": if (act == "damage") a.weapon = 0f; break;
                    case "scale": if (act == "damage" || act == "heal" || act == "shield" || act == "stamina") a.scale = null; break;
                    case "perrank": a.perRank = 0; break;
                    case "stored": a.stored = 0f; break;
                    case "self": if ((a.to ?? "").ToLowerInvariant() == "self") acts.Remove(a); break;
                    case "chain":
                        if (act == "chain") { int at = acts.IndexOf(a); acts.Remove(a); acts.InsertRange(at, a.actions ?? new List<ActionSpec>()); }
                        break;
                    default:
                        if (act != k) break;
                        if (arg == null) { acts.Remove(a); break; }
                        if (act == "damage" && string.Equals(a.type, arg, StringComparison.OrdinalIgnoreCase)) acts.Remove(a);
                        else if (act == "effect" && a.effect != null && (string.Equals(a.effect, arg, StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(Actions.EffectId(a.effect, classId), Actions.EffectId(arg, classId), StringComparison.OrdinalIgnoreCase))) acts.Remove(a);
                        else if (act == "summon" && a.creature != null && a.creature.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0) acts.Remove(a);
                        else if ((act == "field" || act == "trap") && string.Equals(a.fieldName, arg, StringComparison.OrdinalIgnoreCase)) acts.Remove(a);
                        break;
                }
                if (acts.Contains(a)) foreach (List<ActionSpec> inner in Nested(a)) Remove(inner, key, classId, depth + 1);
            }
        }

        private static void AddTrait(List<ActionSpec> acts, string key, JToken v, int depth)
        {
            if (acts == null || depth > 6) return;
            string k = key.ToLowerInvariant();
            foreach (ActionSpec a in acts)
            {
                if (a == null) continue;
                if ((a.@do ?? "") == "damage")
                    switch (k)
                    {
                        case "lifesteal": a.lifesteal += F(v); break;
                        case "truedamage": a.trueDamage = B(v); break;
                        case "weapon": a.weapon += F(v); break;
                        case "scale": a.scale = string.IsNullOrEmpty(a.scale) ? S(v) : a.scale + "+" + S(v); break;
                        case "scalemult": a.scaleMult = F(v); break;
                        case "perrank": a.perRank += I(v); break;
                        case "bonus": a.min += I(v); a.max += I(v); break;
                        case "chance": a.chance = Mathf.Clamp(I(v), 1, 100); break;
                        case "levelcap": a.levelCap = I(v); break;
                    }
                foreach (List<ActionSpec> inner in Nested(a)) AddTrait(inner, key, v, depth + 1);
            }
        }

        // ---- casting ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Runs the form at cast time, after the skill has picked its victims. True = the form took the actions away
        /// (a trap, a monster, a drifter) and the skill's own hit loop must not run.
        /// </summary>
        internal static bool Deliver(Runes.Tuning t, ModAbility ab, ActionContext ctx, List<Character> victims, Vector3 center, Vector3 from, float radius, List<ActionSpec> targetActions)
        {
            if (t?.form == null) return false;
            string name = string.IsNullOrEmpty(t.formName) ? ab.GetDisplayName() : t.formName;
            var laid = new ActionContext
            {
                caster = ctx.caster, source = ctx.source, classId = ctx.classId, sourceName = ab.GetDisplayName(), center = center,
                rank = ctx.rank, damageMult = ctx.damageMult, chanceBonus = ctx.chanceBonus, hitVfx = ctx.hitVfx,
            };
            switch (t.form)
            {
                case "trap":
                    Traps.Lay(new ActionSpec
                    {
                        @do = "trap", fieldName = name, affects = "enemies", armTurns = Math.Max(0, t.armTurns), charges = t.charges == 0 ? 1 : t.charges,
                        turns = t.turns > 0 ? t.turns : 8, fuse = t.fuse, triggerRadius = t.triggerRadius, radius = radius, look = t.look, model = t.model,
                        vfx = t.areaVfx ?? ctx.hitVfx, sound = t.sound, actions = targetActions.ToList(),
                    }, laid, null);
                    victims.Clear();
                    return true;
                case "monster":
                    {
                        var before = new HashSet<Character>(Summons.Mine(Summons.OwnerTag(ctx)));
                        Summons.Summon(new ActionSpec
                        {
                            @do = "summon", creature = t.creature ?? "PA_Engineer_Spidermine.characterPrefab", amount = 1, turns = t.turns > 0 ? t.turns : 4,
                            summonName = t.summonName ?? name, model = t.model, summonVfx = t.castVfx,
                        }, ctx, null, 1, center);
                        foreach (Character c in Summons.Mine(Summons.OwnerTag(ctx)).Where(c => !before.Contains(c)))
                        {
                            var load = new Payload
                            {
                                id = NextId(), name = name, actions = targetActions.ToList(), ctx = laid, radius = Mathf.Max(radius, DefaultRadius("monster")),
                                vfx = t.areaVfx ?? ctx.hitVfx, sound = t.sound, contact = t.burstOn == "contact",
                            };
                            Payloads[c] = load;
                            c.SetTag(PayloadTag, load.id); // tags are saved with the character, so the spell finds it again after a load
                            Game.LOG($"<color=#E59CFF>{c.GetName()}</color> carries {ab.GetDisplayName()} and bursts into it {(t.burstOn == "contact" ? "when an enemy comes close" : "when it dies")}.");
                        }
                        Record();
                        victims.Clear();
                        return true;
                    }
                case "drifter":
                    Drifters.Launch(new ActionSpec
                    {
                        @do = "drifter", fieldName = name, vfx = t.travelVfx ?? t.areaVfx ?? ctx.hitVfx, turns = t.turns > 0 ? t.turns : 4,
                        steps = t.steps, stepChance = t.stepChance, radius = Mathf.Max(radius, DefaultRadius("drifter")), actions = targetActions.ToList(),
                    }, ctx, victims.FirstOrDefault());
                    victims.Clear();
                    return true;
                case "nova":
                    {
                        // the burst spreads from you to every side: one streak per direction
                        string fx = t.travelVfx;
                        if (!string.IsNullOrEmpty(fx) && ctx.caster != null)
                        {
                            Vector3 at = ctx.caster.GetHitFx().position;
                            for (int d = 0; d < 8; d++)
                                Actions.SpawnTravel(fx, at, from + Quaternion.Euler(0f, 45f * d, 0f) * Vector3.forward * Mathf.Max(1f, radius) + Vector3.up * 0.9f, 14f);
                        }
                        return false;
                    }
                case "scatter":
                    {
                        // the aimed foe and random others within the skill's reach
                        if (AI_Manager.instance == null || ctx.caster == null) return false;
                        int extra = Math.Max(1, t.jumps > 0 ? t.jumps : 2);
                        var others = AI_Manager.instance.GetEnemiesAt(from, Mathf.Max(2f, ab.range) + 0.5f)
                            .Where(c => c != null && !c.GetIsDead() && c != ctx.caster && !victims.Contains(c) && !TileStacks.IsAlly(c))
                            .OrderBy(_ => UnityEngine.Random.value).Take(extra).ToList();
                        foreach (Character c in others)
                        {
                            victims.Add(c);
                            if (!string.IsNullOrEmpty(t.travelVfx)) Actions.SpawnTravel(t.travelVfx, ctx.caster.GetHitFx().position, c.GetHitFx().position, 14f);
                        }
                        return false;
                    }
                default:
                    return false;
            }
        }

        /// <summary>Says what a gamble made of the skill as it leaves you.</summary>
        internal static void Announce(Runes.Tuning t, ModAbility ab)
        {
            var drawn = t?.mods?.Where(c => c.rolled && !string.IsNullOrEmpty(c.mod.name)).ToList();
            if (drawn == null || drawn.Count == 0) return;
            Game.LOG($"<color=#E59CFF>{drawn[0].source}:</color> {ab.GetDisplayName()} comes out <color=#FFD15C>{string.Join(", ", drawn.Select(c => c.mod.name))}</color>.");
        }

        /// <summary>After a rewritten cast: effects that are spent by it lose a stack.</summary>
        internal static void Spend(Runes.Tuning t)
        {
            if (t?.mods == null) return;
            foreach (Carried c in t.mods.Where(c => c.mod.consume && c.carrier != null).ToList())
            {
                string nm = c.carrier.GetName();
                c.carrier.RemoveStacks(1);
                Game.LOG($"<color=#9FB7C9>{nm} is spent.</color>");
            }
        }

        // ---- monsters that burst into a spell ---------------------------------------------------------------------

        private class Payload
        {
            public int id;
            public string name, vfx, sound;
            public List<ActionSpec> actions;
            public ActionContext ctx;
            public float radius;
            public bool contact;
        }

        /// <summary>On a carrier: the id of its payload record (SideAreas.Data "payloads"), kept through saves like a summon's lifetime.</summary>
        internal const string PayloadTag = "SCCL_PAYLOAD";
        private const string Key = "payloads";

        private static readonly Dictionary<Character, Payload> Payloads = new Dictionary<Character, Payload>();

        /// <summary>Summons carrying a spell right now, loaded ones included (tests).</summary>
        internal static int Loaded => AI_Manager.instance == null ? 0
            : AI_Manager.instance.GetPlayerMinions().Count(c => c != null && !c.GetIsDead() && c.HasTag(PayloadTag) && Resolve(c) != null);

        private static int NextId()
        {
            var data = SideAreas.Data;
            int n = data != null && data.TryGetValue(Key + "_next", out object v) && v is int i ? i : 1;
            if (data != null) data[Key + "_next"] = n + 1;
            return n;
        }

        /// <summary>The spells carried, kept in the save so a loaded game's carriers still go off.</summary>
        private static void Record()
        {
            var data = SideAreas.Data;
            if (data == null) return;
            data[Key] = Payloads.Where(kv => kv.Key != null && !kv.Key.GetIsDead()).Select(kv => new Dictionary<string, object>
            {
                ["id"] = kv.Value.id, ["name"] = kv.Value.name, ["class"] = kv.Value.ctx.classId ?? "", ["source"] = kv.Value.ctx.source?.internalName ?? "",
                ["sourceName"] = kv.Value.ctx.sourceName ?? kv.Value.name, ["actions"] = Newtonsoft.Json.JsonConvert.SerializeObject(kv.Value.actions),
                ["rank"] = kv.Value.ctx.rank, ["mult"] = kv.Value.ctx.damageMult, ["chance"] = kv.Value.ctx.chanceBonus, ["hitVfx"] = kv.Value.ctx.hitVfx ?? "",
                ["radius"] = kv.Value.radius, ["vfx"] = kv.Value.vfx ?? "", ["sound"] = kv.Value.sound ?? "", ["contact"] = kv.Value.contact,
            }).ToList();
        }

        /// <summary>The payload a carrier holds: the live one, or one rebuilt from the save by the id in its tag.</summary>
        private static Payload Resolve(Character c)
        {
            if (c == null) return null;
            if (Payloads.TryGetValue(c, out Payload p)) return p;
            if (!c.HasTag(PayloadTag)) return null;
            int id = c.GetTag(PayloadTag);
            var data = SideAreas.Data;
            if (data == null || !(data.TryGetValue(Key, out object v) && v is List<Dictionary<string, object>> list)) return null;
            var d = list.FirstOrDefault(x => x.TryGetValue("id", out object i) && i is int n && n == id);
            if (d == null) { c.RemoveTag(PayloadTag); return null; }
            try
            {
                string src = d["source"] as string;
                Ability source = string.IsNullOrEmpty(src) || Player_AbilityControl.instance == null ? null
                    : Player_AbilityControl.instance.GetAbilities().FirstOrDefault(a => a != null && a.internalName == src);
                p = new Payload
                {
                    id = id, name = d["name"] as string, radius = (float)d["radius"], contact = (bool)d["contact"],
                    vfx = (d["vfx"] as string) is string fx && fx.Length > 0 ? fx : null, sound = (d["sound"] as string) is string sd && sd.Length > 0 ? sd : null,
                    actions = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ActionSpec>>((string)d["actions"]) ?? new List<ActionSpec>(),
                    ctx = new ActionContext
                    {
                        caster = Player.instance, source = source, classId = d["class"] as string, sourceName = d["sourceName"] as string,
                        rank = (int)d["rank"], damageMult = (float)d["mult"], chanceBonus = (int)d["chance"], hitVfx = (d["hitVfx"] as string) is string hv && hv.Length > 0 ? hv : null,
                    },
                };
                Payloads[c] = p;
                return p;
            }
            catch (Exception e) { Plugin.Log.LogError($"carried spell {id} could not be restored: {e.Message}"); c.RemoveTag(PayloadTag); return null; }
        }

        /// <summary>The last bursts ("Ember Lance: 2"), for tests.</summary>
        internal static readonly List<string> Bursts = new List<string>();

        private static void Burst(Character c, Payload p)
        {
            Payloads.Remove(c);
            c.RemoveTag(PayloadTag);
            Record();
            if (Map.instance == null || AI_Manager.instance == null) return;
            NavNode node = c.GetMover()?.GetCurrentNavNode();
            Vector3 at = node != null ? node.transform.position : c.transform.position;
            var caught = AI_Manager.instance.GetEnemiesAt(at, p.radius).Where(v => v != null && !v.GetIsDead() && v != c && v != p.ctx.caster && !TileStacks.IsAlly(v)).ToList();
            Bursts.Add($"{p.name}: {caught.Count}");
            if (Bursts.Count > 40) Bursts.RemoveAt(0);
            Game.LOG($"<color=#E59CFF>{c.GetName()}</color> bursts into <color=#FFD15C>{p.name}</color>{(caught.Count > 0 ? $", catching {caught.Count}" : "")}!");
            Actions.SpawnVfx(p.vfx, c.GetHitFx().position);
            ModAudio.Play(p.sound, at, p.ctx.classId);
            p.ctx.center = at;
            p.ctx.groundAtCenter = true;
            var self = p.actions.Where(a => (a.to ?? "").ToLowerInvariant() == "self").ToList();
            var rest = p.actions.Except(self).ToList();
            foreach (Character v in caught) { p.ctx.target = v; Actions.Run(rest, p.ctx, v); }
            if (caught.Count == 0) Actions.Run(rest.Where(a => a.@do == "field" || a.@do == "hazard" || a.@do == "trap").ToList(), p.ctx, null);
            Actions.Run(self, p.ctx, p.ctx.caster);
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_CarrierDies
        {
            private static void Prefix(Character __instance)
            {
                if (__instance == null || __instance.GetIsDead() || Game.Game_isLoading) return;
                Payload p = Resolve(__instance);
                if (p == null) return;
                try { Burst(__instance, p); } catch (Exception e) { Plugin.Log.LogError($"{p.name} burst: {e}"); }
            }
        }

        /// <summary>Each of your turns: contact carriers beside an enemy go off; dead ones are forgotten. Carriers from a
        /// loaded game pick their spell up again here (and in the death patch) by the id in their tag.</summary>
        private static void Tick()
        {
            if (AI_Manager.instance == null || Game.Game_isLoading) return;
            bool changed = false;
            foreach (Character m in AI_Manager.instance.GetPlayerMinions())
                if (m != null && !m.GetIsDead() && m.HasTag(PayloadTag) && !Payloads.ContainsKey(m)) Resolve(m);
            foreach (var kv in Payloads.ToList())
            {
                Character c = kv.Key;
                if (c == null || c.GetIsDead()) { Payloads.Remove(c); changed = true; continue; }
                if (!kv.Value.contact) continue;
                Vector3 at = c.GetMover()?.GetCurrentNavNode()?.transform.position ?? c.transform.position;
                if (AI_Manager.instance.GetEnemiesAt(at, 1.5f).Any(v => v != null && !v.GetIsDead() && v != c && !TileStacks.IsAlly(v)))
                {
                    Actions.SpawnVfx("PA_Engineer_Boltron.PS_summon", c.GetHitFx().position);
                    c.Die();
                }
            }
            if (changed) Record();
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
        private static class Patch_Turn
        {
            private static void Postfix() { try { Tick(); } catch (Exception e) { Plugin.Log.LogError("skill rewrites: " + e); } }
        }

        internal static void ClearPayloads() { Payloads.Clear(); Record(); } // devtests

        // ---- rules text ------------------------------------------------------------------------------------------

        /// <summary>The form, aim, swaps and behaviors of a rune or rewrite, in one line (Runes.Describe adds the numbers).</summary>
        internal static string DescribeShape(RuneSpec m, string classId)
        {
            if (m == null) return "";
            var bits = new List<string>();
            float r = m.radius > 0f ? m.radius : DefaultRadius(m.form ?? "");
            string area = r > 0f ? $"everyone within {r:0.#}" : "whoever sets it off";
            switch (m.form)
            {
                case "trap":
                    bits.Add($"laid as a trap where you aim: it {(m.armTurns > 0 ? $"arms in {m.armTurns} turn{(m.armTurns > 1 ? "s" : "")} and " : "")}goes off under the first enemy to step on it" +
                             (r > 0f ? $", hitting {area}" : "") + (m.fuse > 0 ? $", or by itself after {m.fuse} turns" : "") + $" (lasts {(m.turns > 0 ? m.turns : 8)} turns)");
                    break;
                case "grenade": bits.Add($"thrown at a spot, bursting over {area}"); break;
                case "monster":
                    bits.Add($"calls {Summons.NameOf(m.creature ?? "PA_Engineer_Spidermine.characterPrefab", m.summonName)} where you aim, which bursts into it over {area} " +
                             (m.burstOn == "contact" ? "as soon as an enemy stands beside it" : "when it dies") + $" (it lasts {(m.turns > 0 ? m.turns : 4)} turns)");
                    break;
                case "drifter": bits.Add($"let loose to wander the deck for {(m.turns > 0 ? m.turns : 4)} turns, going off on whoever it meets, friend or foe"); break;
                case "nova": bits.Add($"a nova around you, hitting {area}, no aiming"); break;
                case "line": bits.Add("a line from you that pierces everyone on it"); break;
                case "cone": bits.Add("a cone from you"); break;
                case "area": bits.Add($"bursts over {area}"); break;
                case "chain": bits.Add("hops on"); break;
                case "scatter": bits.Add($"also leaps to {(m.jumps > 0 ? m.jumps : 2)} random other enemies in reach"); break;
            }
            if (!string.IsNullOrEmpty(m.target) && m.form == null) bits.Add($"aimed at {(m.target == "self" ? "you" : m.target == "floor" ? "a spot" : "an enemy")}");
            if (m.instant == true) bits.Add("instant");
            if (m.instant == false) bits.Add("ends your turn");
            foreach (var kv in m.damage ?? new Dictionary<string, string>()) bits.Add($"{kv.Key} damage becomes {kv.Value}");
            foreach (var kv in m.effects ?? new Dictionary<string, string>()) bits.Add($"applies {Runes.EffectName(kv.Value, classId)} instead of {Runes.EffectName(kv.Key, classId)}");
            string gains = DescribeAdds(m.add, classId);
            if (gains.Length > 0) bits.Add("gains: " + gains);
            string loses = DescribeRemoves(m.remove, classId);
            if (loses.Length > 0) bits.Add("loses: " + loses);
            return string.Join(", ", bits);
        }

        private static string DescribeAdds(Dictionary<string, JToken> add, string classId)
        {
            if (add == null || add.Count == 0) return "";
            var bits = new List<string>();
            int fxTurns = add.TryGetValue("effectTurns", out JToken ft) ? I(ft) : 0;
            foreach (var kv in add)
            {
                JToken v = kv.Value;
                switch (kv.Key.ToLowerInvariant())
                {
                    case "resetonkill": bits.Add(B(v) ? "a kill makes it ready again" : "no reset on a kill"); break;
                    case "instant": bits.Add(B(v) ? "instant" : "ends your turn"); break;
                    case "dodgeable": bits.Add(B(v) ? "can be dodged" : "can't be dodged"); break;
                    case "melee": bits.Add(B(v) ? "a melee swing" : "no melee swing"); break;
                    case "command": bits.Add("usable in any transformation"); break;
                    case "reactionslots": bits.Add($"{I(v)} reaction slots"); break;
                    case "pierce": if (B(v)) bits.Add(v.Type == JTokenType.Boolean ? "pierces everyone in its line" : $"flies on through {I(v)} {(I(v) > 1 ? "enemies" : "enemy")}"); break;
                    case "jumps": bits.Add($"jumps to {I(v)} more {(I(v) > 1 ? "enemies" : "enemy")}"); break;
                    case "lifesteal": bits.Add($"heals you for {Mathf.RoundToInt(F(v) * 100)}% of its damage"); break;
                    case "truedamage": bits.Add(B(v) ? "true damage (ignores armor, shields and resistances)" : "ordinary damage"); break;
                    case "weapon": bits.Add($"+{Mathf.RoundToInt(F(v) * 100)}% of a weapon roll"); break;
                    case "scale": bits.Add($"also scales with {S(v)}"); break;
                    case "scalemult": bits.Add($"{Mathf.RoundToInt(F(v) * 100)}% per stat point"); break;
                    case "perrank": bits.Add($"+{I(v)} damage per rank"); break;
                    case "bonus": bits.Add($"{I(v):+#;-#} damage"); break;
                    case "chance": bits.Add($"damage lands {I(v)}% of the time"); break;
                    case "levelcap": bits.Add($"full strength at level {I(v)}"); break;
                    case "stun": bits.Add($"stuns {I(v)} turn{(I(v) > 1 ? "s" : "")}"); break;
                    case "knockback": bits.Add($"knocks back {I(v)}"); break;
                    case "pull": bits.Add($"pulls {I(v)} toward you"); break;
                    case "blind": bits.Add($"blinds {I(v)} turn{(I(v) > 1 ? "s" : "")}"); break;
                    case "silence": bits.Add($"silences {I(v)} turn{(I(v) > 1 ? "s" : "")}"); break;
                    case "taunt": bits.Add($"taunts {I(v)} turn{(I(v) > 1 ? "s" : "")}"); break;
                    case "exhaust": bits.Add($"drains {I(v)} stamina"); break;
                    case "purge": bits.Add($"removes {Runes.EffectName(S(v), classId)}"); break;
                    case "effect": bits.Add($"applies {string.Join(", ", Many(v).Select(x => Runes.EffectName(x, classId)))}{(fxTurns > 0 ? $" for {fxTurns} turns" : "")}"); break;
                    case "effectturns": break;
                    case "execute": bits.Add($"{I(v)}% to finish the target outright"); break;
                    case "hazard": bits.Add($"leaves {GroundFields.HazardName(S(v))}"); break;
                    case "swap": if (B(v)) bits.Add("swaps places with the target"); break;
                    case "heal": bits.Add($"heals you {I(v)}"); break;
                    case "shield": bits.Add($"shields you for {I(v)}"); break;
                    case "stamina": bits.Add($"restores {I(v)} stamina"); break;
                    case "cooldowns": bits.Add($"your other cooldowns -{I(v)}"); break;
                    case "selfeffect": bits.Add($"gives you {string.Join(", ", Many(v).Select(x => Runes.EffectName(x, classId)))}"); break;
                }
            }
            return string.Join("; ", bits);
        }

        private static string DescribeRemoves(List<string> remove, string classId)
        {
            if (remove == null || remove.Count == 0) return "";
            return string.Join("; ", remove.Where(r => !string.IsNullOrEmpty(r)).Select(r =>
            {
                string k = r.ToLowerInvariant(), arg = null;
                int colon = r.IndexOf(':');
                if (colon > 0) { arg = r.Substring(colon + 1).Trim(); k = k.Substring(0, colon); }
                switch (k)
                {
                    case "effect": return arg == null ? "every effect it applies" : "its " + Runes.EffectName(arg, classId);
                    case "damage": return arg == null ? "its damage" : $"its {arg} damage";
                    case "chain": return "its hops (the hits stay)";
                    case "self": return "what it does to you";
                    case "radius": case "area": return "its area (one target)";
                    case "shape": return "its shape";
                    case "travel": return "its flight";
                    case "resetonkill": return "its reset on a kill";
                    case "lifesteal": return "its life steal";
                    case "truedamage": return "its true damage";
                    case "weapon": return "its weapon share";
                    case "scale": return "its stat scaling";
                    case "perrank": return "its growth per rank";
                    case "stored": return "its stored damage";
                    case "summon": return arg == null ? "its summons" : $"its {arg}";
                    case "field": case "trap": return arg == null ? $"its {k}s" : $"its {arg}";
                    default: return $"its {k}{(arg != null ? " " + arg : "")}";
                }
            }));
        }

        /// <summary>One line of rules text for a rewrite, as it applies to one skill.</summary>
        internal static string Describe(SkillModSpec m, string classId)
        {
            if (m == null) return "";
            var bits = new List<string>();
            string rune = Runes.Describe(m, classId);
            if (rune.Length > 0) bits.Add(rune);
            if (m.roll != null && string.IsNullOrEmpty(m.summary))
                bits.Add($"{(m.chance < 100 ? m.chance + "% for " : "")}one of {string.Join(", ", m.roll.Where(o => o != null).Select(o => o.name))}, drawn for each skill and again after each cast");
            if (m is AugmentSpec aug)
            {
                if (aug.seal > 0) bits.Add($"<color=#FF9F43>seals {aug.seal} rune socket{(aug.seal > 1 ? "s" : "")}</color>");
                if (!string.IsNullOrEmpty(aug.drawback)) bits.Add($"<color=#FF9F43>{aug.drawback}</color>");
            }
            if (m.consume) bits.Add("spent by the cast");
            if (m.when != null) bits.Add($"only while {When.Describe(m.when, classId)}");
            return string.Join(", ", bits);
        }

        /// <summary>The tooltip lines of a skill's rewrites in force, one per source.</summary>
        internal static string Lines(Runes.Tuning t, string classId)
        {
            if (t?.mods == null || t.mods.Count == 0) return "";
            return string.Join("\n", t.mods.Select(c =>
                $"<color=#E59CFF>Reshaped by {c.source}{(string.IsNullOrEmpty(c.mod.name) ? "" : " (" + c.mod.name + ")")}:</color> {Describe(c.mod, c.classId)}."));
        }

        /// <summary>For a passive's or effect's own tooltip: what it does to which skills.</summary>
        internal static string Summary(List<SkillModSpec> mods, string classId)
        {
            if (mods == null || mods.Count == 0) return "";
            return string.Join("\n", mods.Where(m => m != null).Select(m =>
            {
                string which = string.IsNullOrEmpty(m.skills) || m.skills == "class" ? "this class's skills" : m.skills == "*" ? "all your skills"
                    : m.skills == "attacks" ? "your attacks" : m.skills == "others" ? "your other classes' attacks"
                    : m.skills.StartsWith("class:", StringComparison.OrdinalIgnoreCase) ? ClassRegistry.AllDefs.FirstOrDefault(d => string.Equals(d.id, m.skills.Substring(6), StringComparison.OrdinalIgnoreCase))?.name + " skills"
                    : m.skills.Replace("|", ", ");
                return $"<color=#E59CFF>Reshapes {which}{(string.IsNullOrEmpty(m.name) ? "" : " (" + m.name + ")")}:</color> {Describe(m, classId)}.";
            }));
        }
    }
}
