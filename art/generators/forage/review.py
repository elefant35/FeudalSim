#!/usr/bin/env python3
"""Assemble complete P1 botanical turntables and explicit lookalike comparisons."""
import json
from pathlib import Path
from PIL import Image,ImageDraw

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'art/previews/forage'
reports=json.loads((Path(__file__).parent/'checks.json').read_text())['assets']
bushes=reports[:14];plants=reports[14:]
groups={'bushes':bushes}
for i in range(4):groups['plants_'+str(i+1)]=plants[i*14:i*14+14]
for group,assets in groups.items():
    rows=(len(assets)+3)//4;sheet=Image.new('RGB',(4*448,rows*474),'#30393d');draw=ImageDraw.Draw(sheet)
    for i,a in enumerate(assets):
        name=a['species']+'_'+a['variant'];x=(i%4)*448;y=(i//4)*474
        sheet.paste(Image.open(OUT/(name+'_turntable.png')).convert('RGB').resize((448,448)),(x,y+26))
        draw.text((x+8,y+7),name+' | '+str(a['check']['tris']['0'])+' tris',fill='white')
    sheet.save(OUT/(group+'_contact.png'))
    sheet=Image.new('RGB',(4*256,rows*282),'#30393d');draw=ImageDraw.Draw(sheet)
    for i,a in enumerate(assets):
        name=a['species']+'_'+a['variant'];x=(i%4)*256;y=(i//4)*282
        sheet.paste(Image.open(OUT/(name+'_silhouette.png')).convert('RGB').resize((256,256)),(x,y+26))
        draw.text((x+8,y+7),name,fill='white')
    sheet.save(OUT/(group+'_silhouettes.png'))
names=['wild_carrot','hemlock','water_hemlock','field_mushroom','death_cap','ramsons','lily_of_the_valley',
       'comfrey','foxglove','bilberry','deadly_nightshade']
rows=(len(names)+2)//3;sheet=Image.new('RGB',(3*512,rows*538),'#30393d');draw=ImageDraw.Draw(sheet)
for i,name in enumerate(names):
    x=(i%3)*512;y=(i//3)*538;path=OUT/(name+'_detail.png')
    if not path.exists():path=OUT/(name+'_a_turntable.png')
    im=Image.open(path).convert('RGB')
    if im.size!=(512,512):im=im.crop((0,0,512,512))
    sheet.paste(im,(x,y+26));draw.text((x+8,y+7),name,fill='white')
sheet.save(OUT/'lookalikes_contact.png')
if all((OUT/(name+'_gills.png')).exists() for name in ('field_mushroom','death_cap')):
    sheet=Image.new('RGB',(1024,538),'#30393d');draw=ImageDraw.Draw(sheet)
    for i,name in enumerate(('field_mushroom','death_cap')):
        sheet.paste(Image.open(OUT/(name+'_gills.png')).convert('RGB'),(i*512,26))
        draw.text((i*512+8,7),name+' | gill underside / stem base',fill='white')
    sheet.save(OUT/'mushroom_tells_contact.png')
if all((OUT/(name+'_berry.png')).exists() for name in ('bilberry','deadly_nightshade')):
    sheet=Image.new('RGB',(1024,538),'#30393d');draw=ImageDraw.Draw(sheet)
    for i,name in enumerate(('bilberry','deadly_nightshade')):
        sheet.paste(Image.open(OUT/(name+'_berry.png')).convert('RGB'),(i*512,26))
        draw.text((i*512+8,7),name+' | berry / persistent calyx',fill='white')
    sheet.save(OUT/'berry_tells_contact.png')
