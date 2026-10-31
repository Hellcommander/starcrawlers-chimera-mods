"""Real skill icons for the tiers that still show make_placeholder_icons.py's hue-shifted badge, through the AAMT
toolkit's SD3.5 stage (D:\\games\\Ai assisted toolkit). Each icon matches its class row:
  painted - glowing emblem on black, full bleed (most classes)                       -> <mod>/<icon>
  flat    - bold pictogram on white, cut out onto the row's own backdrop and frame   -> <mod>/<icon>
Concepts are kept in _modding/item_art/icons/<slug>.png. Only placeholder files are replaced unless --redo.
Usage: python make_class_icons.py [--only slug,slug] [--stage concept|icon|all] [--redo]"""
import argparse, sys, time, zlib
from pathlib import Path
from PIL import Image, ImageFilter

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from make_icons import background, frame, rgba, W, S
from make_item_assets import cutout, shared

MODS = HERE.parent / "Mods"
ART = HERE / "item_art" / "icons"

PAINTED = ("fantasy sci-fi game skill icon, {what}, glowing {col} energy, centered emblem, dramatic, highly detailed "
           "digital painting, pure black background, vivid glow, symmetrical composition")
PAINTED_NEG = "text, letters, numbers, watermark, frame, border, signature, photo, blurry, cropped, multiple icons, ui"
FLAT = ("flat vector pictogram icon, {what}, bold simple solid shapes, two-tone {col} and white, thick clean "
        "silhouettes, minimalist, centered, isolated on plain white background")
FLAT_NEG = "text, letters, numbers, watermark, frame, border, gradient background, photo, 3d, shading, texture, busy, square tile, rounded rectangle, app icon background, badge"

# (icon path under Mods, style, colour words, what to draw)
ICONS = [
    # ---- ArendethClasses ------------------------------------------------------------------------------------
    ("ArendethClasses/castborn_t6.png", "painted", "molten orange", "roaring iron furnace mask with open vents spraying molten slag"),
    ("ArendethClasses/castborn_t7.png", "painted", "white-hot orange", "cracked iron heart-core flooding with fresh molten metal, glowing seams"),
    ("ArendethClasses/coilwright_t6.png", "painted", "electric cyan", "ring of crackling live wire fence posts around a coil tower"),
    ("ArendethClasses/coilwright_t7.png", "painted", "electric cyan", "network of tesla coil towers linked by arcing lightning"),
    ("ArendethClasses/emberhorn_t6.png", "painted", "fiery red-orange", "steam-breathing bull horns tossing a body into the air, sparks"),
    ("ArendethClasses/emberhorn_t7.png", "painted", "furnace orange", "boiler firebox burning inside an armoured chest, flames through the grate"),
    ("ArendethClasses/entropy_weaver_t6.png", "painted", "prismatic multicolour", "a stray glowing thread skipping between three points, each a different element"),
    ("ArendethClasses/entropy_weaver_t7.png", "painted", "violet and teal", "a weaving loom of glowing threads looping back into itself"),
    ("ArendethClasses/etcher_t6.png", "painted", "acid green", "boot skating on a hissing slick of acid solvent, wake of vapour"),
    ("ArendethClasses/etcher_t7.png", "painted", "acid green", "deeply etched layered sigil crumbling apart into pieces"),
    ("ArendethClasses/gloomspore_warden_t6.png", "painted", "sickly violet and green", "cluster of glowing mushroom caps bursting in clouds of spores"),
    ("ArendethClasses/gloomspore_warden_t7.png", "painted", "violet and green", "glowing fungal hyphae threads spreading under skin like veins"),
    ("ArendethClasses/graftwild_t6.png", "painted", "emerald green", "thorny rooting tendril hooking and reeling, puff of spores"),
    ("ArendethClasses/graftwild_t7.png", "painted", "autumn amber and green", "shed bark husk and falling leaves around a turning wheel of seasons"),
    ("ArendethClasses/gravemote_astronomer_t6.png", "painted", "deep violet", "cord of bent space hooked into a glowing chest, gravity leash"),
    ("ArendethClasses/gravemote_astronomer_t7.png", "painted", "pale violet and grey", "small dead moon fragment on a tight orbit, dust trail"),
    ("ArendethClasses/rimelight_seer_t6.png", "painted", "icy blue", "calm frost aura crystallizing around a closed eye"),
    ("ArendethClasses/rimelight_seer_t7.png", "painted", "icy white-blue", "frost judge's gavel shattering into ice splinters"),
    ("ArendethClasses/runeforger_t6.png", "painted", "cyan and steel", "glowing ward sigil burned onto an armour plate, sparks from a graver"),
    ("ArendethClasses/runeforger_t7.png", "painted", "gold and cyan", "glowing maker's hallmark stamp on an anvil, awakened rune"),
    ("ArendethClasses/scrapper_t6.png", "painted", "orange and steel", "armoured shoulder barging forward, impact shockwave"),
    ("ArendethClasses/scrapper_t7.png", "painted", "red and orange", "bloodied fist rising from the ground, second wind aura"),
    ("ArendethClasses/squallblade_t6.png", "painted", "storm blue-white", "narrow funnel cloud tornado whirling over a deck plate"),
    ("ArendethClasses/squallblade_t7.png", "painted", "storm blue", "two swirling air pressure vortices meeting edge to edge"),
    ("ArendethClasses/tidewright_shackle.png", "painted", "sea teal and white", "thick twisting rope of rushing water coiled tight like a shackle chain around an iron ring, spray flying"),
    ("ArendethClasses/tidewright_jetwash.png", "painted", "sea teal and white", "surging horizontal torrent of seawater shaped like a speeding arrow, long spray trail behind it, wave crashing at its tip"),
    ("ArendethClasses/tidewright_gyre.png", "painted", "deep sea blue and teal", "churning whirlpool vortex seen from above, debris spiralling into the centre"),
    ("ArendethClasses/tidewright_t7.png", "painted", "deep sea blue and teal", "massive floodgate valve wheel bursting open, a towering wall of water crashing out"),
    ("ArendethClasses/fulgurant.png", "painted", "electric blue-white", "duelist mid-lunge dissolving into a forked lightning bolt, blade drawn, crackling trail"),
    ("ArendethClasses/fulgurant_t0.png", "painted", "electric cyan", "human nerve fibres sparking like live wires along a running leg"),
    ("ArendethClasses/fulgurant_t1.png", "painted", "electric blue-white", "sword-wielding silhouette blurring forward in a single lightning streak"),
    ("ArendethClasses/fulgurant_t2.png", "painted", "electric cyan and violet", "spark flaring on one side while a blade strikes from behind out of the shadow"),
    ("ArendethClasses/fulgurant_t3.png", "painted", "electric blue", "a single lightning bolt branching into three jagged paths that strike three shadowy figures, no cutlery"),
    ("ArendethClasses/fulgurant_t4.png", "painted", "pale electric blue", "ghostly afterimage of a fighter made of crackling static left on a metal deck"),
    ("ArendethClasses/fulgurant_t5.png", "painted", "white-hot electric blue", "straight rail of lightning streaking across a corridor, figure at its tip"),
    ("ArendethClasses/fulgurant_t6.png", "painted", "blinding blue-white", "bright return lightning stroke flashing up a jagged channel into a raised blade"),
    ("ArendethClasses/fulgurant_t7.png", "painted", "electric blue and white", "human heart overflowing with arcing electricity, forks leaping outward"),
    ("ArendethClasses/ichorborn.png", "painted", "deep crimson and violet", "gaunt horned figure with two great wings of dripping black ichor and one glowing violet eye"),
    ("ArendethClasses/ichorborn_t0.png", "painted", "deep crimson", "pair of wet black ichor wings folded like a shield, dripping crimson"),
    ("ArendethClasses/ichorborn_t1.png", "painted", "deep crimson and black", "a thick glob of black tar flying from an open fanged mouth, dripping"),
    ("ArendethClasses/ichorborn_t2.png", "painted", "crimson and violet", "figure sinking into a pool of black liquid on a metal deck, ripples, a second pool rising elsewhere"),
    ("ArendethClasses/ichorborn_t3.png", "painted", "dark crimson", "rope of black ichor dragging a struggling silhouette forward through shattered debris"),
    ("ArendethClasses/ichorborn_t4.png", "painted", "deep crimson and black", "a towering wave of black tar crashing forward, dark droplets flying"),
    ("ArendethClasses/ichorborn_t5.png", "painted", "violet and black", "single huge open eye firing a straight beam of black and violet light"),
    ("ArendethClasses/ichorborn_t6.png", "painted", "crimson and violet", "swirling torn rift in the air with small black ooze creatures spilling out"),
    ("ArendethClasses/ichorborn_t7.png", "painted", "crimson", "a single torn black ichor wing spinning through dark empty space, crimson droplets trailing, on pure black"),
    ("ArendethClasses/ichorborn_t8.png", "painted", "violet and shadow", "wounded figure rising with two spectral shadow wings unfurling, violet shockwave"),
    ("ArendethClasses/snareseer.png", "painted", "psychic violet", "hooded seer with a glowing third eye standing over a web of glowing glyph traps on the floor"),
    ("ArendethClasses/snareseer_t0.png", "painted", "psychic violet", "glowing open eye inside an hourglass, threads of fate leading out of it"),
    ("ArendethClasses/snareseer_t1.png", "painted", "dark violet", "menacing glowing violet psychic rune floating in darkness, wisps of dread rising, no plate, no frame"),
    ("ArendethClasses/snareseer_t2.png", "painted", "pale violet", "translucent ghostly decoy figure with a hidden glowing snare circle at its feet"),
    ("ArendethClasses/snareseer_t3.png", "painted", "violet and magenta", "psychic hook of light latched onto a boot, chain of thought pinning it to the floor"),
    ("ArendethClasses/snareseer_t4.png", "painted", "violet and white", "footstep landing inside its own glowing silhouette, foreseen future self"),
    ("ArendethClasses/snareseer_t5.png", "painted", "violet", "glowing mine exploding and leaving a smaller echo copy of itself behind, nested rings"),
    ("ArendethClasses/snareseer_t6.png", "painted", "violet and gold", "many glowing floor glyphs detonating in a chain across a dark room, branching timelines"),
    ("ArendethClasses/snareseer_t7.png", "painted", "deep violet", "vast spider web made of glowing psychic threads with eyes at every knot"),
    ("ArendethClasses/mimeticist.png", "painted", "sickly green and amber", "scientist in a lab coat whose silhouette splits into several beast shapes, claws, shell and wings, DNA helix glowing"),
    ("ArendethClasses/mimeticist_t0.png", "painted", "sickly green", "glass sample vial with a glowing creature tissue sample inside, tweezers"),
    ("ArendethClasses/mimeticist_t1.png", "painted", "sickly green", "barbed collection dart with a glowing green sample chamber, flying"),
    ("ArendethClasses/mimeticist_t2.png", "painted", "teal and black", "sleek black panther head and shoulders snarling out of darkness, glowing teal eyes, claws raised, dark vignette"),
    ("ArendethClasses/mimeticist_t3.png", "painted", "rust orange", "snarling scrappy junkyard hound with a pack of shadowy hounds behind it"),
    ("ArendethClasses/mimeticist_t4.png", "painted", "steel blue", "huge armoured beast, a horned bull with a heavy riveted tortoise shell on its back, charging head down, dust flying"),
    ("ArendethClasses/mimeticist_t5.png", "painted", "murky green", "toad-crocodile swamp beast lunging out of dark water with a long sticky tongue"),
    ("ArendethClasses/mimeticist_t6.png", "painted", "pale violet and green", "long-legged spider weaving glowing cable-silk web across a shaft"),
    ("ArendethClasses/mimeticist_t7.png", "painted", "molten orange and green", "winged dragon silhouette breathing a stream of molten slag, glowing green radioactive scales, dark smoky background"),
    ("ArendethClasses/mimeticist_t8.png", "painted", "green and amber", "human hand half turning into a scaled clawed beast hand, seamless transition"),
    ("ArendethClasses/bulkhead_psion.png", "painted", "steel grey and psychic blue", "telekinetic figure with raised hands tearing riveted steel deck plates up into a wall, blue psychic glow"),
    ("ArendethClasses/bulkhead_psion_t0.png", "painted", "steel and blue", "glowing brain outline fused into a riveted steel bulkhead wall"),
    ("ArendethClasses/bulkhead_psion_t1.png", "painted", "psychic blue", "invisible force shockwave shoving a figure backwards into a steel wall"),
    ("ArendethClasses/bulkhead_psion_t2.png", "painted", "steel grey and blue", "thick riveted steel bulkhead wall erupting up out of a cracked deck"),
    ("ArendethClasses/bulkhead_psion_t3.png", "painted", "steel and red", "jagged steel spikes bursting out of a riveted wall"),
    ("ArendethClasses/bulkhead_psion_t4.png", "painted", "steel and blue", "corridor walls crushing inward with spikes, figures squeezed in the middle"),
    ("ArendethClasses/bulkhead_psion_t5.png", "painted", "steel and orange", "giant fist of psychic force smashing a body into a steel wall, sparks"),
    ("ArendethClasses/bulkhead_psion_t6.png", "painted", "steel and blue", "figure sealed between two steel slabs with spikes driving in, a tomb of steel"),
    ("ArendethClasses/bulkhead_psion_t7.png", "painted", "steel and blue", "whole space station hull glowing with psychic blue veins, spikes bristling outward"),
    ("ArendethClasses/reactant_hauler.png", "painted", "orange and teal", "burly dockhand hurling a sloshing chemical drum that bursts into flame and frost"),
    ("ArendethClasses/reactant_hauler_t0.png", "painted", "amber", "hands rummaging a pile of salvaged canisters and drums, sharp-eyed"),
    ("ArendethClasses/reactant_hauler_t1.png", "painted", "oily amber and black", "dented oil drum splitting open mid-air spraying black oil"),
    ("ArendethClasses/reactant_hauler_t2.png", "painted", "rust orange", "arm tearing open a wall panel full of pipes and canisters"),
    ("ArendethClasses/reactant_hauler_t3.png", "painted", "fiery orange", "clay pot of glowing coals shattering into flames"),
    ("ArendethClasses/reactant_hauler_t4.png", "painted", "icy blue", "steel keg bursting in a cloud of freezing coolant vapour"),
    ("ArendethClasses/reactant_hauler_t5.png", "painted", "teal and electric blue", "glass jug of brine shattering with lightning arcing through the splash"),
    ("ArendethClasses/reactant_hauler_t6.png", "painted", "sandy tan", "canvas sack swung open throwing a wide arc of grit"),
    ("ArendethClasses/reactant_hauler_t7.png", "painted", "multicolour fire ice and lightning", "overflowing barrel exploding with fire, frost and lightning all at once"),
    ("ArendethClasses/reactant_hauler_t8.png", "painted", "orange and teal", "chain of chemical explosions linked one into the next"),
    ("ArendethClasses/miasmancer.png", "painted", "sickly green and violet", "hooded alchemist drawing swirling toxic vapours out of the air into glowing flasks"),
    ("ArendethClasses/miasmancer_t0.png", "painted", "pale green", "a sieve catching glowing motes of vapour from foul air"),
    ("ArendethClasses/miasmancer_t1.png", "painted", "sickly green", "face exhaling a stream of glowing green toxic breath"),
    ("ArendethClasses/miasmancer_t2.png", "painted", "acid green", "rolling cloud of caustic green mist spreading low over a deck"),
    ("ArendethClasses/miasmancer_t3.png", "painted", "crimson and violet", "psychic hand pulling a glowing drop of ichor out of a body"),
    ("ArendethClasses/miasmancer_t4.png", "painted", "blood red and green", "a bloom of red ichor bursting open like a toxic flower"),
    ("ArendethClasses/miasmancer_t5.png", "painted", "rust orange", "cone of corrosive rust salt dust eating through metal plating"),
    ("ArendethClasses/miasmancer_t6.png", "painted", "green red and orange", "huge alchemical flask exploding into three coloured toxic clouds"),
    ("ArendethClasses/miasmancer_t7.png", "painted", "dark green", "glowing veins of green blood under skin, a skull poison sign fading away"),
    ("ArendethClasses/miasmancer_t8.png", "painted", "sickly green", "air thick with drifting glowing spores and droplets, saturated haze"),
    ("Synergies/toxic_breath.png", "painted", "toxic green and purple", "grotesque mutant maw spewing a cone of glowing green poison vomit"),
    ("Synergies/acid_fog.png", "painted", "acid green and yellow", "low rolling fog of glowing acid eating etched lines into metal plates"),
    ("Synergies/dissolving_catalyst.png", "painted", "violet and acid green", "glowing alchemical catalyst cloud dissolving matter into swirling particles"),
    ("Synergies/vapour_drum.png", "painted", "teal and sickly green", "metal drum bursting into a cloud of poisoned freezing brine vapour"),
    ("Synergies/miasma_tripwire.png", "painted", "sickly green", "glowing tripwire strung across a corridor leaking puffs of toxic gas"),
    ("ArendethClasses/wildcaster.png", "painted", "prismatic magenta and teal", "wild-haired mage with spells of many different elements spilling uncontrolled from both hands"),
    ("ArendethClasses/wildcaster_t0.png", "painted", "prismatic", "veins glowing with many colours of unstable magic"),
    ("ArendethClasses/wildcaster_t1.png", "painted", "multicolour", "a bolt split into fire, ice, lightning and poison strands"),
    ("ArendethClasses/wildcaster_t2.png", "painted", "violet and gold", "hand reaching through a rift and pulling out a glowing spellbook page"),
    ("ArendethClasses/wildcaster_t3.png", "painted", "feverish magenta and amber", "an open burning hand whose spell melts and warps into a spiral, a spike and a ring all at once"),
    ("ArendethClasses/wildcaster_t4.png", "painted", "violet and teal", "a torn patch of reality on the floor spewing random elemental bursts"),
    ("ArendethClasses/wildcaster_t5.png", "painted", "magenta, teal and gold on a dark background", "a clenched gauntleted fist throwing off stray sparks of red, blue and green magic as it strikes"),
    ("ArendethClasses/wildcaster_t6.png", "painted", "prismatic", "a waterfall of many different spells pouring down onto one point"),
    ("ArendethClasses/wildcaster_t7.png", "painted", "violet and gold", "a spell leaving an open hand and splitting into a beam, a burst and a glowing floor rune"),
    ("ArendethClasses/wildcaster_t8.png", "painted", "red, blue, green, gold and violet on a dark background", "a robed mage in the eye of a storm of many differently coloured spells, a huge ghostly claw reaching out of it"),
    ("ArendethClasses/wildcaster_shrunk.png", "painted", "pale blue and violet", "a tiny frightened figure standing beside a huge boot, shrinking magic spiralling around it"),
    ("ArendethClasses/wildcaster_swollen.png", "painted", "acid green and purple", "a muscular arm bursting out of a torn sleeve, swelling with glowing magic"),
    ("ArendethClasses/wildcaster_luck.png", "painted", "green and gold", "a pile of glowing lucky coins and dice spilling across a metal deck"),
    ("ArendethClasses/wildcaster_glass.png", "painted", "icy cyan", "a human silhouette made of cracked glass"),
    ("ArendethClasses/wildcaster_gold.png", "painted", "gold", "a huge golden dragon claw gently touching a glowing figure"),
    ("ArendethClasses/vasculist_t6.png", "painted", "blood crimson", "pulse of blood arcing from body to body in a chain, heartbeat line"),
    ("ArendethClasses/vasculist_t7.png", "painted", "crimson red", "anatomical heart pounding with shock rings, crescendo"),
    # ---- CustomClasses ---------------------------------------------------------------------------------------
    ("CustomClasses/corsair_t6.png", "painted", "gold and sea teal", "pirate plank over a dark void, cutlass slash"),
    ("CustomClasses/corsair_t7.png", "painted", "gold and blood red", "grinning pirate skull with crossed cutlasses and gold coins"),
    ("CustomClasses/demolitionist_t6.png", "painted", "hazard orange", "row of sticky bombs with blinking red lights linked by glowing tripwires, seen from above, explosive glow"),
    ("CustomClasses/demolitionist_t7.png", "painted", "orange and yellow", "chain of sticky charges detonating one after another"),
    ("CustomClasses/gun_kata_t6.png", "painted", "brass gold", "single bullet ricocheting between four points, tracer lines"),
    ("CustomClasses/gun_kata_t7.png", "painted", "gold and red", "two pistols crossed in an infinite loop flow, motion arcs"),
    ("CustomClasses/juggernaut_t6.png", "painted", "orange and steel", "armoured boot stamping a glowing line into the deck, dare"),
    ("CustomClasses/juggernaut_t7.png", "painted", "steel blue", "unstoppable armoured juggernaut rolling forward, momentum streaks"),
    ("CustomClasses/longshot_t6.png", "painted", "red", "sniper scope crosshair with a narrow glowing window"),
    ("CustomClasses/longshot_t7.png", "painted", "cool cyan", "steady sniper crosshair over a calm still water surface"),
    ("CustomClasses/mad_tinkerer_t6.png", "painted", "green and orange sparks", "ramshackle welded scrap robot rattletrap, welding sparks"),
    ("CustomClasses/mad_tinkerer_t7.png", "painted", "electric green", "exploding gadget with a lightbulb idea glowing in the smoke"),
    ("CustomClasses/nanite_herder_t6.png", "painted", "silver-cyan", "swarm of nanites bursting outward from a host silhouette"),
    ("CustomClasses/nanite_herder_t7.png", "painted", "silver and cyan", "lead nanite swarm cloud with a bell shape, following"),
    ("CustomClasses/phase_drifter_t6.png", "painted", "magenta and cyan", "two silhouettes swapping places through a phase rift"),
    ("CustomClasses/phase_drifter_t7.png", "painted", "magenta", "unanchored ghostly figure trailing afterimages"),
    ("CustomClasses/psi_knight_t6.png", "painted", "psychic blue", "clenched psychic fist dragging everything toward a sword"),
    ("CustomClasses/psi_knight_t7.png", "painted", "calm blue and white", "mirror-still meditating knight reflected in a perfect mirror"),
    ("CustomClasses/puppetmaster_t6.png", "painted", "magenta", "net of glowing puppet strings cast over a crowd of hands"),
    ("CustomClasses/puppetmaster_t7.png", "painted", "magenta and gold", "taut puppet strings tied to a striking hand, backlash"),
    ("CustomClasses/relic_bearer_t6.png", "painted", "gold and blood red", "ancient idol with a glowing open mouth speaking one word"),
    ("CustomClasses/relic_bearer_t7.png", "painted", "gold", "smiling golden idol cradling a small figure, favour"),
    ("CustomClasses/ringmaster_t6.png", "painted", "circus red and gold", "ringmaster's whip snaring a hulking brute, circus ring"),
    ("CustomClasses/ringmaster_t7.png", "painted", "gold spotlight", "spotlight on a performer dodging, cheering crowd silhouettes"),
    ("CustomClasses/shadow_broker_t6.png", "painted", "violet", "figure fading into a curtain of holographic smoke"),
    ("CustomClasses/shadow_broker_t7.png", "painted", "violet and black", "hooded silhouette with a finger over lips, closed ledger"),
    ("CustomClasses/splice_host_t6.png", "painted", "toxic green", "backward-jointed mutant legs leaping, feral pounce"),
    ("CustomClasses/splice_host_t7.png", "painted", "green and violet", "DNA helix seamlessly morphing between beast forms"),
    ("CustomClasses/void_hacker_t6.png", "painted", "violet and red", "hijacked security turret with glowing hacked firmware runes"),
    ("CustomClasses/void_hacker_t7.png", "painted", "glitch violet", "corrupted memory blocks leaking glitch data, segmentation fault"),
    # ---- UniqueClasses ---------------------------------------------------------------------------------------
    ("UniqueClasses/glyphrunner_t6.png", "painted", "cyan", "chain of self-copying glowing glyphs daisy-chained in a line"),
    ("UniqueClasses/glyphrunner_t7.png", "painted", "cyan and gold", "glowing script rewriting itself, runes branching"),
    ("UniqueClasses/null_chorister_t6.png", "painted", "deep violet", "massive low sound wave slamming down, resonating ring"),
    ("UniqueClasses/null_chorister_t7.png", "painted", "violet", "choir of ghostly singing mouths around an echoing note"),
    ("UniqueClasses/paradox_witch_t6.png", "painted", "teal and gold", "ledger of wounds collected into a cracked hourglass"),
    ("UniqueClasses/paradox_witch_t7.png", "painted", "teal", "hex sigil with compounding interest spirals, coins"),
    ("UniqueClasses/probability_shaman_t6.png", "painted", "green and gold", "bolt splitting down many branching forked paths"),
    ("UniqueClasses/probability_shaman_t7.png", "painted", "emerald green", "two twin dice mirrored across a branching timeline"),
    ("UniqueClasses/reliquary_knight_t6.png", "painted", "gold", "armoured knight arriving with a bell-shaped shockwave, three footprints"),
    ("UniqueClasses/reliquary_knight_t7.png", "painted", "gold and pale blue", "sun, moon and nail relics glowing in a liturgical circle"),
    ("UniqueClasses/starfire_adept_t6.png", "painted", "plasma orange and white", "solar magnetic field lines snapping back into a star core"),
    ("UniqueClasses/starfire_adept_t7.png", "painted", "orange and violet", "burned-out star remnant with a burning ground ring"),
    # ---- flat rows -------------------------------------------------------------------------------------------
    ("Conjoined/icons/rout_of_many_hands.png", "flat", "teal and pink", "six fists punching outward from a central body, radial arrangement"),
    ("Conjoined/icons/borrowed_blood.png", "flat", "teal and pink", "two linked figures sharing one heart"),
    ("FluxSavant/icons/glyph_shrapnel.png", "flat", "lime green and violet", "hexagonal rune tile shattering into shards"),
    ("FluxSavant/icons/closed_loop.png", "flat", "lime green and cyan", "circular recycling arrow around a hexagonal rune"),
    ("Kinetics/icons/unstitch.png", "flat", "crimson", "single dagger slicing through a zigzag stitched wound line"),
    ("Kinetics/icons/never_drop_one.png", "flat", "crimson", "open hand with three throwing knives circling above it in an arc"),
    ("Kinetics/icons/crowd_breaker.png", "flat", "blue", "large bold figure in a wide stance at the centre, a shockwave ring knocking small figures away on both sides, filling the frame"),
    ("Kinetics/icons/iron_oath.png", "flat", "blue", "raised fist in front of a shield"),
    ("Kinetics/icons/tic_tac_barrage.png", "flat", "green", "figure kicking off walls in a zigzag"),
    ("Kinetics/icons/untouchable_pace.png", "flat", "green", "sprinting figure with speed lines"),
    ("Kinetics/icons/cats_cradle.png", "flat", "teal", "grappling hook line looping through several points"),
    ("Kinetics/icons/taut_line.png", "flat", "teal", "grappling hook on a straight tight rope with tension lines"),
    ("Kinetics/icons/freight_swing.png", "flat", "orange", "strongman swinging a body by the ankle"),
    ("Kinetics/icons/longshoremans_hold.png", "flat", "orange", "two hands gripping a heavy crate"),
    ("Kinetics/icons/strip_for_parts.png", "flat", "red", "robotic claw hand gripping and ripping a square armour plate"),
    ("Kinetics/icons/red_zone.png", "flat", "red", "round pressure gauge dial with the needle pushed into a red zone, flames above"),
    ("Kinetics/icons/cut_the_deck.png", "flat", "gold", "revolver pistol silhouette firing, a playing card torn in half flying beside the muzzle"),
    ("Kinetics/icons/table_stakes.png", "flat", "gold", "stack of poker chips with a coin"),
    ("ReactionArchitect/icons/slip_contract.png", "flat", "cyan", "figure sidestepping a blow, signed contract"),
    ("ReactionArchitect/icons/contingency_lattice.png", "flat", "cyan", "lattice of four linked nodes"),
    ("SwarmConductor/icons/spearhead.png", "flat", "cyan and amber", "heavy walker robot with a spearhead arrow"),
    ("SwarmConductor/icons/commandeer_spdr.png", "flat", "cyan and amber", "spider-legged walker robot with a signal link"),
    ("SwarmConductor/icons/failover_mesh.png", "flat", "cyan and amber", "mesh network rerouting around a broken node"),
    ("UniqueClasses/axiomancer_t0.png", "flat", "violet and gold", "triangle of first principles with an eye"),
    ("UniqueClasses/axiomancer_t1.png", "flat", "violet and gold", "magnifying glass over a face"),
    ("UniqueClasses/axiomancer_t2.png", "flat", "violet and gold", "speech bubble striking like a hammer"),
    ("UniqueClasses/axiomancer_t3.png", "flat", "violet and cyan", "calm head with a still wave line"),
    ("UniqueClasses/axiomancer_t4.png", "flat", "violet and cyan", "brain inside a protective shell"),
    ("UniqueClasses/axiomancer_t5.png", "flat", "violet and gold", "three lightbulbs connected in a row by chain links, bold shapes"),
    ("UniqueClasses/axiomancer_t6.png", "flat", "violet and green", "glowing idea creature hatching from a flask"),
    ("UniqueClasses/axiomancer_t7.png", "flat", "violet and gold", "radiant head with a halo of stacked rings"),
    ("UniqueClasses/axiomancer_t8.png", "flat", "violet and gold", "many arrows converging into one burst"),
]


def is_placeholder(path):
    """make_placeholder_icons.py draws a gold frame on all four edges."""
    im = Image.open(path).convert("RGBA").resize((256, 256))
    px = [im.getpixel((5, 128)), im.getpixel((128, 5)), im.getpixel((250, 128))]
    return all(abs(r - 255) < 40 and abs(g - 209) < 50 and abs(b - 92) < 60 for r, g, b, a in px)


def slug(rel):
    return rel.replace("/", "_").replace(".png", "")


def concept(rel, style, col, what, redo):
    out = ART / f"{slug(rel)}.png"
    if out.exists() and not redo: return out
    ART.mkdir(parents=True, exist_ok=True)
    ar = shared()
    from sd_http_client import plan_sd_size
    w, h, steps = plan_sd_size(256, 256, kind="icon")
    prompt, neg = (PAINTED, PAINTED_NEG) if style == "painted" else (FLAT, FLAT_NEG)
    t = time.time()
    ar.make_image(prompt.format(what=what, col=col), out, negative_prompt=neg, width=w, height=h, steps=steps,
                  seed=zlib.crc32((rel + what).encode()) % 100000)
    print(f"concept {rel}: {w}x{h} in {time.time() - t:.0f}s")
    return out


def class_icons(target):
    """Every icon the class that owns target uses (its class icon and tier icons)."""
    import json
    for f in MODS.glob("*/*.json"):
        try: d = json.loads(f.read_text(encoding="utf-8"))
        except Exception: continue
        for c in d if isinstance(d, list) else [d]:
            if not isinstance(c, dict) or "tiers" not in c: continue
            names = [c.get("icon")] + [(t.get("newAbility") or {}).get("icon") for t in c["tiers"]]
            paths = [f.parent / n for n in names if n and not n.startswith("SKILLTREE_")]
            if any(p.resolve() == target.resolve() for p in paths): return paths
    return []


def row_colours(target):
    """Frame and backdrop of a flat row, read off a real icon of the same class."""
    for sib in class_icons(target):
        if sib.resolve() == target.resolve() or not sib.exists() or is_placeholder(sib): continue
        im = Image.open(sib).convert("RGB").resize((256, 256))
        edge = im.getpixel((8, 128))      # the coloured frame line (make_icons.frame)
        outer = im.getpixel((24, 128))
        inner = tuple(min(255, int(c * 1.8) + 8) for c in outer)
        return ("#%02x%02x%02x" % edge, "#%02x%02x%02x" % inner, "#%02x%02x%02x" % outer)
    return ("#6FE3FF", "#1e2638", "#06070c")


def icon(rel, style, redo):
    out = MODS / rel
    if out.exists() and not redo and not is_placeholder(out): return out
    src = ART / f"{slug(rel)}.png"
    if not src.exists(): print(f"icon {rel}: no concept yet"); return None
    if style == "painted":
        img = Image.open(src).convert("RGB").resize((S, S), Image.LANCZOS)
    else:
        edge, inner, outer = row_colours(out)
        obj = cutout(Image.open(src))
        img = background(inner, outer)
        k = int(W * 0.7) / max(obj.size)
        obj = obj.resize((max(1, int(obj.width * k)), max(1, int(obj.height * k))), Image.LANCZOS)
        pos = ((W - obj.width) // 2, (W - obj.height) // 2)
        halo = Image.new('RGBA', img.size, (0, 0, 0, 0))
        tint = Image.new('RGBA', obj.size, rgba(edge))
        tint.putalpha(obj.split()[3].filter(ImageFilter.GaussianBlur(14)).point(lambda v: int(v * 0.5)))
        halo.alpha_composite(tint, pos)
        img.alpha_composite(halo)
        img.alpha_composite(obj, pos)
        frame(img, edge)
        img = img.resize((S, S), Image.LANCZOS)
    img.save(out)
    print("wrote", out)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--stage", default="all", choices=["concept", "icon", "all"])
    ap.add_argument("--redo", action="store_true")
    a = ap.parse_args()
    only = {s.strip() for s in a.only.split(",") if s.strip()}
    todo = [i for i in ICONS if not only or slug(i[0]) in only or Path(i[0]).stem in only]
    # Skip icons already replaced by real art (a rerun after a stop only does what's left).
    if not a.redo: todo = [i for i in todo if not (MODS / i[0]).exists() or is_placeholder(MODS / i[0])]
    print(f"{len(todo)} icons to make")
    if a.stage in ("concept", "all"):
        for rel, style, col, what in todo: concept(rel, style, col, what, a.redo)
    if a.stage in ("icon", "all"):
        for rel, style, col, what in todo: icon(rel, style, a.redo)


if __name__ == "__main__":
    main()
