# Renders a CS2 weapon GLB (original mesh + original textures) in the three views the game's character
# sprites use. blender -b --factory-startup --python bl_views.py -- <glb> <outdir> <tilt_deg> <px_long>
#   s = side, muzzle pointing right;  b = from above-behind, muzzle up;  f = from above-front, muzzle down
# The barrel axis = longest bbox axis; muzzle = the end with the smaller cross-section; up = away from the grip/magazine.
import bpy, sys, os, math, json
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index("--") + 1:]
glb, out, tilt, pxl = a[0], a[1], float(a[2]), int(a[3])
keep_side = a[4] if len(a) > 4 else ""
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
bpy.ops.import_scene.gltf(filepath=glb)
for o in bpy.data.objects:
    if o.type == "ARMATURE":
        o.data.pose_position = "REST"; o.animation_data_clear()
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
keep = [o for o in meshes if "body_hd" in o.name] or [o for o in meshes if "legacy" not in o.name] or meshes
for o in meshes: o.hide_render = o not in keep
bpy.context.view_layer.update()
if keep_side:
    import bmesh
    ax = "xyz".index(keep_side[0]); sign = 1 if keep_side[1] == "+" else -1
    dg0 = bpy.context.evaluated_depsgraph_get()
    vals = []
    for o in keep:
        e = o.evaluated_get(dg0); m = e.to_mesh(); vals += [(e.matrix_world @ v.co)[ax] for v in m.vertices]; e.to_mesh_clear()
    mid = (min(vals) + max(vals)) / 2
    for o in keep:
        e = o.evaluated_get(dg0); m = e.to_mesh(); wpos = [(e.matrix_world @ v.co)[ax] for v in m.vertices]; e.to_mesh_clear()
        bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i, w in enumerate(wpos) if (w - mid) * sign < 0], context="VERTS")
        bm.to_mesh(o.data); bm.free()
    bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
pts = []
for o in keep:
    e = o.evaluated_get(dg); m = e.to_mesh()
    pts += [e.matrix_world @ v.co for v in m.vertices]; e.to_mesh_clear()
mn = Vector((min(p[i] for p in pts) for i in range(3))); mx = Vector((max(p[i] for p in pts) for i in range(3)))
size = mx - mn; order = sorted(range(3), key=lambda i: -size[i])
L, Uax, W = order
def ext(sel, ax):
    v = [p[ax] for p in sel]; return (min(v), max(v)) if v else (0, 0)
lo = [p for p in pts if p[L] < mn[L] + 0.12 * size[L]]; hi = [p for p in pts if p[L] > mx[L] - 0.12 * size[L]]
elo, ehi = ext(lo, Uax), ext(hi, Uax)
muzzle_hi = (ehi[1] - ehi[0]) < (elo[1] - elo[0])
F = Vector((0, 0, 0)); F[L] = 1 if muzzle_hi else -1
mz = ehi if muzzle_hi else elo; c = (mz[0] + mz[1]) / 2
U = Vector((0, 0, 0)); U[Uax] = 1 if (c - mn[Uax]) > (mx[Uax] - c) else -1   # more of the gun hangs below the bore
center = (mn + mx) / 2
cam = bpy.data.cameras.new("c"); cam.type = "ORTHO"; cam.clip_start = 0.001; cam.clip_end = 1000
co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co); sc.camera = co
def sun(e, rx, ry):
    l = bpy.data.lights.new("s", "SUN"); l.energy = e; o = bpy.data.objects.new("s", l); sc.collection.objects.link(o)
    o.parent = co; o.rotation_euler = (math.radians(rx), math.radians(ry), 0)
sun(3.2, 30, -32); sun(0.8, -35, 35)
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND"); bg.inputs[0].default_value = (1, 1, 1, 1); bg.inputs[1].default_value = 0.5
for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
    try: sc.render.engine = eng; break
    except TypeError: pass
sc.render.film_transparent = True
sc.render.image_settings.file_format = "PNG"; sc.render.image_settings.color_mode = "RGBA"
try: sc.view_settings.view_transform = "Standard"
except Exception: pass
info = {"glb": glb, "keep": [o.name for o in keep], "size_m": list(size), "axes": {"barrel": L, "up": Uax}, "F": list(F), "U": list(U), "views": {}}
t = math.radians(tilt)
views = {
    "s": (F, U, F.cross(U)),
    "b": None, "f": None,
}
for name in ("s", "b", "f"):
    if name == "s": X, Y, Z = F, U, F.cross(U)
    else:
        Z = (U * math.cos(t) + (-F if name == "b" else F) * math.sin(t)).normalized()
        want = F if name == "b" else -F
        Y = (want - want.dot(Z) * Z).normalized(); X = Y.cross(Z)
    R = Matrix((X, Y, Z)).transposed()
    xs = [(p - center).dot(X) for p in pts]; ys = [(p - center).dot(Y) for p in pts]
    wx, wy = max(xs) - min(xs), max(ys) - min(ys); cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
    m = 1.04; long = max(wx, wy) * m
    cam.ortho_scale = long
    sc.render.resolution_x = max(8, round(pxl * wx * m / long)); sc.render.resolution_y = max(8, round(pxl * wy * m / long))
    co.matrix_world = Matrix.Translation(center + X * cx + Y * cy + Z * 5 * max(size)) @ R.to_4x4()
    sc.render.filepath = os.path.join(out, name + ".png"); bpy.ops.render.render(write_still=True)
    info["views"][name] = {"w_m": wx, "h_m": wy, "px": [sc.render.resolution_x, sc.render.resolution_y], "m_per_px": long / pxl}
json.dump(info, open(os.path.join(out, "info.json"), "w"), indent=1); print("DONE", json.dumps(info))
