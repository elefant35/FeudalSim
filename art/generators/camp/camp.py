"""Landfall camp: worn salvaged stores, shelters and survival props.
Sail/brush panels are faceted meshes with weighted hems; all objects share palette.
"""
import math,pathlib,random,sys
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]))
from common import *

def vessel(name,r,h,colour='wood_light',seed=1):
 rng=random.Random(seed);parts=[];sides=12
 # Coopered sides, pronounced belly, honest stave silhouettes.
 levels=[(0,r*.84),(.08*h,r*.90),(.30*h,r*.98),(.65*h,r),(.95*h,r*.88),(h,r*.86)]
 verts=[(rad*math.cos(i*math.tau/sides),rad*math.sin(i*math.tau/sides),z) for z,rad in levels for i in range(sides)]
 faces=[(j*sides+i,j*sides+(i+1)%sides,(j+1)*sides+(i+1)%sides,(j+1)*sides+i) for j in range(len(levels)-1) for i in range(sides)]
 faces +=[tuple(reversed(range(sides))),tuple(range((len(levels)-1)*sides,len(levels)*sides))]
 body=mesh('staves',verts,faces,colour)
 for p in body.data.polygons:
  if p.index%sides in (1,5,9):fsart.paint(body,'bark',{p.index})
 parts.append(body)
 for z,rad in [(h*.12,r*.94),(h*.78,r*.98)]:
  verts=[(rr*math.cos(i*math.tau/sides),rr*math.sin(i*math.tau/sides),zz) for zz in (z-.022,z+.022) for rr in [rad+.005] for i in range(sides)]
  parts.append(mesh('hoop',verts,[(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)],'iron'))
 return parts

def crate(parts,w=0.8,h=.65,d=.65):
 for axis in (-1,1):
  for k in range(4):
   z=(k+.5)*h/4
   parts.append(box('plank',(0,axis*d/2,z),(w,.045,h/4-.012),'wood_light' if k%2 else 'bark'))
   parts.append(box('plank',(axis*w/2,0,z),(.045,d,h/4-.012),'wood_light'))
 for y in (-d/2-.027,d/2+.027):
  for x in (-w*.36,w*.36):parts.append(box('brace',(x,y,h/2),(.07,.04,h),'bark_dark'))
 for k in range(5):parts.append(box('lid',((k-2)*w/5,0,h), (w/5-.012,d,.04),'wood_light'))
 parts.append(box('floor',(0,0,.025),(w,d,.05),'bark'))

def generate(params):
 item=params.get('item','campfire');seed=int(params.get('seed',301));rng=random.Random(seed);parts=[];coll=[]
 name='prop_'+item+'_a'
 if item=='campfire':
  for k in range(11):
   a=k*math.tau/11;parts.append(blob('ring',(math.cos(a)*.51,math.sin(a)*.51,.09),(.14,.11,.09),'granite_dark' if k%3==0 else 'granite',seed+k,rings=4,sides=7))
  ring=join(parts,'building_campfire_a');parts=[]
  logs=[]
  for k in range(5):
   a=k*math.pi/5;logs.append(tube('charred',(-.32*math.cos(a),-.32*math.sin(a),.11),(.32*math.cos(a),.32*math.sin(a),.15),.065,.052,'bark_dark',8))
  ash=blob('Ash',(0,0,.025),(.42,.40,.025),'granite_dark',seed,rings=3,sides=10)
  return [ring,join(logs,'Logs'),ash]
 elif item in ('sailcloth_shelter','lean_to'):
  name='building_'+item+'_a';sail=item=='sailcloth_shelter';w=3 if sail else 2;d=3;h=1.95 if sail else 1.65
  # Guy rope keeps the roof useful while open front is walkable.
  for x in (-w/2,w/2):
   for y in (-d/2,d/2):
    parts.append(tube('pole',(x,y,0),(x*.88,y,(h if x<0 else .25) if not sail else .9),.047,.035,'bark',7))
    coll.append(convex_trunk(name+f'_post{len(coll)}',(x,y,0),(x*.88,y,(h if x<0 else .25) if not sail else .9),.045))
  if sail:
   for y in (-d/2,d/2):parts.append(tube('ridgepost',(0,y,0),(0,y,h),.055,.045,'wood_light',7))
   parts.append(tube('oar_ridge',(0,-d/2-.25,h),(0,d/2+.3,h),.038,.030,'wood_light',8))
   parts.append(box('oar_blade',(0,d/2+.3,h),(.22,.38,.035),'wood_light'))
  else:parts.append(tube('ridge',(-w/2,-d/2,h),(-w/2,d/2,h),.06,.055,'bark',8))
  # Longitudinal stripes imply sail seams or parallel brush/thatch bundles.
  for side in (-1,1) if sail else (1,):
   for k in range(10):
    y0=-d/2+k*d/10;y1=y0+d/10-.004
    x0=0 if sail else -w/2; x1=side*w/2 if sail else w/2
    z0=h;z1=.86 if sail else .25
    verts=[(x0,y0,z0),(x1*.45,y0,z0+(z1-z0)*.45-.09),(x1,y0,z1),
           (x0,y1,z0),(x1*.45,y1,z0+(z1-z0)*.45-.09),(x1,y1,z1)] if sail else [(-w/2,y0,h),(0,y0,h*.6),(w/2,y0,.25),(-w/2,y1,h),(0,y1,h*.6),(w/2,y1,.25)]
    # both surfaces: intentional two-sided cloth/brush thickness.
    faces=[(0,1,4,3),(1,2,5,4),(3,4,1,0),(4,5,2,1)]
    colour=('linen' if k%4 else 'wool_grey') if sail else ('straw' if k%3 else 'bark')
    parts.append(mesh('roof_panel',verts,faces,colour,wind=lambda p:(h-p.z)/h*.7))
  if not sail:
   for k in range(13):parts.append(tube('brush',(-w/2,-d/2+k*d/12,h),(w/2,-d/2+k*d/12,.25),.034,.025,'straw',5,wind=.3))
  for x in (-w/2,w/2):
   for y in (-d/2,d/2):
    end=(x*1.25,y*1.16,.04);parts.append(tube('guy',(x,y,.86 if sail else (h if x<0 else .25)),end,.013,.012,'wood_light',5))
    parts.append(blob('weight',end,(.15,.11,.06),'granite',seed+len(parts),rings=3,sides=6))
 elif item=='bough_bed':
  parts.append(box('mat',(0,0,.03),(.8,2,.06),'bark_dark'))
  for k in range(11):
   y=-.9+k*.18;parts.append(blob('bough',(rng.uniform(-.06,.06),y,.12),(.40,.18,.10),'pine_dark' if k%2 else 'pine',seed+k,rings=3,sides=6))
 elif item=='crate':crate(parts)
 elif item=='barrel':parts=vessel(name,.34,.86,seed=seed)
 elif item=='chest':
  crate(parts,.85,.44,.50)
  parts.append(blob('lid',(0,0,.46),(.43,.27,.15),'bark',seed,rings=3,sides=8))
  for x in (-.29,.29):parts.append(box('iron_strap',(x,0,.48),(.035,.57,.05),'iron'))
  parts.append(box('latch',(0,-.30,.39),(.08,.02,.12),'iron'))
 elif item in ('sack','water_skin'):
  sack=item=='sack';h=.66 if sack else .36;r=.27 if sack else .15
  parts.append(blob('body',(0,0,h*.46),(r,r*.8,h*.46),'linen' if sack else 'wool_brown',seed,rings=5,sides=10))
  parts.append(tube('neck',(0,0,h*.81),(0,0,h),r*.3,r*.2,'linen' if sack else 'bark',8))
  parts.append(tube('tie',(0,0,h*.89),(0,0,h*.92),r*.32,r*.32,'bark_dark',8))
  for side in (-1,1):parts.append(tube('cord',(0,0,h*.92),(side*r*.4,0,h*.82),.008,.008,'bark',5))
  if not sack:
   parts.append(tube('strap',(-r,0,h*.4),(0,0,h+.14),.011,.01,'bark',6));parts.append(tube('strap',(0,0,h+.14),(r,0,h*.4),.011,.01,'bark',6))
 elif item=='wooden_bucket':
  sides=12;h=.34;r=.21
  verts=[(rr*math.cos(i*math.tau/sides),rr*math.sin(i*math.tau/sides),z) for z,rr in [(0,r*.82),(h,r),(h,r-.025),(.03,r*.82-.025)] for i in range(sides)]
  faces=[]
  for j in range(3):faces +=[(j*sides+i,j*sides+(i+1)%sides,(j+1)*sides+(i+1)%sides,(j+1)*sides+i) for i in range(sides)]
  faces +=[tuple(range(3*sides,4*sides)),tuple(reversed(range(sides)))]
  parts.append(mesh('staves',verts,faces,'wood_light'))
  for z in (.05,.29):
   rad=r if z>.2 else r*.87
   vv=[(rad*math.cos(i*math.tau/sides),rad*math.sin(i*math.tau/sides),zz) for zz in (z,z+.022) for i in range(sides)]
   parts.append(mesh('band',vv,[(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)],'iron'))
  for side in (-1,1):parts.append(tube('handle',(side*r,0,h*.8),(side*r*.55,0,h+.23),.013,.013,'bark',6))
  parts.append(tube('handle_grip',(-r*.55,0,h+.23),(r*.55,0,h+.23),.019,.019,'wood_light',8))
 elif item in ('firewood_pile','firewood_bundle'):
  pile=item=='firewood_pile';levels=3 if pile else 2
  for j in range(levels):
   for k in range((4 if pile else 3)-j):
    x=(k-((4 if pile else 3)-j-1)/2)*.17;z=.078+j*.135;length=.95 if pile else .54
    parts.append(tube('split_log',(x,-length/2,z),(x,length/2,z),.080,.074,'bark',6,phase=.2))
    parts.append(tube('cut',(x,-length/2-.004,z),(x,-length/2,z),.075,.07,'wood_light',6))
  if not pile:
   for y in (-.17,.17):
    for a,b in [((-.22,y,.1),(-.1,y,.31)),((-.1,y,.31),(.1,y,.31)),((.1,y,.31),(.22,y,.1)),((.22,y,.1),(-.22,y,.1))]:parts.append(tube('binding',a,b,.012,.012,'linen',5))
 else:raise ValueError(item)
 obj=join(parts,name)
 z=min(v.co.z for v in obj.data.vertices)
 for ob in [obj]+coll:
  for p in ob.data.vertices:p.co.z-=z
 if item in ('crate','barrel','chest'):
  coords=[v.co for v in obj.data.vertices];mi=[min(v[i] for v in coords) for i in range(3)];ma=[max(v[i] for v in coords) for i in range(3)]
  c=[(a+b)/2 for a,b in zip(mi,ma)];s=[b-a for a,b in zip(mi,ma)];hull=box(name+'-convcolonly',c,s,'bark');coll.append(hull)
 return [obj]+coll
