"""Every asset for the Kelvin Magus (Mods/KelvinMagus) and the Looking-Glass Rogue (Mods/LookingGlass), through each
AAMT toolkit stage (D:\\games\\Ai assisted toolkit):
  icons     - SD3.5 painted emblems (make_class_icons.py style), full bleed          -> <mod>/icons/<name>.png
  particles - SD3.5 key + Wan2.2 clip cut into a flipbook/sprite sheet               -> <mod>/fx/<name>.png
  mesh      - SD3.5 concept on white, then TRELLIS (creatures) / Stable Fast 3D (props) and mesh_edit's repair
              (make_interloper_assets.py's stages)                                   -> _modding/item_art/mesh/<slug>.glb
  props     - the repaired prop meshes, slimmed, for the fields that stand them on tiles -> <mod>/models/<slug>.glb
  audio     - Stable Audio 3 with library references (unique, shippable)             -> <mod>/sfx/<name>.wav
  animate   - UniRig + Wan2.2 + CoTracker3 skinned clips for the summons (hours each) -> <mod>/models/<slug>.glb
Every stage skips work already on disk, so a stopped run picks up where it left off.
Usage: python make_kelvin_glass_assets.py [--stage icons,particles,mesh,props,audio,animate|all] [--only name,name] [--redo]"""
import argparse, os, shutil, sys, time, zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import make_class_icons as mci
import make_interloper_assets as mi
import make_item_assets as mia

MODS = HERE.parent / "Mods"
KM, LG = "KelvinMagus", "LookingGlass"
ICE, WONDER = "icy blue and white", "magenta, teal and gold"

# (mod, icon name, colour words, what to draw)
ICONS = [
    (KM, "kelvin_magus", ICE, "a mage's open hand drawing glowing orange heat out of the air into a hovering frost crystal"),
    (KM, "heat_pump", "icy blue and ember orange", "a spiral of red warmth flowing into a cold blue crystal heart"),
    (KM, "heat_siphon", "icy blue and ember orange", "a beam pulling orange heat out of a frosted silhouette into an open palm"),
    (KM, "permafrost", ICE, "a cracked metal deck plate frozen over with spreading frost ferns, seen from above"),
    (KM, "glacier_sentinel", ICE, "a towering golem made of glacier ice with a glowing cyan core in its chest"),
    (KM, "thermal_plume", "white steam and ember orange", "a jet of scalding white steam blasting out of a frosted vent"),
    (KM, "icicle_palisade", ICE, "a row of jagged ice spikes erupting from the floor"),
    (KM, "shatterpoint", ICE, "a frozen block cracking apart into flying ice shards"),
    (KM, "zero_point", "deep blue and white", "an absolute zero singularity, a dark blue sphere drawing frost and light into it"),
    (KM, "stolen_heat", "ember orange and ice blue", "a glowing orange ember trapped inside a cube of ice"),
    (KM, "overheated", "red hot orange", "a frost-covered machine core glowing red hot, heat haze"),
    (KM, "supercooled", ICE, "a sharp blue snowflake crystal wreathed in frost"),
    (KM, "frozen_solid", ICE, "a creature's silhouette locked inside a solid block of ice"),
    (KM, "coldsink", ICE, "a frosted glass flask of liquid cold, pale blue mist spilling over its lip"),
    (LG, "looking_glass_rogue", WONDER, "an ornate oval looking-glass mirror with a wide grinning crescent smile in it, playing cards swirling around"),
    (LG, "curiouser", WONDER, "a glowing golden key beside a tiny door, swirling stars"),
    (LG, "snicker_snack", "silver and magenta", "a slender sword slicing a crescent arc, sparkles flying off the edge"),
    (LG, "drink_me", WONDER, "a small glass bottle with a paper tag beside a frosted little cake, sparkles"),
    (LG, "painted_guard", "red and white", "a playing card soldier holding a spear, hearts suit"),
    (LG, "hedgehog_croquet", "green and gold", "a croquet mallet swinging at a hedgehog curled into a ball"),
    (LG, "mad_tea_party", WONDER, "a tilted top hat and steaming teacups on a tipping table"),
    (LG, "vanishing_grin", "violet and pink", "a wide floating grin fading into violet mist"),
    (LG, "grin_without_a_cat", "violet and pink", "a wide toothy grin floating alone in the air where a cat has vanished, faint stripes fading"),
    (LG, "grinning_cat", "violet and magenta", "an uncanny gaunt striped cat with an impossibly wide grin of needle-sharp teeth and unblinking glowing eyes, its body dissolving into swirling stars, dreamlike"),
    (LG, "grin_of_teeth", "magenta and white", "a gigantic grinning mouth of sharp white fangs snapping shut"),
    (LG, "warp_pounce", "violet and pink", "a striped cat leaping claws first out of a swirling space-warp portal"),
    (LG, "which_way", "teal and gold", "a crooked signpost with arrows pointing every way at once, swirling warp"),
    (LG, "all_mad_here", WONDER, "a spiral of madness, swirling eyes and question marks around a grin"),
    (LG, "reappear", "violet and pink", "a cat's grin appearing first out of thin air, sparkles"),
    (LG, "lingering_grin", "violet and pink", "a lone glowing grin with sharp teeth floating in darkness"),
    (LG, "tea_clock", "teal and gold", "a pocket watch stuck at six o'clock with a steaming teacup"),
    (LG, "wonder", WONDER, "a swirling question mark made of stars"),
    (LG, "shrunk", "teal and gold", "a tiny figure standing beside a giant glass bottle"),
    (LG, "grown", "magenta and gold", "a giant figure hunched under a low ceiling, head bumping it"),
    (LG, "painted_red", "crimson and white", "a white rose dripping with red paint"),
    (LG, "topsy_turvy", WONDER, "an upside-down hourglass spinning, sand flowing upward"),
    (LG, "grinning", "violet and pink", "a glowing grin floating in darkness"),
]

# make_particles recipes; white-tinted sheets take each particle's colour in the mod's particles.json
PARTICLES = [
    (KM, {"name": "km_ice_shards", "preset": "shards", "seed": 21,
          "desc": "a single jagged shard of clear pale blue ice, sharp crystal fragment"}),
    (KM, {"name": "km_frost_burst", "preset": "impact", "seed": 22,
          "desc": "a bright white frost burst, an explosion of ice crystals and frozen mist with radiating spikes"}),
    (KM, {"name": "km_steam", "preset": "smoke_puff", "seed": 23, "desc": "a billowing puff of white steam vapour"}),
    (LG, {"name": "lg_cards", "preset": "shards", "seed": 31,
          "desc": "a single playing card with red hearts, slightly bent, seen at an angle"}),
    (LG, {"name": "lg_whimsy", "preset": "magic_burst", "seed": 32,
          "desc": "a swirling burst of glitter and tiny stars spiralling outward, whimsical magic sparkle"}),
    (LG, {"name": "lg_warp", "preset": "magic_burst", "seed": 34,
          "desc": "a swirling violet and pink space warp vortex, a ring of folded light twisting into a bright centre"}),
    (LG, {"name": "lg_petals", "preset": "shards", "seed": 33, "desc": "a single red rose petal, curled"}),
]

# make_interloper_assets.ASSETS rows: (mod, slug, accent, kind, body plan, prompt)
MESHES = [
    (KM, "glacier_sentinel", "#9FE6FF", "creature", "upright",
     "bulky humanoid golem made of translucent pale blue glacier ice standing straight, arms held away from its sides, "
     "legs apart, frosted crystal plates on its shoulders, glowing cyan core in its chest, front view"),
    (LG, "painted_guard", "#FF5C7A", "creature", "upright",
     "playing card soldier standing straight, its body a thick white playing card with red heart suit markings, "
     "small round head in a red helmet, thin arms held away from its sides, one hand holding a short spear, legs apart, front view"),
    (KM, "ice_spikes", "#9FE6FF", "prop", None, "cluster of thick jagged spikes of snow-frosted white and pale blue ice jutting straight up out of the floor, rough opaque frozen surfaces, no glass, no pedestal, no base plate"),
    (LG, "grin", "#FF70D8", "prop", None, "floating head of an eerie gaunt striped cat, an impossibly wide crescent grin far wider than its own head, the mouth stretching out past both cheeks, far too many long needle-sharp teeth, unblinking glowing yellow eyes, dark violet and magenta striped fur, uncanny, dreamlike and subtly wrong, front view"),
    (LG, "teapot", "#5CE1C8", "prop", None, "whimsical porcelain teapot with a crooked spout, pastel teal with small red hearts painted on it"),
]

ANIM = {
    "glacier_sentinel": ("a bulky golem made of blue ice", "biped", {
        "attack": "{it} raises both fists and slams them down in front of it, then straightens. Fast, energetic movement.",
        "attack2": "{it} swings a heavy sideways punch with one arm, then recovers. Fast, energetic movement.",
        "special": "{it} plants its feet and spreads its arms as frost swirls around it."}),
    "painted_guard": ("a playing card soldier with a spear", "biped", {
        "attack": "{it} steps forward and thrusts its spear straight ahead, then steps back. Fast, energetic movement.",
        "attack2": "{it} swings its spear in a wide sideways sweep, then recovers. Fast, energetic movement.",
        "special": "{it} stands to attention and raises its spear high."}),
}

# (mod, file, prompt, seconds)
AUDIO = [
    (KM, "km_siphon", "icy magic drain, warmth sucked out of the air, crackling frost forming, inward whoosh", 1.6),
    (KM, "km_permafrost", "ground freezing over, ice spreading and crackling across a metal floor", 2.0),
    (KM, "km_sentinel", "massive ice golem forming, grinding glacier ice, deep crystalline rumble", 2.4),
    (KM, "km_plume", "scalding steam vent burst, pressurised hiss blast", 1.6),
    (KM, "km_thermal_shock", "ice cracking explosively from sudden heat, sharp glassy crack and a burst of hissing steam", 1.8),
    (KM, "km_palisade", "ice spikes bursting up out of the ground, sharp crystalline crunch", 1.4),
    (KM, "km_shatter", "frozen block shattering into ice shards, bright glassy smash", 1.4),
    (KM, "km_zero_point", "freezing wind howl collapsing inward into silence, cold magic implosion", 2.6),
    (KM, "km_overheat", "overheating machine warning, boiling hiss and hot metal ticking", 1.8),
    (LG, "lg_flurry", "quick whimsical sword swishes, two fast blade cuts with a sparkle", 1.2),
    (LG, "lg_shrink", "magical shrinking sound, rising slide whistle with glittering sparkle", 1.4),
    (LG, "lg_grow", "magical growing sound, deep descending stretching groan with sparkle", 1.6),
    (LG, "lg_guard", "shuffling a deck of playing cards, cards snapping crisply into place", 1.4),
    (LG, "lg_croquet", "wooden croquet mallet whack and a ball thudding away", 1.0),
    (LG, "lg_teaparty", "porcelain teacups clinking and rattling, tea pouring, whimsical", 2.2),
    (LG, "lg_grin", "magical vanishing shimmer fading away, soft mischievous purr", 1.6),
    (LG, "lg_claw", "fast feline claw swipe, three sharp scratching slashes", 1.0),
    (LG, "lg_bite", "huge monstrous jaws snapping shut, heavy crunching bite with a hiss", 1.4),
    (LG, "lg_warp", "space warp whoosh, reality folding with a deep phasing pop", 1.4),
    (LG, "lg_mad_here", "eerie swirling psychic whispers rising into a mad echoing laugh of chimes", 2.0),
    (LG, "lg_cat", "magical transformation shimmer ending in a deep rumbling purr", 2.4),
    (LG, "lg_watch", "pocket watch ticking very fast, clock spring winding back", 1.6),
]


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
    work = HERE / "item_art" / "particles_kg"  # its own work folder: make_particle_assets.py runs in item_art/particles
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
        # props, and creature meshes that are shown as they are (no animation entry): the Looking-Glass grin
        if not (kind == "prop" or (kind == "creature" and slug not in ANIM)) or not src.exists() or (out.exists() and not redo): continue
        out.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, out)
        mia.slim(out)
        print(f"prop {slug}: {out.stat().st_size // 1024} KB", flush=True)


def audio(todo, redo):
    ar = mia.shared()
    for mod, name, prompt, secs in todo:
        out = MODS / mod / "sfx" / f"{name}.wav"
        if out.exists() and not redo: continue
        out.parent.mkdir(parents=True, exist_ok=True)
        t = time.time()
        try:
            ar.make_audio(prompt, out, duration=secs, seed=zlib.crc32(name.encode()) % 100000)
            print(f"audio {name}: {time.time() - t:.0f}s", flush=True)
        except Exception as e: print(f"audio {name} FAILED: {e}", flush=True)


def animate(todo, redo):
    mi.ANIM.update(ANIM)
    for (mod, slug, _, kind, body, _) in todo:
        if kind != "creature": continue
        try: mi.animate(MODS, mod, slug, body, redo)
        except Exception as e: print(f"animate {slug} FAILED: {e}", flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", default="all")
    ap.add_argument("--only", default="")
    ap.add_argument("--redo", action="store_true")
    a = ap.parse_args()
    stages = ["icons", "particles", "mesh", "props", "audio", "animate"] if a.stage == "all" else a.stage.split(",")
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
    elif st == "audio": audio(pick(AUDIO, lambda r: r[1]), a.redo)
    elif st == "animate": animate(pick(MESHES, lambda r: r[1]), a.redo)


if __name__ == "__main__":
    main()
