"""Small harvested forage and primitive craft materials (≤150 tris per forage pickup).
Held produce is modelled as the harvested plant part, without world-patch scenery.
"""
import math,pathlib,random,sys
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]))
from common import *
ITEMS=['wild_carrot','hemlock','field_mushroom','death_cap','ramsons','lily_of_the_valley','comfrey','foxglove','valerian','water_hemlock','bilberries','nightshade_berries','hazelnuts','blackberries','nettles','sorrel','sea_beet','wild_strawberries','yarrow','plantain','sphagnum']

def leaf(a,b,width,colour='leaf',serrated=False):
 a,b=Vector(a),Vector(b);c=(a+b)/2;side=Vector((width,0,0))
 verts=[a,c+side,b,c-side,c+Vector((0,-width*.22,0))]
 return mesh('leaf',verts,[(0,1,4),(1,2,4),(2,3,4),(3,0,4)],colour)

def generate(params):
 item=params['item'];seed=int(params.get('seed',601));rng=random.Random(seed);parts=[];coll=[]
 name='prop_'+item+'_a';forage=item.startswith('item_');plant=item[5:] if forage else item
 if not forage:
  if item in ('rough_log','pole'):
   r=.12 if item=='rough_log' else .045;length=1.4 if item=='rough_log' else 1.8
   parts.append(tube('wood',(0,0,-length*.35),(0,0,length*.65),r,r*.7,'bark',10))
   parts.append(tube('end',(0,0,length*.65),(0,0,length*.65+.005),r*.7,r*.7,'wood_light',10))
   parts.append(tube('snag',(0,0,.3),(.19,0,.45),r*.3,.012,'bark',6))
  elif item=='reed_thatch':
   for k in range(9):
    x,y=(k%3-1)*.045,(k//3-1)*.045
    parts.append(tube('reed',(x,y,-.25),(x+.035,y,.4+rng.random()*.09),.018,.009,'straw',5))
   for z in (-.12,.2):parts.append(tube('tie',(0,0,z),(0,0,z+.024),.1,.1,'linen',10))
  elif item=='clay':parts.append(blob('clay',(0,0,.03),(.13,.09,.10),'clay',seed,rings=5,sides=10))
  elif item=='antler_billet':
   parts.append(tube('antler',(0,0,-.12),(0,0,.21),.04,.056,'linen',10))
   parts.append(blob('crown',(0,0,.22),(.067,.055,.043),'limestone',seed,rings=3,sides=8))
  elif item=='pressure_flaker':
   parts.append(tube('handle',(0,0,-.07),(0,0,.11),.032,.025,'bark',10));parts.append(tube('tip',(0,0,.10),(0,0,.20),.023,.004,'linen',8))
  else:raise ValueError(item)
 elif plant in ('field_mushroom','death_cap'):
  for k in range(1 if plant=='death_cap' else 2):
   x=(k-.5)*.12;h=.12+k*.025
   parts.append(tube('stem',(x,0,0),(x,0,h),.017,.014,'linen',5))
   cap=blob('cap',(x,0,h),(.080,.075,.045),'linen' if plant=='field_mushroom' else 'moss',seed+k,rings=3,sides=6);parts.append(cap)
   # Gill underside uses the gameplay colour cue; death cap retains its base cup.
   parts.append(tube('gills',(x,0,h-.008),(x,0,h),.065,.065,'wool_brown' if plant=='field_mushroom' else 'linen',6))
   if plant=='death_cap':parts.append(blob('volva',(x,0,.019),(.026,.026,.019),'linen',seed,rings=3,sides=5))
 elif plant in ('wild_carrot','hemlock','valerian','water_hemlock'):
  for k in range(3):
   x=(k-1)*.055;r=.023 if plant in ('wild_carrot','hemlock') else .034
   parts.append(tube('root',(x,0,-.14),(x,.01,.06),.004,r,'sand' if plant!='wild_carrot' else 'wood_light',6))
   parts.append(tube('stem',(x,.01,.05),(x,.01,.18),.008,.004,'leaf',5))
   parts.append(leaf((x,.01,.09),(x+(-1)**k*.1,.01,.17),.034))
   if plant=='hemlock':parts.append(mesh('blotch',[(x-.004,-.008,.088),(x+.004,-.008,.088),(x+.004,-.008,.113),(x-.004,-.008,.113)],[(0,1,2,3)],'woad'))
 elif plant in ('bilberries','nightshade_berries','hazelnuts','blackberries','wild_strawberries'):
  for k in range(4 if plant=='nightshade_berries' else 5):
   a=k*2.4;x=math.cos(a)*.06;y=math.sin(a)*.04;z=.018+(k%2)*.035
   colour='hair_brown' if plant=='hazelnuts' else 'madder' if plant=='wild_strawberries' else 'hair_dark' if plant=='blackberries' else 'woad'
   parts.append(blob('fruit',(x,y,z),(.025,.023,.025),colour,seed+k,rings=3,sides=5))
   if plant=='nightshade_berries':
    verts=[(x,y,z+.025)]+[(x+.044*math.cos(j*math.tau/10),y+.044*math.sin(j*math.tau/10),z+.02) if j%2==0 else (x+.014*math.cos(j*math.tau/10),y+.014*math.sin(j*math.tau/10),z+.024) for j in range(10)]
    parts.append(mesh('star_calyx',verts,[(0,j+1,(j+1)%10+1) for j in range(10)],'leaf_dark'))
 elif plant=='sphagnum':
  for k in range(5):parts.append(blob('moss',((k%3-1)*.06,(k//3-.5)*.06,.035),(.06,.05,.035),'moss' if k%2 else 'leaf_light',seed+k,rings=3,sides=5))
 else:
  broad=plant in ('ramsons','lily_of_the_valley','comfrey','foxglove','sea_beet','plantain');width=.035 if broad else .018
  count=7 if broad else 8
  for k in range(count):
   x=(k%3-1)*.025;y=(k//3-1)*.018;h=.12+(k%3)*.025
   parts.append(tube('stem',(x,y,-.04),(x,y,h*.35),.005,.003,'leaf_dark',4))
   parts.append(leaf((x,y,h*.2),(x+(-1)**k*.04,y,h),width,'leaf' if k%3 else 'leaf_light'))
  if plant in ('yarrow','nettles'):
   # A few small lateral leaflets convey the finely divided/serrated harvest.
   for k in range(3):parts.append(leaf((0,0,.045+k*.024),((-.08 if k%2 else .08),0,.08+k*.024),.016))
 ob=join(parts,name)
 return [ob]
