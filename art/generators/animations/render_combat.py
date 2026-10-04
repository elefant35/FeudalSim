"""Render three phases of five first-person checks and all nine P2 poses, actual body.
The camera follows Head's eye position and forward direction; no compensating
camera pitch. Head/Hair/Beard/Headwear modules are hidden for first person.
"""
import pathlib,sys,math
ROOT=pathlib.Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'tools/art')); sys.path.insert(0,str(ROOT/'art/generators/settlers'))
sys.path.insert(0,str(pathlib.Path(__file__).parent))
import bpy,numpy as np,fsart,settlers
import combat_motion as human_motion
from mathutils import Vector
fsart.reset(); objects=settlers.generate({'sex':'male'})
arm=next(o for o in objects if o.type=='ARMATURE')
meshes=[o for o in objects if o.type=='MESH']
slot={}
for o in sorted(meshes,key=lambda o:o.name):
 group=o.name.split('_')[0]
 if group in ('Head','Hair','Beard','Headwear'):
  o.hide_render=group in slot; slot.setdefault(group,o)
scene=bpy.context.scene; scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=400; scene.render.resolution_y=300; scene.render.resolution_percentage=100
scene.view_settings.view_transform='Standard'
world=bpy.data.worlds.new('AnimationReview'); world.use_nodes=True;scene.world=world
world.node_tree.nodes['Background'].inputs['Color'].default_value=(.37,.45,.48,1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value=.6
ld=bpy.data.lights.new('key','SUN');ld.energy=2.7
lo=bpy.data.objects.new('key',ld);scene.collection.objects.link(lo);lo.rotation_euler=(.5,-.4,-.3)
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.01)); ground=bpy.context.object
fsart.paint(ground,'earth_brown' if 'earth_brown' in fsart.SWATCH else list(fsart.SWATCH)[0])
cam=bpy.data.objects.new('ReviewCamera',bpy.data.cameras.new('ReviewCamera'));scene.collection.objects.link(cam);scene.camera=cam
out=ROOT/'art/previews/animations_p2';out.mkdir(parents=True,exist_ok=True)
def save_sheet(paths,name):
 w,h=scene.render.resolution_x,scene.render.resolution_y; cols=3;rows=math.ceil(len(paths)/cols)
 canvas=np.ones((rows*h,cols*w,4),dtype=np.float32)
 for i,p in enumerate(paths):
  im=bpy.data.images.load(str(p),check_existing=False);pix=np.empty(w*h*4,dtype=np.float32);im.pixels.foreach_get(pix)
  x=i%cols*w;y=(rows-1-i//cols)*h;canvas[y:y+h,x:x+w]=pix.reshape(h,w,4);bpy.data.images.remove(im)
 im=bpy.data.images.new(name,cols*w,rows*h);im.pixels.foreach_set(canvas.ravel());im.filepath_raw=str(out/(name+'.png'));im.file_format='PNG';im.save()
 for p in paths:p.unlink()
 return str(out/(name+'.png'))
fp={}
for clip in ('unarmed_ready','punch_a','punch_b','block','shove'):
 paths=[]
 for idx,phase in enumerate((.05,.36,.66)):
  human_motion.pose(arm,clip,phase)
  head=arm.pose.bones['Head']; deform=head.matrix@head.bone.matrix_local.inverted()
  eye=deform@Vector((0,-.10,1.634));forward=deform.to_quaternion()@Vector((0,-1,0));up=deform.to_quaternion()@Vector((0,0,1))
  cam.location=eye;cam.rotation_euler=forward.to_track_quat('-Z','Y').to_euler();cam.data.type='PERSP';cam.data.lens=20
  for o in meshes:
   if o.name.split('_')[0] in ('Head','Hair','Beard','Headwear'):o.hide_render=True
  p=out/f'_tmp_{clip}_{idx}.png';scene.render.filepath=str(p);bpy.ops.render.render(write_still=True);paths.append(p)
 fp[clip]=save_sheet(paths,'fp_'+clip)
 # Restore default assembled modules for external review.
 for group,o in slot.items():o.hide_render=False
third={}
scene.render.resolution_x=256;scene.render.resolution_y=320
cam.data.type='ORTHO';cam.data.ortho_scale=2.35;cam.location=(2.6,-4.7,2.3)
cam.rotation_euler=(Vector((0,-.05,.82))-cam.location).to_track_quat('-Z','Y').to_euler()
for family,clips in human_motion.SETS.items():
 if fsart.args() and family not in fsart.args():continue
 paths=[]
 for clip in clips:
  for idx,phase in enumerate((.0,.36,.66)):
   human_motion.pose(arm,clip,phase);p=out/f'_tmp_{clip}_{idx}.png'
   scene.render.filepath=str(p);bpy.ops.render.render(write_still=True);paths.append(p)
 third[family]=save_sheet(paths,'third_'+family)
fsart.result(ok=True,first_person=fp,third_person=third)
