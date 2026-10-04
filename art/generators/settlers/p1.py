"""P1 character library extension: preserve P0 names, six heads/sex, four outer layers,
and a 1.235 m child on the shared humanoid hierarchy with child proportions.
Run through export.py with {'sex':'male'|'female'|'child'}.
"""
import pathlib,sys,math
import bpy
from mathutils import Vector
sys.path.insert(0,str(pathlib.Path(__file__).parent))
import settlers
from settlers import Mesh,fsart


def extra_head(rig,sex,index,scale):
 ob=settlers.head(rig,sex,0,scale);ob.name=ob.data.name='Head_'+sex+'_'+chr(97+index)
 # Change every target and basis consistently, preserving relative facial expressions.
 for key in ob.data.shape_keys.key_blocks:
  for point in key.data:
   x,y,z=point.co/scale
   if index==4:
    x*=1.09 if z>1.57 else 1.02
    if y<-.122 and 1.60<z<1.64:y-=.016
    if 1.65<z<1.67:z-=.002
   else:
    x*=.89 if z<1.6 else .97
    if y<-.12 and 1.60<z<1.64:y+=.015
    if 1.49<z<1.56:z-=.007
    if 1.62<z<1.65 and y<-.085:y-=.003
   point.co=Vector((x,y,z))*scale
 ob['variant']='broad angular brow / long nose' if index==4 else 'narrow jaw / short upturned nose'
 return ob


def outer_layers(rig,sex,scale):
 female=sex=='female';result=[]
 vest=Mesh('Cloth_hide_jerkin',rig,scale)
 rows=[(0,0,.96,.201,.146),(0,0,1.08,.181,.132),(0,0,1.27,.218,.142),(0,0,1.409,.229,.137),(0,0,1.448,.106,.091)]
 vest.rings(rows,n=12,color='wool_brown',bone=lambda j,i:'Spine' if j<2 else 'UpperChest',caps=False)
 # Ties and cut leather facings, never bright modern buckles.
 for j in range(4):
  z=1.06+j*.078;y=-.145
  vest.patch([(-.029,y,z),(.029,y,z+.008),(.029,y-.003,z+.017),(-.029,y-.003,z+.009)],'linen','Spine' if j<2 else 'UpperChest')
 vest.patch([(-.033,-.15,1.38),(-.039,-.146,.97),(-.055,-.144,.97),(-.049,-.15,1.38)],'russet','Spine')
 vest.patch([(.033,-.15,1.38),(.039,-.146,.97),(.055,-.144,.97),(.049,-.15,1.38)],'russet','Spine')
 result.append(vest.object())
 for name,col,hem in [('fur_cloak','wool_grey',.63),('oiled_cloak','wool_brown',.45)]:
  m=Mesh('Cloth_'+name,rig,scale);n=14;ids=[]
  for j,(z,rx,ry) in enumerate([(1.435,.19,.115),(1.31,.252,.16),(1.09,.296,.193),(.84,.34,.21),(hem,.373,.218)]):
   row=[]
   for i in range(n):
    a=.51+i*(math.tau-1.02)/(n-1);z1=z
    if j==4 and name=='fur_cloak':z1-=.018 if i%2 else 0
    row.append(m.vert((math.sin(a)*rx,-math.cos(a)*ry+.03,z1),'UpperChest' if j<2 else {'Hips':.8,'Spine':.2},j/4))
   ids.append(row)
  for j in range(4):
   for i in range(n-1):m.face((ids[j][i],ids[j][i+1],ids[j+1][i+1],ids[j+1][i]),col)
  if name=='fur_cloak':
   for i in range(n-1):
    a=.51+(i+.5)*(math.tau-1.02)/(n-1)
    # Broad layered ragged fur edges, rather than polygon noise over the whole skin.
    tip=m.vert((math.sin(a)*.24,-math.cos(a)*.149+.03,1.30-(i%3)*.008),'UpperChest',.12)
    m.face((ids[0][i],ids[0][i+1],tip),'wool_grey' if i%3 else 'wool_brown')
    a=.51+(i+.5)*(math.tau-1.02)/(n-1)
    tip=m.vert((math.sin(a)*.382,-math.cos(a)*.225+.03,hem-.055),'Hips',1)
    m.face((ids[-1][i],tip,ids[-1][i+1]),'wool_grey')
  else:
   # Broad overlapped weather flap from the right shoulder; a sewn repair at the hem.
   m.patch([(-.12,-.09,1.40),(-.22,-.095,1.32),(-.25,-.09,1.11),(-.08,-.1,1.18)],'wool_brown','UpperChest')
   m.patch([(.26,.01,.65),(.30,.01,.66),(.34,.01,.51),(.29,.01,.49)],'wool_grey','Hips')
  result.append(m.object())
 poncho=Mesh('Cloth_sailcloth_poncho',rig,scale)
 # Front and back panels retain their neck opening; irregular folded hems hang freely.
 for sy in [-1,1]:
  ids=[]
  for j,(z,w,y) in enumerate([(1.437,.27,.11),(1.31,.43,.15),(1.03,.44,.20),(.67,.42,.23)]):
   row=[]
   for i in range(7):
    x=(i-3)*w/3;zz=z+(.035 if j==0 and i in [2,4] else 0)-(.045 if j==3 and i%3==1 else 0)
    row.append(poncho.vert((x,sy*y,zz),'UpperChest' if j<2 else {'Spine':.4,'Hips':.6},j/3))
   ids.append(row)
  for j in range(3):
   for i in range(6):
    if j==0 and i in [2,3]:continue
    poncho.face((ids[j][i],ids[j][i+1],ids[j+1][i+1],ids[j+1][i]),'linen' if (i+j)%5 else 'wool_grey')
  for sx in [-1,1]:
   poncho.patch([(sx*.27,-.11,1.437),(sx*.27,.11,1.437),(sx*.09,.08,1.471),(sx*.09,-.08,1.471)],'linen','UpperChest')
 # Hand-sewn sail seam: a narrow muted welt, on the anchored lower front panel.
 poncho.patch([(-.014,-.203,1.01),(.014,-.203,1.01),(.014,-.233,.69),(-.014,-.233,.69)],'wool_grey','Hips')
 result.append(poncho.object())
 return result


def child_point(p):
 x,y,z=p;anchors=[(0,0),(.48,.30),(.89,.56),(1.02,.67),(1.41,.97),(1.51,1.04),(1.72,1.235)]
 for (za,aa),(zb,bb) in zip(anchors,anchors[1:]):
  if za<=z<=zb:zz=aa+(z-za)/(zb-za)*(bb-aa);break
 else:zz=z*.72 if z<0 else 1.235+(z-1.72)*.8
 # Larger cranium and broad child hands; shorter lateral limb reach.
 if z>1.49:k=.805
 else:k=.70-.075*max(0,min(1,(abs(x)-.17)/.33))
 return Vector((x*k,y*(.805 if z>1.49 else .70),zz))


def generate(params):
 sex=params.get('sex','male');child=sex=='child';base='male' if child else sex
 scale=1 if base=='male' else 1.62/1.72
 objs=settlers.generate({'sex':base});rig=objs[0]
 objs.extend(extra_head(rig,base,i,scale) for i in [4,5])
 objs.extend(outer_layers(rig,base,scale))
 if child:
  old=next(o for o in objs if o.name=='Head_male_d');objs.remove(old);bpy.data.objects.remove(old,do_unlink=True)
  young=settlers.head(rig,'male',1,1);young.name=young.data.name='Head_male_d';objs.append(young)
  # Same names/hierarchy and rotation profile; only rest lengths and mesh proportions change.
  objs=[o for o in objs if not o.name.startswith('Beard_')]
  for ob in list(bpy.context.scene.objects):
   if ob.name.startswith('Beard_'):bpy.data.objects.remove(ob,do_unlink=True)
  bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
  for b in rig.data.edit_bones:b.head=child_point(b.head);b.tail=child_point(b.tail)
  bpy.ops.object.mode_set(mode='OBJECT')
  for ob in objs:
   if ob.type!='MESH':continue
   if ob.data.shape_keys:
    for key in ob.data.shape_keys.key_blocks:
     for point in key.data:point.co=child_point(point.co)
   else:
    for point in ob.data.vertices:point.co=child_point(point.co)
   if ob.name=='Body_Male':ob.name=ob.data.name='Body_Child'
   elif ob.name.startswith('Head_male_'):ob.name=ob.data.name=ob.name.replace('Head_male_','Head_child_')
  rig['height_scale']=.72;rig['body_proportions']='child: larger cranium, shorter arms and legs; same shared humanoid hierarchy'
 return objs
