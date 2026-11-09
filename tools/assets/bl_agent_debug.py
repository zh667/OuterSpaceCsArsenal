# Debug: list meshes/materials/armatures of an agent GLB and render REST vs idle from the front (-y) and side (+x).
import bpy, sys, os, math, json
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index("--") + 1:]; glb, out = a[0], a[1]; os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
bpy.ops.import_scene.gltf(filepath=glb)
for o in bpy.data.objects:
    if o.type == "MESH":
        print("MESH", o.name, len(o.data.vertices), [m.name for m in o.data.materials if m], "parent", o.parent.name if o.parent else None,
              "mods", [(m.type, getattr(m, "object", None) and m.object.name) for m in o.modifiers])
    elif o.type == "ARMATURE":
        print("ARM", o.name, len(o.data.bones), "rot", tuple(round(x, 3) for x in o.rotation_euler), "anim", o.animation_data.action.name if o.animation_data and o.animation_data.action else None)
    else: print("OBJ", o.type, o.name)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
def vis(o):
    n = o.name.lower(); mats = " ".join(m.name.lower() for m in o.data.materials if m)
    return not any(k in n for k in ("firstperson", "defuse", "physics")) and "skeleton" not in mats
for o in meshes: o.hide_render = not vis(o)
cam = bpy.data.cameras.new("c"); cam.type = "ORTHO"; co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co); sc.camera = co
l = bpy.data.lights.new("s", "SUN"); l.energy = 3; lo = bpy.data.objects.new("s", l); sc.collection.objects.link(lo); lo.rotation_euler = (0.6, 0.2, 0.3)
for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
    try: sc.render.engine = eng; break
    except TypeError: pass
sc.render.film_transparent = True; sc.render.resolution_x = 256; sc.render.resolution_y = 256
def bounds():
    dg = bpy.context.evaluated_depsgraph_get(); pts = []
    for o in meshes:
        if o.hide_render: continue
        e = o.evaluated_get(dg); m = e.to_mesh(); pts += [e.matrix_world @ v.co for v in m.vertices]; e.to_mesh_clear()
    return Vector((min(p[i] for p in pts) for i in range(3))), Vector((max(p[i] for p in pts) for i in range(3)))
def shot(tag):
    mn, mx = bounds(); c = (mn + mx) / 2; s = max(mx - mn) * 1.1; print("BOUNDS", tag, [round(x, 2) for x in mn], [round(x, 2) for x in mx])
    cam.ortho_scale = s
    for nm, Z, Y in (("front", Vector((0, -1, 0)), Vector((0, 0, 1))), ("side", Vector((1, 0, 0)), Vector((0, 0, 1))), ("top", Vector((0, 0, 1)), Vector((0, 1, 0)))):
        X = Y.cross(Z); co.matrix_world = Matrix.Translation(c + Z * 20) @ Matrix((X, Y, Z)).transposed().to_4x4()
        sc.render.filepath = os.path.join(out, f"dbg_{tag}_{nm}.png"); bpy.ops.render.render(write_still=True)
arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
acts = [x.name for x in bpy.data.actions]; print("ACTIONS", acts)
for ar in arms: ar.data.pose_position = "REST"
sc.frame_set(1); shot("rest")
for ar in arms: ar.data.pose_position = "POSE"
sc.frame_set(1); shot("pose")
print("DONE")
