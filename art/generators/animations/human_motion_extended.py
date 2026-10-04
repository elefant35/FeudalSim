"""P1 human motion expansion, retaining every P0 action and rig unchanged.
Rebuild using tools/art/export.py with {'set':'social'} etc. All clips30fps,
Root fixed, Hips horizontal fixed; vertical articulation grounds the character.
"""
import math,pathlib,sys
import bpy
from mathutils import Matrix,Vector
sys.path.insert(0,str(pathlib.Path(__file__).parent))
import human_motion as base
P1={
 'locomotion':{'crouch_walk':1.1,'carry_walk':.9,'jump':.8,'fall':1.0,'land':.6},
 'social':{'shrug':1.6,'laugh':2.4,'argue_a':2.2,'argue_b':2.6,'cry':3.2},
 'needs':{'drink_skin':2.6},
 'work':{'carry_log_walk':.9,'dig_loop':2.0,'stoke_fire':2.0},
}
LOOPS={'crouch_walk','carry_walk','fall','laugh','argue_a','argue_b','cry','drink_skin','carry_log_walk','dig_loop','stoke_fire'}
BASE_POSE=base.pose

def arms(arm,hands,grips=None,direction=(0,-.65,-.76),pole='side'):
 for side,sign in [('Left',1),('Right',-1)]:
  base.joint(arm,side+'UpperArm',side+'LowerArm',hands[side],(sign,0,-.2) if pole=='side' else (0,1,-.25))
  base.aim(arm,side+'Hand',direction)
  base.grip(arm,side,(grips or {}).get(side,0))
 bpy.context.view_layer.update()

def stance(arm,hip=.86,lean=0,feet=None,head=0):
 pb=arm.pose.bones['Hips'];pb.location=pb.bone.matrix_local.to_3x3().inverted()@Vector((0,0,hip-.86))
 base.local(arm,'Spine',x=lean*.45);base.local(arm,'Chest',x=lean*.35);base.local(arm,'UpperChest',x=lean*.20)
 base.local(arm,'Head',x=head-lean)
 feet=feet or {'Left':Vector((.13,-.06,.085)),'Right':Vector((-.13,.06,.085))}
 for side in ('Left','Right'):
  base.joint(arm,side+'UpperLeg',side+'LowerLeg',feet[side],(0,-1,.05));base.aim(arm,side+'Foot',(0,-1,-.04))

def look_at_work(arm):
 target=(arm.pose.bones['RightHand'].head+arm.pose.bones['LeftHand'].head)*.5;origin=arm.pose.bones['Head'].head
 pitch=math.degrees(math.atan2(origin.z-target.z,max(.1,origin.y-target.y)))
 # Trunk rotations contribute to the head's final gaze.
 q=arm.pose.bones['Neck'].matrix.to_quaternion();f=q@arm.data.bones['Neck'].matrix_local.to_quaternion().inverted()@Vector((0,-1,0))
 trunk=math.degrees(math.atan2(-f.z,-f.y))
 base.local(arm,'Head',x=max(-35,min(48,pitch))-trunk)

def extended_pose(arm,name,t):
 if name not in {clip for clips in P1.values() for clip in clips}:
  BASE_POSE(arm,name,t);return
 s=math.sin(math.tau*t);c=math.cos(math.tau*t);p=base.pulse(t)
 if name in ('carry_walk','carry_log_walk'):
  BASE_POSE(arm,'walk',t)
  height=1.23 if name=='carry_walk' else 1.18
  hands={'Left':Vector((.23,-.40,height)),'Right':Vector((-.23,-.40,height))}
  arms(arm,hands,{'Left':.65,'Right':.65},direction=(0,-.30,.95));base.local(arm,'Head',x=21);return
 if name=='crouch_walk':
  BASE_POSE(arm,'idle',t);feet={}
  for side,phase,sign in [('Left',t%1,1),('Right',(t+.5)%1,-1)]:
   f,z=base.footcycle(phase,.8,1.1,.6);feet[side]=Vector((.13*sign,-f,z))
  stance(arm,.55+.012*(1-math.cos(4*math.pi*t)),25,feet,head=8)
  arms(arm,{'Left':Vector((.23,-.25,.80)),'Right':Vector((-.23,-.25,.80))});return
 BASE_POSE(arm,'idle',t)
 if name in ('jump','fall','land'):
  if name=='jump':
   hip=.86-.14*math.sin(math.pi*t*2) if t<.25 else .86+.20*math.sin(math.pi*(t-.25)/.75)
   lift=.30*math.sin(math.pi*t)**2
  elif name=='fall':hip=.86;lift=.18
  else:hip=.86-.24*p;lift=0
  stance(arm,hip,8*p,{'Left':Vector((.13,-.08,.085+lift)),'Right':Vector((-.13,.08,.085+lift))},head=0)
  hands={'Left':Vector((.38,-.15,1.14+.15*p)),'Right':Vector((-.38,-.15,1.14+.15*p))}
  arms(arm,hands,direction=(0,-1,0));return
 if name=='shrug':
  base.local(arm,'LeftShoulder',y=-10*p);base.local(arm,'RightShoulder',y=10*p)
  hands={'Left':Vector((.23+.13*p,-.23,.96+.30*p)),'Right':Vector((-.23-.13*p,-.23,.96+.30*p))}
  arms(arm,hands,direction=(0,-.25,.97));base.local(arm,'Head',z=-7*p)
 elif name=='laugh':
  stance(arm,.86,4+2*math.sin(6*math.pi*t),head=-3)
  arms(arm,{'Left':Vector((.13,-.20,1.07)),'Right':Vector((-.18,-.30,1.21+.035*math.sin(6*math.pi*t)))},{'Left':.2,'Right':.18})
  base.local(arm,'Jaw',x=5*(.5+.5*math.sin(6*math.pi*t)))
 elif name in ('argue_a','argue_b'):
  strong=name=='argue_a';stance(arm,.84,6+2*s,head=3*s)
  hands={'Left':Vector((.26,-.29-.09*s,1.15+.16*c)),'Right':Vector((-.19,-.33-.13*c,1.18+.17*s))}
  arms(arm,hands,{'Left':.25,'Right':.42 if strong else .18},direction=(0,-.7,.7));base.local(arm,'Head',z=6*s)
  base.local(arm,'Jaw',x=4*(.5+.5*math.sin(8*math.pi*t)))
 elif name=='cry':
  stance(arm,.82,14+1.5*math.sin(6*math.pi*t),head=25)
  arms(arm,{'Left':Vector((.10,-.29,1.36)),'Right':Vector((-.10,-.29,1.36))},{'Left':.10,'Right':.10},direction=(0,-.15,.99))
 elif name=='drink_skin':
  stance(arm,.86,0,head=24)
  hands={'Left':Vector((.22,-.28,1.13)),'Right':Vector((-.10,-.43,1.26+.23*p))}
  arms(arm,hands,{'Right':.75},direction=(0,-.25,.97))
 elif name=='dig_loop':
  stance(arm,.71,24+8*p,head=25)
  hands={'Left':Vector((.07,-.52,.86+.17*p)),'Right':Vector((-.07,-.48,1.04+.17*p))}
  arms(arm,hands,{'Left':.8,'Right':.8},direction=(0,-.65,-.76));look_at_work(arm)
 elif name=='stoke_fire':
  BASE_POSE(arm,'warm_hands_loop',t)
  hands={'Left':Vector((.17,-.48,.63)),'Right':Vector((-.16,-.53-.08*p,.50+.10*p))}
  arms(arm,hands,{'Right':.7},direction=(0,-.87,-.49));look_at_work(arm)
 bpy.context.view_layer.update()

def generate(params):
 bpy.context.scene.render.fps=30;bpy.context.scene.frame_start=1
 arm=base.create_skeleton();family=params.get('set','locomotion')
 clips={**base.SETS[family],**P1.get(family,{})};base.LOOPS|=LOOPS;base.pose=extended_pose
 for name,duration in clips.items():base.add_action(arm,name,duration)
 for pb in arm.pose.bones:pb.matrix_basis=Matrix.Identity(4)
 arm['animation_set']=family;arm['root_motion']='Root fixed, Hips XY fixed, vertical articulated postures'
 return [arm]

# Pose/clip aliases for the standalone native-body preview and pose auditor.
pose=extended_pose
SETS=P1
