using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Reaction slots: "arm" actions put a reaction in a slot; enemies set them off by stepping next to you, using a
    /// special attack, hitting you or missing you, buffing or healing their own, calling in more, moving in range or
    /// downing one of yours. Reactions answer from a queue in priority order. They can't set off other reactions, and
    /// only a few go off per round. A "special" reaction runs before the enemy's ability and can cut it off. Armed
    /// reactions are saved in the mod's save slot. A passive's proc can arm too (a wire that is always strung at the
    /// start of a fight), and "rearm" puts charges back.
    /// </summary>
    internal static class Reactions
    {
        internal const int BaseSlots = 2;
        /// <summary>Most reactions that go off between two of your turns.</summary>
        internal const int MaxPerRound = 5;

        internal class Armed
        {
            public string abilityId;
            public int armIndex;
            public int charges;
            public int turns;
            /// <summary>Your turns until it may answer again (spec.cooldown).</summary>
            public int cool;
            public ReactionSpec spec;
            public string classId;
            /// <summary>Enemies that already set this one off this round (each enemy once per round per reaction).</summary>
            public readonly HashSet<Character> firedOn = new HashSet<Character>();
        }

        private static readonly List<Armed> armed = new List<Armed>();
        private static readonly Queue<(Armed r, Character enemy, Character victim)> queue = new Queue<(Armed, Character, Character)>();
        private static bool draining, resolving;
        private static int firedThisRound;
        private static object loadedFrom;

        internal static IReadOnlyList<Armed> Current { get { Ensure(); return armed; } }

        /// <summary>Triggers about something an enemy does to you or your side: "who" says whether allies count.</summary>
        private static readonly HashSet<string> Protective = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "approach", "retreat", "hurt", "ranged", "lowHealth", "debuff" };

        /// <summary>Triggers with a reach ("range") from you.</summary>
        private static readonly HashSet<string> Ranged = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "special", "move", "turn" };

        // ---- slots and saving ---------------------------------------------------------------------------------

        internal static int Slots
        {
            get
            {
                var pac = Player_AbilityControl.instance;
                int best = 0;
                if (pac != null)
                    foreach (Ability a in pac.GetAbilities())
                        if (a is ModAbility m && m.Spec != null) best = Math.Max(best, m.Spec.reactionSlots);
                return Math.Max(BaseSlots, best);
            }
        }

        /// <summary>
        /// Every "arm" action of an ability, in order: its actions, the perk's addActions, then the top level of each of
        /// its procs and the perk's added procs (a passive that strings a wire when a fight starts). The index into this
        /// list is what the save keeps, so the order only changes when the file does.
        /// </summary>
        internal static List<ActionSpec> ArmActions(AbilitySpec s)
        {
            if (s == null) return new List<ActionSpec>();
            IEnumerable<ActionSpec> all = (s.actions ?? new List<ActionSpec>()).Concat(s.perk?.addActions ?? new List<ActionSpec>());
            foreach (ProcSpec p in (s.procs ?? new List<ProcSpec>()).Concat(s.perk?.addProcs ?? new List<ProcSpec>()))
                if (p?.actions != null) all = all.Concat(p.actions);
            return all.Where(a => a != null && string.Equals(a.@do, "arm", StringComparison.OrdinalIgnoreCase) && a.reaction != null).ToList();
        }

        /// <summary>Reads the armed list from the save slot whenever a different save is in play.</summary>
        private static void Ensure()
        {
            var data = SideAreas.Data;
            if (ReferenceEquals(data, loadedFrom)) return;
            loadedFrom = data;
            armed.Clear();
            queue.Clear();
            if (data == null || !data.TryGetValue("reactions", out object v) || !(v is string[] rows)) return;
            foreach (string row in rows)
            {
                string[] p = (row ?? "").Split('|');
                if (p.Length < 4 || !ModAbility.Specs.TryGetValue(p[0], out var rt)) continue;
                int.TryParse(p[1], out int idx); int.TryParse(p[2], out int ch); int.TryParse(p[3], out int tu);
                int cool = 0;
                if (p.Length > 4) int.TryParse(p[4], out cool);
                var arms = ArmActions(rt.spec);
                if (idx < 0 || idx >= arms.Count) continue;
                armed.Add(new Armed { abilityId = p[0], armIndex = idx, charges = ch, turns = tu, cool = cool, spec = arms[idx].reaction, classId = rt.classId });
            }
        }

        private static void Save()
        {
            var data = SideAreas.Data;
            loadedFrom = data;
            if (data != null) data["reactions"] = armed.Select(r => $"{r.abilityId}|{r.armIndex}|{r.charges}|{r.turns}|{r.cool}").ToArray();
        }

        // ---- arming ---------------------------------------------------------------------------------------------

        /// <summary>Most charges an armed reaction holds when arming stacks or rearming adds to it.</summary>
        private static int Cap(ReactionSpec s) => Math.Max(Math.Max(1, s.charges), s.maxCharges > 0 ? s.maxCharges : Math.Max(1, s.charges) * 3);

        /// <summary>
        /// The "arm" action: sets (or refreshes) a reaction; a full set of slots drops the oldest. With "stack", arming
        /// it again adds its charges (up to maxCharges) instead.
        /// </summary>
        internal static void Arm(ActionSpec a, ActionContext ctx)
        {
            if (a.reaction == null || ctx.caster != Player.instance || !(ctx.source is ModAbility src) || src.Spec == null) return;
            Ensure();
            int idx = ArmActions(src.Spec).IndexOf(a);
            if (idx < 0) return;
            ReactionSpec s = a.reaction;
            Armed same = armed.FirstOrDefault(r => r.abilityId == src.internalName && r.armIndex == idx);
            if (same != null && s.stack)
            {
                int was = same.charges;
                same.charges = Math.Min(Cap(s), same.charges + Math.Max(1, s.charges));
                same.turns = Math.Max(same.turns, Math.Max(1, s.turns));
                EventBus.Mark(EventBus.Reactions);
                Save();
                Game.LOG($"<color=#7FE0FF>{s.name}</color> {(same.charges > was ? $"stacks to {same.charges}x" : $"is already at {same.charges}x")} ({same.turns} turns).");
                return;
            }
            if (same != null) armed.Remove(same);
            int slots = Slots;
            while (armed.Count >= slots)
            {
                Game.LOG($"<color=#9FB7C9>{armed[0].spec.name} is pushed out of its slot.</color>");
                armed.RemoveAt(0);
            }
            armed.Add(new Armed { abilityId = src.internalName, armIndex = idx, charges = Math.Max(1, s.charges), turns = Math.Max(1, s.turns), spec = s, classId = ctx.classId });
            EventBus.Mark(EventBus.Reactions);
            Save();
            Game.LOG($"<color=#7FE0FF>{s.name} armed</color> ({Math.Max(1, s.charges)}x, {Math.Max(1, s.turns)} turns): {TriggerText(s)}.");
        }

        /// <summary>
        /// The "rearm" action: every armed reaction gets "amount" charges back (up to its cap) and "turns" more turns;
        /// one with its name in "match" only, when set.
        /// </summary>
        internal static void Rearm(ActionSpec a, ActionContext ctx)
        {
            if (ctx.caster != Player.instance) return;
            Ensure();
            int n = 0;
            foreach (Armed r in armed)
            {
                if (!string.IsNullOrEmpty(a.skill) && !a.skill.Split('|').Any(w => ProcHub.Glob(r.spec.name, w.Trim()))) continue;
                int before = r.charges;
                r.charges = Math.Min(Cap(r.spec), r.charges + Math.Max(0, a.amount));
                r.turns += Math.Max(0, a.turns);
                r.cool = 0;
                if (r.charges != before || a.turns > 0) n++;
            }
            if (n == 0) return;
            EventBus.Mark(EventBus.Reactions);
            Save();
            Game.LOG($"<color=#7FE0FF>Rearmed</color> {n} reaction{(n == 1 ? "" : "s")}.");
        }

        internal static void Clear() { Ensure(); armed.Clear(); queue.Clear(); EventBus.Mark(EventBus.Reactions); Save(); }

        /// <summary>Charges across everything armed (the "charges" condition value).</summary>
        internal static int Charges { get { Ensure(); return armed.Sum(r => r.charges); } }

        // ---- triggers -------------------------------------------------------------------------------------------

        private static bool IsEnemy(Character c) => c != null && !c.GetIsDead() && c != Player.instance && c.ai != null && !TileStacks.IsAlly(c)
                                                    && c.ai.GetTeam() != AI.AI_TEAM.Player && c.ai.GetTeam() != AI.AI_TEAM.Passive;

        private static bool IsAlly(Character c) => c != null && c != Player.instance && c.ai != null && c.ai.GetTeam() == AI.AI_TEAM.Player;

        private static bool Matches(Armed r, string on, Character victim) =>
            r.charges > 0 && r.cool <= 0 && string.Equals(r.spec.on, on, StringComparison.OrdinalIgnoreCase)
            && (!Protective.Contains(on) || victim == null || victim == Player.instance || string.Equals(r.spec.who, "allies", StringComparison.OrdinalIgnoreCase));

        private static bool Listening(string on) => armed.Any(r => r.charges > 0 && string.Equals(r.spec.on, on, StringComparison.OrdinalIgnoreCase));

        /// <summary>Queues every armed reaction that answers this event; they resolve a moment later, in priority order.</summary>
        internal static void Fire(string on, Character enemy, Character victim)
        {
            if (resolving || Game.Game_isLoading || Player.instance == null || !IsEnemy(enemy)) return;
            Ensure();
            var hits = Candidates(on, enemy, victim);
            if (hits.Count == 0) return;
            foreach (Armed r in hits) { r.firedOn.Add(enemy); queue.Enqueue((r, enemy, victim)); }
            if (!draining) Plugin.Instance.StartCoroutine(Drain());
        }

        private static List<Armed> Candidates(string on, Character enemy, Character victim) =>
            armed.Where(r => Matches(r, on, victim) && !r.firedOn.Contains(enemy) && (on != "lowHealth" || CrossedLow(r, victim)) && InReach(r, enemy))
                 .OrderByDescending(r => r.spec.priority).ToList();

        /// <summary>A ranged trigger only answers enemies within the reaction's range of you.</summary>
        private static bool InReach(Armed r, Character enemy) =>
            !Ranged.Contains(r.spec.on ?? "") || Player.instance == null || enemy == null
            || Vector3.Distance(enemy.transform.position, Player.instance.transform.position) <= r.spec.range;

        /// <summary>Health the victim had before the hit being answered (lowHealth needs the hit to cross the line).</summary>
        private static int healthBefore;

        private static bool CrossedLow(Armed r, Character victim)
        {
            if (victim == null || victim.GetHealthMax() <= 0) return false;
            int line = Mathf.Clamp(r.spec.threshold, 1, 99) * victim.GetHealthMax();
            return victim.GetHealth() * 100 < line && healthBefore * 100 >= line;
        }

        /// <summary>
        /// An enemy's hit landed on you or an ally (health went down from <paramref name="before"/>): "hurt", plus
        /// "ranged" from two or more tiles away and "lowHealth" when it took the victim under a reaction's threshold.
        /// </summary>
        internal static void Hurt(Character enemy, Character victim, int before)
        {
            if (enemy == null || victim == null) return;
            Fire("hurt", enemy, victim);
            if (Vector3.Distance(Flat(enemy.transform.position), Flat(victim.transform.position)) > 1.8f) Fire("ranged", enemy, victim);
            healthBefore = before;
            Fire("lowHealth", enemy, victim);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static IEnumerator Drain()
        {
            draining = true;
            try
            {
                yield return null;
                while (queue.Count > 0)
                {
                    var (r, enemy, victim) = queue.Dequeue();
                    // a moving enemy is answered once it stands on its tile
                    for (float t = 0f; t < 1.5f && enemy != null && enemy.GetMover() != null && enemy.GetMover().isMoving; t += Time.deltaTime) yield return null;
                    if (Resolve(r, enemy, victim)) yield return new WaitForSeconds(0.25f);
                }
            }
            finally { draining = false; }
        }

        /// <summary>Runs one reaction now. False when it couldn't (spent, cooling, its condition fails, budget used up, enemy gone).</summary>
        private static bool Resolve(Armed r, Character enemy, Character victim)
        {
            if (r.charges <= 0 || r.cool > 0 || !armed.Contains(r) || !IsEnemy(enemy) || Player.instance == null || Player.instance.GetIsDead()) return false;
            if (firedThisRound >= MaxPerRound) return false;
            var src = Player_AbilityControl.instance?.GetAbility(r.abilityId) as ModAbility;
            ActionContext ctx = src != null ? src.MakeContext() : new ActionContext { caster = Player.instance, classId = r.classId };
            ctx.sourceName = r.spec.name;
            ctx.target = enemy;
            ctx.attacker = enemy;
            ctx.victim = victim ?? Player.instance;
            if (r.spec.when != null && !When.Check(r.spec.when, ctx, enemy)) return false;
            firedThisRound++;
            r.charges--;
            r.cool = Math.Max(0, r.spec.cooldown);
            resolving = true;
            try
            {
                Vector3 at = enemy.GetHitFx().position;
                ModAudio.Play(r.spec.sound, at, r.classId);
                Actions.SpawnVfx(r.spec.vfx ?? "PA_Elementalist_ChainLightning.shockImpact@0.5", at);
                if (HUD.instance != null) HUD.instance.MakeFct("<color=#7FE0FF>" + r.spec.name + "</color>", Player.instance.GetHitFx().position, false);
                Game.LOG($"<color=#7FE0FF>{r.spec.name}</color> answers {enemy.GetName()}.");
                var victims = new List<Character> { enemy };
                if (r.spec.radius > 0f && AI_Manager.instance != null)
                    victims.AddRange(AI_Manager.instance.GetEnemiesAt(enemy.transform.position, r.spec.radius).Where(v => v != enemy && IsEnemy(v)));
                var self = r.spec.actions.Where(a => (a.to ?? "").ToLowerInvariant() == "self" || (a.to ?? "").ToLowerInvariant() == "victim").ToList();
                var rest = r.spec.actions.Except(self).ToList();
                foreach (Character v in victims) if (v != null && !v.GetIsDead()) { ctx.target = v; Actions.Run(rest, ctx, v); }
                ctx.target = enemy;
                Actions.Run(self, ctx, enemy);
                ProcHub.Fire("reaction", enemy, r.spec.name);
            }
            catch (Exception e) { Plugin.Log.LogError($"Reaction {r.spec.name}: {e}"); }
            finally { resolving = false; }
            if (r.charges <= 0) armed.Remove(r);
            EventBus.Mark(EventBus.Reactions);
            Save();
            return true;
        }

        /// <summary>
        /// "spring" action: every armed reaction goes off once right now at the nearest enemy it can reach, spending a
        /// charge each (outside the per-round limit).
        /// </summary>
        internal static void Spring(ActionContext ctx) => Plugin.Instance.StartCoroutine(SpringSoon());

        /// <summary>A moment later, so stacks the casting ability spends are gone before the reactions give new ones.</summary>
        private static IEnumerator SpringSoon()
        {
            yield return new WaitForSeconds(0.3f);
            Ensure();
            Player p = Player.instance;
            if (p == null || AI_Manager.instance == null) yield break;
            foreach (Armed r in armed.ToList())
            {
                float reach = Ranged.Contains(r.spec.on ?? "") ? r.spec.range : 6f;
                Character enemy = AI_Manager.instance.GetEnemiesAt(p.transform.position, reach).Where(IsEnemy)
                    .OrderBy(e => Vector3.Distance(e.transform.position, p.transform.position)).FirstOrDefault();
                if (enemy == null) continue;
                int before = firedThisRound;
                firedThisRound = 0;
                r.cool = 0;
                bool went = Resolve(r, enemy, p);
                firedThisRound = before;
                if (went) yield return new WaitForSeconds(0.25f);
            }
        }

        // ---- rules text -----------------------------------------------------------------------------------------

        internal static string TriggerText(ReactionSpec s)
        {
            bool allies = string.Equals(s.who, "allies", StringComparison.OrdinalIgnoreCase);
            string side = allies ? "you or one of your allies" : "you";
            switch ((s.on ?? "").ToLowerInvariant())
            {
                case "approach": return allies ? "when an enemy steps next to you or one of your allies" : "when an enemy steps next to you";
                case "retreat": return allies ? "when an enemy next to you or one of your allies steps away" : "when an enemy next to you steps away";
                case "special": return $"when an enemy within {s.range:0.#} uses {(string.IsNullOrEmpty(s.ability) ? "a special attack" : "an ability that " + AbilityMatch.Describe(s.ability).Replace("deal ", "deals ").Replace("have ", "has ").Replace("are ", "is "))}" + (s.interrupt ? " (cutting it off)" : "");
                case "hurt": return $"when an enemy hits {side}";
                case "ranged": return $"when an enemy hits {side} from two or more tiles away";
                case "lowhealth": return $"when an enemy's hit takes {side} under {Mathf.Clamp(s.threshold, 1, 99)}% health";
                case "debuff": return $"when an enemy puts a debuff on {side}";
                case "dodge": return "when you dodge an enemy";
                case "buff": return "when an enemy puts a buff on itself or another enemy";
                case "heal": return "when an enemy heals itself or another enemy";
                case "summon": return "when an enemy calls in another enemy";
                case "allydown": return "when an enemy downs one of your allies";
                case "move": return $"when an enemy within {s.range:0.#} moves";
                case "turn": return $"when an enemy within {s.range:0.#} begins its turn";
                default: return "when " + s.on;
            }
        }

        /// <summary>The reaction's limits, after its trigger: condition, area, cooldown, stacking.</summary>
        internal static string TermsText(ReactionSpec s, string classId)
        {
            var bits = new List<string>();
            if (s.when != null) bits.Add("only while " + When.Describe(s.when, classId));
            if (s.radius > 0f) bits.Add($"everyone within {s.radius:0.#} of it is hit too");
            if (s.cooldown > 0) bits.Add($"then rests {s.cooldown} turn{(s.cooldown == 1 ? "" : "s")}");
            if (s.stack) bits.Add($"arming it again adds charges, up to {Cap(s)}");
            return bits.Count == 0 ? "" : " (" + string.Join("; ", bits) + ")";
        }

        internal static string Describe(ReactionSpec s, string classId)
        {
            var parts = Actions.Describe(s.actions, classId, "that enemy").Split('\n')
                .Select(l => l.Replace("• ", "").Trim().TrimEnd('.'))
                .Where(l => l.Length > 0)
                .Select(l => char.ToLowerInvariant(l[0]) + l.Substring(1));
            return $"Arms <color=#7FE0FF>{s.name}</color> ({Math.Max(1, s.charges)}x, {Math.Max(1, s.turns)} turns): {TriggerText(s)}, it {string.Join("; ", parts)}{TermsText(s, classId)}";
        }

        // ---- turn, round and save hooks -------------------------------------------------------------------------

        /// <summary>Each of your turns: reactions age and cool, the round's budget and per-enemy marks reset.</summary>
        internal static void OnPlayerTurn()
        {
            Ensure();
            firedThisRound = 0;
            if (armed.Count == 0) return;
            foreach (Armed r in armed) { r.turns--; if (r.cool > 0) r.cool--; r.firedOn.Clear(); }
            foreach (Armed r in armed.Where(r => r.turns <= 0).ToList())
            {
                Game.LOG($"<color=#9FB7C9>{r.spec.name} disarms.</color>");
                armed.Remove(r);
            }
            Save();
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
        private static class Patch_Turn
        {
            private static void Postfix() { try { OnPlayerTurn(); } catch (Exception e) { Plugin.Log.LogError("Reactions: " + e); } }
        }

        /// <summary>
        /// An enemy about to use an ability with a cooldown: "special" reactions answer first. If one cuts it off, or
        /// the enemy is dead, stunned or silenced afterwards, the ability is wasted (and on cooldown) instead.
        /// </summary>
        [HarmonyPatch(typeof(Ability), nameof(Ability.AbilityUse))]
        private static class Patch_Special
        {
            private static bool Prefix(Ability __instance, ref IEnumerator __result)
            {
                Character enemy = __instance.GetCaster();
                if (resolving || __instance is Ability_Player || Player.instance == null) return true;
                if (AbilityMatch.Fizzle(__instance, enemy)) { __result = Nothing(); return false; }
                if (!IsEnemy(enemy)) return true;
                Ensure();
                if (armed.Count == 0) return true;
                float dist = Vector3.Distance(enemy.transform.position, Player.instance.transform.position);
                var hits = Candidates("special", enemy, null).Where(r => dist <= r.spec.range
                    && (string.IsNullOrEmpty(r.spec.ability) ? __instance.cooldown > 0 : AbilityMatch.Matches(__instance, r.spec.ability))).ToList();
                if (hits.Count == 0) return true;
                bool cut = false;
                foreach (Armed r in hits)
                {
                    if (!Resolve(r, enemy, Player.instance)) continue;
                    r.firedOn.Add(enemy);
                    cut |= r.spec.interrupt;
                }
                if (!cut && !enemy.GetIsDead() && enemy.effects.GetEffect("EFFECT_Stun") == null && !Afflictions.IsSilenced(enemy)) return true;
                __instance.SetCooldownAmount(__instance.cooldown + 1);
                if (!enemy.GetIsDead())
                {
                    Game.LOG($"<color=#7FE0FF>{enemy.GetName()}'s {__instance.GetDisplayName()} is cut off!</color>");
                    if (HUD.instance != null) HUD.instance.MakeFct("<color=#7FE0FF>Cut off</color>", enemy.GetHitFx().position, false);
                }
                __result = Nothing();
                return false;
            }
        }

        private static IEnumerator Nothing() { yield break; }

        /// <summary>
        /// An enemy stepping onto a tile: "move" for any step within a reaction's range; "approach" onto a tile next to
        /// you (or, for "allies", next to one of your allies); "retreat" off one.
        /// </summary>
        [HarmonyPatch(typeof(NavNode), nameof(NavNode.OnEnterNode))]
        private static class Patch_Approach
        {
            private static void Postfix(NavNode __instance, Character c)
            {
                if (resolving || c == null || Game.Game_isLoading || Player.instance == null || !IsEnemy(c)) return;
                Ensure();
                if (Listening("move")) Fire("move", c, Player.instance);
                bool approach = Listening("approach");
                bool retreat = Listening("retreat");
                if (!approach && !retreat) return;
                var links = __instance.GetLinks();
                NavNode from = c.GetMover()?.GetPreviousNode();
                var fromLinks = from != null && from != __instance ? from.GetLinks() : null;
                // you first, then each ally: an approach is a step onto a tile next to them, a retreat a step off one
                var who = new List<Character> { Player.instance };
                if (AI_Manager.instance != null) who.AddRange(AI_Manager.instance.GetPlayerMinions().Where(a => a != null && !a.GetIsDead()));
                foreach (Character v in who)
                {
                    NavNode n = v.GetMover()?.GetCurrentNavNode();
                    if (n == null || n == __instance) continue;
                    bool nextTo = links.Contains(n), wasNextTo = fromLinks != null && fromLinks.Contains(n);
                    if (approach && nextTo) { Fire("approach", c, v); return; }
                    if (retreat && wasNextTo && !nextTo) { Fire("retreat", c, v); return; }
                }
            }
        }

        /// <summary>
        /// The enemies' half of the round: from the AI turn starting until you act again. Game.LAST_CHARACTER (the
        /// one whose turn it is) is only right then; during your own turn it still names the last enemy that moved.
        /// </summary>
        private static bool enemiesActing;

        /// <summary>For the self-tests, which play an enemy's turn by hand.</summary>
        internal static bool EnemiesActing { get => enemiesActing; set => enemiesActing = value; }

        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.DoAiTurn))]
        private static class Patch_AiTurn
        {
            private static void Prefix() => enemiesActing = true;
        }

        [HarmonyPatch(typeof(Game), nameof(Game.PlayerAction))]
        private static class Patch_YouAct
        {
            private static void Prefix() => enemiesActing = false;
        }

        [HarmonyPatch(typeof(Ability), nameof(Ability.AbilityUse))]
        private static class Patch_YouCast
        {
            private static void Prefix(Ability __instance) { if (__instance is Ability_Player) enemiesActing = false; }
        }

        /// <summary>The enemy whose turn it is, while the enemies act and it is close enough to matter.</summary>
        private static Character Acting(Character near)
        {
            if (!enemiesActing) return null;
            Character enemy = Game.LAST_CHARACTER;
            if (!IsEnemy(enemy) || near == null || Vector3.Distance(enemy.transform.position, near.transform.position) > 12f) return null;
            return enemy;
        }

        /// <summary>
        /// An effect landing on an enemy's turn: a debuff on you or an ally gets the "debuff" answer, a buff on an enemy
        /// (itself or another) the "buff" answer. The buffed enemy is the victim ("to": "victim" strips it).
        /// </summary>
        [HarmonyPatch(typeof(EffectManager), nameof(EffectManager.ApplyEffect), new[] { typeof(string), typeof(Character) })]
        private static class Patch_Debuff
        {
            private static void Postfix(Character target, Effect __result)
            {
                if (resolving || !enemiesActing || __result == null || target == null || Game.Game_isLoading || Player.instance == null) return;
                bool ours = target == Player.instance || IsAlly(target);
                if (__result.isDebuff && ours)
                {
                    Ensure();
                    if (!Listening("debuff")) return;
                    Character enemy = Acting(target);
                    if (enemy != null) Fire("debuff", enemy, target);
                }
                else if (!__result.isDebuff && !ours && IsEnemy(target))
                {
                    Ensure();
                    if (!Listening("buff")) return;
                    Character enemy = Acting(target);
                    if (enemy != null) Fire("buff", enemy, target);
                }
            }
        }

        /// <summary>An enemy healing itself or another enemy on its turn: the "heal" answer, the healed one the victim.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Heal), new[] { typeof(int), typeof(bool), typeof(string) })]
        private static class Patch_Heal
        {
            private static void Prefix(Character __instance, out int __state) => __state = __instance != null ? __instance.health : 0;

            private static void Postfix(Character __instance, int __state)
            {
                if (resolving || !enemiesActing || __instance == null || __instance.health <= __state || Game.Game_isLoading || Player.instance == null || !IsEnemy(__instance)) return;
                Ensure();
                if (!Listening("heal")) return;
                Character enemy = Acting(__instance);
                if (enemy != null) Fire("heal", enemy, __instance);
            }
        }

        /// <summary>One of your allies going down on an enemy's turn: the "allyDown" answer against the enemy acting.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Die))]
        private static class Patch_AllyDown
        {
            private static void Prefix(Character __instance)
            {
                if (resolving || !enemiesActing || __instance == null || __instance.GetIsDead() || !IsAlly(__instance) || Game.Game_isLoading || Player.instance == null) return;
                Ensure();
                if (!Listening("allyDown")) return;
                Character enemy = Acting(__instance);
                if (enemy != null) Fire("allyDown", enemy, __instance);
            }
        }

        /// <summary>An enemy calling in another on its turn: the "summon" answer; the newcomer is the victim.</summary>
        [HarmonyPatch(typeof(AI_Manager), nameof(AI_Manager.Spawn_Character), new[] { typeof(Character), typeof(Vector3), typeof(Quaternion), typeof(AI.AI_TEAM) })]
        private static class Patch_Summon
        {
            private static void Postfix(Character __result)
            {
                if (resolving || !enemiesActing || __result == null || Game.Game_isLoading || Player.instance == null || !IsEnemy(__result)) return;
                Ensure();
                if (!Listening("summon")) return;
                Character enemy = Acting(__result);
                if (enemy != null && enemy != __result) Fire("summon", enemy, __result);
            }
        }

        /// <summary>An enemy within range beginning its turn: the "turn" answer.</summary>
        [HarmonyPatch(typeof(AI), nameof(AI.TakeTurn))]
        private static class Patch_EnemyTurn
        {
            private static void Prefix(AI __instance)
            {
                try
                {
                    Character c = __instance != null ? __instance.GetCharacter() : null;
                    if (resolving || c == null || Game.Game_isLoading || Player.instance == null || !IsEnemy(c)) return;
                    Ensure();
                    if (Listening("turn")) Fire("turn", c, Player.instance);
                }
                catch (Exception e) { Plugin.Log.LogError("Reactions: " + e); }
            }
        }

        // ---- debug / tests --------------------------------------------------------------------------------------

        internal static int FiredThisRound => firedThisRound;
        internal static bool Busy => draining || queue.Count > 0;
    }
}
