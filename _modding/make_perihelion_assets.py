"""Perihelion's own art (Mods/Perihelion), through the AAMT toolkit stages (D:\\games\\Ai assisted toolkit), the way
make_shadow_magus_assets.py does it:
  particles - SD3.5 key + Wan2.2 clip cut into a flipbook, or SD sprites             -> Perihelion/fx/<name>.png
  mesh      - SD3.5 concept on white, then TRELLIS and mesh_edit's repair            -> _modding/item_art/mesh/<slug>.glb
  props     - the nucleus that Ephemeris drops, repaired and slimmed                 -> Perihelion/models/ph_nucleus.glb
  animate   - UniRig + Wan2.2 + CoTracker3 clips for the body inside the comet       -> Perihelion/models/ph_comet_body.glb
Every stage skips work already on disk, so a stopped run picks up where it left off.
Usage: python make_perihelion_assets.py [--stage particles,mesh,props,animate|all] [--only name,name] [--redo]"""
import argparse, shutil, sys, time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_interloper_assets as mi
import make_item_assets as mia

MODS = HERE.parent / "Mods"
PH = "Perihelion"

# make_particles recipes; white-tinted sheets take each particle's colour in particles.json
PARTICLES = [
    {"name": "ph_head", "preset": "energy_orb", "seed": 411, "key_scale": 0.5, "gamma": 3.0,
     "desc": "a comet head seen from directly in front, a small bright core inside a dim round cloud of wispy glowing gas, fine ragged filaments of vapour radiating evenly in all directions, floating alone in empty black space",
     "motion": "The glowing gas boils and streams around the bright core, jets of vapour bursting off its surface and curling away. Fast, turbulent, continuous motion; it stays whole and centred."},
    {"name": "ph_plume", "preset": "smoke_puff", "seed": 312, "key_scale": 0.5, "blend": "add", "gamma": 1.6,
     "desc": "a small ragged clot of glowing white vapour with fine streaming filaments, the whole clot visible with black space on every side",
     "motion": "The glowing vapour tears apart and streams outward, filaments stretching and curling as it spreads. Fast, continuous motion."},
    {"name": "ph_blast", "preset": "explosion", "seed": 313, "key_scale": 0.45, "tint": "white", "grow_in": 1,
     "desc": "a small white-hot impact flash, a dense core of light with jagged tongues of fire and flying sparks, black space on every side",
     "motion": "The flash erupts outward violently, tongues of fire and sparks tearing away from the centre in every direction. Very fast, violent, continuous motion."},
    {"name": "ph_shock", "preset": "impact", "seed": 314, "key_scale": 0.45, "grow_in": 0,
     "desc": "a thin jagged ring of white shockwave seen from directly above, cracked rough edges, torn streaks of dust around it, a dark empty centre",
     "motion": "The ring tears outward from the centre, widening fast, its jagged edge breaking up into streaks. Very fast, continuous motion."},
    {"name": "ph_dust", "preset": "smoke_puff", "seed": 315, "key_scale": 0.5, "gamma": 2.0,
     "desc": "a small dense ball of thick white rock dust with heavy billows, the whole cloud visible with black space on every side",
     "motion": "The dust billows outward and rolls over itself, heavy clumps tumbling as the cloud swells to several times its size. Fast, turbulent, continuous motion."},
    {"name": "ph_orbit_mark", "preset": "energy_orb", "seed": 316, "key_scale": 0.85, "blend": "add",
     "desc": "a star chart drawn in thin white lines of light seen from directly above, three tilted thin elliptical orbit rings crossing around a tiny bright point, small tick marks along the rings",
     "motion": "The thin orbit rings rotate slowly around the bright centre and small sparks of light travel along them. Steady, continuous motion; the rings stay thin and sharp."},
    {"name": "ph_tidal", "preset": "energy_orb", "seed": 317, "key_scale": 0.85, "blend": "add",
     "desc": "a whirlpool of fine white dust streaks seen from directly above, long thin curved arms spiralling into a dark hollow centre, soft feathered edges",
     "motion": "The spiral arms of dust wind quickly inward and vanish into the dark centre, rotating around it. Fast, continuous, flowing motion."},
    {"name": "ph_jet", "preset": "flame_loop", "seed": 318, "key_scale": 0.6, "tint": "white",
     "desc": "one narrow violent jet of white burning gas blasting straight upward from a small crack, a thin tapering plume with ragged wisps",
     "motion": "The jet of burning gas blasts upward hard, sputtering and whipping, wisps tearing off its sides. Fast, violent, continuous motion."},
    {"name": "ph_calve", "preset": "magic_burst", "seed": 319, "key_scale": 0.5, "grow_in": 1,
     "desc": "a glowing round shell of thin white light cracked across by bright jagged fracture lines, small plates lifting off it, a brilliant core showing through the cracks",
     "motion": "The cracks race across the shell and it breaks apart, plates of light flying outward from the bright core. Very fast, sudden, continuous motion."},
    {"name": "ph_bits", "preset": "shards",  "seed": 320,
     "desc": "a single jagged chunk of dirty comet ice, dark grey rock frozen into pale blue-white ice, rough broken faces, frost on its edges"},
]

# make_interloper_assets.ASSETS rows: (mod, slug, accent, kind, body plan, prompt)
MESHES = [
    (PH, "ph_comet_body", "#BFE8FF", "creature", "upright",
     # A figure of "light through ice" came back see-through and TRELLIS made it a hollow shell: ask for solid matte stone. "Statue" adds a pedestal, so it is a golem.
     "solid opaque golem, a heavy featureless humanoid creature standing barefoot straight, arms held away from its sides, legs apart, thick body of matte dark grey rock "
     "crusted with white frost, thin glowing orange cracks in the rock, no face, plain simple hands, rough stone surface, front view"),
    (PH, "ph_nucleus", "#BFE8FF", "prop", None,
     "comet nucleus, a lumpy potato-shaped boulder of dark frozen rock crusted with dirty pale blue ice, deep pits and vents, bright glowing cracks, "
     "no flames, solid object"),
]
mi.TRELLIS_PROPS = set(mi.TRELLIS_PROPS) | {"ph_nucleus"}

# animate stage: "idle" is what the dash form shows (nothing drives it but the idle loop), so idle is the flight
ANIM = {
    "ph_comet_body": ("a humanoid figure of cracked ice and dark rock lit from within", "biped", {
        "idle": "{it} leans hard forward with both arms swept back behind it and its legs trailing, its body shuddering with speed and its shoulders shaking, head down. Fast, continuous, shaking movement.",
        "attack": "{it} leans far to one side and drives its right fist across its body in a huge punch, then straightens. Fast, energetic movement.",
        "special": "{it} drops into a crouch and throws both arms out to the sides, its chest heaving, then straightens. Fast, energetic movement.",
        "hit": "{it} flinches, its shoulders jerking back and its head snapping back, feet planted on the ground, then straightens. Fast, sudden movement.",
        "move": "{it} marches on the spot, lifting each knee in turn and swinging its arms, keeping its upper body level and its head still. Steady rhythmic movement."}),
}


def particles(todo, redo):
    ar = mia.shared()
    work = HERE / "item_art" / "particles_ph"
    res = ar.make_particles(todo, work / "out", work=work, redo=["key", "video", "sheet"] if redo else None)
    fx = MODS / PH / "fx"
    fx.mkdir(parents=True, exist_ok=True)
    for x in res:
        shutil.copy2(x["texture"], fx / Path(x["texture"]).name)
        print(f"{x['name']}: {x['sheet']['grid']} -> {fx / Path(x['texture']).name}  (preview {x['preview']})", flush=True)


def meshes(todo, redo):
    for (_, slug, _, kind, _, what) in todo:
        try: mi.concept(slug, kind, what, redo)
        except Exception as e: print(f"concept {slug} FAILED: {e}", flush=True)
    try:
        from sd_server_lifecycle import stop_sd_server
        stop_sd_server()
    except Exception as e: print("stop sd:", e)
    mi.meshes(todo, redo)


def props(todo, redo):
    for (mod, slug, _, kind, _, _) in todo:
        src, out = mi.MESH_DIR / f"{slug}.glb", MODS / mod / "models" / f"{slug}.glb"
        if slug in ANIM or not src.exists() or (out.exists() and not redo): continue
        out.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, out)
        mia.slim(out)
        print(f"model {slug}: {out.stat().st_size // 1024} KB", flush=True)


def animate(todo, redo):
    mi.ANIM.update(ANIM)
    for (mod, slug, _, kind, body, _) in todo:
        if slug not in ANIM: continue
        try: mi.animate(MODS, mod, slug, body, redo)
        except Exception as e: print(f"animate {slug} FAILED: {e}", flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", default="all")
    ap.add_argument("--only", default="")
    ap.add_argument("--redo", action="store_true")
    a = ap.parse_args()
    stages = ["particles", "mesh", "props", "animate"] if a.stage == "all" else a.stage.split(",")
    only = {s for s in a.only.split(",") if s}
    for st in stages:
        t = time.time()
        print(f"=== {st}", flush=True)
        if st == "particles": particles([r for r in PARTICLES if not only or r["name"] in only], a.redo)
        else:
            rows = [r for r in MESHES if not only or r[1] in only]
            {"mesh": meshes, "props": props, "animate": animate}[st](rows, a.redo)
        print(f"=== {st} done in {time.time() - t:.0f}s", flush=True)


if __name__ == "__main__":
    main()
