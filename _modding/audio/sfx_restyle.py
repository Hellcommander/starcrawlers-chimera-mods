"""RETIRED: the licenses of these packs bar AI tools, so this script refuses any clip audio_asset_library.ai_allowed bars.
Every sound in sfx_manifest.json is one of those; sfx_build.py still trims and levels them without AI.

Re-render the licensed SFX through Stable Audio 3 (audio-to-audio), in stereo, so no shipped file is a copy of a pack file.

Each sound in sfx_manifest.json is read from its pack, processed like sfx_build.py does, fed to SA3 as stereo init audio
with its slot's prompt, then trimmed back to the original length and level-matched. The result is compared with the
input: waveform correlation must be low in both channels and their sum (it is not the same file), log-spectrum
similarity should stay high (it still fits the skill).

Usage: python sfx_restyle.py <Mods folder> <out folder> [--noise 0.5] [--only slot,slot] [--write]
  Without --write the renders go to <out folder> only; with --write they replace the files in Mods.
"""
import sys, json, os, argparse, hashlib, wave
import numpy as np

sys.path.insert(0, r"D:\games\Ai assisted toolkit\Tools\Shared")
import aamt_stable_audio_backend as sa
from audio_asset_library import AudioLibrary, ai_allowed
from audio_pipeline import STEREO_MIN_WIDTH, STEREO_PROMPT
from audio_quality_model import shared as judge
from sfx_build import SR, process

HERE = os.path.dirname(os.path.abspath(__file__))
PAD = 1.2          # SA3 works poorly on very short clips: render at least this long, then cut back


def write_wav(path, x):
    x = np.clip(x, -1, 1)
    with wave.open(path, "wb") as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype(np.int16).tobytes())


def resample(x, sr_from, sr_to):
    if sr_from == sr_to:
        return x
    n = int(round(len(x) * sr_to / sr_from))
    t = np.linspace(0, len(x) - 1, n)
    return np.stack([np.interp(t, np.arange(len(x)), x[:, c]) for c in range(2)], axis=1).astype(np.float32)


def width(x):
    """Side level over mid level: 0 is mono in two channels, around 0.5 or more is plainly wide."""
    mid, side = (x[:, 0] + x[:, 1]) / 2, (x[:, 0] - x[:, 1]) / 2
    return float(np.sqrt(np.mean(side ** 2) / max(np.mean(mid ** 2), 1e-12)))


def finish(x, n):
    """Same shaping as sfx_build.process: original length, tail fade, about -19 dBFS RMS, peak <= -1 dBFS."""
    x = x[:n].astype(np.float32).copy()
    if len(x) < n:
        x = np.pad(x, ((0, n - len(x)), (0, 0)))
    head = min(int(0.003 * SR), n // 4)
    if head > 0:
        x[:head] *= np.linspace(0, 1, head, dtype=np.float32)[:, None]
    fade = min(int(0.12 * SR), n // 3)
    if fade > 0:
        x[-fade:] *= (np.linspace(1, 0, fade, dtype=np.float32) ** 2)[:, None]
    rms = float(np.sqrt(np.mean(x ** 2))) or 1e-6
    return x * min(10 ** (-19 / 20) / rms, 10 ** (-1 / 20) / (np.abs(x).max() or 1e-6))


def logspec(x, bands=32):
    """Coarse log-frequency spectrum over time, for a 'does it still sound like this' score."""
    frame, hop = 2048, 512
    if len(x) < frame:
        x = np.pad(x, (0, frame - len(x)))
    win = np.hanning(frame)
    frames = np.stack([np.abs(np.fft.rfft(x[i:i + frame] * win)) for i in range(0, len(x) - frame + 1, hop)])
    freqs = np.fft.rfftfreq(frame, 1 / SR)
    edges = np.geomspace(150, SR / 2, bands + 1)   # every band holds at least one FFT bin from here up
    out = np.stack([frames[:, (freqs >= a) & (freqs < b)].mean(axis=1) for a, b in zip(edges[:-1], edges[1:])], axis=1)
    return np.log10(out + 1e-6)


def wave_corr(a, b):
    """Best normalised cross-correlation within +-20 ms: 1.0 means the same waveform."""
    n = len(a)
    lag = int(0.02 * SR)
    corr = 0.0
    for d in range(-lag, lag + 1, 8):
        u, v = (a[d:], b[:n - d]) if d >= 0 else (a[:n + d], b[-d:])
        den = np.linalg.norm(u) * np.linalg.norm(v)
        if den > 0:
            corr = max(corr, abs(float(np.dot(u, v) / den)))
    return corr


def scores(a, b):
    n = min(len(a), len(b))
    a, b = a[:n], b[:n]
    corr = max(wave_corr(a[:, 0], b[:, 0]), wave_corr(a[:, 1], b[:, 1]), wave_corr(a.sum(axis=1), b.sum(axis=1)))
    sa_, sb = logspec(a.mean(axis=1)), logspec(b.mean(axis=1))
    m = min(len(sa_), len(sb))
    sa_, sb = sa_[:m] - sa_[:m].mean(), sb[:m] - sb[:m].mean()
    spec = float((sa_ * sb).sum() / (np.linalg.norm(sa_) * np.linalg.norm(sb) or 1))
    return corr, spec


def onset(x):
    """When the sound arrives: first moment its 5 ms envelope reaches a quarter of its peak."""
    k = max(1, int(0.005 * SR))
    env = np.convolve(np.abs(x).max(axis=1), np.ones(k) / k, mode="same")
    return float(np.argmax(env >= 0.25 * env.max())) / SR


def pick_score(corr, spec, late, lost, heard):
    """Higher is better: still sounds like the original, not its waveform, hits on time, keeps its stereo width and
    the CLAP judge hears it as clean and on prompt."""
    return spec - 2.0 * max(0.0, corr - 0.35) - 4.0 * max(0.0, abs(late) - 0.03) - lost + heard


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mods"); ap.add_argument("out")
    ap.add_argument("--noise", type=float, default=0.5)
    ap.add_argument("--only", default="")
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--steps", type=int, default=sa.unique_steps())
    ap.add_argument("--tries", type=int, default=3, help="candidates per sound; the best by pick_score is kept")
    args = ap.parse_args()

    manifest = json.load(open(os.path.join(HERE, "sfx_manifest.json"), encoding="utf-8"))
    plan = json.load(open(os.path.join(HERE, "sfx_plan.json"), encoding="utf-8"))
    only = set(filter(None, args.only.split(",")))
    os.makedirs(args.out, exist_ok=True)
    lib = AudioLibrary(); lib.load()
    records = {(r.pack, r.inner): r for r in lib.records}
    report = {}
    for slot, m in manifest.items():
        if only and slot not in only and slot.split("/")[-1] not in only:
            continue
        if not ai_allowed(m["pack"], m["source"]):
            raise SystemExit(f"{slot}: {m['pack']}'s license bars AI tools, so it can't go through Stable Audio")
        dest = os.path.join(args.mods, m["file"].replace("Mods/", "", 1))
        orig, _ = process(records[(m["pack"], m["source"])], plan[slot].get("max", 1.5))
        n = len(orig)
        init = np.pad(orig, ((0, max(0, int(PAD * SR) - n)), (0, 0)))
        prompt = plan[slot]["q"] + ", video game sound effect, clean, punchy"
        t0 = onset(orig)
        best = None
        for t in range(args.tries):
            seed = int(hashlib.md5(f"{slot}#{t}".encode()).hexdigest()[:8], 16)
            # SA3 can return one signal in both channels; a stereo original gets asked for its width back
            collapsed = best is not None and width(best[1]) < STEREO_MIN_WIDTH and width(orig) >= 0.1
            arr, osr = sa.generate_array(prompt + (STEREO_PROMPT if collapsed else ""), duration=len(init) / SR, seed=seed, init_audio=(SR, init.T),
                                         init_noise_level=args.noise, steps=args.steps, negative_prompt=sa.unique_negative())
            y = finish(resample(arr[:, :2].astype(np.float32), osr, SR), n)
            corr, spec = scores(orig, y)
            late = onset(y) - t0
            lost = max(0.0, min(width(orig), 1.0) - width(y))
            cand = (pick_score(corr, spec, late, lost, judge().score(y, SR, plan[slot]["q"])["total"]), y, seed, corr, spec, late)
            if best is None or cand[0] > best[0]:
                best = cand
        _, y, seed, corr, spec, late = best
        name = slot.split("/")[-1]
        write_wav(os.path.join(args.out, name + ".wav"), y)
        if args.write:
            write_wav(dest, y)
        report[slot] = {"noise": args.noise, "steps": args.steps, "seed": seed, "wave_corr": round(corr, 3),
                        "spec_sim": round(spec, 3), "onset_shift": round(late, 3),
                        "width_src": round(width(orig), 3), "width": round(width(y), 3)}
        print(f"{slot:36} corr {corr:.3f}  spec {spec:.3f}  onset {late:+.3f}  width {width(orig):.3f} -> {width(y):.3f}", flush=True)
    with open(os.path.join(args.out, "restyle_report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=1)


if __name__ == "__main__":
    main()
