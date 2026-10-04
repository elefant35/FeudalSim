"""Original P1 botanical patches and fruiting bushes, with explicit lookalike tells."""
import math
import pathlib
import random
import sys

import bpy
from mathutils import Vector

sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]))
import common as c
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]/'understory'))
from understory import leaf as blade, berry

BUSHES=('hawthorn','blackthorn','elder','crab_apple','juniper','dog_rose','bilberry')
PLANTS=('yarrow','plantain','comfrey','ramsons','feverfew','meadowsweet','wild_thyme','sphagnum',
        'valerian','water_mint','nettle','sorrel','wild_carrot','hemlock','water_hemlock','foxglove',
        'lily_of_the_valley','deadly_nightshade','field_mushroom','death_cap','fly_agaric','wild_oats',
        'sea_beet','wild_cabbage','wild_pea','wild_flax','wild_hops','wild_strawberry')
ALL=BUSHES+PLANTS


def leaf(name,base,tip,width,color,wind,edge='entire'):
    if edge=='entire':return blade(name,base,tip,width,color,wind)
    a,b=Vector(base),Vector(tip); d=b-a
    side=Vector((-d.y,d.x,0)).normalized()
    if side.length<.1:side=Vector((1,0,0))
    if edge=='arrow':
        outline=[a+side*width*.75+d*.05,a+side*width*.42+d*.62,b,
                 a-side*width*.42+d*.62,a-side*width*.75+d*.05,a+d*.25]
    else:
        pattern=(.95,.32,.78) if edge=='lobed' else ((.65,1,.65) if edge=='round' else (.35,.85,.5,1,.45))
        left=[a];right=[]
        for i,w in enumerate(pattern):
            t=(i+1)/(len(pattern)+1);p=a+d*t+Vector((0,0,width*.18*math.sin(t*math.pi)))
            left.append(p-side*width*w);right.append(p+side*width*w)
        outline=left+[b]+list(reversed(right))
    centre=a+d*.5+Vector((0,0,width*.28))
    verts=outline+[centre];faces=[(len(outline),i,(i+1)%len(outline)) for i in range(len(outline))]
    start=max(0,min(1,wind(a)))
    def weight(v):
        t=max(0,min(1,(Vector(v)-a).dot(d)/d.length_squared));return start+(1-start)*t
    return c.mesh(name,verts,faces,color,weight)


def star(name,p,r,color,wind,petals=5,vertical=False,petal_width=.14):
    p=Vector(p);verts=[];faces=[]
    for i in range(petals):
        a=math.tau*i/petals
        u=Vector((math.cos(a),math.sin(a),0)) if not vertical else Vector((math.cos(a),0,math.sin(a)))
        s=Vector((-u.y,u.x,0)) if not vertical else Vector((-u.z,0,u.x))
        n=len(verts);verts.extend([p+s*r*petal_width,p+u*r,p-s*r*petal_width]);faces.append((n,n+1,n+2))
    return c.mesh(name,verts,faces,color,wind)


def bell(name,top,r,depth,color,wind,spots=False):
    top=Vector(top);verts=[];sides=5
    for z,rad in ((0,r*.5),(-depth,r)):
        for i in range(sides):
            a=math.tau*i/sides;verts.append(top+Vector((rad*math.cos(a),rad*math.sin(a),z)))
    faces=[(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
    faces.append(tuple(reversed(range(sides))))
    return c.mesh(name,verts,faces,color,wind)


class Plant:
    def __init__(self,name,height,rng):
        self.name=name;self.height=height;self.rng=rng;self.body=[];self.flowers=[];self.fruit=[]
        self.wind=lambda v:max(0,min(1,v.z/max(height,.04)))
    def stem(self,a,b,r=.009,color='grass',sides=3):
        o=c.tube(self.name+'_stem',a,b,r,r*.55,color,sides,self.wind);self.body.append(o);return o
    def leaf(self,a,b,w=.055,color='leaf',edge='entire'):
        self.body.append(leaf(self.name+'_leaf',a,b,w,color,self.wind,edge))
    def crown(self,p,size,color='leaf_dark',seed=0,rings=3,sides=5):
        self.body.append(c.blob(self.name+'_crown',p,size,color,seed,rings,sides,self.wind,.18))
    def bloom(self,p,r=.035,color='linen',petals=5,vertical=False):
        self.flowers.append(star(self.name+'_flowers',p,r,color,self.wind,petals,vertical))
    def berry(self,p,r=.018,color='flint'):
        self.fruit.append(berry(self.name+'_fruit',p,r,color,self.wind))
    def twig(self,a,b):
        a,b=Vector(a),Vector(b);side=Vector((.003,0,0))
        self.body.append(c.mesh(self.name+'_fruit_twig',[a-side,a+side,b+side,b-side],[(0,1,2,3)],'bark',self.wind))
    def bell(self,p,r=.025,depth=.048,color='woad'):
        self.flowers.append(bell(self.name+'_flowers',p,r,depth,color,self.wind))
    def finish(self):
        objects=[]
        for bucket,suffix in ((self.body,''),(self.flowers,'_flowers'),(self.fruit,'_fruit')):
            if bucket:objects.append(c.join(bucket,self.name+suffix))
        return objects


def rosette(p,base,h,count=6,width=.09,color='leaf',edge='entire'):
    base=Vector(base)
    for j in range(count):
        a=math.tau*j/count+p.rng.uniform(-.15,.15)
        tip=base+Vector((h*.5*math.cos(a),h*.5*math.sin(a),h*p.rng.uniform(.5,1)))
        p.leaf(base,tip,width,color,edge)


def pinnate(p,base,tip,pairs=3,width=.025):
    a,b=Vector(base),Vector(tip);d=b-a;side=Vector((-d.y,d.x,0)).normalized()
    p.stem(a,b,.003)
    if side.length<.1:side=Vector((1,0,0))
    for j in range(pairs):
        t=.24+j*.21;anchor=a+d*t
        for sign in (-1,1):
            end=anchor+side*sign*d.length*.24*(1-t)+Vector((0,0,.03))
            p.leaf(anchor,end,width,'leaf' if j%2 else 'leaf_dark')


def umbel(p,base,h,kind):
    base=Vector(base);top=base+Vector((0,0,h));p.stem(base,top)
    for j in range(5):
        a=math.tau*j/5
        point=top+Vector((math.cos(a)*.12,math.sin(a)*.12,.025))
        p.stem(top-Vector((0,0,.07)),point,.004)
        p.bloom(point,.042,'linen',6)
    p.bloom(top+Vector((0,0,.028)),.045,'linen',6)
    if kind=='wild_carrot':
        p.flowers.append(berry(p.name+'_central_floret',top+Vector((0,0,.032)),.009,'bark_dark',p.wind))
        p.body.append(star(p.name+'_bract',top-Vector((0,0,.018)),.13,'leaf_dark',p.wind,5))
        # Sparse visible stem hairs; silhouette remains shared with hemlock.
        for j in range(6):
            z=h*(.15+j*.10);q=base+Vector((0,0,z));a=j*2.1
            p.body.append(c.mesh(p.name+'_hair',[q+Vector((.007,0,0)),q+Vector((.016*math.cos(a),.016*math.sin(a),.007)),q+Vector((.007,0,.010))],[(0,1,2)],'leaf_light',p.wind))
    elif kind=='hemlock':
        # Distinct irregular cool blotches proxy purple with existing woad.
        for j in range(6):
            q=base+Vector((0,-.009,h*(.1+j*.105)))
            p.body.append(c.mesh(p.name+'_stem_blotch',[q+Vector((-.004,0,0)),q+Vector((.006,0,.006)),q+Vector((.003,0,.027)),q+Vector((-.004,0,.022))],[(0,1,2,3)],'woad',p.wind))
    for j in range(2):
        z=h*(.22+j*.22);a=j*2.4;anchor=base+Vector((0,0,z))
        end=anchor+Vector((.22*math.cos(a),.22*math.sin(a),.15))
        if kind=='water_hemlock':
            for sign in (-1,1):
                side=Vector((-math.sin(a),math.cos(a),0))
                p.leaf(anchor,end+side*sign*.06,.037,'leaf','toothed')
        else:pinnate(p,anchor,end,2,.017)


def mushroom(p,base,kind,scale=1):
    b=Vector(base);s=scale;stemtop=b+Vector((0,0,.12*s))
    p.stem(b,stemtop,.012*s,'linen',5)
    sides=8;verts=[]
    for radius,z in ((.065,.115),(.045,.146)):
        for j in range(sides):
            a=math.tau*j/sides;verts.append(b+Vector((radius*s*math.cos(a),radius*s*math.sin(a),z*s)))
    verts += [b+Vector((0,0,.156*s)),b+Vector((0,0,.112*s))]
    faces=[(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
    faces += [(16,8+i,8+(i+1)%8) for i in range(8)]
    faces += [(17,(i+1)%8,i) for i in range(8)]
    color='linen' if kind=='field_mushroom' else ('sand' if kind=='death_cap' else 'madder')
    obj=c.mesh(p.name+'_cap',verts,faces,color,0)
    c.fsart.paint(obj,'bark' if kind=='field_mushroom' else 'linen',range(16,24))
    if kind=='death_cap':c.fsart.paint(obj,'moss',range(8,12))
    p.body.append(obj)
    # A thin annular collar, not a complete disk blocking the stem.
    verts=[]
    for radius in (.013,.027):
        for j in range(6):
            a=math.tau*j/6;verts.append(b+Vector((radius*s*math.cos(a),radius*s*math.sin(a),.082*s)))
    p.body.append(c.mesh(p.name+'_ring',verts,[(i,(i+1)%6,(i+1)%6+6,i+6) for i in range(6)],'linen',0))
    if kind!='field_mushroom':
        verts=[]
        for radius,z in ((.018,0),(.028,.035)):
            for j in range(6):
                a=math.tau*j/6;verts.append(b+Vector((radius*s*math.cos(a),radius*s*math.sin(a),z*s)))
        p.body.append(c.mesh(p.name+'_volva',verts,[(i,(i+1)%6,(i+1)%6+6,i+6) for i in range(6)],'linen',0))
    if kind=='fly_agaric':
        for j in range(5):
            a=math.tau*j/5;point=b+Vector((.035*s*math.cos(a),.035*s*math.sin(a),.15*s))
            p.body.append(star(p.name+'_white_spot',point,.007*s,'linen',lambda v:0,3))


def bush(p,species,h,seed):
    if species=='bilberry':
        for j in range(3):
            a=math.tau*j/3;r=Vector((math.cos(a),math.sin(a),0));base=r*.06
            mid=r*.12+Vector((0,0,h*.45));top=r*.25+Vector((0,0,h*.87))
            p.stem(base,mid,.008,'grass');p.stem(mid,top,.007,'grass')
            p.crown(top-Vector((0,0,.08)),(.18,.17,.12),'leaf_dark',seed+j,2,5)
            for k in range(4):
                anchor=mid.lerp(top,k/4);side=Vector((-r.y,r.x,0))
                p.leaf(anchor,anchor+side*(-1 if k%2 else 1)*.12+r*.03+Vector((0,0,.025)),.035,'leaf')
            p.berry(top+Vector((0,-.12,-.035)),.014,'flint')
        return
    if species=='dog_rose':
        for j in range(3):
            a=math.tau*j/3;r=Vector((math.cos(a),math.sin(a),0))
            pts=[r*.04,r*.24+Vector((0,0,h*.65)),r*.57+Vector((0,0,h)),r*.86+Vector((0,0,h*.65))]
            for k in range(3):p.stem(pts[k],pts[k+1],.014,'bark')
            for k in range(3):pinnate(p,pts[1].lerp(pts[2],k*.32),pts[1].lerp(pts[2],k*.32)+Vector((-r.y*.24,r.x*.24,.12)),2,.028)
            p.bloom(pts[2],.065,'skin_1',5,True)
            p.fruit.append(c.blob(p.name+'_hip',pts[3],(.024,.024,.042),'madder',seed+j,2,4,p.wind,0))
            p.body.append(star(p.name+'_thorns',pts[1],.046,'bark_dark',p.wind,3,True))
        return
    if species=='juniper':
        for j in range(4):
            a=math.tau*j/4;r=Vector((math.cos(a),math.sin(a),0));top=r*.22+Vector((0,0,h*(.66+j*.06)))
            p.stem(r*.03,top,.019,'bark_dark')
            p.crown(top-Vector((0,0,h*.18)),(.30,.28,h*.43),'pine_dark' if j%2 else 'pine',seed+j,4,5)
            for k in range(3):
                q=top-Vector((0,0,k*h*.13));p.leaf(q,q+r*.12+Vector((0,0,.08)),.012,'leaf_light')
            if j<3:p.berry(top+Vector((0,-.25,-.12)),.018,'granite_dark')
        return
    trunks=4 if species in ('hawthorn','blackthorn','crab_apple') else 3
    for j in range(trunks):
        a=math.tau*j/trunks+p.rng.uniform(-.15,.15);r=Vector((math.cos(a),math.sin(a),0))
        top=r*.62+Vector((0,0,h*p.rng.uniform(.68,.79)))
        p.stem(r*.08,top,.035,'bark_dark' if species=='blackthorn' else 'bark',4)
        p.crown(top,(.67,.61,h*.22),'leaf_dark' if j%2 else 'leaf',seed+j,4,5)
        if species=='elder':
            anchor=top+Vector((0,-.49,0))
            pinnate(p,anchor,anchor+Vector((.42,0,.18)),2,.048)
            p.leaf(anchor+Vector((.35,0,.15)),anchor+Vector((.50,0,.18)),.048,'leaf')
        else:
            for sign in (-1,1):
                anchor=top+Vector((sign*.18,-.49,-.1));end=anchor+Vector((sign*.16,-.06,.13))
                p.leaf(anchor,end,.095 if species=='hawthorn' else .06,'leaf','lobed' if species=='hawthorn' else 'entire')
        if species=='crab_apple':
            point=top+Vector((0,-.64,-.18));p.berry(point,.040,'madder' if j%2 else 'sand')
        elif species=='hawthorn':
            point=top+Vector((0,-.64,-.16));p.berry(point,.024,'madder')
        else:
            point=top+Vector((0,-.64,-.16));p.berry(point,.020,'flint')
        p.twig(top+Vector((0,-.4,-.04)),point)
        if species=='elder':
            for sign in (-1,1):p.berry(point+Vector((sign*.035,-.004,-.014)),.017,'flint')
        if species=='elder':
            p.bloom(top+Vector((.35,0,.23)),.055,'linen',6)
        elif species in ('hawthorn','blackthorn'):
            p.bloom(top+Vector((.3,0,.1)),.04,'linen',5)
            q=top+Vector((0,-.6,-.2))
            p.body.append(c.mesh(p.name+'_thorn',[q+Vector((-.008,0,0)),q+Vector((.008,0,0)),q+Vector((.035,-.02,.065))],[(0,1,2)],'bark_dark',p.wind))


def herbs(p,species,h,seed):
    rng=p.rng
    if species in ('field_mushroom','death_cap','fly_agaric'):
        p.wind=lambda v:0  # Rigid fruitbodies; stems and caps must not separate in wind.
        for j in range(3):
            a=math.tau*j/3+rng.uniform(-.18,.18);radius=(.22 if j else .02)*rng.uniform(.88,1.12)
            mushroom(p,(radius*math.cos(a),radius*math.sin(a),0),species,(.83,1,1.14)[j]*rng.uniform(.94,1.06)*h/.18)
        return
    if species in ('wild_carrot','hemlock','water_hemlock'):
        for j in range(2):umbel(p,(-.18+j*.36,.12*j,0),h*(1-.14*j),species)
        return
    if species=='sphagnum':
        for j in range(3):
            a=math.tau*j/3;point=Vector((.2*math.cos(a),.2*math.sin(a),.035))
            p.crown(point,(.24,.24,.035),'moss',seed+j,3,5)
        for j in range(12):
            a=j*2.4;r=.32*math.sqrt((j+1)/12);point=Vector((r*math.cos(a),r*math.sin(a),.065))
            p.body.append(star(p.name+'_capitulum',point,.048,'leaf_light' if j%3==0 else 'moss',p.wind,6))
        return
    if species in ('ramsons','lily_of_the_valley'):
        for j in range(3 if species=='ramsons' else 2):
            base=Vector((-.18+j*.18,.10*(j%2),0));rosette(p,base,h,3,.067,'leaf')
            top=base+Vector((0,0,h*.93))
            if species=='ramsons':
                p.stem(base,top,.005)
                for k in range(3):
                    a=k*math.tau/3;p.bloom(top+Vector((.035*math.cos(a),.035*math.sin(a),.01)),.026,'linen',6)
            else:
                mid=base+Vector((.01,0,h*.95));tip=base+Vector((.16,0,h*.72))
                p.stem(base,mid,.004);p.stem(mid,tip,.003)
                for k in range(5):
                    q=mid.lerp(tip,.12+k*.18)+Vector((0,-.015,0));p.bell(q,.018,.030,'linen')
        return
    if species in ('comfrey','foxglove'):
        for j in range(2):
            base=Vector((-.17+j*.34,.1*j,0));rosette(p,base,h*.34,5,.079,'leaf_dark')
            top=base+Vector((0,0,h*(1-.2*j)));p.stem(base,top,.009)
            if species=='comfrey':
                for k in range(3):
                    anchor=base.lerp(top,.28+k*.18);q=anchor+Vector(((-1 if k%2 else 1)*.20,0,.04));p.leaf(anchor,q,.076,'leaf_dark')
                    hair=q+Vector((0,0,.016));p.body.append(star(p.name+'_leaf_hairs',hair,.021,'leaf_light',p.wind,3,True))
                tip=top+Vector((.10,0,-.065));p.stem(top,tip,.004)
                p.bell(tip,.024,.050,'woad');p.bell(tip+Vector((.05,0,-.02)),.023,.050,'linen')
            else:
                for k in range(7):
                    q=base+Vector(((-1 if k%2 else 1)*.027,0,h*(.45+k*.065)*(1-.2*j)))
                    p.bell(q,.027,.067,'woad' if k%3 else 'madder')
        return
    if species=='deadly_nightshade':
        for j in range(2):
            base=Vector((-.21+j*.42,.12*j,0));top=base+Vector((0,0,h*(1-.12*j)));p.stem(base,top,.010)
            for k in range(3):
                anchor=base.lerp(top,.25+k*.22)
                for sign in (-1,1):
                    end=anchor+Vector((sign*.22,.045,.12));p.leaf(anchor,end,.068,'leaf_dark')
                q=anchor+Vector((0,-.07,.035));p.berry(q,.018,'flint')
                # Persistent five-lobed calyx extends past the dark berry.
                p.body.append(star(p.name+'_star_calyx',q+Vector((0,0,.019)),.039,'leaf',p.wind,5,False,.38))
            p.bell(top+Vector((.045,0,-.03)),.023,.050,'madder')
        return
    if species in ('plantain','sea_beet','wild_cabbage','sorrel'):
        for j in range(2):
            base=Vector((-.18+j*.36,.12*j,0))
            if species=='sorrel':
                for k in range(5):
                    a=math.tau*k/5;end=base+Vector((.22*math.cos(a),.22*math.sin(a),.16))
                    p.leaf(base,end,.051,'leaf','arrow')
            else:rosette(p,base,h*.6,6,.075 if species=='plantain' else .105,'pine' if species=='wild_cabbage' else 'leaf','toothed' if species=='sea_beet' else ('round' if species=='wild_cabbage' else 'entire'))
            top=base+Vector((0,0,h));p.stem(base,top,.007)
            if species=='plantain':
                p.fruit.append(c.tube(p.name+'_seed_spike',top-Vector((0,0,.12)),top,.019,.012,'bark',5,p.wind))
            elif species=='wild_cabbage':
                for k in range(3):p.bloom(top+Vector((.035*k,0,-.04*k)),.028,'straw',4)
            else:
                for k in range(4):
                    q=top-Vector((0,0,.065*k));p.flowers.append(berry(p.name+'_small_seed',q,.012,'madder' if species=='sorrel' else 'moss',p.wind))
        return
    if species in ('wild_pea','wild_hops'):
        for j in range(2):
            base=Vector((-.17+j*.34,0,0));pts=[base,base+Vector((.12,.03,h*.34)),base+Vector((-.06,.10,h*.68)),base+Vector((.16,.04,h))]
            for k in range(3):p.stem(pts[k],pts[k+1],.007)
            for k in range(3):
                anchor=pts[1].lerp(pts[3],k/3);q=anchor+Vector(((-1 if k%2 else 1)*.17,0,.04))
                p.leaf(anchor,q,.065 if species=='wild_hops' else .03,'leaf','lobed' if species=='wild_hops' else 'entire')
            if species=='wild_hops':
                for k in range(3):p.fruit.append(c.blob(p.name+'_hop_cone',pts[2]+Vector((.10*k,-.08,-.06*k)),(.03,.03,.06),'leaf_light',seed+j+k,2,4,p.wind,0))
            else:
                for k in range(2):
                    q=pts[2]+Vector((.1*k,-.08,-.04*k));p.fruit.append(c.blob(p.name+'_pea_pod',q,(.022,.018,.085),'grass',seed+j+k,2,4,p.wind,0))
                    p.bloom(q+Vector((0,0,.08)),.041,'skin_2',5,True)
        return
    if species=='wild_strawberry':
        for j in range(3):
            a=math.tau*j/3;base=Vector((.17*math.cos(a),.17*math.sin(a),0));top=base+Vector((0,0,h*.58));p.stem(base,top,.004)
            for k in range(3):
                angle=a+(k-1)*.7;end=top+Vector((.12*math.cos(angle),.12*math.sin(angle),.025));p.leaf(top,end,.048,'leaf','toothed')
            flower=base+Vector((.03,0,h));p.stem(base,flower,.004);p.bloom(flower,.03,'linen',5)
            p.fruit.append(c.blob(p.name+'_red_strawberry',top+Vector((0,-.045,-.015)),(.016,.016,.026),'madder',seed+j,2,4,p.wind,0))
        return
    if species=='wild_oats':
        for j in range(3):
            base=Vector((-.15+j*.15,.07*(j%2),0));top=base+Vector((.03,0,h*(1-.08*j)));p.stem(base,top,.006)
            for k in range(2):
                q=base.lerp(top,.25+k*.24);p.leaf(q,q+Vector(((-1 if k%2 else 1)*.28,0,.18)),.018,'grass')
            for k in range(3):
                q=top-Vector((0,0,k*.10));end=q+Vector(((-1 if k%2 else 1)*.14,0,-.07));p.stem(q,end,.003)
                p.fruit.append(c.blob(p.name+'_oat_spikelet',end,(.015,.012,.045),'sand',seed+j+k,2,3,p.wind,0))
        return
    if species in ('nettle','water_mint'):
        for j in range(2):
            base=Vector((-.18+j*.36,.1*j,0));top=base+Vector((0,0,h*(1-.13*j)));p.stem(base,top,.008)
            for k in range(3):
                anchor=base.lerp(top,.25+k*.22)
                for sign in (-1,1):p.leaf(anchor,anchor+Vector((sign*.18,0,.05)),.064,'leaf','toothed')
            if species=='water_mint':
                for k in range(4):
                    a=math.tau*k/4;p.bloom(top+Vector((.018*math.cos(a),.018*math.sin(a),-.04)),.025,'skin_1',6)
            else:
                for k in range(2):p.body.append(star(p.name+'_stings',base.lerp(top,.32+k*.27),.03,'leaf_light',p.wind,3,True))
                p.flowers.append(c.blob(p.name+'_green_catkin',top-Vector((0,0,.13)),(.09,.02,.025),'moss',seed+j,2,3,p.wind,0))
        return
    # Distinct loose flower architectures for the remaining eight herb families.
    low=species in ('wild_thyme','wild_flax')
    for j in range(3):
        base=Vector((-.16+j*.16,.08*(j%2),0));top=base+Vector((0,0,h*(1-.10*j)));p.stem(base,top,.004 if low else .007)
        if species in ('yarrow','valerian'):
            for k in range(2):
                anchor=base.lerp(top,.24+k*.26);pinnate(p,anchor,anchor+Vector(((-1 if k%2 else 1)*.18,0,.10)),2,.018 if species=='yarrow' else .034)
        else:
            for k in range(3):
                anchor=base.lerp(top,.18+k*.22);p.leaf(anchor,anchor+Vector(((-1 if k%2 else 1)*(.09 if low else .18),0,.035)),.016 if low else .052,'leaf')
        if species=='meadowsweet':
            for k in range(4):
                point=top+Vector((math.cos(k*2.4)*.065,math.sin(k*2.4)*.065,.015*k))
                p.flowers.append(c.blob(p.name+'_airy_plume',point,(.040,.038,.046),'linen',seed+j+k,2,4,p.wind,.12))
        elif species in ('yarrow','valerian'):
            for k in range(3):
                a=math.tau*k/3;point=top+Vector((.045*math.cos(a),.045*math.sin(a),.008));p.stem(top,point,.002)
                p.bloom(point,.043,'linen' if species=='yarrow' else 'skin_1',6)
        elif species=='feverfew':
            p.bloom(top,.060,'linen',8);p.flowers.append(star(p.name+'_gold_disc',top+Vector((0,0,.004)),.017,'straw',p.wind,6))
        else:p.bloom(top,.035,'woad' if species=='wild_flax' else 'skin_2',5)


HEIGHTS={'hawthorn':2.4,'blackthorn':2.1,'elder':2.7,'crab_apple':3.0,'juniper':1.1,'dog_rose':1.0,'bilberry':.35,
         'yarrow':.58,'plantain':.34,'comfrey':.68,'ramsons':.29,'feverfew':.48,'meadowsweet':.90,'wild_thyme':.16,'sphagnum':.09,
         'valerian':.95,'water_mint':.54,'nettle':.70,'sorrel':.49,'wild_carrot':.67,'hemlock':1.52,'water_hemlock':1.05,
         'foxglove':1.20,'lily_of_the_valley':.30,'deadly_nightshade':.92,'field_mushroom':.18,'death_cap':.18,'fly_agaric':.18,
         'wild_oats':.82,'sea_beet':.49,'wild_cabbage':.45,'wild_pea':.68,'wild_flax':.46,'wild_hops':.82,'wild_strawberry':.18}


def generate(params):
    species=params.get('species','wild_carrot');variant=params.get('variant','a')
    if species not in ALL or variant not in ('a','b'):raise ValueError('unknown P1 species/variant')
    index=ALL.index(species);vi=ord(variant)-97;seed=int(params.get('seed',7101+index*100+vi))
    rng=random.Random(seed);h=HEIGHTS[species]*(1.12 if vi else 1)
    name=('bush_' if species in BUSHES else 'plant_')+species+'_'+variant
    p=Plant(name,h,rng)
    if species in BUSHES:bush(p,species,h,seed)
    else:herbs(p,species,h,seed)
    return p.finish()


if __name__=='__main__':
    import fsart,json
    reports=[]
    for species in ALL:
        for variant in ('a','b'):
            fsart.reset();objects=generate({'species':species,'variant':variant})
            tris=sum(fsart.triangles(o) for o in objects)
            low=min(v.co.z for o in objects for v in o.data.vertices);high=max(v.co.z for o in objects for v in o.data.vertices)
            report={'species':species,'variant':variant,'tris':tris,'height_m':round(high-low,4),'min_z':round(low,5)}
            reports.append(report);fsart.result(**report)
    (pathlib.Path(__file__).parent/'preparation.json').write_text(json.dumps({'assets':reports},indent=2)+'\n')
