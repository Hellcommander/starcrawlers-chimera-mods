"""Every asset for the Taintbearer (Mods/Taintbearer), through the AAMT toolkit stages (D:\\games\\Ai assisted toolkit),
the way make_shadow_magus_assets.py does it:
  icons     - SD3.5 painted emblems (make_class_icons.py style), full bleed  -> Taintbearer/icons/<name>.png
  particles - SD3.5 key + Wan2.2 clip cut into a flipbook                    -> Taintbearer/fx/<name>.png
  wire      - writes Taintbearer/particles.json from the sheets' grids
Sounds are not made here (the class uses the game's own Void Psyker sounds).
Every stage skips work already on disk, so a stopped run picks up where it left off.
Usage: python make_taintbearer_assets.py [--stage icons,particles,wire|all] [--only name,name] [--redo]"""
import argparse, json, os, shutil, sys, time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_class_icons as mci
import make_item_assets as mia

MODS = HERE.parent / "Mods"
TB = "Taintbearer"
ROT = "black and sickly yellow-green"

# (icon name, colour words, what to draw)
ICONS = [
    ("taintbearer", ROT, "a gaunt hooded mage holding out one open hand, black veins of corruption spreading up the arm from the palm, sickly green glow seeping from the cracks"),
    ("vessel_flesh", ROT, "a human torso seen close, the skin crossed with dark cracked veins of glowing green rot, yet whole and standing firm"),
    ("seep", ROT, "a single thin thread of oily black and green corruption snaking through the air from an open hand"),
    ("open_the_vessel", ROT, "a figure with arms spread wide and head thrown back as streams of black green rot pour into its chest from every side"),
    ("pass_it_on", ROT, "two hands, one pressing a writhing knot of black green corruption into the chest of a shadowy figure"),
    ("return_to_sender", ROT, "a black green curse sigil bouncing back off an outstretched palm toward the one who threw it, motion streaks"),
    ("spillover", ROT, "a figure overflowing, black green corruption bursting outward from its body in a ring of splashing rot"),
    ("blackened_marrow", ROT, "a cracked bone glowing with black and green rot inside, dark veins branching from it"),
    ("fouled_ground", ROT, "riveted metal deck plates soaked through with spreading black green rot, sickly vapour rising from the stain"),
    ("rot_through", ROT, "a single point of blinding green light at the centre of a figure's chest, every dark vein in its body drawn toward it, about to burst"),
    ("taint", ROT, "a drop of oily black corruption with a sickly green sheen, dark veins spreading out from where it landed"),
    ("gathering_rot", ROT, "many thin threads of black green rot spiralling inward to one bright point"),
]

# make_particles recipes; white-tinted sheets take each particle's colour in particles.json
PARTICLES = [
    {"name": "tb_rot_smoke", "preset": "smoke_puff", "seed": 3301, "key_scale": 0.5, "gamma": 2.2,
     "desc": "a small dense ball of thick oily white smoke with heavy drooping billows, the whole cloud visible with black space on every side",
     "motion": "The smoke rolls and swells outward, heavy curls sagging and churning over each other. Fast, turbulent, continuous motion."},
    {"name": "tb_rot_burst", "preset": "magic_burst", "seed": 3302, "key_scale": 0.5,
     "desc": "a small knot of thick white dripping tendrils and droplets around a dark centre, like a splash of thick liquid",
     "motion": "The tendrils and droplets splash outward in every direction, stretching and dripping as they fly apart. Very fast, violent motion."},
    {"name": "tb_rot_pool", "preset": "energy_orb", "seed": 3303, "key_scale": 0.85, "blend": "alpha",
     "desc": "a round spreading stain of thick white vapour seen from directly above, branching vein-like wisps creeping out from its centre, soft feathered edges",
     "motion": "The stain creeps and churns, its vein-like wisps crawling outward and curling at the rim. Slow, continuous, clearly visible motion."},
]


def icons(todo, redo):
    os.environ.setdefault("AAMT_SD_KEEP_SERVER", "1")
    os.environ.setdefault("SD_SKIP_T5", "1")
    (MODS / TB / "icons").mkdir(parents=True, exist_ok=True)
    failed = []
    for name, col, what in todo:
        rel = f"{TB}/icons/{name}.png"
        try:
            mci.concept(rel, "painted", col, what, redo)
            mci.icon(rel, "painted", redo)
        except Exception as e:
            print(f"icon {rel} FAILED: {e}", flush=True); failed.append(name)
    if failed: raise SystemExit(f"{len(failed)} icon(s) failed: {', '.join(failed)}")


def particles(todo, redo):
    ar = mia.shared()
    work = HERE / "item_art" / "particles_tb"
    res = ar.make_particles(todo, work / "out", work=work, redo=["key", "video", "sheet"] if redo else None)
    fx = MODS / TB / "fx"
    fx.mkdir(parents=True, exist_ok=True)
    old = json.loads((work / "grids.json").read_text()) if (work / "grids.json").exists() else {}
    for x in res:
        shutil.copy2(x["texture"], fx / Path(x["texture"]).name)
        old[x["name"]] = x["sheet"]["grid"]
        print(f"{x['name']}: {x['sheet']['grid']} -> {fx / Path(x['texture']).name}  (preview {x['preview']})", flush=True)
    (work / "grids.json").write_text(json.dumps(old, indent=1))


def grid(name):
    f = HERE / "item_art" / "particles_tb" / "grids.json"
    return json.loads(f.read_text()).get(name, [6, 6]) if f.exists() else [6, 6]


def wire():
    smoke, burst, pool = "fx/tb_rot_smoke.png", "fx/tb_rot_burst.png", "fx/tb_rot_pool.png"
    gs, gb, gp = grid("tb_rot_smoke"), grid("tb_rot_burst"), grid("tb_rot_pool")
    dark, dark2, green, green2 = "#0A0E04", "#1E2A0A", "#B8E03A", "#4A6A10"
    parts = [
        # Seep and Pass It On: a knot of oily rot flying, trailing smoke (burst layers fly, rate layers trail)
        {"id": "TbSeepBolt", "light": {"color": "#8CC02A", "range": 3, "intensity": 1.1, "time": 0.4}, "layers": [
            {"texture": "soft", "blend": "add", "count": 1, "life": [0.6, 0.6], "speed": [0, 0], "size": [0.55, 0.55], "color": green, "color2": green2, "sizeCurve": "flat", "shape": "point", "alpha": 0.8},
            {"texture": smoke, "blend": "add", "count": 1, "life": [0.6, 0.6], "speed": [0, 0], "size": [0.5, 0.5], "color": green, "color2": green2, "grid": gs, "sizeCurve": "flat", "shape": "point", "randomFrame": True, "spin": [-200, 200], "alpha": 0.6},
            {"texture": smoke, "blend": "alpha", "count": 1, "life": [0.6, 0.6], "speed": [0, 0], "size": [0.3, 0.3], "color": dark, "color2": dark2, "grid": gs, "sizeCurve": "flat", "shape": "point", "randomFrame": True, "spin": [-260, 260], "alpha": 0.85},
            {"texture": smoke, "blend": "alpha", "count": 0, "life": [0.35, 0.6], "speed": [0.05, 0.25], "size": [0.14, 0.3], "color": dark, "color2": dark2, "grid": gs, "rate": 50, "duration": 2, "radius": 0.06, "randomFrame": True, "sizeCurve": "grow", "alpha": 0.8, "spin": [-50, 50], "shape": "sphere"},
            {"texture": "mote", "blend": "add", "count": 0, "life": [0.3, 0.5], "speed": [0.1, 0.4], "size": [0.02, 0.04], "color": green, "color2": green2, "rate": 60, "duration": 2, "radius": 0.1, "shape": "sphere"},
            {"texture": smoke, "blend": "add", "count": 0, "life": [0.3, 0.5], "speed": [0.05, 0.2], "size": [0.15, 0.3], "color": green2, "color2": "#1A2A06", "grid": gs, "rate": 35, "duration": 2, "radius": 0.06, "randomFrame": True, "sizeCurve": "grow", "alpha": 0.5, "shape": "sphere"}]},
        # impacts, Spillover, Rot Through: rot splashing out of a host
        {"id": "TbRotBurst", "light": {"color": "#8CC02A", "range": 4, "intensity": 1.5, "time": 0.5}, "layers": [
            {"texture": smoke, "blend": "alpha", "count": 9, "life": [0.5, 0.9], "speed": [0.6, 1.4], "size": [0.4, 0.7], "color": dark, "color2": dark2, "grid": gs, "radius": 0.2, "drag": 2.2, "sizeCurve": "grow", "alpha": 0.85, "spin": [-50, 50], "shape": "sphere"},
            {"texture": burst, "blend": "add", "count": 1, "life": [0.5, 0.55], "speed": [0, 0], "size": [2.0, 2.3], "color": green, "color2": green2, "grid": gb, "sizeCurve": "flat", "shape": "point"},
            {"texture": smoke, "blend": "add", "count": 6, "life": [0.4, 0.7], "speed": [0.5, 1.2], "size": [0.4, 0.7], "color": green, "color2": green2, "grid": gs, "radius": 0.2, "drag": 2.2, "sizeCurve": "grow", "alpha": 0.55, "spin": [-50, 50], "shape": "sphere"},
            {"texture": "soft", "blend": "add", "count": 1, "life": [0.3, 0.3], "speed": [0, 0], "size": [1.4, 1.4], "color": green, "color2": green2, "sizeCurve": "shrink", "shape": "point", "alpha": 0.7},
            {"texture": "mote", "blend": "add", "count": 30, "life": [0.4, 0.8], "speed": [0.8, 2.0], "size": [0.03, 0.07], "color": green, "color2": green2, "radius": 0.15, "drag": 1.5, "gravity": 0.6, "shape": "sphere"}]},
        # Fouled Ground: a creeping stain on the deck
        {"id": "TbFouledPool", "layers": [
            {"texture": pool, "blend": "alpha", "count": 0, "life": [1.4, 1.8], "speed": [0, 0], "size": [1.2, 1.3], "color": dark, "color2": dark2, "grid": gp, "randomFrame": True, "rate": 1.5, "duration": 1.0, "sizeCurve": "pulse", "alpha": 0.8, "shape": "point", "flat": True, "ground": True, "spin": [-20, 20]},
            {"texture": pool, "blend": "add", "count": 0, "life": [1.2, 1.6], "speed": [0, 0], "size": [1.3, 1.4], "color": "#7AB020", "color2": green2, "grid": gp, "randomFrame": True, "rate": 1, "duration": 1.0, "sizeCurve": "pulse", "alpha": 0.7, "shape": "point", "flat": True, "ground": True, "spin": [-20, 20]},
            {"texture": smoke, "blend": "alpha", "count": 0, "life": [1.0, 1.6], "speed": [0.1, 0.25], "size": [0.2, 0.4], "color": "#1E2A0A", "color2": "#0A0E04", "grid": gs, "rate": 4, "duration": 1.0, "randomFrame": True, "sizeCurve": "grow", "alpha": 0.4, "shape": "disc", "radius": 0.6}]},
        # casting, the Taint on a host, Gathering Rot: threads of rot drawn in
        {"id": "TbRotDraw", "layers": [
            {"texture": smoke, "blend": "alpha", "count": 0, "life": [0.5, 0.8], "speed": [0, 0], "radial": -1.4, "size": [0.12, 0.24], "color": dark, "color2": dark2, "grid": gs, "rate": 18, "duration": 1.0, "radius": 0.6, "randomFrame": True, "sizeCurve": "shrink", "alpha": 0.75, "shape": "sphere"},
            {"texture": "mote", "blend": "add", "count": 0, "life": [0.4, 0.7], "speed": [0, 0], "radial": -1.8, "size": [0.02, 0.04], "color": green, "color2": green2, "rate": 40, "duration": 1.0, "radius": 0.7, "shape": "sphere", "alpha": 1}]},
    ]
    out = MODS / TB / "particles.json"
    out.write_text(json.dumps({"particles": parts}, indent=1), encoding="utf-8")
    print("wrote", out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", default="all")
    ap.add_argument("--only", default="")
    ap.add_argument("--redo", action="store_true")
    a = ap.parse_args()
    stages = ["icons", "particles", "wire"] if a.stage == "all" else a.stage.split(",")
    only = set(x for x in a.only.split(",") if x)
    for st in stages:
        t = time.time(); print("===", st, flush=True)
        if st == "icons": icons([r for r in ICONS if not only or r[0] in only], a.redo)
        elif st == "particles": particles([r for r in PARTICLES if not only or r["name"] in only], a.redo)
        elif st == "wire": wire()
        print(f"=== {st} done in {time.time() - t:.0f}s", flush=True)


if __name__ == "__main__":
    main()
