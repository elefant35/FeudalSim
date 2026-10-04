"""Compare sampled motion against a Git revision, resolving local LFS pointers.
Usage: python3 art/generators/animations/compare_committed.py [baseline-revision]
"""
import json,struct,subprocess,pathlib,sys
REF=sys.argv[1] if len(sys.argv)>1 else 'HEAD'
r={};gitdir=pathlib.Path(subprocess.check_output(['git','rev-parse','--git-common-dir'],text=True).strip())
def clips(raw):
 if raw.startswith(b'version https://git-lfs'):
  sha=raw.decode().split('oid sha256:')[1].split()[0];raw=(gitdir/'lfs/objects'/sha[:2]/sha[2:4]/sha).read_bytes()
 n=struct.unpack_from('<I',raw,12)[0];j=json.loads(raw[20:20+n]);blob=raw[n+28:]
 def acc(i):
  a=j['accessors'][i];v=j['bufferViews'][a['bufferView']];w={'SCALAR':1,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']];fmt={5126:'f',5123:'H',5125:'I'}[a['componentType']];size=v.get('byteStride',struct.calcsize(fmt)*w);o=v.get('byteOffset',0)+a.get('byteOffset',0)
  return [struct.unpack_from('<'+fmt*w,blob,o+k*size) for k in range(a['count'])]
 out={}
 for a in j['animations']:
  data={}
  for ch in a['channels']:
   s=a['samplers'][ch['sampler']];data[(j['nodes'][ch['target']['node']]['name'],ch['target']['path'])]=(acc(s['input']),acc(s['output']))
  out[a['name']]=data
 return out
for family in ('locomotion','rest','social','needs','work'):
 path='game/assets/characters/anims/'+family+'.glb';old=clips(subprocess.check_output(['git','show',REF+':'+path]));new=clips(pathlib.Path(path).read_bytes());maxerr=0
 for name,channels in old.items():
  assert name in new
  for key,values in channels.items():
   assert key in new[name]
   for aa,bb in zip(values,new[name][key]):
    assert len(aa)==len(bb),(name,key)
    maxerr=max(maxerr,max((abs(a-b) for va,vb in zip(aa,bb) for a,b in zip(va,vb)),default=0))
 assert maxerr<1e-6,(family,maxerr)
 r[family]={'committed_clips_retained':len(old),'max_key_component_delta':maxerr}
p=pathlib.Path('art/previews/animations/p0_preservation.json');p.write_text(json.dumps(r,indent=2)+'\n');print(r)
