"""M2 common human rig. Metres, Z up, -Y forward, A pose.
create_skeleton(scale=1.0) -> armature; BONES maps names to (parent, head, tail).
The contract's explicit list has 38 bones and two sockets (40 total), not 37.
We follow its complete hierarchy and stay below the 45-bone cap.
"""
import bpy
from mathutils import Vector
BONES = {
 'Root': (None,(0,0,0),(0,0,.12)),
 'Hips': ('Root',(0,0,.86),(0,0,1.02)),
 'Spine': ('Hips',(0,0,1.02),(0,0,1.16)),
 'Chest': ('Spine',(0,0,1.16),(0,0,1.30)),
 'UpperChest': ('Chest',(0,0,1.30),(0,0,1.43)),
 'Neck': ('UpperChest',(0,0,1.43),(0,0,1.51)),
 'Head': ('Neck',(0,0,1.51),(0,0,1.68)),
 'Jaw': ('Head',(0,-.055,1.565),(0,-.095,1.535)),
 'LeftEye': ('Head',(.038,-.088,1.634),(.038,-.118,1.634)),
 'RightEye': ('Head',(-.038,-.088,1.634),(-.038,-.118,1.634)),
}
for side,s in [('Left',1),('Right',-1)]:
 def add(name,parent,a,b): BONES[side+name]=(parent if parent in BONES else side+parent,tuple(x*s if i==0 else x for i,x in enumerate(a)),tuple(x*s if i==0 else x for i,x in enumerate(b)))
 add('Shoulder','UpperChest',(.055,0,1.41),(.17,0,1.41))
 add('UpperArm','Shoulder',(.17,0,1.41),(.365,0,1.205))
 add('LowerArm','UpperArm',(.365,0,1.205),(.535,-.005,1.015))
 add('Hand','LowerArm',(.535,-.005,1.015),(.603,-.005,.945))
 add('ThumbMetacarpal','Hand',(.554,-.020,.994),(.575,-.053,.970))
 add('ThumbProximal','ThumbMetacarpal',(.575,-.053,.970),(.603,-.064,.943))
 add('IndexProximal','Hand',(.596,-.028,.950),(.630,-.028,.916))
 add('IndexIntermediate','IndexProximal',(.630,-.028,.916),(.652,-.028,.894))
 add('MiddleProximal','Hand',(.602,.001,.946),(.640,.001,.908))
 add('MiddleIntermediate','MiddleProximal',(.640,.001,.908),(.666,.001,.882))
 add('UpperLeg','Hips',(.105,0,.89),(.11,-.015,.48))
 add('LowerLeg','UpperLeg',(.11,-.015,.48),(.11,0,.085))
 add('Foot','LowerLeg',(.11,0,.085),(.11,-.115,.05))
 add('Toes','Foot',(.11,-.115,.05),(.11,-.21,.05))
 add('HandProp','Hand',(.58,-.015,.973),(.625,-.015,.928))

def create_skeleton(scale=1.0):
 arm=bpy.data.armatures.new('HumanSkeleton')
 obj=bpy.data.objects.new('HumanSkeleton',arm)
 bpy.context.collection.objects.link(obj)
 bpy.context.view_layer.objects.active=obj
 obj.select_set(True)
 bpy.ops.object.mode_set(mode='EDIT')
 for name,(parent,head,tail) in BONES.items():
  b=arm.edit_bones.new(name)
  b.head=Vector(head)*scale; b.tail=Vector(tail)*scale
  b.use_deform=name!='Root' and not name.endswith('Prop')
  if parent: b.parent=arm.edit_bones[parent]
  b.use_connect=False
 bpy.ops.object.mode_set(mode='OBJECT')
 obj['contract']='M2 art brief 6.1: complete 38-bone explicit hierarchy plus 2 sockets'
 obj['height_scale']=scale
 return obj
