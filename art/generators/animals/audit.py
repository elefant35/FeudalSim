"""Audit the delivered glTF skeleton and sampled channels, without Blender."""
import json,pathlib,struct,math,sys
ROOT=pathlib.Path(__file__).resolve().parents[3]
LOOPS={'idle','walk','run','flee','graze_loop','sleep_loop'}
def read(path):
 raw=path.read_bytes();size=struct.unpack_from('<I',raw,12)[0];j=json.loads(raw[20:20+size]);blob=raw[28+size:]
 def accessor(idx):
  a=j['accessors'][idx];view=j['bufferViews'][a['bufferView']];width={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
  fmt={5126:'f',5123:'H',5125:'I',5121:'B'}[a['componentType']];size=struct.calcsize(fmt)*width;offset=view.get('byteOffset',0)+a.get('byteOffset',0);stride=view.get('byteStride',size)
  return [struct.unpack_from('<'+fmt*width,blob,offset+i*stride) for i in range(a['count'])]
 return j,accessor

def audit(species):
 j,acc=read(ROOT/f'game/assets/animals/{species}.glb');joints=j['skins'][0]['joints'];bones=[j['nodes'][n]['name'] for n in joints]
 assert len(bones)<=35 and 'Root' in bones,(species,bones)
 node_root=next(n for n in j['nodes'] if n.get('name')=='Root');assert max(abs(v) for v in node_root.get('translation',[0,0,0]))<1e-6
 expected={'idle','walk','run','flee','graze_loop','alert','hit','death','sleep_loop'}
 if species in ('wild_boar','grey_wolf'):expected.add('attack')
 clips={c['name']:c for c in j.get('animations',[])};assert expected<=clips.keys(),(species,clips.keys())
 report={}
 for name,clip in clips.items():
  closure=0;duration=0;maxgap=0
  for ch in clip['channels']:
   sam=clip['samplers'][ch['sampler']];times=[t[0] for t in acc(sam['input'])];values=acc(sam['output']);node=j['nodes'][ch['target']['node']]['name'];kind=ch['target']['path']
   duration=max(duration,max(times));maxgap=max(maxgap,max((b-a for a,b in zip(times,times[1:])),default=0))
   assert all(abs(v*30-round(v*30))<3e-5 for v in times),(species,name,'sample rate')
   if node=='Root':assert max(math.dist(values[0],v) for v in values)<1e-6,(species,name,'Root moves')
   if node=='Pelvis' and kind=='translation':assert max(abs(v[k]-values[0][k]) for v in values for k in (0,2))<1e-6,(species,name,'Pelvis XY moves')
   if name in LOOPS:
    e=min(math.dist(values[0],values[-1]),math.dist(values[0],[-v for v in values[-1]])) if kind=='rotation' else math.dist(values[0],values[-1]);closure=max(closure,e)
  if name in LOOPS:assert closure<1e-5,(species,name,'loop',closure)
  report[name]={'duration_s':round(duration,4),'max_gap_s':round(maxgap,7),'loop_error':round(closure,8)}
 return {'ok':True,'bones':len(bones),'clips':report}
if __name__=='__main__':
 names=sys.argv[1:] or [p.stem for p in sorted((ROOT/'game/assets/animals').glob('*.glb'))];out={n:audit(n) for n in names}
 dest=ROOT/'art/generators/animals/motion_audit.json';old=json.loads(dest.read_text()) if dest.exists() else {};old.update(out);dest.write_text(json.dumps(old,indent=2)+'\n');print(json.dumps({'ok':True,'species':list(out)}))
