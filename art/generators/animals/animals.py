"""Original low-poly island fauna: species-specific proportions and silhouette.
Mesh+deform rig+30 fps clips in one GLB. Source preparation precedes the P2 gate.
"""
import pathlib,sys,math
import bpy
from mathutils import Vector
sys.path.insert(0,str(pathlib.Path(__file__).parent))
sys.path.insert(0,str(pathlib.Path(__file__).parents[1]/'settlers'))
from settlers import Mesh
from profiles import PROFILES
import animal_rig


def ellipsoid(m,c,r,bone,col,n=12,rings=6):
 c=Vector(c);rx,ry,rz=r;bottom=m.vert(c+Vector((0,0,-rz)),bone);ids=[]
 for j in range(1,rings):
  phi=-math.pi/2+j*math.pi/rings
  row=[m.vert(c+Vector((rx*math.cos(phi)*math.cos(math.tau*i/n),ry*math.cos(phi)*math.sin(math.tau*i/n),rz*math.sin(phi))),bone) for i in range(n)];ids.append(row)
 top=m.vert(c+Vector((0,0,rz)),bone)
 for i in range(n):m.face((bottom,ids[0][(i+1)%n],ids[0][i]),col(0,i) if callable(col) else col)
 for j in range(len(ids)-1):
  for i in range(n):m.face((ids[j][i],ids[j][(i+1)%n],ids[j+1][(i+1)%n],ids[j+1][i]),col(j+1,i) if callable(col) else col)
 for i in range(n):m.face((ids[-1][i],ids[-1][(i+1)%n],top),col(rings-1,i) if callable(col) else col)


def body(m,p):
 small=p['kind']=='hare';n=8 if small else 18 if p['kind']=='bear' else 14;length=p['length'];w=p['width'];h=p['height'];center=h-(.105 if small else .22 if p['kind'] in ('deer','goat') else .20)
 zrad=.11 if small else .25 if p['kind'] in ('deer','goat') else .28 if p['kind']=='boar' else .33 if p['kind']=='bear' else .21 if p['species']=='grey_wolf' else .105
 rows=[(-.42,.14,.37),(-.32,.80,.91),(-.22,1,1.08),(-.06,1.02,1),(.12,.97,.88),(.30,.91,.85),(.43,.78,.87),(.49,.34,.47)]
 if small:rows=[(-.39,.50,.55),(-.23,.70,.72),(-.06,.91,.88),(.13,1.03,1.10),(.29,1.02,1.27),(.43,.63,.75)]
 ids=[]
 for j,(yy,wr,zr) in enumerate(rows):
  y=yy*length;bone='Chest' if j<3 else 'Spine' if j<5 else 'Pelvis';row=[]
  for i in range(n):
   a=math.tau*i/n;z=center+math.cos(a)*zrad*zr
   if p['kind']=='canid' and j in [3,4,5]:z+=.035 if p['species']=='grey_wolf' else .018
   if p['kind']=='boar' and math.cos(a)>.4:z+=.08*(1-j/(len(rows)-1))
   row.append(m.vert((math.sin(a)*w*wr,y,z),bone))
  ids.append(row)
 for j in range(len(ids)-1):
  for i in range(n):
   a=math.tau*(i+.5)/n;color=p['coat']
   if math.cos(a)<-.55:color=p['belly']
   elif p['kind']=='canid' and j in [2,3,4] and math.cos(a)>.65:color='ashen_grey' if p['species']=='grey_wolf' else 'brannoch_rust'
   m.face((ids[j][i],ids[j][(i+1)%n],ids[j+1][(i+1)%n],ids[j+1][i]),color)
 m.face(tuple(reversed(ids[0])),p['coat']);m.face(tuple(ids[-1]),'linen' if p['kind']=='deer' else p['coat'])
 # Blended shoulder/haunch masses taper down into the upper limb rather than spherical joints.
 for side,s in [('Left',1),('Right',-1)]:
  for limb,land in [('Front',p['front']),('Hind',p['hind'])]:
   if small and limb=='Front':continue
   x=w*(.67 if limb=='Front' else .73)*s
   upper=side+limb+'Upper';bn={upper:.65,'Chest' if limb=='Front' else 'Pelvis':.35}
   z=land[1]-(.04 if small else .07)
   rad=(w*.56,length*(.12 if limb=='Front' else .14),zrad*.86)
   if small:rad=(.07,.11,.12)
   ellipsoid(m,(x,land[0],z),rad,bn,p['coat'],8 if small else 12 if p['kind']=='bear' else 10,4 if small else 6)

 if p['kind']=='canid':
  # Ruff joins the low horizontal neck into the shoulder mass; this is a canid,
  # not the upright slender cervical silhouette used for the deer family.
  rx=.24 if p['species']=='grey_wolf' else .115
  ellipsoid(m,(0,p['neck'][1],p['neck'][2]-.055),(rx,rx*1.02,rx*.80),'Chest',p['coat'],10,5)


def legs(m,p):
 small=p['kind']=='hare';hoof=p['kind'] in ('deer','goat','boar');w=p['width'];h=p['height'];n=6 if small else 8
 for side,s in [('Left',1),('Right',-1)]:
  for limb,land in [('Front',p['front']),('Hind',p['hind'])]:
   x=w*(.66 if limb=='Front' else .73)*s;pts=[Vector((x,land[i],land[i+1])) for i in range(0,8,2)]
   names=[side+limb+a for a in ['Upper','Lower','Metatarsal','Foot']]
   mid=pts[0].lerp(pts[1],.52);allpts=[pts[0],mid,pts[1],pts[2],pts[3]]
   radius=.021 if small else .045 if hoof else .042 if p['species']=='fox' else .067 if p['kind']=='bear' else .057
   radii=[radius*1.85,radius*1.45,radius, radius*.68,radius*.69]
   weights=[{names[0]:.75,'Chest' if limb=='Front' else 'Pelvis':.25},names[0],{names[0]:.35,names[1]:.65},{names[1]:.25,names[2]:.75},names[2]]
   color=lambda j,i: 'hair_dark' if p['species']=='fox' and j>1 else p['coat']
   m.tube(allpts,radii,lambda j:weights[j],color,n=n,caps=not small)
   sole=pts[3];footlen=.11 if hoof else .12 if p['kind']=='bear' else .13 if p['kind']=='hare' and limb=='Hind' else .07 if small else .10 if p['species']=='grey_wolf' else .055
   fr=.040 if hoof else .023 if small else .083 if p['kind']=='bear' else .053 if p['species']=='grey_wolf' else .025
   if hoof:
    # Two individually modeled cloven hoof shells, with a legible dark split.
    for half in [-1,1]:
     m.rings([(x+half*fr*.51,sole.y-footlen*.37,0,fr*.46,footlen*.57),(x+half*fr*.51,sole.y-footlen*.40,sole.z*.64,fr*.50,footlen*.57),(x+half*fr*.46,sole.y,sole.z,fr*.44,footlen*.34)],n=6,color='hair_dark',bone=names[3])
    if p['kind']=='boar':
     for half in [-1,1]:ellipsoid(m,(x+half*fr*.55,sole.y+.025,sole.z+.035),(.014,.015,.025),names[2],'hair_dark',6,3)
   else:
    ellipsoid(m,(x,sole.y-footlen*.35,sole.z*.5),(fr,footlen*.72,sole.z*.5),names[3],p['coat'] if p['species']!='fox' else 'hair_dark',6 if small else 8,3 if small else 4)
    if p['kind']=='bear':
     for k in range(4):m.tube([(x+(k-1.5)*.035,sole.y-footlen*.75,.035),(x+(k-1.5)*.034,sole.y-footlen*1.12,.015)],[.009,.003],names[3],'limestone',n=5)


def face(m,p):
 kind=p['kind'];small=kind=='hare';head=Vector(p['head']);muzzle=Vector(p['muzzle']);neck=Vector(p['neck']);coat=p['coat'];n=8 if small else 12
 headr=(.085,.098,.087) if small else (.135,.18,.13) if kind in ('deer','goat') else (.20,.24,.21) if kind=='boar' else (.24,.24,.19) if kind=='bear' else (.13,.17,.14) if p['species']=='grey_wolf' else (.071,.087,.082)
 neckrad=.06 if small else .115 if kind=='deer' else .16 if kind=='boar' else .21 if kind=='bear' else .14 if p['species']=='grey_wolf' else .075 if p['species']=='fox' else .12
 m.tube([neck,neck.lerp(head,.36),neck.lerp(head,.74),head],[neckrad*1.38,neckrad*1.13,neckrad*.85,neckrad*.73],lambda j:'NeckLower' if j<2 else 'NeckUpper',coat,n=n)
 ellipsoid(m,head,headr,'Head',coat,n,5 if small else 7)
 mr=.04 if small else .073 if kind=='deer' else .09 if kind=='goat' else .14 if kind=='boar' else .13 if kind=='bear' else .063 if p['species']=='grey_wolf' else .032
 start=head+Vector((0,-headr[1]*.46,-headr[2]*.30))
 m.tube([start,start.lerp(muzzle,.53),muzzle],[ (headr[0]*.72,headr[2]*.63),(mr*1.14,mr*.86),(mr,mr*.65)],'Head',lambda j,i:p['belly'] if kind=='canid' and i>n/2 else coat,n=n)
 ellipsoid(m,muzzle+Vector((0,-.012,.008)),(mr*.79,.028 if not small else .009,mr*.47),'Head','skin_4' if kind=='boar' else 'ink',8 if not small else 6,3)
 # Lower jaw can open for grazing, yawns, and the predator's bite.
 jaw=head+Vector((0,-headr[1]*.42,-headr[2]*.66));end=muzzle+Vector((0,.015,-mr*.48))
 m.tube([jaw,jaw.lerp(end,.7),end],[headr[0]*.60,mr*.85,mr*.75],'Jaw',p['belly'] if kind=='canid' else coat,n=6)
 for side,s in [('Left',1),('Right',-1)]:
  ex=head.x+s*headr[0]*.94;ey=head.y-headr[1]*.30;ez=head.z+headr[2]*.17;d=.026 if not small else .013
  m.patch([(ex,ey-d,ez),(ex+s*.008,ey,ez+d*.52),(ex,ey+d,ez),(ex+s*.006,ey,ez-d*.45)],'ink','Head')
  m.patch([(ex+s*.009,ey-d*.20,ez+.003),(ex+s*.009,ey,ez+.008),(ex+s*.009,ey+d*.20,ez+.003)],'hair_fair' if kind=='canid' else 'wood_light','Head')
  # Broad species-specific pinnae: cervid leaves, canid triangles, tall black-tipped hare ears.
  root=head+Vector((s*headr[0]*.58,.028,headr[2]*.78));tip=root+Vector((s*(.055 if small else .11 if kind=='deer' else .09),.015,p['ear']))
  width=.039 if small else .064 if kind=='deer' else .070 if kind=='bear' else .051
  if kind=='bear':
   ellipsoid(m,root+Vector((s*.030,0,.05)),(.064,.036,.071),side+'Ear',coat,8,4);continue
  if kind=='boar':tip+=Vector((s*.08,-.08,-p['ear']*.15))
  mid=root.lerp(tip,.55);bn=side+'Ear'
  points=[root+Vector((-width,0,0)),root+Vector((width,0,0)),mid+Vector((width*.73,0,0)),tip,mid+Vector((-width*.73,0,0))]
  front=[q+Vector((0,-.016,0)) for q in points];back=[q+Vector((0,.016,0)) for q in points]
  ids=[m.vert(q,bn) for q in front+back];m.face(ids[:5],coat);m.face(tuple(reversed(ids[5:])),coat)
  for i in range(5):m.face((ids[i],ids[(i+1)%5],ids[(i+1)%5+5],ids[i+5]),coat)
  m.patch([root.lerp(mid,.32)+Vector((0,-.018,0)),mid+Vector((width*.41,-.018,0)),tip.lerp(mid,.18)+Vector((0,-.018,0)),mid+Vector((-width*.41,-.018,0))],'skin_4' if kind=='boar' else 'wool_brown',bn)
  if small:
   m.patch([tip+Vector((0,-.019,0)),tip.lerp(mid,.33)+Vector((width*.22,-.019,0)),tip.lerp(mid,.33)+Vector((-width*.22,-.019,0))],'hair_dark',bn)
 if kind=='boar':
  for s in [-1,1]:
   a=muzzle+Vector((s*mr*.83,.08,-.028));m.tube([a,a+Vector((s*.039,-.025,.055)),a+Vector((s*.032,-.028,.107))],[.025,.018,.0025],'Jaw','limestone',n=6)
 if kind=='goat':
  m.tube([muzzle+Vector((0,.08,-.04)),muzzle+Vector((0,.1,-.20))],[.040,.006],'Jaw','hair_dark',n=8)
 if kind=='canid':
  # One row of short teeth travels with the hinged jaw, understated when shut.
  for s in [-1,1]:
   for j in range(3):
    y=muzzle.y+.05+j*.045;z=muzzle.z-.035+(y-muzzle.y)*.20
    m.tube([(s*mr*.75,y,z),(s*mr*.75,y,z+.020)],[.008,.0018],'Jaw','linen',n=4)


def antlers(m,p):
 if p.get('antlers'):
  head=Vector(p['head'])
  for s in [-1,1]:
   root=head+Vector((s*.092,.040,.095))
   main=[root,root+Vector((s*.16,.05,.18)),root+Vector((s*.31,.15,.40)),root+Vector((s*.34,.23,.67)),root+Vector((s*.42,.28,.81))]
   m.tube(main,[.042,.037,.028,.017,.0025],'Head','wood_light',n=7)
   for start,delta,r in [(main[0]+Vector((0,.02,.07)),(s*.08,-.24,.27),.024),(main[1],(s*.06,-.18,.34),.022),(main[2],(s*.20,-.02,.26),.019),(main[3],(-s*.10,-.08,.23),.015)]:
    end=start+Vector(delta);m.tube([start,start.lerp(end,.50)+Vector((s*.026,0,0)),end],[r,r*.65,.002],'Head','wood_light',n=6)
 elif p['kind']=='goat':
  head=Vector(p['head'])
  for s in [-1,1]:
   a=head+Vector((s*.065,.03,.095));m.tube([a,a+Vector((s*.04,.09,.19)),a+Vector((s*.065,.22,.30)),a+Vector((s*.05,.34,.32))],[.04,.030,.017,.002],'Head','hair_dark',n=8)


def tail(m,p):
 b=m.rig.data.bones['Tail1'];b2=m.rig.data.bones['Tail2'];r=.035 if p['kind'] in ('deer','goat') else .065 if p['species']=='grey_wolf' else .060 if p['species']=='fox' else .012 if p['kind']=='boar' else .035
 if p['kind']=='hare':ellipsoid(m,tuple(b2.tail_local),(.045,.045,.043),'Tail2','linen',8,4);return
 m.tube([b.head_local,b.tail_local,b2.tail_local],[r*.9,r,.005 if p['kind']=='boar' else r*.37],lambda j:'Tail1' if j<2 else 'Tail2',lambda j,i:'linen' if p['species']=='fox' and j==1 else p['coat'],n=8)
 if p['kind']=='boar':
  for i in range(9):
   yy=-.58+i*.13;z=p['height']+.030-i*.004;m.patch([(-.025,yy,z-.065),(.025,yy,z-.065),(0,yy+.035,z+.025)],'hair_dark','Chest' if i<4 else 'Spine')


def generate(params):
 species=params.get('species','red_deer_stag');p=dict(PROFILES[species]);p['species']=species
 rig=animal_rig.create(p);m=Mesh('animal_'+species+'_lod0',rig)
 body(m,p);legs(m,p);face(m,p);tail(m,p);antlers(m,p);ob=m.object();ob['species']=species
 if params.get('animate',True):
  import animal_motion;animal_motion.bake(rig,p)
 return [rig,ob]
