"""Review every P1 outer layer, all six heads/sex and child motion."""
import bpy,pathlib,sys,runpy,importlib.util,io,contextlib,json,math,numpy as np
from mathutils import Vector
ROOT=pathlib.Path(__file__).resolve().parents[3];sys.path.insert(0,str(ROOT/'tools/art'));import fsart
out=ROOT/'art/previews/settlers'
spec=importlib.util.spec_from_file_location('artcheck',ROOT/'tools/art/check.py');check=importlib.util.module_from_spec(spec);spec.loader.exec_module(check)
audit={}
for sex in ['male','female','child']:
 asset=ROOT/f'game/assets/characters/body_{sex}.glb';sys.argv=['check','--',str(asset),'character'];buf=io.StringIO()
 with contextlib.redirect_stdout(buf):check.main()
 audit[sex]=json.loads(next(line[7:] for line in buf.getvalue().splitlines() if line.startswith('RESULT ')))
 sys.argv=['review','--',sex];runpy.run_path(str(ROOT/'art/generators/settlers/review.py'))
 for outer in range(4):
  sys.argv=['preview','--',str(asset),str(out/f'p1_{sex}_outer{outer}'),'--outercloak',str(outer),'--hair','2'];runpy.run_path(str(ROOT/'tools/art/preview.py'))
(ROOT/'art/generators/settlers/audit_p1.json').write_text(json.dumps(audit,indent=2)+'\n')
# Child proportions and movement on its own rest rig; first pose is unchanged A-pose.
fsart.reset();sys.path.insert(0,str(ROOT/'art/generators/settlers'));import p1
objs=p1.generate({'sex':'child'});rig=objs[0]
for ob in objs:
 if ob.type=='MESH':
  ob.hide_render=(ob.name.startswith(('Head_','Hair_','Beard_')) and ob.name not in ('Head_child_a','Hair_cropped')) or ob.name in ['Cloth_fur_cloak','Cloth_oiled_cloak','Cloth_sailcloth_poncho']
sys.path.insert(0,str(ROOT/'art/generators/animations'));import human_motion
from skeleton import create_skeleton
adult_rig=create_skeleton()
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=400;scene.render.resolution_y=600;scene.render.resolution_percentage=100;scene.view_settings.view_transform='Standard'
world=bpy.data.worlds.new('qa');world.use_nodes=True;scene.world=world;world.node_tree.nodes['Background'].inputs['Strength'].default_value=.75
ld=bpy.data.lights.new('sun','SUN');ld.energy=2;lo=bpy.data.objects.new('sun',ld);scene.collection.objects.link(lo);lo.rotation_euler=(.5,-.4,-.3)
cam=bpy.data.objects.new('cam',bpy.data.cameras.new('cam'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=1.65;cam.location=(1.7,-2.2,1.3);cam.rotation_euler=(Vector((0,0,.62))-cam.location).to_track_quat('-Z','Y').to_euler()
canvas=np.zeros((600,1600,4),np.float32)
for i,(name,t) in enumerate([('idle',.0),('walk',.1),('walk',.6),('sit_ground_loop',.5)]):
 human_motion.pose(adult_rig,name,t)
 for pb in rig.pose.bones:
  source=adult_rig.pose.bones[pb.name];pb.rotation_mode='QUATERNION';pb.rotation_quaternion=source.rotation_quaternion;pb.location=source.location*.68
 bpy.context.view_layer.update()
 floor=min(rig.pose.bones[n].head.z for n in ['LeftFoot','RightFoot'])
 rig.pose.bones['Hips'].location+=rig.pose.bones['Hips'].bone.matrix_local.to_3x3().inverted()@Vector((0,0,.053-floor))
 bpy.context.view_layer.update();tmp=out/'_child_pose.png';scene.render.filepath=str(tmp);bpy.ops.render.render(write_still=True);im=bpy.data.images.load(str(tmp),check_existing=False);a=np.empty(400*600*4,np.float32);im.pixels.foreach_get(a);canvas[:,i*400:(i+1)*400]=a.reshape(600,400,4);tmp.unlink()
im=bpy.data.images.new('child_motion',1600,600);im.pixels.foreach_set(canvas.ravel());im.filepath_raw=str(out/'child_motion.png');im.file_format='PNG';im.save()
fsart.result(ok=all(a['ok'] for a in audit.values()),audit='art/generators/settlers/audit_p1.json')
