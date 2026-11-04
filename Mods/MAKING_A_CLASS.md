# Make your own mod and class

This guide walks you through making a Breachwork mod with a class of your own, from an empty folder to a class you
can pick on the **Select Your Skills** screen. You don't need to write code: a class is one text file that describes
its skills, and the Breachwork Loader turns it into real abilities, tooltips and all.

By the end you'll have built the **Rivet Driver**, a small melee class with three skills:

- **Hot Rivets** (passive): your melee hits sometimes leave a rivet in the enemy, which makes it easier to hit.
- **Set Rivet**: a strike that drives two rivets at once.
- **Shear Line**: tears every rivet out at once. The more rivets, the harder it hits, and a riveted enemy is stunned.

Every file in this guide was loaded and played in game before it was written down, so you can copy them as they are.

`Mods/README.md` is the full reference for everything the loader understands. This guide links into it whenever
there's more to say.

## What you need

- *StarCrawlers Chimera* with Breachwork installed (the `Mods` folder and `BreachworkModManager.exe` sit in the game
  folder).
- A text editor. Anything works; one that checks JSON for you (VS Code, Notepad++, Sublime Text) saves you a lot of
  hunting for missing commas.

## How a mod is put together

```
StarCrawlers Chimera/
  Mods/
    Library/              the shared library: common effects (#Burning, #Shocked...) and skill templates
    MyFirstMod/           your mod
      mod.json            who you are and what you need
      rivet_driver.json   one class
      rivet_driver.png    its icon (optional)
  BepInEx/LogOutput.log   where the loader writes what went wrong
```

- **A mod** is a folder under `Mods/` with a `mod.json` in it.
- **A class** is a `.json` file in that folder. A mod can hold as many classes as you like, one file each.
- The loader reads every mod when the game **starts**. After you change a file, restart the game to see it.

Two files the loader writes for you are worth knowing:

- `Mods/vanilla_reference.txt` lists every vanilla skill tree, its abilities, the stat names, the damage types and the
  item ids. It is rewritten each time the game starts. Open it whenever you need an id.
- `BepInEx/LogOutput.log` is where errors go: an unknown stat, a missing icon, a mistyped effect. When something
  doesn't show up, look here first.

## Step 1: the mod folder

Make a folder `Mods/MyFirstMod/` and put this `mod.json` in it:

```json
{
  "id": "MyFirstMod",
  "name": "My First Mod",
  "author": "you",
  "version": "1.0.0",
  "description": "The Rivet Driver, the class built in the modding guide.",
  "requires": ["Library"]
}
```

- `id` is your mod's name to the loader. Keep it short, with no spaces, and don't change it once people use your mod.
- `requires` lists mods that must load first. We'll use the shared `Library`'s skill templates, so we need it.

Start the game and open **Options > Mods**. *My First Mod* should be in the list. It holds nothing yet, but if it says
why it can't load, fix that before going on.

## Step 2: the smallest class that loads

Make `Mods/MyFirstMod/rivet_driver.json`:

```json
{
  "id": "MOD_RivetDriver",
  "name": "Rivet Driver",
  "description": "A hull fitter with a pneumatic rivet gun. Favors [STR] and [TGH].",
  "archetype": "Muscle",
  "iconFrom": "SKILLTREE_Smashmaster",
  "attributePriorities": { "Strength": 4, "Toughness": 2 },
  "starterItems": ["WEAPON_M_Carbonite_Baton"],

  "tiers": [
    {
      "attributes": { "Strength": 1 },
      "newAbility": {
        "base": "Passive",
        "name": "Hot Rivets",
        "icon": "SKILLTREE_Smashmaster/0",
        "description": "Your strikes leave a rivet behind."
      }
    }
  ]
}
```

What each line does:

| Field | Meaning |
|---|---|
| `id` | The class's permanent name. It is written into save files, so **never rename it** after you've played with it. Start it with `MOD_` so it can't clash with a vanilla tree. |
| `name`, `description` | What players see. `[STR] [TGH] [AGL] [REC] [SAV] [SMT] [PSI] [HP] [ARM]` become stat icons. |
| `archetype` | Which column it sits in: `Muscle`, `Guile` or `Intellect`. |
| `iconFrom` | Borrow a vanilla tree's icon until you have your own (step 6). |
| `attributePriorities` | Which stats the class preview says it wants. |
| `starterItems` | Item ids from `vanilla_reference.txt` (or `"OtherMod:ItemId"` for a mod's item). |
| `tiers` | The skills, in the order they unlock. Tier 0 is yours from the start. A class can have up to 9. |

Inside a tier:

- `attributes` are the stat points the player gets for taking that tier.
- `newAbility` is the skill. `"base": "Passive"` starts it from the Library's passive template, so we only write what
  is different. The templates are `Bolt`, `Shot`, `Strike`, `Smash`, `Nova`, `Blast`, `Blink`, `Buff` and `Passive`. The active
  ones already have sensible targeting, costs, visuals and sounds; `Passive` only makes the skill a passive, since a
  passive is never cast.
- `icon` can be a PNG next to the json, or `"SKILLTREE_Tree/N"` for a vanilla ability's icon.

Start the game, make a new character and look in the **Muscle** column. The Rivet Driver is there. Its passive does
nothing yet; that's next.

## Step 3: an effect of your own

Rivets are something that stays on an enemy, so they are an **effect**. Add an `effects` list to the class, next to
`tiers`:

```json
"effects": [
  {
    "id": "Riveted",
    "name": "Riveted",
    "description": "Rivets pin its plating together, so it can't twist away from a blow.",
    "duration": 6,
    "debuff": true,
    "maxStacks": 5,
    "stats": { "Dodge": -3 }
  }
],
```

- `duration` is in turns.
- `maxStacks: 5` lets it stack up to five times. An effect's `stats` count once per stack, so five rivets mean -15 Dodge.
- `debuff: true` marks it as bad for the bearer (it shows red, and effects that clear debuffs remove it).

You refer to your own effects as `"@Riveted"`. The Library's shared ones are `"#Burning"`, `"#Shocked"`, `"#Marked"`
and so on, and vanilla effects use their id from `vanilla_reference.txt`.

## Step 4: make the passive do something

Skills work by **actions** (what happens) and **procs** (when something happens on its own). Give Hot Rivets a proc:

```json
"newAbility": {
  "base": "Passive",
  "name": "Hot Rivets",
  "equipped": "melee",
  "icon": "SKILLTREE_Smashmaster/0",
  "description": "Your strikes leave a rivet behind.",
  "procs": [
    {
      "on": "hit",
      "chance": 30,
      "actions": [ { "do": "effect", "effect": "@Riveted" } ]
    }
  ]
}
```

- `"on": "hit"` fires whenever you hit something. Other triggers include `kill`, `hurt`, `dodge` and `turn`.
- `"chance": 30` is a 30% chance each time.
- `"equipped": "melee"` means it only works with a melee weapon in hand. A rivet gun pressed against plating is a
  melee thing, so a rifle shot shouldn't drive rivets.

You never write the tooltip's rules text. The loader reads your actions and writes "30% when you hit: Applies Riveted to
the target" for you, so the numbers can't drift out of date. `description` is only the flavour line.

## Step 5: an attack, and a payoff

Add two more tiers after the first one:

```json
{
  "attributes": { "Strength": 1, "Toughness": 1 },
  "newAbility": {
    "base": "Strike",
    "name": "Set Rivet",
    "equipped": "melee",
    "icon": "SKILLTREE_Smashmaster/2",
    "description": "Drive two rivets through the plate in one blow.",
    "actions": [
      { "do": "damage", "type": "Crushing", "min": 2, "max": 4, "weapon": 0.5, "scale": "Strength", "scaleMult": 0.3, "perRank": 1 },
      { "do": "effect", "effect": "@Riveted", "stacks": 2 }
    ]
  }
},
{
  "attributes": { "Strength": 2 },
  "newAbility": {
    "base": "Smash",
    "name": "Shear Line",
    "equipped": "melee",
    "icon": "SKILLTREE_Smashmaster/4",
    "cooldown": 6,
    "staminaCost": 15,
    "description": "Tear every rivet out of the plate at once.",
    "actions": [
      { "do": "stun", "turns": 1, "ifTargetHas": "@Riveted" },
      { "do": "damage", "type": "Crushing", "min": 3, "max": 5, "scale": "Strength", "scaleMult": 0.4, "perRank": 1,
        "bonusPerStack": 3, "stacksOf": "@Riveted", "stacksFrom": "target", "consumeStacks": true }
    ]
  }
}
```

The damage fields, which you'll use in almost every class:

| Field | Meaning |
|---|---|
| `type` | A damage type: `Slashing Crushing Piercing Ballistic Fire Cryo Energy Void Psi Meta Bio Shock`. |
| `min`, `max` | The base roll. |
| `weapon` | Adds that share of a roll of your weapon's damage, and counts as a weapon hit (your weapon's own on-hit effects go off). |
| `scale` + `scaleMult` | Adds a stat times the multiplier. `"scale": "Strength", "scaleMult": 0.3` adds 3 at 10 Strength. |
| `perRank` | Extra damage for each rank of the skill. |
| `bonusPerStack` + `stacksOf` | Extra damage per stack of an effect. `stacksFrom` says whose stacks (`target` or `self`), and `consumeStacks` spends them. |

Two things in Shear Line are worth seeing:

- **Order matters.** The stun comes first, while the target still has its rivets. If the damage went first, it would
  spend the rivets and the stun's `ifTargetHas` would find none.
- **The class now has a loop.** Hot Rivets and Set Rivet build Riveted, Shear Line cashes it in. A class is more fun
  when its skills feed each other like this than when each one is a separate button.

Skills cost **stamina** (`staminaCost`) and have a **cooldown** in turns. Stamina is the game's own resource; reach for a
new resource bar only when the class's theme can't work without one (see "Resource and combo mechanics" in the
reference).

## Step 6: perks

When a skill is perked (perk points are rare), its `perk` block changes it. Since a player only gets a few, a perk
should change how the skill plays, not just add a number. Add one to each active skill:

```json
"perk": {
  "name": "Pneumatic Feed",
  "description": "The gun cycles the next rivet before your arm comes back.",
  "instant": true
}
```

```json
"perk": {
  "name": "Popped Heads",
  "description": "Plating that falls apart frees the gun for the next one.",
  "resetOnKill": true
}
```

- `instant: true`: Set Rivet no longer ends your turn, so you can rivet and then act again.
- `resetOnKill: true`: a kill with Shear Line makes it ready again, so a riveted room can be torn through in a chain.

Perks can also add actions (`addActions`) or triggers that only exist once perked (`addProcs`). See "Brand-new
abilities" in the reference for every perk field.

## The finished class

Here is the whole file. It is exactly what was played in game for this guide.

```json
{
  "id": "MOD_RivetDriver",
  "name": "Rivet Driver",
  "description": "A hull fitter with a pneumatic rivet gun. Rivets pin an enemy's plating in place, and a Shear Line tears every one of them out at once. Favors [STR] and [TGH].",
  "archetype": "Muscle",
  "iconFrom": "SKILLTREE_Smashmaster",
  "attributePriorities": { "Strength": 4, "Toughness": 2 },
  "starterItems": ["WEAPON_M_Carbonite_Baton"],

  "effects": [
    {
      "id": "Riveted",
      "name": "Riveted",
      "description": "Rivets pin its plating together, so it can't twist away from a blow.",
      "duration": 6,
      "debuff": true,
      "maxStacks": 5,
      "stats": { "Dodge": -3 }
    }
  ],

  "tiers": [
    {
      "attributes": { "Strength": 1 },
      "newAbility": {
        "base": "Passive",
        "name": "Hot Rivets",
        "equipped": "melee",
        "icon": "SKILLTREE_Smashmaster/0",
        "description": "Your strikes leave a rivet behind.",
        "procs": [
          {
            "on": "hit",
            "chance": 30,
            "actions": [ { "do": "effect", "effect": "@Riveted" } ]
          }
        ]
      }
    },
    {
      "attributes": { "Strength": 1, "Toughness": 1 },
      "newAbility": {
        "base": "Strike",
        "name": "Set Rivet",
        "equipped": "melee",
        "icon": "SKILLTREE_Smashmaster/2",
        "description": "Drive two rivets through the plate in one blow.",
        "actions": [
          { "do": "damage", "type": "Crushing", "min": 2, "max": 4, "weapon": 0.5, "scale": "Strength", "scaleMult": 0.3, "perRank": 1 },
          { "do": "effect", "effect": "@Riveted", "stacks": 2 }
        ],
        "perk": {
          "name": "Pneumatic Feed",
          "description": "The gun cycles the next rivet before your arm comes back.",
          "instant": true
        }
      }
    },
    {
      "attributes": { "Strength": 2 },
      "newAbility": {
        "base": "Smash",
        "name": "Shear Line",
        "equipped": "melee",
        "icon": "SKILLTREE_Smashmaster/4",
        "cooldown": 6,
        "staminaCost": 15,
        "description": "Tear every rivet out of the plate at once.",
        "actions": [
          { "do": "stun", "turns": 1, "ifTargetHas": "@Riveted" },
          { "do": "damage", "type": "Crushing", "min": 3, "max": 5, "scale": "Strength", "scaleMult": 0.4, "perRank": 1,
            "bonusPerStack": 3, "stacksOf": "@Riveted", "stacksFrom": "target", "consumeStacks": true }
        ],
        "perk": {
          "name": "Popped Heads",
          "description": "Plating that falls apart frees the gun for the next one.",
          "resetOnKill": true
        }
      }
    }
  ]
}
```

## Step 7: icons of your own

Borrowed icons are fine while you build. When you want your own:

- **Class icon**: a square PNG (about 256 x 256) next to the json, named in `"icon": "rivet_driver.png"`. Keep
  `iconFrom` as well: it is used if the PNG goes missing.
- **Skill icons**: the same, in each `newAbility`'s `"icon"`, e.g. `"icon": "rivet_driver_t1.png"`.

`Mods/ExampleClasses/` shows the usual naming (`arc_gunner.png`, `arc_gunner_t0.png` ... `_t5.png`).

## Step 8: testing and fixing

1. Restart the game after every change. Mods are only read at start.
2. Make a test character with your class and play a room or two. Read every tooltip: the generated rules text shows
   exactly what the loader understood.
3. If the class is missing or a skill does nothing, open `BepInEx/LogOutput.log` and search for your class id or file
   name.

Common problems:

| What you see | Usually |
|---|---|
| The mod says it can't load | `mod.json` isn't valid JSON, its `id` is taken by another mod, or something in `requires` is off. **Options > Mods** shows which. |
| The class isn't on the creation screen | The class file isn't valid JSON (a missing or extra comma, a missing quote), or `archetype` is misspelled. The log names the file. |
| "unknown stat" / "unknown resist" in the log | A stat or damage type name is misspelled. Copy names from the top of `vanilla_reference.txt`. |
| An effect never shows up | `"@Name"` must match the effect's `id`, not its `name`. Library effects use `#`, yours use `@`. |
| A skill is locked on the hotbar | Its `equipped` rule doesn't match your gear (e.g. `"melee"` with a gun in hand). |
| The creation screen won't let you pick two trees together | A class can't be picked alongside a vanilla tree it borrows abilities from. Classes built only from `newAbility` (like this one) never hit this. |

The reference's examples use `//` notes to explain fields. Leave those out of your own files.

## Step 9: from three skills to a full class

The Rivet Driver is a sketch. A full class has up to **9 tiers**, and players usually finish a class's tiers by about
level 9, then spend later levels on their other classes. If a skill's damage should grow with the character, give the
damage action `"levelCap": 9` so it ramps up to full by then.

Some things that make a class feel good to play:

- **Each skill should bend a rule of the fight.** "Deal 5 damage" is a button. "Stun anything that's riveted",
  "a kill makes it ready again", "doesn't end your turn" change what the player does next.
- **Skills should feed each other.** Build something up with some skills and spend it with others, like Riveted here.
- **Effects follow from the theme.** A rivet pins plating, so it lowers Dodge; it wouldn't heal you. If you can't say
  why a skill does what it does, the player won't be able to either.
- **Flavour text describes, it doesn't instruct.** Write what happens in the world ("Tear every rivet out of the plate
  at once."), not what the player should do. The rules text is generated for you.
- **Read finished classes.** `Mods/ExampleClasses/arc_gunner.json` is a complete nine-skill class built the same way as
  this one. `Mods/CustomClasses/`, `Mods/UniqueClasses/` and `Mods/ArendethClasses/` hold dozens more to learn from.

When you want more than damage and effects, the reference covers it:

| You want | Look up in `Mods/README.md` |
|---|---|
| Areas, lines and cones | "Brand-new abilities" (`radius`, `shape`) |
| Summons, traps, chains, knockback, teleports | "Brand-new abilities" (the action list) |
| A resource bar or mana pool | "Resource and combo mechanics" |
| Stances, combos, counters, "if X then Y" | "Conditions, counters and triggers" |
| Your own sounds | "Custom sounds" |
| Your own particle effects or 3D models | "Custom particle effects", "Custom models and textures" |
| Items, crafting recipes, enemies | "Mods: manifest, items and tweaks" |
| Changing someone else's class | "Classes in mods" (patching) |
| Writing the class as XML instead of JSON | "XML class files" |
| Logic the data can't express | "Scripts (Lua)", then "Code plugins" |

## Step 10: sharing your mod

- **Zip the folder** (`MyFirstMod/` with everything in it). Players install it with **Install from .zip / folder** in
  `BreachworkModManager.exe`, or by unpacking it into `Mods/`.
- **Bump `version`** in `mod.json` each time you release, and keep the class `id`s the same so saves keep working.
- **Your mod is yours.** If you started from one of the bundled classes, the Breachwork license (`Mods/LICENSE.md`) asks
  you to credit "Arendeth" and "Breachwork, a mod pack for StarCrawlers Chimera" and to add something of your own. Don't
  copy anything from an `audio_licensed/` folder into your mod; point `"sound"` at your own files or at the game's own
  `event:/...` sounds instead.
- Don't ship files from the game itself (its art, sounds or `vanilla_reference.txt`). Your mod refers to them by id, and
  every player already has them.
