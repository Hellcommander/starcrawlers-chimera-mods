# Breachwork changelog

The version is the loader's version, and every bundled mod follows it. The numbers 1.2 and 1.3 were never used: the
release after 1.1.0 is 1.4.0. The dates are the dates of the work.

## 1.4.0 (not released yet)

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
  picture from your classes, skills, gear, effects and form. The `Tool` setting picks what makes it: the optional
  Portrait Maker download, which draws one from code with no GPU, or the AI asset toolkit, which needs an NVIDIA GPU.

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
  stat bonus, so "crit" or "savvy" finds the classes that offer it. Hovering a result says where it matched.

### Modding

- **Code plugins:** mods can carry C# in `plugins/*.dll`, which only load once the player allows that mod's code.
- **Lua scripts:** `scripts/*.lua` run sandboxed on MoonSharp and need no OK.
- Plugin API levels 2 and 3: `AddPortraitButton`, `CurrentForm`, `PortraitFolder`, `CurrentPortrait`, `SetPortrait` and
  `SimultaneousTurns`.
- **Realtime with Pause** (source only). An unfinished example plugin that makes fights run in real time with a pause
  key. It ships with no dll and does nothing until you build it, and its per-character timers have never run in game.
- Rewrite fields `form`, `add`, `remove`, `others`, `attacks`, `roll` and numeric `pierce`.
- `procChancePerStack`, `synergyPenalty`, `augments`, `runeCharge`, `runeDischarge` and `thirdPerson`.
- Particle layers can `borrow` another system's material, and aliases can use `without`, `use` and `ground`.
- **Boss Looks** (new mod): repoints look-alike effects at the installed game's own particles instead of copying them.
  Particle names can point into an enemy prefab.

### Fixes

- A summon carrying a spell keeps it through a save and load.
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
