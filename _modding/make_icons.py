"""Generates the icon and UI art for the Conjoined and Swarm Conductor mods (original art, drawn from primitives)."""
import math, os, sys
from PIL import Image, ImageDraw, ImageFilter, ImageChops

S = 256          # icon size
SS = 2           # supersampling
W = S * SS

def rgba(h, a=255):
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)

def blank():
    return Image.new('RGBA', (W, W), (0, 0, 0, 0))

def background(inner, outer):
    img = Image.new('RGBA', (W, W), rgba(outer))
    d = ImageDraw.Draw(img)
    ci, co = rgba(inner), rgba(outer)
    steps = 60
    for i in range(steps):
        t = i / steps
        r = int(W * 0.75 * (1 - t))
        col = tuple(int(co[k] + (ci[k] - co[k]) * t) for k in range(3)) + (255,)
        d.ellipse([W / 2 - r, W / 2 - r, W / 2 + r, W / 2 + r], fill=col)
    # faint scanlines for the tech feel
    for y in range(0, W, 8 * SS):
        d.line([(0, y), (W, y)], fill=(255, 255, 255, 3), width=SS)
    return img

def glow(layer, radius, strength=1.6):
    g = layer.filter(ImageFilter.GaussianBlur(radius * SS))
    if strength != 1:
        r, gg, b, a = g.split()
        a = a.point(lambda v: min(255, int(v * strength)))
        g = Image.merge('RGBA', (r, gg, b, a))
    return g

def compose(base, layer, glow_r=10, strength=1.8):
    base.alpha_composite(glow(layer, glow_r, strength))
    base.alpha_composite(layer)

def frame(img, edge):
    d = ImageDraw.Draw(img)
    e = rgba(edge)
    t = 7 * SS
    d.rectangle([0, 0, W - 1, W - 1], outline=(10, 14, 20, 255), width=t)
    d.rectangle([t, t, W - 1 - t, W - 1 - t], outline=e, width=3 * SS)
    d.rectangle([t + 3 * SS, t + 3 * SS, W - 1 - t - 3 * SS, W - 1 - t - 3 * SS], outline=(0, 0, 0, 120), width=2 * SS)
    # corner notches
    c = 26 * SS
    for (x, y, sx, sy) in [(t, t, 1, 1), (W - 1 - t, t, -1, 1), (t, W - 1 - t, 1, -1), (W - 1 - t, W - 1 - t, -1, -1)]:
        d.line([(x, y + sy * c), (x, y), (x + sx * c, y)], fill=e, width=6 * SS)
    # vignette
    v = Image.new('L', (W, W), 0)
    vd = ImageDraw.Draw(v)
    for i in range(30):
        vd.rectangle([i * SS * 2, i * SS * 2, W - 1 - i * SS * 2, W - 1 - i * SS * 2], outline=int(90 * (1 - i / 30)), width=2 * SS)
    shade = Image.new('RGBA', (W, W), (0, 0, 0, 0))
    shade.putalpha(v)
    img.alpha_composite(shade)

def finish(img, path):
    out = img.resize((S, S), Image.LANCZOS)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    out.save(path)
    print('wrote', path)

def P(v):  # 0..1 coordinates to pixels
    return v * W

# ---- shapes ------------------------------------------------------------------------------------------------------

def figure(d, cx, cy, s, col, width=0):
    """A bust: head and shoulders, centred at (cx, cy) in 0..1 units, s = scale."""
    hr = 0.085 * s
    d.ellipse([P(cx - hr), P(cy - 0.20 * s - hr), P(cx + hr), P(cy - 0.20 * s + hr)], fill=col)
    d.pieslice([P(cx - 0.20 * s), P(cy - 0.08 * s), P(cx + 0.20 * s), P(cy + 0.30 * s)], 180, 360, fill=col)
    d.rectangle([P(cx - 0.20 * s), P(cy + 0.11 * s), P(cx + 0.20 * s), P(cy + 0.16 * s)], fill=col)

def arrow_arc(d, cx, cy, r, a0, a1, col, w):
    box = [P(cx - r), P(cy - r), P(cx + r), P(cy + r)]
    d.arc(box, a0, a1, fill=col, width=int(w * SS))
    a = math.radians(a1)
    hx, hy = cx + r * math.cos(a), cy + r * math.sin(a)
    tang = a + math.pi / 2
    L = 0.07
    pts = [(hx + L * math.cos(tang), hy + L * math.sin(tang)),
           (hx + 0.045 * math.cos(a), hy + 0.045 * math.sin(a)),
           (hx - 0.045 * math.cos(a), hy - 0.045 * math.sin(a))]
    d.polygon([(P(x), P(y)) for x, y in pts], fill=col)

def drone(d, cx, cy, s, col, eye):
    r = 0.09 * s
    for sx in (-1, 1):
        d.line([P(cx + sx * r * 0.8), P(cy - r * 0.5), P(cx + sx * r * 2.1), P(cy - r * 1.2)], fill=col, width=int(5 * SS * s))
        d.ellipse([P(cx + sx * r * 2.1 - r * 0.9), P(cy - r * 1.35), P(cx + sx * r * 2.1 + r * 0.9), P(cy - r * 1.05)], fill=col)
    d.ellipse([P(cx - r), P(cy - r), P(cx + r), P(cy + r)], fill=col)
    d.ellipse([P(cx - r * 0.45), P(cy - r * 0.45), P(cx + r * 0.45), P(cy + r * 0.45)], fill=eye)

def signal_arcs(d, cx, cy, col, n=3, start=0.08, step=0.07, a0=-60, a1=60, w=6):
    for i in range(n):
        r = start + i * step
        d.arc([P(cx - r), P(cy - r), P(cx + r), P(cy + r)], a0, a1, fill=col, width=int(w * SS))

def reticle(d, cx, cy, r, col, w=6):
    d.ellipse([P(cx - r), P(cy - r), P(cx + r), P(cy + r)], outline=col, width=int(w * SS))
    for ang in (0, 90, 180, 270):
        a = math.radians(ang)
        d.line([P(cx + math.cos(a) * r * 0.55), P(cy + math.sin(a) * r * 0.55), P(cx + math.cos(a) * r * 1.3), P(cy + math.sin(a) * r * 1.3)], fill=col, width=int(w * SS))
    d.ellipse([P(cx - 0.015), P(cy - 0.015), P(cx + 0.015), P(cy + 0.015)], fill=col)

def bolt(d, pts, col, w=7):
    d.line([(P(x), P(y)) for x, y in pts], fill=col, width=int(w * SS), joint='curve')

def chevrons(d, cx, cy, col, n=3, w=0.22, h=0.08, gap=0.09, t=9):
    for i in range(n):
        y = cy + (i - (n - 1) / 2) * gap
        d.line([P(cx - w / 2), P(y + h / 2), P(cx), P(y - h / 2), P(cx + w / 2), P(y + h / 2)], fill=col, width=int(t * SS), joint='curve')

# ---- Conjoined ---------------------------------------------------------------------------------------------------

FLESH, TEAL, BONE, BLOOD = '#e2687a', '#4fd6c6', '#efe3c8', '#c2304a'

def conj_class(path):
    img = background('#3a1420', '#0b0710')
    L = blank(); d = ImageDraw.Draw(L)
    figure(d, 0.40, 0.56, 1.25, rgba(FLESH, 235))
    figure(d, 0.60, 0.56, 1.25, rgba(TEAL, 220))
    compose(img, L, 14)
    S2 = blank(); d2 = ImageDraw.Draw(S2)
    d2.line([P(0.5), P(0.28), P(0.5), P(0.86)], fill=rgba(BONE), width=5 * SS)
    for y in (0.40, 0.52, 0.64, 0.76):
        d2.line([P(0.46), P(y), P(0.54), P(y)], fill=rgba(BONE), width=4 * SS)
    compose(img, S2, 6)
    frame(img, '#b7475c'); finish(img, path)

def shared_marrow(path):
    img = background('#2a1418', '#08070a')
    G = blank(); d = ImageDraw.Draw(G)
    figure(d, 0.32, 0.58, 0.9, rgba(FLESH, 70)); figure(d, 0.68, 0.58, 0.9, rgba(TEAL, 70))
    img.alpha_composite(G)
    L = blank(); d = ImageDraw.Draw(L)
    col = rgba(BONE)
    d.line([P(0.27), P(0.73), P(0.73), P(0.27)], fill=col, width=22 * SS)
    for (x, y) in [(0.25, 0.70), (0.30, 0.75), (0.70, 0.25), (0.75, 0.30)]:
        d.ellipse([P(x - 0.06), P(y - 0.06), P(x + 0.06), P(y + 0.06)], fill=col)
    d.line([P(0.33), P(0.67), P(0.67), P(0.33)], fill=rgba(BLOOD, 200), width=6 * SS)
    compose(img, L, 12, 1.4)
    frame(img, '#b7475c'); finish(img, path)

def trade_bodies(path):
    img = background('#13262a', '#06090c')
    L = blank(); d = ImageDraw.Draw(L)
    figure(d, 0.33, 0.60, 0.95, rgba(FLESH, 230))
    figure(d, 0.67, 0.60, 0.95, rgba(TEAL, 230))
    compose(img, L, 10)
    A = blank(); da = ImageDraw.Draw(A)
    arrow_arc(da, 0.5, 0.42, 0.25, 200, 335, rgba('#ffd15c'), 8)
    arrow_arc(da, 0.5, 0.52, 0.25, 20, 155, rgba('#ffd15c'), 8)
    compose(img, A, 8, 2.0)
    frame(img, '#3fa79c'); finish(img, path)

def converging_blow(path):
    img = background('#3a1a14', '#0a0606')
    L = blank(); d = ImageDraw.Draw(L)
    for ang in range(0, 360, 60):
        a = math.radians(ang + 30)
        x0, y0 = 0.5 + math.cos(a) * 0.40, 0.5 + math.sin(a) * 0.40
        x1, y1 = 0.5 + math.cos(a) * 0.14, 0.5 + math.sin(a) * 0.14
        d.line([P(x0), P(y0), P(x1), P(y1)], fill=rgba(FLESH if ang % 120 else TEAL), width=10 * SS)
        tang = a + math.pi / 2
        d.polygon([(P(x1 - math.cos(a) * 0.0), P(y1)), (P(x1 + math.cos(a) * 0.07 + math.cos(tang) * 0.04), P(y1 + math.sin(a) * 0.07 + math.sin(tang) * 0.04)),
                   (P(x1 + math.cos(a) * 0.07 - math.cos(tang) * 0.04), P(y1 + math.sin(a) * 0.07 - math.sin(tang) * 0.04))], fill=rgba(FLESH if ang % 120 else TEAL))
    compose(img, L, 9)
    B = blank(); db = ImageDraw.Draw(B)
    star = []
    for i in range(16):
        r = 0.13 if i % 2 == 0 else 0.05
        a = math.radians(i * 22.5)
        star.append((P(0.5 + math.cos(a) * r), P(0.5 + math.sin(a) * r)))
    db.polygon(star, fill=rgba('#fff2c0'))
    compose(img, B, 14, 2.4)
    frame(img, '#b7475c'); finish(img, path)

def knit_together(path):
    img = background('#13301c', '#060a07')
    L = blank(); d = ImageDraw.Draw(L)
    figure(d, 0.5, 0.60, 1.3, rgba('#7a8a7e', 230))
    img.alpha_composite(L)
    T = blank(); dt = ImageDraw.Draw(T)
    xs = [0.5 + 0.02 * (1 if i % 2 else -1) for i in range(9)]
    ys = [0.28 + i * 0.065 for i in range(9)]
    dt.line([(P(x), P(y)) for x, y in zip(xs, ys)], fill=rgba('#7cff9a'), width=4 * SS)
    for y in ys[::1]:
        dt.line([P(0.44), P(y), P(0.56), P(y + 0.02)], fill=rgba('#7cff9a'), width=5 * SS)
    compose(img, T, 8, 2.2)
    frame(img, '#4caf6a'); finish(img, path)

def one_pulse(path):
    img = background('#2a1430', '#08060c')
    G = blank(); d = ImageDraw.Draw(G)
    for i, x in enumerate((0.24, 0.5, 0.76)):
        figure(d, x, 0.42, 0.7, rgba([FLESH, BONE, TEAL][i], 110))
    img.alpha_composite(G)
    L = blank(); d = ImageDraw.Draw(L)
    pts = [(0.08, 0.68), (0.30, 0.68), (0.36, 0.56), (0.42, 0.84), (0.50, 0.46), (0.57, 0.76), (0.62, 0.68), (0.92, 0.68)]
    d.line([(P(x), P(y)) for x, y in pts], fill=rgba('#ff6b8b'), width=9 * SS, joint='curve')
    compose(img, L, 10, 2.2)
    frame(img, '#9a4fb7'); finish(img, path)

def spread_thin(path):
    img = background('#2a1010', '#080505')
    L = blank(); d = ImageDraw.Draw(L)
    for i, (x, a) in enumerate([(0.30, 70), (0.42, 110), (0.58, 110), (0.70, 70)]):
        figure(d, x, 0.58, 0.9, rgba(FLESH, a))
    figure(d, 0.5, 0.58, 0.95, rgba('#ff8f8f', 230))
    compose(img, L, 6, 1.2)
    D = blank(); dd = ImageDraw.Draw(D)
    dd.line([P(0.18), P(0.86), P(0.82), P(0.86)], fill=rgba('#ff3b3b'), width=6 * SS)
    dd.polygon([(P(0.12), P(0.86)), (P(0.2), P(0.82)), (P(0.2), P(0.90))], fill=rgba('#ff3b3b'))
    dd.polygon([(P(0.88), P(0.86)), (P(0.8), P(0.82)), (P(0.8), P(0.90))], fill=rgba('#ff3b3b'))
    compose(img, D, 6, 2)
    frame(img, '#a03030'); finish(img, path)

# ---- Swarm Conductor ---------------------------------------------------------------------------------------------

CYAN, ICE, GOLD, RED = '#4fe3ff', '#bff6ff', '#ffd15c', '#ff4a4a'

def swarm_class(path):
    img = background('#0f2a3a', '#05080d')
    L = blank(); d = ImageDraw.Draw(L)
    figure(d, 0.40, 0.66, 1.15, rgba('#2c5a6e', 255))
    signal_arcs(d, 0.40, 0.42, rgba(CYAN), n=3, start=0.12, step=0.08, a0=-55, a1=25, w=7)
    compose(img, L, 8)
    D = blank(); dd = ImageDraw.Draw(D)
    drone(dd, 0.76, 0.26, 0.85, rgba(ICE), rgba(CYAN))
    drone(dd, 0.80, 0.56, 0.7, rgba(ICE), rgba(CYAN))
    compose(img, D, 8)
    frame(img, '#2d9cc0'); finish(img, path)

def neural_uplink(path):
    img = background('#10283a', '#05080d')
    L = blank(); d = ImageDraw.Draw(L)
    figure(d, 0.36, 0.66, 1.2, rgba('#2c5a6e'))
    signal_arcs(d, 0.36, 0.40, rgba(CYAN), n=4, start=0.12, step=0.08, a0=-60, a1=40, w=8)
    compose(img, L, 10, 2)
    frame(img, '#2d9cc0'); finish(img, path)

def hijack_drone(path):
    img = background('#132a30', '#05080b')
    L = blank(); d = ImageDraw.Draw(L)
    drone(d, 0.5, 0.52, 1.6, rgba(ICE), rgba(GOLD))
    compose(img, L, 9)
    T = blank(); dt = ImageDraw.Draw(T)
    for i in range(6):
        y = 0.86 - i * 0.04
        dt.line([P(0.5), P(y), P(0.5), P(y - 0.02)], fill=rgba(CYAN), width=5 * SS)
    signal_arcs(dt, 0.5, 0.94, rgba(CYAN), n=2, start=0.07, step=0.06, a0=200, a1=340, w=6)
    compose(img, T, 8, 2.2)
    frame(img, '#2d9cc0'); finish(img, path)

def target_uplink(path):
    img = background('#2a1418', '#08060a')
    L = blank(); d = ImageDraw.Draw(L)
    reticle(d, 0.52, 0.54, 0.22, rgba(RED), 8)
    compose(img, L, 9, 2.0)
    S2 = blank(); ds = ImageDraw.Draw(S2)
    signal_arcs(ds, 0.18, 0.20, rgba(CYAN), n=3, start=0.08, step=0.07, a0=10, a1=80, w=6)
    compose(img, S2, 7, 2)
    frame(img, '#c04050'); finish(img, path)

def priority_override(path):
    img = background('#2e2410', '#090704')
    D = blank(); dd = ImageDraw.Draw(D)
    drone(dd, 0.5, 0.68, 1.1, rgba(ICE), rgba(CYAN))
    compose(img, D, 7)
    L = blank(); d = ImageDraw.Draw(L)
    chevrons(d, 0.5, 0.33, rgba(GOLD), n=3, w=0.34, h=0.11, gap=0.10, t=11)
    compose(img, L, 10, 2.2)
    frame(img, '#c0a040'); finish(img, path)

def recall_protocol(path):
    img = background('#10262e', '#05080b')
    L = blank(); d = ImageDraw.Draw(L)
    d.line([P(0.24), P(0.62), P(0.24), P(0.82), P(0.76), P(0.82), P(0.76), P(0.62)], fill=rgba('#6fa8b8'), width=9 * SS, joint='curve')
    drone(d, 0.5, 0.40, 1.0, rgba(ICE), rgba(CYAN))
    compose(img, L, 7)
    A = blank(); da = ImageDraw.Draw(A)
    da.line([P(0.5), P(0.52), P(0.5), P(0.70)], fill=rgba(GOLD), width=8 * SS)
    da.polygon([(P(0.42), P(0.66)), (P(0.58), P(0.66)), (P(0.5), P(0.76))], fill=rgba(GOLD))
    compose(img, A, 8, 2)
    frame(img, '#2d9cc0'); finish(img, path)

def swarm_overload(path):
    img = background('#0e2238', '#04060c')
    L = blank(); d = ImageDraw.Draw(L)
    for i in range(6):
        a = math.radians(i * 60 - 90)
        drone(d, 0.5 + math.cos(a) * 0.30, 0.52 + math.sin(a) * 0.30, 0.55, rgba(ICE), rgba(CYAN))
    compose(img, L, 6)
    B = blank(); db = ImageDraw.Draw(B)
    for i in range(6):
        a = math.radians(i * 60 - 90)
        x, y = 0.5 + math.cos(a) * 0.24, 0.52 + math.sin(a) * 0.24
        mx, my = (x + 0.5) / 2 + math.sin(a) * 0.04, (y + 0.52) / 2 - math.cos(a) * 0.04
        bolt(db, [(x, y), (mx, my), (0.5, 0.52)], rgba(CYAN), 6)
    db.ellipse([P(0.44), P(0.46), P(0.56), P(0.58)], fill=rgba('#ffffff'))
    compose(img, B, 12, 2.6)
    frame(img, '#2d9cc0'); finish(img, path)

def signal_fx(path):
    img = background('#10283a', '#05080d')
    L = blank(); d = ImageDraw.Draw(L)
    for i in range(4):
        x = 0.24 + i * 0.16
        h = 0.14 + i * 0.12
        d.rectangle([P(x), P(0.80 - h), P(x + 0.10), P(0.80)], fill=rgba(CYAN))
    compose(img, L, 9, 2)
    frame(img, '#2d9cc0'); finish(img, path)

def designated_fx(path):
    img = background('#2a1012', '#080506')
    L = blank(); d = ImageDraw.Draw(L)
    figure(d, 0.5, 0.62, 1.1, rgba('#5a3a3e'))
    img.alpha_composite(L)
    R = blank(); dr = ImageDraw.Draw(R)
    reticle(dr, 0.5, 0.45, 0.16, rgba(RED), 7)
    compose(img, R, 9, 2.2)
    frame(img, '#c04050'); finish(img, path)

def linked_fx(path):
    img = background('#10283a', '#05080d')
    L = blank(); d = ImageDraw.Draw(L)
    for cx, cy in [(0.40, 0.42), (0.60, 0.58)]:
        d.rounded_rectangle([P(cx - 0.17), P(cy - 0.09), P(cx + 0.17), P(cy + 0.09)], radius=P(0.08), outline=rgba(CYAN), width=11 * SS)
    compose(img, L, 9, 2)
    frame(img, '#2d9cc0'); finish(img, path)

def prioritized_fx(path):
    img = background('#2e2410', '#090704')
    L = blank(); d = ImageDraw.Draw(L)
    chevrons(d, 0.5, 0.5, rgba(GOLD), n=2, w=0.40, h=0.14, gap=0.16, t=13)
    compose(img, L, 10, 2.2)
    frame(img, '#c0a040'); finish(img, path)

def crowded_fx(path):
    img = background('#2a2010', '#080604')
    L = blank(); d = ImageDraw.Draw(L)
    for i, (x, y, a) in enumerate([(0.36, 0.60, 150), (0.64, 0.60, 150), (0.5, 0.66, 235)]):
        figure(d, x, y, 0.95, rgba(['#c9a64a', '#c9a64a', '#ffd98a'][i], a))
    compose(img, L, 6, 1.2)
    B = blank(); db = ImageDraw.Draw(B)
    for sx in (-1, 1):
        x = 0.5 + sx * 0.36
        db.line([P(x), P(0.30), P(x - sx * 0.08), P(0.38), P(x), P(0.46)], fill=rgba('#ff8a3b'), width=7 * SS, joint='curve')
    compose(img, B, 7, 2)
    frame(img, '#c0a040'); finish(img, path)

def taunted_fx(path):
    """A bust locked on by a reticle, with anger ticks: it only has eyes for one target."""
    img = background('#301610', '#090504')
    L = blank(); d = ImageDraw.Draw(L)
    figure(d, 0.5, 0.64, 1.15, rgba('#6a3a30'))
    img.alpha_composite(L)
    R = blank(); dr = ImageDraw.Draw(R)
    reticle(dr, 0.5, 0.42, 0.13, rgba('#ff9f5c'), 7)
    for sx in (-1, 1):
        x = 0.5 + sx * 0.30
        dr.line([P(x), P(0.20), P(x + sx * 0.06), P(0.28)], fill=rgba('#ff5c3b'), width=7 * SS)
        dr.line([P(x - sx * 0.04), P(0.27), P(x + sx * 0.05), P(0.33)], fill=rgba('#ff5c3b'), width=7 * SS)
    compose(img, R, 9, 2.2)
    frame(img, '#c06a40'); finish(img, path)

def silenced_fx(path):
    """Signal waves leaving a bust, struck through: no special attacks."""
    img = background('#1e1430', '#07050c')
    L = blank(); d = ImageDraw.Draw(L)
    figure(d, 0.36, 0.64, 1.1, rgba('#4a3a6a'))
    signal_arcs(d, 0.36, 0.44, rgba('#b58cff', 150), n=3, start=0.12, step=0.08, a0=-50, a1=50, w=7)
    compose(img, L, 7, 1.4)
    X = blank(); dx = ImageDraw.Draw(X)
    dx.line([P(0.26), P(0.76), P(0.78), P(0.20)], fill=rgba('#ff4a6a'), width=13 * SS)
    compose(img, X, 9, 2.2)
    frame(img, '#8a5cc0'); finish(img, path)

# ---- Reaction Architect ------------------------------------------------------------------------------------------

WIRE, SPARK, AMBER, SLATE = '#7fe0ff', '#e8fbff', '#ffcc5c', '#2c4656'

def node(d, x, y, r, col):
    d.ellipse([P(x - r), P(y - r), P(x + r), P(y + r)], fill=col)

def ra_class(path):
    img = background('#0f2630', '#04080b')
    L = blank(); d = ImageDraw.Draw(L)
    pts = [(0.24, 0.30), (0.50, 0.22), (0.76, 0.32), (0.30, 0.62), (0.70, 0.60), (0.50, 0.80)]
    for a, b in [(0, 1), (1, 2), (0, 3), (2, 4), (3, 5), (4, 5), (1, 3), (1, 4)]:
        d.line([P(pts[a][0]), P(pts[a][1]), P(pts[b][0]), P(pts[b][1])], fill=rgba(WIRE, 170), width=6 * SS)
    for x, y in pts: node(d, x, y, 0.045, rgba(SPARK))
    compose(img, L, 9, 1.8)
    B = blank(); db = ImageDraw.Draw(B)
    node(db, 0.50, 0.50, 0.08, rgba(AMBER))
    compose(img, B, 14, 2.4)
    frame(img, '#3fa7c0'); finish(img, path)

def standing_orders(path):
    img = background('#132a33', '#05090c')
    L = blank(); d = ImageDraw.Draw(L)
    for i, y in enumerate((0.30, 0.50, 0.70)):
        d.rounded_rectangle([P(0.22), P(y - 0.07), P(0.78), P(y + 0.07)], radius=P(0.04), outline=rgba(WIRE), width=8 * SS)
        node(d, 0.32, y, 0.03, rgba(AMBER if i < 2 else SLATE))
        d.line([P(0.42), P(y), P(0.68), P(y)], fill=rgba(WIRE, 150 if i < 2 else 60), width=5 * SS)
    compose(img, L, 8, 1.8)
    frame(img, '#3fa7c0'); finish(img, path)

def probe_bolt(path):
    img = background('#10262e', '#05080b')
    L = blank(); d = ImageDraw.Draw(L)
    reticle(d, 0.62, 0.40, 0.17, rgba(WIRE), 7)
    compose(img, L, 8, 2)
    B = blank(); db = ImageDraw.Draw(B)
    bolt(db, [(0.12, 0.88), (0.30, 0.70), (0.26, 0.62), (0.52, 0.48)], rgba(SPARK), 8)
    compose(img, B, 10, 2.4)
    frame(img, '#3fa7c0'); finish(img, path)

def tripwire_clause(path):
    img = background('#14262a', '#05080a')
    L = blank(); d = ImageDraw.Draw(L)
    for x in (0.18, 0.82):
        d.rectangle([P(x - 0.03), P(0.40), P(x + 0.03), P(0.84)], fill=rgba(SLATE))
    d.line([P(0.18), P(0.58), P(0.82), P(0.58)], fill=rgba(WIRE), width=6 * SS)
    compose(img, L, 6, 1.6)
    S2 = blank(); ds = ImageDraw.Draw(S2)
    star = []
    for i in range(12):
        r = 0.11 if i % 2 == 0 else 0.04
        a = math.radians(i * 30)
        star.append((P(0.50 + math.cos(a) * r), P(0.58 + math.sin(a) * r)))
    ds.polygon(star, fill=rgba(AMBER))
    compose(img, S2, 12, 2.6)
    frame(img, '#3fa7c0'); finish(img, path)

def interdict(path):
    img = background('#2a1c10', '#090604')
    L = blank(); d = ImageDraw.Draw(L)
    d.line([P(0.12), P(0.52), P(0.52), P(0.52)], fill=rgba('#ff8a5c'), width=12 * SS)
    d.polygon([(P(0.52), P(0.40)), (P(0.52), P(0.64)), (P(0.62), P(0.52))], fill=rgba('#ff8a5c'))
    compose(img, L, 7, 1.6)
    B = blank(); db = ImageDraw.Draw(B)
    db.rectangle([P(0.66), P(0.20), P(0.76), P(0.84)], fill=rgba(WIRE))
    for y in (0.40, 0.64):
        db.line([P(0.60), P(y - 0.04), P(0.68), P(y)], fill=rgba(SPARK), width=5 * SS)
    compose(img, B, 12, 2.4)
    frame(img, '#c08a40'); finish(img, path)

def retort_protocol(path):
    img = background('#10262e', '#05080b')
    L = blank(); d = ImageDraw.Draw(L)
    d.polygon([(P(0.30), P(0.26)), (P(0.70), P(0.26)), (P(0.70), P(0.56)), (P(0.50), P(0.80)), (P(0.30), P(0.56))], outline=rgba(WIRE), width=9 * SS)
    compose(img, L, 8, 1.8)
    A = blank(); da = ImageDraw.Draw(A)
    arrow_arc(da, 0.50, 0.50, 0.30, 200, 340, rgba(AMBER), 8)
    compose(img, A, 9, 2.2)
    frame(img, '#3fa7c0'); finish(img, path)

def cascade_failure(path):
    img = background('#0c2230', '#03060a')
    L = blank(); d = ImageDraw.Draw(L)
    for ang in range(0, 360, 45):
        a = math.radians(ang)
        x1, y1 = 0.5 + math.cos(a) * 0.20, 0.5 + math.sin(a) * 0.20
        x2, y2 = 0.5 + math.cos(a + 0.35) * 0.38, 0.5 + math.sin(a + 0.35) * 0.38
        bolt(d, [(0.5, 0.5), (x1, y1), (x2, y2)], rgba(WIRE), 6)
        node(d, x2, y2, 0.03, rgba(SPARK))
    compose(img, L, 10, 2.2)
    B = blank(); db = ImageDraw.Draw(B)
    node(db, 0.5, 0.5, 0.09, rgba(AMBER))
    compose(img, B, 16, 2.6)
    frame(img, '#3fa7c0'); finish(img, path)

def exit_clause(path):
    img = background('#14262a', '#05080a')
    L = blank(); d = ImageDraw.Draw(L)
    # a doorway with a wire strung across it
    d.rectangle([P(0.20), P(0.18), P(0.30), P(0.84)], fill=rgba(SLATE))
    d.rectangle([P(0.20), P(0.18), P(0.62), P(0.26)], fill=rgba(SLATE))
    d.line([P(0.30), P(0.56), P(0.62), P(0.50)], fill=rgba(WIRE), width=6 * SS)
    compose(img, L, 6, 1.6)
    # an arrow leaving through it, snagged where it meets the wire
    A = blank(); da = ImageDraw.Draw(A)
    da.line([P(0.44), P(0.62), P(0.80), P(0.62)], fill=rgba('#ff8a5c'), width=10 * SS)
    da.polygon([(P(0.80), P(0.52)), (P(0.80), P(0.72)), (P(0.90), P(0.62))], fill=rgba('#ff8a5c'))
    compose(img, A, 7, 1.6)
    B = blank(); db = ImageDraw.Draw(B)
    node(db, 0.50, 0.53, 0.05, rgba(SPARK))
    for ang in (-50, 20, 90, 160):
        a = math.radians(ang)
        db.line([P(0.50), P(0.53), P(0.50 + math.cos(a) * 0.11), P(0.53 + math.sin(a) * 0.11)], fill=rgba(AMBER), width=4 * SS)
    compose(img, B, 12, 2.4)
    frame(img, '#3fa7c0'); finish(img, path)

def foresight_fx(path):
    img = background('#10262e', '#05080b')
    L = blank(); d = ImageDraw.Draw(L)
    d.chord([P(0.14), P(0.30), P(0.86), P(0.90)], 200, 340, fill=rgba(WIRE, 90))
    d.chord([P(0.14), P(0.10), P(0.86), P(0.70)], 20, 160, fill=rgba(WIRE, 90))
    d.arc([P(0.14), P(0.30), P(0.86), P(0.90)], 200, 340, fill=rgba(WIRE), width=7 * SS)
    d.arc([P(0.14), P(0.10), P(0.86), P(0.70)], 20, 160, fill=rgba(WIRE), width=7 * SS)
    node(d, 0.5, 0.5, 0.10, rgba(AMBER))
    node(d, 0.5, 0.5, 0.04, rgba('#1a1a1a'))
    compose(img, L, 9, 2)
    frame(img, '#3fa7c0'); finish(img, path)

def profiled_fx(path):
    img = background('#2a1814', '#080505')
    L = blank(); d = ImageDraw.Draw(L)
    figure(d, 0.5, 0.62, 1.1, rgba('#6a4a40'))
    img.alpha_composite(L)
    B = blank(); db = ImageDraw.Draw(B)
    c, w = 0.12, 8 * SS
    for (x, y, sx, sy) in [(0.20, 0.20, 1, 1), (0.80, 0.20, -1, 1), (0.20, 0.82, 1, -1), (0.80, 0.82, -1, -1)]:
        db.line([P(x), P(y + sy * c), P(x), P(y), P(x + sx * c), P(y)], fill=rgba(WIRE), width=w)
    for y in (0.36, 0.48, 0.60):
        db.line([P(0.30), P(y), P(0.70), P(y)], fill=rgba(WIRE, 110), width=3 * SS)
    compose(img, B, 8, 2)
    frame(img, '#c06a40'); finish(img, path)

# ---- Factions (Outside Interests) --------------------------------------------------------------------------------

FACTIONS = {
    'kesslervane': ('#6FB7FF', '#0f2238'),
    'halyard': ('#FFB35C', '#2e1f0c'),
    'brassjaw': ('#FF6F5C', '#2e100c'),
    'openward': ('#7CE08A', '#0f2a16'),
    'nullrow': ('#C38CFF', '#1e1030'),
}

def emblem_mark(d, key, col):
    if key == 'kesslervane':  # a hexagon with a vane cutting through it
        pts = [(0.5 + 0.26 * math.cos(math.radians(a)), 0.5 + 0.26 * math.sin(math.radians(a))) for a in range(30, 390, 60)]
        d.line([(P(x), P(y)) for x, y in pts + pts[:1]], fill=col, width=10 * SS, joint='curve')
        d.polygon([(P(0.30), P(0.66)), (P(0.72), P(0.30)), (P(0.62), P(0.50))], fill=col)
    elif key == 'halyard':  # a hooked line over a crate
        d.rectangle([P(0.28), P(0.52), P(0.72), P(0.80)], outline=col, width=9 * SS)
        d.line([P(0.28), P(0.66), P(0.72), P(0.66)], fill=col, width=6 * SS)
        d.line([P(0.5), P(0.16), P(0.5), P(0.40)], fill=col, width=8 * SS)
        d.arc([P(0.40), P(0.32), P(0.60), P(0.52)], 0, 180, fill=col, width=8 * SS)
    elif key == 'brassjaw':  # a trap jaw
        for top in (True, False):
            y0 = 0.42 if top else 0.58
            teeth = []
            for i in range(6):
                x = 0.22 + i * 0.112
                teeth += [(P(x), P(y0)), (P(x + 0.056), P(y0 + (0.12 if top else -0.12)))]
            teeth.append((P(0.78), P(y0)))
            d.line(teeth, fill=col, width=7 * SS, joint='curve')
            d.arc([P(0.18), P(0.22 if top else 0.40), P(0.82), P(0.60 if top else 0.78)], 180 if top else 0, 360 if top else 180, fill=col, width=8 * SS)
    elif key == 'openward':  # an open door with light spilling out
        d.rectangle([P(0.30), P(0.20), P(0.62), P(0.82)], outline=col, width=8 * SS)
        d.polygon([(P(0.62), P(0.20)), (P(0.78), P(0.28)), (P(0.78), P(0.74)), (P(0.62), P(0.82))], outline=col, width=6 * SS)
        d.polygon([(P(0.34), P(0.78)), (P(0.58), P(0.78)), (P(0.70), P(0.92)), (P(0.22), P(0.92))], fill=col)
    elif key == 'nullrow':  # a grid crossed out
        for i in range(4):
            v = 0.26 + i * 0.16
            d.line([P(v), P(0.22), P(v), P(0.78)], fill=col, width=4 * SS)
            d.line([P(0.22), P(v), P(0.78), P(v)], fill=col, width=4 * SS)
        d.ellipse([P(0.24), P(0.24), P(0.76), P(0.76)], outline=col, width=9 * SS)
        d.line([P(0.24), P(0.76), P(0.76), P(0.24)], fill=col, width=10 * SS)

def faction_emblem(path, key):
    col, bg = FACTIONS[key]
    img = background(bg, '#05070a')
    L = blank(); d = ImageDraw.Draw(L)
    emblem_mark(d, key, rgba(col))
    compose(img, L, 9, 2)
    frame(img, col); finish(img, path)

def faction_goods(path, key):
    col, bg = FACTIONS[key]
    img = background('#1a1f26', '#06080a')
    L = blank(); d = ImageDraw.Draw(L)
    d.rounded_rectangle([P(0.20), P(0.34), P(0.80), P(0.80)], radius=P(0.05), fill=rgba('#3a4450'), outline=rgba('#8a96a4'), width=5 * SS)
    d.rectangle([P(0.20), P(0.50), P(0.80), P(0.60)], fill=rgba(col))
    d.rectangle([P(0.44), P(0.26), P(0.56), P(0.36)], fill=rgba('#8a96a4'))
    img.alpha_composite(L)
    E = blank(); de = ImageDraw.Draw(E)
    # the owner's mark, small, on the lid
    sub = blank(); ds = ImageDraw.Draw(sub)
    emblem_mark(ds, key, rgba('#ffffff'))
    sub = sub.resize((W // 3, W // 3), Image.LANCZOS)
    E.alpha_composite(sub, (int(W * 0.62), int(W * 0.08)))
    compose(img, E, 5, 1.4)
    frame(img, col); finish(img, path)

def dead_drop_case(path):
    img = background('#24262a', '#07080a')
    L = blank(); d = ImageDraw.Draw(L)
    d.rounded_rectangle([P(0.18), P(0.36), P(0.82), P(0.78)], radius=P(0.06), fill=rgba('#4a525c'), outline=rgba('#a8b2bc'), width=5 * SS)
    d.rounded_rectangle([P(0.38), P(0.26), P(0.62), P(0.38)], radius=P(0.03), outline=rgba('#a8b2bc'), width=6 * SS)
    img.alpha_composite(L)
    S2 = blank(); ds = ImageDraw.Draw(S2)
    ds.ellipse([P(0.42), P(0.48), P(0.58), P(0.64)], fill=rgba('#FFD966'))
    compose(img, S2, 8, 2.2)
    frame(img, '#c0a040'); finish(img, path)

def contract_mark(path):
    img = background('#2a1012', '#080506')
    L = blank(); d = ImageDraw.Draw(L)
    reticle(d, 0.5, 0.5, 0.24, rgba('#FF5C5C'), 9)
    d.line([P(0.30), P(0.30), P(0.70), P(0.70)], fill=rgba('#FFD966'), width=6 * SS)
    compose(img, L, 9, 2.2)
    frame(img, '#c04050'); finish(img, path)

def operative(path):
    img = background('#1a2430', '#06080a')
    L = blank(); d = ImageDraw.Draw(L)
    figure(d, 0.5, 0.64, 1.2, rgba('#5a6a7a'))
    img.alpha_composite(L)
    B = blank(); db = ImageDraw.Draw(B)
    db.rounded_rectangle([P(0.56), P(0.56), P(0.74), P(0.70)], radius=P(0.02), fill=rgba('#FFD966'))
    db.line([P(0.59), P(0.62), P(0.71), P(0.62)], fill=rgba('#3a3a3a'), width=3 * SS)
    compose(img, B, 6, 1.8)
    frame(img, '#c0a040'); finish(img, path)

def hit_team(path):
    img = background('#2a1012', '#080506')
    L = blank(); d = ImageDraw.Draw(L)
    for i, x in enumerate((0.30, 0.70, 0.5)):
        figure(d, x, 0.62 if i < 2 else 0.66, 0.85 if i < 2 else 1.0, rgba('#7a3a3a' if i < 2 else '#a04a4a'))
    img.alpha_composite(L)
    R = blank(); dr = ImageDraw.Draw(R)
    reticle(dr, 0.5, 0.30, 0.10, rgba('#FF5C5C'), 6)
    compose(img, R, 8, 2.2)
    frame(img, '#c04050'); finish(img, path)

# ---- Echoes (side-area anomalies and vaults) ---------------------------------------------------------------------

def in_the_dark(path):
    img = background('#0a0d18', '#020306')
    L = blank(); d = ImageDraw.Draw(L)
    d.ellipse([P(0.26), P(0.22), P(0.70), P(0.66)], fill=rgba('#c9d6ff', 220))
    d.ellipse([P(0.36), P(0.16), P(0.80), P(0.60)], fill=rgba('#0a0d18'))
    compose(img, L, 10, 1.6)
    S2 = blank(); ds = ImageDraw.Draw(S2)
    for x, y in [(0.30, 0.78), (0.50, 0.84), (0.70, 0.76)]:
        ds.ellipse([P(x - 0.018), P(y - 0.018), P(x + 0.018), P(y + 0.018)], fill=rgba('#FF5C5C'))
    compose(img, S2, 6, 2.2)
    frame(img, '#3a4a8a'); finish(img, path)

def rich_seam(path):
    img = background('#2a2410', '#080604')
    L = blank(); d = ImageDraw.Draw(L)
    gem = [(0.5, 0.20), (0.74, 0.40), (0.62, 0.80), (0.38, 0.80), (0.26, 0.40)]
    d.polygon([(P(x), P(y)) for x, y in gem], fill=rgba('#FFD966'))
    d.line([P(0.26), P(0.40), P(0.74), P(0.40)], fill=rgba('#a07a20'), width=4 * SS)
    d.line([P(0.5), P(0.20), P(0.5), P(0.80)], fill=rgba('#a07a20'), width=3 * SS)
    compose(img, L, 12, 2.2)
    frame(img, '#c0a040'); finish(img, path)

def garrison(path):
    img = background('#24161a', '#080506')
    L = blank(); d = ImageDraw.Draw(L)
    figure(d, 0.5, 0.66, 1.15, rgba('#7a4a50'))
    img.alpha_composite(L)
    C = blank(); dc = ImageDraw.Draw(C)
    chevrons(dc, 0.5, 0.30, rgba('#FFC35C'), n=2, w=0.30, h=0.09, gap=0.10, t=9)
    compose(img, C, 7, 2)
    frame(img, '#c08a40'); finish(img, path)

def vault_warden(path):
    img = background('#2a1e10', '#080604')
    L = blank(); d = ImageDraw.Draw(L)
    d.polygon([(P(0.30), P(0.22)), (P(0.70), P(0.22)), (P(0.70), P(0.54)), (P(0.50), P(0.80)), (P(0.30), P(0.54))], fill=rgba('#5a4a30'), outline=rgba('#FFD966'), width=7 * SS)
    img.alpha_composite(L)
    K = blank(); dk = ImageDraw.Draw(K)
    dk.ellipse([P(0.42), P(0.32), P(0.58), P(0.48)], outline=rgba('#FFD966'), width=6 * SS)
    dk.line([P(0.5), P(0.48), P(0.5), P(0.68)], fill=rgba('#FFD966'), width=6 * SS)
    dk.line([P(0.5), P(0.60), P(0.56), P(0.60)], fill=rgba('#FFD966'), width=5 * SS)
    compose(img, K, 8, 2.2)
    frame(img, '#c0a040'); finish(img, path)

# ---- party card UI -----------------------------------------------------------------------------------------------

def card(path, top, bottom, edge, accent):
    w, h = 500, 148
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    base = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(base)
    t0, t1 = rgba(top), rgba(bottom)
    for y in range(h):
        k = y / (h - 1)
        d.line([(0, y), (w, y)], fill=tuple(int(t0[i] + (t1[i] - t0[i]) * k) for i in range(3)) + (238,))
    mask = Image.new('L', (w, h), 0)
    ImageDraw.Draw(mask).polygon([(0, 0), (w - 24, 0), (w, 24), (w, h), (24, h), (0, h - 24)], fill=255)
    img.paste(base, (0, 0), mask)
    d = ImageDraw.Draw(img)
    e = rgba(edge)
    d.line([(0, 0), (w - 24, 0), (w - 1, 24), (w - 1, h - 1), (24, h - 1), (0, h - 24), (0, 0)], fill=e, width=3)
    d.line([(6, 6), (6, h - 30)], fill=rgba(accent), width=5)
    d.rectangle([128, 108, w - 12, 112], fill=(255, 255, 255, 14))
    img.save(path); print('wrote', path)

def tab(path):
    w, h = 240, 68
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(0, 0), (w - 20, 0), (w, 20), (w, h), (0, h)], fill=rgba('#0d1d27', 240), outline=rgba('#3fa79c'))
    d.line([(10, h - 8), (w - 10, h - 8)], fill=rgba('#3fa79c', 160), width=3)
    img.save(path); print('wrote', path)

def hp_fill(path):
    w, h = 256, 16
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for y in range(h):
        k = 1 - abs(y - h / 2) / (h / 2)
        v = int(170 + 85 * k)
        d.line([(0, y), (w, y)], fill=(v, v, v, 255))
    img.save(path); print('wrote', path)

if __name__ == '__main__':
    mods = sys.argv[1]
    cj = os.path.join(mods, 'Conjoined')
    conj_class(os.path.join(cj, 'icons', 'conjoined.png'))
    shared_marrow(os.path.join(cj, 'icons', 'shared_marrow.png'))
    trade_bodies(os.path.join(cj, 'icons', 'trade_bodies.png'))
    converging_blow(os.path.join(cj, 'icons', 'converging_blow.png'))
    knit_together(os.path.join(cj, 'icons', 'knit_together.png'))
    one_pulse(os.path.join(cj, 'icons', 'one_pulse.png'))
    spread_thin(os.path.join(cj, 'icons', 'spread_thin.png'))
    os.makedirs(os.path.join(cj, 'ui'), exist_ok=True)
    card(os.path.join(cj, 'ui', 'card.png'), '#13232e', '#08111a', '#2f6f7a', '#3fa79c')
    card(os.path.join(cj, 'ui', 'card_active.png'), '#2c2410', '#120e06', '#c9a64a', '#ffd15c')
    card(os.path.join(cj, 'ui', 'card_down.png'), '#1c1c1c', '#0c0c0c', '#5a2a2a', '#8a3a3a')
    tab(os.path.join(cj, 'ui', 'tab.png'))
    hp_fill(os.path.join(cj, 'ui', 'hp_fill.png'))
    sc = os.path.join(mods, 'SwarmConductor')
    swarm_class(os.path.join(sc, 'icons', 'swarm_conductor.png'))
    neural_uplink(os.path.join(sc, 'icons', 'neural_uplink.png'))
    hijack_drone(os.path.join(sc, 'icons', 'hijack_drone.png'))
    target_uplink(os.path.join(sc, 'icons', 'target_uplink.png'))
    priority_override(os.path.join(sc, 'icons', 'priority_override.png'))
    recall_protocol(os.path.join(sc, 'icons', 'recall_protocol.png'))
    swarm_overload(os.path.join(sc, 'icons', 'swarm_overload.png'))
    signal_fx(os.path.join(sc, 'icons', 'signal.png'))
    designated_fx(os.path.join(sc, 'icons', 'designated.png'))
    linked_fx(os.path.join(sc, 'icons', 'linked.png'))
    prioritized_fx(os.path.join(sc, 'icons', 'prioritized.png'))
    ra = os.path.join(mods, 'ReactionArchitect', 'icons')
    ra_class(os.path.join(ra, 'reaction_architect.png'))
    standing_orders(os.path.join(ra, 'standing_orders.png'))
    probe_bolt(os.path.join(ra, 'probe_bolt.png'))
    tripwire_clause(os.path.join(ra, 'tripwire_clause.png'))
    interdict(os.path.join(ra, 'interdict.png'))
    retort_protocol(os.path.join(ra, 'retort_protocol.png'))
    cascade_failure(os.path.join(ra, 'cascade_failure.png'))
    exit_clause(os.path.join(ra, 'exit_clause.png'))
    foresight_fx(os.path.join(ra, 'foresight.png'))
    profiled_fx(os.path.join(ra, 'profiled.png'))
    if len(sys.argv) > 2:  # the class loader's own assets folder
        crowded_fx(os.path.join(sys.argv[2], 'crowded.png'))
        taunted_fx(os.path.join(sys.argv[2], 'taunted.png'))
        silenced_fx(os.path.join(sys.argv[2], 'silenced.png'))
