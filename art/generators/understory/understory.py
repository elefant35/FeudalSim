"""Original, seeded P0.6 shrubs and groundcover; palette + vertex R wind only."""
import math
import pathlib
import random
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import common as c

VARIANTS = {"hazel": 3, "bramble": 2, "gorse": 2, "reeds": 3, "grass_tuft": 4, "fern": 3}


def leaf(name, base, tip, width, color, wind):
    a, b = Vector(base), Vector(tip)
    direction = b-a
    side = Vector((-direction.y, direction.x, 0)).normalized()
    if side.length < .1: side = Vector((1, 0, 0))
    mid = a + direction * .54 + Vector((0, 0, width*.24))
    verts = [a-side*width*.25, a+side*width*.25, mid-side*width, mid+side*width, b]
    anchor_weight=max(0,min(1,wind(a)))
    def leaf_wind(v):
        t=max(0,min(1,(Vector(v)-a).dot(direction)/direction.length_squared))
        return anchor_weight+(1-anchor_weight)*t
    return c.mesh(name, verts, [(0, 1, 3, 2), (2, 3, 4)], color, leaf_wind)


def berry(name, centre, radius, color, wind):
    p = Vector(centre)
    vs=[p+Vector(v)*radius for v in ((1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1))]
    fs=[(4,0,2),(4,2,1),(4,1,3),(4,3,0),(5,2,0),(5,1,2),(5,3,1),(5,0,3)]
    return c.mesh(name, vs, fs, color, wind)


def generate(params):
    species=params.get("species", "hazel"); variant=params.get("variant", "a")
    idx=ord(variant)-ord("a")
    if species not in VARIANTS or not 0 <= idx < VARIANTS[species]:
        raise ValueError("unsupported species/variant")
    seed=int(params.get("seed", 6001+list(VARIANTS).index(species)*100+idx))
    rng=random.Random(seed)
    category="bush" if species in ("hazel", "bramble", "gorse") else "plant"
    name=f"{category}_{species}_{variant}"
    parts=[]; berries=[]
    if species == "hazel":
        h=(3.4, 4.0, 4.6)[idx]; wind=lambda v:max(0,(v.z-.8)/(h-.8))
        for j in range(5):
            angle=math.tau*j/5 + rng.uniform(-.2,.2)
            radial=Vector((math.cos(angle), math.sin(angle), 0))
            base=radial*rng.uniform(.08,.23)
            top=radial*rng.uniform(.65,1.05)+Vector((0,0,h*rng.uniform(.63,.76)))
            parts.append(c.tube(name+"_stem",base,top,.042,.018,"bark",5,wind))
            twig=top+radial*.35+Vector((0,0,.3))
            parts.append(c.tube(name+"_twig",top,twig,.018,.006,"bark",3,wind))
            centre=top+Vector((0,0,.2))
            parts.append(c.blob(name+"_leaves",centre,(.78,.75,h*.22),
                                ("leaf", "leaf_dark", "leaf_light")[j%3],seed+j,4,5,wind,.17))
            if j in (1,3):
                lower=Vector(base).lerp(top,.65)+Vector((0,0,.07))
                parts.append(c.blob(name+"_lower_leaves",lower,(.66,.60,h*.18),
                                    "leaf_dark" if j==1 else "leaf",seed+20+j,2,6,wind,.18))
    elif species == "bramble":
        h=.82 if idx==0 else 1.05; wind=lambda v:v.z/(h+.1)
        leaf_no=0
        for j in range(4):
            a=math.tau*j/4+.24*idx
            r=Vector((math.cos(a),math.sin(a),0))
            points=[r*.06,r*.35+Vector((0,0,h*.85)),r*.76+Vector((0,0,h)),r*1.16+Vector((0,0,h*.36))]
            for k in range(3): parts.append(c.tube(name+"_cane",points[k],points[k+1],.019,.013,"bark_dark",3,wind))
            neighbor=Vector((-r.y,r.x,0))*.35+Vector((0,0,h*.72))
            parts.append(c.tube(name+"_cross_cane",points[1],neighbor,.014,.010,"bark_dark",3,wind))
            for k in range(3):
                anchor=points[1].lerp(points[2],k/3)
                side=Vector((-r.y,r.x,0))
                for s in (-1,1):
                    end=anchor+side*s*.23+r*.07+Vector((0,0,.04))
                    parts.append(leaf(name+"_leaf",anchor,end,.115,"leaf_dark" if leaf_no%3 else "leaf",wind));leaf_no+=1
            for anchor in (points[0].lerp(points[1],.55),points[2].lerp(points[3],.55)):
                for sign in (-1,1):
                    end=anchor+Vector((-r.y,r.x,0))*sign*.22+r*.05+Vector((0,0,.05))
                    parts.append(leaf(name+"_leaf",anchor,end,.11,"leaf_dark" if sign<0 else "leaf",wind))
            if j<3:
                parts.append(c.blob(name+"_dense_leaves",r*.28+Vector((0,0,h*.46)),
                                    (.47,.47,h*.28),"leaf_dark" if j%2 else "leaf",seed+30+j,2,4,wind,.17))
            for k in range(1 if j<3 else 0):
                pos=points[2]+Vector((-.04+k*.09, .015*j, -.08))
                berries.append(berry(name+"_berries",pos,.035,"flint",wind))
    elif species == "gorse":
        h=1.18 if idx==0 else 1.48; wind=lambda v:v.z/h
        for j in range(6):
            a=math.tau*j/6+.1
            r=Vector((math.cos(a),math.sin(a),0))
            top=r*.48+Vector((0,0,h*.66))
            parts.append(c.tube(name+"_woody",r*.04,top,.025,.009,"bark_dark",3,wind))
            parts.append(c.blob(name+"_dense_green",r*.3+Vector((0,0,h*.45)),
                                (.43,.40,h*.40),"pine_dark",seed+j,2,4,wind,.15))
            for k in range(6):
                base=r*(.1+k*.065)+Vector((0,0,h*(.22+k*.09)))
                tip=base+r*.2+Vector((0,0,h*.19))
                parts.append(leaf(name+"_needle",base,tip,.035,"pine_dark" if k%2 else "pine",wind))
            for k in range(2):
                p=top+Vector((0,0,k*.13))+r*.10
                parts.append(berry(name+"_flowers",p,.045,"straw",wind))
    elif species == "reeds":
        h=(1.7,2.05,2.35)[idx]; wind=lambda v:v.z/h
        for j in range(12):
            a=rng.uniform(0,math.tau); r=rng.uniform(.04,.48)
            base=Vector((r*math.cos(a),r*math.sin(a),0))
            top=base+Vector((rng.uniform(-.14,.14),rng.uniform(-.14,.14),h*rng.uniform(.76,1)))
            parts.append(c.tube(name+"_culm",base,top,.012,.006,"grass",3,wind))
            for sign in (-1,1):
                p=base.lerp(top,.38 if sign<0 else .66)
                tip=p+Vector((math.cos(a)*.38*sign,math.sin(a)*.38*sign,.29))
                parts.append(leaf(name+"_blade",p,tip,.027,"leaf_light",wind))
            if j%2==0:
                centre=top-Vector((0,0,.11))
                parts.append(c.blob(name+"_seedhead",centre,(.025,.025,.12),"bark",seed+j,2,4,wind,0))
    elif species == "grass_tuft":
        h=(.22,.34,.46,.58)[idx]; wind=lambda v:v.z/h
        for j in range(16):
            a=rng.uniform(0,math.tau); r=rng.uniform(.015,h*.23)
            base=Vector((r*math.cos(a),r*math.sin(a),0))
            tip=base+Vector((math.cos(a)*h*.34,math.sin(a)*h*.34,h*rng.uniform(.7,1)))
            parts.append(leaf(name+"_blade",base,tip,.018+idx*.003,
                              ("grass","moss","leaf_light")[j%3],wind))
    elif species == "fern":
        nominal=(.65,.85,1.10)[idx]; h=nominal/.74; wind=lambda v:v.z/nominal
        for j in range(6):
            a=math.tau*j/6+.15*idx
            r=Vector((math.cos(a),math.sin(a),0))
            points=[Vector((0,0,0)),r*h*.14+Vector((0,0,h*.42)),r*h*.48+Vector((0,0,h*.73)),r*h*.91+Vector((0,0,h*.64))]
            for k in range(3): parts.append(c.tube(name+"_rachis",points[k],points[k+1],.008,.004,"moss",3,wind))
            side=Vector((-r.y,r.x,0))
            for k in range(7):
                t=.15+k*.11
                p=points[1].lerp(points[2],min(t*2,1)) if t<.5 else points[2].lerp(points[3],(t-.5)*2)
                length=h*.18*(1-t)*(.8 if k==0 else 1)
                for sign in (-1,1):
                    q=p+side*sign*length-r*length*.32
                    # One folded triangular leaflet, broad at base and tapered.
                    verts=[p-r*h*.05,q+Vector((0,0,h*.035)),p+r*h*.07+Vector((0,0,h*.025))]
                    parts.append(c.mesh(name+"_pinna",verts,[(0,1,2)],"leaf" if k%2 else "leaf_dark",wind))
    obj=c.join(parts,name)
    objects=[obj]
    if berries: objects.append(c.join(berries,name+"_berries"))
    return objects


if __name__ == "__main__":
    import fsart
    for species,count in VARIANTS.items():
        for i in range(count):
            fsart.reset()
            params={"species":species,"variant":chr(97+i)}
            objs=generate(params)
            fsart.result(species=species,variant=chr(97+i),triangles=sum(fsart.triangles(o) for o in objs),height=max(v.co.z for o in objs for v in o.data.vertices))
