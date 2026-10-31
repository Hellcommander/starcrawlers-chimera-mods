"""Alter the sounds that came from packs whose licenses bar AI tools, with plain signal processing and no AI generation.

Covers the audio_licensed/ files (sfx_manifest.json) and every sound with a .meta.json whose references include a barred
pack (audio_asset_library.ai_allowed). Each is rebuilt from its pack clips, not from the current file: pitch shift, a layer
from a second clip, a random all-pass in each channel (which gives the stereo width), a spectral curve, then the usual
trim and level. The result must differ from every clip it used: waveform correlation under MAX_CORR in both channels and
their sum. Seeds are fixed, so a run repeats. The processing uses numpy only: no model, no GPU.

Listening: an audio model, not a person, judges the candidates. Every try that passes the correlation test is scored by
the CLAP judge (audio_quality_model: match to the sound's purpose, cleanliness, clipping, cut-off tails) and the best one
wins; the current file is scored the same way, so the report says for each sound whether the listener likes the new one
more or less than before. --no-listen falls back to the plain spectrum match. No pack clip is ever scored or fed to any
model: only our outputs are. Settings (sfx_alter_settings.json) are chosen from numbers and text (--features).

Usage: python sfx_alter.py <Mods folder> <out folder> [--only name,name] [--write] [--features] [--no-listen]
  Without --write the sounds go to <out folder> only; with --write they replace the files in Mods and update the sfx
  .meta.json sidecars to say how the sound was made.
"""
import argparse
import glob
import json
import os
import sys
import wave
import zlib

import numpy as np

sys.path.insert(0, r"D:\games\Ai assisted toolkit\Tools\Shared")
from audio_asset_library import AudioLibrary, ai_allowed
from sfx_build import SR, process

HERE = os.path.dirname(os.path.abspath(__file__))
MAX_CORR = 0.25    # against any source clip; 1.0 is the same waveform
MIN_SPEC = 0.45    # log-spectrum similarity to the main clip: under this it no longer sounds like that skill
TRIES = 12
# What sfx_alter_settings.json can change, per class of sound: pitch moves in semitones (never small, so no clip survives
# nearly as it was), the layer's level, how far the two channels differ (0 = one signal, 1 = fully independent), and how
# hard the phase scrambles (more knots = more different, softer attack).
DEFAULTS = {"steps": [-5, -4, -3, 3, 4, 5, -6, 6], "layer": 0.45, "spread": 0.35, "knots": 96, "eq_db": 4.5, "reshape": 1.0}
# Sidecar fields from the Stable Audio renders that no longer describe the file once it is rebuilt here.
AI_FIELDS = ("conditioned_prompt", "strength", "init_noise_level", "copy_corr", "source_corr")


def read_wav(path):
    with wave.open(path, "rb") as w:
        n, ch, sr = w.getnframes(), w.getnchannels(), w.getframerate()
        x = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float32) / 32768
    x = x.reshape(-1, ch)
    return (np.repeat(x, 2, axis=1) if ch == 1 else x[:, :2]), sr


def write_wav(path, x):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype(np.int16).tobytes())


def repitch(x, semitones):
    """Resampling: the pitch moves and the length moves with it."""
    rate = 2 ** (semitones / 12)
    n = max(8, int(len(x) / rate))
    t = np.linspace(0, len(x) - 1, n)
    return np.stack([np.interp(t, np.arange(len(x)), x[:, c]) for c in range(2)], axis=1).astype(np.float32)


def fit(x, n):
    return x[:n] if len(x) >= n else np.pad(x, ((0, n - len(x)), (0, 0)))


def allpass_and_curve(x, rng, cfg, harder=1):
    """Random phase (a different waveform that keeps its spectrum and most of its attack), a little different per channel
    for the width, and a gentle EQ curve."""
    n = len(x)
    size = int(2 ** np.ceil(np.log2(n + 2048)))
    bins = size // 2 + 1
    knots = np.interp(np.linspace(0, 1, bins), np.linspace(0, 1, 24), rng.uniform(-cfg["eq_db"], cfg["eq_db"], 24))
    curve = 10 ** (knots / 20)
    out = np.zeros_like(x)
    knots = int(cfg["knots"] * harder)
    # smooth across frequency, so the phase spreads the sound by milliseconds and doesn't wash it out
    # log frequency, so the bass that carries a heavy sound gets scrambled as much as the top end
    grid = np.log1p(np.arange(bins)) / np.log1p(bins - 1)
    shared = np.interp(grid, np.linspace(0, 1, knots), rng.uniform(-np.pi, np.pi, knots))
    for c in range(2):
        own = np.interp(grid, np.linspace(0, 1, knots), rng.uniform(-np.pi, np.pi, knots))
        phase = shared + own * cfg["spread"]
        spec = np.fft.rfft(x[:, c], size) * curve * np.exp(1j * phase)
        out[:, c] = np.fft.irfft(spec, size)[:n]
    return out


def envelope(x, ms=4):
    size = max(3, int(SR * ms / 1000))
    return np.convolve(np.abs(x).max(axis=1), np.ones(size) / size, mode="same") + 1e-5


def reshape(y, x, amount=1.0):
    """Puts the loudness shape of x back over y: the attack returns, the waveform doesn't. amount under 1 puts back only
    part of it: for a slow, low sound the loudness shape is most of the waveform, so a full reshape copies too much."""
    gain = np.clip(envelope(x) / envelope(y), 0.1, 6.0) ** amount
    return y * gain[:, None].astype(np.float32)


def finish(x, n):
    """Original length, soft ends, about -19 dBFS RMS, peak at most -1 dBFS."""
    x = fit(x, n).copy()
    head = min(int(0.003 * SR), n // 4)
    if head > 0:
        x[:head] *= np.linspace(0, 1, head, dtype=np.float32)[:, None]
    fade = min(int(0.12 * SR), n // 3)
    if fade > 0:
        x[-fade:] *= (np.linspace(1, 0, fade, dtype=np.float32) ** 2)[:, None]
    rms = float(np.sqrt(np.mean(x ** 2))) or 1e-6
    return x * min(10 ** (-19 / 20) / rms, 10 ** (-1 / 20) / (np.abs(x).max() or 1e-6))


def corr1(a, b):
    """Best normalised cross-correlation at any lag: FFT, so a moved copy can't hide."""
    n = min(len(a), len(b))
    a, b = a[:n] - a[:n].mean(), b[:n] - b[:n].mean()
    size = int(2 ** np.ceil(np.log2(2 * n)))
    cc = np.fft.irfft(np.fft.rfft(a, size) * np.conj(np.fft.rfft(b, size)), size)
    return float(np.abs(cc).max() / (np.linalg.norm(a) * np.linalg.norm(b) or 1))


def corr(a, b):
    return max(corr1(a[:, 0], b[:, 0]), corr1(a[:, 1], b[:, 1]), corr1(a.sum(axis=1), b.sum(axis=1)))


def logspec(x, bands=32):
    frame, hop = 1024, 256
    if len(x) < frame:
        x = np.pad(x, (0, frame - len(x)))
    win = np.hanning(frame)
    frames = np.stack([np.abs(np.fft.rfft(x[i:i + frame] * win)) for i in range(0, len(x) - frame + 1, hop)])
    freqs = np.fft.rfftfreq(frame, 1 / SR)
    edges = np.geomspace(400, SR / 2, bands + 1)   # every band holds at least one FFT bin from here up
    out = np.stack([frames[:, (freqs >= a) & (freqs < b)].mean(axis=1) for a, b in zip(edges[:-1], edges[1:])], axis=1)
    return np.log10(out + 1e-6)


def spectrum_match(a, b):
    """Overall tone of two sounds, 1 = the same; time is averaged out, since a repitched sound doesn't line up frame by frame."""
    u, v = logspec(a.mean(axis=1)).mean(axis=0), logspec(b.mean(axis=1)).mean(axis=0)
    u, v = u - u.mean(), v - v.mean()
    return float((u * v).sum() / (np.linalg.norm(u) * np.linalg.norm(v) or 1))


def width(x):
    mid, side = (x[:, 0] + x[:, 1]) / 2, (x[:, 0] - x[:, 1]) / 2
    return float(np.sqrt(np.mean(side ** 2) / max(np.mean(mid ** 2), 1e-12)))


def harder(cfg, times=1):
    """For a sound no ordinary try gets far enough from its clips: bigger pitch moves, more layer, more scramble."""
    far = [-8 - times, -7 - times, 7 + times, 8 + times]
    return dict(cfg, steps=far + list(cfg["steps"]), layer=max(cfg["layer"], 0.5 + 0.3 * (times - 1)), spread=max(cfg["spread"], 0.5 + 0.2 * (times - 1)), knots=cfg["knots"] * 2 * times)


def candidate(name, clips, n, cfg, t, tag=""):
    """One try, made by the toolkit's shared rebuild step (audio_rebuild.candidate): anti-aliased pitch move, layers,
    low-band-safe all-pass scramble, the lead clip's colour and attack put back, light glue, the lead clip's loudness."""
    from audio_rebuild import candidate as shared_candidate
    return shared_candidate(name, clips, n, dict(cfg, level_db=-19.0), t, tag)


def alter(name, clips, n, cfg, listen=None, purpose="", tries=TRIES):
    """clips: the sources, main one first. Returns the best version under MAX_CORR as
    (sound, spectrum, corr, semitones, width, listen score or None), or None when no try gets there. Seeds come from the
    try number, so more tries only add candidates: a rerun with more can't pick a worse one."""
    for tag, c in (("", cfg), ("hard", harder(cfg)), ("harder", harder(cfg, 2))):
        passing = []
        for t in range(tries):
            y, step = candidate(name, clips, n, c, t, tag)
            cr = max(corr(y, fit(s, n)) for s in clips)
            if cr > MAX_CORR:
                continue
            s = spectrum_match(y, fit(clips[0], n))
            passing.append((y, s, cr, step))
            if listen is None and s >= 0.9:
                break
        if not passing:
            continue
        if listen is None:
            y, s, cr, step = max(passing, key=lambda p: p[1])
            return y, s, cr, step, width(y), None
        # The listener decides between the tries that are far enough from their clips; the main clip is only the tone
        # reference (plain signal processing), so it never goes into the model.
        i, scores = listen.pick([p[0] for p in passing], SR, purpose, tone_ref=fit(clips[0], n))
        y, s, cr, step = passing[i]
        return y, s, cr, step, width(y), scores[i]
    return None


class Finder:
    """Finds a clip by id, or by its path when the index was rebuilt since (a pack moved into a folder or an archive
    changes every id and the pack name; the path inside the pack stays)."""

    def __init__(self, lib):
        self.lib = lib
        self.by_inner = {}
        self.by_tail = {}
        for r in lib.records:
            self.by_inner.setdefault(r.inner, r)
            self.by_tail.setdefault(r.inner.split("/")[-1].lower(), []).append(r)

    def find(self, pack, inner, aid=None):
        r = self.lib.get(aid) if aid else None
        if r is not None:
            return r
        inner = (inner or "").replace("\\", "/")
        if inner in self.by_inner:
            return self.by_inner[inner]
        for cand in self.by_inner:   # the same path inside a pack that now lives in an archive
            if cand.endswith("/" + inner) or inner.endswith("/" + cand):
                return self.by_inner[cand]
        tail = inner.split("/")[-1].lower()
        same = [r for r in self.by_tail.get(tail, []) if os.path.splitext(os.path.basename(pack or ""))[0].lower() in r.pack.lower()]
        return same[0] if same else None


def jobs(mods, lib):
    """(name, destination, sources, length, meta path or None) for every sound to alter. A sound in an sfx/ folder is
    rebuilt from Noise Alchemy / Dark Fantasy clips only (its licensed-pack references are dropped); one in audio_licensed/
    uses all of its clips."""
    manifest = json.load(open(os.path.join(HERE, "sfx_manifest.json"), encoding="utf-8"))
    plan = json.load(open(os.path.join(HERE, "sfx_plan.json"), encoding="utf-8"))
    finder = Finder(lib)
    items = list(manifest.items())
    for slot, m in items:
        rec = finder.find(m["pack"], m["source"])
        if rec is None:
            print(f"SKIP {slot}: {m['source']} is not in the audio index")
            continue
        main, _ = process(rec, plan[slot].get("max", 1.5))
        kin = [o for k, o in items if k != slot and o["pack"] == m["pack"]][:64]
        other = kin[zlib.crc32(slot.encode()) % len(kin)] if kin else None
        other_rec = finder.find(other["pack"], other["source"]) if other else None
        extra = process(other_rec, 3.0)[0] if other_rec is not None else None
        yield slot, m["file"].replace("Mods/", "", 1), [c for c in (main, extra) if c is not None], len(main), None
    metas = glob.glob(os.path.join(mods, "**", "sfx", "*.meta.json"), recursive=True) + glob.glob(os.path.join(mods, "**", "audio_licensed", "*.meta.json"), recursive=True)
    for meta in sorted(metas):
        j = json.load(open(meta, encoding="utf-8"))
        used = set(j.get("used_ids") or [])
        refs = [r for r in j["references"] if isinstance(r, dict) and (not used or r.get("id") in used)]
        if all(ai_allowed(r.get("pack", ""), r.get("inner", "")) for r in refs) and j.get("engine") != "sfx_alter":
            continue   # a render from allowed references needs nothing; one we rebuilt before is rebuilt again
        in_sfx = os.path.basename(os.path.dirname(meta)).lower() == "sfx"
        if in_sfx:
            refs = [r for r in refs if ai_allowed(r.get("pack", ""), r.get("inner", ""))]
        wav = meta[:-len(".meta.json")] + ".wav"
        found = [(r, finder.find(r.get("pack"), r.get("inner"), r.get("id"))) for r in refs]
        clips = [c for c in (process(rec, 3.0)[0] for r, rec in found if rec is not None) if c is not None]
        if clips:
            yield os.path.relpath(wav, mods).replace(os.sep, "/")[:-4], os.path.relpath(wav, mods), clips, len(read_wav(wav)[0]), meta


def settings_for(name, table):
    """The class of a sound from sfx_alter_settings.json: defaults, then its class, then anything set for that one sound."""
    cfg = dict(DEFAULTS)
    short = name.split("/")[-1]
    cfg.update(table.get("classes", {}).get(table.get("sounds", {}).get(short, {}).get("class", ""), {}))
    cfg.update({k: v for k, v in table.get("sounds", {}).get(short, {}).items() if k != "class"})
    return cfg


def features(name, x):
    """Numbers only, for choosing settings."""
    env = np.abs(x).max(axis=1)
    m = x.mean(axis=1)
    spec = np.abs(np.fft.rfft(m))
    freqs = np.fft.rfftfreq(len(m), 1 / SR)
    return {"seconds": round(len(x) / SR, 2), "attack_ms": round(1000 * int(env.argmax()) / SR), "brightness_hz": round(float((spec * freqs).sum() / (spec.sum() or 1))),
            "peak_to_rms": round(float(env.max() / (np.sqrt(np.mean(m ** 2)) or 1e-6)), 1)}


def purpose_of(name, meta, plan):
    """What the sound is for, in words: the plan's query for licensed slots, the render prompt for sfx."""
    if plan.get(name, {}).get("q"):
        return plan[name]["q"]
    if meta and os.path.exists(meta):
        return json.load(open(meta, encoding="utf-8")).get("prompt", "")
    return ""


def update_sidecar(meta, c, step, w, j, finder=None):
    """The .meta.json now says the file is a plain-processing derivative of its references, not a render. References
    that the index knows under a new id or pack name are refreshed; ones the rebuild no longer uses (licensed packs, for
    a sound in sfx/) are dropped."""
    j_meta = json.load(open(meta, encoding="utf-8"))
    in_sfx = os.path.basename(os.path.dirname(meta)).lower() == "sfx"
    refs = []
    for r in j_meta.get("references", []):
        if not isinstance(r, dict):
            continue
        if in_sfx and not ai_allowed(r.get("pack", ""), r.get("inner", "")):
            continue
        rec = finder.find(r.get("pack"), r.get("inner"), r.get("id")) if finder else None
        if rec is not None:
            r = dict(r, id=rec.id, pack=rec.pack, inner=rec.inner, license=rec.license_tier())
        refs.append(r)
    j_meta["references"] = refs
    j_meta.pop("used_ids", None)
    for k in AI_FIELDS:
        j_meta.pop(k, None)
    j_meta.update({"engine": "sfx_alter", "method": "rebuilt from the referenced clips by pitch shift, layering, random all-pass and EQ; no AI generation",
                   "unique": False, "derived": True, "max_corr": round(c, 3), "semitones": int(step), "width": round(w, 3)})
    if j is not None:
        j_meta["listen"] = {"match": j["match"], "clean": j["clean"], "faults": j["faults"]}
    json.dump(j_meta, open(meta, "w", encoding="utf-8"), indent=2)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mods"); ap.add_argument("out")
    ap.add_argument("--only", default="")
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--features", action="store_true", help="only write sfx_features.json: numbers and text for choosing settings")
    ap.add_argument("--no-listen", action="store_true", help="pick by spectrum match alone, without the CLAP judge")
    ap.add_argument("--tries", type=int, default=TRIES, help="candidates per stage for the listener to choose from")
    args = ap.parse_args()
    table_path = os.path.join(HERE, "sfx_alter_settings.json")
    table = json.load(open(table_path, encoding="utf-8")) if os.path.exists(table_path) else {}
    plan = json.load(open(os.path.join(HERE, "sfx_plan.json"), encoding="utf-8"))
    found = {}
    only = set(filter(None, args.only.split(",")))
    lib = AudioLibrary(); lib.load()
    listen = None
    if not args.features and not args.no_listen:
        from audio_listener import Listener
        listen = Listener()
    report, failed, worse = {}, [], []
    for name, rel, clips, n, meta in jobs(args.mods, lib):
        if only and name not in only and name.split("/")[-1] not in only:
            continue
        purpose = purpose_of(name, meta, plan)
        if args.features:
            found[name] = dict(features(name, clips[0]), purpose=purpose)
            continue
        current = os.path.join(args.mods, rel)
        old = read_wav(current)[0] if listen is not None and os.path.exists(current) else None
        r = alter(name, clips, n, settings_for(name, table), listen, purpose, args.tries)
        if r is None:
            failed.append(name)
            print("FAIL", name)
            continue
        y, s, c, step, w, j = r
        write_wav(os.path.join(args.out, rel), y)
        if args.write:
            write_wav(current, y)
            if meta:
                update_sidecar(meta, c, step, w, j, Finder(lib))
        row = {"corr": round(c, 3), "spectrum": round(s, 3), "semitones": int(step), "width": round(w, 3), "clips": len(clips), "seconds": round(n / SR, 2), "purpose": purpose}
        line = f"{name}: corr {c:.3f} spectrum {s:.3f} {step:+d} st width {w:.2f}"
        if j is not None:
            row["listen"] = {k: j[k] for k in ("match", "clean", "faults", "total", "tone")}
            line += f"  listen {j['total']:+.3f} (match {j['match']:.3f}, clean {j['clean']:+.2f}, tone {j['tone']:.2f}{', ' + ','.join(j['faults']) if j['faults'] else ''})"
            if old is not None:
                cmp = listen.compare(y, old, SR, purpose, new_score=j)
                row["before"] = {k: cmp["old"][k] for k in ("match", "clean", "faults", "total")}
                row["verdict"] = cmp["verdict"]
                line += f"  before {cmp['old']['total']:+.3f} ({cmp['verdict']})"
                if cmp["verdict"] == "worse":
                    worse.append(name)
        report[name] = row
        print(line)
    if args.features:
        json.dump(found, open(os.path.join(HERE, "sfx_features.json"), "w", encoding="utf-8"), indent=1)
        print(f"{len(found)} sounds measured")
        return
    os.makedirs(args.out, exist_ok=True)
    json.dump({"sounds": report, "failed": failed, "worse_than_before": worse}, open(os.path.join(args.out, "alter_report.json"), "w", encoding="utf-8"), indent=1)
    print(f"{len(report)} altered, {len(failed)} failed" + (f", {len(worse)} the listener likes less than before: {', '.join(worse)}" if worse else ""))


if __name__ == "__main__":
    main()
