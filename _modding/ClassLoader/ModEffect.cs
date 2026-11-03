using System;
using System.Collections.Generic;
using System.Linq;
using I2.Loc;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// A status effect defined by an EffectSpec. Registered into EffectManager's lookup under "SCCLFX_..." so the game
    /// applies, saves and restores it like any vanilla effect. Configuration is looked up by name in Awake.
    /// </summary>
    public class ModEffect : Effect
    {
        internal class Runtime
        {
            public EffectSpec spec;
            public string classId;
            public string id;
            public Sprite icon;
            public LocalizedString name, desc;
            public ParticleControl particle;

            private List<List<STAT_TYPE>> capStats;

            /// <summary>The ceiling a scaling cap can reach; maxStacks when it doesn't scale.</summary>
            internal int CapCeiling => spec.capScale == null ? Mathf.Max(1, spec.maxStacks)
                : Mathf.Max(Mathf.Max(1, spec.maxStacks), spec.capScale.ceiling > 0 ? spec.capScale.ceiling : spec.maxStacks * 2);

            /// <summary>
            /// The stack cap on this character right now. The effect's own "stats" are left out, so a pool that gives
            /// the stat it scales with (a pool that gives Smarts) can't raise its own cap.
            /// </summary>
            internal int CapOn(Character who)
            {
                int floor = Mathf.Max(1, spec.maxStacks);
                StackCapSpec c = spec.capScale;
                if (c == null || who == null || who.primaryAttributes == null || who.secondaryAttributes == null) return floor;
                capStats ??= Actions.ScaleStats(c.scale).ToList();
                int own = who.effects?.GetEffect(id) is ModEffect me ? me.Stacks : 0;
                int Point(STAT_TYPE st) => who.GetStat(st) - own * OwnPerStack(st);
                float points = capStats.Sum(any => any.Max(st => Point(st)));
                return Mathf.Clamp(floor + Mathf.FloorToInt(Mathf.Max(0f, points - c.from) * c.perPoint), floor, CapCeiling);
            }

            private int OwnPerStack(STAT_TYPE st)
            {
                if (spec.stats == null) return 0;
                foreach (var kv in spec.stats)
                    if (Enum.TryParse(kv.Key, true, out STAT_TYPE t) && t == st) return kv.Value;
                return 0;
            }

            /// <summary>The line tooltips show for a scaling cap, or "" for a fixed one.</summary>
            internal string CapLine() => spec.capScale == null ? ""
                : $"<color=#9FD8FF>Cap grows with {Actions.ScaleWords(spec.capScale.scale)}, up to {CapCeiling}.</color>";
        }

        internal const string Prefix = "SCCLFX_";
        internal static readonly Dictionary<string, Runtime> Specs = new Dictionary<string, Runtime>();

        /// <summary>The id must avoid SKILL/PASSIVE/RARE/MUTATION/CURSED/QUEST: EffectManager routes on those words.</summary>
        internal static string MakeId(string classId, string name)
        {
            var key = (classId, name);
            if (!madeIds.TryGetValue(key, out string id)) madeIds[key] = id = Prefix + Sanitize(classId) + "_" + Sanitize(name);
            return id;
        }

        /// <summary>Conditions and stack checks ask for the same few ids on every hit, and building one walks both names letter by letter.</summary>
        private static readonly Dictionary<(string, string), string> madeIds = new Dictionary<(string, string), string>();

        private static string Sanitize(string s)
        {
            s = new string((s ?? "").Where(char.IsLetterOrDigit).ToArray());
            foreach (string bad in new[] { "SKILL", "PASSIVE", "RARE", "MUTATION", "CURSED", "QUEST" })
                s = s.Replace(bad, bad.Substring(0, 1) + bad.Substring(1).ToLowerInvariant());
            return s;
        }

        private Runtime rt;
        private bool bound;

        internal EffectSpec Spec { get { Bind(); return rt?.spec; } }
        internal string ClassId { get { Bind(); return rt?.classId; } }
        internal bool Stackable { get { Bind(); return rt?.spec.stackable ?? true; } }
        /// <summary>The cap right now: it can move with the bearer's stats (capScale).</summary>
        internal int MaxStacks { get { Bind(); return rt?.CapOn(target) ?? 1; } }
        /// <summary>Stack count lives in chargeCount, which the base class already saves and restores.</summary>
        internal int Stacks => Mathf.Max(1, chargeCount);

        internal static int burstDepth;

        /// <summary>
        /// Adds stacks (clamped to the cap), rescales stats, and bursts at burstAt. Stacks already above a cap that
        /// fell stay until spent: gear coming off stops the pool filling, it doesn't take what you hold.
        /// </summary>
        internal void AddStacks(int n, ActionContext ctx)
        {
            Bind();
            if (rt == null || MaxStacks <= 1) return;
            int before = Stacks;
            chargeCount = Mathf.Clamp(Stacks + n, 1, Mathf.Max(MaxStacks, Stacks));
            ScaleToStacks();
            if (target == Player.instance) EventBus.Mark(EventBus.Effects);
            foreach (ThresholdSpec th in rt.spec.thresholds ?? new List<ThresholdSpec>())
                if (before < th.at && chargeCount >= th.at) RunGroup(th.actions, th.radius, th.vfx, th.sound, th.message, ctx);
            if (rt.spec.burstAt > 0 && chargeCount >= rt.spec.burstAt) Burst(ctx);
        }

        /// <summary>Takes stacks away (an ability spending a resource); the effect goes when none are left.</summary>
        internal void RemoveStacks(int n)
        {
            Bind();
            if (n <= 0) return;
            if (Stacks - n <= 0)
            {
                Character who = target;
                RemoveInstant();
                if (who != null && !who.GetIsDead()) BuffLane.AfterRemoved(who);
                return;
            }
            chargeCount = Stacks - n;
            ScaleToStacks();
            if (target == Player.instance) EventBus.Mark(EventBus.Effects);
        }

        /// <summary>Sets the stack count outright (code-driven effects such as Strain); no thresholds or bursts.</summary>
        internal void SetStacks(int n)
        {
            Bind();
            n = Mathf.Clamp(n, 1, MaxStacks);
            if (rt == null || n == Stacks) return;
            chargeCount = n;
            ScaleToStacks();
            if (target == Player.instance) EventBus.Mark(EventBus.Effects);
        }

        private float DealtMult => rt.spec.damageDealtMult + rt.spec.damageDealtPerStack * (MaxStacks > 1 ? Stacks : 1);
        private float TakenMult => rt.spec.invulnerable && !Invulnerable ? 1f : rt.spec.damageTakenMult + rt.spec.damageTakenPerStack * (MaxStacks > 1 ? Stacks : 1);

        /// <summary>Runs actions as this effect: default target is the bearer, or enemies around it with a radius.</summary>
        /// <summary>Health the bearer has lost since this effect went on (for "stored" damage: a revenge counter).</summary>
        internal int Taken;

        internal void RunGroup(List<ActionSpec> actions, float radius, string vfx, string sound, string message, ActionContext ctx)
        {
            if (actions == null || actions.Count == 0 || target == null || burstDepth > 3) return;
            Character bearer = target;
            var c = new ActionContext
            {
                stored = Taken,
                caster = caster != null ? caster : (ctx?.caster ?? Player.instance),
                source = ctx?.source,
                classId = rt.classId,
                sourceName = GetName(),
                rank = ctx?.rank ?? 0,
            };
            burstDepth++;
            try
            {
                Vector3 at = bearer.GetHitFx().position;
                ModAudio.Play(sound, at, rt.classId);
                Actions.SpawnVfx(vfx, at);
                if (!string.IsNullOrEmpty(message)) Game.LOG($"<color=#FFD15C>{message}</color>");
                var self = actions.Where(a => (a.to ?? "").ToLowerInvariant() == "self").ToList();
                var rest = actions.Except(self).ToList();
                if (radius > 0f && AI_Manager.instance != null && bearer.GetMover() != null && bearer.GetMover().GetCurrentNavNode() != null)
                {
                    var hit = AI_Manager.instance.GetEnemiesAt(bearer.GetMover().GetCurrentNavNode().transform.position, radius);
                    if (bearer != Player.instance && !TileStacks.IsAlly(bearer) && !hit.Contains(bearer)) hit.Insert(0, bearer); // your own summons don't blow themselves up
                    if (self.Count == 0) HitLane.Each(hit, rest, c);
                    else foreach (Character ch in hit) Actions.Run(rest, c, ch);
                }
                else Actions.Run(rest, c, bearer);
                Actions.Run(self, c, c.caster);
            }
            finally { burstDepth--; }
        }

        /// <summary>Called by Actions after a fresh application.</summary>
        internal void OnApplied(ActionContext ctx)
        {
            Bind();
            if (rt == null || target == null) return;
            if (!string.IsNullOrEmpty(rt.spec.group))
                foreach (Effect other in target.effects.GetEffects().ToList())
                    if (other != this && other is ModEffect m && m.rt != null && m.rt.spec.group == rt.spec.group) other.RemoveInstant();
            RunGroup(rt.spec.onApplyActions, 0f, null, null, null, ctx);
            RefreshShares();
            if (target == Player.instance) DashCam.Showcase(rt.spec.thirdPerson);
            else StartAura();
        }

        private GameObject aura;

        /// <summary>The effect's "aura": its one-shot layers set looping, on the bearer until the effect goes.</summary>
        private void StartAura()
        {
            GameObject prefab = string.IsNullOrEmpty(rt.spec.aura) ? null : Actions.ResolveVfx(rt.spec.aura);
            if (prefab == null || aura != null) return;
            aura = Instantiate(prefab, target.GetHitFx(), false);
            foreach (ParticleSystem ps in aura.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = true;
                main.playOnAwake = true;
            }
            aura.SetActive(true);
        }

        private bool expiring;

        protected override void Expired_Duration()
        {
            Bind();
            if (rt != null && !expiring && target != null && !target.GetIsDead() && (rt.spec.onExpireActions?.Count ?? 0) > 0)
            {
                expiring = true;
                RunGroup(rt.spec.onExpireActions, rt.spec.onExpireRadius, null, null, $"{GetName()} comes due!", null);
            }
            base.Expired_Duration();
        }

        /// <summary>The bearer is dying (still alive for one more moment): run onDeathActions.</summary>
        internal void OnBearerDying()
        {
            Bind();
            if (rt == null || (rt.spec.onDeathActions?.Count ?? 0) == 0 || target == null) return;
            Character bearer = target;
            var ctx = new ActionContext { caster = caster != null ? caster : Player.instance, victim = bearer, classId = rt.classId, sourceName = GetName(), stored = Taken };
            try { Actions.Run(rt.spec.onDeathActions, ctx, bearer); }
            catch (Exception e) { Plugin.Log.LogError($"{GetName()} on death: {e}"); }
        }

        [HarmonyLib.HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_BearerDies
        {
            private static void Prefix(Character __instance)
            {
                if (__instance == null || __instance.GetIsDead() || __instance.effects == null) return;
                foreach (Effect e in __instance.effects.GetEffects().ToList())
                    if (e is ModEffect m) m.OnBearerDying();
            }
        }

        /// <summary>On the player this rests on the [Fixes] PlayerImmortalTag switch: off, the effect protects nothing.</summary>
        internal bool Invulnerable { get { Bind(); return rt != null && rt.spec.invulnerable && (!(target is Player) || BugFixes.ImmortalPlayer.Value); } }

        /// <summary>Lethal-damage check from the DamageHealth patch. True if this effect saved the bearer.</summary>
        internal bool TrySave(ref int dmg)
        {
            Bind();
            if (rt == null || rt.spec.preventDeath <= 0 || target == null) return false;
            if (target.health - dmg > 0) return false;
            if (rt.spec.preventDeathWhen != null
                && !When.Check(rt.spec.preventDeathWhen, new ActionContext { caster = target, classId = rt.classId, sourceName = GetName() }, target)) return false;
            dmg = Mathf.Max(0, target.health - rt.spec.preventDeath);
            Game.LOG($"<color=#FFD15C>{GetName()} keeps {target.GetName()} alive!</color>");
            var keep = target;
            RemoveInstant();
            keep.CalculateStats();
            if (keep.health - dmg < rt.spec.preventDeath) dmg = Mathf.Max(0, keep.health - rt.spec.preventDeath);
            var ctx = new ActionContext { caster = keep, classId = rt.classId, sourceName = GetName() };
            Actions.Run(rt.spec.onSaveActions, ctx, keep);
            return true;
        }

        private void ScaleToStacks()
        {
            EffectSpec s = rt.spec;
            int k = MaxStacks > 1 ? Stacks : 1;
            if ((s.stats?.Count ?? 0) + (s.resists?.Count ?? 0) + (s.statsFrom?.Count ?? 0) + attributeMods.Count + resistanceMods.Count > 0)
            {
                var old = attributeMods;
                attributeMods = (s.stats ?? new Dictionary<string, int>())
                    .Select(kv => Enum.TryParse(kv.Key, true, out STAT_TYPE t) ? new StatObj(t, kv.Value * k) : null)
                    .Where(x => x != null).ToList();
                attributeMods.AddRange(StatsFromShare(s, k, old));
                resistanceMods = (s.resists ?? new Dictionary<string, int>())
                    .Select(kv => Enum.TryParse(kv.Key, true, out DAMAGE_TYPE t) ? new DmgObj(t, kv.Value * k) : null)
                    .Where(x => x != null).ToList();
            }
            if (target != null && !target.GetIsDead() && !BuffLane.MovesNothing(this)) target.CalculateStats();
            if (target == Player.instance && Menu_HUD.instance != null) Menu_HUD.instance.UpdatePlayerEffects();
        }

        /// <summary>
        /// "statsFrom" on an effect: stats worth a share of another stat of whoever applied it (the bearer for a buff on
        /// yourself), per stack. What this effect itself already adds is left out, so a stat can't feed on itself.
        /// </summary>
        private List<StatObj> StatsFromShare(EffectSpec s, int k, List<StatObj> mine)
        {
            var add = new List<StatObj>();
            Character from = caster != null && !caster.GetIsDead() ? caster : target;
            if (s.statsFrom == null || from == null) return add;
            foreach (StatFromSpec f in s.statsFrom)
            {
                if (f?.from == null || !Enum.TryParse(f.stat, true, out STAT_TYPE to)) continue;
                int total = 0;
                foreach (string name in f.from.Split('+'))
                    if (Enum.TryParse(name.Trim(), true, out STAT_TYPE st))
                        total += from.GetStat(st) - (from == target ? (mine ?? new List<StatObj>()).Where(x => x != null && x.statType == st).Sum(x => x.amount) : 0);
                int amount = Math.Max(0, total) / Math.Max(1, f.per) * k;
                if (amount != 0) add.Add(new StatObj(to, amount));
            }
            return add;
        }

        /// <summary>Re-reads statsFrom shares (the source's stats move with gear and other effects).</summary>
        private void RefreshShares()
        {
            if (rt?.spec.statsFrom == null || rt.spec.statsFrom.Count == 0 || target == null || target.GetIsDead()) return;
            int k = MaxStacks > 1 ? Stacks : 1;
            var fresh = (rt.spec.stats ?? new Dictionary<string, int>())
                .Select(kv => Enum.TryParse(kv.Key, true, out STAT_TYPE t) ? new StatObj(t, kv.Value * k) : null)
                .Where(x => x != null).ToList();
            fresh.AddRange(StatsFromShare(rt.spec, k, attributeMods));
            if (fresh.Count == attributeMods.Count && fresh.Zip(attributeMods, (a, b) => a.statType == b.statType && a.amount == b.amount).All(x => x)) return;
            attributeMods = fresh;
            target.CalculateStats();
        }

        private void Burst(ActionContext ctx)
        {
            if (burstDepth > 3 || target == null) return;
            EffectSpec s = rt.spec;
            Character bearer = target;
            var bctx = new ActionContext
            {
                caster = caster != null ? caster : (ctx?.caster ?? Player.instance),
                source = ctx?.source,
                classId = rt.classId,
                sourceName = GetName(),
                rank = ctx?.rank ?? 0,
            };
            burstDepth++;
            try
            {
                RemoveInstant(); // gone before the burst so chained re-applies start fresh
                BuffLane.AfterRemoved(bearer);
                // a burst on you is gone before OnApplied could ask for its shot
                if (bearer == Player.instance) DashCam.Showcase(s.thirdPerson);
                Vector3 at = bearer.GetHitFx().position;
                ModAudio.Play(s.burstSound, at, rt.classId);
                Actions.SpawnVfx(s.burstVfx, at);
                Game.LOG($"<color=#FFD15C>{GetName()} bursts on {bearer.GetName()}!</color>");
                var self = (s.burstActions ?? new List<ActionSpec>()).Where(a => (a.to ?? "").ToLowerInvariant() == "self").ToList();
                var rest = (s.burstActions ?? new List<ActionSpec>()).Except(self).ToList();
                if (s.burstRadius > 0f && AI_Manager.instance != null && bearer.GetMover() != null && bearer.GetMover().GetCurrentNavNode() != null)
                {
                    var hit = AI_Manager.instance.GetEnemiesAt(bearer.GetMover().GetCurrentNavNode().transform.position, s.burstRadius);
                    if (bearer != Player.instance && !TileStacks.IsAlly(bearer) && !hit.Contains(bearer)) hit.Insert(0, bearer); // your own summons don't blow themselves up
                    if (self.Count == 0) HitLane.Each(hit, rest, bctx);
                    else foreach (Character c in hit) Actions.Run(rest, bctx, c);
                }
                else if (bearer != Player.instance) Actions.Run(rest, bctx, bearer);
                Actions.Run(self, bctx, bctx.caster);
            }
            finally { burstDepth--; }
        }

        private void Awake() => Bind();

        internal void Bind()
        {
            if (bound) return;
            string key = gameObject.name.Replace("(Clone)", "").Trim();
            if (!Specs.TryGetValue(key, out rt)) return;
            bound = true;
            EffectSpec s = rt.spec;
            internalName = rt.id;
            displayName = rt.name;
            description = rt.desc;
            icon = rt.icon;
            isDebuff = s.debuff;
            isStackable = true; // refresh-vs-stack is decided in Actions
            duration = s.duration;
            spawnParticle = rt.particle;
            attributeMods = (s.stats ?? new Dictionary<string, int>())
                .Select(kv => Enum.TryParse(kv.Key, true, out STAT_TYPE t) ? new StatObj(t, kv.Value) : null)
                .Where(x => x != null).ToList();
            resistanceMods = (s.resists ?? new Dictionary<string, int>())
                .Select(kv => Enum.TryParse(kv.Key, true, out DAMAGE_TYPE t) ? new DmgObj(t, kv.Value) : null)
                .Where(x => x != null).ToList();
            if (chargeCount < 1) chargeCount = 1;
            OnApply_Apply ??= new string[0];
            OnApply_Purge ??= new string[0];
            OnExpire_Apply ??= new string[0];
            OnApply_Refresh ??= new string[0];
        }

        public override void Init(Character c)
        {
            Bind();
            base.Init(c);
            Root();
            Resize();
        }

        // Several size effects can overlap and end in any order, so a model's true size is kept apart from them.
        private class Sized { public Vector3 full; public readonly List<ModEffect> by = new List<ModEffect>(); }
        private static readonly Dictionary<Transform, Sized> sized = new Dictionary<Transform, Sized>();
        private Transform resized;

        private void Resize()
        {
            if (rt == null || rt.spec.modelScale <= 0f || resized != null) return;
            resized = target?.ai?.model;
            if (resized == null) return;
            if (!sized.TryGetValue(resized, out Sized s)) sized[resized] = s = new Sized { full = resized.localScale };
            s.by.Add(this);
            resized.localScale = s.full * s.by.Aggregate(1f, (k, m) => k * m.rt.spec.modelScale);
        }

        private void Unresize()
        {
            if (resized == null || !sized.TryGetValue(resized, out Sized s)) return;
            s.by.Remove(this);
            resized.localScale = s.full * s.by.Aggregate(1f, (k, m) => k * m.rt.spec.modelScale);
            if (s.by.Count == 0) sized.Remove(resized);
            resized = null;
        }

        /// <summary>Set while this effect is what keeps its bearer rooted, so only it lets go.</summary>
        private bool rooting;

        private void Root()
        {
            if (rt == null || !rt.spec.root || rooting || target == null || target.FLAG_isRooted) return;
            if (target.HasTag("FLYING") || target.HasTag("IMMUNE_ROOT")) return;
            target.FLAG_isRooted = true;
            rooting = true;
        }

        private void Unroot()
        {
            if (rooting && target != null) target.FLAG_isRooted = false;
            rooting = false;
        }

        protected override void OnDestroy()
        {
            Unroot();
            Unresize();
            if (aura != null) Destroy(aura);
            base.OnDestroy();
        }

        private int DotBonus(EffectSpec s) => Mathf.RoundToInt(Actions.StatBonus(s.dot.scale, s.dot.scaleMult, caster != null ? caster : target));

        private int HotBonus(EffectSpec s) => Mathf.RoundToInt(Actions.StatBonus(s.hotScale, s.hotScaleMult, caster != null ? caster : target));

        public override void TurnOver_Instant()
        {
            Bind();
            if (rt != null && !isFirstTurn && target != null && !target.GetIsDead())
            {
                EffectSpec s = rt.spec;
                if (s.dot != null && Enum.TryParse(s.dot.type, true, out DAMAGE_TYPE dt))
                {
                    int dmg = (Random.Range(s.dot.min, Mathf.Max(s.dot.min, s.dot.max) + 1) + DotBonus(s)) * (MaxStacks > 1 ? Stacks : 1);
                    target.TakeDamage(caster != null && caster != target ? caster : null, new List<DmgObj> { new DmgObj(dt, dmg) }, $"{GetName()} afflicts {target.GetName()}", null, 0f);
                }
                if (s.hot > 0 && target != null && !target.GetIsDead()) target.Heal(s.hot + HotBonus(s), false);
                if (s.staminaPerTurn != 0 && target != null) target.ModStamina(s.staminaPerTurn);
                RefreshShares();
                // stacks scale it through the actions' own bonusPerStack + stacksOf
                if ((s.tickActions?.Count ?? 0) > 0 && target != null && !target.GetIsDead())
                    RunGroup(s.tickActions, s.tickRadius, null, null, null, null);
            }
            base.TurnOver_Instant();
        }

        public override List<DmgObj> OnDealDamage(Ability ability, Character victim, List<DmgObj> dmgList, Item_Weapon attackerWeapon, float critDmgMod)
        {
            Bind();
            if (rt != null && DealtMult != 1f) ScaleWhere(dmgList, Mathf.Max(0f, DealtMult), rt.spec.dealtWith, ability);
            if (rt != null && critDmgMod > 0f)
            {
                float extra = rt.spec.critDamage + rt.spec.critDamagePerStack * (MaxStacks > 1 ? Stacks : 1);
                if (extra != 0f) Scale(dmgList, Mathf.Max(0f, (1f + critDmgMod + extra) / (1f + critDmgMod)));
            }
            return dmgList;
        }

        public override List<DmgObj> OnTakeDamage(Character victim, List<DmgObj> dmgList, string attackString, Ability dmgSource, float critDmgMod)
        {
            Bind();
            if (rt != null && HazardProof(dmgSource)) { Scale(dmgList, 0f); return dmgList; }
            if (rt != null && TakenMult != 1f) ScaleWhere(dmgList, Mathf.Max(0f, TakenMult), rt.spec.takenFrom, dmgSource);
            if (rt != null && rt.spec.redirect > 0f) Redirect(dmgList);
            if (rt != null && rt.spec.breakOnHurt && !breaking && dmgList.Any(d => d != null && d.damage > 0))
            {
                breaking = true;
                Plugin.Instance.StartCoroutine(Break());
            }
            return dmgList;
        }

        private bool breaking;

        /// <summary>"breakOnHurt": ends once the hit has landed, not in the middle of the game's walk over the bearer's effects.</summary>
        private System.Collections.IEnumerator Break()
        {
            yield return null;
            Character bearer = target;
            if (rt == null || bearer == null || bearer.effects == null || bearer.effects.GetEffect(internalName) != this) yield break;
            Game.LOG($"<color=#FFD15C>{GetName()} breaks.</color>");
            RunGroup(rt.spec.onBreakActions, 0f, null, null, null, null);
            bearer.effects.RemoveEffect(internalName, true);
            if (!bearer.GetIsDead()) bearer.CalculateStats();
            if (bearer == Player.instance && Menu_HUD.instance != null) Menu_HUD.instance.UpdatePlayerEffects();
        }

        private static bool redirecting;

        /// <summary>
        /// A bodyguard's cover: part of each hit the bearer takes goes to whoever put the effect on (if alive and not the
        /// bearer), or with "redirectTo" to one of that side's summons. Note the game hands OnTakeDamage the attacker, not
        /// the bearer; the bearer is this effect's target.
        /// </summary>
        private void Redirect(List<DmgObj> dmgList)
        {
            Character guard = string.IsNullOrEmpty(rt.spec.redirectTo) ? caster : Guard(), victim = target;
            if (redirecting || guard == null || guard.GetIsDead() || victim == null || guard == victim) return;
            float part = Mathf.Clamp01(rt.spec.redirect);
            var moved = new List<DmgObj>();
            foreach (DmgObj d in dmgList)
            {
                if (d == null || d.damage <= 0) continue;
                int take = Mathf.RoundToInt(d.damage * part);
                if (take <= 0) continue;
                d.damage -= take;
                moved.Add(new DmgObj(d.dmgType, take));
            }
            if (moved.Count == 0) return;
            redirecting = true;
            try { guard.TakeDamage(null, moved, $"{guard.GetName()} takes the hit for {victim.GetName()}", null, 0f); }
            finally { redirecting = false; }
        }

        /// <summary>"hazardProof": a hazard hurts through a bare Ability it adds to itself, so the hit's source tells which one it was.</summary>
        private bool HazardProof(Ability src)
        {
            var proof = rt.spec.hazardProof;
            if (proof == null || proof.Count == 0 || src == null) return false;
            Hazard hz = src.GetComponent<Hazard>();
            if (hz == null) return false;
            string id = string.IsNullOrEmpty(hz.internalName) ? hz.name.Replace("(Clone)", "").Trim() : hz.internalName;
            return proof.Any(p => p.Equals("all", StringComparison.OrdinalIgnoreCase) || id.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>"redirectTo": the matching summon of yours nearest the bearer. Your summons only stand guard for your side.</summary>
        private Character Guard()
        {
            if (target == null || caster == null || (caster != Player.instance && !TileStacks.IsAlly(caster))) return null;
            string which = rt.spec.redirectTo.Equals("summon", StringComparison.OrdinalIgnoreCase) ? "any" : rt.spec.redirectTo;
            Vector3 at = target.transform.position;
            return Summons.Matching(which, new ActionContext { caster = caster, classId = rt.classId })
                .Where(s => s != target).OrderBy(s => (s.transform.position - at).sqrMagnitude).FirstOrDefault();
        }

        private static void Scale(List<DmgObj> list, float m)
        {
            foreach (DmgObj d in list)
                if (d != null && d.damage > 0) d.damage = Mathf.Max(m < 1f ? 0 : 1, Mathf.RoundToInt(d.damage * m));
        }

        /// <summary>Scale, but only the parts of a hit "dealtWith" / "takenFrom" names; unset, all of it.</summary>
        private static void ScaleWhere(List<DmgObj> list, float m, string filter, Ability by)
        {
            if (string.IsNullOrEmpty(filter)) { Scale(list, m); return; }
            foreach (DmgObj d in list)
                if (d != null && d.damage > 0 && Hits(filter, by, d)) d.damage = Mathf.Max(m < 1f ? 0 : 1, Mathf.RoundToInt(d.damage * m));
        }

        /// <summary>"melee" and "ranged" ask the skill or attack the hit came from (none for damage over time or a hazard);
        /// any other word is a damage type.</summary>
        private static bool Hits(string filter, Ability by, DmgObj d)
        {
            foreach (string w in filter.Split('|'))
            {
                string k = w.Trim();
                if (k.Equals("melee", StringComparison.OrdinalIgnoreCase)) { if (by != null && by.isMelee) return true; }
                else if (k.Equals("ranged", StringComparison.OrdinalIgnoreCase)) { if (by != null && by.isRanged && !by.isMelee) return true; }
                else if (k.Equals("attack", StringComparison.OrdinalIgnoreCase)) { if (by is PA_AutoAttack) return true; }
                else if (k.Equals("skill", StringComparison.OrdinalIgnoreCase)) { if (by != null && !(by is PA_AutoAttack)) return true; }
                else if (k.Equals("offhand", StringComparison.OrdinalIgnoreCase)) { if (Offhand(by) == true) return true; }
                else if (k.Equals("mainhand", StringComparison.OrdinalIgnoreCase)) { if (Offhand(by) == false) return true; }
                else if (Enum.TryParse(k, true, out DAMAGE_TYPE t) && d.dmgType == t) return true;
            }
            return false;
        }

        /// <summary>Which hand a hit came from: the auto-attack's swing (its off-hand pass runs on the left hand's animator),
        /// or the weapon a mod skill swings. Null when no weapon swung it.</summary>
        private static bool? Offhand(Ability by)
        {
            if (by is PA_AutoAttack aa) return Player_Control.instance != null && aa.Handimator != null && aa.Handimator == Player_Control.instance.Handimator_L;
            if (by is ModAbility m && m.Spec != null && (m.Spec.actions ?? new List<ActionSpec>()).Any(a => a != null && (a.weapon > 0f || a.channel)) && Player.instance != null)
                return Equipped.Hand(m.Spec.equipped, Player.instance, out bool off) != null ? off : (bool?)null;
            return null;
        }

        /// <summary>Words for a "dealtWith" / "takenFrom" filter: " with melee hits", " from Fire or Cryo".</summary>
        private static string HitWords(string filter, bool dealt)
        {
            if (string.IsNullOrEmpty(filter)) return "";
            string Word(string w)
            {
                switch (w.ToLowerInvariant())
                {
                    case "melee": case "ranged": return w.ToLowerInvariant() + " hits";
                    case "attack": return "weapon attacks";
                    case "skill": return "skills";
                    case "offhand": return "off-hand swings";
                    case "mainhand": return "main-hand swings";
                    default: return w;
                }
            }
            var parts = filter.Split('|').Select(w => Word(w.Trim())).ToList();
            return (dealt ? " with " : " from ") + string.Join(" or ", parts);
        }

        internal float OffhandPower { get { Bind(); return (rt?.spec.offhandPower ?? 0f) * Math.Max(1, Stacks); } }

        public override void Clear()
        {
            Character t = target;
            Unroot();
            base.Clear();
            if (t != null && !t.GetIsDead()) BuffLane.AfterRemoved(t);
        }

        public override void Deserialize(object d)
        {
            base.Deserialize(d);
            Bind();
            if (rt != null) { icon = rt.icon; ScaleToStacks(); Root(); }
        }

        public override string GetDescription()
        {
            Bind();
            if (rt == null) return base.GetDescription();
            EffectSpec s = rt.spec;
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(s.description)) lines.Add(s.description);
            if (MaxStacks > 1) lines.Add($"<color=#FFD15C>Stacks: {Stacks}/{MaxStacks}</color> (values below are for the current stacks)");
            if (s.capScale != null) lines.Add(rt.CapLine());
            foreach (StatObj st in attributeMods) lines.Add($"{(st.amount >= 0 ? "+" : "")}{st.amount} {StatObj.GetStatString(st.statType)}");
            foreach (DmgObj r in resistanceMods) lines.Add($"{(r.damage >= 0 ? "+" : "")}{r.damage} {Game.GetSpriteString(r.dmgType, isResist: true)} resistance");
            if (s.dot != null && Enum.TryParse(s.dot.type, true, out DAMAGE_TYPE dt)) lines.Add($"Takes {s.dot.min + DotBonus(s)}-{s.dot.max + DotBonus(s)} {Game.GetSpriteString(dt)} each turn");
            if (s.hot > 0) lines.Add($"Heals {s.hot + HotBonus(s)} each turn");
            if (s.staminaPerTurn != 0) lines.Add($"{s.staminaPerTurn:+#;-#} {StatObj.GetStatString(STAT_TYPE.Stamina)} each turn");
            if (DealtMult != 1f) lines.Add($"Deals {Mathf.RoundToInt((DealtMult - 1f) * 100):+#;-#}% damage{HitWords(s.dealtWith, true)}");
            if (TakenMult != 1f) lines.Add($"Takes {Mathf.RoundToInt((TakenMult - 1f) * 100):+#;-#}% damage{HitWords(s.takenFrom, false)}");
            if (OffhandPower != 0f) lines.Add($"Off-hand swings deal {(OffhandPower > 0f ? "an extra " : "")}{Mathf.RoundToInt(Mathf.Abs(OffhandPower) * 100)}% of the weapon's roll {(OffhandPower > 0f ? "on top of" : "less than")} the usual 50%");
            if (s.breakOnHurt) lines.Add("<b>Breaks when the bearer is hurt</b>" + ((s.onBreakActions?.Count ?? 0) > 0 ? ":\n" + Actions.Describe(s.onBreakActions, rt.classId, "the bearer") : "."));
            if ((s.hazardProof?.Count ?? 0) > 0) lines.Add(s.hazardProof.Any(p => p.Equals("all", StringComparison.OrdinalIgnoreCase)) ? "No harm from hazards"
                : "No harm from " + string.Join(", ", s.hazardProof.Select(p => p.Replace("HAZARD_", "").Replace('_', ' ').ToLowerInvariant())));
            if (s.redirect > 0f) lines.Add($"{Mathf.RoundToInt(Mathf.Clamp01(s.redirect) * 100)}% of each hit goes to {(string.IsNullOrEmpty(s.redirectTo) ? "whoever put this on" : "the nearest of your summons")}");
            foreach (ProcSpec p in s.procs ?? new List<ProcSpec>()) lines.Add(Actions.DescribeProc(p, rt.classId, ProcChanceBonus));
            foreach (ThresholdSpec th in s.thresholds ?? new List<ThresholdSpec>())
                lines.Add($"<b>At {th.at} stacks:</b>\n" + Actions.Describe(th.actions, rt.classId, th.radius > 0 ? $"enemies within {th.radius:0.#}" : "the bearer"));
            if ((s.onApplyActions?.Count ?? 0) > 0) lines.Add("<b>When applied:</b>\n" + Actions.Describe(s.onApplyActions, rt.classId, "the bearer"));
            if ((s.tickActions?.Count ?? 0) > 0) lines.Add("<b>Each turn:</b>\n" + Actions.Describe(s.tickActions, rt.classId, s.tickRadius > 0 ? $"enemies within {s.tickRadius:0.#}" : "the bearer"));
            if ((s.onDeathActions?.Count ?? 0) > 0) lines.Add("<b>If the bearer dies with it:</b>\n" + Actions.Describe(s.onDeathActions, rt.classId, "beside the bearer"));
            if ((s.onExpireActions?.Count ?? 0) > 0) lines.Add("<b>When it runs out:</b>\n" + Actions.Describe(s.onExpireActions, rt.classId, s.onExpireRadius > 0 ? $"enemies within {s.onExpireRadius:0.#}" : "the bearer"));
            if (s.invulnerable) lines.Add("<b>Can't lose health</b>");
            if (s.preventDeath > 0) lines.Add($"<b>Prevents death once</b> (survive at {s.preventDeath} health{(s.preventDeathWhen != null ? ", only when " + When.Describe(s.preventDeathWhen, rt.classId) : "")})" + ((s.onSaveActions?.Count ?? 0) > 0 ? ":\n" + Actions.Describe(s.onSaveActions, rt.classId, "you") : "."));
            if (!string.IsNullOrEmpty(s.group)) lines.Add($"<color=#9FB7C9>Stance: replaces other {s.group} stances.</color>");
            if (s.castRules != null) lines.Add(CastRules.Describe(s.castRules));
            string reshapes = SkillMods.Summary(s.modifies, rt.classId);
            if (reshapes.Length > 0) lines.Add(reshapes);
            if (s.burstAt > 0)
            {
                string who = s.burstRadius > 0 ? $"enemies within {s.burstRadius:0.#}" : "the bearer";
                lines.Add($"<b>At {s.burstAt} stacks it bursts:</b>\n" + Actions.Describe(s.burstActions, rt.classId, who));
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// One or two lines for the effect rows in a character's hover tip (the row already lists stat changes):
        /// stacks and damage dealt and taken, or the opening words when it has no numbers. The effect icon's own tip has everything.
        /// </summary>
        internal string ShortDescription()
        {
            Bind();
            if (rt == null) return "";
            var parts = new List<string>();
            if (MaxStacks > 1) parts.Add($"<color=#FFD15C>x{Stacks}/{MaxStacks}</color>");
            if (DealtMult != 1f) parts.Add($"deals {Mathf.RoundToInt((DealtMult - 1f) * 100):+#;-#}%{HitWords(rt.spec.dealtWith, true)}");
            if (TakenMult != 1f) parts.Add($"takes {Mathf.RoundToInt((TakenMult - 1f) * 100):+#;-#}%{HitWords(rt.spec.takenFrom, false)}");
            // the words only when there are no numbers to go on (stat changes are already on the row above)
            bool numbers = parts.Count > (MaxStacks > 1 ? 1 : 0) || attributeMods.Count > 0 || resistanceMods.Count > 0;
            string d = numbers ? "" : rt.spec.description ?? "";
            int stop = d.IndexOfAny(new[] { '.', ':', ';' });
            if (stop > 0) d = d.Substring(0, stop + 1);
            if (d.Length > 60) d = d.Substring(0, 57).TrimEnd() + "...";
            if (d.Length > 0) parts.Add($"<color=#C9D6E0>{d}</color>");
            return string.Join("  ", parts);
        }

        internal List<ProcSpec> Procs { get { Bind(); return rt?.spec.procs; } }

        internal int ProcChanceBonus { get { Bind(); return (rt?.spec.procChancePerStack ?? 0) * Math.Max(0, Stacks - 1); } }

        internal void HandleProc(string on, Character other)
        {
            Bind();
            if (rt?.spec.procs == null || target == null || target.GetIsDead()) return;
            // On a summon the procs run as that summon ("to": "self" is the summon; its hits are what triggered them).
            var ctx = new ActionContext { caster = target, classId = rt.classId, sourceName = GetName(), rank = rt.spec.rankFromStacks ? Stacks : 0, chanceBonus = ProcChanceBonus };
            foreach (ProcSpec p in rt.spec.procs)
                if (string.Equals(p.on, on, StringComparison.OrdinalIgnoreCase))
                    ProcHub.RunProc(p, ctx, on, other);
        }
    }
}

namespace SCClassLoader
{
    /// <summary>Every mod effect tallies the health its bearer actually loses while it is on (ModEffect.Taken), so an
    /// effect's expiry, death or burst actions can hit back with a share of it ("stored").</summary>
    internal static class DamageTally
    {
        /// <summary>Health the player has lost in all (tests read it where the health itself is kept topped up).</summary>
        internal static int PlayerLost;

        internal static void Add(Character c, int before)
        {
            if (c == null) return;
            int lost = before - Mathf.Max(0, c.health);
            if (lost <= 0) return;
            if (c == Player.instance) PlayerLost += lost;
            List<Effect> all = c.effects.effects;
            for (int i = 0; i < all.Count; i++)
                if (all[i] is ModEffect me && me != null) me.Taken += lost;
        }

        [HarmonyLib.HarmonyPatch(typeof(Character), nameof(Character.DamageHealth), new[] { typeof(int), typeof(Ability), typeof(bool) })]
        private static class Patch_Character
        {
            private static void Prefix(Character __instance, out int __state) => __state = __instance != null ? __instance.health : 0;
            private static void Postfix(Character __instance, int __state) => Add(__instance, __state);
        }

        /// <summary>Enemies override the two-argument one with their own health change.</summary>
        [HarmonyLib.HarmonyPatch(typeof(Mob), nameof(Mob.DamageHealth), new[] { typeof(int), typeof(Ability) })]
        private static class Patch_Mob
        {
            private static void Prefix(Mob __instance, out int __state) => __state = __instance != null ? __instance.health : 0;
            private static void Postfix(Mob __instance, int __state) => Add(__instance, __state);
        }

        /// <summary>The player's own override never reaches the three-argument one.</summary>
        [HarmonyLib.HarmonyPatch(typeof(Player), nameof(Player.DamageHealth), new[] { typeof(int), typeof(Ability) })]
        private static class Patch_Player
        {
            private static void Prefix(Player __instance, out int __state) => __state = __instance != null ? __instance.health : 0;
            private static void Postfix(Player __instance, int __state) => Add(__instance, __state);
        }
    }
}
