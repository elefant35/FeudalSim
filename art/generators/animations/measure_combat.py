"""Audit nine P2 combat GLB actions without Blender. Original P0 auditor remains available."""
import pathlib,json,struct,math
ROOT=pathlib.Path(__file__).resolve().parents[3]
EXPECTED={'combat_basic':'unarmed_ready punch_a punch_b block shove hit_react_front stagger downed_loop death_a'}
ONESHOTS={'punch_a','punch_b','block','shove','hit_react_front','stagger','death_a'}
report={}
for family,text in EXPECTED.items():
 path=ROOT/'game/assets/characters/anims'/f'{family}.glb';raw=path.read_bytes();n=struct.unpack_from('<I',raw,12)[0];j=json.loads(raw[20:20+n]);blob=raw[n+28:]
 def acc(i):
  a=j['accessors'][i];v=j['bufferViews'][a['bufferView']];w={'SCALAR':1,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']];fmt={5126:'f',5123:'H',5125:'I'}[a['componentType']];st=v.get('byteStride',w*struct.calcsize(fmt));o=v.get('byteOffset',0)+a.get('byteOffset',0)
  return [struct.unpack_from('<'+fmt*w,blob,o+k*st) for k in range(a['count'])]
 assert sorted(text.split())==sorted(a['name'] for a in j['animations']);assert len(j['nodes'])==41
 clips={}
 for a in j['animations']:
  length=0;closure=0;hipmin=10;hipmax=-10
  for ch in a['channels']:
   sampler=a['samplers'][ch['sampler']];times=acc(sampler['input']);vals=acc(sampler['output']);length=max(length,max(t[0] for t in times));node=j['nodes'][ch['target']['node']]['name'];kind=ch['target']['path']
   assert all(abs(t[0]*30-round(t[0]*30))<2e-5 for t in times)
   if kind=='translation':
    if node=='Root':assert max(math.dist(vals[0],v) for v in vals)<1e-6
    if node=='Hips':
     assert max(abs(v[k]-vals[0][k]) for v in vals for k in (0,2))<1e-6;hipmin=min(hipmin,min(v[1] for v in vals));hipmax=max(hipmax,max(v[1] for v in vals))
   if a['name'] not in ONESHOTS:
    error=math.dist(vals[0],vals[-1]);error=min(error,math.dist(vals[0],[-x for x in vals[-1]])) if kind=='rotation' else error;closure=max(closure,error)
  if a['name'] not in ONESHOTS:assert closure<1e-5,(a['name'],closure)
  clips[a['name']]={'duration_s':round(length,6),'loop_end_error':closure,'hips_gltf_y_range_m':[round(hipmin,6),round(hipmax,6)]}
 report[family]={'clips':clips,'bones':40,'tris':0,'materials':0,'bytes':path.stat().st_size}
p=ROOT/'art/previews/animations/measurements_p2.json';p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({'ok':True,'clips':sum(len(f['clips']) for f in report.values()),'report':str(p)}))
