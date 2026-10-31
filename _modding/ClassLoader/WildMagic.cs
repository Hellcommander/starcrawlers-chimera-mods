using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// "wildCast": casts random spells from every installed class at the target. The pool is every registered mod
    /// ability (tiers, form kits, synergy grants) that is an active, aimed skill made only of plain effects (damage,
    /// statuses, crowd control, ground, chains) with no cost of its own beyond stamina, so a borrowed spell can't move
    /// you, summon, craft, spend another class's resources or need its gear. Effects keep their own class ("@X" is still
    /// that class's effect), the damage scales with your stats, and area spells hit around the target.
    ///   { "do": "wildCast", "amount": 2, "falloff": 0.8 }  - two spells, each at 80% (falloff 0 or unset = full)
    ///   "notClass": "MOD_Wildcaster" keeps a class's own spells out (the caster's own class always is).
    /// A class's "wildSpells" are spells nothing else can cast: three in ten plain wild casts are one of them, and "pool"
    /// asks for one outright ("wild", or "bosses" for its boss echoes).
    /// </summary>
    internal static class WildMagic
    {
        private static readonly HashSet<string> SafeActions = new HashSet<string>
        {
            "damage", "effect", "stun", "knockback", "blind", "pull", "throw", "chain", "field", "hazard", "silence",
            "exhaust", "random", "if", "execute", "purge", "dispel", "cleanse", "show", "tether", "wallspikes", "vfx",
        };

        private static List<ModAbility.Runtime> pool;
        private static List<MobSkillSpec> monsters;

        /// <summary>The mod monsters' attacks made of plain effects (no summoning, no moving the monster).</summary>
        private static List<MobSkillSpec> Monsters()
        {
            if (monsters != null) return monsters;
            monsters = EnemyAbilities.Specs.Values.Where(m => m != null && (m.target == "enemy" || m.target == "area")
                && m.actions != null && m.actions.Any(a => a.@do == "damage" || a.@do == "effect") && Safe(m.actions)).ToList();
            Plugin.Log.LogInfo($"Wild magic: {monsters.Count} monster attacks in the pool");
            return monsters;
        }

        internal static int MonsterCount => Monsters().Count;

        /// <summary>The last spells cast wild ("Brine Lash (Tidewright)"), for tests and the log.</summary>
        internal static readonly List<string> Recent = new List<string>();

        internal static int PoolSize => Pool().Count;

        private static bool Safe(IEnumerable<ActionSpec> acts, int depth = 0)
        {
            if (acts == null) return true;
            foreach (ActionSpec a in acts)
            {
                if (a == null) continue;
                if (!SafeActions.Contains(a.@do ?? "")) return false;
                if ((a.to ?? "").ToLowerInvariant() is string to && to != "" && to != "target") return false; // nothing done to you or your pets
                if (depth > 4 || !Safe(a.actions, depth + 1) || !Safe(a.then, depth + 1) || !Safe(a.@else, depth + 1)) return false;
                if (a.options != null && a.options.Any(o => !Safe(o.actions, depth + 1))) return false;
            }
            return true;
        }

        private static List<ModAbility.Runtime> Pool()
        {
            if (pool != null) return pool;
            pool = ModAbility.Specs.Values.Where(rt =>
            {
                AbilitySpec s = rt?.spec;
                if (s == null || s.type == "passive" || (s.target != "enemy" && s.target != "floor")) return false;
                if (s.itemCost != null || s.stackCost != null || s.creditCost > 0 || !string.IsNullOrEmpty(s.requires) || !string.IsNullOrEmpty(s.equipped)) return false;
                if (s.actions == null || s.actions.Count == 0 || !s.actions.Any(a => a.@do == "damage" || a.@do == "effect" || a.@do == "chain")) return false;
                return Safe(s.actions);
            }).ToList();
            Plugin.Log.LogInfo($"Wild magic: {pool.Count} spells in the pool");
            return pool;
        }

        internal static void Cast(ActionSpec a, ActionContext ctx, Character t)
        {
            if (t == null || t.GetIsDead() || ctx.caster == null) return;
            if ((t == ctx.caster || TileStacks.IsAlly(t)) && !ctx.backfire) return; // wild spells only go at enemies, unless the magic is loose
            string which = (a.pool ?? "spells").ToLowerInvariant();
            if (which == "wild" || which == "bosses")
            {
                for (int i = 0, count = Math.Max(1, a.amount) + Actions.StackBonusOf(a, ctx, t); i < count && !t.GetIsDead(); i++)
                    Own(Pick(which == "bosses", ctx, t), a, ctx, t);
                return;
            }
            var choices = which == "monsters" ? new List<ModAbility.Runtime>()
                : Pool().Where(rt => rt.classId != ctx.classId && (string.IsNullOrEmpty(a.notClass) || rt.classId != a.notClass)).ToList();
            var beasts = which == "spells" ? new List<MobSkillSpec>() : Monsters();
            if (choices.Count + beasts.Count == 0) return;
            int n = Math.Max(1, a.amount) + Actions.StackBonusOf(a, ctx, t);
            for (int i = 0; i < n && !t.GetIsDead(); i++)
            {
                if (which == "all" && Random.value < BossShare && Own(Pick(true, ctx, t), a, ctx, t)) continue;
                if (which != "monsters" && Random.value < WildShare && Own(Pick(false, ctx, t), a, ctx, t)) continue;
                if (Random.Range(0, choices.Count + beasts.Count) >= choices.Count) { Monster(beasts[Random.Range(0, beasts.Count)], a, ctx, t); continue; }
                ModAbility.Runtime rt = choices[Random.Range(0, choices.Count)];
                string spell = rt.spec.name;
                string cls = ClassName(rt.classId);
                Recent.Add($"{spell} ({cls})");
                if (Recent.Count > 40) Recent.RemoveAt(0);
                Game.LOG($"<color=#E59CFF>Wild magic!</color> {ctx.sourceName} comes out as <color=#FFD15C>{spell}</color> ({cls}).");
                var c = new ActionContext
                {
                    caster = ctx.caster, source = ctx.source, classId = rt.classId, sourceName = spell, rank = ctx.rank,
                    damageMult = ctx.damageMult * (a.falloff > 0f && a.falloff < 1f ? a.falloff : 1f), chanceBonus = ctx.chanceBonus,
                    center = t.transform.position, target = t, backfire = ctx.backfire,
                };
                float radius = rt.spec.radius > 0f ? rt.spec.radius : Shapes.Is(rt.spec.shape) ? 1.5f : 0f;
                var victims = new List<Character> { t };
                NavNode node = t.GetMover()?.GetCurrentNavNode();
                if (radius > 0f && node != null && AI_Manager.instance != null)
                    victims.AddRange(AI_Manager.instance.GetEnemiesAt(node.transform.position, radius).Where(v => v != t));
                Actions.SpawnVfx(rt.spec.impactVfx ?? rt.spec.areaVfx ?? "PA_Psykonics_Temporal.castParticle@0.5", t.GetHitFx().position);
                ModAudio.Play(rt.spec.sound, t.GetHitFx().position, rt.classId);
                foreach (Character v in victims)
                    if (v != null && !v.GetIsDead()) { c.target = v; Actions.Run(rt.spec.actions, c, v); }
            }
        }

        /// <summary>One monster attack, thrown by you: its effects at your target (and around it for an area attack).</summary>
        private static void Monster(MobSkillSpec m, ActionSpec a, ActionContext ctx, Character t)
        {
            string name = m.name ?? m.id;
            Recent.Add($"{name} (monster)");
            if (Recent.Count > 40) Recent.RemoveAt(0);
            Game.LOG($"<color=#E59CFF>Wild magic!</color> {ctx.sourceName} comes out as a monster's <color=#FF9F5C>{name}</color>.");
            var c = new ActionContext
            {
                caster = ctx.caster, source = ctx.source, classId = EnemyAbilities.EffectClass, sourceName = name, rank = ctx.rank,
                damageMult = ctx.damageMult * (a.falloff > 0f && a.falloff < 1f ? a.falloff : 1f), chanceBonus = ctx.chanceBonus,
                center = t.transform.position, target = t, backfire = ctx.backfire,
            };
            var victims = new List<Character> { t };
            NavNode node = t.GetMover()?.GetCurrentNavNode();
            float radius = m.radius > 0f ? m.radius : Shapes.Is(m.shape) ? 1.5f : 0f;
            if (radius > 0f && node != null && AI_Manager.instance != null)
                victims.AddRange(AI_Manager.instance.GetEnemiesAt(node.transform.position, radius).Where(v => v != t));
            Actions.SpawnVfx(m.impactVfx ?? m.castVfx ?? "PA_Psykonics_Temporal.castParticle@0.5", t.GetHitFx().position);
            ModAudio.Play(m.sound, t.GetHitFx().position, EnemyAbilities.EffectClass);
            foreach (Character v in victims)
                if (v != null && !v.GetIsDead() && (ctx.backfire || (v != ctx.caster && !TileStacks.IsAlly(v)))) { c.target = v; Actions.Run(m.actions, c, v); }
        }

        // ---- wild spells: exist only as wild magic. A plain wild cast is one of these WildShare of the time ----------

        private const float WildShare = 0.3f, BossShare = 0.15f;

        private static readonly List<(WildSpell spell, string classId)> wildSpells = new List<(WildSpell, string)>();

        internal static string ForceSpell; // devtests

        internal static void Register(ClassDef def)
        {
            wildSpells.RemoveAll(w => w.classId == def.id);
            foreach (WildSpell s in def.wildSpells ?? new List<WildSpell>())
                if (s != null && !string.IsNullOrEmpty(s.name) && s.actions != null) wildSpells.Add((s, def.id));
        }

        private static bool IsBoss(WildSpell s) => s.group == "boss";

        internal static IEnumerable<string> SpellNames(bool bosses) => wildSpells.Where(w => IsBoss(w.spell) == bosses).Select(w => w.spell.name);

        /// <summary>A spell that would do nothing at all to this target isn't picked: no announcing a swap that can't happen.</summary>
        private static bool Fits(WildSpell s, ActionContext ctx, Character t) =>
            ctx.caster.level >= s.minLevel && (!s.actions.Any(a => a.@do == "swap") || Afflictions.CanSwap(ctx.caster, t));

        private static (WildSpell spell, string classId) Pick(bool bosses, ActionContext ctx, Character t)
        {
            var from = wildSpells.Where(w => IsBoss(w.spell) == bosses && (ForceSpell == null || w.spell.name == ForceSpell) && Fits(w.spell, ctx, t)).ToList();
            int roll = Random.Range(0, from.Sum(w => Math.Max(1, w.spell.weight)));
            foreach (var w in from) { roll -= Math.Max(1, w.spell.weight); if (roll < 0) return w; }
            return default;
        }

        /// <summary>One wild spell at the target. False if there was none to cast.</summary>
        private static bool Own((WildSpell spell, string classId) pick, ActionSpec a, ActionContext ctx, Character t)
        {
            WildSpell s = pick.spell;
            if (s == null) return false;
            var c = new ActionContext
            {
                caster = ctx.caster, source = ctx.source, classId = pick.classId, sourceName = s.name, rank = ctx.rank,
                damageMult = ctx.damageMult * (a.falloff > 0f && a.falloff < 1f ? a.falloff : 1f), chanceBonus = ctx.chanceBonus,
                center = t.transform.position, target = t, backfire = ctx.backfire || s.friendlyFire, atYou = a.atYou,
            };
            if (IsBoss(s)) { Actions.Run(s.actions, c, t); return true; } // the surge inside tells it and shows it
            Recent.Add($"{s.name} (wild)");
            if (Recent.Count > 40) Recent.RemoveAt(0);
            Game.LOG($"<color=#E59CFF>Wild magic!</color> {ctx.sourceName} comes out as <color=#7CFFD0>{s.name}</color>{(string.IsNullOrEmpty(s.say) ? "." : ": " + s.say)}");
            Actions.SpawnVfx(s.impactVfx ?? "PA_Psykonics_Temporal.castParticle@0.5", t.GetHitFx().position);
            ModAudio.Play(s.sound, t.GetHitFx().position, pick.classId);
            var victims = new List<Character> { t };
            NavNode node = t.GetMover()?.GetCurrentNavNode();
            if (s.radius > 0f && node != null && Map.instance != null)
                foreach (NavNode n in Map.instance.GetNavNodesAt(node.transform.position, s.radius))
                {
                    Character v = n?.GetOccupant();
                    if (v != null && !v.GetIsDead() && !victims.Contains(v) && (s.friendlyFire || (v != ctx.caster && !TileStacks.IsAlly(v)))) victims.Add(v);
                }
            foreach (Character v in victims)
                if (v != null && !v.GetIsDead()) { c.target = v; Actions.Run(s.actions, c, v); }
            return true;
        }

        // ---- unruly casting: "wildForm" on a passive, the % chance your spell changes form as it leaves you ---------

        internal static string ForceForm; // devtests
        private static Dictionary<string, string> formVfx;

        private static readonly (string form, int weight)[] Forms =
            { ("area", 3), ("line", 2), ("scatter", 3), ("retarget", 2), ("trap", 2), ("backfire", 1) };

        internal static bool RollForm(out string form)
        {
            form = null;
            var pac = Player_AbilityControl.instance;
            if (pac == null) return false;
            AbilitySpec best = pac.GetAbilities().OfType<ModAbility>().Select(m => m.Spec).Where(sp => sp != null && sp.wildForm > 0)
                .OrderByDescending(sp => sp.wildForm).FirstOrDefault();
            int chance = best?.wildForm ?? 0;
            if (chance <= 0) return false;
            formVfx = best.formVfx;
            if (ForceForm != null) { form = ForceForm; return true; }
            if (Random.Range(1, 101) > chance) return false;
            int roll = Random.Range(0, Forms.Sum(f => f.weight));
            foreach (var f in Forms) { if (roll < f.weight) { form = f.form; return true; } roll -= f.weight; }
            form = "area";
            return true;
        }

        /// <summary>Rewrites who the spell lands on, or sinks it into a trap, or turns it on you.</summary>
        internal static void Convert(string form, string spell, ActionContext ctx, List<Character> victims, Vector3 center, Vector3 from, float range, List<ActionSpec> acts)
        {
            Character caster = ctx.caster;
            List<Character> Foes(Vector3 at, float r) => AI_Manager.instance == null ? new List<Character>()
                : AI_Manager.instance.GetEnemiesAt(at, r).Where(c => c != null && !c.GetIsDead() && c != caster && !TileStacks.IsAlly(c)).ToList();
            string fx = formVfx != null && formVfx.TryGetValue(form, out string f0) ? f0 : null;
            string say;
            switch (form)
            {
                case "area":
                    foreach (Character c in Foes(center, 2f)) if (!victims.Contains(c)) victims.Add(c);
                    Actions.SpawnVfx(fx ?? "PA_Prototype_Thunderclap.impactParticle@0.4", center + Vector3.up * 0.2f);
                    say = "bursts outward over everything near the target";
                    break;
                case "line":
                    {
                        float reach = Mathf.Max(range, 5f);
                        foreach (Character c in Shapes.Hit(Foes(from, reach + 1f), from, center, "line", reach, 1f, 60f))
                            if (!victims.Contains(c)) victims.Add(c);
                        Vector3 dir = center - from; dir.y = 0f;
                        if (fx != null && dir.sqrMagnitude > 0.01f) Actions.SpawnTravel(fx, caster.GetHitFx().position, from + dir.normalized * reach + Vector3.up * 0.9f, 14f);
                        say = "comes out as a beam through everything in its path";
                        break;
                    }
                case "scatter":
                    foreach (Character c in Foes(center, 6f).Where(c => !victims.Contains(c)).OrderBy(_ => Random.value).Take(2).ToList())
                    {
                        victims.Add(c);
                        if (fx != null) Actions.SpawnVfx(fx, c.GetHitFx().position);
                    }
                    say = "splits and scatters onto other foes";
                    break;
                case "retarget":
                    {
                        Character other = Foes(caster.transform.position, 8f).Where(c => !victims.Contains(c)).OrderBy(_ => Random.value).FirstOrDefault();
                        if (other == null) return;
                        victims.Clear(); victims.Add(other);
                        if (fx != null) Actions.SpawnTravel(fx, caster.GetHitFx().position, other.GetHitFx().position, 14f);
                        say = $"swerves off and hits {other.GetName()} instead";
                        break;
                    }
                case "trap":
                    Traps.Lay(new ActionSpec
                    {
                        @do = "trap", fieldName = "Wild " + spell, affects = "enemies", armTurns = 1, charges = 1, turns = 8,
                        look = "HAZARD_VoidRift@0.35", actions = acts.ToList(),
                    }, new ActionContext { caster = caster, source = ctx.source, classId = ctx.classId, sourceName = spell, center = center, rank = ctx.rank, damageMult = ctx.damageMult }, null);
                    victims.Clear();
                    if (fx != null) Actions.SpawnVfx(fx, center + Vector3.up * 0.1f);
                    say = "sinks into the deck and waits there as a trap";
                    break;
                case "backfire":
                    victims.Clear(); victims.Add(caster);
                    ctx.backfire = true;
                    if (fx != null) Actions.SpawnVfx(fx, caster.GetHitFx().position);
                    say = "<color=#FF6B6B>turns on you</color>";
                    break;
                default: return;
            }
            Recent.Add("form:" + form);
            if (Recent.Count > 40) Recent.RemoveAt(0);
            Game.LOG($"<color=#E59CFF>Unruly magic!</color> {spell} {say}.");
        }

        // ---- surges: a wild echo of something huge (a boss's attack) that cares nothing for sides -------------------

        /// <summary>"surge": lands on the target, or (atYou %, less half your Crit) on you, and hits everyone in its radius,
        /// you and your allies included. The damage it does to you is real.</summary>
        internal static void Surge(ActionSpec a, ActionContext ctx, Character t)
        {
            Character caster = ctx.caster;
            if (caster == null || Map.instance == null) return;
            int crit = caster.GetStat(STAT_TYPE.Crit);
            int aim = ctx.atYou > 0 ? ctx.atYou : a.atYou;
            int youChance = aim <= 0 ? 0 : Math.Max(aim / 3, aim - crit / 2);
            bool onYou = t == null || t.GetIsDead() || t == caster || Random.Range(1, 101) <= youChance;
            Character at = onYou ? caster : t;
            NavNode node = at.GetMover()?.GetCurrentNavNode();
            Vector3 pos = node != null ? node.transform.position : at.transform.position;
            var victims = new List<Character> { at };
            if (a.radius > 0f)
                foreach (NavNode n in Map.instance.GetNavNodesAt(pos, a.radius))
                {
                    Character c = n?.GetOccupant();
                    if (c != null && !c.GetIsDead() && !victims.Contains(c)) victims.Add(c);
                }
            string name = a.surgeName ?? "A surge";
            Recent.Add("surge:" + name + (onYou ? " (you)" : ""));
            if (Recent.Count > 40) Recent.RemoveAt(0);
            Game.LOG($"<color=#E59CFF>Wild surge!</color> <color=#FF9F5C>{name}</color> {(onYou ? "<color=#FF6B6B>comes down on you</color>" : $"comes down on {at.GetName()}")}" +
                     (victims.Count > 1 ? $", catching {victims.Count - 1} more" : "") + ".");
            Actions.SpawnVfx(a.vfx ?? "PA_Prototype_Thunderclap.impactParticle@0.5", at.GetHitFx().position);
            ModAudio.Play(a.sound, at.GetHitFx().position, ctx.classId);
            var c2 = new ActionContext
            {
                caster = caster, source = ctx.source, classId = ctx.classId, sourceName = name, rank = ctx.rank,
                damageMult = ctx.damageMult * Actions.LevelMult(a.levelCap), chanceBonus = ctx.chanceBonus, center = pos, backfire = true,
            };
            foreach (Character v in victims)
                if (v != null && !v.GetIsDead()) { c2.target = v; Actions.Run(a.actions, c2, v); }
        }

        private static string ClassName(string classId) =>
            ClassRegistry.AllDefs.FirstOrDefault(d => d.id == classId)?.name ?? (classId.StartsWith(Synergies.EffectClass) ? "a synergy" : classId);

        internal static string Describe(ActionSpec a, string who) =>
            (a.pool ?? "").ToLowerInvariant() == "bosses" ? $"Calls an echo of a boss's attack down on {who} (any of {SpellNames(true).Count()})"
            : (a.pool ?? "").ToLowerInvariant() == "wild" ? $"Casts a wild spell at {who} (any of {SpellNames(false).Count()}, found nowhere else)"
            : (a.pool ?? "").ToLowerInvariant() == "monsters" ? $"Throws {(a.amount > 1 ? a.amount + " random monster attacks" : "a random monster attack")} at {who} (any of {MonsterCount})"
            : $"Casts {(a.amount > 1 ? a.amount + " random spells" : "a random spell")} {((a.pool ?? "").ToLowerInvariant() == "all" ? "or monster attacks " : "")}from another class at {who}" +
            (a.falloff > 0f && a.falloff < 1f ? $" at {Mathf.RoundToInt(a.falloff * 100)}% power" : "") +
            $" (any of {PoolSize} spells from every installed class, or one of {SpellNames(false).Count()} wild spells found nowhere else" +
            ((a.pool ?? "").ToLowerInvariant() == "all" ? ", or an echo of a boss's attack" : "") + ")";
    }
}
