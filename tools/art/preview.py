"""M2/legacy turntable and silhouette; assembled modular characters, collision hidden.
Blender -b -P tools/art/preview.py -- asset.glb out-prefix [--head N] [--hair N]
"""
import math, pathlib, re, sys
sys.path.insert(0, str(pathlib.Path(__file__).parent))
import fsart
import bpy
import numpy as np
from mathutils import Vector

args = fsart.args()
path, prefix = args[:2]
fsart.reset()
bpy.ops.import_scene.gltf(filepath=path)
# COLOR_0.R is a wind mask, not albedo. glTF's generic importer multiplies it
# into the base colour; preview the palette directly like the game's wind shader.
for mat in bpy.data.materials:
    if not mat.use_nodes: continue
    bsdf=next((n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
    texture=next((n for n in mat.node_tree.nodes if n.type=='TEX_IMAGE' and n.image and 'palette' in n.image.name),None)
    if bsdf and texture:
        mat.node_tree.links.new(texture.outputs['Color'],bsdf.inputs['Base Color'])
bone_shapes = {p.custom_shape.name for o in bpy.context.scene.objects if o.type == 'ARMATURE' for p in o.pose.bones if p.custom_shape}
for o in bpy.context.scene.objects:
    if o.name in bone_shapes: o.hide_render=True
meshes = sorted((o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name not in bone_shapes), key=lambda o:o.name)
visible = []
slots = {}
for o in meshes:
    o.hide_render = o.name.endswith(('-colonly','-convcolonly')) or bool(re.search(r'_lod[1-9]\d*$',o.name))
    if not o.hide_render:
        slot = o.name.split('_')[0]
        if o.name in ('Cloth_wool_cloak','Cloth_fur_cloak','Cloth_oiled_cloak','Cloth_sailcloth_poncho'):
            slot = 'OuterCloak'
        if slot in ('Head','Hair','Beard','Headwear','OuterCloak'):
            slots.setdefault(slot, []).append(o)
        else:
            visible.append(o)
for slot, alternatives in slots.items():
    if slot=='OuterCloak': alternatives.sort(key=lambda o: (o.name!='Cloth_wool_cloak',o.name))
    arg = '--'+slot.lower()
    idx = int(args[args.index(arg)+1]) if arg in args else 0
    for i,o in enumerate(alternatives):
        o.hide_render = i != idx
    visible.append(alternatives[idx])
for o in meshes:
    if o.name in bone_shapes:
        o.hide_render=True
if not visible:
    raise ValueError('no renderable model; animation-only sets need a character for preview')
points = [o.matrix_world @ Vector(c) for o in visible for c in o.bound_box]
low = Vector(tuple(min(p[i] for p in points) for i in range(3)))
high = Vector(tuple(max(p[i] for p in points) for i in range(3)))
centre=(low+high)/2
extent=max(high-low)
scene=bpy.context.scene
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=scene.render.resolution_y=512
scene.render.resolution_percentage=100
scene.view_settings.view_transform='Standard'
world=bpy.data.worlds.new('preview'); world.use_nodes=True; scene.world=world
bg=world.node_tree.nodes['Background']
bg.inputs['Color'].default_value=(0.42,0.49,0.55,1)
bg.inputs['Strength'].default_value=0.6
sun_data=bpy.data.lights.new('sun','SUN'); sun_data.energy=2.5
sun=bpy.data.objects.new('sun',sun_data); scene.collection.objects.link(sun)
sun.rotation_euler=(math.radians(35),math.radians(-20),math.radians(-30))
cam=bpy.data.objects.new('cam',bpy.data.cameras.new('cam')); scene.collection.objects.link(cam); scene.camera=cam
cam.data.type='ORTHO'; cam.data.ortho_scale=extent*1.35
prefix=pathlib.Path(prefix); prefix.parent.mkdir(parents=True,exist_ok=True)
frames=[]
for i,angle in enumerate((-90,0,90,180)):
    a=math.radians(angle); cam.location=centre+Vector((extent*2*math.cos(a),extent*2*math.sin(a),extent*0.35))
    cam.rotation_euler=(centre-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(prefix)+f'_{i}.png'; bpy.ops.render.render(write_still=True)
    frames.append(scene.render.filepath)
sheet=bpy.data.images.new('sheet',1024,1024)
canvas=np.zeros((1024,1024,4),dtype=np.float32)
for i,f in enumerate(frames):
    im=bpy.data.images.load(f); src=np.empty(512*512*4,dtype=np.float32); im.pixels.foreach_get(src)
    x,y=(i%2)*512,(1-i//2)*512; canvas[y:y+512,x:x+512]=src.reshape(512,512,4)
sheet.pixels.foreach_set(canvas.ravel()); sheet.filepath_raw=str(prefix)+'_turntable.png'; sheet.file_format='PNG'; sheet.save()
for f in frames:pathlib.Path(f).unlink()
black=bpy.data.materials.new('black'); black.use_nodes=True
black.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(0,0,0,1)
black.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=1
for o in visible:
    o.data.materials.clear(); o.data.materials.append(black)
    for p in o.data.polygons:p.material_index=0
bg.inputs['Color'].default_value=(1,1,1,1); bg.inputs['Strength'].default_value=1; sun_data.energy=0
cam.data.type='PERSP'; cam.data.lens=min(200,40*24/(extent*1.25))
cam.location=centre+Vector((0,-40,0)); cam.rotation_euler=(centre-cam.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(prefix)+'_silhouette.png'; bpy.ops.render.render(write_still=True)
fsart.result(ok=True,turntable=str(prefix)+'_turntable.png',silhouette=str(prefix)+'_silhouette.png',assembled_objects=[o.name for o in visible])
