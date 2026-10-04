"""Audit delivered GLB clips: exact names,30fps sampling, loop closure, root motion."""
import json,pathlib,struct,math
ROOT=pathlib.Path(__file__).resolve().parents[3]
EXPECTED={'locomotion':['idle','idle_alt','walk','jog','sprint','turn_left','turn_right'],'rest':['sit_ground_down','sit_ground_loop','sit_ground_up','lie_down','sleep_loop','lie_up','warm_hands_loop'],'social':['talk_a','talk_b','talk_c','nod','shake_head','point','wave'],'needs':['eat_loop','drink_kneel_loop'],'work':['chop_loop','pick_up','forage_pick_loop','knap_loop']}
LOOPS={'idle','idle_alt','walk','jog','sprint','sit_ground_loop','sleep_loop','warm_hands_loop','talk_a','talk_b','talk_c','eat_loop','drink_kneel_loop','chop_loop','forage_pick_loop','knap_loop'}
def read(path):
 raw=path.read_bytes();jsize=struct.unpack_from('<I',raw,12)[0];j=json.loads(raw[20:20+jsize]);blob=raw[28+jsize:]
 def accessor(idx):
  a=j['accessors'][idx];bv=j['bufferViews'][a['bufferView']];width={'SCALAR':1,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
  fmt={5126:'f',5123:'H',5125:'I'}[a['componentType']];size=struct.calcsize(fmt)*width;offset=bv.get('byteOffset',0)+a.get('byteOffset',0);stride=bv.get('byteStride',size)
  return [struct.unpack_from('<'+fmt*width,blob,offset+i*stride) for i in range(a['count'])]
 return j,accessor
report={}
for family,expected in EXPECTED.items():
 path=ROOT/'game/assets/characters/anims'/f'{family}.glb';j,acc=read(path)
 names=[a['name'] for a in j['animations']];assert sorted(names)==sorted(expected),(family,names)
 assert len(j['nodes'])==41
 measured={}
 for clip in j['animations']:
  duration=0;maxgap=0;closure=0;hip_min=10;hip_max=-10
  for ch in clip['channels']:
   target=ch['target'];name=j['nodes'][target['node']]['name'];s=clip['samplers'][ch['sampler']];times=[x[0] for x in acc(s['input'])];values=acc(s['output'])
   duration=max(duration,max(times));maxgap=max(maxgap,max((b-a for a,b in zip(times,times[1:])),default=0))
   if target['path']=='translation':
    if name=='Root':assert max(math.dist(values[0],v) for v in values)<1e-6
    if name=='Hips':
     assert max(abs(v[k]-values[0][k]) for v in values for k in (0,2))<1e-6
     hip_min=min(hip_min,min(v[1] for v in values));hip_max=max(hip_max,max(v[1] for v in values))
   if clip['name'] in LOOPS:
    if target['path']=='rotation':error=min(math.dist(values[0],values[-1]),math.dist(values[0],[-x for x in values[-1]]))
    else:error=math.dist(values[0],values[-1])
    closure=max(closure,error)
  assert all(abs(x[0]*30-round(x[0]*30))<2e-5 for s in clip['samplers'] for x in acc(s['input'])),clip['name']
  if clip['name'] in LOOPS: assert closure<1e-5,(clip['name'],closure)
  measured[clip['name']]={'duration_s':round(duration,6),'max_sample_gap_s':round(maxgap,8),'loop_end_error':round(closure,9),'hips_gltf_y_range_m':[round(hip_min,6),round(hip_max,6)]}
 report[family]={'bytes':path.stat().st_size,'bones':40,'clips':measured,'tris':0,'materials':0}
out=ROOT/'art/previews/animations/measurements.json';out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps({'ok':True,'clips':sum(len(x['clips']) for x in report.values()),'files':len(report),'report':str(out)}))
