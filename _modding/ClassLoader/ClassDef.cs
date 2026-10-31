using System.Collections.Generic;
using Newtonsoft.Json;

namespace SCClassLoader
{
    /// <summary>One custom skill tree ("class"), loaded from Mods/**/*.json.</summary>
    public class ClassDef
    {
        /// <summary>Unique internal id. Saved into save files, so never change it once people play with it.</summary>
        public string id;
        public string name;
        public string description;
        /// <summary>Muscle, Guile or Intellect. Drives the archetype meters and base attribute growth.</summary>
        public string archetype = "Muscle";
        /// <summary>PNG file next to the json, or empty to use iconFrom.</summary>
        public string icon;
        /// <summary>Vanilla tree id whose icon is used when no png is given.</summary>
        public string iconFrom;
        public bool enabled = true;
        /// <summary>"win": kept off the creation screen until this computer has seen the game won (see Unlocks).</summary>
        public string unlock;
        /// <summary>Nothing hostile this class does (damage, debuffs, shoves, stuns, swaps...) ever lands on your own side:
        /// you, your summons and pets, thralls and crew. Buffs and heals still reach them.</summary>
        public bool ignoresAllies;

        public int starterCredits;
        /// <summary>Item ids from vanilla_reference.txt, or mod items ("Mod:Id", or "Id" for this class's own mod).</summary>
        public List<string> starterItems = new List<string>();
        /// <summary>Also give the starter gear of these vanilla trees.</summary>
        public List<string> starterGearFrom = new List<string>();

        /// <summary>Stat priorities shown in the character preview summary.</summary>
        public Dictionary<string, int> attributePriorities;
        public Dictionary<string, int> playerTags = new Dictionary<string, int>();
        /// <summary>Copy the player tags of every tree a tier is borrowed from (some abilities need them).</summary>
        public bool inheritTags = true;

        public List<TierDef> tiers = new List<TierDef>();
        /// <summary>Custom status effects, referenced from actions as "@id".</summary>
        public List<EffectSpec> effects = new List<EffectSpec>();
        /// <summary>Alchemical reactions: two effects meeting on an enemy set this off (only while you have the class).</summary>
        public List<ReagentReaction> reactions;
        /// <summary>Spells that exist only as wild magic: never on a skill bar, cast by wildCast (see WildMagic).</summary>
        public List<WildSpell> wildSpells;
        /// <summary>Augments your rune-socketed skills can buy from this class's pool in the rune matrix (see Augments).</summary>
        public List<AugmentSpec> augments;

        [JsonIgnore] public string sourceFile;
        [JsonIgnore] public string folder;
        /// <summary>The mod this class came from.</summary>
        [JsonIgnore] public ModPack pack;
    }

    public class TierDef
    {
        /// <summary>Vanilla tree id to borrow this tier from.</summary>
        public string from;
        /// <summary>0-based tier index inside that tree.</summary>
        public int tier;

        public string name;
        public string description;
        /// <summary>Replaces the tier's attribute bonuses, e.g. {"Strength": 2, "HealthMax": 10}.</summary>
        public Dictionary<string, int> attributes;
        /// <summary>Replaces the tier's resistances, e.g. {"Fire": 10}.</summary>
        public Dictionary<string, int> resistances;
        /// <summary>Added on top of the borrowed tier's bonuses instead of replacing.</summary>
        public Dictionary<string, int> addAttributes;
        public Dictionary<string, int> addResistances;

        public AbilityDef ability;

        /// <summary>A new ability instead of a borrowed one ("from"/"tier" are then ignored).</summary>
        public AbilitySpec newAbility;
    }

    public class AbilityDef
    {
        public string name;
        public string description;
        public string icon;
        public int? staminaCost;
        public int? cooldown;
        public int? hitChance;
        public float? range;
        /// <summary>Replaces the base damage list.</summary>
        public List<DamageDef> damage;
    }

    public class DamageDef
    {
        public string type;
        public int min;
        public int max;
    }
}
