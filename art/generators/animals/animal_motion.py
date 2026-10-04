"""Original quadruped actions: planted stance feet, species gait and articulated ears.
Analytic two-link IK reaches each wrist/hock; the distal segment preserves the
species' rest ankle-to-sole offset. No Root motion and no pelvis X/Y translation.
"""
import math,bpy
from mathutils import Matrix,Quaternion,Vector
LOOPS={'idle','walk','run','graze_loop','flee','sleep_loop'}
DURATIONS={'idle':3,'walk':1.2,'run':.6,'graze_loop':3.2,'alert':1.1,'flee':.6,'hit':.7,'death':1.7,'sleep_loop':3.6}
def smooth(t):return t*t*(3-2*t)
def pulse(t):return math.sin(math.pi*t)**2

def local(arm,name,x=0,y=0,z=0):
 pb=arm.pose.bones[name];rest=pb.bone.matrix_local.to_quaternion()
 q=Quaternion((1,0,0),math.radians(x))@Quaternion((0,1,0),math.radians(y))@Quaternion((0,0,1),math.radians(z))
 pb.rotation_quaternion=rest.inverted()@q@rest
 bpy.context.view_layer.update()

def aim(arm,name,direction):
 pb=arm.pose.bones[name];rest=pb.bone.matrix_local.to_quaternion();d=Vector(direction)
 q=(rest@Vector((0,1,0))).rotation_difference(d.normalized())@rest
 pb.matrix=Matrix.Translation(pb.head)@q.to_matrix().to_4x4();bpy.context.view_layer.update()

def joint(arm,upper,lower,target,pole):
 a=arm.pose.bones[upper].head.copy();target=Vector(target);la=arm.data.bones[upper].length;lb=arm.data.bones[lower].length
 delta=target-a;d=max(.0001,min(delta.length,la+lb-.00001));axis=delta.normalized();bend=Vector(pole);bend-=axis*bend.dot(axis)
 if bend.length<.001:bend=Vector((0,-1,0))
 bend.normalize();along=(la*la-lb*lb+d*d)/(2*d);elbow=a+axis*along+bend*math.sqrt(max(0,la*la-along*along))
 aim(arm,upper,elbow-a);aim(arm,lower,target-elbow)

def cycle(t,stride,duty,lift):
 if t<duty:return -stride*.5+stride*t/duty,0
 u=(t-duty)/(1-duty);return stride*.5-stride*smooth(u),lift*math.sin(math.pi*u)

def pose(arm,p,name,t):
 for pb in arm.pose.bones:pb.rotation_mode='QUATERNION';pb.rotation_quaternion=Quaternion();pb.location=(0,0,0);pb.scale=(1,1,1)
 s=math.sin(math.tau*t);c=math.cos(math.tau*t);small=p['kind']=='hare';h=p['height'];offset=0;roll=0;rest=False
 targets={}
 for side in ('Left','Right'):
  for limb in ('Front','Hind'):
   prefix=side+limb;targets[prefix]=arm.data.bones[prefix+'Foot'].head_local.copy()
 local(arm,'NeckUpper',z=1.4*s);local(arm,'Tail1',x=2*s,z=5*s);local(arm,'Tail2',z=-7*s)
 if name in ('walk','run','flee'):
  running=name!='walk';duty=.37 if running else .65
  stride=(.43 if p['kind']=='deer' else .33 if p['kind'] in ('boar','bear') else .32 if p['species']=='grey_wolf' else .15 if small else .19)* (1.7 if running else 1)
  lift=(.13 if running else .06)*h
  offsets={'LeftFront':0,'RightHind':.25,'RightFront':.5,'LeftHind':.75}
  if running:
   offsets={'LeftFront':0,'RightFront':.12,'LeftHind':.52,'RightHind':.64}
   if p['kind']=='boar':offsets={'LeftFront':0,'RightHind':0,'RightFront':.5,'LeftHind':.5}
   if small:offsets={'LeftFront':0,'RightFront':.04,'LeftHind':.52,'RightHind':.57}
  offset=(.025 if running else .014)*h*(1-math.cos(math.tau*t*(1 if running else 2)))-(.030*h if running else 0)
  local(arm,'Spine',x=(5 if running else 1.5)*s);local(arm,'Chest',x=(-3 if running else -.8)*s)
  local(arm,'NeckLower',x=(5 if running else 2)*s);local(arm,'Head',x=-3*s)
  for prefix in targets:
   y,z=cycle((t+offsets[prefix])%1,stride,duty,lift);targets[prefix]+=Vector((0,y,z))
 elif name=='graze_loop':
  if p['kind'] in ('deer','goat'):
   lower,upper,head=110,25,-42;offset=-(.10 if p['kind']=='goat' else .15)*h
  elif p['kind']=='canid':
   lower,upper,head=(50 if p['species']=='fox' else 75),0,-10;offset=-(.03 if p['species']=='fox' else .10)*h
  else:lower,upper,head=28,0,-3;offset=-.08*h
  local(arm,'NeckLower',x=lower+1.3*s);local(arm,'NeckUpper',x=upper);local(arm,'Head',x=head,z=3*s)
  local(arm,'Jaw',x=5*(.5+.5*math.sin(6*math.pi*t)))
 elif name=='alert':
  q=pulse(t);local(arm,'NeckLower',x=-12*q);local(arm,'Head',z=12*q)
  local(arm,'LeftEar',x=-22*q);local(arm,'RightEar',x=-22*q)
 elif name=='attack':
  q=pulse(t);offset=0;local(arm,'Spine',x=10*q)
  local(arm,'NeckLower',x=18*q if p['kind']=='boar' else -12*q);local(arm,'Head',x=-16*q if p['kind']=='boar' else 8*q)
  local(arm,'Jaw',x=28*q if p['kind']=='canid' else 4*q)
  for prefix in targets:
   if prefix.endswith('Front'):targets[prefix]+=Vector((0,-.12*q,.08*h*q))
 elif name=='hit':
  q=pulse(t);local(arm,'Pelvis',y=8*q);local(arm,'NeckLower',x=-14*q,z=9*q);offset=-.025*h*q
 elif name in ('death','sleep_loop'):
  rest=True;q=smooth(min(t/.63,1)) if name=='death' else 1
  roll=86*q;offset=-h*.60*q
  local(arm,'Pelvis',y=roll);local(arm,'Spine',z=-9*q);local(arm,'NeckLower',x=13*q);local(arm,'NeckUpper',x=18*q);local(arm,'Head',z=12*q)
  local(arm,'Jaw',x=5*q)
  for side in ('Left','Right'):
   for limb in ('Front','Hind'):
    local(arm,side+limb+'Upper',x=((-65 if limb=='Front' else 62) if name=='sleep_loop' else (-27 if limb=='Front' else 32))*q)
    local(arm,side+limb+'Lower',x=((110 if limb=='Front' else -112) if name=='sleep_loop' else (58 if limb=='Front' else -66))*q)
    local(arm,side+limb+'Metatarsal',x=((-55 if limb=='Front' else 58) if name=='sleep_loop' else (-35 if limb=='Front' else 39))*q)
  if name=='sleep_loop':local(arm,'Chest',x=.7*s);local(arm,'LeftEar',z=1.4*s)
 if not rest:
  local(arm,'LeftEar',z=3*s);local(arm,'RightEar',z=-2*s)
 pb=arm.pose.bones['Pelvis'];pb.location=pb.bone.matrix_local.to_3x3().inverted()@Vector((0,0,offset));bpy.context.view_layer.update()
 if not rest:
  for prefix,sole in targets.items():
   rb=arm.data.bones[prefix+'Metatarsal'];ankle=sole+(rb.head_local-rb.tail_local)
   joint(arm,prefix+'Upper',prefix+'Lower',ankle,(0,1 if 'Front' in prefix else -1,.05))
   aim(arm,prefix+'Metatarsal',sole-arm.pose.bones[prefix+'Metatarsal'].head);aim(arm,prefix+'Foot',(0,-1,0))
 bpy.context.view_layer.update()
 if name=='sleep_loop':
  turn=Quaternion((0,1,0),math.radians(roll))
  for side in ('Left','Right'):
   for limb in ('Front','Hind'):
    fore=limb=='Front';prefix=side+limb
    aim(arm,prefix+'Upper',turn@Vector((0,-1 if fore else 1,-.25)))
    aim(arm,prefix+'Lower',turn@Vector((0,1 if fore else -1,.48)))
    aim(arm,prefix+'Metatarsal',turn@Vector((0,-.25,-1)))
    aim(arm,prefix+'Foot',turn@Vector((0,-1,0)))
 if rest:
  # Ground the actual skinned side silhouette, including antlers and folded limbs.
  deps=bpy.context.evaluated_depsgraph_get();lowest=100
  for ob in arm.children:
   if ob.type=='MESH':
    evaluated=ob.evaluated_get(deps);mesh=evaluated.to_mesh();lowest=min(lowest,min((evaluated.matrix_world@v.co).z for v in mesh.vertices));evaluated.to_mesh_clear()
  pb=arm.pose.bones['Pelvis'];pb.location+=pb.bone.matrix_local.to_3x3().inverted()@Vector((0,0,-lowest));bpy.context.view_layer.update()


def bake(arm,p):
 bpy.context.scene.render.fps=30;dur=dict(DURATIONS)
 if p['species'] in ('wild_boar','grey_wolf'):dur['attack']=1.1
 for name,duration in dur.items():
  n=round(duration*30);act=bpy.data.actions.new(name);act['fps']=30;act['loop']=name in LOOPS;act['in_place']=True
  arm.animation_data_create();arm.animation_data.action=act
  for frame in range(n+1):
   bpy.context.scene.frame_set(frame+1);pose(arm,p,name,frame/n)
   for pb in arm.pose.bones:
    pb.keyframe_insert(data_path='rotation_quaternion',frame=frame+1,group=pb.name)
    if pb.name=='Pelvis':pb.keyframe_insert(data_path='location',frame=frame+1,group=pb.name)
  act.use_fake_user=True;track=arm.animation_data.nla_tracks.new();track.name=name;strip=track.strips.new(name,1,act);strip.action_frame_end=n+1;track.mute=True;arm.animation_data.action=None
 for pb in arm.pose.bones:pb.matrix_basis=Matrix.Identity(4)
