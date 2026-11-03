using System.Collections.Generic;

namespace SCClassLoader
{
    /// <summary>A brand-new ability written in a class file (TierDef.newAbility).</summary>
    public class AbilitySpec
    {
        public string name;
        /// <summary>Flavor text. The rules text is generated from the actions and appended.</summary>
        public string description;
        /// <summary>Hand-written rules text shown instead of the generated action and proc lines, for skills whose
        /// mechanics are too intricate to list without overflowing the tooltip. Unset = generated.</summary>
        public string rules;
        public string icon;
        /// <summary>active | passive</summary>
        public string type = "active";
        /// <summary>self | enemy | floor | point (an enemy or a floor tile: with "teleport" you land beside the enemy, or on the
        /// tile) | item | pet (one of your own summons, optionally only those of petGroup)</summary>
        public string target = "enemy";
        /// <summary>A skill for your own side: your companions stay solid while you aim it, where any other skill's aim
        /// passes through them. Unset, "pet" skills and those whose field "affects" allies count as this.</summary>
        public bool? forAllies;
        /// <summary>target "pet": only summons of this summonGroup ("Hoarlight").</summary>
        public string petGroup;
        public float range = 1.5f;
        /// <summary>Area around the target (or around you for self abilities). 0 = single target.</summary>
        public float radius;
        /// <summary>"cone" or "line": hits everyone in that shape from you toward the target or aim point, out to the
        /// skill's range, instead of a round area. A line pierces everyone on it.</summary>
        public string shape;
        /// <summary>Line width in tiles (default 1).</summary>
        public float width = 1f;
        /// <summary>Cone spread in degrees (default 60).</summary>
        public float angle = 60f;
        public int staminaCost;
        public int cooldown;
        /// <summary>Does not end your turn.</summary>
        public bool instant;
        /// <summary>If the cast kills anything, the cooldown starts over at 0 (go again).</summary>
        public bool resetOnKill;
        /// <summary>The cooldown doesn't count down while you carry this effect ("@Stance"): a stance, form or shield that
        /// can't be used again until it has ended and its cooldown run. Other skills that cut cooldowns still do.</summary>
        public string cooldownWhile;
        /// <summary>The cooldown counts this trigger ("kill", "crit", "dodge", ... any proc "on") instead of turns: each
        /// time it fires for you the cooldown drops by one, and turns passing don't count. Cooldown 1 + "kill": ready
        /// again after your next kill, whatever made it.</summary>
        public string cooldownOn;
        /// <summary>When this skill fires your gun (a channel, or a rewrite that shoots), each shot spends this many
        /// times the gun's rounds, rounded up (1.5: a burst; 2: a double load). Each extra round also adds the ammo's
        /// damage, as in the game's Suppression. The skill can't be used without the rounds.</summary>
        public float ammoMult;
        /// <summary>An order to your summons: still usable while transformed. Unset = worked out from the actions
        /// (everything aimed at your summons or you, nothing summoned, at least one action on a summon).</summary>
        public bool? command;
        /// <summary>Reaction slots this ability gives while you know it (the most from any one ability counts; base 2).</summary>
        public int reactionSlots;
        /// <summary>The primary target may dodge it.</summary>
        public bool dodgeable;
        /// <summary>Melee swing visual and melee flag.</summary>
        public bool melee;

        /// <summary>Vanilla particle to borrow: an effect id ("EFFECT_Fire") or ability id ("VOID_PSYKER_SHADOWCOIL[.field]").</summary>
        public string castVfx;
        public string impactVfx;
        /// <summary>A creature id ("Hoarlight"): while this ability moves you (leap, warp), the third-person dash shot
        /// shows you as that creature (its model, glow and particles) streaking through the target, not as your crawler.</summary>
        public string dashAs;
        /// <summary>With dashAs on a "teleport" skill: a line of this colour ("#FFD08A") left along the path.</summary>
        public string dashTrail;
        /// <summary>Seconds the camera pulls out over your shoulder as you use it, to see what it does on your body (DashCam).</summary>
        public float thirdPerson;
        /// <summary>Particle that flies from the caster to the target (enemy or floor) before the hit lands.</summary>
        public string travelVfx;
        /// <summary>Flight speed of travelVfx in tiles per second (the flight is kept between 0.12 and 0.6 s).</summary>
        public float travelSpeed = 14f;
        /// <summary>Particle played once at the centre of the area (floor aim point, or the target), on top of each impactVfx.</summary>
        public string areaVfx;
        /// <summary>FMOD event, e.g. "event:/Abilities/Soldier/Rocket_Explode".</summary>
        public string sound;

        public List<ActionSpec> actions = new List<ActionSpec>();
        /// <summary>Run on you as the cast starts, before any move or hit (protection that has to cover the landing).</summary>
        public List<ActionSpec> castActions;
        /// <summary>Passive (or always-on) triggers.</summary>
        public List<ProcSpec> procs = new List<ProcSpec>();
        /// <summary>Upgrade bought with a perk point.</summary>
        public PerkSpec perk;
        /// <summary>What this skill pays for each level of the synergies its class is in. Those synergies then cost no
        /// Synergy Strain: the skill's price replaces it.</summary>
        public SynergyPenaltySpec synergyPenalty;

        /// <summary>Items used up each cast; the ability can't be used without them.</summary>
        public List<CostSpec> itemCost;
        /// <summary>Credits paid each cast; the ability can't be used without them.</summary>
        public int creditCost;
        /// <summary>Stacks of one of your effects spent each cast, e.g. {"effect": "@Scrap", "amount": 2}.</summary>
        public StackCostSpec stackCost;
        /// <summary>Extra use condition: "livingBody" / "downedBody" (Conjoined bodies). Empty = none.</summary>
        public string requires;
        /// <summary>A form's kit only: this kit ability joins the kit only if you also have this skill tree (a tree id,
        /// a mod class id or a display name; "A|B" = either), at skillRank or higher. A cross-class synergy.</summary>
        public string ifSkill;
        public int skillRank = 1;
        /// <summary>A granted skill only: the skill it rides on (display or internal name). It stays off the hotbar and
        /// is reached by right-clicking that skill's hotbar icon (BonusSkills).</summary>
        public string attachTo;
        /// <summary>A passive: % chance that each of your offensive mod spells changes form as you cast it (area, line,
        /// scatter, a random other target, laid as a trap, or turned on you). See WildMagic.</summary>
        public int wildForm;
        /// <summary>A passive: stats raised by a share of another stat while you know it. See StatsFrom.</summary>
        public List<StatFromSpec> statsFrom;
        /// <summary>With wildForm: the effect each form shows ("area", "line", "scatter", "retarget", "trap", "backfire").
        /// "line" and "retarget" fly it as a travel effect from you; the rest play where the spell lands.</summary>
        public Dictionary<string, string> formVfx;
        /// <summary>Gear you must have equipped ("melee", "ranged|shield", "dualWield", "item:Mod:Id", see Equipped).
        /// An active can't be used without it; a passive's procs stay quiet.</summary>
        public string equipped;
        /// <summary>Only usable while this holds (checked about you, e.g. below half health).</summary>
        public WhenSpec usableWhen;
        /// <summary>Only on targets for which this holds (e.g. a finisher on enemies below 30% health).</summary>
        public WhenSpec targetWhen;
        /// <summary>target "item": which inventory items it can be used on (by tag and/or item id).</summary>
        public string itemTag;
        public string itemId;
        /// <summary>target "item": what kind of item, joined with "|": "gear" (anything you can wear or wield), "weapon",
        /// "melee", "ranged", "armor" (gear that isn't a weapon), "consumable", or a slot ("head", "torso", "hands", "legs",
        /// "feet", "neck", "belt", "accessory").</summary>
        public string itemKind;
        /// <summary>target "item": the least rarity it works on ("Rare" = rare, epic or legendary).</summary>
        public string itemRarity;
        /// <summary>Rune sockets: runes set into them retune the skill (element, shape, power, payload). Runes stay in
        /// the socket until you take them out; they are never used up.</summary>
        public int runeSlots;
        /// <summary>Which rune kinds fit (element, shape, power, payload). Unset = all.</summary>
        public List<string> runeKinds;
        /// <summary>The effect a rune's "instability" feeds on every cast ("@Instability"). Unset = none.</summary>
        public string runeMeter;
        /// <summary>Casts each socketed rune lasts (a rune's own "wear" overrides it); a matching rune from your bag
        /// replaces one that burns out. 0 = runes never wear, which is the default.</summary>
        public int runeWear;
        /// <summary>With runeWear: stamina per cast to recharge a rune. A spent rune stays in its socket and does nothing until recharged.</summary>
        public int runeCharge;
        /// <summary>With runeCharge: turns before a discharge can give stamina again (shared by all your runes).</summary>
        public int runeDischarge;
        /// <summary>The class rank each socket opens at, e.g. [1, 3, 5]. Unset = all open.</summary>
        public List<int> runeSlotRanks;
        /// <summary>Signature combos: what particular runes do in this skill, on top of (or instead of) their usual effect.</summary>
        public List<RuneVariant> runeVariants;
        /// <summary>Rewrites of OTHER skills while you know this one (a passive, normally): their form (trap, grenade,
        /// monster, drifter, nova, line, cone), element, effects, reach and cost. See SkillMods.</summary>
        public List<SkillModSpec> modifies;

        /// <summary>A copy sharing the lists (for a rewrite that swaps target, shape or particles without touching the file's spec).</summary>
        internal AbilitySpec ShallowCopy() => (AbilitySpec)MemberwiseClone();
    }

    /// <summary>An item handed out by giveItem/salvage, with an optional chance.</summary>
    public class GiveSpec
    {
        public string item;
        public int count = 1;
        public int chance = 100;
    }

    public class StackCostSpec
    {
        public string effect;
        public int amount = 1;
    }

    /// <summary>One outcome of a "random" action.</summary>
    public class RandomOption
    {
        /// <summary>Shown in the log when it comes up ("Jackpot!").</summary>
        public string name;
        public int weight = 1;
        /// <summary>Extra weight per stack of the random action's stacksOf effect (luck that builds up).</summary>
        public int weightPerStack;
        public List<ActionSpec> actions = new List<ActionSpec>();
    }

    /// <summary>One thing an ability, proc or effect does.</summary>
    public class ActionSpec
    {
        /// <summary>chain: particle on each hop after the first (default: the ability's impactVfx). trap: the particle it goes off in.</summary>
        public string vfx;
        // credits / random / execute / summon conditions
        /// <summary>random: the outcomes, one of which happens.</summary>
        public List<RandomOption> options;
        /// <summary>execute: % of max health that bosses, elites, rares and kill squads take instead of dying.</summary>
        public int elitePercent = 15;
        /// <summary>Only when a summon is alive: "self" (made by this ability), "any", or a creature name.</summary>
        public string ifSummonAlive;
        /// <summary>Only when no such summon is alive (same values as ifSummonAlive).</summary>
        public string unlessSummonAlive;

        // giveItem / consumeItem / salvage / craft
        /// <summary>giveItem/consumeItem: item id ("Mod:Id" or vanilla). consumeItem may use "tag" instead.</summary>
        public string item;
        public string tag;
        /// <summary>salvage: what the targeted item breaks into.</summary>
        public List<GiveSpec> gives;
        /// <summary>craft: limit the Fabricator to these recipe ids (default: every field recipe).</summary>
        public List<string> recipes;

        /// <summary>damage | heal | stamina | shield | effect | stun | knockback | blind | cooldowns | teleport | purge |
        /// taunt | silence | exhaust | swap | ... (see the README for the full list)</summary>
        public string @do;
        /// <summary>target (default for enemy/floor abilities) | self | attacker | victim</summary>
        public string to;
        public int chance = 100;

        // damage
        public string type;
        public int min;
        public int max;
        /// <summary>Stat added on top: +stat * scaleMult.</summary>
        public string scale;
        public float scaleMult = 0.5f;
        /// <summary>Extra flat amount per rank of this skill tree.</summary>
        public int perRank;
        /// <summary>Damage actions: adds this share of a roll of your main-hand weapon's damage (0.5 = half), for
        /// skills that are half blade, half spell. Nothing without a weapon, and only for you.</summary>
        public float weapon;
        /// <summary>Damage actions: the hit counts as a weapon hit (your weapon's and ammo's on-hit effects go off) with
        /// no weapon damage added. Comes with SkillMods' "channel".</summary>
        public bool channel;
        /// <summary>Damage actions run by an effect (onExpireActions, onDeathActions, a burst or threshold): adds this
        /// share of the health its bearer lost while the effect was on it (0.5 = half). A revenge counter.</summary>
        public float stored;

        // heal / stamina / shield / cooldowns / knockback / stun turns
        public int amount;

        // effect
        /// <summary>Vanilla effect id ("EFFECT_Fire") or one of this class's effects ("@Hexed").</summary>
        public string effect;
        public int turns;
        /// <summary>Turns added per stack of stacksOf (with stacksUsed as a cap): a stun, silence, taunt, Horror or effect that
        /// lasts longer with a synergy's level ("stacksOf": "@SynergyId" inside a synergy) or with a resource.</summary>
        public int turnsPerStack;

        /// <summary>Only when the target has this effect (vanilla id or "@name").</summary>
        public string ifTargetHas;
        /// <summary>Skip the action when the target carries this effect.</summary>
        public string unlessTargetHas;
        /// <summary>Only when YOU have this effect (stances, overheat states).</summary>
        public string ifSelfHas;
        /// <summary>Only when you also have this skill tree (vanilla "SKILLTREE_Pyromaniac", a mod class id, or a
        /// display name; "A|B" = either one), at rank skillRank or higher (1 = just taken).</summary>
        public string ifSkill;
        /// <summary>Skip the action when you have this skill tree.</summary>
        public string unlessSkill;
        public int skillRank = 1;
        /// <summary>Any condition (see WhenSpec): all of it must hold for the action to run.</summary>
        public WhenSpec when;

        // counter: a named number kept on the character ("to"), changed by amount (or set to it)
        public string counter;
        public bool counterSet;
        public int counterMin;
        /// <summary>0 = no cap.</summary>
        public int counterMax;
        /// <summary>silence: a boss is silenced this many turns, then can't be silenced for 3 more. 0 = bosses shrug it off.</summary>
        public int bossTurns;
        /// <summary>if: runs "then" when "when" holds, else "else" (both on the same target).</summary>
        public List<ActionSpec> then;
        public List<ActionSpec> @else;
        /// <summary>Remove that effect from the target after the action.</summary>
        public bool consume;

        /// <summary>Stacks added by an "effect" action (stacking effects only).</summary>
        public int stacks = 1;
        /// <summary>Damage/heal/stamina/shield bonus per stack of <see cref="stacksOf"/>. On effect actions it adds stacks
        /// and on summon actions summons (there "stacks"/"amount" may be 0: then none without stacks).</summary>
        public int bonusPerStack;
        /// <summary>Effect whose stacks feed bonusPerStack ("@name" or vanilla id).</summary>
        public string stacksOf;
        /// <summary>Where to count stacksOf: target (default) | self.</summary>
        public string stacksFrom;
        /// <summary>Remove the stacksOf effect after reading it.</summary>
        public bool consumeStacks;
        /// <summary>Count at most this many stacksOf stacks (0 = all of them); with consumeStacks only those are spent, so a
        /// skill can cash in part of a pile ("stacksUsed": 3 spends 3 of your 10).</summary>
        public int stacksUsed;
        /// <summary>Damage actions: heal you for this fraction of the damage actually dealt.</summary>
        public float lifesteal;
        /// <summary>Damage actions: bypass armor, shields and resistances.</summary>
        public bool trueDamage;

        // chain: hop from the target to up to `jumps` more enemies within `radius`, optionally only ones with `ifTargetHas`
        public int jumps;
        public float radius;
        /// <summary>chain: you charge along it, landing beside each link before it is struck (a link you can't reach
        /// is still struck from where you are).</summary>
        public bool leap;
        /// <summary>Actions run on every link of a chain.</summary>
        public List<ActionSpec> actions;
        /// <summary>summon: creature prefab ("ABILITY.field", "nanite_cloud", or part of a creature name); amount = how many,
        /// turns = lifetime (0 = permanent), maxSummons = cap of living summons from this ability, effect = applied to each summon.</summary>
        public string creature;
        public int maxSummons;
        /// <summary>summon, "creature": "self": how strongly the copy fights (0.4 = 40% of your damage, and its stuns, blinds
        /// and silences land 40% as often; effects it puts on last and stack that much). See Mirrors.</summary>
        public float strength = 0.4f;
        /// <summary>summon, "creature": "self": the copy's health as a share of yours.</summary>
        public float healthShare = 0.3f;
        /// <summary>exhaust: count the turns from when the target recovers. Cooldowns tick while stunned, so a plain set-back under a stun runs out unseen. Put it after the stun.</summary>
        public bool afterStun;
        /// <summary>throw: bosses fly too, past the game's NOKNOCKBACK on them. Not Cerberus (IMMORTAL), whose fight runs on platforms, or rooted bosses, or onto tiles closed to bosses.</summary>
        public bool bosses;
        /// <summary>throw: away from the aim point, not from you (something that lands there).</summary>
        public bool fromCenter;
        /// <summary>trap: only its fuse (or springTraps) sets it off, never someone stepping or standing on it.</summary>
        public bool fuseOnly;
        /// <summary>trap: a hex colour; a streak of it comes down from overhead as it goes off.</summary>
        public string fall;
        /// <summary>trap: a creature id whose model (mesh and particles, as "dashAs") comes down from overhead; its burst
        /// and sound play as it lands, the trap's own effects land at once.</summary>
        public string fallAs;
        /// <summary>springTraps: only traps this close to you (0 = all of them on the floor).</summary>
        public float near;
        /// <summary>raiseWall: on the tile this many steps behind the target as you see it, if the way there is open (something for a throw to hit).</summary>
        public int behind;
        /// <summary>raiseWall: the class whose folder holds "model", for a synergy borrowing another class's wall.</summary>
        public string modelFrom;
        /// <summary>An action a rune or rewrite adds: run before the skill's own instead of after them.</summary>
        public bool first;
        /// <summary>summon: shown name of each summon (instead of the creature's own).</summary>
        public string summonName;
        /// <summary>summon: the summon stays on the tile it appears on (it still attacks what comes within reach).</summary>
        public bool stationary;
        /// <summary>summon: the particle each summon appears in (default the Boltron's lightning; "none" for nothing).</summary>
        public string summonVfx;
        /// <summary>warp: where it lands - "behind" (you, on the far side of the target), "random" (the target, a random
        /// free tile within amount), "away" (the target, the free tile within amount farthest from you).</summary>
        public string warpTo;
        /// <summary>rush: which way you go from the way you face - "forward" (default), "back", "left" or "right".</summary>
        public string direction;
        /// <summary>show: seconds the model stays up (default 0.8); "showStyle": "pop" (default) or "snap" (jaws closing).</summary>
        public float showTime;
        public string showStyle;
        /// <summary>summon: a pool shared by every ability that names it, for maxSummons and for "group:Name" in
        /// ifSummonAlive / unlessSummonAlive / stacksOf.</summary>
        public string summonGroup;
        /// <summary>Where a summon centred on you appears: "behind" (your shadow steps out behind you, not into the camera),
        /// "beside", or nothing for the nearest free tile (usually the one in front).</summary>
        public string summonAt;
        /// <summary>summon: the owner stat it grows with ("PsiPower"), overriding the creature's own scaleStat; default Smarts.</summary>
        public string scaleStat;
        /// <summary>summon: a new look for each summon (mesh/texture next to the class file; see ModelSpec).
        /// field: a mesh shown on the field's tiles instead of (or with) a hazard look - a knife stuck in the deck, a coin.</summary>
        public ModelSpec model;
        /// <summary>pull: "self" (default, toward you) or "center" (toward the ability's aim point).</summary>
        public string pullToward;
        /// <summary>hazard: a vanilla hazard id (HAZARD_...) laid on the target tile and, with radius, around it.</summary>
        public string hazard;
        /// <summary>field: who its actions touch ("enemies" default, "allies", "all"), its name, the vanilla hazard
        /// it borrows its look from, a particle that pulses on its tiles each turn, and whether stepping in triggers it.</summary>
        public string affects;
        public string fieldName;
        public string look;
        /// <summary>chain: it only jumps to what the one it leaves can see, never through a wall.</summary>
        public bool sight;
        public string pulseVfx;
        public bool onEnter = true;
        /// <summary>Fields: covers the casting skill's cone or line out to "radius", not a circle around the aim.</summary>
        public bool overShape;
        /// <summary>field: tiles between copies of its look or model (default 1.9; 1 = one on every tile, a palisade of spikes).</summary>
        public float spacing;
        /// <summary>trap: your turns before it is live (0 = at once).</summary>
        public int armTurns;
        /// <summary>trap: times it goes off before it is used up (0 = until its turns run out).</summary>
        public int charges = 1;
        /// <summary>trap: goes off on its own after this many of your turns (a timed mine; 0 = never).</summary>
        public int fuse;
        /// <summary>trap: how close a character has to come to set it off (0 = onto its tile; 1.5 = beside it too).</summary>
        public float triggerRadius;
        /// <summary>trap: most of these (by fieldName) on a floor at once; laying another removes the oldest (0 = no cap).</summary>
        public int maxTraps;
        /// <summary>trap: the sound it makes going off (FMOD event or a mod sound).</summary>
        public string sound;
        /// <summary>Damage multiplier per hop for chains, or strength of an echo recast.</summary>
        public float falloff = 1f;
        /// <summary>wildCast: leave this class's spells out of the pool (the caster's own class always is).</summary>
        public string notClass;
        /// <summary>wildCast: "spells" (default: other classes' skills, some of them wild spells), "wild" (only wild spells),
        /// "bosses" (only boss echoes), "monsters" (the mod monsters' attacks) or "all"".</summary>
        public string pool;
        /// <summary>cast: the skill it fires for free, by name ("Comet Charge"); your own class's skills are looked up first.
        /// cooldowns: only this skill's cooldown moves (by its shown name).</summary>
        public string skill;
        /// <summary>damage: the share lost per tile between the victim and the area's centre (0.25 = a quarter less a tile out,
        /// half two tiles out), so a blast hits hardest where it lands. Never below a tenth.</summary>
        public float areaFalloff;
        /// <summary>rewind: health goes back up to what it was at the anchor (never down).</summary>
        public bool restore;
        /// <summary>corpses: the nested actions not aimed at you hit the enemies within this of each corpse.</summary>
        public float blastRadius;
        /// <summary>corpses: leave the bodies lying there (default: they are used up).</summary>
        public bool keepCorpses;
        /// <summary>loot: what drops ("Random", "Weapon", "Gear", "Consumable", "Credits", "Food", "Ammo", "Luxury") and how rare
        /// ("Common", "Rare", "Epic", "Legendary"; unset = the floor's own odds).</summary>
        public string lootType;
        public string rarity;
        /// <summary>unlock / disarm / steal: % chance each lock, trap or theft works. Unset: every lock and trap gives; a theft
        /// works at 50 + your Smarts, as the Hacker's does.</summary>
        public int odds;
        /// <summary>tune: the stat it adds ("Strength", "Crit", ... or "random", picked as the Modder's tinkering picks).</summary>
        public string stat;
        /// <summary>tune: a piece of gear takes at most this many tunings (counted on the item, under "tag"; 0 = no limit).</summary>
        public int limit;
        /// <summary>tune: the equipped item to work on when the skill isn't aimed at an item ("mainhand", "offhand" or a slot:
        /// "head", "torso", "hands", "legs", "feet", "neck", "belt", "accessory").</summary>
        public string slot;
        /// <summary>summon: it arrives against you, as an anySide summon does when the coin goes the wrong way.</summary>
        public bool hostile;
        /// <summary>order: which of your summons act ("self" = made by this skill, "any", "group:X" or a creature name; default any).</summary>
        public string pets;
        /// <summary>summon: a coin toss decides whose side it arrives on. Against you it gives no xp and goes when its turns end.</summary>
        public bool anySide;
        /// <summary>summon: it arrives on the chaos side, the game's own side for loose nanite clouds: it fights everyone
        /// near it, you, your summons and your enemies alike, and they fight it. It gives no xp and goes when its turns end.</summary>
        public bool chaos;
        /// <summary>surge: what it is called in the log ("Thundercrunch, Doctor Cerberus's").</summary>
        public string surgeName;
        /// <summary>surge: % chance it lands on you instead of the target; your Crit (luck) takes half of itself off it,
        /// down to a floor of a third of the chance.</summary>
        public int atYou;
        /// <summary>drifter: tiles it moves each turn (default 2), and the % chance it goes off on each (default 50).</summary>
        public int steps;
        public int stepChance;
        /// <summary>damage, surge, drifter: the damage grows with your level, from 30% at level 1 to full at this level
        /// and no further, so a huge effect is neither a nuke nor a death sentence early on.</summary>
        public int levelCap;
        /// <summary>arm: the reaction this action sets (see ReactionSpec).</summary>
        public ReactionSpec reaction;
        /// <summary>Fields the loader doesn't know: a code plugin's action reads its own from here (ActionCall.Arg).</summary>
        [Newtonsoft.Json.JsonExtensionData] public IDictionary<string, Newtonsoft.Json.Linq.JToken> extra;

        internal ActionSpec ShallowCopy() => (ActionSpec)MemberwiseClone();
    }

    /// <summary>
    /// A reaction armed by an "arm" action: it waits in one of your reaction slots and runs its actions on the enemy
    /// that triggers it (default target), with "to": "victim" being whoever the enemy went for.
    /// </summary>
    public class ReactionSpec
    {
        public string name;
        /// <summary>approach (an enemy steps next to you) | retreat (an enemy next to you steps away) | special (an
        /// enemy uses an ability with a cooldown) | hurt (an enemy hits you) | ranged (an enemy hits you from two or
        /// more tiles away) | lowHealth (an enemy's hit takes you under threshold % health) | debuff (an enemy puts a
        /// debuff on you) | dodge (you dodge an enemy)</summary>
        public string on = "approach";
        /// <summary>approach / retreat / hurt / ranged / lowHealth / debuff: "you" (default) or "allies" (you or any
        /// of your summons and pets).</summary>
        public string who = "you";
        /// <summary>lowHealth: the health % the hit has to take you under.</summary>
        public int threshold = 40;
        public int charges = 1;
        /// <summary>Your turns until it disarms on its own.</summary>
        public int turns = 6;
        /// <summary>Higher goes first when several reactions answer the same event.</summary>
        public int priority = 50;
        /// <summary>special: only enemies this close to you.</summary>
        public float range = 8f;
        /// <summary>special: the enemy's ability is cut off (wasted and on cooldown).</summary>
        public bool interrupt;
        public string vfx;
        public string sound;
        public List<ActionSpec> actions = new List<ActionSpec>();
    }

    public class ProcSpec
    {
        /// <summary>hit | kill | hurt | dodge | turn | reaction | crit | miss | cast | move | wait | heal | lowHealth |
        /// allyDeath | effectGained | effectLost | combatStart | combatEnd | enterRoom | loot | levelUp | enemyDeath | allyKill |
        /// allyHurt | summon | inflict | useItem | equip | unequip | search | newFloor | shieldBreak | absorb | trap | charge | leap |
        /// teleport | knockback | swap | chain</summary>
        public string on;
        public int chance = 100;
        /// <summary>Only when this holds (target = the other character of the event, if any).</summary>
        public WhenSpec when;
        /// <summary>Your turns before it can fire again.</summary>
        public int cooldown;
        /// <summary>Fires on every Nth time the event happens (3 = every third hit).</summary>
        public int every;
        /// <summary>Most times per turn (0 = no limit).</summary>
        public int maxPerTurn;
        /// <summary>cast: ability name or id; effectGained / effectLost / inflict: effect id ("@X", vanilla); useItem / equip /
        /// unequip: item name or id. Empty = any.</summary>
        public string match;
        /// <summary>lowHealth: the health % you have to drop below (default 30).</summary>
        public int threshold = 30;
        /// <summary>Hit an area around the proc target instead of one character.</summary>
        public float radius;
        /// <summary>With radius: only this many random enemies in it (a turn proc's one random foe nearby).</summary>
        public int pick;
        public string vfx;
        public string sound;
        public List<ActionSpec> actions = new List<ActionSpec>();

        /// <summary>ProcHub's split of <see cref="actions"/> into what lands on you and the rest, kept per list.</summary>
        internal List<ActionSpec> splitOf, selfActions, restActions;
    }

    public class SynergyPenaltySpec
    {
        /// <summary>Percent less damage per synergy level (never below a tenth of the damage).</summary>
        public int damage;
        /// <summary>Extra stamina cost per synergy level.</summary>
        public int stamina;
        /// <summary>Extra cooldown turns per synergy level.</summary>
        public int cooldown;
    }

    public class PerkSpec
    {
        public string name;
        public string description;
        public float damageMult = 1f;
        public int cooldownDelta;
        public int staminaDelta;
        public float radiusDelta;
        public float rangeDelta;
        public int chanceBonus;
        public List<ActionSpec> addActions = new List<ActionSpec>();
        /// <summary>Triggers the skill only has once perked.</summary>
        public List<ProcSpec> addProcs;
        /// <summary>Using it no longer ends your turn.</summary>
        public bool instant;
        /// <summary>A kill with it makes it ready again.</summary>
        public bool resetOnKill;
        /// <summary>The primary target can no longer dodge it.</summary>
        public bool undodgeable;
    }

    /// <summary>A custom status effect a class defines (ClassDef.effects).</summary>
    /// <summary>A spell only wild magic casts. <see cref="group"/> "boss" is an echo of a boss's attack: its pool is
    /// "bosses", and its actions are normally one "surge", which does its own telling and its own look.</summary>
    public class WildSpell
    {
        public string name;
        /// <summary>A line for the log, after the name.</summary>
        public string say;
        public string group;
        public int weight = 1;
        /// <summary>Only comes up once the caster's total level reaches this.</summary>
        public int minLevel;
        /// <summary>Also lands on those within this many tiles of the target.</summary>
        public float radius;
        /// <summary>The radius catches everyone, you and your allies included, and its damage can hurt you.</summary>
        public bool friendlyFire;
        public string impactVfx;
        public string sound;
        public List<ActionSpec> actions;
    }

    /// <summary>
    /// "capScale" on a pool: cap = maxStacks + perPoint for each stat point above "from", never below maxStacks and
    /// never above "ceiling". For pools only; a meter's burst point stays where it is.
    /// </summary>
    public class StackCapSpec
    {
        /// <summary>The stats, as in an action's "scale": "Smarts+PsiPower", or "Smarts|PsiPower" for the higher one.</summary>
        public string scale;
        /// <summary>Stacks gained per stat point. Fractions add up: 0.25 is one stack for every four points.</summary>
        public float perPoint = 0.25f;
        /// <summary>The stat total where growth starts: about what the class has early on, so early play doesn't change.</summary>
        public int from;
        /// <summary>The most the cap can reach. 0 = twice maxStacks.</summary>
        public int ceiling;
    }

    public class EffectSpec
    {
        /// <summary>Resizes the bearer's model while it lasts (0.6 shrinks, 1.4 swells).</summary>
        public float modelScale;
        public string id;
        public string name;
        public string description;
        /// <summary>PNG next to the class file; defaults to the icon of the ability that applies it.</summary>
        public string icon;
        public int duration = 3;
        public bool debuff;
        /// <summary>Re-applying refreshes the duration instead of stacking.</summary>
        public bool stackable;
        /// <summary>
        /// Shows the stacks as a bar on the resource overlay. Unset = automatic: shown when an ability pays with it
        /// (stackCost) or it is a pool (regenPerTurn / startStacks). Needs maxStacks &gt; 1.
        /// </summary>
        public bool? resource;
        /// <summary>The bar's colour, "#4FA3FF".</summary>
        public string resourceColor;
        /// <summary>A mana-style pool: stacks you gain at the start of each of your turns while you have the class.</summary>
        public int regenPerTurn;
        /// <summary>A pool's stacks when you first have the class (a new character, or the class taken later).</summary>
        public int startStacks;
        /// <summary>A pool's cap that grows with the bearer's stats; maxStacks stays the least it can be. See StackCapSpec.</summary>
        public StackCapSpec capScale;
        public Dictionary<string, int> stats;
        public Dictionary<string, int> resists;
        /// <summary>Damage dealt to the bearer each turn.</summary>
        public DamageTick dot;
        /// <summary>Healing each turn.</summary>
        public int hot;
        /// <summary>Stats added to "hot", from whoever applied it, as in an action's "scale".</summary>
        public string hotScale;
        public float hotScaleMult = 0.1f;
        public int staminaPerTurn;
        public float damageDealtMult = 1f;
        public float damageTakenMult = 1f;
        /// <summary>Extra critical damage while it holds: 0.25 = a crit does 25% of the hit more (the game's own crit adds 50%).
        /// Crit chance is a stat ("stats": { "Crit": 10 }).</summary>
        public float critDamage;
        public float critDamagePerStack;
        /// <summary>Part of each hit the bearer takes that goes to whoever applied the effect instead (0.5 = half): a bodyguard's cover.</summary>
        public float redirect;
        /// <summary>redirect goes to one of your summons instead of whoever applied it: "summon" (the one nearest the bearer),
        /// "group:X" or a creature name. Only for effects your side applies.</summary>
        public string redirectTo;
        /// <summary>damageDealtMult (and its per-stack part) only on these hits: "melee", "ranged", "attack" (a weapon attack,
        /// not a skill), "skill", "mainhand" / "offhand" (the hand that swung) or damage types, joined with "|"
        /// ("ranged|Fire"). Unset = every hit.</summary>
        public string dealtWith;
        /// <summary>damageTakenMult only on these hits, the same words: "ranged" for a deflector, "Fire|Cryo" for insulation.</summary>
        public string takenFrom;
        /// <summary>Hazards whose damage can't touch the bearer: ids as in a "hazard" action ("HAZARD_Fire"; a prefix covers its
        /// kin, so "HAZARD_Fire" also stops HAZARD_Fire_Mini) or "all". What a hazard puts on you is stopped by "immune".</summary>
        public List<string> hazardProof;
        /// <summary>The effect ends as soon as a hit gets through to its bearer, from anything (damage left after its own
        /// reductions): a channel, a trance, a hiding place a hit gives away. onBreakActions run as it goes, with whoever
        /// applied it as the caster.</summary>
        public bool breakOnHurt;
        public List<ActionSpec> onBreakActions = new List<ActionSpec>();
        /// <summary>Added to the share of a roll your off-hand weapon deals (the game's base is 0.5): 0.25 = off-hand swings
        /// at 75%. Per stack. On the crawler only.</summary>
        public float offhandPower;
        /// <summary>Game character tags the bearer counts as having while this is on: "BULLET_DODGER" (dodge rolls work on
        /// ranged attacks too), "NOKNOCKBACK", "FLYING", ... Read live, never written to the save.</summary>
        public List<string> grantTags;
        /// <summary>% chance a consumable you use isn't spent (per stack, the chances of several effects add up). On the
        /// crawler only. keepItemMatch narrows it to items carrying that tag ("CHEM", "GRENADE") or whose id or name
        /// contains it; several joined with "|".</summary>
        public int keepItem;
        public string keepItemMatch;
        /// <summary>% chance, per stack, that each round your guns spend goes back into the load (the shot still has
        /// its full damage). On the crawler only.</summary>
        public int ammoSave;
        /// <summary>Rules for the floor's damaging hazards while this is on the crawler, whoever laid them (see HazardRuleSpec).</summary>
        public List<HazardRuleSpec> hazardRules;
        /// <summary>Added to the damage multipliers per stack: -0.12 = 12% less damage dealt for each stack.</summary>
        /// <summary>The bearer gets this share of its owner's attributes (whoever applied it), kept current every turn.</summary>
        public float ownerShare;
        /// <summary>Stats worth a share of another stat of whoever applied it, per stack, kept current each turn:
        /// [{ "stat": "Armor", "from": "Toughness", "per": 2 }] (one Armor per 2 Toughness). See StatFromSpec.</summary>
        public List<StatFromSpec> statsFrom;
        /// <summary>The bearer can't move while it lasts, like a vanilla root (flying and root-immune bearers shrug it off).</summary>
        public bool root;
        /// <summary>% chance a projectile or energy attack on the bearer is turned back at its attacker (see Reflect).</summary>
        public int reflect;
        /// <summary>% of enemy sight checks on the bearer that fail while it holds, so enemies more than a tile away lose track of
        /// it (rolled once per enemy per turn; see Concealment). The dark hides: Shadow Magus's Unlit and Nightwalker.</summary>
        public int concealment;
        /// <summary>Effects the bearer can't get ("@Gloomrot", "#Poisoned", "EFFECT_Poison", "CONFUSE"), see Immunity.</summary>
        public List<string> immune;
        /// <summary>No damage from your own side reaches the bearer (your summons, bursts and damage over time).</summary>
        public bool immuneToAllies;
        /// <summary>How much of a reflected attack goes back (default 1 = all of it).</summary>
        public float reflectShare;
        public float damageDealtPerStack;
        public float damageTakenPerStack;
        /// <summary>Vanilla effect whose particle to show on the bearer.</summary>
        public string vfx;
        /// <summary>A mod particle looping on a bearer other than you for as long as the effect holds (on you it would fill your view).</summary>
        public string aura;
        /// <summary>Triggers while the bearer has the effect (for buffs on you).</summary>
        public List<ProcSpec> procs = new List<ProcSpec>();

        /// <summary>&gt;1 makes the effect stack: re-applying adds stacks; stats, resists and dot scale per stack.</summary>
        public int maxStacks = 1;
        /// <summary>At this many stacks the effect bursts: burstActions run, then the effect is removed. 0 = never.</summary>
        public int burstAt;
        /// <summary>Burst hits enemies within this radius of the bearer (0 = the bearer only).</summary>
        public float burstRadius;
        public List<ActionSpec> burstActions = new List<ActionSpec>();
        public string burstVfx;
        public string burstSound;

        /// <summary>One-shot triggers when stacks first reach a value (without removing the effect).</summary>
        public List<ThresholdSpec> thresholds = new List<ThresholdSpec>();
        /// <summary>Run when the effect is first applied (stance transitions).</summary>
        public List<ActionSpec> onApplyActions = new List<ActionSpec>();
        /// <summary>Run at each turn's end while it holds, as whoever applied it (a virus working inside a target, a drain
        /// feeding its caster): "to": "self" is the applier, the rest lands on the bearer, or around it with tickRadius.</summary>
        public List<ActionSpec> tickActions = new List<ActionSpec>();
        public float tickRadius;
        /// <summary>Run when the duration runs out naturally (delayed detonations). Not on burst or purge.</summary>
        public List<ActionSpec> onExpireActions = new List<ActionSpec>();
        public float onExpireRadius;
        /// <summary>Run as the bearer dies while carrying the effect (whoever killed it), before it's gone: summons appear beside it.
        /// "to": "self" means whoever applied the effect.</summary>
        public List<ActionSpec> onDeathActions = new List<ActionSpec>();
        /// <summary>Only one effect per group can be on a character; applying one removes the others (stances).</summary>
        public string group;
        /// <summary>The bearer can't lose health while it lasts, whatever the source (hits, damage over time, hazards, executes).
        /// With "damageTakenMult": 0 the hits also show as nothing.</summary>
        public bool invulnerable;
        /// <summary>When the bearer would die: survive at this much health, run onSaveActions, and lose the effect.</summary>
        public int preventDeath;
        /// <summary>preventDeath only saves the bearer while this condition holds (both "self" and "target" are the bearer):
        /// { "has": "@Vow" }, { "inCombat": true }, { "value": "enemiesNear:3", "atLeast": 2 }. Unmet, the effect stays on.</summary>
        public WhenSpec preventDeathWhen;
        /// <summary>Seconds the camera pulls out over your shoulder when this lands on you (DashCam).</summary>
        public float thirdPerson;
        public List<ActionSpec> onSaveActions = new List<ActionSpec>();

        /// <summary>
        /// Makes the effect a transformation: while it is on you, these abilities take over your hotbar and your other
        /// active abilities can't be used (passives keep working). The normal hotbar comes back when it ends.
        /// </summary>
        public List<AbilitySpec> kit;
        /// <summary>Names of your own abilities that stay usable (and on the hotbar) during the transformation.</summary>
        public List<string> keep;
        /// <summary>While it is on you, the rules of casting change for every skill you use. See CastRuleSpec.</summary>
        public CastRuleSpec castRules;
        /// <summary>Only this class's abilities give way to the kit; your other skill trees stay usable.</summary>
        public bool keepOtherClasses;
        /// <summary>The effect's procs count its stacks as their rank (perRank grows with the stacks). Synergies use it.</summary>
        public bool rankFromStacks;
        /// <summary>Each stack past the first adds this to its procs' chance (percent). Synergies use it for their level.</summary>
        public int procChancePerStack;
        /// <summary>Rewrites of your skills while this effect is on you (a stance, or a charge an active skill puts on
        /// you for the next cast). See SkillMods.</summary>
        public List<SkillModSpec> modifies;
    }

    public class ThresholdSpec
    {
        public int at;
        public float radius;
        public string vfx;
        public string sound;
        public string message;
        public List<ActionSpec> actions = new List<ActionSpec>();
    }

    public class DamageTick
    {
        public string type = "Void";
        public int min = 1;
        public int max = 2;
        /// <summary>Stats added to each tick, from whoever applied it, as in an action's "scale".</summary>
        public string scale;
        public float scaleMult = 0.1f;
    }
}
