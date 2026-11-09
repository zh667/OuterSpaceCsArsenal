# Runs ON WINDOWS. CS2 original weapon sounds -> res/Resources/Sounds (played by src/CsSound.cs instead of the game's
# FMOD events, which a mod cannot add to):
#   <key>_fire_<n>.wav   the gun's WEAPON_SOUND_SINGLE event (up to 3 variants, picked at random in game)
#   <key>_reload.wav     clip out + clip in (+ slide / bolt / pump), or one shell insert for tube-fed shotguns
#   sounds.json          {key: {"fire": [...], "reload": "..."}}
# Mono 22050 Hz 16-bit, trailing silence trimmed, peak-normalised. Source: all_weapons/05_audio (decoded .wav).
#   python make_sounds.py
import glob, json, os, struct, subprocess, tempfile, wave
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(os.path.dirname(HERE))
AW = r"E:\projects\CSMCReverse\local_cs2_analysis\all_weapons"
OUT = os.path.join(ROOT, "res", "Resources", "Sounds")
KEYS = ["glock18", "usp_silencer", "hkp2000", "p250", "elite", "taser", "tec9", "fiveseven", "cz75a", "deagle", "revolver",
        "mac10", "mp9", "mp7", "mp5sd", "ump45", "bizon", "nova", "sawedoff", "mag7", "xm1014",
        "p90", "galilar", "famas", "ak47", "m4a4", "m4a1_silencer", "ssg08", "sg556", "aug"]
SR = 22050
BLENDER = r"E:\Develop\Blender-5.1.0-Beta\blender.exe"   # its audio library (aud, ffmpeg inside) decodes the mp3 sources
CACHE = os.path.join(tempfile.gettempdir(), "csa_sound_cache")
# decode only (Sound.data -> samples) and write the wav with Python: aud's own wav writer fails on some short mp3s
BL_SCRIPT = """
import aud, sys, json, wave
import numpy as np
for src, dst in json.load(open(sys.argv[sys.argv.index('--') + 1], encoding='utf-8')):
    try:
        s = aud.Sound(src); rate = int(s.specs[0]); a = np.asarray(s.data(), dtype=np.float32)
        if a.ndim == 2: a = a.mean(axis=1)
        with wave.open(dst, 'wb') as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(rate); w.writeframes((np.clip(a, -1, 1) * 32767).astype('<i2').tobytes())
    except Exception as e: print('FAILED', src, e)
"""

def as_wav(paths):
    """source path -> a RIFF wav path (mp3 and other files converted once through Blender)"""
    os.makedirs(CACHE, exist_ok=True); out = {}; todo = []
    for p in paths:
        src = p
        if p.lower().endswith(".vsnd"):   # some entries were never decoded: a decoded sibling with the same stem
            stem = p[:-5]; src = next((stem + e for e in (".wav", ".mp3") if os.path.exists(stem + e)), None)
            if src is None: out[p] = None; print("NO DECODED FILE", p); continue
        if open(src, "rb").read(4) == b"RIFF": out[p] = src; continue
        d = os.path.join(CACHE, os.path.basename(os.path.dirname(p)) + "__" + os.path.basename(p) + ".wav"); out[p] = d
        if not os.path.exists(d): todo.append([src, d])
    if todo:
        lst = os.path.join(CACHE, "todo.json"); json.dump(todo, open(lst, "w", encoding="utf-8"))
        sp = os.path.join(CACHE, "conv.py"); open(sp, "w", encoding="utf-8").write(BL_SCRIPT)
        r = subprocess.run([BLENDER, "-b", "--factory-startup", "--python", sp, "--", lst], capture_output=True, text=True, creationflags=0x08000000)
        print("blender converted", len(todo), [l for l in r.stdout.splitlines() if "FAILED" in l][:5])
    for p, d in list(out.items()):
        if d is not None and not os.path.exists(d): print("UNREADABLE", p); out[p] = None
    return out

def read_wav(p):
    b = open(p, "rb").read(); i = 12; fmt = None; data = None
    while i + 8 <= len(b):
        cid = b[i:i + 4]; n = struct.unpack("<I", b[i + 4:i + 8])[0]; body = b[i + 8:i + 8 + n]
        if cid == b"fmt ": fmt = struct.unpack("<HHIIHH", body[:16])
        elif cid == b"data": data = body
        i += 8 + n + (n & 1)
    tag, ch, rate, _, _, bits = fmt
    if tag == 0xFFFE: tag = 3 if bits == 32 and b"\x03\x00\x00\x00\x00\x00\x10\x00" in b[:200] else 1
    if tag == 3: a = np.frombuffer(data, "<f4" if bits == 32 else "<f8").astype(np.float32)
    elif bits == 16: a = np.frombuffer(data[:len(data) // 2 * 2], "<i2").astype(np.float32) / 32768
    elif bits == 24:
        u = np.frombuffer(data[:len(data) // 3 * 3], np.uint8).reshape(-1, 3)
        a = ((u[:, 0].astype(np.int32) | (u[:, 1].astype(np.int32) << 8) | (u[:, 2].astype(np.int32) << 16)) << 8 >> 8).astype(np.float32) / 8388608
    elif bits == 8: a = (np.frombuffer(data, np.uint8).astype(np.float32) - 128) / 128
    else: a = np.frombuffer(data[:len(data) // 4 * 4], "<i4").astype(np.float32) / 2147483648
    a = a[:len(a) // ch * ch].reshape(-1, ch).mean(axis=1)
    if rate != SR: a = np.interp(np.arange(0, len(a), rate / SR), np.arange(len(a)), a).astype(np.float32)
    return a

def trim(a, floor=0.003, max_s=None, fade=0.05):
    nz = np.nonzero(np.abs(a) > floor)[0]
    if len(nz) == 0: return a[:1]
    a = a[nz[0]:nz[-1] + 1]
    if max_s and len(a) > max_s * SR:
        a = a[:int(max_s * SR)].copy(); f = int(fade * SR); a[-f:] *= np.linspace(1, 0, f)
    return a

def write(p, a, peak):
    m = np.abs(a).max() or 1; a = a / m * peak
    with wave.open(p, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes((np.clip(a, -1, 1) * 32767).astype("<i2").tobytes())

def main():
    os.makedirs(OUT, exist_ok=True)
    for f in glob.glob(os.path.join(OUT, "*.wav")): os.remove(f)
    manifests = {}
    for m in glob.glob(os.path.join(AW, "00_catalog", "by_type", "*", "*", "manifest.json")):
        j = json.load(open(m, encoding="utf-8")); manifests[j["identity"]["folder"]] = j
    shoot = {e["event"]: e for e in json.load(open(os.path.join(AW, "05_audio", "shoot-event-mapping.json"), encoding="utf-8"))}
    allev = {}
    for e in json.load(open(os.path.join(AW, "05_audio", "weapon-soundevent-mapping.json"), encoding="utf-8")): allev.setdefault(e["event"], e)
    plan = {}
    for k in KEYS:
        j = manifests.get(k)
        if not j: print(k, "NO MANIFEST"); continue
        ev = (j["weapon_data"].get("shoot_soundevents") or {})
        fire_ev = ev.get("WEAPON_SOUND_SINGLE") or next(iter(ev.values()), None)
        if "silencer" in k:   # USP-S / M4A1-S carry the silencer: their silenced shot, not the bare one
            pre0 = (fire_ev or "").split(".")[0] + "."
            sil = sorted(e for e in list(shoot) + list(allev) if e.startswith(pre0) and "silenc" in e.lower() and "distant" not in e.lower() and "attach" not in e.lower() and "detach" not in e.lower())
            if sil: fire_ev = sil[0]
        files = (shoot.get(fire_ev) or allev.get(fire_ev) or {}).get("decoded_files", [])
        # reload: the gun's own events under the same prefix (Weapon_Glock.Clipout, .Clipin, .Slideback ...)
        pre = (fire_ev or "").split(".")[0] + "."
        mine = {e.split(".", 1)[1].lower(): v for e, v in allev.items() if e.startswith(pre) and v.get("decoded_files")}
        order = ["clipout", "clipin", "boltpull", "boltrelease", "slideback", "sliderelease", "pump"]
        parts = [mine[n] for n in order if n in mine]
        if not parts: parts = [v for n, v in mine.items() if "insert" in n][:1]
        if not parts and files:   # event prefix differs from the gun's (Weapon_CZ75A.Single vs Weapon_CZ75.Clipout): same sound folder
            folder = "/".join(files[0].replace("\\", "/").split("/")[:-1]) + "/"
            byname = {}
            for e, v in allev.items():
                f0 = (v.get("decoded_files") or [""])[0].replace("\\", "/")
                if f0.startswith(folder): byname.setdefault(e.split(".", 1)[-1].lower(), v)
            parts = [byname[n] for n in order if n in byname]
        plan[k] = (fire_ev, [os.path.join(AW, f) for f in files[:3]], [os.path.join(AW, v["decoded_files"][0]) for v in parts[:4]])
    wav = as_wav([p for _, f, r in plan.values() for p in f + r])
    index = {}
    for k, (fire_ev, fsrc, rsrc) in plan.items():
        fires = []
        for i, f in enumerate(fsrc):
            if not wav.get(f): continue
            a = trim(read_wav(wav[f]), max_s=1.4)
            name = f"{k}_fire_{i}.wav"; write(os.path.join(OUT, name), a, 0.8); fires.append(name)
        reload = None
        if rsrc and any(wav.get(f) for f in rsrc):
            gap = np.zeros(int(0.25 * SR), np.float32); seq = []
            for f in rsrc:
                if wav.get(f): seq += [trim(read_wav(wav[f]), max_s=0.8), gap]
            a = np.concatenate(seq[:-1])[:int(2.5 * SR)]
            reload = f"{k}_reload.wav"; write(os.path.join(OUT, reload), a, 0.6)
        index[k] = {"fire": fires, "reload": reload, "event": fire_ev, "reload_parts": [os.path.basename(f) for f in rsrc]}
        print(k, fire_ev, len(fires), "fire,", "reload" if reload else "NO reload", index[k]["reload_parts"])
    json.dump({k: {"fire": v["fire"], "reload": v["reload"]} for k, v in index.items()}, open(os.path.join(OUT, "sounds.json"), "w", encoding="utf-8"), indent=1)
    json.dump(index, open(os.path.join(ROOT, "round0", "sounds_report.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    total = sum(os.path.getsize(f) for f in glob.glob(os.path.join(OUT, "*.wav")))
    print("files", len(glob.glob(os.path.join(OUT, "*.wav"))), "bytes", total)

if __name__ == "__main__":
    main()
