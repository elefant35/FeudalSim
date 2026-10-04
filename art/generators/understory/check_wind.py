#!/usr/bin/env python3
"""Verify the exported P0.6 vertex-color wind channel, separate from mesh budgets."""
import json
from pathlib import Path
import struct
import numpy as np

ROOT=Path(__file__).resolve().parents[3]
SPEC={"hazel":3,"bramble":2,"gorse":2,"reeds":3,"grass_tuft":4,"fern":3}
CT={5121:(np.uint8,255),5123:(np.uint16,65535),5126:(np.float32,1)}


def run():
    result=[]
    for species,count in SPEC.items():
        folder="bushes" if species in ("hazel","bramble","gorse") else "plants"
        for i in range(count):
            path=ROOT/f"game/assets/flora/{folder}/{species}_{chr(97+i)}.glb"
            raw=path.read_bytes(); size,kind=struct.unpack_from('<II',raw,12)
            assert kind==0x4e4f534a
            doc=json.loads(raw[20:20+size]); bin_size,bin_kind=struct.unpack_from('<II',raw,20+size)
            assert bin_kind==0x004e4942
            binary=raw[28+size:28+size+bin_size]; values=[]
            for mesh in doc['meshes']:
                for primitive in mesh['primitives']:
                    assert 'COLOR_0' in primitive['attributes'],str(path)+' missing COLOR_0'
                    ac=doc['accessors'][primitive['attributes']['COLOR_0']]
                    view=doc['bufferViews'][ac['bufferView']]
                    dtype,divisor=CT[ac['componentType']]; nbytes=np.dtype(dtype).itemsize
                    stride=view.get('byteStride',4*nbytes)
                    offset=view.get('byteOffset',0)+ac.get('byteOffset',0)
                    arr=np.ndarray((ac['count'],4),dtype=dtype,buffer=binary,offset=offset,strides=(stride,nbytes)).astype(float)
                    if ac.get('normalized'):arr/=divisor
                    assert np.all(np.abs(arr[:,1:3])<.0001),str(path)+' wind leaking into G/B'
                    assert np.all((arr[:,0]>=0)&(arr[:,0]<=1)),str(path)+' wind outside unit range'
                    values.append(arr[:,0])
            all_values=np.concatenate(values)
            assert all_values.min()<.03 and all_values.max()>.85,str(path)+' missing anchor/tip gradient'
            result.append({'asset':str(path.relative_to(ROOT)), 'wind_min':round(float(all_values.min()),5),
                           'wind_max':round(float(all_values.max()),5),'wind_vertices':len(all_values)})
    out=Path(__file__).parent/'wind_checks.json'
    out.write_text(json.dumps({'result':'pass','assets':result},indent=2)+'\n')
    print(f'PASS: wind COLOR_0 on {len(result)} exported assets; ground anchors and weighted tips')


if __name__=='__main__':run()
