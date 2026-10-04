"""Headless M2/legacy GLB validation. Blender -b -P check.py -- asset.glb class.

M2 names need no LOD suffix. Collision never counts against visible budgets.
Character counts describe the largest assembled outfit, not all alternative heads/hair.
Animation-only files are checked directly against glTF channels for in-place motion.
"""
import json, math, pathlib, re, struct, sys
sys.path.insert(0, str(pathlib.Path(__file__).parent))
import fsart
import bpy
from mathutils import Vector


def glb_json(path):
    with open(path, 'rb') as f:
        header = f.read(12)
        if header[:4] != b'glTF':
            raise ValueError('not a GLB')
        size, kind = struct.unpack('<II', f.read(8))
        if kind != 0x4e4f534a:
            raise ValueError('missing JSON chunk')
        return json.loads(f.read(size))


def assembled(meshes):
    """All garment layers + one of each alternative modular slot."""
    base, slots = [], {}
    for o in meshes:
        prefix = o.name.split('_')[0]
        if prefix in ('Head', 'Hair', 'Beard', 'Headwear'):
            slots.setdefault(prefix, []).append(o)
        else:
            base.append(o)
    return base + [max(v, key=fsart.triangles) for v in slots.values()]


def main():
    path, cls = fsart.args()[:2]
    document = glb_json(path)
    problems = []
    if cls == 'animation':
        animations = document.get('animations', [])
        names = [a.get('name', '') for a in animations]
        if not names or len(set(names)) != len(names):
            problems.append('missing/duplicate animation names')
        if document.get('meshes'):
            problems.append('animation set must contain skeleton/actions only')
        # Blender importer samples channels for matrix and location inspection.
        fsart.reset()
        bpy.ops.import_scene.gltf(filepath=path)
        rigs = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
        if len(rigs) != 1:
            problems.append('animation set requires exactly one shared rig')
        if rigs:
            missing = human_bones() - set(rigs[0].data.bones.keys())
            if missing:
                problems.append('missing humanoid bones: ' + ', '.join(sorted(missing)))
        # Examine translation channels in file space; Y is glTF vertical.
        with open(path, 'rb') as f:
            f.seek(12)
            n, _ = struct.unpack('<II', f.read(8))
            f.seek(n, 1)
            n, _ = struct.unpack('<II', f.read(8))
            binary = f.read(n)
        for a in animations:
            for c in a['channels']:
                target = c['target']
                node = document['nodes'][target['node']].get('name')
                if node not in ('Root', 'Hips') or target['path'] != 'translation':
                    continue
                ac = document['accessors'][a['samplers'][c['sampler']]['output']]
                bv = document['bufferViews'][ac['bufferView']]
                offset = bv.get('byteOffset', 0) + ac.get('byteOffset', 0)
                stride = bv.get('byteStride', 12)
                values = [struct.unpack_from('<fff', binary, offset + i * stride) for i in range(ac['count'])]
                axes = range(3) if node == 'Root' else (0, 2)
                if any(max(v[j] for v in values) - min(v[j] for v in values) > 0.0001 for j in axes):
                    problems.append(f"{a['name']}: {node} travels")
        fsart.result(ok=not problems, asset=path, budget_class=cls, clips=names,
                     tris={'0': 0}, materials=[], problems=problems)
        return not problems

    budget = fsart.BUDGETS[cls]
    fsart.reset()
    bpy.ops.import_scene.gltf(filepath=path)
    bone_shapes = {p.custom_shape.name for o in bpy.context.scene.objects if o.type == 'ARMATURE'
                   for p in o.pose.bones if p.custom_shape}
    meshes = sorted((o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name not in bone_shapes), key=lambda o: o.name)
    collision = [o for o in meshes if o.name.endswith(('-colonly', '-convcolonly'))]
    visible = [o for o in meshes if o not in collision]
    lod0 = [o for o in visible if not re.search(r'_lod[1-9]\d*$', o.name)]
    selected = assembled(lod0) if cls == 'character' else lod0
    tris = {'0': sum(fsart.triangles(o) for o in selected)}
    if not selected:
        problems.append('no visible LOD0 meshes')
    lo, hi = budget['tris']
    # Contract §6.4 explicit first-person allowance (other props stay at 600).
    if pathlib.Path(path).stem in ('flint_knife', 'stone_axe', 'iron_axe', 'hammerstone'):
        hi = 1200
    if not lo <= tris['0'] <= hi:
        problems.append(f"assembled LOD0 {tris['0']} triangles outside {lo}–{hi}")
    materials = sorted({s.material.name for o in visible for s in o.material_slots if s.material})
    if len(materials) > budget['materials']:
        problems.append(f'{len(materials)} materials exceeds {budget["materials"]}')
    swatches = list(fsart.SWATCH.values())
    for o in visible:
        if not o.data.uv_layers:
            problems.append(o.name + ': no palette UV layer')
        else:
            for p in o.data.polygons:
                coords = [o.data.uv_layers.active.data[i].uv for i in p.loop_indices]
                if not coords:
                    continue
                if not any(all(abs(uv.x-u) < 0.0002 and abs(uv.y-v) < 0.0002 for uv in coords) for u,v in swatches):
                    problems.append(o.name + ': UV face not collapsed onto a palette centre')
                    break
        if any(not math.isfinite(c) for v in o.data.vertices for c in v.co):
            problems.append(o.name + ': nonfinite geometry')
        if any(p.area < 1e-12 for p in o.data.polygons):
            problems.append(o.name + ': degenerate faces')
        # Mesh origins must be the shared asset origin, transforms applied.
        if any(abs(s-1) > 0.0001 for s in o.scale):
            problems.append(o.name + ': unapplied scale')
    points = [o.matrix_world @ v.co for o in selected for v in o.data.vertices]
    height = min_z = None
    if points:
        min_z = min(p.z for p in points)
        height = max(p.z for p in points) - min_z
        grip = cls == 'prop_handheld'
        waterline = pathlib.Path(path).stem in ('hull_bow', 'hull_stern')
        if not grip and not waterline and abs(min_z) > 0.05:
            problems.append(f'base outside ground plane: {min_z:.3f} m')
        if not 0.005 <= height <= 60:
            problems.append(f'implausible height {height:.3f} m')
    if cls == 'character':
        rigs = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
        if len(rigs) != 1:
            problems.append('requires one shared skeleton')
        else:
            names = set(rigs[0].data.bones.keys())
            if human_bones() != names:
                problems.append('skeleton mismatch: missing=' + str(sorted(human_bones()-names)) + ' extra=' + str(sorted(names-human_bones())))
        for o in visible:
            if o.name.startswith('Head_'):
                required = {'blink','joy','anger','fear','grief','shame','jealousy','mouth_open','mouth_wide','mouth_round'}
                keys = set(o.data.shape_keys.key_blocks.keys()) if o.data.shape_keys else set()
                if required - keys:
                    problems.append(o.name + ': missing face shapes ' + str(sorted(required-keys)))
        if height is not None and not 1.59 <= height <= 1.86:
            problems.append('adult height outside contract scale')
    fsart.result(ok=not problems, asset=path, budget_class=cls, tris=tris,
                 stored_tris=sum(fsart.triangles(o) for o in lod0), assembled_objects=[o.name for o in selected],
                 collision_objects=[o.name for o in collision], materials=materials,
                 height_m=round(height, 3) if height is not None else None, problems=problems)
    return not problems


def human_bones():
    names = {'Root','Hips','Spine','Chest','UpperChest','Neck','Head','Jaw','LeftEye','RightEye'}
    for s in ('Left','Right'):
        names.update(s+n for n in ('Shoulder','UpperArm','LowerArm','Hand','ThumbMetacarpal','ThumbProximal',
            'IndexProximal','IndexIntermediate','MiddleProximal','MiddleIntermediate','UpperLeg','LowerLeg','Foot','Toes','HandProp'))
    return names


if __name__ == '__main__':
    sys.exit(0 if main() else 1)
