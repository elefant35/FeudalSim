"""Actual shared-rig work poses, six FP checks for each P1 outer layer.
Render with Head/Hair/Beard/Headwear hidden and a single cloak alternative.
"""
import pathlib,sys,math
ROOT=pathlib.Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'tools/art'));sys.path.insert(0,str(pathlib.Path(__file__).parent))
sys.path.insert(0,str(ROOT/'art/generators/animations'))
import bpy,numpy as np,fsart,human_motion,p1
from mathutils import Vector
fsart.reset();objects=p1.generate({'sex':'male'})
arm=next(o for o in objects if o.type=='ARMATURE');meshes=[o for o in objects if o.type=='MESH']
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=320;scene.render.resolution_y=240;scene.render.resolution_percentage=100
scene.view_settings.view_transform='Standard'
world=bpy.data.worlds.new('P1FirstPerson');world.use_nodes=True;scene.world=world
world.node_tree.nodes['Background'].inputs['Color'].default_value=(.37,.45,.48,1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value=.6
ld=bpy.data.lights.new('key','SUN');ld.energy=2.7
lo=bpy.data.objects.new('key',ld);scene.collection.objects.link(lo);lo.rotation_euler=(.5,-.4,-.3)
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.01));fsart.paint(bpy.context.object,list(fsart.SWATCH)[0])
cam=bpy.data.objects.new('ReviewCamera',bpy.data.cameras.new('ReviewCamera'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.lens=20
out=ROOT/'art/previews/settlers';out.mkdir(parents=True,exist_ok=True)
cloaks={'Cloth_wool_cloak','Cloth_fur_cloak','Cloth_oiled_cloak','Cloth_sailcloth_poncho'}
clips=('chop_loop','knap_loop','forage_pick_loop','eat_loop','drink_kneel_loop','pick_up')
results=[]
for layer in sorted(cloaks):
 for o in meshes:
  o.hide_render=o.name.startswith(('Head_','Hair_','Beard_','Headwear_')) or (o.name in cloaks and o.name!=layer)
 paths=[]
 for clip in clips:
  for idx,phase in enumerate((.05,.36,.66)):
   human_motion.pose(arm,clip,phase)
   head=arm.pose.bones['Head'];deform=head.matrix@head.bone.matrix_local.inverted()
   eye=deform@Vector((0,-.10,1.634));forward=deform.to_quaternion()@Vector((0,-1,0))
   cam.location=eye;cam.rotation_euler=forward.to_track_quat('-Z','Y').to_euler()
   path=out/f'_tmp_p1fp_{clip}_{idx}.png';scene.render.filepath=str(path);bpy.ops.render.render(write_still=True);paths.append(path)
 w,h=scene.render.resolution_x,scene.render.resolution_y;canvas=np.ones((len(clips)*h,3*w,4),dtype=np.float32)
 for i,path in enumerate(paths):
  im=bpy.data.images.load(str(path),check_existing=False);pixels=np.empty(w*h*4,dtype=np.float32);im.pixels.foreach_get(pixels)
  x=i%3*w;y=(len(clips)-1-i//3)*h;canvas[y:y+h,x:x+w]=pixels.reshape(h,w,4);bpy.data.images.remove(im);path.unlink()
 name='p1_fp_'+layer.removeprefix('Cloth_');im=bpy.data.images.new(name,3*w,len(clips)*h);im.pixels.foreach_set(canvas.ravel());im.filepath_raw=str(out/(name+'.png'));im.file_format='PNG';im.save();results.append(im.filepath_raw)
fsart.result(ok=True,first_person=results,rows=list(clips))
