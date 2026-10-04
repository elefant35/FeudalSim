"""Shared helpers for FeudalSim's headless-Blender art pipeline (docs/production/32 §6).

Import from a script run as:
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P <script> -- <args>
Conventions: 1 unit = 1 m, Blender Z-up (glTF export converts to Y-up), models face -Y, origin at the
base centre, palette UVs (faces collapsed onto swatch centres), names <category>_<name>_<variant>_lod<N>.
"""
import json, os, pathlib, sys

import bpy

ROOT = pathlib.Path(__file__).resolve().parents[2]
PALETTE_PNG = ROOT / "art" / "palettes" / "palette.png"
PALETTE = json.loads((ROOT / "art" / "palettes" / "palette.json").read_text())
SWATCH = {s["name"]: (s["u"], s["v"]) for s in PALETTE["swatches"]}
BUDGETS = json.loads((pathlib.Path(__file__).parent / "budgets.json").read_text())


def args():
    """Arguments after `--` on the Blender command line."""
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def palette_material():
    mat = bpy.data.materials.get("palette")
    if mat:
        return mat
    mat = bpy.data.materials.new("palette")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = 0.9
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(str(PALETTE_PNG), check_existing=True)
    tex.interpolation = "Closest"
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def paint(obj, swatch, faces=None):
    """Collapse the UVs of `faces` (default: all) onto a palette swatch centre and use the palette material."""
    if swatch not in SWATCH:
        raise KeyError(f"unknown swatch '{swatch}'")
    mesh = obj.data
    if not mesh.uv_layers:
        mesh.uv_layers.new(name="UVMap")
    if not mesh.materials:
        mesh.materials.append(palette_material())
    u, v = SWATCH[swatch]
    uv = mesh.uv_layers.active.data
    for poly in mesh.polygons:
        if faces is None or poly.index in faces:
            for li in poly.loop_indices:
                uv[li].uv = (u, v)


def triangles(obj):
    """Triangles of a mesh object (0 for armatures and empties)."""
    if obj.type != "MESH":
        return 0
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def export_glb(path, objects):
    path = pathlib.Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", use_selection=True,
                              export_apply=True, export_yup=True, export_cameras=False, export_lights=False,
                              export_all_vertex_colors=True, export_vertex_color='ACTIVE')
    return path


def result(**fields):
    """Machine-readable last line for callers (tests, CI, Claude)."""
    print("RESULT " + json.dumps(fields, sort_keys=True))
