"""Wearable armour for crew crawlers (Mods/CrewWear), made with the AAMT toolkit like make_item_assets.py:
  concept - Stable Diffusion 3.5 front view of the armour piece alone on white   -> _modding/item_art/wear_<slug>.png
  mesh    - Stable Fast 3D of the concept (1024 texture), one batch              -> Mods/CrewWear/models/sf3d/raw/wear_<slug>.glb
  fix     - mesh_edit.py repair chain (FIX, per-piece extras) + preview sheet     -> Mods/CrewWear/models/sf3d/wear_<slug>.glb
            Agents: Read _modding/item_art/wear_<slug>_fix.png, tune FIX_EXTRA, rerun --stage fix --redo.
Each stage skips work already on disk. The class loader stands each mesh up on the crawler's chest or hips and
sizes it to the body (Wear.cs); Mods/CrewWear/wear.json maps vanilla gear to these meshes.
Usage: python make_wear_assets.py [--only slug,slug] [--stage concept|mesh|fix|all] [--redo]"""
import argparse, sys, time, zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_item_assets as mia

MOD = HERE.parent / "Mods" / "CrewWear"
STYLE = "single armor piece, front view, centered, isolated on plain white background, empty, nobody wearing it, sci-fi, detailed, studio lighting"
NEGATIVE = "person, body, head, helmet, face, visor, robot, mech, arms, hands, mannequin, text, letters, watermark, multiple objects, cropped, frame, border, busy background, shadow"

# slug, what it is
PIECES = [
    ("chest_cloth", "padded work jacket vest with utility pockets and zip, sleeveless, grey and orange fabric"),
    ("chest_guard", "detailed corporate security tactical body armor vest, layered navy blue hard armor plates with grey trim, molle webbing straps, utility pouches, high collar, shoulder pads, worn scratched edges"),
    ("chest_heavy", "product photo of a single empty sci-fi heavy armor chestplate with shoulder pauldrons, hollow torso shell, neck opening empty, brushed steel and gunmetal plates, yellow hazard stripes, rivets, panel seams, small orange glowing vents, scratched edges, entire armor visible, small in frame, wide margins"),  # concept picked from seeds 101/202/303 (202)
    ("chest_shroud", "sleek black stealth ninja chest armor, segmented carbon plates, glowing thin cyan lines"),
    ("legs_cloth", "pair of work trousers with knee pads and cargo pockets, grey fabric, standing upright"),
    ("legs_guard", "pair of tactical armored leggings with thigh and shin plates, blue grey, standing upright"),
    ("legs_heavy", "pair of heavy sci-fi armored leg plates with knee guards and thick thigh plates, gunmetal, standing upright"),
]

# SF3D leaves floating shards; a Blender voxel remesh fuses them into one shell and rebakes the texture
FIX = ["--remesh", "0.005", "--decimate", "7000", "--drop-small", "0.02", "--texture-size", "512"]  # small enough for the repo
FIX_EXTRA = {"chest_heavy": ["--match-back", "0.6"], "chest_guard": ["--match-back", "0.8", "--squash", "z:0.5"], "chest_cloth": ["--match-back", "0.6"], "chest_shroud": ["--match-back", "0.6"]}


def concept(slug, what, redo):
    out = mia.ART / f"wear_{slug}.png"
    if out.exists() and not redo: return out
    mia.ART.mkdir(parents=True, exist_ok=True)
    ar = mia.shared()
    t = time.time()
    ar.make_image(f"{what}, {STYLE}", out, negative_prompt=NEGATIVE, width=512, height=512, steps=28, seed=zlib.crc32(slug.encode()) % 100000)
    print(f"concept {slug} in {time.time() - t:.0f}s")
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--stage", default="all", choices=["concept", "mesh", "fix", "all"])
    ap.add_argument("--redo", action="store_true")
    a = ap.parse_args()
    only = set(filter(None, a.only.split(",")))
    todo = [(s, w) for s, w in PIECES if not only or s in only]
    if a.stage in ("concept", "all"):
        for s, w in todo: concept(s, w, a.redo)
    if a.stage in ("mesh", "all"):
        # Stable Fast 3D: one model load for the batch, ~2 s a mesh (TRELLIS took ~50 s and needed more fixing)
        out = MOD / "models" / "sf3d" / "raw"
        pairs = [(mia.ART / f"wear_{s}.png", out / f"wear_{s}.glb") for s, _ in todo if a.redo or not (out / f"wear_{s}.glb").exists()]
        if pairs:
            for r in mia.shared().make_fast_meshes(pairs, bake_res=1024):
                print(("ok   " if r.get("ok") else "FAIL ") + r.get("glb", r.get("error", "")))
    if a.stage in ("fix", "all"):
        sf = MOD / "models" / "sf3d"
        for s, _ in todo:
            src, dst = sf / "raw" / f"wear_{s}.glb", sf / f"wear_{s}.glb"
            if not src.exists() or (dst.exists() and not a.redo and dst.stat().st_mtime > src.stat().st_mtime): continue
            r = mia.shared().edit_mesh(src, dst, FIX + FIX_EXTRA.get(s, []), preview=mia.ART / f"wear_{s}_fix.png")
            print(f"fix  {s}: {r['tris']} tris, {r['pieces']} pieces; " + "; ".join(r.get("hint", [])))


if __name__ == "__main__":
    main()
