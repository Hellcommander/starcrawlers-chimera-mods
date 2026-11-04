"""The Burden Projector's own art (Mods/ExampleItems), through the AAMT toolkit stages (D:\\games\\Ai assisted toolkit):
  concept   - SD3.5 renders on white: the rifle in side profile, and the Unburden skill emblem -> _modding/item_art/<slug>.png
  particles - SD3.5 key + Wan2.2 clip cut into flipbooks: the shot, its trail, its splash   -> ExampleItems/fx/<name>.png
  mesh      - TRELLIS image-to-3D of the rifle, slimmed                                      -> ExampleItems/models/items/burden_projector.glb
  icon      - the concepts cut out onto the mods' icon backdrop and frame                    -> ExampleItems/items/<slug>.png
  wire      - ExampleItems/particles.json for the shot, and the item's icon, models and skill look in items.json
All GPU stages run in this one process, one after another (several CUDA jobs at once crash the driver). Every stage
skips work already on disk, so a stopped run picks up where it left off.
Usage: python make_burden_projector_assets.py [--stage concept,particles,mesh,icon,wire|all] [--only name,name] [--redo]"""
import argparse, json, os, re, shutil, sys, time, zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_item_assets as mia

MOD = HERE.parent / "Mods" / "ExampleItems"
ART = mia.ART
ACCENT = "#B07CFF"  # murky violet: afflictions drawn out of the body

# (slug, prompt, negative extra). The rifle is drawn side-on: profiles reconstruct best, and a solid matte body keeps
# TRELLIS from building a see-through shell.
# (slug, prompt, negative extra, seed, T5). The rifle is drawn side-on: profiles reconstruct best, and solid matte keeps
# TRELLIS from building a see-through shell. Without T5, SD drew a light SMG, a wheeled cannon and plain assault rifles
# for "heavy": the long description needs T5 to come back as a heavy gun.
CONCEPTS = [
    ("burden_projector",
     "A heavy two-handed science fiction bio weapon in clean side profile, big and bulky like a heavy machine gun: "
     "a thick armoured gunmetal receiver, a heavy shoulder stock, a pistol grip with trigger, a vertical fore grip and a carry handle on top. "
     "A large round glass drum full of murky violet liquid is bolted underneath the receiver where a magazine would be, joined to it by a ribbed brass hose. "
     "A short thick barrel ends in a wide brass muzzle. Realistic 3D render, solid matte materials, the whole weapon in frame, isolated on a plain white background.",
     "hands, people, scope, pixel art, wheels, vehicle, thin, slim, transparent body", 842, True),
    ("unburden",
     # smoke cuts out badly from white: it is drawn glowing on black and its brightness becomes the icon's alpha
     "small tight spiral of murky violet smoke being sucked into a tiny glowing green core, "
     "the whole spiral small in the middle, wide pure black space on every side",
     "hands, people, face, skull, ring, text, white background", None, False),
]

# make_particles recipes (see the toolkit's PARTICLES.md). Never "fade into darkness": the sheet's own fade ends them.
PARTICLES = [
    # first take came back a thin ribbon of smoke: ask for a round ball that stays round
    {"name": "bp_core", "preset": "energy_orb", "seed": 5111, "key_scale": 0.45, "gamma": 2.0, "tint": "white",
     "desc": "a dense round ball of churning thick smoke with a small bright core, short curling tendrils all around its edge, the whole ball small in the middle, black space on every side",
     "motion": "The ball of smoke churns and rolls over itself around its bright core, short tendrils curling around its edge. Fast, turbulent, continuous motion; it stays round, whole and centred."},
    {"name": "bp_wisp", "preset": "smoke_puff", "seed": 5102, "key_scale": 0.5, "gamma": 1.8,
     "desc": "a small ragged wisp of murky violet vapour with fine curling threads, the whole wisp visible with black space on every side",
     "motion": "The wisp of vapour curls and stretches apart, its threads drifting and twisting as it spreads. Fast, continuous motion."},
    # first take was cut flat by the frame and drifted green to red: a round burst, white so particles.json colours it
    {"name": "bp_splash", "preset": "smoke_puff", "seed": 5113, "key_scale": 0.4, "grow_in": 1, "tint": "white",
     "desc": "a small round burst of thick vapour blowing outward from its centre in every direction, droplets flying around it, the whole burst small in the middle, black space on every side",
     "motion": "The vapour bursts outward from the centre in every direction, swelling and rolling over itself, droplets flying away. Very fast, violent, continuous motion; it stays centred."},
]


def concepts(todo, redo):
    ar = mia.shared()
    from sd_http_client import plan_sd_size
    from sd_server_lifecycle import stop_sd_server
    os.environ.setdefault("AAMT_SD_KEEP_SERVER", "1")
    t5 = None
    for slug, prompt, neg, seed, wants_t5 in todo:
        out = ART / f"{slug}.png"
        if out.exists() and not redo: continue
        ART.mkdir(parents=True, exist_ok=True)
        if t5 is not None and t5 != wants_t5: stop_sd_server()  # the server loads T5 or not when it starts
        os.environ["SD_SKIP_T5"] = "0" if wants_t5 else "1"
        t5 = wants_t5
        w, h, steps = (1024, 512, 28) if slug == "burden_projector" else plan_sd_size(256, 256, kind="icon")
        t = time.time()
        ar.make_image(prompt, out, negative_prompt=f"{mia.NEGATIVE}, {neg}", width=w, height=h, steps=steps,
                      seed=seed if seed is not None else zlib.crc32(slug.encode()) % 100000)
        print(f"concept {slug}: {w}x{h} in {time.time() - t:.0f}s", flush=True)


def particles(todo, redo):
    ar = mia.shared()
    work = HERE / "item_art" / "particles_bp"
    res = ar.make_particles(todo, work / "out", work=work, redo=["key", "video", "sheet"] if redo else None)
    fx = MOD / "fx"
    fx.mkdir(parents=True, exist_ok=True)
    grids = {}
    for x in res:
        shutil.copy2(x["texture"], fx / Path(x["texture"]).name)
        grids[x["name"]] = x["sheet"]["grid"]
        print(f"{x['name']}: {x['sheet']['grid']} -> {fx / Path(x['texture']).name}  (preview {x['preview']})", flush=True)
    old = json.loads((work / "grids.json").read_text()) if (work / "grids.json").exists() else {}
    old.update(grids)
    (work / "grids.json").write_text(json.dumps(old, indent=1))


def mesh(redo):
    ar = mia.shared()  # puts the toolkit on the path first
    from sd_server_lifecycle import stop_sd_server
    stop_sd_server()  # TRELLIS needs the whole card
    os.environ["AAMT_TRELLIS_KEEP_SERVER"] = "1"
    os.environ.pop("NoDefaultCurrentDirectoryInExePath", None)
    try:
        ar.ensure("mesh")
        glb = mia.mesh(MOD, "burden_projector", redo)
        # TRELLIS leaves small floating shards around thin parts; mesh_edit drops them (then slim again)
        if glb is not None and glb.exists() and ar.inspect_mesh(glb).get("small_pieces", 0) > 0:
            ar.edit_mesh(glb, glb, ["--drop-small", "0.02"])
            mia.slim(glb)
    finally:
        from trellis_http_client import stop_server
        stop_server()


GLOW_ICONS = {"unburden"}


def glow_icon(slug, redo):
    """An effect drawn on black: its brightness is its alpha, laid on the icon backdrop inside the frame."""
    from PIL import Image, ImageChops
    from make_icons import background, frame, W, S
    out = MOD / "items" / f"{slug}.png"
    if out.exists() and not redo: return
    src = Image.open(ART / f"{slug}.png").convert("RGB")
    r, g, b = src.split()
    alpha = ImageChops.lighter(ImageChops.lighter(r, g), b).point(lambda v: 0 if v < 18 else min(255, int((v - 18) * 1.4)))
    obj = src.convert("RGBA"); obj.putalpha(alpha)
    box = alpha.point(lambda v: 255 if v > 30 else 0).getbbox()
    if box: obj = obj.crop(box)
    img = background('#1e2638', '#06070c')
    k = int(W * 0.8) / max(obj.size)
    obj = obj.resize((max(1, int(obj.width * k)), max(1, int(obj.height * k))), Image.LANCZOS)
    img.alpha_composite(obj, ((W - obj.width) // 2, (W - obj.height) // 2))
    frame(img, ACCENT)
    out.parent.mkdir(parents=True, exist_ok=True)
    img.resize((S, S), Image.LANCZOS).save(out)
    print("wrote", out)


def icons(redo):
    for slug, *_ in CONCEPTS:
        if slug in GLOW_ICONS: glow_icon(slug, redo)
        else: mia.icon(MOD, slug, ACCENT, redo)


def grid(name):
    f = HERE / "item_art" / "particles_bp" / "grids.json"
    g = json.loads(f.read_text()).get(name) if f.exists() else None
    return g or [6, 6]


def wire():
    # The shot: the knot rides as the projectile (burst-only layers fly), the wisps leave its trail (a rate trails).
    shot = {"id": "BpUnburdenShot", "light": {"color": ACCENT, "range": 5, "intensity": 2.2, "time": 0.6}, "layers": [
        {"texture": "fx/bp_core.png", "grid": grid("bp_core"), "count": 1, "rate": 0, "duration": 0.6, "life": [0.6, 0.6],
         "speed": [0], "size": [0.75, 0.75], "sizeCurve": "flat", "spin": [-90, 90], "shape": "point",
         "color": "#E8D8FF", "color2": "#9CFF8A", "alpha": 1},
        {"texture": "fx/bp_wisp.png", "grid": grid("bp_wisp"), "count": 0, "rate": 30, "duration": 0.6, "life": [0.35, 0.6],
         "speed": [0.05, 0.25], "size": [0.3, 0.5], "sizeCurve": "grow", "spin": [-120, 120], "shape": "point",
         "color": "#C8A8FF", "color2": "#5A3A8A", "alpha": 0.8},
        {"texture": "mote", "count": 0, "rate": 40, "duration": 0.6, "life": [0.25, 0.45], "speed": [0.2, 0.6],
         "size": [0.03, 0.06], "sizeCurve": "shrink", "shape": "sphere", "radius": 0.15, "color": "#D8FFB0", "color2": "#8A5AD8", "alpha": 0.9}]}
    hit = {"id": "BpUnburdenHit", "light": {"color": ACCENT, "range": 6, "intensity": 2.6, "time": 0.7}, "layers": [
        {"texture": "fx/bp_splash.png", "grid": grid("bp_splash"), "count": 1, "rate": 0, "duration": 0.1, "life": [0.7, 0.7],
         "speed": [0], "size": [1.6, 1.6], "sizeCurve": "flat", "shape": "point", "color": "#FFFFFF", "color2": "#B07CFF", "alpha": 1},
        {"texture": "fx/bp_wisp.png", "grid": grid("bp_wisp"), "count": 6, "rate": 0, "duration": 0.1, "life": [0.6, 1.0],
         "speed": [0.6, 1.4], "size": [0.4, 0.7], "sizeCurve": "grow", "drag": 2, "spin": [-90, 90], "shape": "sphere", "radius": 0.2,
         "color": "#C8A8FF", "color2": "#3A2A5A", "alpha": 0.75},
        {"texture": "mote", "count": 24, "rate": 0, "duration": 0.1, "life": [0.4, 0.8], "speed": [2, 4], "size": [0.04, 0.08],
         "sizeCurve": "shrink", "gravity": 1.2, "shape": "sphere", "radius": 0.1, "color": "#D8FFB0", "color2": "#6A3AA8", "alpha": 1}]}
    (MOD / "particles.json").write_text(json.dumps({"particles": [shot, hit]}, indent=1), encoding="utf-8")
    print("wrote", MOD / "particles.json")

    # The item: edit the JSON text so its hand layout stays.
    path = MOD / "items.json"
    text = path.read_text(encoding="utf-8")
    m = re.search(r'"id":\s*"BurdenProjector"', text)
    start = text.rfind("{", 0, m.start())
    depth = 0
    for end in range(start, len(text)):
        depth += {"{": 1, "}": -1}.get(text[end], 0)
        if depth == 0: break
    obj = text[start:end + 1]
    new = obj
    looks = []
    if (MOD / "items/burden_projector.png").exists() and '"icon"' not in new.split('"skill"')[0]: looks.append('"icon": "items/burden_projector.png"')
    if (MOD / "models/items/burden_projector.glb").exists() and '"model"' not in new:
        looks.append('"model": { "mesh": "models/items/burden_projector.glb", "scale": 1.1 }')
        looks.append('"held": { "mesh": "models/items/burden_projector.glb" }')
    if looks: new = re.sub(r'("base":\s*"[^"]*",)', lambda g: g.group(1) + "\n      " + ", ".join(looks) + ",", new, count=1)
    if '"travelVfx"' not in new:
        skill_look = '"travelVfx": "BpUnburdenShot", "travelSpeed": 14, "impactVfx": "BpUnburdenHit",'
        if (MOD / "items/unburden.png").exists(): skill_look = '"icon": "items/unburden.png", ' + skill_look
        new = re.sub(r'("name":\s*"Unburden",)', lambda g: g.group(1) + "\n        " + skill_look, new, count=1)
    if new != obj:
        path.write_text(text[:start] + new + text[end + 1:], encoding="utf-8")
        json.loads(path.read_text(encoding="utf-8"))  # still valid
        print("wired BurdenProjector in items.json")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", default="all")
    ap.add_argument("--only", default="")
    ap.add_argument("--redo", action="store_true")
    a = ap.parse_args()
    stages = ["concept", "particles", "mesh", "icon", "wire"] if a.stage == "all" else a.stage.split(",")
    only = {s for s in a.only.split(",") if s}
    for st in stages:
        t = time.time()
        print(f"=== {st}", flush=True)
        if st == "concept": concepts([c for c in CONCEPTS if not only or c[0] in only], a.redo)
        elif st == "particles": particles([r for r in PARTICLES if not only or r["name"] in only], a.redo)
        elif st == "mesh": mesh(a.redo)
        elif st == "icon": icons(a.redo)
        elif st == "wire": wire()
        print(f"=== {st} done in {time.time() - t:.0f}s", flush=True)


if __name__ == "__main__":
    main()
