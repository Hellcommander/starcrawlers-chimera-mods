"""Fulgurant's art in one GPU job: its class and tier icons (make_class_icons.py's painted rows) and its skill sounds
(AAMT library retrieval + Stable Audio), written into Mods/ArendethClasses/. Existing files are kept unless --redo.
Usage: python make_fulgurant_assets.py [--stage icons|audio|all] [--redo]"""
import argparse, sys, time, zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_class_icons as mci
from make_item_assets import shared

SFX = HERE.parent / "Mods" / "ArendethClasses" / "sfx"

# (file, seconds, what it sounds like)
SOUNDS = [
    ("fulg_step", 0.8, "instant electric zip and thunder snap of a fighter blinking forward, sharp blade hit, sci-fi fantasy game sound effect, punchy, clean, no music, no voice"),
    ("fulg_feint", 0.8, "quick crackle of sparks then a swift blade strike from behind, sci-fi fantasy game sound effect, punchy, clean, no music, no voice"),
    ("fulg_fork", 1.4, "rapid string of lightning zaps jumping between targets, each with a blade impact, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("fulg_ghost", 1.0, "static discharge burst with a fading electric hum, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("fulg_rail", 1.0, "searing straight lightning streak whooshing past at high speed, sci-fi fantasy game sound effect, punchy, clean, no music, no voice"),
    ("fulg_stroke", 1.4, "huge thunderclap return stroke of lightning with a heavy blade impact, sci-fi fantasy game sound effect, powerful, clean, no music, no voice"),
]


def icons(redo):
    todo = [i for i in mci.ICONS if "/fulgurant" in i[0]]
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
