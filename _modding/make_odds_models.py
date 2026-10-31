"""Procedural props for the Odds Breaker (original geometry, written as OBJ + PNG): a pair of loaded dice.
Usage: python make_odds_models.py <Mods folder>
Uses the mesh helpers from make_kinetics_models.py (same OBJ convention: Y up, counter-clockwise front faces)."""
import math, os, sys
from PIL import Image, ImageDraw
from make_kinetics_models import Mesh, rot_x, rot_z

# Pip counts per face cell of a 3x2 atlas: cell k holds face value k + 1.
CELL_W, CELL_H = 1 / 3, 1 / 2

def cell_uv(k):
    cx, cy = k % 3, k // 3
    u0, v0 = cx * CELL_W, 1 - (cy + 1) * CELL_H
    m = 0.01
    return [(u0 + m, v0 + m), (u0 + CELL_W - m, v0 + m), (u0 + CELL_W - m, v0 + CELL_H - m), (u0 + m, v0 + CELL_H - m)]

def die(m, s, place):
    """A cube of half-size s; opposite faces add up to seven. place(p) moves each corner into the scene."""
    P = lambda x, y, z: place((x * s, y * s, z * s))
    faces = [  # (corners counter-clockwise from outside, face value)
        ((P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1)), 1),     # +z
        ((P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1)), 6), # -z
        ((P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1)), 3),     # +x
        ((P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1)), 4), # -x
        ((P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1)), 5),     # +y (shows on top)
        ((P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1)), 2), # -y
    ]
    start = len(m.f)
    for corners, value in faces:
        m.quad(*corners, *cell_uv(value - 1))
    return start

def dice(out):
    m = Mesh(); s = 0.11
    # two dice, side by side, turned differently, resting on the deck (y = s)
    for (dx, dz, yaw, tip) in [(-0.15, 0.03, 18, 0), (0.14, -0.05, -31, 90)]:
        def place(p, dx=dx, dz=dz, yaw=yaw, tip=tip):
            p = rot_x(p, tip)
            r = math.radians(yaw); c, sn = math.cos(r), math.sin(r)
            p = (p[0] * c + p[2] * sn, p[1], -p[0] * sn + p[2] * c)
            return (p[0] + dx, p[1] + s, p[2] + dz)
        k = die(m, s, place)
        print('  die faces flipped outward:', m.orient(k))
    m.save(os.path.join(out, 'loaded_dice.obj'), 'loaded_dice')

    W, H = 768, 512
    img = Image.new('RGB', (W, H), (150, 14, 24))
    d = ImageDraw.Draw(img)
    pip_spots = {1: [(0, 0)], 2: [(-1, -1), (1, 1)], 3: [(-1, -1), (0, 0), (1, 1)], 4: [(-1, -1), (1, -1), (-1, 1), (1, 1)],
                 5: [(-1, -1), (1, -1), (0, 0), (-1, 1), (1, 1)], 6: [(-1, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (1, 1)]}
    cw, ch = W // 3, H // 2
    for k in range(6):
        x0, y0 = (k % 3) * cw, (k // 3) * ch
        cx, cy = x0 + cw // 2, y0 + ch // 2
        for i in range(40):  # translucent-casino look: a lighter core fading to a dark rim
            t = i / 40
            rr = int(cw * 0.5 * (1 - t))
            d.rectangle([cx - rr, cy - rr, cx + rr, cy + rr], fill=(int(120 + 90 * t), int(10 + 25 * t), int(20 + 25 * t)))
        d.rectangle([x0 + 4, y0 + 4, x0 + cw - 5, y0 + ch - 5], outline=(70, 4, 10), width=8)
        for (px, py) in pip_spots[k + 1]:
            r = 26 if k else 34
            x, y = cx + px * 64, cy + py * 64
            d.ellipse([x - r - 4, y - r - 4, x + r + 4, y + r + 4], fill=(90, 6, 14))  # drilled rim
            d.ellipse([x - r, y - r, x + r, y + r], fill=(245, 235, 210))
    img.save(os.path.join(out, 'loaded_dice.png'))
    print('wrote', os.path.join(out, 'loaded_dice.png'))

if __name__ == '__main__':
    mods = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '..', 'Mods')
    dice(os.path.join(mods, 'OddsBreaker', 'models'))
