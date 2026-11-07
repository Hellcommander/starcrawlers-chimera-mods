using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCClassLoader
{
    /// <summary>
    /// Loaded skills (AbilitySpec.loads): one skill that can use any of several items, and does what the item it is
    /// loaded with does. A quick right-click on its hotbar icon opens a picker with the items it takes and how many you
    /// carry; the pick is kept per skill in the save (SideAreas.Data "loads"). Each cast spends that item (LoadSpec.count)
    /// and is retuned by it as a rune retunes a socketed skill (element, reagent, extra actions, fields, shape...).
    /// When the picked item runs out the skill goes on with the next one in its list you still carry, and it can't be
    /// used with none of them. The item a cast uses is held for the whole cast, so spending the last one doesn't change
    /// what that cast does.
    /// </summary>
    internal static class Loads
    {
        internal const string Key = "loads";

        private static Dictionary<string, string> Store
        {
            get
            {
                var data = SideAreas.Data;
                if (data == null) return null;
                if (!(data.TryGetValue(Key, out object v) && v is Dictionary<string, string> d)) data[Key] = d = new Dictionary<string, string>();
                return d;
            }
        }

        /// <summary>The item each skill is holding for the cast in progress.</summary>
        private static readonly Dictionary<string, LoadSpec> casting = new Dictionary<string, LoadSpec>();

        internal static string Resolve(LoadSpec l)
        {
            if (l == null || string.IsNullOrEmpty(l.item)) return null;
            if (l.resolved != null) return l.resolved;
            string id = Crafting.ResolveItem(l.item, null);
            // mod items may not be registered yet while classes build: only keep an answer once the game is running
            if (Menu_Inventory.instance != null) l.resolved = id;
            return id;
        }

        /// <summary>How many of this load you carry.</summary>
        internal static int Have(LoadSpec l)
        {
            string id = Resolve(l);
            // classes build before there is an inventory
            return id == null || Menu_Inventory.instance == null ? 0 : Crafting.Count(new CostSpec { item = id, resolved = id, count = 1 });
        }

        internal static bool Enough(LoadSpec l) => Resolve(l) != null && Have(l) >= Mathf.Max(1, l.count);

        internal static string NameOf(LoadSpec l) => !string.IsNullOrEmpty(l?.name) ? l.name : ItemRegistry.NameOf(Resolve(l));

        /// <summary>The load you picked for this skill (whether or not you still carry it), else the first in its list.</summary>
        internal static LoadSpec Picked(string abilityId, AbilitySpec s)
        {
            if (s?.loads == null || s.loads.Count == 0) return null;
            var store = Store;
            if (store != null && abilityId != null && store.TryGetValue(abilityId, out string id))
            {
                LoadSpec hit = s.loads.FirstOrDefault(l => Resolve(l) == id);
                if (hit != null) return hit;
            }
            return s.loads[0];
        }

        /// <summary>What a cast would use right now: the item held for a cast in progress, else your pick while you carry
        /// enough of it, else the first load in the list you carry; null when you carry none.</summary>
        internal static LoadSpec Current(string abilityId, AbilitySpec s)
        {
            if (s?.loads == null || s.loads.Count == 0 || abilityId == null) return null;
            if (casting.TryGetValue(abilityId, out LoadSpec held)) return held;
            LoadSpec pick = Picked(abilityId, s);
            if (pick != null && Enough(pick)) return pick;
            return s.loads.FirstOrDefault(Enough);
        }

        internal static void Choose(string abilityId, LoadSpec l)
        {
            var store = Store;
            string id = Resolve(l);
            if (store == null || abilityId == null || id == null) return;
            store[abilityId] = id;
        }

        /// <summary>Holds this cast's item and spends it (unless paid already: a charged skill going off). Null when the
        /// skill takes no loads or you carry none.</summary>
        internal static LoadSpec Begin(string abilityId, AbilitySpec s, bool pay, string skillName)
        {
            LoadSpec l = Current(abilityId, s);
            if (l == null) return null;
            casting[abilityId] = l;
            if (pay && !Consumables.KeepsCost(skillName))
            {
                string id = Resolve(l);
                Crafting.Consume(new CostSpec { item = id, resolved = id, count = Mathf.Max(1, l.count) });
                LastSpent = id;
            }
            return l;
        }

        /// <summary>The item the last paid cast spent (devtests: a skill that also finds items can't be checked by counting).</summary>
        internal static string LastSpent;

        internal static void End(string abilityId) { if (abilityId != null) casting.Remove(abilityId); }

        /// <summary>One line of rules text for a load in this skill.</summary>
        internal static string Describe(LoadSpec l, string classId)
        {
            if (l == null) return "";
            if (!string.IsNullOrEmpty(l.description)) return l.description;
            return Runes.Describe(l, classId);
        }

        /// <summary>The tooltip block: what it is loaded with and what the others would do.</summary>
        internal static string TipLine(string abilityId, AbilitySpec s, string classId)
        {
            if (s?.loads == null || s.loads.Count == 0) return "";
            LoadSpec now = Current(abilityId, s);
            var lines = new List<string>();
            lines.Add(now == null
                ? "<color=#FF7272>You carry nothing it can use.</color>"
                : $"<color=#FFB86B>Loaded with {NameOf(now)}</color> <color=#7F8FA0>({Have(now)} carried). Right-click it on your hotbar to pick another.</color>");
            foreach (LoadSpec l in s.loads)
            {
                int have = Have(l);
                string count = l.count > 1 ? $" (uses {l.count})" : "";
                string head = l == now ? $"<color=#FFB86B>{NameOf(l)}</color>" : have > 0 ? NameOf(l) : $"<color=#8C99A6>{NameOf(l)}</color>";
                lines.Add($"• {head} ×{have}{count}: {Describe(l, classId)}");
            }
            return string.Join("\n", lines);
        }

        // ---- the picker ----------------------------------------------------------------------------------------

        /// <summary>The picker's rows for this skill: every load, with what you carry.</summary>
        internal static List<BonusSkills.Choice> Choices(ModAbility m)
        {
            var list = new List<BonusSkills.Choice>();
            AbilitySpec s = m?.Spec;
            if (s?.loads == null) return list;
            foreach (LoadSpec l in s.loads)
            {
                LoadSpec pick = l;
                string id = Resolve(l);
                list.Add(new BonusSkills.Choice
                {
                    icon = ItemRegistry.VanillaOrModItem(id)?.GetInventorySprite(),
                    label = () =>
                    {
                        int have = Have(pick);
                        bool on = Picked(m.internalName, s) == pick;
                        string name = on ? $"<color=#FFB86B>{NameOf(pick)}</color>" : Enough(pick) ? NameOf(pick) : $"<color=#8C99A6>{NameOf(pick)}</color>";
                        return $"{name} <size=80%><color=#9FB7C9>×{have}</color></size>";
                    },
                    title = () => NameOf(pick),
                    tip = () => $"{Describe(pick, m.ClassId)}\nCarried: {Have(pick)}",
                    pick = () =>
                    {
                        Choose(m.internalName, pick);
                        m.Refresh();
                        Menu_HUD.instance?.Refresh_Hotbar();
                        Game.LOG($"{m.GetDisplayName()}: loaded with {NameOf(pick)}{(Enough(pick) ? "" : " (you have none right now)")}.");
                    },
                });
            }
            return list;
        }
    }
}
