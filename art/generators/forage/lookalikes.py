"""Blender close morphology views for the P1 forage lookalikes (original GLBs)."""
import math,pathlib,sys
import bpy
from mathutils import Vector

ROOT=pathlib.Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'tools/art'))
import fsart
OUT=ROOT/'art/previews/forage';OUT.mkdir(parents=True,exist_ok=True)
NAMES=('wild_carrot','hemlock','water_hemlock','field_mushroom','death_cap','ramsons',
       'lily_of_the_valley','comfrey','foxglove','bilberry','deadly_nightshade')
for name in NAMES:
    modes=('detail','gills') if name in ('field_mushroom','death_cap') else (('detail','berry') if name in ('bilberry','deadly_nightshade') else ('detail',))
    for mode in modes:
        fsart.reset()
        folder='bushes' if name=='bilberry' else 'plants'
        bpy.ops.import_scene.gltf(filepath=str(ROOT/f'game/assets/flora/{folder}/{name}_a.glb'))
        meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
        for mat in bpy.data.materials:
            if not mat.use_nodes:continue
            bsdf=next((n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
            tex=next((n for n in mat.node_tree.nodes if n.type=='TEX_IMAGE'),None)
            if bsdf and tex:mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
        points=[o.matrix_world@Vector(v) for o in meshes for v in o.bound_box]
        low=Vector(tuple(min(p[i] for p in points) for i in range(3)))
        high=Vector(tuple(max(p[i] for p in points) for i in range(3)))
        centre=(low+high)/2;extent=max(high-low)
        scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE'
        scene.render.resolution_x=scene.render.resolution_y=512;scene.render.resolution_percentage=100
        scene.view_settings.view_transform='Standard'
        world=bpy.data.worlds.new('botanical_review');world.use_nodes=True;scene.world=world
        world.node_tree.nodes['Background'].inputs[0].default_value=(.38,.45,.49,1)
        world.node_tree.nodes['Background'].inputs[1].default_value=.8
        lightdata=bpy.data.lights.new('overcast','SUN');lightdata.energy=2
        light=bpy.data.objects.new('overcast',lightdata);scene.collection.objects.link(light)
        light.rotation_euler=(math.radians(35),math.radians(-25),math.radians(-20))
        camera=bpy.data.objects.new('camera',bpy.data.cameras.new('camera'));scene.collection.objects.link(camera);scene.camera=camera
        camera.data.type='ORTHO';camera.data.ortho_scale=extent*1.15
        if mode=='berry':
            centre=Vector((.25,-.12,.35*.87-.035)) if name=='bilberry' else Vector((-.21,-.07,.92*.25+.035))
            extent=.16;camera.data.ortho_scale=.16
        camera.location=centre+Vector((extent*.55,-extent*2.8,extent*(1.8 if mode=='berry' else (.7 if mode=='detail' else -.62))))
        camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(OUT/(name+'_'+mode+'.png'));bpy.ops.render.render(write_still=True)
        fsart.result(ok=True,species=name,view=mode,path=scene.render.filepath)
