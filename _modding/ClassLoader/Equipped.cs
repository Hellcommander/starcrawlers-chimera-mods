using System;
using System.Collections.Generic;
using System.Linq;

namespace SCClassLoader
{
    /// <summary>
    /// What you have equipped, for abilities that only work with certain gear ("equipped" on an ability, or the
    /// "equipped" key of a condition). A spec names gear kinds: "A|B" = either, "A+B" = both, "!A" = not A.
    ///
    /// Weapon kinds hold with that weapon in either hand: melee (light or heavy), ranged (light or heavy), lightMelee,
    /// heavyMelee, lightRanged, heavyRanged, heavy, object (orbs), weapon (anything but a shield), damage:TYPE (deals
    /// that damage type, e.g. damage:Shock). "mainhand:KIND" / "offhand:KIND" ask for one hand, "mainhand" /
    /// "offhand" alone whether anything is there. unarmed = no weapon in either hand (a shield is fine), shield = a
    /// shield generator in either hand, dualWield = a weapon in each hand. Slots: head, torso, belt, hands, legs, feet,
    /// neck, accessory, ammo (something in it). item:ID (vanilla or "Mod:Id"), tag:TAG (any equipped item carrying it).
    /// Only the active weapon set counts. Gear is read from the crawler you're playing; anyone else has none.
    ///
    /// A skill swings one weapon, never both (Hand): the main-hand one if it fits the skill's rule, else the off-hand
    /// one at the game's off-hand power (50%, more with dual-wield skills).
    /// </summary>
    internal static class Equipped
    {
        private static readonly HashSet<string> warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>While set, weapon kinds are asked of this one weapon only (does this weapon fit the rule?), and
        /// `named` notes whether some term of the rule picked it out (a kind it is, or its hand by name).</summary>
        private static Item_Weapon focus;
        private static bool named;

        internal static bool Check(string spec, Character who)
        {
            if (string.IsNullOrEmpty(spec)) return true;
            if (who == null || who != Player.instance || Menu_Player.instance == null) return false;
            return Holds(spec);
        }

        private static bool Holds(string spec)
        {
            foreach (string alt in spec.Split('|'))
            {
                named = false;
                if (alt.Split('+').All(part => Term(part.Trim())) && (focus == null || named)) return true;
            }
            return false;
        }

        /// <summary>The one weapon a skill with this rule swings, and whether it's the off-hand one: the main hand if it
        /// fits, else an off-hand weapon that does. No rule (or nothing fits) = the main hand, as before.</summary>
        internal static Item_Weapon Hand(string spec, Player pl, out bool offhand)
        {
            offhand = false;
            Item_Weapon main = pl?.GetMainhandWeapon(), off = pl?.GetOffhandWeapon();
            if (IsShield(off)) off = null;
            if (string.IsNullOrEmpty(spec) || pl != Player.instance || Menu_Player.instance == null || off == null) return main;
            if (main != null && !IsShield(main) && Fits(spec, main)) return main;
            if (Fits(spec, off)) { offhand = true; return off; }
            return main;
        }

        /// <summary>Share of a roll an off-hand swing deals (the game's own off-hand modifier); 1 for the main hand.</summary>
        internal static float Power(Player pl, Item_Weapon w, bool offhand)
        {
            if (!offhand || pl == null || w == null) return 1f;
            WEAPON_TYPE t = w.GetWeaponType();
            return pl.GetOffhandDamageMod(t == WEAPON_TYPE.Ranged || t == WEAPON_TYPE.HeavyRanged);
        }

        /// <summary>The weapon a skill swings: from its "equipped" rule when the source is a mod ability.</summary>
        internal static Item_Weapon Hand(ActionContext ctx, out bool offhand, out float power)
        {
            Player pl = ctx?.caster as Player;
            Item_Weapon w = Hand((ctx?.source as ModAbility)?.Spec?.equipped, pl, out offhand);
            power = Power(pl, w, offhand);
            return w;
        }

        private static bool Fits(string spec, Item_Weapon w)
        {
            focus = w;
            try { return Holds(spec); }
            finally { focus = null; }
        }

        private static bool Term(string t)
        {
            if (t.StartsWith("!"))
            {
                bool was = named;
                bool r = !Term(t.Substring(1).Trim());
                named = was; // "not a heavy weapon" picks nothing out
                return r;
            }
            Menu_Player mp = Menu_Player.instance;
            Item_Weapon main = mp.slot_mainhand?.item as Item_Weapon, off = mp.slot_offhand?.item as Item_Weapon;
            Item_Weapon[] hands = { main, off };
            int colon = t.IndexOf(':');
            string key = colon > 0 ? t.Substring(0, colon) : t, arg = colon > 0 ? t.Substring(colon + 1).Trim() : null;
            switch (key.ToLowerInvariant())
            {
                case "offhand": return arg == null ? mp.slot_offhand?.item != null : InHand(off, arg, t);
                case "mainhand": return arg == null ? mp.slot_mainhand?.item != null : InHand(main, arg, t);
                case "unarmed": return !new[] { main, off }.Any(w => w != null && !IsShield(w));
                case "shield": return IsShield(main) || IsShield(off);
                case "dualwield": return main != null && off != null && !IsShield(main) && !IsShield(off);
                case "item":
                    {
                        string id = Crafting.ResolveItem(arg, null);
                        return mp.GetEquipment().Any(e => e != null && e.itemId == id);
                    }
                case "tag": return arg != null && mp.GetEquipment().Any(e => e != null && (e.HasTag(arg) || e.HasTag(arg.ToUpperInvariant())));
                case "damage":
                    if (!Enum.TryParse(arg, true, out DAMAGE_TYPE dt)) return Unknown(t);
                    return Picks(hands, w => w != null && !IsShield(w) && w.GetDamageList().Any(d => d != null && d.dmgType == dt));
            }
            if (Enum.TryParse(key, true, out EQUIP_TYPE slot) && slot != EQUIP_TYPE.None && slot != EQUIP_TYPE.Weapon && colon < 0)
                return mp.GetEquipment().Any(e => e != null && e.GetEquipType() == slot);
            if (colon >= 0 || Kind(null, key) == null) return Unknown(t);
            return Picks(hands, w => Kind(w, key) == true);
        }

        /// <summary>A weapon-kind term holds with such a weapon in either hand; it picks out the focus weapon when that
        /// is one.</summary>
        private static bool Picks(Item_Weapon[] hands, Func<Item_Weapon, bool> fits)
        {
            if (focus != null && fits(focus)) named = true;
            return hands.Any(fits);
        }

        /// <summary>"offhand:melee": that hand holds that kind (picks the weapon out when it's the one being asked about).</summary>
        private static bool InHand(Item_Weapon w, string kind, string t)
        {
            bool? k = Kind(w, kind);
            if (k == null) return Unknown(t);
            if (k.Value && focus != null && focus == w) named = true;
            return k.Value;
        }

        /// <summary>Is this weapon of that kind? Null when the kind isn't one.</summary>
        private static bool? Kind(Item_Weapon w, string kind)
        {
            WEAPON_TYPE? type = w?.GetWeaponType();
            switch (kind.ToLowerInvariant())
            {
                case "melee": return type == WEAPON_TYPE.Melee || type == WEAPON_TYPE.HeavyMelee;
                case "ranged": return type == WEAPON_TYPE.Ranged || type == WEAPON_TYPE.HeavyRanged;
                case "lightmelee": return type == WEAPON_TYPE.Melee;
                case "heavymelee": return type == WEAPON_TYPE.HeavyMelee;
                case "lightranged": return type == WEAPON_TYPE.Ranged;
                case "heavyranged": return type == WEAPON_TYPE.HeavyRanged;
                case "heavy": return type == WEAPON_TYPE.HeavyMelee || type == WEAPON_TYPE.HeavyRanged;
                case "object": case "orb": return type == WEAPON_TYPE.Object;
                case "weapon": return w != null && type != WEAPON_TYPE.Shield;
                default: return null;
            }
        }

        private static bool IsShield(Item_Weapon w) => w != null && w.GetWeaponType() == WEAPON_TYPE.Shield;

        private static bool Unknown(string t)
        {
            if (warned.Add(t)) Plugin.Log.LogWarning($"Unknown equipment kind '{t}' (see Equipped in the README)");
            return false;
        }

        // ---- rules text ---------------------------------------------------------------------------------------

        /// <summary>"a melee weapon or a shield" for a spec.</summary>
        internal static string Describe(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return "";
            return string.Join(" or ", spec.Split('|').Select(alt => string.Join(" and ", alt.Split('+').Select(p => Name(p.Trim())))));
        }

        /// <summary>Describe plus "equipped" where the wording needs it ("a melee weapon equipped", but "a weapon in
        /// each hand").</summary>
        internal static string Requirement(string spec)
        {
            string d = Describe(spec);
            bool placed = d.Contains(" hand") || d.Contains(" slot") || d.StartsWith("no ") || d.Contains(" or no ") || d.Contains(" and no ");
            return placed ? d : d + " equipped";
        }

        private static string Name(string t)
        {
            if (t.StartsWith("!")) return "no " + Bare(Name(t.Substring(1).Trim()));
            int colon = t.IndexOf(':');
            string key = colon > 0 ? t.Substring(0, colon) : t, arg = colon > 0 ? t.Substring(colon + 1).Trim() : null;
            switch (key.ToLowerInvariant())
            {
                case "offhand":
                case "mainhand":
                    {
                        string hand = key.ToLowerInvariant() == "offhand" ? "off hand" : "main hand";
                        if (arg == null) return "something in your " + hand;
                        string k = KindName(arg);
                        return k.Length > 0 ? $"{k} in your {hand}" : t;
                    }
                case "unarmed": return "no weapon in your hands";
                case "shield": return "a shield generator";
                case "dualwield": return "a weapon in each hand";
                case "item": return ItemRegistry.NameOf(Crafting.ResolveItem(arg, null));
                case "tag": return $"gear tagged {arg?.ToLowerInvariant()}";
                case "damage": return $"a weapon that deals {arg}";
            }
            if (Enum.TryParse(key, true, out EQUIP_TYPE slot) && slot != EQUIP_TYPE.None && slot != EQUIP_TYPE.Weapon && colon < 0)
                return $"something in your {slot.ToString().ToLowerInvariant()} slot";
            return KindName(key) is string n && n.Length > 0 ? n : t;
        }

        private static string KindName(string kind)
        {
            switch (kind.ToLowerInvariant())
            {
                case "melee": return "a melee weapon";
                case "ranged": return "a ranged weapon";
                case "lightmelee": return "a light melee weapon";
                case "heavymelee": return "a heavy melee weapon";
                case "lightranged": return "a light ranged weapon";
                case "heavyranged": return "a heavy ranged weapon";
                case "heavy": return "a heavy weapon";
                case "object": case "orb": return "an orb";
                case "weapon": return "a weapon";
                default: return "";
            }
        }

        private static string Bare(string s) => s.StartsWith("a ") ? s.Substring(2) : s.StartsWith("an ") ? s.Substring(3) : s;
    }
}
