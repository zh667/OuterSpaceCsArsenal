import bpy, sys
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=a[0])
for o in bpy.data.objects:
    if o.type == "ARMATURE":
        names = [b.name for b in o.data.bones]
        print("ARM", o.name, len(names), [n for n in names if any(k in n.lower() for k in ("hand", "grip", "trigger", "weapon", "mag", "muzzle", "attach"))][:40])
    elif o.type == "EMPTY":
        print("EMPTY", o.name)
