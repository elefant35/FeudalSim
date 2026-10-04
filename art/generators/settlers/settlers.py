"""Reproducible P0 modular Varrow settlers, original FeudalSim geometry.
Run through tools/art/export.py with {'sex':'male'|'female'}.
All alternatives export for runtime slot selection; assembled triangle counts are
recorded separately from the complete wardrobe library.
"""
import sys, pathlib, math
import bpy
from mathutils import Vector
sys.path.insert(0,str(pathlib.Path(__file__).parent))
from skeleton import create_skeleton, BONES
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[3]/'tools/art'))
import fsart

class Mesh:
 def __init__(self,name,rig,scale=1):
  self.name=name; self.rig=rig; self.scale=scale
  self.v=[]; self.f=[]; self.c=[]; self.w=[]; self.wind=[]; self.tags=[]
 def vert(self,p,bone,wind=0,tag=''):
  self.v.append(tuple(q*self.scale for q in p)); self.w.append(bone); self.wind.append(wind); self.tags.append(tag); return len(self.v)-1
 def face(self,vs,color): self.f.append(tuple(vs)); self.c.append(color)
 def rings(self,rows,n=12,color='skin_2',bone='Hips',caps=True,tag='',wind=None):
  ids=[]
  for j,row in enumerate(rows):
   x,y,z,rx,ry=row; ring=[]
   for i in range(n):
    a=2*math.pi*i/n
    wi=wind(j,i) if wind else 0
    bn=bone(j,i) if callable(bone) else bone
    ring.append(self.vert((x+math.sin(a)*rx,y-math.cos(a)*ry,z),bn,wi,tag))
   ids.append(ring)
  for j in range(len(ids)-1):
   for i in range(n):
    cc=color(j,i) if callable(color) else color
    self.face((ids[j][i],ids[j][(i+1)%n],ids[j+1][(i+1)%n],ids[j+1][i]),cc)
  if caps:
   self.face(tuple(reversed(ids[0])),color(0,0) if callable(color) else color)
   self.face(ids[-1],color(len(ids)-2,0) if callable(color) else color)
  return ids
 def tube(self,points,radii,bone,color,n=8,tag='',caps=True,wind=None):
  ids=[]
  for j,p in enumerate(points):
   direction=Vector(points[min(j+1,len(points)-1)])-Vector(points[max(0,j-1)])
   d=direction.normalized(); reference=Vector((0,1,0)) if abs(d.y)<.95 else Vector((0,0,1)); t=d.cross(reference).normalized(); u=d.cross(t).normalized()
   r=radii[j]; rx,ry=r if isinstance(r,tuple) else (r,r)
   ids.append([self.vert(Vector(p)+t*(math.cos(i*2*math.pi/n)*rx)+u*(math.sin(i*2*math.pi/n)*ry),bone(j) if callable(bone) else bone,wind(j) if wind else 0,tag) for i in range(n)])
  for j in range(len(ids)-1):
   for i in range(n): self.face((ids[j][i],ids[j][(i+1)%n],ids[j+1][(i+1)%n],ids[j+1][i]),color(j,i) if callable(color) else color)
  if caps:
   self.face(tuple(reversed(ids[0])),color(0,0) if callable(color) else color)
   self.face(ids[-1],color(len(ids)-2,0) if callable(color) else color)
  return ids
 def patch(self,points,color,bone,tag=''):
  self.face([self.vert(p,bone,0,tag) for p in points],color)
 def object(self):
  mesh=bpy.data.meshes.new(self.name); mesh.from_pydata(self.v,[],self.f); mesh.update()
  ob=bpy.data.objects.new(self.name,mesh); bpy.context.collection.objects.link(ob)
  mesh.materials.append(fsart.palette_material()); mesh.uv_layers.new(name='UVMap')
  for poly,col in zip(mesh.polygons,self.c):
   for li in poly.loop_indices: mesh.uv_layers.active.data[li].uv=fsart.SWATCH[col]
  # Clamp exported cloth anchor-to-hem wind in the red channel, never multiply palette colour.
  if self.name.startswith(('Cloth_','Headwear_')):
   attr=mesh.color_attributes.new(name='WindWeight',type='FLOAT_COLOR',domain='POINT')
   for i,w in enumerate(self.wind): attr.data[i].color=(w,0,0,1)
  groups={}
  for i,b in enumerate(self.w):
   weights=b if isinstance(b,dict) else {b:1}
   for name,weight in weights.items():
    if name not in groups: groups[name]=ob.vertex_groups.new(name=name)
    groups[name].add([i],weight,'REPLACE')
  mod=ob.modifiers.new('HumanSkin','ARMATURE'); mod.object=self.rig
  ob.parent=self.rig
  ob['slot']=self.name.split('_')[0]
  return ob

def shoulder_arm(m,side,color,radius=.064,detail=5):
 s=1 if side=='Left' else -1
 p=[(.155*s,0,1.40),(.22*s,0,1.355),(.30*s,0,1.28),(.365*s,0,1.205),(.45*s,-.002,1.11),(.535*s,-.005,1.015)]
 weights=[{side+'UpperArm':.85,'UpperChest':.15},side+'UpperArm',side+'UpperArm',{side+'UpperArm':.5,side+'LowerArm':.5},side+'LowerArm',side+'LowerArm']
 m.tube(p,[radius*1.35,radius*1.2,radius,radius*.9,radius*.82,radius*.67],lambda j:weights[j],color,n=10)

def hands(m,side):
 s=1 if side=='Left' else -1
 palm=[(.535*s,-.005,1.015),(.56*s,-.005,.99),(.60*s,-.005,.95)]
 m.tube(palm,[(.030,.025),(.043,.028),(.037,.023)],side+'Hand','skin_2',n=8)
 for k,(yy,length) in enumerate([(-.029,.073),(-.009,.080),(.012,.072),(.032,.056)]):
  start=Vector((.596*s,yy,.952))
  end=start+Vector((length*s*.707,0,-length*.707))
  mid=start.lerp(end,.58)
  name='Index' if k==0 else 'Middle'
  m.tube([start,mid,end],[.010,.009,.006],lambda j:side+name+('Proximal' if j==0 else 'Intermediate'),'skin_2',n=6,tag='finger')
 m.tube([(.554*s,-.020,.994),(.575*s,-.053,.970),(.603*s,-.064,.943)],[.013,.011,.008],lambda j:side+'Thumb'+('Metacarpal' if j==0 else 'Proximal'),'skin_2',n=6,tag='thumb')

def body(rig,sex,scale):
 m=Mesh('Body_'+sex.title(),rig,scale); female=sex=='female'
 m.rings([(0,0,.82,.15,.090),(0,0,.92,.157,.098),(0,0,1.02,.13,.090),(0,0,1.12,.125 if female else .145,.085),(0,0,1.24,.174 if female else .18,.105),(0,0,1.35,.18,.10),(0,0,1.425,.115,.065)],n=12,bone=lambda j,i:['Hips','Hips','Spine','Chest','Chest','UpperChest'][min(j,5)],color='skin_2')
 for side,s in [('Left',1),('Right',-1)]:
  shoulder_arm(m,side,'skin_2'); hands(m,side)
  m.rings([(.10*s,0,.88,.087,.088),(.105*s,0,.75,.086,.087),(.11*s,-.01,.60,.069,.07),(.11*s,-.015,.48,.062,.057),(.11*s,0,.30,.060,.057),(.11*s,0,.13,.041,.042),(.11*s,0,.065,.04,.045)],n=10,bone=lambda j,i:side+('UpperLeg' if j<3 else 'LowerLeg'),color='skin_2')
  m.rings([(.11*s,-.060,.017,.051,.145),(.11*s,-.060,.045,.059,.154),(.11*s,-.025,.085,.047,.10)],n=10,bone=side+'Foot',color='skin_2')
 return m.object()

def head(rig,sex,index,scale):
 m=Mesh('Head_'+sex+'_'+chr(97+index),rig,scale)
 profiles=[(.098,.096,.087),(.107,.098,.100),(.092,.102,.083),(.103,.102,.097)]
 rx,ry,jaw=profiles[index]
 if sex=='female': rx*=.93; jaw*=.88
 skin=['skin_2','skin_1','skin_3','skin_2'][index]
 m.rings([(0,0,1.425,.051,.052),(0,0,1.49,.052,.052)],n=12,bone='Neck',color=skin)
 zs=[1.496,1.52,1.558,1.59,1.623,1.652,1.682,1.706,1.72]
 rxs=[.041,jaw*.75,jaw*.94,rx,rx*1.03,rx,rx*.92,rx*.63,.008]
 rys=[.043,ry*.69,ry*.82,ry*.98,ry,ry*1.03,ry*.95,ry*.7,.01]
 m.rings([(0,.004,z,a,b) for z,a,b in zip(zs,rxs,rys)],n=14,bone='Head',color=lambda j,i:skin,tag='face')
 # Angular bridge, nostrils and alae give each head a legible profile.
 nose_depth=[.143,.135,.148,.142][index]; nz=1.611; nw=[.019,.021,.016,.023][index]
 p=[(-.009,-.093,1.659),(.009,-.093,1.659),(-nw,-.105,nz), (nw,-.105,nz),(-.013,-nose_depth,nz+.007),(.013,-nose_depth,nz+.007),(0,-nose_depth-.008,nz+.017)]
 ids=[m.vert(v,'Head',tag='nose') for v in p]
 for face in [(0,1,6),(1,3,5,6),(0,6,4,2),(4,6,5),(2,4,5,3),(0,2,3,1)]: m.face([ids[i] for i in face],skin)
 for s in [-1,1]:
  # Ears: broad outer shell with darker centre. Distinctively human silhouette.
  m.rings([(s*(rx+.004),.013,1.585,.014,.023),(s*(rx+.009),.011,1.626,.016,.024),(s*(rx+.003),.012,1.651,.011,.021)],n=6,bone='Head',color=skin,tag='ear')
  m.patch([(s*(rx+.019),-.011,1.600),(s*(rx+.022),-.012,1.632),(s*(rx+.012),-.016,1.630),(s*(rx+.010),-.016,1.603)],'skin_3','Head')
  ex=s*.039; y=-ry-.003; ez=1.637
  # Recessed almond eyes, dark upper lids and small irises; no oversized doll eyes.
  m.patch([(ex-.020,y,ez),(ex-.009,y-.003,ez+.006),(ex+.011,y-.003,ez+.006),(ex+.020,y,ez),(ex+.008,y-.004,ez-.004),(ex-.010,y-.004,ez-.004)],'linen','Head',tag='eye')
  m.patch([(ex-.004,y-.006,ez-.004),(ex-.004,y-.006,ez+.005),(ex+.004,y-.006,ez+.005),(ex+.004,y-.006,ez-.004)],'hair_dark','Head',tag='eye')
  m.patch([(ex-.021,y-.006,ez+.002),(ex-.011,y-.007,ez+.008),(ex+.010,y-.007,ez+.008),(ex+.020,y-.006,ez+.002),(ex+.009,y-.007,ez+.005),(ex-.009,y-.007,ez+.005)],'skin_3','Head',tag='lid')
  browz=1.661+(0.003 if index==3 else 0)
  m.patch([(ex-.023,-ry+.003,browz-.004),(ex-.013,-ry-.007,browz+.004),(ex+.010,-ry-.006,browz+.003),(ex+.020,-ry+.002,browz-.003)],'hair_brown','Head',tag='brow')
  if index==3:
   ex=s*.039; ey=-ry-.009
   m.patch([(ex-.022,ey,1.625),(ex+.020,ey,1.624),(ex+.018,ey-.001,1.621),(ex-.020,ey-.001,1.622)],'skin_3','Head',tag='wrinkle')
   m.patch([(s*.023,-ry-.009,1.602),(s*.034,-ry-.008,1.587),(s*.038,-ry-.007,1.583),(s*.027,-ry-.010,1.600)],'skin_3','Head',tag='wrinkle')
   for z in [1.672,1.680]: m.patch([(s*.008,-ry*.96,z),(s*.062,-ry*.80,z-.001),(s*.060,-ry*.80,z-.002),(s*.01,-ry*.96,z-.001)],'skin_3','Head',tag='wrinkle')
 # Quiet mouth with a shallow upper lip, lower lip and dark opening.
 my=-ry*.98-.012; mz=1.58
 m.patch([(-.030,my,mz),(-.012,my-.004,mz+.005),(0,my-.005,mz+.003),(.012,my-.004,mz+.005),(.030,my,mz),(0,my-.008,mz-.002)],'skin_3','Head',tag='mouth')
 m.patch([(-.029,my-.001,mz),(.029,my-.001,mz),( .020,my-.003,mz-.002),(-.020,my-.003,mz-.002)],'hair_dark','Head',tag='mouth_gap')
 m.patch([(-.025,my,mz-.003),(0,my-.006,mz-.007),(.025,my,mz-.003),(0,my-.005,mz-.002)],skin,'Head',tag='mouth')
 ob=m.object(); basis=ob.shape_key_add(name='Basis')
 for name in ['blink','joy','anger','fear','grief','shame','jealousy','mouth_open','mouth_wide','mouth_round']:
  key=ob.shape_key_add(name=name,from_mix=False); key.value=0.0
  for i,(v,tag) in enumerate(zip(m.v,m.tags)):
   co=key.data[i].co; x,y,z=v; q=scale
   if name=='blink' and tag in ['eye','lid']: co.z=1.637*q+(z-1.637*q)*.12
   if tag in ['mouth','mouth_gap']:
    if name=='joy': co.z+=abs(x)*.18; co.x*=1.12
    elif name=='anger': co.z-=abs(x)*.15; co.x*=.93
    elif name=='fear': co.z-=.006*q
    elif name=='grief': co.z-=abs(x)*.23
    elif name=='shame': co.x*=.82
    elif name=='mouth_wide': co.x*=1.45
    elif name=='mouth_round': co.x*=.62; co.z=1.58*q+(z-1.58*q)*2.8
    elif name=='mouth_open': co.z=1.58*q+(z-1.58*q)*3.5-.005*q
   if tag=='brow':
    if name=='anger': co.z-=.010*q*(1-min(abs(x)/(.06*q),1))
    if name in ['fear','grief']: co.z+=.009*q*(1-min(abs(x)/(.08*q),1))
    if name=='jealousy' and x>0: co.z+=.006*q
    if name=='shame': co.z-=.004*q
   if name=='mouth_open' and tag=='face' and z<1.59*q and z>1.495*q: co.z-=.011*q
 ob['age']='elder' if index==3 else 'adult'; return ob

def hair(rig,style,scale):
 names=['cropped','shoulder_length','braided','tied_back','balding_fringe','long_loose']
 color=['hair_dark','hair_brown','hair_fair','hair_brown','hair_dark','hair_fair'][style]
 m=Mesh('Hair_'+names[style],rig,scale)
 # Scalp hemisphere with a shaped hairline, never covering the eyes.
 n=16; ids=[]
 for j,(rx,ry,z) in enumerate([(.106,.108,1.668),(.105,.11,1.696),(.080,.089,1.716),(.028,.033,1.73)]):
  ring=[]
  for i in range(n):
   a=i*2*math.pi/n
   # Balding style leaves the entire crown open.
   ring.append(m.vert((math.sin(a)*rx,-math.cos(a)*ry+.010,z-(.026 if i<3 or i>13 else .052)*(j==0)),'Head'))
  ids.append(ring)
 for j in range(len(ids)-1):
  if style==4 and j>0: continue
  for i in range(n):
   if style==4 and (i<3 or i>13): continue
   m.face((ids[j][i],ids[j][(i+1)%n],ids[j+1][(i+1)%n],ids[j+1][i]),color)
 if style!=4: m.face(ids[-1],color)
 if style in [1,5]:
  length=1.41 if style==1 else 1.30
  for s in [-1,1]:
   m.tube([(s*.093,.014,1.68),(s*.107,.044,1.58),(s*.115,.055,1.47),(s*.105,.05,length)],[.025,.033,.035,.009],'Head',color,n=7)
  m.tube([(0,.085,1.68),(0,.11,1.56),(0,.12,1.45),(0,.12,length)],[.062,.075,.066,.033],'Head',color,n=8)
 if style==2:
  for s in [-1,1]:
   pts=[(s*.106+math.sin(j*2.8)*.012,.014,1.64-j*.036) for j in range(8)]
   m.tube(pts,[.022-.0015*j for j in range(8)],'Head',color,n=6)
 if style==3:
  m.tube([(0,.09,1.65),(0,.139,1.60),(0,.16,1.55),(0,.145,1.49)],[.040,.028,.026,.013],'Head',color,n=8)
 return m.object()

def beard(rig,style,scale):
 m=Mesh('Beard_'+['short','full','moustache_chin'][style],rig,scale)
 col=['hair_brown','hair_dark','hair_fair'][style]
 if style<2:
  m.rings([(0,-.025,1.594,.079,.089),(0,-.032,1.555,.080,.092),(0,-.038,1.522 if style==0 else 1.47,.048,.067)],n=12,bone='Jaw',color=col)
 else:
  m.tube([(-.03,-.12,1.592),(0,-.127,1.594),(.03,-.12,1.592)],[.008,.012,.008],'Head',col,n=6)
  m.tube([(0,-.09,1.56),(0,-.099,1.53),(0,-.087,1.50)],[.021,.025,.009],'Jaw',col,n=8)
 return m.object()

def clothing(rig,sex,scale):
 female=sex=='female'; result=[]
 def garment(name,rows,col,n=16,wind=False):
  m=Mesh('Cloth_'+name,rig,scale)
  def garment_weights(j,i):
   z=rows[j][2]; side='Left' if math.sin(i*math.tau/n)>=0 else 'Right'
   if name=='wool_tunic' and z<1.0:
    amount=min(.70,max(0,(1.0-z)/.60))
    leg=side+('LowerLeg' if female and z<.4 else 'UpperLeg')
    return {'Hips':1-amount,leg:amount}
   return 'Hips' if z<1.03 else 'Spine' if z<1.2 else 'UpperChest'
  m.rings(rows,n=n,color=col,bone=garment_weights,caps=False,wind=(lambda j,i: min(1,max(0,(1.22-rows[j][2])/(1.22-rows[0][2])))) if wind else None)
  return m
 shirt=garment('linen_shirt',[(0,0,.86,.16,.105),(0,0,1.03,.145,.10),(0,0,1.24,.188,.113),(0,0,1.38,.187,.102),(0,0,1.455,.069,.065)],'linen')
 for side in ['Left','Right']: shoulder_arm(shirt,side,'linen',radius=.068)
 result.append(shirt.object())
 hem=.17 if female else .60
 tunic=garment('wool_tunic',[(0,0,hem,.225 if female else .203,.16),(0,0,.78,.211,.15),(0,0,.96,.192,.134),(0,0,1.035,.157,.113),(0,0,1.14,.172,.119),(0,0,1.29,.201,.128),(0,0,1.39,.213,.12),(0,0,1.433,.093,.075)],'russet' if female else 'woad',wind=True)
 for side in ['Left','Right']:
  # Wool sleeves finish before linen cuffs; the hands remain clean separate skin.
  s=1 if side=='Left' else -1
  tunic.tube([(.168*s,0,1.40),(.25*s,0,1.32),(.365*s,0,1.205),(.465*s,-.002,1.095),(.505*s,-.005,1.05)],[.111,.098,.085,.074,.059],lambda j:[{'UpperChest':.5,side+'UpperArm':.5},{'UpperChest':.1,side+'UpperArm':.9},{side+'UpperArm':.5,side+'LowerArm':.5},side+'LowerArm',side+'LowerArm'][j],'russet' if female else 'woad',n=10)
 # Broad shoulder yokes bridge the rotating sleeve root under the collar.
 # They follow the chest predominantly; the underlying sleeve remains free to swing.
 for side,s in [('Left',1),('Right',-1)]:
  tunic.rings([(s*.17,0,1.35,.085,.078),(s*.17,0,1.415,.106,.096),(s*.17,0,1.454,.045,.052)],n=8,color='russet' if female else 'woad',bone={'UpperChest':.75,side+'UpperArm':.25})
 # Belt lies within the tunic object, with an unpolished tied end and a brass pin.
 tunic.rings([(0,0,1.012,.151,.111),(0,0,1.038,.153,.112)],n=16,color='wool_brown',bone='Spine',caps=False)
 tunic.patch([(-.026,-.115,1.037),(.011,-.119,1.031),(.017,-.123,.86),(-.004,-.121,.83)],'wool_brown','Hips')
 tunic.patch([(-.018,-.116,1.035),(.010,-.116,1.035),(.010,-.116,1.016),(-.018,-.116,1.016)],'varrow_gold','Spine')
 # Narrow woven homeland hem; accents occur on one garment only.
 tunic.rings([(0,0,hem+.024,.226 if female else .204,.162),(0,0,hem+.042,.223 if female else .202,.16)],n=16,color='varrow_blue',bone='Hips',caps=False)
 result.append(tunic.object())
 hose=Mesh('Cloth_wool_hose',rig,scale)
 for side,s in [('Left',1),('Right',-1)]:
  hose.rings([(.11*s,0,.09,.049,.05),(.11*s,0,.25,.067,.066),(.11*s,-.015,.48,.068,.068),(.105*s,0,.72,.094,.097),(.10*s,0,.90,.10,.10)],n=10,color='wool_grey',bone=lambda j,i:side+('LowerLeg' if j<2 else 'UpperLeg'),caps=False)
  for z in [.16,.23,.30,.37]: hose.rings([(.11*s,0,z,.069 if z>.24 else .060,.067 if z>.24 else .059),(.11*s,0,z+.014,.070 if z>.24 else .061,.068 if z>.24 else .06)],n=10,color='wool_brown',bone=side+'LowerLeg',caps=False)
 result.append(hose.object())
 shoes=Mesh('Cloth_turnshoes',rig,scale)
 for side,s in [('Left',1),('Right',-1)]:
  shoes.rings([(.11*s,-.060,.002,.061,.154),(.11*s,-.063,.035,.066,.161),(.11*s,-.058,.074,.062,.152),(.11*s,-.011,.127,.046,.055)],n=12,color='wool_brown',bone=side+'Foot')
  shoes.patch([(.08*s,-.062,.10),(.14*s,-.062,.10),(.14*s,-.038,.113),(.08*s,-.038,.113)],'linen',side+'Foot')
 result.append(shoes.object())
 cloak=Mesh('Cloth_wool_cloak',rig,scale)
 # Open front, unequal pinned shoulder, broad softly faceted back; perimeter hem.
 rings=[]; n=14
 for j,(z,rx,ry) in enumerate([(1.39,.205,.12),(1.29,.235,.15),(1.08,.28,.18),(.84,.32,.19),(.60,.35,.19)]):
  row=[]
  for i in range(n):
   a=.62+i*(2*math.pi-1.24)/(n-1)
   # Breathing room around the sleeves; front edge drawn to the right shoulder pin.
   row.append(cloak.vert((math.sin(a)*rx,-math.cos(a)*ry+.027,z+(.045 if j==0 and i==n-1 else 0)), 'UpperChest' if j<2 else {'Hips':.75,'Spine':.25},j/(len([1,2,3,4,5])-1)))
  rings.append(row)
 for j in range(4):
  for i in range(n-1): cloak.face((rings[j][i],rings[j][i+1],rings[j+1][i+1],rings[j+1][i]),'wool_brown')
 # Bronze pin just below the right clavicle.
 cloak.tube([(-.13,-.084,1.38),(-.13,-.106,1.38)],[.014,.014],'UpperChest','wool_brown',n=8)
 result.append(cloak.object())
 hood=Mesh('Headwear_wool_hood',rig,scale)
 # Open face hood: only rear and sides; face edges leave both eyes and nose free.
 rows=[]; n=12
 for j,(z,rx,ry) in enumerate([(1.48,.092,.104),(1.58,.121,.126),(1.69,.119,.123),(1.745,.064,.08)]):
  row=[]
  for i in range(n):
   a=.64+i*(2*math.pi-1.28)/(n-1)
   row.append(hood.vert((math.sin(a)*rx,-math.cos(a)*ry+.02,z),'Head',.08))
  rows.append(row)
 for j in range(3):
  for i in range(n-1): hood.face((rows[j][i],rows[j][i+1],rows[j+1][i+1],rows[j+1][i]),'wool_grey')
 hood.face(rows[-1],'wool_grey')
 result.append(hood.object())
 hood=Mesh('Cloth_wool_hood',rig,scale)
 hood.rings([(0,.012,1.48,.091,.095),(0,.015,1.395,.19,.135),(0,.020,1.32,.22,.147)],n=16,color='wool_grey',bone=lambda j,i:'Neck' if j==0 else 'UpperChest',caps=False,wind=lambda j,i:j/2)
 result.append(hood.object())
 return result

def generate(params):
 sex=params.get('sex','male'); scale=1 if sex=='male' else 1.62/1.72
 rig=create_skeleton(scale)
 result=[rig,body(rig,sex,scale)]
 result.extend(head(rig,sex,i,scale) for i in range(4))
 result.extend(hair(rig,i,scale) for i in range(6))
 # Both libraries share beards so an older woman can use facial hair if content asks.
 if sex=='male': result.extend(beard(rig,i,scale) for i in range(3))
 result.extend(clothing(rig,sex,scale))
 for ob in result:
  if ob.type=='MESH': ob['library_alternative']=ob.name.startswith(('Head_','Hair_','Beard_'))
 return result
