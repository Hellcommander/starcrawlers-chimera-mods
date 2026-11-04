using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>An aura: while its source has it, everyone in reach wears an effect, and loses it on leaving.</summary>
    public class AuraSpec
    {
        /// <summary>The effect worn in reach ("@Name", "#Name" or a vanilla id).</summary>
        public string effect;
        public float radius = 2f;
        /// <summary>enemies (default) | allies | all: whose side, from the source's point of view.</summary>
        public string affects = "enemies";
        /// <summary>The source wears it too (allies / all only).</summary>
        public bool self;
        /// <summary>Only those this holds about (checked about each one, as the target).</summary>
        public WhenSpec when;
    }

    /// <summary>
    /// "auras" on a passive (while you know it) or on any effect (while its bearer, you, an ally or an enemy, has it):
    /// characters within reach wear the aura's effect, and lose it as soon as nothing keeps them in one. Checked several
    /// times a second, so stepping in or out counts at once, on anyone's turn.
    /// </summary>
    internal static class Auras
    {
        private struct Kept { public Character who; public string effect; }
        /// <summary>(bearer instance, effect id) the auras put on, so only those are taken off again.</summary>
        private static readonly Dictionary<(int, string), Kept> applied = new Dictionary<(int, string), Kept>();
        private static float next;
        /// <summary>Any loaded class or effect has an aura; nothing to do otherwise.</summary>
        internal static bool Any;

        /// <summary>After registration: is any aura or block loaded at all?</summary>
        internal static void Scan()
        {
            Any = ModAbility.Specs.Values.Any(r => r.spec?.auras?.Count > 0) || ModEffect.Specs.Values.Any(r => r.spec?.auras?.Count > 0);
            AbilityMatch.AnyBlocks = ModEffect.Specs.Values.Any(r => !string.IsNullOrEmpty(r.spec?.blocks));
            applied.Clear();
        }

        internal static void Tick()
        {
            if (!Any || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.15f;
            if (Player.instance == null || Game.Game_isLoading || AI_Manager.instance == null || Map.instance == null) { applied.Clear(); return; }
            try { Refresh(); }
            catch (Exception e) { Plugin.Log.LogError("Auras: " + e); }
        }

        private static IEnumerable<(Character src, AuraSpec aura, string classId)> Sources(List<Character> everyone)
        {
            Player p = Player.instance;
            if (Player_AbilityControl.instance != null && !p.GetIsDead())
                foreach (ModAbility a in Player_AbilityControl.instance.GetAbilities().OfType<ModAbility>())
                    foreach (AuraSpec au in a.Spec?.auras ?? Enumerable.Empty<AuraSpec>())
                        yield return (p, au, a.ClassId);
            foreach (Character c in everyone)
            {
                if (c == null || c.GetIsDead() || c.effects == null) continue;
                foreach (ModEffect e in c.effects.GetEffects().OfType<ModEffect>())
                    foreach (AuraSpec au in e.Spec?.auras ?? Enumerable.Empty<AuraSpec>())
                        yield return (c, au, e.ClassId);
            }
        }

        private static bool PlayerSide(Character c) => c == Player.instance || TileStacks.IsAlly(c);

        private static void Refresh()
        {
            var everyone = Contracts.AllCharacters().Where(c => c != null && !c.GetIsDead()).ToList();
            if (!everyone.Contains(Player.instance)) everyone.Add(Player.instance);
            var want = new Dictionary<(int, string), (Character who, Character src, AuraSpec aura, string classId)>();
            foreach (var (src, aura, classId) in Sources(everyone).ToList())
            {
                if (string.IsNullOrEmpty(aura.effect)) continue;
                string id = Actions.EffectId(aura.effect, classId);
                string side = (aura.affects ?? "enemies").ToLowerInvariant();
                Vector3 at = src.transform.position;
                foreach (Character t in everyone)
                {
                    if (t == src && !aura.self) continue;
                    if (t != src)
                    {
                        bool foe = PlayerSide(src) != PlayerSide(t);
                        if (side == "enemies" && !foe || side == "allies" && foe) continue;
                    }
                    if (Vector3.Distance(at, t.transform.position) > aura.radius + 0.25f) continue;
                    if (aura.when != null && !When.Check(aura.when, new ActionContext { caster = src, classId = classId, sourceName = "aura" }, t)) continue;
                    want[(t.GetInstanceID(), id)] = (t, src, aura, classId);
                }
            }
            // in reach: wear it (it holds while kept; a long duration so it never runs out under the aura)
            foreach (var kv in want)
            {
                var (who, src, aura, classId) = kv.Value;
                if (who.effects.GetEffect(kv.Key.Item2) == null)
                {
                    Actions.ApplyEffect(new ActionSpec { @do = "effect", effect = aura.effect, turns = 99 }, new ActionContext { caster = src, classId = classId, sourceName = "aura" }, who);
                    if (who.effects.GetEffect(kv.Key.Item2) != null) applied[kv.Key] = new Kept { who = who, effect = kv.Key.Item2 };
                }
            }
            // out of reach (or the source is gone): take off what an aura put on
            foreach (var key in applied.Keys.Where(k => !want.ContainsKey(k)).ToList())
            {
                Kept k = applied[key];
                applied.Remove(key);
                Effect e = k.who != null && k.who.effects != null ? k.who.effects.GetEffect(k.effect) : null;
                if (e != null) { e.RemoveInstant(); k.who.CalculateStats(); }
            }
        }

        internal static string Describe(AuraSpec a, string classId)
        {
            string side = (a.affects ?? "enemies").ToLowerInvariant();
            string who = side == "all" ? "everyone else" : side == "allies" ? "your allies" : "enemies";
            if (a.self && side != "enemies") who += " and you";
            string cond = a.when != null ? $" while {When.Describe(a.when, classId)}" : "";
            string verb = who == "everyone else" ? "has" : "have";
            return $"<b>Aura:</b> {who} within {a.radius:0.#} tiles{cond} {verb} {Actions.EffectNameOf(a.effect, classId)}; it leaves them when they step out.";
        }
    }

    /// <summary>
    /// Which abilities a rule names, for "blocks" on effects, the "uses" condition and the "ability" of a "special"
    /// reaction: "damage:Psi" (it deals that type), "cooldown" (it has one), "melee", "ranged", or its name or id;
    /// "a|b" = either, "a+b" = both, "!a" = not.
    /// </summary>
    internal static class AbilityMatch
    {
        internal static bool Matches(Ability a, string rule)
        {
            if (a == null || string.IsNullOrEmpty(rule)) return false;
            foreach (string alt in rule.Split('|'))
                if (alt.Split('+').All(t => Term(a, t.Trim()))) return true;
            return false;
        }

        private static bool Term(Ability a, string t)
        {
            if (t.StartsWith("!")) return !Term(a, t.Substring(1).Trim());
            if (t.StartsWith("damage:", StringComparison.OrdinalIgnoreCase)) return Deals(a, t.Substring(7).Trim());
            switch (t.ToLowerInvariant())
            {
                case "cooldown": return a.cooldown > 0;
                case "melee": return a.isMelee;
                case "ranged": return !a.isMelee && a.range > 2f;
            }
            return string.Equals(a.internalName, t, StringComparison.OrdinalIgnoreCase) || string.Equals(Name(a), t, StringComparison.OrdinalIgnoreCase);
        }

        private static string Name(Ability a) { try { return a.GetDisplayName(); } catch { return null; } }

        /// <summary>The ability deals this damage type: in its own damage list, or in a mod ability's damage actions.</summary>
        internal static bool Deals(Ability a, string type)
        {
            if (!Enum.TryParse(type, true, out DAMAGE_TYPE dt)) return false;
            if (a.damageList != null && a.damageList.Any(d => d != null && d.dmgType == dt)) return true;
            if (a is ModAbility m && m.Spec != null)
                return ClassRegistry.AllActionsOf(m.Spec).Any(x => x.@do == "damage" && string.Equals(x.type, type, StringComparison.OrdinalIgnoreCase));
            return false;
        }

        /// <summary>Every ability a character can use.</summary>
        internal static IEnumerable<Ability> Of(Character c)
        {
            if (c == null) return Enumerable.Empty<Ability>();
            if (c == Player.instance) return Player_AbilityControl.instance?.GetAbilities()?.Where(x => x != null) ?? Enumerable.Empty<Ability>();
            return c.ai?.GetAbilities()?.Where(x => x != null) ?? Enumerable.Empty<Ability>();
        }

        /// <summary>"uses": the character has an ability the rule names.</summary>
        internal static bool Uses(Character c, string rule) => Of(c).Any(a => Matches(a, rule));

        internal static string Describe(string rule)
        {
            // "damage:Psi|damage:Void" reads "deal Psi or Void damage"
            var alts = rule.Split('|').Select(x => x.Trim()).ToList();
            if (alts.Count > 1 && alts.All(x => x.StartsWith("damage:", StringComparison.OrdinalIgnoreCase) && !x.Contains("+")))
                return $"deal {string.Join(" or ", alts.Select(x => x.Substring(7).Trim()))} damage";
            return Words(rule);
        }

        private static string Words(string rule) => string.Join(" or ", rule.Split('|').Select(alt => string.Join(" and ", alt.Split('+').Select(t =>
        {
            t = t.Trim();
            bool not = t.StartsWith("!");
            if (not) t = t.Substring(1).Trim();
            string s = t.StartsWith("damage:", StringComparison.OrdinalIgnoreCase) ? $"deal {t.Substring(7)} damage"
                : t == "cooldown" ? "have a cooldown" : t == "melee" ? "are melee" : t == "ranged" ? "are ranged" : $"are {t}";
            return not ? "don't " + s.Replace("are ", "be ").Replace("deal ", "deal ") : s;
        }))));

        // ---- "blocks" on an effect ----------------------------------------------------------------------------

        /// <summary>Any loaded effect blocks abilities (else the checks below cost nothing).</summary>
        internal static bool AnyBlocks;

        /// <summary>An effect on the character blocks this ability.</summary>
        internal static bool Blocked(Character c, Ability a)
        {
            if (!AnyBlocks || c == null || a == null || c.effects == null) return false;
            foreach (ModEffect e in c.effects.GetEffects().OfType<ModEffect>())
            {
                string rule = e.Spec?.blocks;
                if (!string.IsNullOrEmpty(rule) && Matches(a, rule)) return true;
            }
            return false;
        }

        /// <summary>The effect on the character that blocks this ability, if any.</summary>
        internal static ModEffect Blocker(Character c, Ability a)
        {
            if (!AnyBlocks || c == null || a == null || c.effects == null) return null;
            foreach (ModEffect e in c.effects.GetEffects().OfType<ModEffect>())
            {
                string rule = e.Spec?.blocks;
                if (!string.IsNullOrEmpty(rule) && Matches(a, rule)) return e;
            }
            return null;
        }

        /// <summary>
        /// An ability going off while its user has an effect that blocks it (an enemy that was already mid-plan as it
        /// stepped in, or one the game uses without asking CanUse): it fizzles, goes on cooldown, and the effect's
        /// onBlockedActions run.
        /// </summary>
        internal static bool Fizzle(Ability a, Character user)
        {
            if (!AnyBlocks) return false;
            if (user == null) user = Owner(a);
            ModEffect by = Blocker(user, a);
            if (by == null) return false;
            if (a.cooldown > 0) a.SetCooldownAmount(a.cooldown + 1);
            Game.LOG($"<color=#9FB4C8>{user.GetName()}'s {a.GetDisplayName()} fizzles under {by.GetName()}.</color>");
            if (HUD.instance != null) HUD.instance.MakeFct("<color=#9FB4C8>Fizzled</color>", user.GetHitFx().position, false);
            by.OnBlocked(a);
            return true;
        }

        private static Character Owner(Ability a)
        {
            Character c = a.GetCaster();
            if (c != null) return c;
            if (Player_AbilityControl.instance != null && a.transform.IsChildOf(Player_AbilityControl.instance.transform)) return Player.instance;
            return a.GetComponentInParent<Character>();
        }

        /// <summary>Every CanUse in the game's ability classes (and ours): a blocked ability can't be used, by the
        /// player's hotbar, an enemy or a summon alike.</summary>
        [HarmonyPatch]
        private static class Patch_CanUse
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
                AccessTools.AllTypes().Where(t => typeof(Ability).IsAssignableFrom(t))
                    .Select(t => AccessTools.DeclaredMethod(t, nameof(Ability.CanUse), Type.EmptyTypes))
                    .Where(m => m != null && !m.IsAbstract).Distinct();

            private static void Postfix(Ability __instance, ref bool __result)
            {
                if (!__result || !AnyBlocks) return;
                try { if (Blocked(Owner(__instance), __instance)) __result = false; }
                catch { }
            }
        }

        /// <summary>An enemy or summon that picked a blocked ability falls back to its best one that isn't (or closes in).</summary>
        [HarmonyPatch(typeof(AI), "SelectAbility")]
        private static class Patch_Select
        {
            private static void Postfix(AI __instance)
            {
                if (!AnyBlocks) return;
                Character c = __instance.GetCharacter();
                var tr = Traverse.Create(__instance);
                Ability chosen = tr.Field("chosenAbility").GetValue<Ability>();
                if (chosen == null || !Blocked(c, chosen) || chosen == tr.Field("channelAbility").GetValue<Ability>()) return;
                var shortlist = tr.Field("abilityShortlist").GetValue<List<Ability>>();
                Ability other = shortlist?.Where(x => x != null && !Blocked(c, x) && x.GetCooldownAmount() <= 0).OrderByDescending(x => x.GetPriority()).FirstOrDefault();
                tr.Field("chosenAbility").SetValue(other);
            }
        }
    }
}
