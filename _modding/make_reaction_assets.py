"""Art for the Status Synergies mod's reactions (Mods/StatusSynergies), through the AAMT toolkit stages
(D:\\games\\Ai assisted toolkit), the way make_class_icons.py and make_comet_breaker_assets.py do it:
  icons     - SD3.5 painted emblem on black for each reaction synergy and reaction status  -> StatusSynergies/icons/<name>.png
  particles - SD3.5 key + Wan2.2 clip cut into a flipbook for the reaction flashes          -> StatusSynergies/fx/<name>.png
              and the emitter entries for them                                               -> StatusSynergies/particles.json
Every stage skips work already on disk, so a stopped run picks up where it left off. One process, one GPU job.
Usage: python make_reaction_assets.py [--stage icons,particles|all] [--only name,name] [--redo]"""
import argparse, json, shutil, sys, time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_class_icons as mci
import make_item_assets as mia

MODS = HERE.parent / "Mods"
SS = "StatusSynergies"

# (icon path under Mods, style, colour words, what to draw) - make_class_icons rows
ICONS = [
    ("StatusSynergies/icons/shatter_point.png", "painted", "orange fire and ice blue", "a block of ice with fire on one side, split by a jagged glowing crack running through the middle"),
    ("StatusSynergies/icons/flashover.png", "painted", "sickly green and bright orange", "a cloud of green toxic vapour catching fire, a ring of flame bursting outward from its heart"),
    ("StatusSynergies/icons/conduction.png", "painted", "pale ice blue and electric white", "a bolt of lightning leaping along a chain of frozen icicles, arcs jumping from one to the next"),
    ("StatusSynergies/icons/arc_flash.png", "painted", "blinding white, electric blue and orange", "a blinding electric flash bursting from a flame, forking arcs and a ring of glare"),
    ("StatusSynergies/icons/convulsion.png", "painted", "electric blue and blood red", "a deep wound torn open wide by jagged lightning running through it"),
    ("StatusSynergies/icons/black_ice.png", "painted", "ice blue and dark violet", "a figure slipping on a sheet of black ice, feet thrown upward, frost crystals scattering"),
    ("StatusSynergies/icons/septic_wound.png", "painted", "sickly green and dark red", "an open wound seeping green poison, veins of sickly light spreading from it"),
    ("StatusSynergies/icons/overload.png", "painted", "electric blue and target red", "a targeting reticle struck by lightning, arcs leaping from it to three smaller reticles"),
    ("StatusSynergies/icons/brittle.png", "painted", "ice blue and white", "a cracked crystalline shell covered in a web of fine glowing fractures, about to shatter"),
    ("StatusSynergies/icons/septic.png", "painted", "sickly green and dark red", "a festering wound with green rot spreading outward in branching veins"),
]

# make_particles recipes: the flashes a reaction plays where it goes off. White-tinted sheets take the colour of the
# layer, so one crack serves Shatter Point and Black Ice, one arc serves Conduction, Arc Flash and Overload.
PARTICLES = [
    {"name": "ss_crack", "preset": "magic_burst", "seed": 511, "key_scale": 0.5, "grow_in": 1, "tint": "white",
     "desc": "a round shell of pale ice cracked across by bright jagged fracture lines, splinters of ice lifting off it, a bright core showing through the cracks",
     "motion": "The cracks race across the shell and it breaks apart, splinters of ice flying outward from the bright core. Very fast, sudden, continuous motion.",
     "layer": {"count": 1, "life": [0.7], "speed": [0], "size": [1.6], "sizeCurve": "flat", "shape": "point", "color": "#DFF4FF", "color2": "#7FC8FF", "alpha": 1}},
    {"name": "ss_arc", "preset": "electric", "seed": 512, "key_scale": 0.5, "tint": "white",
     "desc": "a bright electric flash with forking arcs of lightning leaping outward in every direction from a blinding white core, black space on every side",
     "motion": "The arcs flicker and fork outward, whipping and reconnecting around the bright core. Very fast, jittery, continuous motion.",
     "layer": {"count": 1, "life": [0.5], "speed": [0], "size": [1.8], "sizeCurve": "flat", "shape": "point", "randomFrame": False, "color": "#FFFFFF", "color2": "#8FD6FF", "alpha": 1}},
    {"name": "ss_flashover", "preset": "explosion", "seed": 513, "key_scale": 0.5, "grow_in": 2,
     "desc": "a ring of orange fire bursting outward through a cloud of sickly green vapour, embers flying, black space on every side",
     "motion": "The fire erupts outward through the vapour, the green cloud flashing orange as it burns away, embers tearing off. Very fast, violent, continuous motion.",
     "layer": {"count": 1, "life": [0.8], "speed": [0], "size": [2.2], "sizeCurve": "flat", "shape": "point", "alpha": 1}},
]

LIGHTS = {"ss_crack": {"color": "#9FD8FF", "range": 4, "intensity": 2, "time": 0.5},
          "ss_arc": {"color": "#BFE8FF", "range": 5, "intensity": 3, "time": 0.4},
          "ss_flashover": {"color": "#FF8A3C", "range": 6, "intensity": 3, "time": 0.7}}
IDS = {"ss_crack": "SsCrack", "ss_arc": "SsArc", "ss_flashover": "SsFlashover"}


def icons(todo, redo):
    (MODS / SS / "icons").mkdir(parents=True, exist_ok=True)
    for rel, style, col, what in todo:
        try: mci.concept(rel, style, col, what, redo)
        except Exception as e: print(f"concept {rel} FAILED: {e}", flush=True)
    for rel, style, col, what in todo:
        try: mci.icon(rel, style, redo)
        except Exception as e: print(f"icon {rel} FAILED: {e}", flush=True)


def particles(todo, redo):
    ar = mia.shared()
    work = HERE / "item_art" / "particles_ss"
    res = ar.make_particles(todo, work / "out", work=work, redo=["key", "video", "sheet"] if redo else None)
    fx = MODS / SS / "fx"
    fx.mkdir(parents=True, exist_ok=True)
    pj = MODS / SS / "particles.json"
    data = json.loads(pj.read_text(encoding="utf-8")) if pj.exists() else {"particles": []}
    for x in res:
        name = Path(x["texture"]).name
        shutil.copy2(x["texture"], fx / name)
        fxe = json.loads(Path(x["effect"]).read_text(encoding="utf-8"))
        fxe["id"] = IDS.get(x["name"], "Ss" + x["name"])
        for layer in fxe.get("layers", []): layer["texture"] = f"fx/{name}"
        if x["name"] in LIGHTS: fxe["light"] = LIGHTS[x["name"]]
        data["particles"] = [p for p in data["particles"] if p.get("id") != fxe["id"]] + [fxe]
        print(f"{x['name']}: {x['sheet']['grid']} -> {fx / name}  (preview {x['preview']})", flush=True)
    pj.write_text(json.dumps(data, indent=2), encoding="utf-8")
    print("wrote", pj, flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", default="all")
    ap.add_argument("--only", default="")
    ap.add_argument("--redo", action="store_true")
    a = ap.parse_args()
    stages = ["icons", "particles"] if a.stage == "all" else a.stage.split(",")
    only = {s for s in a.only.split(",") if s}
    for st in stages:
        t = time.time()
        print(f"=== {st}", flush=True)
        if st == "icons": icons([r for r in ICONS if not only or Path(r[0]).stem in only], a.redo)
        elif st == "particles": particles([r for r in PARTICLES if not only or r["name"] in only], a.redo)
        print(f"=== {st} done in {time.time() - t:.0f}s", flush=True)


if __name__ == "__main__":
    main()
