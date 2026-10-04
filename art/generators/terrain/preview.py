"""Blender walking-height material previews, without changing game code.

blender -b --factory-startup -P art/generators/terrain/preview.py -- <texture-dir>
"""
import sys
from pathlib import Path
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[3]
TEXTURES = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
OUT = ROOT / "art/previews/terrain"
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.samples = 16
scene.cycles.use_denoising = True
scene.render.resolution_x = 640
scene.render.resolution_y = 480
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.view_settings.view_transform = "Standard"
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs[0].default_value = (.5, .55, .6, 1)
scene.world.node_tree.nodes["Background"].inputs[1].default_value = .8
bpy.ops.object.camera_add(location=(0, -3, 1.7))
camera = bpy.context.object
camera.rotation_euler = (Vector((0, 3, 0)) - camera.location).to_track_quat("-Z", "Y").to_euler()
camera.data.lens = 28
scene.camera = camera
bpy.ops.object.light_add(type="AREA", location=(-3, 1, 8))
light = bpy.context.object
light.data.energy = 900
light.data.size = 10
light.rotation_euler = (Vector((0, 1, 0)) - light.location).to_track_quat("-Z", "Y").to_euler()
bpy.ops.mesh.primitive_plane_add(size=30)
plane = bpy.context.object
for loop in plane.data.uv_layers.active.data: loop.uv *= 15  # 30 m / 15 repeats = 2 m

for layer in ("sand", "shingle", "grass", "grass_rough", "forest_floor", "heather_moor", "mud", "rock"):
    mat = bpy.data.materials.new("terrain_" + layer)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes; links = mat.node_tree.links
    bsdf = nodes.get("Principled BSDF")
    a = nodes.new("ShaderNodeTexImage")
    a.image = bpy.data.images.load(str(TEXTURES / (layer + "_albedo_height.png")))
    a.image.alpha_mode = "CHANNEL_PACKED"
    n = nodes.new("ShaderNodeTexImage")
    n.image = bpy.data.images.load(str(TEXTURES / (layer + "_normal_rough.png")))
    n.image.colorspace_settings.name = "Non-Color"
    n.image.alpha_mode = "CHANNEL_PACKED"
    normal = nodes.new("ShaderNodeNormalMap")
    links.new(a.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(n.outputs["Color"], normal.inputs["Color"])
    links.new(normal.outputs["Normal"], bsdf.inputs["Normal"])
    links.new(n.outputs["Alpha"], bsdf.inputs["Roughness"])
    plane.data.materials.clear(); plane.data.materials.append(mat)
    scene.render.filepath = str(OUT / (layer + "_walking.png"))
    bpy.ops.render.render(write_still=True)
    print("RESULT walking_material " + layer, flush=True)
