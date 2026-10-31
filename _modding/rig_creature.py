"""Blender (headless): rigs a generated creature mesh and keyframes its clips, for the class loader's skinned .glb models.
  blender -b --factory-startup -P rig_creature.py -- <in.glb> <out.glb> <plan> [preview.png]
Plans (options after a colon):
  upright[:noarms] - two-legged: thigh and shin per leg, pelvis/chest/head, arms found sticking out of the torso
  crawler[:quad][:flip][:headright] - many-legged along its length: a leg bone per leg found on each side (three per side, or two
                     for a four-legged runner with :quad) and a spine from tail to head
  hover            - floating: a body, fins/arms found at its sides, and anything dangling underneath
  serpent          - a worm lying along its length: an eight-bone spine from tail to head
  prop             - only normalised (standing on 0, longest side 1), no rig
  <plan>:norig     - any plan's clean-up and orientation, then normalised with no rig (for the AAMT animation pipeline)
  <plan>:rx-90:rz180 - turns by hand (degrees about X, Y, Z, in order) before the automatic orientation
Limbs come from the mesh itself: legs are what reaches the floor on each side, arms what sticks out past the torso.
Weights blend smoothly between a limb and the body at its joint, so any generated mesh deforms without tearing.
Clips (30 fps): idle, idle2 (looks around), move (a real gait), attack, attack2, hit, death, special (wind-up).
The mesh is normalised: feet at 0, centred, longest side 1 unit; front faces -Y (glTF +Z, Unity +Z)."""
import math, sys
import bpy
import numpy as np
from mathutils import Vector, Quaternion, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT, PLAN = argv[0], argv[1], argv[2]
BODY, *OPTS = PLAN.split(":")
FPS = 30


def log(*a):
    print("RIG:", *a, flush=True)


# ---- scene and mesh -----------------------------------------------------------------------------------------------

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = FPS
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
if not meshes:
    raise SystemExit("no mesh in " + SRC)
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o != body:
        bpy.data.objects.remove(o, do_unlink=True)
me = body.data

# image-to-3D sometimes rebuilds the concept's soft floor shadow as a ground plate: a separate, wafer-thin, wide piece
# at the very bottom. Drop it (feet are thick or attached, so they stay).
import bmesh
bm = bmesh.new()
bm.from_mesh(me)
bm.verts.ensure_lookup_table()
allz = [v.co.z for v in bm.verts]
zlo, ztot = min(allz), max(allz) - min(allz)
xyspan = max(max(v.co.x for v in bm.verts) - min(v.co.x for v in bm.verts), max(v.co.y for v in bm.verts) - min(v.co.y for v in bm.verts))
seen, plates = set(), []
for v0 in bm.verts:
    if v0.index in seen:
        continue
    island, stack = [], [v0]
    seen.add(v0.index)
    while stack:
        v = stack.pop()
        island.append(v)
        for e in v.link_edges:
            o = e.other_vert(v)
            if o.index not in seen:
                seen.add(o.index)
                stack.append(o)
    zs_ = [v.co.z for v in island]
    span = max(max(v.co.x for v in island) - min(v.co.x for v in island), max(v.co.y for v in island) - min(v.co.y for v in island))
    if max(zs_) - min(zs_) < 0.04 * ztot and min(zs_) < zlo + 0.08 * ztot and span > 0.25 * xyspan:
        plates.append(island)
if plates:
    bmesh.ops.delete(bm, geom=[v for isl in plates for v in isl], context="VERTS")
    bm.to_mesh(me)
    me.update()
print(f"RIG: dropped {len(plates)} ground plate(s)", flush=True)
# a solid slab under the creature: its bottom 5% is far denser or wider than the band above. Slice it off.
if BODY != "hover":
    zs_ = np.array([v.co.z for v in bm.verts])
    xs_ = np.array([v.co.x for v in bm.verts])
    ys_ = np.array([v.co.y for v in bm.verts])
    zlo, ztot = float(zs_.min()), float(np.ptp(zs_))
    b0 = zs_ < zlo + 0.05 * ztot
    b1 = (zs_ >= zlo + 0.05 * ztot) & (zs_ < zlo + 0.10 * ztot)
    def footprint(m):
        return float(np.ptp(xs_[m])) * float(np.ptp(ys_[m])) if m.sum() > 3 else 0.0
    if b0.sum() > 3 * max(1, b1.sum()) or footprint(b0) > 2.5 * max(1e-6, footprint(b1)):
        cut = zlo + 0.055 * ztot
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, cut), plane_no=(0, 0, 1), clear_inner=True)
        bm.to_mesh(me)
        me.update()
        bm.verts.ensure_lookup_table()
        print(f"RIG: sliced off a base slab (bottom band {int(b0.sum())} vertices vs {int(b1.sum())} above)", flush=True)
# a plate fused to the feet: in the bottom band, faces lying flat and facing up are its top (soles and bellies face down)
if BODY != "hover":
    bm.faces.ensure_lookup_table()
    allz = [v.co.z for v in bm.verts]
    zlo, ztot = min(allz), max(allz) - min(allz)
    flat = [f for f in bm.faces if f.normal.z > 0.85 and max(v.co.z for v in f.verts) < zlo + 0.07 * ztot]
    area = sum(f.calc_area() for f in flat)
    total = sum(f.calc_area() for f in bm.faces) or 1.0
    if area > 0.04 * total:  # only a real plate, not the soles of a few feet
        bmesh.ops.delete(bm, geom=flat, context="FACES")
        loose = [v for v in bm.verts if not v.link_faces]
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
        bm.to_mesh(me)
        me.update()
        print(f"RIG: cut a fused ground plate ({len(flat)} faces, {area / total:.0%} of the surface)", flush=True)
bm.free()


def coords():
    return np.array([v.co[:] for v in me.vertices])


# rx<deg> / ry<deg> / rz<deg>: turns given by hand, in that order, when the automatic ones below can't tell (a crab
# drawn face-on comes back standing on its edge, but its spread legs make it as deep as it is tall). They imply notip.
for o in OPTS:
    if len(o) > 2 and o[0] == "r" and o[1] in "xyz":
        me.transform(Matrix.Rotation(math.radians(float(o[2:])), 4, o[1].upper()))
        log(f"turned {o[2:]} degrees about {o[1].upper()}")
        OPTS.append("notip")
co = coords()
if BODY in ("crawler", "serpent"):
    # Image-to-3D reads a creature drawn from above at an angle as one standing up to face the camera: tip it
    # forward to whatever pitch lies flattest, but only when that's much flatter (a hound really stands).
    def height(deg):
        a = math.radians(deg)
        return float(np.ptp(co[:, 1] * math.sin(a) + co[:, 2] * math.cos(a)))
    h0 = height(0)
    best = min(range(-90, 1, 5), key=height)
    tipped = height(best) < h0 * 0.65 and "notip" not in OPTS  # notip: the mesh already stands the right way up
    if tipped:
        me.transform(Matrix.Rotation(math.radians(best), 4, "X"))
        log(f"crawler: tipped {best} degrees (height {h0:.2f} -> {height(best):.2f})")
    co = coords()
    # and level it side to side: a slight roll about the length makes one side's legs look like body
    def roll_height(deg):
        a = math.radians(deg)
        return float(np.ptp(-co[:, 0] * math.sin(a) + co[:, 2] * math.cos(a)))
    r0 = roll_height(0)
    rbest = min(range(-35, 36, 5), key=roll_height)
    if rbest != 0 and roll_height(rbest) < r0 * 0.9:
        me.transform(Matrix.Rotation(math.radians(rbest), 4, "Y"))
        log(f"crawler: levelled {rbest} degrees (height {r0:.2f} -> {roll_height(rbest):.2f})")
    co = coords()
    # legs go down: the half that splays wider across is the underside (a shell or back is narrower on top)
    zmid = (co[:, 2].min() + co[:, 2].max()) / 2
    def spread(mask):
        return float(np.percentile(np.abs(co[mask, 0] - np.median(co[:, 0])), 95)) if mask.sum() > 20 else 0.0
    top, bottom = spread(co[:, 2] > zmid), spread(co[:, 2] <= zmid)
    if BODY == "crawler" and (("flip" in OPTS) or (tipped and top > bottom * 1.3 and "noflip" not in OPTS)):
        me.transform(Matrix.Rotation(math.pi, 4, "Y"))
        log(f"crawler: rolled over, legs down (spread top {top:.2f} > bottom {bottom:.2f})")
    co = coords()
    # centred on its mass across, so left and right legs split evenly
    me.transform(Matrix.Translation((-float(np.median(co[:, 0])), 0, 0)))
    co = coords()
    # lies along Y: a creature longer across X turns a quarter
    if np.ptp(co[:, 0]) > np.ptp(co[:, 1]) * 1.15 and "noturn" not in OPTS:  # noturn: drawn from the front, wide on purpose
        # drawn side on: image-to-3D mirrors the picture, so a head on the image's left lands at +X ("headright": -X);
        # either way it is turned to -Y
        me.transform(Matrix.Rotation(math.radians(90 if "headright" in OPTS else -90), 4, "Z"))
        co = coords()
lo, hi = co.min(0), co.max(0)
k = 1.0 / float((hi - lo).max())
centre = Vector((0.0 if BODY in ("crawler", "serpent") else (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2]))  # crawlers stay centred on their mass
me.transform(Matrix.Scale(k, 4) @ Matrix.Translation(-centre))
me.update()
co = coords()
X, Y, Z = co[:, 0], co[:, 1], co[:, 2]
lo, hi = co.min(0), co.max(0)
W, L, H = float(hi[0] - lo[0]), float(hi[1] - lo[1]), float(hi[2] - lo[2])
NV = len(co)
log(f"{PLAN}: normalised to {W:.2f} x {L:.2f} x {H:.2f}, {NV} vertices")
if BODY == "prop" or "norig" in OPTS:  # norig: cleaned and oriented for a learned rigger (anim_pipeline.py)
    bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", export_draco_mesh_compression_enable=False)
    log("wrote", OUT)
    raise SystemExit(0)


def ramp(v, a, b):
    """0 at a, 1 at b (either direction), smooth."""
    t = np.clip((v - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


# ---- skeleton plan: bones (head, tail, parent) and per-vertex weights ---------------------------------------------

bones = {}      # name -> (head, tail, parent)
weights = {}    # name -> np.array(NV)
order = []


def bone(name, head, tail, parent=None):
    bones[name] = (Vector(head), Vector(tail), parent)
    order.append(name)


def chain_weights(names, along, centres, mask):
    """Blend the masked vertices along a chain by a coordinate, between neighbouring bone centres."""
    n = len(names)
    for nm in names:
        weights.setdefault(nm, np.zeros(NV))
    if n == 1:
        weights[names[0]] += mask
        return
    t = along
    for i, nm in enumerate(names):
        c = centres[i]
        lo_c = centres[i - 1] if i > 0 else -1e9
        hi_c = centres[i + 1] if i < n - 1 else 1e9
        w = np.where(t <= c, ramp(t, lo_c, c) if i > 0 else 1.0, 1.0 - ramp(t, c, hi_c) if i < n - 1 else 1.0)
        weights[nm] += w * mask


legs, arms, fins = [], [], []
limb_mask = np.zeros(NV)

if BODY == "upright":
    hip = 0.42 * H
    knee = 0.2 * H
    gap = 0.03 * W
    bone("root", (0, 0, 0), (0, 0, 0.08 * H))
    bone("pelvis", (0, 0, hip), (0, 0, 0.62 * H), "root")
    bone("chest", (0, 0, 0.62 * H), (0, 0, 0.82 * H), "pelvis")
    bone("head", (0, 0, 0.82 * H), (0, 0, 1.0 * H), "chest")
    below = Z < hip
    for side, sgn in (("L", 1), ("R", -1)):
        sel = below & (X * sgn > gap)
        if sel.sum() < NV * 0.02:
            continue
        xs = float(np.median(np.abs(X[sel]))) * sgn
        yc = float(np.median(Y[sel]))
        bone(f"thigh{side}", (xs, yc, hip), (xs, yc, knee), "pelvis")
        bone(f"shin{side}", (xs, yc, knee), (xs, yc, 0.0), f"thigh{side}")
        legs.append(side)
        member = ramp(X * sgn, gap * 0.3, gap * 1.6) * (1 - ramp(Z, hip - 0.05 * H, hip + 0.06 * H))
        thigh = member * ramp(Z, knee - 0.05 * H, knee + 0.05 * H)
        shin = member - thigh
        weights[f"thigh{side}"] = thigh
        weights[f"shin{side}"] = shin
        limb_mask += member
    if "noarms" not in OPTS:
        zone = (Z > 0.45 * H) & (Z < 0.9 * H)
        tw = float(np.percentile(np.abs(X[zone]), 55)) if zone.any() else W / 4
        for side, sgn in (("L", 1), ("R", -1)):
            sel = zone & (X * sgn > tw * 1.2)
            if sel.sum() < NV * 0.015:
                continue
            ax = np.abs(X[sel])
            sh = (float(np.percentile(ax, 15)) * sgn, float(np.median(Y[sel])), float(np.percentile(Z[sel], 92)))
            hd = (float(np.percentile(ax, 70)) * sgn, float(np.median(Y[sel])), float(np.percentile(Z[sel], 8)))
            bone(f"arm{side}", sh, hd, "chest")
            arms.append(side)
            member = ramp(X * sgn, tw * 0.95, tw * 1.25) * ramp(Z, 0.38 * H, 0.46 * H) * (1 - ramp(Z, 0.9 * H, 0.97 * H)) * (1 - np.clip(limb_mask, 0, 1))
            weights[f"arm{side}"] = member
            limb_mask += member
    rest = np.clip(1 - limb_mask, 0, 1)
    chain_weights(["pelvis", "chest", "head"], Z, [0.5 * H, 0.72 * H, 0.92 * H], rest)

elif BODY == "crawler":
    quad = "quad" in OPTS
    legz = 0.42 * H
    bodyz = float(np.median(Z[Z > legz])) if (Z > legz).any() else 0.6 * H
    ys = [hi[1] - L * f for f in (0.0, 0.3, 0.55, 0.78, 1.0)]  # tail (+Y) to head (-Y)
    bone("root", (0, 0, 0), (0, 0, 0.08 * H))
    spine = []
    for i in range(4):
        bone(f"spine{i}", (0, ys[i], bodyz), (0, ys[i + 1], bodyz), "root" if i == 0 else f"spine{i - 1}")
        spine.append(f"spine{i}")
    lat = 0.0 if quad else 0.28 * (W / 2)
    per_side = 2 if quad else 3
    for side, sgn in (("L", 1), ("R", -1)):
        sel = (Z < legz) & (X * sgn > lat)
        log(f"legs {side}: {int(sel.sum())} vertices")
        if sel.sum() < NV * 0.006:
            continue
        edges = np.quantile(Y[sel], np.linspace(0, 1, per_side + 1))
        for j in range(per_side):
            g = sel & (Y >= edges[j]) & (Y <= edges[j + 1])
            if g.sum() < NV * 0.004:
                continue
            ax = np.abs(X[g])
            yc = float(np.median(Y[g]))
            hipx = (float(np.percentile(ax, 10)) if not quad else float(np.median(ax))) * sgn
            footx = (float(np.percentile(ax, 92)) if not quad else float(np.median(ax))) * sgn
            name = f"leg{side}{j}"
            bone(name, (hipx, yc, min(float(Z[g].max()), bodyz)), (footx, yc, float(Z[g].min())), spine[min(3, max(0, int((hi[1] - yc) / L * 4)))])
            legs.append((name, sgn, j, yc))
            ylo, yhi = edges[j], edges[j + 1]
            pad = (yhi - ylo) * 0.15
            member = ((Y >= ylo - pad) & (Y <= yhi + pad)).astype(float) * ramp(X * sgn, lat * 0.6, lat + 0.03) * (1 - ramp(Z, legz - 0.06 * H, legz + 0.04 * H))
            member *= (1 - np.clip(limb_mask, 0, 1))
            weights[name] = member
            limb_mask += member
    rest = np.clip(1 - limb_mask, 0, 1)
    along = (hi[1] - Y) / L
    chain_weights(spine, along, [0.15, 0.42, 0.66, 0.89], rest)

elif BODY == "serpent":
    zc = float(np.median(Z))
    n = 8
    ys = [hi[1] - L * i / n for i in range(n + 1)]  # tail (+Y) to head (-Y)
    bone("root", (0, 0, 0), (0, 0, 0.08 * H))
    seg = []
    for i in range(n):
        bone(f"seg{i}", (0, ys[i], zc), (0, ys[i + 1], zc), "root" if i == 0 else f"seg{i - 1}")
        seg.append(f"seg{i}")
    chain_weights(seg, (hi[1] - Y) / L, [(i + 0.5) / n for i in range(n)], np.ones(NV))

else:  # hover
    mx = float(np.abs(X).max())
    bone("root", (0, 0, 0), (0, 0, 0.1 * H))
    bone("body", (0, 0, 0.3 * H), (0, 0, H), "root")
    for side, sgn in (("L", 1), ("R", -1)):
        sel = X * sgn > 0.62 * mx
        if sel.sum() < NV * 0.015:
            continue
        zc, yc = float(np.median(Z[sel])), float(np.median(Y[sel]))
        bone(f"fin{side}", (0.42 * mx * sgn, yc, zc), (mx * sgn, yc, zc), "body")
        fins.append(side)
        member = ramp(X * sgn, 0.5 * mx, 0.66 * mx)
        weights[f"fin{side}"] = member
        limb_mask += member
    under = (Z < 0.3 * H) & (np.abs(X) < 0.5 * mx)
    if under.sum() > NV * 0.02:
        bone("dangle", (0, 0, 0.36 * H), (0, 0, 0.0), "body")
        member = (1 - ramp(Z, 0.24 * H, 0.36 * H)) * (1 - ramp(np.abs(X), 0.45 * mx, 0.55 * mx)) * (1 - np.clip(limb_mask, 0, 1))
        weights["dangle"] = member
        limb_mask += member
    weights["body"] = np.clip(1 - limb_mask, 0, 1)

log(f"bones: {', '.join(order)}")

# ---- armature and skin --------------------------------------------------------------------------------------------

arm_data = bpy.data.armatures.new("Rig")
arm = bpy.data.objects.new("Rig", arm_data)
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
for name in order:
    h, t, parent = bones[name]
    if (t - h).length < 1e-3:
        t = h + Vector((0, 0, 0.02))
    b = arm_data.edit_bones.new(name)
    b.head, b.tail = h, t
    if parent:
        b.parent = arm_data.edit_bones[parent]
bpy.ops.object.mode_set(mode="OBJECT")

total = np.zeros(NV)
for w in weights.values():
    total += w
total[total < 1e-6] = 1.0
fallback = order[1] if len(order) > 1 else order[0]
for name in order:
    vg = body.vertex_groups.new(name=name)
    w = weights.get(name)
    if w is None:
        continue
    w = w / total
    idx = np.nonzero(w > 0.01)[0]
    for i in idx:
        vg.add([int(i)], float(w[i]), "REPLACE")
orphans = np.nonzero(sum(weights.values()) < 1e-6)[0] if weights else np.arange(NV)
if len(orphans):
    body.vertex_groups[fallback].add([int(i) for i in orphans], 1.0, "REPLACE")
body.parent = arm
mod = body.modifiers.new("Armature", "ARMATURE")
mod.object = arm

# ---- clips --------------------------------------------------------------------------------------------------------

bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")
pb = arm.pose.bones
for p in pb:
    p.rotation_mode = "QUATERNION"
rest = {p.name: p.bone.matrix_local.to_quaternion() for p in pb}


def local_rot(name, euler_deg):
    """A rotation given in armature axes (degrees about X, Y, Z), as the bone's own pose rotation (about its head)."""
    rw = Euler([math.radians(a) for a in euler_deg], "XYZ").to_quaternion()
    r = rest[name]
    return r.inverted() @ rw @ r


def local_loc(name, offset):
    return rest[name].inverted() @ Vector(offset)


def clip(name, frames, keys):
    """keys(t in 0..1) -> {bone: (euler_deg, offset[, scale])}."""
    act = bpy.data.actions.new(name)
    arm.animation_data_create()
    arm.animation_data.action = act
    for p in pb:
        p.rotation_quaternion = Quaternion()
        p.location = Vector()
        p.scale = Vector((1, 1, 1))
    fr = list(range(0, frames + 1, 2))
    if fr[-1] != frames:
        fr.append(frames)
    for f in fr:
        for bn, spec in keys(f / frames).items():
            if bn not in pb:
                continue
            rot, off = spec[0], spec[1]
            p = pb[bn]
            p.rotation_quaternion = local_rot(bn, rot)
            p.location = local_loc(bn, off)
            p.keyframe_insert("rotation_quaternion", frame=f)
            p.keyframe_insert("location", frame=f)
            if len(spec) > 2:
                p.scale = Vector(spec[2])
                p.keyframe_insert("scale", frame=f)
    act.use_fake_user = True
    track = arm.animation_data.nla_tracks.new()
    track.name = name
    track.strips.new(name, 0, act)
    arm.animation_data.action = None
    log(f"clip {name}: {frames} frames")


S, C = math.sin, math.cos
TAU = 2 * math.pi
Z0 = ((0, 0, 0), (0, 0, 0))


def ease(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


def bell(t, a, b):
    return S(math.pi * (t - a) / (b - a)) if a < t < b else 0.0


def merge(*ds):
    out = {}
    for d in ds:
        out.update(d)
    return out


if BODY == "upright":
    LEG = {s: (f"thigh{s}", f"shin{s}") for s in legs}
    ARM = {s: f"arm{s}" for s in arms}
    sgn = {"L": 1, "R": -1}

    def legs_pose(swing_l, swing_r, bend_l=0.0, bend_r=0.0):
        d = {}
        for s, (th, sh) in LEG.items():
            sw, bd = (swing_l, bend_l) if s == "L" else (swing_r, bend_r)
            d[th] = ((sw, 0, 0), (0, 0, 0))
            d[sh] = ((bd, 0, 0), (0, 0, 0))
        return d

    def arms_pose(f):
        return {ARM[s]: f(s) for s in ARM}

    clip("idle", 60, lambda t: merge(
        {"root": Z0, "pelvis": ((0, 0, 0), (0, 0, -0.008 * H * (1 - C(TAU * t)))),
         "chest": ((2 * S(TAU * t), 0, 1.5 * S(TAU * t + 1)), (0, 0, 0), (1 + 0.015 * S(TAU * t),) * 3),
         "head": ((-2 * S(TAU * t + 0.6), 0, 3 * S(TAU * t)), (0, 0, 0))},
        legs_pose(0, 0, 2 * (1 - C(TAU * t)), 2 * (1 - C(TAU * t))),
        arms_pose(lambda s: ((3 * S(TAU * t + 0.5), 0, sgn[s] * 3 * (1 - C(TAU * t))), (0, 0, 0)))))
    clip("idle2", 72, lambda t: merge(
        {"root": Z0, "chest": ((0, 0, 14 * bell(t, 0.1, 0.5) - 14 * bell(t, 0.5, 0.9)), (0, 0, 0)),
         "head": ((-6 * bell(t, 0.0, 1.0), 0, 26 * bell(t, 0.1, 0.5) - 26 * bell(t, 0.5, 0.9)), (0, 0, 0))},
        legs_pose(0, 0), arms_pose(lambda s: ((4 * bell(t, 0.2, 0.8), 0, 0), (0, 0, 0)))))

    def walk(t):
        ph = TAU * t
        swing = 24 * S(ph)
        d = {"root": ((0, 0, 0), (0, 0, 0.025 * H * abs(S(ph)))),
             "pelvis": ((0, 4 * S(ph), 5 * S(ph)), (0, 0, 0)),
             "chest": ((-4, -3 * S(ph), -7 * S(ph)), (0, 0, 0)),
             "head": ((2, 0, 4 * S(ph)), (0, 0, 0))}
        d.update(legs_pose(-swing, swing, 30 * max(0.0, S(ph + 1.2)), 30 * max(0.0, -S(ph + 1.2))))
        d.update(arms_pose(lambda s: ((swing * 0.7 * (1 if s == "L" else -1), 0, sgn[s] * 4), (0, 0, 0))))
        return d
    clip("move", 30, walk)

    def attack(t):  # wind back, swing the arms (or the whole upper body) forward
        back, hit = bell(t, 0.0, 0.42), bell(t, 0.3, 0.85)
        d = {"root": ((0, 0, 0), (0, -0.12 * hit, 0)),
             "pelvis": ((-6 * hit + 4 * back, 0, 8 * back - 10 * hit), (0, 0, 0)),
             "chest": ((-20 * hit + 10 * back, 0, 14 * back - 18 * hit), (0, 0, 0)),
             "head": ((-8 * hit, 0, 0), (0, 0, 0))}
        d.update(legs_pose(-12 * hit, 10 * hit, 0, 16 * hit))
        d.update(arms_pose(lambda s: ((50 * back - 95 * hit, 0, sgn[s] * 10 * hit), (0, 0, 0))))
        return d
    clip("attack", 24, attack)

    def attack2(t):  # a stomp and shove: both arms thrust, a heavy step
        lift, slam = bell(t, 0.0, 0.45), bell(t, 0.4, 0.8)
        d = {"root": ((0, 0, 0), (0, -0.1 * slam, 0.03 * H * lift - 0.02 * H * slam)),
             "pelvis": ((6 * lift - 10 * slam, 0, 0), (0, 0, 0)),
             "chest": ((10 * lift - 24 * slam, 0, 0), (0, 0, 0)),
             "head": ((-6 * slam, 0, 0), (0, 0, 0))}
        d.update(legs_pose(-40 * lift + 10 * slam, 6 * lift, 45 * lift, 8 * lift))
        d.update(arms_pose(lambda s: ((-30 * lift - 70 * slam, 0, -sgn[s] * 18 * slam), (0, 0, 0))))
        return d
    clip("attack2", 27, attack2)

    def hit(t):
        b = bell(t, 0, 1)
        d = {"root": ((0, 0, 0), (0, 0.06 * b, 0)), "pelvis": ((6 * b, 0, 0), Z0[1]),
             "chest": ((14 * b, 0, 8 * b), (0, 0, 0)), "head": ((18 * b, 0, -10 * b), (0, 0, 0))}
        d.update(legs_pose(6 * b, -8 * b, 10 * b, 0))
        d.update(arms_pose(lambda s: ((25 * b, 0, sgn[s] * 30 * b), (0, 0, 0))))
        return d
    clip("hit", 15, hit)

    def death(t):
        buckle, fall = ease(t * 2.2), ease((t - 0.25) * 1.6)
        d = {"root": ((0, 0, 0), (0, 0.18 * fall, -0.28 * H * buckle)),
             "pelvis": ((70 * fall, 0, 6 * fall), (0, 0, 0)),
             "chest": ((12 * fall, 0, 0), (0, 0, 0)), "head": ((20 * fall, 0, 15 * fall), (0, 0, 0))}
        d.update(legs_pose(-60 * buckle + 40 * fall, -50 * buckle + 30 * fall, 95 * buckle, 80 * buckle))
        d.update(arms_pose(lambda s: ((-40 * buckle + 100 * fall, 0, sgn[s] * 40 * fall), (0, 0, 0))))
        return d
    clip("death", 40, death)

    def special(t):  # bracing for something: shaking, swelling, arms spread
        j = S(TAU * t * 9)
        d = {"root": ((0, 0, 0), (0.01 * j, 0, 0)),
             "pelvis": ((0, 3 * j, 0), (0, 0, 0)),
             "chest": ((-8, 4 * j, 0), (0, 0, 0), (1 + 0.06 * bell(t, 0, 1),) * 3),
             "head": ((10, 0, 5 * j), (0, 0, 0), (1 + 0.1 * bell(t, 0, 1),) * 3)}
        d.update(legs_pose(0, 0, 20, 20))
        d.update(arms_pose(lambda s: ((-30, 0, sgn[s] * (40 + 6 * j)), (0, 0, 0))))
        return d
    clip("special", 30, special)

elif BODY == "crawler":
    quad = "quad" in OPTS
    sp = ["spine0", "spine1", "spine2", "spine3"]

    def legs_gait(t, amp_swing, amp_lift, speed=1.0):
        d = {}
        for name, s, j, yc in legs:
            if quad:  # trot: diagonal pairs together, swing fore and aft
                phase = 0.5 * ((j + (0 if s > 0 else 1)) % 2)
                a = TAU * (t * speed + phase)
                d[name] = ((amp_swing * S(a), 0, 0), (0, 0, amp_lift * 0.002 * max(0.0, C(a))))
            else:     # tripod: alternate legs on each side, sweeping forward and back and lifting
                phase = 0.5 * ((j + (0 if s > 0 else 1)) % 2)
                a = TAU * (t * speed + phase)
                d[name] = ((0, -s * amp_lift * max(0.0, C(a)), amp_swing * S(a)), (0, 0, 0))
        return d

    def idle(t):
        d = {sp[i]: ((1.2 * S(TAU * t - i), 0, 1.5 * S(TAU * t - i * 0.7)), (0, 0, 0), (1 + 0.02 * S(TAU * t),) * 3) for i in range(4)}
        d["root"] = Z0
        d.update(legs_gait(t, 3, 4))
        return d
    clip("idle", 60, idle)

    def idle2(t):  # head sweeps side to side, sniffing
        d = {sp[i]: ((0, 0, (i / 3) * 22 * (bell(t, 0.05, 0.45) - bell(t, 0.5, 0.95))), (0, 0, 0)) for i in range(4)}
        d[sp[3]] = ((-10 * bell(t, 0.2, 0.8), 0, 30 * (bell(t, 0.05, 0.45) - bell(t, 0.5, 0.95))), (0, 0, 0))
        d["root"] = Z0
        d.update(legs_gait(t * 0.25, 4, 3))
        return d
    clip("idle2", 72, idle2)

    def move(t):
        d = {sp[i]: ((0, 0, 7 * S(TAU * t * 2 - i * 0.9)), (0, 0, 0)) for i in range(4)}
        d["root"] = ((0, 0, 0), (0, 0, H * 0.03 * abs(S(TAU * t * 2))))
        d.update(legs_gait(t, 26 if quad else 24, 30 if quad else 22, 2.0 if not quad else 1.0))
        return d
    clip("move", 24, move)

    def attack(t):  # rear up, then strike forward and down
        rear, strike = bell(t, 0.0, 0.42), bell(t, 0.32, 0.85)
        d = {sp[i]: ((-(22 * rear - 30 * strike) * (i + 1) / 4, 0, 0), (0, 0, 0)) for i in range(4)}
        d["root"] = ((0, 0, 0), (0, -0.2 * strike, H * 0.06 * rear))
        for name, s, j, yc in legs:
            front = yc < (lo[1] + hi[1]) / 2
            d[name] = ((-35 * rear + 25 * strike, 0, 0) if quad else (0, -s * (30 * rear if front else -8 * rear), -20 * strike if front else 6 * strike), (0, 0, 0))
        return d
    clip("attack", 21, attack)

    def attack2(t):  # quick snapping bites with the head
        snaps = bell(t, 0.05, 0.35) + bell(t, 0.4, 0.7)
        d = {sp[3]: ((-25 * snaps, 0, 8 * S(TAU * t * 2)), (0, -0.05 * snaps, 0)), sp[2]: ((-10 * snaps, 0, 0), (0, 0, 0)), "root": Z0}
        d.update(legs_gait(t, 6, 6, 2.0))
        return d
    clip("attack2", 24, attack2)

    def hit(t):
        b = bell(t, 0, 1)
        d = {sp[i]: ((-12 * b * (i + 1) / 4, 0, 8 * b * (1 if i % 2 else -1)), (0, 0, 0)) for i in range(4)}
        d["root"] = ((0, 0, 0), (0, 0.07 * b, 0.02 * H * b))
        for name, s, j, yc in legs:
            d[name] = ((20 * b, 0, 0) if quad else (0, s * 25 * b, 0), (0, 0, 0))
        return d
    clip("hit", 15, hit)

    def death(t):
        e = ease(t * 1.3)
        curl = ease((t - 0.3) * 1.6)
        d = {sp[i]: ((8 * e * (1 if i % 2 else -1), 0, 6 * e), (0, 0, 0)) for i in range(4)}
        if quad:  # collapses onto its side
            d["root"] = ((0, 82 * e, 0), (0.1 * e, 0, H * (0.18 * e - 0.45 * e * e)))
            for name, s, j, yc in legs:
                d[name] = ((25 * curl * (1 if j % 2 else -1), 0, 0), (0, 0, 0))
        else:     # flips over, legs curling up
            d["root"] = ((0, 172 * e, 0), (0, 0, H * (0.8 * e - 0.55 * e * e)))
            for name, s, j, yc in legs:
                d[name] = ((0, s * 55 * curl, 10 * curl), (0, 0, 0))
        return d
    clip("death", 36, death)

    def special(t):
        j = S(TAU * t * 10)
        d = {sp[i]: ((-6, 0, 3 * j), (0, 0, 0), (1 + 0.05 * bell(t, 0, 1),) * 3) for i in range(4)}
        d["root"] = ((0, 0, 0), (0.008 * j, 0, 0))
        d.update(legs_gait(t, 4, 10, 4.0))
        return d
    clip("special", 30, special)

elif BODY == "serpent":
    seg = [f"seg{i}" for i in range(8)]
    n = len(seg)

    def body(f):
        """f(i, u) -> (rot, off) for each segment; u = 0 at the tail, 1 at the head."""
        d = {seg[i]: f(i, i / (n - 1)) for i in range(n)}
        d["root"] = Z0
        return d

    def raised(u, amount):
        """The front third lifts: negative X turns a head-forward bone up."""
        return -amount * max(0.0, (u - 0.6) / 0.4)

    clip("idle", 60, lambda t: body(lambda i, u: ((raised(u, 14) + 3 * S(TAU * t - i * 0.5), 0, 5 * S(TAU * t - i * 0.7)), (0, 0, 0), (1 + 0.025 * S(TAU * t - i * 0.6),) * 3)))
    clip("idle2", 72, lambda t: body(lambda i, u: ((raised(u, 22), 0, (28 * (bell(t, 0.05, 0.45) - bell(t, 0.5, 0.95)) if u > 0.7 else 4 * S(TAU * t - i * 0.6))), (0, 0, 0))))

    def move(t):
        d = body(lambda i, u: ((raised(u, 6) + 2 * S(TAU * t * 2 - i), 0, 22 * S(TAU * t * 2 - i * 0.85) * (0.5 + 0.5 * u)), (0, 0, 0)))
        d["root"] = ((0, 0, 0), (0, 0, 0.01 * H * abs(S(TAU * t * 2))))
        return d
    clip("move", 30, move)

    def attack(t):  # rears up, then strikes forward and down
        rear, strike = bell(t, 0.0, 0.45), bell(t, 0.35, 0.85)
        d = body(lambda i, u: ((-(45 * rear - 40 * strike) * max(0.0, (u - 0.45) / 0.55) + (8 * rear if u < 0.45 else 0), 0, 0), (0, 0, 0)))
        d["root"] = ((0, 0, 0), (0, -0.18 * strike, 0))
        return d
    clip("attack", 24, attack)

    def attack2(t):  # a whip of the whole body to one side and back
        w = bell(t, 0.05, 0.6) - 0.6 * bell(t, 0.45, 0.95)
        return body(lambda i, u: ((raised(u, 10), 0, 26 * w * (0.3 + u)), (0, 0, 0)))
    clip("attack2", 24, attack2)

    def hit(t):
        b = bell(t, 0, 1)
        return body(lambda i, u: ((raised(u, 10) + 12 * b * u, 0, 14 * b * (1 if i % 2 else -1)), (0, 0, 0)))
    clip("hit", 15, hit)

    def death(t):
        writhe = (1 - ease((t - 0.35) * 2.5)) * S(TAU * t * 3)
        curl = ease((t - 0.3) * 1.5)
        d = body(lambda i, u: ((raised(u, 10) * (1 - curl), 0, 25 * writhe * (1 if i % 2 else -1) + 30 * curl * u), (0, 0, 0)))
        d["root"] = ((0, 60 * curl, 0), (0, 0, 0))
        return d
    clip("death", 42, death)

    def special(t):  # coils up tight and shudders
        c = ease(t * 2.5)
        j = S(TAU * t * 10)
        return body(lambda i, u: ((raised(u, 30) * c, 0, 38 * c + 4 * j), (0, 0, 0), (1 + 0.05 * bell(t, 0, 1),) * 3))
    clip("special", 30, special)

else:  # hover
    FIN = {s: f"fin{s}" for s in fins}
    sgn = {"L": 1, "R": -1}

    def fins_pose(f):
        return {FIN[s]: f(s) for s in FIN}

    def dangle(rot):
        return {"dangle": (rot, (0, 0, 0))} if "dangle" in bones else {}

    clip("idle", 60, lambda t: merge(
        {"root": Z0, "body": ((3 * S(TAU * t), 2 * S(TAU * t + 1.3), 0), (0, 0, H * 0.06 * S(TAU * t)))},
        fins_pose(lambda s: ((0, sgn[s] * 10 * S(TAU * t * 2), 0), (0, 0, 0))),
        dangle((8 * S(TAU * t + 0.8), 0, 5 * S(TAU * t)))))
    clip("idle2", 72, lambda t: merge(
        {"root": Z0, "body": ((0, 0, 40 * bell(t, 0.05, 0.45) - 40 * bell(t, 0.5, 0.95)), (0, 0, H * 0.04 * S(TAU * t)))},
        fins_pose(lambda s: ((0, sgn[s] * 15 * bell(t, 0.3, 0.7), 0), (0, 0, 0))),
        dangle((10 * S(TAU * t * 2), 0, 0))))
    clip("move", 24, lambda t: merge(
        {"body": ((-14, 5 * S(TAU * t), 0), (0, 0, H * 0.05 * S(TAU * t * 2)))},
        fins_pose(lambda s: ((0, sgn[s] * (-20 + 8 * S(TAU * t * 2)), 0), (0, 0, 0))),
        dangle((22, 0, 6 * S(TAU * t)))))

    def attack(t):
        back, dart = bell(t, 0.0, 0.4), bell(t, 0.3, 0.85)
        return merge({"body": ((15 * back - 28 * dart, 0, 0), (0, 0.1 * back - 0.32 * dart, H * 0.04 * back))},
                     fins_pose(lambda s: ((-50 * dart, sgn[s] * 25 * back, 0), (0, 0, 0))),
                     dangle((-60 * dart + 20 * back, 0, 0)))
    clip("attack", 21, attack)

    def attack2(t):  # a spin, lashing out with what hangs underneath
        spin = ease(t * 1.4) * 360
        return merge({"body": ((0, 0, spin), (0, 0, H * 0.05 * bell(t, 0, 1)))},
                     fins_pose(lambda s: ((0, sgn[s] * 30 * bell(t, 0.1, 0.9), 0), (0, 0, 0))),
                     dangle((40 * bell(t, 0.1, 0.9), 0, 0)))
    clip("attack2", 24, attack2)

    def hit(t):
        b = bell(t, 0, 1)
        return merge({"body": ((18 * b, 22 * b, 10 * b), (0, 0.1 * b, 0))},
                     fins_pose(lambda s: ((0, -sgn[s] * 30 * b, 0), (0, 0, 0))), dangle((30 * b, 0, 20 * b)))
    clip("hit", 15, hit)

    def death(t):
        e = ease(t * 1.2)
        j = S(TAU * t * 8) * (1 - e)
        return merge({"body": ((35 * e + 6 * j, 120 * e, 8 * j), (0, 0, -H * 0.3 * e))},
                     fins_pose(lambda s: ((0, -sgn[s] * 60 * e, 0), (0, 0, 0))), dangle((70 * e, 0, 0)))
    clip("death", 36, death)

    def special(t):  # shuddering, fins flared, swelling
        j = S(TAU * t * 11)
        return merge({"body": ((5 * j, 0, 6 * j), (0, 0, 0), (1 + 0.08 * bell(t, 0, 1),) * 3)},
                     fins_pose(lambda s: ((0, sgn[s] * (35 + 8 * j), 0), (0, 0, 0))), dangle((15 * j, 0, 0)))
    clip("special", 30, special)

bpy.ops.object.mode_set(mode="OBJECT")
for p in pb:
    p.rotation_quaternion = Quaternion()
    p.location = Vector()
    p.scale = Vector((1, 1, 1))

# ---- export -------------------------------------------------------------------------------------------------------

bpy.ops.object.select_all(action="SELECT")
kw = dict(filepath=OUT, export_format="GLB", use_selection=False, export_skins=True, export_animations=True,
          export_yup=True, export_apply=False, export_morph=False, export_draco_mesh_compression_enable=False)
try:
    bpy.ops.export_scene.gltf(**kw, export_animation_mode="NLA_TRACKS", export_force_sampling=True, export_frame_step=1)
except TypeError as e:
    log("exporter options:", e)
    bpy.ops.export_scene.gltf(**kw)
log("wrote", OUT)
