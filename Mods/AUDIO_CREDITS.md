# Audio credits

## Sound effects in the `sfx/` folders

**Sound effects created with NOISE ALCHEMY – NOISEALCHEMY.COM**

The sounds in the `sfx/` folders are made from Noise Alchemy material only, apart from the synthesized ones named below. 91 were rendered new with our own
asset tool (Stable Audio 3), which used clips from the packs below as reference material: every render starts from
noise level 0.87, so the result is a new, unique sound, never a light edit of a clip (each one's correlation with its
references is at most 0.081). 34 were rebuilt from their Noise Alchemy reference clips by plain signal
processing, with no AI (pitch shift, layering, random all-pass, EQ; correlation with any clip at most 0.25), because
their first renders had also leaned on a pack that bars AI tools, and one (`vasculist_spike`) and the Spellsmith's nine (`ss_*`) are synthesized from
material parameters by the asset toolkit's bench with no clip and no model behind them (the read-only listener only picks,
among seeded takes, the one that reads most clearly as its own sound and least like its siblings); each one's `.meta.json` says which it is. Noise
Alchemy's terms allow transformed sounds in commercial projects, so all of these are free to share, adapt and use in
your own work under Mods/LICENSE.md, but not to sell as sounds or audio packs. Credit Noise Alchemy as above wherever
you use them.

Only Noise Alchemy packs, whose terms allow AI tools, were references for the renders. Sounds whose main clip came
from an Ovani Sound, Tao & Sound or Function Loops pack were rebuilt from those clips without AI and live in the
`audio_licensed/` folders (second table below).

Reference packs (each sound's `.meta.json` names the clips behind it):

- **Noise Alchemy** (<https://noisealchemy.com/>): the *Dark Fantasy Studio* packs by Nicolas Jeudy, and the
  *NOISE ALCHEMY* packs (Hybrid, Nerve, Cynetic, Textures, Horse Steps, Flash Light, UI Activation, Uneasy Listening,
  Downsweeps, Drekavac, Invasion Texture, Protocol Chaos, Reality Glitch, Doom, Dragon Wings, Monster Screams, Ancient
  Mechanisms, Hostile, Breathing Darkness).

## Licensed sound effects in the `audio_licensed/` folders

The sounds in every `audio_licensed/` folder are **third-party sound effects**, used under license by the person who goes by the alias Arendeth. They
were rebuilt for this project from the packs' clips with plain signal processing and no AI at all, because these
packs' licenses bar AI tools: a pitch shift of at least three semitones, a layer from a second clip, a random all-pass
filter in each channel (the stereo width), an EQ curve, then trim, fade and level-match (`_modding/audio/sfx_alter.py`).
Every file's waveform correlation with any clip it came from is at most 0.25, so no original can be recovered from it.
The tuning is programmatic: a read-only audio-quality tuner (the CLAP listener, `audio_listener` in the asset toolkit)
measures each sound, works out what it needs (the settings in `sfx_alter_settings.json`, chosen from numbers and text
only) and keeps the try it hears as best, but it never generates or changes audio itself, and no pack clip ever goes
into it. They are still derived from the packs below, so the packs' licences still apply.

These files ship sealed in each mod's `sounds.pak` and are not in the source repository (a build from a plain clone
plays the game's own sounds instead). The `sfx/` folders are different: those sounds were made new with our own
asset tool, from Noise Alchemy references only.

**They are not covered by the open terms in Mods/LICENSE.md.** They may be shared only as part of these mods, for
use in StarCrawlers Chimera. Do not extract them, re-share them on their own or put them in a sound pack. If you
adapt a mod, replace these sounds with your own.

Sources:

- **Function Loops**: *Ultimate SFX for Games*, sold through Humble Bundle (<https://www.functionloops.com/>). Their
  terms allow editing and re-working the sounds for a production and distributing the finished product, never sharing
  the sounds themselves, and bar every AI use (training, datasets, generation): so these sounds are produced by plain
  processing only.
- **Tao & Sound**: *Magic Spells SFX Bundle*, *Inventory SFX Bundle* and *Puzzle Audio Kit*. Sound assets by Fernando
  del Pueyo Montesinos, Tao & Sound Creative Music Solutions (<https://taoandsound.com/>), used under their EULA: inside
  an application only, never extractable, never for AI training or datasets.
- **Ovani Sound** (<https://ovanisound.com/>): *Ice Sound FX Pack Vol. 1*, *Microscale Bugs Sound FX Pack Vol. 1* and
  *Robotic Beings Sound FX Pack Vol. 1*. The most restricted of the packs: no AI, nothing standalone; used only where
  Noise Alchemy's packs have no fitting sound.
- **Noise Alchemy**, for the clips layered into the sounds in the second table. Sound effects created with NOISE
  ALCHEMY – NOISEALCHEMY.COM.

No clip from these packs is ever embedded by, or fed to, any model: the asset library finds them by file name and a plain
spectral fingerprint, and the sounds made from them are shaped by plain processing. The sealed `sounds.pak` archives keep
the files from being lifted out of the mods.

| File | Source | Original file |
|---|---|---|
| `FluxSavant/audio_licensed/arc_lance.wav` | Tao & Sound - Magic Spells SFX Bundle | Electric 6.mp3 |
| `FluxSavant/audio_licensed/catalyst_flask.wav` | Tao & Sound - Inventory SFX Bundle | PotionUse5.mp3 |
| `FluxSavant/audio_licensed/critical_mass.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Magic Spells & Attack SFX | FL_GFX_Magic_Spell_Attack_Sfx_01.wav |
| `FluxSavant/audio_licensed/instability.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Glitches | FL_GFX_Motion_Glitch_19.wav |
| `FluxSavant/audio_licensed/lattice_field.wav` | Tao & Sound - Magic Spells SFX Bundle | Ice 8.mp3 |
| `FluxSavant/audio_licensed/react_aether_arc.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Magic Spells & Attack SFX | FL_GFX_Magic_Spell_Attack_Sfx_25.wav |
| `FluxSavant/audio_licensed/react_brittle_lattice.wav` | Tao & Sound - Magic Spells SFX Bundle | Ice 5 (freeze).mp3 |
| `FluxSavant/audio_licensed/react_caustic_vapour.wav` | Tao & Sound - Magic Spells SFX Bundle | Venom 5.mp3 |
| `FluxSavant/audio_licensed/react_dissolution.wav` | Tao & Sound - Magic Spells SFX Bundle | Venom 6.mp3 |
| `FluxSavant/audio_licensed/react_flashpoint.wav` | Tao & Sound - Magic Spells SFX Bundle | Fire 4.mp3 |
| `FluxSavant/audio_licensed/react_galvanic_bloom.wav` | Tao & Sound - Magic Spells SFX Bundle | Venom 7.mp3 |
| `FluxSavant/audio_licensed/react_plasma_flash.wav` | Tao & Sound - Magic Spells SFX Bundle | Electric 1.mp3 |
| `FluxSavant/audio_licensed/react_stasis_bloom.wav` | Tao & Sound - Magic Spells SFX Bundle | Ice 6.mp3 |
| `FluxSavant/audio_licensed/react_superconduct.wav` | Tao & Sound - Magic Spells SFX Bundle | Electric 3.mp3 |
| `FluxSavant/audio_licensed/react_thermal_shock.wav` | Tao & Sound - Magic Spells SFX Bundle | Ice 4.mp3 |
| `FluxSavant/audio_licensed/rune_press.wav` | Tao & Sound - Inventory SFX Bundle | Rune1.mp3 |
| `Kinetics/audio_licensed/blade_fan_of_knives.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Medieval Fighting | FL_GFX_Medieval_Fighting_Sword_03.wav |
| `Kinetics/audio_licensed/blade_knife_toss.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Swing & Dash | FL_GFX_Swing_Dash_30.wav |
| `Kinetics/audio_licensed/blade_pinning_throw.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Medieval Fighting | FL_GFX_Medieval_Fighting_Arrow_11.wav |
| `Kinetics/audio_licensed/blade_recall.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Swing & Dash | FL_GFX_Swing_Dash_17.wav |
| `Kinetics/audio_licensed/blade_thousand_returns.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Orchestral Whooshes | FL_GFX_Orchestral_Whoosh_18.wav |
| `Kinetics/audio_licensed/grapnel_pendulum.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Orchestral Whooshes | FL_GFX_Orchestral_Whoosh_29.wav |
| `Kinetics/audio_licensed/grapnel_swing_kick.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Swing & Dash | FL_GFX_Swing_Dash_11.wav |
| `Kinetics/audio_licensed/grapnel_wall_pin.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Medieval Fighting | FL_GFX_Medieval_Fighting_Arrow_06.wav |
| `Kinetics/audio_licensed/grapnel_yank.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Cozy Mechanics | FL_GFX_Cozy_Mechanic_Fishing_Line.wav |
| `Kinetics/audio_licensed/grapnel_zip_line.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Swing & Dash | FL_GFX_Swing_Dash_22.wav |
| `Kinetics/audio_licensed/grappler_body_slam.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Medieval Fighting | FL_GFX_Medieval_Fighting_War_Club_05.wav |
| `Kinetics/audio_licensed/grappler_collar_grab.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Clothing, Fabric & Materials | FL_GFX_Clothing_Fabric_Material_Foley_Take_Off_02.wav |
| `Kinetics/audio_licensed/grappler_iron_clinch.wav` | Tao & Sound - Inventory SFX Bundle | MetalWeaponDrop2.mp3 |
| `Kinetics/audio_licensed/grappler_piledriver.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Orchestral Impacts | FL_GFX_Orchestral_Impact_26.wav |
| `Kinetics/audio_licensed/grappler_suplex.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Orchestral Whooshes | FL_GFX_Orchestral_Whoosh_06.wav |
| `Kinetics/audio_licensed/guard_close_protection.wav` | Tao & Sound - Magic Spells SFX Bundle | Shield 3.mp3 |
| `Kinetics/audio_licensed/guard_over_here.wav` | Tao & Sound - Magic Spells SFX Bundle | Bravery 6.mp3 |
| `Kinetics/audio_licensed/guard_payback.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Orchestral Impacts | FL_GFX_Orchestral_Impact_10.wav |
| `Kinetics/audio_licensed/guard_retort_stance.wav` | Tao & Sound - Inventory SFX Bundle | LeatherEquip5.mp3 |
| `Kinetics/audio_licensed/guard_shoulder_check.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Medieval Fighting | FL_GFX_Medieval_Fighting_War_Club_06.wav |
| `Kinetics/audio_licensed/rig_hammerfall.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Orchestral Impacts | FL_GFX_Orchestral_Impact_19.wav |
| `Kinetics/audio_licensed/rig_hydraulic_charge.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Doors | FL_GFX_Home_Fx_Door_Sliding_07.wav |
| `Kinetics/audio_licensed/rig_overheat.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Alarms & Sirens | FL_GFX_Alarm_Siren_14.wav |
| `Kinetics/audio_licensed/rig_piston_jab.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Household Foley & Devices | FL_GFX_Household_Foley_Devices_Latch_01.wav |
| `Kinetics/audio_licensed/rig_plate_up.wav` | Tao & Sound - Inventory SFX Bundle | MetalWeaponDrop1.mp3 |
| `Kinetics/audio_licensed/rig_vent_blast.wav` | Tao & Sound - Puzzle Audio Kit | Explosion 1.wav |
| `Kinetics/audio_licensed/run_flying_knee.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Swing & Dash | FL_GFX_Swing_Dash_13.wav |
| `Kinetics/audio_licensed/run_second_wind.wav` | Tao & Sound - Magic Spells SFX Bundle | Heal 3.mp3 |
| `Kinetics/audio_licensed/run_slide_tackle.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Dreamy Whooshes | FL_GFX_Motion_Dreamy_Whoosh_10.wav |
| `Kinetics/audio_licensed/run_vault_stomp.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Orchestral Impacts | FL_GFX_Orchestral_Impact_24.wav |
| `Kinetics/audio_licensed/run_wall_run.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Swing & Dash | FL_GFX_Swing_Dash_04.wav |
| `Kinetics/audio_licensed/trick_bank_shot.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Guns | FL_GFX_Gun_Shot_01.wav |
| `Kinetics/audio_licensed/trick_coin_flick.wav` | Tao & Sound - Inventory SFX Bundle | CoinFlip2.mp3 |
| `Kinetics/audio_licensed/trick_deadeye.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Guns | FL_GFX_Gun_Shot_Reloaded_02.wav |
| `Kinetics/audio_licensed/trick_jackpot.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Guns | FL_GFX_Gun_Shot_Spray_01.wav |
| `Kinetics/audio_licensed/trick_pocket_change.wav` | Tao & Sound - Inventory SFX Bundle | CoinPouring2.mp3 |
| `Library/audio_licensed/moves_leap.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Swing & Dash | FL_GFX_Swing_Dash_19.wav |
| `Library/audio_licensed/moves_recall.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Swing & Dash | FL_GFX_Swing_Dash_15.wav |
| `Library/audio_licensed/moves_ricochet.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Bluster Shots | FL_GFX_Bluster_Shot_02.wav |
| `Library/audio_licensed/moves_tether.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Household Foley & Devices | FL_GFX_Household_Foley_Devices_Reel.wav |
| `Library/audio_licensed/moves_throw_impact.wav` | Function Loops - Ultimate SFX for Games (Humble Bundle), Medieval Fighting | FL_GFX_Medieval_Fighting_War_Club_04.wav |

These sounds were first made with the asset tool, with a clip from an Ovani Sound, Tao & Sound or Function Loops
pack as their main reference. Those packs bar AI tools, so each was rebuilt from its clips by plain processing, as
above, and moved here from its mod's `sfx/` folder. Ovani's packs are the most restricted (nothing standalone, no AI);
they are used where Noise Alchemy's packs have no fitting sound (ice, microscale bugs, robotic beings). The clips a
rebuild takes, main one first:

| File | Clips |
|---|---|
| `ArendethClasses/audio_licensed/coil_slug.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Shots Big E.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Shots Big C.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Shots Big B.wav |
| `ArendethClasses/audio_licensed/fulg_feint.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Magic Impact Small C.wav; Noise Alchemy - Dark Fantasy Studio Smashed: Dark Fantasy Studio- Smashed 51.wav; Noise Alchemy - Dark Fantasy Studio Swords: Dark Fantasy Studio- Swords 45.wav |
| `ArendethClasses/audio_licensed/fulg_fork.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Snow Impact Big A.wav; Tao & Sound - Magic Spells SFX Bundle: Electric 2.mp3; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Magic Impact Small C.wav |
| `ArendethClasses/audio_licensed/fulg_step.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Magic Impact Small C.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Shots Small B.wav; Noise Alchemy - Dark Fantasy Studio Swords: Dark Fantasy Studio- Swords 45.wav |
| `ArendethClasses/audio_licensed/mia_salt.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Hammer A.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Magic JRPG E.wav; Function Loops - Ultimate SFX for Games, FL_NHW_ONE SHOT DRUMS: FL_NHW_CRASH 01.wav |
| `ArendethClasses/audio_licensed/mim_bite.wav` | Ovani Sound - Microscale Bugs Sound FX Pack Vol. 1: Spider Bite B.wav; Ovani Sound - Microscale Bugs Sound FX Pack Vol. 1: Tick Bite B.wav; Ovani Sound - Microscale Bugs Sound FX Pack Vol. 1: Spider Bite A.wav |
| `ArendethClasses/audio_licensed/mim_tongue.wav` | Ovani Sound - Microscale Bugs Sound FX Pack Vol. 1: Mosquito Suck A.wav; Noise Alchemy - Dark Fantasy Studio Crawler: Dark Fantasy Studio-Crawler_28.wav; Noise Alchemy - Dark Fantasy Studio Crawler: Dark Fantasy Studio-Crawler_4.wav |
| `ArendethClasses/audio_licensed/rh_arc.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Magic JRPG E.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Riser Slow E.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Riser Slow B.wav |
| `ArendethClasses/audio_licensed/rh_freeze.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Magic JRPG D.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Magic JRPG E.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Magic JRPG B.wav |
| `ArendethClasses/audio_licensed/rh_scrounge.wav` | Function Loops - Ultimate SFX for Games, Foley: FL_GFX_Home_Foley_Trash.wav; Ovani Sound - Robotic Beings Sound FX Pack Vol. 1: Bulky Dreadnought 003.wav; Noise Alchemy - Dark Fantasy Studio Rocks and boulders: Dark Fantasy Studio- Rocks and boulders 19.wav |
| `ArendethClasses/audio_licensed/rh_steam.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Shimmer A.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Shimmer C.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Shimmer B.wav |
| `ArendethClasses/audio_licensed/rh_throw.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Riser Slow B.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Magic JRPG D.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Magic JRPG E.wav |
| `ArendethClasses/audio_licensed/rune_cascade.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Magic JRPG E.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Magic Impact Medium C.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Shots Big A.wav |
| `ArendethClasses/audio_licensed/snare_burst.wav` | Ovani Sound - Robotic Beings Sound FX Pack Vol. 1: Damage Deactivation 003.wav; Noise Alchemy - Dark Fantasy Studio Chaos: Dark Fantasy Studio- Chaos 45.wav; Noise Alchemy - Doom: NOISE_ALCHEMY_DOOM_1_GLITCH.wav |
| `ArendethClasses/audio_licensed/wild_borrow.wav` | Tao & Sound - Magic Spells SFX Bundle: Madness 6.mp3; Tao & Sound - Magic Spells SFX Bundle: Madness 6.mp3; Noise Alchemy - Dark Fantasy Studio Dimensional portal: Dark Fantasy Studio- Dimensional portal 36.wav |
| `ExampleClasses/audio_licensed/arcgunner_round.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Shots Small B.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Shots Medium E.wav; Noise Alchemy - Dark Fantasy Studio Lightgun: Dark Fantasy Studio-lightgun-30.wav |
| `KelvinMagus/audio_licensed/km_palisade.wav` | Tao & Sound - Magic Spells SFX Bundle: Ice 1.mp3; Tao & Sound - Magic Spells SFX Bundle: Ice 1.mp3; Tao & Sound - Magic Spells SFX Bundle: Ice 4.mp3 |
| `KelvinMagus/audio_licensed/km_permafrost.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Freezing Loop.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice and Snow Debris Slide E.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Giant Ice Movement D.wav |
| `KelvinMagus/audio_licensed/km_sentinel.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Giant Ice Movement G.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Giant Ice Movement E.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Giant Ice Movement H.wav |
| `KelvinMagus/audio_licensed/km_thermal_shock.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Magic Impact Medium D.wav; Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Magic Impact Medium C.wav; Noise Alchemy - Dark Fantasy Studio Broken glass: Dark Fantasy Studio- Broken glass 8.wav |
| `LookingGlass/audio_licensed/lg_bite.wav` | Ovani Sound - Microscale Bugs Sound FX Pack Vol. 1: Tick Bite B.wav; Ovani Sound - Microscale Bugs Sound FX Pack Vol. 1: Mosquito Suck A.wav; Ovani Sound - Microscale Bugs Sound FX Pack Vol. 1: Mosquito Suck C.wav |
| `LookingGlass/audio_licensed/lg_claw.wav` | Ovani Sound - Microscale Bugs Sound FX Pack Vol. 1: Spider Leg Rub B.wav; Ovani Sound - Microscale Bugs Sound FX Pack Vol. 1: Spider Leg Rub Short C.wav; Ovani Sound - Microscale Bugs Sound FX Pack Vol. 1: Ant Step Grass D.wav |
| `LookingGlass/audio_licensed/lg_grin.wav` | Tao & Sound - Magic Spells SFX Bundle: Heal 11.mp3; Tao & Sound - Magic Spells SFX Bundle: Revive 5.mp3; Tao & Sound - Magic Spells SFX Bundle: Revive 5.mp3 |
| `LookingGlass/audio_licensed/lg_guard.wav` | Tao & Sound - Magic Spells SFX Bundle: Close Scroll 1.mp3; Tao & Sound - Magic Spells SFX Bundle: Close Scroll 1.mp3; Tao & Sound - Inventory SFX Bundle: ScrollOpen2.mp3 |
| `LookingGlass/audio_licensed/lg_teaparty.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Cubes In Glass A.wav; Ovani Sound - Robotic Beings Sound FX Pack Vol. 1: Little Mech 004.wav; Ovani Sound - Robotic Beings Sound FX Pack Vol. 1: Mech Bug 002.wav |
| `LookingGlass/audio_licensed/lg_watch.wav` | Tao & Sound - Puzzle Audio Kit: Clock 1.wav; Tao & Sound - Puzzle Audio Kit: Clock 1.mp3; Tao & Sound - Puzzle Audio Kit: Clock 3.wav |
| `UniqueClasses/audio_licensed/glyph_mark.wav` | Tao & Sound - Puzzle Audio Kit: Affirmative Voice 3.wav; Tao & Sound - Puzzle Audio Kit: Affirmative Voice 6.wav; Tao & Sound - Puzzle Audio Kit: Affirmative Voice 5.wav |
| `UniqueClasses/audio_licensed/relic_sun.wav` | Ovani Sound - Ice Sound FX Pack Vol. 1: Ice Riser Fast E.wav; Noise Alchemy - Dark Fantasy Studio Shipwreck: Dark Fantasy Studio - Shipwreck 22.wav; Noise Alchemy - Dark Fantasy Studio Shipwreck: Dark Fantasy Studio - Shipwreck 25.wav |
