using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Control actions that need the enemy AI to cooperate: taunt (who it attacks), silence (no special attacks),
    /// exhaust (special attacks recover later) and swap (trade places). Taunted and Silenced are built-in effects,
    /// so they show on the enemy, tick down, and save like any other effect.
    /// </summary>
    internal static class Afflictions
    {
        internal static string TauntedId => ModEffect.MakeId(TileStacks.CoreId, "Taunted");
        internal static string SilencedId => ModEffect.MakeId(TileStacks.CoreId, "Silenced");

        internal static void RegisterEffects()
        {
            var def = new ClassDef
            {
                id = TileStacks.CoreId, name = "Breachwork Loader", folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Afflictions).Assembly.Location), "assets"),
                tiers = new List<TierDef>(),
                effects = new List<EffectSpec>
                {
                    new EffectSpec
                    {
                        id = "Taunted", name = "Taunted", icon = "taunted.png", debuff = true, duration = 2,
                        description = "Can't look away from whoever taunted it: attacks them whenever it can see them.",
                    },
                    new EffectSpec
                    {
                        id = "Silenced", name = "Silenced", icon = "silenced.png", debuff = true, duration = 1,
                        description = "Can't use special attacks (anything with a cooldown); basic attacks only.",
                    },
                },
            };
            ClassRegistry.RegisterEffects(def, null);
        }

        private static bool Alive(Character c) => c != null && !c.GetIsDead();

        private static bool IsEnemy(Character c) => Alive(c) && c != Player.instance && c.ai != null && !TileStacks.IsAlly(c)
                                                    && c.ai.GetTeam() != AI.AI_TEAM.Player && c.ai.GetTeam() != AI.AI_TEAM.Passive;

        // ---- taunt ----------------------------------------------------------------------------------------------

        /// <summary>
        /// On an enemy: it attacks the caster. On you or an ally: enemies around them (radius, default 4) that can
        /// see them attack them instead.
        /// </summary>
        internal static void Taunt(ActionSpec a, ActionContext ctx, Character t)
        {
            int turns = TauntTurns(a);
            if (IsEnemy(t))
            {
                if (Alive(ctx.caster)) TauntOne(t, ctx.caster, turns, ctx);
                return;
            }
            if (!Alive(t) || AI_Manager.instance == null) return;
            float r = a.radius > 0 ? a.radius : 4f;
            int n = 0;
            foreach (Character e in AI_Manager.instance.GetVisibleEnemiesAt(t.GetHeadFx().position, r, null))
                if (IsEnemy(e) && e.ai.CanSee(t) && TauntOne(e, t, turns, ctx)) n++;
            if (n > 0) Game.LOG($"{ctx.sourceName}: <color=#FF9F5C>{n} {(n == 1 ? "enemy turns" : "enemies turn")} on {t.GetName()}</color>.");
        }

        internal static int TauntTurns(ActionSpec a) => Math.Max(1, a.turns > 0 ? a.turns : 2);

        private static bool TauntOne(Character enemy, Character by, int turns, ActionContext ctx)
        {
            if (!IsEnemy(enemy) || !Alive(by)) return false;
            var c = new ActionContext { caster = by, classId = ctx.classId, sourceName = ctx.sourceName, source = ctx.source };
            Actions.ApplyEffect(new ActionSpec { @do = "effect", effect = TauntedId, turns = turns }, c, enemy);
            Effect e = enemy.effects.GetEffect(TauntedId);
            if (e == null) return false;
            e.caster = by; // a re-taunt by someone else takes over
            enemy.ai.SetLockTarget(false, null);
            enemy.ai.SetTarget(by);
            return true;
        }

        /// <summary>Whom a taunted character must attack, or null when it's free to choose.</summary>
        internal static Character TaunterOf(AI ai)
        {
            Character me = ai != null ? ai.GetCharacter() : null;
            if (!Alive(me) || me.effects == null) return null;
            Effect e = me.effects.GetEffect(TauntedId);
            if (e == null || !Alive(e.caster) || e.caster == me) return null;
            if (!(e.caster == Player.instance || TileStacks.IsAlly(e.caster))) return null; // its taunter switched sides
            return ai.CanSee(e.caster) ? e.caster : null;
        }

        /// <summary>Enemies re-pick the nearest target at the start of each turn; a taunted one picks its taunter.</summary>
        [HarmonyPatch(typeof(AI), "GetNearestEnemy")]
        private static class Patch_TauntTarget
        {
            private static void Postfix(AI __instance, ref Character __result)
            {
                Character by = TaunterOf(__instance);
                if (by != null) __result = by;
            }
        }

        // ---- silence --------------------------------------------------------------------------------------------

        internal static void Silence(ActionSpec a, ActionContext ctx, Character t)
        {
            if (!IsEnemy(t)) return;
            if (t.HasTag("BOSS") || t.HasTag("BOSSHEALTHBAR"))
            {
                Game.LOG($"{ctx.sourceName}: {t.GetName()} shrugs off the silence.");
                return;
            }
            Actions.ApplyEffect(new ActionSpec { @do = "effect", effect = SilencedId, turns = SilenceTurns(a) }, ctx, t);
        }

        internal static int SilenceTurns(ActionSpec a) => Math.Max(1, a.turns > 0 ? a.turns : a.amount);

        internal static bool IsSilenced(Character c) => Alive(c) && c.effects != null && c.effects.GetEffect(SilencedId) != null;

        /// <summary>
        /// A silenced enemy that picked a special attack falls back to its best attack without a cooldown, or to
        /// closing in when it has none. Channels already under way finish.
        /// </summary>
        [HarmonyPatch(typeof(AI), "SelectAbility")]
        private static class Patch_Silence
        {
            private static void Postfix(AI __instance)
            {
                if (!IsSilenced(__instance.GetCharacter())) return;
                var tr = Traverse.Create(__instance);
                Ability chosen = tr.Field("chosenAbility").GetValue<Ability>();
                if (chosen == null || chosen.cooldown <= 0 || chosen == tr.Field("channelAbility").GetValue<Ability>()) return;
                var shortlist = tr.Field("abilityShortlist").GetValue<List<Ability>>();
                Ability basic = shortlist?.Where(x => x != null && x.cooldown <= 0).OrderByDescending(x => x.GetPriority()).FirstOrDefault();
                tr.Field("chosenAbility").SetValue(basic);
            }
        }

        // ---- exhaust --------------------------------------------------------------------------------------------

        /// <summary>Every special attack (ability with a cooldown) of an enemy needs `turns` more turns to come back.</summary>
        internal static void Exhaust(ActionContext ctx, Character t, int turns)
        {
            if (!IsEnemy(t) || turns <= 0) return;
            int n = 0;
            foreach (Ability ab in t.ai.GetAbilities())
            {
                if (ab == null || ab.cooldown <= 0) continue;
                ab.SetCooldownAmount(Math.Max(0, ab.GetCooldownAmount()) + turns);
                n++;
            }
            if (n > 0) Game.LOG($"{ctx.sourceName}: {t.GetName()}'s special attacks are set back {turns} turn{(turns > 1 ? "s" : "")}.");
        }

        // ---- swap -----------------------------------------------------------------------------------------------

        internal static bool CanSwap(Character me, Character t)
        {
            if (!Alive(me) || !Alive(t) || me == t || me.GetMover() == null || t.GetMover() == null) return false;
            if (t.HasTag("BOSS") || t.HasTag("NOKNOCKBACK") || t.FLAG_isRooted) return false;
            NavNode mine = me.GetMover().GetCurrentNavNode(), theirs = t.GetMover().GetCurrentNavNode();
            return mine != null && theirs != null && mine != theirs && !TileStacks.Riders(theirs).Any();
        }

        /// <summary>The caster and the target trade tiles. Not with bosses, unmovable foes, or across shared tiles.</summary>
        internal static void Swap(ActionContext ctx, Character t)
        {
            Character me = ctx.caster;
            if (!CanSwap(me, t)) return;
            NavNode mine = me.GetMover().GetCurrentNavNode(), theirs = t.GetMover().GetCurrentNavNode();
            // your own pets sharing your tile step aside first; any other shared tile has too many people to sort out
            foreach (Character pal in TileStacks.Riders(mine).Where(r => r != me && r.ai != null && me.ai == null && r.ai.GetTeam() == AI.AI_TEAM.Player).ToList())
            {
                NavNode aside = mine.GetLinks().FirstOrDefault(n => n != null && n != theirs && !n.IsOccupied() && !n.BlockCheck() && !n.Block_Move && !n.HasDoor());
                if (aside == null) break;
                Place(pal, aside);
                TileStacks.Sweep(); // it left the stack
            }
            if (TileStacks.Riders(mine).Any() || TileStacks.Riders(theirs).Any()) return;
            mine.ClearOccupant();
            theirs.ClearOccupant();
            Triggers.Styled("swap", me == Player.instance ? t : me, () => { Place(me, theirs); Place(t, mine); });
            if (t.ai != null) t.ai.ForceMove = true;
            if (t.anim != null) t.Flinch();
            Actions.SpawnVfx("PA_Elementalist_ChainLightning.shockImpact@0.5", t.GetHitFx().position);
        }

        private static void Place(Character c, NavNode n)
        {
            c.GetMover().transform.position = n.transform.position;
            c.GetMover().SetCurrentNavNode(n);
            n.OnEnterNode(c);
            n.SetOccupant(c);
            if (c != Player.instance) return;
            Player.CURRENT_ROOM = n.GetRoom();
            n.GetRoom().Discover();
            Map.instance.UpdateOcclusion();
            CullManager.instance.UpdateCulling();
        }
    }
}
