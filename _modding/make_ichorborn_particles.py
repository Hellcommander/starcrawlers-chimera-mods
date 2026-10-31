"""Ichorborn's own particle textures through the AAMT particle stage (particle_client.py): SD3.5 key -> Wan2.2 clip ->
flipbook atlas (or a sheet of cut-out sprites), written to Mods/ArendethClasses/fx/ for ichorborn_fx.json.
  work -> _modding/item_art/particles/ichorborn/<name>/      atlas -> Mods/ArendethClasses/fx/<name>.png
Every stage skips work on disk. Usage: python make_ichorborn_particles.py [--only ich_splash,...] [--redo key,video,sheet]"""
import argparse, shutil, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
AAMT = Path(r"D:\games\Ai assisted toolkit\Tools\Shared")
ART = HERE / "item_art" / "particles" / "ichorborn"
FX = HERE.parent / "Mods" / "ArendethClasses" / "fx"

# Our own words for the doctor's corrupted ichor: black-crimson liquid, void-violet light, shadow wings.
RECIPES = [
    {"name": "ich_splash", "preset": "smoke_puff", "seed": 211,
     "desc": "a thick splash of glossy black liquid ichor bursting outward from one point, heavy droplets and strands "
             "flung in every direction, floating alone in empty black space, no ground"},
    {"name": "ich_puddle", "preset": "shards", "seed": 212,
     "desc": "a single glossy puddle of black ichor with a dark crimson sheen and a few droplets, seen from directly above"},
    {"name": "ich_tar", "preset": "shards", "seed": 220,
     "desc": "a single thick irregular blob of black tar with a lumpy dripping silhouette, heavy and viscous, dull oily sheen"},
    {"name": "ich_bits", "preset": "shards", "seed": 213,
     "desc": "a single torn wet scrap of black leathery wing membrane dripping crimson ichor"},
    {"name": "ich_beam", "preset": "energy_orb", "seed": 214,
     "desc": "a pulsing orb of black void energy wrapped in bright violet light, thin dark tendrils curling around it"},
    {"name": "ich_eye", "preset": "magic_burst", "seed": 215,
     "desc": "a huge glowing eye of white and violet light opening inside a ring of dark swirling energy, floating alone "
             "in empty black space"},
    {"name": "ich_rift", "preset": "flame_loop", "seed": 216,
     "desc": "a swirling vortex rift torn in the air, crimson and violet energy spiralling into a black centre, "
             "floating alone in empty black space, no ground"},
    {"name": "ich_storm", "preset": "electric", "seed": 217,
     "desc": "a ring of crackling violet-white lightning arcs forming a jagged crown, bolts forking outward"},
    {"name": "ich_claw", "preset": "impact", "seed": 218,
     "desc": "three curved glowing claw slash streaks side by side, a bright raking swipe with flying sparks"},
    {"name": "ich_molt", "preset": "magic_burst", "seed": 219,
     "desc": "two huge spectral wings of shadow and violet light unfurling outward from a bright centre, feathers of "
             "dark light scattering, floating alone in empty black space",
     "motion": "The two wings sweep open and outward from the centre while dark feathers of light drift away. Smooth, "
               "continuous motion; it stays whole and centred."},
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--redo", default="")
    a = ap.parse_args()
    sys.path.insert(0, str(AAMT))
    from particle_client import make_particles
    only = {s for s in a.only.split(",") if s}
    rs = [r for r in RECIPES if not only or r["name"] in only]
    FX.mkdir(parents=True, exist_ok=True)
    for x in make_particles(rs, ART / "out", work=ART, redo=[s for s in a.redo.split(",") if s] or None):
        shutil.copy2(x["texture"], FX / Path(x["texture"]).name)
        print(f"{x['name']}: {x['sheet']['grid']} -> {FX / Path(x['texture']).name}  (preview {x['preview']})", flush=True)


if __name__ == "__main__":
    main()
