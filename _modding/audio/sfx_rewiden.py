"""Regenerate the sfx/ sounds that came back from Stable Audio as one signal in both channels, from their .meta.json prompts.

Each new clip is made in a temp folder and replaces the old one only if it has a stereo image and is shippable.
Usage: python sfx_rewiden.py <Mods folder> <temp folder> [--min-width 0.02] [--salt text] [--dry]"""
import sys, json, glob, os, shutil, argparse, zlib, time
import numpy as np
import soundfile as sf

sys.path.insert(0, r"D:\games\Ai assisted toolkit")
sys.path.insert(0, r"D:\games\Ai assisted toolkit\Tools\Shared")


def width(path):
    x, _ = sf.read(path, always_2d=True)
    mid, side = (x[:, 0] + x[:, 1]) / 2, (x[:, 0] - x[:, 1]) / 2
    return float(np.sqrt(np.mean(side ** 2) / max(np.mean(mid ** 2), 1e-12)))


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("mods"); ap.add_argument("temp")
    ap.add_argument("--min-width", type=float, default=0.02)
    ap.add_argument("--salt", default="", help="changes the seeds, for sounds an earlier run could not widen")
    ap.add_argument("--dry", action="store_true")
    a = ap.parse_args(argv)
    todo = [p for p in sorted(glob.glob(os.path.join(a.mods, "*", "sfx", "*.wav"))) if width(p) < a.min_width]
    print(f"{len(todo)} sounds under width {a.min_width}", flush=True)
    if a.dry:
        return
    import ai_resources as ar
    os.makedirs(a.temp, exist_ok=True)
    done, kept = 0, []
    for wav in todo:
        name = os.path.splitext(os.path.basename(wav))[0]
        meta_path = wav[:-4] + ".meta.json"
        if not os.path.exists(meta_path):
            kept.append((name, "no meta"))
            continue
        meta = json.load(open(meta_path, encoding="utf-8"))
        out = os.path.join(a.temp, name + ".wav")
        t = time.time()
        try:
            ar.make_audio(meta["prompt"], out, duration=meta["duration"], seed=zlib.crc32((name + a.salt).encode()) % 100000)
        except Exception as e:
            kept.append((name, str(e)[:80]))
            continue
        new_meta = json.load(open(out[:-4] + ".meta.json", encoding="utf-8"))
        old_w, new_w = width(wav), width(out)
        if new_w < a.min_width or not new_meta.get("shippable"):
            kept.append((name, f"width {new_w:.3f}, shippable {new_meta.get('shippable')}"))
            continue
        new_meta["output"] = os.path.abspath(wav)
        shutil.copy2(out, wav)
        json.dump(new_meta, open(meta_path, "w", encoding="utf-8"), indent=2)
        done += 1
        print(f"{name:28} width {old_w:.4f} -> {new_w:.3f}  {time.time() - t:.0f}s", flush=True)
    print(f"rewidened {done}, kept {len(kept)}", flush=True)
    for name, why in kept:
        print(f"KEPT {name}: {why}", flush=True)


if __name__ == "__main__":
    main()
