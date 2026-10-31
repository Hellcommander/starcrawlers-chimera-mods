"""Original icon art for the Flux Savant mod (class, skills, effects and rune tiles), drawn from primitives.
Usage: python make_flux_icons.py <Mods folder>"""
import math, os, sys
from PIL import ImageDraw
from make_icons import rgba, blank, background, compose, frame, finish, P, bolt, reticle, chevrons, arrow_arc

CYAN, VIOLET, AMBER, ACID, FROST, EMBER = '#6FE3FF', '#B58CFF', '#FF9F43', '#9BE15D', '#9FD8FF', '#FF7A3D'

def hexagon(d, cx, cy, r, fill=None, outline=None, w=6):
    pts = [(P(cx + r * math.cos(math.radians(60 * i + 30))), P(cy + r * math.sin(math.radians(60 * i + 30)))) for i in range(6)]
    d.polygon(pts, fill=fill, outline=outline, width=int(w * 2) if outline else 0)

def ring_glyphs(d, cx, cy, r, col, n=8, w=4):
    d.ellipse([P(cx - r), P(cy - r), P(cx + r), P(cy + r)], outline=col, width=int(w * 2))
    for i in range(n):
        a = math.radians(360 / n * i)
        x, y = cx + r * math.cos(a), cy + r * math.sin(a)
        d.line([P(x - 0.02), P(y), P(x + 0.02), P(y)], fill=col, width=int(w * 2))
        d.line([P(x), P(y - 0.02), P(x), P(y + 0.02)], fill=col, width=int(w * 2))

def flask(d, cx, cy, s, glass, liquid):
    d.rectangle([P(cx - 0.05 * s), P(cy - 0.26 * s), P(cx + 0.05 * s), P(cy - 0.12 * s)], outline=glass, width=10)
    d.polygon([(P(cx - 0.05 * s), P(cy - 0.12 * s)), (P(cx + 0.05 * s), P(cy - 0.12 * s)), (P(cx + 0.20 * s), P(cy + 0.16 * s)), (P(cx - 0.20 * s), P(cy + 0.16 * s))], outline=glass, width=10)
    d.polygon([(P(cx - 0.12 * s), P(cy + 0.02 * s)), (P(cx + 0.12 * s), P(cy + 0.02 * s)), (P(cx + 0.18 * s), P(cy + 0.14 * s)), (P(cx - 0.18 * s), P(cy + 0.14 * s))], fill=liquid)

def zig(d, cx, cy, col, h=0.30, w=9):
    bolt(d, [(cx + 0.05, cy - h / 2), (cx - 0.04, cy), (cx + 0.04, cy), (cx - 0.05, cy + h / 2)], col, w)

# ---- class and skills --------------------------------------------------------------------------------------------

def class_icon(path):
    img = background('#1c2440', '#06070e')
    L = blank(); d = ImageDraw.Draw(L)
    ring_glyphs(d, 0.5, 0.5, 0.33, rgba(VIOLET, 230), n=10)
    flask(d, 0.5, 0.52, 1.0, rgba(CYAN), rgba(ACID, 220))
    compose(img, L, 10)
    Z = blank(); dz = ImageDraw.Draw(Z)
    zig(dz, 0.5, 0.36, rgba(AMBER), h=0.22, w=7)
    compose(img, Z, 8, 2.2)
    frame(img, VIOLET); finish(img, path)

def bench_instincts(path):
    img = background('#1a2030', '#05060a')
    L = blank(); d = ImageDraw.Draw(L)
    for cx in (0.36, 0.64):
        d.ellipse([P(cx - 0.12), P(0.38), P(cx + 0.12), P(0.62)], outline=rgba(CYAN), width=18)
        d.ellipse([P(cx - 0.05), P(0.45), P(cx + 0.05), P(0.55)], fill=rgba(VIOLET))
    d.line([P(0.48), P(0.5), P(0.52), P(0.5)], fill=rgba(CYAN), width=14)
    compose(img, L, 9)
    S = blank(); ds = ImageDraw.Draw(S)
    for (x, y) in [(0.22, 0.24), (0.78, 0.26), (0.30, 0.78), (0.72, 0.76)]:
        ds.line([P(x - 0.03), P(y), P(x + 0.03), P(y)], fill=rgba(AMBER), width=8)
        ds.line([P(x), P(y - 0.03), P(x), P(y + 0.03)], fill=rgba(AMBER), width=8)
    compose(img, S, 6, 2)
    frame(img, CYAN); finish(img, path)

def arc_lance(path):
    img = background('#14243a', '#04060c')
    L = blank(); d = ImageDraw.Draw(L)
    bolt(d, [(0.18, 0.80), (0.38, 0.58), (0.33, 0.52), (0.58, 0.36), (0.53, 0.30), (0.82, 0.18)], rgba(CYAN), 12)
    compose(img, L, 12, 2.2)
    R = blank(); dr = ImageDraw.Draw(R)
    hexagon(dr, 0.24, 0.76, 0.08, outline=rgba(VIOLET), w=5)
    compose(img, R, 6)
    frame(img, CYAN); finish(img, path)

def rune_press(path):
    img = background('#221a34', '#07050c')
    L = blank(); d = ImageDraw.Draw(L)
    d.rectangle([P(0.30), P(0.16), P(0.70), P(0.30)], fill=rgba('#5a5f78'))
    d.rectangle([P(0.46), P(0.30), P(0.54), P(0.46)], fill=rgba('#8a90aa'))
    hexagon(d, 0.5, 0.66, 0.16, fill=rgba('#2a2440'), outline=rgba(VIOLET), w=7)
    img.alpha_composite(L)
    G = blank(); dg = ImageDraw.Draw(G)
    dg.line([P(0.44), P(0.60), P(0.56), P(0.72)], fill=rgba(AMBER), width=12)
    dg.line([P(0.56), P(0.60), P(0.44), P(0.72)], fill=rgba(AMBER), width=12)
    compose(img, G, 9, 2.2)
    frame(img, VIOLET); finish(img, path)

def catalyst_flask(path):
    img = background('#1a2a18', '#050805')
    L = blank(); d = ImageDraw.Draw(L)
    flask(d, 0.5, 0.56, 1.25, rgba('#cfe8ff'), rgba(ACID, 230))
    compose(img, L, 10)
    B = blank(); db = ImageDraw.Draw(B)
    for (x, y, r) in [(0.36, 0.24, 0.035), (0.6, 0.18, 0.025), (0.68, 0.30, 0.03)]:
        db.ellipse([P(x - r), P(y - r), P(x + r), P(y + r)], outline=rgba(ACID), width=8)
    compose(img, B, 6, 2)
    frame(img, ACID); finish(img, path)

def lattice_field(path):
    img = background('#16263a', '#04070c')
    L = blank(); d = ImageDraw.Draw(L)
    for i in range(5):
        y = 0.30 + i * 0.1
        d.line([P(0.18), P(y), P(0.82), P(y)], fill=rgba(FROST, 200), width=6)
        x = 0.30 + i * 0.1
        d.line([P(x), P(0.22), P(x), P(0.78)], fill=rgba(FROST, 200), width=6)
    compose(img, L, 6)
    G = blank(); dg = ImageDraw.Draw(G)
    for a in range(0, 180, 30):
        r = math.radians(a)
        dg.line([P(0.5 - 0.16 * math.cos(r)), P(0.5 - 0.16 * math.sin(r)), P(0.5 + 0.16 * math.cos(r)), P(0.5 + 0.16 * math.sin(r))], fill=rgba('#ffffff'), width=10)
    compose(img, G, 10, 2.2)
    frame(img, FROST); finish(img, path)

def critical_mass(path):
    img = background('#3a1a10', '#0a0504')
    L = blank(); d = ImageDraw.Draw(L)
    for i, col in enumerate([EMBER, CYAN, FROST, ACID]):
        a = math.radians(90 * i + 45)
        x, y = 0.5 + 0.22 * math.cos(a), 0.5 + 0.22 * math.sin(a)
        d.ellipse([P(x - 0.07), P(y - 0.07), P(x + 0.07), P(y + 0.07)], fill=rgba(col))
        d.line([P(x), P(y), P(0.5), P(0.5)], fill=rgba(col, 200), width=10)
    compose(img, L, 10)
    C = blank(); dc = ImageDraw.Draw(C)
    dc.ellipse([P(0.40), P(0.40), P(0.60), P(0.60)], fill=rgba('#fff4d0'))
    compose(img, C, 18, 2.6)
    frame(img, AMBER); finish(img, path)

# ---- effects -----------------------------------------------------------------------------------------------------

def charge_fx(path):
    img = background('#10243a', '#04070c')
    L = blank(); d = ImageDraw.Draw(L)
    d.rectangle([P(0.32), P(0.22), P(0.68), P(0.80)], outline=rgba(CYAN), width=14)
    d.rectangle([P(0.44), P(0.14), P(0.56), P(0.22)], fill=rgba(CYAN))
    for i in range(3):
        y = 0.66 - i * 0.14
        d.rectangle([P(0.38), P(y), P(0.62), P(y + 0.09)], fill=rgba(CYAN, 220))
    compose(img, L, 9)
    frame(img, CYAN); finish(img, path)

def instability_fx(path):
    img = background('#3a2410', '#0a0604')
    L = blank(); d = ImageDraw.Draw(L)
    d.arc([P(0.2), P(0.25), P(0.8), P(0.85)], 180, 360, fill=rgba(AMBER), width=14)
    for i in range(5):
        a = math.radians(180 + 45 * i)
        d.line([P(0.5 + 0.26 * math.cos(a)), P(0.55 + 0.26 * math.sin(a)), P(0.5 + 0.30 * math.cos(a)), P(0.55 + 0.30 * math.sin(a))], fill=rgba(AMBER), width=10)
    d.line([P(0.5), P(0.55), P(0.72), P(0.34)], fill=rgba('#ff5040'), width=12)
    compose(img, L, 9, 2)
    frame(img, AMBER); finish(img, path)

def residue_fx(path):
    img = background('#22163a', '#06040c')
    L = blank(); d = ImageDraw.Draw(L)
    for i in range(4):
        r = 0.08 + i * 0.07
        d.arc([P(0.5 - r), P(0.5 - r), P(0.5 + r), P(0.5 + r)], 40 * i, 40 * i + 250, fill=rgba(VIOLET, 230 - i * 30), width=10)
    compose(img, L, 10, 2)
    frame(img, VIOLET); finish(img, path)

def galvanized_fx(path):
    img = background('#1a2a20', '#050806')
    L = blank(); d = ImageDraw.Draw(L)
    d.polygon([(P(0.30), P(0.24)), (P(0.70), P(0.24)), (P(0.70), P(0.56)), (P(0.50), P(0.80)), (P(0.30), P(0.56))], outline=rgba(ACID), width=12)
    zig(d, 0.5, 0.5, rgba(CYAN), h=0.32, w=8)
    compose(img, L, 9)
    frame(img, ACID); finish(img, path)

def brittle_fx(path):
    img = background('#16263a', '#04070c')
    L = blank(); d = ImageDraw.Draw(L)
    hexagon(d, 0.5, 0.5, 0.26, outline=rgba(FROST), w=7)
    bolt(d, [(0.40, 0.30), (0.52, 0.46), (0.44, 0.56), (0.58, 0.72)], rgba('#ffffff'), 6)
    compose(img, L, 9)
    frame(img, FROST); finish(img, path)

# ---- rune tiles ----------------------------------------------------------------------------------------------------

KIND = {'element': EMBER, 'shape': '#66BFFF', 'power': '#FFD15C', 'payload': VIOLET}

def rune(path, kind, glyph, tier=1, col=None):
    img = background('#1a1c26', '#050608')
    L = blank(); d = ImageDraw.Draw(L)
    hexagon(d, 0.5, 0.5, 0.36, fill=rgba('#2c2f3c'), outline=rgba('#5a6070'), w=6)
    if tier > 1:
        hexagon(d, 0.5, 0.5, 0.30, outline=rgba('#d8c070'), w=4)
    if tier > 2:
        hexagon(d, 0.5, 0.5, 0.25, outline=rgba('#ff9fe0'), w=4)
    img.alpha_composite(L)
    G = blank(); dg = ImageDraw.Draw(G)
    glyph(dg, rgba(col or KIND[kind]))
    compose(img, G, 9, 2.4)
    frame(img, col or KIND[kind]); finish(img, path)

def g_pyro(d, c):
    d.polygon([(P(0.5), P(0.28)), (P(0.62), P(0.52)), (P(0.56), P(0.70)), (P(0.44), P(0.70)), (P(0.38), P(0.52))], outline=c, width=12)
def g_volt(d, c): zig(d, 0.5, 0.5, c, h=0.40, w=11)
def g_cryo(d, c):
    for a in (0, 60, 120):
        r = math.radians(a)
        d.line([P(0.5 - 0.18 * math.cos(r)), P(0.5 - 0.18 * math.sin(r)), P(0.5 + 0.18 * math.cos(r)), P(0.5 + 0.18 * math.sin(r))], fill=c, width=12)
def g_caustic(d, c):
    d.polygon([(P(0.5), P(0.28)), (P(0.64), P(0.56)), (P(0.5), P(0.72)), (P(0.36), P(0.56))], outline=c, width=12)
    d.ellipse([P(0.46), P(0.52), P(0.54), P(0.60)], fill=c)
def g_aether(d, c):
    d.ellipse([P(0.32), P(0.40), P(0.68), P(0.60)], outline=c, width=11)
    d.ellipse([P(0.45), P(0.45), P(0.55), P(0.55)], fill=c)
def g_fork(d, c):
    d.line([P(0.5), P(0.74), P(0.5), P(0.50), P(0.34), P(0.28)], fill=c, width=12, joint='curve')
    d.line([P(0.5), P(0.50), P(0.66), P(0.28)], fill=c, width=12)
def g_bloom(d, c):
    d.ellipse([P(0.42), P(0.42), P(0.58), P(0.58)], fill=c)
    for i in range(8):
        a = math.radians(45 * i)
        d.line([P(0.5 + 0.12 * math.cos(a)), P(0.5 + 0.12 * math.sin(a)), P(0.5 + 0.22 * math.cos(a)), P(0.5 + 0.22 * math.sin(a))], fill=c, width=9)
def g_lens(d, c):
    reticle(d, 0.5, 0.5, 0.13, c, 6)
def g_overclock(d, c):
    d.arc([P(0.28), P(0.30), P(0.72), P(0.74)], 160, 380, fill=c, width=11)
    d.line([P(0.5), P(0.52), P(0.66), P(0.36)], fill=c, width=11)
def g_capacitor(d, c):
    d.line([P(0.44), P(0.30), P(0.44), P(0.70)], fill=c, width=12)
    d.line([P(0.56), P(0.30), P(0.56), P(0.70)], fill=c, width=12)
    d.line([P(0.28), P(0.5), P(0.44), P(0.5)], fill=c, width=9)
    d.line([P(0.56), P(0.5), P(0.72), P(0.5)], fill=c, width=9)
def g_coolant(d, c):
    for y in (0.40, 0.52, 0.64):
        d.arc([P(0.30), P(y - 0.06), P(0.50), P(y + 0.06)], 180, 360, fill=c, width=9)
        d.arc([P(0.50), P(y - 0.06), P(0.70), P(y + 0.06)], 0, 180, fill=c, width=9)
def g_siphon(d, c):
    for a in (0, 120, 240):
        r = math.radians(a)
        x, y = 0.5 + 0.22 * math.cos(r), 0.5 + 0.22 * math.sin(r)
        d.line([P(x), P(y), P(0.5 + 0.08 * math.cos(r)), P(0.5 + 0.08 * math.sin(r))], fill=c, width=10)
    d.ellipse([P(0.46), P(0.46), P(0.54), P(0.54)], fill=c)
def g_tether(d, c):
    d.ellipse([P(0.30), P(0.40), P(0.50), P(0.60)], outline=c, width=10)
    d.ellipse([P(0.50), P(0.40), P(0.70), P(0.60)], outline=c, width=10)
def g_mark(d, c):
    d.line([P(0.34), P(0.34), P(0.66), P(0.66)], fill=c, width=12)
    d.line([P(0.66), P(0.34), P(0.34), P(0.66)], fill=c, width=12)
    d.ellipse([P(0.30), P(0.30), P(0.70), P(0.70)], outline=c, width=8)

def g_singularity(d, c):
    for i in range(3):
        r = 0.07 + i * 0.06
        d.arc([P(0.5 - r), P(0.5 - r), P(0.5 + r), P(0.5 + r)], 90 * i, 90 * i + 220, fill=c, width=9)
    d.ellipse([P(0.47), P(0.47), P(0.53), P(0.53)], fill=c)
def g_prism(d, c):
    d.polygon([(P(0.5), P(0.28)), (P(0.70), P(0.66)), (P(0.30), P(0.66))], outline=c, width=11)
    for i, col in enumerate(['#FF7A3D', '#6FE3FF', '#9FD8FF', '#9BE15D']):
        d.line([P(0.62), P(0.50), P(0.76), P(0.42 + i * 0.05)], fill=rgba(col), width=6)
def g_cascade(d, c):
    for (x, y) in [(0.36, 0.36), (0.5, 0.5), (0.64, 0.64)]:
        d.ellipse([P(x - 0.06), P(y - 0.06), P(x + 0.06), P(y + 0.06)], outline=c, width=8)
    d.line([P(0.40), P(0.40), P(0.46), P(0.46)], fill=c, width=6)
    d.line([P(0.54), P(0.54), P(0.60), P(0.60)], fill=c, width=6)
def g_paradox(d, c):
    d.arc([P(0.26), P(0.40), P(0.50), P(0.60)], 0, 360, fill=c, width=10)
    d.arc([P(0.50), P(0.40), P(0.74), P(0.60)], 0, 360, fill=c, width=10)
    d.line([P(0.5), P(0.30), P(0.5), P(0.70)], fill=c, width=6)

RUNES = [
    ('rune_pyro', 'element', g_pyro, 1), ('rune_volt', 'element', g_volt, 1, CYAN), ('rune_cryo', 'element', g_cryo, 1, FROST),
    ('rune_caustic', 'element', g_caustic, 1, ACID), ('rune_aether', 'element', g_aether, 2, VIOLET),
    ('rune_fork', 'shape', g_fork, 1), ('rune_bloom', 'shape', g_bloom, 1), ('rune_lens', 'shape', g_lens, 1),
    ('rune_overclock', 'power', g_overclock, 2), ('rune_capacitor', 'power', g_capacitor, 1), ('rune_coolant', 'power', g_coolant, 1),
    ('rune_siphon', 'payload', g_siphon, 2), ('rune_tether', 'payload', g_tether, 1), ('rune_mark', 'payload', g_mark, 2),
    ('rune_singularity', 'shape', g_singularity, 3), ('rune_prism', 'element', g_prism, 3, '#FFFFFF'),
    ('rune_cascade', 'payload', g_cascade, 3), ('rune_paradox', 'power', g_paradox, 3),
]

if __name__ == '__main__':
    out = os.path.join(sys.argv[1], 'FluxSavant', 'icons')
    class_icon(os.path.join(out, 'flux_savant.png'))
    for f in (bench_instincts, arc_lance, rune_press, catalyst_flask, lattice_field, critical_mass,
              charge_fx, instability_fx, residue_fx, galvanized_fx, brittle_fx):
        f(os.path.join(out, f.__name__.replace('_fx', '') + '.png'))
    for name, kind, glyph, tier, *col in RUNES:
        rune(os.path.join(out, name + '.png'), kind, glyph, tier, col[0] if col else None)
