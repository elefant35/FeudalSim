"""Original procedural Wending Star wreck: clinker-built 22 m North Atlantic trader.
Hull halves are independent: place bow centre y=-5.5, stern centre y=+5.5,
with matching headings. Waterline is z=0; both are baked 25 degrees to port.
Run with {'item': one of hull_bow, hull_stern, mast_broken, rigging_tangle,
reef_rocks, flotsam_planks, flotsam_barrel, flotsam_crate, rope_coil, sail_heap}.
"""
import math,pathlib,random,sys
import bpy
from mathutils import Vector,Matrix
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]))
from common import *
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]/'camp'))
from camp import vessel,crate


def solid_panel(name,p,col,thick=.06,axis=(1,0,0)):
 a=Vector(axis)*thick
 vv=[Vector(v) for v in p]+[Vector(v)-a for v in p]
 return mesh(name,vv,[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],col)


def rope(parts,points,r=.028,col='wood_light',name='rope',sides=6):
 vertices=[];faces=[]
 for j,p in enumerate(points):
  d=(Vector(points[min(j+1,len(points)-1)])-Vector(points[max(0,j-1)])).normalized()
  ref=Vector((0,0,1)) if abs(d.z)<.95 else Vector((0,1,0)); t=d.cross(ref).normalized();u=d.cross(t).normalized()
  vertices.extend([Vector(p)+r*(t*math.cos(i*math.tau/sides)+u*math.sin(i*math.tau/sides)) for i in range(sides)])
 for j in range(len(points)-1):
  for i in range(sides):faces.append((j*sides+i,j*sides+(i+1)%sides,(j+1)*sides+(i+1)%sides,(j+1)*sides+i))
 faces.extend([tuple(reversed(range(sides))),tuple(range((len(points)-1)*sides,len(points)*sides))])
 parts.append(mesh(name,vertices,faces,col))


def hull(item,seed):
 bow=item=='hull_bow';sign=-1 if bow else 1;rng=random.Random(seed);parts=[];cols=[];n=10
 # Separate overlapping strakes expose their thickness, especially at the shattered seam.
 def profile(y,t,side):
  w=3.22*math.sqrt(max(.012,1-(abs(y)/11.1)**2))
  if not bow:w=max(.30,w)
  keel=-1.82+.014*y*y;gun=1.62+.011*y*y
  return Vector((side*w*math.sin(t*math.pi/2),sign*y-sign*5.5,keel+(gun-keel)*t**.81))
 stations=[0,1.6,3.3,5.1,7.1,9.0,10.8]
 for side in [-1,1]:
  for k in range(n):
   t0=k/n;t1=(k+1)/n+.018
   for j in range(len(stations)-1):
    ya,yb=stations[j],stations[j+1]
    # Broken amidships: irregular teeth, one missing plank and exposed ribs.
    if j==0 and k in [2,6] and side==1:continue
    a=profile(ya,t0,side);b=profile(yb,t0,side);c=profile(yb,t1,side);d=profile(ya,t1,side)
    if j==0:
     a.y+=sign*rng.uniform(-.30,.40);d.y+=sign*rng.uniform(-.33,.47)
    c.x+=side*.025;d.x+=side*.025
    col=['bark','wood_light','bark','bark_dark','bark'][k%5]
    parts.append(solid_panel('clinker_strake',[a,b,c,d],col,.073,(side,0,0)))
   # Tarred gunwale follows the rising sheer.
  for j in range(len(stations)-1):
   parts.append(tube('gunwale',profile(stations[j],1,side),profile(stations[j+1],1,side),.095,.08,'bark_dark',7))
 # Inside frames following the hull section, seen at the break and inside the open cargo hold.
 for y in [.35,2.3,4.4,6.5,8.4,10.2]:
  for side in [-1,1]:
   ps=[profile(y,k/5,side) for k in range(6)]
   for p in ps:p.x-=side*.105
   for a,b in zip(ps,ps[1:]):parts.append(tube('rib',a,b,.073,.075,'wood_light',4))
 # A substantial keel beam and curved stem; their splintered ends face the fracture.
 rope(parts,[profile(y,0,1) for y in stations],.11,'bark_dark','keel',6)
 stem=[profile(10.8,k/5,1) for k in range(6)]
 rope(parts,stem,.12,'bark_dark','stem',7)
 # Partial deck: longitudinal sawn boards with ragged break, cargo hatch retained.
 for xindex in range(-8,9):
  x=xindex*.30
  for ya,yb in [(0,2.0),(5.0,7.0),(7.0,9.0)]:
   width=profile((ya+yb)/2,1,1).x
   if abs(x)+.15>width:continue
   if ya==5 and abs(x)<.85:continue
   z=1.12+.007*((ya+yb)/2)**2
   start=sign*ya-sign*5.5+(sign*rng.uniform(-.32,.28) if ya==0 else 0)
   end=sign*yb-sign*5.5
   parts.append(box('deck_board',(x,(start+end)/2,z),(.287,abs(end-start),.085),'wood_light' if xindex%4 else 'bark'))
 # Heavy hatch coaming and remnants of transverse floor beams.
 for y in [2.25,5.0,7.0]:
  w=profile(y,1,1).x
  parts.append(box('deck_beam',(0,sign*y-sign*5.5,1.12+.007*y*y),(w*1.9,.16,.17),'bark_dark'))
 for x in [-.92,.92]:parts.append(box('hatch_coaming',(x,sign*3.6-sign*5.5,1.24),(.14,2.4,.25),'bark'))
 # One mast stump per half proves the two-masted hull, neither a modern yacht nor a raft.
 my=sign*4.4-sign*5.5
 parts.append(tube('mast_stump',(0,my,-1.55),(0,my,2.9 if bow else 3.45),.24,.19,'bark',12))
 for i in range(5):
  a=i*math.tau/5
  parts.append(tube('mast_splinter',(math.cos(a)*.13,my+math.sin(a)*.13,2.55 if bow else 3.1),(math.cos(a)*.19,my+math.sin(a)*.19,(2.9 if bow else 3.45)+rng.uniform(.03,.4)),.04,.003,'wood_light',4))
 # Raised stern cross-planked platform and a side-mounted steering oar.
 if not bow:
  for k in range(9):
   y=7.4+k*.25;w=profile(y,1,1).x
   parts.append(box('quarterdeck',(0,y-5.5,1.67+.016*y*y),(w*1.8,.239,.09),'bark' if k%3 else 'wood_light'))
  parts.append(tube('steering_oar',(-1.7,2.5,2.4),(-1.45,5.2,-1.4),.065,.055,'wood_light',8))
  parts.append(solid_panel('rudder_blade',[(-1.45,4.8,-.5),(-1.45,5.4,-.4),(-1.45,5.7,-1.65),(-1.45,4.9,-1.65)],'bark',.12,(1,0,0)))
 # Collision: hollow hull surfaces plus deck slabs, allowing traversal inside.
 cv=[];cf=[]
 for side in [-1,1]:
  for j in range(len(stations)-1):
   for k in range(5):
    start=len(cv);cv.extend([profile(stations[j],k/5,side),profile(stations[j+1],k/5,side),profile(stations[j+1],(k+1)/5,side),profile(stations[j],(k+1)/5,side)]);cf.append(tuple(range(start,start+4)))
 coll=mesh('wreck_'+item+'_a-colonly',cv,cf,'bark')
 for ya,yb in [(0,2),(5,7),(7,9)]:
  w=profile((ya+yb)/2,1,1).x
  cols.append(box('deck_collision',(0,sign*(ya+yb)/2-sign*5.5,1.11+.007*((ya+yb)/2)**2),(w*1.8,yb-ya,.10),'bark'))
 # Match the same baked heel on both halves; z=0 stays the common sea surface.
 obj=join(parts,'wreck_'+item+'_a');collision=join([coll]+cols,'wreck_'+item+'_a-colonly')
 heel=Matrix.Rotation(math.radians(-25),4,'Y')
 for ob in [obj,collision]:
  for v in ob.data.vertices:v.co=heel@v.co
 return [obj,collision]


def mast_broken(seed):
 rng=random.Random(seed);parts=[]
 # A toppled mast lies diagonally, kinked by the break; spar and torn reefed sail remain attached.
 a=(-3,-2,.22);b=(3,3,.7)
 parts.append(tube('mast',a,b,.20,.11,'bark',12))
 for i in range(7):
  angle=i*math.tau/7
  parts.append(tube('splinter',(-3+math.cos(angle)*.12,-2+math.sin(angle)*.12,.22),(-3.5-rng.random()*.6,-2.2+rng.random()*.5,.18+math.cos(angle)*.13),.042,.002,'wood_light',5))
 rope(parts,[(-2.3,1.8,.55),(0,.1,.73),(2.5,-1.9,.57)],.07,'wood_light','spar',10)
 # Sail panels sag and tear into three asymmetrical hems; reds are cloth wind.
 for i in range(12):
  x=-2.25+i*.4
  y=-.5+i*.03;z=.12+(.04 if i%2 else 0)
  verts=[(x,1.65-i*.23,.57),(x+.39,1.65-(i+1)*.23,.57),(x+.42,y-.7-rng.random()*.6,z),(x,y-1-rng.random()*.7,z)]
  sail=solid_panel('torn_sail',verts,'linen' if i%4 else 'wool_grey',.012,(0,0,1))
  attr=sail.data.color_attributes.active_color
  for v,c in zip(sail.data.vertices,attr.data):c.color=(min(1,max(0,(.6-v.co.z)/.5)),0,0,1)
  parts.append(sail)
 for i in range(4):
  rope(parts,[(-2+i*1.1,1-i*.8,.65),(-2.6+i*1.2,-.5,.3),(-1.8+i*1.2,-2.0,.05)],.018,'wood_light','line')
 # Distinct individual lashing wraps around the spar junction.
 for j in range(8):
  t=j*.06-.2;pts=[(t+math.cos(k*math.tau/12)*.17,.12+math.sin(k*math.tau/12)*.17,.70) for k in range(13)]
  rope(parts,pts,.016,'linen','lashing',5)
 return [join(parts,'wreck_mast_broken_a')]


def rigging(seed):
 rng=random.Random(seed);parts=[]
 for j in range(8):
  pts=[]
  for k in range(19):
   t=k/18*math.tau*1.6+j*.6;rad=.23+j*.09
   pts.append((math.cos(t)*rad+(j-4)*.10,math.sin(t)*rad,.028+.013*j+math.sin(k*.6)**2*.04))
  rope(parts,pts,.019 if j%2 else .026,'wood_light' if j%3 else 'bark','rigging',5)
 for j in range(3):
  parts.append(blob('deadeye',(-.5+j*.42,.2+j*.13,.16),(.10,.08,.10),'bark_dark',seed+j,rings=3,sides=8))
 return [join(parts,'wreck_rigging_tangle_a')]


def generate(params):
 item=params.get('item','hull_bow');seed=int(params.get('seed',700));rng=random.Random(seed)
 if item.startswith('hull_'):return hull(item,seed)
 if item=='mast_broken':res=mast_broken(seed)
 elif item=='rigging_tangle':res=rigging(seed)
 else:
  parts=[];collisions=[]
  if item=='reef_rocks':
   for j,(x,y,z,rx,ry,rz) in enumerate([(-1.6,0,.4,1.8,1.2,1.1),(.9,.5,.3,1.5,1.4,.9),(.5,-1.4,.1,1.1,.8,.8),(2.2,-.9,.1,.8,.65,.5)]):parts.append(blob('reef',(x,y,z),(rx,ry,rz),'granite_dark' if j%2 else 'flint',seed+j,rings=5,sides=10))
   for j,ob in enumerate(parts):
    cp=ob.copy();cp.data=ob.data.copy();cp.name=f'wreck_reef_rocks_{j}-convcolonly';bpy.context.collection.objects.link(cp);collisions.append(cp)
  elif item=='flotsam_planks':
   for j in range(8):
    a=j*.45;cx=rng.uniform(-.6,.6);cy=rng.uniform(-.4,.4);length=rng.uniform(1.2,2.5)
    q=Matrix.Rotation(a,4,'Z');ob=box('board',(0,0,0),(.16,length,.048),'bark' if j%3 else 'wood_light')
    for v in ob.data.vertices:v.co=q@v.co+Vector((cx,cy,.05+j*.027))
    parts.append(ob)
    # Bare end-grain splinter rising from one washed-out end.
    parts.append(tube('splinter',(cx,cy,.06+j*.027),(cx+.1,cy+.5,.1+j*.027),.035,.002,'wood_light',4))
  elif item=='flotsam_barrel':
   parts=vessel('barrel',.34,.84,'bark',seed)
   for ob in parts:
    for v in ob.data.vertices:v.co=Matrix.Rotation(math.pi/2,4,'X')@v.co+Vector((0,.42,.35))
   parts.append(tube('loose_stave',(-.2,-.5,.06),(.5,.4,.13),.035,.030,'wood_light',4))
  elif item=='flotsam_crate':
   crate(parts,.8,.62,.65)
   # One visibly separated lid board was torn loose in the surf.
   parts.append(box('loose_plank',(.38,.10,.075),(.20,.93,.045),'bark'))
  elif item=='rope_coil':
   for j in range(3):
    rad=.20+j*.055
    pts=[(math.cos(k*math.tau/20)*rad,math.sin(k*math.tau/20)*rad,.025+j*.008) for k in range(21)]
    rope(parts,pts,.023,'wood_light','rope',4)
   rope(parts,[(.34,0,.04),(.52,-.1,.033),(.63,.08,.025),(.73,.15,.025)],.023,'wood_light','tail',4)
  elif item=='sail_heap':
   # Folded triangular sailcloth settles into a damp, irregular low heap.
   for j in range(9):
    angle=j*.63;q=Matrix.Rotation(angle,4,'Z');w=.6+rng.random()*.4;h=.07+j*.024
    verts=[q@Vector((-w,-.55,h)),q@Vector((w,-.5,h+.03)),q@Vector((.5,.10,h+.20)),q@Vector((-.4,.55,h+.06))]
    parts.append(solid_panel('sail_fold',verts,'linen' if j%4 else 'wool_grey',.024,(0,0,1)))
   for j in range(6):rope(parts,[(-.5+j*.16,-.58,.08),(-.4+j*.16,.1,.32),(-.3+j*.16,.55,.1)],.010,'wool_brown','seam',5)
  else:raise ValueError(item)
  res=[join(parts,'wreck_'+item+'_a')]+collisions
 # All small debris sits on the beach plane; hulls intentionally preserve sea surface.
 low=min(v.co.z for ob in res for v in ob.data.vertices)
 for ob in res:
  for v in ob.data.vertices:v.co.z-=low
 return res
