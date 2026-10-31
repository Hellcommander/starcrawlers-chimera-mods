"""Art kits for the generated side-area places (Mods/SideAreas "places"). Each kit piece goes through the AAMT toolkit
(D:\\games\\Ai assisted toolkit):
  concept - Stable Diffusion 3.5: props, doors and exits alone on white; floors, walls and ceilings as seamless textures
                                                                                  -> _modding/item_art/places/<place>/<piece>.png
  mesh    - Stable Fast 3D for props, doors and exits (nothing with limbs), one model load for the whole batch
  fix     - mesh_edit: weld, drop crumbs, cut to the piece's triangle budget, preview sheet to judge by eye
                                                                                  -> Mods/SideAreas/models/places/<place>/<piece>.glb
  texture - surfaces made to tile (seams blended across the wrap)                 -> Mods/SideAreas/models/places/<place>/<piece>.jpg
            (SF3D was tried on floor tiles and walls first: it puffs a flat plate into a bumpy cushion and blurs the
            pattern, so surfaces are textures on the game's own grid geometry instead)
  wire    - the place's "kit" in side_areas.json points at the pieces on disk (edits the JSON text, keeps its layout)
Every stage skips work already on disk, so a stopped batch picks up where it left off.
Usage: python make_place_assets.py [--place derelict,lab] [--only piece,piece] [--stage concept|mesh|fix|texture|wire|all] [--redo]"""
import argparse, json, re, sys, time, zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
GAME = HERE.parent
AAMT = Path(r"D:\games\Ai assisted toolkit\Tools\Shared")
ART = HERE / "item_art" / "places"
MOD = GAME / "Mods" / "SideAreas"
OUT = MOD / "models" / "places"

STYLE = "single game asset, centered, isolated on plain white background, detailed sci-fi game art, even studio lighting, sharp focus"
FLAT = "single game asset, centered, isolated on plain white background, straight-on orthographic front view, flat lighting, sharp focus"
NEGATIVE = "text, letters, watermark, logo, people, hands, multiple objects, cropped, frame, border, busy background, room, scene, perspective corridor"
TEXTURE = "seamless tileable game texture, flat orthographic {view} view, the pattern fills the whole image edge to edge, even diffuse lighting, no shadows, sharp detail"
TEX_NEGATIVE = "text, letters, watermark, logo, perspective, vanishing point, objects, white background, border, frame, vignette, blurry"
# a wall panel repeats along every wall, so anything shaped like a way through reads as a fake door
WALL_NEGATIVE = ", door, doorway, hatch, airlock, archway, opening, window, porthole, corridor, hallway, tunnel, depth, symmetrical centrepiece"
SURFACES = ("floor", "wall", "ceiling")

# Piece kinds: floor / wall / ceiling feed the kit lists; door, lintel and exit are single; prop:<id> are props.
# Triangle budgets keep a whole room cheap: a room repeats its tiles dozens of times.
BUDGET = {"door": 2500, "lintel": 1200, "exit": 6000, "prop": 6000}

PLACES = {
    "derelict": [
        ("floor_a", "floor", "worn steel cargo deck plating, diamond tread pattern, scuffs and rust streaks, dark gunmetal, square plate seams"),
        ("floor_b", "floor", "dark steel floor grating over pipes and cables, grime, square grid"),
        ("wall_a", "wall", "flat riveted grey hull plating on a spaceship wall, rectangular plates with recessed seams, one horizontal pipe, small vents, soot and scratches"),
        ("wall_b", "wall", "spaceship wall, ribbed dark metal with cable conduits and a cracked red warning lamp, dents"),
        ("ceiling", "ceiling", "spaceship ceiling panels, grey metal with recessed light strips and vent grilles"),
        ("door", "door", "single solid sliding spaceship bulkhead door, one thick grey steel slab filling the whole tall rectangle, riveted plates, yellow and black hazard stripes down both edges, dents, no window, no frame"),
        ("exit", "exit", "spaceship airlock hatch set in a thick frame, heavy door with rounded corners, locking wheel, amber warning lamps above, worn steel"),
        ("crate", "prop:crate", "small battered metal cargo crate with carry handles, chipped orange paint, three-quarter view"),
        ("console", "prop:console", "spaceship computer console, a squat steel cabinet with a sloped glowing blue screen and a keyboard ledge, chunky hard-surface game prop, clean bevelled edges, painted and worn metal, readable silhouette, front three-quarter view"),
        ("locker", "prop:locker", "tall narrow steel crew locker, dented door hanging ajar, three-quarter view"),
        ("cryopod", "prop:cryopod", "upright cryogenic sleep pod, frosted glass window, hoses and cables, three-quarter view"),
        ("debris", "prop:debris", "heap of broken metal debris, twisted hull panels and snapped pipes, three-quarter view"),
        ("reactor", "prop:reactor", "large cylindrical starship reactor core, glowing orange rings, thick pipes at the base, three-quarter view"),
    ],
    "outpost": [
        ("floor_a", "floor", "worn grey cast concrete floor, faint scuffed tyre marks, oil stains, fine cracks, muted colours"),
        ("floor_b", "floor", "olive painted steel floor plates, bolted corners, worn paint"),
        ("wall_a", "wall", "prefab military bunker wall, olive drab corrugated steel, bolted frame, stencilled numbers"),
        ("wall_b", "wall", "cast concrete bunker wall with steel trim strips and a cable tray"),
        ("ceiling", "ceiling", "corrugated steel ceiling, olive drab, caged lamps"),
        ("door", "door", "single heavy military blast door leaf, tall portrait rectangle, thick olive steel with rivets and a small wire glass window"),
        ("exit", "exit", "military landing bay hatch in a thick frame, heavy steel door, red and white stripes, stencilled warning panel, floodlight above"),
        ("crate", "prop:crate", "olive green military supply crate with stencilled markings and latches, three-quarter view"),
        ("locker", "prop:locker", "tall grey steel equipment locker with vents, three-quarter view"),
        ("bunk", "prop:bunk", "military two-tier bunk bed, square olive steel tube frame, two folded grey mattresses with blankets, a ladder at one end, chunky hard-surface game prop, clean bevelled edges, painted and worn metal, readable silhouette, front three-quarter view"),
        ("barricade", "prop:barricade", "portable steel barricade with sandbags piled against it, three-quarter view"),
        ("console", "prop:console", "rugged military radio terminal, an olive steel crate-shaped case with an amber screen, dials and a short antenna, chunky hard-surface game prop, clean bevelled edges, painted and worn metal, readable silhouette, front three-quarter view"),
        ("rack", "prop:rack", "military armory weapon rack, one sturdy olive steel cabinet frame with a solid back wall, side panels and a base shelf, four rifles standing upright inside it, chunky hard-surface game prop, clean bevelled edges, painted and worn metal, readable silhouette, front three-quarter view"),
    ],
    "lab": [
        ("floor_a", "floor", "clean white laboratory floor tiles, glossy, thin grey seams"),
        ("floor_b", "floor", "light grey speckled anti-static floor tiles with small drains"),
        ("wall_a", "wall", "clean white laboratory wall panels with grey trim strips and small status screens"),
        ("wall_b", "wall", "white laboratory wall panels with thin pale blue light strips, small flush access plates and grey trim"),
        ("ceiling", "ceiling", "white ceiling panels with square diffuser lights"),
        ("door", "door", "single white sliding laboratory door leaf, tall portrait rectangle, frosted glass window, green access light, biohazard sign"),
        ("exit", "exit", "decontamination chamber door in a thick white frame, round sealed hatch with a porthole, yellow warning stripes"),
        ("bench", "prop:bench", "laboratory workbench, a solid grey steel cabinet base with drawers and a thick black top, a microscope and two flasks on it, chunky hard-surface game prop, clean bevelled edges, painted and worn metal, readable silhouette, front three-quarter view"),
        ("console", "prop:console", "white medical computer terminal, a solid pedestal cabinet with a large angled glowing cyan screen, chunky hard-surface game prop, clean bevelled edges, painted and worn metal, readable silhouette, front three-quarter view"),
        ("tank", "prop:tank", "tall glass specimen tank filled with glowing green liquid, dark shape floating inside, steel caps, three-quarter view"),
        ("cabinet", "prop:cabinet", "tall pale blue steel medical cabinet with two glass doors and shelves of bottles, three-quarter view"),
        ("server", "prop:server", "tall black server cabinet, a solid box with a perforated front, rows of small blue lights, chunky hard-surface game prop, clean bevelled edges, painted and worn metal, readable silhouette, front three-quarter view"),
    ],
    "mine": [
        ("floor_a", "floor", "rough rocky cave ground, gravel and dust, brown stone"),
        ("floor_b", "floor", "cracked dark rock cave floor with scattered ore"),
        ("wall_a", "wall", "rough rock cave wall, brown stone with mineral streaks and wooden support beams"),
        ("wall_b", "wall", "rough dark rock cave wall with glowing blue crystal veins"),
        ("ceiling", "ceiling", "rough jagged dark rock cave ceiling"),
        ("door", "door", "single heavy steel mesh mine gate leaf, tall portrait rectangle, rusted frame, hazard stripes along the bottom"),
        ("exit", "exit", "mining shuttle hatch in a rugged steel frame, heavy door, yellow warning lamps, scratched paint, rock dust"),
        ("orecart", "prop:orecart", "rusty mining ore cart full of rocks, three-quarter view"),
        ("boulder", "prop:boulder", "large rough grey boulder, three-quarter view"),
        ("crystal", "prop:crystal", "cluster of glowing blue crystals growing from a rock, three-quarter view"),
        ("crate", "prop:crate", "battered wooden mining supply crate with steel corners, three-quarter view"),
        ("drill", "prop:drill", "large industrial mining drill rig on tracks, yellow paint, big drill bit, three-quarter view"),
    ],
}


def shared():
    if str(AAMT) not in sys.path: sys.path.insert(0, str(AAMT))
    import ai_resources
    return ai_resources


def pieces(places, only):
    for place in places:
        for slug, kind, what in PLACES[place]:
            if not only or slug in only: yield place, slug, kind, what


def concept(place, slug, kind, what, redo):
    out = ART / place / f"{slug}.png"
    if out.exists() and not redo: return out
    out.parent.mkdir(parents=True, exist_ok=True)
    ar = shared()
    if kind in SURFACES:
        prompt, neg = f"{what}, " + TEXTURE.format(view="front" if kind == "wall" else "top-down"), TEX_NEGATIVE + (WALL_NEGATIVE if kind == "wall" else "")
        w, h = (576, 1152) if kind == "wall" else (768, 768)  # a wall panel is one tile wide and two high
    else:
        prompt, neg = f"{what}, {FLAT if kind in ('door', 'exit', 'lintel') else STYLE}", NEGATIVE
        w, h = 768, 768
    t = time.time()
    for attempt in range(3):
        # the animation batch stops the SD server at the start of each of its video jobs; a request caught in that just retries
        try:
            ar.make_image(prompt, out, negative_prompt=neg, width=w, height=h, steps=28, seed=zlib.crc32((place + slug + what).encode()) % 100000)
            break
        except Exception as e:
            print(f"concept {place}/{slug}: attempt {attempt + 1} failed ({str(e)[:160]})", flush=True)
            if attempt == 2: return None
            time.sleep(20)
    print(f"concept {place}/{slug} in {time.time() - t:.0f}s", flush=True)
    return out


# Hard-surface props go through TRELLIS: Stable Fast 3D melted their flat faces into lumps and tilted them.
TRELLIS_PIECES = {("derelict", "console"), ("outpost", "console"), ("outpost", "bunk"), ("outpost", "rack"),
                  ("lab", "bench"), ("lab", "console"), ("lab", "server")}
# Pieces TRELLIS built facing away (the screen came out on the back): turned round in the fix stage.
TURN = {("derelict", "console"): 180, ("outpost", "console"): 180, ("outpost", "rack"): 180}


def raw_path(place, slug):
    return ART / place / (f"{slug}_trellis.glb" if (place, slug) in TRELLIS_PIECES else f"{slug}_sf3d.glb")


def meshes(todo, redo):
    pairs = [(ART / p / f"{s}.png", raw_path(p, s)) for p, s, k, w in todo
             if k not in SURFACES and (ART / p / f"{s}.png").exists() and (redo or not raw_path(p, s).exists())]
    hard = [(img, out) for img, out in pairs if out.stem.endswith("_trellis")]
    pairs = [x for x in pairs if x not in hard]
    if hard: trellis_meshes(hard)
    if not pairs: return
    ar = shared()
    t = time.time()
    res = ar.make_fast_meshes(pairs, bake_res=1024)
    for (img, out), r in zip(pairs, res):
        print(f"mesh {out.parent.name}/{out.stem}: {'ok' if r.get('ok') else 'FAILED ' + str(r.get('error'))}", flush=True)
    print(f"{len(pairs)} meshes in {time.time() - t:.0f}s", flush=True)


def trellis_meshes(pairs):
    """TRELLIS, one server for the batch; SD is stopped first so it gets the whole card. It takes the image's alpha as
    the object's mask, so it's handed the concept cut out of its white backdrop and padded."""
    import os
    from PIL import Image
    from make_item_assets import cutout
    ar = shared()
    from sd_server_lifecycle import stop_sd_server
    stop_sd_server()
    os.environ["AAMT_TRELLIS_KEEP_SERVER"] = "1"
    os.environ.pop("NoDefaultCurrentDirectoryInExePath", None)  # TRELLIS's launcher bats "call" each other by bare name
    try:
        ar.ensure("mesh")
        for img, out in pairs:
            t = time.time()
            obj = cutout(Image.open(img))
            k = 360 / max(obj.size)
            obj = obj.resize((max(1, int(obj.width * k)), max(1, int(obj.height * k))), Image.LANCZOS)
            pad = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
            pad.alpha_composite(obj, ((512 - obj.width) // 2, (512 - obj.height) // 2))
            mask = img.with_name(img.stem + "_mask.png")
            pad.save(mask)
            try:
                ar.make_mesh(mask, out, resolution=512, mesh_simplify=10, texture_size=1024)
                print(f"mesh {out.parent.name}/{out.stem}: ok in {time.time() - t:.0f}s", flush=True)
            except Exception as e:
                print(f"mesh {out.parent.name}/{out.stem}: FAILED {str(e)[:200]}", flush=True)
    finally:
        from trellis_http_client import stop_server
        stop_server()


def fix(place, slug, kind, redo):
    src, dst = raw_path(place, slug), OUT / place / f"{slug}.glb"
    if not src.exists(): print(f"fix {place}/{slug}: no mesh yet"); return None
    if dst.exists() and not redo: return dst
    dst.parent.mkdir(parents=True, exist_ok=True)
    ar = shared()
    budget = BUDGET[kind.split(":")[0]]
    preview = ART / place / f"{slug}_preview.png"
    # no voxel remesh: it rebakes the texture soft; SF3D props come out as one piece anyway
    ops = ["--merge", "--drop-small", "0.02", "--decimate", str(budget)]
    if (place, slug) not in TRELLIS_PIECES: ops += ["--match-back", "0.8"]  # SF3D guesses pale backs; TRELLIS doesn't
    if (place, slug) in TURN: ops += ["--rotate", f"y:{TURN[(place, slug)]}"]
    res = ar.edit_mesh(src, dst, ops, preview=preview)
    from make_item_assets import slim
    slim(dst)  # colour map as JPEG, unused maps dropped: about a quarter of the size
    print(f"fix {place}/{slug}: {res.get('tris', res.get('triangles', '?'))} tris, {dst.stat().st_size // 1024} KB; preview {preview}", flush=True)
    return dst


def texture(place, slug, kind, redo):
    """A surface texture that tiles: the image is wrapped by half its size, and the wrapped copy (whose seams now sit in
    the middle) is blended in along the original's edges, so opposite edges match."""
    from PIL import Image, ImageFilter
    src, dst = ART / place / f"{slug}.png", OUT / place / f"{slug}.jpg"
    if not src.exists(): print(f"texture {place}/{slug}: no concept yet"); return None
    if dst.exists() and not redo: return dst
    im = Image.open(src).convert("RGB")
    w, h = im.size
    shifted = Image.new("RGB", (w, h))
    shifted.paste(im.crop((w // 2, h // 2, w, h)), (0, 0)); shifted.paste(im.crop((0, h // 2, w // 2, h)), (w - w // 2, 0))
    shifted.paste(im.crop((w // 2, 0, w, h // 2)), (0, h - h // 2)); shifted.paste(im.crop((0, 0, w // 2, h // 2)), (w - w // 2, h - h // 2))
    # mask: 1 (the wrapped copy) near the edges, 0 (the original) in the middle
    band = max(8, min(w, h) // 6)
    mask = Image.new("L", (w, h), 0)
    px = mask.load()
    for y in range(h):
        for x in range(w):
            d = min(x, y, w - 1 - x, h - 1 - y)
            px[x, y] = 255 if d < band // 2 else max(0, 255 - (d - band // 2) * 255 // band)
    mask = mask.filter(ImageFilter.GaussianBlur(band // 4))
    out = Image.composite(shifted, im, mask)
    out = out.resize((512, 1024) if kind == "wall" else (512, 512), Image.LANCZOS)
    dst.parent.mkdir(parents=True, exist_ok=True)
    out.save(dst, quality=90)
    print(f"texture {place}/{slug}: {dst.stat().st_size // 1024} KB", flush=True)
    return dst


def wire(place):
    """Writes the place's kit model lists from the pieces on disk, as one-line JSON entries, keeping the rest of the file."""
    path = MOD / "side_areas.json"
    text = path.read_text(encoding="utf-8")
    m = re.search(r'\{ "id": "%s", "name"' % re.escape(place), text)
    if not m: print(f"wire {place}: place not found"); return
    k = text.find('"kit": {', m.end())
    if k < 0: print(f"wire {place}: no kit"); return
    rel = lambda s: f"models/places/{place}/{s}.glb"
    have = {s: k_ for s, k_, w in PLACES[place] if (OUT / place / f"{s}.{'jpg' if k_ in SURFACES else 'glb'}").exists()}
    lists = {kind: [s for s, k_ in have.items() if k_ == kind] for kind in SURFACES}
    adds = []
    for kind in SURFACES:
        if lists[kind]: adds.append(f'"{kind}": [{", ".join(json.dumps({"texture": f"models/places/{place}/{s}.jpg"}) for s in lists[kind])}]')
    for kind in ("door", "lintel", "exit"):
        s = next((s for s, k_ in have.items() if k_ == kind), None)
        if s: adds.append(f'"{kind}": {json.dumps({"mesh": rel(s)})}')
    # drop entries an earlier wire wrote, then put the new ones first in the kit
    body_start = k + len('"kit": {')
    end = text.find('"props"', body_start)
    head = text[body_start:end]
    head = re.sub(r'\s*"(floor|wall|ceiling)": \[[^\]]*\],', "", head)
    head = re.sub(r'\s*"(door|lintel|exit)": \{[^}]*\},', "", head)
    text = text[:body_start] + (" " + ", ".join(adds) + "," if adds else "") + head + text[end:]
    # props: a "model" on each prop the kit has a mesh for
    for s, k_ in have.items():
        if not k_.startswith("prop:"): continue
        pid = k_.split(":")[1]
        lo = text.find('"kit": {', m.end())
        hi = text.find('"templates"', lo)
        pm = re.search(r'\{ "id": "%s", "where".*?\}(?=,?[ \t]*\n)' % re.escape(pid), text[lo:hi])
        if not pm: continue
        a, b = lo + pm.start(), lo + pm.end()
        entry = re.sub(r', "model": \{[^}]*\}', "", text[a:b])
        entry = entry[:-1].rstrip() + f', "model": {json.dumps({"mesh": rel(s)})} }}'
        text = text[:a] + entry + text[b:]
    path.write_text(text, encoding="utf-8")
    json.loads(text)
    print(f"wire {place}: {len(have)} pieces", flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--place", default=",".join(PLACES))
    ap.add_argument("--only", default="")
    ap.add_argument("--stage", default="all", choices=["concept", "mesh", "fix", "texture", "wire", "all"])
    ap.add_argument("--redo", action="store_true")
    a = ap.parse_args()
    places = [p for p in a.place.split(",") if p]
    only = {s for s in a.only.split(",") if s}
    todo = list(pieces(places, only))
    if a.stage in ("concept", "all"):
        for p, s, k, w in todo: concept(p, s, k, w, a.redo)
    if a.stage in ("mesh", "all"): meshes(todo, a.redo)
    if a.stage in ("fix", "all"):
        for p, s, k, w in todo:
            if k not in SURFACES: fix(p, s, k, a.redo)
    if a.stage in ("texture", "all"):
        for p, s, k, w in todo:
            if k in SURFACES: texture(p, s, k, a.redo)
    if a.stage in ("wire", "all"):
        for p in places: wire(p)


if __name__ == "__main__":
    main()
