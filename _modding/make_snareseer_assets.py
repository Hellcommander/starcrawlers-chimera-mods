"""Snareseer's art in one GPU job: its class and tier icons (make_class_icons.py's painted rows) and its skill sounds
(AAMT library retrieval + Stable Audio), written into Mods/ArendethClasses/. Existing files are kept unless --redo.
Usage: python make_snareseer_assets.py [--stage icons|audio|all] [--redo]"""
import argparse, sys, time, zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_class_icons as mci
from make_item_assets import shared

SFX = HERE.parent / "Mods" / "ArendethClasses" / "sfx"

# (file, seconds, what it sounds like)
SOUNDS = [
    ("snare_glyph", 0.8, "eerie psychic rune being etched with a low whispering hum, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("snare_spring", 0.8, "psychic trap snapping shut, sharp mental shriek and glassy crack, sci-fi fantasy game sound effect, punchy, clean, no music, no voice"),
    ("snare_lure", 1.2, "hypnotic shimmering psychic hum pulling inward, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("snare_latch", 0.8, "ghostly chain hook latching with a metallic psychic clank, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("snare_step", 0.7, "soft time-warp blink with a reversed whoosh, sci-fi fantasy game sound effect, clean, no music, no voice"),
    ("snare_burst", 1.0, "psychic blast detonation with a warbling echo repeating itself, sci-fi fantasy game sound effect, punchy, clean, no music, no voice"),
    ("snare_cascade", 1.6, "rapid chain of psychic traps detonating one after another in a rising cascade, sci-fi fantasy game sound effect, clean, no music, no voice"),
]


def icons(redo):
    todo = [i for i in mci.ICONS if "/snareseer" in i[0]]
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
