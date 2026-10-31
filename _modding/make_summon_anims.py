"""Bodies of their own for class summons that borrowed a vanilla creature (turret, Boltron, Junker, LoaderBot, slime...),
through the AAMT stages, so the skills that call them stop looking like copies of vanilla ones:
  concept - SD3.5 front view on white                                -> _modding/item_art/<slug>.png
  mesh    - TRELLIS (seeds retried if it collapses to a flat card), then the creature repair -> item_art/mesh/<slug>.glb
  orient  - rig_creature.py <plan>:norig, then facing checked by rendering front and back and keeping the one that
            matches the concept (image-to-3D sometimes hands the creature back facing away)
  animate - the animation stage (UniRig + Wan2.2 + CoTracker3)       -> <mod>/models/<slug>.glb
  touch   - touch_up_summons.py fixes for bodies that need them (TOUCH): a baked ground plate, a lost glow
  wire    - "model" added to every summon action with that summonName in the class file (a .bak is kept)
Every stage skips work on disk. Usage: python make_summon_anims.py [--only coil_tower,scrap_bull] [--stage ...]"""
import argparse, json, re, shutil, struct, subprocess, sys, time, zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import os
os.environ.pop("NoDefaultCurrentDirectoryInExePath", None)  # agent shells set it; TRELLIS's launcher bats "call" each other by bare name
import make_item_assets as mia
import make_interloper_assets as mi
from make_item_assets import ART

MODS = HERE.parent / "Mods"
STYLE = "single creature, full body in frame, front view facing the viewer, limbs clear of the body, isolated on plain white background, sci-fi game creature, bright even studio lighting"
NEG = "text, letters, watermark, people, human, multiple creatures, cropped, frame, border, busy background, shadow, floor, ground, dark, black, motion blur, side view, back view"
STILL = "{it} stays in place, humming and shifting slightly, its parts turning and adjusting."

# slug, mod folder, class file, summonName, body (anim gait), rig plan, height (m), concept, description, prompt overrides
SUMMONS = [
    ("scrap_bull", "ArendethClasses", "emberhorn.json", "Scrap Bull", "quadruped", "crawler:quad:noturn:notip", 1.3,
     "mechanical bull built from scrap metal and furnace parts, glowing furnace chest, big horned head, four sturdy legs",
     "a mechanical scrap-metal bull with a glowing furnace chest", {}),
    ("coil_tower", "ArendethClasses", "coilwright.json", "Coil Tower", "hover", "prop", 1.4,
     "small tesla coil tower on a three-legged tripod base, copper coils, glowing blue capacitor sphere on top",
     "a small tesla coil tower on a tripod", {"move": STILL, "attack": "{it} crackles and fires a bolt of lightning from its top, its coils flaring. Fast, energetic movement."}),
    ("heavy_coil", "ArendethClasses", "coilwright.json", "Heavy Coil", "biped", "upright", 1.5,
     "heavy two-legged walking robot carrying a big copper tesla coil on its back, stubby legs, glowing blue capacitor",
     "a heavy walking tesla coil robot", {}),
    ("foundry_watchman", "ArendethClasses", "castborn.json", "Foundry Watchman", "biped", "upright", 1.6,
     "squat cast-iron foundry golem with a glowing molten furnace belly, heavy fists, standing straight, arms away from its sides",
     "a squat cast-iron foundry golem with a glowing furnace belly", {}),
    ("barbvine", "ArendethClasses", "graftwild.json", "Barbvine", "serpent", "prop", 1.2,
     "carnivorous thorny vine plant creature rising from a root mound, several whip-like barbed green tendrils, a toothy flower head",
     "a thorny carnivorous vine plant", {"move": "{it} sways in place, its barbed tendrils lashing and curling.",
                                          "attack": "{it} lashes forward with its barbed tendrils and snaps its toothy flower head. Fast, energetic movement."}),
    ("glyph_sentry", "ArendethClasses", "runeforger.json", "Glyph Sentry", "hover", "hover", 1.3,
     "floating stone sentinel engraved with glowing cyan runes, a single glowing eye, two hovering stone fists",
     "a floating rune-carved stone sentinel", {}),
    ("thunderhead", "ArendethClasses", "squallblade.json", "Thunderhead", "hover", "hover", 1.2,
     "small living storm cloud creature, dark grey cloud body, glowing white lightning eyes, wispy cloud arms",
     "a small living storm cloud", {}),
    ("clotling", "ArendethClasses", "vasculist.json", "Clotling", "crawler", "crawler:noturn:notip", 0.7,
     "small blob creature made of clotted blood, dark red jelly body with visible veins, four stubby legs, beady eyes",
     "a small blob of clotted blood", {}),
    ("tidebound", "ArendethClasses", "tidewright.json", "Tidebound", "biped", "upright", 1.5,
     "humanoid creature made of swirling blue seawater with a coral and kelp core, standing, arms away from its sides",
     "a humanoid creature of swirling seawater", {}),
    ("rattletrap", "CustomClasses", "mad_tinkerer.json", "Rattletrap", "biped", "upright", 1.1,
     "rickety little robot built from junk, dented tin-can body, mismatched legs, a big antenna, a single camera eye",
     "a rickety junk-built little robot", {}),
    ("hijacked_sentry", "CustomClasses", "void_hacker.json", "Hijacked Sentry", "hover", "prop", 1.2,
     "security sentry turret corrupted by glitches, cracked magenta screens, loose cables, a twin gun barrel head",
     "a glitching corrupted sentry turret", {"move": STILL}),
]

# touch_up_summons.py ops per summon, on the finished glb (concept paths are relative to the game folder)
TOUCH = {
    "scrap_bull": ["plate"],  # TRELLIS baked the concept's ground shadow into a plate bridging the front feet
    "foundry_watchman": ["project=_modding/item_art/foundry_watchman.png:0.85"],  # bake lost the furnace belly; washed-out bronze
    "hijacked_sentry": ["project=_modding/item_art/hijacked_sentry.png:0.85"],  # bake lost the magenta screen
}


def log(*a):
    print(*a, flush=True)


def extents(glb):
    b = Path(glb).read_bytes(); n = struct.unpack_from("<I", b, 12)[0]; g = json.loads(b[20:20 + n])
    acc = [g["accessors"][p["attributes"]["POSITION"]] for m_ in g["meshes"] for p in m_["primitives"]]
    lo = [min(a["min"][i] for a in acc) for i in range(3)]; hi = [max(a["max"][i] for a in acc) for i in range(3)]
    return [h - l for l, h in zip(lo, hi)]


def concept(ar, slug, what):
    out = ART / f"{slug}.png"
    if out.exists(): return out
    from sd_http_client import plan_sd_size
    w, h, steps = plan_sd_size(512, 512, kind="icon")
    ar.make_image(f"{what}, {STYLE}", out, negative_prompt=NEG, width=w, height=h, steps=steps, seed=zlib.crc32(slug.encode()) % 100000)
    return out


def mesh(ar, slug):
    raw, fixed = mi.MESH_DIR / f"{slug}_raw.glb", mi.MESH_DIR / f"{slug}.glb"
    if not raw.exists():
        for seed in (1234, 77, 2024, 9):
            ar.ensure("mesh")  # TRELLIS stops itself after each job
            ar.make_mesh(mi.trellis_input(slug), raw, resolution=512, mesh_simplify=10, texture_size=1024, seed=seed)
            e = extents(raw)
            if min(e) > 0.12 * max(e): break  # a real body, not a flat card
            log(f"{slug}: seed {seed} collapsed to a card {[round(x, 2) for x in e]}, retrying")
    if not fixed.exists():
        ar.edit_mesh(raw, fixed, mi.FIX_CREATURE)
    return fixed


def orient(ac, slug, plan, fixed):
    """norig with the plan; then front-vs-back renders against the concept, turning 180 degrees if the back wins."""
    work = ART / "anim" / slug
    work.mkdir(parents=True, exist_ok=True)
    oriented = work / "oriented.glb"
    if oriented.exists(): return work
    base = plan if plan != "prop" else "upright:noarms"
    for turn in ("", ":rz180"):
        r = subprocess.run([str(ac.blender_exe()), "-b", "--factory-startup", "-P", str(mi.BLENDER_SCRIPT), "--", str(fixed),
                            str(oriented), base + turn + ":norig"], capture_output=True, text=True)
        ac.prep(oriented, work)
        if turn: break
        if facing_ok(ac, work, ART / f"{slug}.png"): break
        log(f"{slug}: rendered back matches the concept better than the front; turning it round")
    for f in ("frame0.png", "cam.json", "visible.npy"):
        (work / f).unlink(missing_ok=True)
    return work


def facing_ok(ac, work, concept_png):
    """True if the view from the front (-Y) looks more like the concept than the view from behind."""
    tmp = work / "_facing"
    scores = []
    for az in (0, 180):
        d = tmp / str(az); d.mkdir(parents=True, exist_ok=True)
        shutil.copy2(work / "norm.glb", d / "norm.glb")
        ac._blender("render", d, 384, 384, az, 8, label=f"facing {az}")
        scores.append(similarity(d / "frame0.png", concept_png))
    shutil.rmtree(tmp, ignore_errors=True)
    log(f"facing scores front {scores[0]:.3f} back {scores[1]:.3f}")
    return scores[0] >= scores[1]


def similarity(img, concept_png):
    """Colour histogram + grayscale shape correlation of the object (non-white) against the concept and its mirror."""
    import numpy as np
    from PIL import Image, ImageOps

    def obj(p):
        im = Image.open(p).convert("RGB")
        a = np.asarray(im).astype(np.float32)
        m = a.min(-1) < 225
        ys, xs = np.nonzero(m)
        if not len(ys): return None
        return im.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)).resize((96, 96))

    a, b = obj(img), obj(concept_png)
    if a is None or b is None: return 0.0
    best = -1.0
    for bb in (b, ImageOps.mirror(b)):
        x, y = np.asarray(a).astype(np.float32), np.asarray(bb).astype(np.float32)
        hx = np.histogramdd(x.reshape(-1, 3), bins=6, range=[(0, 256)] * 3)[0].ravel(); hx /= hx.sum()
        hy = np.histogramdd(y.reshape(-1, 3), bins=6, range=[(0, 256)] * 3)[0].ravel(); hy /= hy.sum()
        hist = float(np.minimum(hx, hy).sum())
        gx, gy = x.mean(-1).ravel(), y.mean(-1).ravel()
        corr = float(np.corrcoef(gx, gy)[0, 1])
        best = max(best, hist + corr)
    return best


def wire(mod, cls_file, name, slug, height):
    """Adds the model to every summon action with this summonName that has none yet."""
    p = MODS / mod / cls_file
    s = p.read_text(encoding="utf-8")
    spec = f'"model": {{ "mesh": "models/{slug}.glb", "height": {height}, "keepParticles": false }}'
    pat = re.compile(r'("summonName"\s*:\s*"' + re.escape(name) + r'")(?![^{}]*"model")')
    new, n = pat.subn(lambda m: m.group(1) + ", " + spec, s)
    if n:
        shutil.copy2(p, p.with_suffix(".json.bak"))
        json.loads(re.sub(r"(?m)^\s*//.*$", "", new))  # still valid JSON
        p.write_text(new, encoding="utf-8")
    log(f"wired {name}: {n} summon action(s) in {cls_file}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--stage", default="concept,mesh,orient,animate,touch,wire")
    a = ap.parse_args()
    only = {s for s in a.only.split(",") if s}
    stages = set(a.stage.split(","))
    ar = mia.shared()
    import anim_client as ac
    for slug, mod, cls_file, name, body, plan, height, what, desc, prompts in SUMMONS:
        if only and slug not in only: continue
        t = time.time()
        try:
            if "concept" in stages:
                ar.ensure("image"); concept(ar, slug, what)
            if "mesh" in stages:
                from sd_server_lifecycle import stop_sd_server
                stop_sd_server(force=True)
                fixed = mesh(ar, slug)
            else:
                fixed = mi.MESH_DIR / f"{slug}.glb"
            if "orient" in stages:
                work = orient(ac, slug, plan, fixed)
            work = ART / "anim" / slug
            out = MODS / mod / "models" / f"{slug}.glb"
            if "animate" in stages:
                out.parent.mkdir(parents=True, exist_ok=True)
                rep = ac.animate(work / "oriented.glb", out, desc, body=body, work=work, prompts=prompts)
                mia.slim(out)
                out.with_suffix(".glb.bak").unlink(missing_ok=True)  # touch-ups start from this build, not an older one
                log(f"{slug}: rerolls {json.dumps(rep.get('rerolls'))}")
            if "touch" in stages and slug in TOUCH and out.exists():
                subprocess.run([sys.executable, str(HERE / "touch_up_summons.py"), str(out), *TOUCH[slug]], cwd=HERE.parent, check=True)
            if "wire" in stages and out.exists():
                wire(mod, cls_file, name, slug, height)
            log(f"summon {slug}: {time.time() - t:.0f}s")
        except Exception as e:  # noqa: BLE001
            import traceback
            traceback.print_exc()
            log(f"summon {slug} FAILED: {e}")


if __name__ == "__main__":
    main()
