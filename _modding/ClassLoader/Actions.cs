using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>Everything one action needs to know about who did what to whom.</summary>
    internal class ActionContext
    {
        public Character caster;
        public Character target;
        public Character attacker;
        /// <summary>A boss echo's % chance of landing on you, set by the wildCast that called it (surge "atYou" otherwise).</summary>
        public int atYou;
        public Character victim;
        /// <summary>The ally in a proc's event: the one hurt (allyHurt), who killed (allyKill), who joined (summon) or fell (allyDeath).</summary>
        public Character ally;
        public Ability source;
        public string classId;
        public string sourceName;
        public int rank;
        /// <summary>The inventory item an item-target ability was used on.</summary>
        public Item item;
        public float damageMult = 1f;
        public int chanceBonus;
        /// <summary>The ability's aim point (the enemy's or floor tile's position; you for self abilities).</summary>
        public Vector3? center;
        /// <summary>Wide ground (fields/hazards with a radius) already laid by this cast, so an area ability lays it once.</summary>
        public HashSet<ActionSpec> groundLaid;
        /// <summary>Set when an effect runs its actions: the health its bearer lost while it was on ("stored" shares of it).</summary>
        public int stored;
        /// <summary>A spell turned on its caster (wild magic): its damage may land on you this once.</summary>
        public bool backfire;
        /// <summary>Ground (fields, hazards) goes at the aim point, not under the target: a floor-aimed ability with nobody there.</summary>
        public bool groundAtCenter;
        /// <summary>Set by area abilities: stacks consumed from yourself are removed once, after every target is hit
        /// (effect id -> how many; 0 = all).</summary>
        public Dictionary<string, int> deferredSelfConsume;
        /// <summary>The casting ability's impact particle, for hits it spreads (chain hops).</summary>
        public string hitVfx;
        /// <summary>Moves that play out over time (a leaping chain's hops). The ability waits for them before its own
        /// self actions; null = run everything at once (procs, effects).</summary>
        public List<IEnumerator> paced;
    }

    /// <summary>Runs ActionSpecs and writes their rules text. Shared by abilities, procs and effects.</summary>
    internal static class Actions
    {
        /// <summary>&gt;0 while a proc is resolving, so proc damage cannot trigger more procs.</summary>
        public static int ProcDepth;

        public static void Run(IEnumerable<ActionSpec> list, ActionContext ctx, Character defaultTarget)
        {
            if (list == null) return;
            foreach (ActionSpec a in list)
            {
                try { RunOne(a, ctx, defaultTarget); }
                catch (Exception e) { Plugin.Log.LogError($"Action '{a.@do}' from {ctx.sourceName} failed: {e}"); }
            }
        }

        private static Character Resolve(ActionSpec a, ActionContext ctx, Character defaultTarget)
        {
            switch ((a.to ?? "target").ToLowerInvariant())
            {
                case "self": return ctx.caster;
                case "attacker": return ctx.attacker;
                case "victim": return ctx.victim;
                case "ally": return ctx.ally;
                // the summon this ability made, or the first one matching the action's ifSummonAlive ("any", a creature name)
                case "summon": return Summons.Matching(string.IsNullOrEmpty(a.ifSummonAlive) ? "self" : a.ifSummonAlive, ctx).FirstOrDefault();
                default: return defaultTarget;
            }
        }

        private static void RunOne(ActionSpec a, ActionContext ctx, Character defaultTarget)
        {
            // every living summon matching ifSummonAlive (default: all your pets), each rolled separately
            if ((a.to ?? "").ToLowerInvariant() == "summons")
            {
                foreach (Character s in Summons.Matching(string.IsNullOrEmpty(a.ifSummonAlive) ? "any" : a.ifSummonAlive, ctx))
                    RunOn(a, ctx, s);
                return;
            }
            RunOn(a, ctx, Resolve(a, ctx, defaultTarget));
        }

        private static readonly HashSet<string> Hostile = new HashSet<string>
            { "damage", "stun", "knockback", "blind", "throw", "pull", "silence", "exhaust", "execute", "swap", "tame", "hazard" };

        /// <summary>An ignoresAllies class (astral magic) never lands a hostile action on its own side.</summary>
        private static bool SparesAlly(ActionSpec a, string act, ActionContext ctx, Character t)
        {
            if (ctx.classId == null || !ClassRegistry.IgnoresAllies.Contains(ctx.classId) || t == null || t == ctx.caster) return false;
            if (!(t == Player.instance || (t.ai != null && t.ai.GetTeam() == AI.AI_TEAM.Player))) return false;
            if (Hostile.Contains(act) || PluginHost.IsHostile(act) || act == "warp" && (a.to ?? "").ToLowerInvariant() != "self") return true;
            if (act == "effect")
            {
                string id = EffectId(a.effect, ctx.classId);
                if (ModEffect.Specs.TryGetValue(id ?? "", out var rt)) return rt.spec.debuff;
                return id == "CHARM" || id == "CONFUSE" || id == "SLOW" || (id != null && id.StartsWith("EFFECT_Root"));
            }
            return false;
        }

        private static void RunOn(ActionSpec a, ActionContext ctx, Character t)
        {
            if (t == null || t.GetIsDead()) return;
            if (!string.IsNullOrEmpty(a.ifTargetHas) && t.effects.GetEffect(EffectId(a.ifTargetHas, ctx.classId)) == null) return;
            if (!string.IsNullOrEmpty(a.unlessTargetHas) && t.effects.GetEffect(EffectId(a.unlessTargetHas, ctx.classId)) != null) return;
            if (!string.IsNullOrEmpty(a.ifSelfHas) && (ctx.caster == null || ctx.caster.effects.GetEffect(EffectId(a.ifSelfHas, ctx.classId)) == null)) return;
            if (!string.IsNullOrEmpty(a.ifSummonAlive) && Summons.Matching(a.ifSummonAlive, ctx).Count == 0) return;
            if (!string.IsNullOrEmpty(a.unlessSummonAlive) && Summons.Matching(a.unlessSummonAlive, ctx).Count > 0) return;
            if (!string.IsNullOrEmpty(a.ifSkill) && Synergies.TreeLevel(a.ifSkill) < Math.Max(1, a.skillRank)) return;
            if (!string.IsNullOrEmpty(a.unlessSkill) && Synergies.TreeLevel(a.unlessSkill) > 0) return;
            string act = (a.@do ?? "").ToLowerInvariant();
            // "if" branches on its condition; everywhere else the condition gates the action
            if (act == "if")
            {
                if (a.chance < 100 && Random.Range(1, 101) > a.chance + ctx.chanceBonus) return;
                Run(When.Check(a.when, ctx, t) ? a.then : a.@else, ctx, t);
                return;
            }
            if (a.when != null && !When.Check(a.when, ctx, t)) return;
            if (a.chance < 100 && Random.Range(1, 101) > a.chance + ctx.chanceBonus) return;
            if (SparesAlly(a, act, ctx, t)) return;

            switch (act)
            {
                case "counter": Counters.Run(a, ctx, t); break;
                case "credits": Credits(a, ctx, t); break;
                case "random": RandomPick(a, ctx, t); break;
                case "execute": Execute(a, ctx, t); break;
                case "dismiss": Dismiss(ctx, t); break;
                case "phase": break; // target "pet" skills: the ability spends the pet and moves you (ModAbility.Use)
                case "detonate": Detonate(ctx, t); break;
                case "pull": Pull(a, ctx, t); break;
                case "throw": Moves.Throw(a, ctx, t); break;
                case "leap": Moves.Leap(a, ctx, t); break;
                case "warp": Moves.Warp(a, ctx, t); break;
                case "show": Moves.Show(a, ctx, t); break;
                case "tether": Moves.Tether(a, ctx, t); break;
                case "recall": Moves.Recall(a, ctx); break;
                case "ricochet": Moves.Ricochet(a, ctx); break;
                case "taunt": Afflictions.Taunt(a, ctx, t); break;
                case "silence": Afflictions.Silence(a, ctx, t); break;
                case "exhaust": Afflictions.Exhaust(ctx, t, Math.Max(1, a.amount + StackBonus(a, ctx, t))); break;
                case "swap": Afflictions.Swap(ctx, t); break;
                case "arm": Reactions.Arm(a, ctx); break;
                case "spring": Reactions.Spring(ctx); break;
                case "hazard": GroundFields.LayHazard(a, ctx, t); break;
                case "field": GroundFields.Lay(a, ctx, t); break;
                case "trap": Traps.Lay(a, ctx, t); break;
                case "springtraps": Traps.Spring(a); break;
                case "wallspikes": Walls.Spikes(a, ctx, t); break;
                case "wildcast": WildMagic.Cast(a, ctx, t); break;
                case "surge": WildMagic.Surge(a, ctx, t); break;
                case "drifter": Drifters.Launch(a, ctx, t); break;
                case "horror": Horrors.Launch(a, ctx, t); break;
                case "vfx": if (t != null && !string.IsNullOrEmpty(a.vfx)) SpawnVfx(a.vfx, t.GetHitFx().position); break;
                case "raisewall": Walls.Raise(a, ctx, t); break;
                case "swapbody": Conjoined.SwapToNearest(ctx.sourceName); break;
                case "revivebody": Conjoined.ReviveOne(ctx.sourceName, a.amount > 0 ? a.amount : 50); break;
                case "damage": Damage(a, ctx, t); break;
                case "heal":
                    {
                        int amt = Amount(a, ctx) + StackBonus(a, ctx, t);
                        t.Heal(amt, false);
                        Game.LOG($"{ctx.sourceName}: {t.GetName()} {Ability.T("General/heals")} {amt}.");
                        if (HUD.instance != null) HUD.instance.MakeFct("<color=#5CFF6B>+" + amt + "</color>", t.GetHitFx().position, false);
                        break;
                    }
                case "stamina": t.ModStamina(a.amount < 0 ? -DrainAmount(a, ctx) - StackBonus(a, ctx, t) : Amount(a, ctx) + StackBonus(a, ctx, t)); break;
                case "shield":
                    {
                        // Overshield never decays on its own, so cap stacking at half max health.
                        int room = Math.Max(0, t.GetHealthMax() / 2 - t.oshielding);
                        int add = Math.Min(room, Amount(a, ctx) + StackBonus(a, ctx, t));
                        if (add > 0) t.AddOshields(add);
                        break;
                    }
                case "effect": ApplyEffect(a, ctx, t); break;
                case "stun": Ability.ApplyStun(t, Turns(a)); break;
                case "knockback": Ability.Knockback(t, ctx.caster, Math.Max(1, a.amount)); break;
                case "blind": if (ctx.source != null && t != Player.instance) ctx.source.ApplyBlind(t); break;
                case "cooldowns":
                    if (Player_AbilityControl.instance != null)
                        foreach (Ability ab in Player_AbilityControl.instance.GetAbilities())
                            if (ab != ctx.source) ab.ReduceCooldown(Math.Max(1, a.amount));
                    if (Menu_HUD.instance != null) Menu_HUD.instance.Refresh_Cooldown();
                    break;
                case "purge": t.effects.RemoveEffect(EffectId(a.effect, ctx.classId), true); t.CalculateStats(); break;
                case "teleport": break; // handled by ModAbility for floor targets
                case "dispel": Dispel(a, ctx, t, debuffs: false); break;
                case "cleanse": Dispel(a, ctx, t, debuffs: true); break;
                case "chain": Chain(a, ctx, t); break;
                case "echo": Echo(a, ctx, t); break;
                case "summon": Summons.Summon(a, ctx, t, ScaledCount(a, a.amount, ctx, t)); break;
                case "tame": Taming.Tame(a, ctx, t, Amount(a, ctx)); break;
                case "giveitem": GiveItem(a, ctx); break;
                case "consumeitem": ConsumeItem(a, ctx); break;
                case "salvage": Salvage(a, ctx); break;
                case "craft": if (ctx.caster == Player.instance) Crafting.Open("field", a.recipes); break;
                case "runepanel": if (ctx.caster == Player.instance) RunePanel.OpenCharging(); break;
                case "runerestore": if (ctx.caster == Player.instance) Runes.Restore(Math.Max(1, a.amount)); break;
                default:
                    if (!PluginHost.RunAction(act, a, ctx, t)) Plugin.Log.LogWarning($"{ctx.sourceName}: unknown action '{a.@do}'");
                    break;
            }

            if (a.consume && !string.IsNullOrEmpty(a.ifTargetHas) && !t.GetIsDead())
            {
                t.effects.RemoveEffect(EffectId(a.ifTargetHas, ctx.classId), true);
                t.CalculateStats();
            }
            if (a.consumeStacks && !string.IsNullOrEmpty(a.stacksOf))
            {
                Character holder = StackHolder(a, ctx, t);
                string sid = EffectId(a.stacksOf, ctx.classId);
                int n = Math.Max(0, a.stacksUsed);
                if (holder == ctx.caster && ctx.deferredSelfConsume != null)
                {
                    // several actions reading the same pile in one cast spend it once: all of it if any asks for all
                    ctx.deferredSelfConsume[sid] = ctx.deferredSelfConsume.TryGetValue(sid, out int had) && (had == 0 || n == 0) ? 0 : Math.Max(had, n);
                }
                else if (holder != null && !holder.GetIsDead()) Spend(holder, sid, n);
            }
        }

        /// <summary>Spends `n` stacks of an effect (0 = all), or takes `n` off a "counter:NAME" (0 = zeroes it).</summary>
        private static void Spend(Character c, string id, int n = 0)
        {
            if (id.StartsWith("counter:", StringComparison.OrdinalIgnoreCase))
            {
                string name = id.Substring(8);
                Counters.Set(c, name, n > 0 ? Math.Max(0, Counters.Get(c, name) - n) : 0);
                return;
            }
            if (n > 0 && c.effects.GetEffect(id) is ModEffect me) me.RemoveStacks(n);
            else c.effects.RemoveEffect(id, true);
            c.CalculateStats();
        }

        /// <summary>Removes stacks that area actions consumed from the caster.</summary>
        public static void FlushDeferred(ActionContext ctx)
        {
            if (ctx.deferredSelfConsume == null || ctx.caster == null) return;
            foreach (var kv in ctx.deferredSelfConsume) Spend(ctx.caster, kv.Key, kv.Value);
            if (ctx.deferredSelfConsume.Count > 0) ctx.caster.CalculateStats();
            ctx.deferredSelfConsume.Clear();
        }

        private static Character StackHolder(ActionSpec a, ActionContext ctx, Character t)
        {
            switch ((a.stacksFrom ?? "").ToLowerInvariant())
            {
                case "self": return ctx.caster;
                case "target": return ctx.target ?? t; // the ability's target, even for actions applied to you
                case "victim": return ctx.victim ?? t; // the one killed (kill procs, on-death actions)
                default: return t;
            }
        }

        /// <summary>Stacks of an effect on a character: ModEffect stack count, 1 for any other present effect, else 0.</summary>
        public static int StacksOn(Character c, string id)
        {
            if (id == "summons") return Summons.Count();
            if (id == "bodies") return Conjoined.LivingOthers();
            if (id != null && id.StartsWith("group:", StringComparison.OrdinalIgnoreCase)) return Summons.Mine(Summons.GroupTag(id.Substring(6))).Count;
            if (c == null || string.IsNullOrEmpty(id)) return 0;
            if (id.StartsWith("counter:", StringComparison.OrdinalIgnoreCase)) return Math.Max(0, Counters.Get(c, id.Substring(8)));
            Effect e = c.effects.GetEffect(id);
            if (e == null) return c == lastDead && lastDeadStacks.TryGetValue(id, out int was) ? was : 0;
            return e is ModEffect me ? me.Stacks : Math.Max(1, e.GetCharge());
        }

        /// <summary>
        /// What the last character to die carried as it died: dying strips its effects before the "kill" procs run,
        /// so "kill a foe that has X" reads them from here.
        /// </summary>
        private static Character lastDead;
        private static readonly Dictionary<string, int> lastDeadStacks = new Dictionary<string, int>();

        [HarmonyLib.HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_RememberDying
        {
            private static void Prefix(Character __instance)
            {
                if (__instance == null || __instance.GetIsDead() || __instance.effects == null) return;
                lastDeadStacks.Clear();
                lastDead = null; // StacksOn below must read the live effects
                foreach (Effect e in __instance.effects.GetEffects())
                    if (e != null && e.internalName != null) lastDeadStacks[e.internalName] = StacksOn(__instance, e.internalName);
                lastDead = __instance;
            }
        }

        private static int StackBonus(ActionSpec a, ActionContext ctx, Character t)
        {
            if (a.bonusPerStack == 0 || string.IsNullOrEmpty(a.stacksOf)) return 0;
            return a.bonusPerStack * UsedStacks(a, StacksOn(StackHolder(a, ctx, t), EffectId(a.stacksOf, ctx.classId)));
        }

        /// <summary>The stacks an action counts: all it finds, or at most stacksUsed.</summary>
        private static int UsedStacks(ActionSpec a, int have) => a.stacksUsed > 0 ? Math.Min(have, a.stacksUsed) : have;

        internal static int StackBonusOf(ActionSpec a, ActionContext ctx, Character t) => StackBonus(a, ctx, t);

        private static bool Scaled(ActionSpec a) => a.bonusPerStack != 0 && !string.IsNullOrEmpty(a.stacksOf);

        /// <summary>
        /// Stacks an effect action adds, or summons a summon action makes. With bonusPerStack the count follows the
        /// stacksOf count and the base may be 0 (none without stacks); without it, at least 1.
        /// </summary>
        private static int ScaledCount(ActionSpec a, int baseCount, ActionContext ctx, Character t) =>
            Scaled(a) ? Math.Max(0, baseCount + StackBonus(a, ctx, t)) : Math.Max(1, baseCount);

        /// <summary>Removes buffs (dispel) or debuffs (cleanse); a.effect then goes on you once per effect removed.</summary>
        private static void Dispel(ActionSpec a, ActionContext ctx, Character t, bool debuffs)
        {
            int limit = a.amount > 0 ? a.amount : 99;
            var removed = t.effects.GetEffects()
                .Where(e => e != null && !e.isHidden && e.isDebuff == debuffs && !(e.duration < 0 && !(e is ModEffect)))
                .Take(limit).ToList();
            foreach (Effect e in removed) e.RemoveInstant();
            t.CalculateStats();
            if (removed.Count > 0) Game.LOG($"{ctx.sourceName} strips {removed.Count} effect{(removed.Count > 1 ? "s" : "")} from {t.GetName()}.");
            if (!string.IsNullOrEmpty(a.effect) && removed.Count > 0 && ctx.caster != null)
                ApplyEffect(new ActionSpec { @do = "effect", effect = a.effect, stacks = removed.Count }, ctx, ctx.caster);
        }

        /// <summary>Runs a.actions on the target, then hops to nearby enemies (optionally only ones with a.effect).</summary>
        private static void Chain(ActionSpec a, ActionContext ctx, Character first)
        {
            // a charge inside an ability plays hop by hop; the ability waits for it
            if (a.leap && ctx.paced != null) { ctx.paced.Add(PacedChain(a, ctx, first)); return; }
            var visited = new HashSet<Character>();
            Character cur = first;
            float mult = ctx.damageMult;
            string need = string.IsNullOrEmpty(a.effect) ? null : EffectId(a.effect, ctx.classId);
            for (int hop = 0; cur != null && hop <= Math.Max(0, a.jumps); hop++)
            {
                visited.Add(cur);
                var c = Clone(ctx); c.damageMult = mult; c.target = cur;
                if (hop > 0) SpawnVfx(a.vfx ?? ctx.hitVfx ?? "PA_Elementalist_ChainLightning.shockImpact", cur.GetHitFx().position);
                if (a.leap && cur.GetHealth() > 0) Triggers.Styled("charge", cur, () => Moves.Leap(a, ctx, cur));
                // a chain set off by a death starts at the one dying: it only passes things on from there
                if (cur.GetHealth() > 0) Run(a.actions, c, cur);
                Triggers.ChainHop(ctx.caster, cur, hop);
                mult *= a.falloff;
                if (AI_Manager.instance == null || cur.GetMover() == null || cur.GetMover().GetCurrentNavNode() == null) break;
                Vector3 p = cur.GetMover().GetCurrentNavNode().transform.position;
                cur = AI_Manager.instance.GetEnemiesAt(p, a.radius > 0 ? a.radius : 3f)
                    .Where(e => !visited.Contains(e) && !e.GetIsDead() && (need == null || e.effects.GetEffect(need) != null))
                    .OrderBy(e => Vector3.Distance(e.transform.position, p)).FirstOrDefault();
            }
        }

        /// <summary>A leaping chain one hop at a time: dash, strike, a beat, on to the next.</summary>
        private static IEnumerator PacedChain(ActionSpec a, ActionContext ctx, Character first)
        {
            var visited = new HashSet<Character>();
            Character cur = first;
            float mult = ctx.damageMult;
            string need = string.IsNullOrEmpty(a.effect) ? null : EffectId(a.effect, ctx.classId);
            for (int hop = 0; cur != null && hop <= Math.Max(0, a.jumps); hop++)
            {
                visited.Add(cur);
                if (ctx.caster == null || ctx.caster.GetIsDead()) yield break;
                if (cur.GetHealth() > 0)
                {
                    Triggers.Styled("charge", cur, () => Moves.Leap(a, ctx, cur));
                    yield return new WaitForSeconds(0.12f); // the dash lands
                    var c = Clone(ctx); c.damageMult = mult; c.target = cur; c.paced = null;
                    SpawnVfx(a.vfx ?? ctx.hitVfx ?? "PA_Elementalist_ChainLightning.shockImpact", cur.GetHitFx().position);
                    Run(a.actions, c, cur);
                    Triggers.ChainHop(ctx.caster, cur, hop);
                    yield return new WaitForSeconds(0.22f); // a beat before the next
                }
                mult *= a.falloff;
                if (AI_Manager.instance == null || cur.GetMover() == null || cur.GetMover().GetCurrentNavNode() == null) break;
                Vector3 p = cur.GetMover().GetCurrentNavNode().transform.position;
                cur = AI_Manager.instance.GetEnemiesAt(p, a.radius > 0 ? a.radius : 3f)
                    .Where(e => !visited.Contains(e) && !e.GetIsDead() && (need == null || e.effects.GetEffect(need) != null))
                    .OrderBy(e => Vector3.Distance(e.transform.position, p)).FirstOrDefault();
            }
        }

        /// <summary>The last new ability you used, for echo actions.</summary>
        internal static ModAbility LastUsed;

        private static void Echo(ActionSpec a, ActionContext ctx, Character t)
        {
            ModAbility src = LastUsed;
            if (src == null || src == ctx.source || src.Spec == null) return;
            var c = Clone(ctx);
            c.damageMult *= a.falloff;
            c.sourceName = ctx.sourceName + " (" + src.GetDisplayName() + ")";
            Game.LOG($"{ctx.sourceName} echoes {src.GetDisplayName()}.");
            Run(src.Spec.actions.Where(x => (x.to ?? "").ToLowerInvariant() != "self" && x.@do != "echo" && x.@do != "teleport"), c, t);
        }

        private static ActionContext Clone(ActionContext x) => new ActionContext
        {
            caster = x.caster, target = x.target, attacker = x.attacker, victim = x.victim, source = x.source,
            classId = x.classId, sourceName = x.sourceName, rank = x.rank, damageMult = x.damageMult,
            chanceBonus = x.chanceBonus, deferredSelfConsume = x.deferredSelfConsume, hitVfx = x.hitVfx, paced = x.paced,
        };

        private static int Turns(ActionSpec a) => Math.Max(1, a.turns > 0 ? a.turns : a.amount);

        internal static int Amount(ActionSpec a, ActionContext ctx)
        {
            float v = a.amount + a.perRank * Math.Max(0, ctx.rank) + ScaleBonus(a, ctx);
            return Math.Max(1, Mathf.RoundToInt(v));
        }

        /// <summary>A drain's size: "amount": -15 drains 15, plus its rank and stat scaling.</summary>
        private static int DrainAmount(ActionSpec a, ActionContext ctx) =>
            Math.Max(1, Mathf.RoundToInt(-a.amount + a.perRank * Math.Max(0, ctx.rank) + ScaleBonus(a, ctx)));

        /// <summary>"scale": one stat, or several joined with "+" ("Smarts+PsiPower"), each counting scaleMult per point.
        /// A part written "Smarts|PsiPower" counts only the higher of those stats.</summary>
        private static IEnumerable<List<STAT_TYPE>> ScaleStats(ActionSpec a) => ScaleStats(a.scale);

        internal static IEnumerable<List<STAT_TYPE>> ScaleStats(string scale)
        {
            if (string.IsNullOrEmpty(scale)) yield break;
            foreach (string part in scale.Split('+'))
            {
                var any = new List<STAT_TYPE>();
                foreach (string s in part.Split('|'))
                    if (Enum.TryParse(s.Trim(), true, out STAT_TYPE st)) any.Add(st);
                if (any.Count > 0) yield return any;
            }
        }

        /// <summary>The same stat sum for anything else that scales ("PsiPower+Smarts" at `mult` per point).</summary>
        internal static float StatBonus(string scale, float mult, Character who) =>
            who == null ? 0f : ScaleStats(new ActionSpec { scale = scale }).Sum(any => any.Max(st => who.GetStat(st)) * mult);

        private static float ScaleBonus(ActionSpec a, ActionContext ctx) =>
            ctx.caster == null ? 0f : ScaleStats(a).Sum(any => any.Max(st => ctx.caster.GetStat(st)) * a.scaleMult);

        /// <summary>"weapon": a share of one roll of the one weapon the skill swings (Equipped.Hand: the main hand, or
        /// an off-hand weapon that fits its gear rule at off-hand power).</summary>
        private static float WeaponBonus(ActionSpec a, ActionContext ctx)
        {
            if (a.weapon <= 0f || !(ctx.caster is Player)) return 0f;
            Item_Weapon w = Equipped.Hand(ctx, out _, out float power);
            return w == null ? 0f : w.GetDamageList().Sum(d => d.damage) * a.weapon * power;
        }

        /// <summary>"levelCap": 30% of the damage at level 1, rising evenly to full at the cap.</summary>
        internal static float LevelMult(int cap)
        {
            if (cap <= 1 || Player.instance == null) return 1f;
            return Mathf.Clamp(0.3f + 0.7f * (Player.instance.level - 1) / (cap - 1f), 0.3f, 1f);
        }

        internal static string LevelNote(int cap) => cap > 1 ? $" (grows with your level: {Mathf.RoundToInt(LevelMult(cap) * 100)}% now, full at level {cap})" : "";

        private static void Damage(ActionSpec a, ActionContext ctx, Character t)
        {
            if (t == ctx.caster && !ctx.backfire) return; // never hurt yourself (unless your wild magic turned on you)
            if (!Enum.TryParse(a.type ?? "Crushing", true, out DAMAGE_TYPE type)) type = DAMAGE_TYPE.Crushing;
            float v = Random.Range(a.min, Math.Max(a.min, a.max) + 1) + a.perRank * Math.Max(0, ctx.rank) + ScaleBonus(a, ctx);
            v += StackBonus(a, ctx, t) + WeaponBonus(a, ctx) + a.stored * ctx.stored;
            v *= ctx.damageMult * LevelMult(a.levelCap);
            int dmg = Math.Max(1, Mathf.RoundToInt(v));
            int hpBefore = t.GetHealth();
            var list = new List<DmgObj> { new DmgObj(type, dmg) };
            string log = $"{ctx.sourceName} hits {t.GetName()}";
            if (a.trueDamage)
            {
                Game.LOG($"{log} for <color=#FFFFFF>{dmg} true damage</color>.");
                if (HUD.instance != null) HUD.instance.MakeFct(dmg.ToString(), t.GetHitFx().position, false);
                t.DamageHealth(dmg, ctx.source);
                if (ctx.caster == Player.instance && Player_AbilityControl.instance != null) Player_AbilityControl.instance.OnDealDamage(t, ctx.source, dmg);
            }
            else if (ctx.source != null && ctx.caster != null)
            {
                ctx.source.SetCaster(ctx.caster);
                ctx.source.DealDamage(ctx.caster, t, list, log, ctx.source, null);
            }
            else t.TakeDamage(ctx.caster, list, log, null, 0f);
            // a blade-and-spell hit is a weapon hit too: the weapon's own on-hit effects, its ammo's, and every
            // passive that answers weapon hits go off, the way a normal attack sets them off
            if ((a.weapon > 0f || a.channel) && ctx.caster is Player && ctx.source != null && Equipped.Hand(ctx, out _, out _) is Item_Weapon wpn && t != null)
            {
                try { ctx.source.OnHit(wpn, list, t); }
                catch (Exception e) { Plugin.Log.LogWarning($"{ctx.sourceName}: weapon on-hit effects failed: {e.Message}"); }
            }
            if (a.lifesteal > 0f && ctx.caster != null && !ctx.caster.GetIsDead())
            {
                int dealt = Math.Max(0, hpBefore - Math.Max(0, t.GetHealth()));
                int heal = Mathf.RoundToInt(dealt * a.lifesteal);
                if (heal > 0)
                {
                    ctx.caster.Heal(heal, false);
                    if (HUD.instance != null) HUD.instance.MakeFct("<color=#FF5C7A>+" + heal + "</color>", ctx.caster.GetHitFx().position, false);
                }
            }
        }

        internal static void ApplyEffect(ActionSpec a, ActionContext ctx, Character t)
        {
            string id = EffectId(a.effect, ctx.classId);
            if (string.IsNullOrEmpty(id) || EffectManager.instance == null) return;
            // negative stacks bleed that many off (a resource that drains); the effect goes when none are left
            if (a.stacks < 0 && !Scaled(a))
            {
                if (t.effects.GetEffect(id) is ModEffect have) { have.RemoveStacks(-a.stacks); t.CalculateStats(); }
                return;
            }
            if (!EffectManager.instance.CanUseOn(id, t)) return;
            int stacks = ScaledCount(a, a.stacks, ctx, t);
            if (stacks <= 0) return;

            Effect existing = t.effects.GetEffect(id);
            if (existing is ModEffect me && (me.MaxStacks > 1 || !me.Stackable))
            {
                if (a.turns > 0) existing.OverrideDuration(a.turns); else existing.RefreshDuration();
                if (me.MaxStacks > 1) me.AddStacks(stacks, ctx);
                Triggers.Inflicted(ctx.caster, t, id);
                return;
            }
            Effect e;
            Triggers.ModApplying = true;
            try { e = EffectManager.instance.ApplyEffect(id, t); }
            finally { Triggers.ModApplying = false; }
            if (e == null) return;
            e.caster = ctx.caster;
            if (a.turns > 0) e.OverrideDuration(a.turns);
            e.Activate();
            if (e is ModEffect fresh)
            {
                fresh.OnApplied(ctx);
                if (fresh.MaxStacks > 1) fresh.AddStacks(stacks - 1, ctx);
            }
            t.CalculateStats();
            if (t == Player.instance && Menu_HUD.instance != null) Menu_HUD.instance.UpdatePlayerEffects();
            Triggers.Inflicted(ctx.caster, t, id);
        }

        // ---------------------------------------------------------------- credits, chance, execute

        /// <summary>Gains (amount above 0) or spends (below 0) credits; spending never goes below zero.</summary>
        private static void Credits(ActionSpec a, ActionContext ctx, Character t)
        {
            if (ctx.caster != Player.instance || Menu_Inventory.instance == null) return;
            int amt = a.amount + a.perRank * Math.Max(0, ctx.rank) + StackBonus(a, ctx, t);
            if (amt < 0) amt = -Math.Min(-amt, Menu_Inventory.instance.GetCredits());
            if (amt == 0) return;
            Menu_Inventory.instance.AdjustCredits(amt);
            Game.LOG($"{ctx.sourceName}: <color={(amt > 0 ? "#FFD966" : "#FF9F5C")}>{(amt > 0 ? "+" : "−")}{Math.Abs(amt)} credits</color>.");
            if (amt > 0) AudioManager.PlayEvent("event:/UI/UI_CashRegister");
        }

        /// <summary>Picks one weighted outcome and runs it on the same target.</summary>
        private static void RandomPick(ActionSpec a, ActionContext ctx, Character t)
        {
            var opts = (a.options ?? new List<RandomOption>()).Where(o => o != null).ToList();
            if (opts.Count == 0) return;
            int stacks = string.IsNullOrEmpty(a.stacksOf) ? 0 : UsedStacks(a, StacksOn(StackHolder(a, ctx, t), EffectId(a.stacksOf, ctx.classId)));
            var weights = opts.Select(o => Math.Max(0, o.weight + o.weightPerStack * stacks)).ToList();
            int total = weights.Sum();
            if (total <= 0) return;
            int roll = Random.Range(0, total);
            for (int i = 0; i < opts.Count; i++)
            {
                if (roll >= weights[i]) { roll -= weights[i]; continue; }
                if (!string.IsNullOrEmpty(opts[i].name)) Game.LOG($"{ctx.sourceName}: <color=#FFD966>{opts[i].name}</color>");
                Run(opts[i].actions, ctx, t);
                return;
            }
        }

        private static bool Tough(Character c) => Elites.IsElite(c) || c.HasTag("IMMORTAL");

        /// <summary>amount % (+ bonusPerStack per stack) to kill outright; tough enemies take elitePercent % of max health instead.</summary>
        private static void Execute(ActionSpec a, ActionContext ctx, Character t)
        {
            if (t == ctx.caster || t.GetIsDead()) return;
            int chance = a.amount + StackBonus(a, ctx, t) + ctx.chanceBonus;
            if (Random.Range(1, 101) > chance)
            {
                Game.LOG($"{ctx.sourceName}: {t.GetName()} survives the odds ({Mathf.Clamp(chance, 0, 100)}%).");
                return;
            }
            bool tough = Tough(t);
            int dmg = tough ? Math.Max(1, t.GetHealthMax() * Math.Max(0, a.elitePercent) / 100) : t.GetHealth();
            Game.LOG(tough
                ? $"{ctx.sourceName}: <color=#FF7272>{t.GetName()} is too tough to finish outright and takes {dmg}</color>."
                : $"{ctx.sourceName}: <color=#FF7272>{t.GetName()} is finished off!</color>");
            if (HUD.instance != null) HUD.instance.MakeFct("<color=#FF7272>" + dmg + "</color>", t.GetHitFx().position, false);
            t.DamageHealth(dmg, ctx.source);
            if (ctx.caster == Player.instance && Player_AbilityControl.instance != null) Player_AbilityControl.instance.OnDealDamage(t, ctx.source, dmg);
        }

        /// <summary>Drags the target up to `amount` tiles toward you (or the ability's aim point), one free tile at a time.</summary>
        private static void Pull(ActionSpec a, ActionContext ctx, Character t)
        {
            if (t == null || t.GetIsDead() || t == ctx.caster || t.HasTag("NOKNOCKBACK") || t.HasTag("BOSS") || t.GetMover() == null) return;
            NavNode start = t.GetMover().GetCurrentNavNode();
            if (start == null || ctx.caster == null) return;
            Vector3 dest = (a.pullToward ?? "").ToLowerInvariant() == "center" && ctx.center.HasValue ? ctx.center.Value : ctx.caster.transform.position;
            NavNode node = start;
            for (int i = 0; i < Math.Max(1, a.amount); i++)
            {
                float here = Vector3.Distance(node.transform.position, dest);
                NavNode next = node.GetLinks().Where(l => l != null && !l.BlockCheck() && Vector3.Distance(l.transform.position, dest) < here - 0.1f
                                                          && t.GetMover().MoveCheck(node, l))
                    .OrderBy(l => Vector3.Distance(l.transform.position, dest)).FirstOrDefault();
                if (next == null) break;
                node = next;
            }
            if (node == start) return;
            if (t.ai != null) t.ai.ForceMove = true;
            t.GetMover().MoveTo(start, node);
            if (t.anim != null) t.Flinch();
        }

        /// <summary>Your own summon dies on the spot, so its on-death effects (bursts, chains) go off.</summary>
        private static void Detonate(ActionContext ctx, Character t)
        {
            if (t == null || t == Player.instance || t.ai == null || t.ai.GetTeam() != AI.AI_TEAM.Player || t.GetIsDead()) return;
            SpawnVfx("PA_Elementalist_ChainLightning.shockImpact", t.GetHitFx().position);
            t.xpValue = 0;
            t.AddTag("NOXP");
            t.DamageHealth(t.GetHealth() + 999, null);
        }

        /// <summary>Sends one of your own minions away: no death, no corpse, no loot. Never works on enemies.</summary>
        private static void Dismiss(ActionContext ctx, Character t)
        {
            if (t == null || t == ctx.caster) return;
            PetHandling.Unsummon(t, ctx.sourceName, breakdown: false);
        }

        // ---------------------------------------------------------------- items

        private static void GiveItem(ActionSpec a, ActionContext ctx)
        {
            if (ctx.caster != Player.instance || string.IsNullOrEmpty(a.item)) return;
            string id = Crafting.ResolveItem(a.item, null);
            if (Crafting.Give(id, Math.Max(1, a.amount)) != null) Game.LOG($"{ctx.sourceName}: you get {Math.Max(1, a.amount)}× {ItemRegistry.NameOf(id)}.");
        }

        private static void ConsumeItem(ActionSpec a, ActionContext ctx)
        {
            if (ctx.caster != Player.instance) return;
            var cost = new CostSpec { item = a.item, tag = a.tag, count = Math.Max(1, a.amount) };
            if (cost.item != null) cost.resolved = Crafting.ResolveItem(cost.item, null);
            if (Crafting.Count(cost) >= cost.count) Crafting.Consume(cost);
        }

        /// <summary>Destroys one of the targeted inventory item and hands out "gives".</summary>
        private static void Salvage(ActionSpec a, ActionContext ctx)
        {
            Item it = ctx.item;
            if (it == null || ctx.caster != Player.instance) return;
            string name = it.GetName(false);
            if (it.stack > 1) { it.SetStack(it.stack - 1); it.CalculateValue(); }
            else
            {
                Menu_Inventory.instance.RemoveItem(it, null, isSilent: true);
                Object.Destroy(it.gameObject);
            }
            var got = new List<string>();
            foreach (GiveSpec g in a.gives ?? new List<GiveSpec>())
            {
                if (g.chance < 100 && Random.Range(1, 101) > g.chance + ctx.chanceBonus) continue;
                string id = Crafting.ResolveItem(g.item, null);
                if (Crafting.Give(id, Math.Max(1, g.count)) != null) got.Add($"{Math.Max(1, g.count)}× {ItemRegistry.NameOf(id)}");
            }
            Menu_Inventory.instance.Refresh();
            Game.LOG($"{ctx.sourceName}: {System.Text.RegularExpressions.Regex.Replace(name, "<.*?>", "")} breaks down into {(got.Count > 0 ? string.Join(", ", got) : "nothing useful")}.");
        }

        /// <summary>"@Hexed" -> this class's effect id; anything else is a vanilla id.</summary>
        public static string EffectId(string name, string classId)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name.StartsWith("#")) return ModEffect.MakeId(ClassRegistry.LibId, name.Substring(1));
            if (name.StartsWith("@")) return ModEffect.MakeId(classId, name.Substring(1));
            // "MOD_Etcher@Corroded": another class's effect
            int at = name.IndexOf('@');
            return at > 0 ? ModEffect.MakeId(name.Substring(0, at), name.Substring(at + 1)) : name;
        }

        /// <summary>True for a mod effect nothing defines any more; a vanilla effect is never "gone".</summary>
        internal static bool PoolGone(string name, string classId)
        {
            string id = EffectId(name, classId);
            return !string.IsNullOrEmpty(id) && id.StartsWith(ModEffect.Prefix) && !ModEffect.Specs.ContainsKey(id);
        }

        // ---------------------------------------------------------------- rules text

        public static string Describe(IEnumerable<ActionSpec> list, string classId, string defaultWho)
        {
            if (list == null) return "";
            return string.Join("\n", list.Select(a => DescribeOne(a, classId, defaultWho)).Where(s => s.Length > 0));
        }

        private static string DescribeOne(ActionSpec a, string classId, string defaultWho)
        {
            if (a.@do == "tether" || a.@do == "show") return ""; // only a visual
            string who = (a.to ?? "target").ToLowerInvariant() switch
            {
                "self" => "you",
                "attacker" => "the attacker",
                "victim" => "the victim",
                "ally" => "the ally",
                "summons" => "each of your summons",
                _ => defaultWho,
            };
            string s;
            switch ((a.@do ?? "").ToLowerInvariant())
            {
                case "damage":
                    {
                        Enum.TryParse(a.type ?? "Crushing", true, out DAMAGE_TYPE type);
                        string range = a.max > a.min ? $"{a.min}-{a.max}" : $"{a.min}";
                        s = $"Deals <color={DmgObj.GetDamageColor(type)}>{range}{Scaling(a)}{(a.weapon > 0f ? $" (+{Mathf.RoundToInt(a.weapon * 100)}% weapon, with its on-hit effects)" : "")}{(a.stored > 0f ? $" (+{Mathf.RoundToInt(a.stored * 100)}% of the damage it took while this lasted)" : "")} {Game.GetSpriteString(type)}</color>{(a.trueDamage ? " <color=#FFFFFF>true damage</color>" : "")} to {who}{LevelNote(a.levelCap)}";
                        break;
                    }
                case "heal": s = $"Heals {who} for <color=#5CFF6B>{a.amount}{Scaling(a)}</color>"; break;
                case "stamina": s = $"{(a.amount >= 0 ? "Restores" : "Drains")} <color=#7CFC00>{Math.Abs(a.amount)}{Scaling(a)}</color> {StatObj.GetStatString(STAT_TYPE.Stamina)} for {who}"; break;
                case "shield": s = $"Gives {who} <color=#6FD3FF>{a.amount}{Scaling(a)}</color> {StatObj.GetStatString(STAT_TYPE.Shield)} overshield"; break;
                case "effect":
                    s = a.stacks < 0 && !Scaled(a)
                        ? $"Removes {-a.stacks} <color=#FFD15C>{EffectName(a.effect, classId)}</color> from {who}"
                        : $"Applies <color=#FFD15C>{EffectName(a.effect, classId)}</color> to {who}{(a.turns > 0 ? $" for {a.turns} turns" : "")}";
                    break;
                case "stun": s = $"Stuns {who} for {Turns(a)} turn{(Turns(a) > 1 ? "s" : "")}"; break;
                case "knockback": s = $"Knocks {who} back {Math.Max(1, a.amount)}"; break;
                case "blind": s = $"Blinds {who}"; break;
                case "cooldowns": s = $"Reduces your other cooldowns by {Math.Max(1, a.amount)}"; break;
                case "purge": s = $"Removes {EffectName(a.effect, classId)} from {who}"; break;
                case "teleport": s = "Teleports you to the target spot"; break;
                case "dispel": s = $"Strips {(a.amount > 0 ? a.amount.ToString() : "all")} buff{(a.amount == 1 ? "" : "s")} from {who}{(string.IsNullOrEmpty(a.effect) ? "" : $"; you gain {EffectName(a.effect, classId)} per buff removed")}"; break;
                case "cleanse": s = $"Removes {(a.amount > 0 ? a.amount.ToString() : "all")} debuff{(a.amount == 1 ? "" : "s")} from {who}"; break;
                case "chain":
                    {
                        string link = string.IsNullOrEmpty(a.effect) ? "enemies" : $"enemies with {EffectName(a.effect, classId)}";
                        string inner = Describe(a.actions, classId, "each target").Replace("• ", "    - ");
                        s = a.leap ? $"You charge {who}, then on to up to {a.jumps} nearby {link}, landing beside each{(a.falloff != 1f ? $" ({Mathf.RoundToInt(a.falloff * 100)}% power per jump)" : "")}:\n{inner}" :
                            $"Strikes {who}, then jumps to up to {a.jumps} nearby {link}{(a.falloff != 1f ? $" ({Mathf.RoundToInt(a.falloff * 100)}% power per jump)" : "")}:\n{inner}";
                        break;
                    }
                case "summon": s = $"Summons {(a.amount > 1 && !Scaled(a) ? a.amount + " " : "a ")}{a.summonName ?? CreatureName(a.creature)}{(a.turns > 0 ? $" for {a.turns} turns" : "")}{(a.maxSummons > 0 ? $" (max {a.maxSummons})" : "")}{SummonGrowth(a)}"; break;
                case "echo": s = $"Echoes your last new ability onto {who} at {Mathf.RoundToInt(a.falloff * 100)}% power"; break;
                case "tame":
                    {
                        string odds = $"{a.amount}%{(a.perRank > 0 ? $" (+{a.perRank}/rank)" : "")}{Scaling(a)}";
                        string won = a.then != null && a.then.Count > 0 ? "\n" + Describe(a.then, classId, "it").Replace("• ", "    - ") : "";
                        string lost = a.@else != null && a.@else.Count > 0 ? "\n  If it throws you off:\n" + Describe(a.@else, classId, who).Replace("• ", "    - ") : "";
                        s = $"Tries to bind {who} to you for good ({odds}, up to +50% more the more wounded it is; not bosses or elites){(a.maxSummons > 0 ? $", at most {a.maxSummons} at a time" : "")}{won}{lost}";
                        break;
                    }
                case "giveitem": s = $"Gives you {Math.Max(1, a.amount)}× {ItemRegistry.NameOf(Crafting.ResolveItem(a.item, null))}"; break;
                case "consumeitem": s = $"Uses up {Math.Max(1, a.amount)}× {(a.item != null ? ItemRegistry.NameOf(Crafting.ResolveItem(a.item, null)) : (a.tag ?? "?").ToLowerInvariant())}"; break;
                case "salvage": s = $"Breaks the item down into {string.Join(", ", (a.gives ?? new List<GiveSpec>()).Select(g => $"{g.count}× {ItemRegistry.NameOf(Crafting.ResolveItem(g.item, null))}{(g.chance < 100 ? $" ({g.chance}%)" : "")}"))}"; break;
                case "craft": s = "Opens field crafting"; break;
                case "runepanel": s = "Opens the rune matrix, where you can charge and discharge runes, even in combat"; break;
                case "runerestore": s = $"Puts {Math.Max(1, a.amount)} cast{(a.amount > 1 ? "s" : "")} of charge back into each of your runes"; break;
                case "credits": s = a.amount >= 0 ? $"You gain <color=#FFD966>{a.amount}{(a.perRank > 0 ? $" (+{a.perRank}/rank)" : "")} credits</color>" : $"Costs another <color=#FF9F5C>{-a.amount} credits</color>"; break;
                case "dismiss": s = $"Recalls {who} (gone without a fight: no corpse, no loot)"; break;
                case "phase": s = "Spends the summon and phases you into its place"; break;
                case "pull": s = $"Pulls {who} {Math.Max(1, a.amount)} toward {((a.pullToward ?? "").ToLowerInvariant() == "center" ? "the centre" : "you")}"; break;
                case "taunt":
                    {
                        int tt = Afflictions.TauntTurns(a);
                        string to = (a.to ?? "").ToLowerInvariant();
                        s = to == "self" || to == "summon" || to == "summons"
                            ? $"Enemies within {(a.radius > 0 ? a.radius : 4f):0.#} that can see {who} attack {(to == "self" ? "you" : "it")} for {tt} turn{(tt > 1 ? "s" : "")} (<color=#FF9F5C>Taunted</color>)"
                            : $"<color=#FF9F5C>Taunts</color> {who} into attacking you for {tt} turn{(tt > 1 ? "s" : "")}";
                        break;
                    }
                case "silence": s = $"<color=#B58CFF>Silences</color> {who} for {Afflictions.SilenceTurns(a)} turn{(Afflictions.SilenceTurns(a) > 1 ? "s" : "")}: basic attacks only (bosses shrug it off)"; break;
                case "exhaust": s = $"Sets back {who}'s special attacks by {Math.Max(1, a.amount)} turn{(Math.Max(1, a.amount) > 1 ? "s" : "")}"; break;
                case "swap": s = $"You trade places with {who}"; break;
                case "throw":
                    {
                        string imp = Describe(a.actions, classId, "both").Replace("• ", "").Replace("\n", "; ");
                        s = $"Throws {who} up to {Math.Max(1, a.amount)} tiles away from you" + (imp.Length > 0 ? $"; slamming into someone or a wall: {imp}" : "");
                        break;
                    }
                case "leap": s = $"You leap to {who}"; break;
                case "warp":
                    s = (a.warpTo ?? "").ToLowerInvariant() == "behind" ? $"You warp behind {who}"
                      : (a.warpTo ?? "").ToLowerInvariant() == "away" ? $"Warps {who} up to {Math.Max(1, a.amount)} tiles away from you"
                      : $"Warps {who} to a random spot up to {Math.Max(1, a.amount)} tiles off"; break;
                case "recall":
                    {
                        string per = Describe(a.actions, classId, "each enemy in the way").Replace("• ", "").Replace("\n", "; ");
                        s = $"Every {(a.fieldName ?? "?").ToLowerInvariant()} on the floor flies back to you: {per}";
                        break;
                    }
                case "ricochet":
                    {
                        string per = Describe(a.actions, classId, "it").Replace("• ", "").Replace("\n", "; ");
                        s = $"Bounces off every {(a.fieldName ?? "?").ToLowerInvariant()} on the floor into the nearest enemy within {(a.radius > 0 ? a.radius : 4f):0.#} (used up): {per}";
                        break;
                    }
                case "arm": s = a.reaction != null ? Reactions.Describe(a.reaction, classId) : "Arms nothing"; break;
                case "spring": s = "Every armed reaction goes off once right now at the nearest enemy it can reach, spending a charge"; break;
                case "hazard": s = $"Leaves {GroundFields.HazardName(a.hazard)} on the floor{(a.radius > 0 ? $" around {who} (area {a.radius:0.#})" : $" under {who}")}"; break;
                case "field":
                    {
                        string aff = (a.affects ?? "enemies").ToLowerInvariant() switch { "allies" => "allies", "all" => "anyone", _ => "enemies" };
                        string inner = Describe(a.actions, classId, aff + " in it").Replace("• ", "").Replace("\n", "; ");
                        s = $"Leaves {(a.fieldName ?? "a field")} for {Math.Max(1, a.turns)} turns{(a.radius > 0 ? $" (area {a.radius:0.#})" : "")}: each turn{(a.onEnter ? " and when they step in" : "")}, {inner}";
                        break;
                    }
                case "trap":
                    {
                        string aff = (a.affects ?? "enemies").ToLowerInvariant() switch { "allies" => "ally", "all" => "one", _ => "enemy" };
                        string hit = a.radius > 0 ? $"everyone{((a.affects ?? "enemies").ToLowerInvariant() == "all" ? "" : $" ({(aff == "ally" ? "allies" : "enemies")})")} within {a.radius:0.#}" : "it";
                        string inner = Describe(a.actions, classId, hit).Replace("• ", "").Replace("\n", "; ");
                        var extra = new List<string>();
                        if (a.charges != 1) extra.Add(a.charges > 0 ? $"goes off {a.charges} times" : "never runs out");
                        if (a.fuse > 0) extra.Add($"goes off by itself after {a.fuse} turn{(a.fuse > 1 ? "s" : "")}");
                        if (a.turns > 0) extra.Add($"fizzles after {a.turns} turns");
                        if (a.maxTraps > 0) extra.Add($"up to {a.maxTraps} at once");
                        s = $"Sets {(a.fieldName ?? "a trap")}{(a.armTurns > 0 ? $" (live after {a.armTurns} turn{(a.armTurns > 1 ? "s" : "")})" : "")}: the first {aff} to "
                          + (a.triggerRadius > 0 ? $"come within {a.triggerRadius:0.#}" : "step on it") + $" sets it off: {inner}"
                          + (extra.Count > 0 ? $" ({string.Join("; ", extra)})" : "");
                        break;
                    }
                case "springtraps": s = $"Sets off every {(a.fieldName ?? "trap of yours").ToLowerInvariant()} on this floor at once"; break;
                case "wallspikes":
                    {
                        string inner = Describe(a.actions, classId, who).Replace("• ", "    - ");
                        s = $"A spike bursts from every wall beside {who}{(a.amount > 0 ? $" (up to {a.amount})" : "")}, each one:\n{inner}";
                        break;
                    }
                case "wildcast": s = WildMagic.Describe(a, who); break;
                case "drifter": s = Drifters.Describe(a, classId); break;
                case "horror": s = Horrors.Describe(a, classId); break;
                case "surge":
                    s = $"{a.surgeName ?? "A surge"}: lands on {who}{(a.atYou > 0 ? $" ({a.atYou}% on you instead, less the luckier you are)" : "")}" +
                        $"{(a.radius > 0 ? $" and hits everyone within {a.radius:0.#}, friend or foe" : "")}{LevelNote(a.levelCap)}:\n" + Describe(a.actions, classId, "each");
                    break;
                case "raisewall": s = $"Raises a wall {Math.Max(1, a.amount)} wide across your aim for {(a.turns > 0 ? a.turns : 4)} turns (it blocks movement and counts as a wall)"; break;
                case "counter":
                    {
                        string whose = who == "you" ? "your" : who + "'s";
                        string cap = a.counterMax > 0 ? $" (max {a.counterMax})" : "";
                        s = a.counterSet ? $"Sets {whose} <color=#9FD3FF>{a.counter}</color> to {a.amount}"
                            : a.amount >= 0 ? $"Adds {a.amount} to {whose} <color=#9FD3FF>{a.counter}</color>{cap}"
                            : $"Takes {-a.amount} from {whose} <color=#9FD3FF>{a.counter}</color>";
                        break;
                    }
                case "if":
                    {
                        string yes = Describe(a.then, classId, who).Replace("• ", "    - ");
                        string no = Describe(a.@else, classId, who).Replace("• ", "    - ");
                        s = $"If {When.Describe(a.when, classId)}:\n{(yes.Length > 0 ? yes : "    - nothing")}" + (no.Length > 0 ? $"\n  Otherwise:\n{no}" : "");
                        return "• " + (a.chance < 100 ? $"{a.chance}%: " : "") + s;
                    }
                case "detonate": s =$"{who} blows apart on the spot (setting off whatever it carries)"; break;
                case "swapbody": s ="You take over your nearest other body; the one you leave fights on beside you"; break;
                case "revivebody": s = $"A fallen body of yours gets back up next to you with {(a.amount > 0 ? a.amount : 50)}% health"; break;
                case "execute":
                    s = $"<color=#FF7272>{a.amount}%</color> chance to finish {who} outright; bosses, elites and kill squads take {a.elitePercent}% of their max health instead";
                    break;
                case "random":
                    {
                        var opts = (a.options ?? new List<RandomOption>()).Where(o => o != null).ToList();
                        int total = Math.Max(1, opts.Sum(o => Math.Max(0, o.weight)));
                        int Pct(RandomOption o) => Mathf.RoundToInt(100f * Math.Max(0, o.weight) / total);
                        bool lucky = opts.Any(o => o.weightPerStack != 0) && !string.IsNullOrEmpty(a.stacksOf);
                        // Long lists (rune and loot tables) on one line, so the tooltip still fits on screen.
                        if (!lucky && opts.Count > 0 && opts.All(o => o.actions != null && o.actions.Count == 1 && (o.actions[0].@do ?? "").ToLowerInvariant() == "giveitem"))
                        {
                            var items = opts.GroupBy(o => ItemRegistry.NameOf(Crafting.ResolveItem(o.actions[0].item, null)))
                                .Select(g => (name: g.Key, pct: g.Sum(Pct))).OrderByDescending(x => x.pct)
                                .Select(x => $"{x.name} <color=#FFD966>{x.pct}%</color>");
                            s = "Gives you one of: " + string.Join(", ", items);
                            return a.chance < 100 ? $"• {a.chance}%: {s}" : "• " + s;
                        }
                        if (!lucky && (opts.Count > 5 || opts.Count >= 3 && opts.All(o => o.actions != null && o.actions.Count == 1)))
                        {
                            var shortList = opts.Select(o =>
                            {
                                string what = o.name ?? Describe(o.actions, classId, who).Replace("• ", "").Replace("\n", "; ");
                                return $"{(what.Length > 0 ? what : "nothing")} <color=#FFD966>{Pct(o)}%</color>";
                            });
                            s = "One of: " + string.Join(", ", shortList);
                            return a.chance < 100 ? $"• {a.chance}%: {s}" : "• " + s;
                        }
                        var lines = opts.Select(o =>
                        {
                            string inner = Describe(o.actions, classId, who).Replace("• ", "").Replace("\n", "; ");
                            string luck = o.weightPerStack != 0 && !string.IsNullOrEmpty(a.stacksOf) ? $", {(o.weightPerStack > 0 ? "likelier" : "rarer")} with each {EffectName(a.stacksOf, classId)}" : "";
                            return $"    - <color=#FFD966>{o.name ?? "Outcome"}</color> ({Mathf.RoundToInt(100f * Math.Max(0, o.weight) / total)}%{luck}): {(inner.Length > 0 ? inner : "nothing")}";
                        });
                        s = "One of these happens:\n" + string.Join("\n", lines);
                        string full = "• " + s;
                        if (a.chance < 100) full = $"• {a.chance}%: {s}";
                        return full;
                    }
                default:
                    s = PluginHost.ActionText(a, who);
                    if (s.Length == 0) return "";
                    break;
            }
            if ((a.to ?? "").ToLowerInvariant() == "summon") s = s.Replace(defaultWho, "your summon");
            if ((a.@do ?? "").ToLowerInvariant() == "dismiss" && (a.ifSummonAlive ?? "").StartsWith("group:", StringComparison.OrdinalIgnoreCase))
                return $"• Spends one of your {a.ifSummonAlive.Substring(6)}s (it's gone: no corpse, no loot)";
            if (!string.IsNullOrEmpty(a.ifSummonAlive)) s = $"While {SummonWord(a.ifSummonAlive)} is out: {s.Substring(0, 1).ToLower()}{s.Substring(1)}";
            if (!string.IsNullOrEmpty(a.unlessSummonAlive)) s = $"If {SummonWord(a.unlessSummonAlive)} isn't out: {s.Substring(0, 1).ToLower()}{s.Substring(1)}";
            string act = (a.@do ?? "").ToLowerInvariant();
            if (act == "effect" && a.stacks > 1 && !Scaled(a)) s += $" (+{a.stacks} stacks)";
            if (Scaled(a))
            {
                string sf = (a.stacksFrom ?? "").ToLowerInvariant(); string on = sf == "self" ? "on you" : sf == "target" ? "on the target" : $"on {who}";
                string per = a.stacksOf == "summons" ? "summon you have" : a.stacksOf == "bodies" ? "other living body" :
                    a.stacksOf.StartsWith("counter:", StringComparison.OrdinalIgnoreCase) ? $"point of <color=#9FD3FF>{a.stacksOf.Substring(8)}</color> {on}" :
                    a.stacksOf.StartsWith("group:", StringComparison.OrdinalIgnoreCase) ? $"{a.stacksOf.Substring(6)} you have" : $"stack of {EffectName(a.stacksOf, classId)} {on}";
                string used = a.consumeStacks && !a.stacksOf.StartsWith("group:", StringComparison.OrdinalIgnoreCase) && a.stacksOf != "summons" && a.stacksOf != "bodies" ? (a.stacksUsed > 0 ? $" (uses up to {a.stacksUsed} and spends only those)" : " (consumes them)") : a.stacksUsed > 0 ? $" (counts up to {a.stacksUsed})" : "";
                // effect and summon actions scale a count (stacks, summons) rather than an amount
                int baseN = act == "effect" ? a.stacks : act == "summon" ? a.amount : -1;
                if (baseN < 0) s += $", +{a.bonusPerStack} per {per}{used}";
                else if (baseN > 0) s += $" ({baseN}, plus {a.bonusPerStack} more per {per}){used}";
                else s += $": {a.bonusPerStack} per {per}{used}";
            }
            if (a.lifesteal > 0f) s += $", healing you for {Mathf.RoundToInt(a.lifesteal * 100)}% of the damage";
            if (!string.IsNullOrEmpty(a.unlessTargetHas)) s = $"Unless {who} has {EffectName(a.unlessTargetHas, classId)}: {s.Substring(0, 1).ToLower()}{s.Substring(1)}";
            if (!string.IsNullOrEmpty(a.ifTargetHas)) s = $"If {who} has {EffectName(a.ifTargetHas, classId)}{(a.consume ? " (consumed)" : "")}: {s.Substring(0, 1).ToLower()}{s.Substring(1)}";
            if (!string.IsNullOrEmpty(a.ifSelfHas)) s = $"While you have {EffectName(a.ifSelfHas, classId)}: {s.Substring(0, 1).ToLower()}{s.Substring(1)}";
            if (a.when != null)
            {
                string cond = When.Describe(a.when, classId);
                if (cond.Length > 0) s = $"If {cond}: {s.Substring(0, 1).ToLower()}{s.Substring(1)}";
            }
            if (a.chance < 100) s = $"{a.chance}%: {s}";
            if (!string.IsNullOrEmpty(a.unlessSkill)) s = $"Without {Synergies.TreeNames(a.unlessSkill)}: {s.Substring(0, 1).ToLower()}{s.Substring(1)}";
            if (!string.IsNullOrEmpty(a.ifSkill))
                s = $"<color=#7FE0C8>With {Synergies.TreeNames(a.ifSkill)}{(a.skillRank > 1 ? $" (rank {a.skillRank}+)" : "")}</color>: {s.Substring(0, 1).ToLower()}{s.Substring(1)}";
            return "• " + s + ".";
        }

        private static string SummonWord(string spec) => (spec ?? "").ToLowerInvariant() switch
        {
            "self" => "this ability's summon",
            "any" => "any of your summons",
            _ when spec.StartsWith("group:", StringComparison.OrdinalIgnoreCase) => "one of your " + spec.Substring(6) + "s",
            _ => "your " + spec.Replace("_", " ").ToLowerInvariant(),
        };

        /// <summary>", growing with your Psi Power" for a summon whose scaleStat isn't Smarts.</summary>
        private static string SummonGrowth(ActionSpec a)
        {
            string stat = a.scaleStat ?? EnemyRegistry.DefOf(EnemyRegistry.Creature(a.creature))?.scaleStat;
            return Enum.TryParse(stat ?? "", true, out STAT_TYPE st) && st != STAT_TYPE.Smarts ? $", growing with your {StatObj.GetStatString(st)}" : "";
        }

        private static string CreatureName(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return "ally";
            if (spec.Contains(".")) spec = spec.Split('.')[1].Replace("Prefab", "").Replace("character", "construct");
            return spec.Replace("_", " ").Trim().ToLowerInvariant();
        }

        private static string Scaling(ActionSpec a)
        {
            string r = "";
            var stats = ScaleStats(a).ToList();
            if (stats.Count > 0)
                r += $" (+{Mathf.RoundToInt(a.scaleMult * 100)}% {ScaleWords(stats)})";
            if (a.perRank > 0) r += $" (+{a.perRank}/rank)";
            return r;
        }

        private static string ScaleWords(List<List<STAT_TYPE>> stats) =>
            string.Join(" and ", stats.Select(any => any.Count == 1 ? StatObj.GetStatString(any[0]) : string.Join(" or ", any.Select(st => StatObj.GetStatString(st))) + ", whichever is higher"));

        /// <summary>"Smarts and Psi Power" for a "scale" string.</summary>
        internal static string ScaleWords(string scale) => ScaleWords(ScaleStats(scale).ToList());

        internal static string EffectNameOf(string name, string classId) => EffectName(name, classId);

        private static string EffectName(string name, string classId)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            if ((name.StartsWith("@") || name.StartsWith("#") || name.IndexOf('@') > 0) && ModEffect.Specs.TryGetValue(EffectId(name, classId), out var rt))
                return rt.spec.name ?? name.Substring(name.IndexOf('@') + 1);
            Effect e = EffectManager.instance != null ? EffectManager.instance.GetEffect(name, false) : null;
            string n = e != null ? e.GetName() : null;
            return string.IsNullOrEmpty(n) ? name.Replace("EFFECT_", "") : n;
        }

        public static string DescribeProc(ProcSpec p, string classId, int chanceBonus)
        {
            string when = (p.on ?? "").ToLowerInvariant() switch
            {
                "hit" => "when you hit",
                "kill" => "when you kill",
                "hurt" => "when you are hurt",
                "dodge" => "when you dodge",
                "turn" => "each turn",
                "reaction" => "when one of your reactions goes off",
                "crit" => "when you land a critical hit",
                "miss" => "when you miss",
                "cast" => string.IsNullOrEmpty(p.match) ? "when you use an ability" : $"when you use {p.match}",
                "move" => (p.match ?? "").ToLowerInvariant() switch
                {
                    "any" => "when you move",
                    "ability" => "when one of your skills moves you",
                    "forced" => "when something moves you against your will",
                    _ => "when you take a step",
                },
                "wait" => "when you wait a turn",
                "heal" => "when you are healed",
                "lowhealth" => $"when your health drops below {p.threshold}%",
                "allydeath" => "when one of your summons or pets dies",
                "effectgained" => string.IsNullOrEmpty(p.match) ? "when you gain an effect" : $"when you gain {EffectName(p.match, classId)}",
                "effectlost" => string.IsNullOrEmpty(p.match) ? "when an effect on you ends" : $"when {EffectName(p.match, classId)} ends",
                "combatstart" => "when a fight starts",
                "combatend" => "when a fight ends",
                "enterroom" => "when you enter a new room",
                "loot" => "when you pick up credits",
                "levelup" => "when you level up",
                "enemydeath" => "when an enemy dies",
                "allykill" => "when one of your allies kills an enemy",
                "allyhurt" => "when one of your allies is hurt",
                "summon" => "when an ally joins you",
                "inflict" => string.IsNullOrEmpty(p.match) ? "when you put an effect on an enemy" : $"when you inflict {EffectName(p.match, classId)}",
                "useitem" => string.IsNullOrEmpty(p.match) ? "when you use an item" : $"when you use {p.match}",
                "equip" => string.IsNullOrEmpty(p.match) ? "when you equip an item" : $"when you equip {p.match}",
                "unequip" => string.IsNullOrEmpty(p.match) ? "when you take off an item" : $"when you take off {p.match}",
                "search" => "when you search a container",
                "newfloor" => "when you reach another floor",
                "shieldbreak" => "when your shield breaks",
                "charge" => "when you charge",
                "leap" => "when you leap",
                "teleport" => "when you teleport",
                "knockback" => "when you knock an enemy back",
                "swap" => (p.match ?? "").ToLowerInvariant() switch
                {
                    "places" => "when you trade places with someone",
                    "weapon" => "when you swap weapons",
                    _ => "when you trade places or swap weapons",
                },
                "chain" => "when one of your chains jumps to another enemy",
                _ => PluginHost.EventText(p.on) ?? p.on,
            };
            int chance = Math.Min(100, p.chance + chanceBonus);
            if (p.when != null) { string c = When.Describe(p.when, classId); if (c.Length > 0) when += $", if {c}"; }
            if (p.every > 1) when = $"every {p.every}{Ordinal(p.every)} time {when.Replace("when ", "").Replace("each turn", "a turn passes")}";
            var limits = new List<string>();
            if (p.cooldown > 0) limits.Add($"then not for {p.cooldown} turn{(p.cooldown > 1 ? "s" : "")}");
            if (p.maxPerTurn > 0) limits.Add($"at most {p.maxPerTurn}× a turn");
            if (limits.Count > 0) when += $" ({string.Join(", ", limits)})";
            string who = p.radius > 0 ? (p.pick > 0 ? $"{(p.pick == 1 ? "a random enemy" : p.pick + " random enemies")} within {p.radius:0.#}" : $"enemies within {p.radius:0.#}") : DefaultProcWho(p.on);
            string head = chance < 100 ? $"<b>{chance}% {when}:</b>" : $"<b>{char.ToUpper(when[0]) + when.Substring(1)}:</b>";
            return head + "\n" + Describe(p.actions, classId, who);
        }

        public static string DefaultProcWho(string on) => (on ?? "").ToLowerInvariant() switch
        {
            "hit" => "the target",
            "crit" => "the target",
            "miss" => "the target",
            "kill" => "the victim",
            "hurt" => "the attacker",
            "dodge" => "the attacker",
            "reaction" => "that enemy",
            "cast" => "the ability's target",
            "allydeath" => "the fallen ally",
            "enemydeath" => "the fallen enemy",
            "allykill" => "the victim",
            "allyhurt" => "the attacker",
            "shieldbreak" => "the attacker",
            "charge" => "the target",
            "leap" => "the target",
            "knockback" => "that enemy",
            "swap" => "the one you swapped with",
            "chain" => "the enemy it jumped to",
            "summon" => "the new ally",
            "inflict" => "that enemy",
            _ => "you",
        };

        private static string Ordinal(int n) => n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };

        // ---------------------------------------------------------------- borrowed visuals

        private static readonly Dictionary<string, GameObject> vfxCache = new Dictionary<string, GameObject>();

        /// <summary>At most this many copies of one particle play at once; an area hit on six enemies isn't six explosions.</summary>
        private const int MaxCopies = 3;

        /// <summary>
        /// Spawns a vanilla particle by effect id ("EFFECT_Fire") or ability id ("VOID_PSYKER_BLAST[.field]").
        /// "name@0.5" plays it at half size.
        /// </summary>
        public static void SpawnVfx(string spec, Vector3 pos)
        {
            if (string.IsNullOrEmpty(spec)) return;
            SplitScaleOnly(spec, out string own);
            float scale = SplitScale(spec, out string name);
            GameObject prefab = ResolveVfx(name);
            if (prefab == null) return;
            if (VfxLife.Live.Count(l => l != null && l.spec == spec) >= MaxCopies) return;
            GameObject go = Object.Instantiate(prefab, pos, Quaternion.identity);
            if (CustomVfx.AliasOnFloor(own)) CustomVfx.GroundSnap.Floor(go);
            CustomVfx.Trim(own, go);
            Rescale(go, scale);
            go.SetActive(true);
            Wake(prefab, go);
            go.AddComponent<VfxLife>().spec = spec;
        }

        /// <summary>
        /// Sends a particle (mod or vanilla) flying from one point to another on a shallow arc; returns how long the
        /// flight takes so the caller can land the hit when it arrives. Speed is in tiles per second.
        /// </summary>
        public static float SpawnTravel(string spec, Vector3 from, Vector3 to, float speed)
        {
            if (string.IsNullOrEmpty(spec)) return 0f;
            float scale = SplitScale(spec, out string name);
            GameObject prefab = ResolveVfx(name);
            if (prefab == null) return 0f;
            float time = Mathf.Clamp(Vector3.Distance(from, to) / Mathf.Max(1f, speed), 0.12f, 0.6f);
            GameObject go = Object.Instantiate(prefab, from, Quaternion.identity);
            Rescale(go, scale);
            var t = go.AddComponent<CustomVfx.Travel>();
            t.from = from; t.to = to; t.time = time; t.arc = Mathf.Min(0.6f, Vector3.Distance(from, to) * 0.08f);
            t.Prepare();
            go.SetActive(true);
            Wake(prefab, go);
            go.AddComponent<VfxLife>().spec = spec;
            return time;
        }

        /// <summary>"NAME@0.5" → 0.5 and NAME; no suffix → 1.</summary>
        internal static float SplitScale(string spec, out string name)
        {
            float own = SplitScaleOnly(spec, out name);
            for (int hop = 0; hop < 4 && CustomVfx.Alias(name) != null; hop++) own *= SplitScaleOnly(CustomVfx.Alias(name), out name);
            return own;
        }

        private static float SplitScaleOnly(string spec, out string name)
        {
            name = spec;
            int at = spec?.LastIndexOf('@') ?? -1;
            if (at <= 0) return 1f;
            name = spec.Substring(0, at);
            return float.TryParse(spec.Substring(at + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s) && s > 0f ? s : 1f;
        }

        /// <summary>Particles ignore their transform's scale unless told to follow it.</summary>
        internal static void Rescale(GameObject go, float scale)
        {
            if (Mathf.Approximately(scale, 1f)) return;
            go.transform.localScale *= scale;
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            foreach (Light l in go.GetComponentsInChildren<Light>(true)) l.range *= scale;
        }

        /// <summary>
        /// Lets a borrowed particle play out: looping systems (effect auras used as a one-off) stop emitting after a
        /// moment and the object goes once its last particles have faded, instead of everything vanishing at once.
        /// </summary>
        internal class VfxLife : MonoBehaviour
        {
            internal static readonly List<VfxLife> Live = new List<VfxLife>();
            internal string spec;
            private ParticleSystem[] systems;
            private float born;

            private void Awake() => Live.Add(this);
            private void OnDestroy() => Live.Remove(this);

            private void Start()
            {
                born = Time.time;
                systems = GetComponentsInChildren<ParticleSystem>(true);
            }

            private void Update()
            {
                float age = Time.time - born;
                if (systems == null || systems.Length == 0) { if (age > 3f) Destroy(gameObject); return; }
                if (age > 1.5f)
                    foreach (ParticleSystem ps in systems)
                        if (ps != null && ps.isEmitting && ps.main.loop) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                if ((age > 1.6f && systems.All(ps => ps == null || !ps.IsAlive(false))) || age > 6f) Destroy(gameObject);
            }
        }

        public static GameObject ResolveVfx(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return null;
            SplitScale(spec, out spec);
            if (CustomVfx.Has(spec)) return CustomVfx.Template(spec);
            if (vfxCache.TryGetValue(spec, out GameObject cached) && cached != null) return cached;
            GameObject found = null;
            try
            {
                // "ABILITY.field" is an ability particle; a bare name is an effect id, then an ability id;
                // "CREATURE/ABILITY.field" is a particle of an enemy's ability, read from the game's own prefab.
                int slash = spec.IndexOf('/');
                if (slash > 0)
                {
                    found = CreatureParticle(spec.Substring(0, slash), spec.Substring(slash + 1));
                    if (found != null) FromCreature.Add(found);
                }
                else
                {
                    string[] parts = spec.Split('.');
                    if (parts.Length == 1 && EffectManager.instance != null)
                    {
                        Effect e = EffectManager.instance.GetEffect(parts[0], false);
                        if (e != null && e.spawnParticle != null) found = e.spawnParticle.gameObject;
                    }
                    if (found == null)
                    {
                        Ability src = FindVanillaAbility(parts[0]);
                        if (src != null) found = ParticleField(src, parts.Length > 1 ? parts[1] : null);
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"vfx '{spec}': {e.Message}"); }
            if (found == null) Plugin.Log.LogWarning($"vfx '{spec}' not found");
            vfxCache[spec] = found;
            return found;
        }

        private static Ability FindVanillaAbility(string internalName)
        {
            if (Game.instance == null) return null;
            foreach (Skill_Tree t in Game.instance.skills)
            {
                if (t == null) continue;
                foreach (Skill_Tier tier in t.skillTiers)
                {
                    if (tier == null) continue;
                    if (tier.ability != null && tier.ability.internalName == internalName) return tier.ability;
                    if (tier.passive != null && tier.passive.internalName == internalName) return tier.passive;
                }
            }
            return null;
        }

        /// <summary>Templates that came out of an enemy prefab: their systems often wait for the boss's script to start them.</summary>
        private static readonly HashSet<GameObject> FromCreature = new HashSet<GameObject>();

        /// <summary>"Enemy_BOSS_Cerberus" + "Cerberus_Warp.impactParticle": that enemy's ability (by internal name or
        /// class name, the first one when several share a class) and its particle field. Nothing is copied: the game's
        /// own prefab is the template, the same way vanilla player abilities are borrowed.</summary>
        private static GameObject CreatureParticle(string creature, string rest)
        {
            if (AI_Manager.instance == null) return null;
            Character c = AI_Manager.instance.GetCharacter(creature);
            if (c == null) return null;
            string[] parts = rest.Split('.');
            var abilities = c.GetComponentsInChildren<Ability>(true);
            Ability src = abilities.FirstOrDefault(a => a != null && a.internalName == parts[0])
                ?? abilities.FirstOrDefault(a => a != null && a.GetType().Name == parts[0]);
            return src == null ? null : ParticleField(src, parts.Length > 1 ? parts[1] : null);
        }

        /// <summary>Starts every system in a copy of an enemy-prefab particle (they are often parked inactive or stopped).</summary>
        internal static void Wake(GameObject template, GameObject go)
        {
            if (!FromCreature.Contains(template)) return;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true)) { ps.Clear(true); ps.Play(true); }
        }

        internal static GameObject ParticleField(Ability src, string field)
        {
            foreach (var f in src.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            {
                if (field != null ? f.Name != field : !f.Name.ToLowerInvariant().Contains("particle")) continue;
                object v = f.GetValue(src);
                if (v is GameObject g && g != null) return g;
                if (v is ParticleControl pc && pc != null) return pc.gameObject;
                if (v is Component c && c != null && !(v is PSMeshRendererUpdater)) return c.gameObject;
            }
            return null;
        }

        /// <summary>For the reference dump: every borrowable particle.</summary>
        internal static IEnumerable<string> ListVfx()
        {
            if (Game.instance == null) yield break;
            var seen = new HashSet<string>();
            foreach (Skill_Tree t in Game.instance.skills)
            {
                if (t == null) continue;
                foreach (Skill_Tier tier in t.skillTiers)
                {
                    Ability a = tier == null ? null : (tier.ability != null ? tier.ability : tier.passive);
                    if (a == null) continue;
                    foreach (var f in a.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        object v = f.GetValue(a);
                        bool ok = (v is GameObject g && g != null) || (v is ParticleControl pc && pc != null);
                        if (ok && seen.Add(a.internalName + "." + f.Name)) yield return a.internalName + "." + f.Name;
                    }
                }
            }
        }
    }
}
