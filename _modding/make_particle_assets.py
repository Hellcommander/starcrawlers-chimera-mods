"""Generated particle textures for SkillVfx, made by the AAMT particle stage (D:\\games\\Ai assisted toolkit\\Tools\\Shared\\
particle_client.py): SD3.5 draws each effect at its peak, Wan2.2 sets it in motion, and the frames are cut into a
flipbook atlas (or, for debris and sigils, a sheet of separate sprites that particles pick from at random).
  work   - key frames, clips and per-effect notes  -> _modding/item_art/particles/<name>/
  sheet  - atlas, preview, gif, emitter layer      -> _modding/item_art/particles/out/
  copy   - the atlas the game loads                -> Mods/SkillVfx/fx/<name>.png
make_skill_vfx.py swaps its built-in sprites for these where FX_TEX names one and the file exists.
Every stage skips work already on disk. Usage: python make_particle_assets.py [--only name,name] [--redo key|video|sheet]"""
import argparse, shutil, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
AAMT = Path(r"D:\games\Ai assisted toolkit\Tools\Shared")
ART = HERE / "item_art" / "particles"
FX = HERE.parent / "Mods" / "SkillVfx" / "fx"

# Our own descriptions; presets supply the motion, blend and cut. White-tinted sheets take each class's palette.
RECIPES = [
    {"name": "fire_blast", "preset": "explosion", "seed": 3},
    {"name": "flash_burst", "preset": "impact", "seed": 2,
     "desc": "a bright white energy impact flash, a star-shaped burst of light with radiating spikes and fine sparks"},
    {"name": "smoke_puff", "preset": "smoke_puff", "seed": 4},
    {"name": "arc_crackle", "preset": "electric", "seed": 5,
     "desc": "a crackling cluster of white electric arcs, jagged forked lightning bolts radiating from a bright centre"},
    {"name": "magic_flare", "preset": "magic_burst", "seed": 106,
     # the first take (seed 6) had boxes and pixel noise in its middle frames
     "desc": "a round flare of glowing white magical light, a bright soft centre inside a swirling ring of light with "
             "small sparkles, floating alone in empty black space, no ground, no objects",
     "motion": "The ring of light swells gently outward and slowly turns while the sparkles drift away from the centre. "
               "Smooth, continuous motion; it stays whole and centred."},
    {"name": "plasma_orb", "preset": "energy_orb", "seed": 7},
    {"name": "flame_lick", "preset": "flame_loop", "seed": 8},
    {"name": "rubble", "preset": "shards", "seed": 9,
     "desc": "a single jagged chunk of broken grey stone and rusted metal debris, sharp angular fragment"},
    {"name": "sigil", "preset": "glyph", "seed": 10,
     "desc": "a glowing white circular arcane sigil, concentric rings of runes and geometric lines drawn in light, "
             "seen from directly above"},
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    ap.add_argument("--redo", help="comma list of key,video,sheet")
    ap.add_argument("--stage", help="comma list of key,video,sheet (default all)")
    a = ap.parse_args()
    sys.path.insert(0, str(AAMT))
    from particle_client import make_particles
    rs = [r for r in RECIPES if not a.only or r["name"] in a.only.split(",")]
    res = make_particles(rs, ART / "out", work=ART, only=a.stage.split(",") if a.stage else None,
                         redo=a.redo.split(",") if a.redo else None)
    FX.mkdir(parents=True, exist_ok=True)
    for x in res:
        shutil.copy2(x["texture"], FX / Path(x["texture"]).name)
        print(f"{x['name']}: {x['sheet']['grid']} -> {FX / Path(x['texture']).name}  (preview {x['preview']})")


if __name__ == "__main__":
    main()
