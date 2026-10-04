"""P2 primitive hunting equipment and carcass props: no polished/fantasy materials."""
import math,pathlib,random,sys
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]))
from common import *

def rope(points,r=.006,colour='linen',sides=4):
 out=[]
 for a,b in zip(points,points[1:]):out.append(tube('cord',a,b,r,r,colour,sides))
 return out

def generate(params):
 item=params['item'];parts=[];coll=[];name='prop_'+item+'_a'
 if item=='wooden_spear':
  parts.append(tube('haft',(0,0,-.6),(0,0,1.05),.025,.018,'bark',10))
  parts.append(tube('whittled_tip',(0,0,1.05),(0,0,1.37),.018,.002,'wood_light',8))
  parts += rope([(.028*math.cos(k*.9),.028*math.sin(k*.9),.86+k*.007) for k in range(24)],.0035,'bark_dark')
 elif item=='sling':
  parts.append(blob('pouch',(0,0,.32),(.048,.08,.015),'wool_brown',1,rings=3,sides=8))
  parts += rope([(-.012,0,0),(-.05,0,.12),(-.045,0,.29)],.007,'linen',6)
  parts += rope([(.012,0,0),(.052,0,.13),(.045,0,.29)],.007,'linen',6)
  parts += rope([(.02*math.cos(k*math.tau/8),.02*math.sin(k*math.tau/8),-.015) for k in range(9)],.004,'linen')
 elif item=='self_bow':
  # Bow stave at grip origin; bend away from the string in -Y.
  points=[(0,-.12*math.sin(k*math.pi/10),-.72+k*.144) for k in range(11)]
  for k,(a,b) in enumerate(zip(points,points[1:])):parts.append(tube('stave',a,b,.019*(1-abs(4.5-k)/8),.019*(1-abs(4.5-k)/8),'wood_light',7))
  parts += rope([(0,0,-.72),(0,.035,0),(0,0,.72)],.003,'linen',5)
  for k in range(5):parts.append(tube('grip',(0,-.013,-.05+k*.02),(0,-.013,-.04+k*.02),.025,.025,'bark_dark',8))
  # Arrows are delivered separately; the game attaches a nocked arrow when drawing.
 elif item=='arrows':
  for i in range(3):
   x=(i-1)*.032
   parts.append(tube('shaft',(x,0,-.22),(x,0,.46),.006,.004,'wood_light',7))
   parts.append(tube('point',(x,0,.46),(x,0,.53),.014,.001,'flint',6))
   for s in (-1,1):parts.append(mesh('fletching',[(x,0,-.22),(x+s*.023,0,-.22),(x+s*.017,0,-.14),(x,0,-.11)],[(0,1,2,3)],'linen'))
 elif item=='round_shield':
  # Disc lies in XZ; grip faces +Y. A centre boss and hide rim stay functional.
  sides=16;r=.38;centre=(0,0,0)
  verts=[(0,-.025,0),(0,.025,0)]+[(r*math.cos(k*math.tau/sides),y,r*math.sin(k*math.tau/sides)) for y in (-.025,.025) for k in range(sides)]
  faces=[]
  for k in range(sides):
   nxt=(k+1)%sides;faces +=[(0,2+k,2+nxt),(1,2+sides+nxt,2+sides+k),(2+k,2+sides+k,2+sides+nxt,2+nxt)]
  parts.append(mesh('wooden_disc',verts,faces,'wood_light'))
  for x in (-.23,-.08,.08,.23):
   h=math.sqrt(r*r-x*x)*.95
   parts.append(mesh('plank_seam',[(x-.002,-.0255,-h),(x+.002,-.0255,-h),(x+.002,-.0255,h),(x-.002,-.0255,h)],[(0,1,2,3)],'bark_dark'))
  parts.append(blob('boss',(0,-.038,0),(.09,.07,.09),'iron',1,rings=4,sides=10))
  parts += rope([(r*math.cos(k*math.tau/sides),0,r*math.sin(k*math.tau/sides)) for k in range(sides+1)],.013,'wool_brown',5)
  parts.append(tube('hand_grip',(-.1,.09,0),(.1,.09,0),.018,.018,'bark',8))
 elif item=='hide_frame':
  for x in (-.75,.75):parts.append(tube('post',(x,0,0),(x,0,1.75),.055,.035,'bark',8))
  for z in (.2,1.65):parts.append(tube('crossbar',(-.75,0,z),(.75,0,z),.05,.04,'bark',8))
  outline=[(-.32,-.015,.33),(-.5,-.015,.58),(-.4,-.015,.92),(-.55,-.015,1.2),(-.25,-.015,1.35),(-.16,-.015,1.5),(.16,-.015,1.5),(.25,-.015,1.35),(.55,-.015,1.2),(.4,-.015,.92),(.5,-.015,.58),(.32,-.015,.33)]
  verts=[(0,-.015,.95)]+outline;parts.append(mesh('hide',verts,[(0,i+1,(i+1)%len(outline)+1) for i in range(len(outline))],'wool_brown'))
  for i,p in enumerate(outline):
   end=(math.copysign(.75,p[0]),0,p[2]);parts+=rope([p,end],.009,'linen',5)
 elif item=='snare':
  parts.append(tube('stake',(0,0,0),(0,0,.32),.025,.015,'bark',8))
  points=[(.16+.18*math.cos(k*math.tau/24),0,.27+.18*math.sin(k*math.tau/24)) for k in range(25)]
  parts+=rope(points,.004,'linen');parts+=rope([(0,0,.3),points[0]],.006,'linen')
 elif item=='deer_carcass':
  parts.append(blob('body',(0,0,.29),(.38,.67,.27),'wool_brown',1,rings=5,sides=10))
  parts.append(blob('neck',(0,-.61,.21),(.15,.25,.15),'wool_brown',2,rings=4,sides=8))
  parts.append(blob('head',(0,-.88,.16),(.12,.22,.12),'bark',3,rings=4,sides=8))
  parts.append(blob('nose',(0,-1.085,.145),(.07,.035,.05),'bark_dark',5,rings=3,sides=6))
  for side in (-1,1):parts.append(blob('closed_eye',(side*.105,-.94,.20),(.014,.028,.006),'bark_dark',5,rings=3,sides=6))
  parts.append(blob('short_tail',(0,.66,.29),(.07,.12,.065),'linen',5,rings=3,sides=6))
  for side in (-1,1):
   for y in (-.45,.43):
    parts.append(tube('leg',(side*.23,y,.24),(side*.46,y+.20,.09),.075,.045,'wool_brown',8))
    parts.append(tube('lower_leg',(side*.46,y+.20,.09),(side*.54,y-.06,.055),.044,.022,'bark',7))
  for side in (-1,1):parts.append(mesh('ear',[(side*.10,-.82,.24),(side*.22,-.84,.36),(side*.20,-.93,.29)],[(0,1,2)],'wool_brown'))
  parts.append(blob('cut',(0,-.28,.51),(.12,.16,.03),'blood',4,rings=3,sides=8))
 else:raise ValueError(item)
 ob=join(parts,name)
 if item in ('hide_frame','snare','deer_carcass'):
  z=min(v.co.z for v in ob.data.vertices)
  for v in ob.data.vertices:v.co.z-=z
 if item=='hide_frame':
  for side in (-1,1):coll.append(convex_trunk(name+'_post_'+str(side),(side*.75,0,0),(side*.75,0,1.75),.055))
 return [ob]+coll
