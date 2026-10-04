"""Later Landfall camp structures and shelter construction stages, own-work.
Frame stages retain temporary braces; half-covered roofs use alternating panels.
"""
import math,pathlib,random,sys
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]))
from common import *

def generate(params):
 item=params['item'];rng=random.Random(params.get('seed',501));parts=[];coll=[];name='building_'+item+'_a'
 def pole(a,b,r=.05,c='bark',sides=8,collision=False):
  ob=tube('pole',a,b,r,r*.85,c,sides);parts.append(ob)
  if collision:coll.append(convex_trunk(name+str(len(coll)),a,b,r))
 def plank(c,s,col='wood_light'):parts.append(box('plank',c,s,col))
 def stone(c,s,seed=0):parts.append(blob('stone',c,s,'granite',seed,rings=5,sides=10))
 stage=0
 if '_stage' in item:
  base,st=item.split('_stage');stage=int(st)
 else:base=item
 if base in ('sailcloth_shelter','lean_to'):
  sail=base=='sailcloth_shelter';w=3 if sail else 2;d=3;h=1.95 if sail else 1.65
  for x in (-w/2,w/2):
   for y in (-d/2,d/2):pole((x,y,0),(x,y,.9 if sail else h if x<0 else .25),collision=True)
  if sail:
   for y in (-d/2,d/2):pole((0,y,0),(0,y,h),collision=True)
   pole((0,-d/2-.2,h),(0,d/2+.2,h),.045,'wood_light')
  else:pole((-w/2,-d/2,h),(-w/2,d/2,h),.055)
  for y in (-d/2,d/2):
   if sail:
    for s in (-1,1):pole((0,y,h),(s*w/2,y,.9),.03)
   else:pole((-w/2,y,h),(w/2,y,.25),.03)
  # Construction support braces are removed when the shelter is finished.
  for y in (-d/2,d/2):pole((-w/2,y,.1),(-w/2+.45,y,1.2),.028)
  for x in (-w/2,w/2):
   for y in (-d/2,d/2):
    pole((x,y,.9 if sail else h if x<0 else .25),(x*1.23,y*1.15,.04),.013,'wood_light',5)
    stone((x*1.23,y*1.15,.07),(.15,.11,.07),len(parts))
  if stage==2:
   for k in range(5):
    y0=-d/2+k*d/5;y1=y0+d/10
    for s in (-1,1) if sail else (1,):
     verts=[(0 if sail else -w/2,y0,h),(s*w/2,y0,.9 if sail else .25),(s*w/2,y1,.9 if sail else .25),(0 if sail else -w/2,y1,h)]
     face=(3,2,1,0) if sail and s<0 else (0,1,2,3)
     parts.append(mesh('halfcover',verts,[face],'linen' if sail else 'straw',wind=.4))
     if not sail:
      for j in range(3):pole((-w/2,y0+j*.07,h),(w/2,y0+j*.07,.25),.028,'straw',5)
 elif base=='hut':
  w,d,h=4,5,2.10
  # Corner posts and the open doorway; no invisible wall crosses the door.
  for x in (-2,2):
   for y in (-2.5,2.5):pole((x,y,0),(x,y,h),.10,collision=True)
  for x in (-.55,.55):pole((x,-2.5,0),(x,-2.5,2.02),.08,collision=True)
  for y in (-2.5,2.5):pole((-2,y,h),(2,y,h),.10)
  for x in (-2,2):pole((x,-2.5,h),(x,2.5,h),.10)
  for y in (-2.5,0,2.5):
   pole((-2.15,y,h),(0,y,3.4),.06);pole((0,y,3.4),(2.15,y,h),.06)
  pole((0,-2.65,3.4),(0,2.65,3.4),.09)
  walls=[((0,2.5,h/2),(4,.12,h)),((-2,0,h/2),(.12,5,h)),((2,0,h/2),(.12,5,h)),((-1.27,-2.5,h/2),(1.45,.12,h)),((1.27,-2.5,h/2),(1.45,.12,h)),((0,-2.5,2.04),(1.1,.12,.12))]
  if stage!=1:
   for i,(c,s) in enumerate(walls):
    if stage==2 and i%2:continue
    plank(c,s,'clay' if i%2 else 'sand');coll.append(box(name+f'_wall{i}-convcolonly',c,s,'bark'))
  if stage!=1:
   for y in (-2.5,2.5):
    if stage==2 and y>0:continue
    verts=[(-2,y,h),(2,y,h),(0,y,3.4)]
    parts.append(mesh('gable',verts,[(0,1,2) if y<0 else (2,1,0)],'sand'))
  # Visible wattle lattice on side walls, survives in frame construction stage.
  for side in (-1,1):
   for k in range(8):pole((side*2.07,-2.5,.2+k*.23),(side*2.07,2.5,.2+k*.23),.018,'bark',5)
  if stage!=1:
   for side in (-1,1):
    for k in range(12):
     if stage==2 and k%2:continue
     y0=-2.7+k*5.4/12;y1=y0+5.4/12-.01
     verts=[(0,y0,3.4),(side*2.28,y0,2.0),(side*2.28,y1,2.0),(0,y1,3.4)]
     face=(3,2,1,0) if side<0 else (0,1,2,3)
     parts.append(mesh('thatch',verts,[face],'straw' if k%3 else 'wood_light'))
     pole((0,y0,3.42),(side*2.29,y0,2.02),.026,'straw',5)
 elif item=='drying_rack':
  for x in (-.8,.8):
   for y in (-.4,.4):pole((x,y,0),(x*.8,0,1.6),.05,collision=True)
  pole((-.95,0,1.55),(.95,0,1.55),.065)
  for z in (.45,.85,1.2):pole((-.8,0,z),(.8,0,z),.032)
  for x in (-.6,-.2,.2,.6):pole((x,0,.4),(x,0,1.55),.012,'linen',6)
  for x in (-.7,.7):
   for z in (.45,.85,1.2):pole((x,0,z-.03),(x,0,z+.03),.052,'wood_light',8)
 elif item=='knapping_stone':
  stone((-.52,.12,.26),(.35,.28,.26),1);stone((.3,0,.19),(.45,.32,.19),2)
  for k in range(7):
   a=k*2.4;parts.append(blob('flake',(math.cos(a)*.5+.1,math.sin(a)*.5,.026),(.06,.045,.026),'flint',k,rings=3,sides=5))
 elif item=='storage_pit':
  parts.append(blob('pit_shadow',(0,0,.015),(.95,.7,.015),'soil_dark',1,rings=3,sides=12))
  for k in range(12):
   a=k*math.tau/12;stone((math.cos(a)*.95,math.sin(a)*.72,.1),(.21,.16,.1),k)
  for k in range(7):plank((0,(k-3)*.19,.24),(1.9,.17,.055),'bark' if k%2 else 'wood_light')
  for x in (-.55,.55):plank((x,0,.29),(.08,1.35,.05),'bark_dark')
 elif item=='latrine':
  for x in (-.55,.55):
   for y in (-.6,.6):pole((x,y,0),(x,y,1.65),.055,collision=True)
  for y in (-.6,.6):pole((-.6,y,1.65),(.6,y,1.65),.05)
  for x in (-.55,.55):
   for k in range(8):pole((x,-.6,.05+k*.18),(x,.6,.05+k*.18),.035,'bark',5)
  for k in range(8):pole((-.55,.6,.05+k*.18),(.55,.6,.05+k*.18),.035,'bark',5)
  for x in (-.38,.38):plank((x,0,.45),(.22,.8,.06),'wood_light')
  for y in (-.35,.35):plank((0,y,.45),(.60,.10,.06),'wood_light')
 else:raise ValueError(item)
 ob=join(parts,name)
 z=min(v.co.z for v in ob.data.vertices)
 for o in [ob]+coll:
  for v in o.data.vertices:v.co.z-=z
 return [ob]+coll
