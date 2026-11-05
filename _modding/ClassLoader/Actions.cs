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
        /// <summary>A copy of you is casting (Mirrors): how much of a skill's control and effects reach the target.</summary>
        public float share = 1f;
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
        /// <summary>A nova that moves you: you go the way you aimed, once, not to everyone it reaches (null aimed = the aim was a tile).</summary>
        public bool spreading;
        public Character aimed;
        /// <summary>Set by area abilities: stacks consumed from yourself are removed once, after every target is hit
        /// (effect id -> how many; 0 = all).</summary>
        public Dictionary<string, int> deferredSelfConsume;
        /// <summary>The casting ability's impact particle, for hits it spreads (chain hops).</summary>
        public string hitVfx;
        /// <summary>Moves that play out over time (a leaping chain's hops). The ability waits for them before its own
        /// self actions; null = run everything at once (procs, effects).</summary>
        public List<IEnumerator> paced;
        /// <summary>Tiles the last "rush" in this cast covered ("value:rushed").</summary>
        public int rushed;
        /// <summary>Tiles "clearHazards" has cleared so far in this cast ("value:cleared").</summary>
        public int cleared;
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
            { "damage", "stun", "knockback", "blind", "throw", "pull", "silence", "exhaust", "execute", "swap", "tame", "hazard", "forget", "attempt" };

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
                return VanillaDebuff(id);
            }
            return false;
        }

        /// <summary>A vanilla effect the game marks as a debuff, plus the control effects it leaves unmarked.</summary>
        private static bool VanillaDebuff(string id)
        {
            if (id == null) return false;
            if (id == "CHARM" || id == "CONFUSE" || id == "SLOW" || id.StartsWith("EFFECT_Root")) return true;
            var all = EffectManager.instance?.allEffects;
            if (all == null) return false;
            foreach (var e in all) if (e.effect != null && e.effect.internalName == id) return e.effect.isDebuff;
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
            if (!string.IsNullOrEmpty(a.ifSkill) && Synergies.TreeLevel(a.ifSkill, ctx.classId) < Math.Max(1, a.skillRank)) return;
            if (!string.IsNullOrEmpty(a.unlessSkill) && Synergies.TreeLevel(a.unlessSkill, ctx.classId) > 0) return;
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
            if (ctx.share < 1f && (a = Mirrors.Weaken(a, act, ctx)) == null) return;
            bool spendsCounter = a.consumeStacks && a.stacksUsed == 0 && !string.IsNullOrEmpty(a.stacksOf) && a.stacksOf.StartsWith("counter:", StringComparison.OrdinalIgnoreCase);
            int counterRead = spendsCounter ? Counters.Get(StackHolder(a, ctx, t), a.stacksOf.Substring(8)) : 0;

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
                case "rush": Moves.Rush(a, ctx, t); break;
                case "forget": Forget(ctx, t); break;
                case "warp": Moves.Warp(a, ctx, t); break;
                case "show": Moves.Show(a, ctx, t); break;
                case "tether": Moves.Tether(a, ctx, t); break;
                case "recall": Moves.Recall(a, ctx); break;
                case "ricochet": Moves.Ricochet(a, ctx); break;
                case "taunt": Afflictions.Taunt(a, ctx, t); break;
                case "silence": Afflictions.Silence(a, ctx, t); break;
                case "exhaust": Afflictions.Exhaust(ctx, t, Math.Max(1, a.amount + StackBonus(a, ctx, t)) + (a.afterStun ? Afflictions.StunLeft(t) + 1 : 0)); break;
                case "swap": Afflictions.Swap(ctx, t); break;
                case "arm": Reactions.Arm(a, ctx); break;
                case "rearm": Reactions.Rearm(a, ctx); break;
                case "spring": Reactions.Spring(ctx); break;
                case "hazard": GroundFields.LayHazard(a, ctx, t); break;
                case "clearhazards": HazardRules.Clear(a, ctx, t); break;
                case "field": GroundFields.Lay(a, ctx, t); break;
                case "trap": Traps.Lay(a, ctx, t); break;
                case "springtraps": Traps.Spring(a, ctx); break;
                case "movetraps": Traps.Move(a, ctx, t); break;
                case "attempt": Attempt(a, ctx, t); break;
                case "ready": CastRules.Ready(); break;
                case "wallspikes": Walls.Spikes(a, ctx, t); break;
                case "wildcast": WildMagic.Cast(a, ctx, t); break;
                case "surge": WildMagic.Surge(a, ctx, t); break;
                case "drifter": Drifters.Launch(a, ctx, t); break;
                case "horror": Horrors.Launch(a, ctx, t); break;
                case "vfx": if (t != null && !string.IsNullOrEmpty(a.vfx)) SpawnVfx(a.vfx, t.GetHitFx().position); break;
                case "raisewall": Walls.Raise(a, ctx, t); break;
                case "swapbody": Conjoined.SwapToNearest(ctx.sourceName, ctx.source as ModAbility); break;
                case "carrytobody": Conjoined.Carry(a, ctx.classId); break;
                case "revivebody": Conjoined.ReviveOne(ctx.sourceName, a.amount > 0 ? a.amount : 50); break;
                case "damage": Damage(a, ctx, t); break;
                case "heal":
                    {
                        int amt = Scaled(a, ctx, t);
                        if (amt <= 0) break;
                        t.Heal(amt, false);
                        Game.LOG($"{ctx.sourceName}: {t.GetName()} {Ability.T("General/heals")} {amt}.");
                        if (HUD.instance != null) HUD.instance.MakeFct("<color=#5CFF6B>+" + amt + "</color>", t.GetHitFx().position, false);
                        break;
                    }
                case "stamina": t.ModStamina(a.amount < 0 ? -DrainAmount(a, ctx) - StackBonus(a, ctx, t) : Scaled(a, ctx, t)); break;
                case "shield":
                    {
                        // Overshield never decays on its own, so cap stacking at half max health.
                        int room = Math.Max(0, t.GetHealthMax() / 2 - t.oshielding);
                        int add = Math.Min(room, Scaled(a, ctx, t));
                        if (add > 0) t.AddOshields(add);
                        break;
                    }
                case "recharge":
                    {
                        // fills your regular shield pool (ShieldMax), which needs no shield in hand; the cap also holds when one is.
                        // Always yours, so a hit can refill you by what is on its target (stacksFrom "target")
                        Player me = Player.instance;
                        if (ctx.caster != me || me.shielding >= me.shieldingMax) break;
                        int charge = Math.Min(me.shieldingMax - me.shielding, Scaled(a, ctx, t));
                        if (charge > 0) Menu_Player.instance.ModShield(charge);
                        break;
                    }
                case "effect": ApplyEffect(a, ctx, t); break;
                case "stun": Ability.ApplyStun(t, Turns(a) + ExtraTurns(a, ctx, t)); break;
                case "knockback": Ability.Knockback(t, ctx.caster, Math.Max(1, a.amount)); break;
                case "blind":
                    {
                        // A summon's skill or effect has no player ability behind it; the game's blind only needs one for its stats.
                        Ability src = ctx.source ?? (Player_AbilityControl.instance != null ? Player_AbilityControl.instance.GetAbilities().FirstOrDefault(x => x != null) : null);
                        if (src != null && t != Player.instance && !TileStacks.IsAlly(t)) src.ApplyBlind(t);
                        break;
                    }
                case "cooldowns":
                    if (Player_AbilityControl.instance != null)
                        foreach (Ability ab in Player_AbilityControl.instance.GetAbilities())
                            if (ab != ctx.source && (string.IsNullOrEmpty(a.skill) || string.Equals(ab.GetDisplayName(), a.skill, StringComparison.OrdinalIgnoreCase)))
                                ab.ReduceCooldown(Math.Max(1, a.amount));
                    if (Menu_HUD.instance != null) Menu_HUD.instance.Refresh_Cooldown();
                    break;
                case "purge": t.effects.RemoveEffect(EffectId(a.effect, ctx.classId), true); t.CalculateStats(); break;
                case "teleport": break; // handled by ModAbility for floor targets
                case "dispel": Dispel(a, ctx, t, debuffs: false); break;
                case "cleanse": Dispel(a, ctx, t, debuffs: true); break;
                case "transfer": Transfer(a, ctx, t); break;
                case "chain": if (!string.IsNullOrEmpty(a.sound) && t != null) ModAudio.Play(a.sound, t.GetHitFx().position, ctx.classId); Chain(a, ctx, t); break;
                case "echo": Echo(a, ctx, t); break;
                case "volley": Volley(a, ctx, t); break;
                case "cast": CastSkill(a, ctx, t); break;
                case "shorten": Shorten(a, ctx, t); break;
                case "xp": Xp(a, ctx); break;
                case "ammo": Ammo(a, ctx, t); break;
                case "anchor": Moves.SetAnchor(a, ctx); break;
                case "rewind": Moves.Rewind(a, ctx); break;
                case "corpses": Corpses.Use(a, ctx, t); break;
                case "loot": Loot(a, ctx, t); break;
                case "unlock": Unlock(a, ctx); break;
                case "disarm": Disarm(a, ctx); break;
                case "steal": Steal(a, ctx); break;
                case "order": Order(a, ctx, t); break;
                case "summon": Summons.Summon(a, ctx, t, ScaledCount(a, a.amount, ctx, t)); break;
                case "tame": Taming.Tame(a, ctx, t, Amount(a, ctx)); break;
                case "giveitem": GiveItem(a, ctx); break;
                case "consumeitem": ConsumeItem(a, ctx); break;
                case "salvage": Salvage(a, ctx); break;
                case "tune": Tune(a, ctx); break;
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
            if (a.consumeStacks && !string.IsNullOrEmpty(a.stacksOf) && !a.stacksOf.StartsWith("value:", StringComparison.OrdinalIgnoreCase))
            {
                Character holder = StackHolder(a, ctx, t);
                string sid = EffectId(a.stacksOf, ctx.classId);
                int n = Math.Max(0, a.stacksUsed);
                if (holder == ctx.caster && ctx.deferredSelfConsume != null)
                {
                    // a counter spends what the cast read, so what a kill adds on the way stays
                    if (spendsCounter && (n = counterRead) == 0) return;
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
            return a.bonusPerStack * UsedStacks(a, CountOf(a, ctx, t));
        }

        /// <summary>
        /// What an action's stacksOf counts: an effect's stacks, a counter, summons, bodies, or "value:NAME" — any
        /// condition value (health%, healthLost%, shield, credits, floor, enemiesNear:3, distance, Strength...) read from
        /// the stack holder, optionally divided: "value:credits/50" counts one per 50 credits.
        /// </summary>
        private static int CountOf(ActionSpec a, ActionContext ctx, Character t)
        {
            Character holder = StackHolder(a, ctx, t);
            if (!a.stacksOf.StartsWith("value:", StringComparison.OrdinalIgnoreCase)) return StacksOn(holder, EffectId(a.stacksOf, ctx.classId));
            string name = a.stacksOf.Substring(6);
            float div = 1f;
            int slash = name.LastIndexOf('/');
            if (slash > 0 && float.TryParse(name.Substring(slash + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d) && d > 0f)
            {
                div = d;
                name = name.Substring(0, slash);
            }
            float? v = When.Value(name, ctx, holder, t);
            return v.HasValue ? Math.Max(0, Mathf.FloorToInt(v.Value / div)) : 0;
        }

        /// <summary>A described action list as one run of text: "Deals 9 Crushing to it; Heals you for 5".</summary>
        private static string Joined(string described) =>
            string.Join("; ", described.Split('\n').Select(l => l.Replace("• ", "").Trim().TrimEnd('.')).Where(l => l.Length > 0));

        /// <summary>Rules text for a "value:" count: "point of Strength on you", "50 of your credits".</summary>
        private static string ValuePer(string name, string classId, string on)
        {
            int slash = name.LastIndexOf('/');
            string div = slash > 0 ? name.Substring(slash + 1) : null;
            string bare = slash > 0 ? name.Substring(0, slash) : name;
            string[] parts = bare.Split(':');
            string head = parts[0].ToLowerInvariant(), r = parts.Length > 1 ? parts[1] : "1.5";
            // counts read as the thing counted: "+3 per enemy within 4 of you"
            string one = head == "enemiesnear" ? $"enemy within {r} of you" : head == "alliesnear" ? $"ally within {r} of you"
                : head == "corpses" ? $"corpse within {(parts.Length > 1 ? r : "3")} of you"
                : head == "wallsnear" ? "wall beside you" : head == "summons" ? "summon you have" : head == "rushed" ? "tile you rushed" : head == "cleared" ? "tile cleared" : head == "rounds" ? "round the shot fired"
                : head == "buffs" ? $"buff {on}" : head == "debuffs" ? $"debuff {on}" : null;
            if (one != null) return div != null ? $"{div} (counting each {one})" : one;
            string what = When.ValueText(bare, classId) + (When.IsGlobalValue(bare) ? "" : " " + on);
            if (bare.EndsWith("%")) return $"{div ?? "1"}% of {what}";
            return div != null ? $"{div} {what}" : $"point of {what}";
        }

        /// <summary>The stacks an action counts: all it finds, or at most stacksUsed.</summary>
        private static int UsedStacks(ActionSpec a, int have) => a.stacksUsed > 0 ? Math.Min(have, a.stacksUsed) : have;

        internal static int StackBonusOf(ActionSpec a, ActionContext ctx, Character t) => StackBonus(a, ctx, t);

        /// <summary>Turns an action's turnsPerStack adds, from the stacks its stacksOf counts (capped by stacksUsed).</summary>
        internal static int ExtraTurns(ActionSpec a, ActionContext ctx, Character t)
        {
            if (a.turnsPerStack == 0 || string.IsNullOrEmpty(a.stacksOf)) return 0;
            return a.turnsPerStack * UsedStacks(a, CountOf(a, ctx, t));
        }

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

        /// <summary>
        /// "transfer": moves up to "amount" (0 = all) of your debuffs onto the target, with the turns they had left and their
        /// stacks; "effect" moves only that one. A debuff the target can't take (immune, resisted), or one whose effect
        /// says "transferable": false, stays on you. Moved
        /// effects count as yours from then on, so their damage and triggers are your doing.
        /// </summary>
        private static void Transfer(ActionSpec a, ActionContext ctx, Character t)
        {
            Character from = ctx.caster;
            if (from == null || t == null || t == from || t.GetIsDead() || EffectManager.instance == null) return;
            string only = string.IsNullOrEmpty(a.effect) ? null : EffectId(a.effect, ctx.classId);
            int limit = a.amount > 0 ? a.amount : 99;
            var mine = from.effects.GetEffects()
                // the game leaves its control effects (slow, confuse, charm, roots) unmarked; they are afflictions too
                .Where(e => e != null && !e.isHidden && (e.isDebuff || VanillaDebuff(e.internalName)) && !(e.duration < 0 && !(e is ModEffect)) && (only == null || e.internalName == only)
                    && !(e is ModEffect fx && fx.Spec != null && !fx.Spec.transferable)) // "transferable": false stays put
                .ToList();
            int moved = 0;
            foreach (Effect e in mine)
            {
                if (moved >= limit) break; // count what actually moved: a resisted one doesn't use up the amount
                string id = e.internalName;
                if (!EffectManager.instance.CanUseOn(id, t)) continue;
                int stacks = e is ModEffect me && me.MaxStacks > 1 ? me.Stacks : 1;
                ApplyEffect(new ActionSpec { @do = "effect", effect = id, stacks = stacks, turns = e.duration > 0 ? e.duration : 0 }, ctx, t);
                if (t.effects.GetEffect(id) == null) continue; // shrugged off
                e.RemoveInstant();
                moved++;
            }
            if (moved == 0) return;
            from.CalculateStats();
            if (from == Player.instance && Menu_HUD.instance != null) Menu_HUD.instance.UpdatePlayerEffects();
            Game.LOG($"{ctx.sourceName} passes {moved} affliction{(moved > 1 ? "s" : "")} from {from.GetName()} to {t.GetName()}.");
        }

        /// <summary>chain: the effects a link has to carry ("effect": "#Chilled|EFFECT_Chill" = any of them); null = any enemy.</summary>
        private static HashSet<string> ChainNeeds(ActionSpec a, ActionContext ctx) =>
            string.IsNullOrEmpty(a.effect) ? null : new HashSet<string>(a.effect.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).Select(x => EffectId(x, ctx.classId)));

        /// <summary>Runs a.actions on the target, then hops to nearby enemies (optionally only ones with a.effect).</summary>
        private static void Chain(ActionSpec a, ActionContext ctx, Character first)
        {
            // a charge inside an ability plays hop by hop; the ability waits for it
            if (a.leap && ctx.paced != null) { ctx.paced.Add(PacedChain(a, ctx, first)); return; }
            var visited = new HashSet<Character>();
            Character cur = first;
            float mult = ctx.damageMult;
            HashSet<string> need = ChainNeeds(a, ctx);
            Vector3 last = Vector3.zero;
            for (int hop = 0; cur != null && hop <= Math.Max(0, a.jumps); hop++)
            {
                visited.Add(cur);
                var c = Clone(ctx); c.damageMult = mult; c.target = cur;
                if (hop > 0) SpawnVfx(a.vfx ?? ctx.hitVfx ?? "PA_Elementalist_ChainLightning.shockImpact", cur.GetHitFx().position);
                // "look": a line of that colour along each jump
                if (hop > 0 && !string.IsNullOrEmpty(a.look)) Moves.Streak(last, cur.GetHitFx().position, Moves.Hex(a.look, Color.white), 0.025f, 0.5f);
                last = cur.GetHitFx().position;
                if (a.leap && cur.GetHealth() > 0) Triggers.Styled("charge", cur, () => Moves.Leap(a, ctx, cur));
                // a chain set off by a death starts at the one dying: it only passes things on from there; so does one
                // with skipFirst, which spreads something from its target without landing on it again
                if (cur.GetHealth() > 0 && !(a.skipFirst && hop == 0)) Run(a.actions, c, cur);
                Triggers.ChainHop(ctx.caster, cur, hop);
                mult *= a.falloff;
                if (AI_Manager.instance == null || cur.GetMover() == null || cur.GetMover().GetCurrentNavNode() == null) break;
                Vector3 p = cur.GetMover().GetCurrentNavNode().transform.position;
                AI eyes = a.sight ? cur.ai : null;
                cur = AI_Manager.instance.GetEnemiesAt(p, a.radius > 0 ? a.radius : 3f)
                    .Where(e => !visited.Contains(e) && !e.GetIsDead() && (need == null || need.Any(n => e.effects.GetEffect(n) != null)) && (eyes == null || eyes.CanSee(e)))
                    .OrderBy(e => Vector3.Distance(e.transform.position, p)).FirstOrDefault();
            }
        }

        /// <summary>A leaping chain one hop at a time: dash, strike, a beat, on to the next.</summary>
        private static IEnumerator PacedChain(ActionSpec a, ActionContext ctx, Character first)
        {
            var visited = new HashSet<Character>();
            Character cur = first;
            float mult = ctx.damageMult;
            HashSet<string> need = ChainNeeds(a, ctx);
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
                    if (!(a.skipFirst && hop == 0)) Run(a.actions, c, cur);
                    Triggers.ChainHop(ctx.caster, cur, hop);
                    yield return new WaitForSeconds(0.22f); // a beat before the next
                }
                mult *= a.falloff;
                if (AI_Manager.instance == null || cur.GetMover() == null || cur.GetMover().GetCurrentNavNode() == null) break;
                Vector3 p = cur.GetMover().GetCurrentNavNode().transform.position;
                cur = AI_Manager.instance.GetEnemiesAt(p, a.radius > 0 ? a.radius : 3f)
                    .Where(e => !visited.Contains(e) && !e.GetIsDead() && (need == null || need.Any(n => e.effects.GetEffect(n) != null)))
                    .OrderBy(e => Vector3.Distance(e.transform.position, p)).FirstOrDefault();
            }
        }

        internal static int VolleyRetargets;

        /// <summary>
        /// "volley": "amount" shots of "actions" at the target, one after another; once it falls, the rest turn on the
        /// nearest enemy within "radius" (3 unset) of where it stood, a salvo of homing missiles finding a new mark. Each
        /// shot draws a "look" streak from you and plays "vfx" and "sound" where it lands. Inside a skill the shots
        /// come a beat apart and the skill waits for them.
        /// </summary>
        private static void Volley(ActionSpec a, ActionContext ctx, Character first)
        {
            // counted now, as the skill goes off: the shots come later, after the skill's other actions (a spent load)
            int n = Math.Max(1, a.amount + StackBonus(a, ctx, first));
            if (ctx.paced != null) { ctx.paced.Add(Shots(a, ctx, first, true, n)); return; }
            IEnumerator run = Shots(a, ctx, first, false, n);
            while (run.MoveNext()) { }
        }

        private static IEnumerator Shots(ActionSpec a, ActionContext ctx, Character cur, bool paced, int n)
        {
            Vector3 at = cur != null ? cur.transform.position : ctx.caster.transform.position;
            for (int shot = 0; shot < n; shot++)
            {
                if (cur == null || cur.GetIsDead() || cur.GetHealth() <= 0)
                {
                    if (AI_Manager.instance == null) yield break;
                    Character dead = cur;
                    cur = AI_Manager.instance.GetEnemiesAt(at, a.radius > 0f ? a.radius : 3f)
                        .Where(e => e != null && e != dead && e != ctx.caster && !e.GetIsDead() && e.GetHealth() > 0)
                        .OrderBy(e => Vector3.Distance(e.transform.position, at)).FirstOrDefault();
                    if (cur == null) yield break;
                    VolleyRetargets++;
                }
                at = cur.transform.position;
                Vector3 hit = cur.GetHitFx().position;
                if (!string.IsNullOrEmpty(a.look) && ctx.caster != null) Moves.Streak(ctx.caster.GetHitFx().position, hit, Moves.Hex(a.look, Color.white), 0.025f, 0.35f);
                if (!string.IsNullOrEmpty(a.vfx)) SpawnVfx(a.vfx, hit);
                if (!string.IsNullOrEmpty(a.sound)) ModAudio.Play(a.sound, hit, ctx.classId);
                var c = Clone(ctx); c.target = cur; c.paced = null;
                Run(a.actions, c, cur);
                if (paced && shot < n - 1) yield return new WaitForSeconds(0.15f);
            }
        }

        /// <summary>
        /// "attempt": a hold on the target that rolls its odds once a beat for up to "amount" tries (default 6), each try
        /// "oddsPerTry" likelier and costing "tryCost" stamina, until one lands. Odds = "odds" + your "scale" stats ×
        /// scaleMult − the target's "resistStat" × resistMult. A landed try runs "actions" on the target, none landing
        /// runs "else". Inside a skill the tries come a second apart under a cast bar; from a trigger they roll at once.
        /// With "together", a skill that hits several targets holds them all in one hold: each beat costs tryCost once
        /// and rolls every target still held, and a target that's caught drops out while the rest go on.
        /// </summary>
        private static void Attempt(ActionSpec a, ActionContext ctx, Character t)
        {
            if (t == null || t.GetIsDead() || ctx.caster == null) return;
            if (ctx.paced != null)
            {
                if (a.together)
                {
                    var key = (ctx.paced, a);
                    if (Holds.TryGetValue(key, out var held)) { if (!held.Contains(t)) held.Add(t); return; }
                    held = new List<Character> { t };
                    Holds[key] = held;
                    ctx.paced.Add(Tries(a, ctx, held, true, key));
                    return;
                }
                ctx.paced.Add(Tries(a, ctx, new List<Character> { t }, true, null));
                return;
            }
            IEnumerator run = Tries(a, ctx, new List<Character> { t }, false, null);
            while (run.MoveNext()) { }
        }

        /// <summary>Holds of a "together" attempt being gathered while a skill resolves, by its paced list and action.</summary>
        private static readonly Dictionary<(List<IEnumerator>, ActionSpec), List<Character>> Holds = new Dictionary<(List<IEnumerator>, ActionSpec), List<Character>>();

        /// <summary>Tries rolled, holds that landed, and the most targets one hold had (tests).</summary>
        internal static int Attempts, AttemptsWon, AttemptsHeld;

        internal static float AttemptOdds(ActionSpec a, Character me, Character t, int i) =>
            a.odds + StatBonus(a.scale, a.scaleMult, me) - (string.IsNullOrEmpty(a.resistStat) ? 0f : StatBonus(a.resistStat, a.resistMult, t)) + a.oddsPerTry * i;

        private static IEnumerator Tries(ActionSpec a, ActionContext ctx, List<Character> held, bool paced, (List<IEnumerator>, ActionSpec)? key)
        {
            // the skill has finished gathering targets by the time paced moves run
            if (key.HasValue) Holds.Remove(key.Value);
            Character me = ctx.caster;
            var left = held.Where(x => x != null && !x.GetIsDead()).ToList();
            if (left.Count == 0) yield break;
            AttemptsHeld = Math.Max(AttemptsHeld, left.Count);
            int tries = a.amount > 0 ? a.amount : 6;
            bool bar = paced && me == Player.instance && Menu_HUD.instance != null;
            if (bar)
            {
                Menu_HUD.instance.SetCastBarTitle(ctx.sourceName ?? "", Moves.Hex(a.look, Color.cyan));
                Menu_HUD.instance.StartCoroutine(Menu_HUD.instance.ShowCastBar(tries));
            }
            if (!string.IsNullOrEmpty(a.sound)) ModAudio.Play(a.sound, left[0].GetHitFx().position, ctx.classId);
            for (int i = 0; i < tries && left.Count > 0; i++)
            {
                left.RemoveAll(x => x == null || x.GetIsDead());
                if (left.Count == 0 || me == null || me.GetIsDead()) break;
                if (a.tryCost > 0)
                {
                    if (me.GetStamina() < a.tryCost) break;
                    me.ModStamina(-a.tryCost);
                }
                foreach (Character t in left.ToList())
                {
                    Attempts++;
                    if (!string.IsNullOrEmpty(a.look)) Moves.Streak(me.GetHitFx().position, t.GetHeadFx().position, Moves.Hex(a.look, Color.white), 0.02f, paced ? 0.9f : 0.3f);
                    if (Random.Range(0, 100) >= Mathf.Clamp(Mathf.RoundToInt(AttemptOdds(a, me, t, i)), 0, 100)) continue;
                    left.Remove(t);
                    AttemptsWon++;
                    if (!string.IsNullOrEmpty(a.vfx)) SpawnVfx(a.vfx, t.GetHitFx().position);
                    Game.LOG($"{ctx.sourceName} takes hold of {t.GetName()}.");
                    var c = Clone(ctx); c.target = t; c.paced = null;
                    Run(a.actions, c, t);
                }
                if (left.Count > 0 && paced && i < tries - 1) yield return new WaitForSeconds(1f);
            }
            if (bar) Menu_HUD.instance.CancelCastBar();
            foreach (Character t in left)
            {
                if (t == null) continue;
                Game.LOG($"{t.GetName()} throws off {ctx.sourceName}.");
                var c = Clone(ctx); c.target = t; c.paced = null;
                Run(a.@else, c, t);
            }
        }

        private static string Plural(string name) => name.EndsWith("s") ? name : name + "s";

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

        /// <summary>"shorten": takes "amount" turns off the target's effects ("effect": an id, "debuffs" (default) or "buffs");
        /// one run down to nothing ends. A negative amount draws them out instead. Effects with no end are left alone.</summary>
        private static void Shorten(ActionSpec a, ActionContext ctx, Character t)
        {
            if (t?.effects == null) return;
            string which = string.IsNullOrEmpty(a.effect) ? "debuffs" : a.effect;
            bool debuffs = which.Equals("debuffs", StringComparison.OrdinalIgnoreCase), buffs = which.Equals("buffs", StringComparison.OrdinalIgnoreCase);
            string id = debuffs || buffs ? null : EffectId(which, ctx.classId);
            int by = a.amount == 0 ? 1 : a.amount;
            bool changed = false;
            foreach (Effect e in t.effects.GetEffects().ToList())
            {
                if (e == null || e.duration <= 0 || e.durationIsHidden) continue;
                bool bad = e.isDebuff || VanillaDebuff(e.internalName);
                if (id != null ? e.internalName != id : debuffs != bad) continue;
                int left = e.duration - by;
                if (left <= 0) t.effects.RemoveEffect(e.internalName, true);
                else e.duration = left;
                changed = true;
            }
            if (!changed) return;
            t.CalculateStats();
            if (t == Player.instance && Menu_HUD.instance != null) Menu_HUD.instance.UpdatePlayerEffects();
        }

        /// <summary>"xp": you gain experience the way a won floor event pays it, which grows with the floor; "amount" multiplies it.</summary>
        private static void Xp(ActionSpec a, ActionContext ctx)
        {
            Player p = Player.instance;
            if (p == null || Menu_Skills.instance == null || (ctx.caster != p && !TileStacks.IsAlly(ctx.caster))) return;
            int lv = Math.Min(5 + Game.FLOOR * 5, Menu_Skills.instance.xpLevels.Length - 1);
            int xp = Math.Max(1, (int)(Menu_Skills.instance.xpLevels[lv] * 0.03)) * Math.Max(1, a.amount);
            p.AddXp(xp, lv, "mod_skill");
        }

        /// <summary>"ammo": puts rounds back in your loaded ammo (a refund, up to a full stack), or takes them out with a
        /// negative amount; the last round taken empties the slot as firing it would, and rounds put into an empty gun load as plain ammo.</summary>
        private static void Ammo(ActionSpec a, ActionContext ctx, Character t)
        {
            if (!(ctx.caster is Player p)) return;
            Item_Ammo ammo = p.GetAmmo();
            int n = a.amount + StackBonus(a, ctx, t);
            if (ammo == null)
            {
                // an empty gun takes plain rounds: what comes back after the load ran out
                if (n <= 0 || Menu_Player.instance == null || !(ItemDatabase.instance?.GetItem("WEAPON_AMMO_Generic") is Item_Ammo fresh)) return;
                fresh.stack = n;
                Menu_Player.instance.AutoEquip(fresh, null, null);
                if (!Menu_Player.instance.IsEquipped(fresh)) Menu_Inventory.instance?.AddToInventory(fresh, doStack: true, isSilent: true, null);
                Menu_Player.instance.RefreshAmmo();
                return;
            }
            int now = Mathf.Min(ammo.stack + n, ammo.GetStackMax());
            if (now <= 0 && Menu_Player.instance != null) Menu_Player.instance.DestroyAmmo(ammo);
            else ammo.SetStack(Mathf.Max(1, now));
            if (Menu_Player.instance != null) Menu_Player.instance.RefreshAmmo();
        }

        /// <summary>"loot": the game rolls "amount" items of "lootType" (and "rarity", or the floor's odds), and they spill
        /// from the target onto the floor the way a broken crate's do.</summary>
        private static void Loot(ActionSpec a, ActionContext ctx, Character t)
        {
            Character from = t ?? ctx.caster;
            if (LootManager.instance == null || from == null) return;
            if (!Enum.TryParse(a.lootType ?? "Random", true, out LOOT_TYPE type)) type = LOOT_TYPE.Random;
            if (!Enum.TryParse(a.rarity ?? "None", true, out RARITY rarity)) rarity = RARITY.None;
            Vector3 at = from.transform.position;
            for (int i = 0, n = Math.Max(1, a.amount) + StackBonus(a, ctx, t); i < n; i++)
            {
                Item item = LootManager.instance.Generate_Loot(type, rarity);
                if (item == null) continue;
                item.transform.position = at + Vector3.up * 0.8f;
                item.OnEnterWorld();
                LootManager.instance.DoLootSpew(at, item, false, 0.6f);
                LootDropped++;
            }
        }

        /// <summary>Items the "loot" action has dropped (tests).</summary>
        internal static int LootDropped;

        /// <summary>"unlock": every locked door and container within "radius" of you (default 2.5) comes open, by the base
        /// game's hacking rules: sealed, broken and boss doors stay shut, and an opened container spills its loot. "odds"
        /// rolls once per lock.</summary>
        private static void Unlock(ActionSpec a, ActionContext ctx)
        {
            if (!(ctx.caster is Player p)) return;
            int opened = 0;
            foreach (Door d in Around<Door>(p, a.radius))
            {
                if (!d.GetIsLocked() || d.FLAG_isUberDoor || d.isBroken || d.isSealed || !Rolls(a.odds)) continue;
                d.Lockpicked(p);
                opened++;
            }
            foreach (Container c in Around<Container>(p, a.radius))
            {
                if (!c.isLocked || !Rolls(a.odds)) continue;
                c.Unlock();
                opened++;
            }
            if (opened > 0) Game.LOG($"{ctx.sourceName} opens {opened} {(opened == 1 ? "lock" : "locks")}.");
        }

        /// <summary>"disarm": every armed trap within "radius" of you (default 2.5) is made safe, seen or not, and gives its
        /// parts as a disarm by hand does (so the Gadgeteer's and Security's disarm skills go off too). Hazards laid by
        /// skills can't be disarmed and stay.</summary>
        private static void Disarm(ActionSpec a, ActionContext ctx)
        {
            if (!(ctx.caster is Player p)) return;
            int made = 0;
            // the map's own list: a trap in a room the game has culled from view is inactive, and FindObjectsOfType misses it
            float reach = (a.radius > 0f ? a.radius : 2.5f) + 0.25f;
            foreach (Trap_Trigger h in (Map.instance?.GetTraps() ?? new List<Trap_Trigger>()).ToList())
            {
                if (h == null) continue;
                Vector3 off = h.transform.position - p.transform.position;
                off.y = 0f;
                if (off.magnitude > reach) continue;
                if (!h.CanDisarm() || h.GetTripped() || !Rolls(a.odds)) continue;
                h.Disarm(giveParts: true);
                made++;
            }
            if (made > 0) Game.LOG($"{ctx.sourceName} makes {made} {(made == 1 ? "trap" : "traps")} safe.");
        }

        /// <summary>"steal": takes "amount" items (default 1) from each vendor within "radius" of you who has any, by the
        /// Hacker's rules: each theft works at 50 + your Smarts % ("odds" sets it), and the first that fails turns the
        /// vendor on you (or calls the guards) and locks the shop.</summary>
        private static void Steal(ActionSpec a, ActionContext ctx)
        {
            if (!(ctx.caster is Player p)) return;
            foreach (Vendor v in Around<Vendor>(p, a.radius))
            {
                for (int i = 0; i < Math.Max(1, a.amount); i++)
                {
                    if (!v.canBeHacked || v.isLocked || v.vendor_items == null || v.vendor_items.Count == 0) break;
                    if (Random.Range(1, 101) > (a.odds > 0 ? a.odds : 50 + p.GetStat(STAT_TYPE.Smarts))) { v.ThieveryFail(); break; }
                    v.Hacked();
                }
            }
        }

        private static bool Rolls(int odds) => odds <= 0 || Random.Range(1, 101) <= odds;

        /// <summary>Scene objects of a kind within r of you on the floor plan (default 2.5; a little slack for where the
        /// object's pivot sits).</summary>
        private static IEnumerable<T> Around<T>(Character who, float r) where T : Component
        {
            float reach = (r > 0f ? r : 2.5f) + 0.25f;
            Vector3 at = who.transform.position;
            foreach (T o in Object.FindObjectsOfType<T>(true)) // rooms out of view are inactive; still on the floor
            {
                if (o == null || !o.gameObject.scene.IsValid()) continue;
                Vector3 d = o.transform.position - at;
                d.y = 0f;
                if (d.magnitude <= reach) yield return o;
            }
        }

        private static int casting; // a skill that casts itself stops after a few rounds

        /// <summary>"cast": fires another skill for free (no stamina, no cooldown) at the target, around it if the skill has an area.</summary>
        private static void CastSkill(ActionSpec a, ActionContext ctx, Character t)
        {
            if (string.IsNullOrEmpty(a.skill) || casting >= 3 || ctx.caster == null) return;
            ModAbility.Runtime rt = SkillNamed(a.skill, ctx.classId);
            if (rt == null) { Plugin.Log.LogWarning($"{ctx.sourceName}: cast found no skill named '{a.skill}'"); return; }
            AbilitySpec s = rt.spec;
            var acts = s.actions.Where(x => x.@do != "teleport" && x.@do != "phase").ToList();
            var c = Clone(ctx);
            c.classId = rt.classId;
            c.sourceName = s.name;
            Game.LOG($"{ctx.sourceName} fires <color=#FFD15C>{s.name}</color>.");
            casting++;
            try
            {
                bool aimed = s.target == "enemy" || s.target == "point" || s.target == "floor";
                if (!aimed || t == null || t == ctx.caster || t.GetIsDead()) { c.target = ctx.caster; Run(acts, c, ctx.caster); return; }
                c.target = t;
                c.center = t.transform.position;
                float radius = s.radius > 0f ? s.radius : Shapes.Is(s.shape) ? 1.5f : 0f;
                var victims = new List<Character> { t };
                NavNode node = t.GetMover()?.GetCurrentNavNode();
                if (radius > 0f && node != null && AI_Manager.instance != null)
                    victims.AddRange(AI_Manager.instance.GetEnemiesAt(node.transform.position, radius).Where(v => v != t));
                if (!string.IsNullOrEmpty(s.impactVfx)) SpawnVfx(s.impactVfx, t.GetHitFx().position);
                ModAudio.Play(s.sound, t.GetHitFx().position, rt.classId);
                Run(acts.Where(x => (x.to ?? "").ToLowerInvariant() == "self"), c, t);
                HitLane.Each(victims, acts.Where(x => (x.to ?? "").ToLowerInvariant() != "self"), c);
            }
            finally { casting--; }
        }

        /// <summary>A skill by its shown name: the caster's own class first, then any class.</summary>
        private static ModAbility.Runtime SkillNamed(string name, string classId)
        {
            var named = ModAbility.Specs.Values.Where(r => r?.spec != null && r.spec.type != "passive" && string.Equals(r.spec.name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            return named.FirstOrDefault(r => string.Equals(r.classId, classId, StringComparison.OrdinalIgnoreCase)) ?? named.FirstOrDefault();
        }

        /// <summary>"order": each of your summons carries out the nested actions itself, at your target (or at itself when there is none).</summary>
        private static void Order(ActionSpec a, ActionContext ctx, Character t)
        {
            if (a.actions == null || a.actions.Count == 0) return;
            foreach (Character pet in Summons.Matching(string.IsNullOrEmpty(a.pets) ? "any" : a.pets, ctx))
            {
                Character at = t != null && t != ctx.caster && !t.GetIsDead() ? t : pet;
                var c = Clone(ctx);
                c.caster = pet;
                c.target = at;
                c.ally = ctx.caster;
                Run(a.actions, c, at);
            }
        }

        internal static ActionContext Clone(ActionContext x) => new ActionContext
        {
            caster = x.caster, target = x.target, attacker = x.attacker, victim = x.victim, source = x.source,
            classId = x.classId, sourceName = x.sourceName, rank = x.rank, damageMult = x.damageMult,
            chanceBonus = x.chanceBonus, deferredSelfConsume = x.deferredSelfConsume, hitVfx = x.hitVfx, paced = x.paced, share = x.share,
            rushed = x.rushed, cleared = x.cleared,
        };

        private static int Turns(ActionSpec a) => Math.Max(1, a.turns > 0 ? a.turns : a.amount);

        /// <summary>An amount plus what its stacks add. With bonusPerStack and a base of 0 nothing happens without stacks,
        /// so rank and stat scaling only count once some stacks do.</summary>
        private static int Scaled(ActionSpec a, ActionContext ctx, Character t)
        {
            int bonus = StackBonus(a, ctx, t);
            if (a.amount == 0 && Scaled(a) && bonus == 0) return 0;
            return Amount(a, ctx) + bonus;
        }

        internal static int Amount(ActionSpec a, ActionContext ctx)
        {
            float v = a.amount + a.perRank * Math.Max(0, ctx.rank) + ScaleBonus(a, ctx);
            // with bonusPerStack a base of 0 is meant: nothing happens without stacks
            return Math.Max(a.amount == 0 && a.bonusPerStack != 0 ? 0 : 1, Mathf.RoundToInt(v));
        }

        /// <summary>A drain's size: "amount": -15 drains 15, plus its rank and stat scaling.</summary>
        private static int DrainAmount(ActionSpec a, ActionContext ctx) =>
            Math.Max(1, Mathf.RoundToInt(-a.amount + a.perRank * Math.Max(0, ctx.rank) + ScaleBonus(a, ctx)));

        /// <summary>"scale": one stat, or several joined with "+" ("Smarts+PsiPower"), each counting scaleMult per point.
        /// A part written "Smarts|PsiPower" counts only the higher of those stats.</summary>
        private static IEnumerable<List<STAT_TYPE>> ScaleStats(ActionSpec a) => ScaleStats(a.scale);

        internal static IEnumerable<List<STAT_TYPE>> ScaleStats(string scale) => ScaleParts(scale);

        // read on every hit, and a skill's text never changes: each is parsed once
        private static readonly Dictionary<string, List<List<STAT_TYPE>>> scales = new Dictionary<string, List<List<STAT_TYPE>>>();
        private static readonly List<List<STAT_TYPE>> NoScale = new List<List<STAT_TYPE>>();

        private static List<List<STAT_TYPE>> ScaleParts(string scale)
        {
            if (string.IsNullOrEmpty(scale)) return NoScale;
            if (scales.TryGetValue(scale, out var parts)) return parts;
            parts = new List<List<STAT_TYPE>>();
            foreach (string part in scale.Split('+'))
            {
                var any = new List<STAT_TYPE>();
                foreach (string s in part.Split('|'))
                    if (Enum.TryParse(s.Trim(), true, out STAT_TYPE st)) any.Add(st);
                if (any.Count > 0) parts.Add(any);
            }
            return scales[scale] = parts;
        }

        private static float StatPoints(string scale, float mult, Character who)
        {
            double sum = 0d;
            foreach (List<STAT_TYPE> any in ScaleParts(scale))
            {
                int best = who.GetStat(any[0]);
                for (int i = 1; i < any.Count; i++) best = Math.Max(best, who.GetStat(any[i]));
                sum += best * mult;
            }
            return (float)sum;
        }

        /// <summary>The highest of "Strength|Agility" on this character, or the one stat named.</summary>
        internal static string BestStat(string any, Character who)
        {
            if (who == null || !any.Contains("|")) return any;
            return ScaleParts(any).First().OrderByDescending(who.GetStat).First().ToString();
        }

        /// <summary>The same stat sum for anything else that scales ("PsiPower+Smarts" at `mult` per point).</summary>
        internal static float StatBonus(string scale, float mult, Character who) =>
            who == null ? 0f : StatPoints(scale, mult, who);

        private static float ScaleBonus(ActionSpec a, ActionContext ctx) =>
            ctx.caster == null ? 0f : StatPoints(a.scale, a.scaleMult, ctx.caster);

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

        private static readonly Dictionary<string, DAMAGE_TYPE> damageTypes = new Dictionary<string, DAMAGE_TYPE>();

        private static DAMAGE_TYPE DamageType(string name)
        {
            name = name ?? "Crushing";
            if (damageTypes.TryGetValue(name, out DAMAGE_TYPE type)) return type;
            return damageTypes[name] = Enum.TryParse(name, true, out type) ? type : DAMAGE_TYPE.Crushing;
        }

        private static readonly DAMAGE_TYPE[] Resistible =
        {
            DAMAGE_TYPE.Slashing, DAMAGE_TYPE.Crushing, DAMAGE_TYPE.Piercing, DAMAGE_TYPE.Ballistic, DAMAGE_TYPE.Fire, DAMAGE_TYPE.Cryo,
            DAMAGE_TYPE.Energy, DAMAGE_TYPE.Void, DAMAGE_TYPE.Psi, DAMAGE_TYPE.Bio, DAMAGE_TYPE.Shock,
        };

        /// <summary>"type": "weakest" (or "weakest:Fire|Cryo|Shock" to choose among those): the type the target resists
        /// least, the first listed on a tie. Null for any other type.</summary>
        internal static DAMAGE_TYPE? Weakest(string type, Character t)
        {
            if (type == null || !type.StartsWith("weakest", StringComparison.OrdinalIgnoreCase)) return null;
            int colon = type.IndexOf(':');
            var pool = colon < 0 ? Resistible
                : type.Substring(colon + 1).Split('|').Select(n => Enum.TryParse(n.Trim(), true, out DAMAGE_TYPE d) ? (DAMAGE_TYPE?)d : null).Where(d => d.HasValue).Select(d => d.Value).ToArray();
            if (pool.Length == 0) pool = Resistible;
            if (t == null) return pool[0];
            DAMAGE_TYPE best = pool[0];
            int least = int.MaxValue;
            foreach (DAMAGE_TYPE d in pool)
            {
                int r = t.GetResistance(d);
                if (r < least) { least = r; best = d; }
            }
            return best;
        }

        /// <summary>"areaFalloff": less damage the further the victim stands from the area's centre, a tenth at the least.</summary>
        private static float Falloff(ActionSpec a, ActionContext ctx, Character t)
        {
            if (a.areaFalloff <= 0f || !ctx.center.HasValue || t == null) return 1f;
            Vector3 d = t.transform.position - ctx.center.Value;
            d.y = 0f;
            return Mathf.Max(0.1f, 1f - a.areaFalloff * Mathf.Round(d.magnitude));
        }

        internal static int BothHandHits;

        private static void Damage(ActionSpec a, ActionContext ctx, Character t)
        {
            if (t == ctx.caster && !ctx.backfire) return; // never hurt yourself (unless your wild magic turned on you)
            // "hands": "both": the hit once with each weapon held, main hand first; a fallen target takes no second swing
            if ((a.weapon > 0f || a.channel) && string.Equals(a.hands, "both", StringComparison.OrdinalIgnoreCase) && ctx.caster is Player pl && !Equipped.Forcing)
            {
                var hands = Equipped.BothHands(pl);
                // an off-hand weapon alone swings too (a plain lookup would ask the empty main hand)
                if (hands.Count > 1 || hands.Count == 1 && hands[0].off)
                {
                    foreach (var (w, off) in hands)
                    {
                        if (t == null || t.GetIsDead() || t.GetHealth() <= 0) break;
                        using (Equipped.Force(w, off)) Damage(a, ctx, t);
                        BothHandHits++;
                    }
                    return;
                }
            }
            DAMAGE_TYPE type = Weakest(a.type, t) ?? DamageType(a.type);
            float v = Random.Range(a.min, Math.Max(a.min, a.max) + 1) + a.perRank * Math.Max(0, ctx.rank) + ScaleBonus(a, ctx);
            v += StackBonus(a, ctx, t) + WeaponBonus(a, ctx) + a.stored * ctx.stored;
            v *= ctx.damageMult * LevelMult(a.levelCap) * Falloff(a, ctx, t);
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
                if (t.effects.GetEffect(id) is ModEffect have) { have.RemoveStacks(-a.stacks); BuffLane.AfterRemoved(t); }
                return;
            }
            if (!EffectManager.instance.CanUseOn(id, t)) return;
            int stacks = ScaledCount(a, a.stacks, ctx, t);
            if (stacks <= 0) return;

            Effect existing = t.effects.GetEffect(id);
            if (existing is ModEffect me && (me.MaxStacks > 1 || !me.Stackable))
            {
                if (a.turns > 0) existing.OverrideDuration(a.turns + ExtraTurns(a, ctx, t)); else existing.RefreshDuration();
                if (me.Invulnerable) existing.isFirstTurn = true; // or the refresh runs out on your own turn, before the enemies' it was cast to outlast
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
            if (a.turns > 0) e.OverrideDuration(a.turns + ExtraTurns(a, ctx, t));
            e.Activate();
            if (e is ModEffect fresh)
            {
                fresh.OnApplied(ctx);
                if (fresh.MaxStacks > 1) fresh.AddStacks(stacks - 1, ctx);
            }
            // a mod effect has its numbers from the start, so landing already counted them; a game effect may set them after Init
            if (!(e is ModEffect) || !EventBus.On) t.CalculateStats();
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
            int stacks = string.IsNullOrEmpty(a.stacksOf) ? 0 : UsedStacks(a, CountOf(a, ctx, t));
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
        /// <summary>"forget": an enemy drops you as its target, turns a random way and goes idle (unaware), as the game's
        /// Memory Hole and Stealth do. Bosses keep their focus.</summary>
        private static void Forget(ActionContext ctx, Character t)
        {
            if (t?.ai == null || t == Player.instance || TileStacks.IsAlly(t) || t.HasTag("BOSS")) return;
            if (t.ai.GetTarget() == Player.instance || t.ai.GetTarget() == ctx.caster) t.ai.SetTarget(null);
            NavNode at = t.GetMover()?.GetCurrentNavNode();
            if (at != null) t.ai.SetFacing(at.transform.position + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)));
            t.ai.ChangeState(AI.AI_STATE.Idle);
        }

        private static void Dismiss(ActionContext ctx, Character t)
        {
            if (t == null || t == ctx.caster) return;
            PetHandling.Unsummon(t, ctx.sourceName, breakdown: false);
        }

        // ---------------------------------------------------------------- items

        private static void GiveItem(ActionSpec a, ActionContext ctx)
        {
            if (ctx.caster != Player.instance || string.IsNullOrEmpty(a.item)) return;
            string id;
            if (a.item.Equals("$item", StringComparison.OrdinalIgnoreCase))
            {
                Item behind = ctx.item != null ? ctx.item : ProcHub.ItemNow;
                if (behind == null || string.IsNullOrEmpty(behind.itemId)) return;
                id = behind.itemId;
            }
            else id = Crafting.ResolveItem(a.item, null);
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
            // "whole": the stack goes, and the gives count once per "amount" items in it
            int used = a.whole ? Math.Max(1, it.stack) : 1;
            int times = a.whole ? used / Math.Max(1, a.amount) : 1;
            if (it.stack > used) { it.SetStack(it.stack - used); it.CalculateValue(); }
            else
            {
                Menu_Inventory.instance.RemoveItem(it, null, isSilent: true);
                Object.Destroy(it.gameObject);
            }
            var got = new List<string>();
            foreach (GiveSpec g in a.gives ?? new List<GiveSpec>())
            {
                int count = 0;
                for (int i = 0; i < times; i++)
                    if (g.chance >= 100 || Random.Range(1, 101) <= g.chance + ctx.chanceBonus) count += Math.Max(1, g.count);
                if (count <= 0) continue;
                string id = Crafting.ResolveItem(g.item, null);
                if (Crafting.Give(id, count) != null) got.Add($"{count}× {ItemRegistry.NameOf(id)}");
            }
            Menu_Inventory.instance.Refresh();
            Game.LOG($"{ctx.sourceName}: {System.Text.RegularExpressions.Regex.Replace(name, "<.*?>", "")} breaks down into {(got.Count > 0 ? string.Join(", ", got) : "nothing useful")}.");
        }

        /// <summary>"tune": works a lasting change into a piece of gear, saved with it: the item the skill is aimed at
        /// ("target": "item"), or the one equipped in "slot". "stat" adds min-max of a stat; "type" on a weapon adds
        /// min-max to that damage type's roll (a new type joins the weapon's damage). "limit" caps how often one item can
        /// be tuned, counted under "tag" (default SCCL_TUNED, shared by every class that tunes).</summary>
        private static void Tune(ActionSpec a, ActionContext ctx)
        {
            if (!(ctx.caster is Player p) || Menu_Player.instance == null) return;
            Item_Equipment it = ctx.item as Item_Equipment ?? SlotItem(a.slot);
            if (it == null || it.equipType == EQUIP_TYPE.None || it.equipType == EQUIP_TYPE.Ammo) return;
            string name = System.Text.RegularExpressions.Regex.Replace(it.GetName(false), "<.*?>", "");
            string counter = string.IsNullOrEmpty(a.tag) ? "SCCL_TUNED" : a.tag;
            if (a.limit > 0 && it.GetTag(counter) >= a.limit) { Game.LOG($"{name} can't take any more tuning."); return; }
            int n = a.max > a.min ? Random.Range(a.min, a.max + 1) : a.min;
            if (n == 0) return;
            var got = new List<string>();
            if (!string.IsNullOrEmpty(a.stat))
            {
                STAT_TYPE? st = a.stat.Equals("random", StringComparison.OrdinalIgnoreCase) ? RandomGearStat() : Enum.TryParse(a.stat, true, out STAT_TYPE s) ? s : (STAT_TYPE?)null;
                if (st == null) { Plugin.Log.LogWarning($"tune: unknown stat '{a.stat}'"); return; }
                it.AddStat(st.Value, n);
                got.Add($"{n:+#;-#} {Game.GetSpriteString(st.Value)}");
            }
            if (!string.IsNullOrEmpty(a.type) && it is Item_Weapon w && Enum.TryParse(a.type, true, out DAMAGE_TYPE dt))
            {
                w.AddDamage(new DmgObj(dt, n) { damageMax = w.damageList.Any(d => d != null && d.dmgType == dt && d.damageMax > 0) ? n : 0 });
                got.Add($"{n:+#;-#} {Game.GetSpriteString(dt)} damage");
            }
            if (got.Count == 0) return;
            it.AddTag(counter, 1); // a new tag starts at 1, an old one counts up
            if (Menu_Player.instance.IsEquipped(it))
            {
                p.CalculateStats();
                Menu_Player.instance.GetItemSlot(it)?.DoShing();
            }
            Game.LOG($"{ctx.sourceName}: {name} gains {string.Join(", ", got)}.");
        }

        /// <summary>A stat a piece of gear could roll: drawn from one of the game's own stat templates, as the Modder's
        /// tinkering draws.</summary>
        private static STAT_TYPE? RandomGearStat()
        {
            StatTemplate t = ItemDatabase.instance?.GetRandomStatTemplate();
            if (t == null) return null;
            var pool = (t.stats_major ?? new StatObject[0]).Concat(t.stats_minor ?? new StatObject[0]).Where(s => s != null).Select(s => s.stat).Distinct().ToList();
            return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : (STAT_TYPE?)null;
        }

        /// <summary>The item in an equip slot: "mainhand", "offhand", or a slot name.</summary>
        internal static Item_Equipment SlotItem(string slot)
        {
            Menu_Player mp = Menu_Player.instance;
            if (string.IsNullOrEmpty(slot) || mp == null) return null;
            if (slot.Equals("mainhand", StringComparison.OrdinalIgnoreCase)) return mp.slot_mainhand?.item as Item_Equipment;
            if (slot.Equals("offhand", StringComparison.OrdinalIgnoreCase)) return mp.slot_offhand?.item as Item_Equipment;
            return Enum.TryParse(slot, true, out EQUIP_TYPE e) ? mp.GetEquipment().FirstOrDefault(x => x != null && x.GetEquipType() == e) as Item_Equipment : null;
        }

        /// <summary>"@Hexed" -> this class's effect id; anything else is a vanilla id.</summary>
        public static string EffectId(string name, string classId)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name[0] == '#') return ModEffect.MakeId(ClassRegistry.LibId, name.Substring(1));
            if (name[0] == '@') return ModEffect.MakeId(classId, name.Substring(1));
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
                        DAMAGE_TYPE? weak = Weakest(a.type, null);
                        Enum.TryParse(weak.HasValue ? "Crushing" : a.type ?? "Crushing", true, out DAMAGE_TYPE type);
                        string range = a.max > a.min ? $"{a.min}-{a.max}" : $"{a.min}";
                        string kind = weak.HasValue ? $"{range}{Scaling(a)}" + (a.type.Contains(":") ? $" {a.type.Substring(a.type.IndexOf(':') + 1).Replace("|", ", ")}" : "") + " damage, of whichever type it resists least,"
                            : $"<color={DmgObj.GetDamageColor(type)}>{range}{Scaling(a)}{(a.weapon > 0f ? $" (+{Mathf.RoundToInt(a.weapon * 100)}% weapon, with its on-hit effects)" : "")}{(a.stored > 0f ? $" (+{Mathf.RoundToInt(a.stored * 100)}% of the damage it took while this lasted)" : "")} {Game.GetSpriteString(type)}</color>";
                        s = $"Deals {kind}{(a.trueDamage ? " <color=#FFFFFF>true damage</color>" : "")} to {who}{LevelNote(a.levelCap)}";
                        if (a.areaFalloff > 0f) s += $", {Mathf.RoundToInt(a.areaFalloff * 100)}% less for each tile from the centre";
                        if ((a.weapon > 0f || a.channel) && string.Equals(a.hands, "both", StringComparison.OrdinalIgnoreCase))
                            s += a.weapon > 0f ? ", once with each weapon you hold (the off-hand one's roll at its usual share)" : ", once with each weapon you hold";
                        break;
                    }
                case "volley":
                    {
                        string per = Joined(Describe(a.actions, classId, "it"));
                        int n = Math.Max(1, a.amount);
                        string count = Scaled(a) ? $"{(a.amount > 0 ? $"{a.amount} shot{(a.amount > 1 ? "s" : "")}, plus " : "")}{a.bonusPerStack} per {PerWord(a, classId, who)}," : $"{n} shot{(n > 1 ? "s" : "")}";
                        s = $"Fires {count} at {who}, one after another; once it falls, the rest turn on the nearest enemy within {(a.radius > 0f ? a.radius : 3f):0.#} of it. Each shot: {per}";
                        break;
                    }
                case "attempt":
                    {
                        int n = a.amount > 0 ? a.amount : 6;
                        string yes = Describe(a.actions, classId, who).Replace("• ", "    - ");
                        string no = Describe(a.@else, classId, who).Replace("• ", "    - ");
                        string odds = $"{a.odds}%{(string.IsNullOrEmpty(a.scale) ? "" : $" + {Mathf.RoundToInt(a.scaleMult * 100)}% of your {a.scale.Replace("+", " and ")}")}"
                            + (string.IsNullOrEmpty(a.resistStat) ? "" : $", less {Mathf.RoundToInt(a.resistMult * 100)}% of its {a.resistStat}");
                        s = $"Bears down on {(a.together ? $"{who}, all at once," : who)} for up to {n} tr{(n > 1 ? "ies" : "y")}, a second apart, at odds of {odds}"
                            + (a.oddsPerTry != 0 ? $", {a.oddsPerTry:+#;-#}% each try after" : "")
                            + (a.tryCost > 0 ? $"; each try costs {a.tryCost} {StatObj.GetStatString(STAT_TYPE.Stamina)}{(a.together ? " however many it holds" : "")} and it stops when you can't pay" : "")
                            + (a.together ? "; each one caught drops out while the rest are held" : "")
                            + $".\n  If one lands:\n{(yes.Length > 0 ? yes : "    - nothing")}" + (no.Length > 0 ? $"\n  If none does:\n{no}" : "");
                        return "• " + (a.chance < 100 ? $"{a.chance}%: " : "") + s;
                    }
                case "heal": s = $"Heals {who} for <color=#5CFF6B>{a.amount}{Scaling(a)}</color>"; break;
                case "stamina": s = $"{(a.amount >= 0 ? "Restores" : "Drains")} <color=#7CFC00>{Math.Abs(a.amount)}{Scaling(a)}</color> {StatObj.GetStatString(STAT_TYPE.Stamina)} for {who}"; break;
                case "shield": s = $"Gives {who} <color=#6FD3FF>{a.amount}{Scaling(a)}</color> {StatObj.GetStatString(STAT_TYPE.Shield)} overshield"; break;
                case "recharge": s = $"Recharges <color=#6FD3FF>{a.amount}{Scaling(a)}</color> of your {StatObj.GetStatString(STAT_TYPE.Shield)}"; break;
                case "effect":
                    s = a.stacks < 0 && !Scaled(a)
                        ? $"Removes {-a.stacks} <color=#FFD15C>{EffectName(a.effect, classId)}</color> from {who}"
                        : $"Applies <color=#FFD15C>{EffectName(a.effect, classId)}</color> to {who}{(a.turns > 0 ? $" for {a.turns} turns" : "")}";
                    break;
                case "stun": s = $"Stuns {who} for {Turns(a)} turn{(Turns(a) > 1 ? "s" : "")}"; break;
                case "knockback": s = $"Knocks {who} back {Math.Max(1, a.amount)}"; break;
                case "blind": s = $"Blinds {who}"; break;
                case "cooldowns": s = string.IsNullOrEmpty(a.skill) ? $"Reduces your other cooldowns by {Math.Max(1, a.amount)}" : $"Takes {Math.Max(1, a.amount)} off {a.skill}'s cooldown"; break;
                case "shorten":
                    {
                        string what = string.IsNullOrEmpty(a.effect) || a.effect.Equals("debuffs", StringComparison.OrdinalIgnoreCase) ? "debuff" : a.effect.Equals("buffs", StringComparison.OrdinalIgnoreCase) ? "buff" : null;
                        int by = a.amount == 0 ? 1 : a.amount;
                        string on = what != null ? $"each {what} on {who}" : $"{EffectName(a.effect, classId)} on {who}";
                        s = by > 0 ? $"Takes {by} turn{(by > 1 ? "s" : "")} off {on}" : $"Adds {-by} turn{(by < -1 ? "s" : "")} to {on}";
                        break;
                    }
                case "ammo": s = a.amount >= 0 ? $"Puts <color=#FFD15C>{a.amount}{Scaling(a)}</color> rounds back in your loaded ammo" : $"Spends <color=#FFD15C>{-a.amount}</color> rounds of your loaded ammo"; break;
                case "unlock": s = $"Opens {(a.odds > 0 ? $"with {a.odds}% odds each " : "")}the locked doors and containers within {(a.radius > 0f ? a.radius : 2.5f):0.#} of you (not sealed or boss doors)"; break;
                case "disarm": s = $"Disarms {(a.odds > 0 ? $"with {a.odds}% odds each " : "")}the armed traps within {(a.radius > 0f ? a.radius : 2.5f):0.#} of you, seen or not, and salvages their parts"; break;
                case "steal":
                    {
                        int n = Math.Max(1, a.amount);
                        s = $"Steals {(n == 1 ? "an item" : $"up to {n} items")} from each vendor within {(a.radius > 0f ? a.radius : 2.5f):0.#} of you, "
                          + (a.odds > 0 ? $"{a.odds}%" : "50% + your Smarts") + " odds each; a failed theft turns the vendor on you";
                        break;
                    }
                case "anchor": s = "Marks the tile you stand on and your health as they are now"; break;
                case "rewind": s = $"Pulls you back to your marked tile{(a.restore ? ", your health back up to what it was there" : "")} (uses up the mark)"; break;
                case "xp": s = $"You gain experience{(a.amount > 1 ? $" ({a.amount}x)" : "")} as for a won floor event"; break;
                case "purge": s = $"Removes {EffectName(a.effect, classId)} from {who}"; break;
                case "teleport": s = "Teleports you to the target spot"; break;
                case "dispel": s = $"Strips {(a.amount > 0 ? a.amount.ToString() : "all")} buff{(a.amount == 1 ? "" : "s")} from {who}{(string.IsNullOrEmpty(a.effect) ? "" : $"; you gain {EffectName(a.effect, classId)} per buff removed")}"; break;
                case "cleanse": s = $"Removes {(a.amount > 0 ? a.amount.ToString() : "all")} debuff{(a.amount == 1 ? "" : "s")} from {who}"; break;
                case "transfer": s = string.IsNullOrEmpty(a.effect)
                        ? $"Moves {(a.amount > 0 ? a.amount.ToString() : "all")} of your debuff{(a.amount == 1 ? "" : "s")} onto {who}, with the turns and stacks they had left"
                        : $"Moves your {EffectName(a.effect, classId)} onto {who}, with the turns and stacks it had left"; break;
                case "chain":
                    {
                        string link = string.IsNullOrEmpty(a.effect) ? "enemies" : $"enemies with {string.Join(" or ", a.effect.Split('|').Select(x => EffectName(x.Trim(), classId)))}";
                        string inner = Describe(a.actions, classId, "each target").Replace("• ", "    - ");
                        string power = a.falloff != 1f ? $" ({Mathf.RoundToInt(a.falloff * 100)}% power per jump)" : "";
                        s = a.leap ? $"You charge {who}, then on to up to {a.jumps} nearby {link}, landing beside each{power}:\n{inner}" :
                            a.skipFirst ? $"From {who}, jumps to up to {a.jumps} nearby {link}{power}:\n{inner}" :
                            $"Strikes {who}, then jumps to up to {a.jumps} nearby {link}{power}:\n{inner}";
                        break;
                    }
                case "summon": s = $"Summons {(a.amount > 1 && !Scaled(a) ? a.amount + " " : "a ")}{a.summonName ?? CreatureName(a.creature)}{(a.turns > 0 ? $" for {a.turns} turns" : "")}{(a.maxSummons > 0 ? $" (max {a.maxSummons})" : "")}{SummonGrowth(a)}{(a.hostile ? ", against you" : a.chaos ? ", fighting everyone near it, you included" : a.anySide ? "; a coin toss decides whose side it takes" : "")}"; break;
                case "corpses":
                    {
                        string many = a.amount > 0 ? $"up to {a.amount} {(a.amount == 1 ? "corpse" : "corpses")}" : "every corpse";
                        string per = Joined(Describe(a.actions, classId, a.blastRadius > 0f ? $"enemies within {a.blastRadius:0.#} of it" : who));
                        s = $"{(a.keepCorpses ? "Draws on" : "Consumes")} {many} within {(a.radius > 0f ? a.radius : 3f):0.#} of you. For each: {per}";
                        break;
                    }
                case "loot":
                    {
                        string kind = string.IsNullOrEmpty(a.lootType) || a.lootType.Equals("Random", StringComparison.OrdinalIgnoreCase) ? "item" : a.lootType.ToLowerInvariant() + " item";
                        string what = (string.IsNullOrEmpty(a.rarity) ? "" : a.rarity.ToLowerInvariant() + " ") + kind;
                        int n = Math.Max(1, a.amount);
                        s = $"Drops {(n > 1 ? n.ToString() : "aeiou".IndexOf(what[0]) >= 0 ? "an" : "a")} {what}{(n > 1 ? "s" : "")} from {who}";
                        break;
                    }
                case "echo": s = $"Echoes your last new ability onto {who} at {Mathf.RoundToInt(a.falloff * 100)}% power"; break;
                case "tame":
                    {
                        string odds = $"{a.amount}%{(a.perRank > 0 ? $" (+{a.perRank}/rank)" : "")}{Scaling(a)}";
                        string won = a.then != null && a.then.Count > 0 ? "\n" + Describe(a.then, classId, "it").Replace("• ", "    - ") : "";
                        string lost = a.@else != null && a.@else.Count > 0 ? "\n  If it throws you off:\n" + Describe(a.@else, classId, who).Replace("• ", "    - ") : "";
                        s = $"Tries to bind {who} to you for good ({odds}, up to +50% more the more wounded it is; not bosses or elites){(a.maxSummons > 0 ? $", at most {a.maxSummons} at a time" : "")}{won}{lost}";
                        break;
                    }
                case "giveitem": s = "$item".Equals(a.item, StringComparison.OrdinalIgnoreCase)
                        ? $"Gives you {(a.amount > 1 ? a.amount + " more" : "another")} of that item"
                        : $"Gives you {Math.Max(1, a.amount)}× {ItemRegistry.NameOf(Crafting.ResolveItem(a.item, null))}"; break;
                case "consumeitem": s = $"Uses up {Math.Max(1, a.amount)}× {(a.item != null ? ItemRegistry.NameOf(Crafting.ResolveItem(a.item, null)) : (a.tag ?? "?").ToLowerInvariant())}"; break;
                case "salvage": s = $"Breaks {(a.whole ? "the whole stack" : "the item")} down into {string.Join(", ", (a.gives ?? new List<GiveSpec>()).Select(g => $"{g.count}× {ItemRegistry.NameOf(Crafting.ResolveItem(g.item, null))}{(g.chance < 100 ? $" ({g.chance}%)" : "")}"))}{(a.whole ? $" for every {(a.amount > 1 ? a.amount + " items" : "item")} in it" : "")}"; break;
                case "craft": s = "Opens field crafting"; break;
                case "tune":
                    {
                        string range = a.max > a.min ? $"{a.min}-{a.max}" : $"{a.min}";
                        var adds = new List<string>();
                        if (!string.IsNullOrEmpty(a.stat))
                            adds.Add(a.stat.Equals("random", StringComparison.OrdinalIgnoreCase) ? $"{range} of a random gear stat"
                                : Enum.TryParse(a.stat, true, out STAT_TYPE st) ? $"{range} {Game.GetSpriteString(st)}" : $"{range} {a.stat}");
                        if (!string.IsNullOrEmpty(a.type)) adds.Add($"{range} {a.type} damage (weapons)");
                        string what = !string.IsNullOrEmpty(a.slot) ? $"the gear in your {(a.slot.ToLowerInvariant() == "mainhand" ? "main hand" : a.slot.ToLowerInvariant() == "offhand" ? "off hand" : a.slot.ToLowerInvariant() + " slot")}" : "the item";
                        s = $"Permanently adds {string.Join(" and ", adds)} to {what}{(a.limit > 0 ? $" (each item takes {a.limit} at most)" : "")}";
                        break;
                    }
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
                case "silence": s = $"<color=#B58CFF>Silences</color> {who} for {Afflictions.SilenceTurns(a)} turn{(Afflictions.SilenceTurns(a) > 1 ? "s" : "")}: basic attacks only ({(a.bossTurns > 0 ? $"a boss for {a.bossTurns} turn{(a.bossTurns > 1 ? "s" : "")}, then not again for {Afflictions.BossSilenceRest} turns" : "bosses shrug it off")})"; break;
                case "exhaust": s = $"Sets back {who}'s special attacks by {Math.Max(1, a.amount)} turn{(Math.Max(1, a.amount) > 1 ? "s" : "")}"; break;
                case "swap": s = $"You trade places with {who}"; break;
                case "throw":
                    {
                        string imp = Describe(a.actions, classId, "both").Replace("• ", "").Replace("\n", "; ").TrimEnd('.', ' ');
                        s = $"Throws {who} up to {Math.Max(1, a.amount)} tile{(Math.Max(1, a.amount) > 1 ? "s" : "")} away from you" + (imp.Length > 0 ? $"; slamming into someone or a wall: {imp}" : "");
                        break;
                    }
                case "leap": s = $"You leap to {who}"; break;
                case "forget": s = $"{char.ToUpperInvariant(who[0])}{who.Substring(1)} loses track of you: it stops hunting you and looks away, unaware again"; break;
                case "rush":
                    {
                        int n = Math.Max(1, a.amount);
                        string way = (a.direction ?? "").ToLowerInvariant() switch { "back" => "back", "left" => "to your left", "right" => "to your right", _ => "ahead" };
                        string imp = Describe(a.actions, classId, "it").Replace("• ", "").Replace("\n", "; ").TrimEnd('.', ' ');
                        s = $"You rush up to {n} tile{(n > 1 ? "s" : "")} {way}, stopping at a wall, a closed door or anyone in the way"
                          + (imp.Length > 0 ? $"; an enemy you run into: {imp}" : "");
                        break;
                    }
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
                case "cast": s = $"Fires <color=#FFD15C>{a.skill ?? "?"}</color> at {who} for free"; break;
                case "order":
                    {
                        string p = (a.pets ?? "any").ToLowerInvariant();
                        string pets = p == "any" ? "Each of your summons" : p == "self" ? "Each summon from this skill" : p.StartsWith("group:") ? $"Each of your {a.pets.Substring(6)}" : $"Each of your {a.pets}";
                        s = $"{pets} acts on its own: {Joined(Describe(a.actions, classId, who))}";
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
                case "rearm": s = $"{(string.IsNullOrEmpty(a.skill) ? "Every armed reaction" : $"Your armed {a.skill.Replace("|", " or ")}")} gets {(a.amount > 0 ? $"{a.amount} charge{(a.amount == 1 ? "" : "s")} back" : "")}{(a.amount > 0 && a.turns > 0 ? " and " : "")}{(a.turns > 0 ? $"{a.turns} more turn{(a.turns == 1 ? "" : "s")}" : "")}"; break;
                case "hazard": s = $"Leaves {GroundFields.HazardName(a.hazard)} on the floor{(a.radius > 0 ? $" around {who} (area {a.radius:0.#})" : $" under {who}")}"; break;
                case "clearhazards": s = $"Puts out {(string.IsNullOrEmpty(a.hazard) || a.hazard.Equals("all", StringComparison.OrdinalIgnoreCase) ? "fire, acid and other harmful ground" : GroundFields.HazardName(a.hazard).ToLowerInvariant())}{(a.radius > 0 ? $" around {who} (area {a.radius:0.#})" : $" under {who}")}"; break;
                case "field":
                    {
                        string aff = (a.affects ?? "enemies").ToLowerInvariant() switch { "allies" => "allies", "all" => "anyone", _ => "enemies" };
                        string inner = Describe(a.actions, classId, aff + " in it").Replace("• ", "").Replace("\n", "; ");
                        s = $"Leaves {(a.fieldName ?? "a field")} for {Math.Max(1, a.turns)} turns{(a.overShape ? " across the cone" : a.radius > 0 ? $" (area {a.radius:0.#})" : "")}: each turn{(a.onEnter ? " and when they step in" : "")}, {inner}";
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
                        string what = a.scatter > 0 && a.trail
                            ? $"up to {a.scatter} × {(a.fieldName ?? "trap")}, one on each tile along the way to the aim"
                            : a.scatter > 0
                            ? $"{a.scatter} × {(a.fieldName ?? "trap")}, strewn over random tiles within {(a.spread > 0f ? a.spread : 3f):0.#} of the aim"
                            : a.fieldName ?? "a trap";
                        s = $"Sets {what}{(a.armTurns > 0 ? $" (live after {a.armTurns} turn{(a.armTurns > 1 ? "s" : "")})" : "")}: the first {aff} to "
                          + (a.triggerRadius > 0 ? $"come within {a.triggerRadius:0.#}" : "step on " + (a.scatter > 0 ? "one" : "it")) + $" sets {(a.scatter > 0 ? "that one" : "it")} off: {inner}"
                          + (extra.Count > 0 ? $" ({string.Join("; ", extra)})" : "");
                        break;
                    }
                case "springtraps": s = $"Sets off every {(a.fieldName ?? "trap of yours").ToLowerInvariant()} on this floor at once"; break;
                case "movetraps":
                    s = $"Pulls your {(a.fieldName == null ? "traps" : Plural(a.fieldName.ToLowerInvariant()))}{(a.near > 0f ? $" within {a.near:0.#}" : " on this floor")} in under {who}, fuses and all; a live one landing on someone it catches goes off";
                    break;
                case "ready": s = "The skill you are using is ready again and doesn't end your turn"; break;
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
                case "swapbody": s ="You take over your nearest other body; the one you leave fights on beside you. With none standing, you trade places with a fallen one's corpse"; break;
                case "carrytobody": s = $"{EffectName(a.effect, classId)} goes with you into the body you step into"; break;
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
            if (Scaled(a) && act != "volley") // a volley says its shot count itself
            {
                string per = PerWord(a, classId, who);
                string used = a.consumeStacks && !a.stacksOf.StartsWith("group:", StringComparison.OrdinalIgnoreCase) && !a.stacksOf.StartsWith("value:", StringComparison.OrdinalIgnoreCase) && a.stacksOf != "summons" && a.stacksOf != "bodies" ? (a.stacksUsed > 0 ? $" (uses up to {a.stacksUsed} and spends only those)" : " (consumes them)") : a.stacksUsed > 0 ? $" (counts up to {a.stacksUsed})" : "";
                // effect and summon actions scale a count (stacks, summons) rather than an amount
                int baseN = act == "effect" ? a.stacks : act == "summon" ? a.amount : -1;
                // a base of 0 is the per-stack amount alone: "Heals you for 1 per round the shot fired"
                int zero = baseN < 0 && a.amount == 0 ? s.IndexOf(">0<", StringComparison.Ordinal) : -1;
                if (zero >= 0) s = s.Substring(0, zero) + $">{Math.Abs(a.bonusPerStack)}<" + s.Substring(zero + 3) + $" per {per}{used}";
                else if (baseN < 0) s += $", +{a.bonusPerStack} per {per}{used}";
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
            return "• " + s + (s.EndsWith(".") ? "" : "."); // a nested list already ends its last line
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
            if (stat != null && stat.Contains("|")) return $", growing with {ScaleWords(stat)}";
            var names = (stat ?? "").Split('+').Select(x => Enum.TryParse(x, true, out STAT_TYPE st) && st != STAT_TYPE.Smarts ? StatObj.GetStatString(st) : null).Where(x => x != null).ToList();
            return names.Count > 0 ? $", growing with your {string.Join(" and ", names)}" : "";
        }

        private static string CreatureName(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return "ally";
            if (spec.Contains(".")) spec = spec.Split('.')[1].Replace("Prefab", "").Replace("character", "construct");
            return spec.Replace("_", " ").Trim().ToLowerInvariant();
        }

        /// <summary>What one count of an action's stacksOf is, in words: "stack of Hexed on the target", "summon you have".</summary>
        private static string PerWord(ActionSpec a, string classId, string who)
        {
            string sf = (a.stacksFrom ?? "").ToLowerInvariant(); string on = sf == "self" ? "on you" : sf == "target" ? "on the target" : $"on {who}";
            return a.stacksOf == "summons" ? "summon you have" : a.stacksOf == "bodies" ? "other living body" :
                a.stacksOf.StartsWith("value:", StringComparison.OrdinalIgnoreCase) ? ValuePer(a.stacksOf.Substring(6), classId, on) :
                a.stacksOf.StartsWith("counter:", StringComparison.OrdinalIgnoreCase) ? $"point of <color=#9FD3FF>{a.stacksOf.Substring(8)}</color> {on}" :
                a.stacksOf.StartsWith("group:", StringComparison.OrdinalIgnoreCase) ? $"{a.stacksOf.Substring(6)} you have" : $"stack of {EffectName(a.stacksOf, classId)} {on}";
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
            string.Join(" and ", stats.Select(any => any.Count > 3 ? "your best stat" : any.Count == 1 ? StatObj.GetStatString(any[0]) : string.Join(" or ", any.Select(st => StatObj.GetStatString(st))) + ", whichever is higher"));

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
            string when = ProcWhen(p, classId);
            int chance = Math.Min(100, p.chance + chanceBonus);
            if (p.when != null) { string c = When.Describe(p.when, classId); if (c.Length > 0) when += $", if {c}"; }
            if (p.every > 1) when = $"every {p.every}{Ordinal(p.every)} time {when.Replace("when ", "").Replace("each turn", "a turn passes")}";
            var limits = new List<string>();
            if (p.cooldown > 0) limits.Add($"then not for {p.cooldown} turn{(p.cooldown > 1 ? "s" : "")}");
            if (p.maxPerTurn > 0) limits.Add($"at most {p.maxPerTurn}× a turn");
            if (p.cascade > 0) limits.Add("even when another proc set it off");
            if (limits.Count > 0) when += $" ({string.Join(", ", limits)})";
            string who = p.radius > 0 ? (p.pick > 0 ? $"{(p.pick == 1 ? "a random enemy" : p.pick + " random enemies")} within {p.radius:0.#}" : $"enemies within {p.radius:0.#}") + (string.Equals(p.on, "smash", StringComparison.OrdinalIgnoreCase) ? " of the wreck" : "") : DefaultProcWho(p.on);
            string head = chance < 100 ? $"<b>{chance}% {when}:</b>" : $"<b>{char.ToUpper(when[0]) + when.Substring(1)}:</b>";
            return head + "\n" + Describe(p.actions, classId, who);
        }

        /// <summary>A trigger in words: "when you kill", "each turn".</summary>
        internal static string ProcWhen(ProcSpec p, string classId)
        {
            return (p.on ?? "").ToLowerInvariant() switch
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
                "craft" => string.IsNullOrEmpty(p.match) ? "when you fabricate an item" : $"when you fabricate {p.match}",
                "ammo" => string.IsNullOrEmpty(p.match) ? "when your gun fires rounds" : $"when your gun fires {p.match} rounds",
                "equip" => string.IsNullOrEmpty(p.match) ? "when you equip an item" : $"when you equip {p.match}",
                "unequip" => string.IsNullOrEmpty(p.match) ? "when you take off an item" : $"when you take off {p.match}",
                "search" => "when you search a container",
                "smash" => "when a crate, console or door breaks near you",
                "newfloor" => "when you reach another floor",
                "shieldbreak" => "when your shield breaks",
                "absorb" => "when your shield takes a hit",
                "block" => "when you block a hit",
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
            "absorb" => "the attacker",
            "block" => "the attacker",
            "charge" => "the target",
            "leap" => "the target",
            "knockback" => "that enemy",
            "swap" => "the one you swapped with",
            "chain" => "the enemy it jumped to",
            "summon" => "the new ally",
            "inflict" => "that enemy",
            "useitem" => "whoever it was used on",
            _ => "you",
        };

        private static string Ordinal(int n) => n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };

        // ---------------------------------------------------------------- borrowed visuals

        private static readonly Dictionary<string, GameObject> vfxCache = new Dictionary<string, GameObject>();

        /// <summary>At most this many copies of one particle play at once; an area hit on six enemies isn't six explosions.</summary>
        internal static int MaxCopies = 3;

        /// <summary>
        /// Spawns a vanilla particle by effect id ("EFFECT_Fire") or ability id ("VOID_PSYKER_BLAST[.field]").
        /// "name@0.5" plays it at half size.
        /// </summary>
        public static void SpawnVfx(string spec, Vector3 pos)
        {
            if (string.IsNullOrEmpty(spec)) return;
            if (!ParticleLoad.Later(spec, pos)) SpawnNow(spec, pos);
        }

        /// <summary>One of a pattern that only reads whole (a nova's eight ways): past MaxCopies, and never held back a frame.</summary>
        internal static void SpawnPattern(string spec, Vector3 pos)
        {
            if (!string.IsNullOrEmpty(spec)) SpawnNow(spec, pos, capped: false);
        }

        /// <summary>True when an effect was actually started.</summary>
        internal static bool SpawnNow(string spec, Vector3 pos, bool capped = true)
        {
            if (capped && VfxLife.Copies.TryGetValue(spec, out int copies) && copies >= MaxCopies) return false;
            if (EventBus.SkipEffect(pos)) return false;
            SplitScaleOnly(spec, out string own);
            float scale = SplitScale(spec, out string name);
            GameObject prefab = ResolveVfx(name);
            if (prefab == null) return false;
            bool floor = CustomVfx.AliasOnFloor(own);
            VfxLife kept = ParticleLoad.Reuse(spec);
            if (kept != null)
            {
                kept.transform.SetPositionAndRotation(pos, Quaternion.identity);
                if (floor) CustomVfx.GroundSnap.Floor(kept.gameObject);
                kept.gameObject.SetActive(true);
                kept.Replay();
                return true;
            }
            GameObject go = ParticleGate.Make(prefab, pos);
            if (floor) CustomVfx.GroundSnap.Floor(go);
            CustomVfx.Trim(own, go);
            Rescale(go, scale);
            if (EventBus.On) ParticleLoad.Lighten(go);
            go.SetActive(true);
            Wake(prefab, go);
            VfxLife life = go.AddComponent<VfxLife>();
            life.spec = spec;
            life.reusable = !CustomVfx.Has(name) && CustomVfx.Alias(own) == null && !go.GetComponentsInChildren<ParticleSystem>(true).Any(ps => ps.main.stopAction != ParticleSystemStopAction.None);
            return true;
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
            if (EventBus.SkipEffect(from) && EventBus.SkipEffect(to)) return time; // the hit still waits for the flight nobody saw
            GameObject go = ParticleGate.Make(prefab, from);
            if (EventBus.On) ParticleLoad.Lighten(go);
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
            internal static readonly Dictionary<string, int> Copies = new Dictionary<string, int>();
            private const int LookEvery = 4; // asking every system whether it's done, every frame, is most of what a crowd of these costs
            internal bool reusable;
            private string named;
            private ParticleSystem[] systems;
            private float born;
            private int index = -1, turn;

            /// <summary>AddComponent wakes this before anyone can name it, so a name can arrive after it's already live.</summary>
            internal string spec
            {
                get => named;
                set { Tally(-1); named = value; Tally(1); }
            }

            private void Tally(int by)
            {
                if (named == null || index < 0) return;
                Copies.TryGetValue(named, out int now);
                Copies[named] = now + by;
            }

            private void OnEnable()
            {
                index = Live.Count;
                Live.Add(this);
                Tally(1);
                born = Time.time;
                turn = GetInstanceID() & (LookEvery - 1);
                if (systems == null) systems = GetComponentsInChildren<ParticleSystem>(true);
            }

            private void OnDisable()
            {
                Tally(-1);
                VfxLife last = Live[Live.Count - 1];
                Live[index] = last;
                last.index = index;
                Live.RemoveAt(Live.Count - 1);
                index = -1;
            }

            internal void Replay()
            {
                foreach (ParticleSystem ps in systems)
                {
                    if (ps == null) continue;
                    ps.Clear(false);
                    ps.Play(false);
                }
            }

            private void Finish()
            {
                if (!ParticleLoad.Keep(this)) Destroy(gameObject);
            }

            private void Update()
            {
                float age = Time.time - born;
                if (systems.Length == 0) { if (age > 3f) Finish(); return; }
                if (age > 6f) { Finish(); return; }
                if (age <= 1.5f) return;
                if (EventBus.On && (Time.frameCount & (LookEvery - 1)) != turn) return;
                bool alive = false;
                foreach (ParticleSystem ps in systems)
                {
                    if (ps == null) continue;
                    if (ps.isEmitting && ps.main.loop) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    if (!alive && ps.IsAlive(false)) alive = true;
                }
                if (!alive && age > 1.6f) Finish();
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
