"""Closes the middle of the Looking-Glass burrow with a dark disc, so the shaft reads as a drop and not as the deck.
TRELLIS leaves the hole open. The disc joins the mesh and uses the darkest spot of its texture, painted black.
Usage: python cap_rabbit_hole.py in.glb out.glb"""
import sys
import numpy as np
import trimesh
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]
m = trimesh.load(src, force="mesh", process=False)
tex = m.visual.material.baseColorTexture.convert("RGB")
px = np.asarray(tex).astype(float).sum(axis=2)
y, x = np.unravel_index(np.argmin(px), px.shape)
h, w = px.shape
painted = tex.copy()
painted.paste((0, 0, 0), (max(0, x - 3), max(0, y - 3), min(w, x + 4), min(h, y + 4)))
m.visual.material.baseColorTexture = painted
uv = np.array([x / (w - 1), 1 - y / (h - 1)])

v = m.vertices
mid = (v.max(axis=0) + v.min(axis=0)) / 2
tall = v[:, 1].max() - v[:, 1].min()
radial = np.hypot(v[:, 0] - mid[0], v[:, 2] - mid[2])
inner = radial.min()
print(f"hole radius {inner:.3f}, height {tall:.3f}, darkest texel {px.min() / 3:.0f}")
n = 48
a = np.linspace(0, 2 * np.pi, n, endpoint=False)
r = inner * 1.15
floor = v[:, 1].min() + tall * 0.08
disc = np.vstack([[mid[0], floor, mid[2]], np.c_[mid[0] + r * np.cos(a), np.full(n, floor), mid[2] + r * np.sin(a)]])
base = len(v)
faces = np.array([[base, base + 1 + (i + 1) % n, base + 1 + i] for i in range(n)])
out = trimesh.Trimesh(np.vstack([v, disc]), np.vstack([m.faces, faces]), process=False,
                      visual=trimesh.visual.TextureVisuals(uv=np.vstack([m.visual.uv, np.tile(uv, (n + 1, 1))]), material=m.visual.material))
out.export(dst)
print("wrote", dst)
