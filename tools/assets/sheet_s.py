import os
from PIL import Image, ImageDraw, ImageFont
R=r"E:\projects\os-cs-arsenal\round0\render"
keys=["tec9","fiveseven","cz75a","deagle","revolver","mac10","mp9","mp7","mp5sd","ump45","bizon","nova","sawedoff","mag7","xm1014"]
font=ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc",18)
cw,ch=520,260; sh=Image.new("RGB",(cw*3,ch*5),(200,205,210)); d=ImageDraw.Draw(sh)
for i,k in enumerate(keys):
    im=Image.open(os.path.join(R,k,"s.png")).convert("RGBA"); im.thumbnail((cw-20,ch-40))
    x,y=(i%3)*cw,(i//3)*ch; sh.paste(im,(x+10,y+30),im); d.text((x+10,y+4),k,font=font,fill=(0,0,0))
sh.save(r"E:\projects\os-cs-arsenal\round0\cs2_render_s.jpg",quality=88); print(sh.size)
