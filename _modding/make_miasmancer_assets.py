"""Miasmancer's art in one GPU job: its class and tier icons (make_class_icons.py's painted rows) and its skill sounds
(AAMT library retrieval + Stable Audio), written into Mods/ArendethClasses/. Existing files are kept unless --redo.
Usage: python make_miasmancer_assets.py [--stage icons|audio|all] [--redo]"""
import argparse, sys, time, zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_class_icons as mci
from make_item_assets import shared

SFX = HERE.parent / "Mods" / "ArendethClasses" / "sfx"

# (file, seconds, what it sounds like)
SOUNDS = [
    ("mia_draw", 0.8, "deep inhale of foul air then a hissing spit, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("mia_mist", 1.2, "low rolling hiss of toxic gas spreading, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("mia_extract", 0.9, "wet psychic pull, fluid being drawn out with a squelch, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("mia_bloom", 1.0, "wet bursting bloom of liquid with a gurgle, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("mia_salt", 0.9, "gritty corrosive dust blast fizzing on metal, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("mia_grand", 1.5, "big alchemical explosion with bubbling, hissing and a deep boom, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("../../Synergies/sfx/toxic_breath", 1.3, "monstrous retching roar spewing a gush of bubbling toxic liquid, sci-fi fantasy game sound effect, clean, no music, no voice"),
]


def icons(redo):
    todo = [i for i in mci.ICONS if "/miasmancer" in i[0] or "toxic_breath" in i[0]]
    if not redo: todo = [i for i in todo if not (mci.MODS / i[0]).exists() or mci.is_placeholder(mci.MODS / i[0])]
    print(f"{len(todo)} icons to make", flush=True)
    for rel, style, col, what in todo: mci.concept(rel, style, col, what, redo)
    for rel, style, col, what in todo: mci.icon(rel, style, redo)


def audio(redo):
    ar = shared()
    SFX.mkdir(parents=True, exist_ok=True)
    for name, secs, prompt in SOUNDS:
        out = (SFX / f"{name}.wav").resolve()
        out.parent.mkdir(parents=True, exist_ok=True)
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
