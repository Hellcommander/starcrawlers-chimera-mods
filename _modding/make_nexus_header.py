"""Nexus Mods page header (1300x372) through the AAMT toolkit's SD3.5 stage (D:\\games\\Ai assisted toolkit).
The art shows what the pack adds: a breach pad torn open in Chimera, crawlers using the new classes' powers and the
Interlopers coming through. SD3.5 degrades past 2:1, so each scene is drawn at 1536x640 and the 3.5:1 band is
cover-cropped out of it.
  concept - SD3.5 scenes into _modding/item_art/nexus/<scene>_<seed>.png
  header  - crop + grade one concept to _modding/nexus_header.png (and _modding/item_art/nexus/header_<pick>.png)
Usage: python make_nexus_header.py [--stage concept|header] [--only scene,scene] [--pick <scene>_<seed>] [--y 0.5]"""
import argparse, re, sys, time
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter, ImageFont

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from make_item_assets import shared

MODS = HERE.parent / "Mods"
ART = HERE / "item_art" / "nexus"
OUT = HERE / "nexus_header.png"
W, H = 1300, 372
GEN_W, GEN_H, STEPS = 1536, 640, 40
SEEDS = (1311, 4207)

STYLE = ("ultra wide cinematic panorama, sci-fi fantasy video game key art, detailed digital painting, dramatic "
         "volumetric lighting, dark moody palette with vivid glowing accents, horizontal composition, action spread "
         "across the full width")
NEGATIVE = ("text, letters, logo, title, watermark, signature, frame, border, ui, hud, blurry, lowres, deformed hands, "
            "extra limbs, cropped heads, bright daylight, white background")

# Every element is something the pack adds: breach pads (SideAreas), the classes' powers, the Interlopers.
SCENES = {
    "breach": ("inside a dark corporate megatower corridor of steel bulkheads and neon strip lights, a huge glowing "
               "purple dimensional breach rift torn open in the middle of the hall, four armoured adventurers seen "
               "from behind on the left facing it, one wreathed in molten orange metal, one swirling a rope of "
               "seawater, one surrounded by floating icy blue frost crystals, one casting crackling cyan lightning "
               "from a crossbow, on the right a riot robot with a shield, a purple shelled alien crab beast and a "
               "giant rusty armoured worm crawling out of the rift"),
    "lineup": ("a row of seven different sci-fi fantasy heroes standing side by side in a ruined corporate arcology "
               "hall, a glowing purple rift behind them, a knight of molten iron with a furnace chest, a water "
               "sorceress with a spiral of seawater, a frost seer with floating ice shards and starlight, a fungal "
               "summoner with glowing violet mushrooms and spores, an astronomer with tiny moons orbiting her hand, "
               "a tinkerer with a tesla coil crossbow, a psychic knight with a glowing blue sword"),
    "derelict": ("a squad of crawlers fighting inside a derelict starship cargo hold lit by a purple breach portal, "
                 "a heroine hurling a wave of seawater, a hulking brawler with a fist of molten metal, a mage "
                 "freezing an enemy in ice, glowing green spore clouds, cyan lightning arcs, swarming robot drones, "
                 "an alien crab beast with a purple shell, a lean red robotic attack hound, sparks and debris"),
}


def concept(only):
    ART.mkdir(parents=True, exist_ok=True)
    ar = shared()
    for scene, what in SCENES.items():
        if only and scene not in only: continue
        for seed in SEEDS:
            out = ART / f"{scene}_{seed}.png"
            if out.exists(): continue
            t = time.time()
            ar.make_image(f"{what}, {STYLE}", out, negative_prompt=NEGATIVE, width=GEN_W, height=GEN_H, steps=STEPS, seed=seed)
            print(f"concept {out.name}: {GEN_W}x{GEN_H} in {time.time() - t:.0f}s", flush=True)


def class_icons():
    """Each class's own emblem: the icon whose file is named after the class JSON."""
    found = []
    for js in sorted(MODS.glob("*/*.json")):
        if js.parent.name.startswith("Example"): continue
        for rel in re.findall(r'"icon":\s*"([^"]+)"', js.read_text(encoding="utf-8", errors="ignore")):
            p = js.parent / rel
            if p.stem == js.stem and p.exists():
                found.append(p)
                break
    return found


def font(size, weight):
    f = ImageFont.truetype(r"C:\Windows\Fonts\bahnschrift.ttf", size)
    f.set_variation_by_name(weight)
    return f


def overlay(img):
    """Title, a one-line list of what the pack adds and a strip of real class emblems from Mods/."""
    img = img.convert("RGBA")
    d = ImageDraw.Draw(img)
    x, y = 34, 196
    big, small = font(54, "Bold"), font(23, "SemiBold")
    glow = Image.new("RGBA", img.size)
    gd = ImageDraw.Draw(glow)
    gd.text((x, y), "BREACHWORK", font=big, fill=(170, 90, 255, 255))
    gd.text((x, y + 60), "MOD PACK AND FRAMEWORK FOR STARCRAWLERS CHIMERA", font=small, fill=(170, 90, 255, 255))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(9)))
    d.text((x, y), "BREACHWORK", font=big, fill=(244, 240, 255, 255))
    d.text((x + 2, y + 60), "MOD PACK AND FRAMEWORK FOR STARCRAWLERS CHIMERA", font=small, fill=(205, 175, 255, 255))
    d.text((x + 2, y + 92), "new classes  \u00b7  breaches  \u00b7  factions  \u00b7  new enemies  \u00b7  crew  \u00b7  skill synergies",
           font=font(19, "Regular"), fill=(215, 222, 235, 255))
    icons = class_icons()
    n, size, gap = 13, 38, 6
    pick = [icons[round(i * (len(icons) - 1) / (n - 1))] for i in range(n)]
    x0, y0 = W - 34 - n * size - (n - 1) * gap, H - 22 - size
    for i, p in enumerate(pick):
        ic = Image.open(p).convert("RGBA").resize((size, size), Image.LANCZOS)
        m = Image.new("L", (size, size))
        ImageDraw.Draw(m).rounded_rectangle((0, 0, size - 1, size - 1), 7, fill=255)
        ic.putalpha(ImageChops.multiply(ic.getchannel("A"), m))
        px = x0 + i * (size + gap)
        d.rounded_rectangle((px - 2, y0 - 2, px + size + 1, y0 + size + 1), 8, fill=(8, 6, 16, 200), outline=(150, 110, 230, 255), width=1)
        img.alpha_composite(ic, (px, y0))
    return img.convert("RGB")


def header(pick, y, title):
    src = Image.open(ART / f"{pick}.png").convert("RGB")
    band = round(src.width * H / W)
    top = round((src.height - band) * y)
    img = src.crop((0, top, src.width, top + band)).resize((W, H), Image.LANCZOS)
    img = ImageEnhance.Contrast(img).enhance(1.08)
    img = img.filter(ImageFilter.UnsharpMask(radius=1.2, percent=60, threshold=2))
    # Darken the left and bottom edges a little: Nexus lays the mod title and buttons over that corner.
    shade = Image.new("L", (W, H))
    px = shade.load()
    for yy in range(H):
        for xx in range(W):
            v = max(0.0, 1 - xx / (W * 0.45)) * 0.35 + max(0.0, (yy - H * 0.55) / (H * 0.45)) * 0.45
            px[xx, yy] = int(255 * min(v, 0.6))
    img = Image.composite(Image.new("RGB", (W, H)), img, shade)
    img.save(ART / f"art_{pick}.png")
    if title: img = overlay(img)
    img.save(OUT)
    img.save(ART / f"header_{pick}.png")
    print(f"header {OUT} from {pick} (y={y})")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", default="concept", choices=["concept", "header"])
    ap.add_argument("--only", default="")
    ap.add_argument("--pick", default="breach_1311")
    ap.add_argument("--y", type=float, default=0.85, help="where the crop band sits, 0 = top, 1 = bottom")
    ap.add_argument("--no-title", action="store_true", help="art only, without the title and class emblem strip")
    a = ap.parse_args()
    if a.stage == "concept": concept({s for s in a.only.split(",") if s})
    else: header(a.pick, a.y, not a.no_title)
