"""Hand-authored M2 human motion, sampled at30fps on the shared40-bone rig.
No external motion data. Root remains fixed; Hips X/Y never translate. Vertical
posture offsets ground seated/kneeling/supine clips. Analytic two-bone IK gives
planted gait feet at the contract speeds; all loops include duplicate endpoints.
Run via tools/art/export.py -- this.py <set>.glb '{"set":"work"}'.
"""
import math, pathlib, sys
import bpy
from mathutils import Matrix, Quaternion, Vector
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]/'settlers'))
from skeleton import create_skeleton
SETS={
 'locomotion':{'idle':3.2,'idle_alt':4.0,'walk':.9,'jog':.64,'sprint':.52,'turn_left':.8,'turn_right':.8},
 'rest':{'sit_ground_down':1.8,'sit_ground_loop':3.2,'sit_ground_up':1.8,'lie_down':2.4,'sleep_loop':4.0,'lie_up':2.4,'warm_hands_loop':2.8},
 'social':{'talk_a':2.8,'talk_b':3.2,'talk_c':3.0,'nod':1.0,'shake_head':1.2,'point':1.6,'wave':2.0},
 'needs':{'eat_loop':2.4,'drink_kneel_loop':3.2},
 'work':{'chop_loop':1.8,'pick_up':1.8,'forage_pick_loop':2.6,'knap_loop':1.4},
}
LOOPS={'idle','idle_alt','walk','jog','sprint','sit_ground_loop','sleep_loop','warm_hands_loop','talk_a','talk_b','talk_c','eat_loop','drink_kneel_loop','chop_loop','forage_pick_loop','knap_loop'}
def smooth(x): return x*x*(3-2*x)
def lerp(a,b,u): return a+(b-a)*u
def pulse(t): return math.sin(math.pi*t)**2

def aim(arm,name,direction):
 """Set bone's world direction preserving authored rest roll, without translations."""
 pb=arm.pose.bones[name]; rb=pb.bone
 rest=rb.matrix_local.to_quaternion()
 q=(rest@Vector((0,1,0))).rotation_difference(Vector(direction).normalized())@rest
 pb.matrix=Matrix.Translation(pb.head)@q.to_matrix().to_4x4()
 bpy.context.view_layer.update()

def joint(arm,upper,lower,target,pole):
 a=arm.pose.bones[upper].head.copy(); target=Vector(target)
 la=arm.data.bones[upper].length; lb=arm.data.bones[lower].length
 delta=target-a; d=max(.001,min(delta.length,la+lb-.0001)); axis=delta.normalized()
 pole=Vector(pole); bend=pole-axis*pole.dot(axis)
 if bend.length<.001: bend=Vector((0,-1,0))
 bend.normalize(); along=(la*la-lb*lb+d*d)/(2*d)
 elbow=a+axis*along+bend*math.sqrt(max(0,la*la-along*along))
 aim(arm,upper,elbow-a); aim(arm,lower,target-elbow)

def local(arm,name,x=0,y=0,z=0):
 pb=arm.pose.bones[name]; pb.rotation_mode='QUATERNION'
 q=Quaternion((1,0,0),math.radians(x))@Quaternion((0,1,0),math.radians(y))@Quaternion((0,0,1),math.radians(z))
 rest=pb.bone.matrix_local.to_quaternion(); pb.rotation_quaternion=rest.inverted()@q@rest
 bpy.context.view_layer.update()

def grip(arm,side,amount):
 # Curl about each bone's local Z, thumb opposes the fingers. The three inner
 # fingers share the Middle chain, as required by the abbreviated hand rig.
 for name,factor in [('IndexProximal',65),('IndexIntermediate',70),('MiddleProximal',70),('MiddleIntermediate',72)]:
  pb=arm.pose.bones[side+name]; pb.rotation_quaternion=Quaternion((0,0,1),math.radians(factor*amount*(1 if side=='Left' else -1)))
 pb=arm.pose.bones[side+'ThumbMetacarpal']; pb.rotation_quaternion=Quaternion((1,0,0),math.radians(30*amount))@Quaternion((0,0,1),math.radians(-25*amount if side=='Left' else 25*amount))
 pb=arm.pose.bones[side+'ThumbProximal']; pb.rotation_quaternion=Quaternion((0,0,1),math.radians(40*amount if side=='Left' else -40*amount))

def footcycle(phase,speed,period,duty):
 stride=speed*period*duty
 if phase<duty: return (stride*.5-speed*period*phase,.085)
 u=(phase-duty)/(1-duty)
 return (-stride*.5+stride*smooth(u),.085+.16*math.sin(math.pi*u))

def pose(arm,name,t):
 for pb in arm.pose.bones:
  pb.rotation_mode='QUATERNION'; pb.rotation_quaternion=Quaternion(); pb.location=(0,0,0); pb.scale=(1,1,1)
 s=math.sin(math.tau*t); c=math.cos(math.tau*t)
 hip=.86; lean=0; pelvis=0; bend=0; rest=None; progress=0; yaw=0
 hands={'Left':Vector((.23,-.025,.93)),'Right':Vector((-.23,-.025,.93))}
 feet={'Left':Vector((.11,0,.085)),'Right':Vector((-.11,0,.085))}
 if name in ('walk','jog','sprint'):
  speed,period,duty={'walk':(1.6,.9,.50),'jog':(4,19/30,.36),'sprint':(6.5,16/30,.29)}[name]
  hip={'walk':.74,'jog':.70,'sprint':.68}[name]+(.016 if name=='walk' else .025)*(1-math.cos(4*math.pi*t))
  lean={'walk':4,'jog':10,'sprint':17}[name]
  for side,phase,sign in [('Left',t%1,1),('Right',(t+.5)%1,-1)]:
   forward,z=footcycle(phase,speed,period,duty)
   feet[side]=Vector((.11*sign,-forward,z))
   hands[side]=Vector((.22*sign,.23*math.sin(math.tau*phase),1.02 if name=='walk' else 1.20))
   maxreach=max(abs(v.y) for v in feet.values())
  hip=min(hip,.085+math.sqrt(max(.01,.803**2-maxreach**2))-.035)
 elif name in ('sit_ground_down','sit_ground_up','sit_ground_loop','knap_loop'):
  rest='sit'; progress=1 if name in ('sit_ground_loop','knap_loop') else smooth(t if name.endswith('down') else 1-t)
 elif name in ('lie_down','lie_up','sleep_loop'):
  rest='lie'; progress=1 if name=='sleep_loop' else smooth(t if name=='lie_down' else 1-t)
 elif name in ('warm_hands_loop','forage_pick_loop','drink_kneel_loop'):
  rest='kneel'; progress=1
 elif name=='pick_up':
  rest='kneel'; progress=pulse(t)
 if rest=='sit':
  hip=lerp(.86,.32,progress); lean=lerp(0,12,progress)
  for side,sign in [('Left',1),('Right',-1)]:
   feet[side]=feet[side].lerp(Vector((.28*sign,-.38,.085)),progress)
   hands[side]=hands[side].lerp(Vector((.18*sign,-.36,.48)),progress)
 elif rest=='kneel':
  hip=lerp(.86,.44,progress); lean=lerp(0,22,progress)
  feet['Left']=feet['Left'].lerp(Vector((.14,-.34,.085)),progress)
  feet['Right']=feet['Right'].lerp(Vector((-.12,.40,.085)),progress)
  for side,sign in [('Left',1),('Right',-1)]:
   hands[side]=hands[side].lerp(Vector((.18*sign,-.43,.50)),progress)
 elif rest=='lie':
  hip=lerp(.86,.17,progress); pelvis=lerp(0,-87,progress)
  for side,sign in [('Left',1),('Right',-1)]:
   feet[side]=feet[side].lerp(Vector((.12*sign,-.72,.10)),progress)
   hands[side]=hands[side].lerp(Vector((.20*sign,.28,.25)),progress)
 if name=='idle_alt': yaw=4*math.sin(math.tau*t); lean=1+1.1*c
 if name in ('turn_left','turn_right'):
  yaw=(1 if name=='turn_left' else -1)*25*pulse(t)
  feet['Left'].y-=.055*pulse(t); feet['Right'].y+=.055*pulse(t)
 # Hips local Y follows Blender Z for the vertical rest bone; assign global
 # Z offset through inverse rest basis rather than an axis-name assumption.
 pb=arm.pose.bones['Hips']; pb.location=pb.bone.matrix_local.to_3x3().inverted()@Vector((0,0,hip-.86))
 local(arm,'Hips',x=pelvis,z=yaw)
 local(arm,'Spine',x=lean*.45); local(arm,'Chest',x=lean*.35)
 local(arm,'UpperChest',x=lean*.20+.6*s,z=.7*s if rest!='lie' else 0)
 local(arm,'Head',x=-lean*.45,z=yaw*.25)
 if name=='nod': local(arm,'Head',x=10*math.sin(4*math.pi*t)*pulse(t))
 if name=='shake_head': local(arm,'Head',z=17*math.sin(4*math.pi*t)*pulse(t))
 if name.startswith('talk_'):
  a={'talk_a':.10,'talk_b':.15,'talk_c':.07}[name]
  hands['Right']=Vector((-.25,-.22-a*(.5+.5*s),1.09+a*c))
  hands['Left']=Vector((.26,-.20+a*s,1.07+a*.5*math.sin(math.tau*t+1)))
  local(arm,'Head',x=2*s,z=3*math.sin(math.tau*t+1))
  local(arm,'Jaw',x=2*(.5+.5*math.sin(6*math.pi*t)))
 if name=='point':
  hands['Right']=hands['Right'].lerp(Vector((-.17,-.57,1.35)),pulse(t)); grip(arm,'Right',.65*pulse(t))
  local(arm,'RightIndexProximal'); local(arm,'RightIndexIntermediate')
 if name=='wave': hands['Right']=hands['Right'].lerp(Vector((-.37+.10*math.sin(6*math.pi*t),-.16,1.75)),pulse(t))
 if name=='warm_hands_loop':
  for side,sign in [('Left',1),('Right',-1)]: hands[side]=Vector((.17*sign,-.49,.60+.022*math.sin(math.tau*t+sign)))
 if name=='forage_pick_loop':
  hands['Left']=Vector((.18,-.37,.53)); hands['Right']=Vector((-.17,-.51,.24+.32*pulse(t)))
  grip(arm,'Right',.55*(1-pulse(t))); local(arm,'Head',x=30)
 if name=='drink_kneel_loop':
  lift=pulse(t); local(arm,'Head',x=30*(1-lift))
  for side,sign in [('Left',1),('Right',-1)]: hands[side]=Vector((.065*sign,-.50-.08*lift,.32+.70*lift)); grip(arm,side,.22)
 if name=='eat_loop':
  lift=pulse(t); hands['Left']=Vector((.16,-.37,1.25)); hands['Right']=Vector((-.07,-.36+.06*lift,1.22+.23*lift)); local(arm,'Head',x=32)
  grip(arm,'Right',.52); grip(arm,'Left',.22); local(arm,'Jaw',x=2.5*pulse((t*3)%1))
 if name=='pick_up':
  hands['Right']=Vector((-.20,-.40,1.28)).lerp(Vector((-.18,-.49,.25)),progress); grip(arm,'Right',.6*smooth(t)); local(arm,'Head',x=25+15*progress)
 if name=='knap_loop':
  hands['Left']=Vector((.08,-.52,.68)); hands['Right']=Vector((-.12,-.52,.74+.13*pulse(t)))
  grip(arm,'Right',.8); grip(arm,'Left',.42); local(arm,'Head',x=28)
 if name=='chop_loop':
  # Preparation -> lift -> sharp strike -> recovery; no camera-crossing motion.
  knots=[(0,1.23),(.33,1.80),(.53,1.85),(.66,1.18),(1,1.23)]
  for (a,za),(b,zb) in zip(knots,knots[1:]):
   if a<=t<=b: z=lerp(za,zb,smooth((t-a)/(b-a))); break
  forward=.46+.17*(1-(z-1.18)/.67)
  hands['Right']=Vector((-.065,-forward,z)); hands['Left']=Vector((.065,-forward-.025,z-.14))
  grip(arm,'Left',.85); grip(arm,'Right',.85); local(arm,'Head',x=lerp(10,-12,(z-1.18)/.67))
 if name=='sleep_loop':
  local(arm,'Chest',x=.8*s); local(arm,'Head',z=7)
 for side,sign in [('Left',1),('Right',-1)]:
  joint(arm,side+'UpperLeg',side+'LowerLeg',feet[side],(0,-1,.05))
  aim(arm,side+'Foot',(0,-1,-.04))
  arm_pole=(0,1,-.25) if name in ('walk','jog','sprint') else (sign,0,-.20)
  joint(arm,side+'UpperArm',side+'LowerArm',hands[side],arm_pole)
  handdir=(0,-1,-.15) if name in ('talk_a','talk_b','talk_c','point','wave') else (0,-.65,-.76)
  if name in ('eat_loop','drink_kneel_loop'): handdir=(0,-.3,.95)
  if name=='chop_loop': handdir=(0,-.45,.89)
  if rest=='lie': handdir=(0,-1,0)
  aim(arm,side+'Hand',handdir)
 if name=='chop_loop':
  target=arm.pose.bones['RightHand'].head; origin=arm.pose.bones['Head'].head
  pitch=math.degrees(math.atan2(origin.z-target.z,max(.1,origin.y-target.y)))
  local(arm,'Head',x=max(-35,min(40,pitch)))
 bpy.context.view_layer.update()


def add_action(arm,name,duration):
 n=round(duration*30); action=bpy.data.actions.new(name)
 action['fps']=30; action['loop']=name in LOOPS; action['in_place']=True
 arm.animation_data_create(); arm.animation_data.action=action
 for frame in range(n+1):
  bpy.context.scene.frame_set(frame+1)
  pose(arm,name,frame/n)
  for pb in arm.pose.bones:
   pb.keyframe_insert(data_path='rotation_quaternion',frame=frame+1,group=pb.name)
   if pb.name=='Hips': pb.keyframe_insert(data_path='location',frame=frame+1,group=pb.name)
 action.use_fake_user=True
 track=arm.animation_data.nla_tracks.new(); track.name=name
 strip=track.strips.new(name,1,action); strip.action_frame_start=1; strip.action_frame_end=n+1
 arm.animation_data.action=None
 track.mute=True
 return action


def generate(params):
 bpy.context.scene.render.fps=30; bpy.context.scene.frame_start=1
 arm=create_skeleton(); family=params.get('set','locomotion')
 for name,duration in SETS[family].items(): add_action(arm,name,duration)
 for pb in arm.pose.bones: pb.matrix_basis=Matrix.Identity(4)
 arm['animation_set']=family; arm['root_motion']='Root static; Hips XY static; vertical posture offset preserved'
 return [arm]
