"""Wildcaster's art in one GPU job: its class and tier icons (make_class_icons.py's painted rows) and its skill sounds
(AAMT library retrieval + Stable Audio), written into Mods/ArendethClasses/. Existing files are kept unless --redo.
Usage: python make_wildcaster_assets.py [--stage icons|audio|all] [--redo]"""
import argparse, sys, time, zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_class_icons as mci
from make_item_assets import shared

SFX = HERE.parent / "Mods" / "ArendethClasses" / "sfx"

# (file, seconds, what it sounds like)
SOUNDS = [
    ("wild_bolt", 0.8, "unstable magic bolt with a warbling pitch that changes as it flies, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("wild_borrow", 1.0, "magic portal ripping open and a stolen spell whooshing out, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("wild_ward", 1.0, "shimmering prismatic shield chime rising through tones, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("wild_zone", 1.4, "reality tearing open with chaotic crackles of many elements, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("wild_hex", 0.9, "twisting distorted magic curse with a warped wobble, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("wild_surge", 1.5, "huge distant magic detonation rolling in like thunder with a monstrous roar inside it, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("wild_cascade", 1.6, "rapid chaotic cascade of many different spell impacts one after another, sci-fi fantasy game sound effect, clean, no music, no voice"),
]


def icons(redo):
    todo = [i for i in mci.ICONS if "/wildcaster" in i[0]]
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
