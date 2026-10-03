# Prototype (2026-10-03): proves headless Blender 5.1 can generate a palette-coloured low-poly asset
# and export glTF for Godot. Starting point for M0-A3/M0-A4 (docs/production/33-progress.md).
# Run: /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P tools/art/prototypes/conifer_test.py -- out/pine_a.glb
# Result on the dev Mac: 56 tris, 2 materials, ~6 KB .glb.
import bpy, bmesh, math, os, sys
out = sys.argv[sys.argv.index("--")+1]
bpy.ops.wm.read_factory_settings(use_empty=True)
def mat(name, rgb):
    m = bpy.data.materials.new(name); m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0); bsdf.inputs["Roughness"].default_value = 0.9
    return m
bark, needle = mat("pal_bark", (0.32,0.20,0.12)), mat("pal_pine", (0.12,0.30,0.16))
bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.18, depth=1.2, location=(0,0,0.6))
trunk = bpy.context.object; trunk.name = "trunk"; trunk.data.materials.append(bark)
tiers = []
for i,(r,z) in enumerate([(1.3,1.5),(1.0,2.3),(0.7,3.0)]):
    bpy.ops.mesh.primitive_cone_add(vertices=7, radius1=r, radius2=0.0, depth=1.4, location=(0,0,z))
    c = bpy.context.object; c.rotation_euler[2] = i*0.45; c.data.materials.append(needle); tiers.append(c)
for o in [trunk]+tiers: o.select_set(True)
bpy.context.view_layer.objects.active = trunk
bpy.ops.object.join(); tree = bpy.context.object; tree.name = "flora_pine_a_lod0"
bpy.ops.object.shade_flat() if hasattr(bpy.ops.object, "shade_flat") else None
tris = sum(len(p.vertices)-2 for p in tree.data.polygons)
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=False)
print(f"RESULT blender={bpy.app.version_string} object={tree.name} tris={tris} materials={len(tree.data.materials)} glb_bytes={os.path.getsize(out)}")
