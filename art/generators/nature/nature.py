"""Glacial erratics, loose granite, chalk-rinded flint, bleached driftwood and slope slabs."""
import math,pathlib,random,sys
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]))
from common import *

def generate(params):
 species=params.get('species','boulder');seed=int(params.get('seed',201));v=params.get('variant','a');rng=random.Random(seed)
 name=f'rock_{species}_{v}';parts=[];coll=[]
 if species in ('boulder','fieldstone','outcrop'):
  width=rng.uniform(1.3,2.5) if species=='boulder' else rng.uniform(.35,.55) if species=='fieldstone' else rng.uniform(4,7)
  height=width*(.57 if species!='outcrop' else .43)
  obj=blob('stone',(0,0,height*.5),(width*.5,width*.42,height*.5),'granite',seed,rings=5,sides=10,irregular=.22)
  # Face-level fissure and lichen accents; no stray decals or materials.
  for p in obj.data.polygons:
   if p.normal.z<-.1 or rng.random()<.14:fsart.paint(obj,'granite_dark',{p.index})
   elif p.normal.z>.15 and rng.random()<.09:fsart.paint(obj,'moss',{p.index})
  parts.append(obj)
  hull=blob(name+'-colonly' if species=='outcrop' else name+'-convcolonly',(0,0,height*.5),(width*.5,width*.42,height*.5),'granite',seed,rings=3,sides=8,irregular=.22);coll.append(hull)
 elif species=='flint_scatter':
  for k in range(5):
   x,y=rng.uniform(-.6,.6),rng.uniform(-.5,.5);r=rng.uniform(.075,.13)
   o=blob('nodule',(x,y,r*.6),(r,r*.75,r*.6),'limestone',seed+k,rings=3,sides=6)
   for p in o.data.polygons:
    if p.normal.z>.3 and rng.random()<.6:fsart.paint(o,'flint',{p.index})
   parts.append(o)
 elif species=='driftwood':
  length=rng.uniform(1.7,3.5);r=.10+rng.random()*.06
  parts.append(tube('log',(-length/2,0,r),(length/2,.17,r*.72),r,r*.72,'wool_grey',10))
  parts.append(tube('cut',(-length/2-.005,0,r),(-length/2,0,r),r*.9,r*.9,'wood_light',10))
  for k in range(3):
   x=-length*.3+k*length*.26
   parts.append(tube('snag',(x,.04,r),(x+.28,rng.choice([-1,1])*.32,r+.05),r*.45,.023,'wool_grey',6))
  # Bark cracks running along exposed grain.
  for k in range(3):parts.append(tube('grain',(-length*.42,-r*.7+k*r*.6,r*1.75),(length*.38,.1-r*.7+k*r*.6,r*1.25),.01,.007,'granite_dark',4))
  coll.append(convex_trunk(name,(-length/2,0,r),(length/2,.17,r*.72),r))
 else:raise ValueError(species)
 o=join(parts,name)
 # Ensure touch ground (tube cross-section orientations can shift extrema).
 z=min(v.co.z for v in o.data.vertices)
 for meshobj in [o]+coll:
  for p in meshobj.data.vertices:p.co.z-=z
 return [o]+coll
