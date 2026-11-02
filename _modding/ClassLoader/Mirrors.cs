using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Copies of you: a "summon" with "creature": "self". Each one is your class's crawler body wearing your gear, with
    /// your stats as they are this moment, a share of your health, and your own attacks: your weapon's damage on its
    /// basic attack, and every offensive skill you know (mod skills: vanilla abilities have no spec to copy), each on its
    /// own cooldown. "strength" is what reaches the target: that share of your damage, hard control (stun, silence, blind,
    /// taunt, exhaust) lands that share as often, and effects last and stack that share (at least one turn and one stack).
    /// It pays no stamina, and it keeps to the part of each skill aimed at the enemy: what a skill does to its caster,
    /// summons and the rest of the ground work stay yours. A skill that moves you ("teleport") becomes a leap for the copy.
    /// Tags keep it through saves; the skills themselves are rebuilt from your sheet the first turn after a load.
    /// </summary>
    internal static class Mirrors
    {
        /// <summary>Tag values: strength and health share in percent, and the glow as 0xRRGGBB.</summary>
        internal const string Tag = "SCCL_MIRROR", HealthTag = "SCCL_MIRRORHP", LookTag = "SCCL_MIRRORLOOK";
        private const string Fist = "#9FD8FF";
        private const float GlowStrength = 0.25f;

        /// <summary>What a copy's skill keeps: what lands on the enemy. Everything else belongs to you.</summary>
        private static readonly HashSet<string> Copied = new HashSet<string>
            { "damage", "effect", "stun", "knockback", "blind", "silence", "exhaust", "throw", "pull", "chain", "if", "tether", "vfx", "leap" };

        private static readonly HashSet<string> HardControl = new HashSet<string> { "stun", "silence", "blind", "taunt", "exhaust" };

        internal static bool IsSelf(string creature) => string.Equals(creature, "self", StringComparison.OrdinalIgnoreCase);

        internal static Character Body() => DashCam.CrawlerPrefab() ?? AI_Manager.instance?.GetCharacter("Nemesis_Soldier");

        /// <summary>A summoned copy as you stand right now. keepHealth: a copy back from a save.</summary>
        internal static void Fit(Character c, float strength, float healthShare, string look, int keepHealth = -1)
        {
            Player me = Player.instance;
            if (c == null || me == null) return;
            Strip(c);
            c.SetTag(Tag, Mathf.RoundToInt(strength * 100f));
            c.SetTag(HealthTag, Mathf.RoundToInt(healthShare * 100f));
            Color glow = Moves.Hex(look, Moves.Hex(Fist, Color.white));
            c.SetTag(LookTag, ((int)(glow.r * 255f) << 16) | ((int)(glow.g * 255f) << 8) | (int)(glow.b * 255f));
            c.SetTag("NOCORPSE", 1);
            c.AddTag("NOXP");
            c.xpValue = 0;
            CopyStats(c, me, healthShare, keepHealth);
            Teach(c, strength);
            string kind = c.internalName?.Replace("Nemesis_", "") ?? "";
            try { Wear.Dress(c, Worn, kind, me.playerName); }
            catch (Exception e) { Plugin.Log.LogWarning($"Copy of you: dressing: {e.Message}"); }
            Glow(c, glow);
            if (c.ai != null)
            {
                c.ai.aiStyle = AI.AI_STYLE.Brawler;
                Traverse.Create(c.ai).Field("maneuverChance").SetValue(0);
            }
        }

        /// <summary>The body's own standing as a Nemesis: what it is now comes from whoever it copies.</summary>
        internal static void Strip(Character c)
        {
            foreach (string t in c.tags.Where(t => t?.tagName != null).Select(t => t.tagName).ToList())
                if (t.Contains("NEMESIS") || t == "BOSS" || t == "ELITE" || t == "BOSSHEALTHBAR" || t.StartsWith("KILLSQUAD") || t.StartsWith("RARESPAWN")) c.RemoveTag(t);
        }

        /// <summary>Your stats and resistances on the body; corrupt moves the resistances elsewhere first.</summary>
        internal static void CopyStats(Character c, Character me, float healthShare, int keepHealth, Func<List<DmgObj>, List<DmgObj>> corrupt = null)
        {
            foreach (StatObj st in (c.primaryAttributes ?? new StatObj[0]).Concat(c.secondaryAttributes ?? new StatObj[0]))
                if (st != null && st.statType != STAT_TYPE.HealthMax) st.amount = me.GetStat(st.statType);
            c.resistances = (me.resistances ?? new List<DmgObj>()).Where(r => r != null).Select(r => new DmgObj(r.dmgType, r.damage)).ToList();
            if (corrupt != null) c.resistances = corrupt(c.resistances);
            int hp = Mathf.Max(1, Mathf.RoundToInt(me.GetHealthMax() * healthShare));
            if (c is Mob mob) mob.healthMax = hp;
            c.SetStat(STAT_TYPE.HealthMax, hp);
            c.CacheBaseStats();
            c.CalculateStats();
            c.level = me.level;
            int max = Mathf.Max(1, c.GetHealthMax());
            c.SetHealth(keepHealth > 0 ? Mathf.Min(keepHealth, max) : max, max);
        }

        /// <summary>What you have equipped in a slot, as a fresh copy Wear destroys when done.</summary>
        internal static Item Worn(EQUIP_TYPE slot)
        {
            Item_Equipment on = Menu_Player.instance?.GetEquipment().FirstOrDefault(e => e != null && e.equipType == slot);
            return on != null ? ItemDatabase.instance.GetItem(on.itemId) : null;
        }

        // ---- its attacks --------------------------------------------------------------------------------------------

        private static void Teach(Character c, float strength)
        {
            foreach (Ability_Mob own in c.GetComponentsInChildren<Ability_Mob>(true).Where(a => !(a is ModMobAbility))) own.isAvailable = false;
            EnemyAbilities.Attach(c.gameObject, Basic(strength))?.Init(c);
            foreach (ModAbility ab in Player_AbilityControl.instance.GetAbilities().OfType<ModAbility>())
            {
                MobSkillSpec s = Copy(ab, strength);
                if (s != null) EnemyAbilities.Attach(c.gameObject, s)?.Init(c);
            }
            c.ai?.GetAbilities(); // the AI re-reads its list
        }

        private static MobSkillSpec Register(MobSkillSpec s)
        {
            s.key = "Mirror_" + s.id + "_" + Mathf.RoundToInt(s.share * 100f);
            s.target = s.target ?? "enemy";
            EnemyAbilities.Specs[s.key] = s;
            return s;
        }

        /// <summary>Its basic attack: your weapon's damage (or a fist), through the same power your own swing or shot uses.</summary>
        internal static MobSkillSpec Basic(float strength)
        {
            var w = Player_Control.instance?.GetWeapon_Mainhand() as Item_Weapon;
            List<DmgObj> dmg = w?.GetStatDamageList();
            bool ranged = w != null && Loadout.IsRanged(w);
            var acts = new List<ActionSpec>();
            if (ranged) acts.Add(new ActionSpec { @do = "tether", look = Fist });
            foreach (DmgObj d in dmg ?? new List<DmgObj>())
                acts.Add(new ActionSpec { @do = "damage", type = d.dmgType.ToString(), min = d.damage, max = Math.Max(d.damage, d.damageMax), scaleMult = 0f });
            if (!acts.Any(a => a.@do == "damage")) acts.Add(new ActionSpec { @do = "damage", type = "Crushing", min = 1, max = 3, scaleMult = 0f });
            return Register(new MobSkillSpec
            {
                id = "Basic", name = ranged ? "Copied Shot" : "Copied Blow", verb = ranged ? "fires at" : "swings at", share = strength, cooldown = 0, priority = 1,
                range = ranged ? 6f : 1.5f, melee = !ranged, dodgeable = true, anim = ranged ? "Ranged" : "Melee", power = ranged ? "RangedPower" : "MeleePower",
                impactVfx = "PA_DualWield_SinisterStrike.impactEffect@0.3", actions = acts, sound = ranged ? null : "event:/Abilities/Misc/DualWield_SinisterStrike",
            });
        }

        /// <summary>One of your skills as a copy casts it, or null when there's nothing in it for a copy to do.</summary>
        internal static MobSkillSpec Copy(ModAbility ab, float strength)
        {
            AbilitySpec s = ab.Eff;
            return s == null || ab.isPassive ? null : Copy(s, ab.CurrentActions, ab.internalName, ab.GetDisplayName(), ab.range, ab.ClassRank - 1, ab.ClassId, strength);
        }

        /// <summary>The same from a skill as its class defines it, for a copy that never had it to tune.</summary>
        internal static MobSkillSpec Copy(AbilitySpec s, List<ActionSpec> all, string internalName, string name, float abilityRange, int rank, string classId, float strength)
        {
            if (s.target == "item" || s.target == "pet" || s.itemCost != null || s.creditCost > 0 || !string.IsNullOrEmpty(s.requires)) return null;
            if (s.target == "self" && s.radius <= 0f) return null;
            if (all.Any(a => (a.@do ?? "").ToLowerInvariant() == "summon")) return null;
            List<ActionSpec> acts = all.Where(a => Copied.Contains((a.@do ?? "").ToLowerInvariant()) && (a.to ?? "target").ToLowerInvariant() == "target").ToList();
            if (!acts.Any(a => (a.@do ?? "").ToLowerInvariant() == "damage" || (a.@do ?? "").ToLowerInvariant() == "chain")) return null;
            bool around = s.target == "self";
            return Register(new MobSkillSpec
            {
                id = ModPacks.Sanitize(internalName), name = name, share = strength, copyRank = rank, classId = classId,
                target = s.radius > 0f ? "area" : "enemy", aroundSelf = around, radius = s.radius, range = around ? s.radius : s.melee ? 1.5f : abilityRange,
                shape = s.shape, width = s.width, angle = s.angle, melee = s.melee, dodgeable = s.dodgeable, cooldown = s.cooldown,
                priority = 2 + s.cooldown, castVfx = s.castVfx, impactVfx = s.impactVfx, sound = s.sound, anim = s.melee ? "Melee" : "Ranged",
                leapFirst = all.Any(a => (a.@do ?? "").ToLowerInvariant() == "teleport"), actions = acts,
            });
        }

        /// <summary>What's left of an action cast by a copy, or null when it fails to take. See the class summary.</summary>
        internal static ActionSpec Weaken(ActionSpec a, string act, ActionContext ctx)
        {
            if (HardControl.Contains(act))
            {
                if (UnityEngine.Random.value > ctx.share) return null;
                if (act == "exhaust") return a;
                ActionSpec shorter = a.ShallowCopy();
                shorter.turns = Math.Max(1, Mathf.CeilToInt(Math.Max(1, a.turns > 0 ? a.turns : a.amount) * ctx.share));
                return shorter;
            }
            if (act != "effect") return a;
            ActionSpec weaker = a.ShallowCopy();
            int turns = a.turns > 0 ? a.turns : ModEffect.Specs.TryGetValue(Actions.EffectId(a.effect, ctx.classId) ?? "", out var rt) ? rt.spec.duration : 0;
            if (turns > 0) weaker.turns = Math.Max(1, Mathf.CeilToInt(turns * ctx.share));
            if (a.stacks > 1) weaker.stacks = Math.Max(1, Mathf.CeilToInt(a.stacks * ctx.share));
            return weaker;
        }

        // ---- its look -----------------------------------------------------------------------------------------------

        /// <summary>A faint glow over the body, so a copy reads as a copy and not a stranger in your armour.</summary>
        internal static void Glow(Character c, Color tint, float strength = GlowStrength)
        {
            foreach (Renderer r in c.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                foreach (Material m in r.materials)
                {
                    if (m.HasProperty("_Color")) m.color *= tint;
                    if (!m.HasProperty("_EmissionColor")) continue;
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", tint * strength);
                }
            }
        }

        /// <summary>
        /// Stained, not painted over. Glow's flat emission wipes the textures and leaves a solid shape with every polygon
        /// showing: here the colour goes through the texture, and what shines is the texture itself.
        /// </summary>
        internal static void Stain(Character c, Color tint, float strength)
        {
            foreach (Renderer r in c.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                foreach (Material m in r.materials)
                {
                    if (m.HasProperty("_Color")) m.color = Color.Lerp(m.color, m.color * tint, 0.5f);
                    if (!m.HasProperty("_EmissionColor")) continue;
                    if (m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") == null && m.mainTexture != null) m.SetTexture("_EmissionMap", m.mainTexture);
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", tint * strength);
                }
            }
        }

        /// <summary>Copies sent out by Nova, ever, and where the last ones landed (tests).</summary>
        internal static int NovaSent;
        internal static readonly List<Vector3> NovaSpots = new List<Vector3>();

        /// <summary>
        /// A nova you are one point of ("mirrors" on a "nova" rewrite): you take the way you aimed, and `count` copies take
        /// the others, evenly round you, diagonals too. They leave from where you stood, go as far as you did, each sets the
        /// spell off where it lands, then fades. Its damage is the nova's; its status is a copy's share. A spell that goes
        /// down on the ground (a trap, a field) stays a plain nova.
        /// </summary>
        internal static bool Nova(ModAbility ab, ActionContext ctx, Vector3 origin, Vector3 center, float radius, List<ActionSpec> actions, int count)
        {
            if (count <= 0 || ctx.caster != Player.instance || Map.instance == null) return false;
            if (actions.Any(a => a.@do == "trap" || a.@do == "field" || a.@do == "hazard")) return false;
            NavNode start = Map.instance.GetNearestNavNodeAt(origin, 1f);
            if (start == null) return false;
            const float Share = 0.4f;
            const string Tint = "#6FB4F0", Name = "Fragment of You";
            string tag = Summons.OwnerTag(ctx);
            var known = new HashSet<Character>(Summons.Mine(tag));
            var sent = new List<Character>();
            float reach = Mathf.Max(radius, 1.5f);
            Vector3 aim = center - origin;
            aim.y = 0f;
            if (aim.sqrMagnitude < 0.01f) aim = ctx.caster.transform.forward;
            float heading = Mathf.Round(Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg / 45f) * 45f;
            int tiles = Mathf.Clamp(Mathf.RoundToInt(aim.magnitude), 2, 6);
            NovaSpots.Clear();
            for (int d = 1; d <= count; d++)
            {
                Vector3 dir = Quaternion.Euler(0f, heading + 360f * d / (count + 1), 0f) * Vector3.forward;
                NavNode spot = Reach(start, Mathf.RoundToInt(dir.x), Mathf.RoundToInt(dir.z), tiles);
                if (spot == null) continue;
                Summons.Summon(new ActionSpec { @do = "summon", creature = "self", summonName = Name, amount = 1, turns = 2, strength = Share, look = Tint }, ctx, null, 1, spot.transform.position);
                Character c = Summons.Mine(tag).FirstOrDefault(x => !known.Contains(x));
                if (c == null) continue;
                known.Add(c);
                sent.Add(c);
                NovaSent++;
                NovaSpots.Add(spot.transform.position);
                c.transform.rotation = Quaternion.LookRotation(dir);
                Moves.Streak(origin + Vector3.up * 1.1f, spot.transform.position + Vector3.up * 1.1f, Moves.Hex(ab.Spec.dashTrail ?? Tint, Color.white), 0.04f, 0.7f);
                Actions.SpawnVfx(ab.Spec.areaVfx, spot.transform.position + Vector3.up * 0.1f);
                var cast = new ActionContext
                {
                    caster = c, source = ctx.source, classId = ctx.classId, sourceName = $"{c.GetName()}'s {ab.GetDisplayName()}", center = spot.transform.position,
                    rank = ctx.rank, damageMult = ctx.damageMult, chanceBonus = ctx.chanceBonus, hitVfx = ctx.hitVfx, share = Share,
                };
                foreach (Character v in SkillMods.NovaHits(spot.transform.position, reach))
                {
                    if (v == null || v.GetIsDead()) continue;
                    Actions.SpawnVfx(ab.Spec.impactVfx, v.GetHitFx().position);
                    cast.target = v;
                    Actions.Run(actions, cast, v);
                }
            }
            if (sent.Count == 0) return false;
            Plugin.Instance.StartCoroutine(FadeOut(sent, ab.GetDisplayName()));
            return true;
        }

        /// <summary>As far along a grid direction (sx, sz; diagonals too) as the floor runs, up to `tiles`; null if there's nowhere to go. A spell (`pastFolk`) flies on past whoever stands there.</summary>
        internal static NavNode Reach(NavNode start, int sx, int sz, int tiles, bool pastFolk = false)
        {
            Vector3 x = new Vector3(sx, 0f, 0f), z = new Vector3(0f, 0f, sz);
            NavNode n = start;
            for (int i = 0; i < tiles; i++)
            {
                NavNode next = sx != 0 && sz != 0 ? Diagonal.Target(n, x, z, out _) : Diagonal.Link(n, sx != 0 ? x : z);
                if (next == null || (!pastFolk && next.IsOccupied()) || next.BlockCheck() || next.Block_Move || next.HasDoor()) break;
                n = next;
            }
            return n == start ? null : n;
        }

        private static System.Collections.IEnumerator FadeOut(List<Character> copies, string source)
        {
            yield return new WaitForSeconds(1.1f);
            foreach (Character c in copies)
                if (c != null && !c.GetIsDead()) PetHandling.Unsummon(c, source, breakdown: false);
        }

        /// <summary>A save keeps a copy's tags and stats, not the skills it was taught: teach them again before it next acts.</summary>
        [HarmonyPatch(typeof(AI), "SelectAbility")]
        private static class Patch_BackFromSave
        {
            private static void Prefix(AI __instance)
            {
                Character c = __instance.GetCharacter();
                if (c == null || !c.HasTag(Tag) || c.GetComponent<ModMobAbility>() != null) return;
                try
                {
                    int rgb = c.GetTag(LookTag);
                    Fit(c, c.GetTag(Tag) / 100f, c.GetTag(HealthTag) / 100f, $"#{rgb:X6}", c.GetHealth());
                }
                catch (Exception e) { Plugin.Log.LogError("Copy of you, after a load: " + e); }
            }
        }
    }
}
