"""Bulkhead Psion's art in one GPU job: its class and tier icons (make_class_icons.py's painted rows) and its skill sounds
(AAMT library retrieval + Stable Audio), written into Mods/ArendethClasses/. Existing files are kept unless --redo.
Usage: python make_bulkhead_assets.py [--stage icons|audio|all] [--redo]"""
import argparse, sys, time, zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_class_icons as mci
from make_item_assets import shared

SFX = HERE.parent / "Mods" / "ArendethClasses" / "sfx"

# (file, seconds, what it sounds like)
SOUNDS = [
    ("bp_raise", 1.2, "heavy steel deck plates tearing up and slamming upright into a wall, metal groan, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("bp_spike", 0.7, "sharp steel spike bursting out of a metal wall with a screech, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("bp_shove", 0.8, "deep psychic force shove whoosh ending in a body slamming into steel, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("bp_crush", 1.2, "metal walls grinding inward with crushing pressure, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("bp_hammer", 0.9, "massive psychic punch impact with a clang of steel, sci-fi fantasy game sound effect, clean, no music, no voice"),
]


def icons(redo):
    todo = [i for i in mci.ICONS if "/bulkhead_psion" in i[0]]
    if not redo: todo = [i for i in todo if not (mci.MODS / i[0]).exists() or mci.is_placeholder(mci.MODS / i[0])]
    print(f"{len(todo)} icons to make", flush=True)
    for rel, style, col, what in todo: mci.concept(rel, style, col, what, redo)
    for rel, style, col, what in todo: mci.icon(rel, style, redo)


def audio(redo):
    ar = shared()
    SFX.mkdir(parents=True, exist_ok=True)
    for name, secs, prompt in SOUNDS:
        out = SFX / f"{name}.wav"
        if out.exists() and not redo: continue
        t = time.time()
        try:
            ar.make_audio(prompt, out, duration=secs, seed=zlib.crc32(name.encode()) % 100000)
            print(f"audio {name}: {time.time() - t:.0f}s", flush=True)
        except Exception as e: print(f"audio {name} FAILED: {e}", flush=True)


PROPS = [
    ("ArendethClasses", "bp_spike", "#B0B8C0", "prop", None, "a single long jagged spike of torn grey steel deck plating, sharp point, rivets along its base"),
    ("ArendethClasses", "bp_bulkhead", "#8090A0", "prop", None, "a free-standing thick section of riveted grey steel bulkhead wall plate torn up out of a deck, faint blue glowing cracks, upright"),
]


def meshes(redo):
    import shutil
    import make_interloper_assets as mi
    import make_item_assets as mia
    for (_, slug, _, kind, _, what) in PROPS: mi.concept(slug, kind, what, redo)
    try:
        from sd_server_lifecycle import stop_sd_server
        stop_sd_server()
    except Exception as e: print("stop sd:", e)
    mi.meshes(PROPS, redo)
    for (mod, slug, _, _, _, _) in PROPS:
        src_, out = mi.MESH_DIR / f"{slug}.glb", HERE.parent / "Mods" / mod / "models" / f"{slug}.glb"
        if not src_.exists() or (out.exists() and not redo): continue
        out.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src_, out); mia.slim(out)
        print(f"prop {slug}: {out.stat().st_size // 1024} KB", flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", default="all", choices=["icons", "audio", "mesh", "all"])
    ap.add_argument("--redo", action="store_true")
    a = ap.parse_args()
    if a.stage in ("icons", "all"): icons(a.redo)
    if a.stage in ("audio", "all"): audio(a.redo)
    if a.stage in ("mesh", "all"): meshes(a.redo)
    print("done", flush=True)


if __name__ == "__main__":
    main()
