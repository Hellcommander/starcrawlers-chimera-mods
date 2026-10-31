using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Transformations: an effect with a "kit". While it is on you the kit's abilities take over the hotbar and every
    /// other active tree ability is locked; commands (orders to your summons, portal, ChimeraLinq, beacon, special actions,
    /// weapon swap, plain attack, pet bar) and item abilities (worn gear, grenades and other usable items, rune skills) still work. When it ends (runs out, purged,
    /// death) the hotbar you had comes back.
    /// The effect is the only saved state: the hotbar follows it, so a save made mid-form loads back into the form.
    /// </summary>
    internal static class Forms
    {
        private class Def
        {
            public string effectId, classId;
            public List<string> kit;
            public List<string> keep;
            public bool keepOthers;
        }

        private static readonly Dictionary<string, Def> defs = new Dictionary<string, Def>();

        internal static void Clear() => defs.Clear();

        internal static void Register(string effectId, string classId, List<string> kit, List<string> keep, bool keepOtherClasses)
        {
            if (kit.Count == 0) return;
            defs[effectId] = new Def { effectId = effectId, classId = classId, kit = kit, keep = keep ?? new List<string>(), keepOthers = keepOtherClasses };
        }

        /// <summary>Self-test hook: flips keepOtherClasses on a registered form.</summary>
        internal static void SetKeepOthers(string effectId, bool on) { if (defs.TryGetValue(effectId, out Def d)) d.keepOthers = on; }
        internal static bool KeepsOthers(string effectId) => defs.TryGetValue(effectId, out Def d) && d.keepOthers;

        internal static bool IsKitAbility(string internalName) => defs.Values.Any(d => d.kit.Contains(internalName));

        // ---- runtime ------------------------------------------------------------------------------------------

        private static Def active;
        private static Menu_HUD activeHud;
        private static readonly List<Ability> allowed = new List<Ability>();
        /// <summary>Kit abilities already made, so their cooldowns carry over between transformations.</summary>
        private static readonly Dictionary<string, ModAbility> built = new Dictionary<string, ModAbility>();
        private static float nextCheck;

        /// <summary>The layout the transformation replaced (what the save gets), or null when not transformed.</summary>
        internal static string[] Stashed { get; private set; }
        internal static bool Active => active != null;
        internal static string ActiveEffectId => active?.effectId;

        /// <summary>Called every frame; enters or leaves the form to match the effects on you.</summary>
        internal static void Tick()
        {
            if (defs.Count == 0 && active == null) return;
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.2f;
            Player p = Player.instance;
            Menu_HUD hud = Menu_HUD.instance;
            if (p == null || hud == null || Player_AbilityControl.instance == null || Game.Game_isLoading) return;
            if (active != null && hud != activeHud) Drop(); // a rebuilt HUD already shows the saved, normal layout
            if (Game.RESOLVING || Game.TARGETING || hud.GetDraggingItem() != null) return;

            Def want = null;
            if (!p.GetIsDead() && p.effects != null)
                foreach (Effect e in p.effects.GetEffects())
                    if (e != null && e.internalName != null && defs.TryGetValue(e.internalName, out Def d)) { want = d; break; }
            if (want == active) return;
            try
            {
                if (active != null) Exit();
                if (want != null) Enter(want);
            }
            catch (Exception e) { Plugin.Log.LogError("Transformation: " + e); Drop(); }
        }

        private static void Enter(Def d)
        {
            Player_AbilityControl pac = Player_AbilityControl.instance;
            // synergy kit abilities ("ifSkill") only join when you have that other tree
            var kit = d.kit.Select(id => Build(id, d.classId)).Where(a => a != null && KitUnlocked(a.Spec)).ToList();
            foreach (ModAbility ab in kit) pac.LoadAbility(ab);

            var actives = pac.GetAbilities().Where(a => a != null && !a.isPassive && !kit.Contains(a)).ToList();
            // named in the form's keep list: usable, and put on the bar if it wasn't there
            var named = actives.Where(a => d.keep.Any(k =>
                string.Equals(k, a.GetDisplayName(), StringComparison.OrdinalIgnoreCase) || string.Equals(k, a.internalName, StringComparison.OrdinalIgnoreCase))).ToList();
            // orders to your summons, gear and rune abilities, and with keepOtherClasses everything not from this class: usable where they already sit
            var stay = actives.Where(a => !named.Contains(a) && ((a is ModAbility m && m.IsCommand) || FromItem(a)
                || (d.keepOthers && (a.skillTree == null || a.skillTree.internalName != d.classId)))).ToList(); // other trees, gear
            allowed.Clear();
            allowed.AddRange(kit);
            allowed.AddRange(named);
            allowed.AddRange(stay);

            // Consumables, commands and kept abilities stay where they were; the kit fills the first free slots.
            string[] before = HotbarPages.Visible();
            var layout = before.Select(n => n != null && (n.StartsWith("I_") || IsCommand(n) || allowed.Any(k => n == "A_" + k.internalName)) ? n : "EMPTY").ToArray();
            var queue = new Queue<string>(kit.Select(a => "A_" + a.internalName)
                .Concat(named.Select(a => "A_" + a.internalName).Where(n => !layout.Contains(n))));
            for (int i = 0; i < layout.Length && queue.Count > 0; i++)
                if (layout[i] == "EMPTY") layout[i] = queue.Dequeue();

            Stashed = before;
            active = d;
            activeHud = Menu_HUD.instance;
            HotbarPages.Replace(layout);
            string name = ModEffect.Specs.TryGetValue(d.effectId, out var rt) ? rt.spec.name : d.effectId;
            Game.LOG($"<color=#FFD15C>You take {name}.</color> Your other abilities wait until it ends.");
        }

        internal static bool KitUnlocked(AbilitySpec s) =>
            s == null || string.IsNullOrEmpty(s.ifSkill) || Synergies.TreeLevel(s.ifSkill) >= Math.Max(1, s.skillRank);

        private static void Exit()
        {
            Menu_HUD hud = Menu_HUD.instance;
            string[] back = Stashed;
            // Anything that landed on the form's bar meanwhile (a tier learned mid-form) goes back on the normal bar.
            var strays = hud.actionIconSlots.Where(s => s != null && s.actionIcon != null && s.actionIcon.ability != null)
                .Select(s => s.actionIcon.ability)
                .Where(a => !allowed.Contains(a) && back != null && !back.Contains("A_" + a.internalName)).ToList();
            string name = ModEffect.Specs.TryGetValue(active.effectId, out var rt) ? rt.spec.name : active.effectId;
            Drop();
            if (back != null) HotbarPages.Replace(back);
            foreach (Ability a in strays) hud.AddAbility(a);
            Game.LOG($"<color=#9FB7C9>{name} ends; your own abilities are back.</color>");
        }

        /// <summary>Forgets the form without touching the slots (the HUD was rebuilt or reloaded underneath it).</summary>
        private static void Drop()
        {
            Player_AbilityControl pac = Player_AbilityControl.instance;
            if (pac != null && active != null)
                foreach (string id in active.kit)
                    if (built.TryGetValue(id, out ModAbility ab) && ab != null) pac.abilities.Remove(ab);
            active = null;
            activeHud = null;
            Stashed = null;
            allowed.Clear();
        }

        private static ModAbility Build(string id, string classId)
        {
            Player_AbilityControl pac = Player_AbilityControl.instance;
            if (built.TryGetValue(id, out ModAbility ab) && ab != null && ab.transform.parent == pac.transform) return ab;
            var go = new GameObject(ModAbility.Prefix + id);
            go.transform.SetParent(pac.transform, false);
            ab = go.AddComponent<ModAbility>(); // Awake binds it to its spec by name
            ab.skillTree = Player_SkillControl.instance?.skillTrees?.FirstOrDefault(t => t != null && t.internalName == classId);
            ab.Unlock();
            built[id] = ab;
            return ab;
        }

        /// <summary>Utilities that live on the hotbar but aren't anyone's moves; a transformation leaves them alone.</summary>
        private static readonly HashSet<string> Commands = new HashSet<string> { "ChimeraLinq", "ApartmentPortal", "VendordamusBeacon" };

        private static bool IsCommand(string hotbarEntry) => hotbarEntry != null && hotbarEntry.StartsWith("A_") && Commands.Contains(hotbarEntry.Substring(2));

        /// <summary>
        /// Usable while transformed: the kit, kept abilities, commands (the hotbar utilities above, the special-action
        /// bar, the plain weapon attack and weapon swap) and item abilities. The pet command bar doesn't go through here.
        /// </summary>
        private static bool Allowed(Action_Icon icon, Ability a) =>
            allowed.Contains(a) || a is PA_AutoAttack || a is PA_WeaponSwap || Commands.Contains(a.internalName ?? "") || (icon != null && icon.isSpecialAction)
            || FromItem(a);

        /// <summary>
        /// An ability an item gives rather than a tree: a usable item's (grenades, dips, widgets; it stays under the item)
        /// or worn gear's (moved onto the player while worn, so the slots are checked), and skills that run on items:
        /// rune sockets, items spent per cast, or used on an item. Gear put on mid-form counts too.
        /// </summary>
        private static bool FromItem(Ability a)
        {
            if (a == null) return false;
            AbilitySpec s = (a as ModAbility)?.Spec;
            if (s != null && (s.runeSlots > 0 || (s.itemCost?.Count ?? 0) > 0 || !string.IsNullOrEmpty(s.itemTag) || !string.IsNullOrEmpty(s.itemId))) return true;
            if (a.GetComponentInParent<Item_OnUse_Trigger>() != null) return true;
            if (Game.PLAYER_ITEM_ABILITY is Item_Consumable c && c.useTrigger != null && c.useTrigger.use_ability == a) return true;
            Menu_Player mp = Menu_Player.instance;
            return mp != null && mp.slots != null && mp.slots.Any(s => s != null && s.item is Item_Equipment eq
                && eq.equipTrigger != null && eq.equipTrigger.equipment_ability == a);
        }

        // ---- patches ------------------------------------------------------------------------------------------

        /// <summary>While transformed, only the kit (and kept abilities, special actions and plain attacks) can be used.</summary>
        [HarmonyPatch(typeof(Player_AbilityControl), nameof(Player_AbilityControl.AbilitySelected))]
        private static class Patch_Lock
        {
            private static bool Prefix(Action_Icon icon, Ability a)
            {
                if (active == null || a == null || Allowed(icon, a)) return true;
                AudioManager.PlayEvent("event:/UI/UI_Error");
                string name = ModEffect.Specs.TryGetValue(active.effectId, out var rt) ? rt.spec.name : "this form";
                Game.LOG($"<color=#FF9F5C>{a.GetDisplayName()} can't be used in {name}.</color>");
                return false;
            }
        }

        /// <summary>
        /// The game loading slots by itself (a save, a Conjoined body swap) replaces whatever the form showed; start
        /// over from that layout and let Tick put the form back if the effect is still there.
        /// </summary>
        [HarmonyPatch(typeof(Menu_HUD), nameof(Menu_HUD.LoadHotbar))]
        private static class Patch_ExternalLoad
        {
            private static void Prefix()
            {
                if (active != null && !HotbarPages.Swapping) Drop();
            }
        }
    }
}
