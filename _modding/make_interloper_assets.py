"""Art for the side-area work and the Interlopers enemies, through the AAMT toolkit (D:\\games\\Ai assisted toolkit):
  concept - Stable Diffusion 3.5 render on plain white                         -> _modding/item_art/<slug>.png
  icon    - effect icons: the concept cut out onto the mods' backdrop and frame -> <mod>/icons/<slug>.png
  mesh    - creatures: TRELLIS image-to-3D; props: Stable Fast 3D; then mesh_edit's repair -> _modding/item_art/mesh/<slug>.glb
  animate - creatures: the AAMT animation stage (anim_client.py): rig_creature.py <plan>:norig orients and cleans the
            mesh, UniRig rigs and skins it, Wan2.2 films each action from a render of it, and the bones are fitted to
            CoTracker3 tracks of that footage                                   -> <mod>/models/<slug>.glb
            (work files in _modding/item_art/anim/<slug>; hours per creature on the 2080 Ti, every step cached)
  rig     - the older, offline path: rig_creature.py (Blender) guesses the bones from the mesh and keyframes
            idle/move/attack/hit/death by hand; also what props go through (copied as they are)
Every stage skips work already on disk, so a stopped batch picks up where it left off. "all" animates creatures and
falls back to the rig stage for any the animation stage can't do.
Usage: python make_interloper_assets.py <Mods folder> [--only slug,slug] [--stage concept|icon|mesh|animate|rig|all] [--redo]
       [--anim-only prep,rig,render,video,fit,build] [--anim-redo video,fit]"""
import argparse, os, shutil, subprocess, sys, time, zlib
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_item_assets as mia
from make_item_assets import ART, cutout
from make_icons import background, frame, rgba, W, S
from PIL import ImageFilter

CREATURE = "single creature, full body in frame, limbs clear of the body, isolated on plain white background, sci-fi game monster, bright even studio lighting"
PROP = "single object, centered, whole object in frame, isolated on plain white background, three-quarter view, sci-fi, detailed, studio lighting"
ICON = "single symbol, centered, isolated on plain white background, bold simple shape, game ability icon, sci-fi, glossy"
NEGATIVE = "text, letters, watermark, people, human, multiple creatures, cropped, frame, border, busy background, shadow, floor, ground, dark, black, motion blur"

# kind: creature (rig = body plan for rig_creature.py) | prop | icon
ASSETS = [
    # Interlopers: creatures (mod, slug, accent, kind, rig, prompt)
    ("Interlopers", "riot_bulwark", "#6FB7FF", "creature", "upright", "bulky two-legged riot robot standing straight, arms held away from its sides, legs apart, riot shield on its left forearm, light grey and bright blue armour, red visor"),
    ("Interlopers", "fusebot", "#FF9F43", "creature", "hover", "small spherical hovering maintenance drone, glowing orange core, two short jointed claw arms at its sides, light grey and yellow striped plates, no legs, no wheels"),
    ("Interlopers", "burstcap", "#9BE15D", "creature", "upright:noarms", "walking fungus creature, cracked mottled brown and moss green mushroom cap with spore pods, fibrous pale stalk, clawed root legs, muted colours"),
    ("Interlopers", "shellback", "#B07CFF", "creature", "crawler:notip:noturn:rz180", "side view of an alien crab beast standing high on six jointed legs, body raised off the ground, two big claws held up in front, glossy purple domed shell, teal legs"),
    ("Interlopers", "skitterling", "#C9A06A", "creature", "crawler", "small alien tick insect seen from above at an angle, flat round armoured brown body, eight thin jointed legs spread out, tiny head with mandibles, no eyes"),
    ("Interlopers", "mender_unit", "#7CE08A", "creature", "hover", "hovering medical robot drone, rounded white body, green cross light, round lens face, two thin jointed arms with syringe tips at its sides, no legs, no feet"),
    ("Interlopers", "vaultback", "#FF5C5C", "creature", "crawler:quad:headright", "side view of a lean robotic attack hound standing on four straight legs, long body, light grey and red armour plates, jet vents on its back, glowing red eyes"),
    ("Interlopers", "ventmaw", "#C97A4A", "creature", "serpent", "giant armoured worm stretched out flat on the ground, side view, long straight segmented body, rusty orange plates, round toothed mouth on the left, tapered tail on the right"),
    # Side areas: objective props
    ("SideAreas", "uplink_node", "#5CE1FF", "prop", None, "short sturdy data uplink terminal box on a metal stand, glowing cyan screen, small antenna, plain pure white background"),
    ("SideAreas", "siege_beacon", "#FF8F5C", "prop", None, "distress beacon tower on three tripod legs, big pulsing orange lamp on top, warning stripes"),
    # effect icons
    ("SideAreas", "stim_haze", "#9FE08A", "icon", None, "green chemical stim injector vial leaking glowing green vapour cloud"),
    ("SideAreas", "chilled", "#9FE6FF", "icon", None, "pale blue ice crystal snowflake with frost"),
    ("SideAreas", "armored", "#C9D6E0", "icon", None, "heavy steel armour plate shield with rivets"),
    ("SideAreas", "frenzied", "#FF5C5C", "icon", None, "three parallel red claw scratch marks slashed diagonally, sharp edges, glowing red"),
    ("SideAreas", "hulking", "#FFC35C", "icon", None, "huge flexed metal robot arm bicep, orange glow"),
    ("SideAreas", "shrouded", "#B8A8FF", "icon", None, "purple hooded cloak silhouette fading into mist"),
    ("SideAreas", "mending", "#7CE08A", "icon", None, "green medical cross made of glowing nanite particles"),
    ("SideAreas", "rival", "#E070A0", "icon", None, "pink skull wearing a combat visor with a gold star"),
    ("Interlopers", "infested", "#C9A06A", "icon", None, "tiny brown mites crawling out of a bite wound mark, sickly"),
    ("Interlopers", "rallied", "#FFD966", "icon", None, "golden military megaphone loudspeaker with sound waves"),
    ("Interlopers", "scanned", "#7CE08A", "icon", None, "green targeting reticle over a heartbeat line"),
    ("Interlopers", "hardened", "#B07CFF", "icon", None, "purple crab shell carapace dome with a shimmering barrier"),
    ("Interlopers", "sporesick", "#9BE15D", "icon", None, "green spore cloud bursting from a mushroom cap"),
    ("Interlopers", "overloading", "#FF9F43", "icon", None, "glowing orange overheating reactor core with warning triangle"),
]

# SF3D leaves shards (a voxel remesh fuses them); TRELLIS comes back whole and only needs thinning
FIX_PROP = ["--remesh", "0.008", "--decimate", "12000", "--drop-small", "0.02", "--texture-size", "1024"]
FIX_CREATURE = ["--decimate", "14000", "--drop-small", "0.02", "--min-luma", "80", "--texture-size", "1024"]
# per creature, after the usual repair: mender_unit's syringe tips come back as loose blobs floating under it;
# shellback's mesh is TRELLIS candidate 2 at seed 1234 (seed 1234 on candidate 3 and others collapsed to a flat card)
EXTRA_FIX = {"mender_unit": ["--drop-small", "0.08"],
             "shellback": ["--match-back", "0.8"]}  # TRELLIS guessed its back pale grey
BLENDER_SCRIPT = HERE / "rig_creature.py"
MESH_DIR = ART / "mesh"


def concept(slug, kind, what, redo):
    out = ART / f"{slug}.png"
    if out.exists() and not redo: return out
    ART.mkdir(parents=True, exist_ok=True)
    ar = mia.shared()
    from sd_http_client import plan_sd_size
    w, h, steps = plan_sd_size(512, 512, kind="icon")
    style = {"creature": CREATURE, "prop": PROP, "icon": ICON}[kind]
    t = time.time()
    ar.make_image(f"{what}, {style}", out, negative_prompt=NEGATIVE, width=w, height=h, steps=steps, seed=zlib.crc32((slug + what).encode()) % 100000)
    print(f"concept {slug}: {w}x{h} in {time.time() - t:.0f}s", flush=True)
    return out


def icon(mods, mod, slug, accent, redo):
    out = mods / mod / "icons" / f"{slug}.png"
    if out.exists() and not redo and out.stat().st_mtime > (ART / f"{slug}.png").stat().st_mtime: return out
    src = ART / f"{slug}.png"
    if not src.exists(): print(f"icon {slug}: no concept yet"); return None
    obj = cutout(Image.open(src))
    img = background('#1e2638', '#06070c')
    room = int(W * 0.74)
    k = room / max(obj.size)
    obj = obj.resize((max(1, int(obj.width * k)), max(1, int(obj.height * k))), Image.LANCZOS)
    halo = Image.new('RGBA', img.size, (0, 0, 0, 0))
    a = obj.split()[3].filter(ImageFilter.GaussianBlur(14))
    tint = Image.new('RGBA', obj.size, rgba(accent))
    tint.putalpha(a.point(lambda v: int(v * 0.55)))
    pos = ((W - obj.width) // 2, (W - obj.height) // 2)
    halo.alpha_composite(tint, pos)
    img.alpha_composite(halo)
    img.alpha_composite(obj, pos)
    frame(img, accent)
    out.parent.mkdir(parents=True, exist_ok=True)
    img.resize((S, S), Image.LANCZOS).save(out)
    print("wrote", out, flush=True)
    return out


HEAVY_SHADOW = {"shellback", "ventmaw"}


def trellis_input(slug):
    """TRELLIS takes the alpha as the object mask: the cutout, padded onto a transparent square."""
    obj = cutout(Image.open(ART / f"{slug}.png"))
    # The soft floor shadow under a creature survives the cutout (it's darker than the backdrop) and TRELLIS turns
    # it into a ground plate fused to the feet: clear light, colourless pixels from the alpha too.
    import numpy as np
    from PIL import ImageDraw
    a = np.array(obj).astype(np.int16)
    rgb, alpha = a[:, :, :3], a[:, :, 3]
    shadowish = (rgb.min(axis=2) > 70) & ((rgb.max(axis=2) - rgb.min(axis=2)) < 26)
    if slug in HEAVY_SHADOW:  # darker, slightly tinted shadows, below the body only (never a robot's grey plating)
        rows = np.arange(rgb.shape[0])[:, None] > rgb.shape[0] * 0.55
        shadowish |= rows & (rgb.max(axis=2) > 35) & ((rgb.max(axis=2) - rgb.min(axis=2)) < 40)
    # only grey that touches the outside counts (a shadow); grey highlights inside the body stay
    open_ = ((alpha < 30) | shadowish).astype(np.uint8) * 255
    pad = np.zeros((open_.shape[0] + 2, open_.shape[1] + 2), np.uint8)
    pad[1:-1, 1:-1] = open_
    pad[0, :] = pad[-1, :] = pad[:, 0] = pad[:, -1] = 255
    m = Image.fromarray(pad)
    ImageDraw.floodfill(m, (0, 0), 128)
    outside = np.array(m)[1:-1, 1:-1] == 128
    alpha[outside] = 0
    if slug in HEAVY_SHADOW:  # a shadow ringed by a darker edge stays an island: keep only the body's own piece
        from scipy import ndimage
        lab, n = ndimage.label(alpha > 30)
        if n > 1:
            sizes = ndimage.sum(np.ones_like(lab), lab, index=range(1, n + 1))
            alpha[(lab != 0) & (lab != 1 + int(np.argmax(sizes)))] = 0
    a[:, :, 3] = alpha
    obj = Image.fromarray(a.astype(np.uint8), "RGBA")
    box = obj.split()[3].point(lambda v: 255 if v > 24 else 0).getbbox()
    if box: obj = obj.crop(box)
    k = 420 / max(obj.size)
    obj = obj.resize((max(1, int(obj.width * k)), max(1, int(obj.height * k))), Image.LANCZOS)
    pad = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    pad.alpha_composite(obj, ((512 - obj.width) // 2, (512 - obj.height) // 2))
    out = ART / f"{slug}_mask.png"
    pad.save(out)
    return out


# Props too intricate for Stable Fast 3D's one-view guess (it flattened the ice spikes into a holed slab): TRELLIS.
TRELLIS_PROPS = {"ice_spikes", "bp_spike", "bp_bulkhead"}


def meshes(todo, redo):
    """Creatures through TRELLIS (real volume, separate limbs); props through Stable Fast 3D (seconds each, fine for
    simple shapes). Then the Blender repair of each."""
    MESH_DIR.mkdir(parents=True, exist_ok=True)
    ar = mia.shared()
    def wanted(slug, kind):
        raw = MESH_DIR / f"{slug}_raw.glb"
        if kind == "icon" or (raw.exists() and not redo): return False
        if not (ART / f"{slug}.png").exists(): print(f"mesh {slug}: no concept yet"); return False
        return True
    props = [(ART / f"{slug}.png", MESH_DIR / f"{slug}_raw.glb") for (_, slug, _, kind, _, _) in todo
             if kind == "prop" and slug not in TRELLIS_PROPS and wanted(slug, kind)]
    creatures = [slug for (_, slug, _, kind, _, _) in todo
                 if (kind == "creature" or slug in TRELLIS_PROPS) and wanted(slug, kind)]
    if props:
        t = time.time()
        for r in ar.make_fast_meshes(props, bake_res=1024):
            print("sf3d", r, flush=True)
        print(f"sf3d: {len(props)} prop(s) in {time.time() - t:.0f}s", flush=True)
    if creatures:
        os.environ["AAMT_TRELLIS_KEEP_SERVER"] = "1"
        os.environ.pop("NoDefaultCurrentDirectoryInExePath", None)  # TRELLIS's launcher bats "call" each other by bare name
        try:
            ar.ensure("mesh")
            for slug in creatures:
                t = time.time()
                try:
                    ar.make_mesh(trellis_input(slug), MESH_DIR / f"{slug}_raw.glb", resolution=512, mesh_simplify=10, texture_size=1024)
                    print(f"trellis {slug}: {time.time() - t:.0f}s", flush=True)
                except Exception as e:
                    print(f"trellis {slug} FAILED: {e}", flush=True)
        finally:
            from trellis_http_client import stop_server
            stop_server()
    for (_, slug, _, kind, _, _) in todo:
        raw, fixed = MESH_DIR / f"{slug}_raw.glb", MESH_DIR / f"{slug}.glb"
        if kind == "icon" or not raw.exists() or (fixed.exists() and not redo): continue
        try:
            r = ar.edit_mesh(raw, fixed, (FIX_PROP if kind == "prop" and slug not in TRELLIS_PROPS else FIX_CREATURE) + EXTRA_FIX.get(slug, []), preview=MESH_DIR / f"{slug}_fix.png")
            print(f"fixed {slug}: {r}", flush=True)
        except Exception as e:
            print(f"fix {slug} FAILED: {e}", flush=True)


def blender_exe():
    env = os.environ.get("AAMT_BLENDER")
    if env and Path(env).exists(): return env
    found = sorted(Path(r"D:\tools\Blender Foundation").glob("*/blender.exe"))
    if not found: raise RuntimeError("no Blender under D:\\tools\\Blender Foundation")
    return str(found[-1])


def rig(mods, mod, slug, kind, body, redo):
    src = MESH_DIR / f"{slug}.glb"
    out = mods / mod / "models" / f"{slug}.glb"
    if out.exists() and not redo and out.stat().st_mtime > src.stat().st_mtime if src.exists() else False: return out
    if not src.exists(): print(f"rig {slug}: no fixed mesh yet"); return None
    out.parent.mkdir(parents=True, exist_ok=True)
    cmd = [blender_exe(), "-b", "--factory-startup", "-P", str(BLENDER_SCRIPT), "--", str(src), str(out), body or "prop", str(MESH_DIR / f"{slug}_rig.png")]
    t = time.time()
    r = subprocess.run(cmd, capture_output=True, text=True, timeout=900)
    tail = "\n".join(l for l in (r.stdout + r.stderr).splitlines() if "RIG" in l or "Error" in l or "Traceback" in l or "error" in l.lower())[-3000:]
    print(f"rig {slug} ({body}): exit {r.returncode} in {time.time() - t:.0f}s\n{tail}", flush=True)
    if r.returncode == 0 and out.exists(): mia.slim(out)
    return out if out.exists() else None


# For the animation stage: the creature as the video prompts name it, its gait, and what its attack and wind-up look
# like ({it} is the description). Clips without an entry use anim_client's defaults.
ANIM = {
    "riot_bulwark": ("a bulky grey and blue riot robot", "biped", {
        "attack2": "{it} swings a wide backhand blow with its other arm, then recovers. Fast, energetic movement.",
        "attack": "{it} steps in and throws a heavy punch forward, then steps back. Fast, energetic movement.",
        "special": "{it} plants its feet and braces, arms raised, shaking with effort.",
        "hit": "{it} is knocked back by a heavy blow to the chest, staggers backwards a step with its arms flung out, then steadies itself. Fast, sudden movement."}),
    "fusebot": ("a small round orange hovering drone with claw arms", "hover", {
        "attack2": "{it} swings both claw arms forward in a wide slashing arc, then pulls them back. Fast, energetic movement.",
        "idle2": "{it} turns to look left, then right, its claw arms opening and closing, then settles.",
        "hit": "{it} is struck and knocked sideways, tumbling in the air with its claws flailing, then rights itself. Fast, sudden movement.",
        "attack": "{it} darts forward and snaps its claws shut, then pulls back. Fast, energetic movement.",
        "special": "{it} shudders and spins as its core flares brighter and brighter."}),
    "burstcap": ("a walking mushroom creature on two root legs", "biped", {
        "attack": "{it} rears its cap back high, then slams it down hard in front of it, then straightens. Fast, energetic movement.",
        "hit": "{it} is struck and rocks backwards, its cap wobbling and its legs staggering, then steadies itself. Fast, sudden movement.",
        "special": "{it} swells and shudders, its cap puffing up."}),
    "shellback": ("a purple-shelled alien crab", "crawler", {
        "attack2": "{it} pinches hard with one big claw, then pulls it back. Fast, energetic movement.",
        "attack": "{it} rears up and snaps both big claws forward, then settles back down. Fast, energetic movement."}),
    "skitterling": ("a small brown alien tick with eight legs", "crawler", {
        "attack": "{it} raises its front legs high, rears up and lunges forward, then drops back down. Fast, energetic movement."}),
    "mender_unit": ("a white hovering medical drone with thin arms", "hover", {
        "attack": "{it} tilts forward and jabs with its thin arms, then settles. Fast, energetic movement.",
        "special": "{it} rises up and spreads its thin arms wide as its lens glows, then lowers them again.",
        "attack2": "{it} swings both thin arms forward and jabs twice with the syringe tips, then pulls them back. Fast, energetic movement.",
        "death": "{it} sparks, its arms go limp and it tips over sideways and drops to the ground."}),
    "vaultback": ("a lean grey and red robotic hound", "quadruped", {
        "attack2": "{it} rears up on its hind legs and swipes down hard with both front paws, then drops back down. Fast, energetic movement.",
        "attack": "{it} leaps forward and bites, then lands and recovers. Fast, energetic movement."}),
    "ventmaw": ("a giant rusty armoured worm", "serpent", {
        "attack2": "{it} lifts its tail high and whips it around in a wide arc, then lowers it. Fast, energetic movement.",
        "death": "{it} thrashes wildly from side to side, rolls over onto its side and goes limp.",
        "attack": "{it} rears its head up and strikes down with its toothed mouth, then settles. Fast, energetic movement."}),
}


def animate(mods, mod, slug, body_plan, redo, only=None, anim_redo=None):
    """The AAMT animation stage for one creature. Returns the .glb, or None if it couldn't (the caller falls back)."""
    if slug not in ANIM: return None
    src = MESH_DIR / f"{slug}.glb"
    out = mods / mod / "models" / f"{slug}.glb"
    if not src.exists(): print(f"animate {slug}: no fixed mesh yet"); return None
    work = ART / "anim" / slug
    oriented = work / "oriented.glb"
    work.mkdir(parents=True, exist_ok=True)
    if redo or not oriented.exists() or oriented.stat().st_mtime < src.stat().st_mtime:
        plan = (body_plan or "prop") + ":norig"
        r = subprocess.run([blender_exe(), "-b", "--factory-startup", "-P", str(BLENDER_SCRIPT), "--", str(src), str(oriented), plan],
                           capture_output=True, text=True, timeout=900)
        print("\n".join(l for l in r.stdout.splitlines() if l.startswith("RIG:")), flush=True)
        if r.returncode or not oriented.exists(): print(f"animate {slug}: orienting failed"); return None
    ar = mia.shared()
    from anim_client import available
    if not available(): print("animate: the animation stage isn't installed (anim_client.py --status)"); return None
    desc, body, prompts = ANIM[slug]
    t = time.time()
    rep = ar.make_animated_mesh(oriented, out, desc, body=body, work=work, prompts=prompts, only=only,
                                redo=anim_redo or (["prep", "rig", "render", "video", "fit"] if redo else None))
    fails = [f for f in rep.get("fit", []) if not f.get("ok", True)] + [v for v in rep.get("video", []) if not v.get("ok", True)]
    print(f"animate {slug}: {time.time() - t:.0f}s, {len(fails)} failed step(s) {fails if fails else ''}", flush=True)
    if not out.exists(): return None
    mia.slim(out)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mods")
    ap.add_argument("--only", default="")
    ap.add_argument("--stage", default="all", choices=["concept", "icon", "mesh", "animate", "rig", "all"])
    ap.add_argument("--redo", action="store_true")
    ap.add_argument("--anim-only", default="")
    ap.add_argument("--anim-redo", default="")
    a = ap.parse_args()
    mods = Path(a.mods)
    only = {s.strip() for s in a.only.split(",") if s.strip()}
    todo = [x for x in ASSETS if not only or x[1] in only]
    if a.stage in ("concept", "all"):
        os.environ.setdefault("AAMT_SD_KEEP_SERVER", "1")
        os.environ.setdefault("SD_SKIP_T5", "1")
        for (_, slug, _, kind, _, what) in todo:
            try: concept(slug, kind, what, a.redo)
            except Exception as e: print(f"concept {slug} FAILED: {e}", flush=True)
    if a.stage in ("icon", "all"):
        for (mod, slug, accent, kind, _, _) in todo:
            if kind == "icon": icon(mods, mod, slug, accent, a.redo)
    if a.stage in ("mesh", "all"):
        try:
            from sd_server_lifecycle import stop_sd_server
            stop_sd_server()
        except Exception as e: print("stop sd:", e)
        meshes(todo, a.redo)
    animated = set()
    if a.stage in ("animate", "all"):
        split = lambda v: [x for x in v.split(",") if x] or None
        for (mod, slug, _, kind, body, _) in todo:
            if kind != "creature": continue
            try:
                if animate(mods, mod, slug, body, a.redo, split(a.anim_only), split(a.anim_redo)): animated.add(slug)
            except Exception as e: print(f"animate {slug} FAILED: {e}", flush=True)
    if a.stage in ("rig", "all"):
        for (mod, slug, _, kind, body, _) in todo:
            if kind in ("creature", "prop") and slug not in animated:
                try: rig(mods, mod, slug, kind, body, a.redo)
                except Exception as e: print(f"rig {slug} FAILED: {e}", flush=True)


if __name__ == "__main__":
    main()
