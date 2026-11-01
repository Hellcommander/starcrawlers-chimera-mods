"""Every asset for the Shadow Magus (Mods/ShadowMagus), through the AAMT toolkit stages (D:\\games\\Ai assisted toolkit),
the way make_kelvin_glass_assets.py does it:
  icons     - SD3.5 painted emblems (make_class_icons.py style), full bleed          -> ShadowMagus/icons/<name>.png
  particles - SD3.5 key + Wan2.2 clip cut into a flipbook/sprite sheet               -> ShadowMagus/fx/<name>.png
  mesh      - SD3.5 concept on white, then TRELLIS and mesh_edit's repair            -> _modding/item_art/mesh/<slug>.glb
  props     - the repaired mesh, slimmed, as the Shade's body (static until animated) -> ShadowMagus/models/shade.glb
  animate   - UniRig + Wan2.2 + CoTracker3 skinned clips for the Shade (hours)       -> ShadowMagus/models/shade.glb
  reanim    - re-films the REANIM clips only, on the existing rig                    -> ShadowMagus/models/<slug>.glb
  recolour  - turns the Shade's blue seams violet in the shipped .glb (after reanim)  -> ShadowMagus/models/shade.glb
Sounds are not made here: the Breachwork audio pipeline (.claude/skills/breachwork-audio) makes them from licensed clips.
Every stage skips work already on disk, so a stopped run picks up where it left off.
Usage: python make_shadow_magus_assets.py [--stage icons,particles,mesh,props,animate,reanim,recolour|all] [--only name,name] [--redo]"""
import argparse, os, shutil, sys, time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_class_icons as mci
import make_interloper_assets as mi
import make_item_assets as mia

MODS = HERE.parent / "Mods"
SM = "ShadowMagus"
DARK = "deep violet and black"

# (mod, icon name, colour words, what to draw)
ICONS = [
    (SM, "shadow_magus", DARK, "a hooded mage pinching out a candle flame between two fingers, thick shadow pouring from the hand like smoke"),
    (SM, "dark_adapted", DARK, "a single wide eye whose pupil has opened huge and black, faint violet rim, darkness around it"),
    (SM, "snuff", DARK, "two fingers pinching out a small flame, a curl of black smoke rising where the light was"),
    (SM, "lose_them", DARK, "one standing figure splitting into five dark silhouettes flung outward in every direction, violet motion streaks"),
    (SM, "cast_a_shade", DARK, "a mage's shadow peeling up off the floor and standing on its own beside the mage, violet glowing seams"),
    (SM, "shadowflame", DARK, "black flames burning on a riveted metal deck plate, the fire casts no light, violet edges"),
    (SM, "swallowed", DARK, "a huge dark hand closing over a tiny shrinking figure, violet glow leaking between the fingers"),
    (SM, "where_youre_looking", DARK, "a jagged crack of violet light with black shadow pouring through it toward two silhouettes"),
    (SM, "eyes_of_the_dark", DARK, "many narrow glowing violet eyes in pitch darkness around a figure that is fading away"),
    (SM, "the_thing_in_the_dark", "black and sickly violet", "an enormous indistinct many-limbed thing surging out of pitch darkness, rows of pale teeth, one baleful eye"),
    (SM, "benighted", DARK, "an eye with a band of shadow drawn across it like a blindfold made of smoke"),
    (SM, "darkvision", "violet and silver", "a sharp slit-pupil eye glowing violet in total darkness, fine bright details in the iris"),
    (SM, "nightwalker", DARK, "a cloaked figure stepping out of a shaft of light into deep shadow, only the edge lit"),
]

# make_particles recipes; white-tinted sheets take each particle's colour in the mod's particles.json
PARTICLES = [
    (SM, {"name": "sm_dark_smoke", "preset": "smoke_puff", "seed": 141, "key_scale": 0.5, "gamma": 2.4,
          "desc": "a small dense ball of thick white smoke with turbulent curling billows, the whole cloud visible with black space on every side",
          "motion": "The smoke violently billows outward and swells quickly to several times its size, rolling and churning, thick curls tumbling over each other. Fast, turbulent, continuous motion."}),
    (SM, {"name": "sm_void_burst", "preset": "magic_burst", "seed": 142, "key_scale": 0.45,
          "desc": "a small tight knot of thin white smoky tendrils and sparks around a dark centre",
          "motion": "The tendrils whip outward in every direction, lashing and writhing as they shoot away from the centre and spread wide. Very fast, violent, continuous motion."}),
    (SM, {"name": "sm_black_flame", "preset": "flame_loop", "seed": 243, "key_scale": 0.6, "tint": "white", "blend": "alpha",
          "desc": "a wide roaring blaze of many tall licking tongues of white fire, the flames rising from a flat base, detailed wispy edges",
          "motion": "The flames roar and leap upward, tongues splitting, licking and flickering rapidly, each tongue changing shape every moment. Fast, energetic motion."}),
    (SM, {"name": "sm_gloom_pool", "preset": "energy_orb", "seed": 144, "key_scale": 0.85, "blend": "alpha",
          "desc": "a round swirling disc of thick white vapour seen from directly above, spiral wisps curling toward its centre, soft feathered edges",
          "motion": "The vapour disc rotates and churns, wisps curling and swirling around its centre and creeping at the rim. Continuous, clearly visible motion."}),
    (SM, {"name": "sm_eye_flash", "preset": "magic_burst", "seed": 245, "key_scale": 0.6, "grow_in": 2,
          "desc": "one wide horizontal almond-shaped glowing white eye with a thin vertical slit pupil, fine lines of light, wisps trailing from its corners",
          "motion": "The eye glares, the slit pupil narrowing and widening, glow pulsing, wisps of light streaming off its corners. Fast flickering motion."}),
    (SM, {"name": "sm_tendrils", "preset": "magic_burst", "seed": 246, "key_scale": 0.6, "grow_in": 0,
          "desc": "five thick curling tentacles of white smoke spiralling around an empty dark centre, tapered tips, soft wispy edges",
          "motion": "The tentacles of smoke coil and tighten inward around the centre, twisting over each other. Very fast, grasping motion."}),
    (SM, {"name": "sm_rift", "preset": "magic_burst", "seed": 247, "key_scale": 0.7, "grow_in": 2,
          "desc": "a single narrow glowing tear in space, a tall thin pointed ellipse of white light with smoky wisping edges, a dark hollow inside",
          "motion": "The tear widens, its smoky edges rippling and streaming outward, light surging from within. Fast, energetic motion."}),
]

# make_interloper_assets.ASSETS rows: (mod, slug, accent, kind, body plan, prompt)
MESHES = [
    (SM, "shade", "#6A3BFF", "creature", "upright",
     "featureless humanoid figure made of living shadow standing straight, arms held away from its sides, legs apart, "
     "body of smoky matte black with faint violet glowing seams, no face, wisps of dark smoke trailing from its shoulders, front view"),
    (SM, "thing", "#9A6BFF", "creature", "upright",
     "eldritch horror made of living shadow standing upright, a hunched humanoid core of smoky matte black, a crown of writhing "
     "tentacles in place of a head around a cluster of glowing violet eyes, two long clawed arms held away from its sides, legs apart, "
     "thinner tentacles trailing from its back and shoulders, glowing violet arcane sigils carved into its chest, wisps of dark smoke, front view"),
]

# animate stage: the Shade's clips (the props stage already shipped the static mesh; animate overwrites it with the rig)
ANIM_WHEN_READY = {}
ANIM = {
    "shade": ("a featureless humanoid figure made of living shadow", "biped", {
        "attack": "{it} leans far to one side and swings its right arm out to full length, then sweeps it across in a huge slash to the other side, then straightens. Fast, energetic movement.",
        "attack2": "{it} leans its torso sideways and thrusts its right arm straight out to the side at full length, then snaps it back. Fast, energetic movement.",
        "special": "{it} spreads both arms wide as dark smoke pours off it.",
        "hit": "{it} flinches, its shoulders jerking back and its head snapping back, feet planted on the ground, then straightens. Fast, sudden movement.",
        "move": "{it} marches on the spot, lifting each knee in turn and swinging its arms, keeping its upper body level and its head still. Steady rhythmic movement.",
        "idle2": "{it} slowly turns its featureless head left and right as if listening, then settles."}),
    "thing": ("an eldritch horror of living shadow with a crown of tentacles and many violet eyes", "biped", {
        "attack": "{it} leans far to one side and sweeps one long clawed arm out in a huge slash across to the other side, feet planted on the ground, then straightens. Fast, energetic movement.",
        "attack2": "{it} lunges forward, its crown of tentacles whipping ahead of it, then draws back. Fast, energetic movement.",
        "special": "{it} rears up tall and spreads its arms and tentacles wide as darkness pours out of its eyes, then settles.",
        "hit": "{it} jerks backward, its torso bending back and its long arms flailing outward, feet planted on the ground, then straightens. Fast, sudden movement.",
        "idle2": "{it} sways slowly from side to side, its tentacles curling and uncurling, then settles."}),
}


def icons(todo, redo):
    os.environ.setdefault("AAMT_SD_KEEP_SERVER", "1")
    os.environ.setdefault("SD_SKIP_T5", "1")
    for mod, name, col, what in todo:
        rel = f"{mod}/icons/{name}.png"
        (MODS / mod / "icons").mkdir(parents=True, exist_ok=True)
        try:
            mci.concept(rel, "painted", col, what, redo)
            mci.icon(rel, "painted", redo)
        except Exception as e: print(f"icon {rel} FAILED: {e}", flush=True)


def particles(todo, redo):
    ar = mia.shared()
    work = HERE / "item_art" / "particles_sm"
    res = ar.make_particles([r for _, r in todo], work / "out", work=work, redo=["key", "video", "sheet"] if redo else None)
    by = {r["name"]: m for m, r in todo}
    for x in res:
        fx = MODS / by[x["name"]] / "fx"
        fx.mkdir(parents=True, exist_ok=True)
        shutil.copy2(x["texture"], fx / Path(x["texture"]).name)
        print(f"{x['name']}: {x['sheet']['grid']} -> {fx / Path(x['texture']).name}  (preview {x['preview']})", flush=True)


def meshes(todo, redo):
    os.environ.setdefault("AAMT_SD_KEEP_SERVER", "1")
    os.environ.setdefault("SD_SKIP_T5", "1")
    for (_, slug, _, kind, _, what) in todo:
        try: mi.concept(slug, kind, what, redo)
        except Exception as e: print(f"concept {slug} FAILED: {e}", flush=True)
    try:
        from sd_server_lifecycle import stop_sd_server
        stop_sd_server()
    except Exception as e: print("stop sd:", e)
    mi.meshes(todo, redo)


def props(todo, redo):
    for (mod, slug, _, kind, _, _) in todo:
        src, out = mi.MESH_DIR / f"{slug}.glb", MODS / mod / "models" / f"{slug}.glb"
        if slug in ANIM or not src.exists() or (out.exists() and not redo): continue
        out.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, out)
        mia.slim(out)
        print(f"model {slug}: {out.stat().st_size // 1024} KB", flush=True)


def animate(todo, redo):
    ANIM.update(ANIM_WHEN_READY)
    mi.ANIM.update(ANIM)
    for (mod, slug, _, kind, body, _) in todo:
        if kind != "creature": continue
        try: mi.animate(MODS, mod, slug, body, redo)
        except Exception as e: print(f"animate {slug} FAILED: {e}", flush=True)


# reanim stage: re-film only the clips that read badly; the rig, the render and every other clip stay as they are
REANIM = {"shade": ["attack"], "thing": ["attack"]}


def reanim(todo):
    ar = mia.shared()
    for (mod, slug, _, kind, _, _) in todo:
        if slug not in REANIM: continue
        desc, body, prompts = ANIM[slug]
        work, out = mi.ART / "anim" / slug, MODS / mod / "models" / f"{slug}.glb"
        try:
            ar.make_animated_mesh(work / "oriented.glb", out, desc, body=body, work=work, prompts=prompts, clips=REANIM[slug],
                                  only=["video", "fit", "build"], redo=["video", "fit"], seed=53)
            mia.slim(out)
            print(f"reanim {slug}: {out.stat().st_size // 1024} KB", flush=True)
        except Exception as e: print(f"reanim {slug} FAILED: {e}", flush=True)


def recolour(todo):
    """TRELLIS painted the Shade's seams blue: turns every blue in its colour map to the class's violet, in place."""
    import io, json, struct
    import numpy as np
    from PIL import Image
    for (mod, slug, _, _, _, _) in todo:
        if slug != "shade": continue
        glb = MODS / mod / "models" / f"{slug}.glb"
        data = glb.read_bytes()
        jlen = struct.unpack_from("<I", data, 12)[0]
        g = json.loads(data[20:20 + jlen])
        bin_start = 20 + jlen + 8
        blob = data[bin_start:bin_start + struct.unpack_from("<I", data, 20 + jlen)[0]]
        images = {img["bufferView"] for img in g.get("images", [])}
        out = bytearray()
        for vi, v in enumerate(g["bufferViews"]):
            raw = blob[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]]
            if vi in images:
                hsv = np.asarray(Image.open(io.BytesIO(raw)).convert("HSV")).copy()
                blue = (hsv[..., 0] > 135) & (hsv[..., 0] < 190)  # PIL hue is 0-255: about 190 to 270 degrees
                hsv[..., 0][blue] = 192  # 271 degrees, the #6A3BFF family
                buf = io.BytesIO()
                Image.fromarray(hsv, "HSV").convert("RGB").save(buf, "JPEG", quality=88)
                raw = buf.getvalue()
                print(f"recolour {slug}: {blue.mean():.1%} of the map was blue", flush=True)
            while len(out) % 4: out.append(0)
            v["byteOffset"] = len(out); v["byteLength"] = len(raw)
            out += raw
        while len(out) % 4: out.append(0)
        g["buffers"][0]["byteLength"] = len(out)
        j = json.dumps(g, separators=(",", ":")).encode()
        j += b" " * (-len(j) % 4)
        glb.write_bytes(struct.pack("<III", 0x46546C67, 2, 28 + len(j) + len(out)) + struct.pack("<I", len(j)) + b"JSON" + j
                        + struct.pack("<I", len(out)) + b"BIN\x00" + bytes(out))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", default="all")
    ap.add_argument("--only", default="")
    ap.add_argument("--redo", action="store_true")
    a = ap.parse_args()
    stages = ["icons", "particles", "mesh", "props"] if a.stage == "all" else a.stage.split(",")
    only = {s for s in a.only.split(",") if s}
    pick = lambda rows, key: [r for r in rows if not only or key(r) in only]  # noqa: E731
    for st in stages:
        t = time.time()
        print(f"=== {st}", flush=True)
        for attempt in range(4):  # SD is shared with other batches, which may stop it under us; every stage resumes
            try: run(st, a, only, pick); break
            except Exception as e:
                print(f"{st} attempt {attempt + 1} FAILED: {e}", flush=True)
                time.sleep(90)
        print(f"=== {st} done in {time.time() - t:.0f}s", flush=True)


def run(st, a, only, pick):
    if st == "icons": icons(pick(ICONS, lambda r: r[1]), a.redo)
    elif st == "particles": particles(pick(PARTICLES, lambda r: r[1]["name"]), a.redo)
    elif st == "mesh": meshes(pick(MESHES, lambda r: r[1]), a.redo)
    elif st == "props": props(pick(MESHES, lambda r: r[1]), a.redo)
    elif st == "animate": animate(pick(MESHES, lambda r: r[1]), a.redo)
    elif st == "reanim": reanim(pick(MESHES, lambda r: r[1]))
    elif st == "recolour": recolour(pick(MESHES, lambda r: r[1]))


if __name__ == "__main__":
    main()
