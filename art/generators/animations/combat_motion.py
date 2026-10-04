"""Original hand-authored P2 unarmed combat on the common40-bone human rig.
No locomotor Root/Hips XY translation; hips articulate vertically in falls.
Export: tools/art/export.py -- combat_motion.py anims/combat_basic.glb '{}'
"""
import math,pathlib,sys
import bpy
from mathutils import Matrix,Vector
sys.path.insert(0,str(pathlib.Path(__file__).parent))
import human_motion as base
import human_motion_extended as ext
CLIPS={'unarmed_ready':2.4,'punch_a':.8,'punch_b':.8,'block':1.2,'shove':.9,'hit_react_front':.8,'stagger':1.6,'downed_loop':3.2,'death_a':2.4}
LOOPS={'unarmed_ready','downed_loop'}
SETS={'combat_basic':CLIPS}
BASE_POSE=ext.BASE_POSE

def pose(arm,name,t):
 if name not in CLIPS:BASE_POSE(arm,name,t);return
 s=math.sin(math.tau*t);p=base.pulse(t)
 BASE_POSE(arm,'idle',t)
 hands={'Left':Vector((.15,-.35,1.46+.008*s)),'Right':Vector((-.15,-.35,1.46-.008*s))}
 if name=='unarmed_ready':
  ext.stance(arm,.83,4,head=0);ext.arms(arm,hands,{'Left':.95,'Right':.95},direction=(0,-.8,.6));return
 if name in ('punch_a','punch_b'):
  # A short preparation, fast extension, held contact and slower recovery.
  if t<.25:u=base.smooth(t/.25)*-.12
  elif t<.46:u=base.smooth((t-.25)/.21)
  elif t<.54:u=1
  else:u=1-base.smooth((t-.54)/.46)
  side='Right' if name=='punch_a' else 'Left';sgn=-1 if side=='Right' else 1
  base.local(arm,'Hips',z=sgn*9*max(0,u));ext.stance(arm,.83,7*max(0,u),head=0)
  hands[side]=Vector((sgn*(.15-.06*max(0,u)),-.35-.24*u,1.46-.025*max(0,u)))
  opposite='Left' if side=='Right' else 'Right';hands[opposite]=Vector((-sgn*.13,-.30,1.45))
  ext.arms(arm,hands,{'Left':.95,'Right':.95},direction=(0,-1,.08));base.local(arm,'Head',z=-sgn*6*max(0,u))
 elif name=='block':
  ext.stance(arm,.82-.03*p,4,head=-2*p)
  hands['Left']=hands['Left'].lerp(Vector((.05,-.35,1.54)),p)
  hands['Right']=hands['Right'].lerp(Vector((-.05,-.37,1.51)),p)
  ext.arms(arm,hands,{'Left':.9,'Right':.9},direction=(0,-.25,.97))
 elif name=='shove':
  # Open palms push at chest height. Body leans with the effort; feet plant.
  force=math.sin(math.pi*t)**4;ext.stance(arm,.82,12*force,head=0)
  hands={side:Vector((sign*.15,-.33-.25*force,1.44-.06*force)) for side,sign in [('Left',1),('Right',-1)]}
  ext.arms(arm,hands,direction=(0,-.15,.99))
 elif name=='hit_react_front':
  ext.stance(arm,.83-.06*p,-17*p,head=-9*p)
  hands={side:Vector((sign*(.15+.04*p),-.33+.08*p,1.42+.05*p)) for side,sign in [('Left',1),('Right',-1)]}
  ext.arms(arm,hands,{'Left':.75,'Right':.75},direction=(0,-.5,.87))
 elif name=='stagger':
  ext.stance(arm,.82-.13*p,4*p,head=0)
  base.local(arm,'Spine',x=2*p,y=14*math.sin(4*math.pi*t)*p)
  hands={side:Vector((sign*(.23+.15*p),-.20,1.21+.05*p)) for side,sign in [('Left',1),('Right',-1)]}
  ext.arms(arm,hands,{'Left':.3,'Right':.3},direction=(0,-1,0))
 elif name=='downed_loop':
  BASE_POSE(arm,'sleep_loop',t)
  ext.arms(arm,{'Left':Vector((.30,.22,.20)),'Right':Vector((-.24,.38,.24))},{'Left':.08,'Right':.08},direction=(0,-1,0))
 elif name=='death_a':
  u=min(1,t*1.6);BASE_POSE(arm,'lie_down',u)
  end={'Left':Vector((.30,.22,.20)),'Right':Vector((-.24,.38,.24))}
  hands={side:hands[side].lerp(end[side],base.smooth(u)) for side in end}
  ext.arms(arm,hands,{'Left':.1,'Right':.1},direction=(0,-1,0))
 bpy.context.view_layer.update()

def generate(params):
 bpy.context.scene.render.fps=30;bpy.context.scene.frame_start=1;arm=base.create_skeleton();base.pose=pose;base.LOOPS|=LOOPS
 for name,duration in CLIPS.items():base.add_action(arm,name,duration)
 for pb in arm.pose.bones:pb.matrix_basis=Matrix.Identity(4)
 arm['animation_set']='combat_basic';arm['root_motion']='Root fixed; Hips horizontal fixed; vertical floor posture articulation'
 return [arm]
