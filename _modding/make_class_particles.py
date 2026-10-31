"""Per-class particle textures for SkillVfx, so no two classes' skills share the same look (and none borrow vanilla
sprites): through the AAMT particle stage (particle_client.py), each class gets
  <class>_burst - its own impact/flare flipbook (SD key -> Wan2.2 clip -> atlas), white so the class palette tints it
  <class>_bits  - a sheet of its own debris sprites (SD only), in the class's own material
make_skill_vfx.py uses them in place of the shared flash_burst / magic_flare / rubble sheets where they exist.
  work  -> _modding/item_art/particles/class/<name>/      atlas -> Mods/SkillVfx/fx/<name>.png
Every stage skips work on disk. Usage: python make_class_particles.py [--only castborn,etcher] [--kind burst,bits] [--redo key,video,sheet --reseed 100]"""
import argparse, shutil, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
AAMT = Path(r"D:\games\Ai assisted toolkit\Tools\Shared")
ART = HERE / "item_art" / "particles" / "class"
FX = HERE.parent / "Mods" / "SkillVfx" / "fx"

# class slug -> (what its impact flash looks like, what flies off when it hits). Our own words, from each class's theme.
THEMES = {
    "castborn": ("a burst of white-hot molten metal, splashing droplets of liquid slag flung outward", "a single glowing chunk of cooling slag metal with a molten orange core"),
    "coilwright": ("a crackling burst of capacitor discharge, a ring of jagged electric sparks around a bright core", "a single small copper wire coil with a frayed sparking end"),
    "emberhorn": ("a burst of furnace fire shaped like a charging bull's horns, two curved flame plumes", "a single glowing red-hot ember coal with cracks of fire"),
    "entropy_weaver": ("a burst of unravelling energy, frayed glowing threads whipping outward chaotically", "a single twisted shard of crystal that seems to unravel at its edges"),
    "etcher": ("a hissing splash of acid, droplets spattering outward with curls of corrosive vapour", "a single drop of glowing green acid with a corroded metal flake"),
    "gloomspore_warden": ("a burst of spores, a puff of glowing dust from a bursting fungal pod", "a single round fungal spore pod with tiny tendrils"),
    "graftwild": ("a burst of splintered wood and leaves, thorny vines lashing outward", "a single thorny wooden splinter with a small green leaf"),
    "gravemote_astronomer": ("a gravitational shockwave, light bending inward then rippling out around a dark point", "a single small dark stone mote orbited by a faint ring of light"),
    "rimelight_seer": ("a burst of frost and starlight, ice crystals blooming outward in a star pattern", "a single sharp translucent ice crystal shard"),
    "runeforger": ("a burst of hammered sparks around a glowing rune, smith's sparks flying", "a single small metal plate engraved with a glowing rune"),
    "scrapper": ("a burst of scrap impact, rusted metal fragments and sparks exploding outward", "a single rusty bent bolt and twisted scrap of sheet metal"),
    "squallblade": ("a burst of compressed air, swirling white gust lines spiralling outward", "a single wisp of curled white wind, a small spiral of air"),
    "tidewright": ("a spherical splash of clear water droplets exploding outward from one point, isolated in a black void, no ground, no container", "a single droplet of clear blue water mid-splash"),
    "fulgurant": ("a forked lightning strike flash, jagged white-blue bolts branching outward from a bright core", "a single small jagged shard of crackling blue lightning"),
    "snareseer": ("a burst of violet psychic energy, a glowing eye-shaped sigil flashing inside rippling rings", "a single small glowing violet glyph fragment"),
    "mimeticist": ("a burst of mutating flesh and green bio-luminous spray, claws and scales flaring outward", "a single small glowing green scale with a bony spine"),
    "bulkhead_psion": ("a burst of torn steel plate fragments and rivets flung outward by a blue psychic shockwave", "a single jagged fragment of riveted grey steel plate"),
    "reactant_hauler": ("a messy chemical splash burst, oil, sparks and frost vapour spraying outward together", "a single dented small metal canister lid with a splash of liquid"),
    "miasmancer": ("a billowing burst of toxic green vapour and droplets swirling outward", "a single glowing green droplet of toxin with a wisp of vapour"),
    "wildcaster": ("a round flash of prismatic magic bursting outward from one bright centre, sparks of many colours radiating out on every side", "a single small spinning shard of iridescent rainbow crystal"),
    "vasculist": ("a burst of blood mist, a ruptured spray of red droplets", "a single drop of dark red blood"),
    "conjoined": ("a burst of pulsing flesh-pink energy with branching nerve-like filaments", "a single small pink cell-like blob with a glowing nucleus"),
    "corsair": ("a burst of golden sparks and a flash of cutlass steel", "a single gold doubloon coin with a skull stamp"),
    "demolitionist": ("a sharp detonation flash, a shockwave ring with flying sparks", "a single scorched metal shrapnel fragment"),
    "gun_kata": ("a muzzle flash, a star-shaped burst of gunfire light", "a single spent brass bullet casing"),
    "juggernaut": ("a heavy impact shockwave, a dust ring pushed out from a crushing blow", "a single dented chunk of armour plate"),
    "longshot": ("a sharp laser impact, a narrow beam flare with a bright pinpoint centre", "a single glowing red-hot lens fragment"),
    "mad_tinkerer": ("a sputtering gadget pop, a puff of sparks and springs", "a single small brass gear cog"),
    "nanite_herder": ("a burst of nanite swarm, a cloud of glittering tiny particles dispersing", "a single tiny metallic nanite bead, a speck of silver"),
    "phase_drifter": ("a phase blink flash, a shimmering ghostly afterimage splitting apart", "a single fragment of glitching translucent light, a shard of a broken reflection"),
    "psi_knight": ("a psionic burst, a ripple of blue mental force around a blade flash", "a single small blue glowing psi crystal"),
    "puppetmaster": ("a burst of glowing psychic strings snapping taut and whipping outward", "a single loose glowing pink thread with a knot"),
    "relic_bearer": ("a burst of whispering idol energy, an eye-shaped flare of crimson and gold light", "a single small carved stone idol fragment with a red gem"),
    "ringmaster": ("a burst of confetti and stage spotlight sparkle", "a single piece of red and gold circus confetti"),
    "shadow_broker": ("a burst of glowing violet shadow wisps, curling smoky tendrils of pale purple light",  # bursts are added light: black draws nothing
                      "a single shard of dark violet shadow glass"),
    "splice_host": ("a burst of mutagen, bubbling green goo spattering outward", "a single sharp bony spine dripping green mutagen"),
    "void_hacker": ("a glitch burst, a blocky cluster of corrupted pixels and scanlines", "a single blocky glitched data fragment, a magenta and cyan pixel cube"),
    "arc_gunner": ("a forked lightning strike flash, branching bolts from a bright point", "a single small glowing capacitor cell"),
    "flux_savant": ("a burst of retuned energy, a prism splitting light into rune-like rays", "a single small faceted rune crystal glowing violet and green"),
    "blade_juggler": ("a burst of spinning knife glints, crescent slashes of light", "a single small throwing knife"),
    "bodyguard": ("a glowing energy shield flash, thin bright hexagon lines rippling outward from a bright core, floating in empty space", "a single small hexagonal shield plate fragment"),
    "freerunner": ("a burst of kinetic speed lines, streaks radiating from a dash", "a single chip of concrete kicked loose"),
    "grapnel_acrobat": ("a burst of a taut cable snapping, a line whipping in an arc", "a single small steel grappling hook"),
    "grappler": ("a heavy grapple impact, a dust burst from a body slam", "a single chunk of a broken cargo crate"),
    "rig_jockey": ("a hydraulic exhaust blast, steam and sparks venting from a piston", "a single hydraulic bolt with a scorched thread"),
    "trick_shot": ("a ricochet spark burst off a spinning coin", "a single square brass gambling token with a round hole punched through its centre, plain, no lettering"),  # a round gold chit read as a Bitcoin
    "odds_breaker": ("a burst of luck, glittering green and gold casino sparkle", "a single casino chip"),
    "reaction_architect": ("a tripwire trigger flash, thin laser lines snapping and sparking", "a single small blueprint circuit node"),
    "swarm_conductor": ("a signal pulse, concentric radio-wave rings with a drone light", "a single tiny drone rotor blade"),
    "axiomancer": ("a burst of luminous thought, geometric proof lines and symbols flaring outward", "a single small glowing geometric polyhedron"),
    "glyphrunner": ("a burst of ancient code sigils, cyan glyph strings radiating outward", "a single small cyan glowing sigil tile"),
    "null_chorister": ("a hollow resonance burst, a ring of silent violet sound waves around nothing", "a single curved shard of a cracked bronze bell with a faint violet glow on its edge"),  # tuning forks read as dinner forks
    "paradox_witch": ("a time-skip flash, a clock-face ring of light rewinding", "a single small brass clock hand"),
    "probability_shaman": ("a branching burst of possibility, many faint ghost-paths fanning out", "a single small four-leaf token of green light"),
    "reliquary_knight": ("a holy relic flare, a radiant golden halo with beams", "a single small golden reliquary shard with engraving"),
    "starfire_adept": ("a burst of stellar plasma, a miniature solar flare with arcing prominences", "a single small glowing fragment of star plasma"),
}


def recipes(only, kinds, reseed=0):
    for slug, (burst, bits) in THEMES.items():
        if only and slug not in only:
            continue
        seed = sum(map(ord, slug)) % 997 + reseed
        if "burst" in kinds:
            yield {"name": f"{slug}_burst", "preset": "impact", "seed": seed, "desc": burst}
        if "bits" in kinds:
            yield {"name": f"{slug}_bits", "preset": "shards", "seed": seed + 1, "desc": bits}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--kind", default="burst,bits")
    ap.add_argument("--reseed", type=int, default=0, help="seed offset, with --redo, for takes that came out glitched")
    ap.add_argument("--redo", default="", help="stages to redo: key,video,sheet")
    a = ap.parse_args()
    only = {s for s in a.only.split(",") if s}
    sys.path.insert(0, str(AAMT))
    from particle_client import make_particles
    rs = list(recipes(only, set(a.kind.split(",")), a.reseed))
    redo = [x for x in a.redo.split(",") if x] or None
    FX.mkdir(parents=True, exist_ok=True)
    # a batch per handful, so a stopped night still leaves finished classes in place
    for i in range(0, len(rs), 6):
        for x in make_particles(rs[i:i + 6], ART / "out", work=ART, redo=redo):
            shutil.copy2(x["texture"], FX / Path(x["texture"]).name)
            print(f"{x['name']}: {x['sheet']['grid']} -> {FX / Path(x['texture']).name}", flush=True)


if __name__ == "__main__":
    main()
