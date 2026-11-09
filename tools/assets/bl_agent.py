# Renders a CS2 agent GLB (original mesh + textures + CS2 world animations).
#   blender -b --factory-startup --python bl_agent.py -- <glb> <outdir> probe
#       one image per side (+x,-x,+y,-y) of the first frame of the idle clip, to find which way the model faces
#   blender -b --factory-startup --python bl_agent.py -- <glb> <outdir> frames <view_axis> <elev_deg> <px_h> <clip=regex:n_frames> ...
#       n evenly spaced frames of each clip, seen from view_axis (e.g. "-y") raised by elev_deg; transparent PNGs
#   blender ... -- <glb> <outdir> front <view_axis> <px_h>        the bind/idle pose from the front (for colours)
import bpy, sys, os, math, re, json
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index("--") + 1:]
glb, out, mode = a[0], a[1], a[2]
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
bpy.ops.import_scene.gltf(filepath=glb)
arm = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
# third-person body only: the exports also carry first-person arms/sleeves, the defuse kit and dozens of weapon
# skeletons with placeholder meshes ("empty_mesh_reference") that would blow up the bounds
for o in meshes:
    o.hide_render = "thirdperson" not in o.name.lower()
meshes = [o for o in meshes if not o.hide_render]
arm = next((o.parent for o in meshes if o.parent and o.parent.type == "ARMATURE"), arm)
# matte, like the game's flat pixel art: the CS2 gear is shiny and its highlights wash the colours out when shrunk
for m in bpy.data.materials:
    if not m.use_nodes: continue
    for n in m.node_tree.nodes:
        if n.type != "BSDF_PRINCIPLED": continue
        for name, val in (("Metallic", 0.0), ("Roughness", 0.9), ("Specular IOR Level", 0.0), ("Specular", 0.0), ("Coat Weight", 0.0), ("Sheen Weight", 0.0)):
            if name in n.inputs and not n.inputs[name].is_linked: n.inputs[name].default_value = val
            elif name in n.inputs and name in ("Metallic", "Roughness", "Specular IOR Level"):
                for l in list(n.inputs[name].links): m.node_tree.links.remove(l)
                n.inputs[name].default_value = val
acts = {x.name: x for x in bpy.data.actions}
print("ACTIONS", list(acts))
def set_action(rx):
    # world clips only: the exports also carry first-person (viewmodel) clips with the same short names
    act = next((x for n, x in acts.items() if re.search(rx, n) and "/viewmodel/" not in n), None)
    if arm and act:
        arm.animation_data_create(); arm.animation_data.action = act
        try:
            slot = act.slots[0] if hasattr(act, "slots") and len(act.slots) else None
            if slot is not None: arm.animation_data.action_slot = slot
        except Exception as e: print("slot", e)
    return act
cam = bpy.data.cameras.new("c"); cam.type = "ORTHO"; cam.clip_start = 0.01; cam.clip_end = 1000
co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co); sc.camera = co
def sun(e, rx, ry):
    l = bpy.data.lights.new("s", "SUN"); l.energy = e; o = bpy.data.objects.new("s", l); sc.collection.objects.link(o)
    o.parent = co; o.rotation_euler = (math.radians(rx), math.radians(ry), 0)
sun(3.0, 25, -30); sun(0.9, -30, 35)
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND"); bg.inputs[0].default_value = (1, 1, 1, 1); bg.inputs[1].default_value = 0.6
for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
    try: sc.render.engine = eng; break
    except TypeError: pass
sc.render.film_transparent = True; sc.render.image_settings.file_format = "PNG"; sc.render.image_settings.color_mode = "RGBA"
try: sc.view_settings.view_transform = "Standard"
except Exception: pass
def bounds():
    dg = bpy.context.evaluated_depsgraph_get(); pts = []
    for o in meshes:
        if o.hide_render: continue
        e = o.evaluated_get(dg); m = e.to_mesh(); pts += [e.matrix_world @ v.co for v in m.vertices]; e.to_mesh_clear()
    mn = Vector((min(p[i] for p in pts) for i in range(3))); mx = Vector((max(p[i] for p in pts) for i in range(3)))
    return mn, mx
AX = {"+x": Vector((1, 0, 0)), "-x": Vector((-1, 0, 0)), "+y": Vector((0, 1, 0)), "-y": Vector((0, -1, 0))}
def aim(view, elev, center, height, pxh, aspect=0.75):
    d = AX[view]; e = math.radians(elev)
    Z = (d * math.cos(e) + Vector((0, 0, 1)) * math.sin(e)).normalized()   # towards the camera
    Y = (Vector((0, 0, 1)) - Vector((0, 0, 1)).dot(Z) * Z).normalized(); X = Y.cross(Z)
    cam.ortho_scale = height * 1.15
    sc.render.resolution_y = pxh; sc.render.resolution_x = int(pxh * aspect)
    co.matrix_world = Matrix.Translation(center + Z * 20) @ Matrix((X, Y, Z)).transposed().to_4x4()
info = {}
if mode == "probe":
    set_action(r"world/rifle/_default_rifle/idle_rifle$"); sc.frame_set(1)
    mn, mx = bounds(); c = (mn + mx) / 2; h = (mx - mn).z
    info["bounds"] = [list(mn), list(mx)]
    for v in AX:
        aim(v, 10, c, h, 256); sc.render.filepath = os.path.join(out, "probe_" + v.replace("+", "p").replace("-", "m") + ".png")
        bpy.ops.render.render(write_still=True)
elif mode == "front":
    view, pxh = a[3], int(a[4])
    set_action(r"world/rifle/_default_rifle/idle_rifle$"); sc.frame_set(1)
    mn, mx = bounds(); c = (mn + mx) / 2; h = (mx - mn).z
    aim(view, 0, c, h, pxh); sc.render.filepath = os.path.join(out, "front.png"); bpy.ops.render.render(write_still=True)
    info["bounds"] = [list(mn), list(mx)]
else:
    view, elev, pxh = a[3], float(a[4]), int(a[5])
    set_action(r"world/rifle/_default_rifle/idle_rifle$"); sc.frame_set(1); mn, mx = bounds(); h = (mx - mn).z
    c0 = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, (mn.z + mx.z) / 2))
    for spec in a[6:]:
        rx, n = spec.rsplit(":", 1); n = int(n)
        act = set_action(rx)
        if act is None: print("no clip", rx); continue
        f0, f1 = act.frame_range
        name = re.sub(r"[^a-z0-9]+", "_", rx.lower()).strip("_")
        info[name] = {"clip": act.name, "range": [f0, f1], "frames": []}
        for i in range(n):
            f = f0 + (f1 - f0) * i / n
            sc.frame_set(int(f), subframe=f - int(f))
            # keep the root in place (walk clips may move the root): centre on the pelvis horizontally
            mn2, mx2 = bounds(); cc = Vector(((mn2.x + mx2.x) / 2, (mn2.y + mx2.y) / 2, c0.z))
            aim(view, elev, cc, h, pxh)
            p = os.path.join(out, f"{name}_{i}.png"); sc.render.filepath = p; bpy.ops.render.render(write_still=True)
            info[name]["frames"].append(os.path.basename(p))
json.dump(info, open(os.path.join(out, "info_" + mode + ".json"), "w"), indent=1); print("DONE", json.dumps(info)[:800])
