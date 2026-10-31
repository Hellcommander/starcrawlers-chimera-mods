using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// A synergy from a mod's "synergies" list: two skill trees that work well together. Take both and you get its
    /// bonus, a permanent effect whose level is the lower of the two trees' ranks (1 when both are just taken).
    /// </summary>
    public class SynergySpec
    {
        public string id;
        public string name;
        /// <summary>What it does, in a sentence (shown on the creation screen and on the effect).</summary>
        public string description;
        /// <summary>PNG next to the json; defaults to the icon of the first tree.</summary>
        public string icon;
        /// <summary>The trees needed: vanilla ids, mod class ids or display names; "A|B" = either one.</summary>
        public List<string> trees = new List<string>();
        public int maxLevel = 5;
        public bool enabled = true;
        /// <summary>The effect on you. Stats, resists and the per-stack multipliers count once per level; in its
        /// procs, perRank means per level.</summary>
        public EffectSpec bonus;
        /// <summary>Optional effect kept on your summons and pets while the synergy holds (same level rules).</summary>
        public EffectSpec allies;
        /// <summary>Active skills the synergy gives you while it holds (written like a tier's newAbility; "base"
        /// templates work). They join your abilities and hotbar, rank with the first tree, and go when it lapses.</summary>
        public List<AbilitySpec> grants;

        [JsonIgnore] public ModPack pack;
    }

    internal static class Synergies
    {
        internal const string EffectClass = "SCCL_Synergy";
        internal static readonly List<SynergySpec> All = new List<SynergySpec>();

        /// <summary>Tree specs already looked up (they're checked every turn).</summary>
        private static readonly Dictionary<string, List<Skill_Tree>> resolved = new Dictionary<string, List<Skill_Tree>>(StringComparer.OrdinalIgnoreCase);

        internal static string BonusId(SynergySpec s) => ModEffect.MakeId(EffectClass, s.id);
        internal static string AlliesId(SynergySpec s) => ModEffect.MakeId(EffectClass, s.id + "Allies");
        internal static string StrainId => ModEffect.MakeId(EffectClass, "SynergyStrain");
        internal static string StrainAlliesId => ModEffect.MakeId(EffectClass, "SynergyStrainAllies");

        internal static ConfigEntry<int> StrainPerLevel, StrainMax;

        internal static void Bind(ConfigFile cfg)
        {
            StrainPerLevel = cfg.Bind("Balance", "SynergyStrainPerLevel", 1, new ConfigDescription(
                "Synergy Strain offsets what synergies add: percent less damage, and 1 less max health, for each level across your active synergies. Summons take the damage cut only, from the synergies that buff them. 0 = no strain.", new AcceptableValueRange<int>(0, 10)));
            StrainMax = cfg.Bind("Balance", "SynergyStrainMax", 20, new ConfigDescription(
                "The most damage, in percent, that Synergy Strain can take away.", new AcceptableValueRange<int>(0, 50)));
        }

        // ---- loading ------------------------------------------------------------------------------------------

        internal static void Load()
        {
            All.Clear();
            resolved.Clear();
            foreach (ModPack p in ModPacks.Ordered)
                foreach (var (file, obj) in p.data)
                    foreach (JObject e in (obj["synergies"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        SynergySpec s;
                        if (e["grants"] is JArray ga)
                            for (int k = 0; k < ga.Count; k++)
                                if (ga[k] is JObject go) ga[k] = ClassRegistry.ResolveBase(go, file, 0);
                        try { s = e.ToObject<SynergySpec>(); }
                        catch (Exception ex) { Plugin.Log.LogError($"{file}: a synergy could not be read: {ex.Message}"); continue; }
                        if (string.IsNullOrEmpty(s.id)) { Plugin.Log.LogError($"{file}: a synergy is missing \"id\""); continue; }
                        All.RemoveAll(x => string.Equals(x.id, s.id, StringComparison.OrdinalIgnoreCase)); // a later mod replaces it
                        if (!s.enabled) continue;
                        s.trees = (s.trees ?? new List<string>()).Where(t => !string.IsNullOrEmpty(t)).ToList();
                        if (s.trees.Count < 2) { Plugin.Log.LogError($"{file}: synergy '{s.id}' needs at least two trees"); continue; }
                        // a synergy naming a class that isn't installed simply never comes up
                        if (s.trees.Any(t => Resolve(t).Count == 0)) { Plugin.Log.LogInfo($"Synergy '{s.id}' skipped: {string.Join(" + ", s.trees)} not all installed"); continue; }
                        s.pack = p;
                        s.maxLevel = Mathf.Clamp(s.maxLevel, 1, 10);
                        All.Add(s);
                    }
            RegisterEffects();
            RegisterGrants();
            if (All.Count > 0) Plugin.Log.LogInfo($"Synergies: {All.Count} ({string.Join(", ", All.Select(s => s.id))})");
        }

        private static void RegisterEffects()
        {
            foreach (var group in All.GroupBy(s => s.pack))
            {
                var effects = new List<EffectSpec>();
                foreach (SynergySpec s in group)
                {
                    string pair = string.Join(" + ", s.trees.Select(TreeNames));
                    EffectSpec b = s.bonus ?? new EffectSpec();
                    b.id = s.id;
                    b.name = "Synergy: " + (s.name ?? s.id);
                    b.description = $"<color=#7FE0C8>{pair}</color>\n{s.description}\nLevel = the lower rank of the two (up to {s.maxLevel}).";
                    b.icon = b.icon ?? s.icon;
                    Permanent(b, s);
                    effects.Add(b);
                    if (s.allies != null)
                    {
                        EffectSpec a = s.allies;
                        a.id = s.id + "Allies";
                        a.name = a.name ?? (s.name ?? s.id);
                        a.description = $"<color=#7FE0C8>Synergy ({pair})</color>\n{a.description ?? s.description}";
                        a.icon = a.icon ?? b.icon;
                        Permanent(a, s);
                        effects.Add(a);
                    }
                }
                ClassRegistry.RegisterEffects(new ClassDef { id = EffectClass, name = "Synergies", tiers = new List<TierDef>(), effects = effects, folder = group.Key?.folder }, null);
            }
            if (All.Count > 0)
                ClassRegistry.RegisterEffects(new ClassDef { id = EffectClass, name = "Synergies", tiers = new List<TierDef>(), effects = new List<EffectSpec> { Strain("SynergyStrain", true), Strain("SynergyStrainAllies", false) } }, null);
            // no png: the first tree's icon
            foreach (SynergySpec s in All)
                foreach (string id in new[] { BonusId(s), AlliesId(s) })
                    if (ModEffect.Specs.TryGetValue(id, out var rt) && rt.icon == null)
                        rt.icon = Resolve(s.trees[0]).Select(t => t.icon).FirstOrDefault(i => i != null);
        }

        private static void Permanent(EffectSpec e, SynergySpec s)
        {
            e.duration = -1;
            e.debuff = false;
            e.maxStacks = Math.Max(2, s.maxLevel); // >1 so stats scale per level
            e.rankFromStacks = true;
        }

        // ---- the cost of synergies --------------------------------------------------------------------------------

        private static int StrainPercent => Mathf.Clamp(StrainPerLevel.Value, 0, 10);
        private static int StrainCap => StrainPercent > 0 ? Mathf.Max(0, StrainMax.Value) / StrainPercent : 0;

        /// <summary>Stacks of strain for this many synergy levels (each stack is StrainPerLevel percent).</summary>
        internal static int StrainStacks(int levels) => Mathf.Min(levels, StrainCap);

        private static EffectSpec Strain(string id, bool health) => new EffectSpec
        {
            id = id, name = "Synergy Strain", duration = -1, maxStacks = Math.Max(2, StrainCap),
            description = "Cross-training pulls your focus apart: every level of your synergies costs a little power.",
            damageDealtPerStack = -StrainPercent / 100f,
            stats = health && StrainPercent > 0 ? new Dictionary<string, int> { { "HealthMax", -1 } } : null,
        };

        /// <summary>The Synergies tab's line on what your synergies cost you right now ("" for nothing).</summary>
        internal static string StrainText()
        {
            int levels = All.Sum(Strained), stacks = StrainStacks(levels);
            if (stacks <= 0) return "";
            string capped = stacks < levels ? " (the most it can cost)" : "";
            return $"<color=#FF9C7A>Synergy Strain</color>  <color=#FFD966>{levels} synergy level{(levels > 1 ? "s" : "")}</color>\n{stacks * StrainPercent}% less damage and {stacks} less max health{capped}.";
        }

        // ---- trees --------------------------------------------------------------------------------------------

        private static IEnumerable<Skill_Tree> GameTrees =>
            Game.instance != null && Game.instance.skills != null ? Game.instance.skills.Where(t => t != null) : Enumerable.Empty<Skill_Tree>();

        /// <summary>The trees a spec names ("A|B", internal ids or display names).</summary>
        internal static List<Skill_Tree> Resolve(string spec)
        {
            spec = spec ?? "";
            if (resolved.TryGetValue(spec, out var hit)) return hit;
            var r = new List<Skill_Tree>();
            foreach (string part in (spec ?? "").Split('|').Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                Skill_Tree t = GameTrees.FirstOrDefault(x => string.Equals(x.internalName, part, StringComparison.OrdinalIgnoreCase))
                               ?? GameTrees.FirstOrDefault(x => string.Equals((string)x.displayName, part, StringComparison.OrdinalIgnoreCase));
                if (t != null && !r.Contains(t)) r.Add(t);
            }
            if (Game.instance != null) resolved[spec] = r;
            return r;
        }

        internal static string TreeNames(string spec)
        {
            var trees = Resolve(spec);
            return trees.Count > 0 ? string.Join(" or ", trees.Select(t => (string)t.displayName)) : spec;
        }

        private static bool Matches(Skill_Tree t, string spec) => t != null && Resolve(spec).Any(x => x.internalName == t.internalName);

        /// <summary>Your level in a tree spec: rank + 1 of the best matching tree you have (0 = you don't have it).</summary>
        internal static int TreeLevel(string spec)
        {
            var mine = Player_SkillControl.instance?.skillTrees;
            if (mine == null) return 0;
            int best = 0;
            foreach (Skill_Tree t in mine)
                if (t != null && Matches(t, spec)) best = Math.Max(best, t.GetRank() + 1);
            return best;
        }

        /// <summary>The synergy's level for you right now (0 = not active).</summary>
        internal static int Level(SynergySpec s)
        {
            int lvl = int.MaxValue;
            foreach (string t in s.trees) lvl = Math.Min(lvl, TreeLevel(t));
            return Mathf.Clamp(lvl, 0, s.maxLevel);
        }

        private static string ChoiceTag(SynergySpec s) => "SCCL_SYNERGY_SET_" + s.id;

        /// <summary>The level you actually use: its full level, unless you turned it down on the Synergies tab.</summary>
        internal static int Applied(SynergySpec s)
        {
            int lvl = Level(s), chosen = Player.instance != null ? Player.instance.GetTag(ChoiceTag(s)) : -1;
            return chosen >= 0 ? Math.Min(lvl, chosen) : lvl;
        }

        /// <summary>Changes wait until the fight is over, so a synergy can't be dropped just for one big hit.</summary>
        internal static bool ChoiceLocked => CombatClarity.InCombat;

        /// <summary>Turns a synergy to this level. Its full level clears the choice, so it goes on growing with your ranks.</summary>
        internal static void Choose(SynergySpec s, int level)
        {
            Player p = Player.instance;
            if (p == null || ChoiceLocked) return;
            level = Mathf.Clamp(level, 0, Level(s));
            if (level >= Level(s)) p.RemoveTag(ChoiceTag(s));
            else p.SetTag(ChoiceTag(s), level);
            Refresh(false);
        }

        private static IEnumerable<ModAbility> Priced =>
            (Player_AbilityControl.instance?.GetAbilities() ?? new List<Ability>()).OfType<ModAbility>().Where(a => a.Spec?.synergyPenalty != null);

        private static bool Has(SynergySpec s, string classId) => s.trees.Any(t => Resolve(t).Any(x => x.internalName == classId));

        /// <summary>The skills of yours that pay for this synergy with their own price instead of Synergy Strain.</summary>
        internal static List<ModAbility> PricedBy(SynergySpec s) => Priced.Where(a => Has(s, a.ClassId)).ToList();

        /// <summary>The levels you use across the synergies this class is part of.</summary>
        internal static int LevelsOf(string classId) => All.Where(s => Has(s, classId)).Sum(Applied);

        /// <summary>Levels that count towards Synergy Strain: a synergy priced by a skill doesn't.</summary>
        private static int Strained(SynergySpec s) => PricedBy(s).Count > 0 ? 0 : Applied(s);

        /// <summary>Would these trees (a creation-screen pick) make the synergy?</summary>
        internal static bool MadeBy(SynergySpec s, IList<Skill_Tree> trees) =>
            s.trees.All(spec => trees.Any(t => Matches(t, spec)));

        internal static IEnumerable<SynergySpec> Involving(Skill_Tree t) => All.Where(s => s.trees.Any(spec => Matches(t, spec)));

        // ---- keeping the effects on -----------------------------------------------------------------------------

        /// <summary>Puts each synergy's effect on you (and your allies) at its level, or takes it off. Cheap; runs every turn.</summary>
        // ---- granted skills -------------------------------------------------------------------------------

        private static string GrantId(SynergySpec s, int k) => $"{EffectClass}_{new string(s.id.Where(char.IsLetterOrDigit).ToArray())}_G{k}";

        /// <summary>Granted skills are registered like any mod ability, under the synergy's first tree.</summary>
        private static void RegisterGrants()
        {
            foreach (SynergySpec s in All)
                for (int k = 0; k < (s.grants?.Count ?? 0); k++)
                {
                    AbilitySpec a = s.grants[k];
                    if (a == null || string.IsNullOrEmpty(a.name)) { Plugin.Log.LogError($"Synergy '{s.id}': grant {k} needs a \"name\""); continue; }
                    ClassRegistry.Normalize(a);
                    if (a.type == "passive") { Plugin.Log.LogWarning($"Synergy '{s.id}': grant '{a.name}' is passive; put passive parts in the bonus"); continue; }
                    string id = GrantId(s, k), cls = s.trees[0];
                    ModAbility.Specs[id] = new ModAbility.Runtime
                    {
                        spec = a, classId = cls, internalName = id,
                        icon = ClassRegistry.LoadSprite(s.pack?.folder, a.icon ?? s.icon),
                        name = ClassRegistry.Term(EffectClass, id + "Name", a.name),
                        desc = ClassRegistry.Term(EffectClass, id + "Desc", a.description ?? ""),
                        perkName = ClassRegistry.Term(EffectClass, id + "PerkName", a.perk?.name ?? a.name + "+"),
                        perkDesc = ClassRegistry.Term(EffectClass, id + "PerkDesc", a.perk?.description ?? ""),
                    };
                }
        }

        private static readonly Dictionary<string, ModAbility> granted = new Dictionary<string, ModAbility>();

        /// <summary>Puts the synergy's skills on you (and your hotbar) while it holds, and takes them away when not.</summary>
        private static void KeepGrants(SynergySpec s, bool on)
        {
            Player_AbilityControl pac = Player_AbilityControl.instance;
            if (pac == null || s.grants == null) return;
            for (int k = 0; k < s.grants.Count; k++)
            {
                string id = GrantId(s, k);
                if (!ModAbility.Specs.ContainsKey(id)) continue;
                granted.TryGetValue(id, out ModAbility ab);
                if (on)
                {
                    if (ab == null || ab.transform.parent != pac.transform)
                    {
                        var go = new GameObject(ModAbility.Prefix + id);
                        go.transform.SetParent(pac.transform, false);
                        ab = go.AddComponent<ModAbility>(); // Awake binds it to its spec by name
                        string tree = s.trees[0];
                        ab.skillTree = Player_SkillControl.instance?.skillTrees?.FirstOrDefault(t => t != null && Matches(t, tree));
                        ab.Unlock();
                        granted[id] = ab;
                    }
                    if (!pac.GetAbilities().Contains(ab)) pac.LoadAbility(ab);
                    Menu_HUD hud = Menu_HUD.instance;
                    if (hud == null || Forms.Active) continue;
                    bool onBar = hud.actionIconSlots.Any(sl => sl != null && sl.actionIcon != null && sl.actionIcon.ability == ab);
                    // a skill that rides on another is reached by right-clicking that one, unless it isn't on the bar
                    bool riding = !string.IsNullOrEmpty(ab.Spec?.attachTo) && BonusSkills.ParentOnBar(ab);
                    if (riding && onBar)
                        foreach (var sl in hud.actionIconSlots.Where(sl => sl != null && sl.actionIcon != null && sl.actionIcon.ability == ab).ToList())
                        { UnityEngine.Object.Destroy(sl.actionIcon.gameObject); sl.ClearActionIcon(); }
                    else if (!riding && !onBar) hud.AddAbility(ab);
                }
                else if (ab != null)
                {
                    Menu_HUD hud = Menu_HUD.instance;
                    if (hud != null)
                        foreach (var sl in hud.actionIconSlots.Where(sl => sl != null && sl.actionIcon != null && sl.actionIcon.ability == ab).ToList())
                        { UnityEngine.Object.Destroy(sl.actionIcon.gameObject); sl.ClearActionIcon(); }
                    pac.abilities.Remove(ab);
                    UnityEngine.Object.Destroy(ab.gameObject);
                    granted.Remove(id);
                }
            }
        }

        internal static ModAbility Granted(string synergyId, int k = 0)
        {
            SynergySpec s = All.FirstOrDefault(x => string.Equals(x.id, synergyId, StringComparison.OrdinalIgnoreCase));
            return s != null && granted.TryGetValue(GrantId(s, k), out ModAbility ab) ? ab : null;
        }

        internal static void Refresh(bool announce = true)
        {
            Player p = Player.instance;
            if (p == null || p.effects == null || EffectManager.instance == null || Player_SkillControl.instance == null || All.Count == 0) return;
            bool changed = false;
            var minions = AI_Manager.instance != null ? AI_Manager.instance.GetPlayerMinions().Where(c => c != null && !c.GetIsDead()).ToList() : new List<Character>();
            int levels = 0, allied = 0;
            foreach (SynergySpec s in All)
            {
                int lvl = Applied(s);
                int strained = Strained(s);
                levels += strained;
                if (s.allies != null) allied += strained;
                if (s.grants != null && s.grants.Count > 0) KeepGrants(s, lvl > 0);
                // the effect is saved with the game, so a load finds it at its level and says nothing
                int before = Keep(p, BonusId(s), lvl);
                changed |= before != lvl;
                if (s.allies != null)
                    foreach (Character c in minions) Keep(c, AlliesId(s), lvl);
                if (!announce || Game.Game_isLoading || lvl <= before) continue;
                string name = $"<color=#7FE0C8>{s.name ?? s.id}</color>";
                Game.LOG(before == 0 ? $"Synergy: {name} ({string.Join(" + ", s.trees.Select(TreeNames))}). {s.description}" : $"Synergy {name} grows to level {lvl}.");
            }
            int strain = StrainStacks(levels), strainBefore = Keep(p, StrainId, strain);
            changed |= strainBefore != strain;
            foreach (Character c in minions) Keep(c, StrainAlliesId, StrainStacks(allied));
            if (announce && !Game.Game_isLoading && strainBefore == 0 && strain > 0)
                Game.LOG("<color=#FF9C7A>Synergy Strain:</color> your synergies cost a little damage and health. The character screen's Synergies tab has the numbers.");
            if (changed) foreach (ModAbility a in Priced) a.Reprice();
            if (changed && Menu_HUD.instance != null) Menu_HUD.instance.UpdatePlayerEffects();
        }

        /// <summary>Sets the effect to the level (0 = off) and returns the level it had.</summary>
        private static int Keep(Character c, string id, int lvl)
        {
            Effect e = c.effects.GetEffect(id);
            int before = e is ModEffect had ? had.Stacks : e != null ? 1 : 0;
            if (lvl <= 0)
            {
                if (e == null) return 0;
                c.effects.RemoveEffect(id, true);
                c.CalculateStats();
                return before;
            }
            if (e == null)
            {
                e = EffectManager.instance.ApplyEffect(id, c);
                if (e == null) return 0;
                e.caster = Player.instance;
                e.Activate();
            }
            if (e is ModEffect m && m.Stacks != lvl) m.SetStacks(lvl);
            if (before != lvl) c.CalculateStats();
            // kept off the buff bar (they never run out, and a few classes make a long row of them); the
            // character screen's Synergies tab lists them instead
            if (!e.isHidden)
            {
                e.isHidden = true;
                if (c == Player.instance && Menu_HUD.instance != null) Menu_HUD.instance.UpdatePlayerEffects();
            }
            return before;
        }

        internal static bool IsSynergyEffect(Effect e) =>
            e != null && e.internalName != null && e.internalName.StartsWith(ModEffect.MakeId(EffectClass, ""), StringComparison.Ordinal);

        /// <summary>A loaded game brings the effects back before the first turn re-hides them: hide them here too.</summary>
        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.UpdatePlayerEffects))]
        private static class Patch_HideIcons
        {
            private static void Prefix()
            {
                if (Player.instance?.effects == null) return;
                foreach (Effect e in Player.instance.effects.GetEffects())
                    if (IsSynergyEffect(e)) e.isHidden = true;
            }
        }

        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.Turn))]
        private static class Patch_Turn
        {
            private static void Postfix()
            {
                try { Refresh(); }
                catch (Exception e) { Plugin.Log.LogError("Synergies: " + e); }
            }
        }

        /// <summary>A rank bought on the skills screen counts at once.</summary>
        [HarmonyPatch(typeof(Skill_Tree), nameof(Skill_Tree.SetRank))]
        private static class Patch_Rank
        {
            private static void Postfix()
            {
                if (Game.Game_isLoading || Player.instance == null) return;
                try { Refresh(); }
                catch (Exception e) { Plugin.Log.LogError("Synergies: " + e); }
            }
        }

        // ---- the creation screen ------------------------------------------------------------------------------

        /// <summary>
        /// Hovering a tree shows what it combines with in a panel beside its icon; the ones your picked trees already
        /// make are ticked. (The description box below the grid has no room: long class descriptions fill it alone.)
        /// </summary>
        [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.Skill_Highlight))]
        private static class Patch_Highlight
        {
            private static float baseSize;

            private static void Postfix(Menu_Creation __instance, SkillSelect s)
            {
                if (s == null || s.skillTree == null || __instance.skillDescription == null) return;
                var box = __instance.skillDescription;
                // a long class description can outgrow the box: shrink it to fit instead of spilling over the grid.
                // The text rect is smaller than its frame (vanilla text is short and simply overflows it), so first
                // stretch it to fill the frame, keeping its left edge clear of the tree name.
                if (baseSize <= 0f)
                {
                    baseSize = box.fontSize;
                    FillFrame(box.rectTransform);
                }
                box.enableAutoSizing = true;
                box.fontSizeMax = baseSize;
                box.fontSizeMin = baseSize * 0.6f;
                string text = CreationText(s.skillTree, __instance.selectedSkills.Where(x => x != null && x.skillTree != null).Select(x => x.skillTree).ToList());
                SynergyTip.Show(__instance, s, text, baseSize);
            }

            private static void FillFrame(RectTransform r)
            {
                if (!(r.parent is RectTransform frame)) return;
                var c = new Vector3[4];
                r.GetWorldCorners(c);
                float left = frame.InverseTransformPoint(c[0]).x - frame.rect.xMin;
                const float padV = 8f, padR = 14f;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(left, padV);
                r.offsetMax = new Vector2(-padR, -padV);
            }
        }

        [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.Skill_DeHighlight))]
        private static class Patch_DeHighlight
        {
            private static void Postfix() => SynergyTip.Hide();
        }

        [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.BTN_BeginGame))]
        private static class Patch_Begin
        {
            private static void Prefix() => SynergyTip.Hide();
        }

        /// <summary>Leaving the creation screen any way (Back, a preset, starting) closes the panel.</summary>
        [HarmonyPatch(typeof(Menu_Creation), nameof(Menu_Creation.Hide))]
        private static class Patch_MenuHide
        {
            private static void Postfix() => SynergyTip.Hide();
        }

        internal static string CreationText(Skill_Tree tree, List<Skill_Tree> picked)
        {
            var mine = Involving(tree).ToList();
            if (mine.Count == 0) return "";
            if (!picked.Contains(tree)) picked = picked.Concat(new[] { tree }).ToList();
            string Partner(SynergySpec s) => string.Join(" + ", s.trees.Where(spec => !Matches(tree, spec)).Select(TreeNames));
            var lit = mine.Where(s => MadeBy(s, picked)).ToList();
            var rest = mine.Except(lit).ToList();
            var lines = new List<string>();
            const int full = 6;
            // the game font has no check-mark glyph: active ones say so in words
            foreach (SynergySpec s in lit) lines.Add($"<color=#7FE0C8>{s.name} - active</color> <color=#9FB7C9>(with {Partner(s)})</color>\n{s.description}");
            foreach (SynergySpec s in rest.Take(Math.Max(0, full - lit.Count))) lines.Add($"<color=#FFD15C>{s.name}</color> <color=#9FB7C9>(with {Partner(s)})</color>\n{s.description}");
            var more = rest.Skip(Math.Max(0, full - lit.Count)).ToList();
            if (more.Count > 0) lines.Add($"<color=#9FB7C9>Also combines with {string.Join(", ", more.Select(Partner).Distinct())}.</color>");
            return "<b>Synergies</b>\n" + string.Join("\n<size=40%>\n</size>", lines);
        }
    }

    /// <summary>The floating synergy panel beside a hovered tree icon (creation screen, Conjoined body picker).</summary>
    internal static class SynergyTip
    {
        private const float Width = 460f, Pad = 12f, Gap = 10f;
        private static RectTransform panel;
        private static TMPro.TextMeshProUGUI label;

        internal static void Show(Menu_Creation mc, SkillSelect s, string text, float fontSize)
        {
            // inside the menu's content, so it goes when the menu does (Back to the main menu, starting a run);
            // never over the description box below the grid: that's what the player is reading
            Transform home = mc.content != null ? mc.content.transform : mc.transform;
            Show(home, s.transform as RectTransform, mc.skillDescription.transform.parent as RectTransform, text, fontSize, UiKit.FontFrom(mc));
        }

        /// <summary>Beside `icon` (on whichever side has room), parented to `home`, kept above `floorFrame` if given.</summary>
        internal static void Show(Transform home, RectTransform icon, RectTransform floorFrame, string text, float fontSize, TMPro.TMP_FontAsset font)
        {
            if (string.IsNullOrEmpty(text) || icon == null || home == null) { Hide(); return; }
            Canvas root = icon.GetComponentInParent<Canvas>()?.rootCanvas;
            if (root == null) return;
            if (panel == null || panel.transform.parent != home) Build(home, font, fontSize);

            label.text = text;
            Vector2 pref = label.GetPreferredValues(text, Width - 2 * Pad, 0f);
            panel.sizeDelta = new Vector2(Width, pref.y + 2 * Pad);

            var rootRt = (RectTransform)root.transform;
            var corners = new Vector3[4];
            icon.GetWorldCorners(corners); // bl, tl, tr, br
            Vector2 tl = rootRt.InverseTransformPoint(corners[1]), tr = rootRt.InverseTransformPoint(corners[2]);
            Rect area = rootRt.rect;
            float scale = panel.lossyScale.x / Mathf.Max(0.0001f, rootRt.lossyScale.x);
            float w = Width * scale, h = panel.sizeDelta.y * scale;
            float x = tr.x + Gap * scale;
            if (x + w > area.xMax) x = tl.x - Gap * scale - w;
            float floor = area.yMin;
            if (floorFrame != null)
            {
                floorFrame.GetWorldCorners(corners);
                floor = rootRt.InverseTransformPoint(corners[1]).y + Gap * scale;
            }
            float y = Mathf.Clamp(Mathf.Max(tl.y, floor + h), area.yMin + h, area.yMax);
            panel.position = rootRt.TransformPoint(new Vector3(x, y, 0f));
            panel.SetAsLastSibling();
            panel.gameObject.SetActive(true);
        }

        /// <summary>What the panel shows right now ("" when hidden).</summary>
        internal static string Text => panel != null && panel.gameObject.activeSelf ? label.text : "";

        internal static void Hide()
        {
            if (panel != null) panel.gameObject.SetActive(false);
        }

        private static void Build(Transform home, TMPro.TMP_FontAsset font, float fontSize)
        {
            if (panel != null) UnityEngine.Object.Destroy(panel.gameObject);
            panel = UiKit.Panel(home, "SCCL_SynergyTip", new Vector2(Width, 100f), UiKit.PanelColor);
            panel.pivot = new Vector2(0f, 1f);
            UiLook.Attach(panel, "SynergyTip");
            // drawn over the skill grid
            var c = panel.gameObject.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder = 500;
            var outline = panel.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = new Color(0.45f, 0.85f, 1f, 0.6f);
            outline.effectDistance = new Vector2(2f, -2f);
            label = UiKit.Label(panel, "", font, Mathf.Max(14f, fontSize * 0.8f), TMPro.TextAlignmentOptions.TopLeft);
            label.enableWordWrapping = true;
            label.overflowMode = TMPro.TextOverflowModes.Overflow;
            var lr = label.rectTransform;
            lr.offsetMin = new Vector2(Pad, Pad);
            lr.offsetMax = new Vector2(-Pad, -Pad);
            panel.gameObject.SetActive(false);
        }
    }
}
