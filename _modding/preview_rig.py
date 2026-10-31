"""Blender (headless): renders a rigged creature .glb at a few moments of each clip, for checking animations by eye.
  blender -b --factory-startup -P preview_rig.py -- <in.glb> <out_dir>
Writes <out_dir>/<clip>_<n>.png (front three-quarter view, Workbench, textured)."""
import math, os, sys
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], argv[1]
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
arm = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)
if arm is None:
    raise SystemExit("no armature")
sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"
sc.display.shading.light = "STUDIO"
sc.display.shading.color_type = "TEXTURE"
sc.render.resolution_x = sc.render.resolution_y = 320
sc.render.film_transparent = False
sc.world = bpy.data.worlds.new("w")
sc.world.color = (0.12, 0.13, 0.16)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
sc.collection.objects.link(cam)
sc.camera = cam
# front three-quarter: the creature faces -Y
cam.location = Vector((1.5, -2.2, 1.3))
cam.rotation_euler = (Vector((0, 0, 0.4)) - cam.location).to_track_quat("-Z", "Y").to_euler()
cam.data.lens = 35
actions = [a for a in bpy.data.actions]
arm.animation_data_create()
for tr in list(arm.animation_data.nla_tracks):
    arm.animation_data.nla_tracks.remove(tr)
for act in actions:
    arm.animation_data.action = act
    try:
        if hasattr(arm.animation_data, "action_slot") and act.slots:
            arm.animation_data.action_slot = act.slots[0]
    except Exception:
        pass
    f0, f1 = act.frame_range
    for i, frac in enumerate((0.0, 0.35, 0.7, 1.0)):
        sc.frame_set(int(round(f0 + (f1 - f0) * frac)))
        sc.render.filepath = os.path.join(OUT, f"{act.name}_{i}.png")
        bpy.ops.render.render(write_still=True)
print("PREVIEW: rendered", [a.name for a in actions], flush=True)
