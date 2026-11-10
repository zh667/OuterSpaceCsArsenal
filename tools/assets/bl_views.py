# Renders a CS2 weapon GLB (original mesh + original textures) in the three views the game's character
# sprites use. blender -b --factory-startup --python bl_views.py -- <glb> <outdir> <tilt_deg> <px_long>
#   s = side, muzzle pointing right;  b = from above-behind, muzzle up;  f = from above-front, muzzle down
# The barrel axis = longest bbox axis; muzzle = the end with the smaller cross-section; up = away from the grip/magazine.
import bpy, sys, os, math, json
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index("--") + 1:]
glb, out, tilt, pxl = a[0], a[1], float(a[2]), int(a[3])
keep_side = a[4] if len(a) > 4 else ""
# options (comma list): islands = drop small loose parts lying apart from the gun (a spare shell, a loose cylinder);
# flipF / flipU = the automatic muzzle or up direction came out wrong for this model
opts = set((a[5] if len(a) > 5 else "").split(","))
# griponly: keep the existing pictures, only add each view's grip pixel to info.json (same framing, checked)
griponly = "griponly" in opts
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
bpy.ops.import_scene.gltf(filepath=glb)
for o in bpy.data.objects:
    if o.type == "ARMATURE":
        if "pose" in opts:   # some rest poses are broken (the R8's grip): use the first frame of an imported clip
            acts = [x for x in bpy.data.actions]
            pick = next((x for x in acts if "idle" in x.name.lower()), None) or next((x for x in acts if "draw" in x.name.lower()), None) or (acts[0] if acts else None)
            if pick is not None:
                if o.animation_data is None: o.animation_data_create()
                o.animation_data.action = pick; sc.frame_set(int(pick.frame_range[1] if "draw" in pick.name.lower() and "idle" not in pick.name.lower() else pick.frame_range[0]))
            print("pose from", pick.name if pick else None, "of", [x.name for x in acts][:12])
        else:
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
if "islands" in opts:
    import bmesh
    dg0 = bpy.context.evaluated_depsgraph_get()
    comps = []   # (object, vertex indices, world bbox min, max)
    for o in keep:
        e = o.evaluated_get(dg0); m = e.to_mesh(); W = [e.matrix_world @ v.co for v in m.vertices]; e.to_mesh_clear()
        bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table(); seen = set()
        for v in bm.verts:
            if v.index in seen: continue
            stack, part = [v], []
            seen.add(v.index)
            while stack:
                x = stack.pop(); part.append(x.index)
                for ed in x.link_edges:
                    y = ed.other_vert(x)
                    if y.index not in seen: seen.add(y.index); stack.append(y)
            ps = [W[i] for i in part]
            comps.append((o, part, Vector((min(p[k] for p in ps) for k in range(3))), Vector((max(p[k] for p in ps) for k in range(3)))))
        bm.free()
    total = sum(len(c[1]) for c in comps); main = max(comps, key=lambda c: len(c[1]))
    # the gun = the biggest part plus every part touching it (grown until nothing new touches)
    gmn, gmx = main[2].copy(), main[3].copy(); inside = {id(main)}; grew = True
    while grew:
        grew = False; pad = (gmx - gmn) * 0.02
        for c in comps:
            if id(c) in inside: continue
            if all(c[2][k] <= gmx[k] + pad[k] and c[3][k] >= gmn[k] - pad[k] for k in range(3)):
                inside.add(id(c)); grew = True
                for k in range(3): gmn[k] = min(gmn[k], c[2][k]); gmx[k] = max(gmx[k], c[3][k])
    drop = {}
    for c in comps:
        if id(c) not in inside and len(c[1]) < 0.05 * total: drop.setdefault(c[0].name, []).extend(c[1])
    for o in keep:
        if o.name not in drop: continue
        bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i in drop[o.name]], context="VERTS"); bm.to_mesh(o.data); bm.free()
    print("islands dropped:", {k: len(v) for k, v in drop.items()}, "of", total, "vertices")
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
if "flipF" in opts: F = -F
if "flipU" in opts: U = -U
center = (mn + mx) / 2
# the model's right-hand bone (ag1_hand_r, where CS2 puts the hand on the grip), in world space
grip_w = trig_w = None   # trigger: in front of the hand on an upright, muzzle-right gun (orientation check)
for o in bpy.data.objects:
    if o.type == "ARMATURE":
        pb = next((b for b in o.pose.bones if b.name.lower() in ("ag1_hand_r", "weapon_hand_r", "hand_r")), None)
        if pb is not None: grip_w = o.matrix_world @ pb.head
        tb = o.pose.bones.get("trigger")
        if tb is not None: trig_w = o.matrix_world @ tb.head
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
info = {"glb": glb, "tilt": tilt, "opts": sorted(o for o in opts if o), "keep": [o.name for o in keep], "size_m": list(size), "axes": {"barrel": L, "up": Uax}, "F": list(F), "U": list(U), "views": {}}
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
    sc.render.filepath = os.path.join(out, name + ".png")
    if not griponly: bpy.ops.render.render(write_still=True)
    rx, ry = sc.render.resolution_x, sc.render.resolution_y
    info["views"][name] = {"w_m": wx, "h_m": wy, "px": [rx, ry], "m_per_px": long / pxl}
    if grip_w is not None:   # pixel of the hand bone in this picture (x right, y down)
        info["views"][name]["grip_px"] = [rx / 2 + ((grip_w - center).dot(X) - cx) * pxl / long, ry / 2 - ((grip_w - center).dot(Y) - cy) * pxl / long]
    if trig_w is not None:
        info["views"][name]["trigger_px"] = [rx / 2 + ((trig_w - center).dot(X) - cx) * pxl / long, ry / 2 - ((trig_w - center).dot(Y) - cy) * pxl / long]
if griponly:
    old = json.load(open(os.path.join(out, "info.json")))
    for v, d in info["views"].items():
        same = old["views"][v]["px"] == d["px"]
        old["views"][v]["grip_px"] = d.get("grip_px"); old["views"][v]["trigger_px"] = d.get("trigger_px"); old["views"][v]["grip_framing_same"] = same
    info = old
json.dump(info, open(os.path.join(out, "info.json"), "w"), indent=1); print("DONE", json.dumps(info))
