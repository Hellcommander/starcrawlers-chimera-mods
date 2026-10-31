"""Real item art for the mod items that still borrow a vanilla look (runes and glyphs showed up as the glass phial).
Each item gets, through the AAMT toolkit (D:\\games\\Ai assisted toolkit):
  concept  - Stable Diffusion 3.5 render of the object alone on white      -> _modding/item_art/<slug>.png
  icon     - the concept cut out onto the mods' icon backdrop and frame    -> <mod>/items/<slug>.png
  mesh     - TRELLIS image-to-3D of the concept, as an uncompressed .glb   -> <mod>/models/items/<slug>.glb
Every stage skips work that is already on disk, so a stopped batch picks up where it left off.
Usage: python make_item_assets.py <Mods folder> [--only slug,slug] [--stage concept|icon|mesh|wire|all] [--redo]"""
import argparse, os, sys, time, zlib
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageChops

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from make_icons import background, frame, rgba, W, S

AAMT = Path(r"D:\games\Ai assisted toolkit\Tools\Shared")
ART = HERE / "item_art"

STYLE = "single game item, centered, isolated on plain white background, three-quarter view, sci-fi, detailed, studio lighting"
NEGATIVE = "text, letters, watermark, hands, people, multiple objects, cropped, frame, border, busy background, shadow"

# (mod folder, item id, slug, accent colour, what it is)
ITEMS = [
    # Runeforger: dormant glyphs are dull steel tiles; keyed ones are lit; scripts fuse two sigils.
    ("ArendethClasses", "RfKineticGlyph", "rf_kinetic_glyph", "#B8C4D6", "thick square steel slab tilted in perspective, visible bevelled side edges, dull scratched force sigil on top, unlit, worn metal"),
    ("ArendethClasses", "RfThermicGlyph", "rf_thermic_glyph", "#FF7A3D", "thick square steel slab tilted in perspective, visible bevelled side edges, soot-blackened grey iron, flame sigil carved into the top, cold, matte, no glow, no embers"),
    ("ArendethClasses", "RfCryicGlyph", "rf_cryic_glyph", "#9FD8FF", "thick square steel slab tilted in perspective, visible bevelled side edges, pale frosted metal, snowflake sigil carved into the top, unlit, pure white background"),
    ("ArendethClasses", "RfVoltaicGlyph", "rf_voltaic_glyph", "#FFE066", "square steel tile with a dull scratched zigzag current sigil, unlit, copper inlay"),
    ("ArendethClasses", "RfGlyphCell", "rf_glyph_cell", "#6FE3FF", "small cylindrical power capacitor cell, brass contacts, glowing cyan core window"),
    ("ArendethClasses", "RhOilDrum", "rh_oil_drum", "#C08040", "small dented black steel oil drum with a leaking spout, oily drips, hazard stripes"),
    ("ArendethClasses", "RhEmberPot", "rh_ember_pot", "#FF7A3D", "squat clay-lined metal canister with glowing orange live coals visible through vents"),
    ("ArendethClasses", "RhCoolantKeg", "rh_coolant_keg", "#9FD8FF", "small frosted steel keg of reactor coolant, ice crystals on the rim, blue warning label"),
    ("ArendethClasses", "RhBrineJug", "rh_brine_jug", "#4AD0E0", "thick glass jug of cloudy salt brine with a cork stopper and salt crust"),
    ("ArendethClasses", "RhGritSack", "rh_grit_sack", "#C8A060", "tied canvas sack bulging with coarse grey blasting grit, grit spilling from the top"),
    ("ArendethClasses", "RfKeyedKinetic", "rf_keyed_kinetic", "#D8E4FF", "thick square steel slab tilted in perspective, visible bevelled side edges, brightly glowing white force sigil on top, energy crackling"),
    ("ArendethClasses", "RfKeyedThermic", "rf_keyed_thermic", "#FF7A3D", "thick square steel slab tilted in perspective, visible bevelled side edges, brightly glowing orange flame sigil on top, embers"),
    ("ArendethClasses", "RfKeyedCryic", "rf_keyed_cryic", "#9FD8FF", "thick square steel slab tilted in perspective, visible bevelled side edges, brightly glowing icy blue snowflake sigil on top, frost"),
    ("ArendethClasses", "RfKeyedVoltaic", "rf_keyed_voltaic", "#FFE066", "thick square steel slab tilted in perspective, visible bevelled side edges, glowing yellow lightning sigil on top, whole object in frame, small"),
    ("ArendethClasses", "RfScorchlineScript", "rf_scorchline_script", "#FF9F43", "thick riveted steel plate with two fused glowing sigils, orange flame and yellow lightning"),
    ("ArendethClasses", "RfStillfrostScript", "rf_stillfrost_script", "#BFE6FF", "thick riveted steel plate with two fused glowing sigils, blue snowflake and white force"),
    ("ArendethClasses", "RfOvervoltScript", "rf_overvolt_script", "#FFE066", "thick riveted steel plate wound with copper wire, glowing yellow lightning sigil"),
    ("ArendethClasses", "RfTripglyph", "rf_tripglyph", "#FF7A3D", "flat round steel pressure plate mine with a faint orange glowing sigil on top"),
    # Flux Savant runes: hexagonal capacitor plates, one glyph each.
    ("FluxSavant", "FsPyroRune", "fs_pyro_rune", "#FF7A3D", "hexagonal dark metal rune tile, glowing orange flame glyph engraved"),
    ("FluxSavant", "FsVoltRune", "fs_volt_rune", "#FFE066", "hexagonal dark metal rune tile, glowing yellow lightning glyph engraved"),
    ("FluxSavant", "FsCryoRune", "fs_cryo_rune", "#9FD8FF", "hexagonal frosted metal rune tile, glowing pale blue ice crystal glyph"),
    ("FluxSavant", "FsCausticRune", "fs_caustic_rune", "#9BE15D", "hexagonal corroded metal rune tile, glowing acid green dripping glyph"),
    ("FluxSavant", "FsAetherRune", "fs_aether_rune", "#B58CFF", "hexagonal translucent violet crystal rune tile, swirling aether glyph inside"),
    ("FluxSavant", "FsForkRune", "fs_fork_rune", "#6FE3FF", "hexagonal dark metal rune tile, glowing cyan forked branching glyph"),
    ("FluxSavant", "FsBloomRune", "fs_bloom_rune", "#FF9F43", "hexagonal dark metal rune tile, glowing amber starburst glyph"),
    ("FluxSavant", "FsLensRune", "fs_lens_rune", "#6FE3FF", "hexagonal metal rune tile with a round glass lens set in its center, cyan glow"),
    ("FluxSavant", "FsOverclockRune", "fs_overclock_rune", "#FF5050", "hexagonal metal rune tile with gears and a filed-off limiter, glowing red glyph"),
    ("FluxSavant", "FsCapacitorRune", "fs_capacitor_rune", "#6FE3FF", "hexagonal metal rune tile with stacked capacitor plates, cyan charge glyph"),
    ("FluxSavant", "FsCoolantRune", "fs_coolant_rune", "#9FD8FF", "hexagonal metal rune tile with coolant fins and frost, pale blue glyph"),
    ("FluxSavant", "FsSiphonRune", "fs_siphon_rune", "#B58CFF", "hexagonal dark metal rune tile, glowing violet spiral siphon glyph"),
    ("FluxSavant", "FsTetherRune", "fs_tether_rune", "#6FE3FF", "two hexagonal metal rune tiles linked by a glowing cyan chain"),
    ("FluxSavant", "FsMarkRune", "fs_mark_rune", "#FF5050", "hexagonal dark metal rune tile, glowing red crosshair target glyph"),
    ("FluxSavant", "FsSingularityRune", "fs_singularity_rune", "#B58CFF", "ornate hexagonal black rune tile, tiny glowing purple black hole at its center"),
    ("FluxSavant", "FsPrismRune", "fs_prism_rune", "#FFFFFF", "ornate hexagonal rune tile with a clear prism crystal casting rainbow light"),
    ("FluxSavant", "FsCascadeRune", "fs_cascade_rune", "#B58CFF", "ornate hexagonal rune tile, violet aether glyph spilling glowing droplets"),
    ("FluxSavant", "FsParadoxRune", "fs_paradox_rune", "#6FE3FF", "ornate hexagonal rune tile, glowing cyan infinity glyph, cracked hourglass"),
    # Kinetics rig hardpoints: bolt-on machine parts.
    ("Kinetics", "KnFlamerNozzle", "kn_flamer_nozzle", "#FF7A3D", "chunky industrial flamethrower nozzle head, flared steel muzzle, pilot light, coiled fuel hose, orange heat stains"),
    ("Kinetics", "KnArcCoil", "kn_arc_coil", "#FFE066", "copper capacitor coil wrapped around a steel striking plate, sparks"),
    ("Kinetics", "KnCryoJacket", "kn_cryo_jacket", "#9FD8FF", "steel cylinder jacket wrapped in frosted blue coolant tubes"),
    ("Kinetics", "KnPistonOverdrive", "kn_piston_overdrive", "#FF5050", "heavy hydraulic piston assembly, red warning stripes, pressure gauge"),
    ("Kinetics", "KnHeatSink", "kn_heat_sink", "#FF9F43", "finned aluminum heat sink radiator block, bolt mounts"),
    ("Kinetics", "KnMagnetClamp", "kn_magnet_clamp", "#6FE3FF", "large U-shaped horseshoe electromagnet, red painted body, silver pole tips, copper coil windings, mounting bracket"),
    ("Kinetics", "KnKineticRam", "kn_kinetic_ram", "#B8C4D6", "spring-loaded steel battering ram piston, heavy coil spring"),
    ("Kinetics", "KnShieldEmitter", "kn_shield_emitter", "#6FE3FF", "compact shield emitter device, round dish, glowing cyan energy dome"),
    ("Kinetics", "KnSpreadChoke", "kn_spread_choke", "#FF9F43", "heavy flared trumpet-shaped steel hammer striker head, wide face, vent slots, bolt flange, gunmetal"),
    # Faction contract goods.
    ("Factions", "DeadDropCase", "dead_drop_case", "#B8C4D6", "sealed black hard-shell briefcase with steel latches and a keypad"),
    ("Factions", "PrototypeCore", "prototype_core", "#6FE3FF", "sealed cylindrical research core canister, glowing cyan window, serial plate"),
    ("Factions", "ManifestCrate", "manifest_crate", "#FF9F43", "small orange courier cargo crate, intact seal, scratched label"),
    ("Factions", "BountyLedger", "bounty_ledger", "#FFE066", "battered brass-cornered rugged datapad ledger, leather strap"),
    ("Factions", "TestimonyDrive", "testimony_drive", "#9BE15D", "rugged portable data drive with green status light, lanyard"),
    ("Factions", "ColdWallet", "cold_wallet", "#B58CFF", "small armored hardware crypto key device, violet status light"),
]


def shared():
    if str(AAMT) not in sys.path: sys.path.insert(0, str(AAMT))
    os.environ.setdefault("AAMT_AUDIO_CANDIDATES", "3")   # the audio stage keeps the take the CLAP judge likes best
    import ai_resources
    return ai_resources


# ---- concept ------------------------------------------------------------------------------------------------

def concept(slug, what, redo):
    out = ART / f"{slug}.png"
    if out.exists() and not redo: return out
    ART.mkdir(parents=True, exist_ok=True)
    ar = shared()
    from sd_http_client import plan_sd_size
    w, h, steps = plan_sd_size(256, 256, kind="icon")
    t = time.time()
    ar.make_image(f"{what}, {STYLE}", out, negative_prompt=NEGATIVE, width=w, height=h, steps=steps, seed=zlib.crc32((slug + what).encode()) % 100000)
    print(f"concept {slug}: {w}x{h} in {time.time() - t:.0f}s")
    return out


# ---- icon ---------------------------------------------------------------------------------------------------

def cutout(img):
    """Alpha from the white backdrop: flood from the borders so white highlights inside the object stay."""
    rgb = img.convert("RGB")
    grey = rgb.convert("L")
    # Light and colourless (the backdrop and its soft drop shadow) and connected to the edge -> background.
    hi = ImageChops.lighter(ImageChops.lighter(*rgb.split()[:2]), rgb.split()[2])
    lo = ImageChops.darker(ImageChops.darker(*rgb.split()[:2]), rgb.split()[2])
    light = hi.point(lambda v: 255 if v > 185 else 0)
    grey_ish = ImageChops.subtract(hi, lo).point(lambda v: 255 if v < 28 else 0)
    near = ImageChops.multiply(light, grey_ish)
    mask = near.copy()
    wd, ht = mask.size
    for x, y in [(0, 0), (wd - 1, 0), (0, ht - 1), (wd - 1, ht - 1), (wd // 2, 0), (wd // 2, ht - 1), (0, ht // 2), (wd - 1, ht // 2)]:
        if mask.getpixel((x, y)) == 255: ImageDraw.floodfill(mask, (x, y), 128)
    bg = mask.point(lambda v: 255 if v == 128 else 0)
    alpha = ImageChops.invert(bg).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(1.2))
    out = rgb.convert("RGBA")
    out.putalpha(alpha)
    box = alpha.point(lambda v: 255 if v > 24 else 0).getbbox()
    return out.crop(box) if box else out


def icon(mod_dir, slug, accent, redo):
    out = mod_dir / "items" / f"{slug}.png"
    if out.exists() and not redo: return out
    src = ART / f"{slug}.png"
    if not src.exists(): print(f"icon {slug}: no concept yet"); return None
    obj = cutout(Image.open(src))
    img = background('#1e2638', '#06070c')
    # Fit inside the frame with a margin.
    room = int(W * 0.72)
    k = room / max(obj.size)
    obj = obj.resize((max(1, int(obj.width * k)), max(1, int(obj.height * k))), Image.LANCZOS)
    # A soft accent glow behind the object so it reads at hotbar size.
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
    print("wrote", out)
    return out


# ---- mesh ---------------------------------------------------------------------------------------------------

def mesh(mod_dir, slug, redo):
    out = mod_dir / "models" / "items" / f"{slug}.glb"
    if out.exists() and not redo: return out
    src = ART / f"{slug}.png"
    if not src.exists(): print(f"mesh {slug}: no concept yet"); return None
    out.parent.mkdir(parents=True, exist_ok=True)
    # TRELLIS takes an RGBA image's alpha as the object mask. Its own background removal kept the white frame
    # around objects that fill the shot (square tiles came back as boxes), so hand it the cutout, padded.
    obj = cutout(Image.open(src))
    k = 360 / max(obj.size)
    obj = obj.resize((max(1, int(obj.width * k)), max(1, int(obj.height * k))), Image.LANCZOS)
    pad = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    pad.alpha_composite(obj, ((512 - obj.width) // 2, (512 - obj.height) // 2))
    src = ART / f"{slug}_mask.png"
    pad.save(src)
    ar = shared()
    t = time.time()
    # Small props: a light mesh and a 512 texture keep the packages small.
    ar.make_mesh(src, out, resolution=512, mesh_simplify=10, texture_size=512)
    slim(out)
    print(f"mesh {slug}: {out.stat().st_size // 1024} KB in {time.time() - t:.0f}s")
    return out


def slim(glb):
    """Keeps only what the class loader reads: drops the metallic-roughness map and stores the colour map as JPEG."""
    import io, json, struct
    data = glb.read_bytes()
    jlen = struct.unpack_from("<I", data, 12)[0]
    g = json.loads(data[20:20 + jlen])
    bin_start = 20 + jlen + 8
    blob = data[bin_start:bin_start + struct.unpack_from("<I", data, 20 + jlen)[0]]
    if any(i.get("mimeType") == "image/jpeg" for i in g.get("images", [])): return
    for m in g.get("materials", []):
        m.get("pbrMetallicRoughness", {}).pop("metallicRoughnessTexture", None)
        m.pop("normalTexture", None); m.pop("occlusionTexture", None)
    used_tex = {m["pbrMetallicRoughness"]["baseColorTexture"]["index"] for m in g.get("materials", []) if "baseColorTexture" in m.get("pbrMetallicRoughness", {})}
    used_img = {g["textures"][t]["source"] for t in used_tex}
    # Rebuild the binary chunk: geometry views as they were, the used images re-encoded, the rest dropped.
    image_views = {img["bufferView"]: i for i, img in enumerate(g.get("images", []))}
    out, views = bytearray(), []
    remap = {}
    for vi, v in enumerate(g["bufferViews"]):
        if vi in image_views and image_views[vi] not in used_img: continue
        raw = blob[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]]
        if vi in image_views:
            im = Image.open(io.BytesIO(raw)).convert("RGB")
            buf = io.BytesIO(); im.save(buf, "JPEG", quality=88); raw = buf.getvalue()
            g["images"][image_views[vi]]["mimeType"] = "image/jpeg"
        while len(out) % 4: out.append(0)
        nv = dict(v); nv["byteOffset"] = len(out); nv["byteLength"] = len(raw)
        remap[vi] = len(views); views.append(nv); out += raw
    g["bufferViews"] = views
    for a in g["accessors"]:
        if "bufferView" in a: a["bufferView"] = remap[a["bufferView"]]
    imgs, img_map = [], {}
    for i, img in enumerate(g.get("images", [])):
        if i in used_img: img_map[i] = len(imgs); img["bufferView"] = remap[img["bufferView"]]; imgs.append(img)
    texs, tex_map = [], {}
    for t, tex in enumerate(g.get("textures", [])):
        if t in used_tex: tex_map[t] = len(texs); tex["source"] = img_map[tex["source"]]; texs.append(tex)
    g["images"], g["textures"] = imgs, texs
    for m in g.get("materials", []):
        bt = m.get("pbrMetallicRoughness", {}).get("baseColorTexture")
        if bt: bt["index"] = tex_map[bt["index"]]
    while len(out) % 4: out.append(0)
    g["buffers"] = [{"byteLength": len(out)}]
    js = json.dumps(g, separators=(",", ":")).encode()
    js += b" " * (-len(js) % 4)
    total = 12 + 8 + len(js) + 8 + len(out)
    glb.write_bytes(struct.pack("<III", 0x46546C67, 2, total) + struct.pack("<II", len(js), 0x4E4F534A) + js + struct.pack("<II", len(out), 0x004E4942) + bytes(out))


# ---- wire ---------------------------------------------------------------------------------------------------

# Longest side on the floor, in metres.
SIZES = {"rf_glyph_cell": 0.16, "rf_tripglyph": 0.36, "dead_drop_case": 0.45, "manifest_crate": 0.45, "prototype_core": 0.34,
         "bounty_ledger": 0.3, "testimony_drive": 0.2, "cold_wallet": 0.18}
GLOWING = ("rf_keyed_", "rf_scorchline", "rf_stillfrost", "rf_overvolt", "rf_tripglyph", "rf_glyph_cell", "fs_")


def wire(mod_dir, item_id, slug, accent):
    """Points the item's "icon" and "model" at the new files, editing the JSON text so its hand layout stays."""
    import json, re
    icon_rel, mesh_rel = f"items/{slug}.png", f"models/items/{slug}.glb"
    if not (mod_dir / icon_rel).exists() and not (mod_dir / mesh_rel).exists(): return
    for path in sorted(mod_dir.glob("*.json")):
        text = path.read_text(encoding="utf-8")
        m = re.search(r'"id":\s*"%s"' % re.escape(item_id), text)
        if not m: continue
        # The item object: from its opening brace to the matching close.
        start = text.rfind("{", 0, m.start())
        depth, end = 0, start
        for end in range(start, len(text)):
            depth += {"{": 1, "}": -1}.get(text[end], 0)
            if depth == 0: break
        obj = text[start:end + 1]
        new = obj
        if (mod_dir / icon_rel).exists():
            if re.search(r'"icon":\s*"[^"]*"', new): new = re.sub(r'"icon":\s*"[^"]*"', f'"icon": "{icon_rel}"', new, count=1)
            else: new = re.sub(r'("base":\s*"[^"]*",)', r'\1 "icon": "%s",' % icon_rel, new, count=1)
        if (mod_dir / mesh_rel).exists():
            spec = {"mesh": mesh_rel, "scale": SIZES.get(slug, 0.28)}
            if slug.startswith(GLOWING): spec.update(glow=accent, glowStrength=0.4)
            spec_text = json.dumps(spec)
            if re.search(r'"model":\s*\{[^}]*\}', new): new = re.sub(r'"model":\s*\{[^}]*\}', lambda _: f'"model": {spec_text}', new, count=1)
            else: new = re.sub(r'("icon":\s*"[^"]*",)', lambda g: f'{g.group(1)} "model": {spec_text},', new, count=1)
        if new != obj:
            path.write_text(text[:start] + new + text[end + 1:], encoding="utf-8")
            json.loads(path.read_text(encoding="utf-8"))  # still valid
            print(f"wired {item_id} in {path.name}")
        return
    print(f"wire {item_id}: not found under {mod_dir}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mods")
    ap.add_argument("--only", default="")
    ap.add_argument("--stage", default="all", choices=["concept", "icon", "mesh", "slim", "wire", "all"])
    ap.add_argument("--redo", action="store_true")
    a = ap.parse_args()
    mods = Path(a.mods)
    only = {s.strip() for s in a.only.split(",") if s.strip()}
    todo = [i for i in ITEMS if not only or i[2] in only]
    # Images first (SD), then meshes (TRELLIS): the two servers share one GPU, so each runs as one batch.
    if a.stage in ("concept", "all"):
        os.environ.setdefault("AAMT_SD_KEEP_SERVER", "1")
        # Prompts fit CLIP's 77 tokens; T5 in RAM only slows each image and pushes SD out of VRAM.
        os.environ.setdefault("SD_SKIP_T5", "1")
        for (_, _, slug, _, what) in todo:
            try: concept(slug, what, a.redo)
            except Exception as e: print(f"concept {slug} FAILED: {e}")
    if a.stage in ("icon", "all"):
        for (mod, _, slug, accent, _) in todo: icon(mods / mod, slug, accent, a.redo)
    if a.stage in ("mesh", "all"):
        ar = shared()
        from sd_server_lifecycle import stop_sd_server
        stop_sd_server()  # TRELLIS needs the whole card
        os.environ["AAMT_TRELLIS_KEEP_SERVER"] = "1"
        # TRELLIS's launcher batch files "call" each other by bare name; agent shells set this and break that.
        os.environ.pop("NoDefaultCurrentDirectoryInExePath", None)
        try:
            ar.ensure("mesh")
            for (mod, _, slug, _, _) in todo:
                try: mesh(mods / mod, slug, a.redo)
                except Exception as e: print(f"mesh {slug} FAILED: {e}")
        finally:
            from trellis_http_client import stop_server
            stop_server()
    if a.stage == "slim":
        for (mod, _, slug, _, _) in todo:
            f = mods / mod / "models" / "items" / f"{slug}.glb"
            if f.exists(): slim(f); print(f"slim {slug}: {f.stat().st_size // 1024} KB")
    if a.stage in ("wire", "all"):
        for (mod, item_id, slug, accent, _) in todo: wire(mods / mod, item_id, slug, accent)


if __name__ == "__main__":
    main()
