"""Builds the SkillVfx mod: a particle effect of its own for every active class skill, in place of the borrowed vanilla
particles and the library templates' shared defaults.

Each skill gets a recipe line of roles, e.g. "travel=bolt impact=burst+shards/fire":
  cast    plays on the caster          impact  on every target hit (and on each chain hop)
  travel  flies caster -> target       area    once at the centre of an area / floor aim point
An archetype (burst, slash, nova, ...) is a stack of particle layers in the class's palette; "/palette" recolours one
role, "+extra" adds a layer. Sizes follow the skill's radius, and a small per-effect jitter keeps look-alikes apart.

Writes Mods/SkillVfx/<class>.json: the effects plus a patch of the class's tiers that sets all four vfx fields
(roles left out are cleared, so nothing vanilla is left playing). Class files themselves are not touched.
Where make_particle_assets.py has made a generated texture (Mods/SkillVfx/fx/<name>.png), ai_textures() swaps it in
for the matching built-in sprite: flipbook smoke, fireballs, impact flashes, lightning, sigils, debris, orbs, flames.
A class's own <class>_burst / <class>_bits sheets (make_class_particles.py) take the place of the shared flash and
debris ones, so each class's skills look like that class.
Usage: python make_skill_vfx.py [--only MOD_Castborn,MOD_Etcher] [--check]"""
import argparse, colorsys, glob, json, random, re, struct, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
MODS = HERE.parent / "Mods"
OUT = MODS / "SkillVfx"

# ---- palettes: bright, deep, light colour, smoke --------------------------------------------------------------------
PAL = {
    "slag": ("#FFC070", "#FF3A10", "#FF7A3D", "#2E2420"), "shock": ("#E0FAFF", "#2A8CFF", "#6FE3FF", "#1A2230"),
    "fire": ("#FFE08A", "#FF4A10", "#FF8A3D", "#2A1E18"), "chaos": ("#FF8AF0", "#20D0C0", "#C070FF", "#201A28"),
    "acid": ("#E8FF8A", "#4AC020", "#9BE15D", "#1E2A14"), "spore": ("#D8A0FF", "#4A8030", "#9A6CE0", "#1E1A22"),
    "wild": ("#C8FF80", "#C08020", "#8CD050", "#22200F"), "gravity": ("#D9CCFF", "#5B3BFF", "#8C6CFF", "#16122A"),
    "frost": ("#F0FBFF", "#5AB8FF", "#9FD8FF", "#C8DCEC"), "rune": ("#FFE6A0", "#20B8E0", "#6FE3FF", "#1A2028"),
    "rust": ("#FFD0A0", "#B85A20", "#FF9F43", "#2A2420"), "wind": ("#F4FBFF", "#8AB8E0", "#C8E4FF", "#B8C8D8"),
    "brine": ("#C8FFF4", "#1A80B0", "#4AD0E0", "#A8C8D0"), "blood": ("#FF8A8A", "#A00818", "#FF3040", "#2A0A0E"),
    "flesh": ("#FFB0C8", "#20B0A0", "#FF7AA0", "#241A1E"), "gold": ("#FFF0B0", "#C88A10", "#FFD070", "#2A2418"),
    "blast": ("#FFF0A0", "#FF6A10", "#FFB040", "#2A2420"), "brass": ("#FFE8B0", "#C07A20", "#FFC060", "#262018"),
    "steel": ("#E8F0FF", "#6A80A0", "#B8C8E0", "#24282E"), "laser": ("#FFB0A0", "#FF1A20", "#FF4040", "#201416"),
    "tinker": ("#F0FF90", "#30C060", "#B8F060", "#1E2418"), "nanite": ("#E8F8FF", "#3AA8C8", "#9ADCF0", "#1C2228"),
    "phase": ("#FFA0F8", "#30E0FF", "#C080FF", "#1E1828"), "psi": ("#C8E0FF", "#3060FF", "#6A9CFF", "#141A2E"),
    "seer": ("#EED8FF", "#7A30E0", "#B080FF", "#160E22"),
    "string": ("#FFB8F0", "#C020A0", "#FF70D8", "#24142A"), "idol": ("#FFE090", "#B01830", "#FFB040", "#2A1418"),
    "circus": ("#FFE880", "#E02020", "#FF9040", "#2A1A14"), "shadow": ("#C8A8FF", "#3A1870", "#8A60E0", "#0E0A16"),
    "mutagen": ("#D8FF70", "#7A20A0", "#A8F040", "#1C2014"), "glitch": ("#F0A0FF", "#FF2050", "#C040FF", "#180E20"),
    "arc": ("#FFFAC0", "#3A90FF", "#FFE066", "#1A1E28"), "flux": ("#E8FF90", "#8A40FF", "#B8F060", "#1A1A26"),
    "blade": ("#F0F4FF", "#C02838", "#E06070", "#1E1A1E"), "guard": ("#D8E8FF", "#2858B8", "#7AA0E8", "#181E28"),
    "kinetic": ("#D0FFE0", "#20A060", "#70E0A0", "#18221C"), "line": ("#C8FFF8", "#108888", "#50D8D0", "#162222"),
    "grip": ("#FFE0A0", "#C06A10", "#FFB347", "#262016"), "heat": ("#FFD090", "#E02810", "#FF6A30", "#2A1A14"),
    "coin": ("#FFF4C0", "#D0A020", "#FFD860", "#262214"), "casino": ("#E0FFC0", "#C09010", "#A8E060", "#1A2016"),
    "logic": ("#D8FCFF", "#1890C0", "#6FE3FF", "#162028"), "signal": ("#C8F8FF", "#E09020", "#6FE3FF", "#1A1E24"),
    "thought": ("#EAD8FF", "#C09020", "#B08CFF", "#1C1626"), "cipher": ("#C8FFFF", "#1878C8", "#5AE0F0", "#14202A"),
    "null": ("#B8A0FF", "#200850", "#6A40D0", "#0A0614"), "time": ("#C8FFF0", "#C89820", "#60D8C0", "#18201E"),
    "luck": ("#D0FFD8", "#D0A820", "#60E080", "#16221A"), "holy": ("#FFF8D8", "#E0A020", "#FFE090", "#2A2618"),
    "plasma": ("#FFFFFF", "#FF7A10", "#FFB050", "#2A1E14"), "moon": ("#F0F4FF", "#8090C0", "#C8D4FF", "#1E2230"),
    "heal": ("#E0FFE0", "#30C060", "#80F0A0", "#182218"), "poison": ("#E0FF90", "#5A9020", "#A0D850", "#1E2414"),
    "void": ("#E0B0FF", "#5010A0", "#A060FF", "#0E0818"), "water": ("#D0F4FF", "#2070D0", "#60B8F0", "#B0C8D8"),
    "smoke": ("#D0D0D8", "#606070", "#A0A0B0", "#30303A"), "earth": ("#E8D0A8", "#806040", "#D0A060", "#3A3024"),
    "ward": ("#F0FFFF", "#40C0E0", "#A0F0FF", "#1A2428"), "ember": ("#FFD8A0", "#E05010", "#FF9050", "#2A1E18"),
    "spellforge": ("#FFE6B0", "#8A3CFF", "#FFB050", "#1E1826"), "coldforge": ("#E4F6FF", "#3A8CFF", "#9FD4FF", "#1A2230"),
}

rng = random.Random()


def j(v, k=0.15):
    """Jitter a number by up to +-k (seeded per effect)."""
    return round(v * (1 + rng.uniform(-k, k)), 3)


def jl(lo, hi, k=0.12):
    return [j(lo, k), j(hi, k)]


# ---- layer helpers (c1 bright, c2 deep, sm smoke) -------------------------------------------------------------------

def flash(c1, c2, size=1.6, life=0.3, alpha=0.85):
    return {"texture": "soft", "count": 1, "life": [j(max(life, 0.42))], "speed": [0], "size": [j(size)], "sizeCurve": "pop", "color": c1, "color2": c2, "alpha": alpha, "shape": "point"}

def sparks(c1, c2, n=30, speed=(3, 6), size=(0.08, 0.16), shape="sphere", radius=0.2, gravity=0.0, stretch=3, life=(0.3, 0.6), drag=1.0):
    life = (max(life[0], 0.3), max(life[1], 0.5))  # impacts should read for about half a second, like vanilla ones
    return {"texture": "spark", "count": int(j(n, 0.2)), "life": jl(*life), "speed": jl(*speed), "size": jl(*size), "stretch": stretch, "gravity": gravity, "drag": drag, "color": c1, "color2": c2, "shape": shape, "radius": radius}

def motes(c1, c2, n=20, speed=(1, 2.5), size=(0.08, 0.18), shape="sphere", radius=0.3, gravity=0.0, life=(0.5, 1.0), **kw):
    d = {"texture": "mote", "count": int(j(n, 0.2)), "life": jl(*life), "speed": jl(*speed), "size": jl(*size), "gravity": gravity, "color": c1, "color2": c2, "shape": shape, "radius": radius}
    d.update(kw); return d

def ring(c1, c2, size=2.0, life=0.45, curve="grow", ground=False, n=1):
    d = {"texture": "ring", "count": n, "life": [j(life)], "speed": [0], "size": [j(size, 0.08)], "sizeCurve": curve, "color": c1, "color2": c2, "shape": "point"}
    if ground: d.update(ground=True, flat=True)
    return d

def smoke(sm, n=8, size=(0.6, 1.1), speed=(0.3, 0.9), shape="sphere", radius=0.4, alpha=0.55, life=(0.9, 1.5), delay=0.05, **kw):
    if sum(int(sm[i:i + 2], 16) for i in (1, 3, 5)) > 450: alpha *= 0.55  # pale smoke (frost, wind) reads as grey blotches
    d = {"texture": "smoke", "blend": "alpha", "count": int(j(n, 0.2)), "delay": delay, "life": jl(*life), "speed": jl(*speed), "size": jl(*size), "sizeCurve": "grow", "spin": [-50, 50], "color": sm, "color2": sm, "alpha": alpha, "shape": shape, "radius": radius}
    d.update(kw); return d

def shards(c1, c2, n=14, speed=(2.5, 5), size=(0.1, 0.22), gravity=1.2, shape="hemisphere", radius=0.2):
    return {"texture": "shard", "count": int(j(n, 0.2)), "life": jl(0.5, 0.9), "speed": jl(*speed), "size": jl(*size), "gravity": gravity, "spin": [-360, 360], "color": c1, "color2": c2, "shape": shape, "radius": radius}

def stars(c1, c2, n=8, size=(0.3, 0.6), shape="sphere", radius=0.5, life=(0.15, 0.35), speed=(0, 0.3)):
    life = (max(life[0], 0.3), max(life[1], 0.5))
    return {"texture": "star", "count": int(j(n, 0.25)), "life": jl(*life), "speed": jl(*speed), "size": jl(*size), "sizeCurve": "pop", "color": c1, "color2": c2, "shape": shape, "radius": radius}

def slashes(c1, c2, n=2, size=1.4):
    return {"texture": "spark", "count": n, "life": [j(0.36), j(0.48)], "speed": [0], "size": jl(size * 0.8, size * 1.1), "sizeCurve": "pop", "color": c1, "color2": c2, "shape": "point"}

def rise(c1, c2, n=24, radius=0.8, speed=(0.8, 1.8), size=(0.08, 0.18), tex="mote", ground=True, life=(0.7, 1.2)):
    d = {"texture": tex, "count": int(j(n, 0.2)), "life": jl(*life), "speed": jl(*speed), "size": jl(*size), "gravity": -0.2, "color": c1, "color2": c2, "shape": "cone", "angle": 8, "radius": radius}
    if ground: d["ground"] = True
    return d

def swirl(c1, c2, n=30, radius=1.0, orbit=0.8, ground=True, tex="mote", size=(0.1, 0.2), speed=(0.1, 0.4)):
    d = {"texture": tex, "count": int(j(n, 0.2)), "life": jl(0.7, 1.2), "speed": jl(*speed), "size": jl(*size), "orbit": j(orbit), "color": c1, "color2": c2, "shape": "ring", "radius": radius}
    if ground: d.update(ground=True, offset=[0, 0.3, 0])
    return d

def implode(c1, c2, n=36, radius=1.5, speed=(3, 4.5), size=(0.08, 0.18), tex="mote"):
    return {"texture": tex, "count": int(j(n, 0.2)), "life": jl(0.35, 0.5), "speed": [-speed[1], -speed[0]], "size": jl(*size), "color": c1, "color2": c2, "shape": "sphere", "radius": radius}

def drip(c1, c2, n=18, size=(0.06, 0.14)):
    return {"texture": "mote", "count": int(j(n, 0.2)), "life": jl(0.6, 1.0), "speed": jl(0.4, 1.2), "size": jl(*size), "gravity": 2.0, "color": c1, "color2": c2, "shape": "sphere", "radius": 0.3}

def trail(c1, c2, rate=110, size=(0.08, 0.18), tex="mote", life=(0.25, 0.45), speed=(0.1, 0.4), stretch=0, gravity=0.0):
    d = {"texture": tex, "count": 0, "rate": int(j(rate, 0.15)), "duration": 0.7, "life": jl(*life), "speed": jl(*speed), "size": jl(*size), "gravity": gravity, "color": c1, "color2": c2, "shape": "sphere", "radius": 0.06}
    if stretch: d["stretch"] = stretch
    return d

def head(c1, c2, size=0.6, tex="soft", spin=None):
    d = {"texture": tex, "count": 1, "life": [0.7], "speed": [0], "size": [j(size)], "sizeCurve": "flat", "color": c1, "color2": c2, "shape": "point", "duration": 0.7}
    if spin: d["spin"] = spin
    return d

# ---- water (make_water_fx.py): looping textures plus the class's own droplets, never a filmed splash --------------

def wdrops(p, n, speed, shape="hemisphere", radius=0.2, gravity=1.8, size=(0.12, 0.22), life=(0.5, 0.85), ground=False):
    """Droplets thrown and falling: the class's own droplet sheet if it has one, else glinting motes."""
    d = {"texture": "mote", "count": int(j(n)), "life": jl(*life), "speed": jl(*speed), "size": jl(*size), "gravity": gravity,
         "color": p[0], "color2": p[1], "shape": shape, "radius": radius}
    if ground: d["ground"] = True
    own_ = own("bits", "", randomFrame=True, blend="alpha", color="#FFFFFF", color2="#FFFFFF", spin=[-200, 200])
    return {**d, **own_} if own_ else d

def wspray(p, n, size=(0.6, 1.0), speed=(0.3, 0.8), shape="sphere", radius=0.3, alpha=0.5, ground=False, orbit=0.0):
    """A haze of sea spray (the generated spray cloud, else soft smoke in the palette's light colour)."""
    d = smoke(p[2], n, size, speed, shape, radius, alpha)
    if ground: d["ground"] = True
    if orbit: d["orbit"] = orbit
    sp = sheet("water_spray", color="#FFFFFF", color2=p[2])
    return {**d, **sp} if sp else d

def worb(p, size=0.55):
    """The head of a water bolt: the swirling orb loop, else a soft glow."""
    h = head(p[0], p[1], size)
    o = sheet("water_orb", color="#FFFFFF", color2=p[2], sizeCurve="flat")
    return {**h, **o} if o else h

# ---- archetypes: (palette, radius) -> layers, light ------------------------------------------------------------------

def A(layers, light=None):
    return {"layers": [l for l in layers if l], "light": light}

def L(c, rng_=3.5, i=1.8, t=0.5):
    return {"color": c, "range": j(rng_, 0.1), "intensity": j(i, 0.1), "time": j(t, 0.1)}

ARCH = {
    # impacts
    "burst":   lambda p, r: A([flash(p[0], p[1], 1.1), sparks(p[0], p[1], 30), motes(p[0], p[1], 16)], L(p[2])),
    "pop":     lambda p, r: A([flash(p[0], p[1], 0.9, 0.32), stars(p[0], p[1], 6, (0.4, 0.7), radius=0.25, life=(0.25, 0.45))], L(p[2], 2.5, 1.4, 0.3)),
    "slash":   lambda p, r: A([slashes(p[0], p[1], 2, 1.5), sparks(p[0], p[1], 16, (2, 4), (0.06, 0.12), stretch=4, life=(0.25, 0.4))]),
    "rend":    lambda p, r: A([slashes(p[0], p[1], 3, 1.3), drip(p[1], p[3], 14), sparks(p[0], p[1], 10, (1.5, 3))]),
    "smash":   lambda p, r: A([flash(p[0], p[1], 1.2, 0.3), ring(p[0], p[1], 1.3, 0.35), shards(p[0], p[1], 12), smoke(p[3], 5, (0.5, 0.9))], L(p[2], 3, 2, 0.4)),
    "pierce":  lambda p, r: A([flash(p[0], p[1], 1.0, 0.4), stars(p[0], p[1], 2, (0.6, 0.9), "point", 0), sparks(p[0], p[1], 26, (4, 8), (0.08, 0.15), life=(0.22, 0.4), stretch=5)], L(p[2], 2.5, 1.2, 0.25)),
    "zap":     lambda p, r: A([flash(p[0], p[1], 0.9, 0.2), stars(p[0], p[1], 7, (0.4, 0.9), radius=0.4, life=(0.15, 0.3)), sparks(p[0], p[1], 26, (3, 7), (0.06, 0.12), life=(0.18, 0.32), stretch=6)], L(p[2], 3.5, 2.2, 0.3)),
    "splash":  lambda p, r: A([flash(p[0], p[1], 1.0, 0.25), motes(p[0], p[1], 26, (1.5, 3.5), gravity=1.6, shape="hemisphere"), smoke(p[3], 5, (0.5, 0.9), alpha=0.45)], L(p[2], 3, 1.4, 0.4)),
    "frostburst": lambda p, r: A([flash(p[0], p[1], 1.2, 0.25), shards(p[0], p[1], 16, gravity=0.6, shape="sphere"), smoke(p[3], 6, (0.5, 1.0), alpha=0.4)], L(p[2], 3, 1.6, 0.4)),
    "flame":   lambda p, r: A([flash(p[0], p[1], 1.3, 0.3), {"texture": "soft", "count": 14, "life": jl(0.4, 0.7), "speed": jl(0.6, 1.4), "size": jl(0.4, 0.8), "sizeCurve": "pop", "gravity": -0.6, "color": p[0], "color2": p[1], "shape": "cone", "angle": 20, "radius": 0.3}, motes(p[0], p[1], 14, (1, 2.5), gravity=-0.4), smoke(p[3], 5, delay=0.2)], L(p[2], 3.5, 2, 0.6)),
    "curse":   lambda p, r: A([ring(p[0], p[1], 1.6, 0.5, "shrink"), motes(p[0], p[1], 18, (0.2, 0.6), gravity=0.6, radius=0.5), smoke(p[3], 4, (0.4, 0.8), alpha=0.5)]),
    "mind":    lambda p, r: A([ring(p[0], p[1], 1.0, 0.45, "grow"), {**ring(p[0], p[1], 1.5, 0.6, "grow"), "delay": 0.12}, stars(p[0], p[1], 5, (0.25, 0.5), radius=0.4)], L(p[2], 3, 1.2, 0.5)),
    "drain":   lambda p, r: A([implode(p[0], p[1], 44, 1.2, (2.2, 3.0), (0.12, 0.24)), ring(p[0], p[1], 1.4, 0.5, "shrink"), flash(p[0], p[1], 1.0, 0.5)], L(p[2], 2.5, 1.2, 0.5)),
    "mark":    lambda p, r: A([ring(p[0], p[1], 1.2, 0.5, "shrink"), stars(p[0], p[1], 1, (0.9, 1.1), "point", 0, (0.35, 0.4))], L(p[2], 2, 1, 0.4)),
    "glyph":   lambda p, r: A([ring(p[0], p[1], 1.4, 0.6, "pulse"), stars(p[0], p[1], 6, (0.2, 0.4), "ring", 0.5, (0.3, 0.5)), sparks(p[0], p[1], 12, (0.5, 1.2), (0.05, 0.1), shape="ring", radius=0.5)], L(p[2], 2.5, 1.4, 0.5)),
    "thread":  lambda p, r: A([stars(p[0], p[1], 4, (0.4, 0.7), radius=0.25), sparks(p[0], p[1], 18, (0.4, 1.2), (0.08, 0.14), "line", 0.6, stretch=5), ring(p[0], p[1], 1.0, 0.45, "shrink")]),
    "explode": lambda p, r: A([flash(p[0], p[1], 1.8, 0.38), sparks(p[0], p[1], 40, (5, 9), (0.1, 0.2), shape="hemisphere", gravity=0.8, drag=1.5), shards(p[0], p[1], 10), smoke(p[3], 9, (0.9, 1.6), (0.5, 1.2), "hemisphere", 0.5, 0.65)], L(p[2], 5, 2.8, 0.6)),
    "heavy":   lambda p, r: A([flash(p[0], p[1], 1.5, 0.32), ring(p[0], p[1], 1.6, 0.4), ring(p[0], p[1], 2.4, 0.5, ground=True), shards(p[0], p[1], 16, (3, 6)), smoke(p[3], 8, (0.7, 1.3), shape="hemisphere", radius=0.5)], L(p[2], 4, 2.6, 0.5)),
    "hook":    lambda p, r: A([flash(p[0], p[1], 0.7, 0.3), sparks(p[0], p[1], 16, (1.5, 3), (0.07, 0.13), life=(0.3, 0.5)), ring(p[0], p[1], 0.9, 0.4, "shrink")]),
    "coinhit": lambda p, r: A([flash(p[0], p[1], 0.8, 0.2), {"texture": "mote", "count": 10, "life": jl(0.5, 0.8), "speed": jl(2, 4), "size": jl(0.1, 0.16), "gravity": 1.8, "spin": [-360, 360], "color": p[0], "color2": p[1], "shape": "hemisphere", "radius": 0.2}, stars(p[0], p[1], 4, (0.3, 0.5), radius=0.3)], L(p[2], 2.5, 1.2, 0.3)),
    # water: bolt, splash, whirlpool, flood
    "waterbolt":   lambda p, r: A([worb(p, 0.55), trail(p[0], p[1], 90, (0.05, 0.1), "mote", (0.3, 0.5), (0.1, 0.4), gravity=1.6), {**wspray(p, 0, (0.25, 0.4), (0.05, 0.15), alpha=0.35), "rate": 24, "duration": 0.7}]),
    "watersplash": lambda p, r: A([flash(p[0], p[1], 1.0, 0.22, 0.8), wdrops(p, 26, (1.8, 3.8)), wspray(p, 4, (0.6, 1.0), (0.3, 0.8)), ring(p[0], p[1], 1.4, 0.45, ground=True)], L(p[2], 3, 1.2, 0.35)),
    "whirlpool":   lambda p, r: A([swirl(p[0], p[1], 46, r, 1.0), wdrops(p, 20, (0.3, 0.8), "ring", r * 0.8, 0.6, life=(0.6, 1.0), ground=True), wspray(p, 8, (0.8, 1.3), (0.05, 0.2), "ring", r * 0.7, 0.4, ground=True, orbit=0.7), ring(p[0], p[1], 2.8 * r, 0.7, "pulse", ground=True)], L(p[2], 3 + r, 1.6, 0.8)),
    "flood":       lambda p, r: A([ring(p[0], p[1], 2.8 * r, 0.5, ground=True), {**ring(p[0], p[1], 2.0 * r, 0.6, ground=True), "delay": 0.12}, wdrops(p, 40, (1.5, 3.5), "disc", r * 0.7, 2.0, ground=True), wspray(p, 10, (1.0, 1.6), (0.3, 0.9), "disc", r * 0.7, 0.5, ground=True)], L(p[2], 3 + r, 2, 0.6)),
    # area / self-area
    "nova":    lambda p, r: A([flash(p[0], p[1], 2.2 + r * 0.4, 0.35), ring(p[0], p[1], 2.8 * r, 0.5, ground=True), sparks(p[0], p[1], 40, (4, 8), (0.1, 0.22), "hemisphere", 0.4, 0.6, drag=1.5), smoke(p[3], 8, (1.0, 1.8), (0.8, 1.6), "ring", r * 0.4, 0.5, ground=True)], L(p[2], 4 + r, 2.6, 0.6)),
    "quake":   lambda p, r: A([ring(p[0], p[1], 2.8 * r, 0.5, ground=True), {**ring(p[0], p[1], 2.0 * r, 0.6, ground=True), "delay": 0.1}, shards(p[3], p[1], 24, (3, 6), (0.12, 0.28), 1.8, "disc", r * 0.6) | {"ground": True}, smoke(p[3], 10, (1.0, 1.8), (0.4, 1.0), "disc", r * 0.6, 0.6, ground=True)], L(p[2], 3 + r, 1.8, 0.5)),
    "blastzone": lambda p, r: A([flash(p[0], p[1], 2.0 + r, 0.4), ring(p[0], p[1], 2.8 * r, 0.45, ground=True), sparks(p[0], p[1], 50, (4, 9), (0.1, 0.22), "hemisphere", r * 0.5, 0.9, drag=1.5) | {"ground": True}, smoke(p[3], 12, (1.0, 1.8), (0.6, 1.4), "disc", r * 0.6, 0.65) | {"ground": True}], L(p[2], 4 + r, 3, 0.6)),
    "pull":    lambda p, r: A([ring(p[0], p[1], 2.8 * r, 0.55, "shrink", ground=True), implode(p[0], p[1], 50, r, (r * 2.5, r * 3.5)) | {"ground": True, "offset": [0, 0.5, 0]}, flash(p[0], p[1], 1.4, 0.4)], L(p[2], 3 + r, 2, 0.6)),
    "vortex":  lambda p, r: A([swirl(p[0], p[1], 50, r, 0.9), smoke(p[3], 10, (0.8, 1.4), (0.1, 0.3), "ring", r * 0.8, 0.45, ground=True, orbit=0.6), ring(p[0], p[1], 2.8 * r, 0.7, "pulse", ground=True)], L(p[2], 3 + r, 1.6, 0.8)),
    "field":   lambda p, r: A([ring(p[0], p[1], 2.8 * r, 0.8, "flat", ground=True), rise(p[0], p[1], 30, r * 0.8, (0.3, 0.8)), smoke(p[3], 8, (0.8, 1.4), (0.05, 0.2), "disc", r * 0.7, 0.4, ground=True)], L(p[2], 3 + r, 1.2, 0.8)),
    "fog":     lambda p, r: A([smoke(p[3], 16, (1.4, 2.4), (0.1, 0.4), "disc", max(r, 1.2) * 0.8, 0.6, (1.4, 2.2), ground=True), motes(p[0], p[1], 14, (0.1, 0.3), radius=1.0)]),
    "rain":    lambda p, r: A([{"texture": "spark", "count": int(j(36)), "life": jl(0.4, 0.55), "speed": [0.05, 0.1], "gravity": 4.0, "size": jl(0.12, 0.24), "stretch": 4, "color": p[0], "color2": p[1], "shape": "disc", "radius": r * 0.8, "ground": True, "offset": [0, 3.5, 0]}, ring(p[0], p[1], 2.6 * r, 0.5, ground=True), smoke(p[3], 6, (0.8, 1.4), shape="disc", radius=r * 0.6, ground=True, delay=0.4)], L(p[2], 3 + r, 2, 0.6)),
    "web":     lambda p, r: A([ring(p[0], p[1], 2.8 * r, 0.6, "flat", ground=True), ring(p[0], p[1], 1.6 * r, 0.6, "flat", ground=True), sparks(p[0], p[1], 30, (0.5, 1.5), (0.05, 0.1), "ring", r, life=(0.3, 0.6)) | {"ground": True, "offset": [0, 0.2, 0]}], L(p[2], 3, 1.4, 0.5)),
    # self
    "aura":    lambda p, r: A([ring(p[0], p[1], 2.4, 0.6, ground=True), rise(p[0], p[1], 26, 0.7), stars(p[0], p[1], 4, (0.3, 0.5), radius=0.6)], L(p[2], 3, 1.6, 0.7)),
    "shield":  lambda p, r: A([flash(p[0], p[1], 2.0, 0.35, 0.5), stars(p[0], p[1], 10, (0.3, 0.6), "ring", 0.9, (0.3, 0.6)), swirl(p[0], p[1], 24, 0.9, 1.0, ground=False)], L(p[2], 3, 1.6, 0.6)),
    "heal":    lambda p, r: A([rise(p[0], p[1], 20, 0.6, tex="star", size=(0.15, 0.3)), rise(p[0], p[1], 20, 0.8), ring(p[0], p[1], 2.0, 0.6, ground=True)], L(p[2], 3, 1.4, 0.7)),
    "summon":  lambda p, r: A([ring(p[0], p[1], 3.0, 0.6, ground=True), rise(p[0], p[1], 40, 0.3, (2, 4), life=(0.4, 0.7)), flash(p[0], p[1], 1.6, 0.4), smoke(p[3], 6, (0.7, 1.2), shape="ring", radius=0.8, ground=True)], L(p[2], 4, 2.2, 0.7)),
    "blink":   lambda p, r: A([implode(p[0], p[1], 30, 1.0, (2.5, 3.5)), flash(p[0], p[1], 1.4, 0.25), smoke(p[3], 5, (0.6, 1.0), alpha=0.4)], L(p[2], 3, 1.6, 0.3)),
    "stealth": lambda p, r: A([smoke(p[3], 14, (1.0, 1.8), (0.3, 0.8), "sphere", 0.6, 0.7, (1.0, 1.6), 0.0), motes(p[0], p[1], 12, (0.2, 0.6), radius=0.7)]),
    "arm":     lambda p, r: A([ring(p[0], p[1], 2.2, 0.7, "pulse", ground=True), stars(p[0], p[1], 6, (0.2, 0.4), "ring", 0.8, (0.3, 0.5)) | {"ground": True, "offset": [0, 0.3, 0]}], L(p[2], 2.5, 1.2, 0.5)),
    "power":   lambda p, r: A([flash(p[0], p[1], 1.8, 0.5, 0.8), implode(p[0], p[1], 44, 1.4, (2.2, 3.2), (0.12, 0.24)), stars(p[0], p[1], 6, (0.4, 0.7), radius=0.6), {**ring(p[0], p[1], 2.6, 0.5, ground=True), "delay": 0.3}], L(p[2], 4, 2.4, 0.8)),
    "taunt":   lambda p, r: A([ring(p[0], p[1], 3.2, 0.5, ground=True), {**ring(p[0], p[1], 4.4, 0.6, ground=True), "delay": 0.15}, sparks(p[0], p[1], 20, (3, 5), shape="hemisphere", radius=0.3)], L(p[2], 4, 2, 0.5)),
    "craft":   lambda p, r: A([sparks(p[0], p[1], 34, (2, 4), (0.05, 0.1), "cone", 0.1, 1.2, life=(0.3, 0.6)) | {"angle": 35}, flash(p[0], p[1], 0.9, 0.25), smoke(p[3], 4, (0.4, 0.7))], L(p[2], 2.5, 1.6, 0.4)),
    "dice":    lambda p, r: A([stars(p[0], p[1], 8, (0.2, 0.45), radius=0.6, life=(0.2, 0.5)), swirl(p[0], p[1], 20, 0.6, 1.5, ground=False, tex="star", size=(0.12, 0.22)), flash(p[0], p[1], 0.9, 0.3)], L(p[2], 2.5, 1.2, 0.5)),
    # travel
    "bolt":    lambda p, r: A([head(p[0], p[1], 0.55), trail(p[0], p[1])]),
    "orb":     lambda p, r: A([head(p[0], p[1], 0.9), head(p[0], p[1], 0.5, "ring", [180, 360]), trail(p[0], p[1], 80, (0.1, 0.22))]),
    "dart":    lambda p, r: A([head(p[0], p[1], 0.5, "shard", [600, 900]), trail(p[0], p[1], 100, (0.08, 0.15), "spark", (0.15, 0.25), stretch=3)]),
    "bullet":  lambda p, r: A([head(p[0], p[1], 0.45, "star"), trail(p[0], p[1], 160, (0.07, 0.12), "spark", (0.08, 0.15), (0.05, 0.2), 2)]),
    "lance":   lambda p, r: A([head(p[0], p[1], 0.45, "spark"), trail(p[0], p[1], 200, (0.08, 0.14), "soft", (0.15, 0.3), (0, 0.1))]),
    "beam":    lambda p, r: A([head(p[0], p[1], 0.4, "star"), trail(p[0], p[1], 260, (0.06, 0.12), "soft", (0.2, 0.35), (0, 0.05))]),
    "glob":    lambda p, r: A([head(p[0], p[1], 0.6, "mote"), trail(p[1], p[3], 70, (0.06, 0.14), gravity=1.6, life=(0.3, 0.6))]),
    "knife":   lambda p, r: A([head(p[0], p[1], 0.6, "shard", [900, 1300]), trail(p[0], p[1], 70, (0.06, 0.11), "spark", (0.1, 0.2), stretch=2)]),
    "cord":    lambda p, r: A([head(p[0], p[1], 0.45), trail(p[0], p[1], 280, (0.06, 0.11), "soft", (0.4, 0.6), (0, 0.03))]),
    "flare":   lambda p, r: A([head(p[0], p[1], 0.7, "star", [90, 180]), trail(p[0], p[1], 120, (0.08, 0.2), life=(0.3, 0.5)), trail(p[3], p[3], 30, (0.2, 0.4), "smoke", (0.4, 0.7))]),
    "lob":     lambda p, r: A([head(p[0], p[1], 0.4, "mote", [200, 400]), trail(p[3], p[3], 40, (0.12, 0.25), "smoke", (0.3, 0.5))]),
    "coin":    lambda p, r: A([head(p[0], p[1], 0.42, "mote", [720, 1080]), trail(p[0], p[1], 60, (0.06, 0.1), "star", (0.1, 0.2))]),
    "swarm":   lambda p, r: A([trail(p[0], p[1], 220, (0.04, 0.08), "mote", (0.3, 0.5), (0.4, 1.0))]),
    "rush":    lambda p, r: A([trail(p[0], p[1], 160, (0.1, 0.25), "spark", (0.2, 0.35), (0.1, 0.3), 3), trail(p[3], p[3], 40, (0.3, 0.6), "smoke", (0.4, 0.6))]),
}

EXTRA = {
    "shards": lambda p, r: shards(p[0], p[1], 10), "smoke": lambda p, r: smoke(p[3], 6), "sparks": lambda p, r: sparks(p[0], p[1], 20),
    "ring": lambda p, r: ring(p[0], p[1], 1.6, 0.4), "groundring": lambda p, r: ring(p[0], p[1], 2.4, 0.5, ground=True),
    "drip": lambda p, r: drip(p[1], p[3]), "stars": lambda p, r: stars(p[0], p[1], 6), "embers": lambda p, r: motes(p[0], p[1], 14, (1, 2.5), gravity=-0.4),
    "rise": lambda p, r: rise(p[0], p[1], 18, 0.6), "swirl": lambda p, r: swirl(p[0], p[1], 20, 0.8, ground=False),
    "implode": lambda p, r: implode(p[0], p[1], 24, 1.0), "flash": lambda p, r: flash(p[0], p[1], 1.6), "frost": lambda p, r: shards("#F0FBFF", "#5AB8FF", 10, gravity=0.5, shape="sphere"),
    "fire": lambda p, r: motes("#FFE08A", "#FF4A10", 16, (1, 2.5), gravity=-0.6), "zap": lambda p, r: sparks("#E0FAFF", "#2A8CFF", 18, (3, 6), (0.05, 0.1), stretch=6, life=(0.1, 0.2)),
    "blood": lambda p, r: drip("#FF3040", "#5A0810", 14), "slash": lambda p, r: slashes(p[0], p[1], 2, 1.3), "fog": lambda p, r: smoke(p[3], 10, (1.0, 1.6), alpha=0.5),
}

# ---- recipes ---------------------------------------------------------------------------------------------------------
# class id -> (default palette, {tier: "role=arch[+extra][/palette] ..."})
RECIPES = {
    "MOD_Castborn": ("slag", {
        1: "impact=smash+embers", 2: "cast=craft/steel", 3: "cast=shield", 4: "cast=summon",
        5: "area=heavy+embers cast=power", 6: "area=nova+embers cast=taunt"}),
    "MOD_Coilwright": ("shock", {
        1: "travel=bolt impact=zap", 2: "cast=summon+zap", 3: "travel=lob area=web", 4: "cast=aura+zap",
        5: "travel=lance impact=heavy+zap", 6: "cast=arm+zap"}),
    "MOD_Emberhorn": ("fire", {
        1: "cast=rush area=blastzone", 2: "impact=rend+embers", 3: "cast=summon+embers", 4: "area=quake+fire",
        5: "cast=power+fire", 6: "impact=heavy+embers"}),
    "MOD_EntropyWeaver": ("chaos", {
        1: "travel=orb impact=dice", 2: "cast=summon+stars", 3: "travel=lob area=blastzone+stars",
        4: "travel=cord impact=mind", 5: "area=pull+stars cast=power", 6: "travel=flare impact=zap/chaos"}),
    "MOD_Etcher": ("acid", {
        1: "travel=dart impact=splash", 2: "travel=glob impact=splash+drip", 3: "travel=lob impact=field+drip",
        4: "cast=aura+smoke", 5: "travel=beam impact=burst+drip", 6: "cast=blink area=field", 7: "travel=lance impact=explode"}),
    "MOD_GloomsporeWarden": ("spore", {
        2: "travel=dart impact=curse", 3: "cast=summon+fog", 4: "travel=cord impact=curse", 5: "cast=heal+fog",
        6: "area=fog+rise", 7: "cast=power+fog"}),
    "MOD_Graftwild": ("wild", {
        1: "impact=rend", 2: "travel=lob area=web+smoke", 3: "cast=rush impact=slash", 4: "cast=rush area=quake",
        5: "area=nova+fog cast=power", 6: "travel=cord impact=hook"}),
    "MOD_GravemoteAstronomer": ("gravity", {
        1: "travel=orb impact=pop", 2: "area=pull", 3: "cast=blink+swirl", 4: "impact=mark",
        5: "travel=orb area=pull+flash", 6: "travel=cord impact=drain", 7: "cast=summon+swirl/moon"}),
    "MOD_RimelightSeer": ("frost", {
        0: "travel=lance impact=frostburst", 1: "cast=summon", 7: "travel=beam impact=frostburst+flash",
        5: "travel=cord impact=mind", 6: "travel=cord impact=mind+frost"}),
    "MOD_Runeforger": ("rune", {
        1: "cast=glyph", 2: "cast=craft", 3: "travel=dart impact=burst+shards", 4: "cast=summon+stars",
        5: "area=nova+stars", 6: "cast=shield+stars"}),
    "MOD_Scrapper": ("rust", {
        1: "impact=smash", 2: "travel=lob impact=smash+shards", 3: "impact=pop", 4: "impact=smash+slash",
        5: "area=quake+shards", 6: "cast=rush impact=heavy"}),
    "MOD_Squallblade": ("wind", {
        1: "travel=lance impact=slash", 2: "travel=rush impact=smash+fog", 3: "travel=cord impact=pull",
        4: "cast=blink area=vortex", 5: "area=vortex+sparks", 6: "area=vortex+implode"}),
    "MOD_Tidewright": ("brine", {
        0: "travel=waterbolt impact=watersplash", 1: "travel=waterbolt impact=watersplash", 2: "travel=waterbolt impact=fog",
        3: "cast=blink area=watersplash", 4: "cast=summon+drip", 5: "area=whirlpool", 6: "impact=watersplash", 7: "area=flood"}),
    "MOD_Fulgurant": ("shock", {
        1: "cast=rush impact=zap+sparks", 2: "cast=blink impact=slash+zap", 3: "cast=rush impact=zap+sparks",
        4: "area=nova+sparks", 5: "travel=beam impact=zap", 6: "impact=explode+zap"}),
    "MOD_Snareseer": ("seer", {
        1: "area=glyph", 2: "area=pull+swirl", 3: "area=glyph+rise", 4: "cast=blink area=mind",
        5: "area=glyph+stars", 6: "cast=power+stars"}),
    "MOD_Mimeticist": ("mutagen", {
        1: "travel=dart impact=pop+drip", 2: "cast=power+rise", 3: "cast=power+rise", 4: "cast=power+rise",
        5: "cast=power+drip", 6: "cast=power+rise", 7: "cast=power+stars"}),
    "MOD_BulkheadPsion": ("steel", {
        1: "impact=heavy+shards", 2: "area=quake+shards", 3: "impact=pierce+shards", 4: "area=pull+shards",
        5: "impact=smash+shards", 6: "impact=heavy+shards"}),
    "MOD_ReactantHauler": ("rust", {
        1: "travel=lob impact=splash+drip", 2: "cast=craft", 3: "travel=lob impact=explode/fire", 4: "travel=lob area=frostburst",
        5: "travel=lob area=splash/brine", 6: "impact=burst+shards", 7: "travel=lob area=explode+drip"}),
    "MOD_Miasmancer": ("acid", {
        1: "travel=glob impact=splash+drip", 2: "area=fog+drip", 3: "travel=cord impact=drain", 4: "area=explode+drip",
        5: "impact=burst+shards", 6: "area=explode+fog"}),
    "MOD_Wildcaster": ("chaos", {
        1: "travel=bolt impact=pop+stars", 2: "travel=orb impact=burst", 3: "cast=shield", 4: "area=vortex+stars",
        6: "travel=orb impact=explode+stars"}),
    "MOD_Vasculist": ("blood", {
        1: "impact=pierce+drip", 2: "area=pull+drip", 3: "cast=summon+drip", 4: "impact=curse+drip",
        5: "travel=cord impact=mind", 6: "impact=pop+drip", 7: "impact=heavy+drip"}),
    "MOD_Conjoined": ("flesh", {1: "cast=blink", 2: "impact=heavy", 3: "cast=heal", 5: "area=nova+slash"}),
    "MOD_Corsair": ("gold", {
        1: "impact=slash", 2: "cast=rush impact=slash+sparks", 3: "cast=heal/brass", 5: "area=blastzone/blast", 6: "impact=rend+stars"}),
    "MOD_Demolitionist": ("blast", {
        1: "travel=lob impact=mark", 2: "travel=lob area=blastzone", 3: "travel=cord/ember impact=pop",
        4: "cast=arm/ember", 5: "area=explode", 6: "area=field/ember"}),
    "MOD_GunKata": ("brass", {
        1: "travel=bullet impact=pierce", 2: "cast=blink/smoke", 3: "travel=bullet impact=pierce+drip",
        5: "area=nova+sparks", 6: "travel=bullet impact=pierce+stars"}),
    "MOD_Juggernaut": ("steel", {
        1: "impact=heavy", 2: "cast=rush area=quake", 3: "cast=shield/ward", 5: "area=quake+flash", 6: "cast=taunt/ember"}),
    "MOD_Longshot": ("laser", {
        1: "travel=bullet impact=pierce", 2: "travel=bullet impact=smash", 3: "cast=aura/wind", 5: "travel=lance impact=explode", 6: "cast=arm"}),
    "MOD_MadTinkerer": ("tinker", {
        1: "travel=bolt impact=zap", 2: "travel=lob area=blastzone+stars", 3: "cast=power+zap", 5: "travel=lob area=explode+zap",
        6: "cast=summon+sparks"}),
    "MOD_NaniteHerder": ("nanite", {
        1: "travel=swarm impact=curse", 2: "cast=heal+swirl", 3: "travel=swarm impact=drain", 5: "area=fog+swirl",
        6: "area=blastzone+implode"}),
    "MOD_PhaseDrifter": ("phase", {
        1: "cast=blink", 2: "impact=slash+stars", 3: "cast=stealth", 5: "cast=blink area=vortex+slash", 6: "impact=blink cast=blink"}),
    "MOD_PsiKnight": ("psi", {
        1: "impact=slash+ring", 2: "travel=lance impact=mind", 3: "cast=shield", 5: "impact=heavy+stars", 6: "area=pull+stars"}),
    "MOD_Puppetmaster": ("string", {
        1: "travel=cord impact=thread", 2: "travel=cord impact=mind", 3: "cast=summon/smoke", 5: "area=nova+slash", 6: "area=web"}),
    "MOD_RelicBearer": ("idol", {
        1: "travel=cord impact=drain", 2: "travel=orb impact=curse", 3: "cast=heal", 5: "area=pull+drip", 6: "travel=flare impact=mind"}),
    "MOD_Ringmaster": ("circus", {
        1: "impact=slash+stars", 2: "area=nova+fire/fire impact=flame/fire", 3: "cast=rush area=blastzone", 4: "cast=aura+stars", 5: "area=explode+stars",
        6: "travel=cord impact=hook"}),
    "MOD_ShadowBroker": ("shadow", {
        1: "impact=rend", 2: "cast=stealth", 3: "travel=dart impact=curse", 5: "impact=slash+implode", 6: "cast=stealth area=fog"}),
    "MOD_SpliceHost": ("mutagen", {
        1: "cast=shield/earth", 2: "impact=rend", 3: "cast=power+slash", 4: "cast=aura+shards", 5: "area=nova+drip", 6: "cast=rush impact=heavy"}),
    "MOD_VoidHacker": ("glitch", {
        1: "travel=dart impact=zap", 2: "travel=swarm impact=glyph", 3: "cast=shield", 5: "impact=explode", 6: "cast=summon+zap"}),
    "MOD_ArcGunner": ("arc", {1: "travel=bullet impact=zap", 2: "travel=lance impact=zap", 3: "cast=shield+zap", 5: "travel=lance impact=heavy+zap"}),
    "MOD_Spellsmith": ("spellforge", {
        0: "cast=craft travel=lance impact=burst+embers", 1: "cast=arm", 2: "cast=craft", 3: "cast=summon+sparks", 4: "cast=power+sparks",
        6: "cast=craft+ring", 7: "cast=blink+swirl", 8: "cast=power+stars"}),
    "MOD_FluxSavant": ("flux", {
        1: "travel=lance impact=zap", 2: "cast=craft+stars", 3: "travel=lob area=blastzone/acid", 4: "travel=bolt impact=web/frost",
        5: "area=explode", 6: "travel=dart impact=explode+shards"}),
    "MOD_BladeJuggler": ("blade", {
        1: "travel=knife impact=slash", 2: "cast=pull+slash", 3: "area=nova+slash", 4: "travel=knife impact=smash",
        5: "area=rain", 6: "impact=rend+slash"}),
    "MOD_Bodyguard": ("guard", {
        1: "impact=smash", 2: "cast=shield", 3: "cast=taunt", 4: "cast=arm", 5: "impact=heavy+flash", 6: "area=nova"}),
    "MOD_Freerunner": ("kinetic", {
        1: "impact=smash", 2: "cast=blink/wind", 3: "cast=rush impact=smash", 4: "cast=aura", 5: "area=quake+flash", 6: "impact=pop+slash"}),
    "MOD_GrapnelAcrobat": ("line", {
        1: "travel=cord impact=hook", 2: "travel=cord impact=pop", 3: "travel=cord impact=smash", 4: "travel=dart impact=heavy",
        5: "area=vortex", 6: "travel=cord impact=hook+sparks"}),
    "MOD_Grappler": ("grip", {
        1: "travel=cord impact=hook", 2: "impact=heavy", 3: "cast=taunt", 4: "impact=heavy+stars", 5: "impact=heavy+groundring", 6: "impact=smash+slash"}),
    "MOD_RigJockey": ("heat", {
        1: "impact=smash+embers", 2: "cast=rush impact=heavy", 3: "cast=shield/steel", 4: "area=nova+embers", 5: "impact=heavy+embers",
        6: "impact=rend+sparks"}),
    "MOD_TrickShot": ("coin", {
        1: "travel=bullet impact=coinhit", 2: "travel=coin area=arm", 3: "travel=coin impact=coinhit", 4: "cast=aura+stars",
        5: "area=rain/coin", 6: "cast=arm"}),
    "MOD_OddsBreaker": ("casino", {
        0: "travel=coin impact=coinhit", 1: "travel=orb impact=dice", 2: "impact=mark", 3: "travel=coin impact=explode/gold",
        5: "travel=flare impact=heavy/gold", 6: "area=nova+stars"}),
    "MOD_ReactionArchitect": ("logic", {
        1: "travel=bolt impact=glyph", 2: "cast=arm", 3: "cast=arm+stars", 4: "cast=arm+zap", 5: "area=nova+zap", 6: "cast=blink+stars"}),
    "MOD_SwarmConductor": ("signal", {
        0: "cast=craft", 1: "cast=summon+zap", 2: "travel=beam impact=mark", 3: "cast=power", 4: "cast=blink",
        5: "area=nova+zap", 6: "cast=summon+sparks", 8: "cast=summon+stars"}),
    "MOD_Axiomancer": ("thought", {
        1: "impact=mind", 2: "travel=lance impact=explode/thought", 4: "cast=shield", 5: "impact=mind+stars", 6: "travel=orb impact=pop",
        7: "cast=power+stars", 8: "area=nova+stars"}),
    "MOD_Glyphrunner": ("cipher", {
        1: "impact=glyph", 2: "impact=glyph+swirl", 3: "impact=slash+stars", 4: "impact=explode", 5: "area=blastzone+stars", 6: "travel=cord impact=glyph"}),
    "MOD_NullChorister": ("null", {
        1: "travel=orb impact=mind", 2: "impact=drain", 3: "area=pull", 4: "cast=shield", 5: "area=nova+implode",
        6: "impact=heavy"}),
    "MOD_ParadoxWitch": ("time", {
        1: "cast=power", 2: "impact=burst+stars", 3: "cast=glyph", 4: "travel=orb impact=mind", 5: "area=field+smoke", 6: "area=pull+stars"}),
    "MOD_ProbabilityShaman": ("luck", {
        1: "travel=coin impact=pop", 2: "cast=dice", 3: "cast=stealth", 4: "cast=shield+stars", 5: "area=nova+stars", 6: "travel=flare impact=dice"}),
    "MOD_ReliquaryKnight": ("holy", {
        1: "cast=aura/fire", 2: "impact=heavy", 3: "cast=aura/moon", 4: "cast=shield/earth", 5: "area=nova", 6: "cast=rush impact=heavy+flash"}),
    "MOD_StarfireAdept": ("plasma", {
        1: "travel=flare impact=burst", 2: "travel=cord impact=zap/plasma", 3: "cast=power", 4: "area=nova+embers", 5: "area=explode+fire",
        6: "area=pull+zap"}),
}

ROLE_KEY = {"cast": "castVfx", "impact": "impactVfx", "travel": "travelVfx", "area": "areaVfx"}

# Effects a class names by id in its own JSON (proc vfx, surge vfx, "vfx" actions, formVfx), beyond its skills' slots:
# class -> {key: (recipe, radius)}; each becomes fx_<class>_<key>. A recipe is one role-less "arch+extra/palette".
NAMED = {
    # the shapes its forms put on other classes' attacks
    "MOD_Spellsmith": {
        "buried": ("explode+embers", 1.0), "lobbed_travel": ("lob", 1.0), "lobbed": ("blastzone+embers", 1.5), "shell": ("explode+shards", 1.5),
        "hammer": ("heavy+sparks", 1.0), "masterwork": ("flare", 1.0),
        "cold": ("frostburst+flash/coldforge", 1.0),
        # the loose spell wandering the deck: steady streams only (played on a loop)
        "loose": {"layers": [
            {"texture": "soft", "count": 0, "rate": 14, "duration": 1.0, "life": [0.5, 0.7], "speed": [0, 0.05], "size": [0.4, 0.55],
             "sizeCurve": "shrink", "color": "#FFB050", "color2": "#8A3CFF", "alpha": 0.4, "shape": "point"},
            {"texture": "fx/spellsmith_bits.png", "grid": [2, 2], "randomFrame": True, "blend": "alpha", "count": 0, "rate": 18, "duration": 1.0,
             "life": [0.6, 1.0], "speed": [0.1, 0.3], "size": [0.07, 0.14], "spin": [-200, 200], "color": "#FFE6B0", "color2": "#8A3CFF",
             "shape": "sphere", "radius": 0.35, "orbit": 1.2},
            {"texture": "mote", "count": 0, "rate": 40, "duration": 1.0, "life": [0.5, 0.9], "speed": [0, 0.15], "size": [0.05, 0.1],
             "gravity": -0.3, "color": "#FFE6B0", "color2": "#8A3CFF", "alpha": 0.8, "shape": "sphere", "radius": 0.25},
        ]},
    },
    "MOD_Wildcaster": {
        # Unruly Casting's forms
        "form_area": ("nova+stars", 2.0), "form_line": ("lance", 5.0), "form_scatter": ("pop+sparks", 1.0),
        "form_retarget": ("bolt", 1.0), "form_trap": ("glyph+swirl", 1.2), "form_backfire": ("curse+flash/blood", 1.0),
        # Stray Magic and the capstone's own surges
        "stray": ("pop+stars", 1.0), "riot_blast": ("nova+implode", 3.0), "riot_self": ("power+rise", 1.0),
        "riot_spark": ("zap/plasma", 1.0), "riot_lash": ("zap+sparks/shock", 1.0), "riot_frost": ("frostburst/frost", 1.0),
        "riot_void": ("drain/void", 1.0), "riot_curse": ("curse+stars", 1.0),
        # boss echoes, each in its element
        "echo_thundercrunch": ("zap+stars/shock", 2.0), "echo_gigaslam": ("quake+zap/kinetic", 1.5),
        "echo_smorn_blast": ("explode+flash/plasma", 2.5), "echo_smorn_wave": ("nova+slash/steel", 3.0),
        "echo_straha_laser": ("pierce+implode/void", 1.0), "echo_firebolt": ("flame+embers/fire", 1.0),
        "echo_tendrils": ("drain+swirl/shadow", 2.0), "echo_spit": ("frostburst+drip/frost", 1.5),
        "echo_annihilation": ("explode+implode/null", 2.0),
        "echo_swipe": ("slash+sparks/blood", 1.5), "echo_pull": ("pull/void", 3.0), "echo_inferno": ("blastzone+embers/fire", 2.0),
        "echo_shockfield": ("rain/shock", 2.0), "echo_warp": ("blink+implode/void", 1.5), "echo_conduit": ("drain+stars/void", 1.0),
        "echo_shell": ("shield/shadow", 2.0), "echo_touch": ("power+rise/gold", 1.5), "echo_mending": ("heal/gold", 2.0),
        "echo_flurry": ("rend+sparks/blood", 1.0), "echo_spit_bio": ("splash+drip/poison", 1.5), "echo_spit_fire": ("splash+embers/fire", 1.5),
        "echo_pulse": ("nova+zap/shock", 3.0), "echo_charge": ("smash+sparks/steel", 1.0), "echo_smash": ("heavy/steel", 1.0),
        "echo_widow": ("blink+sparks/steel", 1.0), "echo_portals": ("vortex+implode/void", 2.0), "echo_docwarp": ("quake+implode/shadow", 1.5),
        "echo_stab": ("pierce+sparks/blood", 1.0), "echo_rot": ("drain+drip/poison", 1.5), "echo_ugh_charge": ("heavy+sparks/kinetic", 1.0),
        "echo_shot": ("pierce+sparks/frost", 1.0), "echo_mut_charge": ("smash+zap/steel", 1.0), "echo_rip": ("rend/blood", 1.0),
        "echo_glide": ("blink+sparks/plasma", 1.0), "echo_strike": ("slash+sparks/plasma", 1.0),
        "brood_horror": ("summon+implode/null", 1.5), "brood_widow": ("summon/steel", 1.0), "brood_straha": ("summon/void", 1.0),
        "brood_smorn": ("summon/plasma", 1.0), "brood_ughstein": ("summon/kinetic", 1.0),
        # spells only wild magic casts
        "wild_places": ("blink+stars/phase", 1.0), "wild_second": ("pop+stars", 1.0), "wild_minute": ("mind+stars/time", 1.0),
        "wild_coin": ("coinhit+stars/coin", 2.0), "wild_winter": ("rain/frost", 2.0), "wild_floor": ("quake+embers/fire", 1.5),
        "wild_hiccup": ("pull/gravity", 3.0), "wild_door": ("blink/phase", 1.5), "wild_spectrum": ("explode+stars", 1.0),
        "wild_shrink": ("blink/moon", 1.0), "wild_grow": ("power+rise/mutagen", 1.0), "wild_luck": ("coinhit+swirl/luck", 2.0),
        "wild_glass": ("glyph/ward", 2.0), "wild_postponed": ("glyph+swirl", 1.0), "wild_well": ("heal+stars/water", 2.0),
        "wild_tongue": ("mark/seer", 2.0),
        # the loose spell that wanders the deck (drifter): steady streams only, played on a loop, so nothing in it
        # flashes or pulses. A raw effect, not a recipe.
        "drifter": {"layers": [
            {"texture": "soft", "count": 0, "rate": 14, "duration": 1.0, "life": [0.5, 0.7], "speed": [0, 0.05], "size": [0.45, 0.6],
             "sizeCurve": "shrink", "color": "#FF8AF0", "color2": "#20D0C0", "alpha": 0.4, "shape": "point"},
            {"texture": "soft", "count": 0, "rate": 6, "duration": 1.0, "life": [0.4, 0.6], "speed": [0, 0.02], "size": [0.16, 0.22],
             "sizeCurve": "shrink", "color": "#F0C8FF", "color2": "#FFD15C", "alpha": 0.35, "shape": "point"},
            {"texture": "mote", "count": 0, "rate": 45, "duration": 1.0, "life": [0.6, 1.0], "speed": [0.1, 0.3], "size": [0.05, 0.1],
             "color": "#7CFF6B", "color2": "#C070FF", "shape": "sphere", "radius": 0.35, "orbit": 1.2},
            {"texture": "mote", "count": 0, "rate": 25, "duration": 1.0, "life": [0.6, 1.0], "speed": [0.1, 0.3], "size": [0.05, 0.1],
             "color": "#FFB040", "color2": "#40A0FF", "shape": "sphere", "radius": 0.3, "orbit": -0.9},
            {"texture": "mote", "count": 0, "rate": 30, "duration": 1.0, "life": [0.5, 0.9], "speed": [0, 0.1], "size": [0.06, 0.12],
             "color": "#C070FF", "color2": "#20D0C0", "alpha": 0.7, "shape": "sphere", "radius": 0.1},
        ]},
    },
}


def slugify(s):
    return re.sub(r"[^a-z0-9]+", "_", s.lower()).strip("_")


def classes():
    """class id -> (file, class json, library templates)."""
    lib = json.loads((MODS / "Library" / "core_library.json").read_text(encoding="utf-8"))["abilities"]
    found = {}
    for f in sorted(glob.glob(str(MODS / "*" / "*.json"))):
        if "SkillVfx" in f: continue
        try: d = json.loads(Path(f).read_text(encoding="utf-8"))
        except Exception: continue
        for c in d if isinstance(d, list) else [d]:
            if isinstance(c, dict) and "tiers" in c and c.get("id"): found[c["id"]] = (Path(f), c, lib)
    return found


# ---- generated textures (make_particle_assets.py) ---------------------------------------------------------------------

def sheet(name, **extra):
    """Layer fields for Mods/SkillVfx/fx/<name>.png, or None when it hasn't been made. 128 px cells: the grid comes
    from the PNG's size."""
    f = OUT / "fx" / f"{name}.png"
    if not f.exists(): return None
    w, h = struct.unpack(">II", f.read_bytes()[16:24])
    d = {"texture": f"fx/{name}.png"}
    if w // 128 * (h // 128) > 1: d["grid"] = [w // 128, h // 128]
    d.update(extra)
    return d


def fiery(p):
    """Orange/red deep colour: explosions there are fireballs; other palettes get a tinted magic flare."""
    h, l, s = colorsys.rgb_to_hls(*(int(p[1][i:i + 2], 16) / 255 for i in (1, 3, 5)))
    return (h * 360 < 35 or h * 360 > 345) and s > 0.6


CLS = ""  # the class being built: its own sheets (make_class_particles.py) win over the shared ones


def own(kind, generic, **extra):
    """The class's own <class>_<kind> sheet if it has been made, else the shared one."""
    return sheet(f"{CLS}_{kind}", **extra) or sheet(generic, **extra)


def ai_textures(arch, p, layers):
    """Swaps built-in sprites for generated ones, by what each layer is doing. Layer motion and timing stay; a
    flipbook plays over each particle's life, so flashes that become one get a little longer to read."""
    out = []
    for l in layers:
        t, n = l.get("texture"), l.get("count", 0)
        flash_ = t == "soft" and n == 1 and l.get("sizeCurve") == "pop"
        new = None
        if t == "smoke":
            new = sheet("smoke_puff")
        elif t == "shard" and n > 1 and arch != "frostburst" and l.get("color") != "#F0FBFF":
            new = own("bits", "rubble", randomFrame=True, blend="alpha")
        elif arch == "zap" and t == "star":
            new = sheet("arc_crackle", randomFrame=True, sizeCurve="flat", life=[max(v, 0.2) for v in l["life"]])
        elif arch in ("glyph", "arm") and t == "ring":
            new = sheet("sigil", randomFrame=True)
        elif arch == "orb" and t == "soft" and l.get("sizeCurve") == "flat":
            new = sheet("plasma_orb")
        elif arch == "flame" and t == "soft" and n > 1:
            new = sheet("flame_lick", color="#FFFFFF", color2=l.get("color2"), sizeCurve="flat")
        elif arch in ("explode", "blastzone", "nova") and flash_:
            life, size = [max(0.7, v) for v in l["life"]], [round(v * 1.3, 3) for v in l["size"]]
            new = (sheet("fire_blast", color="#FFFFFF", color2="#FFFFFF", alpha=1, sizeCurve="flat", life=life, size=size)
                   if fiery(p) else own("burst", "magic_flare", sizeCurve="flat", alpha=1, life=life, size=size))
        elif arch in ("burst", "smash", "heavy", "pierce", "pop") and flash_:
            new = own("burst", "flash_burst", sizeCurve="flat", life=[max(0.4, v) for v in l["life"]])
        out.append({**l, **new} if new else l)
    return out


def build(cid, f, cls, lib, pal_name, recipes, problems):
    global CLS
    effects, tiers = [], []
    short = slugify(cls["name"])
    CLS = short
    for i, t in enumerate(cls["tiers"]):
        a = t.get("newAbility")
        if i not in recipes:
            tiers.append({})
            if a and a.get("base") != "Passive" and not a.get("passive") and ({**lib.get(a.get("base"), {}), **a}.get("target") or a.get("actions")):
                problems.append(f"{cid} tier {i} ({a.get('name')}): active skill with no recipe")
            continue
        if not a: problems.append(f"{cid} tier {i}: recipe but no ability"); tiers.append({}); continue
        m = {**lib.get(a.get("base"), {}), **a}
        r = float(m.get("radius") or 0) or 1.5
        patch = {k: "" for k in ROLE_KEY.values()}
        for tok in recipes[i].split():
            role, spec = tok.split("=", 1)
            pal = pal_name
            if "/" in spec: spec, pal = spec.split("/", 1)
            arch, *extras = spec.split("+")
            if role not in ROLE_KEY: problems.append(f"{cid} {a['name']}: unknown role {role}"); continue
            if arch not in ARCH: problems.append(f"{cid} {a['name']}: unknown archetype {arch}"); continue
            if pal not in PAL: problems.append(f"{cid} {a['name']}: unknown palette {pal}"); continue
            if role == "travel" and m.get("target") not in ("enemy", "floor"): problems.append(f"{cid} {a['name']}: travel on a {m.get('target')} skill")
            if role == "area" and not (m.get("radius") or m.get("target") == "floor"): problems.append(f"{cid} {a['name']}: area on a skill with no area")
            fid = f"fx_{short}_{slugify(a['name'])}_{role}"
            rng.seed(fid)
            fx = ARCH[arch](PAL[pal], r)
            for e in extras:
                if e not in EXTRA: problems.append(f"{cid} {a['name']}: unknown extra {e}"); continue
                fx["layers"].append(EXTRA[e](PAL[pal], r))
            fx["layers"] = ai_textures(arch, PAL[pal], fx["layers"])
            entry = {"id": fid, "layers": [{k: v for k, v in l.items() if v is not None} for l in fx["layers"]]}
            if fx.get("light"): entry["light"] = fx["light"]
            effects.append(entry)
            patch[ROLE_KEY[role]] = fid
        tiers.append({"newAbility": patch})
    for key, val in NAMED.get(cid, {}).items():
        if isinstance(val, dict):
            effects.append({"id": f"fx_{short}_{key}", **val})
            continue
        recipe, r = val
        spec, pal = (recipe.split("/", 1) + [pal_name])[:2]
        arch, *extras = spec.split("+")
        if arch not in ARCH or pal not in PAL: problems.append(f"{cid} {key}: unknown archetype or palette in {recipe}"); continue
        fid = f"fx_{short}_{key}"
        rng.seed(fid)
        fx = ARCH[arch](PAL[pal], r)
        for e in extras:
            if e in EXTRA: fx["layers"].append(EXTRA[e](PAL[pal], r))
            else: problems.append(f"{cid} {key}: unknown extra {e}")
        fx["layers"] = ai_textures(arch, PAL[pal], fx["layers"])
        entry = {"id": fid, "layers": [{k: v for k, v in l.items() if v is not None} for l in fx["layers"]]}
        if fx.get("light"): entry["light"] = fx["light"]
        effects.append(entry)
    while tiers and tiers[-1] == {}: tiers.pop()
    return effects, {"id": cid, "tiers": tiers}


def dump(effects, patch):
    """One effect per block, one layer per line."""
    c = lambda o: json.dumps(o, separators=(", ", ": "))
    out = ['[ { "particles": [']
    for n, e in enumerate(effects):
        out.append(f'  {{ "id": "{e["id"]}",' + (f' "light": {c(e["light"])},' if e.get("light") else ""))
        out.append('    "layers": [')
        out.append(",\n".join("      " + c(l) for l in e["layers"]))
        out.append("    ] }" + ("," if n < len(effects) - 1 else ""))
    out.append("] },")
    out.append('{ "id": "%s", "tiers": [' % patch["id"])
    out.append(",\n".join("  " + c(t) for t in patch["tiers"]))
    out.append("] } ]")
    return "\n".join(out) + "\n"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--check", action="store_true", help="report problems without writing")
    a = ap.parse_args()
    only = {s for s in a.only.split(",") if s}
    found = classes()
    problems, total = [], 0
    for cid in found:
        if cid not in RECIPES and (not only or cid in only): problems.append(f"{cid}: no recipes")
    OUT.mkdir(parents=True, exist_ok=True)
    for cid, (pal, recipes) in RECIPES.items():
        if only and cid not in only: continue
        if cid not in found: problems.append(f"{cid}: class not found"); continue
        f, cls, lib = found[cid]
        effects, patch = build(cid, f, cls, lib, pal, recipes, problems)
        total += len(effects)
        if not a.check:
            (OUT / f"{slugify(cls['name'])}.json").write_text(dump(effects, patch), encoding="utf-8")
    print(f"{total} effects for {len(only) or len(RECIPES)} classes")
    for p in problems: print("  !", p)


if __name__ == "__main__":
    main()
