# Runs ON WINDOWS. Downloads a Steam Workshop item WITHOUT subscribing and copies it to a folder.
#   python fetch.py <published id> <out dir> [app id, default 3080070 Outer Space]
# The game only loads subscribed items, so this never adds the item to the real game.
import os, shutil, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__))
GAME = r"E:\SteamLibrary\steamapps\common\Outer Space"
STEAM_API = os.path.join(GAME, "Outer Space_Data", "Plugins", "x86_64", "steam_api64.dll")
NOWIN = 0x08000000
pid, out = sys.argv[1], sys.argv[2]
app = sys.argv[3] if len(sys.argv) > 3 else "3080070"
r = subprocess.run(["dotnet", "build", os.path.join(HERE, "WorkshopFetch.csproj"), "-c", "Release", "-nologo", "-v:minimal"],
                   capture_output=True, text=True, cwd=HERE, creationflags=NOWIN)
print(r.stdout[-2000:], r.stderr[-2000:])
if r.returncode: sys.exit("build failed")
exe_dir = os.path.join(HERE, "bin")
shutil.copy2(STEAM_API, os.path.join(exe_dir, "steam_api64.dll"))
p = subprocess.run([os.path.join(exe_dir, "WorkshopFetch.exe"), "--app", app, "--id", pid, "--out", out],
                   capture_output=True, text=True, encoding="utf-8", errors="replace", cwd=exe_dir, creationflags=NOWIN)
print(p.stdout, p.stderr)
sys.exit(p.returncode)
