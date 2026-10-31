"""Swarm Conductor art through the AAMT toolkit (same stages as make_item_assets.py):
  concept  - SD3.5 render on white                         -> _modding/item_art/<slug>.png
  icon     - the cutout on the mods' icon backdrop/frame   -> Mods/SwarmConductor/icons/<icon>.png
  mesh     - Stable Fast 3D of the Lattice Drone concept   -> Mods/SwarmConductor/models/lattice_drone.glb
Every stage skips files already on disk unless --redo.
Usage: python make_swarm_assets.py [--stage concept|icon|mesh|all] [--redo]"""
import argparse, sys, time
from pathlib import Path
from PIL import Image, ImageFilter

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from make_icons import background, frame, rgba, W, S
from make_item_assets import ART, concept, cutout, shared, slim

MOD = HERE.parent / "Mods" / "SwarmConductor"

# (concept slug, what it is, [(icon file, accent)])
ART_SET = [
    ("sc_lattice_drone", "compact hovering combat drone seen from above at a three-quarter angle, dark gunmetal and graphite armored "
     "shell with an open hexagonal lattice frame, cyan glowing sensor eye and cyan light strips, four short ducted rotors at the corners, "
     "no ground shadow",
     [("lattice_foundry", "#5FD7FF"), ("lattice", "#9FE8FF")]),
    ("sc_scrap_drone", "small improvised hovering drone bolted together from mismatched scrap parts, exposed wiring, taped joints, "
     "salvaged rotor, cracked lens with a cyan glow, junkyard build",
     [("assemble_drone", "#FFB45C")]),
]


def icon(slug, name, accent, redo):
    out = MOD / "icons" / f"{name}.png"
    if out.exists() and not redo: return
    src = ART / f"{slug}.png"
    if not src.exists(): print(f"icon {name}: no concept yet"); return
    obj = cutout(Image.open(src))
    img = background('#1e2638', '#06070c')
    k = int(W * 0.74) / max(obj.size)
    obj = obj.resize((max(1, int(obj.width * k)), max(1, int(obj.height * k))), Image.LANCZOS)
    halo = Image.new('RGBA', img.size, (0, 0, 0, 0))
    tint = Image.new('RGBA', obj.size, rgba(accent))
    tint.putalpha(obj.split()[3].filter(ImageFilter.GaussianBlur(14)).point(lambda v: int(v * 0.55)))
    pos = ((W - obj.width) // 2, (W - obj.height) // 2)
    halo.alpha_composite(tint, pos)
    img.alpha_composite(halo)
    img.alpha_composite(obj, pos)
    frame(img, accent)
    img.resize((S, S), Image.LANCZOS).save(out)
    print("wrote", out)


def mesh(redo):
    out = MOD / "models" / "lattice_drone.glb"
    if out.exists() and not redo: return
    src = ART / "sc_lattice_drone.png"
    if not src.exists(): print("mesh: no concept yet"); return
    out.parent.mkdir(parents=True, exist_ok=True)
    raw = ART / "sc_lattice_drone_raw.glb"
    t = time.time()
    shared().make_fast_mesh(src, raw)
    print(f"mesh: SF3D in {time.time() - t:.0f}s")
    # SF3D comes out in shards with guessed backs; remesh, thin and drop the crumbs. The concept is top-down, so the
    # mesh comes out facing the camera: tip it onto its belly and flatten the guessed-at dome underneath.
    shared().edit_mesh(raw, out, ["--remesh", "0.008", "--decimate", "9000", "--drop-small", "0.02", "--match-back", "0.8",
                        "--rotate", "x:90", "--squash", "y:0.6", "--center"],
                       preview=ART / "sc_lattice_drone_preview.png")
    slim(out)
    print(f"mesh: {out.stat().st_size // 1024} KB")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", default="all")
    ap.add_argument("--redo", action="store_true")
    a = ap.parse_args()
    for slug, what, icons in ART_SET:
        if a.stage in ("concept", "all"): concept(slug, what, a.redo)
        if a.stage in ("icon", "all"):
            for name, accent in icons: icon(slug, name, accent, a.redo)
    if a.stage in ("mesh", "all"): mesh(a.redo)
