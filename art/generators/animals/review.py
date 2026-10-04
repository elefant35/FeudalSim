"""Imported-file contract checks, turntables, silhouettes and five rows of motion.
Rows: walk, run, idle/graze/alert/hit, death, sleep, attack (or graze).
"""
import pathlib,sys,runpy,importlib.util,json,io,contextlib,math
import bpy,numpy as np
from mathutils import Vector
ROOT=pathlib.Path(__file__).resolve().parents[3];sys.path.insert(0,str(ROOT/'tools/art'));import fsart
names=fsart.args() or ['red_deer_stag','red_deer_hind','wild_boar','hare','grey_wolf','brown_bear','fox','wild_goat']
spec=importlib.util.spec_from_file_location('artcheck',ROOT/'tools/art/check.py');check=importlib.util.module_from_spec(spec);spec.loader.exec_module(check)
out=ROOT/'art/previews/animals';out.mkdir(parents=True,exist_ok=True);results={}
for species in names:
 asset=ROOT/f'game/assets/animals/{species}.glb';cls='animal_small' if species=='hare' else 'animal_medium'
 sys.argv=['check','--',str(asset),cls];buf=io.StringIO()
 with contextlib.redirect_stdout(buf):check.main()
 results[species]=json.loads(next(s[7:] for s in buf.getvalue().splitlines() if s.startswith('RESULT ')))
 sys.argv=['preview','--',str(asset),str(out/species)];runpy.run_path(str(ROOT/'tools/art/preview.py'))
 fsart.reset();bpy.ops.import_scene.gltf(filepath=str(asset));arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
 bone_shapes={pb.custom_shape.name for pb in arm.pose.bones if pb.custom_shape}
 for ob in bpy.context.scene.objects:
  if ob.name in bone_shapes:ob.hide_render=True
 tracks={t.name:t for t in arm.animation_data.nla_tracks}
 for track in tracks.values():track.mute=True
 meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name not in bone_shapes]
 points=[ob.matrix_world@v.co for ob in meshes for v in ob.data.vertices];low=Vector(tuple(min(p[k] for p in points) for k in range(3)));high=Vector(tuple(max(p[k] for p in points) for k in range(3)));center=(low+high)*.5
 scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=320;scene.render.resolution_y=320;scene.render.resolution_percentage=100;scene.view_settings.view_transform='Standard'
 world=bpy.data.worlds.new('AnimalMotion');world.use_nodes=True;scene.world=world;world.node_tree.nodes['Background'].inputs['Color'].default_value=(.32,.37,.36,1);world.node_tree.nodes['Background'].inputs['Strength'].default_value=.75
 ld=bpy.data.lights.new('sun','SUN');ld.energy=2.4;lo=bpy.data.objects.new('sun',ld);scene.collection.objects.link(lo);lo.rotation_euler=(.5,-.4,-.3)
 bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.005));fsart.paint(bpy.context.object,'leaf_dark')
 cam=bpy.data.objects.new('cam',bpy.data.cameras.new('cam'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=max(high-low)*1.52;cam.location=center+Vector((3.7,-5,2.4));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
 canvas=np.ones((6*320,4*320,4),np.float32);samples=[('walk',t) for t in [0,.25,.5,.75]]+[('run',t) for t in [0,.25,.5,.75]]+[('idle',.0),('graze_loop',.25),('alert',.50),('hit',.50)]+[('death',t) for t in [0,.33,.66,1]]+[('sleep_loop',t) for t in [0,.25,.5,.75]]+[(('attack' if 'attack' in tracks else 'graze_loop'),t) for t in [0,.25,.5,.75]]
 ground=[]
 for i,(name,phase) in enumerate(samples):
  strip=tracks[name].strips[0];arm.animation_data.action=strip.action;arm.animation_data.action_slot=strip.action_slot
  a,b=strip.action.frame_range;scene.frame_set(round(a+(b-a)*phase));bpy.context.view_layer.update()
  deps=bpy.context.evaluated_depsgraph_get();minz=100
  for ob in meshes:
   ev=ob.evaluated_get(deps);me=ev.to_mesh();minz=min(minz,min((ev.matrix_world@v.co).z for v in me.vertices));ev.to_mesh_clear()
  ground.append({'clip':name,'phase':phase,'min_z':round(minz,5)})
  tmp=out/'_motion.png';scene.render.filepath=str(tmp);bpy.ops.render.render(write_still=True);im=bpy.data.images.load(str(tmp),check_existing=False);pixels=np.empty(320*320*4,np.float32);im.pixels.foreach_get(pixels);canvas[(5-i//4)*320:(6-i//4)*320,i%4*320:(i%4+1)*320]=pixels.reshape(320,320,4);bpy.data.images.remove(im);tmp.unlink()
 im=bpy.data.images.new(species+'_motion',1280,1920);im.pixels.foreach_set(canvas.ravel());im.filepath_raw=str(out/(species+'_motion.png'));im.file_format='PNG';im.save();results[species]['motion_floor_samples']=ground
 print('REVIEW',species,results[species]['ok'],flush=True)
path=ROOT/'art/generators/animals/audit.json';old=json.loads(path.read_text()) if path.exists() else {};old.update(results);path.write_text(json.dumps(old,indent=2)+'\n');fsart.result(ok=all(r['ok'] for r in results.values()),species=list(results))
