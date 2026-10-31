"""Wisps: summons and creatures that are a mesh core with particle layers living on it (the loader's model
"particles" and "deathVfx", see ModelParticles.cs). One recipe per wisp drives both asset pipelines:
  mesh  - the core: procedural (make_summon_models.py), or with --core ai an AAMT concept (SD3.5) made into a .glb by
          TRELLIS or Stable Fast 3D                                        -> Mods/<mod>/models/<name>[_ai].glb/.obj
  fx    - each "gen:" texture through the AAMT particle stage (SD3.5 key -> Wan2.2 clip -> flipbook atlas)
                                                                           -> Mods/<mod>/fx/<tex>.png
  wire  - the wisp's effects (attached layers and its death burst) into Mods/<mod>/wisp_fx.json, and its "model"
          block into _modding/item_art/wisps/<name>_model.json to paste into the class or creature file
A texture that isn't generated yet uses the layer's stand-in (a built-in sprite, with its own emitter settings), and
"class:<name>" reuses an atlas the class particle stage already made, so wire runs with no GPU at all.
GPU stages run only with --gpu, all in this one process (SD keys and concepts, then TRELLIS, then Wan), one at a time.
Work goes to _modding/item_art/wisps/. Usage:
  python make_wisp_assets.py [--only hoarlight,zenith_hoarlight] [--stage mesh,fx,wire] [--gpu] [--core ai]
                             [--redo key,video,sheet,concept,mesh]"""
import argparse, copy, json, shutil, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
MODS = HERE.parent / "Mods"
AAMT = Path(r"D:\games\Ai assisted toolkit\Tools\Shared")
ART = HERE / "item_art" / "wisps"
CLASS_FX = HERE / "item_art" / "particles" / "class" / "out"  # make_class_particles.py output (grids)
SKILL_FX = MODS / "SkillVfx" / "fx"

FROST, DEEP = "#F0FBFF", "#5AB8FF"

# Our own words for every prompt. Layer fields are CustomVfx's emitter fields; "standin" overrides them while the
# layer's "gen:" atlas doesn't exist yet. Attached effects loop on the wisp ("follow": false leaves a trail).
WISPS = [
    {"name": "hoarlight", "mod": "ArendethClasses",
     "core": {"procedural": "hoarlight",
              "ai": {"desc": "a small glowing teardrop crystal of pale blue ice light with three thin icicle "
                             "splinters hanging from its middle, floating", "mesher": "fast"}},
     "model": {"height": 0.42, "offset": [0, 1.05, 0], "tint": "#DDF6FF", "glow": "#9fe8ff", "glowStrength": 1.0,
               "bob": 0.1, "spin": 70, "pulse": 0.06, "keepParticles": False},
     # A "sphere of mist" came back as a hard snowball; ask for light with no surface instead.
     "gen": {"hoarlight_halo": {"preset": "energy_orb", "seed": 24,
                                "desc": "a faint diffuse glow of pale blue light, a soft hazy aura brightest at its "
                                        "centre and fading smoothly to nothing at its edges, with no solid surface, a "
                                        "few tiny specks of ice glitter floating in it",
                                "motion": "The orb of frost light breathes and swirls gently in place while specks of "
                                          "glitter drift slowly around it. Continuous, calm motion."}},
     "attach": [
         {"id": "RimeHoarlightHalo", "offset": [0, 1.05, 0], "layers": [
             {"texture": "gen:hoarlight_halo", "count": 0, "rate": 2.5, "duration": 1, "life": [1.2, 1.6], "speed": [0],
              "size": [0.55, 0.7], "sizeCurve": "pulse", "shape": "point", "color": "#CFF4FF", "color2": "#6FB8FF",
              "alpha": 0.55, "standin": {"texture": "soft", "size": [0.5, 0.65], "alpha": 0.45}}]},
         {"id": "RimeHoarlightMotes", "offset": [0, 1.05, 0], "layers": [
             {"texture": "mote", "count": 0, "rate": 7, "duration": 1, "life": [1.4, 2.0], "speed": [0.02, 0.08],
              "size": [0.035, 0.07], "sizeCurve": "pop", "shape": "ring", "radius": 0.28, "orbit": 0.35,
              "color": FROST, "color2": DEEP}]}],
     "death": {"id": "RimeHoarlightBurst", "layers": [
         {"texture": "class:rimelight_seer_burst", "count": 1, "life": [0.5], "speed": [0], "size": [1.6],
          "sizeCurve": "flat", "shape": "point", "color": FROST, "color2": DEEP},
         {"texture": "class:rimelight_seer_bits", "blend": "alpha", "randomFrame": True, "count": 8,
          "life": [0.5, 0.8], "speed": [1.5, 3], "size": [0.07, 0.13], "gravity": 1.2, "spin": [-360, 360],
          "shape": "sphere", "radius": 0.1, "color": "#FFFFFF"}],
         "light": {"color": "#9fe8ff", "range": 3, "intensity": 2, "time": 0.4}}},

    {"name": "zenith_hoarlight", "mod": "ArendethClasses",
     "core": {"procedural": "omega_wisp",
              "ai": {"desc": "a glowing faceted core of white-blue mind light inside a thin tilted ring of frost, "
                             "six sharp ice shards pointing outward around it, floating", "mesher": "trellis"}},
     "model": {"height": 0.75, "offset": [0, 1.0, 0], "glow": "#9fe8ff", "glowStrength": 1.2, "bob": 0.12,
               "spin": 40, "pulse": 0.05},
     "gen": {"zenith_halo": {"preset": "energy_orb", "seed": 22,
                             "desc": "a radiant sphere of cold blue-white light wrapped in a thin shimmering ring of "
                                     "frost, crystalline glints caught in its glow",
                             "motion": "The sphere of cold light slowly turns and pulses while its frost ring "
                                       "shimmers and glints travel around it. Continuous, steady motion."}},
     "attach": [
         {"id": "RimeZenithHalo", "offset": [0, 1.0, 0], "layers": [
             {"texture": "gen:zenith_halo", "count": 0, "rate": 2.5, "duration": 1, "life": [1.3, 1.7], "speed": [0],
              "size": [0.72, 0.85], "sizeCurve": "pulse", "shape": "point", "color": "#E6FAFF", "color2": "#7FC8FF",
              "alpha": 0.4, "standin": {"texture": "soft", "size": [0.85, 1.0], "alpha": 0.4}}]},
         {"id": "RimeZenithShards", "offset": [0, 1.0, 0], "layers": [
             {"texture": "class:rimelight_seer_bits", "blend": "alpha", "randomFrame": True, "count": 0, "rate": 3,
              "duration": 1, "life": [2.5, 3.5], "speed": [0], "size": [0.07, 0.12], "sizeCurve": "pop",
              "shape": "ring", "radius": 0.5, "orbit": 0.22, "spin": [-90, 90], "color": "#FFFFFF"},
             {"texture": "mote", "count": 0, "rate": 6, "duration": 1, "life": [1.5, 2.2], "speed": [0.02, 0.06],
              "size": [0.03, 0.06], "sizeCurve": "pop", "shape": "ring", "radius": 0.36, "orbit": -0.3,
              "radial": 0.05, "color": FROST, "color2": DEEP}]},
         {"id": "RimeZenithTrail", "offset": [0, 1.0, 0], "follow": False, "layers": [
             {"texture": "soft", "count": 0, "rate": 14, "duration": 1, "life": [0.5, 0.9], "speed": [0],
              "size": [0.12, 0.2], "sizeCurve": "shrink", "shape": "sphere", "radius": 0.08, "color": "#BFEFFF",
              "color2": "#3F7FD0", "alpha": 0.6}]}],
     "death": {"id": "RimeZenithBurst", "layers": [
         {"texture": "class:rimelight_seer_burst", "count": 1, "life": [0.7], "speed": [0], "size": [2.6],
          "sizeCurve": "flat", "shape": "point", "color": FROST, "color2": DEEP},
         {"texture": "ring", "flat": True, "count": 1, "life": [0.6], "speed": [0], "size": [2.2], "sizeCurve": "grow",
          "shape": "point", "color": "#CFF4FF", "alpha": 0.8},
         {"texture": "class:rimelight_seer_bits", "blend": "alpha", "randomFrame": True, "count": 16,
          "life": [0.6, 1.0], "speed": [2, 4], "size": [0.09, 0.16], "gravity": 1.2, "spin": [-360, 360],
          "shape": "sphere", "radius": 0.15, "color": "#FFFFFF"}],
         "light": {"color": "#9fe8ff", "range": 4, "intensity": 2.5, "time": 0.6}}},

    {"name": "shardspark", "mod": "ArendethClasses",
     "core": {"procedural": "shardspark",
              "ai": {"desc": "three sharp splinters of glowing blue ice crossed through one bright point, a small "
                             "star of broken frost crystal, floating", "mesher": "fast"}},
     "model": {"height": 0.36, "offset": [0, 0.95, 0], "tint": "#E8FBFF", "glow": "#bff0ff", "glowStrength": 1.4,
               "spin": 160, "wobble": 8, "pulse": 0.1, "keepParticles": False},
     "gen": {"shardspark_crackle": {"preset": "electric", "seed": 23,
                                    "desc": "a crackle of brittle frost light, jagged white-blue splinters of ice "
                                            "lightning branching out from one bright point",
                                    "motion": "Splinters of frost light flicker and snap outward, branching and "
                                              "re-forming every moment around the bright point. Fast, restless motion."}},
     "attach": [
         {"id": "RimeShardsparkCrackle", "offset": [0, 0.95, 0], "layers": [
             {"texture": "gen:shardspark_crackle", "count": 0, "rate": 6, "duration": 1, "life": [0.15, 0.3],
              "speed": [0], "size": [0.35, 0.5], "randomFrame": True, "sizeCurve": "flat", "shape": "sphere",
              "radius": 0.05, "color": FROST,
              "standin": {"texture": "spark", "rate": 14, "life": [0.12, 0.25], "speed": [1, 2], "size": [0.04, 0.08],
                          "randomFrame": False, "sizeCurve": "shrink", "stretch": 2, "drag": 2, "radius": 0.08,
                          "color2": DEEP}},
             {"texture": "soft", "count": 0, "rate": 3, "duration": 1, "life": [0.8, 1.1], "speed": [0],
              "size": [0.35, 0.45], "sizeCurve": "pulse", "shape": "point", "color": "#CFF4FF", "alpha": 0.55}]}],
     "death": {"id": "RimeShardsparkBurst", "layers": [
         {"texture": "class:rimelight_seer_burst", "count": 1, "life": [0.4], "speed": [0], "size": [1.2],
          "sizeCurve": "flat", "shape": "point", "color": FROST, "color2": DEEP},
         {"texture": "class:rimelight_seer_bits", "blend": "alpha", "randomFrame": True, "count": 10,
          "life": [0.4, 0.7], "speed": [2, 3.5], "size": [0.05, 0.1], "gravity": 1, "spin": [-360, 360],
          "shape": "sphere", "radius": 0.08, "color": "#FFFFFF"}],
         "light": {"color": "#bff0ff", "range": 2.5, "intensity": 2, "time": 0.3}}},
]


def aamt():
    if str(AAMT) not in sys.path: sys.path.insert(0, str(AAMT))
    import ai_resources
    return ai_resources


# ---- mesh ----------------------------------------------------------------------------------------------------

def core_path(w, core):
    models = MODS / w["mod"] / "models"
    if core == "ai":
        return models / f"{w['name']}_ai.glb"
    return models / f"{w['core']['procedural']}.obj"


def procedural_core(w, redo):
    out = core_path(w, "procedural")
    if out.exists() and not redo: return out
    sys.path.insert(0, str(HERE))
    import make_summon_models as msm
    getattr(msm, w["core"]["procedural"])(str(out.parent))
    return out


def concepts(ws, redo):
    """Every AI core's concept, before any mesh job (SD and TRELLIS don't fit in VRAM together)."""
    ar = aamt()
    for w in ws:
        c = ART / w["name"] / "concept.png"
        if c.exists() and not redo: continue
        c.parent.mkdir(parents=True, exist_ok=True)
        ar.make_image(f"{w['core']['ai']['desc']}, a single object, centred, isolated on a plain flat light grey "
                      "background, studio lighting, no shadow, high detail, game asset, no text", c,
                      negative_prompt="text, watermark, border, frame, ground, floor, scenery, people, cropped, "
                                      "multiple objects", width=1024, height=1024, steps=28, seed=31)
        print("concept", c)


def ai_core(w, redo):
    out = core_path(w, "ai")
    if out.exists() and not redo: return out
    src = ART / w["name"] / "concept.png"
    if not src.exists(): print(f"{w['name']}: no concept yet"); return None
    sys.path.insert(0, str(HERE))
    from make_item_assets import cutout
    from PIL import Image
    obj = cutout(Image.open(src))
    k = 360 / max(obj.size)
    obj = obj.resize((max(1, int(obj.width * k)), max(1, int(obj.height * k))), Image.LANCZOS)
    pad = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    pad.alpha_composite(obj, ((512 - obj.width) // 2, (512 - obj.height) // 2))
    mask = ART / w["name"] / "concept_mask.png"
    pad.save(mask)
    ar = aamt()
    if w["core"]["ai"].get("mesher") == "fast":
        ar.make_fast_mesh(mask, out)
    else:
        ar.make_mesh(mask, out, resolution=512, mesh_simplify=10, texture_size=512)
    print("mesh", out)
    return out


# ---- fx ------------------------------------------------------------------------------------------------------

def fx_dir(w): return MODS / w["mod"] / "fx"


def gen_textures(ws, redo, stage):
    rs = [{"name": n, **g} for w in ws for n, g in w.get("gen", {}).items()]
    if not rs: return
    sys.path.insert(0, str(AAMT))
    from particle_client import make_particles
    res = make_particles(rs, ART / "fx_out", work=ART / "fx", only=stage, redo=redo)
    owner = {n: w for w in ws for n in w.get("gen", {})}
    for x in res:
        d = fx_dir(owner[x["name"]]); d.mkdir(parents=True, exist_ok=True)
        shutil.copy2(x["texture"], d / Path(x["texture"]).name)
        print(f"{x['name']}: {x['sheet']['grid']} -> {d / Path(x['texture']).name}  (preview {x['preview']})")


# ---- wire ----------------------------------------------------------------------------------------------------

def _grid_of(effect_json):
    try: return json.loads(Path(effect_json).read_text(encoding="utf-8"))["layers"][0].get("grid")
    except (OSError, KeyError, IndexError, ValueError): return None


def resolve_layer(w, layer):
    """A recipe layer -> a CustomVfx layer: "gen:" and "class:" textures become the mod's own fx/ copy (with its
    grid), or the stand-in when there is none yet."""
    l = copy.deepcopy(layer)
    standin = l.pop("standin", None)
    tex = l.get("texture", "soft")
    src, grid = None, None
    if tex.startswith("gen:"):
        n = tex[4:]
        src, grid = fx_dir(w) / f"{n}.png", _grid_of(ART / "fx_out" / f"{n}_effect.json")
    elif tex.startswith("class:"):
        n = tex[6:]
        src, grid = SKILL_FX / f"{n}.png", _grid_of(CLASS_FX / f"{n}_effect.json")
    if src is None: return l
    if src.exists():
        dst = fx_dir(w) / src.name
        if src != dst:
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)
        l["texture"] = f"fx/{src.name}"
        if grid and grid[0] * grid[1] > 1: l["grid"] = grid
        return l
    l.update(standin or {"texture": "soft"})
    l.pop("grid", None)
    print(f"  {w['name']}: {tex} not made yet, using stand-in '{l['texture']}'")
    return l


def wire(ws, core):
    by_mod = {}
    for w in ws:
        fxs = [{"id": a["id"], "layers": [resolve_layer(w, l) for l in a["layers"]], **({"light": a["light"]} if a.get("light") else {})}
               for a in w["attach"]]
        d = w["death"]
        fxs.append({"id": d["id"], "layers": [resolve_layer(w, l) for l in d["layers"]], **({"light": d["light"]} if d.get("light") else {})})
        by_mod.setdefault(w["mod"], {})[w["name"]] = fxs

        mesh = core_path(w, core)
        if not mesh.exists(): mesh = core_path(w, "procedural")
        model = {"mesh": f"models/{mesh.name}"}
        tex = mesh.with_suffix(".png")
        if mesh.suffix == ".obj" and tex.exists(): model["texture"] = f"models/{tex.name}"
        model.update(w["model"])
        # An effect sits at the model's own offset unless it says otherwise.
        model["particles"] = [{"id": a["id"], **({"offset": a["offset"]} if a.get("offset") and a["offset"] != model.get("offset") else {}),
                               **({"follow": False} if a.get("follow") is False else {})} for a in w["attach"]]
        model["deathVfx"] = d["id"]
        p = ART / f"{w['name']}_model.json"
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(json.dumps({"model": model}, indent=1), encoding="utf-8")
        print(f"{w['name']}: model block -> {p}")

    for mod, wisps in by_mod.items():
        path = MODS / mod / "wisp_fx.json"
        # Keep other wisps' entries when only some were rebuilt.
        old = json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}
        mine = {fx["id"] for fxs in wisps.values() for fx in fxs}
        kept = [fx for fx in old.get("particles", []) if fx.get("id") not in mine]
        allfx = kept + [fx for fxs in wisps.values() for fx in fxs]
        head = "Wisp particle layers, written by _modding/make_wisp_assets.py (edit the recipes there, not this file)."
        path.write_text(json.dumps({"_comment": head, "particles": allfx}, indent=1), encoding="utf-8")
        print(f"{path}: {len(allfx)} effects")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    ap.add_argument("--stage", default="mesh,fx,wire")
    ap.add_argument("--gpu", action="store_true", help="allow the GPU stages (AI cores, generated flipbooks)")
    ap.add_argument("--core", choices=["procedural", "ai"], default="procedural")
    ap.add_argument("--redo", default="", help="comma list of key,video,sheet,concept,mesh")
    a = ap.parse_args()
    ws = [w for w in WISPS if not a.only or w["name"] in a.only.split(",")]
    stages, redo = set(a.stage.split(",")), set(filter(None, a.redo.split(",")))
    if "mesh" in stages:
        for w in ws: procedural_core(w, "mesh" in redo and a.core == "procedural")
        if a.core == "ai":
            if not a.gpu: sys.exit("--core ai needs --gpu (SD3.5 concept + TRELLIS/SF3D)")
            concepts(ws, "concept" in redo)
            try:
                from sd_server_lifecycle import stop_sd_server
                stop_sd_server()
            except Exception as e: print("could not stop SD before meshing:", e)
            for w in ws: ai_core(w, "mesh" in redo)
    if "fx" in stages and a.gpu:
        gen_textures(ws, [r for r in redo if r in ("key", "video", "sheet")] or None, None)
    elif "fx" in stages:
        print("fx: skipped without --gpu (stand-ins are used for textures not made yet)")
    if "wire" in stages: wire(ws, a.core)


if __name__ == "__main__":
    main()
