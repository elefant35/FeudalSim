"""Check and preview every exported wreck file; compose a waterline staging review."""
import pathlib,sys,json,importlib.util,runpy,io,contextlib
import bpy
from mathutils import Vector
ROOT=pathlib.Path(__file__).resolve().parents[3];sys.path.insert(0,str(ROOT/'tools/art'));import fsart
out=ROOT/'art/previews/wreck';out.mkdir(parents=True,exist_ok=True)
items=json.loads((ROOT/'art/generators/wreck/checks.json').read_text())
spec=importlib.util.spec_from_file_location('artcheck',ROOT/'tools/art/check.py');check=importlib.util.module_from_spec(spec);spec.loader.exec_module(check)
audit={}
for item,record in items.items():
 path=ROOT/f'game/assets/wreck/{item}.glb';sys.argv=['review','--',str(path),record['budget_class']]
 buf=io.StringIO()
 with contextlib.redirect_stdout(buf):ok=check.main()
 resultline=next(s[7:] for s in buf.getvalue().splitlines() if s.startswith('RESULT '));audit[item]=json.loads(resultline)
 print('CHECK',item,ok,resultline)
 sys.argv=['preview','--',str(path),str(out/item)];runpy.run_path(str(ROOT/'tools/art/preview.py'))
(ROOT/'art/generators/wreck/audit.json').write_text(json.dumps(audit,indent=2)+'\n')
# An assembled staging render shows the broken seam, coherent 22m profile, and sea-surface origin.
fsart.reset()
for item,y in [('hull_bow',-5.5),('hull_stern',5.5)]:
 prior=set(bpy.context.scene.objects);bpy.ops.import_scene.gltf(filepath=str(ROOT/f'game/assets/wreck/{item}.glb'))
 for ob in set(bpy.context.scene.objects)-prior:
  ob.location.y+=y;ob.hide_render=ob.name.endswith(('-colonly','-convcolonly'))
for mat in bpy.data.materials:
 if mat.use_nodes:
  tex=next((n for n in mat.node_tree.nodes if n.type=='TEX_IMAGE'),None);bsdf=next((n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
  if tex and bsdf:mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,0));water=bpy.context.object;water.name='QA_sea_surface';fsart.paint(water,'water')
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1400;scene.render.resolution_y=800;scene.render.resolution_percentage=100;scene.view_settings.view_transform='Standard'
world=bpy.data.worlds.new('overcast');world.use_nodes=True;scene.world=world;world.node_tree.nodes['Background'].inputs['Color'].default_value=(.50,.58,.63,1);world.node_tree.nodes['Background'].inputs['Strength'].default_value=.75
ld=bpy.data.lights.new('sun','SUN');ld.energy=2;lo=bpy.data.objects.new('sun',ld);scene.collection.objects.link(lo);lo.rotation_euler=(.5,-.4,-.3)
cam=bpy.data.objects.new('cam',bpy.data.cameras.new('cam'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=29;cam.location=(20,-20,15);cam.rotation_euler=(Vector((0,0,1))-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(out/'wending_star_waterline.png');bpy.ops.render.render(write_still=True)
fsart.result(ok=all(a['ok'] for a in audit.values()),audit='art/generators/wreck/audit.json')
