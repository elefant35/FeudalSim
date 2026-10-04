"""Render imported-library head/emotion/first-person evidence sheets, no edits to assets."""
import bpy,sys,pathlib,math,numpy as np
from mathutils import Vector
ROOT=pathlib.Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'tools/art'));import fsart
out=ROOT/'art/previews/settlers';out.mkdir(parents=True,exist_ok=True)
sex=sys.argv[-1]
fsart.reset();bpy.ops.import_scene.gltf(filepath=str(ROOT/f'game/assets/characters/body_{sex}.glb'))
for mat in bpy.data.materials:
 if mat.use_nodes:
  tex=next((n for n in mat.node_tree.nodes if n.type=='TEX_IMAGE'),None); bsdf=next((n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
  if tex and bsdf: mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=scene.render.resolution_y=384;scene.render.resolution_percentage=100;scene.view_settings.view_transform='Standard'
world=bpy.data.worlds.new('review');world.use_nodes=True;scene.world=world;world.node_tree.nodes['Background'].inputs['Color'].default_value=(.33,.40,.46,1);world.node_tree.nodes['Background'].inputs['Strength'].default_value=.7
ld=bpy.data.lights.new('key','AREA');ld.energy=120;ld.shape='DISK';ld.size=2;lo=bpy.data.objects.new('key',ld);scene.collection.objects.link(lo);lo.location=(-1,-2,2.7);lo.rotation_euler=(Vector((0,0,1.6))-lo.location).to_track_quat('-Z','Y').to_euler()
cam=bpy.data.objects.new('camera',bpy.data.cameras.new('camera'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=.37
scale=1 if sex=='male' else .72 if sex=='child' else 1.62/1.72
cam.location=(0,-1.2,1.63*scale);cam.rotation_euler=(Vector((0,0,1.615*scale))-cam.location).to_track_quat('-Z','Y').to_euler()
heads=sorted([o for o in scene.objects if o.name.startswith('Head_')],key=lambda o:o.name)
for o in scene.objects:
 if o.type=='MESH':o.hide_render=True
hair=bpy.data.objects.get('Hair_cropped');hair.hide_render=False

def render_tile(name):
 p=out/(sex+'_'+name+'.png');scene.render.filepath=str(p);bpy.ops.render.render(write_still=True)
 im=bpy.data.images.load(str(p));a=np.empty(384*384*4,np.float32);im.pixels.foreach_get(a);p.unlink();return a.reshape(384,384,4)
def sheet(name,tiles,cols):
 rows=math.ceil(len(tiles)/cols);can=np.zeros((rows*384,cols*384,4),np.float32)
 for i,t in enumerate(tiles):can[(rows-1-i//cols)*384:(rows-i//cols)*384,(i%cols)*384:(i%cols+1)*384]=t
 im=bpy.data.images.new(name,cols*384,rows*384);im.pixels.foreach_set(can.ravel());im.filepath_raw=str(out/(name+'.png'));im.file_format='PNG';im.save()
tiles=[]
for i,h in enumerate(heads):
 hair.hide_render=i==3 and sex!='child'
 fringe=bpy.data.objects.get('Hair_balding_fringe'); fringe.hide_render=i!=3 or sex=='child'
 h.hide_render=False;tiles.append(render_tile('tile'));h.hide_render=True; fringe.hide_render=True
hair.hide_render=False
sheet(sex+'_heads',tiles,4)
h=heads[0];h.hide_render=False;tiles=[]
for name in ['neutral','blink','joy','anger','fear','grief','shame','jealousy','mouth_open','mouth_wide','mouth_round']:
 for k in h.data.shape_keys.key_blocks:k.value=0
 if name!='neutral':h.data.shape_keys.key_blocks[name].value=1
 tiles.append(render_tile('tile'))
sheet(sex+'_expressions',tiles,4)
for o in scene.objects:
 if o.type=='MESH':
  o.hide_render=not(o.name.startswith(('Body_','Cloth_')))
for k in h.data.shape_keys.key_blocks:k.value=0
cam.data.type='PERSP';cam.data.lens=17;cam.location=(0,-.088*scale,1.634*scale);cam.rotation_euler=(Vector((0,-.8*scale,.64*scale))-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(out/(sex+'_firstperson.png'));bpy.ops.render.render(write_still=True)
fsart.result(ok=True,sex=sex,sheets=['heads','expressions','firstperson'])
