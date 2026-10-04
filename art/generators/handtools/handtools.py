"""Seven original M2 first-person hand tools. Metres, grip at origin, heads +Z.
Deterministic chipped flint, rough wood, hide lashings and dull forged iron.
Rebuild: python3 tools/art/build_m2.py tools --previews
"""
import pathlib,sys,math,random
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]))
import common as C
import fsart

def blade(name,rows,colors):
 """Ridged biface with individually knapped edge facets, running along +Z."""
 verts=[]
 for z,w,thick,centre in rows:
  verts.extend([(centre-w,0,z),(centre,-thick,z),(centre+w,0,z),(centre,thick,z)])
 faces=[]; swatches=[]
 for j in range(len(rows)-1):
  for i in range(4):
   a=j*4+i;b=j*4+(i+1)%4;c=b+4;d=a+4
   faces.extend([(a,b,c),(a,c,d)]);swatches.extend([colors[(j+i)%len(colors)],colors[(j+i+1)%len(colors)]])
 faces.extend([(3,2,1,0),tuple(range((len(rows)-1)*4,len(rows)*4))]);swatches.extend([colors[0],colors[0]])
 obj=C.mesh(name,verts,faces,colors[0])
 for i,col in enumerate(swatches):fsart.paint(obj,col,faces={i})
 return obj

def cord(name,z,radius=.027,loops=1,pitch=.007,colour='wool_brown'):
 # Continuous triangular-section thong helix: shared rings avoid the cost and
 # visible seams of independent capped segments.
 N=loops*8;verts=[]
 for i in range(N+1):
  a=i*math.tau/8
  for j in range(3):
   b=j*math.tau/3;rr=radius+.0022*math.cos(b)
   verts.append((rr*math.cos(a),rr*math.sin(a),z+pitch*i/8+.0022*math.sin(b)))
 faces=[]
 for i in range(N):
  for j in range(3):faces.append((i*3+j,i*3+(j+1)%3,(i+1)*3+(j+1)%3,(i+1)*3+j))
 faces.extend([(2,1,0),(N*3,N*3+1,N*3+2)])
 return [C.mesh(name,verts,faces,colour)]

def handle(name,length,radius=.018,seed=301,wood='bark'):
 # Slightly bent, hand-shaved ash haft, with a thick retained butt.
 rng=random.Random(seed);bottom=-.09;top=length+bottom
 pieces=[]
 for j in range(4):
  a=bottom+(top-bottom)*j/4;b=bottom+(top-bottom)*(j+1)/4
  bend=(.004 if length<.3 else .012)*(j/4)**2;endbend=(.004 if length<.3 else .012)*((j+1)/4)**2
  r0=radius*([1.10,1.08,1.04,1.0][j] if length<.3 else 1.10-.13*j)
  r1=radius*([1.08,1.04,1.0,.95][j] if length<.3 else .99-.13*j)
  pieces.append(C.tube(name,(bend,0,a),(endbend,0,b),r0,r1,wood,9,phase=.12*j))
 # Palm grip has six turns of sea-darkened hide thong, deliberately uneven.
 pieces.extend(cord(name,-.045,radius+.002,loops=5,pitch=.013,colour='wool_brown'))
 return pieces

def axehead(name,stone=False):
 if stone:
  # Lenticular ground stone wedge, wider cutting edge, narrow hafted butt.
  profiles=[(-.182,.465,.022),(-.160,.488,.035),(-.115,.512,.050),(-.055,.528,.060),(.015,.525,.041),(.052,.511,.021)]
  verts=[]
  for x,z,h in profiles:
   verts.extend([(x,-h*.53,z-h),(x,-h*.82,z),(x,-h*.53,z+h),(x,h*.53,z+h),(x,h*.82,z),(x,h*.53,z-h)])
  faces=[]
  for j in range(len(profiles)-1):
   for i in range(6):faces.append((j*6+i,j*6+(i+1)%6,(j+1)*6+(i+1)%6,(j+1)*6+i))
  faces.extend([tuple(reversed(range(6))),tuple(range((len(profiles)-1)*6,len(profiles)*6))])
  ob=C.mesh(name,verts,faces,'granite_dark')
  for p in ob.data.polygons:
   if p.index%5 in (1,3):fsart.paint(ob,'granite',faces={p.index})
  return ob
 # Thick forged cheek, thin bevel and beard: plausible plain salvage axe.
 profile=[(-.21,.43),(-.225,.515),(-.195,.655),(-.025,.615),(.045,.602),(.053,.513),(-.033,.50),(-.066,.467)]
 verts=[(x,sign*(.0035 if x<-.18 else .019 if x<-.07 else .031),z) for sign in (-1,1) for x,z in profile]
 n=len(profile);faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
 faces.extend((i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n))
 ob=C.mesh(name,verts,faces,'iron')
 fsart.paint(ob,'steel',faces={2,3,4});return ob

def generate(params):
 item=params.get('item','stone_axe');seed=int(params.get('seed',301));rng=random.Random(seed);name='prop_'+item+'_a';parts=[]
 if item in ('flint_knife','iron_knife'):
  parts.extend(handle(name,.18,.018,seed,wood='wood_light'))
  if item=='flint_knife':
   rows=[(.085,.022,.010,0),(.125,.042,.012,-.004),(.175,.035,.014,.003),(.235,.023,.011,.005),(.285,.002,.001,.006)]
   parts.append(blade(name,rows,['flint','granite_dark','granite']))
   # Rawhide lashings hold the blade's blunt tang in a split grip.
   parts.append(C.tube(name,(0,0,.060),(0,0,.090),.022,.023,'wood_light',9))
   parts.extend(cord(name,.065,.024,loops=4,pitch=.006,colour='linen'))
  else:
   rows=[(.091,.024,.007,0),(.115,.023,.008,0),(.215,.020,.007,-.001),(.275,.012,.005,-.008),(.305,.001,.001,-.02)]
   parts.append(blade(name,rows,['iron','iron','steel']))
   parts.append(C.box(name,(0,0,.079),(.065,.027,.013),'iron'))
   # Two hammered pins across the worn scales.
   for z in (-.04,.035):parts.append(C.tube(name,(-.019,0,z),(.019,0,z),.0025,.0025,'iron',6))
 elif item in ('stone_axe','iron_axe'):
  stone=item=='stone_axe';parts.extend(handle(name,.64 if stone else .72,.025,seed,wood='wood_light'))
  parts.append(axehead(name,stone))
  if stone:
   parts.append(C.tube(name,(.014,0,.452),(.014,0,.548),.033,.033,'wood_light',9))
   parts.extend(cord(name,.455,.036,loops=6,pitch=.013,colour='wool_brown'))
   # Crossed lashings bridge from butt cheek to shaft, visibly supporting the head.
   for dy in (-.031,.031):
    parts.append(C.tube(name,(-.078,dy,.555),(.039,dy,.458),.0035,.0035,'linen',6))
    parts.append(C.tube(name,(-.075,dy,.48),(.038,dy,.555),.0035,.0035,'wool_brown',6))
  else:
   # Raised iron eye lip makes the hafted construction readable at arm's length.
   for z in (.511,.604):
    N=10
    for i in range(N):
     a=i*math.tau/N;b=(i+1)*math.tau/N
     parts.append(C.tube(name,(.014+.029*math.cos(a),.033*math.sin(a),z),(.014+.029*math.cos(b),.033*math.sin(b),z),.004,.004,'iron',5))
   parts.append(C.box(name,(.014,0,.612),(.040,.015,.012),'wood_light'))
 elif item=='hammerstone':
  ob=C.blob(name,(0,0,.03),(.052,.045,.069),'granite',seed,rings=6,sides=10,irregular=.10)
  for p in ob.data.polygons:
   if p.center.z>.072 or p.index%13==0:fsart.paint(ob,'limestone',faces={p.index})
  parts.append(ob)
 elif item=='flint_nodule':
  ob=C.blob(name,(0,0,.024),(.069,.059,.087),'limestone',seed,rings=5,sides=11,irregular=.24)
  for p in ob.data.polygons:
   if p.index%11 in (1,2,3,4) or p.center.x<-.025:fsart.paint(ob,'flint',faces={p.index})
   elif p.index%7==0:fsart.paint(ob,'granite',faces={p.index})
  parts.append(ob)
 elif item=='flint_flake':
  rows=[(-.017,.008,.006,-.009),(.004,.031,.010,0),(.035,.034,.006,.004),(.069,.028,.004,.002),(.105,.001,.001,-.011)]
  parts.append(blade(name,rows,['flint','granite','flint','granite_dark']))
  parts.append(C.blob(name,(0,-.005,.006),(.012,.006,.012),'flint',seed,rings=3,sides=7))
 else:raise ValueError(item)
 ob=C.join(parts,name);ob['origin_convention']='palm grip; shaft/blade/head +Z';ob['handheld']=True
 return [ob]
