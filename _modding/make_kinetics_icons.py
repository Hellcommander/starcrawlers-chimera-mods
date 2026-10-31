"""Original icon art for the Kinetics mod (7 classes, their skills, resources and rig hardpoints), drawn from primitives.
Usage: python make_kinetics_icons.py <Mods folder>"""
import math, os, sys
from PIL import ImageDraw
from make_icons import rgba, blank, background, compose, frame, finish, P, figure, bolt, chevrons, reticle, arrow_arc

W8 = 8  # base line width (supersampled 2x inside make_icons)

# ---- glyphs (all in 0..1 coordinates) ------------------------------------------------------------------------------

def fist(d, cx, cy, s, col, dark):
    d.rounded_rectangle([P(cx - 0.13 * s), P(cy - 0.10 * s), P(cx + 0.13 * s), P(cy + 0.12 * s)], radius=int(P(0.04 * s)), fill=col)
    for i in range(3):
        x = cx - 0.065 * s + i * 0.065 * s
        d.line([P(x), P(cy - 0.10 * s), P(x), P(cy + 0.01 * s)], fill=dark, width=6)
    d.rounded_rectangle([P(cx - 0.17 * s), P(cy - 0.02 * s), P(cx - 0.09 * s), P(cy + 0.08 * s)], radius=int(P(0.03 * s)), fill=col, outline=dark, width=4)
    d.rectangle([P(cx - 0.09 * s), P(cy + 0.12 * s), P(cx + 0.09 * s), P(cy + 0.24 * s)], fill=col)

def arrow(d, x0, y0, x1, y1, col, w=10, head=0.06):
    d.line([P(x0), P(y0), P(x1), P(y1)], fill=col, width=w * 2)
    a = math.atan2(y1 - y0, x1 - x0)
    pts = [(x1 + head * math.cos(a), y1 + head * math.sin(a)),
           (x1 + head * 0.9 * math.cos(a + 2.4), y1 + head * 0.9 * math.sin(a + 2.4)),
           (x1 + head * 0.9 * math.cos(a - 2.4), y1 + head * 0.9 * math.sin(a - 2.4))]
    d.polygon([(P(x), P(y)) for x, y in pts], fill=col)

def knife(d, cx, cy, ang, s, steel, grip):
    a = math.radians(ang); ux, uy = math.cos(a), math.sin(a); px, py = -uy, ux
    tip = (cx + ux * 0.22 * s, cy + uy * 0.22 * s)
    b0 = (cx + px * 0.035 * s, cy + py * 0.035 * s); b1 = (cx - px * 0.035 * s, cy - py * 0.035 * s)
    d.polygon([(P(tip[0]), P(tip[1])), (P(b0[0]), P(b0[1])), (P(b1[0]), P(b1[1]))], fill=steel)
    g0 = (cx + px * 0.06 * s, cy + py * 0.06 * s); g1 = (cx - px * 0.06 * s, cy - py * 0.06 * s)
    d.line([P(g0[0]), P(g0[1]), P(g1[0]), P(g1[1])], fill=grip, width=10)
    h = (cx - ux * 0.12 * s, cy - uy * 0.12 * s)
    d.line([P(cx), P(cy), P(h[0]), P(h[1])], fill=grip, width=14)

def coin(d, cx, cy, r, col, dark):
    d.ellipse([P(cx - r), P(cy - r), P(cx + r), P(cy + r)], fill=col, outline=dark, width=6)
    d.arc([P(cx - r * 0.5), P(cy - r * 0.5), P(cx + r * 0.5), P(cy + r * 0.5)], 40, 320, fill=dark, width=6)
    d.line([P(cx), P(cy - r * 0.65), P(cx), P(cy + r * 0.65)], fill=dark, width=5)

def hook(d, cx, cy, s, col):
    d.line([P(cx), P(cy - 0.25 * s), P(cx), P(cy + 0.05 * s)], fill=col, width=12)
    d.arc([P(cx - 0.12 * s), P(cy - 0.05 * s), P(cx + 0.12 * s), P(cy + 0.19 * s)], 0, 180, fill=col, width=12)
    d.polygon([(P(cx + 0.12 * s), P(cy + 0.07 * s)), (P(cx + 0.16 * s), P(cy - 0.02 * s)), (P(cx + 0.08 * s), P(cy + 0.02 * s))], fill=col)
    d.polygon([(P(cx - 0.12 * s), P(cy + 0.07 * s)), (P(cx - 0.16 * s), P(cy - 0.02 * s)), (P(cx - 0.08 * s), P(cy + 0.02 * s))], fill=col)

def shield(d, cx, cy, s, col, outline=None):
    pts = [(cx - 0.16 * s, cy - 0.18 * s), (cx + 0.16 * s, cy - 0.18 * s), (cx + 0.16 * s, cy + 0.04 * s), (cx, cy + 0.22 * s), (cx - 0.16 * s, cy + 0.04 * s)]
    d.polygon([(P(x), P(y)) for x, y in pts], fill=col, outline=outline, width=8 if outline else 0)

def flame(d, cx, cy, s, col):
    pts = [(cx, cy - 0.22 * s), (cx + 0.10 * s, cy - 0.04 * s), (cx + 0.13 * s, cy + 0.10 * s), (cx, cy + 0.20 * s), (cx - 0.13 * s, cy + 0.10 * s), (cx - 0.10 * s, cy - 0.04 * s)]
    d.polygon([(P(x), P(y)) for x, y in pts], fill=col)

def piston(d, cx, cy, s, col, dark):
    d.rectangle([P(cx - 0.09 * s), P(cy - 0.22 * s), P(cx + 0.09 * s), P(cy + 0.02 * s)], fill=col, outline=dark, width=6)
    d.rectangle([P(cx - 0.035 * s), P(cy + 0.02 * s), P(cx + 0.035 * s), P(cy + 0.20 * s)], fill=dark)
    d.rectangle([P(cx - 0.12 * s), P(cy + 0.18 * s), P(cx + 0.12 * s), P(cy + 0.24 * s)], fill=col)

def boot(d, cx, cy, s, col):
    pts = [(cx - 0.06 * s, cy - 0.2 * s), (cx + 0.05 * s, cy - 0.2 * s), (cx + 0.05 * s, cy + 0.05 * s), (cx + 0.2 * s, cy + 0.10 * s), (cx + 0.2 * s, cy + 0.18 * s), (cx - 0.06 * s, cy + 0.18 * s)]
    d.polygon([(P(x), P(y)) for x, y in pts], fill=col)

def burst(d, cx, cy, r0, r1, col, n=10, w=8):
    for i in range(n):
        a = 2 * math.pi * i / n
        d.line([P(cx + r0 * math.cos(a)), P(cy + r0 * math.sin(a)), P(cx + r1 * math.cos(a)), P(cy + r1 * math.sin(a))], fill=col, width=w * 2)

def wall(d, x, col):
    for i in range(5):
        y = 0.18 + i * 0.13
        d.rectangle([P(x), P(y), P(x + 0.09), P(y + 0.11)], fill=col)

def rope(d, pts, col, w=5):
    d.line([(P(x), P(y)) for x, y in pts], fill=col, width=w * 2, joint='curve')

def gauge(d, cx, cy, r, col, needle):
    d.arc([P(cx - r), P(cy - r), P(cx + r), P(cy + r)], 180, 360, fill=col, width=12)
    d.line([P(cx), P(cy), P(cx + r * 0.8 * math.cos(math.radians(-35))), P(cy + r * 0.8 * math.sin(math.radians(-35)))], fill=needle, width=10)

# ---- icon builder --------------------------------------------------------------------------------------------------

def icon(path, inner, outer, edge, draw, glow_r=10, strength=2.0):
    img = background(inner, outer)
    L = blank(); d = ImageDraw.Draw(L)
    draw(d)
    compose(img, L, glow_r, strength)
    frame(img, edge); finish(img, path)

C = {
    'grappler': ('#3a2410', '#0a0604', '#FFB347'), 'rig': ('#3a1a12', '#0a0504', '#FF6A3D'),
    'guard': ('#14223a', '#04060c', '#7FB2FF'), 'run': ('#123a26', '#040a06', '#7CFFB2'),
    'trick': ('#3a3010', '#0a0804', '#FFD966'), 'blade': ('#3a1218', '#0a0406', '#E05A6A'),
    'grapnel': ('#103a36', '#040a0a', '#4FD1C5'),
}
STEEL, BRASS, SKIN, DARK = rgba('#C8D0DA'), rgba('#D8A040'), rgba('#E8B48A'), rgba('#2a2018')

def make_all(out):
    def I(name, key, fn, **kw): icon(os.path.join(out, name + '.png'), *C[key], fn, **kw)
    A = lambda k: rgba(C[k][2])

    # Grappler
    I('grappler', 'grappler', lambda d: (figure(d, 0.38, 0.58, 1.0, A('grappler')), figure(d, 0.64, 0.50, 0.9, rgba('#8a6a4a')), arrow_arc(d, 0.5, 0.48, 0.26, 200, 330, rgba('#FFE0A0'), 8)))
    I('haulers_frame', 'grappler', lambda d: (figure(d, 0.5, 0.6, 1.2, A('grappler')), chevrons(d, 0.5, 0.22, rgba('#FFE0A0'), n=2)))
    I('collar_grab', 'grappler', lambda d: (fist(d, 0.36, 0.5, 1.1, A('grappler'), DARK), arrow(d, 0.78, 0.5, 0.56, 0.5, rgba('#FFE0A0'))))
    I('body_slam', 'grappler', lambda d: (figure(d, 0.40, 0.56, 0.8, rgba('#8a6a4a')), arrow(d, 0.46, 0.40, 0.74, 0.30, A('grappler')), wall(d, 0.80, rgba('#6a6a72'))))
    I('iron_clinch', 'grappler', lambda d: (fist(d, 0.36, 0.5, 0.9, A('grappler'), DARK), fist(d, 0.64, 0.5, 0.9, A('grappler'), DARK)))
    I('suplex', 'grappler', lambda d: (figure(d, 0.5, 0.66, 0.9, A('grappler')), arrow_arc(d, 0.5, 0.42, 0.22, 180, 360, rgba('#FFE0A0'), 9)))
    I('piledriver', 'grappler', lambda d: (arrow(d, 0.5, 0.18, 0.5, 0.62, A('grappler'), 14, 0.09), burst(d, 0.5, 0.78, 0.06, 0.16, rgba('#FFE0A0'), 8)))
    I('grip', 'grappler', lambda d: fist(d, 0.5, 0.48, 1.4, A('grappler'), DARK))

    # Rig Jockey
    I('rig_jockey', 'rig', lambda d: (piston(d, 0.36, 0.48, 1.2, rgba('#8a8f9a'), DARK), piston(d, 0.64, 0.48, 1.2, rgba('#8a8f9a'), DARK), flame(d, 0.5, 0.28, 0.6, A('rig'))))
    I('rig_harness', 'rig', lambda d: (figure(d, 0.5, 0.6, 1.1, rgba('#7a7f8a')), d.rectangle([P(0.36), P(0.50), P(0.64), P(0.58)], fill=A('rig'))))
    I('piston_jab', 'rig', lambda d: (piston(d, 0.5, 0.42, 1.2, rgba('#8a8f9a'), DARK), burst(d, 0.5, 0.76, 0.05, 0.13, A('rig'), 8)))
    I('hydraulic_charge', 'rig', lambda d: (chevrons(d, 0.36, 0.5, A('rig'), n=3), piston(d, 0.66, 0.5, 0.9, rgba('#8a8f9a'), DARK)))
    I('plate_up', 'rig', lambda d: (shield(d, 0.5, 0.5, 1.3, rgba('#8a8f9a'), A('rig')),))
    I('vent_blast', 'rig', lambda d: (burst(d, 0.5, 0.5, 0.12, 0.34, A('rig'), 12, 10), flame(d, 0.5, 0.5, 0.6, rgba('#FFD080'))))
    I('hammerfall', 'rig', lambda d: (piston(d, 0.5, 0.36, 1.3, rgba('#8a8f9a'), DARK), burst(d, 0.5, 0.80, 0.04, 0.14, A('rig'), 10)))
    I('heat', 'rig', lambda d: gauge(d, 0.5, 0.6, 0.3, A('rig'), rgba('#FFD080')))
    mods = [('mod_flamer', lambda d: flame(d, 0.5, 0.5, 1.0, A('rig'))), ('mod_arc', lambda d: bolt(d, [(0.55, 0.22), (0.42, 0.50), (0.56, 0.50), (0.44, 0.78)], rgba('#6FE3FF'), 10)),
            ('mod_cryo', lambda d: [d.line([P(0.5 - 0.2 * math.cos(math.radians(a))), P(0.5 - 0.2 * math.sin(math.radians(a))), P(0.5 + 0.2 * math.cos(math.radians(a))), P(0.5 + 0.2 * math.sin(math.radians(a)))], fill=rgba('#9FD8FF'), width=16) for a in (0, 60, 120)]),
            ('mod_overdrive', lambda d: gauge(d, 0.5, 0.6, 0.26, A('rig'), rgba('#FF3030'))), ('mod_heatsink', lambda d: [d.rectangle([P(0.30 + i * 0.09), P(0.30), P(0.35 + i * 0.09), P(0.72)], fill=rgba('#9FD8FF')) for i in range(5)]),
            ('mod_magnet', lambda d: (d.arc([P(0.30), P(0.30), P(0.70), P(0.70)], 180, 360, fill=rgba('#FF5050'), width=28), d.rectangle([P(0.30), P(0.50), P(0.40), P(0.66)], fill=rgba('#C8D0DA')), d.rectangle([P(0.60), P(0.50), P(0.70), P(0.66)], fill=rgba('#C8D0DA')))),
            ('mod_ram', lambda d: (piston(d, 0.40, 0.5, 0.9, rgba('#8a8f9a'), DARK), arrow(d, 0.52, 0.5, 0.80, 0.5, A('rig')))), ('mod_shield', lambda d: shield(d, 0.5, 0.5, 1.2, rgba('#7FB2FF'))),
            ('mod_spread', lambda d: [arrow(d, 0.3, 0.7, 0.3 + 0.4 * math.cos(math.radians(a)), 0.7 - 0.4 * math.sin(math.radians(a)), A('rig'), 6, 0.05) for a in (20, 50, 80)])]
    for name, fn in mods: I(name, 'rig', fn)

    # Bodyguard
    I('bodyguard', 'guard', lambda d: (figure(d, 0.62, 0.62, 0.8, rgba('#5a6a80')), shield(d, 0.42, 0.52, 1.2, A('guard'), rgba('#FFFFFF'))))
    I('thick_skin', 'guard', lambda d: (shield(d, 0.5, 0.5, 1.2, rgba('#3a4a66'), A('guard')), burst(d, 0.5, 0.5, 0.02, 0.10, rgba('#FFFFFF'), 6)))
    I('shoulder_check', 'guard', lambda d: (figure(d, 0.42, 0.58, 1.0, A('guard')), arrow(d, 0.56, 0.48, 0.82, 0.48, rgba('#FFFFFF'))))
    I('close_protection', 'guard', lambda d: (figure(d, 0.66, 0.62, 0.7, rgba('#8a96aa')), shield(d, 0.38, 0.50, 1.1, A('guard'))))
    I('over_here', 'guard', lambda d: (figure(d, 0.5, 0.62, 1.0, A('guard')), [arrow(d, 0.5 + 0.40 * math.cos(math.radians(a)), 0.45 + 0.40 * math.sin(math.radians(a)), 0.5 + 0.22 * math.cos(math.radians(a)), 0.45 + 0.22 * math.sin(math.radians(a)), rgba('#FFFFFF'), 5, 0.04) for a in (200, 270, 340)]))
    I('retort_stance', 'guard', lambda d: (shield(d, 0.4, 0.5, 1.0, A('guard')), fist(d, 0.68, 0.5, 0.7, rgba('#E8E8F0'), DARK)))
    I('payback', 'guard', lambda d: (fist(d, 0.5, 0.48, 1.3, A('guard'), DARK), burst(d, 0.5, 0.48, 0.24, 0.34, rgba('#FFFFFF'), 10, 6)))
    I('resolve', 'guard', lambda d: shield(d, 0.5, 0.5, 1.4, A('guard'), rgba('#FFFFFF')))

    # Freerunner
    I('freerunner', 'run', lambda d: (figure(d, 0.46, 0.58, 1.0, A('run')), chevrons(d, 0.76, 0.5, rgba('#FFFFFF'), n=3, w=0.12)))
    I('flow_state', 'run', lambda d: [arrow_arc(d, 0.5, 0.5, 0.22 + i * 0.08, 200 + i * 20, 330 + i * 20, A('run'), 6) for i in range(3)])
    I('flying_knee', 'run', lambda d: (boot(d, 0.5, 0.5, 1.2, A('run')), burst(d, 0.74, 0.62, 0.03, 0.10, rgba('#FFFFFF'), 6)))
    I('wall_run', 'run', lambda d: (wall(d, 0.72, rgba('#5a6a66')), arrow(d, 0.30, 0.78, 0.62, 0.26, A('run'))))
    I('slide_tackle', 'run', lambda d: (boot(d, 0.40, 0.62, 1.0, A('run')), arrow(d, 0.16, 0.70, 0.36, 0.70, rgba('#FFFFFF'), 6)))
    I('second_wind', 'run', lambda d: (arrow_arc(d, 0.5, 0.5, 0.26, 30, 330, A('run'), 9),))
    I('vault_stomp', 'run', lambda d: (boot(d, 0.5, 0.42, 1.1, A('run')), burst(d, 0.5, 0.80, 0.04, 0.14, rgba('#FFFFFF'), 8)))
    I('momentum', 'run', lambda d: chevrons(d, 0.5, 0.5, A('run'), n=3, w=0.4, h=0.12, gap=0.13, t=12))

    # Trick Shot
    I('trick_shot', 'trick', lambda d: (coin(d, 0.32, 0.66, 0.10, A('trick'), DARK), rope(d, [(0.80, 0.22), (0.32, 0.66), (0.62, 0.80)], rgba('#FFFFFF'), 3), reticle(d, 0.62, 0.80, 0.06, rgba('#FF7272'), 5)))
    I('lucky_streak', 'trick', lambda d: (coin(d, 0.40, 0.58, 0.14, A('trick'), DARK), coin(d, 0.62, 0.40, 0.10, A('trick'), DARK)))
    I('bank_shot', 'trick', lambda d: (coin(d, 0.5, 0.70, 0.09, A('trick'), DARK), rope(d, [(0.16, 0.20), (0.5, 0.70), (0.84, 0.24)], rgba('#FFFFFF'), 4)))
    I('coin_flick', 'trick', lambda d: (coin(d, 0.56, 0.36, 0.13, A('trick'), DARK), arrow_arc(d, 0.40, 0.58, 0.18, 120, 300, rgba('#FFFFFF'), 6)))
    I('pocket_change', 'trick', lambda d: [coin(d, x, y, 0.07, A('trick'), DARK) for x, y in ((0.32, 0.36), (0.56, 0.30), (0.44, 0.58), (0.68, 0.56), (0.36, 0.76))])
    I('deadeye', 'trick', lambda d: reticle(d, 0.5, 0.5, 0.18, A('trick'), 8))
    I('jackpot', 'trick', lambda d: ([coin(d, 0.5 + 0.28 * math.cos(math.radians(a)), 0.5 + 0.28 * math.sin(math.radians(a)), 0.07, A('trick'), DARK) for a in range(0, 360, 60)], burst(d, 0.5, 0.5, 0.03, 0.12, rgba('#FFFFFF'), 8)))

    # Blade Juggler
    I('blade_juggler', 'blade', lambda d: [knife(d, 0.5 + 0.18 * math.cos(math.radians(a)), 0.5 + 0.18 * math.sin(math.radians(a)), a + 90, 0.9, STEEL, A('blade')) for a in (0, 120, 240)])
    I('jugglers_rhythm', 'blade', lambda d: (arrow_arc(d, 0.5, 0.5, 0.28, 200, 520, A('blade'), 6), knife(d, 0.5, 0.48, -90, 1.1, STEEL, A('blade'))))
    I('knife_toss', 'blade', lambda d: (knife(d, 0.46, 0.54, -40, 1.3, STEEL, A('blade')), [d.line([P(0.18 - i * 0.03), P(0.82 - i * 0.05), P(0.32 - i * 0.03), P(0.70 - i * 0.05)], fill=rgba('#FFFFFF', 160), width=6) for i in range(2)]))
    I('recall', 'blade', lambda d: (knife(d, 0.66, 0.40, 200, 1.0, STEEL, A('blade')), arrow(d, 0.56, 0.44, 0.26, 0.62, rgba('#FFFFFF'), 6)))
    I('fan_of_knives', 'blade', lambda d: [knife(d, 0.5 + 0.12 * math.cos(math.radians(a)), 0.5 + 0.12 * math.sin(math.radians(a)), a, 0.8, STEEL, A('blade')) for a in range(0, 360, 60)])
    I('pinning_throw', 'blade', lambda d: (knife(d, 0.5, 0.40, 90, 1.3, STEEL, A('blade')), d.line([P(0.24), P(0.74), P(0.76), P(0.74)], fill=rgba('#6a6a72'), width=12)))
    I('thousand_returns', 'blade', lambda d: [knife(d, 0.5 + 0.30 * math.cos(math.radians(a)), 0.5 + 0.30 * math.sin(math.radians(a)), a + 180, 0.6, STEEL, A('blade')) for a in range(0, 360, 45)])

    # Grapnel Acrobat
    I('grapnel_acrobat', 'grapnel', lambda d: (rope(d, [(0.5, 0.08), (0.42, 0.40)], rgba('#C8C8C8')), figure(d, 0.40, 0.66, 0.9, A('grapnel')), hook(d, 0.5, 0.20, 0.6, rgba('#E8E8F0'))))
    I('line_discipline', 'grapnel', lambda d: (rope(d, [(0.18, 0.30), (0.5, 0.50), (0.82, 0.30)], A('grapnel'), 6), boot(d, 0.56, 0.62, 0.8, rgba('#E8E8F0'))))
    I('grapnel_yank', 'grapnel', lambda d: (hook(d, 0.70, 0.44, 0.8, rgba('#E8E8F0')), arrow(d, 0.62, 0.50, 0.24, 0.50, A('grapnel'))))
    I('zip_line', 'grapnel', lambda d: (rope(d, [(0.12, 0.80), (0.88, 0.22)], rgba('#C8C8C8'), 4), figure(d, 0.48, 0.60, 0.7, A('grapnel'))))
    I('swing_kick', 'grapnel', lambda d: (rope(d, [(0.5, 0.10), (0.38, 0.52)], rgba('#C8C8C8')), arrow_arc(d, 0.5, 0.10, 0.48, 70, 130, A('grapnel'), 8), boot(d, 0.40, 0.62, 0.8, rgba('#E8E8F0'))))
    I('wall_pin', 'grapnel', lambda d: (wall(d, 0.76, rgba('#5a6a6a')), arrow(d, 0.18, 0.5, 0.70, 0.5, A('grapnel'), 8)))
    I('pendulum', 'grapnel', lambda d: (rope(d, [(0.5, 0.12), (0.5, 0.40)], rgba('#C8C8C8')), arrow_arc(d, 0.5, 0.50, 0.30, 20, 340, A('grapnel'), 9)))

if __name__ == '__main__':
    make_all(os.path.join(sys.argv[1], 'Kinetics', 'icons'))
