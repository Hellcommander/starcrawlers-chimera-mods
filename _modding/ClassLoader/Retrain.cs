using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SCClassLoader
{
    /// <summary>
    /// Late-game retraining: Ctrl+right-click the highest skill you own in a tree on the skills screen and pay ChimeraLinq
    /// User Score and credits to give it up. The tier locks again and its skill point (and any perk point on it) comes
    /// back. Only the top of a tree can go, because tiers are bought in order, and a tree's free first
    /// tier stays. Nothing in the game reads the score: it only shows on ChimeraLinq, so spending it locks no posts.
    /// A full run of ChimeraLinq pays about 1500 at the very best, and wrong answers take it away.
    ///
    /// Vanilla only ever adds skills, so giving one up also has to clear what it left behind: effects its passive keeps
    /// on you (named in the skill's own code), the tag a tag passive gave, and the summons it made. [Retraining] in the
    /// config sets the prices.
    ///
    /// Ctrl+right-clicking a tree's own icon swaps the whole class: all its bought ranks come back as skill points and
    /// the new class starts at its free tier, with no starter gear. The creation screen's combination rules apply.
    /// Classes in [Retraining] KeptClasses can't be swapped out: their work outlives them (the Modder's and Gadgeteer's
    /// on your gear, Conjoined's in your other bodies).
    /// </summary>
    internal static class Retrain
    {
        private const string Score = "CHIMERALINQ_SCORE";
        internal const string Color = "#87D4E0";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> MinLevel, BaseCost, SwapCost, CreditsPerLevel, SwapCreditsPerLevel;
        internal static ConfigEntry<string> KeptClasses;
        internal static bool TestCtrl; // devtests

        private static readonly Dictionary<Type, HashSet<string>> named = new Dictionary<Type, HashSet<string>>();

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Retraining", "Enabled", true, "Ctrl+right-click the highest skill of a tree on the skills screen to give it up for ChimeraLinq User Score: its skill point comes back.");
            MinLevel = cfg.Bind("Retraining", "MinLevel", 20, "The character level retraining opens at.");
            BaseCost = cfg.Bind("Retraining", "BaseCost", 50, "User Score a retrain costs. A full run of ChimeraLinq pays about 1500 at the very best.");
            SwapCost = cfg.Bind("Retraining", "SwapCost", 200, "User Score swapping a whole class costs.");
            CreditsPerLevel = cfg.Bind("Retraining", "CreditsPerLevel", 100, "Credits a retrain costs on top of the User Score, per character level.");
            SwapCreditsPerLevel = cfg.Bind("Retraining", "SwapCreditsPerLevel", 300, "Credits a class swap costs on top of the User Score, per character level.");
            KeptClasses = cfg.Bind("Retraining", "KeptClasses", $"SKILLTREE_Modder,SKILLTREE_Gadgeteer,{Conjoined.ClassId}", "Class ids that can't be swapped out, because what they do outlives them. Comma separated.");
        }

        internal static int Cost => BaseCost.Value;
        internal static int ClassCost => SwapCost.Value;
        internal static int Credits => CreditsPerLevel.Value * Player.instance.level;
        internal static int ClassCredits => SwapCreditsPerLevel.Value * Player.instance.level;
        private static int Purse => Menu_Inventory.instance != null ? Menu_Inventory.instance.GetCredits() : 0;
        private static bool CanPay(int cost, int credits) => Have >= cost && Purse >= credits;
        private static string Price(int cost, int credits) => $"{cost} User Score and {credits} credits";
        private static string Owned => $"You have {Have} User Score and {Purse} credits.";
        private static int Have => Player.instance.HasTag(Score) ? Player.instance.GetTag(Score) : 0;

        /// <summary>The tier retraining can take from its tree: the highest one bought, never the free first tier.</summary>
        internal static bool IsTop(Skill_Tier t) =>
            t != null && t.isUnlocked && t.skillTree != null && t.skillTree.rank >= 1 && t.skillTree.rank < t.skillTree.skillTiers.Count
            && t.skillTree.skillTiers[t.skillTree.rank] == t;

        private static bool Open =>
            Enabled.Value && Menu_Skills.instance != null && Menu_Skills.instance.GetIsShowing() && !Menu_Skills.instance.isPreview
            && Menu_Skills.instance.ChimeraLinqEnabled && Player.instance != null && Player.instance.HasTag(Score);

        private static bool Ctrl => TestCtrl || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        internal static bool Kept(Skill_Tree t) =>
            KeptClasses.Value.Split(',').Any(x => x.Trim().Equals(t.internalName, StringComparison.OrdinalIgnoreCase));

        /// <summary>Why this can't be paid for right now; null when it can.</summary>
        private static string Blocked(int cost = -1, int credits = -1)
        {
            if (Player.instance.level < MinLevel.Value) return $"Retraining opens at level {MinLevel.Value}.";
            if (LevelManager.instance != null && LevelManager.instance.IsInCombat()) return "You can't retrain in combat.";
            if (cost < 0) { cost = Cost; credits = Credits; }
            if (!CanPay(cost, credits)) return $"This costs {Price(cost, credits)}. {Owned}";
            return null;
        }

        private static void Ask(Skill_Tier tier)
        {
            string no = Blocked();
            if (no != null) { Game.LOG($"<color=#FFD966>{no}</color>"); Menu_Skills.instance.PlayEvent("event:/UI/Error"); return; }
            string name = tier.displayName, tree = tier.skillTree.displayName;
            Ability a = tier.ability != null ? tier.ability : tier.passive;
            if (a != null) name = a.GetDisplayName();
            bool perk = a != null && a.perkPoints >= 1;
            int cost = Cost;
            Tooltip.instance?.Hide();
            Choice.Show("SCCL_Retrain", $"<color={Color}>Retrain {name}?</color>",
                $"You give up {name}, the highest skill you hold in {tree}, and get its skill point back{(perk ? " along with its perk point" : "")}. Any summons it made leave with it.\n\n"
                + $"This costs <color={Color}>{Price(cost, Credits)}</color>. {Owned}",
                new List<(string, Action, Color?)>
                {
                    ("Retrain", () => { Game.SetLock(true); Do(tier); }, UiKit.DangerColor),
                    ("Keep it", () => Game.SetLock(true), null),
                });
        }

        internal static void Do(Skill_Tier tier)
        {
            if (!IsTop(tier) || Blocked() != null) return;
            Player p = Player.instance;
            Skill_Tree tree = tier.skillTree;
            var skills = new[] { tier.ability, tier.passive }.Where(a => a != null).ToList();
            string name = skills.Count > 0 ? skills[0].GetDisplayName() : (string)tier.displayName;
            int cost = Cost, spent = Credits;
            Menu_Inventory.instance.AdjustCredits(-spent);
            p.SetTag(Score, Have - cost);

            Strip(tier);
            tree.SetRank(tree.rank - 1);
            tree.Refresh();
            Player_AbilityControl.instance.skillPoints++;
            Settle();
            Game.LOG($"<color={Color}>You gave up {name} for {Price(cost, spent)}, and got its skill point back.</color>");
            Plugin.Log.LogInfo($"Retrained {tier.internalName} of {tree.internalName} for {cost}: rank {tree.rank}, {Have} score left");
        }

        /// <summary>Takes one tier away, perk point and all, with everything it left on you.</summary>
        private static void Strip(Skill_Tier tier)
        {
            Player p = Player.instance;
            var skills = new[] { tier.ability, tier.passive }.Where(a => a != null).ToList();
            if (skills.Any(a => a.perkPoints >= 1)) tier.PerkCleared();
            tier.isUnlocked = false;
            foreach (Ability a in skills) { DropPets(tier, a); DropEffects(tier, a); DropTag(a); }
            if (!string.IsNullOrEmpty(tier.effect)) p.effects.ClearEffects(tier.effect, true);
            foreach (Ability a in skills) Player_AbilityControl.instance.RemoveAbility(a, a.transform.parent != null ? a.transform.parent : tier.transform);
        }

        private static void Settle()
        {
            Player p = Player.instance;
            p.CalculateStats();
            p.SetHealth(Math.Min(p.GetHealth(), p.GetHealthMax()));
            p.SetStamina(Math.Min(p.GetStamina(), p.GetStaminaMax()));
            Menu_HUD.instance.Refresh();
            Menu_Player.instance.Refresh();
            Menu_Inventory.instance.Refresh();
            Menu_Skills.instance.Show();
            Menu_Skills.instance.PlayEvent("event:/UI/Ability_Unlock");
        }

        // ---- swapping a whole class -----------------------------------------------------------------------------

        private static void AskSwap(Skill_Tree tree)
        {
            string no = Kept(tree) ? $"{tree.displayName} can't be swapped out: what it does stays with you." : Blocked(ClassCost, ClassCredits);
            if (no != null) { Game.LOG($"<color=#FFD966>{no}</color>"); Menu_Skills.instance.PlayEvent("event:/UI/Error"); return; }
            Tooltip.instance?.Hide();
            BodyPicker.ShowSwap(tree, id => Confirm(tree, id));
        }

        private static void Confirm(Skill_Tree tree, string id)
        {
            Skill_Tree next = Game.instance.skills.First(s => s != null && s.internalName == id);
            int cost = ClassCost, ranks = tree.rank;
            Choice.Show("SCCL_SwapClass", $"<color={Color}>Swap {tree.displayName} for {next.displayName}?</color>",
                $"You lose every {tree.displayName} skill and any summons they made. You get {(ranks == 1 ? "its 1 skill point" : $"its {ranks} skill points")} and any perk points back, and {next.displayName} starts at its first tier, with no starter gear.\n\n"
                + $"This costs <color={Color}>{Price(cost, ClassCredits)}</color>. {Owned}",
                new List<(string, Action, Color?)>
                {
                    ("Swap", () => { Game.SetLock(true); Swap(tree, id); }, UiKit.DangerColor),
                    ("Keep it", () => Game.SetLock(true), null),
                });
        }

        internal static void Swap(Skill_Tree tree, string id)
        {
            Skill_Tree prefab = Game.instance.skills.FirstOrDefault(s => s != null && s.internalName == id);
            Skill_Tree[] trees = Player_SkillControl.instance.skillTrees;
            int slot = Array.IndexOf(trees, tree);
            if (prefab == null || slot < 0 || Kept(tree) || trees.Any(t => t != null && t.internalName == id) || Blocked(ClassCost, ClassCredits) != null) return;
            Player p = Player.instance;
            int cost = ClassCost, spent = ClassCredits, ranks = Math.Max(0, tree.rank);
            string from = tree.displayName;
            Menu_Inventory.instance.AdjustCredits(-spent);
            p.SetTag(Score, Have - cost);

            foreach (Skill_Tier tier in tree.skillTiers.Where(t => t != null && t.isUnlocked).Reverse().ToList()) Strip(tier);
            foreach (Tag t in tree.playerTags ?? new Tag[0])
                if (t != null && !trees.Any(o => o != null && o != tree && (o.playerTags ?? new Tag[0]).Any(x => x != null && x.tagName == t.tagName))) p.RemoveTag(t.tagName);
            trees[slot] = null;
            UnityEngine.Object.Destroy(tree.gameObject);

            Skill_Tree fresh = UnityEngine.Object.Instantiate(prefab);
            fresh.transform.SetParent(Player_SkillControl.instance.transform);
            trees[slot] = fresh;
            fresh.Synch();
            fresh.SetRank(0);
            fresh.BeginGame();
            fresh.Refresh();
            Player_AbilityControl.instance.skillPoints += ranks;
            Settle();
            Game.LOG($"<color={Color}>You swapped {from} for {fresh.displayName} for {Price(cost, spent)}, and got {ranks} skill point{(ranks == 1 ? "" : "s")} back.</color>");
            Plugin.Log.LogInfo($"Swapped {from} for {id} for {cost}: {ranks} points back, {Have} score left");
        }

        /// <summary>
        /// Every string the skill's own code mentions, plus its string settings. Passives keep their effects by name, each
        /// in its own way (when unlocked, on a turn, on equipping a weapon), so this finds them all without a list.
        /// </summary>
        private static HashSet<string> Named(Ability a)
        {
            Type type = a.GetType();
            if (!named.TryGetValue(type, out HashSet<string> set))
            {
                set = new HashSet<string>();
                for (Type t = type; t != null && t != typeof(Ability_Player) && t != typeof(Ability); t = t.BaseType)
                    foreach (MethodInfo m in AccessTools.GetDeclaredMethods(t).Where(m => !m.IsAbstract && m.GetMethodBody() != null))
                        try { set.UnionWith(PatchProcessor.GetOriginalInstructions(m).Where(c => c.opcode == OpCodes.Ldstr).Select(c => (string)c.operand)); }
                        catch (Exception e) { Plugin.Log.LogWarning($"Retrain: can't read {t.Name}.{m.Name}: {e.Message}"); }
                named[type] = set;
            }
            var all = new HashSet<string>(set);
            foreach (FieldInfo f in AccessTools.GetDeclaredFields(type).Where(f => f.FieldType == typeof(string) && !f.IsStatic))
                if (f.GetValue(a) is string s) all.Add(s);
            return all;
        }

        /// <summary>A passive's effects all go; an active skill's timed buffs run out by themselves, its stances don't.</summary>
        private static void DropEffects(Skill_Tier tier, Ability a)
        {
            HashSet<string> names = Named(a);
            bool passive = a.isPassive || a == tier.passive;
            foreach (Effect e in Player.instance.effects.GetEffects().ToList())
                if (e != null && names.Contains(e.internalName) && (passive || e.duration < 0)) e.RemoveInstant();
        }

        private static void DropTag(Ability a)
        {
            Player p = Player.instance;
            if (a is PA_PASSIVE_Cybertechnician_Core) p.RemoveTag("CYBERTECHNICIAN_CORE");
            if (!(a is PA_PASSIVE_Tag t) || string.IsNullOrEmpty(t.addTag)) return;
            // another skill you still hold may give the same one
            bool kept = Player_AbilityControl.instance.GetAbilities().OfType<PA_PASSIVE_Tag>().Any(x => x != a && x.addTag == t.addTag);
            if (kept) return;
            p.RemoveTag(t.addTag);
            if (t.addTag == "ROBOT") p.isRobot = false;
        }

        /// <summary>The summons a skill made go with it: otherwise retraining a pet skill keeps the pet and frees the point.</summary>
        private static void DropPets(Skill_Tier tier, Ability a)
        {
            HashSet<Character> pets = Pets(a);
            // a pet another skill you keep also answers to stays: Form Boltron points at the Junker and the Turret
            foreach (Ability other in Player_AbilityControl.instance.GetAbilities().Where(x => x != null && x != tier.ability && x != tier.passive))
                pets.ExceptWith(Pets(other));
            foreach (Character c in pets) PetHandling.Unsummon(c, a.GetDisplayName(), breakdown: false);
        }

        private static HashSet<Character> Pets(Ability a)
        {
            var pets = new HashSet<Character>();
            for (Type t = a.GetType(); t != null && t != typeof(Ability_Player) && t != typeof(Ability); t = t.BaseType)
                foreach (FieldInfo f in AccessTools.GetDeclaredFields(t).Where(f => !f.IsStatic))
                {
                    object v = f.GetValue(a);
                    if (v is Character c && c != null) pets.Add(c);
                    else if (v is IEnumerable<Character> list) pets.UnionWith(list.Where(x => x != null));
                }
            var minions = AI_Manager.instance?.GetPlayerMinions() ?? new List<Character>();
            pets.RemoveWhere(c => !minions.Contains(c)); // a prefab field is what it spawns, not what's alive
            if (a is PA_Summon s)
            {
                IEnumerable<Character> kinds = (s.randomCharacterPrefabs ?? new Character[0]).Append(s.characterPrefab).Where(k => k != null);
                pets.UnionWith(minions.Where(m => m != null && kinds.Any(k => k.internalName == m.internalName)));
            }
            if (a is ModAbility mod)
            {
                var ctx = new ActionContext { source = mod };
                pets.UnionWith(Summons.Matching("self", ctx));
                foreach (ActionSpec act in mod.Spec?.actions ?? new List<ActionSpec>())
                    if (!string.IsNullOrEmpty(act.summonGroup)) pets.UnionWith(Summons.Matching("group:" + act.summonGroup, ctx));
            }
            return pets;
        }

        [HarmonyPatch(typeof(SkillSelect), nameof(SkillSelect.OnPointerClick))]
        private static class Patch_Click
        {
            private static bool Prefix(SkillSelect __instance, PointerEventData eventData)
            {
                if (eventData == null || eventData.button != PointerEventData.InputButton.Right || Game.RESOLVING) return true;
                if (!Ctrl || !Open) return true;
                if (__instance.skillTier == null && __instance.skillTree != null && Menu_Skills.instance.skillTrees.Contains(__instance)) { AskSwap(__instance.skillTree); return false; }
                if (!IsTop(__instance.skillTier)) return true;
                Ask(__instance.skillTier);
                return false;
            }
        }

        /// <summary>The prompt under a tree's top skill says what retraining it would cost.</summary>
        [HarmonyPatch(typeof(Skill_Tier), nameof(Skill_Tier.GetTooltipPrompt))]
        private static class Patch_Prompt
        {
            private static void Postfix(Skill_Tier __instance, ref string __result)
            {
                if (!Open || !IsTop(__instance) || Player.instance.level < MinLevel.Value) return;
                string line = (CanPay(Cost, Credits) ? "Ctrl+right-click to retrain: " : "Retraining costs ") + Price(Cost, Credits);
                __result = string.IsNullOrEmpty(__result) ? line : __result + "\n" + line;
            }
        }

        /// <summary>A tree icon's hover text says what swapping the class would cost, or that it can't be swapped.</summary>
        [HarmonyPatch(typeof(SkillSelect), nameof(SkillSelect.SetSkillTree))]
        private static class Patch_TreeTip
        {
            private static void Postfix(SkillSelect __instance, Skill_Tree sk)
            {
                if (!Open || Player.instance.level < MinLevel.Value || sk == null) return;
                Tooltip_Tiny tip = __instance.GetComponent<Tooltip_Tiny>();
                if (tip == null) return;
                tip.description = sk.description + "\n\n" + (Kept(sk) ? "This class can't be swapped out."
                    : (CanPay(ClassCost, ClassCredits) ? "Ctrl+right-click to swap this class: " : "Swapping this class costs ") + Price(ClassCost, ClassCredits));
            }
        }
    }
}
