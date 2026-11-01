"""Start new audio_licensed/ sounds for a mod from a purpose line each, so sfx_alter.py can build them from licensed clips.

For every (name, purpose, seconds) the script asks the audio library index for the clips closest to the purpose (text
search: CLAP text embedding plus tags; no pack clip is fed to any model), writes <mod>/audio_licensed/<name>.meta.json
with those references and a silent stereo placeholder wav of the wanted length. sfx_alter.py then rebuilds each sound
from its referenced clips (it never reads the placeholder's samples, only its length) and overwrites the placeholder.

Usage: python sfx_plan_new.py <mod folder> <plan.json> [--k 6]
  plan.json: [ { "name": "sm_snuff", "purpose": "...", "seconds": 1.2, "archetype": "magic" }, ... ]
"""
import argparse
import json
import os
import sys
import wave

import numpy as np

sys.path.insert(0, r"D:\games\Ai assisted toolkit\Tools\Shared")
from audio_asset_library import AudioLibrary  # noqa: E402

SR = 44100
NA_LINE = "Sound effects created with NOISE ALCHEMY \u2013 NOISEALCHEMY.COM"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mod")
    ap.add_argument("plan")
    ap.add_argument("--k", type=int, default=6)
    ap.add_argument("--redo", action="store_true")
    a = ap.parse_args()
    plan = json.load(open(a.plan, encoding="utf-8"))
    out_dir = os.path.join(a.mod, "audio_licensed")
    os.makedirs(out_dir, exist_ok=True)
    lib = AudioLibrary()
    lib.load()
    print(f"library: {len(lib.records)} clips", flush=True)
    for s in plan:
        meta = os.path.join(out_dir, s["name"] + ".meta.json")
        wav = os.path.join(out_dir, s["name"] + ".wav")
        if os.path.exists(meta) and not a.redo:
            print(f"keep {s['name']}"); continue
        hits = lib.search_text(s["purpose"], k=a.k, archetype=s.get("archetype", ""))
        refs = []
        for h in hits:
            r = h.record if hasattr(h, "record") else h
            refs.append({"id": r.id, "score": float(getattr(h, "score", 0.0)), "via": getattr(h, "via", "clap+tag"),
                         "pack": r.pack, "inner": r.inner, "license": r.license_tier(), "tags": list(r.tags), "archetypes": list(r.archetypes)})
        na = any("noise alchemy" in (r["pack"] + r["inner"]).lower() or "dark fantasy" in (r["pack"] + r["inner"]).lower() for r in refs)
        j = {"prompt": s["purpose"], "duration": float(s["seconds"]), "engine": "sfx_alter", "unique": False,
             "archetype": s.get("archetype", ""), "shippable": True, "attribution": NA_LINE if na else "",
             "references": refs, "output": os.path.abspath(wav), "sample_rate": SR,
             "method": "to be rebuilt from the referenced clips by sfx_alter.py", "derived": True}
        json.dump(j, open(meta, "w", encoding="utf-8"), indent=2)
        if not os.path.exists(wav) or a.redo:
            n = int(SR * float(s["seconds"]))
            with wave.open(wav, "wb") as w:
                w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
                w.writeframes(np.zeros((n, 2), dtype=np.int16).tobytes())
        print(f"{s['name']}: {len(refs)} refs: " + "; ".join(f"{r['pack'].split('/')[-1]}::{r['inner'].split('/')[-1]} [{r['license']}]" for r in refs[:3]), flush=True)


if __name__ == "__main__":
    main()
