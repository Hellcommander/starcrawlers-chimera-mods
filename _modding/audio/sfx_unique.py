"""Make Noise Alchemy / Dark Fantasy derived sounds unique: a Stable Audio audio-to-audio re-make of each one.

Those packs are licensed for custom, unique sound effects. AI tools may be used on them, but the result must be a new
sound and never a near copy of a pack clip. sfx_alter.py's DSP rebuild keeps each lead clip's colour (tone) and can
keep its envelope, so for these packs this script goes one step further: the current render (a layering of the
referenced clips) is the init audio for Stable Audio 3 with the sound's own purpose as the prompt, at several noise
levels and seeds. Each take is checked against EVERY referenced clip with audio_bench.distance and kept only if it is
clearly its own sound (waveform, tone and envelope all under the limits below), is real stereo and is levelled to
-19 dBFS; the read-only listener then picks the best kept take for the purpose. Only reference-tier sidecars are
touched (direct / restricted packs bar AI tools: those stay with sfx_alter.py).

Usage: python sfx_unique.py <sidecar or folder> [...] [--seeds 2] [--levels 0.6,0.75,0.9] [--write]
  Without --write the takes go to the scratch folder given by --out (default: <script folder>/unique_out).
"""
import argparse, glob, json, os, sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, r"D:\games\Ai assisted toolkit\Tools\Shared")
import sfx_alter as sa                      # noqa: E402
import audio_bench as ab                    # noqa: E402
from audio_asset_library import AudioLibrary, ai_allowed   # noqa: E402

# "Unique" = no closer to any of its source clips than 9 in 10 UNRELATED clips from these packs are to each other
# (90th percentile over 245 unrelated pairs, 2026-11-03: waveform 16.0%, tone 94.1%, envelope 63.8%; tone alone is
# no evidence, unrelated dark-magic clips have a median tone match of 86%). All three must hold at once.
LIMITS = {"waveform_pct": 15.0, "tone_pct": 94.0, "envelope_pct": 64.0}
LEVEL_DB = -19.0
MIN_WIDTH = 0.2


def sidecars(paths):
    for p in paths:
        if os.path.isdir(p): yield from sorted(glob.glob(os.path.join(p, "*.meta.json")))
        else: yield p


def fit(x, n, sr):
    x = ab.to_stereo(x)
    # drop leading silence so the hit lands at once, then trim or pad to the planned length with a short fade
    lead = np.argmax(np.max(np.abs(x), axis=1) > 0.01) if np.max(np.abs(x)) > 0.01 else 0
    x = x[lead:]
    if len(x) >= n: x = x[:n].copy()
    else: x = np.concatenate([x, np.zeros((n - len(x), 2), dtype=x.dtype)])
    f = min(int(0.03 * sr), n)
    x[-f:] *= np.linspace(1, 0, f)[:, None]
    return x


def level(x):
    r = ab.rms(x)
    if r <= 1e-6: return x
    x = x * ab.amp(LEVEL_DB - ab.db(r))
    peak = float(np.max(np.abs(x)))
    return x * (0.98 / peak) if peak > 0.98 else x


def chance_limits(clips, pool, sr):
    """Per-sound limits: the meter's figures are not alike for every kind of sound (short low booms correlate more by
    chance), so each limit is raised to what 9 in 10 UNRELATED pack clips score against this sound's own sources,
    never lowered below the pack-wide LIMITS. A take passes only if it is no closer to its sources than chance."""
    rows = {k: [] for k in LIMITS}
    for name, c in clips:
        for pname, pc in pool:
            if pname == name: continue
            d = ab.distance(pc, ab.to_stereo(c), sr)
            for k in LIMITS: rows[k].append(d[k])
    return {k: round(max(LIMITS[k], float(np.percentile(v, 90))) if v else LIMITS[k], 1) for k, v in rows.items()}


def reference_pool(finder, mods_root):
    """One clip per reference-tier source cited by any mod sidecar: the unrelated clips the chance limits use."""
    seen, pool = set(), []
    for meta in sorted(glob.glob(os.path.join(mods_root, "*", "audio_licensed", "*.meta.json"))):
        for r in json.load(open(meta, encoding="utf-8")).get("references", []):
            if not isinstance(r, dict) or not ai_allowed(r.get("pack", ""), r.get("inner", "")): continue
            key = (r.get("inner") or r.get("id") or "").replace("\\", "/")
            if key in seen: continue
            seen.add(key)
            rec = finder.find(r.get("pack"), r.get("inner"), r.get("id"))
            c = sa.process(rec, 3.0)[0] if rec is not None else None
            if c is not None: pool.append((rec.inner.split("/")[-1], ab.to_stereo(c)))
    return pool


def worst(x, sr, clips, limits=LIMITS):
    out = None
    for name, c in clips:
        d = ab.distance(x, ab.to_stereo(c), sr)
        over = max(d[k] / limits[k] for k in limits)
        if out is None or over > out[0]: out = (over, d, name)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("paths", nargs="+")
    ap.add_argument("--seeds", type=int, default=2)
    ap.add_argument("--levels", default="0.6,0.75,0.9")
    ap.add_argument("--controls", type=int, default=4, help="prompt-only control takes per sound (0 = pack limits only)")
    ap.add_argument("--max-drop", type=float, default=0.1, help="refuse a take the listener rates this much below the current file")
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "unique_out"))
    a = ap.parse_args()
    levels = [float(v) for v in a.levels.split(",")]
    import aamt_stable_audio_backend as sab
    from audio_listener import Listener
    lib = AudioLibrary(); lib.load()
    finder = sa.Finder(lib)
    listener = Listener()
    pool = reference_pool(finder, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Mods"))
    print(f"chance pool: {len(pool)} reference-tier clips", flush=True)
    os.makedirs(a.out, exist_ok=True)
    report = []
    for meta in sidecars(a.paths):
        j = json.load(open(meta, encoding="utf-8"))
        name = os.path.basename(meta)[:-len(".meta.json")]
        refs = [r for r in j.get("references", []) if isinstance(r, dict)]
        if not refs or not all(ai_allowed(r.get("pack", ""), r.get("inner", "")) for r in refs):
            print(f"SKIP {name}: a reference is from a pack that bars AI tools"); continue
        wav = meta[:-len(".meta.json")] + ".wav"
        init, sr = ab.load(wav, ab.SR)
        n = len(init)
        clips = []
        for r in refs:
            rec = finder.find(r.get("pack"), r.get("inner"), r.get("id"))
            c = sa.process(rec, 3.0)[0] if rec is not None else None
            if c is not None: clips.append((rec.inner.split("/")[-1], c))
        limits = chance_limits(clips, pool, sr)
        print(f"  {name} limits {limits}", flush=True)
        purpose = j.get("prompt", name)
        # "sa3_prompt" in the sidecar words the sound for Stable Audio when the purpose text renders badly
        prompt = j.get("sa3_prompt") or sab.build_prompt(name=name.replace("_", " "), description=purpose, game="sci-fi dungeon crawler")
        now = listener.score(init, sr, purpose)["total"]
        def make(seed, lv):
            kw = dict(init_audio=(sr, init), init_noise_level=lv) if lv is not None else {}
            y, ysr = sab.generate_array(prompt, duration=n / sr + 0.4, seed=seed, steps=sab.unique_steps(),
                                        negative_prompt=sab.unique_negative(), **kw)
            y = ab.to_stereo(np.asarray(y, dtype=np.float32))
            if ysr != sr: y = ab.resample(y, ysr, sr)
            return level(fit(y, n, sr))
        # controls: prompt-only takes that never heard the pack audio. How close THEY land to the source clips is how
        # close "the same kind of sound" gets by itself; a take from the init must be no closer (no trace of the clip).
        controls = []
        for k in range(a.controls):
            try: controls.append((make(9100 + k, None), 9100 + k))
            except Exception as e: print(f"  {name} control {k}: SA3 failed: {e}")
        if controls:
            # per metric, the closest any source clip gets (a take is judged by each metric against every clip)
            figs = [{k: max(ab.distance(y, ab.to_stereo(c), sr)[k] for _, c in clips) for k in LIMITS} for y, _ in controls]
            limits = {k: round(max(limits[k], float(np.median([f[k] for f in figs]))), 1) for k in LIMITS}
            print(f"  {name} limits after {len(controls)} prompt-only controls {limits}", flush=True)
        takes = []
        for y, seed in controls:
            w = sa.width(y)
            over, d, close = worst(y, sr, clips, limits)
            ok = over <= 1.0 and w >= MIN_WIDTH
            print(f"  {name} prompt-only seed {seed}: waveform {d['waveform_pct']}% tone {d['tone_pct']}% envelope {d['envelope_pct']}% width {w:.2f} -> {'keep' if ok else 'drop'}", flush=True)
            if ok: takes.append((y, None, seed, d, close, w))
        for lv in levels:
            for k in range(a.seeds):
                seed = 7000 + 97 * k + int(lv * 100)
                try: y = make(seed, lv)
                except Exception as e:
                    print(f"  {name} level {lv} seed {seed}: SA3 failed: {e}"); continue
                w = sa.width(y)
                over, d, close = worst(y, sr, clips, limits)
                ok = over <= 1.0 and w >= MIN_WIDTH
                print(f"  {name} level {lv} seed {seed}: waveform {d['waveform_pct']}% tone {d['tone_pct']}% envelope {d['envelope_pct']}% width {w:.2f} -> {'keep' if ok else 'drop'}", flush=True)
                if ok: takes.append((y, lv, seed, d, close, w))
        if not takes:
            print(f"FAIL {name}: no take was both unique and stereo"); report.append({"name": name, "ok": False}); continue
        best, scores = listener.pick([t[0] for t in takes], sr, purpose)
        y, lv, seed, d, close, w = takes[best]
        s = scores[best]
        if s["total"] < now - a.max_drop:
            print(f"WEAK {name}: best unique take listens {s['total']:+.3f}, the current file {now:+.3f}; left as it is "
                  f"(try more --seeds or a plainer sa3_prompt in the sidecar)", flush=True)
            report.append({"name": name, "ok": False, "weak": True, "listen": s["total"], "current": now}); continue
        dest = wav if a.write else os.path.join(a.out, name + ".wav")
        ab.save(dest, y, sr)
        print(f"{name}: level {lv if lv is not None else 'prompt-only'} seed {seed}, waveform {d['waveform_pct']}% tone {d['tone_pct']}% envelope {d['envelope_pct']}% (closest {close}), width {w:.2f}, listen {s['total']:+.3f} match {s.get('match')}", flush=True)
        report.append({"name": name, "ok": True, "level": lv, "seed": seed, "similarity": d, "closest": close, "width": round(w, 3), "listen": s})
        if a.write:
            j.update({"engine": "sa3_unique", "unique": True, "derived": True,
                      "method": ("made by Stable Audio 3 from this sound's own prompt alone (no pack audio as input), " if lv is None else
                                 "a DSP layering of the referenced clips, re-made by Stable Audio 3 audio-to-audio from this sound's own prompt, ")
                                + "then checked unique against every referenced clip (waveform, tone and envelope, no closer than prompt-only "
                                  "takes and unrelated pack clips get) and levelled",
                      "sa3": {"prompt": prompt, "init_noise_level": lv, "seed": seed, "steps": sab.unique_steps(), "prompt_only": lv is None},
                      "uniqueness": {"limits": limits, "pack_limits": LIMITS, "closest": close, **{k: d[k] for k in ("waveform_pct", "tone_pct", "envelope_pct")}},
                      "width": round(w, 3), "listen": {k: s.get(k) for k in ("total", "match", "clean")}})
            json.dump(j, open(meta, "w", encoding="utf-8"), indent=2)
    json.dump(report, open(os.path.join(a.out, "unique_report.json"), "w", encoding="utf-8"), indent=1, default=str)


if __name__ == "__main__":
    main()
