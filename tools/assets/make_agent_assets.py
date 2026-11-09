# Runs ON WINDOWS. Round-0 resources for res/Resources/Textures (packed by tools/build.py):
#   weapons/<gun>_{s,b,f}.png          style A gun sprites (round0/sprites/A)
#   agents/route1/<agent>/<part>.png   route 1: a vanilla outfit's parts recoloured with the agent's colours
#   agents/route2/<agent>_{idle,walk}_<i>.png  route 2: frames rendered from the CS2 agent, scaled to the game's pixel size
#   role-customize.xml                 the game's mod interface for skins and weapons
#   python make_agent_assets.py
import os, sys, json
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0, r"E:\projects\analysis\outer-space-20261101")
from extract_tables import read_table
HERE = Path(__file__).resolve().parent; ROOT = HERE.parent.parent
sys.path.insert(0, str(HERE))
from pixelize import _outline
GAME = Path(r"E:\SteamLibrary\steamapps\common\Outer Space\Outer Space_Data\StreamingAssets")
R0 = ROOT / "round0"; OUT = ROOT / "res" / "Resources" / "Textures"
AGENTS = {  # agent: (render folder, base outfit per part type, skin names)
    "sas": ("ctm_sas", {"top": "fangdan", "bottoms": "fangdan", "helmet": "fangdan"},
            {"top": "CSA_SAS_TOP", "bottoms": "CSA_SAS_BOTTOMS", "helmet": "CSA_SAS_HELMET"}),
    "phoenix": ("tm_phoenix", {"top": "jiake", "bottoms": "jiake", "helmet": "fangdan"},
                {"top": "CSA_PHX_TOP", "bottoms": "CSA_PHX_BOTTOMS", "helmet": "CSA_PHX_HELMET"}),
}
# CS-1 pistols, style A4 (user 11-09). Attachment name = "csa_" + gun stem (CsData.GunSpec.Key)
GUNS = [("csa_glock18", "glock18", "handGun", "A4"), ("csa_usp_silencer", "usp_silencer", "handGun", "A4"),
        ("csa_hkp2000", "hkp2000", "handGun", "A4"), ("csa_p250", "p250", "handGun", "A4"),
        ("csa_elite", "elite", "handGun", "A4"), ("csa_taser", "taser", "handGun", "A4")]
# CS-2 (pistols, SMGs, shotguns)
GUNS += [("csa_" + k, k, kind, "A4") for k, kind in [("tec9", "handGun"), ("fiveseven", "handGun"), ("cz75a", "handGun"), ("deagle", "handGun"), ("revolver", "handGun"), ("mac10", "gun"), ("mp9", "gun"), ("mp7", "gun"), ("mp5sd", "gun"), ("ump45", "gun"), ("bizon", "gun"), ("nova", "gun"), ("sawedoff", "gun"), ("mag7", "gun"), ("xm1014", "gun")]]
# CS-3 (rifles, P90, SSG 08)
GUNS += [("csa_" + k, k, "gun", "A4") for k in ["p90", "galilar", "famas", "ak47", "m4a4", "m4a1_silencer", "ssg08", "sg556", "aug"]]
WITH_AGENTS = False   # agent outfits (route 1) come back with the agents round
ICON = 64
DIRNAME = {"00_side": "side", "00_3": "3", "00_9": "9", "00_front": "front", "00_back": "back"}

def atlas():
    L = open(GAME / "spine" / "allrr.atlas", encoding="utf-8").read().splitlines()
    reg = {}; cur = None; page = None
    for l in L:
        if not l.strip(): cur = None; page = None; continue
        if ":" not in l and page is None: page = l.strip(); continue
        if ":" not in l: cur = l.strip(); reg[cur] = {}; continue
        k, v = [x.strip() for x in l.split(":", 1)]
        if cur: reg[cur][k] = v
    return reg, Image.open(GAME / "spine" / "allrr.png").convert("RGBA")

def crop(reg, img, name):
    r = reg[name]; x, y, w, h = map(int, r["bounds"].split(","))
    rot = r.get("rotate") in ("90", "true")
    c = img.crop((x, y, x + (h if rot else w), y + (w if rot else h)))
    if rot: c = c.rotate(90, expand=True)
    a = np.asarray(c).astype(np.float32); al = a[..., 3:4] / 255
    a[..., :3] = np.where(al > 0, np.clip(a[..., :3] / np.maximum(al, 1e-3), 0, 255), 0)   # atlas is premultiplied
    c = Image.fromarray(a.astype(np.uint8), "RGBA")
    if "offsets" in r:
        ox, oy, ow, oh = map(int, r["offsets"].split(","))
        cv = Image.new("RGBA", (ow, oh), (0, 0, 0, 0)); cv.paste(c, (ox, oh - oy - c.height)); return cv
    return c

def lum(a): return 0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]

def agent_colours(front_png):
    """median colours of the helmet / torso / legs bands of the front render (centre 40 % of the width)"""
    a = np.asarray(Image.open(front_png).convert("RGBA")).astype(np.float32) / 255
    ys, xs = np.nonzero(a[..., 3] > 0.5); y0, y1, x0, x1 = ys.min(), ys.max(), xs.min(), xs.max(); H = y1 - y0; W = x1 - x0
    def band(t0, t1, wfrac=0.4):
        sub = a[int(y0 + t0 * H):int(y0 + t1 * H), int(x0 + (0.5 - wfrac / 2) * W):int(x0 + (0.5 + wfrac / 2) * W)]
        px = sub[sub[..., 3] > 0.5][:, :3]
        return np.median(px, axis=0) if len(px) else np.array([0.3, 0.3, 0.3])
    return {"helmet": band(0.0, 0.10), "top": band(0.18, 0.45), "bottoms": band(0.58, 0.85)}

def recolour(part, colour):
    a = np.asarray(part).astype(np.float32) / 255
    al = a[..., 3] > 0.05; L = lum(a)
    body = al & (L > 0.12)                        # keep the dark outline pixels as they are
    if body.sum() == 0: return part
    lo, hi = np.percentile(L[body], 10), np.percentile(L[body], 90)
    t = np.clip((L - lo) / max(hi - lo, 1e-3), 0, 1)
    c = np.array(colour, np.float32)
    lc = 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]
    if lc < 0.2: c = c + (0.2 - lc)               # near-black gear: lift it so the shading stays readable
    new = np.clip(c[None, None, :] * (0.55 + 0.75 * t[..., None]), 0, 1)
    out = a.copy(); out[body, :3] = new[body]
    return Image.fromarray((out * 255).astype(np.uint8), "RGBA")

def route2(agent, folder, px_h=70):
    src = R0 / "agents" / "render" / folder; dst = OUT / "agents" / "route2"; dst.mkdir(parents=True, exist_ok=True)
    n = 0
    for clip, tag in (("idle_rifle", "idle"), ("walk_n_rifle", "walk")):
        i = 0
        while (src / f"{clip}_{i}.png").exists():
            im = Image.open(src / f"{clip}_{i}.png").convert("RGBA")
            W = round(im.width * px_h / im.height)
            k = 8; big = im.resize((W * k, px_h * k), Image.LANCZOS)
            a = np.asarray(big).astype(np.float32) / 255
            A = a[..., 3].reshape(px_h, k, W, k).mean(axis=(1, 3))
            pre = (a[..., :3] * a[..., 3:4]).reshape(px_h, k, W, k, 3).sum(axis=(1, 3))
            C = pre / np.maximum(a[..., 3].reshape(px_h, k, W, k).sum(axis=(1, 3)), 1e-6)[..., None]
            o = np.zeros((px_h, W, 4), np.uint8); o[..., :3] = np.clip(C * 255, 0, 255); o[..., 3] = (A > 0.45) * 255
            _outline(Image.fromarray(o, "RGBA"), (20, 18, 18), 1).save(dst / f"{agent}_{tag}_{i}.png"); i += 1; n += 1
    return n

def main():
    reg, img = atlas()
    rows = read_table(GAME / "def" / "S_CharacterSpineParts.fwr")
    xml = ['<?xml version="1.0" encoding="utf-8"?>', '<ModXmlSpine xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">', "\t<skins>"]
    report = {}
    for agent, (folder, base, skins) in (AGENTS.items() if WITH_AGENTS else []):
        cols = agent_colours(R0 / "agents" / "render" / folder / "front.png")
        report[agent] = {"colours": {k: [round(float(x), 3) for x in v] for k, v in cols.items()}, "parts": {}}
        d = OUT / "agents" / "route1" / agent; d.mkdir(parents=True, exist_ok=True)
        for ptype in ("top", "bottoms", "helmet"):
            xml.append(f'\t\t<ModSpineSkin Name="{skins[ptype]}" SkinType="{ptype}">')
            for r in rows:
                if r["PartType"] != ptype: continue
                pre = r["Slot"].split("-")[0]
                if pre not in DIRNAME: continue
                # the agent's base outfit first, then the armoured set for parts that outfit lacks (jiake has no trousers)
                name = next((n for n in (s + "_" + DIRNAME[pre] + "/" + r["Rack"] for s in (base[ptype], "fangdan")) if n in reg), None)
                if name is None: report[agent]["parts"][r["Key"]] = "no part"; continue
                part = crop(reg, img, name)
                if part.width <= 1: report[agent]["parts"][r["Key"]] = "empty"; continue
                recolour(part, cols[ptype]).save(d / (r["Key"] + ".png"))
                xml.append(f'\t\t\t<PartItem Name="{r["Key"]}" Texture="agents/route1/{agent}/{r["Key"]}.png" />')
                report[agent]["parts"][r["Key"]] = list(part.size)
            xml.append("\t\t</ModSpineSkin>")
        report[agent]["route2_frames"] = route2(agent, folder)
    # The game repacks mod pictures into the character atlas (SpineCombine.loadModSpine -> OptimizeSkin) only when some
    # installed mod defines a skin; without that the weapon attachments point into the wrong atlas and the guns are
    # invisible in hand (11-09: all CS guns vanished once the agent skins were switched off). An empty skin, used by
    # no outfit, makes sure the repack always runs.
    xml.append('\t\t<ModSpineSkin Name="CSA_ATLAS_REPACK" SkinType="adornment">')
    xml.append("\t\t</ModSpineSkin>")
    xml.append("\t</skins>"); xml.append("\t<weapons>")
    wd = OUT / "weapons"; wd.mkdir(parents=True, exist_ok=True)
    import shutil
    shutil.rmtree(OUT / "weapons", ignore_errors=True); shutil.rmtree(OUT / "icons", ignore_errors=True); shutil.rmtree(OUT / "agents", ignore_errors=True)
    wd = OUT / "weapons"; wd.mkdir(parents=True, exist_ok=True); idir = OUT / "icons"; idir.mkdir(parents=True, exist_ok=True)
    for attach, gun, kind, style in GUNS:
        for v in "sbf":
            Image.open(R0 / "sprites" / style / f"{gun}_{v}.png").save(wd / f"{gun}_{v}.png")
        xml.append(f'\t\t<ModSpineWeapon Name="{attach}" WeaponType="{kind}" Texture_T="weapons/{gun}_b.png" Texture_RT="weapons/{gun}_s.png" '
                   f'Texture_S="weapons/{gun}_s.png" Texture_RB="weapons/{gun}_s.png" Texture_B="weapons/{gun}_f.png" />')
        # item icon: the side view, enlarged with whole pixels to fit ICON x ICON
        s = Image.open(R0 / "sprites" / style / f"{gun}_s.png").convert("RGBA"); bb = s.getchannel("A").getbbox(); s = s.crop(bb)
        k = max(1, min((ICON - 4) // s.width, (ICON - 4) // s.height)); s = s.resize((s.width * k, s.height * k), Image.NEAREST)
        ic = Image.new("RGBA", (ICON, ICON), (0, 0, 0, 0)); ic.paste(s, ((ICON - s.width) // 2, (ICON - s.height) // 2), s); ic.save(idir / f"{gun}.png")
    xml += ["\t</weapons>", "</ModXmlSpine>"]
    (OUT / "role-customize.xml").write_text("\n".join(xml), encoding="utf-8")
    (R0 / "agents").mkdir(parents=True, exist_ok=True); json.dump(report, open(R0 / "agents" / "assets_report.json", "w"), indent=1)
    for a, v in report.items(): print(a, v["colours"], "route2 frames", v["route2_frames"], "parts", sum(1 for x in v["parts"].values() if isinstance(x, list)), "missing", [k for k, x in v["parts"].items() if not isinstance(x, list)][:12])

if __name__ == "__main__":
    main()
