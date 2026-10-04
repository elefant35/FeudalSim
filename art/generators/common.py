"""Reusable deterministic low-poly M2 primitives; palette UVs, metre-space geometry.
No runtime dependencies outside Blender. Every piece retains a POINT wind colour.
"""
import math, pathlib, random, sys
import bpy
from mathutils import Vector
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[2]/'tools/art'))
import fsart


def mesh(name, verts, faces, colour, wind=0):
    data=bpy.data.meshes.new(name); data.from_pydata(verts,[],faces); data.update()
    obj=bpy.data.objects.new(name,data); bpy.context.collection.objects.link(obj)
    fsart.paint(obj,colour)
    attr=data.color_attributes.new(name='COLOR_0',type='FLOAT_COLOR',domain='POINT')
    for v,c in zip(data.vertices,attr.data):
        w=wind(v.co) if callable(wind) else wind
        c.color=(max(0,min(1,w)),0,0,1)
    data.color_attributes.active_color=attr
    return obj


def tube(name, a, b, r1, r2, colour, sides=8, wind=0, phase=0):
    a,b=Vector(a),Vector(b); q=(b-a).to_track_quat('Z','Y')
    verts=[a+q@Vector((r1*math.cos(phase+i*math.tau/sides),r1*math.sin(phase+i*math.tau/sides),0)) for i in range(sides)]
    verts += [b+q@Vector((r2*math.cos(phase+i*math.tau/sides),r2*math.sin(phase+i*math.tau/sides),0)) for i in range(sides)]
    faces=[(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
    faces += [tuple(reversed(range(sides))),tuple(range(sides,2*sides))]
    return mesh(name,verts,faces,colour,wind)


def box(name, centre, size, colour, wind=0):
    c=Vector(centre); s=Vector(size)/2
    verts=[c+Vector((x*s.x,y*s.y,z*s.z)) for z in (-1,1) for y in (-1,1) for x in (-1,1)]
    faces=[(0,2,3,1),(4,5,7,6),(0,1,5,4),(2,6,7,3),(0,4,6,2),(1,3,7,5)]
    return mesh(name,verts,faces,colour,wind)


def blob(name, centre, size, colour, seed=0, rings=4, sides=8, wind=0, irregular=.12):
    rng=random.Random(seed); c=Vector(centre); s=Vector(size)
    verts=[c+Vector((0,0,-s.z))]
    for j in range(1,rings):
        phi=-math.pi/2+math.pi*j/rings
        for i in range(sides):
            a=math.tau*i/sides+(.13 if j%2 else 0); noise=1+rng.uniform(-irregular,irregular)
            verts.append(c+Vector((s.x*math.cos(phi)*math.cos(a)*noise,s.y*math.cos(phi)*math.sin(a)*noise,s.z*math.sin(phi))))
    verts.append(c+Vector((0,0,s.z)))
    faces=[]
    for i in range(sides):faces.append((0,1+(i+1)%sides,1+i))
    for j in range(rings-2):
        for i in range(sides):
            a=1+j*sides+i;b=1+j*sides+(i+1)%sides;c1=b+sides;d=a+sides
            faces.extend([(a,b,c1),(a,c1,d)])
    last=1+(rings-2)*sides
    for i in range(sides):faces.append((len(verts)-1,last+i,last+(i+1)%sides))
    return mesh(name,verts,faces,colour,wind)


def join(parts,name):
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:p.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]; bpy.ops.object.join()
    obj=parts[0]; obj.name=obj.data.name=name
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    obj.data.materials.clear();obj.data.materials.append(fsart.palette_material())
    for p in obj.data.polygons:p.material_index=0
    return obj


def convex_trunk(name,a,b,radius):
    obj=tube(name+'-convcolonly',a,b,radius,radius*.8,'bark',6)
    return obj
