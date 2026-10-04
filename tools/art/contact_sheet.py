"""Pack M2 four-angle previews into readable review pages (requires Pillow).
Usage python contact_sheet.py <manifest.yaml> <preview-dir> <output-prefix>.
Other YAML uses PyYAML or the macOS Ruby/Psych fallback.
"""
import pathlib,sys
from PIL import Image,ImageDraw
from sync_palette_derivatives import read_manifest
path,previews,prefix=map(pathlib.Path,sys.argv[1:4])
entries=read_manifest(path)
files=[pathlib.Path(o).stem for e in entries for o in e['outputs'] if o.endswith('.glb')]
for page,start in enumerate(range(0,len(files),12)):
 names=files[start:start+12];sheet=Image.new('RGB',(4*260,3*285),'#a8b1b7');d=ImageDraw.Draw(sheet)
 for i,name in enumerate(names):
  image=Image.open(previews/(name+'_turntable.png')).convert('RGB').resize((260,260))
  x=(i%4)*260;y=(i//4)*285;sheet.paste(image,(x,y));d.text((x+4,y+262),name,fill='#101010')
 output=pathlib.Path(str(prefix)+f'_{page+1}.png');output.parent.mkdir(parents=True,exist_ok=True);sheet.save(output)
 print(output)
