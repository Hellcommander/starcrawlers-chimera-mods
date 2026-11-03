# Breachwork changelog

The version is the loader's version, and every bundled mod follows it. The numbers 1.2 and 1.3 were never used: the
release after 1.1.0 is 1.4.0, and the one after that is 1.45.0. 1.45.2 was never packed: its changes are in
1.46.0. The release after 1.46.0 is 2.0.0. The dates are the dates of the work.

## 2.0.0 (unreleased)

### Skills and interface

- **Toxic Breath** (Mutagenic Brew) now leaves a Toxic Cloud over its whole cone for three turns: anyone in it or walking
  in is poisoned and choking. Fields can follow a skill's cone or line with `"overShape": true`.
- Right-clicking a class's icon on the skills screen lists the skills your synergies give that class; picking one starts
  it.
- 54 new synergies (`Mods/Synergies/synergies_k.json`) reach the 140 class skills none touched before, Bounty Hand and
  Spark Tinker's whole kits among them. Each is a new pair of classes, and passive skills count once you have them.
- With the event bus on, a perk that adds a proc now works from the moment you buy it, not from your next turn.
- Allies and summons no longer path onto a live trap you can see: they go round or wait. A companion's disarm now pays
  the experience yours would.
- The example classes grew to nine skills each: Arc Gunner, Cadence Fencer, Bounty Hand and Spark Tinker.
- `SpecialButtonsBesideCapstone` (`[Interface]`, off by default): when the ninth column has no room, one ninth-tier skill
  shrinks to a quarter of its area and the ChimeraLinq and Vendordamus Beacon buttons share its cell at that size.
- Realtime with Pause has an on-screen pause button.
- A cast that spends a whole counter now spends what it read, so a kill during that cast can add to the counter.
- XML mods: `of` on a condition reads correctly again.

### New class

- **Comet Breaker** (`Mods/CometBreaker/`): a Muscle class of comet magic scaling with Strength and Psi Power. Comet Charge
  takes you to an enemy or a tile, you can't lose health until your next turn, and everything around your landing flies
  back, every boss but Cerberus included; what hits a wall takes more damage, is stunned for 2 turns and can't use its
  specials for a turn after it recovers. Ephemeris marks a spot where a comet falls two turns later, and landing a
  charge beside the mark brings it down at once. Split Nucleus makes two copies of you that fight with your gear and
  skills for reduced damage and weaker status effects. Requires the immortal fix (`PlayerImmortalTag` under `[Fixes]`,
  on by default): with it off, its charges don't protect you. Spellsmith's forms all work on its charges, and together
  the two classes make Comet Breakup: under Nova Form your attack goes out all eight ways, you one way and seven
  Fragments of You the rest.
- Comet Breaker has five more synergies. Grapnel Acrobat, Gravity Assist: Comet Charge on a hooked enemy may tear the
  hook out and sling you on, the charge ready again and your turn not over. Bulkhead Psion, Ejecta Plating: a plate may
  rise two tiles behind what your landing strikes, before it flies. Mimeticist, Cast-Off Shape: a shape ending in a
  fight may fight on as a Fragment of You. Snareseer, Foretold Fall: a trap catching something may shift your Ephemeris
  mark onto it, and landing sets off your traps within 2.6 before the blast throws anything. Phase Drifter, Late
  Arrival: charging while Phased spends it, and the rest of you lands on the same spot a turn later.
- Comet Breaker now has its own particles and models. Comet Charge and Extinction Arc show you as a figure of frozen rock
  flying head-first inside the comet, Ephemeris marks the deck with a turning orbit chart and drops a burning nucleus
  through the ceiling, and impacts, Tidal Pull, Split Nucleus and Outgassing have their own effects. Fragments shed ice
  as they fight.

### New boss

- **Corrupted Mirror**: side areas and the Ichor Deep can now hold bosses (`[SideAreas] BossChance`, 15% by default, 30%
  in the Deep). This one is optional: a cracked black mirror stands in a far room, marked on the minimap, and the fight
  only starts if you click it and choose to touch the glass. Out comes a corrupted copy of you with your gear and stats
  and four times your health, your skills shuffled and all dealing Void, one of them from a class you don't have. It
  hits as hard as you do, then loses 50% of every hit after everything else is worked out. Your minions, summons and
  crew come out with it as corrupted copies with more health and the same cut, and they fall when it does. If your party
  would hit too hard, their damage is cut further and they get more health, armour, dodge, block and resistance instead.
  It drops an epic and corrupted copies of two of your items, stronger, with their bonuses moved around. Item skills
  become new corrupted versions, and corrupted mods with effects of their own replace some bonuses. All of them grow
  with your best stat. Some change casting itself: Bitter Clarity lets nothing go on cooldown for a price in health,
  Steady Void makes your next two skills take no turn, and an Afterimage strikes with your next attack. Unseen turns
  your mod attacks into traps, and the new Wrong Hand mod can reshape them; vanilla skills get an effect of their own
  instead.

### New mods

- **Status Synergies** (`Mods/StatusSynergies/`): synergies built on a status instead of a pair of classes. A class that
  exploits Burning, Gashed, Toxified and the like works with any other class that causes them, and a new class that
  inflicts the status joins by itself. They cap at level 3, fire on a 2-turn cooldown and spend the status they read, so
  they trade one effect for another. Turn the mod off, or any one of them under Mod Content.
- **Threading** (`Mods/Threading/`), off by default: for very large fights. It turns on the event bus, moves enemy paths
  to worker threads and stops head-turning work on off-screen models. Turn it off and the game runs as it always did.
- **Enemy weapons** (loader config, `[EnemyWeapons]`), off by default: the weapon that falls from a dead enemy's hand
  now stays where it lands, and you can pick it up and use it. Every one drops, with no roll. It keeps the enemy's own
  model and its kind: a guard's pistol drops as a pistol, a ChefBot's cleaver as a cleaver. Weapons the base game kept
  fixed to the body fall too, as does one the enemy carried but wasn't holding. Mugs, briefcases and claws stay junk,
  and other loot still comes from the inventory everyone carries. Enemies run their weapons in a low-power mode: they
  hit at 75% of floor loot, but a gun needs no ammo and your skills cost 25% less stamina while you swing a melee one
  (`LowPowerDamage` and `MeleeStaminaSaving` in the config). The weapon deals the damage types of the attack the enemy
  made with it, and a boss's is Epic or better. With the event bus on, the first drop also gives a **Scrap Bot Kit**.
  The bot grinds loose weapons into scrap, rebuilds broken props with it, repairs itself and sells extra scrap when you
  use a vendor. It tracks weapons and props on worker threads, including off-screen ones. It has high armor, and its
  minion menu can scrap weapons from your pack. Boss weapons are never scrapped unless you allow it.

### Minions

- Right-clicking a pet's button on the minion strip now opens its orders menu, the same as right-clicking it in the
  world. Break down moved into that menu.
- New order in that menu, **Swap places**: you and the companion trade tiles, for your turn.
- Charmed minions have **Release** in place of Unsummon, in that menu and on the strip: the charm lifts and it goes
  back to its own side, alive where it stands, and frees its minion slot.

### Class changes

- **Perks:** Perk points are rare, so every mod class's skill perks now change how the skill plays. The perks that were
  only a number (damage x1.25, cooldown -3, area +1) are gone: they now add triggers, ground fields, status payoffs,
  throws and chains, or make a skill instant or ready again on a kill.
- **Looking-Glass Rogue:** Reworked around surreal magic; the grins and the Grinning Cat stay, the rest is new. **Sentence
  First** (was Snicker-Snack): the blow lands now and a verdict falls three turns later, and a sentenced enemy that
  strikes you pays on the spot. **One Side, Other Side** (was Drink Me, Eat Me): size is a loan, taken from an enemy or
  handed over until it is jammed in place. **The Looking-Glass** (was Painted Guard): a copy of you at the opposite
  size; use it again to trade places and sizes. **Down the Rabbit Hole** (was Hedgehog Croquet): an enemy falls out of
  the fight for two turns and lands hard elsewhere, Painted Red adding to it. **Six Impossible Things** (was Mad Tea
  Party): spend Tea Clock hours on skills that take no time and leave no cooldown. As the cat, **No-Name Wood** (was
  Warp Pounce) makes enemies forget whose side they are on. New as the cat: **May Look at a King** hits bosses and
  elites hardest, **No Body to Behead** leaves only your head until your next turn, and **Slowly, Tail First** fades you
  out and bites when you're gone.
- Six skills that didn't do what they say are fixed. **Wild Tangent:** every skip now gets the Spillover bonus, not just
  the first. **Unmaking Gaze:** closing the eye clears its count at once, so the next look starts over. **Grin Without a
  Cat:** the grin stays where you stood and enemies near it turn on it. **Recall Protocol:** recalling your last drone
  refunds stamina. **Remote Detonator:** charged enemies are now knocked back. **Prism Charge:** a Zenith Hoarlight now
  works when no other Hoarlight is left.
- **Spellsmith** works faster and holds less. Forge stances last 10 turns instead of 40 and come back after 1 instead of
  2. Loads and stoked or white-hot attacks stay ready for 2 turns instead of 4. Cooldowns: Bury the Charge 2, Kindled
  Shell 3, Loose Spell 4, Masterwork 6 (from 3, 5, 6 and 9). What the loads leave behind is shorter too: buried charges
  last 5 turns, Kindled Shells 3, Loose Spells 4 (from 8, 4 and 6).
- **Trade Bodies:** with no body standing, Conjoined now trades places with a fallen body's corpse while that corpse
  lasts, and you stay in your own body.
- Procs that could never fire on an effect being gained now work, in Shadow Magus's synergies, Gun Kata and the
  Looking-Glass Rogue.
- Novas (Spellsmith's Nova Form, Wildcaster's Bursting Out of You, Flux Savant's Full Discharge) now go out all eight
  ways from where the skill went off and land on the tiles they reach, so they show with nobody in range. All eight
  impacts play, past the limit of three copies of a particle, and tile-sized ground goes down on every other way, four
  at most.
- A skill that moves you keeps its aim under a nova. You go the way you aimed, once, and the other seven leave from the
  tile you left. Before, most dashes turned into a cast on yourself and never moved you.
- A skill that involves your weapon goes off through it under a nova, for one shot's ammo. A gun fires its own shots
  down all eight ways, and a melee weapon spins through the eight tiles beside you, which the camera pulls out to show.
- A throw's description no longer says "1 tiles" or ends in two periods.
- The Corrupted Mirror's copies and other copies of you hold your weapon properly. Before, a podling gun or other bulky
  weapon showed up tiny and pointing straight up, and a body that carries its own weapon on its back held yours there.

### Interface

- **Aiming past your companions:** While you aim a skill, your minions, pets and crew no longer get in the way: the mouse
  reaches the enemy, tile or object behind them. Buffs still land on them, and holding the Force key lets any skill aim
  at one. `AimThroughMinions` under `[Orders]` turns it off.
- Your effect icons get a fold tab beside the top of their columns. Folded, it reads `+ Effects` with your buffs counted
  in green and your debuffs in red.
- The Mods button on the title menu now waits for the menu to be ready instead of trying once, so it no longer goes
  missing.

### Performance

- **Event bus:** `EventBus` under `[Performance]`, off by default, on with the Threading mod and applied at once. Game
  events skip characters and synergies with nothing listening, the HUD refreshes only when what it shows changes, and
  particles, status looks, damage numbers, hits, items and the combat log are spread over frames or capped. These change
  the look and the timing of the work, never the damage or what a skill does.
- `GraphicsJobs` and `IncrementalGC` under `[Performance]` work without the bus and apply the next time the game starts.
- `AssetStreaming` under `[Performance]`, off by default: every floor's textures sit in video
  memory from the first menu on, about 7 GB. With this on, those nothing on your floor uses leave, and they come back
  from disk when something needs them; on a floor 6 test save that went from 7,093 MB to 2,724 MB. The event bus's
  workers decide what goes, whether or not `EventBus` is on. UI art never does, and neither do textures listed in `AssetStreamingNeverUnload`.

### Modding

- Status synergies: `"status"` names the statuses, `"trees"` the class that exploits them, and `"sources"` adds trees
  that can't be read from class files, such as vanilla ones.
- Plugin API level 9: `Context.Subscribe` and `Api.Publish` use the event bus, and `Api.EventBusOn` says if it's on. A
  plugin should work with the bus off.
- `carryToBody` lands an effect on the body you step into with Trade Bodies.
- The Realtime with Pause example has a pause button at the top of the screen.
- Side area bosses: `"sideAreas": { "bosses": [...] }` in any mod, with an enemy or `"creature": "self"`, their own
  skills, loot and log lines, and an optional `challenge` prop that starts the fight only when the player chooses it.
  Kinds can set `bossChance`. `Mods/ExampleEnemies/enemies.json` shows one.
- `"target": "point"` aims a skill at an enemy or a tile, `castActions` run on the caster as a skill goes off, and
  `dashTrail` lays a particle along a move.
- Effects can be `invulnerable`: no health is lost from any source while they're on. On the player this needs
  `PlayerImmortalTag`.
- Summons can be `"creature": "self"`: copies of the player with `strength`, `healthShare` and `look`.
- `exhaust` takes `afterStun`, because cooldowns tick while stunned, and `throw` takes `bosses` and `fromCenter`.
- Traps take `fuseOnly` and `fall`, and `springTraps` takes `near`.
- Rewrites take `"add": { "mirrors": 7 }` on a nova: copies take the other ways round you.
- Novas reach as many tiles along the diagonals as straight on; a circle stopped one short of the corners.
- Perks take `addProcs`, `instant`, `resetOnKill` and `undodgeable`, and `"when": { "perk": true }` on a skill's own
  actions swaps what they do. The old number fields still work.
- `"castRules"` on an effect changes what using any skill means while it's on you: `freeTurn`, `noCooldown`, `healthCost`,
  `repeat`, `consume` and `when`.
- New conditions: `cast` (`vanilla`, `mod`, `item`, `attack` and more), `inSide`, `healthLost`, `buffs`, `debuffs`,
  `skillsCooling`, `cost`, `cooldown` and `floor`.
- `"forAllies"` on a skill keeps your companions solid for a buff, `"fallAs"` on a trap brings a creature's model down
  from overhead, and `"aura"` on an effect loops a mod particle on anyone but you.
- A `dot` and `hotScale` can add the applier's stats to each tick, and a summon's `scaleStat` takes `|` for the highest
  stat when it's summoned.
- `"corruptedGear"` in `sideAreas` adds corrupted skills and mods and sets how many an item takes.
- The repository now holds the built Threading, Example Plugin and Character Portrait Tools dlls, so a fresh clone gets
  working plugins without building them.
- More of what base-game skills do can now be built in class files:
  - `"on": "block"` procs fire when a hit on you or your summon is blocked, aimed at the attacker.
  - Effects take `tickActions` (with `tickRadius`), run each turn with the applier as the caster, and `statsFrom`, a
    bearer stat worked out from the applier's.
  - `"stacksOf": "value:NAME"` counts any condition value, such as `value:enemiesNear:4` or `value:Toughness/2`.
  - `cast` fires another skill by name for free, and `order` has your summons carry out actions themselves.
  - Damage takes `"type": "weakest"`, the type the target resists least, and `areaFalloff`, less damage further from
    the centre.
  - `cooldowns` takes `skill` to move one skill's cooldown, `shorten` takes turns off effects or adds them, and `xp`
    gives experience the way a won floor event does.
  - `preventDeathWhen` saves the bearer only while a condition holds.
  - `ammo` refunds or spends loaded rounds, and `anchor` and `rewind` mark a tile and pull you back to it, with your
    health if you like.
  - Effects take `dealtWith` and `takenFrom` to limit their damage changes to melee, ranged or some damage types, and
    `redirectTo` to send a bodyguard share to one of your summons.
  - An effect's tooltip now says when part of each hit goes to someone else.
  - `corpses` uses the bodies lying near you, and the `corpses:R` value counts them.
  - `loot` drops items the game rolls, by type and rarity.
  - Summons take `hostile` to arrive against you, and effects take `hazardProof` against floor hazards.
  - `cooldownWhile` holds a skill's cooldown while you carry an effect, for stances and shields.
  - `breakOnHurt` ends an effect when its bearer is hurt, and `onBreakActions` run as it breaks.
  - `unlock` opens the locked doors and containers around you, by the Hacker's rules.
  - `disarm` disarms the traps around you and salvages their parts, and `steal` takes items from vendors nearby.
  - Effects take `offhandPower` to raise what your off-hand weapon deals.
  - Hit filters also take `attack`, `skill`, `mainhand` and `offhand`.
  - `tune` adds a lasting stat or weapon damage to a piece of gear, with a limit per item.
  - Skills aimed at items take `itemKind` and `itemRarity`.
- Rules text no longer ends a nested list with two full stops, and wall conditions read as a sentence ("the target has
  at least 1 wall beside it").
- Conjoined's bodies are a `"bodies"` block any class can use, with `joinTiers` and a `strain` effect.
- `ignoresAllies` now spares your companions vanilla debuffs too, not only mod effects.
- `preventDeath` and `invulnerable` effects now work on enemies and creature summons, which ignored them before.

### Vanilla bug fixes

These came out of stress testing the event bus. None needs the bus, and `[Fixes]` in the config turns each one off.

- When an effect on a character ended, the next one in the list was skipped: it missed that turn's countdown, or sat out
  that hit. Some effects now run out one turn sooner than before.
- An enemy's Warp Jump no longer throws an error if its caster or target is destroyed while it travels.
- Fire on the floor no longer throws an error when its burn sets off a reaction like Flashpoint. The reaction now goes
  off just after the hazard finishes its tiles, so the foe takes the hazard's hit first.
- Your health now respects the game's IMMORTAL tag, so damage over time can't kill you in the cutscene after beating
  Cerberus.
- An event that spawns a mob no longer stops with an error when no tile near it is free.
- Levelling past 100 no longer breaks the level-up.
- Finding an item by name no longer fails on a destroyed item left in your bag's list.
- Aiming at a character with no skill selected no longer throws an error.
- The Ready effect of Arm Cannon and Thunderclap no longer throws an error on someone without the skill.
- An enemy under fire answers an attacker and calls for help once a turn instead of on every hit, because the game's
  "already called" flag is never set.
- `ZoneLeftovers` puts the existing clean-up of old floors from memory on a switch.

## 1.46.0 (2026-11-01)

### Retraining

- From level 20 you can spend ChimeraLinq **User Score** and credits to undo a build choice, outside combat, on the
  skills screen. Ctrl+right-click the highest skill you hold in a class to give it up and get its skill point back, for
  50 User Score and 100 credits per character level.
- Ctrl+right-click a class icon to swap the whole class for another, for 200 User Score and 300 credits per character
  level. Its skill points and perk points come back, and the new class starts at its first tier with no starter gear.
  Rogue Modder, Gadgeteer and Conjoined can't be swapped out.

### Class changes

- **Spellsmith's Ember Lance** now works on your other classes' attacks: it stokes your next one, which hits a quarter
  harder and sets its target Burning. Its perk, White Ember, no longer adds damage to the lance: the stoked attack
  ignores armor, shields and resistances instead.
- **Spellsmith's Loose Spell** has a 2% chance, each time it goes off on someone, to also call down one of the
  Wildcaster's boss echoes. Loose magic isn't picky, so an echo can land on you too.
- **Axiomancer** is rebuilt around a shield pool. First Principles is a new skill: your Smarts and a little Psi Power
  make a small shield pool. It needs no shield in hand, recharges every turn and gains Shield Regen with Smarts, Psi
  Power and class rank, so a hybrid build can survive a swarm. It recharges faster with two or more enemies beside you,
  when you Wait with Quiet Mind, and with each note Refutation spends. Insight and Lucidity now add to the pool instead
  of Psi Power. Circular Reasoning and the penalty for repeating a skill are gone.
- **Axiomancer's skills** no longer treat enemies as someone to debate:
  - **Cold Read** no longer deals damage. It takes notes, gives Insight, and may Silence, Expose or Weaken, with better
    odds from Smarts.
  - **Refutation** is now a passive: when your shield takes a hit from a studied enemy, a note is spent to turn the blow
    back as Psi damage, recharge 2 shield and give Insight, up to 3 times a turn.
  - **Cognitive Shell** now spends Insight on overshield and shield recharge, and gives 3 turns of Piercing and Psi
    resistance.
  - **Excluded Middle** replaces Chain of Inference: enemies around you with a Case Study take Psi damage, and those
    without one get a Case Study.
  - **Axiom of Separation** replaces Living Hypothesis: everything within 2 tiles is thrown back, studied enemies are
    held in place, and your shield recharges.
  - **Grand Synthesis** now turns notes into damage and Insight into shield recharge.
  - The Silence from **Cold Read** and **Grand Synthesis** now works on bosses for 1 turn, then not again for 3 turns.
  - **Insight** now holds one more stack for every 5 Smarts above 16, up to 12.

### Modding

- `statsFrom` on a passive gives one point of a stat for every `per` points of another. `from` can add stats, `rankPer`
  adds class rank, and `start` handles stats where lower is better, such as `ShieldRegen`.
- `"do": "recharge"` fills the regular shield pool, with no shield in hand needed.
- The `absorb` trigger fires when your shield takes a hit, even one that never reaches your health.
- Silence takes `bossTurns`: a boss is silenced for that many turns, then can't be silenced again for 3 turns.
- `[Retraining]` in the config sets the prices, the level and which classes can't be swapped out. A mod class that
  changes gear for good belongs on that list.

## 1.45.1 (2026-11-01)

### License

- Stray license texts are gone, so the pack carries one license.
- **License version 3.1:** `Mods/LICENSE.md` is now the full license, word for word, instead of a shorter text of its
  own. The sound section now says how the sounds are made and shipped, and that `audio_licensed/` sounds may not be used
  with AI tools or training, as their packs' terms already require.

### Mods

- Out-of-date descriptions on the Mods page are fixed: Arendeth Classes no longer gives an old class count, Shadow
  Magus describes its caster Shade and hunting Thing, Skill Synergies describes class interactions instead of a bonus,
  and Flux Savant mentions its runes running flat.

## 1.45.0 (2026-11-01)

Covers everything since the 1.4.0 upload.

### New class

- **Shadow Magus:** a Guile caster of the dark, scaling with Recognition and Psi Power. Benighted dims enemies' aim,
  crits build Darkvision and chain or finish Benighted targets, Lose Them scatters a room blind, a Shade of yourself
  hangs back, casts and pools dark that enemies lose you in, Swallowed shrinks an enemy into the dark, and The Thing in
  the Dark whistles up a Horror of your own that hunts from a distance, with its own model and animations. The Shade and
  The Thing are ranged casters with seven spells each, and they scale with both Recognition and Psi Power, which also
  add to their health.
- While a Shadow Magus is Unlit or a Nightwalker, the Cursed Horror and a Wildcaster's wild Horror can't hunt it and turn
  on whoever else is nearest.
- Six synergies come with the class:
  - **Nothing Watching** (Cursed): the Mark and the dark feed each other, and the Horror is called onto Marked enemies
    and stays a turn longer per level.
  - **Something Loose in the Dark** (Wildcaster): wild spells shake loose from the dark, Shrunk and Swallowed enemies go
    Glassy, and kills in the dark leave you Unbound.
  - **Where the Ichor Pools** (Ichorborn): tar and shadow cling together, and Frenzy leaves you Unlit.
  - **Many Shadows** (Conjoined): every body deepens the dark.
  - **Shapes in the Dark** (Mimeticist): prey in the dark is Tagged, and the Panther's Unseen goes Unlit.
  - **Dark Between the Plates** (Gloomspore Warden): rot and dark feed each other, and Gloomcaps sprout in Shadowflame.
- Snuff pays for the class's synergies itself, at 2% less damage per level, in place of Synergy Strain.

### Class changes

- **Entropy Weaver's Spillnodes** were remade to match their description. They were solid spinning objects, and now
  they're see-through swarms of thread, static and code scraps, with their own moving sprites.
- Spillnodes now run their programs only in a fight. Their lash reaches further and takes the nearest foe, fraying it
  and hurting it if it was already Frayed, and they sit idle less often. It shows as a violet line, as does the snap when
  one winks out, and neither goes through walls.
- **Spellsmith's Cold Forge** no longer needs critical health. Attacks from your other classes now count in threes: the
  first adds Working Heat, the second turns it into Quench, and the third comes out cold-forged. That attack deals Cryo
  damage, swaps burns for chills, always applies Chilled and takes 1 turn off your other cooldowns. A ready Quench
  carries over to the next fight.

### Quality of life

- Class search understands more stat names and abbreviations.
- Summons step out behind you instead of standing up in front of the camera, where a skill asks for it.
- A summon's look turns with its body, so it no longer walks sideways staring at you.

### Fixes

- The synergy panel no longer covers a class's description. It widens, shrinks its text or shortens inactive synergies
  to fit, on the creation screen and the body picker.

### Modding

- An effect's `concealment`: enemies more than a tile away lose sight of the bearer that often, rolled once per enemy
  per turn.
- A model's `prefab` puts a vanilla hazard's body on a creature with its scripts switched off. A look without a rig
  still shows strikes, wind-ups and flinches.
- `turnsPerStack`: a synergy level can grow turns, beside odds and stacks.
- `"kind": "hunt"` keeps a summon closing on the nearest enemy, and a summon aimed at an enemy arrives targeting it.
- `summonAt` (`behind` or `beside`) places a summon along your facing.
- A `chain` action's `look` draws a line of that colour along each jump, and `sight` stops it jumping through walls.
- Creature skills' `sound` and a `chain` action's `sound` play mod sound files as well as vanilla events.
- A mod particle id works as a ground field's `look` and keeps playing for as long as the field lasts.
- `scaleStat` takes two stats (`"PsiPower+Recognition"`): the bonus comes from their average, and they also add summon
  health.
- `hunt` takes a `reach`, so a caster stops at range.

## 1.4.0 (2026-10-31)

Covers everything since 1.1.0.

### New classes

- **Bulkhead Psion:** telekinesis that tears up deck plating to raise walls and drive spikes.
- **Fulgurant:** a lightning duelist who zooms around the room, scaling with both weapon damage and Psi Power.
- **Ichorborn:** ichor magic drawn from the final boss. Tarred stacks into Stuck, and Shadow Molt is the capstone.
- **Miasmancer:** chemistry of foul air, mixed mid-fight.
- **Mimeticist:** tag creatures, feed on them and take on what they are.
- **Reactant Hauler:** throws whatever spills out of the station's machinery.
- **Snareseer:** a psychic who leaves thoughts on the deck like mines.
- **Wildcaster:** wild magic that drags in spells, boss echoes, monster attacks and surges. Spell Fever replaces
  Prismatic Ward: for five turns every attack you aim draws a new delivery, gain, loss and element. A rare Horror can
  also turn up, hunting either side.

### Skill reshaping

- Passives, stances, one-shot charges and socketed runes can rewrite other skills: delivery (trap, grenade, walking
  bomb, drifter, nova, line, cone, area, chain, scatter), element, damage type, effects and behaviors. Tooltips and
  aiming follow the rewrite, and the skill keeps its own scaling.
- **Spellsmith** (example class): now a spellshaper that rewrites attacks from your other classes, with its own
  icons, effects, sounds, buried charge and Kindled Shell. Hammer Form now fires an attack through your weapon, animation, effects and ammo included:
  the attack keeps its own damage and takes the weapon's reach, accuracy and on-hit effects.
- **Rune matrix augments:** rune casters spend a shared pool on delivery and behavior changes, and on flaws that give
  points back. Flux Savant opts in, its flaws bite through rune burn, sealed sockets and Instability, and they only
  discount while they really cost something. When the pool shrinks, skills go dark and keep their picks.

### Character pictures

- **Custom character pictures.** Put a png or jpg in the game's Portraits folder, any size, and it is cut to a square.
  Pick it on the creation screen or above the character sheet, or press Load picture to choose one from anywhere on
  your computer. A save keeps only the file name, and each Conjoined body has its own picture.
- **Character Portrait Tools** (new mod, off by default). Adds Make Portrait to the picture picker, which makes a
  picture from your classes, skills, gear, effects and form. The `Tool` setting picks what makes it:
  - `GameRender` (the default) needs no AI and no download. The game renders your class's crawler body in one frame from
    its 3D models, in a ready pose with your weapons, shield, stowed weapon, headgear, armour colour and tentacle arms,
    and the room you're in blurred behind. It needs a running game, not the creation screen, and glasses, masks and
    similar are left out for now.
  - `PortraitMaker`, an optional download, draws a stylised picture from code with no GPU or AI.
  - The AI asset toolkit paints one on your own NVIDIA GPU, and any other tool works through a `portrait_tool.json`.

### Resources and balance

- **Stamina replaces most class resources.** Signal, Genome Samples, Vapour, Ichor, Salts, Brine Cores, Flux Charge and
  Coldsink are gone. Saves that held them still load, and skills priced in them fall back to stamina.
- Pools that stayed can grow with your stats (`capScale`).
- Flux runes hold a charge: a spent rune stays socketed and does nothing until recharged with stamina. Bench Instincts
  opens the rune matrix in combat to charge or discharge runes.
- **Every synergy is now a class interaction.** Flat stats, resists and plain damage, heals and shields are gone. The
  two classes act on each other instead: marks spread, one class's resource pays for the other's effect, attackers get
  marked, specials are set back and foes are shoved, stunned or silenced. Level raises a synergy's odds.
- Classes without synergies got their own set. Synergy Strain and per-skill penalties weigh on what synergies add.
- **Spiked Ichor** (Ichorborn + Chemistry): a chem in your blood poisons Tarred foes, and their corpses breed Ichorlings.
- **Black Culture** (Ichorborn + Xenobiologist) now sets the two broods on each other: Ichorlings slime, the pet slime
  tars, and a foe dropped carrying both marks breeds an Ichorling.
- Free deaths are a boss's move. Only Looking-Glass keeps one. Scrapper, Demolitionist, Null Chorister and Probability
  Shaman now trigger their last stands on a low-health line.

### Quality of life

- **Class search:** a box on the creation screen narrows the class grid by class name, skill, description, synergy or
  stat bonus, so "crit" or "recognition" finds the classes that offer it. Hovering a result says where it matched.
- **Saved builds:** right-clicking the custom preset button saves the classes you've already picked instead of wiping them.
- **Hotbar pages:** the second page keys are now Ctrl+Page Up and Ctrl+Page Down (`PageKey2Combo`), which frees the plain
  keys for movement.
- **Diagonal steps:** numpad 7, 9, 1 and 3 now work with Num Lock off too, not just on.
- The Synergies view lists only the synergies you have and what they cost you. The creation screen still shows what a
  tree pairs with.

### Modding

- **Code plugins:** mods can carry C# in `plugins/*.dll`, which only load once the player allows that mod's code.
- **Lua scripts:** `scripts/*.lua` run sandboxed on MoonSharp and need no OK.
- Plugin API levels 2 to 4: `AddPortraitButton`, `CurrentForm`, `PortraitFolder`, `CurrentPortrait`, `SetPortrait`,
  `SimultaneousTurns` and `RenderPortrait`.
- **Portrait tools:** any program can make portraits. A folder with a `portrait_tool.json` says how to run it, and it gets
  a job file with the character's classes, gear and effects. The Portrait Maker's source is an example, free for any use.
- **Realtime with Pause** (source only). An unfinished example plugin that makes fights run in real time with a pause
  key. It ships with no dll and does nothing until you build it, and its per-character timers have never run in game.
- Rewrite fields `form`, `add`, `remove`, `others`, `attacks`, `roll` and numeric `pierce`.
- `procChancePerStack`, `synergyPenalty`, `augments`, `runeCharge`, `runeDischarge` and `thirdPerson`.
- Particle layers can `borrow` another system's material, and aliases can use `without`, `use` and `ground`.
- **Boss Looks** (new mod): repoints look-alike effects at the installed game's own particles instead of copying them.
  Particle names can point into an enemy prefab.

### Fixes

- A summon carrying a spell keeps it through a save and load.
- Cancelling a new saved build with Esc no longer hides the Begin button.
- The bar behind a nine-tier row no longer runs out through the skills panel.
- Skill tooltips no longer show a stale range or cost after a rewrite.
- Attacks with their damage inside random outcomes count for "others" rewrites.
- Ichor pools are back on the floor, and rift summons arrive through the rift.

### Sound

- Sounds that came back as one signal in both channels were re-rendered in true stereo, and many others were replaced.
- **The pack's audio now lives in encrypted archives,** to better follow the terms of the audio assets, some of which
  require sounds to be embedded and not shared as loose files. The game plays them as before.
- Sounds can ship sealed in a mod's `sounds.pak`, as PCM under AES-256 (`--flac` for a half-size lossless archive that
  takes FMOD longer to open). Every archive has its own key, wrapped with the
  loader's public key, so any mod can have embedded audio: `BreachworkModManager.exe --seal-audio Mods\YourMod` (or
  `_modding/audio/sfx_archive.py`). No `"sound"` value changes, and a loose file still wins over the pak. Archived sounds
  are read, decrypted and decoded while the game starts, so a play does no file work. Every sound and the (encrypted)
  index carry an authentication tag, so a changed archive is refused rather than played as noise; the header names the
  loader key it was sealed for, so the loader can carry more than one key after a rotation (`sfx_archive.py keygen
  --rotate`); `sfx_archive.py verify` checks an archive; `"soundsPreload": false` in a mod's `mod.json` reads its archive
  on demand instead of at start.
- The archives are ready about 0.2 s after the preload starts, down from about 1.4 s: the loader decrypts and checks
  them with the processor's AES instructions through Windows CNG (after a self-test against the managed code, which
  stays as the fallback) on several threads, those threads hand each sound to FMOD themselves instead of waiting for
  game frames, and the entries are PCM, so FMOD has nothing to decode. The sounds are the same bits; only the speed
  changed. `SCCL_DEVTEST=audiofast` now reports each stage's time.
- A sound that is missing or won't load now plays a vanilla sound, so nothing goes silent. Mods can add their own
  fallbacks with `soundFallbacks`.
- **Lower sound delay:** `[Audio] MixBlock` in Mod Settings sets how quickly every sound in the game reaches your
  speakers. The default here is `512`, half the game's block size. Use `1024` if you hear crackling, or `game` to leave
  the game's own setting alone. It applies after a restart.
- Mod audio left the source repository. The packages still ship it, and a build from a plain clone uses the fallbacks.
- Every licensed sound was rebuilt from its pack clips with plain signal processing and no AI, since those packs'
  licenses bar AI tools. The 63 `sfx/` renders that had such a pack among their references were rebuilt the same
  way: 35 from their Noise Alchemy clips alone, staying in `sfx/`; 28 whose main clip is an Ovani, Tao & Sound or
  Function Loops clip moved to their mods' `audio_licensed/` folders (same file names; the class files point there).
- The rebuilt sounds use an anti-aliased pitch move, keep each clip's colour and attack, and sit at the clip's loudness;
  `vasculist_spike` is synthesized from material parameters, a sound with no clip behind it at all.
- **Sounds from packs that bar AI are rebuilt by a custom programmatic tool.** A listener works out which clips to layer and what tweaks each sound needs, and sends that advice to the tool, which
  compiles the sound by plain signal processing. No AI generates or changes these sounds. The listener is a read-only audio model: it only measures the tries and picks the
  best one, nothing it produces ships, and no pack clip goes into it. 
- Not every sound takes that route. Noise Alchemy and Dark Fantasy allow AI in production, so where neither has a
  good-quality clip that fits, the audio workbench still makes a base sound from their references, changed and layered
  from the original sounds. Those `sfx/` sounds can be Stable Audio renders, and Stable Audio stays in the credits.

### Credits and license

- Soulash 2 and MoonSharp are credited, here and in the roll after Cerberus. The nova and the other delivery forms are
  Arendeth's own.
- The credits now say where each sound comes from: Noise Alchemy for the `sfx/` sounds, and Function Loops, Tao & Sound
  and Ovani Sound for the `audio_licensed/` sounds. The CLAP model is credited as the listening-only quality tuner.
- Odds Breaker and Swarm Conductor are no longer credited as inspired by the first StarCrawlers.
- **License version 3.0: one license for the code and the content, and commercial use is now allowed** with credit. It
  replaces PolyForm Noncommercial 1.0.0 and the non-commercial content terms. Copies already shared keep the terms they
  came with.
- The project may be used with AI tools and training.
- No sound may be sold as a sound or in an audio pack, even altered, and `audio_licensed/` sounds may not be used
  commercially at all. Game material never ships.
- The source of the example plugins is free for any use.

### Source repository

- The GitHub repository was remade to remove access to the old audio files, which its commit history still held. The
  sounds now ship only in the encrypted archives. The repository restarts from a single commit, so this changelog is
  now the record of the earlier work. A clone builds with `-p:PlayerBuild=true`.

## 1.1.0 (2026-10-29)

Covers everything from 2026-10-23 to this release, which was the first with versioned packages.

### The pack

- **Renamed to Breachwork.** The loader is the Breachwork Loader and the manager is `BreachworkModManager.exe`. An older
  `ChimeraModManager.exe` is removed on update.
- Packages are named by version, and every license mention now points at one place.

### New classes and big reworks

- **New:** Axiomancer, Kelvin Magus and Looking-Glass Rogue.
- **Every class** gained two new tiers, and Conjoined went from 5 to 7.
- **Tidewright** is pressure hydromancy now, not coolant and ice: Brine Lash, Brine Shackle, Sea Fret, Jetwash, Gyre and
  Floodgate.
- **Rimelight Seer:** Hoarlights hit and run, with Starbind taming, Phase Swap and Galeward.
- **Gloomspore Warden:** Sporeward and new perks, and Gloomcaps scale with Psi Power.
- **Swarm Conductor:** builds its own drones without waiting, and Lattice Foundry prints Lattice Drones.
- **Looking-Glass Rogue:** reworked around the Grinning Cat.
- **Reaction Architect:** more triggers, perk reactions and a ninth tier, Exit Clause.
- **Flux Savant:** runes wear out and refill from your bag.
- **Phase Drifter:** scales with Agility plus the higher of Smarts or Psi Power.
- **Astral and void summons** scale with Psi Power.
- **Rogue Modder rebalance** (optional, on by default): mods per item come with rank, later mods fail more often, and a
  failure takes the same amount off. Item (Re)Designer is no longer a free reroll.

### New systems

- **After the ending:** credits roll in game, then you choose to leave or stay and descend into the Ichor Deep, an
  endless side area that grows harder and richer.
- **Generated side areas:** new places with their own layouts and art, more kinds, anomalies and searchable containers.
  Breaches stay open, so you can return. Leaving asks first.
- **Crew:** hireable crawlers with gear, wages and orders, on a crew screen. Off by default.
- **Companion orders:** right-click orders and jobs, including Use, Unlock, Bash and fetching for your minions.
- **Interlopers:** new enemies with their own skills, models and animations, in side areas only.
- **Wearable gear:** what you give crew shows on their bodies.
- **Real crafting window:** a recipe list with have-counts and Craft x5 on F10 or a Craft tab. It replaces the
  vendor-screen Fabricator.
- **Mods page and mod manager:** turn mods on or off in game or from the desktop manager, set the load order, install
  from a .zip or folder and see which saves use a class.
- **Mod Settings page:** every setting in game, plus a Mod Content tree to turn off single classes, items and synergies.
- **Particle effects:** every active class skill has its own, from the new Skill Effects mod.
- **Real art:** items got icons and floor models, tier icons replaced the placeholders, and summons got their own models.
- **Licensed sound effects** for Kinetics, Flux Savant and shared moves.
- **Many more synergies,** with a Synergies view on the character screen.

### Quality of life

- Dash camera: dashes, leaps and warps play out in third person, and some skills turn you into a creature mid-move.
- Text too long for its box shrinks instead of being cut off.
- Idle pets, summons and crew join fights.
- Class items only drop when someone in the party can use them.
- Allies avoid live traps you can see, and crew and Conjoined bodies try to disarm them.
- An Own stash page is tied to the save and takes unstashable items.
- Character names can be longer and keep the case you type.
- Tooltips say where a stack stops, and long ones stay on screen.
- Hover tips, wider key binding and interface look settings.
- Hotbar rows 2 and 3 show their modifier.
- ChimeraLinq and Vendordamus Beacon move to free spots on the skills screen.
- Trade Bodies also swaps the two bodies' positions.
- The Feedback button asks if it's a mod bug and can open a filled-in GitHub issue.

### Vanilla bug fixes

- **Rogue Modder:** War Crafted shields average Shield Regen and get a lost Shield Max back. Item skills can't rework
  shared-stash items, which leaked gear to other characters.
- **Perks that didn't match their tooltips:** Iron Harvest, Avast Ye!, Pyramid Scheme, Nanite Replication, Mech Part,
  Unobtanium Chrome Job, Tempt Fate, Bastion, Lucky Find and Old Faithful.
- **Chemistry:** Try Anything Once now lowers addiction for every chem, Weirdest Science no longer cuts chems short or
  doubles the whole dose, and chems fed to the pet slime count as yours.
- **End game:** minions come back after the cutscene, the Apartment portal works again, draw distance resets, and the
  Cerberus fight no longer piles up hazards, adds and beam particles.
- Bosses count as elites for perks that reward elites.
- Health Regen above 7 heals more per turn.
- A skill error no longer freezes the turn.
- Combo Builder keeps its state through a save and load.
- Demented and Doomed can roll their full 5 charges.
- The minimap survives loading a game while in one.

### Save safety

- A save with trees, tiers or perks that are no longer installed gets those points back, and it still opens.
- Summons keep their name, team and health through a save and load, and no longer come back hostile after merging into
  a host.

### Modding

- Data-defined creatures with roles, skills and sizes in metres.
- `behaviors` (kite, retreat, guard, hitAndRun), `shape` (line, cone) and `tame`, `warp`, `show`, `phase`, `leap` and
  `weapon` damage shares.
- Gear requirements, weapon scaling, persistent traps and more proc triggers.
- Effect fields `root`, `reflect`, `ownerShare`, `immune` and `immuneToAllies`.
- Items take a `model`, and custom particle effects are defined in data.
- `runeWear`, `rules`, `scaleStat`, `chanceScale` and `dashAs`.
- "Kill a foe that has X" conditions now work, and item skills stay usable in transformations.

## 1.0.0 (2026-10-23)

First version of the loader and the pack.

- The BepInEx class loader, with classes, items, enemies, factions and synergies written as JSON or XML.
- Arendeth Classes, Custom Classes, Unique Classes, Kinetics, Conjoined, Flux Savant, Odds Breaker, Reaction Architect
  and Swarm Conductor.
- Breaches and echoes, factions, and synergies.
- Hotbar pages, stash tabs, the Graveyard, diagonal steps, shared tiles, saved builds, trap and lockpick odds, and
  clearer combat phases.
