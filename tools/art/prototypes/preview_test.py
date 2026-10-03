# Prototype (2026-10-03): renders an EEVEE preview of a .glb for review (Claude can Read the PNG).
# Run: /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P tools/art/prototypes/preview_test.py -- in.glb out.png
import bpy, sys, math
argv = sys.argv[sys.argv.index("--")+1:]
glb, png = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
obj = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
bpy.ops.mesh.primitive_plane_add(size=8, location=(0,0,0))
g = bpy.context.object; m = bpy.data.materials.new("ground"); m.use_nodes=True
m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value=(0.45,0.52,0.30,1); g.data.materials.append(m)
bpy.ops.object.camera_add(location=(5.5,-5.5,3.6)); cam = bpy.context.object
d = cam.constraints.new('TRACK_TO'); d.target = obj; d.track_axis='TRACK_NEGATIVE_Z'; d.up_axis='UP_Y'
bpy.context.scene.camera = cam
bpy.ops.object.light_add(type='SUN', location=(3,-2,6)); bpy.context.object.data.energy = 3.5
bpy.context.object.rotation_euler = (math.radians(50), 0, math.radians(30))
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_WORKBENCH'
sc.render.resolution_x = sc.render.resolution_y = 384
sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.62,0.72,0.82)
sc.render.filepath = png
bpy.ops.render.render(write_still=True)
print("RENDERED", sc.render.engine)
