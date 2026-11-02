"""The Corrupted Mirror's prop: SD3.5 concept -> TRELLIS mesh -> repaired .glb in Mods/SideAreas/models.

    python make_mirror_assets.py           run whatever hasn't been made
    python make_mirror_assets.py --redo    make it all again
"""
import argparse, os, shutil, sys, time, zlib
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_item_assets as mia
from make_item_assets import ART, cutout

SLUG = "corrupted_mirror"
OUT = HERE.parent / "Mods" / "SideAreas" / "models" / f"{SLUG}.glb"
MESH_DIR = ART / "mesh"
PROMPT = ("tall freestanding full-length mirror, seen straight from the front, jagged frame of torn black metal plates with sharp spikes "
          "and barbs, the glass is obsidian black and shattered with a huge crack from top to bottom, glowing violet purple light pouring "
          "out of every crack, ominous, dangerous cursed sci-fi artifact, standing upright on a heavy spiked base, whole object in frame, "
          "centered, isolated on plain white background, detailed game prop, studio lighting")
NEGATIVE = "text, letters, watermark, people, human, reflection of a person, cropped, border, busy background, floor, ground, shadow, room, wall, motion blur"
# TRELLIS centres the mesh on its origin: --floor stands it on y = 0, where the game puts a prop's feet
FIX = ["--decimate", "16000", "--drop-small", "0.02", "--texture-size", "1024", "--floor"]


def concept(ar, redo):
    out = ART / f"{SLUG}.png"
    if out.exists() and not redo: return out
    ART.mkdir(parents=True, exist_ok=True)
    from sd_http_client import plan_sd_size
    w, h, steps = plan_sd_size(512, 768, kind="icon")
    t = time.time()
    ar.make_image(PROMPT, out, negative_prompt=NEGATIVE, width=w, height=h, steps=steps, seed=zlib.crc32(SLUG.encode()) % 100000)
    print(f"concept: {w}x{h} in {time.time() - t:.0f}s", flush=True)
    return out


def mask(src):
    """TRELLIS takes the alpha as the object mask: the cutout, padded onto a transparent square."""
    obj = cutout(Image.open(src))
    side = int(max(obj.size) * 1.15)
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.paste(obj, ((side - obj.width) // 2, (side - obj.height) // 2))
    out = MESH_DIR / f"{SLUG}_mask.png"
    MESH_DIR.mkdir(parents=True, exist_ok=True)
    square.save(out)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--redo", action="store_true")
    args = ap.parse_args()
    ar = mia.shared()
    src = concept(ar, args.redo)
    print("gpu holder:", ar.gpu_holder(), flush=True)
    from sd_server_lifecycle import stop_sd_server
    stop_sd_server()
    raw, fixed = MESH_DIR / f"{SLUG}_raw.glb", MESH_DIR / f"{SLUG}.glb"
    if args.redo or not raw.exists():
        os.environ.pop("NoDefaultCurrentDirectoryInExePath", None)  # TRELLIS's launcher bats "call" each other by bare name
        t = time.time()
        ar.ensure("mesh")
        ar.make_mesh(mask(src), raw, resolution=512, mesh_simplify=10, texture_size=1024)
        print(f"trellis: {time.time() - t:.0f}s", flush=True)
    print("fixed:", ar.edit_mesh(raw, fixed, FIX, preview=MESH_DIR / f"{SLUG}_fix.png"), flush=True)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(fixed, OUT)
    print("wrote", OUT, flush=True)


if __name__ == "__main__":
    main()
