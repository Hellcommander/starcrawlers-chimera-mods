"""Fixes on finished, animated summon glbs, edited in place in the glb so the skin and clips stay as they are:
  plate    - drops the flat floor plate TRELLIS sometimes bakes from the concept's ground shadow: floor-level, face-up
             triangles that lie away from every foot (the skin drags it around in big clips)
  match=<concept.png>:K - regrades the colour map toward the concept (see match()), for bodies the repair's luma
             floor washed out to flat grey
  project=<concept.png>:K[:-1] - paints the concept back onto the front of the body (see project())
  accent=<concept.png>:F - re-lights the concept's glow colour on up to F of the body (see accent())
A .glb.bak of the first original is kept. Usage: python touch_up_summons.py <glb> plate match=item_art/x.png:0.6"""
import io, json, shutil, struct, sys
from pathlib import Path

import numpy as np
from PIL import Image


def load(p):
    b = Path(p).read_bytes()
    n = struct.unpack_from("<I", b, 12)[0]
    g = json.loads(b[20:20 + n])
    return g, bytearray(b[20 + n + 8:20 + n + 8 + struct.unpack_from("<I", b, 20 + n)[0]])


def save(p, g, views):
    """views: the new bytes of every bufferView, in order; the binary chunk is rebuilt from them."""
    out = bytearray()
    for v, raw in zip(g["bufferViews"], views):
        while len(out) % 4: out.append(0)
        v["byteOffset"], v["byteLength"] = len(out), len(raw)
        out += raw
    while len(out) % 4: out.append(0)
    g["buffers"][0]["byteLength"] = len(out)
    js = json.dumps(g, separators=(",", ":")).encode()
    js += b" " * (-len(js) % 4)
    Path(p).write_bytes(struct.pack("<III", 0x46546C67, 2, 28 + len(js) + len(out)) + struct.pack("<I4s", len(js), b"JSON") + js
                        + struct.pack("<I4s", len(out), b"BIN\0") + out)


def accessor(g, views, i):
    a = g["accessors"][i]
    dt = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}[a["componentType"]]
    k = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[a["type"]]
    return np.frombuffer(bytes(views[a["bufferView"]]), dt, a["count"] * k, a.get("byteOffset", 0)).reshape(-1, k)


def drop_plate(g, views):
    log = []
    for m in g["meshes"]:
        for pr in m["primitives"]:
            P = accessor(g, views, pr["attributes"]["POSITION"])
            ia = g["accessors"][pr["indices"]]
            I = accessor(g, views, pr["indices"]).reshape(-1, 3)
            T = P[I]
            y0 = P[:, 1].min()
            n = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0])
            n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
            floor = T[:, :, 1].max(1) < y0 + 0.02
            # feet: what stands just above the floor; the plate is floor reaching out where no foot stands
            feet = P[(P[:, 1] > y0 + 0.02) & (P[:, 1] < y0 + 0.08)][:, [0, 2]]
            if len(feet):
                d = np.array([np.min(np.linalg.norm(feet - x, axis=1)) for x in T[:, :, [0, 2]].reshape(-1, 2)]).reshape(-1, 3)
                far = d.max(1) > 0.025
            else:
                far = np.ones(len(T), bool)
            drop = floor & (far | (np.abs(n[:, 1]) > 0.85) & (d.mean(1) > 0.015) if len(feet) else far)
            # and the slivers the cut leaves floating (vertices welded by position: TRELLIS splits them at UV seams)
            import scipy.sparse as sp, scipy.sparse.csgraph as cg
            _, w = np.unique(np.round(P, 4), axis=0, return_inverse=True)
            J = w.ravel()[I][~drop]
            e = np.concatenate([J[:, [0, 1]], J[:, [1, 2]]])
            _, lab = cg.connected_components(sp.coo_matrix((np.ones(len(e)), (e[:, 0], e[:, 1])), shape=(w.max() + 1,) * 2), directed=False)
            fl = lab[J[:, 0]]
            small = np.bincount(fl)[fl] < 0.002 * len(I)
            idx = np.flatnonzero(~drop)
            drop[idx[small]] = True
            keep = I[~drop].astype(np.dtype(accessor(g, views, pr["indices"]).dtype))
            views[ia["bufferView"]] = bytearray(keep.tobytes())
            ia["byteOffset"] = 0
            ia["count"] = int(keep.size)
            log.append(f"plate: dropped {int(drop.sum())} of {len(I)} triangles")
    return log


def match(g, views, concept, k, floor=45.0):
    """Regrades the colour map toward the concept: brightness mapped onto the concept's range (rank for rank, never
    below `floor`, so a dark body doesn't read as a hole), then each pixel's colour pulled by `k` toward the concept's
    colour at the same brightness rank (its furnace glow, its magenta screens), keeping the texture's own detail."""
    W = np.array([0.299, 0.587, 0.114], np.float32)
    c = np.asarray(Image.open(concept).convert("RGB")).astype(np.float32).reshape(-1, 3)
    c = c[c.min(1) < 225]  # the object, not the white background
    c = c[np.argsort(c @ W)]
    cl = c @ W
    log = []
    for img in g.get("images", []):
        vi = img["bufferView"]
        a = np.asarray(Image.open(io.BytesIO(bytes(views[vi]))).convert("RGB")).astype(np.float32)
        sh = a.shape
        a = a.reshape(-1, 3)
        luma = a @ W
        used = a.max(1) > 5  # the atlas's empty background
        before = float(np.median(luma[used]))
        rank = np.zeros(len(a))
        rank[np.flatnonzero(used)[np.argsort(luma[used])]] = np.linspace(0, 1, used.sum())
        at = np.minimum((rank * (len(c) - 1)).astype(int), len(c) - 1)
        # the concept's colour at that rank, smoothed over a window so single pixels don't speckle
        win = max(1, len(c) // 200)
        cs = np.cumsum(np.vstack([np.zeros((1, 3)), c]), 0)
        lo, hi = np.clip(at - win, 0, len(c) - 1), np.clip(at + win, 0, len(c) - 1) + 1
        ref = (cs[hi] - cs[lo]) / (hi - lo)[:, None]
        tl = np.maximum(ref @ W, floor)
        out = a * (tl / np.maximum(luma, 1))[:, None]  # the texture's own shading at the concept's brightness
        out = (1 - k) * out + k * ref * (tl / np.maximum(ref @ W, 1))[:, None]
        out = np.where(used[:, None], out, a)
        a = np.clip(out, 0, 255).reshape(sh)
        buf = io.BytesIO()
        Image.fromarray(a.astype(np.uint8)).save(buf, "JPEG" if img.get("mimeType") == "image/jpeg" else "PNG", quality=90)
        views[vi] = bytearray(buf.getvalue())
        log.append(f"match {Path(concept).name} k={k}: median luma {before:.0f} -> {float(np.median((a.reshape(-1, 3) @ W)[used])):.0f}")
        break  # the colour map; any other image is a metal/roughness map
    return log


def accent(g, views, concept, frac):
    """Re-lights the concept's glow colour (furnace belly, magenta screens) that the bake left dull: the concept's
    accent is its most common bright, saturated hue; the texels of the colour map closest to that hue (at most `frac`
    of the body, the most saturated first) are pushed to the accent's colour and brightness, keeping their shading."""
    from PIL import ImageFilter
    ch = np.asarray(Image.open(concept).convert("RGB").convert("HSV")).reshape(-1, 3).astype(np.float32)
    cr = np.asarray(Image.open(concept).convert("RGB")).reshape(-1, 3).astype(np.float32)
    hot = (ch[:, 1] > 120) & (ch[:, 2] > 150)
    if hot.sum() < 50: return ["accent: the concept has no glow colour"]
    hue = np.bincount((ch[hot, 0] // 8).astype(int), minlength=32).argmax() * 8 + 4
    col = np.median(cr[hot & (np.abs(ch[:, 0] - hue) < 12)], 0)
    log = []
    for img in g.get("images", []):
        vi = img["bufferView"]
        im = Image.open(io.BytesIO(bytes(views[vi]))).convert("RGB")
        a = np.asarray(im).astype(np.float32)
        h = np.asarray(im.convert("HSV")).astype(np.float32)
        dh = np.minimum(np.abs(h[..., 0] - hue), 256 - np.abs(h[..., 0] - hue))
        used = a.max(-1) > 5
        score = np.where(used & (dh < 20) & (h[..., 1] > 40), h[..., 1] * (1 - dh / 20) * (0.3 + h[..., 2] / 255), 0)
        n = int(frac * used.sum())
        if n == 0 or not (score > 0).any(): log.append("accent: no texels near the glow hue"); break
        cut = np.sort(score[score > 0])[::-1][min(n, (score > 0).sum()) - 1]
        m = Image.fromarray(((score >= cut) & (score > 0)).astype(np.uint8) * 255).filter(ImageFilter.GaussianBlur(1.5))
        w = (np.asarray(m).astype(np.float32) / 255)[..., None]
        shade = (a.max(-1, keepdims=True) / 255) ** 0.5  # the texel's own shading, lifted
        lit = np.clip(col * (0.55 + 0.6 * shade), 0, 255)
        a = a * (1 - w) + lit * w
        buf = io.BytesIO()
        Image.fromarray(a.astype(np.uint8)).save(buf, "JPEG" if img.get("mimeType") == "image/jpeg" else "PNG", quality=90)
        views[vi] = bytearray(buf.getvalue())
        log.append(f"accent hue {hue:.0f} colour {col.astype(int).tolist()}: lit {int((w[..., 0] > 0.5).sum())} texels")
        break
    return log


def project(g, views, concept, k, front=1.0):
    """Paints the concept back onto the front of the body: every texel of the colour map is placed on the mesh (rest
    pose, through its UVs), and texels facing the viewer (+Z, or -Z with front=-1) take the concept's colour at that
    point of the front view by `k`, fading out as they turn away. For glows and markings the bake lost entirely; the
    concept is a front view, so it lines up with the body the image-to-3D model built from it."""
    log = []
    pr = g["meshes"][0]["primitives"][0]
    P = accessor(g, views, pr["attributes"]["POSITION"]).astype(np.float64)
    UV = accessor(g, views, pr["attributes"]["TEXCOORD_0"]).astype(np.float64)
    I = accessor(g, views, pr["indices"]).reshape(-1, 3)
    img = g["images"][g["textures"][g["materials"][pr.get("material", 0)]["pbrMetallicRoughness"]["baseColorTexture"]["index"]]["source"]]
    vi = img["bufferView"]
    im = Image.open(io.BytesIO(bytes(views[vi]))).convert("RGB")
    W_, H_ = im.size
    a = np.asarray(im).astype(np.float32).copy()
    T = P[I]
    n = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0])
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-12
    facing = n[:, 2] * front
    # the concept's object box onto the body's front-view box
    c = Image.open(concept).convert("RGB")
    ca = np.asarray(c).astype(np.float32)
    ys, xs = np.nonzero(ca.min(-1) < 225)
    cx0, cx1, cy0, cy1 = xs.min(), xs.max(), ys.min(), ys.max()
    lo, hi = P.min(0), P.max(0)
    def to_concept(q):  # body point -> concept pixel; seen from the front, +x is right when front=+1
        u = (q[:, 0] - lo[0]) / (hi[0] - lo[0])
        u = u if front > 0 else 1 - u
        v = 1 - (q[:, 1] - lo[1]) / (hi[1] - lo[1])
        return cx0 + u * (cx1 - cx0), cy0 + v * (cy1 - cy0)
    acc = np.zeros((H_, W_, 3)); wsum = np.zeros((H_, W_))
    for t in np.flatnonzero(facing > 0.15):
        uv = UV[I[t]] * [W_, H_]
        x0, y0 = np.floor(uv.min(0)).astype(int); x1, y1 = np.ceil(uv.max(0)).astype(int)
        x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, W_ - 1), min(y1, H_ - 1)
        if x1 < x0 or y1 < y0: continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        A = np.array([[uv[0, 0] - uv[2, 0], uv[1, 0] - uv[2, 0]], [uv[0, 1] - uv[2, 1], uv[1, 1] - uv[2, 1]]])
        if abs(np.linalg.det(A)) < 1e-9: continue
        l = np.linalg.solve(A, np.stack([gx.ravel() - uv[2, 0], gy.ravel() - uv[2, 1]]))
        b = np.vstack([l, 1 - l.sum(0)]).T
        inside = (b >= -0.02).all(1)
        if not inside.any(): continue
        q = b[inside] @ T[t]
        px, py = to_concept(q)
        col = ca[np.clip(py.astype(int), 0, ca.shape[0] - 1), np.clip(px.astype(int), 0, ca.shape[1] - 1)]
        tx, ty = gx.ravel()[inside].astype(int), gy.ravel()[inside].astype(int)
        w = min(1.0, (facing[t] - 0.15) / 0.45)
        np.add.at(acc, (ty, tx), col * w); np.add.at(wsum, (ty, tx), w)
    m = wsum > 0
    painted = acc[m] / wsum[m][:, None]
    keep = painted.min(1) < 225  # the concept's white background is not a colour to paint
    idx = tuple(x[keep] for x in np.nonzero(m))
    w = (k * np.minimum(wsum[m][keep], 1))[:, None]
    a[idx] = a[idx] * (1 - w) + painted[keep] * w
    buf = io.BytesIO()
    Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).save(buf, "JPEG" if img.get("mimeType") == "image/jpeg" else "PNG", quality=90)
    views[vi] = bytearray(buf.getvalue())
    log.append(f"project {Path(concept).name} k={k}: painted {int(keep.sum())} texels from the front")
    return log


def main():
    args = sys.argv[1:]
    glb = Path(args.pop(0))
    bak = glb.with_suffix(".glb.bak")
    if not bak.exists(): shutil.copy2(glb, bak)
    g, blob = load(bak)  # always from the original, so re-running with other settings doesn't stack
    views = [blob[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]] for v in g["bufferViews"]]
    for op in args:
        if op == "plate": print("\n".join(drop_plate(g, views)))
        elif op.startswith("match="):
            png, k = op[6:].rsplit(":", 1)
            print("\n".join(match(g, views, png, float(k))))
        elif op.startswith("accent="):
            png, f = op[7:].rsplit(":", 1)
            print("\n".join(accent(g, views, png, float(f))))
        elif op.startswith("project="):  # project=<png>:K[:-1]
            parts = op[8:].split(":")
            front = float(parts.pop()) if len(parts) > 2 and parts[-1] in ("1", "-1") else 1.0
            png, k = ":".join(parts[:-1]), float(parts[-1])
            print("\n".join(project(g, views, png, k, front)))
        else: sys.exit(f"unknown op {op}")
    save(glb, g, views)
    print(f"wrote {glb}")


if __name__ == "__main__":
    main()
