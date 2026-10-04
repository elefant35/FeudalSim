"""Automated asset checks (32 §6 step 5). Usage: Blender -b --factory-startup -P tools/art/check.py -- <asset.glb> <budget_class>

Checks: LOD naming and triangle budget for LOD0, LODs decreasing, material count, origin at the base
(min Z ~ 0), plausible size, UVs inside [0,1]. Prints RESULT {...}; exits 1 on failure.
"""
import pathlib, re, sys

sys.path.insert(0, str(pathlib.Path(__file__).parent))
import fsart  # noqa: E402
import bpy  # noqa: E402

path, budget_class = fsart.args()[:2]
budget = fsart.BUDGETS[budget_class]
fsart.reset()
bpy.ops.import_scene.gltf(filepath=path)
# Skinned assets: the glTF importer adds a display shape for bones (not part of the asset).
bone_shapes = {pb.custom_shape.name for o in bpy.context.scene.objects if o.type == "ARMATURE" for pb in o.pose.bones if pb.custom_shape}
meshes = sorted((o for o in bpy.context.scene.objects if o.type == "MESH" and o.name not in bone_shapes), key=lambda o: o.name)
problems = []
lods = {}
for o in meshes:
    m = re.search(r"_lod(\d+)$", o.name)
    if not m:
        problems.append(f"{o.name}: name must end in _lod<N>")
        continue
    lods[int(m.group(1))] = o

tris = {n: fsart.triangles(o) for n, o in sorted(lods.items())}
if 0 not in lods:
    problems.append("no _lod0 mesh")
else:
    lo, hi = budget["tris"]
    if not lo <= tris[0] <= hi:
        problems.append(f"lod0 has {tris[0]} tris; budget {budget_class} is {lo}-{hi}")
for n in sorted(tris)[1:]:
    if (n - 1) in tris and tris[n] >= tris[n - 1]:
        problems.append(f"lod{n} ({tris[n]} tris) is not smaller than lod{n - 1}")

materials = sorted({s.material.name for o in meshes for s in o.material_slots if s.material})
if len(materials) > budget["materials"]:
    problems.append(f"{len(materials)} materials; budget {budget['materials']}")

lod0 = lods.get(0)
height = min_z = None
if lod0:
    zs = [(lod0.matrix_world @ v.co).z for v in lod0.data.vertices]
    min_z, height = min(zs), max(zs) - min(zs)
    if abs(min_z) > 0.05:
        problems.append(f"origin not at base: min z = {min_z:.3f} m")
    if not 0.01 <= height <= 60:
        problems.append(f"implausible height {height:.2f} m")
    for layer in lod0.data.uv_layers:
        bad = sum(1 for d in layer.data if not (0 <= d.uv[0] <= 1 and 0 <= d.uv[1] <= 1))
        if bad:
            problems.append(f"{bad} UVs outside [0,1]")

fsart.result(ok=not problems, asset=path, budget_class=budget_class, tris=tris, materials=materials,
             height_m=round(height, 3) if height is not None else None, problems=problems)
sys.exit(0 if not problems else 1)
