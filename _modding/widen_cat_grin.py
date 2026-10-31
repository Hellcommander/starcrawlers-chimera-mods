"""Looking-Glass grin, step two (make_kelvin_glass_assets.py makes the first concept). Widens the cat concept's grin past its cheeks (a sideways stretch of the mouth band, feathered in), then lets SD3.5
img2img redraw it so the seams and the extra teeth are painted, not pasted. Writes candidates and a sheet to
_modding/item_art/grin_widen/; the pick (w_55_5) was copied over item_art/grin.png before the mesh stage.
Usage: python widen_cat_grin.py  (needs the SD stage; T5 on for the long prompt)"""
import os, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
os.environ["SD_SKIP_T5"] = "0"; os.environ["SD_ALLOW_T5_RAM"] = "1"; os.environ["SD_MAX_SEQ_LEN"] = "256"
sys.path.insert(0, r"D:\games\Ai assisted toolkit\Tools\Shared")
import ai_resources as ar

HERE = Path(__file__).resolve().parent
SRC = HERE / "item_art" / "grin_widen" / "source_white.png"   # the first concept (background already white), kept before grin.png was replaced
OUT = HERE / "item_art" / "grin_widen"; OUT.mkdir(parents=True, exist_ok=True)

im = Image.open(SRC).convert("RGB").resize((512, 512), Image.LANCZOS)
a = np.asarray(im).astype(np.float32)

# 1. background to white: flood from the borders over the smooth purple gradient (compared with the local border colour)
bg = np.zeros(a.shape[:2], bool)
from collections import deque
q = deque([(0, x) for x in range(512)] + [(511, x) for x in range(512)] + [(y, 0) for y in range(512)] + [(y, 511) for y in range(512)])
for y, x in q: bg[y, x] = True
while q:
    y, x = q.popleft()
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        ny, nx = y + dy, x + dx
        if 0 <= ny < 512 and 0 <= nx < 512 and not bg[ny, nx] and np.abs(a[ny, nx] - a[y, x]).sum() < 18:
            bg[ny, nx] = True; q.append((ny, nx))
a[bg] = 255
base = Image.fromarray(a.astype(np.uint8))
base.save(OUT / "0_white.png")

# 2. stretch the mouth band sideways so the grin runs past both cheeks
box = (150, 300, 362, 410)                     # the grin in the 512 concept (x0, y0, x1, y1)
band = base.crop(box)
k = 2.0
wide = band.resize((int(band.width * k), band.height), Image.LANCZOS)
x0 = 256 - wide.width // 2
mask = Image.new("L", wide.size, 0)
ImageDraw.Draw(mask).ellipse((4, 6, wide.width - 4, wide.height - 6), fill=255)
mask = mask.filter(ImageFilter.GaussianBlur(9))
edit = base.copy()
edit.paste(wide, (x0, box[1]), mask)
edit.save(OUT / "1_stretched.png")

# 3. img2img: SD repaints it as one creature
prompt = ("floating head of an uncanny gaunt striped cat, its impossibly wide crescent grin stretches far past both sides of its face, "
          "a long row of needle-sharp teeth, unblinking glowing yellow eyes, dark violet and magenta striped fur, dreamlike and subtly wrong, "
          "isolated on plain white background, front view")
neg = "text, watermark, human, lips, body, cropped, frame, border, colored background, shadow"
paths = []
for strength in (0.4, 0.55):
    for seed in (5, 17):
        out = OUT / f"w_{int(strength * 100)}_{seed}.png"
        if not out.exists():
            ar.make_image(prompt, out, negative_prompt=neg, width=512, height=512, steps=30, seed=seed,
                          reference_image=str(OUT / "1_stretched.png"), image_strength=strength)
        paths.append(out)
W = 300
sheet = Image.new("RGB", (W * 3, W * 2), "white")
for k2, p in enumerate([OUT / "1_stretched.png"] + paths):
    t = Image.open(p).convert("RGB").resize((W, W)); ImageDraw.Draw(t).text((8, 8), str(k2), fill="black")
    sheet.paste(t, ((k2 % 3) * W, (k2 // 3) * W))
sheet.save(OUT / "sheet.png"); print("sheet", OUT / "sheet.png")
