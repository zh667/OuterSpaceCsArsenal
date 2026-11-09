# Runs ON WINDOWS. round0/render/<gun>/{s,b,f}.png (Blender, original mesh+textures) -> game weapon sprites in two styles:
#   A 原比例: the render scaled down with the gun's real proportions, 1-px dark outline
#   B 游戏比例: stretched to the box the game's own guns use (chunky, like gun/AK47s 66x32), then pixelized
# Each sprite is put on the canvas of the game's template attachment the mod copies (gun/870*, handgun/jg2*), so
# whatever size Spine keeps from the template, the gun sits where the game's own guns sit.
#   python make_sprites.py <round dir>
import os, sys, json, base64, io
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelize import stylize_sprite, _outline

RD = sys.argv[1] if len(sys.argv) > 1 else r"E:\projects\os-cs-arsenal\round0"
REF = os.path.join(RD, "reference")
# template canvases (allrr.atlas orig size) and the content box the game's guns occupy in them
KIND = {
    "gun":     {"canvas": {"s": (124, 52), "b": (22, 56), "f": (18, 56)}, "box_s": (30, 8, 62, 30),
                "B": {"s": (60, 28), "b": (14, 52), "f": (14, 52)}, "A_long": {"s": 62, "b": 54, "f": 54}},
    "handGun": {"canvas": {"s": (58, 40), "b": (20, 40), "f": (18, 50)}, "box_s": (0, 0, 58, 40),
                "B": {"s": (50, 36), "b": (16, 36), "f": (14, 46)}, "A_long": {"s": 52, "b": 38, "f": 48}},
}
GUNS = {"glock18": "handGun", "usp_silencer": "handGun", "hkp2000": "handGun", "p250": "handGun", "elite": "handGun",
        "taser": "handGun", "ak47": "gun"}
CN = {"glock18": "格洛克 18", "usp_silencer": "USP 消音版", "hkp2000": "P2000", "p250": "P250", "elite": "双持贝瑞塔（单把）",
      "taser": "宙斯 x27", "ak47": "AK-47"}
REFS = {"gun": "gun__AK47", "handGun": "handgun__p250"}

def unpremul_down(img, W, H):
    """area-average downscale in premultiplied space, hard alpha"""
    k = 8
    big = img.convert("RGBA").resize((W * k, H * k), Image.LANCZOS)
    a = np.asarray(big).astype(np.float32) / 255
    A = a[..., 3].reshape(H, k, W, k).mean(axis=(1, 3))
    pre = (a[..., :3] * a[..., 3:4]).reshape(H, k, W, k, 3).sum(axis=(1, 3))
    C = pre / np.maximum(a[..., 3].reshape(H, k, W, k).sum(axis=(1, 3)), 1e-6)[..., None]
    out = np.zeros((H, W, 4), np.uint8); out[..., :3] = np.clip(C * 255, 0, 255); out[..., 3] = (A > 0.45) * 255
    return Image.fromarray(out, "RGBA")

def trim(im):
    bb = im.getchannel("A").getbbox(); return im.crop(bb) if bb else im

def on_canvas(im, kind, view):
    cw, ch = KIND[kind]["canvas"][view]
    c = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    if view == "s":
        bx, by, bw, bh = KIND[kind]["box_s"]; cx, cy = bx + bw / 2, by + bh / 2
        if kind == "gun" and im.width > bw: cx = bx + im.width / 2          # stock stays where the game's stocks start
        if kind == "handGun": cx = min(max(cx, im.width / 2), cw - im.width / 2)
    else:
        cx, cy = cw / 2, ch / 2
    c.paste(im, (round(cx - im.width / 2), round(cy - im.height / 2)), im)
    return c

def style_A(src, kind, view):
    """original textures, no pixelizing. Side keeps the real proportions; the back/front views (seen almost end-on, so
    1-2 px wide at real proportions) are widened to the width the game's own guns use there (KIND B widths)."""
    L = KIND[kind]["A_long"][view] - 2
    if view == "s": W, H = L, max(1, round(src.height * L / src.width))
    else: H, W = L, KIND[kind]["B"][view][0] - 2
    return _outline(trim(unpremul_down(src, W, H)), (20, 18, 18), 1)

# bigger / chunkier variants of A (user 11-09: guns in hand look out of place and small). Original textures, no palette;
# side: width w, height = real height * exaggeration; back/front: full template height, wider. Canvas caps the size:
# long guns 124x52 (side) / 22x56 (back) / 18x56 (front); pistols 58x40 / 20x40 / 18x50.
VARIANTS = {
    "A2": {"gun": {"s": (78, 1.35), "b": (16, 54), "f": (14, 54)}, "handGun": {"s": (56, 1.15), "b": (16, 38), "f": (14, 48)}, "pop": 1.15},
    "A3": {"gun": {"s": (92, 1.6), "b": (18, 54), "f": (16, 54)}, "handGun": {"s": (58, 1.3), "b": (18, 38), "f": (16, 48)}, "pop": 1.25},
    # A3 size, values pulled into the range of the game's own guns (dark gunmetal), pure black outline
    "A4": {"gun": {"s": (92, 1.6), "b": (18, 54), "f": (16, 54)}, "handGun": {"s": (58, 1.3), "b": (18, 38), "f": (16, 48)}, "pop": 1.2, "tone": (0.07, 0.72, 1.25, 0.9)},
}

def tone(im, lo, hi, gamma, sat):
    """luminance remapped into [lo, hi] with a gamma (darker mid-tones), chroma kept (scaled by sat)"""
    a = np.asarray(im).astype(np.float32) / 255; rgb = a[..., :3]
    L = (0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2])[..., None]
    L2 = lo + (hi - lo) * np.clip(L, 0, 1) ** gamma
    a[..., :3] = np.clip(L2 + (rgb - L) * sat, 0, 1)
    return Image.fromarray((a * 255).astype(np.uint8), "RGBA")

def pop(im, k):
    """contrast and saturation up around the sprite's own mean (keeps the original colours, just livelier)"""
    a = np.asarray(im).astype(np.float32) / 255; al = a[..., 3] > 0
    if not al.any(): return im
    rgb = a[..., :3]; m = rgb[al].mean(axis=0)
    rgb = np.clip(m + (rgb - m) * k, 0, 1)
    L = (0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2])[..., None]
    rgb = np.clip(L + (rgb - L) * k, 0, 1)
    a[..., :3] = rgb
    return Image.fromarray((a * 255).astype(np.uint8), "RGBA")

def style_V(name):
    def f(src, kind, view):
        v = VARIANTS[name]; w, x = v[kind][view]
        if view == "s":
            W = w - 2; H = max(1, round(src.height * W / src.width * x))
            H = min(H, KIND[kind]["canvas"]["s"][1] - 2)
        else:
            W, H = w - 2, x - 2
        im = pop(unpremul_down(src, W, H), v["pop"])
        if "tone" in v: im = tone(im, *v["tone"])
        return _outline(trim(im), (0, 0, 0) if "tone" in v else (10, 8, 8), 1)
    return f

def style_B(src, kind, view):
    W, H = KIND[kind]["B"][view]
    return trim(stylize_sprite(src, W / src.width, H / src.height, colors=16, lo=0.05, hi=0.93, gamma=1.3, sat=1.15,
                               edge_dark=0.62, edge_thr=0.16, outline=(20, 18, 18)))

def zoom(im, s=6):
    return im.resize((im.width * s, im.height * s), Image.NEAREST)

def b64(im):
    buf = io.BytesIO(); im.save(buf, "PNG"); return base64.b64encode(buf.getvalue()).decode()

def main():
    out = os.path.join(RD, "sprites"); rows = []; report = {}
    rows.append(("h", "游戏原版参照（游戏自带的枪，不是我们做的）"))
    for kind in ("handGun", "gun"):
        ref = [trim(Image.open(os.path.join(REF, REFS[kind] + v + ".png")).convert("RGBA")) for v in "sbf"]
        rows.append(("r", {"gun": "原版「阿卡突突枪」", "handGun": "原版「标准手枪」"}[kind], ref))
    rows.append(("h", "CS-1 六把 + AK-47（画风 A：原模型原贴图）"))
    for gun, kind in GUNS.items():
        ims = {}
        for st, fn in (("A", style_A), ("B", style_B)):
            ims[st] = []
            for v in "sbf":
                src = Image.open(os.path.join(RD, "render", gun, v + ".png")).convert("RGBA")
                sp = fn(src, kind, v)
                os.makedirs(os.path.join(out, st), exist_ok=True)
                on_canvas(sp, kind, v).save(os.path.join(out, st, f"{gun}_{v}.png"))
                report[f"{st}/{gun}_{v}"] = list(sp.size); ims[st].append(sp)
        rows.append(("r", CN[gun], ims["A"]))
        if gun in ("glock18", "ak47"):
            for st in ("A2", "A3", "A4"):
                vs = []
                for v in "sbf":
                    src = Image.open(os.path.join(RD, "render", gun, v + ".png")).convert("RGBA")
                    sp = style_V(st)(src, kind, v)
                    os.makedirs(os.path.join(out, st), exist_ok=True)
                    on_canvas(sp, kind, v).save(os.path.join(out, st, f"{gun}_{v}.png"))
                    report[f"{st}/{gun}_{v}"] = list(sp.size); vs.append(sp)
                rows.append(("r", CN[gun] + " " + st, vs))
    html = ['<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>画风对比</title><style>',
            'body{background:#3a3f45;color:#eee;font:15px "Microsoft YaHei",sans-serif;margin:20px}h2{margin:28px 0 8px}',
            '.row{display:flex;gap:28px;align-items:flex-end;margin:10px 0;padding:10px;background:#4a5058;border-radius:6px}',
            '.lab{width:170px;flex:none}.cell{text-align:center;font-size:12px;color:#ccc}img{image-rendering:pixelated;display:block;margin-bottom:4px}',
            '.note{color:#ccc;max-width:900px;line-height:1.6}</style></head><body><h1>CS 枪械第 0 轮：CS-1 六把（画风 A）</h1>',
            '<p class="note">每行依次是侧面（斜向也用它）、背面（枪口朝上）、正面（枪口朝下），放大 6 倍，括号里是实际像素。'
            '画风 A：CS2 原模型原贴图，侧面按真实比例缩小；背面/正面几乎是顺着枪管看，按真实比例只有 1～2 像素宽，所以加宽到原版枪的宽度。进游戏的图在 round0/sprites/A（已按原版挂件模板的画布对齐）。</p>']
    for r in rows:
        if r[0] == "h": html.append(f"<h2>{r[1]}</h2>"); continue
        html.append(f'<div class="row"><div class="lab">{r[1]}</div>')
        for v, im in zip(("侧面", "背面", "正面"), r[2]):
            html.append(f'<div class="cell"><img src="data:image/png;base64,{b64(zoom(im))}">{v} {im.width}×{im.height}</div>')
        html.append("</div>")
    html.append("</body></html>")
    open(os.path.join(RD, "CS-1六把_画风A.html"), "w", encoding="utf-8").write("\n".join(html))
    # one JPEG of the same thing for a quick look
    print(json.dumps(report))

if __name__ == "__main__":
    main()
