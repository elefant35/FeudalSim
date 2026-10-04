#!/usr/bin/env python3
"""Assemble reproducible visual review sheets (NumPy/Pillow Python)."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'art/previews/understory'
GROUPS={
    'hazel':['hazel_'+v for v in 'abc'],
    'bramble_gorse':['bramble_'+v for v in 'ab']+['gorse_'+v for v in 'ab'],
    'reeds_grass':['reeds_'+v for v in 'abc']+['grass_tuft_'+v for v in 'abcd'],
    'fern':['fern_'+v for v in 'abc'],
}
for group,names in GROUPS.items():
    cols=min(4,len(names));rows=(len(names)+cols-1)//cols
    sheet=Image.new('RGB',(cols*448,rows*474),'#30393d');draw=ImageDraw.Draw(sheet)
    for i,name in enumerate(names):
        x=(i%cols)*448;y=(i//cols)*474
        sheet.paste(Image.open(OUT/(name+'_turntable.png')).convert('RGB').resize((448,448)),(x,y+26))
        draw.text((x+8,y+7),name,fill='white')
    sheet.save(OUT/(group+'_contact.png'))
names=sum(GROUPS.values(),[])
sheet=Image.new('RGB',(5*240,4*266),'#30393d');draw=ImageDraw.Draw(sheet)
silhouettes=Image.new('RGB',(5*240,4*266),'#30393d');sd=ImageDraw.Draw(silhouettes)
for i,name in enumerate(names):
    x=(i%5)*240;y=(i//5)*266
    im=Image.open(OUT/(name+'_turntable.png')).convert('RGB').crop((0,0,512,512))
    sheet.paste(im.resize((240,240)),(x,y+26));draw.text((x+8,y+7),name,fill='white')
    silhouettes.paste(Image.open(OUT/(name+'_silhouette.png')).convert('RGB').resize((240,240)),(x,y+26))
    sd.text((x+8,y+7),name+' | 40m',fill='white')
sheet.save(OUT/'understory_contact.png');silhouettes.save(OUT/'understory_silhouettes.png')
