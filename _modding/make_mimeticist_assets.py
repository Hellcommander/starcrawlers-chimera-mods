"""Mimeticist's art in one GPU job: its class and tier icons (make_class_icons.py's painted rows) and its skill sounds
(AAMT library retrieval + Stable Audio), written into Mods/ArendethClasses/. Existing files are kept unless --redo.
Usage: python make_mimeticist_assets.py [--stage icons|audio|all] [--redo]"""
import argparse, sys, time, zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_class_icons as mci
from make_item_assets import shared

SFX = HERE.parent / "Mods" / "ArendethClasses" / "sfx"

# (file, seconds, what it sounds like)
SOUNDS = [
    ("mim_claw", 0.7, "vicious claw slash with a feral snarl, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("mim_bite", 0.7, "crunching beast bite with a wet snap, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("mim_howl", 1.6, "eerie mechanical-sounding wolf howl echoing down a metal corridor, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("mim_stomp", 1.0, "heavy armoured beast charging, thudding footfalls and a metal impact, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("mim_bellow", 1.2, "deep rumbling bellow of a huge shelled beast, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("mim_tongue", 0.7, "wet sticky tongue lash and slurping pull, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("mim_web", 0.8, "sticky web spray hissing out, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("mim_breath", 1.2, "roaring breath of molten slag with a crackling hiss, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("mim_wing", 0.9, "huge leathery wingbeat whoosh, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("mim_dart", 0.5, "quick pneumatic dart shot with a small sting, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("mim_shift", 1.4, "grotesque wet body transformation, bones cracking and flesh reshaping, sci-fi fantasy game sound effect, clean, no music, no voice"),
]


def icons(redo):
    todo = [i for i in mci.ICONS if "/mimeticist" in i[0]]
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


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", default="all", choices=["icons", "audio", "all"])
    ap.add_argument("--redo", action="store_true")
    a = ap.parse_args()
    if a.stage in ("icons", "all"): icons(a.redo)
    if a.stage in ("audio", "all"): audio(a.redo)
    print("done", flush=True)


if __name__ == "__main__":
    main()
