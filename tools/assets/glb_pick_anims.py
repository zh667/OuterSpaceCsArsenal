# Runs ON WINDOWS. Copies a GLB keeping every mesh/skin/node/image but only the animations whose name matches.
# The CS2 agent exports carry ~2000 clips (~1 GB); Blender only needs a few.
#   python glb_pick_anims.py <in.glb> <out.glb> <regex>
import json, re, struct, sys
src, dst, pat = sys.argv[1], sys.argv[2], re.compile(sys.argv[3])
with open(src, "rb") as f:
    magic, ver, total = struct.unpack("<III", f.read(12))
    jl, jt = struct.unpack("<II", f.read(8)); js = json.loads(f.read(jl))
    bl, bt = struct.unpack("<II", f.read(8)); binoff = f.tell()
    js["animations"] = [a for a in js.get("animations", []) if pat.search(a.get("name", ""))]
    used_acc = set()
    for m in js.get("meshes", []):
        for p in m.get("primitives", []):
            used_acc |= set(p.get("attributes", {}).values())
            if "indices" in p: used_acc.add(p["indices"])
            for t in p.get("targets", []): used_acc |= set(t.values())
    for s in js.get("skins", []):
        if "inverseBindMatrices" in s: used_acc.add(s["inverseBindMatrices"])
    for a in js["animations"]:
        for s in a["samplers"]: used_acc |= {s["input"], s["output"]}
    used_bv = {js["accessors"][i]["bufferView"] for i in used_acc if "bufferView" in js["accessors"][i]}
    for im in js.get("images", []):
        if "bufferView" in im: used_bv.add(im["bufferView"])
    newbin = bytearray(); remap = {}
    for i in sorted(used_bv):
        bv = js["bufferViews"][i]
        f.seek(binoff + bv.get("byteOffset", 0)); data = f.read(bv["byteLength"])
        while len(newbin) % 8: newbin.append(0)
        nb = dict(bv); nb["byteOffset"] = len(newbin); newbin += data
        remap[i] = len(remap); js.setdefault("_nbv", []).append(nb)
js["bufferViews"] = js.pop("_nbv")
acc_map = {}; accs = []
for i, a in enumerate(js["accessors"]):
    if i in used_acc:
        a = dict(a)
        if "bufferView" in a: a["bufferView"] = remap[a["bufferView"]]
        if "sparse" in a: raise SystemExit("sparse accessors not handled")
        acc_map[i] = len(accs); accs.append(a)
js["accessors"] = accs
for m in js.get("meshes", []):
    for p in m.get("primitives", []):
        p["attributes"] = {k: acc_map[v] for k, v in p["attributes"].items()}
        if "indices" in p: p["indices"] = acc_map[p["indices"]]
        if "targets" in p: p["targets"] = [{k: acc_map[v] for k, v in t.items()} for t in p["targets"]]
for s in js.get("skins", []):
    if "inverseBindMatrices" in s: s["inverseBindMatrices"] = acc_map[s["inverseBindMatrices"]]
for a in js["animations"]:
    for s in a["samplers"]: s["input"] = acc_map[s["input"]]; s["output"] = acc_map[s["output"]]
for im in js.get("images", []):
    if "bufferView" in im: im["bufferView"] = remap[im["bufferView"]]
js["buffers"] = [{"byteLength": len(newbin)}]
jb = json.dumps(js, separators=(",", ":")).encode()
while len(jb) % 4: jb += b" "
while len(newbin) % 4: newbin.append(0)
with open(dst, "wb") as o:
    o.write(struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(jb) + 8 + len(newbin)))
    o.write(struct.pack("<II", len(jb), 0x4E4F534A)); o.write(jb)
    o.write(struct.pack("<II", len(newbin), 0x004E4942)); o.write(newbin)
print("kept", len(js["animations"]), "animations:", [a["name"] for a in js["animations"]], "bytes", len(newbin))
