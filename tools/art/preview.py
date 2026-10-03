"""Preview renders for review (32 §6 step 6): a 4-angle turntable sheet and a 40 m silhouette.

Usage: Blender -b --factory-startup -P tools/art/preview.py -- <asset.glb> <out-prefix>
Writes <out-prefix>_turntable.png (2x2) and <out-prefix>_silhouette.png. Claude reads these PNGs to
self-check before asking the owner to approve the look.
"""
import math, pathlib, sys

sys.path.insert(0, str(pathlib.Path(__file__).parent))
import fsart  # noqa: E402
import bpy  # noqa: E402

path, prefix = fsart.args()[:2]
fsart.reset()
bpy.ops.import_scene.gltf(filepath=path)
lod0 = next(o for o in bpy.context.scene.objects if o.type == "MESH" and o.name.endswith("_lod0"))
for o in bpy.context.scene.objects:
    if o.type == "MESH" and o is not lod0:
        o.hide_render = True
zs = [(lod0.matrix_world @ v.co).z for v in lod0.data.vertices]
height = max(zs) - min(zs)

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = scene.render.resolution_y = 512
scene.view_settings.view_transform = "Standard"           # true palette colours, pure white silhouette ground
world = bpy.data.worlds.new("preview")
world.use_nodes = True                                   # EEVEE ignores world.color; use the Background node
bg = world.node_tree.nodes["Background"]
scene.world = world

target = bpy.data.objects.new("target", None)
target.location = (0, 0, height * 0.45)
scene.collection.objects.link(target)
sun_data = bpy.data.lights.new("sun", type="SUN")
sun_data.energy = 3.5
sun = bpy.data.objects.new("sun", sun_data)
sun.rotation_euler = (math.radians(50), 0, math.radians(30))
scene.collection.objects.link(sun)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
scene.camera = cam
track = cam.constraints.new("TRACK_TO")
track.target = target
track.track_axis, track.up_axis = "TRACK_NEGATIVE_Z", "UP_Y"

prefix = pathlib.Path(prefix)
prefix.parent.mkdir(parents=True, exist_ok=True)
frames = []
bg.inputs["Color"].default_value = (0.62, 0.72, 0.82, 1)
dist = max(height * 2.2, 2.0)
for i, angle in enumerate((-90, 0, 90, 180)):
    a = math.radians(angle)
    cam.location = (dist * math.cos(a), dist * math.sin(a), height * 0.75)
    scene.render.filepath = str(prefix) + f"_{i}.png"
    bpy.ops.render.render(write_still=True)
    frames.append(scene.render.filepath)

# 2x2 sheet
sheet = bpy.data.images.new("sheet", 1024, 1024)
pixels = [0.0] * (1024 * 1024 * 4)
for i, f in enumerate(frames):
    img = bpy.data.images.load(f)
    src = list(img.pixels)
    ox, oy = (i % 2) * 512, (1 - i // 2) * 512
    for y in range(512):
        row = (oy + y) * 1024 + ox
        pixels[row * 4:(row + 512) * 4] = src[y * 512 * 4:(y + 1) * 512 * 4]
sheet.pixels = pixels
sheet.filepath_raw = str(prefix) + "_turntable.png"
sheet.file_format = "PNG"
sheet.save()
for f in frames:
    pathlib.Path(f).unlink()

# Silhouette at 40 m: black object on white, long lens.
black = bpy.data.materials.new("black")
black.use_nodes = True
black.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0, 0, 0, 1)
black.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 1
lod0.data.materials.clear()
lod0.data.materials.append(black)
bg.inputs["Color"].default_value = (1, 1, 1, 1)
sun_data.energy = 0
cam.location = (0, -40, height * 0.5)
cam.data.lens = 200
scene.render.filepath = str(prefix) + "_silhouette.png"
bpy.ops.render.render(write_still=True)

fsart.result(ok=True, turntable=str(prefix) + "_turntable.png", silhouette=str(prefix) + "_silhouette.png")
