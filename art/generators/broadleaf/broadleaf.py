"""Timber trees: species-specific structure and asymmetric wind-shaped crowns.
Usage export.py -- this.py ... '{"species":"oak","seed":1,"variant":"a"}'
"""
import math,pathlib,random,sys
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]))
from common import *
SPECIES={
 'scots_pine':(20,.46,'bark','pine_dark','pine'),
 'oak':(18,.75,'bark_dark','leaf_dark','leaf'),
 'birch':(14,.22,'linen','leaf','leaf_light'),
 'ash':(18,.40,'granite','leaf','leaf_light'),
 'beech':(18,.52,'wool_grey','leaf_dark','leaf'),
 'alder':(11,.28,'bark_dark','leaf_dark','leaf'),
 'willow':(11,.40,'wool_grey','leaf_dark','leaf'),
 'elm':(19,.55,'bark_dark','leaf_dark','leaf'),
 'yew':(12,.65,'bark','pine_dark','pine'),
 'lime':(17,.52,'granite','leaf','leaf_light')}

def generate(params):
 species=params.get('species','oak');seed=int(params.get('seed',1));variant=params.get('variant','a')
 rng=random.Random(seed);h,r,bark,dark,light=SPECIES[species];h*=rng.uniform(.95,1.05)
 name=f'tree_{species}_{variant}';parts=[];coll=[]
 stage=params.get('stage','tree')
 if stage!='tree':
  length=1.1 if stage=='stump' else h*.65
  a=(0,0,0) if stage=='stump' else (-length/2,0,r)
  b=(0,0,length) if stage=='stump' else (length/2,0,r*.5)
  parts.append(tube('bark',a,b,r,r*.65,bark,10))
  # Pale cut surface, off centre heartwood ring.
  if stage=='stump':parts.append(tube('cut',(0,0,length),(0,0,length+.01),r*.64,r*.64,'wood_light',10))
  else:parts.append(tube('cut',(-length/2-.01,0,r),(-length/2,0,r),r*.92,r*.92,'wood_light',10))
  coll.append(convex_trunk(name,a,b,r))
  o=join(parts,name)
  z=min(v.co.z for v in o.data.vertices)
  for meshobj in [o]+coll:
   for p in meshobj.data.vertices:p.co.z-=z
  return [o]+coll
 lean=rng.uniform(-.7,.7);windshift=.8 if seed%2==0 else .25
 stems=3 if species in ('willow','alder') else 1
 tops=[]
 for k in range(stems):
  ang=k*math.tau/stems;foot=(math.cos(ang)*r*.5,math.sin(ang)*r*.5,0)
  top=(lean+math.cos(ang)*h*.08*(stems>1),math.sin(ang)*h*.06*(stems>1),h*.72)
  mid=(foot[0]+lean*.3,foot[1],h*.32)
  parts.extend([tube('trunk',foot,mid,r/(stems**.5),r*.58/(stems**.5),bark,9),tube('trunk',mid,top,r*.58/(stems**.5),r*.14,'clay' if species=='scots_pine' else bark,8)])
  coll.append(convex_trunk(name+f'_trunk{k}',foot,top,r/(stems**.5)))
  tops.append(top)
 # Root flare: readable grounded trunk, not a cylinder suspended on a point.
 for k in range(5):
  ang=k*math.tau/5;parts.append(tube('root',(math.cos(ang)*r*1.8,math.sin(ang)*r*1.8,.03),(0,0,h*.08),r*.16,r*.35,bark,5))
 crowns=9 if species in ('oak','beech','elm','lime','willow') else 8
 for k in range(crowns):
  ang=k*2.39996+rng.uniform(-.18,.18)
  spread={'oak':.29,'beech':.25,'birch':.17,'ash':.23,'alder':.17,'willow':.29,'scots_pine':.22,'yew':.27}.get(species,.25)
  if species=='scots_pine':z=h*(.65+.29*(k/(crowns-1)));dist=h*spread*(1-.45*k/(crowns-1))
  elif species=='alder':z=h*(.48+.42*k/(crowns-1));dist=h*spread*(1-.7*k/(crowns-1))
  else:z=h*(.65+.23*(k%3)/2);dist=h*spread*rng.uniform(.45,.95)
  c=(lean+math.cos(ang)*dist+windshift,math.sin(ang)*dist,z)
  base=tops[k%stems]; branchstart=(base[0]*.6,base[1]*.6,h*(.40 if species!='scots_pine' else .61))
  elbow=(c[0]*.7,c[1]*.7,z-h*.12)
  parts.extend([tube('branch',branchstart,elbow,r*.26,r*.13,bark,6),tube('twig',elbow,c,r*.13,.035,bark,5)])
  flat=species=='scots_pine'
  rad=h*spread*(.48 if species in ('ash','birch') else .63)
  vertical=h*(.06 if flat else .13)
  if species=='willow':vertical=h*.18
  parts.append(blob('crown',c,(rad,rad*.85,vertical),dark if k%3==0 else light,seed*100+k,rings=4,sides=9,
    wind=lambda p:max(.15,min(1,(p.z-h*.35)/(h*.65))),irregular=.16))
  if species=='willow':
   for j in range(2):
    x=c[0]+(j-.5)*rad*.7
    parts.append(blob('trailing',(x,c[1],c[2]-vertical),(rad*.22,rad*.3,h*.15),dark,seed+k+j,rings=3,sides=5,wind=.9))
 # Species tells at eye height: birch horizontal bark scars; orange upper pine bark.
 if species=='birch':
  for k in range(12):
   z=.6+k*.46;rad=r*(1-.42*z/(h*.32));cx=lean*.3*z/(h*.32)
   angles=[-math.pi/2-.40,-math.pi/2,-math.pi/2+.40]
   verts=[(cx+(rad+.003)*math.cos(a),(rad+.003)*math.sin(a),z+dz) for dz in (-.025,.025) for a in angles]
   parts.append(mesh('barkscar',verts,[(0,1,4,3),(1,2,5,4)],'bark_dark'))

 o=join(parts,name)
 z=min(v.co.z for v in o.data.vertices)
 for meshobj in [o]+coll:
  for p in meshobj.data.vertices:p.co.z-=z
 return [o]+coll
