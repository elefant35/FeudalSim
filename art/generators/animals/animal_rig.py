"""28-bone deform-only species rigs, separate from the shared human skeleton."""
import bpy
from mathutils import Vector

def create(p):
 center=p['height']-.20 if p['kind'] in ('deer','goat') else p['height']-.14
 bodylen=p['length'];hind=p['hind'];front=p['front'];head=Vector(p['head']);neck=Vector(p['neck'])
 bone={'Root':(None,(0,0,0),(0,0,.10)),
 'Pelvis':('Root',(0,hind[0],center),(0,.20,center)),
 'Spine':('Pelvis',(0,.20,center),(0,-.12,center)),
 'Chest':('Spine',(0,-.12,center),(0,front[0],center+.025)),
 'NeckLower':('Chest',tuple(neck),tuple(neck.lerp(head,.5))),
 'NeckUpper':('NeckLower',tuple(neck.lerp(head,.5)),tuple(head)),
 'Head':('NeckUpper',tuple(head),tuple(head+Vector((0,-.18,0)))),
 'Jaw':('Head',tuple(head+Vector((0,-.025,-.07))),tuple(Vector(p['muzzle'])+Vector((0,0,-.025))))}
 for side,s in [('Left',1),('Right',-1)]:
  e=head+Vector((s*.075,.018,.085));bone[side+'Ear']=('Head',tuple(e),tuple(e+Vector((s*.065,.025,p['ear']))))
  for limb,landmarks in [('Front',front),('Hind',hind)]:
   x=p['width']*(.66 if limb=='Front' else .73)*s
   pts=[(x,landmarks[i],landmarks[i+1]) for i in range(0,8,2)]
   pts.append((x,pts[-1][1]-(.07 if p['kind']!='hare' else .085),pts[-1][2]))
   names=[side+limb+n for n in ['Upper','Lower','Metatarsal','Foot']]
   for j,name in enumerate(names):
    parent=('Chest' if limb=='Front' else 'Pelvis') if j==0 else names[j-1]
    bone[name]=(parent,pts[j],pts[j+1])
 tail=(0,bodylen*.44,center+.055);end=(0,tail[1]+p['tail'],tail[2]-.08)
 if p['kind']=='goat':end=(0,tail[1]+p['tail']*.75,tail[2]+.11)
 if p['kind']=='canid':end=(0,tail[1]+p['tail']*.82,tail[2]-p['tail']*.45)
 mid=Vector(tail).lerp(Vector(end),.5);bone['Tail1']=('Pelvis',tail,tuple(mid));bone['Tail2']=('Tail1',tuple(mid),end)
 bpy.ops.object.armature_add();arm=bpy.context.object;arm.name='AnimalRig';bpy.ops.object.mode_set(mode='EDIT')
 for b in list(arm.data.edit_bones):arm.data.edit_bones.remove(b)
 for name,(parent,h,t) in bone.items():
  b=arm.data.edit_bones.new(name);b.head=h;b.tail=t
  if parent:b.parent=arm.data.edit_bones[parent]
  b.use_deform=name!='Root';b.use_connect=False
 bpy.ops.object.mode_set(mode='OBJECT');arm.location=(0,0,0);arm['species']=p['species'];arm['root_motion']='Root remains static; all clips in place'
 return arm
