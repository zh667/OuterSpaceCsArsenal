import subprocess, os, json, glob, shutil, sys
B=r"E:\Develop\Blender-5.1.0-Beta\blender.exe"; P=r"E:\projects\os-cs-arsenal"; AS=os.path.join(P,"tools","assets"); R0=os.path.join(P,"round0")
M=r"E:\projects\CSMCReverse\local_cs2_analysis\all_weapons\02_models\glb_with_animations\weapons\models"
def run(args,cwd=AS,to=900): return subprocess.run(args,capture_output=True,text=True,encoding='utf-8',errors='replace',cwd=cwd,creationflags=0x08000000,timeout=to)
tilt=40   # matches round 0 (calibrated against the Glock render, 11-09)
# 2. the CS-2 guns
OPTS={"mp9":"islands,flipF","ump45":"islands,flipF","bizon":"islands,flipF","revolver":"islands,pose"}   # R8 rest pose has the grip bent up   # checked by eye on cs2_render_s.jpg
keys=["tec9","fiveseven","cz75a","deagle","revolver","mac10","mp9","mp7","mp5sd","ump45","bizon","nova","sawedoff","mag7","xm1014"]
for k in keys:
    g=sorted(glob.glob(os.path.join(M,k,"weapon_*.glb")))
    if not g: print(k,"NO GLB", os.listdir(os.path.join(M,k)) if os.path.isdir(os.path.join(M,k)) else "no dir"); continue
    out=os.path.join(R0,"render",k); shutil.rmtree(out,ignore_errors=True)
    r=run([B,"-b","--factory-startup","--python","bl_views.py","--",g[0],out,str(tilt),"1024","",OPTS.get(k,"islands")])
    ok=os.path.exists(os.path.join(out,"info.json"))
    info=json.load(open(os.path.join(out,"info.json"))) if ok else {}
    print(k,OPTS.get(k,"islands"),[l for l in r.stdout.splitlines() if "islands dropped" in l],os.path.basename(g[0]),"ok" if ok else "FAILED "+r.stdout[-400:]+r.stderr[-300:], info.get("keep"), [round(x,3) for x in info.get("size_m",[])],flush=True)
# 3. sprites, icons, role-customize.xml
r=run([sys.executable,"make_sprites.py",R0]); print("sprites",r.returncode,r.stderr[-800:])
r=run([sys.executable,"make_agent_assets.py"]); print("assets",r.returncode,r.stdout[-300:],r.stderr[-800:])
# 4. contact sheet of the A4 sprites as they go into the game (x3), with the item icons
from PIL import Image, ImageDraw, ImageFont
font=ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc",16)
cells=[]
for k in keys:
    ims=[Image.open(os.path.join(R0,"sprites","A4",f"{k}_{v}.png")).convert("RGBA") for v in "sbf"]
    ic=Image.open(os.path.join(P,"res","Resources","Textures","icons",k+".png")).convert("RGBA")
    cells.append((k,[i.resize((i.width*3,i.height*3),Image.NEAREST) for i in ims]+[ic.resize((128,128),Image.NEAREST)]))
W=1100; H=sum(max(i.height for i in c[1])+40 for c in cells)+20
sh=Image.new("RGB",(W,H),(70,76,84)); d=ImageDraw.Draw(sh); y=10
for k,ims in cells:
    d.text((10,y),k,font=font,fill=(240,240,240)); x=130; h=max(i.height for i in ims)
    for i in ims: sh.paste(i,(x,y+h-i.height),i); x+=i.width+30
    y+=h+40
sh.save(os.path.join(R0,"cs2_sprites_sheet.jpg"),quality=90); print("sheet",sh.size)
