"""Pick, process and write licensed SFX for the Chimera mods, in stereo. Only 'direct' (and 'own') tier sounds are used.
Usage: python sfx_build.py plan.json <Mods folder> [dry]"""
import sys, json, re, wave, os
import numpy as np
sys.path.insert(0, r"D:\games\Ai assisted toolkit\Tools\Shared")
from audio_asset_library import AudioLibrary, license_tier, load_asset_audio

SR = 44100


def folder_of(r):
    p = r.inner.replace(chr(92), "/").split("/")
    return p[-2] if len(p) > 1 else ""


def process(rec, max_len):
    """Trim, fade and level-match a library clip. Returns ((samples, 2) float32, features)."""
    x, sr = load_asset_audio(rec, sr=SR, max_seconds=12.0)
    env = np.abs(x).max(axis=1)
    thr = max(1e-4, env.max() * 10 ** (-42 / 20))
    nz = np.nonzero(env > thr)[0]
    if nz.size == 0:
        return None, {}
    x = x[max(0, nz[0] - int(0.004 * SR)):]
    tail = np.nonzero(np.abs(x).max(axis=1) > thr)[0]
    x = x[: tail[-1] + 1] if tail.size else x
    n = min(len(x), int(max_len * SR))
    x = x[:n].copy()
    fade = min(int(0.12 * SR), n // 3)
    if fade > 0:
        x[-fade:] *= (np.linspace(1, 0, fade, dtype=np.float32) ** 2)[:, None]
    rms = float(np.sqrt(np.mean(x ** 2))) or 1e-6
    # about -19 dBFS RMS, peak <= -1 dBFS. FMOD plays one channel 3 dB down on each side, so this is as loud in game as -16 was in mono
    x *= min(10 ** (-19 / 20) / rms, 10 ** (-1 / 20) / (np.abs(x).max() or 1e-6))
    a = np.abs(x).max(axis=1)
    attack = float(np.argmax(a >= 0.9 * a.max())) / SR
    mid = x.mean(axis=1)   # for the centroid figure only
    spec = np.abs(np.fft.rfft(mid * np.hanning(len(mid))))
    freqs = np.fft.rfftfreq(len(mid), 1 / SR)
    centroid = float((spec * freqs).sum() / (spec.sum() or 1))
    return x, {"len": round(n / SR, 2), "attack": round(attack, 3), "centroid": int(centroid),
               "rms_db": round(20 * np.log10(float(np.sqrt(np.mean(x ** 2))) + 1e-9), 1)}


def main():
    plan = json.load(open(sys.argv[1], encoding="utf-8"))
    mods, dry = sys.argv[2], len(sys.argv) > 3 and sys.argv[3] == "dry"
    lib = AudioLibrary(); lib.load()
    used, manifest = set(), {}
    for slot, s in plan.items():
        hits = lib.search_text(s["q"], k=500, license=["direct", "own"])
        fre = re.compile(s["f"], re.I)
        nre = re.compile(s.get("name", "."), re.I)
        max_att = s.get("att", 9.0)
        pick, tries = None, 0
        for h in hits:
            r = h.record
            key = (folder_of(r).lower(), r.name.strip().lower())   # the same sound ships as .wav and .mp3
            if key in used or r.loop_like or not fre.search(folder_of(r)) or not nre.search(r.name):
                continue
            if "/mono/" in r.inner.replace(chr(92), "/").lower():   # packs with both folders: take the stereo twin
                continue
            if not (0.15 <= (r.duration or 0) <= 12):
                continue
            x, feat = process(r, s.get("max", 1.5))
            tries += 1
            if x is None or feat["attack"] > max_att:
                if tries < 40:
                    continue
            pick = (h, r, key, x, feat)
            break
        if not pick or pick[3] is None:
            print(f"NO MATCH {slot}")
            continue
        h, r, key, x, feat = pick
        used.add(key)
        mod, name = slot.split("/", 1)
        rel = f"audio_licensed/{name}.wav"
        manifest[slot] = {"file": f"Mods/{mod}/{rel}", "pack": r.pack, "source": r.inner, "tier": license_tier(r.pack, r.inner),
                          "score": round(float(h.score), 3), **feat}
        print(f"{slot:36} {feat['len']:4.2f}s att {feat['attack']:.3f} cen {feat['centroid']:5} | {folder_of(r)[:18]}/{r.name[:46]}")
        if dry:
            continue
        out = os.path.join(mods, mod, "audio_licensed", name + ".wav")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        pcm = (np.clip(x, -1, 1) * 32767).astype("<i2")
        with wave.open(out, "wb") as w:
            w.setnchannels(2)
            w.setsampwidth(2)
            w.setframerate(SR)
            w.writeframes(pcm.tobytes())

    json.dump(manifest, open(os.path.join(os.path.dirname(os.path.abspath(sys.argv[1])), "sfx_manifest.json"), "w", encoding="utf-8"),
              indent=1, ensure_ascii=False)
    print("done", len(manifest))


if __name__ == "__main__":
    main()
