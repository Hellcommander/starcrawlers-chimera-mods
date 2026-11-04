"""Make a sound its own sound by mixing alone: the audio agent works out the layering, the mixer program makes it.

The agent (audio_agent.Agent.rebuild_unique) reads the source clips and each render and decides a plan: which clip
gives the punch, body and tail, pitches, gains, offsets, stretch, scramble, EQ, shaping and width. The program
(audio_rebuild.render_unique) renders exactly that plan with plain signal processing; it is the only thing that makes
samples. A judge measures every take against every clip the plan uses (waveform, tone and envelope, each clip against
its own chance limits, the worst of them against the sound's measured pass mark, as sfx_unique.py does), the listener scores the purpose, and the agent revises. When one clip
stays the closest round after round, the agent swaps that layer for a new library clip from Noise Alchemy or Dark
Fantasy (reference tier) and the sidecar's references follow. No generative AI: this path works for every pack tier,
and the listener only hears source clips whose terms allow AI tools.

A take is written only when it is unique, real stereo (width 0.2 or more), clean, the loop's listener rates it no more
than --max-drop below the current file, and so does a second judge on a different CLAP checkpoint that only hears the
finished take (audio_listener.Listener.second; download both with Tools/Download-AudioModels.bat). Sidecars get engine "mix_unique" and the plan, so a render repeats exactly.

Usage: python sfx_mix_unique.py <sidecar or folder> [...] [--rounds 10] [--seeds 2] [--max-drop 0.1] [--cache rows.json]
                                [--workers N] [--no-swap] [--check] [--write] [--out folder]
  --check only measures each current file against its pass mark (no rendering, no listener).
"""
import argparse, json, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, r"D:\games\Ai assisted toolkit\Tools\Shared")
import sfx_alter as sa                      # noqa: E402
import sfx_unique as su                     # noqa: E402
import audio_bench as ab                    # noqa: E402
from audio_asset_library import AudioLibrary, ai_allowed   # noqa: E402
from audio_agent import Agent               # noqa: E402

MIN_WIDTH = 0.2


def ref_of(rec):
    return {"id": rec.id, "pack": rec.pack, "inner": rec.inner, "license": rec.license_tier(), "via": "mix_unique agent"}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("paths", nargs="+")
    ap.add_argument("--rounds", type=int, default=10)
    ap.add_argument("--seeds", type=int, default=2)
    ap.add_argument("--max-drop", type=float, default=0.1)
    ap.add_argument("--cache", default="", help="json {clip inner path: {pool clip: figures}} (chance rows) from an earlier run, to skip that CPU step")
    ap.add_argument("--no-swap", action="store_true", help="never bring in a new library clip")
    ap.add_argument("--workers", type=int, default=None, help="CPU workers for the chance rows (default: [Audio] AudioCpuWorkers in TranscendenceTools.ini)")
    ap.add_argument("--check", action="store_true", help="only measure each CURRENT file against its measured pass mark (no rendering); "
                    "with --write, sidecars of files that pass get unique: true and the figures, the audio untouched")
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--out", default=os.path.join(HERE, "mix_unique_out"))
    a = ap.parse_args()
    from audio_listener import Listener
    lib = AudioLibrary(); lib.load()
    finder = sa.Finder(lib)
    listener = Listener()
    second = Listener.second()   # a different CLAP checkpoint: judges the finished take only, never steers the loop
    agent = Agent(library=lib, listener=listener)
    cache = json.load(open(a.cache, encoding="utf-8")) if a.cache and os.path.exists(a.cache) else {}
    pool = []

    def rows_for(rec, c):
        """What every unrelated pool clip scores against this clip (the chance rows), cached by the clip's path."""
        got = cache.get(rec.inner)
        if isinstance(got, dict) and got and "waveform_pct" not in got:
            return got
        if not pool:
            pool.extend(su.reference_pool(finder, os.path.join(HERE, "..", "..", "Mods")))
        name = rec.inner.split("/")[-1]
        rows = su.chance_rows([(name, c)], pool, ab.SR)[name]
        cache[rec.inner] = rows
        if a.cache:
            json.dump(cache, open(a.cache, "w", encoding="utf-8"))
        return rows

    def source(rec):
        c = sa.process(rec, 3.0)[0]
        if c is None:
            return None
        name = rec.inner.split("/")[-1]
        rows = rows_for(rec, c)
        return {"audio": ab.to_stereo(c), "meta": ref_of(rec), "name": name, "rows": rows,
                "limits": su.limits_from_rows({name: rows})[name], "listen": rec.ai_allowed()}

    def mark_for(checked):
        return su.pass_mark({s["name"]: s["rows"] for s in checked}, {s["name"]: s["limits"] for s in checked}, [s["name"] for s in checked])

    os.makedirs(a.out, exist_ok=True)
    # every cited clip's chance rows up front, in parallel (the slow CPU step), so the loop below only reads them
    save = (lambda c: json.dump(c, open(a.cache, "w", encoding="utf-8"))) if a.cache else None
    su.parallel_chance_rows(list(su.sidecars(a.paths)), cache, os.path.join(HERE, "..", "..", "Mods"), a.workers, save)
    report_path = os.path.join(a.out, "mix_unique_report.json")
    report = []
    for meta in su.sidecars(a.paths):
        j = json.load(open(meta, encoding="utf-8"))
        name = os.path.basename(meta)[:-len(".meta.json")]
        purpose = j.get("prompt", name)
        wav = meta[:-len(".meta.json")] + ".wav"
        now_x, sr = ab.load(wav, ab.SR)
        n = len(now_x)
        refs = [r for r in j.get("references", []) if isinstance(r, dict)]
        srcs, missing = [], 0
        for r in refs:
            rec = finder.find(r.get("pack"), r.get("inner"), r.get("id"))
            s = source(rec) if rec is not None else None
            if s is None:
                missing += 1; continue
            s["meta"] = dict(r, id=rec.id, pack=rec.pack, inner=rec.inner)   # keep the sidecar's own record of it
            srcs.append(s)
        if not srcs or missing:
            print(f"FAIL {name}: {missing} referenced clip(s) not found", flush=True)
            report.append({"name": name, "ok": False, "missing": missing}); continue
        if a.check:
            import audio_rebuild as rb
            mk = mark_for(srcs)
            m = rb.unique_judge([x["audio"] for x in srcs], [x["limits"] for x in srcs], mk)(now_x)
            closest = srcs[m["worst"]["clip"]]["meta"].get("inner", "").split("/")[-1]
            passes = m["over"] <= 1.0
            print(f"{'ok  ' if passes else 'OVER'} {name}: current file over {m['over']:.3f} of its pass mark {mk:.3f} "
                  f"(closest {closest}: {', '.join(f'{k.split(chr(95))[0]} {v}%' for k, v in m['worst']['figures'].items())}), width {m['width']:.2f}", flush=True)
            report.append({"name": name, "ok": passes, "check": True, "over": round(m["over"], 3), "pass_mark": mk, "closest": closest,
                           "figures": m["worst"]["figures"], "width": round(m["width"], 3)})
            if a.write and passes:
                j.update({"unique": True, "uniqueness": {"checked": "current file, no change", "over": round(m["over"], 3), "pass_mark": mk,
                                                         "closest": srcs[m["worst"]["clip"]]["meta"].get("inner"), "figures": m["worst"]["figures"],
                                                         "limits": {x["meta"].get("inner"): x["limits"] for x in srcs}}})
                json.dump(j, open(meta, "w", encoding="utf-8"), indent=2)
            json.dump(report, open(report_path, "w", encoding="utf-8"), indent=1, default=str)
            continue
        now = listener.score(now_x, sr, purpose)["total"]

        def more(in_use, role):
            used = {m.get("inner") for m in in_use}
            for rec in agent.find_clips(purpose, role, k=8, tiers=["reference"]):
                if rec.inner in used or rec.inner.replace("\\", "/").lower().startswith("made/"):
                    continue
                s = source(rec)
                if s is not None:
                    return s
            return None

        # repair first (the current file, changed only as much as the judge needs), then a full rebuild from the clips
        got, notes = None, []
        for mode in ("repair", "rebuild"):
            y, info = agent.rebuild_unique(name, srcs, n, purpose, base_listen=now, rounds=a.rounds, seeds=a.seeds, min_width=MIN_WIDTH,
                                           more=None if a.no_swap else more, base=now_x if mode == "repair" else None, mark_for=mark_for)
            for line in info["log"]:
                print(f"  {name} [{mode}] {line}", flush=True)
            if y is None:
                notes.append(f"{mode}: {info['error']}"); continue
            lis = info["listen"]["total"]
            if lis < now - a.max_drop:
                notes.append(f"{mode}: best unique take listens {lis:+.3f}, the current file {now:+.3f}"); continue
            verdict = second.compare(y, now_x, sr, purpose)
            print(f"  {name} [{mode}] second judge ({second.judge.model}): new {verdict['new']['total']:+.3f}, current {verdict['old']['total']:+.3f}, {verdict['verdict']}", flush=True)
            if verdict["delta"] < -a.max_drop:
                notes.append(f"{mode}: the second judge rates it {verdict['delta']:+.3f} below the current file"); continue
            got = (mode, y, info, lis, verdict); break
        if got is None:
            weak = any("listens" in t or "second judge" in t for t in notes)
            print(f"{'WEAK' if weak else 'FAIL'} {name}: {'; '.join(notes)}; left as it is", flush=True)
            report.append({"name": name, "ok": False, "weak": weak, "notes": notes, "current": now}); continue
        mode, y, info, lis, verdict = got
        dest = wav if a.write else os.path.join(a.out, name + ".wav")
        ab.save(dest, y, sr)
        w = info["worst"]
        print(f"{name}: {mode} round {info['round']}, over {info['over']:.2f} (closest {info['closest'].get('inner', '').split('/')[-1]}), width {info['width']:.2f}, "
              f"listen {lis:+.3f} (current {now:+.3f}), {len(info['used'])} clips", flush=True)
        report.append({"name": name, "ok": True, "mode": mode, "listen": lis, "current": now, "over": info["over"], "width": info["width"],
                       "used": [m.get("inner") for m in info["used"]], "plan": info["plan"]})
        if a.write:
            before = [r.get("inner") for r in refs]
            prev = {k: j.get(k) for k in ("engine", "method") if j.get(k)}
            j.update({"engine": "mix_unique", "unique": True, "derived": True,
                      "method": ("the previous render of this sound (itself made from the referenced clips), changed by plain signal "
                                 "processing only as far as needed (phase scramble, EQ, shaping, stretch, width)" if mode == "repair" else
                                 "mixed from the referenced clips by plain signal processing (layer roles, pitch, gain, offset, "
                                 "stretch, phase scramble, EQ, shaping, width)")
                                + "; an audio agent chose the plan by measuring the clips and each render and listening to the renders, "
                                  "and never touched the audio; checked unique against every referenced clip (waveform, tone and envelope, "
                                  "no closer than unrelated pack clips get) and levelled",
                      "mix_mode": mode, **({"repaired_from": prev} if mode == "repair" else {}),
                      "references": info["used"],
                      "mix_plan": {"plan": info["plan"], "seed": info["seed"]},
                      "uniqueness": {"over": info["over"], "pass_mark": info["pass_mark"], "closest": info["closest"].get("inner"), "figures": w["figures"],
                                     "limits": {m.get("inner"): lim for m, lim in zip(info["used"], info["used_limits"])}},
                      "width": info["width"], "listen": {k: info["listen"].get(k) for k in ("total", "match", "clean")},
                      "second_judge": {"model": second.judge.model, "new": verdict["new"]["total"], "current": verdict["old"]["total"], "verdict": verdict["verdict"]}})
            if [m.get("inner") for m in info["used"]] != before:
                j["references_before_mix"] = before
            json.dump(j, open(meta, "w", encoding="utf-8"), indent=2)
        json.dump(report, open(report_path, "w", encoding="utf-8"), indent=1, default=str)
    json.dump(report, open(report_path, "w", encoding="utf-8"), indent=1, default=str)
    print(f"done: {sum(r['ok'] for r in report)} of {len(report)} {'current files already unique' if a.check else 'mixed unique'}", flush=True)


if __name__ == "__main__":
    main()
