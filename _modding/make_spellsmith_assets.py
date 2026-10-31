"""Spellsmith's own art in one GPU job, written into Mods/ExampleClasses/ (particles go to Mods/SkillVfx/fx/):
  sound    - forge sounds from modal-synthesis strikes (audio_materials) through the bench, no clip behind them; the
             CLAP listener picks the take. CPU only.
  icons    - class and tier icons (make_class_icons.py's painted rows)
  fx       - spellsmith_burst / spellsmith_bits (make_class_particles.py)
  meshes   - the buried charge and the Kindled Shell (make_item_assets.py's concept -> cutout -> TRELLIS)
Existing files are kept unless --redo. Usage: python make_spellsmith_assets.py [--stage sound|icons|fx|meshes|gpu|all] [--redo]"""
import argparse, json, os, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_class_icons as mci
import make_class_particles as mcp
import make_item_assets as mia

MOD = HERE.parent / "Mods" / "ExampleClasses"
SFX = MOD / "sfx"
TAKES = 8

# Reused sound layers: a strike on one material ({size, force} 0..1), a sustained hiss and the shared last stage.
def hit(material, size, force, **kw): return {"synth": "material", "material": material, "size": size, "force": force, **kw}
WIDE = [{"fx": "decorrelate", "spread": 0.45}]
WIDER = [{"fx": "decorrelate", "spread": 0.7}]
# Transient-heavy strikes hit the -1 dBFS peak limit before they reach the -19 dBFS level match; a little density first.
GLUE = {"ss_hammer": 0.45, "ss_kindle": 0.4, "ss_masterwork": 0.25}
ROOM = [{"fx": "reverb", "room": 0.35, "decay": 0.45, "mix": 0.18}]
# Seconds each sound gets: struck metal rings for far longer than a game sound should, so its tail is faded inside this.
LENGTH = {"ss_lance": 0.9, "ss_bury": 1.0, "ss_lob": 0.9, "ss_burst": 1.4, "ss_kindle": 1.1, "ss_nova": 1.6, "ss_hammer": 0.9, "ss_loose": 1.5, "ss_masterwork": 1.8}

# name -> (what the listener wants, sources, layers). Each is a stack around one forge idea, not a filtered copy of another.
SOUNDS = {
    "ss_lance": ("a hot lance of fire shot from a forge, sharp metallic ignition with a roaring hiss",
        {"a": hit("plasma", 0.5, 0.8), "b": hit("metal", 0.2, 0.6)},
        [{"source": "a", "chain": [{"fx": "stretch", "factor": 1.5}, {"fx": "glide", "start_st": 7, "end_st": -5}, {"fx": "noise", "color": "pink", "level_db": -14, "band": [700, 7000]}, {"fx": "saturate", "drive": 0.4}, *WIDE]},
         {"source": "b", "gain_db": -14, "chain": [{"fx": "fade", "out_ms": 200}, *WIDE]}]),
    "ss_bury": ("a heavy metal thud and ratchet clicks, a charge bolted down",
        {"a": hit("metal", 0.75, 0.7), "b": hit("stone", 0.6, 0.8), "c": hit("chitin", 0.3, 0.7)},
        [{"source": "a", "chain": [{"fx": "lowcut", "hz": 90}, {"fx": "highcut", "hz": 3200}, *WIDER]},
         {"source": "b", "gain_db": -3, "pan": 0.35, "chain": WIDE},
         {"source": "c", "gain_db": -8, "pan": -0.45, "offset_ms": 95, "repeat": {"count": 3, "gap_ms": 60, "decay_db": 3, "pitch_st": -1}, "chain": WIDE}]),
    "ss_lob": ("a fast heavy whoosh of a thrown object passing by, then a dull iron clank as it lands",
        {"a": hit("wood", 1.0, 0.2), "b": hit("metal", 0.4, 0.6), "c": hit("stone", 0.7, 0.6)},
        [{"source": "a", "pan_sweep": {"from": -0.9, "to": 0.7}, "doppler": {"semitones": 6, "depth": 0.7}, "chain": [{"fx": "stretch", "factor": 2.0}, {"fx": "noise", "color": "pink", "level_db": -10, "band": [200, 4000]}, {"fx": "glide", "start_st": 3, "end_st": -5}, {"fx": "bandpass", "low_hz": 150, "high_hz": 4500}, {"fx": "fade", "in_ms": 60, "out_ms": 200}, *WIDE]},
         {"source": "b", "gain_db": -5, "pan": 0.45, "offset_ms": 380, "chain": [{"fx": "highcut", "hz": 3500}, {"fx": "fade", "out_ms": 220}, *WIDE]},
         {"source": "c", "gain_db": -8, "pan": 0.3, "offset_ms": 385, "chain": [{"fx": "highcut", "hz": 700}, *WIDE]}]),
    "ss_burst": ("a hot iron shell exploding, a sharp metal crack with a heavy boom and ringing shrapnel",
        {"a": hit("metal", 0.3, 1.0), "b": hit("stone", 1.0, 1.0), "c": hit("void", 0.7, 0.9)},
        [{"source": "b", "chain": [{"fx": "saturate", "drive": 0.45}, *WIDE]},
         {"source": "c", "gain_db": -2, "chain": [{"fx": "decorrelate", "spread": 0.3}]},
         {"source": "a", "gain_db": -6, "chain": WIDE}]),
    "ss_kindle": ("packing a charge into a small metal shell, a click, a spark and a rising forge hiss",
        {"a": hit("chitin", 0.4, 0.9), "b": hit("metal", 0.12, 0.8), "c": hit("plasma", 0.2, 1.0), "d": hit("plasma", 0.6, 0.3, seed=3)},
        [{"source": "a", "pan": -0.3, "repeat": {"count": 3, "gap_ms": 95, "decay_db": 2, "pitch_st": 1}, "chain": WIDE},
         {"source": "b", "gain_db": -4, "pan": 0.3, "offset_ms": 60, "repeat": {"count": 2, "gap_ms": 130, "decay_db": 3}, "chain": [{"fx": "highcut", "hz": 7000}, *WIDE]},
         {"source": "c", "gain_db": -6, "offset_ms": 330, "chain": [{"fx": "lowcut", "hz": 1500}, {"fx": "fade", "out_ms": 120}, *WIDE]},
         {"source": "d", "gain_db": -16, "offset_ms": 300, "chain": [{"fx": "stretch", "factor": 2.0}, {"fx": "noise", "color": "pink", "level_db": -4, "band": [1500, 9000]}, {"fx": "glide", "start_st": -3, "end_st": 5}, {"fx": "lowcut", "hz": 1200}, *WIDER]}]),
    "ss_nova": ("a burst of spell energy blown out on all sides, a bright metal bloom and a deep whump",
        {"a": hit("astral", 0.5, 0.9), "b": hit("void", 0.9, 1.0), "c": hit("plasma", 0.7, 0.9), "d": hit("metal", 0.55, 0.9)},
        [{"source": "b", "chain": [{"fx": "saturate", "drive": 0.5}, {"fx": "sub", "level_db": -6}, {"fx": "decorrelate", "spread": 0.35}]},
         {"source": "c", "gain_db": -5, "chain": [{"fx": "stretch", "factor": 1.4}, {"fx": "noise", "color": "pink", "level_db": -6, "band": [150, 7000]}, {"fx": "glide", "start_st": 3, "end_st": -9}, *WIDER]},
         {"source": "d", "gain_db": -8, "offset_ms": 30, "chain": [{"fx": "lowcut", "hz": 500}, *WIDER]},
         {"source": "a", "gain_db": -9, "offset_ms": 60, "chain": [{"fx": "glide", "start_st": 0, "end_st": -4}, *WIDER]}]),
    "ss_hammer": ("a blacksmith's hammer ringing on an anvil, a hard bright clang",
        {"a": hit("metal", 0.35, 1.0), "b": hit("metal", 0.15, 0.9, seed=4), "c": hit("stone", 0.65, 0.8)},
        [{"source": "a", "pan": -0.45, "chain": WIDER},
         {"source": "a", "gain_db": -2, "pan": 0.55, "offset_ms": 14, "chain": [{"fx": "highcut", "hz": 9000}, *WIDER]},
         {"source": "b", "gain_db": -6, "pan": -0.3, "pitch": 2, "chain": WIDE},
         {"source": "c", "gain_db": -5, "pan": 0.35, "chain": WIDE}]),
    "ss_loose": ("a shimmering spell slipping free and humming away, an eerie wavering metal ring",
        {"a": hit("astral", 0.45, 0.5), "b": hit("glass", 0.3, 0.4)},
        [{"source": "a", "chain": [{"fx": "vibrato", "hz": 7, "depth_ms": 3}, {"fx": "tremolo", "hz": 9}, *WIDE]},
         {"source": "b", "gain_db": -9, "offset_ms": 180, "chain": [{"fx": "reverse"}, *WIDE]}]),
    "ss_masterwork": ("a perfect blade quenched and rung true, a clear high bell strike with a hot steam hiss",
        {"a": hit("crystal", 0.45, 0.9), "b": hit("metal", 0.5, 0.85), "c": hit("plasma", 0.6, 0.4)},
        [{"source": "b", "chain": WIDER},
         {"source": "a", "gain_db": -3, "pan": 0.35, "offset_ms": 40, "chain": [{"fx": "shimmer", "semitones": 12, "feedback": 0.4, "mix": 0.3, "room": 0.7}, *WIDE]},
         {"source": "c", "gain_db": -9, "pan": -0.4, "offset_ms": 120, "chain": [{"fx": "stretch", "factor": 1.8}, {"fx": "lowcut", "hz": 2200}, *WIDER]}]),
}


def width(y):
    import numpy as np
    mid, side = y.mean(axis=1), (y[:, 0] - y[:, 1]) / 2
    return float(np.sqrt((side ** 2).mean()) / (np.sqrt((mid ** 2).mean()) or 1))


def sound(redo, only=()):
    if str(mia.AAMT) not in sys.path: sys.path.insert(0, str(mia.AAMT))
    from audio_bench import Bench, save
    from audio_listener import Listener
    from audio_materials import synth
    judge = Listener()
    SFX.mkdir(parents=True, exist_ok=True)
    for name, (purpose, sources, layers) in SOUNDS.items():
        out = SFX / f"{name}.wav"
        if only and name not in only: continue
        if out.exists() and not redo: continue
        takes, recipes = [], []
        for seed in range(1, TAKES + 1):
            src = {k: synth({"seed": seed, **v}) for k, v in sources.items()}
            secs = LENGTH[name]
            glue = [{"fx": "intensity", "amount": GLUE[name]}] if name in GLUE else []
            r = {"seed": seed, "length": secs, "sources": src, "layers": layers,
                 "master": [*ROOM, {"fx": "length", "seconds": secs}, {"fx": "fade", "in_ms": 2, "out_ms": secs * 500}, *glue, {"fx": "level", "rms_db": -19, "peak_db": -1}]}
            y, sr = Bench(44100).render(r)
            takes.append(y); recipes.append(r)
        _, scores = judge.pick(takes, sr, purpose)
        others = [p for n, (p, _, _) in SOUNDS.items() if n != name]
        margins = []
        for take, sc in zip(takes, scores):
            rival = max(judge.score(take, sr, p)["total"] for p in others)
            margins.append(round(sc["total"] - rival, 3))
        best = max(range(len(takes)), key=lambda i: margins[i])
        save(str(out), takes[best], sr)
        meta = {"prompt": purpose, "engine": "audio_materials + audio_bench", "seed": best + 1, "unique": True, "shippable": True, "references": [],
                "score": scores[best]["total"], "margin_over_siblings": margins[best]}
        (SFX / f"{name}.meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
        print(f"{name}: take {best + 1} of {TAKES}, score {scores[best]['total']}, margin over siblings {margins[best]:+.3f}, width {width(takes[best]):.2f}, {len(takes[best]) / sr:.2f}s", flush=True)


def icons(redo):
    todo = [i for i in mci.ICONS if i[0].startswith("ExampleClasses/spellsmith")]
    print(f"{len(todo)} icons", flush=True)
    for rel, style, col, what in todo: mci.concept(rel, style, col, what, redo)
    for rel, style, col, what in todo: mci.icon(rel, style, redo)


def fx(redo):
    sys.argv = ["make_class_particles.py", "--only", "spellsmith"] + (["--redo", "key,video,sheet"] if redo else [])
    mcp.main()


MESHES = [
    ("ss_buried_charge", "low round forged iron deck mine bolted flat to the floor, heavy rivets around the rim, hammered dark steel lid, thin violet and gold glowing seams, wide and squat"),
    ("ss_kindled_shell", "small walking bomb, a riveted hammered iron crucible shell with a glowing molten gold mouth on top and violet seams, standing on four short jointed metal legs, side view, whole object in frame"),
]


def meshes(redo):
    os.environ.setdefault("SD_SKIP_T5", "1")
    for slug, what in MESHES: mia.concept(slug, what, redo)
    ar = mia.shared()
    from sd_server_lifecycle import stop_sd_server
    stop_sd_server()  # TRELLIS needs the whole card
    os.environ["AAMT_TRELLIS_KEEP_SERVER"] = "1"
    os.environ.pop("NoDefaultCurrentDirectoryInExePath", None)
    try:
        ar.ensure("mesh")
        for slug, _ in MESHES: mia.mesh(MOD, slug, redo)
    finally:
        from trellis_http_client import stop_server
        stop_server()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", default="all", choices=["sound", "icons", "fx", "meshes", "gpu", "all"])
    ap.add_argument("--redo", action="store_true")
    ap.add_argument("--only", default="", help="sound stage: comma-separated sound names to make")
    a = ap.parse_args()
    for name, stage in (("sound", sound), ("icons", icons), ("fx", fx), ("meshes", meshes)):
        if a.stage in (name, "all") or (a.stage == "gpu" and name != "sound"):
            try: stage(a.redo, tuple(x for x in a.only.split(",") if x)) if name == "sound" else stage(a.redo)
            except Exception as e: print(f"{name} FAILED: {e}", flush=True)
    print("done", flush=True)


if __name__ == "__main__":
    main()
