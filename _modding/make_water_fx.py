"""Water effects, built the way the wisps are (make_wisp_assets.py): the video model draws calm looping textures well
(a swirling orb, a drifting haze) but invents scenery around one-shot splashes (boxes, glass, a floor). So water
gets only looping textures from the AAMT particle stage, and its splashes, bolts and whirlpools are assembled from
them by make_skill_vfx.py's water archetypes (waterbolt, watersplash, whirlpool, flood), together with a class's own
droplet sheet (make_class_particles.py "bits").
  water_orb   - a ball of seawater swirling in place, the head of a water bolt          (energy_orb preset, loops)
  water_spray - a soft cloud of fine sea spray                                          (smoke_puff preset)
Atlases -> Mods/SkillVfx/fx/<name>.png; work in _modding/item_art/particles/water/. One GPU job, stages skip work on
disk. Usage: python make_water_fx.py [--only water_orb] [--redo key,video,sheet]"""
import argparse, shutil, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
AAMT = Path(r"D:\games\Ai assisted toolkit\Tools\Shared")
ART = HERE / "item_art" / "particles" / "water"
FX = HERE.parent / "Mods" / "SkillVfx" / "fx"

# Our own words. Keep them alive and in place: never "fade into darkness", never a splash event.
RECIPES = [
    {"name": "water_orb", "preset": "energy_orb", "seed": 31,
     "desc": "a ball of clear sea water floating in empty black space, its rippling surface catching bright highlights, "
             "small bubbles turning inside it, white and pale blue, no container, no ground",
     "motion": "The ball of water churns and swirls in place, its surface rippling and wobbling while bubbles circle "
               "inside it. Continuous, gentle motion, it stays whole and centred."},
    {"name": "water_spray", "preset": "smoke_puff", "seed": 32,
     "desc": "a soft round cloud of fine white sea spray, a haze of tiny glittering droplets, no solid surface, "
             "floating in empty black space, no ground",
     "motion": "The cloud of spray drifts and billows gently outward, droplets glittering as it slowly spreads and thins."},
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--redo", default="")
    a = ap.parse_args()
    only = {s.strip() for s in a.only.split(",") if s.strip()}
    todo = [r for r in RECIPES if not only or r["name"] in only]
    if str(AAMT) not in sys.path: sys.path.insert(0, str(AAMT))
    from particle_client import make_particles
    res = make_particles(todo, ART / "out", work=ART, redo=[s for s in a.redo.split(",") if s])
    FX.mkdir(parents=True, exist_ok=True)
    for x in res:
        shutil.copy2(x["texture"], FX / Path(x["texture"]).name)
        print(f"{x['name']}: {x['sheet']['grid']} -> {FX / Path(x['texture']).name}  (preview {x['preview']})", flush=True)
    print("done", flush=True)


if __name__ == "__main__":
    main()
