using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SCClassLoader
{
    /// <summary>
    /// A brand-new enemy skill ("skills" on a mod enemy, or in "enemyPatches" for vanilla ones). It runs the same
    /// actions as class abilities, with the enemy as the caster. "fromClass": "MOD_Class/Ability Name" borrows a mod
    /// class ability's actions instead of listing them.
    /// </summary>
    public class MobSkillSpec
    {
        public string id, name, description;
        /// <summary>What the log says, after the enemy's name: "hurls a bottle at" (the target follows).</summary>
        public string verb;
        public string fromClass;
        /// <summary>enemy (who the AI is fighting), self, ally (the most hurt ally in range), allies (every ally within
        /// radius of the caster), area (everyone on the other side within radius of the target).</summary>
        public string target = "enemy";
        public float range = 1.5f, radius;
        /// <summary>"cone" / "line": hits every foe in that shape from the caster toward its target, out to range
        /// (a line pierces), instead of one target or a round area.</summary>
        public string shape;
        public float width = 1f, angle = 60f;
        public int cooldown = 3, priority = 5;
        /// <summary>Times it can be used per enemy (0 = no limit).</summary>
        public int uses;
        /// <summary>Chance each turn that the AI considers it at all.</summary>
        public float chance = 1f;
        public float healthBelow = 1.01f, healthAbove;
        /// <summary>Needs at least this many allies within radius (or 6 tiles).</summary>
        public int alliesNear;
        public WhenSpec when;
        public bool melee;
        /// <summary>Rolls to hit against the target's dodge.</summary>
        public bool dodgeable;
        public int accuracy = 90;
        /// <summary>Animator state on the base enemy ("Melee", "Ranged", ...); missing states are skipped.</summary>
        public string anim;
        /// <summary>The clip the creature's own model plays ("attack2", "special", any clip name); default "attack".</summary>
        public string clip;
        public string castVfx, impactVfx, sound;
        /// <summary>Turns spent winding up first (a visible countdown in the log); "grow" swells the enemy meanwhile.</summary>
        public int windup;
        public string windupText;
        public bool grow;
        /// <summary>An effect on the caster while it winds up (so its hover tip shows what's coming).</summary>
        public string windupEffect;
        /// <summary>"area" centred on the caster instead of its target (blasts, bursts).</summary>
        public bool aroundSelf;
        /// <summary>MeleePower, RangedPower or PsiPower scale the damage (default: melee for melee skills, else ranged).</summary>
        public string power;
        /// <summary>Extra damage per floor below the first.</summary>
        public float floorScale = 0.15f;
        public List<ActionSpec> actions = new List<ActionSpec>();
        public List<ActionSpec> selfActions = new List<ActionSpec>();
        public MobSpawnSpec spawn;
        /// <summary>The caster dies once it's done (self-destructs, spores that burst).</summary>
        public bool die;

        [JsonIgnore] internal string key, classId, packId;
    }

    /// <summary>Enemies the skill brings in, on the caster's side.</summary>
    public class MobSpawnSpec
    {
        public List<string> ids = new List<string>();
        public int count = 1;
        /// <summary>At most this many of its spawns alive at once.</summary>
        public int max = 4;
        public float health = 1f;
        public string name;
        /// <summary>Copies of the caster itself (decoys), with "health" of its max.</summary>
        public bool copies;
    }

    /// <summary>Vanilla (or other mods') enemies that get extra skills: all of them, or "chance" of each spawn.</summary>
    public class EnemyPatchSpec
    {
        public List<string> enemies = new List<string>();
        public float chance = 1f;
        public int minFloor, maxFloor = 99;
        /// <summary>Name prefix for the ones that got it ("Rigged" CorpSec Guard).</summary>
        public string prefix;
        /// <summary>Only enemies spawned inside a side area get it (the default), so Chimera's own floors stay as they are.</summary>
        public bool onlyInSideAreas = true;
        public List<MobSkillSpec> skills = new List<MobSkillSpec>();
        [JsonIgnore] internal string key;
    }

    /// <summary>The component: added to enemy prefabs (or live enemies), configured by key so clones keep working.</summary>
    public class ModMobAbility : Ability_Mob
    {
        public string specKey;
        public int usesDone;
        public int windLeft = -1;
        private Vector3 baseScale;

        internal MobSkillSpec Spec => specKey != null && EnemyAbilities.Specs.TryGetValue(specKey, out MobSkillSpec s) ? s : null;

        public override void Init()
        {
            base.Init();
            MobSkillSpec s = Spec;
            if (s == null) return;
            internalName = "SCCLM_" + s.key;
            displayName = s.name ?? s.id;
            cooldown = s.cooldown;
            range = s.range;
            priority = s.priority;
            isMelee = s.melee;
            isRanged = false;
        }

        public override string GetDisplayName() => Spec?.name ?? base.GetDisplayName();

        private int Allies(float r) =>
            caster == null ? 0 : EnemyAbilities.SideOf(caster).Count(c => c != caster && Vector3.Distance(c.transform.position, caster.transform.position) <= r);

        public override bool CanUse()
        {
            MobSkillSpec s = Spec;
            if (s == null || caster == null) return false;
            if (windLeft > 0) return true;
            if (!base.CanUse()) return false;
            if (s.uses > 0 && usesDone >= s.uses) return false;
            float hp = caster.GetHealth() / (float)Math.Max(1, caster.GetHealthMax());
            if (hp >= s.healthBelow || hp < s.healthAbove) return false;
            if (s.alliesNear > 0 && Allies(s.radius > 0 ? s.radius : 6f) < s.alliesNear) return false;
            if (s.target == "ally" && EnemyAbilities.HurtAlly(caster, s.range) == null) return false;
            if (s.spawn != null && EnemyAbilities.SpawnsAlive(caster) >= s.spawn.max) return false;
            // copies never copy themselves: a budding slime's buds would bud, and so on, until the game stalls
            if (s.spawn != null && (caster.HasTag("CLONE") || caster.tags.Any(t => t?.tagName != null && t.tagName.StartsWith(EnemyAbilities.SpawnTagPrefix)))) return false;
            if (s.when != null && !When.Check(s.when, EnemyAbilities.Context(this, s, target), target)) return false;
            return s.chance >= 1f || Random.value < s.chance;
        }

        public override bool CanUseOn(Character c)
        {
            MobSkillSpec s = Spec;
            if (s == null) return false;
            if (windLeft > 0) return true;
            switch (s.target)
            {
                case "self":
                case "ally":
                case "allies": return true;
                default: return c != null && base.CanUseOn(c);
            }
        }

        public override int GetPriority() => windLeft > 0 ? 999 : priority;

        public override IEnumerator Use()
        {
            MobSkillSpec s = Spec;
            if (s == null || caster == null || caster.GetIsDead()) yield break;
            if (s.windup > 0 && windLeft != 0)
            {
                if (windLeft < 0)
                {
                    windLeft = s.windup;
                    baseScale = caster.transform.localScale;
                    ai?.SetChannelAbility(this); // the AI keeps choosing it, saves keep it, and a stun reaches it
                    if (!string.IsNullOrEmpty(s.windupEffect))
                        Actions.ApplyEffect(new ActionSpec { @do = "effect", effect = s.windupEffect, stacks = 1 }, EnemyAbilities.Context(this, s, target), caster);
                    Game.LOG($"<color=#FF8F5C>{caster.GetName()} {s.windupText ?? "is winding up something nasty"}! ({windLeft})</color>");
                }
                else Game.LOG($"<color=#FF8F5C>{caster.GetName()}: {windLeft}...</color>");
                if (s.grow) caster.transform.localScale = baseScale * (1f + 0.07f * (s.windup - windLeft + 1));
                MobAnimDriver.Trigger(caster, "special");
                windLeft--;
                cooldown_amount = 0;
                yield break;
            }
            if (windLeft == 0) ai?.SetChannelAbility(null);
            windLeft = -1;
            if (s.grow && baseScale != Vector3.zero) caster.transform.localScale = baseScale;
            if (!string.IsNullOrEmpty(s.windupEffect)) caster.effects.RemoveEffect(Actions.EffectId(s.windupEffect, s.classId), true);
            usesDone++;
            yield return StartCoroutine(EnemyAbilities.Execute(this, s, target));
        }

        public override bool CanChannel() => windLeft >= 0 && Spec?.windup > 0;

        /// <summary>A save made mid wind-up: it goes off on its next turn.</summary>
        public override void LoadChannel()
        {
            windLeft = 0;
            baseScale = caster != null ? caster.transform.localScale : Vector3.one;
        }

        public override void IncomingStun()
        {
            base.IncomingStun();
            if (windLeft >= 0) // a stun breaks the wind-up, as in the first game
            {
                windLeft = -1;
                ai?.ChannelInterrupt();
                MobSkillSpec sp = Spec;
                if (sp != null && !string.IsNullOrEmpty(sp.windupEffect)) caster.effects.RemoveEffect(Actions.EffectId(sp.windupEffect, sp.classId), true);
                if (Spec?.grow == true && baseScale != Vector3.zero) caster.transform.localScale = baseScale;
                Game.LOG($"<color=#9FE08A>{caster.GetName()}'s wind-up is broken!</color>");
            }
        }
    }

    internal static class EnemyAbilities
    {
        internal const string EffectClass = "SCCL_Mobs";
        internal static readonly Dictionary<string, MobSkillSpec> Specs = new Dictionary<string, MobSkillSpec>(StringComparer.OrdinalIgnoreCase);
        internal static readonly List<EnemyPatchSpec> Patches = new List<EnemyPatchSpec>();
        private static readonly JsonSerializer Json = new JsonSerializer { ObjectCreationHandling = ObjectCreationHandling.Replace };

        // ---- loading ------------------------------------------------------------------------------------------

        /// <summary>Mob effects ("mobEffects"); before EnemyRegistry builds the mod enemies (and their skills).</summary>
        internal static void LoadEffects()
        {
            Specs.Clear();
            foreach (ModPack p in ModPacks.Ordered)
                foreach (var (file, obj) in p.data)
                {
                    if (!(obj["mobEffects"] is JArray fx) || fx.Count == 0) continue;
                    try
                    {
                        ClassRegistry.RegisterEffects(new ClassDef { id = EffectClass, name = "Enemies", tiers = new List<TierDef>(), folder = p.folder,
                            effects = fx.OfType<JObject>().Select(o => o.ToObject<EffectSpec>(Json)).ToList() }, null);
                    }
                    catch (Exception e) { Plugin.Log.LogError($"{file}: mobEffects: {e.Message}"); }
                }
        }

        /// <summary>Enemy patches ("enemyPatches"); after EnemyRegistry, so they can name mod enemies too.</summary>
        internal static void LoadPatches()
        {
            Patches.Clear();
            foreach (ModPack p in ModPacks.Ordered)
                foreach (var (file, obj) in p.data)
                {
                    int n = 0;
                    foreach (JObject e in (obj["enemyPatches"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        try
                        {
                            EnemyPatchSpec ps = e.ToObject<EnemyPatchSpec>(Json);
                            ps.key = $"{p.id}_patch{n++}";
                            ps.enemies = ps.enemies.Select(id => EnemyRegistry.Ids.TryGetValue(ModPacks.Qualify(p, id), out string mid) ? mid : EnemyRegistry.Ids.TryGetValue(id, out mid) ? mid : id).ToList();
                            foreach (MobSkillSpec s in ps.skills) Prepare(s, p, ps.key);
                            Patches.Add(ps);
                        }
                        catch (Exception ex) { Plugin.Log.LogError($"{file}: an enemy patch could not be read: {ex.Message}"); }
                    }
                }
            // patches for every spawn go straight onto the prefab, so saves and every spawn have them
            foreach (EnemyPatchSpec ps in Patches.Where(OnPrefab))
                foreach (string id in ps.enemies)
                {
                    Character prefab = AI_Manager.instance?.GetCharacter(id);
                    if (prefab == null) { Plugin.Log.LogWarning($"Enemy patch {ps.key}: no enemy '{id}'"); continue; }
                    foreach (MobSkillSpec s in ps.skills) Attach(prefab.gameObject, s);
                    if (!string.IsNullOrEmpty(ps.prefix)) Plugin.Log.LogWarning($"Enemy patch {ps.key}: \"prefix\" only applies to chance-based patches");
                }
            if (Patches.Count > 0) Plugin.Log.LogInfo($"Enemy skills: {Patches.Count} patch(es) for {string.Join(", ", Patches.SelectMany(x => x.enemies).Distinct())}");
        }

        /// <summary>Fills in a skill's key and, for "fromClass", the class ability's actions and look.</summary>
        internal static void Prepare(MobSkillSpec s, ModPack p, string owner)
        {
            s.id = s.id ?? s.name ?? "skill";
            s.key = $"{owner}_{ModPacks.Sanitize(s.id)}";
            s.packId = p?.id;
            s.target = (s.target ?? "enemy").ToLowerInvariant();
            s.classId = EffectClass;
            foreach (ActionSpec a in s.actions.Concat(s.selfActions)) a.@do = (a.@do ?? "").ToLowerInvariant();
            Own(s.actions); Own(s.selfActions);
            s.windupEffect = OwnName(s.windupEffect);
            if (!string.IsNullOrEmpty(s.fromClass))
            {
                int slash = s.fromClass.IndexOf('/');
                string cls = slash > 0 ? s.fromClass.Substring(0, slash) : s.fromClass, ab = slash > 0 ? s.fromClass.Substring(slash + 1) : null;
                ClassDef def = ClassRegistry.AllDefs.FirstOrDefault(d => string.Equals(d.id, cls, StringComparison.OrdinalIgnoreCase));
                AbilitySpec spec = def?.tiers?.Select(t => t.newAbility).FirstOrDefault(a => a != null && (ab == null || string.Equals(a.name, ab, StringComparison.OrdinalIgnoreCase)));
                if (spec == null) Plugin.Log.LogError($"Enemy skill {s.key}: fromClass '{s.fromClass}' not found");
                else
                {
                    if (s.actions.Count == 0) s.actions = spec.actions ?? new List<ActionSpec>();
                    s.name = s.name ?? spec.name;
                    s.castVfx = s.castVfx ?? spec.castVfx;
                    s.impactVfx = s.impactVfx ?? spec.impactVfx;
                    s.sound = s.sound ?? spec.sound;
                    if (s.radius <= 0) s.radius = spec.radius;
                    if (s.target == "enemy" && spec.target == "self") s.target = spec.radius > 0 ? "area" : "self";
                    if (s.range == 1.5f && spec.range > 0) s.range = spec.range;
                    s.melee |= spec.melee;
                    s.classId = def.id; // its effects resolve against the class that owns them
                }
            }
            Specs[s.key] = s;
        }

        /// <summary>Plain effect names that are enemy effects ("mobEffects") get the "@" that marks them as the skill's own.</summary>
        private static string OwnName(string name) =>
            string.IsNullOrEmpty(name) || name.StartsWith("#") || name.Contains("@") || !ModEffect.Specs.ContainsKey(ModEffect.MakeId(EffectClass, name)) ? name : "@" + name;

        private static void Own(IEnumerable<ActionSpec> list)
        {
            foreach (ActionSpec a in list ?? Enumerable.Empty<ActionSpec>())
            {
                if (a == null) continue;
                a.effect = OwnName(a.effect);
                Own(a.then); Own(a.@else); Own(a.actions);
            }
        }

        /// <summary>Anything this mod brings in turns to face you, not the map's north.</summary>
        internal static void FacePlayer(Character c)
        {
            if (c == null || Player.instance == null) return;
            Vector3 d = Player.instance.transform.position - c.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f) c.transform.rotation = Quaternion.LookRotation(d);
        }

        internal static ModMobAbility Attach(GameObject go, MobSkillSpec s)
        {
            if (go.GetComponents<ModMobAbility>().Any(m => m.specKey == s.key)) return null;
            ModMobAbility m = go.AddComponent<ModMobAbility>();
            m.specKey = s.key;
            m.isAvailable = true;
            m.useString = new I2.Loc.LocalizedString[] { "" };
            m.Init();
            return m;
        }

        // ---- using --------------------------------------------------------------------------------------------

        /// <summary>Characters on the caster's side (hostiles for a hostile caster).</summary>
        internal static IEnumerable<Character> SideOf(Character caster)
        {
            AI.AI_TEAM team = caster.ai != null ? caster.ai.GetTeam() : AI.AI_TEAM.Player;
            return Contracts.AllCharacters().Where(c => !c.GetIsDead() && c.ai != null && c.ai.GetTeam() == team);
        }

        internal static IEnumerable<Character> FoesOf(Character caster) => Foes(caster);

        private static IEnumerable<Character> Foes(Character caster)
        {
            AI.AI_TEAM team = caster.ai != null ? caster.ai.GetTeam() : AI.AI_TEAM.Player;
            var list = Contracts.AllCharacters().Where(c => !c.GetIsDead() && c.ai != null && c.ai.GetTeam() != team && c.ai.GetTeam() != AI.AI_TEAM.Passive).ToList();
            if (team != AI.AI_TEAM.Player && Player.instance != null && !Player.instance.GetIsDead()) list.Add(Player.instance);
            return list;
        }

        internal static Character HurtAlly(Character caster, float range) =>
            SideOf(caster).Where(c => c.GetHealth() < c.GetHealthMax() && Vector3.Distance(c.transform.position, caster.transform.position) <= Math.Max(range, 1.5f))
                .OrderBy(c => c.GetHealth() / (float)Math.Max(1, c.GetHealthMax())).FirstOrDefault();

        internal static ActionContext Context(ModMobAbility ab, MobSkillSpec s, Character target)
        {
            Character caster = ab.GetComponent<Character>();
            float mult = 1f + s.floorScale * Math.Max(0, Game.FLOOR);
            STAT_TYPE pw = Enum.TryParse(s.power ?? "", true, out STAT_TYPE st) ? st : s.melee ? STAT_TYPE.MeleePower : STAT_TYPE.RangedPower;
            try
            {
                var probe = caster.ApplyPower(pw, new List<DmgObj> { new DmgObj(DAMAGE_TYPE.Crushing, 100) });
                if (probe != null && probe.Count > 0 && probe[0].damage > 0) mult *= probe[0].damage / 100f;
            }
            catch { }
            return new ActionContext
            {
                caster = caster, target = target, source = ab, classId = s.classId, rank = Mathf.Clamp(Game.FLOOR / 2 + 1, 1, 5),
                sourceName = $"{caster.GetName()}'s {s.name ?? s.id}", damageMult = mult, center = target != null ? target.transform.position : caster.transform.position,
            };
        }

        internal static IEnumerator Execute(ModMobAbility ab, MobSkillSpec s, Character target)
        {
            Character caster = ab.GetComponent<Character>();
            AI ai = caster.ai;
            if (s.target == "ally") target = HurtAlly(caster, s.range);
            else if (s.target == "self" || s.target == "allies") target = caster;
            if (target == null) yield break;
            if (target != caster && ai != null && target.GetMover()?.GetCurrentNavNode() != null) ai.SetFacing(target.GetMover().GetCurrentNavNode().transform.position);
            PlayAnim(caster, s.anim ?? (s.melee ? "Melee" : "Ranged"));
            if (!string.IsNullOrEmpty(s.sound)) AudioManager.PlayEvent(s.sound, caster.transform.position);
            if (s.castVfx != null) Vfx(s.castVfx, caster.GetHitFx().position);
            MobAnimDriver.Trigger(caster, string.IsNullOrEmpty(s.clip) ? "attack" : s.clip);
            if (Ability.IsPlayerPresent(caster.transform.position, 6f)) yield return new WaitForSeconds(ab.attackImpactDelay);
            if (caster == null || caster.GetIsDead()) yield break;

            ActionContext ctx = Context(ab, s, target);
            List<Character> targets;
            if (Shapes.Is(s.shape) && target != caster)
            {
                Vector3 from = caster.GetMover()?.GetCurrentNavNode() != null ? caster.GetMover().GetCurrentNavNode().transform.position : caster.transform.position;
                Vector3 aim = target.GetMover()?.GetCurrentNavNode() != null ? target.GetMover().GetCurrentNavNode().transform.position : target.transform.position;
                ctx.center = aim;
                targets = Shapes.Hit(Foes(caster), from, aim, s.shape, s.range, s.width, s.angle);
                if (!targets.Contains(target)) targets.Insert(0, target);
            }
            else switch (s.target)
            {
                case "allies":
                    targets = SideOf(caster).Where(c => Vector3.Distance(c.transform.position, caster.transform.position) <= Math.Max(1.5f, s.radius)).ToList();
                    break;
                case "area":
                    {
                        Vector3 at = s.aroundSelf || (s.radius > 0 && s.range <= 0.01f) ? caster.transform.position : target.transform.position;
                        ctx.center = at;
                        targets = Foes(caster).Where(c => Vector3.Distance(c.transform.position, at) <= Math.Max(1f, s.radius) + 0.1f).ToList();
                        break;
                    }
                default: targets = new List<Character> { target }; break;
            }
            string verb = s.verb ?? $"uses {s.name ?? s.id} on";
            foreach (Character t in targets)
            {
                if (t == null || t.GetIsDead()) continue;
                bool hostile = !SideOf(caster).Contains(t) || t == Player.instance && caster.ai?.GetTeam() != AI.AI_TEAM.Player;
                if (hostile && s.dodgeable)
                {
                    int chance = Mathf.Clamp(s.accuracy - t.GetStat(STAT_TYPE.Dodge), 5, 95);
                    if (Random.Range(0, 100) >= chance)
                    {
                        Game.LOG($"{caster.GetName()} {verb} {(t == Player.instance ? "you" : t.GetName())}, and misses.");
                        if (HUD.instance != null) HUD.instance.MakeFct("Miss", t.GetHitFx().position, false);
                        continue;
                    }
                }
                if (s.impactVfx != null) Vfx(s.impactVfx, t.GetHitFx().position);
                if (s.target == "enemy" || s.target == "area") Game.LOG($"{caster.GetName()} {verb} {(t == Player.instance ? "you" : t.GetName())}.");
                MobAnimDriver.Trigger(t, "hit");
                Actions.Run(s.actions, ctx, t);
            }
            if (s.target == "self" || s.target == "allies" || s.target == "ally") Game.LOG($"{caster.GetName()} {s.verb ?? "uses " + (s.name ?? s.id)}.");
            Actions.FlushDeferred(ctx);
            if (s.spawn != null) Spawn(caster, s.spawn);
            if (s.selfActions.Count > 0) Actions.Run(s.selfActions, ctx, caster);
            if (s.die && caster != null && !caster.GetIsDead())
            {
                caster.xpValue = Math.Max(0, caster.xpValue);
                caster.DamageHealth(caster.GetHealth() + 99999, null);
            }
        }

        internal static void PlayAnim(Character c, string state)
        {
            Animator anim = c.ai != null ? c.ai.anim : null;
            if (anim == null || string.IsNullOrEmpty(state) || !anim.isActiveAndEnabled) return;
            int hash = Animator.StringToHash(state);
            for (int layer = 0; layer < anim.layerCount; layer++)
                if (anim.HasState(layer, hash)) { anim.Play(hash, layer); return; }
        }

        internal static void Vfx(string name, Vector3 at)
        {
            try { Actions.SpawnVfx(name, at); }
            catch (Exception e) { Plugin.Log.LogWarning("Enemy skill vfx: " + e.Message); }
        }

        // ---- spawns -------------------------------------------------------------------------------------------

        private const string SpawnTag = "SCCLM_SPAWNOF_";
        internal const string SpawnTagPrefix = SpawnTag;

        internal static int SpawnsAlive(Character caster) =>
            Contracts.AllCharacters().Count(c => !c.GetIsDead() && c.HasTag(SpawnTag + caster.uniqueId));

        private static void Spawn(Character caster, MobSpawnSpec s)
        {
            NavNode here = caster.GetMover()?.GetCurrentNavNode();
            if (here == null) return;
            var free = here.GetLinks().Where(l => l != null && !l.IsOccupied() && !l.HasDoor())
                .Concat(here.GetLinks().Where(l => l != null).SelectMany(l => l.GetLinks()).Where(l => l != null && l != here && !l.IsOccupied() && !l.HasDoor()))
                .Distinct().OrderBy(_ => Random.value).ToList();
            int room = Math.Max(0, s.max - SpawnsAlive(caster));
            for (int i = 0; i < Math.Min(s.count, room) && i < free.Count; i++)
            {
                string id = s.copies ? caster.internalName : s.ids.Count > 0 ? s.ids[Random.Range(0, s.ids.Count)] : null;
                Character prefab = s.copies ? AI_Manager.instance.GetCharacter(caster.internalName) : EnemyRegistry.Resolve(id);
                if (prefab == null) { Plugin.Log.LogWarning($"Enemy skill spawn: no enemy '{id}'"); return; }
                Character c = AI_Manager.instance.Spawn_Character(prefab, free[i].transform.position, caster.ai != null ? caster.ai.GetTeam() : AI.AI_TEAM.Hostile);
                if (c == null) continue;
                c.AddTag(SpawnTag + caster.uniqueId);
                c.transform.rotation = caster.transform.rotation;
                c.xpValue = s.copies ? 0 : c.xpValue / 2;
                if (s.health != 1f) { int hp = Mathf.Max(1, Mathf.RoundToInt((s.copies ? caster.GetHealthMax() : c.GetHealthMax()) * s.health)); c.SetHealth(hp, hp); }
                if (!string.IsNullOrEmpty(s.name)) c.SetName(s.name);
                else if (s.copies) c.SetName(caster.GetName());
                if (s.copies) { c.AddTag("CLONE"); c.AddTag("NOXP"); }
                if (c.ai != null && Player.instance != null) { c.ai.ChangeState(AI.AI_STATE.Active); c.ai.SetTarget(Player.instance); }
            }
        }

        // ---- patches for vanilla enemies ----------------------------------------------------------------------

        private const string PatchTag = "SCCLM_PATCH_";
        /// <summary>Self-tests only: every chance-based patch takes.</summary>
        internal static bool AlwaysRoll;

        /// <summary>Chance-based patches roll once per spawn and mark the enemy, so a reload keeps its skills.</summary>
        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.Spawn_Character), new[] { typeof(Character), typeof(Vector3), typeof(Quaternion), typeof(AI.AI_TEAM) })]
        private static class Patch_Spawn
        {
            private static void Postfix(Character __result, AI.AI_TEAM team)
            {
                if (__result == null || Patches.Count == 0 || Game.Game_isLoading || team != AI.AI_TEAM.Hostile) return;
                try
                {
                    foreach (EnemyPatchSpec ps in Patches)
                    {
                        if (OnPrefab(ps)) continue; // already on the prefab
                        if (ps.onlyInSideAreas && !SideAreas.InSide) continue;
                        if (!ps.enemies.Contains(__result.internalName) || Game.FLOOR < ps.minFloor || Game.FLOOR > ps.maxFloor || (!AlwaysRoll && Random.value >= ps.chance)) continue;
                        __result.AddTag(PatchTag + ps.key);
                        Apply(__result, ps);
                    }
                }
                catch (Exception e) { Plugin.Log.LogError("Enemy skills: " + e); }
            }
        }

        /// <summary>Every spawn, everywhere: put straight onto the prefab (only when it isn't kept to side areas).</summary>
        private static bool OnPrefab(EnemyPatchSpec ps) => ps.chance >= 1f && ps.minFloor <= 0 && ps.maxFloor >= 99 && !ps.onlyInSideAreas;

        private static void Apply(Character c, EnemyPatchSpec ps)
        {
            foreach (MobSkillSpec s in ps.skills) Attach(c.gameObject, s)?.Init(c);
            if (!string.IsNullOrEmpty(ps.prefix) && !c.GetName().StartsWith(ps.prefix)) c.SetName($"{ps.prefix} {c.GetName()}");
        }

        /// <summary>After a reload the marks are back but the components aren't: re-add them before the AI picks.</summary>
        [HarmonyPatch(typeof(AI), "SelectAbility")]
        private static class Patch_Select
        {
            private static void Prefix(AI __instance)
            {
                Character c = __instance.GetCharacter();
                if (c == null || c.tags == null || Patches.Count == 0) return;
                foreach (Tag t in c.tags.Where(t => t?.tagName != null && t.tagName.StartsWith(PatchTag)).ToList())
                {
                    EnemyPatchSpec ps = Patches.FirstOrDefault(p => PatchTag + p.key == t.tagName);
                    if (ps != null && ps.skills.Any(s => !c.GetComponents<ModMobAbility>().Any(m => m.specKey == s.key))) Apply(c, ps);
                }
            }
        }
    }
}
