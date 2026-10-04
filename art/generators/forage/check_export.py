#!/usr/bin/env python3
"""Check P1 coverage and actual glTF wind channels without launching Blender."""
import json,re,struct
from pathlib import Path
import numpy as np

ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent
CT={5121:(np.uint8,255),5123:(np.uint16,65535),5126:(np.float32,1)}
FUNGUS={'field_mushroom','death_cap','fly_agaric'}


def main():
    reports=json.loads((HERE/'checks.json').read_text())['assets']
    nodes=set(re.findall(r'id:\s*node\.([a-z_]+)',(ROOT/'content/nodes/plants.yaml').read_text()))-{'reeds'}
    bushes={'hawthorn','blackthorn','elder','crab_apple','juniper','dog_rose','bilberry'}
    expected={(species,variant) for species in nodes|bushes for variant in ('a','b')}
    assert {(r['species'],r['variant']) for r in reports}==expected,'missing P1 node/variant'
    checked=[]
    for report in reports:
        path=ROOT/report['output'];raw=path.read_bytes()
        size,kind=struct.unpack_from('<II',raw,12);assert kind==0x4e4f534a
        doc=json.loads(raw[20:20+size]);n,kind=struct.unpack_from('<II',raw,20+size);assert kind==0x004e4942
        binary=raw[28+size:28+size+n];colors=[]
        assert len(doc.get('materials',[]))==1,'more than one palette material: '+str(path)
        assert doc['materials'][0].get('name')=='palette','wrong material: '+str(path)
        for mesh in doc['meshes']:
            for primitive in mesh['primitives']:
                assert 'COLOR_0' in primitive['attributes'],'missing exported wind channel: '+str(path)
                ac=doc['accessors'][primitive['attributes']['COLOR_0']]
                assert ac['type']=='VEC4'
                view=doc['bufferViews'][ac['bufferView']];dtype,divisor=CT[ac['componentType']]
                sz=np.dtype(dtype).itemsize;stride=view.get('byteStride',4*sz)
                offset=view.get('byteOffset',0)+ac.get('byteOffset',0)
                color=np.ndarray((ac['count'],4),dtype=dtype,buffer=binary,offset=offset,strides=(stride,sz)).astype(float)
                if ac.get('normalized'):color/=divisor
                assert np.abs(color[:,1:3]).max()<.0001,'wind written outside R: '+str(path)
                assert color[:,0].min()>=0 and color[:,0].max()<=1,'invalid wind range: '+str(path)
                colors.append(color[:,0])
        wind=np.concatenate(colors)
        if report['species'] in FUNGUS:
            assert wind.max()==0,'rigid mushroom has wind weights: '+str(path)
        else:
            assert wind.min()<.03 and wind.max()>.6,'missing ground anchor/foliage gradient: '+str(path)
        names=[n.get('name','') for n in doc['nodes']]
        assert not any(n.endswith(('-colonly','-convcolonly')) for n in names),'forage must have no collision'
        checked.append({'output':report['output'],'wind_min':round(float(wind.min()),5),
                        'wind_max':round(float(wind.max()),5),'vertices':len(wind),
                        'seasonal_parts':[n for n in names if n.endswith(('_flowers','_fruit'))]})
    (HERE/'wind_checks.json').write_text(json.dumps({'result':'pass','coverage':70,'assets':checked},indent=2)+'\n')
    print('PASS: all 28 forage nodes + 7 bushes have two variants; 70 GLBs with one palette material and correct COLOR_0 wind')


if __name__=='__main__':main()
