using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// A condition written in a class file ("when" on actions and procs, "usableWhen" / "targetWhen" on abilities).
    /// Every key set on one node must hold; "all" / "any" / "not" combine nodes. A plain string names a condition
    /// from a library's "conditions" (e.g. "when": "Bloodied").
    ///
    /// Value checks read a number and compare it:
    ///   { "value": "health%", "of": "target", "below": 30 }
    /// Values: health, health%, stamina, stamina%, shield, armor (any stat by name, e.g. "Strength"),
    /// stacks:EFFECT, counter:NAME, enemiesNear:R, alliesNear:R, corpses:R, summons, summons:GROUP, rank:TREE, level, credits,
    /// distance (caster to target), turnsInCombat. "equipped" checks your gear (see Equipped).
    /// </summary>
    [JsonConverter(typeof(WhenConverter))]
    public class WhenSpec
    {
        public List<WhenSpec> all;
        public List<WhenSpec> any;
        public WhenSpec not;
        /// <summary>A named condition from a library ("conditions").</summary>
        public string use;

        // value check
        public string value;
        /// <summary>self (default for value checks with no target) | target | attacker | victim</summary>
        public string of;
        public float? below, above, atLeast, atMost, @is;

        // shortcuts
        public string has;
        public string lacks;
        /// <summary>Character tag (BOSS, ROBOT, ...); "elite" / "boss" match every elite, rare and boss (Elites.IsElite).</summary>
        public string tag;
        public string skill;
        public int skillRank = 1;
        public bool? inCombat;
        /// <summary>In a side area or not.</summary>
        public bool? inSide;
        public bool? melee;
        /// <summary>The skill being used: vanilla, mod, item, attack, self, aimed, melee, cooldown, instant or its name.
        /// "a|b" = either, "a+b" = both, "!a" = not.</summary>
        public string cast;
        /// <summary>true = a skill of yours is going off right now, false = not (a trap or a damage tick between casts).</summary>
        public bool? casting;
        /// <summary>Gear equipped by whoever is checked (default you): "melee", "ranged|shield", "!heavy" (see Equipped).</summary>
        public string equipped;
        /// <summary>true = only once this skill's perk is bought, false = only before (a perk that changes what the skill does).</summary>
        public bool? perk;
        /// <summary>Creature kind of whoever is checked: Monster, Humanoid, Mechanical, Supernatural ("A|B" = either).</summary>
        public string kind;
        /// <summary>"kind" read once: a check per hit shouldn't split the text and name the enum each time.</summary>
        internal CHARACTER_TYPE[] kinds;
        /// <summary>true = whoever is checked (default the target) hasn't noticed anyone yet: it's idle, as the game's
        /// Assassin Strike reads it; false = it's alert.</summary>
        public bool? unaware;
        /// <summary>"you" = whoever is checked (default the target) has you in its field of view; "away" = it's turned
        /// from you (you're behind or beside it).</summary>
        public string facing;
        public int chance = 100;
        /// <summary>Stats that add to `chance` (a control roll on "PsiPower+Smarts"), `chancePer` points of chance each.</summary>
        public string chanceScale;
        public float chancePer = 1f;
    }

    /// <summary>Lets a condition be written as a plain string ("Bloodied") meaning { "use": "Bloodied" }.</summary>
    internal class WhenConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(WhenSpec);
        public override bool CanWrite => false;

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JToken tok = JToken.Load(reader);
            if (tok.Type == JTokenType.Null) return null;
            if (tok.Type == JTokenType.String) return new WhenSpec { use = (string)tok };
            if (tok is JArray arr) return new WhenSpec { all = arr.Select(x => x.ToObject<WhenSpec>(serializer)).ToList() };
            var w = new WhenSpec();
            // fill without re-entering this converter
            using (var r = tok.CreateReader()) JsonSerializer.CreateDefault().Populate(r, w);
            return w;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotSupportedException();
    }

    internal static class When
    {
        /// <summary>Named conditions from libraries.</summary>
        internal static readonly Dictionary<string, WhenSpec> Named = new Dictionary<string, WhenSpec>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Does the condition hold? Null = always.</summary>
        internal static bool Check(WhenSpec w, ActionContext ctx, Character target, int depth = 0)
        {
            if (w == null) return true;
            if (depth > 12) { Plugin.Log.LogWarning("A condition nests too deep (a named condition using itself?)"); return false; }
            if (!string.IsNullOrEmpty(w.use))
            {
                if (Named.TryGetValue(w.use, out WhenSpec n))
                {
                    if (!Check(n, ctx, target, depth + 1)) return false;
                }
                else
                {
                    bool? mine = PluginHost.Condition(w.use, ctx, Who(w.of, ctx, target, "target"));
                    if (mine == null) Plugin.Log.LogWarning($"Unknown condition '{w.use}'");
                    if (mine != true) return false;
                }
            }
            if (w.all != null && !w.all.All(x => Check(x, ctx, target, depth + 1))) return false;
            if (w.any != null && w.any.Count > 0 && !w.any.Any(x => Check(x, ctx, target, depth + 1))) return false;
            if (w.not != null && Check(w.not, ctx, target, depth + 1)) return false;

            Character who = Who(w.of, ctx, target, w.value == null && (w.has != null || w.lacks != null || w.tag != null || w.kind != null || w.unaware.HasValue || w.facing != null) ? "target" : "self");
            if (!string.IsNullOrEmpty(w.has) && (who == null || Actions.StacksOn(who, Actions.EffectId(w.has, ctx.classId)) <= 0)) return false;
            if (!string.IsNullOrEmpty(w.lacks) && who != null && Actions.StacksOn(who, Actions.EffectId(w.lacks, ctx.classId)) > 0) return false;
            if (!string.IsNullOrEmpty(w.tag) && (who == null || !HasTag(who, w.tag))) return false;
            if (!string.IsNullOrEmpty(w.skill) && Synergies.TreeLevel(w.skill, ctx.classId) < Math.Max(1, w.skillRank)) return false;
            if (w.inCombat.HasValue && InCombat() != w.inCombat.Value) return false;
            if (w.inSide.HasValue && SideAreas.InSide != w.inSide.Value) return false;
            if (!string.IsNullOrEmpty(w.cast) && !IsCast(w.cast, Casting(ctx))) return false;
            if (w.casting.HasValue && CastRules.InCast != w.casting.Value) return false;
            if (!string.IsNullOrEmpty(w.equipped) && !Equipped.Check(w.equipped, Who(w.of, ctx, target, "self"))) return false;
            if (w.melee.HasValue && (ctx.source == null || ctx.source.isMelee != w.melee.Value)) return false;
            if (w.perk.HasValue && ((ctx.source is ModAbility pm && pm.Perked) != w.perk.Value)) return false;
            if (!string.IsNullOrEmpty(w.kind) && (who == null || !IsKind(w, who))) return false;
            if (w.unaware.HasValue && (who?.ai == null || (who.ai.GetState() == AI.AI_STATE.Idle) != w.unaware.Value)) return false;
            if (!string.IsNullOrEmpty(w.facing) && (who == null || FacesYou(who, ctx) != !w.facing.Equals("away", StringComparison.OrdinalIgnoreCase))) return false;
            if (!string.IsNullOrEmpty(w.value))
            {
                float? v = Value(w.value, ctx, who, target);
                if (v == null) return false;
                float x = v.Value;
                if (w.below.HasValue && !(x < w.below.Value)) return false;
                if (w.above.HasValue && !(x > w.above.Value)) return false;
                if (w.atLeast.HasValue && !(x >= w.atLeast.Value)) return false;
                if (w.atMost.HasValue && !(x <= w.atMost.Value)) return false;
                if (w.@is.HasValue && Mathf.Abs(x - w.@is.Value) > 0.001f) return false;
            }
            // last, so a failed check above doesn't use up the roll
            if (!string.IsNullOrEmpty(w.chanceScale))
            {
                int odds = Mathf.Clamp(Mathf.RoundToInt(w.chance + Actions.StatBonus(w.chanceScale, w.chancePer, ctx.caster)), 5, 95);
                if (Random.Range(1, 101) > odds + ctx.chanceBonus) return false;
            }
            else if (w.chance < 100 && Random.Range(1, 101) > w.chance + ctx.chanceBonus) return false;
            return true;
        }

        /// <summary>The skill in question: the one acting, or for a proc or an effect the one you are using right now.</summary>
        private static Ability Casting(ActionContext ctx) => ctx.source != null && !ctx.source.isPassive ? ctx.source : Game.PLAYER_ABILITY != null ? Game.PLAYER_ABILITY : ctx.source;

        internal static bool IsCast(string cast, Ability a)
        {
            if (a == null) return false;
            return cast.Split('|').Any(any => any.Split('+').All(one =>
            {
                string t = one.Trim();
                return t.StartsWith("!") ? !IsCastWord(t.Substring(1).Trim(), a) : IsCastWord(t, a);
            }));
        }

        private static bool IsCastWord(string word, Ability a)
        {
            switch (Lower(word))
            {
                case "vanilla": return !(a is ModAbility);
                case "mod": return a is ModAbility;
                case "item": return a.GetComponentInParent<Item>() != null || (Menu_Player.instance != null && Menu_Player.instance.GetEquipment().Any(i => i != null && i.equipTrigger != null && i.equipTrigger.equipment_ability == a));
                case "attack": return a is ModAbility m ? SkillMods.IsAttack(m.Spec?.actions) : a.GetTargetStyle() == TARGET_STYLE.Character || a.GetTargetStyle() == TARGET_STYLE.FloorCharacter;
                case "self": return a.GetTargetStyle() == TARGET_STYLE.None;
                case "aimed": return a.GetTargetStyle() != TARGET_STYLE.None;
                case "melee": return a.isMelee;
                case "cooldown": return a.cooldown > 0;
                case "instant": return a.isInstant;
                default: return string.Equals(word, a.internalName, StringComparison.OrdinalIgnoreCase) || string.Equals(word, a.GetDisplayName(), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static int Effects(Character who, bool debuffs)
        {
            int n = 0;
            foreach (Effect e in who.effects.effects)
                if (e != null && !e.isHidden && e.isDebuff == debuffs) n++;
            return n;
        }

        private static float SkillsWaiting()
        {
            var pac = Player_AbilityControl.instance;
            if (pac == null) return 0;
            int n = 0;
            foreach (Ability a in pac.abilities)
                if (a != null && !a.isPassive && a.GetCooldownAmount() > 0) n++;
            return n;
        }

        private static bool IsKind(WhenSpec w, Character who)
        {
            if (w.kinds == null)
                w.kinds = w.kind.Split('|').Select(k => Enum.TryParse(k.Trim(), true, out CHARACTER_TYPE t) ? t : CHARACTER_TYPE.Null).Where(t => t != CHARACTER_TYPE.Null).ToArray();
            foreach (CHARACTER_TYPE t in w.kinds)
                if (t == who.characterType) return true;
            return false;
        }

        /// <summary>Names as written in class files, lowered once.</summary>
        private static readonly Dictionary<string, string> lowered = new Dictionary<string, string>();

        private static string Lower(string s)
        {
            if (!lowered.TryGetValue(s, out string low)) lowered[s] = low = s.ToLowerInvariant();
            return low;
        }

        private static Character Who(string of, ActionContext ctx, Character target, string fallback)
        {
            switch (Lower(of ?? fallback))
            {
                case "self": case "you": return ctx.caster;
                case "attacker": return ctx.attacker;
                case "victim": return ctx.victim;
                case "ally": return ctx.ally;
                // the other one in a proc's event: who you hit, or who hit you
                case "other": return ctx.victim ?? ctx.attacker ?? ctx.target;
                default: return target ?? ctx.target;
            }
        }

        internal static bool HasTag(Character c, string tag)
        {
            string t = tag.ToUpperInvariant();
            if (c.HasTag(t)) return true;
            if (t == "BOSS" || t == "ELITE") return Elites.IsElite(c);
            return false;
        }

        /// <summary>Has whoever is checked got the caster in its field of view, by the game's own sight cone? (You have
        /// none, so for you it's the way you face.)</summary>
        internal static bool FacesYou(Character who, ActionContext ctx)
        {
            Character me = ctx.caster != null && ctx.caster != who ? ctx.caster : Player.instance;
            if (me == null || me == who) return true;
            if (who.ai?.detection != null) return who.ai.detection.GetFacing(me.GetHitFx().position) == "front";
            Vector3 fwd = who == Player.instance && Player_Control.instance != null ? Player_Control.instance.transform.forward : who.transform.forward;
            Vector3 to = me.transform.position - who.transform.position;
            fwd.y = to.y = 0f;
            return Vector3.Angle(fwd, to) <= 60f;
        }

        internal static bool InCombat() => LevelManager.instance != null && LevelManager.instance.IsInCombat();

        /// <summary>A named number about a character (or the fight); null when it can't be read.</summary>
        internal static float? Value(string name, ActionContext ctx, Character who, Character target)
        {
            if (PluginHost.Value(name, ctx, who, out float? fromPlugin)) return fromPlugin;
            if (!valueKeys.TryGetValue(name, out ValueKey parsed)) valueKeys[name] = parsed = new ValueKey(name);
            string arg = parsed.arg;
            switch (parsed.key)
            {
                case "health": return who?.GetHealth();
                case "health%": return who == null ? (float?)null : 100f * who.GetHealth() / Mathf.Max(1, who.GetHealthMax());
                case "stamina": return who?.GetStamina();
                case "stamina%": return who == null ? (float?)null : 100f * who.GetStamina() / Mathf.Max(1, who.GetStat(STAT_TYPE.Stamina));
                case "shield": return who?.oshielding;
                case "healthlost": return who == null ? (float?)null : who.GetHealthMax() - who.GetHealth();
                case "buffs": return who == null || who.effects == null ? (float?)null : Effects(who, false);
                case "debuffs": return who == null || who.effects == null ? (float?)null : Effects(who, true);
                case "skillscooling": return SkillsWaiting();
                case "cost": return Casting(ctx)?.GetStaminaCost();
                case "cooldown": return Casting(ctx)?.cooldown;
                case "floor": return Map.instance != null ? Map.instance.floor : 0;
                case "stacks": return who == null || arg == null ? (float?)null : Actions.StacksOn(who, Actions.EffectId(arg, ctx.classId));
                case "counter": return who == null || arg == null ? (float?)null : Counters.Get(who, arg);
                case "enemiesnear": return Near(who, Radius(arg), enemies: true);
                case "alliesnear": return Near(who, Radius(arg), enemies: false);
                case "corpses": return who == null ? (float?)null : Corpses.Near(who.transform.position, string.IsNullOrEmpty(arg) ? 3f : Radius(arg)).Count;
                case "summons": return Summons.Matching(string.IsNullOrEmpty(arg) ? "any" : (arg.StartsWith("group:", StringComparison.OrdinalIgnoreCase) ? arg : "group:" + arg), ctx).Count;
                case "rank": return Synergies.TreeLevel(arg, ctx.classId);
                case "level": return Player.instance != null ? Player.instance.level : 0;
                case "credits": return Menu_Inventory.instance != null ? Menu_Inventory.instance.GetCredits() : 0;
                case "distance":
                    {
                        Character a = ctx.caster, b = target ?? ctx.target;
                        return a == null || b == null ? (float?)null : Vector3.Distance(a.transform.position, b.transform.position);
                    }
                case "turnsincombat": return Counters.TurnsInCombat;
                case "rushed": return ctx.rushed;
                case "rounds": return Triggers.Rounds;
                case "wallsnear": return who == null ? (float?)null : Walls.Count(who);
                default:
                    if (who != null && parsed.stat.HasValue) return who.GetStat(parsed.stat.Value);
                    Plugin.Log.LogWarning($"Unknown condition value '{name}'");
                    return null;
            }
        }

        /// <summary>A value's name taken apart once: "stacks:Bleed" is checked per hit, and naming a stat by text is the slowest step of all.</summary>
        private sealed class ValueKey
        {
            internal readonly string key, arg;
            internal readonly STAT_TYPE? stat;

            internal ValueKey(string name)
            {
                int colon = name.IndexOf(':');
                string written = colon > 0 ? name.Substring(0, colon) : name;
                key = written.ToLowerInvariant();
                arg = colon > 0 ? name.Substring(colon + 1) : null;
                if (Enum.TryParse(written, true, out STAT_TYPE st)) stat = st;
            }
        }

        private static readonly Dictionary<string, ValueKey> valueKeys = new Dictionary<string, ValueKey>();

        private static float Radius(string arg) =>
            float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float r) && r > 0 ? r : 2.5f;

        private static float? Near(Character who, float radius, bool enemies)
        {
            if (who == null || AI_Manager.instance == null) return null;
            Vector3 at = who.transform.position;
            bool side = TileStacks.IsAlly(who);
            int n = 0;
            // enemies of the one being asked about: for you and your allies that's the hostiles
            foreach (Character c in Contracts.AllCharacters())
            {
                if (c == who || c == Player.instance || c.GetIsDead() || (TileStacks.IsAlly(c) == side) == enemies) continue;
                if (enemies && (c.ai == null || c.ai.GetTeam() == AI.AI_TEAM.Passive)) continue;
                if (Vector3.Distance(c.transform.position, at) <= radius) n++;
            }
            Player you = Player.instance;
            if (!enemies && you != null && you != who && !you.GetIsDead() && TileStacks.IsAlly(you) == side && Vector3.Distance(you.transform.position, at) <= radius) n++;
            return n;
        }

        // ---- rules text ---------------------------------------------------------------------------------------

        internal static string Describe(WhenSpec w, string classId)
        {
            if (w == null) return "";
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(w.use)) parts.Add(Named.TryGetValue(w.use, out WhenSpec n) && n != w ? Describe(n, classId) : PluginHost.ConditionText(w.use) ?? w.use);
            if (w.all != null) parts.AddRange(w.all.Select(x => Describe(x, classId)).Where(s => s.Length > 0));
            if (w.any != null && w.any.Count > 0) parts.Add("(" + string.Join(" or ", w.any.Select(x => Describe(x, classId)).Where(s => s.Length > 0)) + ")");
            if (w.not != null) parts.Add("not " + Describe(w.not, classId));
            string whose = (w.of ?? "").ToLowerInvariant() switch { "self" => "you", "you" => "you", "attacker" => "the attacker", "victim" => "the victim", "ally" => "the ally", "target" => "the target", _ => null };
            if (!string.IsNullOrEmpty(w.has)) parts.Add($"{whose ?? "the target"} {(whose == "you" ? "have" : "has")} {Actions.EffectNameOf(w.has, classId)}");
            if (!string.IsNullOrEmpty(w.lacks)) parts.Add($"{whose ?? "the target"} {(whose == "you" ? "don't have" : "doesn't have")} {Actions.EffectNameOf(w.lacks, classId)}");
            if (!string.IsNullOrEmpty(w.tag)) parts.Add($"{whose ?? "the target"} is {w.tag.ToLowerInvariant()}");
            if (!string.IsNullOrEmpty(w.skill)) parts.Add($"you have {Synergies.TreeNames(w.skill)}{(w.skillRank > 1 ? $" at rank {w.skillRank}+" : "")}");
            if (w.inCombat.HasValue) parts.Add(w.inCombat.Value ? "in combat" : "out of combat");
            if (w.inSide.HasValue) parts.Add(w.inSide.Value ? "in a side area" : "outside side areas");
            if (!string.IsNullOrEmpty(w.cast)) parts.Add("using " + CastWords(w.cast));
            if (w.melee.HasValue) parts.Add(w.melee.Value ? "in melee" : "at range");
            if (!string.IsNullOrEmpty(w.equipped)) parts.Add($"{whose ?? "you"} {(whose == null || whose == "you" ? "have" : "has")} {Equipped.Requirement(w.equipped)}");
            if (w.perk.HasValue) parts.Add(w.perk.Value ? "with the perk" : "without the perk");
            if (!string.IsNullOrEmpty(w.kind)) parts.Add($"{whose ?? "the target"} is {string.Join(" or ", w.kind.Split('|').Select(k => k.Trim().ToLowerInvariant()))}");
            if (w.unaware.HasValue) parts.Add($"{whose ?? "the target"} {(whose == "you" ? "are" : "is")} {(w.unaware.Value ? "unaware of you" : "alert")}");
            if (!string.IsNullOrEmpty(w.facing)) parts.Add(w.facing.Equals("away", StringComparison.OrdinalIgnoreCase)
                ? $"{whose ?? "the target"} {(whose == "you" ? "are" : "is")} turned away from you" : $"{whose ?? "the target"} {(whose == "you" ? "are" : "is")} facing you");
            if (!string.IsNullOrEmpty(w.value))
            {
                string what = ValueName(w.value, classId);
                string owner = w.value.StartsWith("enemiesNear", StringComparison.OrdinalIgnoreCase) || w.value.StartsWith("alliesNear", StringComparison.OrdinalIgnoreCase) || w.value.StartsWith("corpses", StringComparison.OrdinalIgnoreCase)
                    ? (whose == null || whose == "you" ? "" : " around " + whose)
                    : IsGlobal(w.value) ? "" : $" of {whose ?? "yours"}".Replace("of yours", "(yours)").Replace("of you", "(yours)");
                var cmp = new List<string>();
                if (w.below.HasValue) cmp.Add($"below {Num(w.below.Value, w.value)}");
                if (w.above.HasValue) cmp.Add($"above {Num(w.above.Value, w.value)}");
                if (w.atLeast.HasValue) cmp.Add($"at least {Num(w.atLeast.Value, w.value)}");
                if (w.atMost.HasValue) cmp.Add($"at most {Num(w.atMost.Value, w.value)}");
                if (w.@is.HasValue) cmp.Add($"exactly {Num(w.@is.Value, w.value)}");
                if (w.value.StartsWith("wallsNear", StringComparison.OrdinalIgnoreCase))
                {
                    // "the target has at least 1 wall beside it", "you have at most 2 walls beside you"
                    bool you = whose == null || whose == "you";
                    float n = w.atLeast ?? w.atMost ?? w.below ?? w.above ?? w.@is ?? 0f;
                    parts.Add($"{(you ? "you have" : whose + " has")} {string.Join(" and ", cmp)} {(Mathf.Approximately(n, 1f) && !w.above.HasValue ? "wall" : "walls")} beside {(you ? "you" : "it")}");
                }
                else parts.Add($"{what}{owner} {string.Join(" and ", cmp)}".Trim());
            }
            if (!string.IsNullOrEmpty(w.chanceScale)) parts.Add($"a {w.chance}% (+{w.chancePer:0.#}% per point of {w.chanceScale.Replace("+", " and ")}) roll succeeds");
            else if (w.chance < 100) parts.Add($"{w.chance}% of the time");
            return string.Join(", ", parts.Where(s => s.Length > 0));
        }

        private static string CastWords(string cast) =>
            string.Join(" or ", cast.Split('|').Select(any => "a " + string.Join(", ", any.Split('+').Select(one =>
            {
                string t = one.Trim();
                string word = t.TrimStart('!').Trim();
                switch (Lower(word))
                {
                    case "vanilla": word = "vanilla"; break;
                    case "mod": word = "mod"; break;
                    case "item": word = "item"; break;
                    case "self": word = "self-cast"; break;
                    case "cooldown": word = "cooldown"; break;
                }
                return (t.StartsWith("!") ? "non-" : "") + word;
            })) + " skill"));

        internal static string ValueText(string v, string classId) => ValueName(v, classId);

        internal static bool IsGlobalValue(string v) => IsGlobal(v);

        private static bool IsGlobal(string v)
        {
            if (PluginHost.HasValue(v)) return true;
            string k = v.Split(':')[0].ToLowerInvariant();
            return k == "level" || k == "credits" || k == "skillscooling" || k == "cost" || k == "cooldown" || k == "floor" || k == "distance" || k == "turnsincombat" || k == "rank" || k == "summons" || k == "rushed" || k == "rounds";
        }

        private static string Num(float v, string value) => value.EndsWith("%") ? $"{v:0.#}%" : v.ToString("0.#", CultureInfo.InvariantCulture);

        private static string ValueName(string v, string classId)
        {
            if (PluginHost.HasValue(v)) return PluginHost.ValueText(v);
            int colon = v.IndexOf(':');
            string k = colon > 0 ? v.Substring(0, colon) : v, arg = colon > 0 ? v.Substring(colon + 1) : null;
            switch (k.ToLowerInvariant())
            {
                case "health": case "health%": return "health";
                case "stamina": case "stamina%": return "stamina";
                case "shield": return "overshield";
                case "healthlost": return "health lost";
                case "buffs": return "buffs";
                case "debuffs": return "debuffs";
                case "skillscooling": return "your skills on cooldown";
                case "cost": return "the skill's stamina cost";
                case "cooldown": return "the skill's cooldown";
                case "floor": return "the floor";
                case "stacks": return $"{Actions.EffectNameOf(arg, classId)} stacks";
                case "counter": return arg;
                case "enemiesnear": return $"enemies within {Radius(arg):0.#}";
                case "alliesnear": return $"allies within {Radius(arg):0.#}";
                case "corpses": return $"corpses within {(string.IsNullOrEmpty(arg) ? 3f : Radius(arg)):0.#}";
                case "summons": return string.IsNullOrEmpty(arg) ? "your summons" : $"your {arg.Replace("group:", "")}s";
                case "rank": return $"your {Synergies.TreeNames(arg)} rank";
                case "level": return "your level";
                case "credits": return "your credits";
                case "distance": return "distance to the target";
                case "turnsincombat": return "turns in this fight";
                case "rushed": return "tiles you rushed";
                case "rounds": return "rounds the shot fired";
                case "wallsnear": return "walls beside";
                default: return Enum.TryParse(k, true, out STAT_TYPE st) ? StatObj.GetStatString(st) : k;
            }
        }
    }

    /// <summary>
    /// Named counters on characters, for classes that track their own state ("do": "counter"). Yours are saved with
    /// the run; anyone else's last as long as they do.
    /// </summary>
    internal static class Counters
    {
        private static readonly Dictionary<Character, Dictionary<string, int>> others = new Dictionary<Character, Dictionary<string, int>>();
        internal static int TurnsInCombat;

        private static Dictionary<string, object> D => SideAreas.Data;

        internal static int Get(Character c, string name)
        {
            if (c == null || string.IsNullOrEmpty(name)) return 0;
            if (c == Player.instance) return D != null && D.TryGetValue("ctr_" + name, out object v) && v is int i ? i : 0;
            return others.TryGetValue(c, out var d) && d.TryGetValue(name, out int n) ? n : 0;
        }

        internal static void Set(Character c, string name, int value)
        {
            if (c == null || string.IsNullOrEmpty(name)) return;
            if (c == Player.instance) { if (D != null) D["ctr_" + name] = value; return; }
            if (!others.TryGetValue(c, out var d)) others[c] = d = new Dictionary<string, int>();
            d[name] = value;
            if (others.Count > 200) foreach (Character dead in others.Keys.Where(k => k == null || k.GetIsDead()).ToList()) others.Remove(dead);
        }

        /// <summary>The counter action: add amount, or set it ("set": true), within min..max.</summary>
        internal static void Run(ActionSpec a, ActionContext ctx, Character t)
        {
            string name = a.counter;
            if (string.IsNullOrEmpty(name)) return;
            int v = a.counterSet ? a.amount : Get(t, name) + a.amount + Actions.StackBonusOf(a, ctx, t);
            if (a.counterMax > 0) v = Math.Min(v, a.counterMax);
            v = Math.Max(a.counterMin, v);
            Set(t, name, v);
        }

        /// <summary>Per turn: how long the current fight has run, and counters that reset out of combat.</summary>
        internal static void Turn()
        {
            if (When.InCombat()) TurnsInCombat++;
            else TurnsInCombat = 0;
        }
    }
}
