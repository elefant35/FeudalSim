"""Sample imported skinned mesh at three-frame intervals through every clip.
Reports surface contact and airborne gait phases. Tolerance is 5 mm for flat
floor penetration between Blender and the glTF skin matrices.
"""
import pathlib,sys,json,bpy
ROOT=pathlib.Path(__file__).resolve().parents[3];sys.path.insert(0,str(ROOT/'tools/art'));import fsart
names=fsart.args() or [p.stem for p in sorted((ROOT/'game/assets/animals').glob('*.glb'))];report={}
for name in names:
 fsart.reset();bpy.ops.import_scene.gltf(filepath=str(ROOT/f'game/assets/animals/{name}.glb'))
 arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE');shapes={p.custom_shape.name for p in arm.pose.bones if p.custom_shape};meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name not in shapes]
 for t in arm.animation_data.nla_tracks:t.mute=True
 results={}
 for track in arm.animation_data.nla_tracks:
  strip=track.strips[0];arm.animation_data.action=strip.action;arm.animation_data.action_slot=strip.action_slot;a,b=strip.action.frame_range
  samples=[]
  for frame in list(range(int(a),int(b)+1,3))+[int(b)]:
   bpy.context.scene.frame_set(frame);bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get();minz=100
   for ob in meshes:
    ev=ob.evaluated_get(deps);me=ev.to_mesh();minz=min(minz,min((ev.matrix_world@v.co).z for v in me.vertices));ev.to_mesh_clear()
   samples.append(minz)
  results[track.name]={'min_surface_z_m':round(min(samples),6),'max_lowest_surface_z_m':round(max(samples),6),'samples':len(samples)}
 report[name]={'ok':all(v['min_surface_z_m']>=-.005 for v in results.values()),'clips':results}
 print('GROUND',name,report[name]['ok'],min(v['min_surface_z_m'] for v in results.values()),flush=True)
path=ROOT/'art/generators/animals/ground_audit.json';path.write_text(json.dumps(report,indent=2)+'\n');fsart.result(ok=all(a['ok'] for a in report.values()),report=str(path))
