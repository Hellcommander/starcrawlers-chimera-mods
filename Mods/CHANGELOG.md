# Breachwork changelog

The version is the loader's version, and every bundled mod follows it. The numbers 1.2 and 1.3 were never used: the
release after 1.1.0 is 1.4.0, and the one after that is 1.45.0. 1.45.2 was never packed: its changes are in
1.46.0. The release after 1.46.0 is 2.0.0. The dates are the dates of the work.

## 2.2.1 (unreleased)

### Class changes

- **Reactant Hauler, Runeforger and Flux Savant use their items from level 1.** Each class's first skill now takes
  any of the items it carries, so you can mix from the start:
  - The Reactant Hauler's **Hurl Reactant** throws whichever reactant you've loaded it with: oil, ember, coolant,
    brine or grit, each leaving its own mark, so two throws of different reactants set off a reaction. It keeps
    Scavenger's Eye's knack for finding reactants on bodies and getting wind back from reactions. Perk: Good Arm
    (the throw bursts over everyone beside the target).
  - The Runeforger's **Flick Glyph** snaps whichever dormant glyph you've loaded and flicks its element at them:
    kinetic shoves, thermic burns, cryic chills, voltaic shocks. It keeps Chisel Habit's glyph and cell finds. Perk:
    Split Sigil (half the glyph finds a second enemy).
  - The Flux Savant starts with **Arc Lance**, two of its rune sockets open from the start (the third at rank 6), so
    your starting runes go straight into it. Bench Instincts moves to the second tier.
- Characters made before this keep their ranks, so a Flux Savant who had only Bench Instincts now has Arc Lance.

### Modding

- **Loaded skills** (`"loads"` on a skill): one skill that can use any of several items and does what the item does.
  Right-click the skill on the hotbar to pick the item; each cast spends it and is retuned by it with rune fields
  (element, reagent, actions, fields, form...). It falls back to the next item you carry when your pick runs out, and
  the tooltip lists what each item does. `SCCL_DEVTEST=loads` tests it.

## 2.2.0 (2026-11-06)

### Status reactions (chain reactions between statuses)

- **Reactions** in the Status Synergies mod: a synergy with `"status"` and `"with"` belongs to two statuses, not to a
  class. Any class that causes the first and any other class that causes the second make it, and it goes off the
  moment the second status lands on an enemy carrying the first, whichever came first. Eight ship: Shatter Point
  (Burning + Chilled leaves the enemy Brittle and Reeling), Flashover (Burning + Toxified bursts fire onto the enemies
  within two tiles), Conduction (Shocked + Chilled: the shock leaps to the nearest other chilled enemy and the ice
  shatters as it goes), Arc Flash (Burning + Shocked: a blinding flash leaves everyone within two tiles Reeling),
  Convulsion (Shocked + Gashed: the gash tears two stacks wider), Black Ice (Chilled + Reeling: a turn stunned),
  Septic Wound (Toxified + Gashed: five turns Septic) and Overload (Shocked + Marked: the shock jumps to every other
  marked enemy within six). Every reaction spends both statuses, fires at 30% plus 5% a level and at most three
  times a turn. Brittle and Septic are new shared statuses (`#Brittle`, `#Septic`) from the mod's own small library.
- **Chain reactions**: Flashover, Conduction and Overload spread a status with a chain, and the status they land is an
  inflict of yours, so the same reaction can go off on the enemy it reached, up to three links deep. A row of chilled
  enemies passes a shock all the way down; a marked squad overloads together. Each reaction shows its name over the
  enemy and in the log, with a flash of its own.

### Reactions, in five classes

The two reaction systems (a class's alchemical `"reactions"`, and the Reaction Architect's armed reactions) grow new
options, and five classes grow into them.

- **Alchemical reactions**: `"rank"` on a reaction keeps it unknown until the class reaches that rank (the rune matrix
  and effect tooltips show it greyed until then), `"keep"` names reagents the reaction leaves alone (a catalyst: it
  stays on, or lands anyway), and `"chance"` with `"chancePerRank"` makes the contact a roll. A class effect that is a
  reagent lists its reactions in its tooltip. A class with reactions but no rune sockets gets the rune matrix as a
  "Reactions" tab, listing what it knows.
- **Armed reactions**: six new triggers: `buff` (an enemy buffs itself or another enemy; `"to": "victim"` reaches the
  buffed one), `heal`, `summon` (the newcomer is the victim), `allyDown` (an enemy downs one of your summons or pets),
  `move` (any step within `range`) and `turn` (an enemy within `range` begins its turn). New options on a reaction:
  `when` (a condition on the enemy; one that fails spends nothing), `radius` (the answer hits the enemies beside it),
  `cooldown` (turns it rests) and `stack` with `maxCharges` (arming it again adds charges). A passive's proc can arm
  (a wire strung on `combatStart`), `{ "do": "rearm" }` puts charges and turns back, the `reaction` proc trigger takes
  a `match`, and the condition values `armed` and `charges` read the slots. The readout and the rules text know the
  new words. `SCCL_DEVTEST=reactions` plays all of it, with a fixture class for the options.
- **Reaction Architect**: every fight opens with a Welcome Mat already strung across the approaches (Standing Orders),
  Tripwire stacks to four charges when strung again, Interdict also arms Veto (strips an enemy's buff and jolts the
  one that gave it), Retort Protocol also arms Clawback (an enemy on file that heals is jolted and the healed one
  bleeds the mending), Fail-Safe blasts everyone beside the attacker back, Full Cascade gives every armed reaction a
  charge back, Binding Terms arms Uninvited (hits and files anything called into the room) and Contingency Lattice
  strings Reprisal when you fight beside allies (the first enemy to down one is hit hard and filed) and gives a
  second Foresight per reaction while three or more are armed.
- **Reactant Hauler**: seven more mixes, learned by rank: Grease Ice (oil over frost knocks it down, the oil stays),
  Sparkshower (grit in fire sprays sparks over everyone near), Salt Flare (brine in fire blinds the area), Scouring
  Paste (brine and grit leave it Salt-Scoured: armor and dodge down), Cold Short (current through frozen coolant sets
  its specials back), Sludge Fire (fire on the sludge burns it, still stuck). The Oil-Slicked, Coolant-Frosted and
  other marks list their reactions in their tooltips.
- **Miasmancer**: seven reactions on its own salts and clouds, with fire, current, acid and frost from anywhere (your
  weapons, your crew, the enemy's own): Rust Flash (fire on rust salts), Salt Cough (a choking enemy coughs the salts
  over its neighbours), Salt Etch (acid eats through, weakening), Galvanic Rot (current runs through the rust to the
  next one), Choke Point (choking on poison silences, odds growing with rank, poison kept), Toxic Smoke (fire burns the
  poison off into a choking cloud, poison kept) and Frozen Lungs (frost on a choking enemy stuns).
- **Flux Savant**: four reactions on the products of reactions: Shatter (fire on Brittle, true damage in an area),
  Resonance Crack (current on Brittle sets specials back), Cold Weld (frost on Galvanized stuns and keeps it flaking)
  and Thermite Bloom (fire on Galvanized). Four augments (Cryo Trace, Overpressure, Deep Etch, Catalyst Bed) and a
  flaw (Thin Plating), and three signature combos: Spectrum Lance (a Prism rune in Arc Lance rides a second reagent on
  every hit), Residue Flask and Residue Lattice (a Cascade rune in Catalyst Flask or Lattice Field lays residue).
- **Rig Jockey**: five hardpoint reactions, by rank: Wiring Fire, Short Circuit and Cold Shear (fire, current and frost
  on plating you have stripped; the plating stays stripped, frost leaves it Cold-Sheared), Quench (fire then frost) and
  Plasma Arc (fire and current). The three element hardpoints carry a reagent, so a Cryo Jacket or Arc Coil in Vent
  Blast turns the blast's fire into frost or current; Vent Blast gets an element socket at rank 5 with Plasma Vent,
  Cold Vent and Arc Vent, Piston Jab gets Taser Jab and Cold Fist, Hydraulic Charge gets Afterburner and Grapple
  Charge, and Plate Up gets Cold Plates and Overlapping Fields. `SCCL_DEVTEST=rigreact` plays it.

### More recipes for the five classes

- **Fire or Burning:** every elemental reagent in the five classes' recipes now also takes the shared status (Fire or
  Burning, Shock or Shocked, Chill or Chilled), so what other mod classes inflict counts. The effect tooltips and the
  Reactions tab read "Fire or Burning" and show any condition a recipe has.
- **Reactant Hauler**, five more: Mortar Set (oil, grit and brine together set like mortar: Mired and Salt-Scoured),
  Set Hard (frost on the sludge: it stays stuck and its special waits), Live Sludge (current through the sludge runs to
  every oiled or mired enemy in the spill, miring them), Salt in the Wound (a gash on salt-scoured seams bleeds two
  stacks wider and weakens) and Flash Rust (fire in the scoured seams burns straight through and leaves it Exposed).
- **Miasmancer**, five more: Full Compound (the choke landing on an enemy that carries poison and two or more salts:
  silenced, the salts stay, and a cloud of poison and salt hangs over everyone near; salting never sets it off, only the
  choke does, so Grand Compound and Rust Salt Cloud still build the stacks the Saturated Compound perk wants), Frost
  Scale (frost cracks the salt-eaten plating: Exposed), Backdraft (a choking enemy draws the flame in: Burning from
  inside), Rust Blood (a gash on the salts poisons and weakens; the gash is salted shut, the salts stay) and Venom Etch
  (acid on the poison strips a buff and weakens).
- **Flux Savant**, five more on its products: Aether Shatter (fire on residue and Brittle together: the glass bursts
  over two tiles and the aether settles on everyone near), Etched Glass (acid on Brittle adds Galvanized, the glass
  kept), Live Plating (current on Galvanized passes the flaking plating on along a chain), Flash Freeze (frost on
  Brittle: Exposed on top of the glass) and Aether Rot (poison on the residue curdles into two more stacks). Aether
  Residue now reacts with poison too.
- **Rig Jockey**, five more: Meltdown (fire on stripped plating while your rig holds six or more Heat melts the whole
  thing down over everyone beside it; cooler, the same fire is a Wiring Fire), Jammed Works (a slow on stripped
  plating: a plate gets into the gears), Heat Fracture (fire on Cold-Sheared metal splits it, true damage), Fault Arc
  (current down the shear stuns and sets its special back) and Servo Lock (current and frost lock the servos).
- **Reaction Architect**: four alchemical clauses on anything on file: Boxed In (a slow on a profiled enemy: its big
  move waits, Foresight to you), Follow-Through (a stumble: every armed reaction gets a charge back), Open Book (an
  exposed flank or a mark: its buffs are read off and stripped) and Case Closed (a weakened arm: the file closes, your
  tools come back two turns early, two Foresight). The Architect gets the Reactions tab, and Profiled's tooltip lists
  them.

### Interface

- **TooltipOpacity** (`[Interface]`, 10-100%, default 100): the game's own tooltips (items, skills, perks, effects,
  characters) fade their background like the mod's panels, never resized. Text stays solid unless `FadeText` is on,
  and `AllOpacity` leaves them alone.
- **EffectShrink** (`[Interface]`, default on): the more buffs and debuffs you carry, the smaller your effect icons
  get. Past `EffectShrinkFrom` icons in a column (default 8) both columns shrink so the longest stays about that long,
  down to `EffectShrinkMin` (default 50%), and they ease back up as effects wear off. The fold tab keeps its size.

### Fixes

- The fold tab on the effect icons (Effects N) no longer jumps when an effect lands or ends: it was placed from the
  icons' positions in the same frame the game added one, before the layout had moved it, and corrected itself a frame
  later. It now runs the canvas update before it looks. `SCCL_DEVTEST=chainsyn` watches it frame by frame.

- Alchemical reactions are held back past 12 a second as a safety valve. That count now runs on real time, so a paused
  game no longer holds it and keeps the next reactions from going off.

Found by the loader's new player bot, a self-test that plays the game the way a player does (see the dev readme):

- The character creation screen threw once on every open: the portrait slot refreshed inside `AddComponent`, before
  its picture existed.
- With asset streaming on, the sprite sheet behind the game log's stat icons was unloaded after two minutes unused
  (it is no Sprite object, so the keep-list missed it), and TextMeshPro then threw on every frame the log changed.
  Sprite sheets now stay.
- Body glow fades (`MeshFade`) threw on floor 2: Mono's `ConditionalWeakTable` handed back the wrong type after a
  collection, and a glow whose colour list had shrunk indexed past it. A plain pruned dictionary and a bounds check.

### Modding

- Three general blocks for status reactions: `"cascade": N` on any proc lets it answer events set off inside other
  procs' actions (up to three deep; without it procs stay quiet there, as before), `"announce"` on a proc shows words
  over its target and logs them, and `"skipFirst": true` on a `chain` spreads its actions from the target to the enemies
  it reaches without landing on the target again. A chain's `"effect"` filter takes `"A|B"` now. A library effect's
  `icon` is read beside its own file whichever library loaded last. `SCCL_DEVTEST=chainsyn` plays the reactions.
- Alchemical reactions: a need written `"EFFECT_Fire|#Burning"` takes either effect for that place in the mix, and
  `"when"` gates a reaction on a condition (checked on the enemy, with you as the caster). A reaction whose condition
  fails is passed over for the next in order, so two can share a mix and part on it. `SCCL_DEVTEST=recipes` plays every
  new recipe on one character.

## 2.1.1 (2026-11-05)

### Class titles

- **Title archives** (`.bwt`): stored titles have their own compressed format, so titles kept for many class
  combinations take little disk. Each class id is written once, a class set is one number (its place among all sets
  of its size, written as the gap from the one before), a title is numbers for its words, and the whole is deflated
  with a hash that refuses a damaged file. A saved seven-class title takes about 7 bytes instead of about 160 as text
  (a million of them: 6.7 MB against 161 MB). `SCCL_titles.bwt` holds your characters' title choices, and Ollama's
  titles are kept once per class combination rather than once per character (up to 20; asking again adds to them,
  and titles nobody shows any more are dropped). The Class Titler saves to `Mods/YourClassTitles/titles.bwt`, and
  any mod can ship a `*.bwt`. An `SCCL_titles.json` or Titler `titles.json` from 2.1.0 is moved into the archive
  (on the first launch, or the Titler's next save) and deleted once the archive reads back.
- **Wordsmith titles** (`TitleSource = Wordsmith`): one short name fused from every class's identity, naming as many of
  your classes as fits in `WordsmithLength` (32 letters; Short style takes two thirds). Each class lists `traits`,
  `roots` and `heads` ("Spored", "Rune", "-smith"); the wordsmith joins them into compounds ("Gloom-Spored",
  "Runesmith", "Hive-Guard") and a word two classes share names both ("Host" for Conjoined and Splice Host:
  "Sworn Psi-Force Bio-Host"). Pieces only fuse within a class or between classes whose main theme is the same, so
  the compounds mean something. Clicks walk through the next best names, the hover tip says which word came from
  which class, and classes without the words use their adjectives, nouns and themes. Ollama-written class words
  include them too. 38 classes have their own; the rest fall back.
- **The Class Titler uses the wordsmith**: it shows the set's wordsmith title (and its alternatives) with which word
  names which class, and Ollama can write from the classes' identity words. Each of Ollama's titles shows how many of
  your classes it names; one fits when it names enough of them within `WordsmithLength`, and "Reroll until one fits"
  asks again (up to five rolls, no repeats, told which classes the best one missed). A title already saved for the
  set guides the rolls. The class catalog now carries each class's wordsmith words.
- **Axiomancer's title words** follow its shield: Warded, Thought-Shelled, Thought-Bulwark, and themes led by guard.
- Twelve hand-written titles for seven-class sets.

## 2.1.0 (2026-11-04)

### Class titles

- **Class Titles** (`Mods/ClassTitles/`): a title under your name on the character sheet and the creation screen, made
  from which classes you have, never their rank. Each class lists themes for what it does, and the strongest pair of
  themes across all seven picks one of 120 mastery names, so the same set of classes always gets the same title. It
  is display only: your name and saves don't change. `[Titles]` sets the style (`Full`, `Short` or `Epithet`), where
  it comes from, a preview on the creation screen and a renown line once several classes are maxed. Every vanilla and
  Breachwork class brings its own title words.
- `OllamaTitles` (off by default): Ctrl+click your title to have a locally running Ollama write one for your class
  combination. It is saved for that character. `OllamaUrl`, `OllamaModel` and `OllamaKeepAlive` set where it runs,
  which model writes and how soon it leaves video memory.
- **Class Titler**, an optional download (`Breachwork-ClassTitler-<ver>.7z`): run it while the game is closed. It lists
  every class with its icon, description and skills. Pick up to seven, see the title the game gives that set, have
  Ollama write more and save the one you like. Saved titles go to `Mods/YourClassTitles` and win over the made-up title
  for that exact set, in any order. The loader writes the class list it reads to `BepInEx/cache` each launch.

### Schematics and terminals

- **Holo-laptops** on office desks can be searched once for a schematic, three per floor by default and the same ones
  every time you load. Smarts gives a chance at a second (`[Crafting] LaptopSchematics`, `LaptopsPerFloor`,
  `LaptopBonusPerSmarts`).
- **Schematics** (`Mods/Schematics/`): general recipes made from the game's own items, each found on certain floors.
  `RecipeLearning` sets whether a found recipe is learned by that character, shared by all of them, or known from the
  start.
- **Terminals:** CorpSec and Special Projects wall terminals offer more than the security shutdown: download
  schematics, override this floor's locks (Agility; never sealed, secret or boss doors), pull the floor plan
  (Recognition) or requisition ammo, medkits and tools for credits (no roll, cheaper with Savvy). A failed roll trips an
  alarm and guards arrive beside you. Odds come from `[Terminals]` `BaseChance`, `ChancePerStat` and `ChancePerFloor`.

### Movement

- The number pad moves you, Num Lock on or off: 8 forward, 2 back, 4 and 6 strafe (or turn, with `NumpadSideKeys`),
  5 waits, and 7, 9, 1 and 3 step diagonally. The arrow keys and Clear do the same with Num Lock off.
- A diagonal step that's refused says why over your head.
- A quick tap released inside the combo window still moves you.
- `EnemyDiagonals` and `AllyDiagonals` (`[Movement]`, off by default): enemies, or your companions, summons and pets,
  cut corners by your corner rule, and each diagonal costs them 1.41 turns.
- `DiagonalReach` (off by default): anyone diagonally beside you counts as next to you, for melee and reach-1 skills,
  for you, your allies and enemies, and the AI treats it as adjacent.
- `TurnBy45` (off by default): turn 45 degrees at a time. Facing a diagonal, you step and attack along it, and facing
  skills such as Dash, Charge and Backflip follow it.

### Interface

- The saved builds list has a fixed height and scrolls past eight builds, with a search box (build or class names)
  and a sort button (Saved, A-Z, Recent). Replacing a build keeps its place.

### New

- **Adding skills to a class:** `"addTiers"` puts new tiers after any class's own, vanilla or modded, and can fill
  its unfinished `* NYI *` tiers. The game's own tree is never changed, so a character keeps loading with the mod off.
  Plugins use `Context.AddTiers` and scripts `mod.addTiers` (plugin API level 10, `Api.TiersAddedTo`).
- **Auras:** `"auras"` on a skill or an effect: everyone in reach wears an effect, and loses it on stepping out.
- **Blocked abilities:** `"blocks"` on an effect stops the bearer's abilities by damage type, cooldown, melee, ranged
  or name, for you, enemies and summons; one used anyway fizzles and runs the effect's `onBlockedActions`. The
  condition `"uses"` and a `special` reaction's `"ability"` read the same words.
- **Capstone Extras** (off by default): every unfinished `* NYI *` tier in the game is now a skill, 31 in 15 trees.
  - Cybertechnician: Null Spine and four more. Psychic powers near you fizzle, and you grow stronger with every
    psychic enemy close by.
  - Pyromaniac: Burn It Shut drinks in the fire around you when you're nearly down.
  - Bodybuilding: Heave throws an enemy, further while you rage; Slab can't be knocked back and knocks back whoever hits it.
  - Force Psyker: Mountain of Will turns you into a colossus with its own four moves for a few turns.
  - Cyberninja: Thunderstep leaps from enemy to enemy, hitting the unaware harder, and resets on a kill.
  - Engineer: Build the Big One calls up a Boltron foreman that stiffens the allies around it.
  - Big Guns: Walking Fire shoots a nearby enemy each time you step, spending a round.
  - Space Pirate: Grappling Hook drags an enemy to you (the perk swings you to it); Boarding Action leaps in and makes
    your next two skills free turns.
  - Smuggler: Shakedown, Fine Print (credits pay to cheat death), Fistful of Credits, Hired Goon and Price on Their Head.
  - Chemistry: Off-Label Use gives chems strange side effects; Lab-Grade Living makes doses last and sometimes not run out.
  - Security: Tripwire Reflexes snares whoever steps up beside you; Systemic Access opens rooms as you walk in and wires corpses.
  - Bullet Farmer: Bumper Crop reaps rounds back from gun kills; Lead Storm fires one shot for every two rounds loaded.
  - Loot Hunter: Treasure Sense (elites always drop something, every third search has more) and Golden Touch.
  - Rogue Modder: Fusion Core adds the damage type the target resists least; War Rig adds a second charge to your
    next three attacks.
  - Gadgeteer: Widget Spray scatters widgets that each do something different; Certified Safe keeps gadgets from being used up.
- **Framework additions for these:**
  - The `loaded` value is the rounds in your gun.
  - A trigger's `match` takes `|` for either and `*` as a wildcard.
  - `volley` takes `bonusPerStack`.
  - Effects and form kits can borrow vanilla icons.
  - On a `hit` proc, `cast` reads what made the hit, so `attack` hears plain weapon attacks.
  - `ammo` loads plain rounds into an empty gun.
- **Item skills:** mod items can give a skill while worn (`"skill"`, written like a class skill).
- **`transfer` action:** moves your debuffs onto the target, with the turns and stacks they had left. An effect with
  `"transferable": false` can't be moved.
- **Burden Projector** (ExampleItems): a Legendary heavy rifle for floors 4 to 7. Its skill Unburden charges for a turn,
  then fires a real shot that moves every affliction on you into what it hits.
- **Charge-up for player skills:** `"windup": N` makes a skill charge for N turns and go off by itself at the start of
  a later turn, at the same target; a stun breaks it off.
- **Held weapon looks:** a mod weapon's `"held"` model replaces the base weapon's look in your hands, laid along it and
  scaled to it. The Burden Projector now has its own heavy model, icon and shot.
- **Mirror blacklist:** a class with `"mirror": false`, or one named in a mirror boss's `blacklist`, is never copied.
  The Corrupted Mirror fights with a random class you don't have in its place.
- **Taintbearer** (`Mods/Taintbearer/`): an Intellect mage of corruption scaling with Toughness and Psi Power. Seep and
  Fouled Ground seed Taint, which weakens its host and moves on when the host dies. Open the Vessel fills you with
  Taint for stamina, and carried Taint feeds your spells. Pass It On, Spillover and Return to Sender move your
  afflictions, enemy curses included, onto enemies, and Rot Through charges for a turn, then spends a target's Taint in one
  blow. A Corrupted Mirror never copies it.
- **`channel` on a skill's own damage** now really fires your weapon (animation, ammo, a gun's accuracy roll), as the
  `channel` a rewrite adds already did. Before, written in a skill it only added the weapon's on-hit effects.

### Fixes

- Mod effects from consumables run their on-apply actions.
- A held weapon model keeps the old look until the new one is ready.
- Unburden also moves control effects that aren't marked, breaks its wind-up if the Burden Projector is gone, and
  drops a dead target.
- The `item` equipped term picks the hand that holds that weapon.
- A duplicate line in a title file no longer resets the hand-written titles.
- `value:rushed` and `value:cleared` keep their count inside nested actions (`chain`, `cast`, `order`, a rush's own
  impact actions); before, those read 0.
- `clearHazards`, `moveTraps` (and `raiseWall` / `springTraps` written in camelCase) now work on a floor aim with
  nobody standing there.
- A heal, shield, recharge or stamina gain with `"amount": 0` and `bonusPerStack` gives nothing without stacks, even
  when it has rank or stat scaling.
- `rush` runs along the nearer grid axis when its user faces diagonally; before, a turning monster's rush never moved.
- `"switched": false` no longer holds when this skill or the last one has no archetype (items).
- `"hands": "both"` with only an off-hand weapon swings it once at its off-hand share, instead of no weapon.
- The `item` and `skill` hit filters know a worn item's skill as an item's.
- An effect that roots and also grants `FLYING` or `IMMUNE_ROOT` doesn't root its own bearer.
- A stacking damage-over-time effect's tooltip shows its damage for the current stacks, as it deals it.
- Consumable effects shared with your summons (`shareItems`) set themselves up as on you: on-apply actions, group and
  caster.
- A hostile or chaos summon with no caster no longer fails.
- A synergy with several tree specs tries every way of matching your trees, so one spec taking the best tree can't
  leave a later one empty. A misspelt `archetype:` in a synergy no longer hangs its rules text.
- Scattered traps save once per cast, not once per bomblet.
- A trap whose `triggerRadius` is wider than its blast `radius` also catches whoever set it off from the edge; before,
  it went off and hit nobody.
- Summons arrive at full health. Before, the pet scaling's first update set them to level × 8, and an `ownerShare`
  effect's attributes counted only from the next turn.
- Summons avoid tiles with mines or fire when a clear tile is free.
- A summon whose creature comes from a placeholder tier that a mod filled (the Engineer's Boltron) is still found.
- A Volley's `bonusPerStack` adds shots; before, it was ignored.

### Tools

- The dev package's mod manager has a **Sounds** button: it opens a mod's sealed sound archive to list, play, export,
  replace, add and remove sounds, then reseals it. It needs the owner's key file and isn't in the player build.

## 2.0.2 (2026-11-03)

### Class changes

- **Axiomancer's** shield pool refilled faster than any health regen: First Principles gave 1 shield point every
  turn, about 2 with starting Smarts and Psi Power, on top of its Shield Regen. That flat refill is gone, and the
  pool now refills the way health does: Shield Regen starts at 8 turns per point and gets 1 turn faster for every
  12 Smarts and Psi Power combined, about what Toughness does for Health Regen, with no bonus from class rank.
  - Two or more enemies beside you put you **Under Pressure**: their hits no longer restart the refill (every hit
    does otherwise), so the pool keeps its pace in a crowd instead of getting free points.
  - Refutation recharges 1 instead of 2, so a refuted small hit can no longer leave you with more shield than before.
  - Quiet Mind's Wait refills 1 plus 10% of Smarts instead of 2 plus 20%.

### Vanilla bug fixes

- **ShieldRegenPastCap** (`[Fixes]`, on): Shield Regen past every turn now counts, like Health Regen past 7. Every 5
  points past it refill 1 more shield per turn. Before, more did nothing, and a total pushed past it refilled every 8
  turns or never.

### Modding

- An effect's `"steadyShield": true` keeps hits from restarting its bearer's Shield Regen count.

## 2.0.1 (2026-11-03)

### Minions

- Five new orders for crew, pets and summons: **Kite** (keep their distance), **Shield** (stand between you and the
  nearest enemy), **Flank** (go round your target to its far side), **Draw fire** (taunt the enemy that's after you
  onto themselves) and **Scatter** (never stand next to each other or you). The order strip shows its orders in rows
  of seven, the right-click menu in two columns, and the crew screen's buttons in two columns. The new orders have no
  key until you set one.
- Allies on a shared tile no longer flicker into each other: they keep apart while walking on or off it, and the
  one left behind glides back to the middle of its tile instead of standing half on the next one.
- A corpse is hidden while someone stands in it, so two bodies aren't drawn inside each other
  (`[Interface] HideCorpsesUnderfoot`, on). It can still be looted, butchered or used.

### Mods

- The Example Script mod (the Spark Tinker) is off by default, like the Example Plugin. Turn it on in the mod manager.
- Scrap from kills only comes while a Spark Tinker is in the party. It used to pay every party with the mod on.

### Modding

- Scripts: `game.has_class(id)` says whether anyone in the party has a class.
- The Threading mod's source, `Mods/Threading/src/`, is now free to use, copy and change for any purpose, like the
  example plugins. It holds no third-party code.
- The event bus is free to use the same way: its code in the loader (`EventBus.cs`, `Bus*.cs`, `*Lane.cs` and the files
  they need) holds no third-party code. The Threading mod only switches it on.

## 2.0.0 (2026-11-03)

Covers everything since 1.46.0.

### Skills and interface

- **Toxic Breath** (Mutagenic Brew) now leaves a Toxic Cloud over its whole cone for three turns: anyone in it or
  walking in is poisoned and choking. Fields can follow a skill's cone or line with `"overShape": true`.
- Right-clicking a class's icon on the skills screen lists the skills your synergies give that class; picking one starts
  it.
- 54 new synergies (`Mods/Synergies/synergies_k.json`) reach the 140 class skills none touched before, Bounty Hand and
  Spark Tinker's whole kits among them. Each is a new pair of classes, and passive skills count once you have them.
- With the event bus on, a perk that adds a proc now works from the moment you buy it, not from your next turn.
- Allies and summons no longer path onto a live trap you can see: they go round or wait. A companion's disarm now pays
  the experience yours would.
- The example classes grew to nine skills each: Arc Gunner, Cadence Fencer, Bounty Hand and Spark Tinker.
- The Apartment Portal button on the skills screen sits beside the perk point count, so it no longer covers a tree's
  extra tier slot (`PortalBesidePerks`, `[Interface]`). The ChimeraLinq and Vendordamus Beacon buttons take the first
  free spot in that column (`SpecialButtonsInFreeSpots`).
- `SpecialButtonsBesideCapstone` (`[Interface]`, off by default): when the ninth column has no room, one ninth-tier
  skill shrinks to a quarter of its area and the ChimeraLinq and Vendordamus Beacon buttons share its cell at that size.
- Realtime with Pause has an on-screen pause button.
- A cast that spends a whole counter now spends what it read, so a kill during that cast can add to the counter.
- XML mods: `of` on a condition reads correctly again.
- `Mods/MAKING_A_CLASS.md`, a step-by-step guide to making your own mod and class, from an empty folder to a
  three-skill class with its own effect and perks.

### New class

- **Comet Breaker** (`Mods/CometBreaker/`): a Muscle class of comet magic scaling with Strength and Psi Power. Comet
  Charge takes you to an enemy or a tile, you can't lose health until your next turn, and everything around your landing
  flies back, every boss but Cerberus included; what hits a wall takes more damage, is stunned for 2 turns and can't use
  its specials for a turn after it recovers. Ephemeris marks a spot where a comet falls two turns later, and landing a
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
- Comet Breaker now has its own particles and models. Comet Charge and Extinction Arc show you as a figure of frozen
  rock flying head-first inside the comet, Ephemeris marks the deck with a turning orbit chart and drops a burning
  nucleus through the ceiling, and impacts, Tidal Pull, Split Nucleus and Outgassing have their own effects. Fragments
  shed ice as they fight.

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
  now stays where it lands, and you can pick it up and use it. It drops at `DropChance` (30% by default); a boss's
  weapon always drops. At the same odds, an enemy holding one weapon carries a spare of the other kind from the floor's loot
  for the attacks its own can't make: a gunman draws a blade for its pistol-whip, a brawler a sidearm for its shots,
  and puts it away for the next attack its own weapon makes. A spare always drops. A weapon keeps the enemy's own
  model and its kind: a guard's pistol drops as a pistol, a ChefBot's cleaver as a cleaver. Weapons the base game kept
  fixed to the body fall too, as does one the enemy carried but wasn't holding. Mugs, briefcases and claws stay junk,
  and other loot still comes from the inventory everyone carries. Enemies run their weapons in a low-power mode: they
  hit at 75% of floor loot, but a gun needs no ammo and your skills cost 25% less stamina while you swing a melee one
  (`LowPowerDamage` and `MeleeStaminaSaving` in the config). The weapon deals the damage types of the attack the enemy
  made with it, and a boss's weapon is Epic or better. With the event bus on, the first drop also gives a **Scrap Bot Kit**.
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
- **Looking-Glass Rogue:** Reworked around surreal magic; the grins and the Grinning Cat stay, the rest is new.
  **Sentence First** (was Snicker-Snack): the blow lands now and a verdict falls three turns later, and a sentenced
  enemy that strikes you pays on the spot. **One Side, Other Side** (was Drink Me, Eat Me): size is a loan, taken from
  an enemy or handed over until it is jammed in place. **The Looking-Glass** (was Painted Guard): a copy of you at the
  opposite size; use it again to trade places and sizes. **Down the Rabbit Hole** (was Hedgehog Croquet): an enemy falls
  out of the fight for two turns and lands hard elsewhere, Painted Red adding to it. **Six Impossible Things** (was Mad
  Tea Party): spend Tea Clock hours on skills that take no time and leave no cooldown. As the cat, **No-Name Wood** (was
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

- **Aiming past your companions:** While you aim a skill, your minions, pets and crew no longer get in the way: the
  mouse reaches the enemy, tile or object behind them. Buffs still land on them, and holding the Force key lets any
  skill aim at one. `AimThroughMinions` under `[Orders]` turns it off.
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
- `AssetStreaming` under `[Performance]`, off by default: every floor's textures sit in video memory from the first menu
  on, about 7 GB. With this on, those nothing on your floor uses leave, and they come back from disk when something
  needs them; on a floor 6 test save that went from 7,093 MB to 2,724 MB. The event bus's workers decide what goes,
  whether or not `EventBus` is on. UI art never does, and neither do textures listed in `AssetStreamingNeverUnload`.

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
- `"castRules"` on an effect changes what using any skill means while it's on you: `freeTurn`, `noCooldown`,
  `healthCost`, `repeat`, `consume` and `when`.
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
  - Effects take `keepItem` for a chance that a used consumable isn't spent, and hit filters take `item` for grenades
    and other consumables (which `skill` no longer counts).
  - Tree specs (`ifSkill`, conditions, `rank:`, synergy trees) take `archetype:Muscle`, `archetype:Guile` or
    `archetype:Intellect` for any other class of that archetype.
  - `rush` runs you the way you face (or back, or sideways) until a wall, door or body stops you, hits an enemy you run
    into, and `value:rushed` scales that hit by the tiles covered.
  - Conditions take `unaware` (the target hasn't noticed anyone) and `facing` (`you` or `away`), and `forget` makes an
    enemy lose track of you, for ambushes and stealth.
  - Effects take `grantTags` to count as having game tags while they last, such as `BULLET_DODGER` to dodge ranged
    attacks.
  - Ammo: skills take `ammoMult` for shots that spend more rounds, effects take `ammoSave` for a chance a round isn't
    spent, and an `ammo` trigger fires per shot with `value:rounds`.
  - Effects take `hazardRules`, so the floor's fire, acid and other hazards spread more or hurt your enemies more
    while you carry them, whoever laid them, and `clearHazards` puts hazards out, with `value:cleared` for what follows.
  - Summons take `chaos` to arrive on the chaos side, fighting you and your enemies alike.
  - Skills take `cooldownOn` to cool down by a trigger, such as kills, instead of by turns.
  - Consumables: effects take `itemTurns` (what your consumables put on you lasts longer), `shareItems` (it goes on
    your summons too) and `keepCost` (a skill's item cost may not be spent). A `craft` trigger fires when you fabricate
    an item, `giveItem` takes `"$item"` for more of the item behind the trigger or the skill, and `salvage` takes
    `whole` to break down a whole stack.
  - `dealtWith` takes `"summons"`, so an effect on you makes your summons and other allies hit harder instead of you.
  - A `smash` trigger fires when a prop breaks near you, and its radius procs hit the enemies around the wreck.
  - Effects take `hidden` to stay off the buff bar.
  - The `useItem` trigger aims at whoever the item was used on, such as the enemy a grenade landed on, and no longer
    fires when a crafting skill (Homebrew, Break It Down) uses up an item as material.
  - Damage actions take `"hands": "both"` to land once with each weapon you hold.
  - A `volley` action fires several shots at a target, and the shots left when it falls find the nearest enemy.
  - Traps take `scatter` to strew several bomblets over random tiles around the aim.
  - `moveTraps` can pull every trap of yours or only those within `near`, and onto a floor skill's aim point. A trap
    that lands on someone goes off there.
  - Effects take `trance` to sit you down while your turns pass on their own, `endWhen` to end once a condition
    holds, and `ramp` to make their per-turn healing, stamina or damage grow each turn.
  - An `attempt` action holds a target for several tries with rising odds, paid in stamina, then runs its actions
    if one lands or its `else` actions if none does. With `together`, a skill holds all its targets in one hold that
    costs stamina once a beat.
  - Scattered traps take `trail` to drop along the line from you to the aim.
  - Combo conditions: `cast` takes an archetype (`muscle`, `guile`, `intellect`), `lastCast` reads the skill you used
    before, `switched` holds when the archetype changed, and the `castStreak` value counts skills of one archetype in
    a row.
- Mod skills now carry their class's archetype, so the game's Combology counts them. Using one no longer breaks a
  running combo.
- The `stamina%` condition now reads your real stamina maximum. It read too high before, so Axiomancer's Insight came
  well short of 90% stamina.
- A heal, stamina, shield or recharge action with `"amount": 0` and `bonusPerStack` now gives nothing without stacks,
  as documented, instead of 1. Gloomspore Warden's and Tidewright's stamina refunds no longer give 1 extra, and the
  rules text reads "1 per ..." instead of "0, +1 per ...".
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
