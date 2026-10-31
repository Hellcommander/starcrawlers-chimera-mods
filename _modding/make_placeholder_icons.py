"""Placeholder art for tier icons a class file names but nobody has drawn yet.

Each missing tier icon is made from the class's own icon: hue-shifted per tier, a little zoomed and turned, with the
tier number in a badge so the row reads at a glance. Replace them with real art whenever; existing files are never
touched. Usage: python make_placeholder_icons.py [Mods dir]
"""
import colorsys, glob, json, os, sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROMAN = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX"]


def hue_shift(img, shift):
    r, g, b, a = img.split()
    px = []
    for rr, gg, bb in zip(r.getdata(), g.getdata(), b.getdata()):
        h, s, v = colorsys.rgb_to_hsv(rr / 255, gg / 255, bb / 255)
        nr, ng, nb = colorsys.hsv_to_rgb((h + shift) % 1.0, min(1.0, s * 1.1), v)
        px.append((int(nr * 255), int(ng * 255), int(nb * 255)))
    out = Image.new("RGB", img.size)
    out.putdata(px)
    out.putalpha(a)
    return out


def font(size):
    for name in ("arialbd.ttf", "DejaVuSans-Bold.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


def make(base, tier, path):
    img = base.convert("RGBA").resize((256, 256), Image.LANCZOS)
    img = hue_shift(img, 0.13 * (tier - 5))
    # zoom and turn a little so neighbouring tiers don't look identical
    z = 1.12 + 0.06 * (tier % 3)
    big = img.resize((int(256 * z), int(256 * z)), Image.LANCZOS).rotate(12 * (tier - 5), resample=Image.BICUBIC)
    o = (big.width - 256) // 2
    img = big.crop((o, o, o + 256, o + 256))
    d = ImageDraw.Draw(img)
    d.rectangle([3, 3, 252, 252], outline=(255, 209, 92, 200), width=5)
    label = ROMAN[tier] if tier < len(ROMAN) else str(tier + 1)
    f = font(46)
    w = d.textlength(label, font=f)
    badge = [256 - w - 34, 186, 248, 248]
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle(badge, 10, fill=(0, 0, 0, 200))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(2)))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle(badge, 10, outline=(255, 209, 92, 230), width=3)
    d.text((badge[0] + 13, 190), label, font=f, fill=(255, 235, 170, 255))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


def main(mods):
    made = 0
    for f in sorted(glob.glob(os.path.join(mods, "*", "*.json"))):
        try:
            data = json.load(open(f, encoding="utf-8"))
        except Exception:
            continue
        for cls in data if isinstance(data, list) else [data]:
            if not isinstance(cls, dict) or "tiers" not in cls or not cls.get("icon"):
                continue
            folder = os.path.dirname(f)
            base_path = os.path.join(folder, cls["icon"])
            if not os.path.exists(base_path):
                continue
            base = Image.open(base_path)
            for i, t in enumerate(cls["tiers"]):
                icon = (t.get("newAbility") or {}).get("icon")
                if not icon or icon.startswith("SKILLTREE_"):
                    continue
                path = os.path.join(folder, icon)
                if not os.path.exists(path):
                    make(base, i, path)
                    made += 1
                    print("made", os.path.relpath(path, mods))
    print(made, "placeholder icons")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Mods"))
