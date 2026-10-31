# Breachwork: a mod pack for StarCrawlers Chimera

The Breachwork Loader (a BepInEx plugin in `BepInEx/plugins/StarCrawlersClassLoader`) adds new
skill trees to character creation, so you get more Dredmor-style combos to pick your 7 from.

## Adding a class

1. Make a folder under `Mods/`, e.g. `Mods/MyClasses/`, with a `mod.json` (see [mod.json](#modjson)); if you
   use library effects or templates, put `"requires": ["Library"]` in it.
2. Put one `.json` per class in it (or an array of classes in one file), plus an optional PNG icon.
3. Start the game. The class shows up in its archetype column on the **Select Your Skills** screen.
   Once there are more than 4 rows, the grid scrolls (mouse wheel, the scrollbar on its right, or
   controller navigation).

`Mods/CustomClasses/`, `Mods/UniqueClasses/` and `Mods/ArendethClasses/` hold 36 ready-made classes with their own
abilities, resources, stack mechanics and summons, built on the shared library. None borrows vanilla abilities, so
any of them can be picked together.

`Mods/KelvinMagus/` (a frost mage that freezes enemies solid, banks the heat it steals and vents it to crack them,
with a Glacier Sentinel golem) and `Mods/LookingGlass/` (a chaotic agile duelist that switches between Shrunk and
Grown, deals Painted Guards, throws a Mad Tea Party and ends as the Grinning Cat; also the worked example of
conditions, counters and triggers, see its README) are single-class mods whose icons, particle sheets, meshes,
animated summons and sounds all come from the AAMT asset pipelines (`_modding/make_kelvin_glass_assets.py`).

`Mods/vanilla_reference.txt` is rewritten every launch. It lists every vanilla tree id, its tiers and
abilities, stats, damage types and item ids. Use it to choose what to borrow.
`Mods/ExampleClasses/arc_gunner.json` is a working example.

## Format

```jsonc
{
  "id": "MOD_ArcGunner",            // unique, written into save files – never rename it later
  "name": "Arc Gunner",
  "description": "Text shown on hover. [STR] [TGH] [AGL] [REC] [SAV] [SMT] [PSI] [HP] [ARM] become stat icons.",
  "archetype": "Intellect",         // Muscle | Guile | Intellect
  "icon": "arc_gunner.png",         // PNG next to the json (square, ~256px)
  "iconFrom": "SKILLTREE_Hacker/4", // fallback when the png is missing: a tree's icon, or "/N" for its tier-N ability icon
  "enabled": true,
  "unlock": "win",              // optional: off the creation screen (grid, random picks, Conjoined bodies) until this
                                //   computer has seen the game won: a win recorded by the loader, a won run in the
                                //   graveyard, or the game's Steam achievement for escaping. Classes that spoil the
                                //   ending use it (the Ichorborn). It stays registered, so saves with it still load.
                                //   [Unlocks] UnlockAll = true shows every class anyway.

  "ignoresAllies": false,       // true: nothing hostile the class does (damage, debuffs, shoves, stuns, swaps) lands on
                                //   your side (you, pets, summons, thralls, crew); buffs and heals still do. Rimelight Seer.
  "starterCredits": 0,
  "starterItems": ["WEAPON_R_Street_Special"],  // vanilla ids from vanilla_reference.txt, or mod items "Mod:Id"
  "starterGearFrom": ["SKILLTREE_Pistolier"],   // also give these trees' starter gear
  "attributePriorities": { "Smarts": 4 },       // preview summary; defaults to the borrowed trees'
  "playerTags": { "SOME_TAG": 1 },
  "inheritTags": true,                          // copy tags of the trees you borrow from

  "tiers": [                                    // 1–9 tiers; tier 0 is unlocked at the start
    {
      "from": "SKILLTREE_Pistolier", "tier": 0, // borrow this vanilla tier (ability, passive and effect)
      "name": "optional tier name", "description": "optional tier text",
      "attributes":    { "Recognition": 1 },    // replace the tier's stat bonuses
      "addAttributes": { "RangedAccuracy": 1 }, // or add on top
      "resistances":    { "Fire": 10 },         // replace resistances
      "addResistances": { "Shock": 5 },         // or add on top
      "ability": {                              // tweak the borrowed ability (all optional)
        "name": "Arc Discharge", "description": "...", "icon": "arc.png",
        "staminaCost": 25, "cooldown": 2, "hitChance": 95, "range": 6,
        "damage": [ { "type": "Shock", "min": 2, "max": 7 } ]
      }
    }
  ]
}
```

## Brand-new abilities

A tier can define a completely new ability with `"newAbility"` instead of borrowing one with `"from"`/`"tier"`.
The rules text in the tooltip (damage, scaling, chances, effects) is generated automatically from the actions;
`description` is just flavor. A skill whose actions and procs are too intricate to list in a tooltip that still fits on
screen can set `"rules"` to hand-written rules text, shown instead of the generated lines (keep its numbers in step
with the actions). Random outcomes that only give items, or have more than five options, are listed on one line.
Every class in `Mods/CustomClasses/`, `Mods/UniqueClasses/` and `Mods/ArendethClasses/`
is built this way.

```jsonc
{ "attributes": { "Smarts": 1 }, "resistances": { "Psi": 5 },   // tier stat/resist bonuses, as before
  "newAbility": {
    "name": "Chrono Bolt", "icon": "chrono_bolt.png", "description": "flavor text",
    "type": "active",            // active | passive
    "target": "enemy",           // enemy | floor | self
    "range": 7, "radius": 0,     // radius > 0 hits every enemy in that area (around you for self abilities)
    "shape": "line", "width": 1, // or "cone" with "angle": 60: everyone in that shape from you toward the target, out to range
    "staminaCost": 12, "cooldown": 0, "instant": false, "dodgeable": false, "melee": false,
    "resetOnKill": false,        // true = a kill with it makes it ready again
    "castVfx": "PA_Psykonics_Temporal.castParticle",                 // borrowed vanilla particles,
    "impactVfx": "PA_Elementalist_ChainLightning.shockImpact",       // see vanilla_reference.txt; "name@0.5" = half size
    "travelVfx": "MyBolt", "travelSpeed": 14,                        // flies caster -> target before the hit lands
    "areaVfx": "MyBlastRing",                                        // once at the centre of an area / floor aim point
    "dashAs": "Hoarlight",       // a creature id: while it moves you (leap, warp), the third-person dash shot shows you as it
    "sound": "event:/Abilities/Misc/Psykonics_Warp",                 // vanilla FMOD event
    "actions": [
      { "do": "damage", "type": "Energy", "min": 2, "max": 5, "scale": "Smarts", "scaleMult": 0.4, "perRank": 1 },
      { "do": "effect", "effect": "@TimeLocked", "chance": 25 }      // "@x" = this class's effect, else a vanilla id
    ],
    "procs": [                   // Dredmor-style triggers: hit | kill | hurt | dodge | turn
      { "on": "hurt", "chance": 20, "radius": 0, "actions": [ { "do": "heal", "to": "self", "amount": 4 } ] }
      // "radius": 5, "pick": 1 = one random enemy within 5 (a turn proc's way to aim at somebody)
    ],
    "perk": { "name": "Paradox Bolt", "damageMult": 1.3, "chanceBonus": 25, "cooldownDelta": -2,
              "staminaDelta": 0, "radiusDelta": 0, "rangeDelta": 0, "addActions": [] }
  } }
```

**Actions** (`"do"`): `damage`, `heal`, `stamina`, `shield` (overshield), `effect`, `stun` (`turns`), `knockback`
(`amount`), `blind`, `cooldowns` (reduce your other cooldowns by `amount`), `purge` (remove `effect`), `teleport`
(floor abilities; same room only), and the control actions `taunt`, `silence`, `exhaust`, `swap` (see below). Any action can have `"to"` (`target` default, `self`, `attacker`, `victim`),
`"chance"`, `"scale"` + `"scaleMult"` (add a stat), `"perRank"`, and `"ifTargetHas": "@Hexed"` (+ `"consume": true`).
**Shapes.** `"shape": "line"` hits everyone on a line from you toward the target or aim point, out to the skill's full
range (it pierces; `"width"` in tiles, default 1). `"shape": "cone"` fans out over the range (`"angle"`, default 60
degrees). A `travelVfx` sweeps along the shape: one streak down a line, three across a cone. Creature and enemy skills
take the same three fields. Rimelight Seer's Rimelance is a line and its Glacial Verdict a cone.

A `damage` action can also take `"weapon": 0.4`: it adds 40% of a roll of your main-hand weapon's damage (or of the
off-hand weapon that fits the skill's gear rule, see Gear requirements), for skills
that are half blade and half spell (nothing without a weapon; mod summons and enemies ignore it). Such a hit is a weapon
hit as well: your weapon's own on-hit effects, your ammo's and every passive that answers weapon hits go off with it.

**Chance, money and summon actions** (`Mods/OddsBreaker/` is a whole class built on them):

- `{ "do": "credits", "amount": 35 }` gives credits (negative spends them, never below 0). An ability can also cost
  credits per use with `"creditCost": 10`; it can't be used without them.
- `{ "do": "random", "options": [ { "name": "Jackpot!", "weight": 1, "weightPerStack": 1, "actions": [...] }, ... ] }`
  runs one weighted outcome on the same target. With `"stacksOf": "@LuckyRun"` (+ `"stacksFrom": "self"`) each
  stack adds `weightPerStack` to that outcome, so luck can build up. The tooltip lists every outcome with its odds.
- `{ "do": "execute", "amount": 10, "bonusPerStack": 12, "stacksOf": "@LuckyRun", "elitePercent": 20 }`: a % chance
  to finish the target outright. Bosses, elites (side-area ones too), rares, nemeses and kill squads take `elitePercent` % of their
  max health instead.
- Any action can have `"ifSummonAlive"` or `"unlessSummonAlive"` (`"self"` = summoned by this ability, `"any"`, or a
  creature name), and `"to": "summon"` aims it at this ability's summon. That gives "use again to command it":
  `[{ "do": "summon", ..., "unlessSummonAlive": "self" }, { "do": "damage", ..., "ifSummonAlive": "self" }]`.

**Custom effects** go in the class's top-level `"effects"` list:

```jsonc
"effects": [
  { "id": "Hexed", "name": "Hexed", "description": "...", "duration": 4, "debuff": true, "stackable": false,
    "stats": { "Dodge": -10 }, "resists": { "Void": -15 }, "dot": { "type": "Void", "min": 1, "max": 3 },
    "hot": 0, "staminaPerTurn": 0, "damageDealtMult": 1.0, "damageTakenMult": 1.0,
    "critDamage": 0.0,            // extra crit damage while it holds (0.35 = crits hit 35% of the hit harder); crit chance is "stats": { "Crit": n }
    "vfx": "EFFECT_Void_Psyker_Drain", "procs": [] }
]
```

Custom abilities and effects are saved and restored with the game like vanilla ones.

**Resource and combo mechanics** (see `Mods/UniqueClasses/` for working examples):

- Effects: `maxStacks` (stats, resists and dot scale per stack; `damageDealtPerStack` / `damageTakenPerStack` add
  to the damage multipliers per stack, e.g. -0.12 = 12% less per stack), `burstAt` + `burstRadius` + `burstActions`,
  `thresholds` (`[{ "at": 5, "actions": [...] }]`, one-shot triggers at a stack count), `onApplyActions`,
  `onExpireActions` + `onExpireRadius` (delayed detonations), `group` (stances: one per group),
  `preventDeath` + `onSaveActions` (cheat death once; kept for the rare class whose theme earns it, like Looking Glass's
  Grinning Cat: a save is a boss's move, so most classes trigger on a `lowHealth` threshold instead).
  **Revenge counters:** every effect keeps a tally of the health its bearer loses while it is on, and a `damage`
  action in its `onExpireActions`, `onDeathActions`, `burstActions` or `thresholds` can add a share of it with
  `"stored": 0.5` (half of all it took). Vasculist's Strung Up bursts this way: the more the puppet soaks fighting
  for you, the bigger the burst. `SCCL_DEVTEST=revenge` tests it.
- Actions: `"stacks"` on effect actions; `bonusPerStack` + `stacksOf` (+ `stacksFrom`: `"self"`, `"target"` or
  `"victim"`, the one that died; `consumeStacks`) to spend stacks. On an `effect` action `bonusPerStack` adds stacks
  and on a `summon` action it adds summons, so one count can set another: `{ "do": "effect", "effect": "@Scored",
  "stacks": 0, "bonusPerStack": 1, "stacksOf": "@Scored", "stacksFrom": "victim" }` in an `onDeathActions` chain hands
  a dying enemy's exact layers to its neighbours, and `{ "do": "summon", ..., "amount": 1, "bonusPerStack": 1,
  "stacksOf": "@Sporeload", "stacksFrom": "self", "consumeStacks": true }` makes one more summon per stack spent.
  `"stacksUsed": 6` counts at most that many stacks and, with `consumeStacks`, spends only those, so a skill can
  cash in part of a pile (Fulgurant's Return Stroke uses up to 6 Potential and leaves the rest). An effect action
  with negative `stacks` takes that many off instead (`"stacks": -2`: a resource that bleeds away).
  With `bonusPerStack` the base (`stacks` / `amount`) may be 0: then nothing happens without stacks; `ifSelfHas` (stance- or state-dependent effects); `lifesteal`; `trueDamage`;
  `dispel` / `cleanse` (`amount`, optional `effect` gained per buff removed); `chain` (`jumps`, `radius`,
  `effect` = only jump to enemies carrying it, `falloff`, nested `actions`; `"leap": true` makes it a charge: you land
  beside each link before it is struck, one hop at a time, and the skill's own self actions wait for the last hop;
  Rimelight Seer's Prism Charge); `echo` (replay your last offensive
  new ability at `falloff` strength).
- Cross-tree riders: `ifSkill` runs an action only when you also have a skill tree (`"SKILLTREE_Pyromaniac"`, a
  mod class id, or a display name; `"A|B"` = either), at `skillRank` or higher (1 = just taken); `unlessSkill` is
  the opposite. Another class's effect can be named as `"MOD_Etcher@Etched"` anywhere an effect id goes.

**Summons** (see `Mods/ArendethClasses/` for Coilwright, Emberhorn, Gloomspore Warden, Runeforger and Castborn):

```jsonc
{ "do": "summon", "creature": "PA_Engineer_Turret.characterPrefab", "amount": 1, "turns": 15, "maxSummons": 2,
  "effect": "@Overclocked" }   // optional effect put on each summon
```

- `creature`: a vanilla prefab from the "SUMMONABLE PREFABS" list in `vanilla_reference.txt` (Junker, Spidermine,
  Turret, Boltron, Void tentacle, `nanite_cloud`, `nanite_plague`), an enemy's internal name (`CORPSEC_DRONE`, or a
  mod enemy's `SCCLMOB_...` id), or part of any creature prefab name.
- Summons fight on your side and scale with your level and Smarts like Engineer pets. `turns` > 0 dismisses them
  after that many of your turns; `maxSummons` caps how many one ability keeps alive at once.
  `"stationary": true` keeps a summon on the tile it appeared on (a decoy, a totem); it still attacks what comes in reach.
- Mod summons don't use up the vanilla 3-pet limit, and you don't need the Engineer tree for Junkers or Turrets.
- `"stacksOf": "summons"` counts your living minions, so `bonusPerStack` can scale with the size of your army.
- `"to": "summons"` runs an action on every living pet (or only those matching `ifSummonAlive`), e.g. a buff for
  the whole swarm.
- `{ "do": "dismiss", "to": "summon" }` recalls one of your pets: it leaves without dying (no corpse, no loot).
  With `"ifSummonAlive": "CORPSEC_DRONE"` it picks a pet whose name matches, and other actions with the same
  condition can pay you back for it (see `Mods/SwarmConductor/`, where a recalled drone refunds stamina).
- `"stackCost": { "effect": "@Reagent", "amount": 2 }` on an ability spends stacks of your own effect per use, and
  the ability can't be used without them. That makes a buildable resource (a mana bar made of stacks). If the
  effect no longer exists (you removed it, or the mod that owned it is off), the ability costs that amount in stamina
  instead, and a save holding its stacks loads without them. Remove a pool freely: old saves keep working.
- **Resource bars and mana pools.** Resources show as small see-through bars just above your health and stamina
  (one pip per stack up to 12, a smooth bar beyond that; hover one for its description). An effect gets a bar
  automatically when one of your abilities pays with it (`stackCost`) or it is a pool. Set `"resource": true` to
  show any other stacking effect of yours, such as a meter that builds toward a backlash (Rimelight Strain), or `false`
  to hide one. A mana-style pool regenerates and starts filled:
  ```jsonc
  { "id": "Mana", "name": "Aether", "duration": -1, "maxStacks": 20,
    "startStacks": 20,          // what you have the first time you have the class (kept in the save)
    "regenPerTurn": 2,          // gained at the start of each of your turns
    "resourceColor": "#4FA3FF" }  // optional; debuffs default to red, others to a fixed colour per resource
  ```
  Abilities then spend it with `"stackCost": { "effect": "@Mana", "amount": 5 }`. Pools regenerate even with the
  bars switched off. `[Resources]` in the config: `ShowOverlay`, `IdleOpacity` (0.55; the bars brighten while a
  resource changes and under the mouse), `Scale`, `ToggleKey` (F3, remembered). The bars hang off the health/stamina
  panel, so they move with it if you drag it.
- **Caps that grow with stats.** `capScale` makes a pool's cap grow with the stats the player gains, so a pool keeps
  up as the character does:
  ```jsonc
  { "id": "Mana", "name": "Aether", "duration": -1, "maxStacks": 20, "startStacks": 20, "regenPerTurn": 2,
    "capScale": { "scale": "Smarts+PsiPower",  // same stat syntax as an action's "scale" ("|" = the higher of two)
                  "perPoint": 0.25,            // stacks per stat point; fractions add up (0.25 = one per four points)
                  "from": 14,                  // growth starts above this stat total
                  "ceiling": 40 } }            // the most it can reach (unset = twice maxStacks)
  ```
  The cap is `maxStacks + perPoint × (stats − from)`, never below `maxStacks` and never above `ceiling`. Set `from`
  to about what the class has early on so early play stays the same, and keep a ceiling: stats keep rising long after
  a class's tiers are done. The bar, its hover tip and the effect's tooltip show the cap as it is now, and it moves as
  soon as a stat does. If a stat falls (gear off, a debuff) you keep the stacks you hold and the bar reads like `30/24`:
  the pool stops filling until you spend below the cap. A pool that starts full (`startStacks` at `maxStacks` or more)
  starts at its real cap. An effect's own `stats` don't count, so a pool can give the stat it scales with. `capScale`
  is ignored on an effect with `burstAt`: a meter's danger point stays where you set it. `SCCL_DEVTEST=capscale` tests it.
- **Two-stat scaling.** `"scale": "Smarts+PsiPower"` adds `scaleMult` per point of each stat (tooltips read "+24%
  Smarts and Psi Power"). Any stats can be joined with `+`. A part written with `|` counts only the higher stat:
  `"Agility+Smarts|PsiPower"` adds Agility plus whichever of Smarts or Psi Power is higher.
- **Rune sockets.** `"runeSlots": 3` gives an ability sockets; runes set into them retune it. Runes are items with a
  `"rune"` block. Setting one takes it from your bag and taking it out puts it back. By default casting never spends
  it; a skill with `"runeWear": N` wears each socketed rune out after N casts (a rune's own `"wear"` overrides N), and a
  matching rune from your bag slots in when one burns out, or the socket goes empty. Casts used are kept per skill and
  rune, so taking a rune out and back in doesn't reset them. Use wear for classes that can make their own runes. The **rune matrix** (F4, or the "Runes" tab left of the health panel, `[Runes] PanelKey`) lists your
  socketed skills, the runes in your bag and the reactions you know; it is locked in combat (a skill with `{ "do": "runepanel" }` opens it for recharging even there, and `{ "do": "runerestore", "amount": 1 }` puts casts back into every rune for free), and its Transmute button
  opens the crafting window with the rune recipes you know.
  ```jsonc
  "runeSlots": 3, "runeSlotRanks": [2, 4, 6],   // sockets open at these class ranks (1 = just taken); unset = all open
  "runeKinds": ["element", "power", "payload"], // optional: which rune kinds fit
  "runeMeter": "@Instability",                  // what a rune's "instability" feeds each cast
  "runeWear": 8,                                // optional: each rune lasts 8 casts, then a bag copy replaces it
  "runeCharge": 2,                              // optional, with runeWear: nothing burns out; a spent rune stays socketed and
                                                // does nothing until you pay 2 stamina per cast to recharge it in the rune matrix;
                                                // discharging there deactivates a rune and turns its remaining casts back into stamina at the same rate
  "runeDischarge": 8,                           // optional, with runeCharge: after a discharge pays out, others give no stamina for 8 turns
  "runeVariants": [                              // signature combos: what a rune does in THIS skill
    { "rune": "FsTetherRune", "name": "Gravity Well", "replace": true,      // replace: drop the rune's usual effect
      "description": "...", "actions": [ { "do": "pull", "amount": 2, "pullToward": "center" } ] } ]
  ```
  A rune item (`"rune": { ... }` on any item):
  ```jsonc
  { "kind": "element",            // element | shape | power | payload; one element rune per skill, no rune twice
    "tier": 1,
    "wear": 4,                    // casts it lasts in a runeWear skill (unset = the skill's runeWear)
    "element": "Fire",            // the skill's damage type
    "reagent": "EFFECT_Fire",     // replaces the skill's reagents (effects used in some class's reactions)
    "jumps": 1, "radius": 1.2, "range": 3, "power": 0.85,   // fork, burst, reach, damage multiplier
    "cost": -2, "stamina": 2, "cooldown": -2,               // cost changes the skill's stackCost (else stamina)
    "instability": 1,             // runeMeter stacks per cast; negative soaks up what the skill itself adds
    "fieldRadius": 1, "fieldTurns": 1, "fieldActions": [ ], // changes to the skill's ground fields
    "actions": [ ], "selfActions": [ ], "impactVfx": "..." } // extra actions on each target / on you once per cast
  ```
  Runes in sockets that lock again (a lower rank) wait there and do nothing. In other mods' runes, refer to a class's
  effects as `"MOD_FluxSavant@Residue"` (rune files aren't part of a class).
- **Spell augments.** A class with an `"augments"` list lets its rune-socketed skills buy rewrites from a shared pool,
  in the rune matrix: each skill's row gets an Augments button, and the right column turns into that skill's bench. The
  pool is the class's rank plus one per rune-socketed skill you have perked, so it grows as those skills advance. A skill
  takes one delivery, one flaw and as many behaviors as you can pay for. Changing them is locked in combat, like runes,
  and a skill that no longer fits a smaller pool goes dark (it works unaugmented) until there is room again. Augments
  never apply to a skill without sockets, and a class without the list shows nothing. Everything a rewrite takes works
  here (see below), `"when"` and `"roll"` included.
  ```jsonc
  "augments": [
    { "id": "Beamline", "name": "Beamline",
      "kind": "delivery",               // delivery | behavior (the default) | flaw
      "points": 2,                      // the price; on a flaw, what it takes off this skill's bill (it never goes below 1)
      "rank": 2,                        // the class rank it opens at (default 1)
      "skills": "Arc Lance|Flask",      // optional: limit it to some skills
      "form": "line", "power": 0.85,    // any rewrite fields; a lower power, more stamina or cooldown, less range show as its price
      "burn": 1,                        // every rune in the skill loses this many extra casts of charge per cast
      "seal": 1,                        // shuts this many of the skill's sockets, from the last open one; a rune in one waits and wears nothing
      "drawback": "...",                // the price in words, for one the numbers don't show
      "resonance": [                    // only while a rune it names is set, open and charged
        { "rune": "FsLensRune", "name": "Focused", "range": 2, "power": 1.15 } ] }   // a rune kind or a rune item
  ]
  ```
  A flaw only discounts while it costs you something: `burn` needs a rune set in the skill, and `seal` needs every open
  socket full. An option that would do nothing on a skill isn't offered: no form on a skill that lays a field, `area` on
  what already bursts, `resetOnKill` without a cooldown, `pierce` on an area skill. `burn` also works on a rune or any
  rewrite. `Mods/FluxSavant/flux_savant.json` has a full set; `SCCL_DEVTEST=augments` plays it through the real buttons.
- **Alchemical reactions.** A class's `"reactions"` list: when an effect lands on an enemy that already carries its
  partner, the two react on contact instead (both used up) and the reaction's actions run with you as the caster.
  Reactions only work while you have that class, but they count reagents from anywhere: your weapons, other classes,
  vanilla fire and shock. Checked before the game applies the status, because some vanilla statuses cancel each other
  (chill puts out fire). A reaction can set off another, up to a small limit.
  ```jsonc
  "reactions": [ { "name": "Plasma Flash", "needs": [ "EFFECT_Fire", "EFFECT_Shock" ], "radius": 1.5,
                   "description": "a blinding burst", "vfx": "...", "sound": "...", "consume": true,
                   "actions": [ { "do": "damage", "type": "Energy", "min": 5, "max": 9 },
                                { "do": "stamina", "to": "self", "amount": 2 } ] } ]
  ```
  `Mods/FluxSavant/` uses all of this: 18 runes in three tiers, 12 signature combos and 10 reactions.
- **Reshaping skills (augments).** A passive you know, an effect on you (a stance, or a charge an active skill puts
  on you for the next cast) or a rune in a socket can rewrite how another skill is delivered, what it is made of and
  what it does. The skill's tooltip shows every rewrite in force ("Reshaped by ..."), the hotbar icon aims the new
  way when clicked, and the cast runs with the skill's own scaling (its damage, rank, perk and your stats).
  `Mods/ExampleClasses/spellsmith.json` turns your other classes' attacks into traps, grenades, walking bombs, novas, weapon swings, loose drifters and chains, with one-shot loads stacking on top of stances; `SCCL_DEVTEST=skillmods` plays it.
  ```jsonc
  // on a passive or active ability ("while you know it"), or on an effect ("while it is on you"):
  "modifies": [
    { "skills": "Ember Lance",      // "A|B" names (display or internal), "class" (this class's actives, the default),
                                    // "class:MOD_X", "others" (attacks from your other classes), "attacks" (every attack
                                    // you aim, any class), or "*" (every active mod skill you know)
      "name": "Buried",             // shown in the skill's tooltip and the log
      "when": { "value": "health%", "below": 50 },   // optional: only while this holds about you
      "consume": true,              // on an effect: one stack (or the effect) is spent when a rewritten skill is cast
      "form": "trap",               // a new delivery, see below
      "element": "Cryo", "effects": { "#Burning": "#Chilled" }, "damage": { "Fire": "Cryo" },  // what it is made of
      "add": { "knockback": 1, "lifesteal": 0.25, "resetOnKill": true },   // behaviors given (keys below)
      "remove": [ "effect:#Burning", "chain" ],                             // behaviors taken away
      "power": 0.6, "stamina": 8, "cost": 0, "cooldown": 1, "radius": 1, "range": 2, "jumps": 1,  // like a rune
      "actions": [ ], "selfActions": [ ],                                   // extra actions, like a rune
      "target": "floor", "shape": "cone", "angle": 90, "instant": true,     // aim and shape overrides
      "castVfx": "...", "travelVfx": "...", "areaVfx": "...", "impactVfx": "...", "sound": "..." }
  ]
  ```
  Every field but `skills`, `name`, `when` and `consume` is also valid in a rune's `"rune"` block, so a socketed
  rune can be a trap rune or a nova rune, and `add`/`remove` make augment items.
  - **Forms** (`"form"`), each run with the skill's own actions and scaling. The aim changes to fit (a trap, grenade
    or monster is aimed at a spot, a nova needs no aim); `"target"` overrides that.
    - `trap`: laid where you aim; goes off under the first enemy to step on it (`armTurns`, `turns` until it fizzles,
      default 8, `charges`, `fuse`, `triggerRadius`, `look`; with a radius it bursts over everyone around it).
    - `grenade`: thrown at a spot, bursts over everyone within its radius (default 1.5); `travelVfx` is the throw.
    - `monster`: calls a creature there (`creature`, default the Spidermine; `summonName`, `model`, `turns` default 4)
      that bursts into the skill over its radius when it dies, or with `"burstOn": "contact"` as soon as an enemy
      stands beside it at your turn. The carried spell is saved with the game, so a loaded creature still goes off.
    - `drifter`: let loose to wander the deck (`turns`, `steps`, `stepChance`), going off on whoever it meets, you included.
    - `nova`: a burst around you, no aiming (radius default 2.5). Pair it with `"power": 0.6` and `"stamina": 8` for a
      wide, weak, tiring version; `travelVfx` streaks out to every side.
    - `line` / `cone`: that shape from you toward the aim point (`width`, `angle`). `area`: a burst around the target.
      `chain`: hops on (`jumps`, default 1). `scatter`: the aimed foe and `jumps` (default 2) random others in reach.
  - **`add` keys**: skill traits `resetOnKill`, `instant`, `dodgeable`, `melee`, `command`, `reactionSlots`, `pierce`
    (`true`: a line through everyone; a number: flies on through that many past the first), `jumps`; damage traits on every damage action `lifesteal` (0.3), `trueDamage`, `weapon` (0.5 of a
    weapon roll), `scale` ("Smarts", joined to what is there), `scaleMult`, `perRank`, `bonus` (+n to min and max),
    `chance`, `levelCap`; on each target `stun`, `knockback`, `pull`, `blind`, `silence`, `taunt` (turns or tiles),
    `exhaust`, `purge` (an effect id), `effect` (an id or a list, `effectTurns`), `execute` (%), `hazard`, `swap`;
    on you once per cast `heal`, `shield`, `stamina`, `cooldowns`, `selfEffect`.
  - **`remove` entries**: an action kind (`"knockback"`, `"stun"`, `"effect"`, `"effect:#Burning"`, `"damage"`,
    `"damage:Fire"`, `"summon"`, `"field:Name"`; `"chain"` keeps the hits and drops the hops), a damage trait
    (`"lifesteal"`, `"trueDamage"`, `"weapon"`, `"scale"`, `"perRank"`, `"stored"`), `"self"` (its actions on you)
    or a skill trait (`"resetOnKill"`, `"instant"`, `"dodgeable"`, `"melee"`, `"radius"`, `"shape"`, `"travel"`).
  - Several rewrites stack: numbers add up, a later form, aim or swap wins, `add` and `remove` all apply. Rewrites
    only touch mod skills (`newAbility`), never borrowed vanilla ones, and a passive's `equipped` gear rule gates its rewrites.
    A carrier's `@X` effects stay its own when it rewrites another class's skill.
  - **Gambles** (`"roll"`): an entry can hold a list of rewrites instead of one. Each skill draws one (by `"weight"`,
    default 1), the entry's `"chance"` (default 100) being the odds anything is drawn at all, and the skill's tooltip and
    aim show what it drew; casting it draws again. Options that would change nothing about a skill are left out of
    its draw: life steal on a spell with no damage of its own, a stun it doesn't have to lose. Draws aren't saved, so a
    loaded game draws afresh. The Wildcaster's Spell Fever (`SCCL_DEVTEST=spellfever`) has four rolls: form, gain, loss, element.
    ```jsonc
    "modifies": [
      { "skills": "attacks", "name": "a gift", "summary": "may gain a behavior", "chance": 80, "roll": [
        { "name": "Thirsty", "add": { "lifesteal": 0.3 } },
        { "name": "Overcharged", "weight": 2, "power": 1.35 } ] }
    ]
    ```
- **Physical moves.**
  - `{ "do": "throw", "amount": 3, "actions": [ ...impact... ] }` throws the target up to 3 tiles straight away from
    you. If it slams into someone, both take the impact actions; into a wall, it takes them alone. Landing in a
    field or hazard sets that off. Bosses and unmovable enemies aren't thrown.
  - `{ "do": "leap" }` puts you on the free tile beside the target nearest to you (hookshots, pounces, charges); put
    it first in an enemy-target ability's actions.
  - `{ "do": "warp", "warpTo": "behind" }` blinks you onto the free tile beside the target farthest from where you
    stood, facing it. `"warpTo": "random"` (or `"away"`, the farthest spot from you) blinks whoever the action is aimed
    at to a free tile up to `amount` (default 3) away in the same room: enemies, or you with `"to": "self"`. Bosses,
    rooted and NOKNOCKBACK enemies stay put. `"vfx"` replaces the flash at both ends.
  - `{ "do": "show", "model": { "mesh": "models/grin.glb", "scale": 1.9 }, "showStyle": "snap", "showTime": 0.9 }` puts
    a model on the target's body for a moment, as big as you like: `pop` swells in and fades, `snap` opens and then
    clamps shut (jaws, a trap). Purely visual; `Mods/LookingGlass/` bites with it.
  - `{ "do": "recall", "fieldName": "Knife", "actions": [...] }` picks up every field of that name on the floor; each
    one runs the actions on every enemy standing on it or in the straight line between it and you.
  - `{ "do": "ricochet", "fieldName": "Coin", "radius": 4, "actions": [...] }` uses up every field of that name; from
    each, the actions hit the nearest enemy within the radius.
  - `{ "do": "tether", "look": "#C8D0DA" }` draws a hook line from you to the target for a moment (purely visual). Leaps,
    recalls and ricochets draw their own short streaks (dash, knife flying home, gold tracer).
  - A field can carry a mesh: `"model": { "mesh": "models/knife.obj", "texture": "models/knife.png", "wobble": 14,
    "randomYaw": true }` stands the model on its tiles (with or without a `look`). A `look` that isn't a hazard id is
    played as a particle for as long as the field lasts.
  - A floor-aimed ability with nobody at the aim point still lays its fields and hazards there (coins, traps).
  - A field's look or model goes on tiles about 1.9 apart; `"spacing": 1` puts one on every tile (a palisade of
    spikes), `1.4` the middle and the corners.
- **Bodyguard cover.** An effect with `"redirect": 0.5` sends half of every hit its bearer takes to whoever applied it.
  Put it on your summons with `"to": "summons"`.
- **Model animation.** Besides `bob` and `spin`, a model takes `wobble` (degrees of rocking that settles after it
  appears), `pulse` (breathing in size, 0.1 = ±10%) and `randomYaw` (each copy turned differently).
- `Mods/Kinetics/` has seven Muscle and Guile classes built on these: Grappler, Rig Jockey (hardpoints are rune
  sockets with hardware items), Bodyguard, Freerunner, Trick Shot, Blade Juggler and Grapnel Acrobat. Its props
  (coin, throwing knife, grapnel spike, deck debris) are generated by `_modding/make_kinetics_models.py`, and its
  icons by `_modding/make_kinetics_icons.py`.
- `"summonName": "Hoarlight"` names the summons. `"summonGroup": "Hoarlight"` puts summons from several abilities
  (and effects) in one pool: `maxSummons` counts the whole pool, and `"group:Hoarlight"` works in `ifSummonAlive`,
  `unlessSummonAlive` and `stacksOf` (+N per Hoarlight). `{ "do": "dismiss", "to": "summon", "ifSummonAlive":
  "group:Hoarlight" }` spends one. `nanite_cloud` makes a good stand-in body for small light/energy constructs until
  custom meshes are possible.
- `"requires": "summon:group:Hoarlight"` (or `summon:` + any summon spec) makes an ability unusable without one.
- **Taming.** `{ "do": "tame", "amount": 25, "scale": "PsiPower+Smarts", "scaleMult": 1.5, "summonGroup": "Starbound", "maxSummons": 1,
  "effect": "@Starbound", "then": [...], "else": [...] }` binds a hostile creature to you for good, the way Command Override takes
  a robot: a permanent charm, kept through saves, as a pet of your own outside the vanilla pet limit. The odds are the amount plus
  its scaling, up to +50% more the more wounded it is, and the log shows them; `then` runs on success, `else` on a failure (a
  backlash). Bosses and elites can't be tamed; past `maxSummons` the oldest of the group goes wild again. A tamed creature
  counts as killed: you get its experience, and a quest target or contract mark counts as down. It regenerates health by
  its own Health Regen the way you do, and heals on your pets reach it. Rimelight Seer's
  Starbind puts it on a two-turn effect's `onExpireActions`, so the binding takes two turns.
- **Pet targets.** `"target": "pet"` (+ `"petGroup": "Hoarlight"`) aims a skill at one of your own summons. With a
  `{ "do": "phase" }` action the summon is spent and you land on its tile; the skill's area (`radius`) is around that spot
  and never hits the pet. Rimelight Seer's Phase Swap.
- A condition's `"chanceScale": "PsiPower+Smarts"` (+ `"chancePer"`, default 1) adds those stats to its `chance` as a roll
  (5-95%): control rolls such as Coldbound Choir's charm.
- An effect's `"root": true` holds its bearer in place while it lasts, like a vanilla root (flying and root-immune
  creatures shrug it off). Tidewright's Brine Shackle uses it.
- An effect's `"reflect": 35` gives a 35% chance that a projectile (a ranged attack or skill) or an Energy attack on its
  bearer is turned back at whoever made it, at `"reflectShare"` of its strength (default all of it); the shot never lands.
  Melee, damage over time and hazards aren't reflected. Rimelight Seer's Galeward uses it.
- An effect's `"ownerShare": 0.5` gives its bearer half of its owner's attributes (Strength, Toughness, Agility, Recognition,
  Smarts, Savvy, Psi Power), kept current every turn. Starbound beasts fight with half of yours.

**Pulling and ground** (any ability, proc or effect):

- `{ "do": "pull", "amount": 2 }` drags the target up to 2 tiles toward you, one free tile at a time (it stops when
  blocked and never lands on you). `"pullToward": "center"` pulls toward the ability's aim point instead: the enemy
  or floor tile you targeted (use it with `"radius"` for a vortex that gathers everyone into the middle). Bosses and
  NOKNOCKBACK enemies don't move.
- `{ "do": "hazard", "hazard": "HAZARD_Acid", "radius": 1.1, "turns": 4 }` lays a real vanilla hazard on the target's
  tile (and the tiles within `radius`). The game runs and saves it. Ids: HAZARD_Fire, HAZARD_Fire_Mini,
  HAZARD_Fire_Sludge, HAZARD_FelFire, HAZARD_Shadowflame, HAZARD_Gas, HAZARD_Miasma, HAZARD_Shock, HAZARD_Energy,
  HAZARD_Cryo, HAZARD_Acid, HAZARD_Acid_Spreading, HAZARD_Holosmoke, HAZARD_Horror, HAZARD_Mushroom, HAZARD_VoidRift,
  HAZARD_SeethingRot_Static, CONSUME_UTL_Mine (a floor mine), HAZARD_Bomblet, HAZARD_Security_Boobytrap.
- A mod **field**: ground that runs your own actions on whoever stands in it, every turn and when they step in.
  ```jsonc
  { "do": "field", "fieldName": "Brine Slick", "turns": 4, "radius": 1.1, "affects": "enemies",   // or "allies", "all"
    "look": "HAZARD_Acid",                       // borrow a vanilla hazard's look (no hazard logic)
    "pulseVfx": "PA_Elementalist_FrigidBlast.impactParticle",   // optional particle each turn
    "onEnter": true,                             // stepping in triggers it too (default)
    "actions": [ { "do": "damage", "type": "Cryo", "min": 2, "max": 4 }, { "do": "effect", "effect": "@Waterlogged" },
                 { "do": "pull", "amount": 1, "pullToward": "center" } ] }
  ```
  Each character is hit at most once per turn per field. On an area ability, a field or hazard with a `radius` is laid
  once per cast, centred on the aim point (not once per enemy hit); with no radius it goes under each enemy hit. Fields are saved with the game (a save mid-fight keeps them)
  and end after `turns` of your turns. Inside a field's actions, `"pullToward": "center"` pulls toward the middle of the field (a whirlpool).
  The look is placed on every other tile from the middle out (not every tile), the same look from an overlapping field
  isn't doubled up, only one copy keeps the hazard's sound, and refractive "glass" layers are left out. Gas and fog
  looks (HAZARD_Gas, HAZARD_Miasma, HAZARD_Cryo) are big: `"look": "HAZARD_Miasma@0.7"` shrinks them.
- A mod **trap**: a device on one tile that waits for someone to set it off, then runs your actions once.
  ```jsonc
  { "do": "trap", "fieldName": "Snare Mine", "affects": "enemies",   // or "allies", "all"
    "armTurns": 1,          // your turns before it is live (default 0: at once)
    "triggerRadius": 0,     // 0 = stepping onto its tile sets it off; 1.5 = coming beside it too (a tripwire)
    "radius": 1.6,          // when it goes off, its actions hit everyone it affects this close; 0 = only who set it off
    "charges": 1,           // times it goes off before it is used up (0 = until "turns" runs out)
    "fuse": 0,              // > 0: it goes off by itself after that many of your turns (a timed mine)
    "turns": 0,             // > 0: it fizzles out after that long; 0 = it stays on that floor until used
    "maxTraps": 3,          // most of this name at once; laying another removes the oldest
    "vfx": "PA_Engineer_Spidermine.PS_explode_shock@0.45", "sound": "event:/Abilities/Smuggler/Smugger_Flashbang",
    "model": { "mesh": "models/snare.glb", "scale": 0.4 },   // or "look": a hazard id; default: the vanilla mine's look
    "actions": [ { "do": "damage", "type": "Energy", "min": 6, "max": 10 }, { "do": "stun", "turns": 1 } ] }
  ```
  It goes at the ability's aim point (a floor-aimed skill with nobody there, or under the enemy you aimed at); with
  `"to": "self"` / `"attacker"` / `"victim"` it goes under that one instead (a proc that drops a mine at an attacker's
  feet). It goes off when someone it affects steps into it, or is standing in it when your turn ends. Allies walk over
  enemy traps safely, and your allies' paths stay off a trap that hits `"all"`. Actions with `"to": "self"` run once
  on you. Traps are saved with the game and stay on their floor while you are away. In its actions, a `field` or
  `hazard` goes down where the trap was.
- **Wild magic.** `{ "do": "wildCast", "amount": 2, "falloff": 0.8 }` casts random spells from other installed classes at
  the target: any active, aimed mod skill made only of plain effects (damage, statuses, control, ground, chains) and with
  no cost of its own, so a borrowed spell never moves you, summons, crafts or spends another class's resources. Its
  effects stay that class's, area spells hit around the target, and wild spells never land on you or your allies.
  `bonusPerStack` adds spells. `"pool": "monsters"` throws the mod monsters' attacks instead (plain-effect ones), and
  `"pool": "all"` mixes both. The Wildcaster is built on it; `SCCL_DEVTEST=wildcaster` tests it.
  - `"wildForm": 25` on a passive: a 25% chance each of your aimed spells changes form as it leaves you: a burst over
    everyone within 2 of the target, a beam through the line, a split onto two more foes, a swerve onto another foe, a
    one-shot trap laid where you aimed (it arms next turn), or (rarely) the whole spell turned on you.
  - A class's top-level `"wildSpells"` list holds spells nothing else can cast (they never go on a skill bar):
    `{ "name": "Coin Toss", "say": "a line for the log", "weight": 1, "radius": 2, "friendlyFire": true, "impactVfx": "...", "sound": "...", "actions": [...] }`.
    Three in ten plain wild casts are one of these. Their actions aren't limited like borrowed spells: they can move you, lay hazards and traps, or use `"to": "self"`.
    `"friendlyFire"` makes the radius catch everyone, you and your allies too. `"group": "boss"` marks a boss echo, normally one `surge` action.
  - `"pool"` on a wildCast picks from: `"spells"` (default), `"wild"` (only wild spells), `"bosses"` (only boss echoes), `"monsters"`, or `"all"` (anything, 15% a boss echo). `"atYou"` on it gives the echo that % chance of landing on you.
  - On a `summon`, `"anySide": true` tosses a coin for whose side it arrives on. Against you it gives no xp, and `"maxSummons"` (default 3) caps how many can be out; it goes when its `"turns"` end. `"creature": "boss:Enemy_BOSS_Smorn"` picks one of the creatures that boss really summons, read from its abilities. A wild spell's `"minLevel"` keeps it out until your total level reaches that.
  - `{ "do": "horror", "turns": 6, "anySide": true, "actions": [...] }` brings in The Horror for a few turns, hunting you or (without `anySide`, always) your enemies. It runs its actions on its quarry when it reaches it, is never saved, and leaves a Cursed player's own Horror alone.
  - An effect's `"modelScale": 0.6` resizes the bearer's model while it lasts.
  - `{ "do": "surge", "surgeName": "Thundercrunch, an echo of Doctor Cerberus", "atYou": 30, "radius": 2, "actions": [...] }`
    lands on the target, or `atYou`% of the time on you (half your Crit comes off that chance, down to a third of it),
    and runs its actions on everyone within `radius`, friend or foe, you included. The Wildcaster's boss echoes use it.
  - `{ "do": "drifter", "fieldName": "Loose Spell", "vfx": "fx_x_drifter", "turns": 4, "steps": 2, "stepChance": 45, "radius": 1.1, "actions": [...] }`
    sends a loose spell from you toward the target that then wanders on its own, `steps` tiles each turn for `turns`
    turns, mostly keeping its heading and sometimes veering at random. On each tile it enters, `stepChance`% it goes off
    on everyone within `radius`: foes, allies and you. Wild spells it throws can land on you too. Its `vfx` plays on a
    loop, so build it from steady streams (`rate` layers, no flashes or pulses) and leave out the light. Drifters are not
    saved; they fizzle when you leave the map. `{ "do": "vfx", "vfx": "id" }` plays an effect on the target, for random
    tables that want a different look per outcome.
  - `"levelCap": 9` on a `damage`, `surge` or `drifter`: its damage grows with your level, from 30% at level 1 to full at
    that level, then stops. A class's 9 tiers can be complete by level 9; later levels come from other classes.
- **Walls.** A tile's wall sides are the directions with no floor beside it (a room's edge), a blocked tile, or a
  wall you raised; doorways don't count. The condition value `wallsNear` reads how many touch someone (0-4).
  - `{ "do": "wallSpikes", "amount": 2, "model": { "mesh": "models/spike.glb", "scale": 0.6 }, "actions": [ ... ] }`: a spike
    bursts from every wall side of the target (up to `amount`) and runs the actions on it once per spike, so an enemy
    in a corner is hit twice.
  - `{ "do": "raiseWall", "amount": 3, "turns": 5, "model": { ... } }`: a wall `amount` tiles wide across your aim at
    the aim point (a floor skill with nobody there still raises it), on free tiles only. It blocks movement, counts as
    a wall for spikes, throws and `wallsNear`, and crumbles after `turns`. Raised walls don't outlast the floor.
  `Mods/ArendethClasses/bulkhead_psion.json` builds a class on them; `SCCL_DEVTEST=bulkhead` tests them.
- `{ "do": "springTraps", "fieldName": "Snare Mine" }` sets off every one of your traps of that name on this floor
  at once, live yet or not (no name = all of them): a remote detonator.

**Particles.** Particle names played on the spot (`castVfx`, `impactVfx`, `pulseVfx`, `burstVfx`, a proc's or
threshold's `vfx`, a field's `look`) take a size (an effect's own `vfx`, the aura it wears, does not):
`"PA_Engineer_Spidermine.PS_explode_bio@0.45"`. Many vanilla explosions are made to be seen from across a room and fill
the screen when they go off on the enemy in front of you, so shrink explosions and clouds used as impacts. At most three
copies of one particle play at once, so an area hit on six enemies doesn't stack six explosions. A floor-target
`teleport` aimed at an occupied tile lands on the free tile beside it nearest to you.

- `{ "do": "detonate", "to": "summons", "ifSummonAlive": "group:Gloomcap" }` kills your own summons on the spot so
  their `onDeathActions` go off (chains of caps setting each other off). Bursts carried by your own allies never hit
  the ally itself. `"unlessTargetHas": "@X"` skips an action on targets that carry X.
- Effects on summons fire their own procs: `hit` / `kill` (the summon's attacks), `hurt` (it gets hit) and `turn`,
  with the summon as the caster (`"to": "self"` = the summon).
- Effects can have `"onDeathActions"`: they run as the bearer dies with the effect on it (whoever killed it), while it's
  still there, so a summon appears beside it; `"to": "self"` is whoever applied the effect and `"to": "victim"` the
  one dying. A `chain` there starts at the one dying but only passes things on: nothing lands on the corpse.

**Controlling enemies and cooldowns** (any ability, proc or effect):

- `{ "do": "taunt", "turns": 2 }` on an enemy: it attacks you for 2 turns. On you or an ally, `{ "do": "taunt",
  "to": "summons", "ifSummonAlive": "group:Gloomcap", "radius": 3, "turns": 2 }`: enemies within `radius` (default 4)
  that can see that ally attack it instead. The enemy wears **Taunted** and goes back to normal when it can't see its
  taunter, when the taunter dies, or when the turns run out. A later taunt by someone else takes over.
- `{ "do": "silence", "turns": 2 }`: the enemy wears **Silenced** and can only use its abilities without a cooldown
  (basic attacks); with none it closes in instead. A channel already under way finishes. Bosses shrug it off.
- `{ "do": "exhaust", "amount": 2 }`: every ability of the enemy that has a cooldown needs 2 more turns to come back
  (the enemy-side stamina burn: their big moves wait). Basic attacks are untouched. Works with `bonusPerStack`.
- `{ "do": "swap" }`: you and the target trade tiles (stepping onto a tile triggers what's there, like walking). Not
  with bosses, NOKNOCKBACK or rooted enemies, or when either tile is shared.
- `"resetOnKill": true` on an ability: if the cast kills anything it hit, its cooldown starts over at 0 and you can
  use it again right away. The tooltip says so.
- The two effects have ids `SCCLFX_SCCLCore_Taunted` and `SCCLFX_SCCLCore_Silenced` for `ifTargetHas` /
  `unlessTargetHas` (e.g. hit silenced enemies harder).

**Reactions** (`Mods/ReactionArchitect/` is a whole class built on them):

```jsonc
{ "do": "arm", "reaction": {
    "name": "Tripwire",
    "on": "approach",          // approach | retreat | special | hurt | ranged | lowHealth | debuff | dodge
    "who": "allies",           // all but special / dodge: "you" (default) or "allies" (you or any summon/pet)
    "threshold": 40,           // lowHealth: the health % the hit has to take you under
    "charges": 2, "turns": 8,  // goes off up to 2 times; disarms after 8 of your turns
    "priority": 40,            // higher answers first when several react to the same thing
    "range": 8,                // special: only enemies this close
    "interrupt": false,        // special: cut the enemy's ability off (wasted, on cooldown)
    "vfx": "PA_Elementalist_ChainLightning.shockImpact@0.5", "sound": "...",
    "actions": [ { "do": "damage", "type": "Shock", "min": 3, "max": 6 }, { "do": "effect", "effect": "SLOW", "turns": 2 } ] } }
```

- An ability with an `arm` action puts the reaction in one of your **reaction slots**. You have 2. An ability with
  `"reactionSlots": 3` raises that while you know it (the highest wins). Arming with every slot full pushes out the
  oldest, and arming the same reaction again refreshes it instead of adding a second copy.
- **Triggers:**
  - `approach`: an enemy steps onto a tile next to you (or next to an ally).
  - `retreat`: an enemy that was next to you (or an ally) steps off to a tile that isn't.
  - `special`: an enemy within `range` uses an ability with a cooldown.
  - `hurt`: an enemy hits you (or an ally).
  - `ranged`: an enemy hits you (or an ally) from two or more tiles away (a `hurt` reaction answers it too).
  - `lowHealth`: an enemy's hit takes you (or an ally) from `threshold` % health or more to under it.
  - `debuff`: an enemy puts a debuff on you (or an ally) on its turn.
  - `dodge`: you dodge an enemy.
- The reaction's actions run on the enemy that set it off (`"to": "victim"` is whoever it went for, e.g. a shield
  for the ally that got hit). They use the arming ability's rank and perk.
- **Interrupts:** a `special` reaction runs before the enemy's ability goes off. With `"interrupt": true`, or when
  the reaction leaves the enemy dead, stunned or silenced, the ability is cut off: wasted, and on its cooldown.
- **Bounded queue:** reactions answer one at a time in priority order. A moving enemy is answered once it stands
  on its tile. Reactions never set off other reactions. Each enemy sets off a given reaction at most once per round,
  and at most 5 reactions go off between two of your turns.
- `{ "do": "spring" }` sets off every armed reaction once right now at the nearest enemy it can reach, spending a
  charge each.
- The proc trigger `"on": "reaction"` fires whenever one of your reactions goes off (with that enemy as the target).
- A panel above the hotbar's left end lists what's armed (name, trigger, charges, turns left), with the full rules
  on hover. It only appears for characters with an ability that arms reactions. Armed reactions are saved with the
  game. `SCCL_DEVTEST=reactions` checks all of this against real enemies.

**Transformations** (a real kit swap; `Mods/ArendethClasses/squallblade.json` has Squallform):

```jsonc
{ "id": "Squallform", "name": "Squallform", "duration": 8, "stats": { "Dodge": 20 }, "procs": [ ... ],
  "keep": [ "Edgewind" ],            // optional: abilities that stay usable (by name), put on the bar if missing
  "keepOtherClasses": false,         // true = only this class gives way (default: everything does)
  "kit": [                           // brand-new abilities, written exactly like a tier's newAbility
    { "name": "Gust Lash", "icon": "gust_lash.png", "range": 5, "actions": [ ... ] },
    { "name": "Settle", "target": "self", "instant": true,
      "actions": [ { "do": "purge", "effect": "@Squallform" } ] }   // a way out before it runs out
  ] }
```

- While the effect is on you, the kit's abilities take over the hotbar, and every other active ability is locked:
  all your skill trees and gear abilities, not just this class. The hotbar pages don't flip.
- **Commands still work** and keep their slots: consumables, the Apartment Portal, ChimeraLinq, the Vendordamus
  beacon, the special-action bar, weapon swap, the plain weapon attack, the pet command bar, and command abilities
  (orders to your summons, see below). Passives keep working. The kit fills the free slots from the left.
- **Command abilities**: a new ability whose actions all go to your summons (`"to": "summon"` / `"summons"`) or to
  you, with at least one on a summon and nothing summoned, is a command automatically (Swarm Conductor's Priority
  Override and Recall Protocol, Gloomspore's Colony Exhale). `"command": true` marks one by hand (Target Uplink:
  painting a target for your drones), `"command": false` opts one out. The tooltip says "a command".
- The kit's abilities aren't in the skills screen and take no perk points.
- When the effect ends (it runs out, gets purged, or you die), your hotbar comes back exactly as it was.
- Kit cooldowns carry over between transformations in one session, so leaving and re-entering doesn't reset them.
- A save made mid-form stores your normal hotbar, and loading it goes straight back into the form while the effect
  is still on you. The game without the mod just loads the normal hotbar.
- Kit abilities without an `icon` use the effect's icon, then the class icon. `"base"` library templates work in a kit.
- **Synergy kit skills:** a kit ability with `"ifSkill": "MOD_Fulgurant"` (a tree id, mod class id or name; `"A|B"` =
  either; `"skillRank"` for a minimum) only joins the kit if you have that tree too, so a form can get its own take on
  another class's skill. Its tooltip says which training it comes from. The Mimeticist's forms have eight.
- `SCCL_DEVTEST=forms` checks all of this in game.

**Pets** (`[Minions]` in the config file):

- `RaiseCap = true` / `Cap = 6`: keep up to 6 vanilla pets instead of 3. A mod's `minionCap` tweak wins over this.
- `SwapWithPets = true`: with tile sharing off, walking into one of your pets (forwards, backwards or strafing) swaps
  places with it, so a pet with nowhere to step aside no longer blocks a corridor. Off = the vanilla nudge.
- `CommandBar = true`: a strip on top of the hotbar with a button per pet (name and health; "scrap" = it cost scrap).
  Click it twice to unsummon the pet (no corpse, no loot). Right-click twice to break it down: you get back the scrap
  it cost (Engineer turrets, or any summon paid for with Mech Parts). Pets that cost no scrap can't be broken down,
  so summoning never turns into a scrap source.
- `GigamawEatsMinions = true`: the BioLabs plant's lasher vines devour converted minions (any type) and organic
  pets (humanoid or monster) standing in reach, and each one counts toward the feedings that open the fight.
  Mechanical pets are never eaten. Off = vanilla (they only get nicked for 5%).
- `JoinFights = true` / `JoinRange = 10`: in vanilla an idle pet spots enemies but never acts on it, so pets, summons,
  converted enemies and hired crawlers stood beside a fight until something hit them. Now, while you're in combat, an
  idle companion on Engage goes for the nearest enemy it can see, or one fighting you or your side within `JoinRange`
  tiles in the same or a linked room. Out of combat they still leave sleeping mobs alone, so you can sneak past. Pets
  under another order (Stand down, Hold, ...) or out on a job, and neutral mobs (quest targets), are left be.
  `SCCL_DEVTEST=petwake` plays it over real turns: with it off and with it on, for a pet and a hired crawler.

## Conditions, counters and triggers

The example class `Mods/ExampleClasses/cadence_fencer.xml` uses every feature below once; `Mods/LookingGlass/` builds a whole class on them (its README maps each feature to the skill that uses it). `SCCL_DEVTEST=conditions` checks them all in game, and `SCCL_DEVTEST=triggers` sets off each newer trigger through the game itself.

**Conditions (`when`).** A condition can go on:
- any action, as `"when"`: the action runs only if it holds;
- any proc, as `"when"`: the proc fires only if it holds;
- an ability, as `"usableWhen"` (checked about you) or `"targetWhen"` (checked about the target).

```jsonc
"when": { "value": "health%", "of": "target", "below": 30 }           // compare a number
"when": { "has": "EFFECT_Bleed", "of": "victim" }                     // has / lacks an effect ("@X", "#X", "MOD_Class@X")
"when": { "any": [ { "tag": "boss" }, { "value": "stacks:@Hexed", "atLeast": 3 } ] }
"when": { "all": [ ... ], "not": { "inCombat": true } }
"when": "Wounded"                                                     // a named condition (see below)
```

- **Numbers** (`value`):
  - your own or anyone's: `health`, `health%`, `stamina`, `stamina%`, `shield`, any stat (`Strength`, `Armor`, ...), `stacks:EFFECT`, `counter:NAME`;
  - around someone: `enemiesNear:3`, `alliesNear:3` (the number is the radius);
  - global: `summons` (or `summons:GROUP`), `rank:TREE`, `level`, `credits`, `distance` (you to the target), `turnsInCombat`.
- **Comparisons:** `below`, `above`, `atLeast`, `atMost`, `is`.
- **`of`** says whose number or effects are checked:
  - `self`; `target` (the default for `has` / `lacks` / `tag`); `victim` (who you hit or killed); `attacker` (who hit you);
  - `other`: whoever else is in a proc's event; `ally`: the ally in an ally trigger (see Triggers).
- **Other checks:** `tag` (`"elite"` and `"boss"` both match every elite, rare, side-area elite, boss, miniboss, nemesis and kill squad), `skill` + `skillRank`, `inCombat`, `melee`, `chance`, `kind` (the creature's kind: `"Monster|Supernatural"`), `perk` (`true` only once this skill's perk is bought, `false` only before: a perk that changes what a skill does, like Rimelight Seer's Zenith Calling). Everything on one node must hold.
- **Gear** (`equipped`): `"when": { "equipped": "melee+shield" }` holds while you have that gear on (see below).
- **Named conditions:** a class or library defines them once and anything can use them by name:
  `"conditions": { "Wounded": { "value": "health%", "of": "target", "below": 40 } }`.

**Gear requirements.** `"equipped": "ranged"` on an ability makes it need that gear. An active can't be used
without it: its hotbar icon locks, clicking it says what it needs, and the tooltip shows the rule (red while your gear
doesn't fit). A passive's procs stay quiet without it. The same string works as the `equipped` key of any condition,
so an action or proc can do something extra with a certain weapon. The Arc Gunner's Arc Round, Thunder Slug and
Static Grip need a ranged weapon.
- Weapon kinds hold with such a weapon in either hand: `melee` and `ranged` (light or heavy), `lightMelee`,
  `heavyMelee`, `lightRanged`, `heavyRanged`, `heavy`, `object` (orbs), `weapon` (anything but a shield),
  `damage:TYPE` (it deals that type: `damage:Shock`). `unarmed` = no weapon in either hand (a shield is fine).
- One hand only: `mainhand:KIND` / `offhand:KIND` (`offhand:melee`), or `mainhand` / `offhand` for anything there.
  `shield` = a shield generator in either hand, `dualWield` = a weapon in each hand.
- **One weapon per swing.** A skill's `"weapon"` share (and the on-hit effects that come with it) uses one weapon,
  never both: the main-hand one if it fits the rule, else the off-hand one that does, at the game's off-hand power
  (50%, raised by dual-wield skills, the same as an off-hand attack). A rule that names no weapon (`dualWield`) uses
  the main hand. With a blade and an off-hand pistol, Arc Round fires the pistol at half power; the tooltip names the
  weapon it swings and the power.
- Slots: `head`, `torso`, `belt`, `hands`, `legs`, `feet`, `neck`, `accessory`, `ammo` (something in it).
- `item:ID` (vanilla or `Mod:Id`), `tag:TAG` (any equipped item with that tag, e.g. a mod item's `tags`).
- Combine them: `|` = either, `+` = both, `!` = not (`"melee+shield|dualWield"`, `"!heavy"`). Only the active weapon
  set counts, and only you have gear to check (summons and enemies never match). `SCCL_DEVTEST=equipped` tests it.

**`if` action.** It branches on its condition:
`{ "do": "if", "when": "InRhythm", "then": [ ... ], "else": [ ... ] }`.

**Counters.** A counter is a named number on a character, with no effect or icon needed. Yours are saved with the run.
- `{ "do": "counter", "to": "self", "counter": "Cadence", "amount": 1, "counterMax": 5 }` adds to it. A negative `amount` takes away, `counterSet: true` sets it, and `counterMin` floors it (default 0).
- Read it in conditions as `counter:Cadence`.
- Feed it to an amount with `"bonusPerStack": 3, "stacksOf": "counter:Cadence", "stacksFrom": "self"`; adding `"consumeStacks": true` zeroes it afterwards.

**Triggers (`"on"`).** Besides `hit`, `kill`, `hurt`, `dodge`, `turn` and `reaction`, a proc can fire on:
- `crit`, `miss`;
- `cast` (you use an ability; `"match"` is its name or id), `wait`;
- `move`: your tile changes, which is checked once the move really happens, so a blocked step doesn't count. `"match"` says which moves:
  - nothing or `"step"`: you walk a tile (diagonals too; a dash or charge is not a step);
  - `"ability"`: your own skill, item or action moves you (a leap, dash, teleport or swap);
  - `"forced"`: something else moves you (an enemy's pull or swap);
  - `"any"`: all of these.
  Taking stairs or entering a side area doesn't count. On a summon or pet, an effect's `move` procs fire on that ally's own steps, so a summon can leave something behind where it walks.
- `heal`;
- `lowHealth` (your health drops below `"threshold"`, default 30%);
- `allyDeath` (one of your summons or pets dies);
- `effectGained` / `effectLost` (`"match"` is the effect id; a lost effect is noticed at the end of your turn);
- `combatStart`, `combatEnd`, `enterRoom`, `loot` (credits), `levelUp`;
- `enemyDeath` (any enemy dies, whoever killed it; aims at the body, so a `radius` bursts around it);
- `allyKill` (one of your summons or pets kills an enemy; aims at the victim);
- `allyHurt` (one of your summons or pets is hit; aims at the attacker);
- `summon` (an ally joins you: a summon, a pet, a charmed or hacked enemy, a hire; aims at the newcomer, noticed when your turn ends);
- `inflict` (you put an effect on an enemy; `"match"` is the effect id; aims at that enemy. Your summons' effects don't count);
- `useItem` (you use a consumable), `equip`, `unequip` (`"match"` is the item's name or id);
- `search` (you open a container nobody searched yet), `newFloor` (stairs take you to another floor; side areas and the Apartment don't count);
- `shieldBreak` (a hit takes your shield to nothing; aims at the attacker);
- `trap` (one of your mod traps goes off; `"match"` is its `fieldName`; aims at whoever set it off, or the first one it caught);
- `charge` (a skill rushes you across the floor: a dash, lunge, rush or charge, or a chain with `"leap": true`; aims at the skill's target);
- `leap` (you jump: a mod `leap`, Backflip, Flying Smash, Vault; aims at the target);
- `teleport` (you're set down somewhere else without walking: warps, blink items, teleport pads);
- `knockback` (you knock an enemy back and it really moves; aims at it);
- `swap` (`"match"`: `"places"` = you trade places with someone, `"weapon"` = you swap weapon sets, nothing = either; aims at who you swapped with);
- `chain` (one of your chains jumps to another enemy, a mod `chain` or Chain Lightning; aims at the enemy it reached).

`charge`, `leap` and `teleport` fire once per skill use: a charge across five tiles is one charge, and a charging chain is one charge however many links it reaches. They also fire `move` (with `"ability"`, or `"forced"` when someone else moves you).

**The ally in the event.** For `allyHurt`, `allyKill`, `summon` and `allyDeath`, `"to": "ally"` on an action and `"of": "ally"` in a condition mean that ally: `{ "on": "allyHurt", "actions": [ { "do": "heal", "to": "ally", "amount": 5 } ] }`.

**Proc limits.**
- `"cooldown"`: your turns before the proc can fire again.
- `"every"`: fires on every Nth time the event passes the proc's condition (3 = every third hit on a bleeding enemy).
- `"maxPerTurn"`: the most times it fires in one turn.

**Ability icons.** `"icon": "SKILLTREE_Blademaster/2"` borrows a vanilla tier's icon, so no png is needed.

## XML class files

Any mod file can be written as `.xml` instead of `.json`: a class, a library, or data such as items or synergies. It loads into the same structure, so every field works the same way.
- **Attributes or elements:** `<action do="damage" min="2" max="4"/>` is the same as `<action><do>damage</do>...</action>`.
- **Lists:** use a wrapper element with one child per entry, e.g. `<actions><action .../><action .../></actions>`. The child's name doesn't matter. Lists of words may also be written as a comma list: `trees="SKILLTREE_Pyromaniac,SKILLTREE_Chemistry"`.
- **Several classes in one file:** use a root element (any name) that holds several `<class>` elements.
- **Example:** `Mods/ExampleClasses/cadence_fencer.xml`.

## Character pictures

Put `.png` or `.jpg` files in the `Portraits` folder in the game folder (it appears on first use). Any size works: pictures are cut to a square from near the top. Click the picture frame on the creation screen, or above the character sheet, to pick one or to open the folder.

The **AI Character Portraits** mod (`Mods/CharacterPortraits`, off by default) adds **Paint Portrait** to the picker: it sends your classes, skills, gear (both weapon sets), current effects and any transformation to the AI asset toolkit, which paints a picture and puts it on your character. It only works with that toolkit on your computer and an NVIDIA GPU (CUDA). Its settings are under **Options > Mod Settings**, or in `BepInEx/config/arendeth.starcrawlers.classloader.cfg` under `[CharacterPortraits]`: `ToolkitFolder`, `Python`, `Appearance`, `IncludeEffects`, `PaintingFrameRate` and `LowTexturesWhilePainting`.

Your picture shows above the character sheet and as the save's thumbnail on the load screen, not on the HUD during play. Each Conjoined body has its own. The save keeps only the file name, so a picture you delete just disappears. `SCCL_DEVTEST=portraits,portraits2` checks it in game.

## Diagonal steps

`[Movement]` in the config file. You can step diagonally:
- **Keyboard:** hold forward or back together with a strafe key (W+Q, W+E, S+Q, S+E with the default keys). A fresh key press waits 0.06 s (`ComboWindow`) for its partner so the pair registers as a diagonal.
- **Numpad:** 7, 9, 1 and 3 (`NumpadDiagonals`).
- **Controller:** push the stick on a diagonal.

It's a normal step to the tile across the corner: it animates, takes a tile and sets off whatever is on the floor.

- **No corner cutting:** both tiles beside the diagonal must be open floor (no wall piece, no door) and not both occupied, and you can't step diagonally onto a door.
- **Cost:** a diagonal is √2 ≈ 1.41 tiles long, so it costs 1.41 turns. Each diagonal is one turn plus 0.41 into a stride debt, and whenever the debt reaches a whole turn, your next moment is spent catching up (the enemies get that turn). About every third diagonal costs an extra turn, with no dice involved.
- **Combat:** combat's "you've been spotted" halt stops diagonals like any other step.
- `DiagonalSteps = false` turns it off. `SCCL_DEVTEST=diagonal` checks the step, the corner rule and the cost in game.

## Shared tiles

`[Tiles]` in the config file. You and your allies (pets, Conjoined bodies) can stand on the same tile; enemies never
share.

- `ShareTiles = true`: walking into an ally's tile steps onto it. Pets and bodies can also share tiles with each
  other. Off = one character per tile, as in vanilla (then `SwapWithPets` applies).
- `MaxPerTile = 3`: most characters on one tile (2-4).
- `PetsMayJoinYourTile = false`: whether pets and bodies may walk onto *your* tile by themselves (you can always step
  onto theirs). Saved games put everyone back where they stood either way.
- `CrowdedDamagePenalty = 15`: everyone on a shared tile is **Crowded**: 15% less damage dealt per other character
  there (0 = no penalty).
- Who gets hit: single-target attacks on a shared tile hit its main occupant (you, if you're there; enemies target the
  others directly). **Area attacks hit everyone on the tile**: explosions, grenades, beams, slams, warps, channel
  blasts, damage fields such as fire and acid, trap bombs and the BioLabs lasher vines all pass their damage, and any
  effect they put on the tile's main occupant in the same moment, to the others.
- Characters sharing your tile stand behind your shoulders; elsewhere they spread out on the tile.
- Events that clear or lock tiles (room locks, boss anchors, loot drops, summoning circles) first move the extra
  characters to free tiles next to them.
- How it works, for modders: a tile still has one occupant as far as the game is concerned. The others are kept in a
  stack by the loader, and the game code that asks "who stands here" was audited (342 occupancy checks in 116
  files). The ~50 methods that affect characters on tiles are patched to include the stack; blocking, pathing and
  single-target checks keep using the one occupant. Also covered: mines and bomblets blow up on everyone on the tile,
  the Cerberus boss's random target can be anyone sharing a tile, and a Nanite Jar captures a cloud sharing a tile.

## Conjoined (several bodies, one mind)

`Mods/Conjoined/` is a class whose bodies are full characters: each has its own level and XP, classes and ranks,
skill and perk points, gear, hotbar (all pages) and effects. Inventory, credits, quests and story flags are shared.

- **Bodies join** at Conjoined tier 1 (2 bodies), tier 3 (3) and tier 5 (4). A panel asks for the new body's other
  classes: pick them by hand, copy yours, roll random, or use one of your saved builds. "Later" puts it off until
  the next floor or rank-up. A new body starts at your level with every point unspent and nothing equipped.
- **Companions:** the bodies you aren't playing walk and fight beside you (they look like the Nemesis of their main
  class: Soldier, Smuggler or Engineer). They follow you between floors, don't use pet slots, and earn the same XP
  you do; it's added when you next play them. A companion fights as itself, not as a floor-scaled enemy: its health,
  stats and resistances come from its own level, its classes' tier bonuses at its ranks, and the gear it has
  equipped, and its basic shot or strike uses its own weapon. Its gear shows on it as on hired crew: the weapon in
  hand, fitted pieces where its body takes them, a rarity tint otherwise (`SCCL_DEVTEST=bodies`).
- **Switching:** the party cards on the left of the screen (StarCrawlers style: class icon, name, level, class,
  health; the body you play is gold, fallen ones grey). Click a card to switch. Out of combat it's free; in combat
  it needs **Trade Bodies** (tier 2) ready and uses it. The **Party** tab or `[Conjoined] PartyPanelKey` (F7) hides
  and shows the cards. Switching is a short load: you stand where that body stood, and the one you left takes your
  place. Casting **Trade Bodies** itself also swaps the two bodies' positions: you take over the nearest body, it
  arrives on your tile, and the body you let go of appears where it stood.
- **Spread Thin:** each other body that's up costs every body 12% damage and 4 max health (tier 5 forgives one).
- **Falling:** if the body you play would die while another stands, you wake up in that one; the fallen body stays
  down until **Knit Together** (tier 4, 40-turn cooldown) brings it back at half health. With no body left
  standing, death is death.
- For class makers: actions `swapBody` and `reviveBody` (`amount` = % health), `"stacksOf": "bodies"` (other bodies
  that are up), and the ability field `"requires": "livingBody"` / `"downedBody"`.

## Saved builds

The creation screen's custom preset button keeps a **list** of builds instead of one. Right-click it and pick
classes to save a build as before (it's added, or replaces one with the same name). Left-click opens the list: click
a build to load it, `X` twice to delete it. Builds live in `SCCL_Builds.json` next to `Game.scc`; your old custom
preset is imported as the first entry. A build naming a class that isn't installed loads the rest instead of
getting stuck.

## Shared library (reuse)

Any json with a top-level `"library"` key is a library, not a class. `Mods/Library/core_library.json` ships with
common status effects and ability templates:

- **Effects** are global: reference them from any class as `"#Burning"`, `"#Gashed"`, `"#Exposed"`,
  `"#Marked"`, `"#Reeling"`, `"#Fortified"`, `"#Hasted"`, `"#Focused"`... (a class's own effects stay `"@Name"`).
- **Ability templates** (`Bolt`, `Shot`, `Strike`, `Smash`, `Nova`, `Blast`, `Blink`, `Buff`, `Passive`) carry
  targeting, costs, visuals and sounds. Use one with `"newAbility": { "base": "Strike", ...your fields }`; your
  fields win and arrays replace. Note `Blink`'s teleport lives in its `actions`, so include `{ "do": "teleport" }`
  if you give it your own actions. Templates can themselves have a `base`.

## Custom sounds

`"sound"`, proc `"sound"`, `"burstSound"` and threshold `"sound"` accept either a vanilla FMOD event
(`"event:/Abilities/..."`) or a `.wav/.ogg/.mp3` path relative to the class file (or the library folder), e.g.
`"sound": "sfx/juggernaut_earthshaker.wav"`. Files play through FMOD on the game's Sound bus, so the in-game volume
sliders apply. The bundled `sfx/` files were generated new with Stable Audio 3 from Noise Alchemy references, or rebuilt
from Noise Alchemy clips by plain processing; none is a copy of any pack (see `AUDIO_CREDITS.md`).
Sound effects created with NOISE ALCHEMY – NOISEALCHEMY.COM.

**Embedded audio, for any mod.** Sounds can ship sealed in the mod's `sounds.pak` instead of as loose files, and the loader
plays them from there with no change to any `"sound"` value. Run

    BreachworkModManager.exe --seal-audio Mods\YourMod [--remove-loose]

(or, with Python and lossless FLAC packing, `python _modding/audio/sfx_archive.py Mods/YourMod`). Every archive gets its own
random key, wrapped with the loader's public key (`_modding/audio/breachwork_public.xml`, in the repository), so only the
Breachwork Loader can open it and you never need anyone's secret; the loader carries the private key, which is not in the
repository, and a loader built without it skips archives and plays the fallbacks below. Every sound and the index (which is
itself encrypted, so the file names are not in the clear) carry an authentication tag: a changed or damaged archive is
refused and logged, never played as noise, and `python _modding/audio/sfx_archive.py verify Mods/YourMod/sounds.pak` checks
one the way the loader does. A loose file wins over the archive, so you can still test a new sound by dropping it in; remove
the loose files from the copy you share. Archives are read and decrypted in the background as the game starts and FMOD
decodes them into ready samples off the main thread, so playing a sound does no file work and no decoding; a mod with a very
large sound set can put `"soundsPreload": false` in its `mod.json` to have its archive read one sound at a time on demand
instead. Someone who decompiles the loader can find the private key: this keeps sounds from being copied out casually, not
from a determined person. `SCCL_DEVTEST=audiofast` measures what a play costs; `audiofallback` also checks the tamper refusals.

`[Audio] MixBlock` in Mod Settings sets how quickly every sound in the game reaches your speakers: `512` (the default here)
halves FMOD's delay, `256` is lower still but can crackle on a weaker PC, and `1024` or `game` is the game's own setting.
It applies after a restart.

A sound file that is missing or won't load plays a vanilla sound instead, so nothing goes silent. Mod audio isn't in the
source repository, so a build from a plain clone uses these throughout. `Mods/Library/sound_fallbacks.json` lists them; any
mod's json can add its own: `{ "soundFallbacks": { "default": "event:/...", "sounds": { "my_sound": "event:/..." } } }`
(file name without its extension). `SCCL_DEVTEST=audiofallback` checks them in game.

Files in an `audio_licensed/` folder are different: they are commercial sound effects (Function Loops, Tao & Sound,
Ovani Sound, and sounds built on those with Noise Alchemy clips) licensed to the person who goes by the alias Arendeth
and rebuilt for these mods with plain signal processing, no AI, so none is the pack's file (`_modding/audio/sfx_alter.py`;
an audio model picks the best try, `audio_listener.py` in the asset toolkit, so nobody has to judge sound quality by ear or in game).
They are **not** under the open terms in `LICENSE.md` and may only travel inside the mods (see `LICENSE.md` and
`AUDIO_CREDITS.md`). If you adapt a mod, point its `"sound"` fields at your own files. A missing file plays the game's
own sound instead, as above. The Library's thrown, leaping, recalled,
ricocheting and tethering moves play the Library's `audio_licensed/moves_*.wav` files.

## Custom particle effects

Every name a particle goes by (`castVfx`, `impactVfx`, `travelVfx`, `areaVfx`, `pulseVfx`, `burstVfx`, a proc's
`vfx`...) can also be an effect of your own. Put them in a data file (any `.json` in your mod with a `"particles"` list);
ids are global, so prefix them with something of your class's. `"MyBurst@0.6"` sizes them like vanilla particles.

```jsonc
{ "particles": [
  { "id": "CbSlagBellow",
    "light": { "color": "#FF7A3D", "range": 6, "intensity": 3, "time": 0.7 },     // optional, fades out over time
    "layers": [
      { "texture": "spark",          // built-in: soft, spark, ring, star, smoke, shard, mote; or "fx/ember.png"
        "grid": [4, 4], "randomFrame": false,   // a PNG flipbook: plays over each particle's life, or one random frame
        "blend": "add",              // add (glow; black is see-through) or alpha (smoke, solid shapes)
        "count": 50, "rate": 0,      // burst size, plus particles per second for "duration" seconds
        "duration": 0.4, "delay": 0,
        "life": [0.5, 0.9], "speed": [5, 9], "size": [0.15, 0.3],   // [min, max], metres and seconds
        "sizeCurve": "shrink",       // shrink, grow, pop, pulse, flat
        "color": "#FFE08A", "color2": "#FF4A10", "alpha": 1,       // colour over life; always fades in and out
        "shape": "hemisphere",       // sphere, hemisphere (up), cone (up, "angle"), ring, disc, point, line
        "radius": 0.4, "gravity": 0.8, "drag": 1.5, "spin": [-90, 90],
        "orbit": 0, "radial": 0,     // swirl around the centre / push out (negative pulls in)
        "stretch": 3,                // streak along the direction of travel
        "flat": false,               // lie flat (rings and circles on the floor)
        "ground": false,             // emit from the floor under the spot (cast effects play at chest height)
        "offset": [0, 0, 0] } ] } ] }
```

**Borrowing from enemies, and aliases.** Besides vanilla player abilities (`"PA_X.field"`) and effects, a particle name
can point into an enemy: `"Enemy_BOSS_Cerberus/Cerberus_Warp.impactParticle"` is that enemy's ability (by internal
name, or class name) and its particle field, read from the game's own prefab when first played (stopped or parked
systems are started). Nothing is copied into the mod. A particle entry can also be just an alias:
`{ "id": "IchSplash", "use": "Enemy_BOSS_Cerberus/Cerberus_Warp.impactParticle@0.7" }` plays that instead under the id
(a hazard id works too, for field looks), and since later mods replace earlier ids, a small mod can repoint a class's
own effects without touching the class. On an alias, `"ground": true` puts the effect on the floor and `"without": ["Name"]`
leaves named parts out. A layer can take `"borrow": "Enemy_BOSS_Cerberus/Cerberus_Warp.impactParticle:BloodGlobs"` instead of a
`texture`: that system's material and flipbook on your own emitter, so lit goo stays lit. `Mods/BossLooks/` does this for the Ichorborn with Doctor Cerberus's particles
and hazards; `SCCL_DEVTEST=ichorgoo` photographs her goo beside the class's and logs what it's made of;
`SCCL_DEVTEST=ichorvs:Hardolo` clones that save with its lowest class swapped for the Ichorborn, takes
it to the final fight and screenshots each of the doctor's attacks next to the skill made from it
(`ichorvs_<pair>_boss_N.png` / `_ichor_N.png`), and logs the doctor's particle fields and hazards.

**Third person for effects on you.** `"thirdPerson": 1.5` on an ability (or on an effect, when it lands on you or
bursts on you) pulls the camera out over your shoulder for that many seconds, the way dashes do, so a crown of
lightning over your head or wings tearing off your back can be seen. `[Camera] SkillThirdPerson` turns it off.

A PNG without transparency (a glow painted on black) gets its brightness as alpha. In a `travelVfx`, burst-only
layers ride along as the projectile and layers with a `rate` leave a trail. Each hop of a `chain` action plays the
chain's own `vfx`, or else the skill's `impactVfx`. `SCCL_DEVTEST=vfx` (with `SCCL_VFX_ONLY=IdA,IdB`) photographs every
particle the mods name, travel effects in flight.

The same effects can also **live on a creature** (a wisp's halo, orbiting motes, a trail) through a model's
`"particles"` (see Custom models below). There every layer loops: a stream (`rate`) runs on, a burst repeats every
`duration`, and the effect's `light` stays lit instead of fading. Give attached layers a `rate` and a `life` a little
longer than `duration` so they never gap. `_modding/make_wisp_assets.py` makes whole wisps: one recipe gives the mesh
core (procedural, or AAMT SD3.5 + TRELLIS/SF3D) and its particle flipbooks (AAMT particle stage), and writes the
effects into the mod's `wisp_fx.json` and the `"model"` block to paste in.

The **Skill Effects** mod (`Mods/SkillVfx`) gives every class skill effects of its own this way, as patches over the
class files (tiers merge by position), so switching it off brings back the borrowed vanilla particles. It is
generated: change a skill's recipe in `_modding/make_skill_vfx.py` and run it again rather than editing its files.

## Custom models and textures

Summons (`"do": "summon"`) and mod enemies take a `"model"`: a new mesh, or just a new texture for the body they have.

```jsonc
{ "do": "summon", "creature": "nanite_cloud", "summonName": "Hoarlight",
  "model": { "mesh": "models/hoarlight.glb", "scale": 0.9, "offset": [0, 1.0, 0], "rotation": [0, 0, 0],
             "glow": "#7fdcff", "glowStrength": 1.4, "tint": "#ffffff", "bob": 0.12, "spin": 35,
             "keepParticles": false,
             "particles": [ { "id": "RimeHoarlightMotes" } ], "deathVfx": "RimeHoarlightBurst" } }
// an enemy: "enemies": [ { "id": "Frostling", "base": "CORPSEC_GUARD_MELEE", "model": { "texture": "frostling.png" } } ]
```

- `mesh`: `.glb` (glTF 2.0 binary) or `.obj`, relative to the class/data file. glTF: positions, normals, UVs and the
  embedded base-colour PNG are used; static node transforms are baked in. **Skinned meshes keep their bones and their
  animations become clips**, played for what the creature is doing: `idle` (and any other `idle…`, picked now and
  then), `move` while it walks, `attack` / `attack2` / … (one picked per attack), `hit` when it takes damage, `death`
  (it stays down; a floating body sinks to the floor), `special` while an enemy skill winds up. Clips are matched by
  name; `"clips": { "attack": "Bite" }` maps others, `"animSpeed"` scales them. Export uncompressed (no Draco/meshopt).
  `.obj` takes its colour from `texture`. Blender exports from the AAMT toolkit work as they are (1 unit ≈ 1 m, Y up).
- `texture` (PNG): with a mesh, replaces its texture; without a mesh, repaints the creature's own body.
- `scale`, `offset` (metres from the feet), `rotation` (degrees), `tint`; `glow` (#RRGGBB, with `glowStrength`) makes
  the model shine in that colour (an additive pass over it that the game's bloom picks up, plus a small light; with
  only a `texture`, the creature's own body glows);
  `bob` / `spin` make it float and turn; `keepParticles` (default true) keeps the creature's own particle effects.
  `faceViewer: true` keeps the model turned toward the camera whichever way its body faces (a floating grin, a ghost).
- `particles`: effects that play on the creature for as long as it lives, each a mod particle id or a vanilla
  particle name (`"@0.5"` sizes it):
  ```jsonc
  "particles": [ { "id": "RimeHoarlightHalo" },                               // sits where the model does
                 { "id": "RimeHoarlightMotes", "offset": [0, 1.3, 0] },       // a little higher
                 { "id": "RimeZenithTrail", "follow": false } ],               // left behind: a trail
  "deathVfx": "RimeHoarlightBurst"
  ```
  `offset` counts like the model's own `offset` (so the same numbers land on the model, however the body under it is
  scaled) and defaults to it; sizes stay in metres. `follow` (default true) keeps the particles with the body; `false` leaves them where they were made, so a moving
  creature draws a trail. `bob` (default true) carries the emitter along with the model's bob. When the creature
  dies, or a summon is spent or recalled, the emitters stop, what's in the air fades where it is, and `deathVfx`
  plays once where the model was. `particles` work without a mesh too (a glow on a vanilla body). Saves and loads
  keep them like the rest of the look. `SCCL_DEVTEST=wisps` photographs Rimelight Seer's wisps live, dying and loaded.
- The creature keeps its movement, AI, attacks and collision; only its look changes. Summons keep their look
  through saves; mod enemies have it built in.

## Rules the loader enforces

- A custom class can't be picked together with a vanilla tree it borrows from, or with any tree that
  grants the same ability. The game finds abilities by name, so two copies would clash. The creation
  screen plays an error sound and explains why.
- Borrowed abilities that scale with "their" tree's rank (Big Guns, Gadgeteer, Cyberninja, ...) scale with
  your custom class's rank instead.
- Each class has at most 9 tiers (vanilla trees use 7). The skills screens get extra slots, and rows longer
  than 8 are spaced tighter so they fit. `TierSlots` in the BepInEx config sets the cap (7–9).
- Maxing a custom tree gives no Steam achievement, and custom trees have no lore dataslate.
- Don't remove a class mod while a save still uses it. The loader leaves that skill slot empty and logs
  a warning, but this case is untested.

Errors (unknown tree id, bad tier index, missing icon, ...) are written to `BepInEx/LogOutput.log`.

## Mods: manifest, items and tweaks

Besides classes, a mod folder can add **items** and change **tweaks**. `Mods/ExampleItems/` is a working example
with one item of each kind.

### mod.json

```jsonc
{ "id": "MyMod", "name": "My Mod", "author": "you", "version": "1.0.0", "description": "...",
  "requires": ["OtherMod"],     // not loaded (error in the log) if these are missing
  "loadAfter": ["ThirdMod"],    // loaded after these when they are present, so you can patch their items
  "apiLevel": 1,                // only with a code plugin: the plugin API level it needs (see Code plugins)
  "enabled": true }
```

A folder with `mod.json` is one mod, including its subfolders. Folders without one still work: each top-level
folder under `Mods/` is then its own mod, named after the folder. Any `.json` with an `items`, `tweaks` or
`effects` list is mod data; everything else is read as a class or library as before.

**Load order**: a mod always loads after what it `requires` or `loadAfter`s; beyond that, mods load in the order chosen
in the mod manager (below), or by folder name when none was chosen. A later mod's entries win over an earlier mod's.

A mod is left out (with the reason in the log and on the Mods page) when it is off, its `mod.json` can't be read, its
id is already taken, or something in `requires` is missing or itself left out, so turning off `Library` also leaves
out every class mod that needs it. A class file in a mod that patches another mod's class (same `id`, that mod in its
`requires` or `loadAfter`) is skipped while that mod is not loaded, instead of becoming a half-made class.

### Classes in mods

Every bundled class folder is a mod: `Library` (the shared library), `CustomClasses`, `UniqueClasses`,
`ArendethClasses` and `ExampleClasses`; the class mods require `Library`. Set `"enabled": false` in a `mod.json` to
turn a whole set off.

- Libraries and classes load in mod order, and only for enabled mods whose `requires` are installed. A later
  library's effect or template with the same name replaces the earlier one.
- **Patching a class from another mod**: give your mod `"loadAfter"` (or `"requires"`) the class's mod and write a
  class file with the same `id` that only has the fields you change. Tiers match by position (`{}` leaves a tier
  alone), effects match by `id` (an unknown id is added), and paths in the patch are relative to your folder:

  ```jsonc
  { "id": "MOD_Scrapper",
    "starterItems": ["ExampleItems:FieldPatch"],
    "effects": [ { "id": "Grit", "maxStacks": 12 } ],
    "tiers": [ {}, { "newAbility": { "name": "Lead Pipe", "icon": "pipe.png" } } ] }
  ```

  `"enabled": false` in a patch removes the class. Patches never change the class id, so saves keep working.
- **Starter items** can be mod items: `"MyMod:ItemId"`, or just `"ItemId"` for an item in the class's own mod.

### Items

```jsonc
{ "effects": [ { "id": "Grounded", "name": "Grounded", "duration": -1, "resists": { "Shock": 10 } } ],
  "items": [
    { "id": "LinemansCowl",                 // short id; other mods use "MyMod:LinemansCowl"
      "base": "GEAR_HEAD_Heavy_Helmet",     // vanilla id from vanilla_reference.txt, or another mod item
      "name": "Lineman's Cowl", "description": "...", "icon": "icons/cowl.png",   // icon optional (png)
      "rarity": "Rare", "level": 2, "value": 60,
      "fixed": true,                        // default: exactly as written. false = vanilla-style random rolls
      "stats": { "Toughness": 1 }, "resists": { "Shock": 15 },
      "equipEffect": "@Grounded",           // "@Id" this mod's effect, "#Id" library effect, or a vanilla id
      "loot": [ { "type": "Gear", "floors": [0, 3], "weight": 2 } ] }
  ] }
```

- **Everything not written is copied from `base`**, including the 3D model and any stats it had. New models are not
  supported; the inventory icon can be a png.
- `base` can be another mod item, so variants are short: `{ "id": "CowlMk2", "base": "LinemansCowl", "stats": {...} }`.
- A later mod (by load order) using the same qualified id patches only the fields it sets.
- Fields by kind (the kind comes from the base item):
  - **gear**: `stats`, `resists`, `bonusDamage` (extra attack damage), `equipEffect`.
  - **weapon** (base must be the same weapon family): `damage` `[{type,min,max}]`, `ammoUse`, `accuracy`
    `{ "close": 10, "medium": 0, "long": -10 }`, plus the gear fields.
  - **ammo**: `damage` (per shot; at least one entry above 0), `onHit` `{ "effect": "#Shocked", "chance": 20 }`
    (a `[X]` in the description shows the chance), `glow` `"#RRGGBB"`, `stack`, `stackMax`.
  - **consumable**: `use` `{ "effect": "@Steadied", "duration": 6, "amount": 0, "consume": true }`, `category`
    (tooltip type line), `stackMax`, and for medkits `heal` (0.2 = 20% of max health).
  - all: `name`, `description`, `icon`, `rarity`, `level`, `value`, `stack`, `tags` `{ "NOSELL": 1 }`, `enabled`.
- **Equip effects** need `"duration": -1` so they last while the item is worn.
- **Loot**: `type` is Weapon, Gear, Consumable, Ammo, Food or Luxury (defaults from the kind); `floors` is
  `[min, max]` (0 = first floor); `weight` is how many lots the item gets (each vanilla item has 1). Vendors stock
  from the same tables, so loot items also show up in shops.
- **Class items**: `"forClasses": ["MOD_FluxSavant"]` (class ids or names) makes an item drop only when a body in
  your party (you, or another Conjoined body) has one of those classes; a roll that lands on it otherwise is rolled
  again. Runes do this on their own: they're for the classes of their own mod whose sockets take them (any socket
  class, if the mod has none). `[Loot] ClassItemsForParty` turns it off.
- In-game ids look like `GEAR_HEAD_SCCL_MyMod_LinemansCowl`. Saves store that id, so keep `id` stable. If a mod is
  removed, its items load as the vanilla "broken item" instead of breaking the save.
- `Mods/vanilla_reference.txt` lists every base id by slot, with names, damage and on-use effects.

### Tweaks

```jsonc
{ "tweaks": { "minionCap": 4, "stashPages": 8 } }
```

| Tweak | Meaning |
|---|---|
| `minionCap` | Minions you can keep at once (vanilla 3) |
| `rarityChanceRare` / `rarityChanceEpic` / `rarityChanceLegendary` | Base drop chances (vanilla 10 / 5 / 1) |
| `vendorMarkup` / `vendorMarkdown` | Vendor sell multiplier / buy divisor (vanilla 2 / 2) |
| `stashPages` | Stash pages (overrides the config) |
| `breachChance` / `breachCacheItems` | Side-area breach chance and bonus items (override the config) |

When several mods set the same tweak, the one loaded last wins (the log says who). Unknown names are reported.

### Recipes (crafting)

Recipes are made in the loader's own **crafting window**: the recipes you know on the left (green dot = you can make
it now, red = something's missing, grey = not made here), the picked recipe's output and ingredients on the right,
with how many of each you carry. Hover the big icon for the item's full tooltip; what you see is the exact item you
get. **Craft** makes one, **Craft ×5** makes up to five and stops when an ingredient runs out. Ingredients come from
your inventory only, never from equipped gear or the stash.

- **Apartment:** press the crafting key (F10, `[Crafting] PanelKey`) or click the **Craft** tab beside the health
  panel anywhere in the Apartment, or step on the amber Fabricator pad next to where you arrive.
- **In the field:** an ability with the `craft` action (below) opens it anywhere, offering "field" recipes. The key
  and tab still open the window as a recipe book, which shows everything you know but can't make it.

```jsonc
{ "recipes": [
  { "id": "LinemansCowl", "output": "LinemansCowl", "amount": 1,       // output: this mod's item, "Mod:Id", or vanilla id
    "inputs": [ { "item": "GEAR_HEAD_Heavy_Helmet", "count": 1 },      // by item id...
                { "tag": "MECHPARTS", "count": 2 } ],                  // ...or anything carrying a tag
    "credits": 25,                                                     // optional credit cost
    "tools": ["LOCKPICK"],                                             // must be carried, not used up
    "requires": { "class": "SKILLTREE_Engineer", "rank": 0 },          // hidden until you have that tree at that rank
    "stations": ["apartment", "field"] }                               // default: both
] }
```

A recipe with the same id in a later mod replaces the earlier one. `Mods/ExampleItems/recipes.json` has working examples.

### Creatures

A creature is a whole new being written in data: its look, size, stats, how it fights and its own skills. Nothing
vanilla shows through, and you never name a vanilla creature to build it on. Use one as a summon
(`"creature": "PaintedGuard"`), or give it `spawn` rules like an enemy. `Mods/LookingGlass/creatures.json` (the
Painted Guard) and `Mods/KelvinMagus/creatures.json` (the Glacier Sentinel) are working examples.

```jsonc
{ "creatures": [
  { "id": "PaintedGuard", "name": "Painted Guard", "subtitle": "Card Soldier", "description": "...",
    "type": "Supernatural",            // Mechanical | Humanoid | Monster | Supernatural
    "role": "brawler",                 // how it fights: brawler | skirmisher | sniper | hover
    "health": 1.0, "healthPerLevel": 15, "smartsPerBonus": 6,   // as a summon: growth per level, Smarts per pet bonus point
    "stats": { "Dodge": 10 }, "resists": { "Cryo": 50 },
    "model": { "mesh": "models/painted_guard.glb", "height": 1.2 },   // height in metres, whatever is under it
    "skills": [                        // its attacks, written like enemy skills (see "Enemy skills" below)
      { "id": "SpearThrust", "name": "Spear Thrust", "verb": "thrusts its spear at", "range": 1.5, "cooldown": 0,
        "melee": true, "dodgeable": true, "clip": "attack",     // the model's own clip for this skill
        "actions": [ { "do": "damage", "type": "Piercing", "min": 2, "max": 4 } ] } ] } ] }
```

- The loader builds each creature on a body of its own for the role, then strips it: no borrowed abilities,
  drops, particles or looks. A creature fights only with its `skills`, so give it at least one with `cooldown: 0`.
- `"scaleStat": "PsiPower"` makes a summon grow with your Psi Power instead of Smarts: its pet bonus comes from that
  stat (`smartsPerBonus` points per bonus point), and it is lent your full Psi Power, so its skills with
  `"power": "PsiPower"` hit harder as yours rises. A `summon` action can set `"scaleStat"` too, for summons on a
  vanilla body (the Gloomspore Warden's Gloomcaps). The Rimelight Seer's and the Warden's summons use it.
- `height` (any model, not only creatures) sets the size in metres; `scale` still works where you want it.
- A skill's `clip` names the clip its model plays (`attack2`, `special`, ...); without one it plays an attack clip.
- `"behaviors"` are movement habits that run after its own turn, at most one a turn (works on mod enemies too):
  ```jsonc
  "behaviors": [ { "kind": "kite", "keep": 3 },        // a foe within 3 tiles: step to the free tile with the most room
                 { "kind": "retreat", "below": 35 },   // under 35% health: keep backing off
                 { "kind": "guard", "leash": 6 },      // a summon farther than 6 from you walks back toward you
                 { "kind": "hitAndRun" } ]             // after using a skill away from where it started, dart straight back
  ```
  Each takes `"cooldown"` (its turns before it can kick in again). The Spitcap kites and retreats, the Zenith Hoarlight
  kites and guards, and Rimelight Seer's Hoarlights hit and run: their Wisp Dart skill leaps in (`{ "do": "leap" }`),
  frosts and silences, and the habit springs them back to where they started.
- Saves store creatures like mod enemies (`SCCLMOB_<Mod>_<Id>`); summons made from them keep their look and skills.
- `"rotation": [0, 180, 0]` turns a model that faces backwards on its body.

### Enemies

Enemies are clones of a vanilla enemy (its model, animations and AI come along), with your changes on top.
`Mods/ExampleEnemies/` has two working examples. Base ids, their abilities and damage are listed under
`=== ENEMIES` in `Mods/vanilla_reference.txt`.

```jsonc
{ "enemies": [
  { "id": "StaticHound", "base": "MUTT_BASIC",            // vanilla enemy, or another mod enemy
    "name": "Static Hound", "subtitle": "Arc-Collared", "description": "...",
    "type": "Monster",                                     // Mechanical | Humanoid | Monster | Supernatural
    "health": 1.2, "damage": 1.0, "xp": 1.3,               // multipliers on the base's per-floor stats
    "stats": { "Dodge": 10 },                              // added to the per-floor stats
    "resists": { "Shock": 50, "Fire": -20 },               // replaces the base's resistances
    "ai": "Sniper", "scale": 1.3,                          // Brawler | Skirmisher | Sniper; model size
    "tags": { "FAST": 1 }, "variant": 0,                   // variant: lock enemies with several looks to one
    "abilities": [
      { "from": "Slime_Electric/Slime_Electric_Smash",     // copy another enemy's ability ("ENEMY/ABILITY")
        "damage": [ { "type": "Shock", "min": 2, "max": 4 } ], "cooldown": 0, "priority": 1 },
      { "from": "MUTT_MELEE_BITE", "cooldown": 2 } ],      // or retune one it already has
    "removeAbilities": ["Mutt_Charge"],
    "loot": { "items": ["ExampleItems:ArcbiteRounds"],     // always dropped
              "random": [ { "type": "Gear", "rarity": "Rare" } ], "replace": false },
    "spawn": [ { "floors": [0, 2], "chance": 8 },          // 8% of ordinary hostile spawns on floors 0-2
               { "floors": [3, 6], "replaces": "MUTT_BASIC", "chance": 25 } ] }   // or 25% of that enemy's spawns
] }
```

- Spawns only touch ordinary hostile spawn points: never bosses, rares, event rooms, boss rooms or friendly NPCs.
  Mod enemies are placed exactly where the enemy they stand in for would have been.
- AI picks abilities by `priority` (highest usable wins; all 0 = random). Copied abilities keep the donor's
  behaviour; some carry visual effects tied to the donor's model.
- Saves store enemies by id (`SCCLMOB_MyMod_StaticHound`). If the mod is removed, those enemies are left out of
  the level rather than breaking the load.
- Not yet: mod enemies as rares or kill squads (side areas have their own elites, see below).

#### Enemy skills

`"skills"` gives an enemy brand-new abilities built from the same actions classes use (damage, effects, heal,
shield, stun, knockback, leap, pull, hazards, fields, ...), with the enemy as the caster:

```jsonc
"skills": [
  { "id": "CriticalOverload", "name": "Critical Overload",
    "target": "area",            // enemy (who it fights) | self | ally (most hurt, in range) | allies (within radius) | area
    "aroundSelf": true, "range": 3.2, "radius": 2.0,
    "cooldown": 0, "priority": 9, "uses": 1,           // the AI picks the highest-priority skill it can use
    "healthBelow": 0.8, "alliesNear": 0, "chance": 1,  // when it may use it ("when" takes a condition tree too)
    "windup": 3, "grow": true, "windupText": "starts to overload", "windupEffect": "Overloading",
                                 // a visible countdown first; a stun breaks it
    "melee": false, "dodgeable": false, "accuracy": 90, "power": "RangedPower", "floorScale": 0.15,
    "verb": "detonates, catching", "castVfx": "PA_Engineer_Spidermine.PS_explode_fire@0.6", "impactVfx": "...",
    "actions": [ { "do": "damage", "type": "Fire", "min": 9, "max": 15 }, { "do": "knockback", "amount": 1 } ],
    "selfActions": [], "spawn": { "ids": ["Interlopers:Burstcap"], "count": 2, "max": 4 },   // or "copies": true
    "die": true },
  { "id": "PinDown", "fromClass": "MOD_Longshot/Pin Down", "range": 7, "cooldown": 6 }       // borrow a class ability
]
```

- Damage scales with the enemy's power stat and the floor. Effects named in `"mobEffects"` (a top-level list of
  effects, as in classes) are the skill's own; `"Class@Effect"` reaches any class's.
- Spawned helpers fight on the caster's side and can't spawn helpers themselves.

`"enemyPatches"` teaches existing enemies new skills, for a share of their spawns:

```jsonc
"enemyPatches": [ { "enemies": ["CORPSEC_TROOPER"], "chance": 0.25, "prefix": "Medic",
                    "skills": [ { "id": "FieldDressing", "target": "ally", "range": 4, "cooldown": 3, "priority": 8,
                                  "actions": [ { "do": "heal", "amount": 6, "perRank": 3 } ] } ] } ]
```

Patches only roll for enemies **inside side areas** (`"onlyInSideAreas": false` lets one out), so Chimera's own
floors stay as they are. Patched enemies keep their skills through saves.

### Item actions for abilities

New ability actions (in class files, `newAbility.actions`):

- `{ "do": "craft", "recipes": ["LinemansCowl"] }` opens the crafting window with field recipes (optionally only these).
- `{ "do": "giveItem", "item": "MyMod:ScrapHelm", "amount": 1 }` puts an item in your inventory.
- `{ "do": "consumeItem", "item": "...", "amount": 2 }` or `"tag": "MECHPARTS"` uses items up if you have them.
- `{ "do": "salvage", "gives": [ { "item": "...", "count": 2, "chance": 50 } ] }` destroys the targeted item and hands
  out the parts. Use it with `"target": "item"` (see below).

New ability fields:

- `"target": "item"` makes the ability target an inventory item (it opens your inventory to pick one), filtered by
  `"itemTag": "GADGET"` and/or `"itemId": "..."`. Equipped, stash and shop items are never valid.
- `"itemCost": [ { "tag": "MECHPARTS", "count": 3 } ]` is used up on every cast; the ability can't be used without it.

## Death recap and the Graveyard

- The death screen now says exactly what killed you: the enemy, its skill, the damage and damage types of the
  killing blow, how much health you had, and the three hits before it.
- Every finished run (death or escape) is added to a local **Graveyard**, Nethack style:
  `1,100  Name the Corsair/Psi Knight/…, killed on floor 1 by CorpSec Guard's Shoot (lvl 1, 412 turns, Normal)`.
  Press **F8** anywhere (title screen too) to see the best runs and your latest one.
- Score = 1000 × deepest floor + 100 × level + 250 × side areas visited + 1500 × deepest depth of the Ichor Deep;
  ×1.25 in Ironmode, ×2 for escaping. A winner who stays and dies in the Ichor Deep later gets one entry for the run,
  not two: the later ending replaces the win.
- Stored in `SCCL_Graveyard.json` next to `Game.scc` (only on your computer). Key and list length: `[Graveyard]`.

## Trap, hack and lockpick odds

- Trap screens add a line explaining the chance, e.g.
  `77% = trap 65 + 1 luck + Agility 6÷2 (3) + Recognition 11÷2 (5) + Smarts 14÷4 (3)`.
  Which stats count, and how much, depends on the trap; floor depth and difficulty change the base.
- The lockpick button now shows its chance (`Lockpick (6) – 66%`) and the screen explains it:
  40 + the lock's modifier + Savvy + Recognition (both divided by the difficulty above Normal) + Lockpopper.
- Hacking rolls instantly, so its odds go to the combat log.
- **Corp Intel (optional):** set `[SecurityOdds] CorpIntelCost` above 0 and the breakdowns become something you buy
  once per run for that many credits, by pressing **I** on a trap or lock screen.
- **Use the floor's traps.** Pressure plates and tripwires normally ignore enemies. An enemy you knock back, pull,
  throw, warp or swap onto a live one now sets it off ("CorpSec Guard is driven onto the Pressure Plate!"), and its
  kill counts as yours. Cameras still only watch for you. `[Traps] ShoveIntoTraps` turns it off.

## Saving

The game has a few saves that ignore the Options autosave toggle. With autosave **off**, these no longer happen
(Ironmode still saves, so it can't be save-scummed):

- the silent save when a floor's lore dataslate is placed (on arriving at most floors),
- the save on leaving the tutorial,
- the save when using the Apartment portal.

Save & Quit, the quicksave button and the save at the start of a new game are unchanged. New option: with
autosave on, `[Saving] AutosaveOnFloorChange = true` also saves every time you take the stairs.

## Hotbar pages

The hotbar has pages (4 by default) so you can keep more skills and items within reach. "Page 1/4" sits on the
hotbar's top-left corner.

- Flip pages with `]` / `[` or Page Down / Page Up, the mouse wheel over the hotbar, or by clicking the page label
  (left = next, right = previous). Pages don't flip while you are aiming an ability. While the stash is open, Page
  Up/Down flip stash pages instead.
- Each page keeps its own layout; set it up the normal way (drag skills and items onto the slots).
- Your current page is remembered in the save.
- Page 1 is what the game itself saves, so a save still loads without the mod (showing page 1).
- Settings are under `[Hotbar]` in the config (pages, keys, mouse wheel), and mods can set the `hotbarPages` tweak.

## Stash pages

The stash gets numbered tabs above its grid (6 pages of 120 slots by default). Click a tab to open that page, press
PageUp/PageDown while the stash is open, or drop an item from the stash or your inventory onto a tab to send it there.
The stash reopens on the page you last looked at.

Page 1 is the vanilla stash and stays in `Game.scc`, so removing the mod only hides pages 2+. Those are kept in
`SCCL_StashPages.dat` next to `Game.scc` (in `%USERPROFILE%\AppData\LocalLow\Juggernaut Games\StarCrawlers Chimera`).
Page count, tab bar offset and the page keys are in `BepInEx/config/arendeth.starcrawlers.classloader.cfg` under `[Stash]`.

The last tab, **Own**, is a page of your own: it is saved with your character's save, not shared with your other
characters, so it also takes the unstashable items the shared pages refuse (gear reworked by the Modder's skills,
floor-bound items). Quest items still stay with you. Items go in from your bag and come back out to it; dragging
between Own and a shared page is refused, so reloading a save can never copy an item across.

## Side areas (breaches)

Each newly generated floor has a chance (60% by default) of a purple breach pad somewhere off the main rooms. Stepping
on it takes you out of Chimera into a **generated place**: a layout built fresh for that breach, with its own floors,
walls, doors, props, lighting and fog. Enemies, loot and traps stay at your current floor's difficulty, plus a bonus
cache of rare-or-better items somewhere inside. You arrive at the place's way out (an airlock, a landing bay, a
decontamination hatch); the room you arrive in is always clear.

| Place | Layout | Feel |
|---|---|---|
| Derelict Hauler | a long gangway with compartments and holds off both sides, the bridge at one end, the engine room at the other | dark, failing cold lights, red emergency lamps |
| Mercenary Outpost | a big central hall with corridors out to barracks wings, an armory and a command room | warm floodlights, more locked doors |
| Research Annex | a straight corridor with identical labs at a steady rhythm, holding cells off them, a containment chamber | bright, clinical, green specimen glow |
| Ore Rock | rough caverns joined by winding tunnels, mostly open gaps instead of doors, a drill site deep inside | low warm work lights, glowing ore |

- The way you came in is the way out, and it asks first (`[SideAreas] ConfirmLeave`), since leaving turns in any job.
  An echo of a real floor keeps none of that floor's stairs, since none of them would go up or down: a green pad where
  you arrive is the way out. Only a tower's stairs between its layers stay.
- The breach stays open. Step on it again and you're back in the same side area, on the layer you left, as you left
  it: the dead stay dead, opened caches stay open, the vault and objective are where you got them to, and a Collapsing
  Echo's countdown carries on. There's no job board the second time. Only a collapse (the timer running out with you
  inside) closes a breach for good.
- Crates, lockers and cabinets in a generated place are the game's own containers: click one from the tile in front
  to search it. A handful per place hold one or two things (some locked: pick or bash them; deeper down, maybe a
  mimic); the rest stand open and empty, already picked clean. Searched stays searched, through saves and return trips.
- `[SideAreas] Brightness` (percent, default 100) turns a generated place's ambient and ceiling lights up or down. A
  blackout still goes dark.
- Kill squads wait until you're back on the real floor, and floor intros don't replay.
- You can save and quit inside: the save keeps the place's seed, so the same layout comes back with everything where
  you left it. The Apartment portal works there too.
- Visits are counted per run, ready for a future scoreboard.

**Anomalies.** Every breach rolls 1–3 anomalies (sometimes plus a Reliquary). With factions on, the job board shows them before you go in; otherwise the log tells you on arrival.

| Anomaly | What it does |
|---|---|
| Blackout | most lights go out; you're harder to spot and hit (+20 stealth, +6 dodge), enemies aim worse (-10 accuracy) |
| Gas Leak | toxic gas fills 3–4 rooms for the whole visit |
| Lockdown | half the doors are locked, harder to pick or bash than usual (never the ones around the stair you came in by) |
| Infestation | 5–11 extra creatures roam the echo: mutts, worms and slimes, nastier on deeper floors |
| Collapsing Echo | 50 turns, with warnings at 20, 10 and 5; then it throws you out with a quarter of your health gone (an unfinished job fails) |
| Static Storm | about every fourth turn, shock arcs onto the tiles around you (sometimes yours) |
| Reinforced Garrison | 40% of the guards are veterans: +20% damage, take 15% less |
| Rich Seam | two extra caches and +25 loot find while you're inside (the one kind anomaly) |
| Reliquary | rare: the vault is two grades higher, warden included |

**The vault.** Every echo has one: the room furthest from where you arrive is sealed, with its loot inside. Its **warden** stands outside: a much tougher copy of the toughest enemy on the map, with grade - 1 veteran guards, and it carries the override. Kill the warden and the seals release.

- **Grade** (1–5) = 1 + each hard anomaly + every danger step above Challenging + 2 for a Reliquary. The board shows the grade for every job and for going in freelance.

  | Grade | Contents |
  |---|---|
  | 1–2 | rares (sometimes an epic at grade 2) |
  | 3 | an epic and rares |
  | 4 | two epics, 35% chance of a **legendary** |
  | 5 | a **legendary**, two epics and a rare |

  Every vault also holds credits.
- **Getting a legendary takes a lot of challenge**, for example:
  - an Extreme job and one hard anomaly;
  - a Very Hard job with two hard anomalies;
  - a Reliquary with anything nasty.
- **The warden:** it has (2 + grade) times the health of what it copies, and +10% damage and -6% damage taken per grade. A grade 5 warden is called the Vault Warden Prime.
- The tracker in the top-left corner shows the echo's anomalies, the collapse countdown and whether the vault is open. `SCCL_DEVTEST=echoes` checks every anomaly and a grade 5 vault in game.

**Kinds of side area.** Each breach rolls a kind, shown by the pad's colour, on the job board and in the log:

| Kind | Place | What's inside |
|---|---|---|
| Derelict | Derelict Hauler | its crew gone; whatever moved in (always infested and dark), and big caches |
| Server Farm | Research Annex | an alarm grid; plant 3 uplinks by standing on each for 3 turns, every uplink brings responders |
| Siege | Mercenary Outpost | light the beacon and survive 3 waves; the exits stay sealed until the last one is down |
| Marked Hideout | outpost, hauler or rock | a named target, much tougher, with bodyguards; it drops its kit |
| Grey Market | hauler or rock | a vendor bot selling rare and epic gear, far from Chimera's auditors |
| Rift | any place | goes deeper: a rift inside leads to another layer (up to 3), each a new place, worse and with a better vault; no jobs |

Objectives pay the moment they're done (credits, XP, items); leaving early just forfeits that.

**Who's inside.** Each breach also rolls a *roster*, a themed population that takes over part of the echo (in the
first game's spirit of per-location enemy charts): CorpSec Detail, Rogue Machines, Lab Staff, Night Shift, Dig Crew,
Scrap Pirates, Escaped Specimens, and with the Interlopers mod Riot Squad, The Brood, Fuse Crew, Spore Garden and Duct
Nest. Then:
- **Elites**: 12% of enemies (`[SideAreas] EliteChance`) get an affix: Armored, Frenzied, Hulking, Shrouded or Mending,
  with more health and a chance to drop a rare.
- **Rival crawlers**: 15% of side areas from floor 2 hold a named rival (a Nemesis body with two affixes, three times
  the health, an epic drop), marked on the minimap.
- Eight more anomalies: Alarm Grid (responder waves once you're seen), Stalker (a hunter after 20 turns), Coolant
  Breach, Void Bleed, Sentry Grid (turrets), Stim Haze (everyone hits harder), Forgotten Stores and Skeleton Shift.

**Only in side areas.** Chimera's own floors stay as they are: the breach pad is the only trace of the side areas
there (no log hints, markers or tracker lines, no new enemies, no faction operations). Inside a side area, its way out,
rifts, the sealed vault, objectives, hunters and rivals are marked on the minimap (`[SideAreas] MapMarkers`), and the
tracker and the Wire list the floors whose breach you haven't used yet.

**Modding side areas.** A mod's `"sideAreas"` object adds or replaces (by `id`) `places`, `anomalies`, `kinds`, `rosters`,
`affixes`, `vaults` (loot per grade), `nemesis` (rival bodies and names) and `effects`. `Mods/SideAreas/side_areas.json`
holds every built-in one and shows each field. An anomaly combines parts: `lights`, `hazards`, `doors`, `spawn`,
`caches`, `timer`, `pulse`, `hunter`, `alarm`, `playerEffect` / `enemyEffect`, `thin`, `vaultGrade`, `notWith`. A kind
sets `theme` (near, deeper, shallower, any, same, floor:N), `anomalyOdds`, `force` / `exclude` / `only`, `rosters` and
`replace`, `strip` / `thin` / `toughness`, `cache`, `vault`, `vendor`, `deeper`, `jobs` and an `objective` (`uplinks`,
`holdout`, `bounty`, with a `model` prop on its pads). A kind picks its place with `place` (or `places`, one rolled per
breach) and can set `size` ([min, max] rooms); a kind with neither is an *echo*, a copy of a neighbouring Chimera floor
(the old mode, kept for mods).

A **place** sets its layout `style` (`spine`, `hub`, `grid` or `caves`), `rooms`, `roomSize`, `corridor`, `spine`,
`height`, `loops` (second ways round), `archways` (gaps with no door), `lockedDoors` / `jammedOpen`, `tilesPerEnemy` /
`tilesPerLoot`, a `look` (ambient light, fog, ceiling light colour, reach, flicker and an accent colour) and a `kit`:
`floor`, `wall` and `ceiling` model lists (1 x 1 tiles and panels, picked at random), a `door` leaf (`doorHeight`,
`doorSlide` side or up), an `exit` and `props` (each with `where`: wall, corner or centre; `blocks`, `size`, `weight`,
`roles`, `glow`). Anything the kit lacks is drawn as a plain stand-in in the look's colours. `templates` are special
rooms drawn as text: `#` solid, `.` floor, `D` a doorway on the outer ring, `E` the way out, `L` loot, `S` an enemy spot,
`x` a tile taken by a big prop next to it, and letters mapped to props in `legend`; `at` says where one goes (`start`
holds the way out; `bow` / `stern` are the ends of a spine; `far` is deep inside; `any`). The game measures in units
where a tile is 1 wide, ceilings are about 2 high and your eyes are at 0.7. `SCCL_DEVTEST=places` plays every place in
game (`places:derelict+mine` for some).

The breach chance and cache size are under `[SideAreas]` in the config. The state is saved inside your normal save
file, so the save still loads without the mod. Only a save made *inside* a side area won't load without it, so step back
out before uninstalling.

## After the ending: credits and the Ichor Deep

Beating Cerberus no longer drops you back on the title screen. After the win screen, press any key and the credits
roll right there: the game's own, then the mods' (`Mods/credits_roll.txt`: a `# ` line is a heading, other lines are
names, a blank line starts a new block). A click skips them. Then you choose:

- **Stay and descend:** the run goes on. The final fight's locks come off (no more endless "combat"), the arena doors
  open, the Apartment Portal works on the final floor again, and a green breach opens beside you into the Ichor Deep.
- **Return to the title:** the vanilla ending.

**The Ichor Deep** is an endless side area (the `ichordeep` kind in `SideAreas/side_areas.json`, never rolled on a
floor). Step on its breach and choose to start at depth 1 or resume at the deepest depth you've reached.

- Depth 1 is as dangerous as the final floor. Every depth adds `EndlessPowerPerDepth` (15%) to the enemies' stats,
  damage included, and `EndlessHealthPerDepth` (20%) to their health. Each depth also raises their level, the XP they
  give, and the elite chance, by 3% a depth up to +45%.
- Every layer is new: a generated place most of the time, otherwise an echo of any floor, with its own roster and
  anomalies. One more hard anomaly joins every third depth (at most three more).
- The rewards grow too. Caches hold 3 items plus one per two depths (up to 10), Rare at first, Epic from depth 3 and
  Legendary from depth 6. Vaults gain a grade every two depths.
- Each layer has a rift one depth down. Any stair or the green pad leads back to the arena, and the breach never
  collapses.

Your win and deepest depth are tags on your character, so they travel with the save. The Ichor Deep's loot comes from
the Excavation's tables, since the final floor has none. Settings: `KeepPlayingAfterWin` and `EndlessDeep` (both
`[Gameplay]`, on), plus the two per-depth percentages above.

## Interlopers (side-area enemies)

`Mods/Interlopers/` adds enemies with their own skills, models and animations, inspired by the kinds of enemy the first
StarCrawlers had and Chimera doesn't. They only turn up in side areas, through the rosters above; the normal floors
stay as they are.

| Enemy | Role | Skills |
|---|---|---|
| Riot Bulwark | shield wall | Shield Wall (barrier on its squad), Shield Bash (knockback, may stun) |
| Fuse Drone | bomber | Critical Overload: once hurt, a 3-turn countdown, then it blows; stun it to stop it |
| Sporecap Cultivator | summoner | Sow Burstcaps, Spore Flask (Spore-Sick) |
| Burstcap | living bomb | Ripen: swells for 2 turns, then bursts in spores |
| Shellback | brood mother | Harden Shell when hurt (much less damage, mends), Brood Call (skitterlings) |
| Skitterling | swarm | Burrowing Bite (Infested, stacks) |
| Mender Unit | medic | Patch Job (heals the most hurt ally), Vitals Scan (you're easier to hit) |
| Vaultback | pouncer | Pounce: leaps beside you from 5 tiles, may stun |
| Ventmaw | duct worm | Burrow Lunge, Constrict (stuns), Acid Gush |
| Shift Supervisor | commander | Rally the Shift (squad hits harder), Flashbang |

Inside side areas some of Chimera's own staff learn tricks too: Medic troopers, Grenadier guards, Unstable bots that
melt down, Alpha mutts that howl, Volatile lab techs, Budding slimes, and enemies using class skills: Marksman
sharpshooters (Longshot's Pin Down), Demolitions geotechs (Sticky Charge), Corsair pirate bots (Cutlass Flourish) and
Heavy commandos (Pile Driver).

The creature models were made with the AAMT toolkit (Stable Diffusion 3.5 concepts, image-to-3D, Blender repair) and
rigged and animated by `_modding/rig_creature.py`. `SCCL_DEVTEST=interlopers` checks every enemy, skill and patch.

## Factions (Outside Interests)

`Mods/Factions/` brings back the first StarCrawlers' faction play, outside Chimera's walls: five groups who want
something from inside the building and hire you through the breaches. Chimera itself is not a faction.

| Faction | Who they are | Hires for | Boon (from Valued) |
|---|---|---|---|
| Kessler-Vane Holdings | rival biotech conglomerate | recover, collect, plant | +3% damage per step |
| Halyard Freight Union | couriers and smugglers | recover, collect | +4 loot find per step |
| Brassjaw Recovery | bounty hunters | clear out, take out | +2 crit per step |
| Open Ward | whistleblowers freeing test subjects | plant, collect, take out | +3 max health per step |
| Nullrow Exchange | data brokers | plant, recover | +2 dodge per step |

**Who you know:** you start knowing Halyard and Open Ward. The others turn up as you work:
- named as a job's target;
- running into their people in an echo;
- a rival's contact telling you about them;
- an encounter that points you their way;
- buying your stolen goods;
- or coming after you.

The Wire (**F9**) lists only factions you've met, and says how many are still out there.

**Standing** (as in the first game):
- **Reputation** runs from -100 to 100: Wanted, Hated, Hostile, Suspicious, Neutral, Valued, Respected, Trusted, Allied.
- **Betrayed and Deceitful:**
  - Reach Trusted, then drop to 10 or below, and they mark you **Betrayed**.
  - Fall to Hated, then climb back to -10, and they mark you **Deceitful**.
  - Either way, their reputation never goes above 0 again that run.
- **Favour** (-10 to 10) drifts after every side area: up to +3 a visit when Allied, decaying toward 0 at Neutral, and down to -3 when Wanted. Betrayal costs extra. When favour bottoms out at -10, it resets to 0 and every operation that faction has is armed again.
- **Boons:** from Valued upward each faction gives its boon, one step per tier (Valued 1, Respected 2, Trusted 3, Allied 4).

**Contacts (the Wire):** each faction has one person you deal with, who has a line for every standing and for how much favour you have. Calling them offers:
- **Favours:** a ladder of rewards bought with favour, each needing a minimum standing.
  - Cheap ones: kit, credits, consumables.
  - Expensive ones need Respected or Trusted: epic weapons and gear, a lump of XP.
  - One favour calls off whatever they have planned for you.
- **Smooth things over:** credits for +5 standing, once per floor. Not once they're Betrayed or Deceitful, and not from Wanted.
- **Ask for work:** a job of theirs is guaranteed on the next breach board.
- **Ask what their rivals are planning:** from Valued, for credits. Their rivals' operations against you show up on the Wire with a countdown, and those rivals become known.

**Jobs:**
- **The board:** stepping on a breach opens it.
  - **Offers:** one per danger level, as in the first game: Cakewalk, Easy, Normal, Challenging and Hard, plus Very Hard half the time and Extreme a quarter of the time.
  - **Pay:** from -10% to +25% by danger, plus XP.
  - **Employer:** a faction you know, chosen with weight by your standing; at -100 a faction hires you about 200 times less often than at +100.
  - **Target:** one of the employer's rivals.
- **Job types:**
  - **Recover / collect:** dead drop cases lie in the echo; carry them out.
  - **Clear out:** 3–6 marked contacts.
  - **Take out:** one marked target, much tougher.
  - **Plant:** step on 1–3 beacon pads in the employer's colour.
- **Danger:**
  - Hard and up add objectives, and the target's hit team is waiting.
  - From Hard up, everything in the echo is tougher, one more step per tier.
- **Bonus objectives** (the first game's alternative objectives) appear on about half the jobs: bring out one of the target's goods, or leave none of the target's people standing. A bonus pays 40% extra, +2 standing and +1 favour, **even if the main job fails**.
- **Turning in:** leave the echo by its way out (the green pad).
  - Done: the credits, the XP, +5 with the employer and -5 with the target. Not done: -5 with both.
  - Cases are handed over either way. The tracker in the top-left corner shows progress and the bonus.

**Territory:** every echo is occupied by a faction (the job's target, or anyone). 2–4 of its enemies are that faction's operatives.
- **Respected or better:** they stand down and leave you alone. Shoot one and it's -5, and every one of them in the echo turns on you.
- **Hostile or worse:** they're a hit team.
- **Killing them:** each one costs 2 standing with their faction, unless your job is against them.

**Encounters:** most echoes hold one faction encounter on a pad in its colour. Each faction has two:
- a runner pinned down;
- a cell full of test subjects;
- a stalled extraction;
- a tap that needs cover;
- a bounty collector looking for help;
- a toll, a debt or a recovery notice when they don't like you.

The options depend on standing, favour, credits or a class you have (a Hacker can splice it themselves, a Thought Leader can talk their way out). Locked options say why. Outcomes move standing with that faction and others, pay credits, favour, XP, loot or healing, hand you goods, introduce new factions, or start a fight.

**Operations:** when a faction's favour runs out and your standing with it is low, its operations are queued.
- **Warning first:** a message reaches you, then the operation goes off a few steps later. Every side area you enter is a step; operations never touch Chimera's own floors.
- **Ambush:** a hit team near you.
- **Hunter:** a much tougher copy of the floor's worst enemy comes looking for you, and drops an epic item when it dies.
- **Squeeze:** a cut of your credits.
- **Who does what:**
  - Kessler-Vane: an asset-denial ambush, then account freezes.
  - Halyard: storage fees, then dock rats.
  - Brassjaw: bounty hunters, then a specialist.
  - Open Ward: direct action, then a leak.
  - Nullrow: account drains, then selling your route.
- **Stopping them:** an operation fires once and re-arms when favour hits -10 or your standing recovers past its threshold. Calling in the right favour calls it off.

**Stolen goods:**
- Every echo holds 1–2 faction goods (1–5 stars, most often the job target's).
- Picking them up costs 2 standing per star with the owner. They can't be sold to vendors or stashed.
- At a breach board, **Auction goods** lets up to three of the owner's rivals bid credits, standing, favour or an item; better goods draw more bidders. Selling to a rival costs another point of standing with the owner per star. The fence always buys for a little cash, with no questions and no standing change.

**Hit teams:**
- A faction that is Hostile or worse and has no favour left for you may send a hit team into an echo: up to three tougher enemies named after it, and the log warns you. The worse your standing, the likelier.
- Betrayed and Deceitful factions are likelier still.

**The rest:**
- **F9** opens the Wire (`[Factions] StandingKey`). `[Factions] Enabled = false` turns it all off and breaches work as before.
- Standing, discovery, operations, the boards and the job in progress are saved with your run.
- **Adding factions:** a mod's `"factions"` list adds factions or replaces them by `id`. `Mods/Factions/factions.json` shows every field:
  - `rivals` (with weights), and `runsFor` / `runsAgainst` (`retrieve`, `collect`, `exterminate`, `assassinate`, `plant`);
  - `goods` (an item), `boon` (stats or `damageDealtPerStack`), `discovered` and `operative`;
  - `contact` (name, title, bio, `mood` by tier or by group, `favourLines`);
  - `rewards` (favour cost, minimum standing, loot, credits, XP, `callOff`);
  - `operations` (`ambush` / `hunter` / `squeeze`, favour and standing thresholds, delay, warning);
  - `encounters` (options with `requires`: standing, favour, credits, flags, a class; and `outcome`: standing for any faction, favour, credits, XP, loot, healing, goods, discover, fight).
- `SCCL_DEVTEST=factions` checks the whole loop in game.

## Crew (team play)

StarCrawlers-style party members. This is an option, off by default: turn on `[Crew] TeamPlay` in the config. `F6` (`[Crew] Key`) opens the crew screen.

- **Who they are:** the crawlers are the game's Nemesis rivals on your side.

  | Crawler | Faction | Weapons |
  |---|---|---|
  | Soldier | Brassjaw Recovery | ranged |
  | Smuggler | Halyard Freight Union | ranged |
  | Cyberninja | Nullrow Exchange | melee |
  | Force Psyker | Open Ward | melee |

  They keep their own moves (grenades, dashes, pulls).
- **Hiring:** only in side areas, through that faction. You need Valued standing, credits (more as you level) and 3 favour. Up to `[Crew] MaxCrew` (2) at once.
- **Their stats ignore the floor:**
  - their body's weakest profile;
  - health that follows yours (each body keeps its own toughness);
  - a little of every attribute per two levels;
  - their class tree's tier bonuses (one rank per two levels);
  - the stats and resistances of the gear you give them.
- **Gear:**
  - Give gear from your pack on the crew screen: weapon, head, torso, hands, legs, feet. Click a slot to take it back.
  - A weapon replaces the damage of their basic shot or strike. Ranged crawlers take ranged weapons, melee crawlers melee ones.
- **Worn gear:** crawlers show what they wear.
  - An equipped weapon's model replaces their own weapon, in the hand and sized by weapon type.
  - Helmets, gloves and boots use the item's 3D model on the head, hands and feet.
  - Chestpieces and legplates tint the body's own torso and legs in the item's rarity colour. Their floor models are flat props, and only the Soldier and Force Psyker have separate torso and leg meshes.
  - A mod item can give a proper wearable model for any slot: `"wear": { "mesh": "models/visor.glb", "scale": 0.3, "offset": [0, 0.08, 0], "rotation": [0, 0, 0] }`, plus optional `"wearBone"` (Head, Chest, Hips, LeftHand, RightHand, LeftFoot, RightFoot). `SCCL_DEVTEST=wear` photographs all four crawlers fully kitted out.
- **Leading a Crew:** like Conjoined's Spread Thin, you and every crawler carry one stack per other crew member. Each stack is `[Crew] DamagePenalty` % less damage (default 12) and 4 less max health. It offsets having several skilled fighters at once.
- **Wages:** every new floor costs a wage per crawler.
  - Miss one payday and they warn you.
  - Miss two and they walk off with the gear you lent them. With `[Crew] Mutiny` (on by default), they may turn on you instead; kill them to take your gear back.
- **Falling:** a crawler who falls is back on the next floor at half health.
- **Test:** `SCCL_DEVTEST=crew` checks all of it in game.

### Orders

Crew crawlers and Conjoined companion bodies take orders. With `[Orders] CommandMinions` on (off by default), your summons and pets do too (any one of them also does once
you order it from its right-click menu), so a minion build plays as tactics rather than on autopilot.

| Order | What they do |
|---|---|
| Engage | Their own judgement (the default). |
| Focus | Everyone attacks your target until it drops: the enemy under your crosshair, else the last one you hit. Then back to Engage. |
| Guard | They go after whoever last hurt you, and stay near you. |
| Hold | They stay put and fight whatever they can reach from there. |
| Follow | They stay at your side and fight only from there. |
| Stand down | They don't fight at all and stay with you. |
| Move here | They go to the floor tile you're aiming at (or a few steps ahead of you) and hold it. |
| Fall back | They regroup beside you, on the side away from the enemy, and only fight what comes adjacent. |
| Unleash | A one-off: next turn each uses its strongest ready special move on your target, then carries on with its order. If it can't within three turns, the order is called off. |

- **Giving orders:**
  - The order strip above the hotbar is out of the way until you call it up with `O` (`[Orders] OrderBarKey`; it
    remembers being up, and only shows while you have someone to order). Companions use their own AI until you
    give an order. `Alt+1` to `Alt+9` give the orders whether the strip is up or not.
  - Its first button (or `Alt+0`) switches who hears them: everyone, your crew (with Conjoined bodies), or your minions.
  - The first versions used `Ctrl+number`, which is also the game's second action bar; settings still on those
    defaults move to `Alt` once.
  - On the crew screen, each crawler has its own standing-order buttons.
  - **Right-click one of your companions** in the world (an Engineer bot, pet, summon, charmed bot, crew crawler or
    Conjoined body) for a small menu at the cursor with the orders for that one alone. The lit one is its current
    order, and the header shows what it's doing and to whom. Pets also get **Unsummon** and, if they cost scrap,
    **Break down** (click twice, as on the minion strip). Only a quick click opens it: holding the right button
    still looks around, clicks on the hotbar and other panels are theirs, and it never opens on enemies or
    neutrals. A minion ordered from its menu takes orders even with `CommandMinions` off, until you tell it to
    Engage. Click anywhere else to close it. `[Orders] MinionMenu` (on) turns it off; its size and opacity are
    `[Interface] MinionMenuSize`/`Opacity`.
  - **Choosing the target or the spot:** Focus…, Unleash… and Move here… (in the menu, and the order strip's
    buttons for the whole group) then ask for a click in the world: the enemy to go after (outlined under the
    cursor) or the floor tile to go to (marked with the game's target marker). A note by the cursor says what
    you're picking and what's under it; right-click or Esc cancels (that Esc doesn't also open the game menu).
    The `Alt` keys still use your crosshair and aim. Clicks reach through see-through things (lattices, grates,
    mesh railings, glass) but not walls, crates or machines, for picking and for the right-click itself.
  - **Jobs for you** (menu only): **Use…** a door (open or close it), a container (open it where it stands, so a
    mimic jumps it, not you) or an item on the floor (fetch it into your pack); **Unlock…** a locked door or
    container with one of your lockpicks, at the game's odds with the better of your and its Savvy and Recognition
    (and your Lockpopper), picks breaking as they do for you; **Bash…** it open with its own Strength and
    Toughness, the roll the game's AI uses on doors, each miss making the next likelier, noise and all. It walks
    over, has a go (a few for Unlock and Bash), then goes back to its order; a new order calls the job off. A
    container whose lock is picked or smashed springs open, loot and all, as it does for you. Your own minions
    standing in front of what you click don't hide it.
    Quest objects and other contraptions stay yours to use. While picking (and over the menu), your click
    doesn't also swing at, open or lockpick whatever is behind it.
- **Saving:** orders are saved with the run. Minions keep theirs for as long as they live.
- **Test:** `SCCL_DEVTEST=commands` checks every order over real turns; `SCCL_DEVTEST=minionmenu:<save>` loads one of
  your saves with saving off (Hardolo by default) and plays the right-click menu with its own minions: orders,
  picking a target and a tile, the strip's group pick, opening, unlocking and bashing a door, opening a container
  and fetching its loot, and unsummoning.

## Skill synergies

Two trees that work together give a bonus when you have both, vanilla or mod. The bundled ones are in `Mods/Synergies/`.
- **Level:** the lower of the two trees' ranks (1 when both are just taken, up to `maxLevel`, default 5). It grows as
  you rank either tree up, and a synergy only appears once both trees are taken.
- **Where you see them:**
  - Hovering a tree on the creation screen shows its synergies in a panel beside the icon. The ones your picked trees already make are marked "active".
  - In game, each active synergy is an effect on you, and the log announces new and grown synergies.

```jsonc
{ "synergies": [ {
  "id": "Accelerant", "name": "Accelerant",
  "trees": ["SKILLTREE_Pyromaniac", "SKILLTREE_Chemistry"],   // ids, mod class ids or display names; "A|B" = either
  "description": "One sentence: what it does in play.",
  "bonus": {                                                  // an effect on you (any effect field)
    "procChancePerStack": 5,                                  // each level past the first adds this to every proc's chance
    "procs": [ { "on": "hit", "chance": 30, "maxPerTurn": 1, "when": { "has": "EFFECT_Fire" }, "actions": [
      { "do": "chain", "jumps": 1, "radius": 3, "actions": [ { "do": "effect", "effect": "EFFECT_Fire", "stacks": 1 } ] } ] } ]
  },
  "allies": { "procs": [ { "on": "hit", "chance": 25, "actions": [ { "do": "effect", "effect": "EFFECT_Fire" } ] } ] }  // optional: kept on your summons and pets
} ] }
```

- **What a synergy is:** something the two classes do together, not a bigger number. The bundled ones have no flat stats,
  resists, or plain damage, healing, overshield or stamina payoffs: they spread a mark to the next foe, spend one class's
  resource on the other's effect, or set something off. Any effect field still works if your mod wants one (`stats`,
  `resists` and `damage*PerStack` grow per level, as does `perRank`). To spread from a death use the proc's `radius`: a
  `chain` on a `kill` proc starts on the corpse and does nothing.
- **Procs:** the bonus's procs fire on all your hits, kills, hurts and dodges, so gate them on the other tree's
  effects (`ifTargetHas`, `ifSelfHas`, `"MOD_Class@Effect"`). `ifTargetHas` checks whoever the action lands on, so
  on a `"to": "self"` action it checks you; to gate a self action on the enemy you hit, use
  `"when": { "has": "MOD_Class@Effect", "of": "victim" }` (or `"of": "attacker"` in hurt and dodge procs).
- **Granted skills:** `"grants": [ { "base": "Bolt", "name": "Toxic Breath", ... } ]` gives you active skills while the
  synergy holds (written like a tier's `newAbility`). They join your abilities and hotbar, rank with the first tree in
  `trees` (so `perRank` and `"rank:TREE"` conditions follow it), and are taken away if the synergy lapses. Miasmancer +
  Chaotic Mutant (Mutagenic Brew) grants Toxic Breath, which grows with your Mutant ranks.
  - `"attachTo": "Caustic Mist"` on a grant keeps it off the hotbar: a quick right-click on that skill's hotbar icon
    opens a small menu of the bonus skills riding on it (with their cooldowns), and clicking one uses it like a hotbar
    click. The parent's tooltip lists them. If the parent isn't on your hotbar, the bonus skill goes there instead.
- **Synergy Strain:** synergies come at a price, so a character with them is not simply stronger than one without.
  Each level across your active synergies is 1% less damage and 1 less max health, up to 20% less damage. Your
  summons take the same damage cut for the synergies that buff them, without the health loss. The Synergies tab shows
  your current strain, and its `[ - ]` and `[ + ]` set how much of each synergy you use, so you can keep the bonuses you
  want and shed the strain of the rest. A synergy turned down stays there as your ranks grow; turn it back up to its
  full level and it grows on its own again. You can't change them during a fight. `[Balance] SynergyStrainPerLevel` and `SynergyStrainMax` set the numbers; 0 turns it off.
- **Skills that set their own price:** an ability can pay for its class's synergies itself:
  `"synergyPenalty": { "damage": 3, "stamina": 1, "cooldown": 1 }`. Each is per level across the synergies you use that
  include the class (percent less damage, never below 10% of the damage; extra stamina; extra cooldown turns), and any
  can be left out. Once you have that skill, those synergies cost no Synergy Strain, and the skill's tooltip and the
  Synergies tab say so. Turning a synergy down lowers the skill's price too.
- **Missing classes:** a synergy naming a class that isn't installed is skipped.
- **Overriding:** a later mod with the same `id` replaces a synergy, and `"enabled": false` removes it.
- **For a single ability:** use `ifSkill` on its actions instead (see Brand-new abilities).
- **Test:** `SCCL_DEVTEST=synergies` checks it in game; `SCCL_DEVTEST=classsyn` plays `synergies_j.json` proc by proc;
  `SCCL_DEVTEST=allsyn` fires every proc in the pack.

## Vanilla bug fixes

`[Fixes]` in the config file; each one can be turned off.

- **BossesAreElites:** perks that reward elites only looked for the game's elite tag, so a boss gave less than an elite.
  Xeno's Render Ooze now makes the elite ooze from boss, miniboss, nemesis and kill-squad corpses too.
  Mod conditions (`"tag": "elite"`) and `execute` already treat bosses as elites, whatever this is set to.
- **ChemistryOnPetSlime:** a chem fed to your pet slime with Special Treat (Xeno's Craft Ooze Jar perk) skipped
  Try Anything Once and half of Weirdest Science, so the slime always crashed in full. Your slime now counts as you:
  it can shrug off the crash, the crash lasts half as long, and ArmrAll blocks 8 hits. Test: `SCCL_DEVTEST=chemistry`.
- **TryAnythingOnceAddiction:** Try Anything Once never lowered the addiction chance for Zerk, Promethean, YEETI,
  Weep, Mindscrew or Soulsuck (their check could never pass). Now it does, as for Choo Choo, MentX and Spice.
- **WeirdestScienceKeepsPositives:** the Weirdest Science perk cut YEETI, Weep, Mindscrew and Soulsuck to a single
  turn instead of halving only their crash. They now last until their charges run out, as without the perk.
- **StuckTurnRecovery:** "UH OH! Something went wrong during the turn… use K to attempt recovery" meant a skill hit
  an error halfway and the turn waited for it until the game's 20-second watchdog gave up. Now a skill that errors
  stops there and the turn carries on as normal (enemies act, cooldowns tick), and the log says
  "<skill> hit a game error and stopped early". A turn that still hangs is freed by itself when the watchdog fires,
  without K. The full error goes to `BepInEx/LogOutput.log` (`SKILL ERROR` / `STUCK TURN`), so it can be fixed.
  `SCCL_DEVTEST=stuck` breaks Homebrew on purpose, with the fix off and on.
- **EndGameMinionsComeBack:** the end-game tunnel and fake apartment hide your minions for their cutscene and the
  game never shows them again. Engineer bots came through the portal invisible: their hotbar buttons stuck, no new
  bot could be built, and their skills hung the turn. They come back once you're past those two rooms.
- **ApartmentLockClears:** those rooms turn off the apartment portal, and the game never turned it back on, so after
  reaching the end-game the portal home (and turn autosave) stayed off for every character until a restart.
- **PortalHomeInEndGame** (`[Gameplay]`): the Apartment Portal also works on the end-game floors, to restock before the
  final fight. Not in the fake apartment, boss rooms or on the lift, and not once the final fight has begun.
- **DrawDistanceResets:** the final map draws characters out to 100 m instead of 20, and the game's reset for other
  maps reset the door distance instead, so every map afterwards kept drawing characters out to 100 m.
- **CerberusLimits:** the final fight piled things up with no limit, which players felt as it getting slower.
  - **Warp hazards:** Cerberus's warps leave hazards around where it leaves and lands. The phase-3 inferno warp
    stacked a new inferno hazard on the same tiles every time. Now each tile takes one warp hazard, and past
    `CerberusMaxWarpHazards` (30) the oldest burns out.
  - **Portal adds:** the phase-3 portal spawned an add every 5 turns until phase 4, with no limit. Now it waits while
    `CerberusMaxPortalAdds` (4) are alive.
  - **Annihilation beam:** its particles scaled with beam length (20 per second per metre). Now each stream is capped
    at `CerberusBeamParticles` (300 per second).
  - `SCCL_DEVTEST=cerberus` plays phase 3 for 25 turns with the limits off and 25 on, and counts what's left.
- **SafeApartmentReturn:** leaving the apartment drops you on the tile you left, with the floor frozen as it was,
  and the portal or door saves first. Portal out of a fight on low health and every reload starts in the apartment
  with the same enemies next to the door. Now enemies next to you on the way back are moved elsewhere in the room
  (bosses stay), the way the game already moves anything standing on a staircase you arrive on.
  `SCCL_DEVTEST=safereturn` plays it with the fix off and on.
- **PerkNumbers:** skills that didn't do what they say.
  - **Iron Harvest:** the perk set the burst to 20 stacks instead of 10 (the tooltip said 10), until you opened the
    tooltip, and again after every load.
  - **Avast Ye!:** the tooltip showed the stun chance as 1% + 1% per Tempt Fate stack, max 15%. The real chance,
    now shown, is 3% + 4% per stack, max 50%.
  - **Pyramid Scheme:** the per-minion percentage was applied twice, so 3 minions gave about +0.3% damage instead of
    +9%. The perk's minion soaked 3% of a hit instead of 30%.
  - **Nanite Replication:** at 3 stacks it used them up, and said a jar was filled, even with no empty jar. Now it
    waits at 3.
  - **Mech Part:** the heal was always 10%. It now goes 5% per Engineer rank, from 10% up to 30%.
  - **Unobtanium Chrome Job:** each hit's Toughness and the 3-hit cap were worked out from Toughness that already
    held the bonus, so it went to 4 stacks and the cap kept rising (+38 instead of +24 at 82 Toughness). It now
    stops at 3 hits, and the perk's armor stops with it.
  - **Tempt Fate:** stacks made before you took Daring Maneuver's perk kept decaying at the old rate. They now
    follow the perk.
  - **Bastion:** Fortress was set up twice per proc (its sound played twice).
  - **Lucky Find:** the perk's tooltip said a triggered Lucky Find had a 10% chance (half the upgrade chance) to find
    something from a lower floor, but it always does, once per turn. The tooltip now says 100%.
  - **Old Faithful:** a guard. If a stack ever took the bonus past its cap, the reset dropped it below the cap
    (20% to 15%). Now it holds at the cap.
  - `SCCL_DEVTEST=perks` and `SCCL_DEVTEST=perks2` check each with the fix off and on.
- **WeirdestScienceDoublesDose:** Weirdest Science doubled the whole running charge total of Promethean, YEETI,
  Weep, Mindscrew and Soulsuck on every dose, so a second YEETI made 60 charges instead of 40. It now doubles the
  dose just taken.
- **SkillStateOnLoad:** Combo Builder saved its state but loading never read it, so the combo counter, last skill
  class, Button Masher progress and the cooldown that gates Konomo Code reset on every load (and the Combo Point icon
  went neutral). They now survive a save and load.
- **EnemyChargeRoll:** Demented and Doomed say they start with up to 5 charges but never rolled 5. Now they can.
- **WarCraftShields:** War Crafting two shields added their Shield Regen together. Shield Regen is the number of
  turns per shield point, so the new shield refilled slower than either one (two Heavy Shields at 8 made one at 16).
  It now takes their average. Older builds could also drop a shield's Shield Max in the merge, leaving a shield
  that blocks nothing and can't be sold or crafted again. Loading the save gives such a shield back the Shield Max
  of its base item. `SCCL_DEVTEST=warcraft` checks both.
- **HealthRegenPastCap:** the game heals 1 HP every (8 − Health Regen) turns, so at 7 Health Regen you heal every
  turn and more did nothing. Now every 5 points past 7 heal 1 more HP per turn (12 heals 2 a turn, 32 heals 6).
  Up to 7 nothing changes. The Health Regen tip on the character sheet says what yours heals.
- **LockerSaveFix:** the game only saved the apartment locker while your inventory held something.
  - **Duplicate:** take an item out of the locker and equip it, so your inventory is empty again, then close. The locker isn't saved, so reopening it brings the item back while you're still wearing it.
  - **Loss:** move your last items into the locker and close it. Reopening it shows the older contents, and the items you just moved are gone.
  - **The fix:** once the locker has been opened, it is always saved when it closes.
- **SortFix:**
  - **Sort only rearranges:** sorting the inventory or locker rebuilt them from an internal list that could disagree with the slots. In vanilla a stale entry made the locker sort pull an item out of your inventory into the locker. Now the sort works only on what is in the slots, each item once, so it can only rearrange. An item that somehow shows in both the locker and the inventory stays in the inventory.
  - **Sort by value** now sorts (vanilla's version left the order unchanged).
  - **Ammo:** sorting the locker no longer moves ammo that matches your equipped ammo into your ammo slot.
- `SCCL_DEVTEST=sortfix` checks all of this:
  - It fuzzes about 240 random moves, merges, splits and sorts through the game's own code, with the fixes off and on.
  - It replays the duplicate and the loss with and without the fixes.
- **ModderStaysInSave:** the Modder's item skills (Mod It Better, Illegal Weapon Modification, Item (Re)Designer, Fusion Forge, War
  Crafting) could target an item in the shared stash. The reworked item stayed there, so every other character
  could take it. They now work only on your bag, your gear and your Own stash page.

## Modder rebalance

The Rogue Modder out-scaled every other tree. Mod It Better put +5 on a random stat three times per item, the first
mod could never fail, a failure cost 1 point at the top rank, and Item (Re)Designer reset the mods, so any miss was a
free reroll. With `[Balance] ModderRebalance` on (the default):

- **Mods per item** come with Modder rank: 1 at ranks 1-2, 2 at ranks 3-4, 3 at rank 5.
- **Each mod** adds 1 + rank/2 (+1, +2, +2, +3, +3) to a random stat.
- **Failure odds:** the 1st, 2nd and 3rd mod on an item fail 10%, 30% and 50% of the time. A failure takes the same
  amount off instead, and it still uses up the slot.
- **Item (Re)Designer** keeps the item's Mod It Better and Illegal Weapon Modification counts. A redesign rolls the stats again, so the
  mods' bonuses go with it: redesign first, then mod.
- Gear modded before this keeps its stats. Turn the option off for the vanilla rules.
- `SCCL_DEVTEST=modder` (or `modder:SaveName`) checks it on a character with the tree.

## Mod manager

`BreachworkModManager.exe` in the game folder manages mods while the game is closed. It lists every mod in load order
with an on/off box, its version, author and status (`Loads`, `Off`, or why it can't load, such as `Needs Core Library
(turned off)`).

- **Load order**: drag a mod, use **Move Up / Move Down / To Top / To Bottom**, or Alt+Up/Down. A mod never moves
  above what it needs or loads after; **To Top** stops right below it, and a blocked move says which mod is in the way.
  **Automatic Order** forgets the chosen order.
- **On/off**: tick or untick. A mod that needs one you turned off shows it at once, and **Turn On What It Needs** fixes
  that.
- **Code**: a mod with a [code plugin](#code-plugins) waits for **Allow Its Code**, which is asked again when its code
  changes.
- **Details** of the selected mod: description, license, folder, what it needs, loads after and is needed by, what it
  holds, which saves have characters using its classes, files that could not be read, and what the last game
  start's log said about it.
- **Install from .zip / folder** (or drop one on the list): finds the mods inside (folders with a `mod.json`, a
  release package's `Mods` folder, or one folder of mod files) and copies them into `Mods/`. A mod already installed
  (same id or folder) is replaced after asking; the old copy goes to the Recycle Bin. **Remove…** moves a mod's
  folder to the Recycle Bin.
- **Play** starts the game. While it runs, the manager says changes apply the next time it starts.

The manager uses the loader's own code (`_modding/ClassLoader/ModCore.cs`) to read, own and order mods, so what it
shows is what the game loads. `SCCL_DEVTEST=modorder` checks in game that the loader used the order chosen by the
manager's own self-test, described in the developer guide.

## Mods page

**Mods** on the title screen (under Options) lists every mod under `Mods/` with an **On/Off** switch and where it
stands: loaded and its place in the load order, off, or why it won't load (`Needs Library (turned off)`, an unreadable
`mod.json`, a taken id). The list shows what the **next start** will load, so switching a requirement off shows its
dependants dropping with it straight away, and a mod that needs something switched off offers **Turn On What It
Needs**. A mod with a [code plugin](#code-plugins) waits for **Allow This Version** or **Always Allow**. Selecting a mod shows its id, version, author, license and description, its folder (**Open Folder**), what it
needs, loads after and is needed by, what it holds (classes, patches to other mods' classes, libraries, items,
enemies, recipes, ...), which saves have characters using its classes (they load with that skill slot empty while
the mod is off), and the errors and warnings this session's log has about it.

Mods are read while the game starts, so a change applies after a restart: the bottom bar lists what will load and
unload, with **Undo Changes** and **Restart Now** (quits and starts the game again). The page and the mod manager share
their choices: `BepInEx/config/arendeth.starcrawlers.classloader.mods.json` holds the mods turned on or off (winning
over each mod's own `"enabled"`), the load order and which mods may run code, saved at once. Updating a mod keeps them; deleting the file goes
back to the mods' own settings and the automatic order. `SCCL_DEVTEST=mods` and then `SCCL_DEVTEST=mods2` check the
page and a restart with a mod turned off.

## Mod Settings

**Options → Mod Settings** (title screen or in game) lists every loader setting by section: on/off toggles, `-`/`+`
steppers for numbers, click-then-press for hotkeys (single keys, or combos such as `Alt+1`: hold the modifiers and
press the key), and **Reset** on anything changed from its default. A key that something else also uses says so
("Also bound to: the game's Quicksave", another mod key, a hotbar slot). Changes are
written to `BepInEx/config/arendeth.starcrawlers.classloader.cfg` at once, and almost all apply right away; the few
read only at startup (such as `Classes/TierSlots`) say "applies after a restart". `SCCL_DEVTEST=settings` checks the
page in game.

**Mod Content** (the first section) is a tree of every mod, the kinds of content in it and each entry, with **On/Off**
at every level and `+`/`-` to open a level:

- **Mod**, e.g. Arendeth Classes (the same switch as the Mods page and the mod manager), then
- **Kind**: Classes, Items, Enemies, Enemy changes, Recipes, Synergies, Factions, Side area kinds, Side area places,
  Anomalies, Affixes and Tweaks (each `"tweaks"` setting is its own entry), then
- **each entry**: one class, item, synergy, tweak, ...

Off on a mod or kind turns off everything under it; the entries keep their own settings for when it comes back on. A
turned-off entry is taken out of its mod's files before anything reads them, as if it had never been written. A
turned-off patch therefore leaves what it patches as it was, and another mod's patch to a turned-off class is skipped
too. Particles, wear models, effects, rosters and vaults aren't listed: other content points at them. A class row says
which saves have that class; turned off, those characters load with that skill slot empty. Rows note what is off
inside them and why a mod won't load.

Content is read while the game starts, so changes apply after a restart: the section says so, with **Undo** and (on
the title screen) **Restart Now**. Choices go in the shared choices file under `"off"` as `Mod/kind` or
`Mod/kind/id` keys, e.g. `ArendethClasses/classes/MOD_Runeforger` or `Synergies/synergies`. `SCCL_DEVTEST=content`
and then `SCCL_DEVTEST=content2` turn off a class and a whole kind with the tree's own buttons and check the restart
in game.

**The whole keyboard binds.** The game's own Keybindings screen used to refuse keys outside its short list; now
Enter, Backspace, Delete, Caps/Num/Scroll Lock, Pause, Print Screen, the Windows and Menu keys and every numpad key
(`+ - * / .` and Enter) can be bound too, and numpad keys read `NUM 1` rather than `1`. When a mod key combo and a
hotbar slot share a key (`Alt+1` with the third action bar on, say), the mod key takes that press.
`SCCL_DEVTEST=keys` checks it in game.

**Longer names:** `[Creation] MaxNameLength` (default 32, up to 64) raises the creation screen's 25-character name
limit; 0 keeps the game's. The character sheet shrinks a name too wide for its header bar onto one line, the save list
already wraps and shrinks it, and the ChimeraLinq sign-up box gets the same room so it doesn't cut your name off.
The box keeps the case you type ("Vex the Unbroken", "McAllister"); turn `[Creation] AutoCapitalizeNames` on for the
game's way, which capitalises every word and lowercases the rest. Either way only letters, single spaces, hyphens and
one apostrophe get in, as in the game. `SCCL_DEVTEST=names` and then `SCCL_DEVTEST=names2` check the limit, the sheet
and the save list in game.

**Interface** sets the size and see-through of every panel the loader draws: the body picker, bonus skill menu, build
list, combat badge, crew screen, dialogs, faction screens, graveyard, hotbar page label, job tracker, minion strip, minion right-click menu, this page, order strip,
party panel, reactions strip, Rune Matrix, load-screen class icons, stash tabs, Synergies tab and synergy tooltip. Each
has `<Panel>Size` (50-200%) and `<Panel>Opacity` (10-100%), and `AllSize` / `AllOpacity` multiply into every one.
Windows and HUD strips also have a small grip in the corner: drag it to change that panel's size setting (`ResizeGrips`
off hides them). On the Synergies tab it scales the text and its `[ - ]` and `[ + ]`, rewrapping them to the box.
Opacity fades the panel's backgrounds and keeps its text and icons solid; turn on `FadeText` to fade those too.
The resource bars keep their own `[Resources] Scale` and `IdleOpacity`.

**Apartment Portal button:** the skills screen puts it in the ninth tier column, on top of the extra tier slot. With
`PortalBesidePerks` (on by default) it sits to the right of the perk point count instead, with room for the count to
reach 99. The ChimeraLinq and Vendordamus Beacon buttons stack down that same column; with
`SpecialButtonsInFreeSpots` (on by default) each goes only where nothing else is: the lowest cell of that column whose
row has no tier slot there, else the points line left of the skill point count (`SCCL_DEVTEST=portal`).

**Collapsing:** the order strip, minion strip, reactions strip and job tracker have a small `-` tab just above
them, and the crew, body picker, faction, Rune Matrix and graveyard screens have one in their top-left corner.
Click it to fold the panel down to a `+ Name` tab, and click that to unfold it. The HUD strips remember being folded
between sessions; screens open whole every time. `CollapseButtons` off removes the tabs and shows everything. (The
party panel and resource bars already have their own hide tab and hotkey.)

**Character tips:** the tip over a character (name, health, effects) shows only while you hold
`CharacterTipKey` (Left Alt by default), so it never covers what you're about to click. `CharacterTipNeedsKey`
off brings back the base game's tip on every mouse-over. Either way it lets clicks through, and mod effects get a
short row in it (stacks and damage changes); point at the effect's icon for the full text. Tips while aiming an
ability always show. `SCCL_DEVTEST=ui` checks all of this in game.

**Stack limits:** the game's skill and perk tooltips give the amount per stack but rarely where it stops. They now add a
`Stacks:` line saying how far a skill builds up, or that it has no limit. Examples: Combo Points 5, Tempt Fate 15,
Rolling Thunder and Judgement 10, Carapace 5, Void Energy 100, Rage 6 Strength per Bodybuilding rank, 3 mods / 5 widgets
per item, 3 Boobytraps, and Better Living has no limit. Values that depend on your rank, level or stats show yours.
`[Interface] ShowStackCaps` off removes the line. `SCCL_DEVTEST=stackcaps` points at every tier on the skills screen.

## Scripts (Lua)

When json isn't enough but a C# plugin is more than you want, a mod can carry **scripts**: Lua files in its `scripts/`
folder that add new actions, conditions, numbers, events and kinds of content, react to what happens in a run, and
reach into the world (who is where, health, effects, items, credits, text and sound). Class, item and enemy files use
what a script registers by name, just as they use a plugin's. `Mods/ExampleScript/` is a complete worked example: the
Spark Tinker class (`spark_tinker.json`), its scripts (`scripts/spark_tinker.lua`, `scripts/util.lua`) and a data file
for them (`scrap_prices.json`).

Scripts are **sandboxed**, so unlike a dll they load without the player's OK: a script reaches the game only through
the `mod` and `game` tables below. It has no access to files, the internet, the operating system or the game's own
code, `require` only loads files from its own mod's `scripts/` folder, and a call that runs too long (an endless loop)
is cut off. Scripts are plain text, so anyone can read what a mod does.

### Making one

1. Make `Mods/MyMod/mod.json` as for any mod, and a `scripts/` folder beside it.
2. Put `.lua` files in it. They run in name order, early in game start (no level or player yet), all in one shared Lua
   state per mod, so a global set in `a.lua` is seen by `b.lua`, and `require("b")` loads a sibling as a module.
3. Register things with `mod.…`, then use them from your json as `"MyMod:name"`.
4. Start the game. Scripts need no restart prompt and no permission: turn the mod on and play. Errors go in the log under
   your mod (`BepInEx/LogOutput.log`, and the mod's page on the Mods screen), with the file and line.

```lua
-- scripts/smite.lua
mod.action("smite", function(call)
  local n = call.amount()                                          -- "amount" with "perRank" and "scale" worked in
  call.run({ act = "damage", type = "Energy", min = n, max = n })  -- the framework's own actions, every rule applied
  if call.target.is_dead then mod.fire("smote", call.target) end
end, { describe = "Smites {who} for {amount}", hostile = true })

mod.event("smote", "when you smite someone down")                -- so a proc's tooltip reads "When you smite someone down:"
mod.on("kill", function(e) mod.log(e.other.name .. " is gone") end)
```

```jsonc
"actions": [ { "do": "MyMod:smite", "amount": 5, "perRank": 1, "scale": "Smarts", "chance": 80 } ],
"procs":   [ { "on": "MyMod:smote", "actions": [ { "do": "heal", "to": "self", "amount": 3 } ] } ]
```

`do` and `else` are Lua words, so action tables write them as `act` and `orelse`: `{ act = "if", when = { … },
then = { … }, orelse = { … } }`. Everything else is spelled as in the class-file format.

### The `mod` table: your mod

| Call | Used in json as | Notes |
|---|---|---|
| `mod.action(name, fn(call), options)` | `"do": "MyMod:name"` | Works everywhere actions do. `options`: `describe` (a string with `{who}` and `{amount}`, or `function(spec, who)` returning the tooltip line), `hostile = true` to keep it off your own side for `ignoresAllies` classes. |
| `mod.condition(name, fn(call, who) → bool, describe)` | `"when": "MyMod:name"` | `who` is whoever is checked. A condition that errors doesn't hold. |
| `mod.value(name, fn(call, who) → number, describe)` | `"value": "MyMod:name"` | A number for value checks; return `nil` if it can't be read. |
| `mod.event(name, describe)`, `mod.fire(name, other, detail)` | `"on": "MyMod:name"` | Your own trigger. `other` is an actor, `detail` what `"match"` compares against. |
| `mod.on(event, fn(e))` | | Runs whenever a proc with that `on` would (`hit`, `kill`, `hurt`, `turn`, `newfloor`, `loot`, … see [Conditions, counters and triggers](#conditions-counters-and-triggers)), or another mod's `"OtherMod:name"`. As with procs, what it does can't set off more procs or events. Also `"ready"` (the game and every mod are loaded; can run more than once) and `"update"` (every frame). |
| `mod.data(name, fn(entry))` | `{ "pluginData": { "MyMod:name": … } }` | A new kind of content any mod's json can add to. Runs once per entry in load order, after every script and plugin has loaded; `entry.value` is the json as a table, `entry.mod` and `entry.file` say where it came from. |
| `mod.setting(key, default, description) → setting` | | A setting under **Options > Mod Settings**, in a section named after your mod. `default` is a number, string or boolean; read and write `setting.value`. |
| `mod.saved(key, fallback)`, `mod.save(key, value)` | | A number, string or boolean saved with the current run. Only these, so a save still opens without your mod. `save` returns false when no run is going. |
| `mod.key("F9", fn)` | | Runs when the key is pressed, outside text fields. Unity `KeyCode` names: `F1`..`F12`, `Alpha1`, `Keypad5`, `LeftBracket`, … |
| `mod.after(seconds, fn)`, `mod.every(seconds, fn) → timer` | | Timers in real time; `timer.cancel()` stops one. |
| `mod.log(text)`, `mod.warn(text)`, `mod.error(text)`, `print(…)` | | Log lines, shown on your mod's page in the Mods screen and the mod manager. |
| `mod.id`, `mod.name`, `mod.version`, `mod.folder` | | What `mod.json` says. |

Names you register are always your mod's (`MyMod:name`), so scripts can't collide, and json refers to them
case-insensitively. Files in `scripts/` are never read as mod data.

### The `call`: one run of your action, condition or value

`call.caster` (who acts), `call.target` (who it lands on, with `"to"` applied), `call.attacker`, `call.victim`,
`call.ally` (as in a proc's event), `call.rank`, `call.class_id`, `call.source_name`, `call.center` (the aim point),
`call.damage_mult`, `call.spec` (the action as written, as a table), `call.amount()` (the `amount` with `perRank` and
stat `scale`), `call.arg("field", fallback)` (your own fields from the json), `call.effect_id("@Hexed")` (this class's
effect id; `"#Shocked"` is the library's), `call.stacks(who, "@Hexed")`, and `call.run(actions, on)`: runs class-file
actions in this call's context, so `to`, `chance`, `when`, `@Effect` and every other field work, landing on `on` or
the target. An event handler gets `e.name`, `e.bearer`, `e.other`, `e.ally`, `e.detail` instead.

### The `game` table: the world

| Call | Notes |
|---|---|
| `game.player()` | You, as an actor (nil on the title screen). |
| `game.allies()`, `game.enemies()` | Lists of actors: your side (you, summons, pets, crew), or every living hostile on the floor. |
| `game.enemies_near(at, radius)`, `game.near(at, radius)` | `at` is an actor or a `vec`. |
| `game.distance(a, b)`, `vec(x, y, z)`, `v.dist(other)`, `v.add(dx, dy, dz)`, `v.x`/`v.y`/`v.z` | Positions. A tile is 1 unit. |
| `game.in_combat`, `game.loading`, `game.floor`, `game.turn`, `game.time` | Where things stand. |
| `game.credits`, `game.add_credits(n)` | |
| `game.give_item(id, count)`, `game.has_item(id, count)`, `game.count_item(id)`, `game.take_item(id, count)`, `game.item_name(id)` | Vanilla ids, or mod items as `"OtherMod:Id"` (your own by short id). `take_item` is all or nothing. |
| `game.spawn(creature, at, "enemy" or "ally")` | A vanilla creature id or a mod creature (`"OtherMod:Id"`), as an actor. |
| `game.run(actions, caster, target, sourceName, classId)` | Class-file actions outside any call. `classId` makes `@Effect` names that class's. |
| `game.vfx(name, at, scale)`, `game.sound(spec, at, volume)` | A particle from `vanilla_reference.txt` or a mod's own; a vanilla FMOD event (`"event:/…"`) or a sound file in your mod (`"sfx/ping.wav"`). |
| `game.text(text, at)`, `game.notify(text)`, `game.message(text)` | Floating text at a point, in the middle of the screen, or a line in the message log. Rich text works. |
| `game.random()`, `game.random(n)`, `game.random(min, max)`, `game.chance(percent)` | A float in [0, 1), 1..n, min..max, or a percent roll. |
| `game.mod_loaded(id)`, `game.api_level`, `game.loader_version` | |

### Actors: characters as a script sees them

The same character is always the same actor, so `foe == call.target` works, and a dead or gone one says so instead of
erroring: `a.name`, `a.id` (the creature's internal name), `a.kind` (`Player`, `Humanoid`, `Mechanical`, `Monster`,
`Supernatural`, …), `a.level`, `a.is_player`, `a.is_ally`, `a.is_enemy`, `a.is_dead`, `a.alive`, `a.exists`, `a.pos`,
`a.distance(other)`, `a.health` (settable), `a.max_health`, `a.stamina` (settable), `a.max_stamina`, `a.shield`,
`a.stat("Smarts")`, `a.resist("Fire")`, `a.tag(name)`, `a.has_tag(name)`, `a.add_tag(name, n)`, `a.remove_tag(name)`,
`a.stacks(effect)`, `a.has_effect(effect)`, `a.counter(name)`, `a.set_counter(name, n)`, `a.add_counter(name, d)`.

What a script does to an actor goes through the framework's own actions, so resists, shields, dodges, procs and logs
all apply: `a.damage(amount, type, by, source)`, `a.heal(n)`, `a.add_shield(n)`, `a.add_stamina(n)`,
`a.effect(id, stacks, turns, by)`, `a.purge(id)`, `a.cleanse(n)`, `a.dispel(n)`, `a.stun(turns)`,
`a.knockback(from, tiles)`, `a.kill()`, and `a.act(action, by, source, classId)` for any other action in the
class-file format. Effect ids are written as in that format: `"#Shocked"` for the library's, a vanilla id, or
`"@Name"` for a class's own (inside a call, or with `classId` given). Counters on you are saved with the run.

### Rules and safety

- **A script that errors only costs that call.** The error goes in the log under your mod with its file and line, and the
  game goes on: a failed action does nothing, a failed condition doesn't hold. A file that fails to load costs that
  file only. After 25 errors a mod's script errors stop being logged, and its `"update"` handlers stop after 10.
- **A call that never returns is stopped** after five million Lua instructions (well under a second), with an error.
  `pcall` catches errors from `game` and `mod` calls like any Lua error.
- **Scripts are sandboxed**: no `io`, `os` (beyond `os.clock`/`os.date`/`os.time`), `debug`, `dofile` or `loadfile`; no
  game types beyond the actors and tables above. So a script mod loads like any data mod. Anything a script can't
  reach needs a [code plugin](#code-plugins).
- **Your scripts are yours.** Like a plugin, a script is its author's own work, on its author's terms, and
  `Mods/ExampleScript/scripts/` is free to copy with no conditions.
- **What's stable:** the `mod`, `game`, `call`, event and actor members above, and the class-file format's field names.
  Lua is MoonSharp's Lua 5.2 (`string`, `table`, `math`, `bit32`, `coroutine`, `json` and the basics are there).
- `SCCL_DEVTEST=scripts` plays all of this in the real game.

## Code plugins

JSON covers most mods, and [scripts](#scripts-lua) most of the rest. When a script can't reach something (it is
sandboxed), a mod can carry its own C#: a **plugin**, a `.dll` that adds new
actions, conditions, numbers, events and kinds of content to the framework. Class, item and enemy files then use them by
name, so everything else about the mod stays data. `Mods/ExamplePlugin/` is a complete worked example: the Bounty Hand
class (`bounty_hand.json`), its plugin's source (`src/`) and a data file for it (`bounties.json`). It's off by default.

### Making one

You need the [.NET SDK](https://dotnet.microsoft.com/download) and the .NET Framework 4.7.2 Developer Pack.

1. Copy `Mods/ExamplePlugin/` to `Mods/MyMod/`. Rename the id in `mod.json`, and `AssemblyName` in `src/*.csproj`.
2. Set `"apiLevel"` in `mod.json` (1 or 2, see below): the lowest plugin API level your code needs. A loader older than that skips the mod
   and says why, rather than crashing on a missing method.
3. Run `dotnet build -c Release` in `src/`. The dll lands in `plugins/`, the folder the loader reads it from.
4. Start the game, turn the mod on in **Mods** on the title screen and choose **Always Allow** so your rebuilds aren't
   asked about, then **Restart Now**. Close the game before rebuilding: it keeps the dll open.

The project references your own install of the game and Breachwork. Share only your dll and your mod's files: none of
those references go in your mod, and game files must never be redistributed.

```csharp
using Breachwork;
using SCClassLoader;     // ActionSpec, the class-file format as C#

public class MyPlugin : BreachworkPlugin
{
    public override void OnLoad()
    {
        Context.AddAction("smite",
            call => call.Run(new ActionSpec { @do = "damage", type = "Energy", min = call.Amount(), max = call.Amount() }),
            (spec, who) => $"Smites {who} for {spec.amount}", hostile: true);
        Context.On("kill", e => Context.Log.Info($"{e.Other?.GetName()} is gone"));
    }
}
```

```jsonc
"actions": [ { "do": "MyMod:smite", "amount": 5, "perRank": 1, "scale": "Smarts", "chance": 80 } ]
```

### What a plugin gets

A plugin is a class deriving from `BreachworkPlugin`; every one in a dll is started, in load order. `OnLoad` runs early in
game start, with no level or player yet, and is where you register things. `OnGameReady` runs once the game and all mods
are registered; it can run more than once in a session, so don't count on once. `OnUpdate` runs every frame.

Everything else is on `Context`:

| Call | Used in json as | Notes |
|---|---|---|
| `AddAction(name, run, describe, hostile)` | `"do": "MyMod:name"` | Works everywhere actions do: abilities, procs, effects, items, enemies. `chance`, `to`, `when`, `ifTargetHas` and the rest apply as usual. `describe` writes the tooltip line. `hostile: true` keeps it off your own side for `ignoresAllies` classes. |
| `AddCondition(name, check, describe)` | `"when": "MyMod:name"` | Gets the call and whoever is checked. |
| `AddValue(name, read, describe)` | `"value": "MyMod:name"` | A number for value checks. Return null if it can't be read. |
| `AddEvent(name, describe)`, `FireEvent(name, other, detail)` | `"on": "MyMod:name"` | Your own trigger; `describe` is the tooltip wording ("when you collect a debt"). |
| `On(event, handler)` | | Runs whenever a proc with that `on` would: `hit`, `kill`, `hurt`, `turn`, ... (see [Conditions, counters and triggers](#conditions-counters-and-triggers)), just before the procs. As with procs, what it does can't set off more procs or events. |
| `OnData(name, read)` | `{ "pluginData": { "MyMod:name": ... } }` | A new kind of content that any mod's json can add to. Runs once per entry in load order, after every plugin's `OnLoad`; `d.As<List<MyType>>()` reads it. |
| `Bind(key, default, description)` | | A setting under **Options > Mod Settings**, in a section named after your mod. |
| `GetSaved(key, fallback)`, `SetSaved(key, value)` | | An int, float, bool or string saved with the run. Only these, so a save still opens without your plugin. |
| `Harmony` | | Your own Harmony instance, for anything the API doesn't cover: `Context.Harmony.PatchAll(GetType().Assembly)`. |
| `AddPortraitButton(label, click)` | | A button on the character picture picker (API level 2). |
| `Log`, `ModFolder`, `PathTo(file)`, `StartCoroutine(routine)` | | Log lines show on your mod's page in the Mods screen and the mod manager. |

An action's handler gets an `ActionCall`: `Caster`, `Target`, `Attacker`, `Victim`, `Ally`, `Source`, `Rank`, `Center`,
`Spec` (the action as written), `Arg<T>("field", fallback)` for custom json fields, `Amount()` (amount with rank and stat
scaling), `Stacks(who, "@Effect")`, and `Run(...)` to run built-in actions in the same context, which is the easy way to
deal damage or apply effects with all their usual rules. `Api` has helpers for code outside a call: `EnemiesNear`,
`IsAlly`, `Stacks`, `Counter`, `SetCounter`, `Vfx`, `FloatText`, `ParseActions`, `RunActions`, `ModLoaded`, and at API level 2
`CurrentForm`, `PortraitFolder`, `CurrentPortrait`, `SetPortrait`, and at level 3 `SimultaneousTurns`.

Names you register are always your mod's (`MyMod:name`), so plugins can't collide, and json refers to them case-insensitively.
Files in a mod's `plugins/`, `bin/` and `obj/` folders are never read as mod data, so a plugin can keep its own json there.

### Rules and safety

- **A plugin that throws only costs itself.** The call fails, the error goes in the log under your mod, and the game goes
  on: a failed action does nothing, a failed condition doesn't hold. `OnLoad` throwing removes the plugin and what it
  registered. `OnUpdate` stops after 10 throws. After 25 errors a plugin's errors stop being logged. A dll that won't load
  doesn't stop the others.
- **A plugin is a real program, not sandboxed.** So a mod with a dll in `plugins/` doesn't load until the player allows it,
  on the Mods page or in the mod manager, with a warning. **Allow This Version** asks again when the code changes;
  **Always Allow** doesn't. Stale or refused, the whole mod stays out, so its classes never appear half working.
- **Your plugin is yours.** It's your own code, not part of Breachwork, so you decide its terms, commercial use included.
  Writing against the plugin API doesn't put it under Breachwork's license, and the example plugins' `src/` folders are free to
  copy with no conditions. Anything of Breachwork's you ship with it (classes, art, sounds) keeps its own license, no sound may be sold as
  a sound or in an audio pack, even altered, and `audio_licensed/` sounds may not be used commercially at all.
- **What's stable:** the `Breachwork` namespace and the fields of the class-file format (`ActionSpec` and friends). Anything
  reached through Harmony or reflection can change with any update, of the game or the loader.
- `SCCL_DEVTEST=plugins,plugins2,plugins3` plays all of this in the real game, as three launches.

## Realtime with Pause

`Mods/RealtimeWithPause` is source only: an unfinished example with no dll, so it does nothing until you build it with
`dotnet build -c Release` in its `src/`. Its per-character timers have never been run in the game. It makes fights run in real time. It is also the second worked example of a code
plugin: where `ExamplePlugin` stays on the stable API, this one shows what Harmony reaches, and its `src/` is free to copy
for any purpose. It keeps turns: cooldowns, effects,
procs, fields and traps all count them. But nobody shares a turn: every enemy and minion takes its own on a timer of its
own, spread over the second, and yours pass at about one a second. A skill with a 5-turn cooldown lasts about 5 seconds.

- **In a fight**, nobody waits for anybody. What you press becomes an order, which runs as soon as you're ready: once a
  tick, while the bar at the top fills. If you give none, a tick passes as a Wait. Enemies and minions walk and swing the
  whole time, during your own actions too. Exploring stays turn-based, and the
  clock stops when the game calls the fight over (nothing has seen you for 8 turns).
- **Pause** with P: nothing changes, and you can look around and give orders. They run in order when you resume, one per
  tick. What's already swinging finishes first, which the screen shows as PAUSING.
- A new step replaces a step already waiting, and at most 6 orders wait. An order that can't be done any more (its target
  died, the skill isn't ready, you were moved) is dropped with a line in the log, and costs nothing.
- Skills marked instant still don't take a turn: they go at once, or first thing on resume.
- The game still ignores input while your own action plays, as it always has; enemies acting doesn't block you.

Mod Settings: `Enabled`, `TickSeconds` (how long a turn is for everyone, default 1), `PauseKey`,
`PauseWhileAiming` and `PauseInMenus` (both on: the clock waits while you aim or have a menu open). The system menu,
dialogs and vendors always hold the clock.

`SCCL_DEVTEST=realtime,realtime2` plays all of this in a real fight, as two launches.

## Limits

New abilities are assembled from the actions, effects and procs above. Anything they can't express can usually be
written as a [script](#scripts-lua); new targeting shapes, new AI and new UI need C# code, written as a
[code plugin](#code-plugins).
