"""Builds one creature through TRELLIS instead of Stable Fast 3D, then the same repair and rig, to compare the two.
Usage: python trellis_compare.py <slug> <rig plan>   -> _modding/item_art/mesh/<slug>_trellis*.glb (+ the rigged one)"""
import os, subprocess, sys, time
from pathlib import Path
from PIL import Image
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_item_assets as mia
import make_interloper_assets as mx

slug, plan = sys.argv[1], sys.argv[2]
ART = mia.ART
raw, fixed, rigged = mx.MESH_DIR / f"{slug}_trellis_raw.glb", mx.MESH_DIR / f"{slug}_trellis.glb", mx.MESH_DIR / f"{slug}_trellis_rig.glb"
ar = mia.shared()
if not raw.exists():
    obj = mia.cutout(Image.open(ART / f"{slug}.png"))
    k = 420 / max(obj.size)
    obj = obj.resize((max(1, int(obj.width * k)), max(1, int(obj.height * k))), Image.LANCZOS)
    pad = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    pad.alpha_composite(obj, ((512 - obj.width) // 2, (512 - obj.height) // 2))
    mask = ART / f"{slug}_trellis_mask.png"
    pad.save(mask)
    from sd_server_lifecycle import stop_sd_server
    stop_sd_server()
    os.environ.pop("NoDefaultCurrentDirectoryInExePath", None)
    os.environ["AAMT_TRELLIS_KEEP_SERVER"] = "0"
    t = time.time()
    ar.ensure("mesh")
    ar.make_mesh(mask, raw, resolution=512, mesh_simplify=10, texture_size=1024)
    print(f"trellis {slug}: {time.time() - t:.0f}s, {raw.stat().st_size // 1024} KB", flush=True)
    try:
        from trellis_http_client import stop_server
        stop_server()
    except Exception as e:
        print("stop trellis:", e)
print("fix:", ar.edit_mesh(raw, fixed, ["--decimate", "14000", "--drop-small", "0.02", "--texture-size", "1024"], preview=mx.MESH_DIR / f"{slug}_trellis_fix.png"), flush=True)
r = subprocess.run([mx.blender_exe(), "-b", "--factory-startup", "-P", str(mx.BLENDER_SCRIPT), "--", str(fixed), str(rigged), plan], capture_output=True, text=True)
print("\n".join(l for l in r.stdout.splitlines() if "RIG:" in l), flush=True)
