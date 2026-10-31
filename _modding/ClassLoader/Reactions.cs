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
    /// special attack, hitting you or missing you. Reactions answer from a queue in priority order. They can't set off
    /// other reactions, and only a few go off per round. A "special" reaction runs before the enemy's ability and
    /// can cut it off. Armed reactions are saved in the mod's save slot.
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

        /// <summary>Every "arm" action of an ability, in order (actions, then the perk's addActions).</summary>
        private static List<ActionSpec> ArmActions(AbilitySpec s) =>
            (s.actions ?? new List<ActionSpec>()).Concat(s.perk?.addActions ?? new List<ActionSpec>())
            .Where(a => string.Equals(a.@do, "arm", StringComparison.OrdinalIgnoreCase) && a.reaction != null).ToList();

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
                var arms = ArmActions(rt.spec);
                if (idx < 0 || idx >= arms.Count) continue;
                armed.Add(new Armed { abilityId = p[0], armIndex = idx, charges = ch, turns = tu, spec = arms[idx].reaction, classId = rt.classId });
            }
        }

        private static void Save()
        {
            var data = SideAreas.Data;
            loadedFrom = data;
            if (data != null) data["reactions"] = armed.Select(r => $"{r.abilityId}|{r.armIndex}|{r.charges}|{r.turns}").ToArray();
        }

        // ---- arming ---------------------------------------------------------------------------------------------

        /// <summary>The "arm" action: sets (or refreshes) a reaction; a full set of slots drops the oldest.</summary>
        internal static void Arm(ActionSpec a, ActionContext ctx)
        {
            if (a.reaction == null || ctx.caster != Player.instance || !(ctx.source is ModAbility src) || src.Spec == null) return;
            Ensure();
            int idx = ArmActions(src.Spec).IndexOf(a);
            if (idx < 0) return;
            ReactionSpec s = a.reaction;
            Armed same = armed.FirstOrDefault(r => r.abilityId == src.internalName && r.armIndex == idx);
            if (same != null) armed.Remove(same);
            int slots = Slots;
            while (armed.Count >= slots)
            {
                Game.LOG($"<color=#9FB7C9>{armed[0].spec.name} is pushed out of its slot.</color>");
                armed.RemoveAt(0);
            }
            armed.Add(new Armed { abilityId = src.internalName, armIndex = idx, charges = Math.Max(1, s.charges), turns = Math.Max(1, s.turns), spec = s, classId = ctx.classId });
            Save();
            Game.LOG($"<color=#7FE0FF>{s.name} armed</color> ({Math.Max(1, s.charges)}x, {Math.Max(1, s.turns)} turns): {TriggerText(s)}.");
        }

        internal static void Clear() { Ensure(); armed.Clear(); queue.Clear(); Save(); }

        // ---- triggers -------------------------------------------------------------------------------------------

        private static bool IsEnemy(Character c) => c != null && !c.GetIsDead() && c != Player.instance && c.ai != null && !TileStacks.IsAlly(c)
                                                    && c.ai.GetTeam() != AI.AI_TEAM.Player && c.ai.GetTeam() != AI.AI_TEAM.Passive;

        private static bool Matches(Armed r, string on, Character victim) =>
            r.charges > 0 && string.Equals(r.spec.on, on, StringComparison.OrdinalIgnoreCase)
            && (victim == null || victim == Player.instance || string.Equals(r.spec.who, "allies", StringComparison.OrdinalIgnoreCase));

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
            armed.Where(r => Matches(r, on, victim) && !r.firedOn.Contains(enemy) && (on != "lowHealth" || CrossedLow(r, victim)))
                 .OrderByDescending(r => r.spec.priority).ToList();

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

        /// <summary>Runs one reaction now. False when it couldn't (spent, budget used up, enemy gone).</summary>
        private static bool Resolve(Armed r, Character enemy, Character victim)
        {
            if (r.charges <= 0 || !armed.Contains(r) || !IsEnemy(enemy) || Player.instance == null || Player.instance.GetIsDead()) return false;
            if (firedThisRound >= MaxPerRound) return false;
            firedThisRound++;
            r.charges--;
            var src = Player_AbilityControl.instance?.GetAbility(r.abilityId) as ModAbility;
            ActionContext ctx = src != null ? src.MakeContext() : new ActionContext { caster = Player.instance, classId = r.classId };
            ctx.sourceName = r.spec.name;
            ctx.target = enemy;
            ctx.attacker = enemy;
            ctx.victim = victim ?? Player.instance;
            resolving = true;
            try
            {
                Vector3 at = enemy.GetHitFx().position;
                ModAudio.Play(r.spec.sound, at, r.classId);
                Actions.SpawnVfx(r.spec.vfx ?? "PA_Elementalist_ChainLightning.shockImpact@0.5", at);
                if (HUD.instance != null) HUD.instance.MakeFct("<color=#7FE0FF>" + r.spec.name + "</color>", Player.instance.GetHitFx().position, false);
                Game.LOG($"<color=#7FE0FF>{r.spec.name}</color> answers {enemy.GetName()}.");
                Actions.Run(r.spec.actions, ctx, enemy);
                ProcHub.Fire("reaction", enemy);
            }
            catch (Exception e) { Plugin.Log.LogError($"Reaction {r.spec.name}: {e}"); }
            finally { resolving = false; }
            if (r.charges <= 0) armed.Remove(r);
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
                float reach = string.Equals(r.spec.on, "special", StringComparison.OrdinalIgnoreCase) ? r.spec.range : 6f;
                Character enemy = AI_Manager.instance.GetEnemiesAt(p.transform.position, reach).Where(IsEnemy)
                    .OrderBy(e => Vector3.Distance(e.transform.position, p.transform.position)).FirstOrDefault();
                if (enemy == null) continue;
                int before = firedThisRound;
                firedThisRound = 0;
                bool went = Resolve(r, enemy, p);
                firedThisRound = before;
                if (went) yield return new WaitForSeconds(0.25f);
            }
        }

        // ---- rules text -----------------------------------------------------------------------------------------

        internal static string TriggerText(ReactionSpec s)
        {
            bool allies = string.Equals(s.who, "allies", StringComparison.OrdinalIgnoreCase);
            switch ((s.on ?? "").ToLowerInvariant())
            {
                case "approach": return allies ? "when an enemy steps next to you or one of your allies" : "when an enemy steps next to you";
                case "retreat": return allies ? "when an enemy next to you or one of your allies steps away" : "when an enemy next to you steps away";
                case "special": return $"when an enemy within {s.range:0.#} uses a special attack" + (s.interrupt ? " (cutting it off)" : "");
                case "hurt": return allies ? "when an enemy hits you or one of your allies" : "when an enemy hits you";
                case "ranged": return allies ? "when an enemy hits you or one of your allies from two or more tiles away" : "when an enemy hits you from two or more tiles away";
                case "lowhealth": return $"when an enemy's hit takes {(allies ? "you or one of your allies" : "you")} under {Mathf.Clamp(s.threshold, 1, 99)}% health";
                case "debuff": return allies ? "when an enemy puts a debuff on you or one of your allies" : "when an enemy puts a debuff on you";
                case "dodge": return "when you dodge an enemy";
                default: return "when " + s.on;
            }
        }

        internal static string Describe(ReactionSpec s, string classId)
        {
            var parts = Actions.Describe(s.actions, classId, "that enemy").Split('\n')
                .Select(l => l.Replace("• ", "").Trim().TrimEnd('.'))
                .Where(l => l.Length > 0)
                .Select(l => char.ToLowerInvariant(l[0]) + l.Substring(1));
            return $"Arms <color=#7FE0FF>{s.name}</color> ({Math.Max(1, s.charges)}x, {Math.Max(1, s.turns)} turns): {TriggerText(s)}, it {string.Join("; ", parts)}";
        }

        // ---- turn, round and save hooks -------------------------------------------------------------------------

        /// <summary>Each of your turns: reactions age, the round's budget and per-enemy marks reset.</summary>
        internal static void OnPlayerTurn()
        {
            Ensure();
            firedThisRound = 0;
            if (armed.Count == 0) return;
            foreach (Armed r in armed) { r.turns--; r.firedOn.Clear(); }
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
                if (resolving || __instance.cooldown <= 0 || __instance is Ability_Player || !IsEnemy(enemy) || Player.instance == null) return true;
                Ensure();
                if (armed.Count == 0) return true;
                float dist = Vector3.Distance(enemy.transform.position, Player.instance.transform.position);
                var hits = Candidates("special", enemy, null).Where(r => dist <= r.spec.range).ToList();
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

        /// <summary>An enemy stepping onto a tile next to you (or, for "allies", next to one of your allies).</summary>
        [HarmonyPatch(typeof(NavNode), nameof(NavNode.OnEnterNode))]
        private static class Patch_Approach
        {
            private static void Postfix(NavNode __instance, Character c)
            {
                if (resolving || c == null || Game.Game_isLoading || Player.instance == null || !IsEnemy(c)) return;
                Ensure();
                bool approach = armed.Any(r => r.charges > 0 && string.Equals(r.spec.on, "approach", StringComparison.OrdinalIgnoreCase));
                bool retreat = armed.Any(r => r.charges > 0 && string.Equals(r.spec.on, "retreat", StringComparison.OrdinalIgnoreCase));
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

        /// <summary>An enemy's debuff landing on you or an ally: the enemy whose turn it is gets the "debuff" answer.</summary>
        [HarmonyPatch(typeof(EffectManager), nameof(EffectManager.ApplyEffect), new[] { typeof(string), typeof(Character) })]
        private static class Patch_Debuff
        {
            private static void Postfix(Character target, Effect __result)
            {
                if (resolving || !enemiesActing || __result == null || !__result.isDebuff || target == null || Game.Game_isLoading || Player.instance == null) return;
                if (target != Player.instance && !(target.ai != null && target.ai.GetTeam() == AI.AI_TEAM.Player)) return;
                Character enemy = Game.LAST_CHARACTER;
                if (!IsEnemy(enemy) || Vector3.Distance(enemy.transform.position, target.transform.position) > 12f) return;
                Ensure();
                if (!armed.Any(r => r.charges > 0 && string.Equals(r.spec.on, "debuff", StringComparison.OrdinalIgnoreCase))) return;
                Fire("debuff", enemy, target);
            }
        }

        // ---- debug / tests --------------------------------------------------------------------------------------

        internal static int FiredThisRound => firedThisRound;
        internal static bool Busy => draining || queue.Count > 0;
    }
}
