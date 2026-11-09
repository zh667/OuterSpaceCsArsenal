# Runs ON WINDOWS. Talks to the debug channel of a running isolated test (python tools/run_isolated.py --tests debug).
#   python tools/dbg.py send '{"cmd":"get","target":"hero:0","path":"heroBean.Name"}'   one command or a JSON list
#   python tools/dbg.py send --file cmds.json [--timeout 120]
#   python tools/dbg.py snippet probe.cs [--timeout 120]   compile a C# file against the game + this mod, load it in
#                                                           the game: [HarmonyPatch] classes are applied, a static
#                                                           Snippet.Run() is called and its return value printed
#   python tools/dbg.py ready                                is a test game serving commands?
# Commands are listed at the top of src/Kit/DebugChannel.cs. Screenshots are also saved as a small JPEG next to the PNG.
import json, os, re, shutil, subprocess, sys, time, uuid

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RUNTIME = r"E:\projects\outer-space-modkit\runtime"
CH = os.path.join(RUNTIME, "csdebug")
MANAGED = r"E:\SteamLibrary\steamapps\common\Outer Space\Outer Space_Data\Managed"
MOD = json.load(open(os.path.join(ROOT, "mod.json"), encoding="utf-8"))


def send(cmds, timeout):
    if not os.path.exists(os.path.join(CH, "ready.txt")):
        sys.exit("no debug channel: start python tools/run_isolated.py --tests debug first")
    beat = os.path.join(RUNTIME, "selftest-heartbeat.txt")
    if not os.path.exists(beat) or time.time() - os.path.getmtime(beat) > 20:
        sys.exit("the test game is not running (no heartbeat for 20 s): start python tools/run_isolated.py --tests debug")
    out = []
    for c in cmds:   # one at a time, in order
        cid = time.strftime("%H%M%S") + "-" + uuid.uuid4().hex[:6]
        tmp = os.path.join(CH, "in", cid + ".tmp")
        with open(tmp, "w", encoding="utf-8") as f: json.dump(c, f, ensure_ascii=False)
        os.replace(tmp, os.path.join(CH, "in", cid + ".json"))
        res = os.path.join(CH, "out", cid + ".json"); t0 = time.time()
        wait = timeout + float(c.get("seconds", 0) or 0)
        while not os.path.exists(res) and time.time() - t0 < wait: time.sleep(0.2)
        if not os.path.exists(res):
            r = {"id": cid, "cmd": c.get("cmd"), "error": "no answer in %d s (game frozen, busy, or the channel stopped)" % wait}
        else:
            time.sleep(0.05); r = json.load(open(res, encoding="utf-8")); os.remove(res)
        if r.get("file") and os.path.exists(r["file"]):
            try:
                from PIL import Image
                im = Image.open(r["file"]).convert("RGB"); im.thumbnail((1200, 1200))
                jp = r["file"][:-4] + ".jpg"; im.save(jp, quality=85); r["jpg"] = jp
            except Exception as e: r["jpgError"] = repr(e)
        print(json.dumps(r, ensure_ascii=False, indent=1)); out.append(r)
        if r.get("error") and "no answer" in r["error"]: break
    return out


def snippet(cs, timeout):
    name = "dbgsnip_" + time.strftime("%H%M%S") + "_" + uuid.uuid4().hex[:4]   # a fresh name: a loaded assembly can't be replaced
    work = os.path.join(ROOT, "obj", "dbg", name); os.makedirs(work, exist_ok=True)
    shutil.copy2(cs, os.path.join(work, "Snippet.cs"))
    proj = open(os.path.join(ROOT, MOD["csproj"]), encoding="utf-8").read()
    proj = re.sub(r"<AssemblyName>.*?</AssemblyName>", "<AssemblyName>" + name + "</AssemblyName>", proj)
    proj = re.sub(r"<RootNamespace>.*?</RootNamespace>", "<RootNamespace>DbgSnippet</RootNamespace>", proj)
    proj = proj.replace(r"lib\0Harmony.dll", os.path.join(ROOT, "lib", "0Harmony.dll"))
    proj = re.sub(r'<Compile Remove="[^"]*" />', "", proj)
    mod_dll = os.path.join(ROOT, "bin", "Release", MOD["dll"])
    proj = proj.replace("</Project>", '  <ItemGroup><Reference Include="%s"><HintPath>%s</HintPath><Private>false</Private></Reference></ItemGroup>\n</Project>'
                        % (MOD["dll"][:-4], mod_dll))
    open(os.path.join(work, name + ".csproj"), "w", encoding="utf-8").write(proj)
    r = subprocess.run(["dotnet", "build", name + ".csproj", "-c", "Release", "-p:GameManaged=" + MANAGED, "-nologo", "-v:minimal"],
                       capture_output=True, text=True, cwd=work, creationflags=0x08000000)
    if r.returncode:
        print(r.stdout[-5000:], r.stderr[-2000:]); sys.exit("snippet build failed")
    dll = os.path.join(work, "bin", "Release", name + ".dll")
    print("built", dll, os.path.getsize(dll))
    return send([{"cmd": "load", "dll": dll}], timeout)


if __name__ == "__main__":
    a = sys.argv[1:]
    timeout = 60
    if "--timeout" in a: i = a.index("--timeout"); timeout = float(a[i + 1]); del a[i:i + 2]
    if not a or a[0] == "ready":
        p = os.path.join(CH, "ready.txt"); print("ready since " + open(p).read() if os.path.exists(p) else "not ready"); sys.exit()
    if a[0] == "send":
        text = open(a[2], encoding="utf-8").read() if a[1] == "--file" else a[1]
        c = json.loads(text); send(c if isinstance(c, list) else [c], timeout)
    elif a[0] == "snippet": snippet(a[1], timeout)
    else: sys.exit(__doc__)
