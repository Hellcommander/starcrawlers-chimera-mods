"""Rank every shipped sound by what the listener hears (audio_listener in the AI asset toolkit), worst first, without
running the game or the GPU.

Each sound is scored against its own purpose (sfx: .meta.json prompt, licensed: sfx_plan.json query) and against every
other sound's purpose: own_rank 1 means no other purpose fits it better. Writes sfx_audit.json next to this script.
Usage: python sfx_audit.py <Mods folder> [--show 25]"""
import sys, json, glob, os, argparse
import soundfile as sf

sys.path.insert(0, r"D:\games\Ai assisted toolkit\Tools\Shared")
from audio_listener import Listener, summarize

HERE = os.path.dirname(os.path.abspath(__file__))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mods")
    ap.add_argument("--show", type=int, default=25)
    a = ap.parse_args()
    plan = json.load(open(os.path.join(HERE, "sfx_plan.json"), encoding="utf-8"))
    sounds = []
    for slot, s in plan.items():
        mod, name = slot.split("/", 1)
        p = os.path.join(a.mods, mod, "audio_licensed", name + ".wav")
        if os.path.exists(p):
            sounds.append((slot, p, s["q"]))
    for p in sorted(glob.glob(os.path.join(a.mods, "*", "sfx", "*.wav")) + glob.glob(os.path.join(a.mods, "*", "audio_licensed", "*.wav"))):
        meta = p[:-4] + ".meta.json"
        if os.path.exists(meta):
            sounds.append((os.path.relpath(p, a.mods).replace(chr(92), "/"), p, json.load(open(meta, encoding="utf-8"))["prompt"]))

    def load(p):
        x, sr = sf.read(p, always_2d=True, dtype="float32")
        return x, int(sr)

    rows = Listener().audit((slot, *load(p), prompt) for slot, p, prompt in sounds)
    json.dump(rows, open(os.path.join(HERE, "sfx_audit.json"), "w", encoding="utf-8"), indent=1)
    print(summarize(rows))
    for r in rows[:a.show]:
        extra = f"  fits '{r['confused_with']}' better" if r.get("confused_with") else ""
        print(f"{r['sound']:44} rank {r['own_rank']:3}  match {r['match']:.3f}  clean {r['clean']:+.2f}{extra}")


if __name__ == "__main__":
    main()
