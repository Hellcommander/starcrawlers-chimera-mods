# Credits

**Made by Arendeth.** Unofficial content for *StarCrawlers Chimera* by Juggernaut Games; not made or endorsed by them.

This file lists everything these mods were made with, and everything they draw on. What may be shared, and on what
terms, is in `LICENSE.md`. Every sound and the pack it came from is in `AUDIO_CREDITS.md`.

## Inspirations

Everything below gave us themes and mechanic ideas only. No names, text, art, code or audio were taken from any of
it.

- **StarCrawlers** (2019), Juggernaut Games. It is the reference point for our classes, and source of the mechanics
  the first game had and Chimera dropped:
  - Factions, reputation and favour, job offers by danger level, and bonus objectives (Outside Interests);
  - the enemy kinds behind Interlopers and the per-location enemy rosters in Breaches and Echoes.
- **Arendeth's own Soulash 2 mods.** Each Arendeth Classes class takes its core mechanic from one of them:

  | Class | Soulash 2 mod |
  |---|---|
  | Castborn | Golem Race |
  | Coilwright | Arcane Ballistics |
  | Emberhorn | Cindertusk |
  | Entropy Weaver | Chaos Weaving |
  | Etcher | Acid Magic |
  | Gloomspore Warden | Void Magic |
  | Graftwild | Arborborn |
  | Gravemote Astronomer | Cosmic Magic |
  | Rimelight Seer | Astral Magic |
  | Runeforger | Runic Magic |
  | Scrapper | Improvised Weapons |
  | Squallblade | Baromancy |
  | Tidewright | Hydromancy |
  | Vasculist | Sanguimancy |

- ***Soulash 2*** (Wizards of the Code). Its amplifiers, slotted into an ability to add behaviors to it and change
  its cost, cooldown and reach, inspired the skill rewrites (`modifies`, `add` and `remove` on passives, effects and
  runes), and its target kinds the line, cone and scatter forms a rewrite can give a skill. The other forms (trap,
  grenade, monster, drifter and the nova) are Arendeth's own. The Spellsmith example class is built on all of them.
  The keys, forms and words are our own.
- **Arendeth's own Caves of Qud mod *Conjoined Extended*,** built on John Snail's *Conjoined* mutation mod for
  *Caves of Qud* (Freehold Games). It may have shaped the idea behind Conjoined: one mind in several bodies.
- ***Dungeons of Dredmor*** (Gaslamp Games) and its modding community:
  - The mod layout follows the *structure* of Dredmor's modding: one folder per mod, a manifest, entries merged by
    id, and on-hit/kill/dodge triggers. The 7-tree class build is Dredmor-style too.
  - The Dredmor mods *Runecaster*, *Runesmith* and *Hexblade* inspired Flux Savant's rune-socketed skills.
  - The *Wild Magic* skill of the Dredmor mod *Compleat Essential Skills* (by Essence) inspired the Wildcaster's wild
    surges: tables of random effects, some on your foes and some on you, set off by casting, striking, killing, being
    hit and dodging, not only by spells.
- **The wild magic of *Dungeons & Dragons*** (Wizards of the Coast): the Wild Magic sorcerer's surges and luck, and
  *Baldur's Gate II*'s Wild Mage, whose spells can misfire into another shape or turn on the caster, inspired the
  Wildcaster's Unruly Casting and crit-luck. The Wildcaster's boss echoes are named after *StarCrawlers Chimera*'s own
  bosses.
- **Lewis Carroll's *Alice's Adventures in Wonderland* and *Through the Looking-Glass*,** and the pop culture built
  on them, inspired the Looking-Glass Rogue: the mirror, growing and shrinking, the grinning cat, playing-card guards,
  the tea party and "off with their heads". The books are in the public domain. The class uses no character names
  and no designs from any film, game or other adaptation.

## Tools and models

Every icon, mesh, texture, animation, particle effect and `sfx/` sound was made with Arendeth's own asset toolkit,
using the models below. Each license lets you own and share what the model outputs. None of them ask for credit on
outputs, but they are credited here anyway.

The `audio_licensed/` sounds are different: no model generates or touches them. They are tuned programmatically, by
plain signal processing (pitch shift, layering, random all-pass filtering, EQ, trim and level), after a read-only
audio-quality tuner has worked out what each sound needs. That tuner is the CLAP model below, used only to listen:
it measures the sound and its candidates, chooses the processing settings from those numbers and keeps the try it
hears as best. Nothing it produces ships, and no pack clip ever goes into it.

| Model | Used for | License |
|---|---|---|
| Stable Diffusion 3.5 Medium (Stability AI) | Concept art, icons, particle key frames | Stability AI Community License |
| Stable Audio 3 (Stability AI) | The `sfx/` sounds | Stability AI Community License |
| CLAP, `laion/clap-htsat-unfused` (LAION) | Listening only: the read-only quality tuner that scores sounds and picks the best try | Apache 2.0 |
| Stable Fast 3D (Stability AI) | Meshes for simple props | Stability AI Community License |
| TRELLIS.2 (Microsoft) | Meshes for creatures, items and gear | MIT |
| Wan2.2 TI2V-5B (Wan-AI, Alibaba) | Motion video for animations and particle flipbooks | Apache 2.0 |
| UniRig (VAST-AI-Research) | Rigging creature meshes | MIT |
| CoTracker3 (Meta AI) | Tracking motion from video onto the rig | CC BY-NC 4.0 |
| BiRefNet (ZhengPeng7) | Cutting particles out of their background | MIT |
| Blender (Blender Foundation) | Mesh repair and rig previews | GPL (the tool; its output is ours) |

## Sound

- **Sound effects created with NOISE ALCHEMY – NOISEALCHEMY.COM.** Their packs are the material behind every sound in
  the `sfx/` folders (as references for the renders, or as the clips a rebuild transforms), and clips from them are
  layered into some of the `audio_licensed/` sounds.
- **Function Loops** (*Ultimate SFX for Games*), **Tao & Sound** (*Magic Spells SFX Bundle*, *Inventory SFX Bundle*,
  *Puzzle Audio Kit*; sound assets by Fernando del Pueyo Montesinos, Tao & Sound Creative Music Solutions, used under
  their EULA) and **Ovani Sound** (*Ice*, *Microscale Bugs* and *Robotic Beings Sound FX Packs*): the licensed sounds in
  the `audio_licensed/` folders, rebuilt from their clips by plain processing with no AI (those packs' licenses bar AI
  tools and datasets, so no model ever embeds or learns from them). They ship sealed in each mod's `sounds.pak`, so
  nobody can lift them out of the mods, as the licenses require. `AUDIO_CREDITS.md` lists the clips behind every file.

## Software

The mods run on the Breachwork Loader, a BepInEx plugin by Arendeth. It is built with:

- BepInEx and UnityDoorstop (LGPL-2.1);
- HarmonyX, MonoMod and Mono.Cecil (MIT);
- Newtonsoft.Json (MIT), in the mod manager;
- MoonSharp (BSD 3-Clause), the Lua interpreter behind script mods (`_modding/ClassLoader/lib/MoonSharp-LICENSE.txt`).

Code plugins and Lua scripts written by other people for the loader are their authors' own work and carry their own
credits. The example plugin and example script are Arendeth's.

`THIRD_PARTY_NOTICES.txt` in the player package has the details.
