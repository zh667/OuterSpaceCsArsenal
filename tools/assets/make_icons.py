# Runs ON WINDOWS. CS2 pictures for the game's icons (user 11-10: "改成那种 CS2 的"):
#   res/Resources/Textures/icons/<gun>.png   item icons: the CS2 inventory picture of each gun (panorama econ
#                                            base_weapons/weapon_<name>_png), ITEM_STYLE "diag" = turned 35 degrees so
#                                            long guns use the square slot, "flat" = as CS2 shows it
#   res/Resources/Textures/tech/<tech>.png   tech node icons: the CS2 HUD silhouette (panorama/images/icons/equipment
#                                            <name>.vsvg_c, an SVG inside) of one gun per tier, white on opaque black
#                                            like the game's Icon_Technology atlas (src/CsTechIcons.cs)
#   python tools/assets/make_icons.py [--style diag|flat]
# make_agent_assets.py calls item_icon() and write_tech() when it rebuilds the resources.
import io, os, subprocess, sys
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "res" / "Resources" / "Textures"
ECON = Path(r"E:\projects\CSMCReverse\local_cs2_analysis\all_weapons\11_icons\panorama\images\econ\weapons\base_weapons")
VPK = Path(r"E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\csgo\pak01_dir.vpk")
VRF = Path(r"E:\projects\ScCsgoKnives\.tmp-survival-tools\vrf\Source2Viewer-CLI.exe")
HUD = ROOT / "round0" / "cs2_hud_raw"   # raw .vsvg_c files (VRF cannot decompile most of them; the SVG text is inside)
ITEM_STYLE = "diag"
ICON = 64
NAME = {"glock18": "glock", "m4a4": "m4a1"}   # our key -> CS2 file name where they differ
TECH = {"cs-1": "usp_silencer", "cs-2": "mp9", "cs-3": "ak47", "cs-4": "awp"}
TECH_SIZE, TECH_FILL = 128, 0.92
ARMOUR = {"kevlar": "kevlar", "helmet": "helmet"}   # CS2 has no inventory picture for armour, only HUD silhouettes (as in its buy menu)


def _fit(im, w, h, rot=0):
    bb = im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox(); im = im.crop(bb)
    if rot:
        im = im.rotate(rot, resample=Image.BICUBIC, expand=True)
        bb = im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox(); im = im.crop(bb)
    s = min(w / im.width, h / im.height)
    im = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)
    c = Image.new("RGBA", (w, h), (0, 0, 0, 0)); c.alpha_composite(im, ((w - im.width) // 2, (h - im.height) // 2))
    return c


def item_icon(gun, style=None):
    style = style or ITEM_STYLE
    src = ECON / f"weapon_{NAME.get(gun, gun)}_png.png"
    im = Image.open(src).convert("RGBA")
    return _fit(im, ICON - 2, ICON - 2, rot=35 if style == "diag" else 0).crop((-1, -1, ICON - 1, ICON - 1))


def _svg(name):
    f = HUD / "panorama" / "images" / "icons" / "equipment" / f"{name}.vsvg_c"
    if not f.exists():
        subprocess.run([str(VRF), "-i", str(VPK), "-o", str(HUD), "--vpk_filepath", "panorama/images/icons/equipment/",
                        "--vpk_extensions", "vsvg_c"], check=True, capture_output=True, creationflags=0x08000000)
    b = f.read_bytes(); return b[b.find(b"<svg"):b.rfind(b"</svg>") + 6]


def tech_texture(name):
    import pymupdf
    pg = pymupdf.open(stream=_svg(name), filetype="svg")[0]; z = 800 / pg.rect.width
    im = Image.open(io.BytesIO(pg.get_pixmap(matrix=pymupdf.Matrix(z, z), alpha=True).tobytes("png"))).convert("RGBA")
    a = im.getchannel("A"); a = a.crop(a.getbbox())
    S = TECH_SIZE; w = round(S * TECH_FILL); h = round(a.height * w / a.width)
    if h > S * TECH_FILL:
        h = round(S * TECH_FILL); w = round(a.width * h / a.height)
    g = Image.new("L", (S, S), 0); g.paste(a.resize((w, h), Image.LANCZOS), ((S - w) // 2, (S - h) // 2))
    return Image.merge("RGBA", (g, g, g, Image.new("L", (S, S), 255)))


def _svg_alpha(name, width_px):
    import pymupdf
    pg = pymupdf.open(stream=_svg(name), filetype="svg")[0]; z = width_px / pg.rect.width
    im = Image.open(io.BytesIO(pg.get_pixmap(matrix=pymupdf.Matrix(z, z), alpha=True).tobytes("png"))).convert("RGBA")
    a = im.getchannel("A"); return a.crop(a.getbbox())


def armour_icon(key):
    from PIL import ImageFilter
    a = _svg_alpha(ARMOUR[key], 400); S = ICON; f = 0.82
    w = round(S * f); h = round(a.height * w / a.width)
    if h > S * f:
        h = round(S * f); w = round(a.width * h / a.height)
    a = a.resize((w, h), Image.LANCZOS); m = Image.new("L", (S, S), 0); m.paste(a, ((S - w) // 2, (S - h) // 2))
    edge = m.filter(ImageFilter.MaxFilter(3))
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(Image.new("RGBA", (S, S), (40, 42, 46, 255)), (0, 0), edge)      # dark rim so it reads on light slots
    out.paste(Image.new("RGBA", (S, S), (222, 224, 226, 255)), (0, 0), m)    # CS2 HUD grey-white
    return out


def write_tech():
    d = OUT / "tech"; d.mkdir(parents=True, exist_ok=True)
    for tid, name in TECH.items():
        tech_texture(name).save(d / f"{tid}.png")
    print("tech icons", sorted(TECH))


if __name__ == "__main__":
    style = sys.argv[sys.argv.index("--style") + 1] if "--style" in sys.argv else ITEM_STYLE
    d = OUT / "icons"; d.mkdir(parents=True, exist_ok=True)
    guns = sorted(p.stem for p in d.glob("*.png") if p.stem not in ARMOUR)
    for g in guns:
        item_icon(g, style).save(d / f"{g}.png")
    print("item icons", len(guns), style)
    for k in ARMOUR:
        armour_icon(k).save(d / f"{k}.png")
    print("armour icons", sorted(ARMOUR))
    write_tech()
