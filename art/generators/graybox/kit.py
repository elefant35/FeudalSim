"""Graybox kit (M1-20, 32 §2 "placeholders are first-class"): capsule people with role markers, primitive props and
block buildings, each a few primitives in palette swatches. generate({"item": <name>}) -> [lod0]. Deterministic.
Names follow <category>_<item>_a_lod0 with category char | prop | bldg | nature. Facing -Y, base at z = 0.
"""
import math

import bpy

import fsart

S = 8   # ring sides: blockout-coarse on purpose


def box(w, d, h, x=0.0, y=0.0, z=0.0, sw="wood_light", rz=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x, y, z + h / 2))
    o = bpy.context.object
    o.scale = (w, d, h)
    o.rotation_euler[2] = rz
    fsart.paint(o, sw)
    return o


def cyl(r, h, x=0.0, y=0.0, z=0.0, sw="wood_light", sides=S, r2=None, rx=0.0, ry=0.0):
    bpy.ops.mesh.primitive_cone_add(vertices=sides, radius1=r, radius2=r if r2 is None else r2, depth=h, location=(x, y, z + h / 2))
    o = bpy.context.object
    o.rotation_euler = (rx, ry, 0)
    fsart.paint(o, sw)
    return o


def ball(r, x=0.0, y=0.0, z=0.0, sw="skin_3", segs=8):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=max(4, segs // 2), radius=r, location=(x, y, z))
    o = bpy.context.object
    fsart.paint(o, sw)
    return o


def person(tunic="wool_brown", scale=1.0):
    """A capsule body (1.75 m) with a head: the base every role marker sits on."""
    k = scale
    return [cyl(0.24 * k, 1.25 * k, sw=tunic, sides=10, r2=0.2 * k), ball(0.14 * k, z=1.42 * k, segs=10)]


def _person_item(name):
    k = 0.65 if name == "child" else 1.0
    tunic = {"priest": "ashen_grey", "guard": "varrow_blue", "smith": "wool_grey", "farmer": "linen", "hunter": "brannoch_green",
             "elder": "wool_brown", "child": "russet"}.get(name, "wool_brown")
    parts = person(tunic, k)
    if name == "smith":
        parts += [box(0.42, 0.04, 0.7, 0, -0.24, 0.45, "bark_dark"), cyl(0.03, 0.4, 0.3, -0.1, 0.6, "wood_light"), box(0.12, 0.06, 0.08, 0.3, -0.1, 1.0, "iron")]
    elif name == "farmer":
        parts += [cyl(0.3, 0.05, z=1.52, sw="straw", sides=10), cyl(0.15, 0.14, z=1.55, sw="straw", sides=10)]
    elif name == "hunter":
        parts += [cyl(0.02, 1.2, 0, 0.26, 0.3, "bark", sides=6, ry=0.4), box(0.12, 0.12, 0.45, -0.12, 0.25, 0.8, "bark_dark")]
    elif name == "priest":
        parts += [cyl(0.32, 1.1, sw="ashen_grey", sides=10, r2=0.22), box(0.05, 0.02, 0.3, 0, -0.27, 0.9, "ashen_ember")]
    elif name == "elder":
        parts += [cyl(0.025, 1.3, 0.32, -0.1, 0.0, "bark", sides=6), ball(0.08, 0, 0.0, 1.5, "wool_grey", segs=8)]
    elif name == "guard":
        parts += [cyl(0.16, 0.12, z=1.5, sw="steel", sides=10, r2=0.1), cyl(0.025, 2.1, 0.35, 0, 0, "wood_light", sides=6), cyl(0.05, 0.25, 0.35, 0, 2.1, "steel", sides=6, r2=0.0)]
    return parts


def generate(params):
    item = params["item"]
    cat, p = ITEMS[item]
    parts = _person_item(item) if cat == "char" else p()
    bpy.ops.object.select_all(action="DESELECT")
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = obj.data.name = f"{cat}_{item}_a_lod0"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    low = min(v.co.z for v in obj.data.vertices)   # snap to the ground: base at z = 0 (32 §6 convention)
    for v in obj.data.vertices:
        v.co.z -= low
    return [obj]


def _woodpile():
    return [cyl(0.12, 1.6, 0, y * 0.25 - 0.4, 0.12 + (abs(y) % 2) * 0.2, "bark", rx=0, ry=math.pi / 2) for y in range(-1, 3)] + [box(1.7, 0.1, 0.05, z=0.0, sw="bark_dark")]


ITEMS = {
    # people with role markers (8)
    "person": ("char", None), "smith": ("char", None), "farmer": ("char", None), "hunter": ("char", None),
    "priest": ("char", None), "child": ("char", None), "elder": ("char", None), "guard": ("char", None),
    # props (18)
    "crate": ("prop", lambda: [box(0.6, 0.6, 0.6, sw="wood_light"), box(0.62, 0.04, 0.08, 0, -0.3, 0.3, "bark")]),
    "barrel": ("prop", lambda: [cyl(0.3, 0.85, sw="bark", r2=0.3), cyl(0.31, 0.05, z=0.2, sw="iron"), cyl(0.31, 0.05, z=0.65, sw="iron")]),
    "sack": ("prop", lambda: [cyl(0.25, 0.55, sw="linen", r2=0.15), ball(0.12, z=0.58, sw="linen")]),
    "basket": ("prop", lambda: [cyl(0.22, 0.25, sw="straw", r2=0.28)]),
    "bucket": ("prop", lambda: [cyl(0.16, 0.3, sw="wood_light", r2=0.19), box(0.4, 0.02, 0.02, z=0.36, sw="iron")]),
    "cookpot": ("prop", lambda: [cyl(0.22, 0.28, sw="iron", r2=0.2), cyl(0.02, 0.7, -0.3, 0, 0, "bark", sides=4), cyl(0.02, 0.7, 0.3, 0, 0, "bark", sides=4), box(0.66, 0.03, 0.03, z=0.68, sw="bark")]),
    "firepit": ("prop", lambda: [cyl(0.8, 0.15, sw="granite", sides=10), cyl(0.55, 0.16, sw="soil_dark", sides=10), cyl(0.25, 0.35, z=0.1, sw="fire", sides=6, r2=0.0)]),
    "woodpile": ("prop", _woodpile),
    "log": ("prop", lambda: [cyl(0.18, 2.0, 0, 0, 0.18, "bark", rx=0, ry=math.pi / 2)]),
    "stump": ("prop", lambda: [cyl(0.32, 0.45, sw="bark", r2=0.28), cyl(0.28, 0.02, z=0.45, sw="wood_light")]),
    "stool": ("prop", lambda: [cyl(0.2, 0.06, z=0.42, sw="wood_light")] + [cyl(0.025, 0.42, 0.12 * math.cos(a), 0.12 * math.sin(a), 0, "bark", sides=4) for a in (0, 2.1, 4.2)]),
    "bench": ("prop", lambda: [box(1.6, 0.35, 0.06, z=0.42, sw="wood_light"), box(0.08, 0.3, 0.42, -0.65, sw="bark"), box(0.08, 0.3, 0.42, 0.65, sw="bark")]),
    "table": ("prop", lambda: [box(1.8, 0.8, 0.06, z=0.74, sw="wood_light")] + [box(0.08, 0.08, 0.74, x, y, 0, "bark") for x in (-0.8, 0.8) for y in (-0.32, 0.32)]),
    "workbench": ("prop", lambda: [box(2.0, 0.7, 0.1, z=0.8, sw="wood_light"), box(1.9, 0.6, 0.04, z=0.3, sw="bark")] + [box(0.1, 0.1, 0.8, x, y, 0, "bark") for x in (-0.9, 0.9) for y in (-0.28, 0.28)]),
    "anvil": ("prop", lambda: [box(0.35, 0.35, 0.5, sw="bark"), box(0.6, 0.22, 0.2, z=0.5, sw="iron"), cyl(0.1, 0.25, 0.4, 0, 0.55, "iron", sides=6, r2=0.0, ry=math.pi / 2)]),
    "chopping_block": ("prop", lambda: [cyl(0.35, 0.5, sw="bark"), cyl(0.03, 0.7, 0.05, 0, 0.5, "wood_light", sides=4, ry=0.5), box(0.15, 0.03, 0.1, 0.2, 0, 1.05, "iron")]),
    "tool_axe": ("prop", lambda: [cyl(0.02, 0.8, sw="wood_light", sides=6), box(0.16, 0.03, 0.1, 0.06, 0, 0.66, "iron")]),
    "tool_hammer": ("prop", lambda: [cyl(0.018, 0.4, sw="wood_light", sides=6), box(0.14, 0.05, 0.05, z=0.38, sw="iron")]),
    # buildings (10)
    "tent": ("bldg", lambda: [cyl(1.6, 2.2, sw="linen", sides=6, r2=0.0), cyl(0.04, 2.5, sw="bark", sides=4)]),
    "lean_to": ("bldg", lambda: [box(2.6, 0.08, 2.0, 0, 0.6, 0, "bark", rz=0), box(2.6, 2.0, 0.08, 0, 0, 1.6, "straw")]),
    "sailcloth_shelter": ("bldg", lambda: [box(3.2, 2.6, 0.04, z=1.9, sw="linen")] + [cyl(0.05, 1.9, x, y, 0, "bark", sides=4) for x in (-1.5, 1.5) for y in (-1.2, 1.2)]),
    "hut": ("bldg", lambda: [cyl(1.8, 1.8, sw="clay", sides=10), cyl(2.1, 1.6, z=1.8, sw="straw", sides=10, r2=0.1), box(0.8, 0.1, 1.5, 0, -1.8, 0, "bark_dark")]),
    "longhouse": ("bldg", lambda: [box(9.0, 4.5, 2.2, sw="wood_light"), box(9.4, 3.2, 1.0, 0, -1.1, 2.2, "straw", rz=0), box(9.4, 3.2, 1.0, 0, 1.1, 2.2, "straw"), box(1.0, 0.1, 1.9, 0, -2.3, 0, "bark_dark")]),
    "storehouse": ("bldg", lambda: [box(3.5, 3.0, 2.2, z=0.5, sw="wood_light"), box(3.9, 3.4, 0.9, z=2.7, sw="straw")] + [cyl(0.15, 0.5, x, y, 0, "granite", sides=6) for x in (-1.5, 1.5) for y in (-1.2, 1.2)]),
    "well": ("bldg", lambda: [cyl(0.9, 0.8, sw="granite", sides=10), cyl(0.7, 0.81, sw="water_deep", sides=10), box(0.1, 0.1, 1.8, -0.8, 0, 0, "bark"), box(0.1, 0.1, 1.8, 0.8, 0, 0, "bark"), box(1.8, 0.12, 0.12, z=1.8, sw="bark")]),
    "palisade_segment": ("bldg", lambda: [cyl(0.14, 2.8 + (i % 2) * 0.2, -1.8 + i * 0.3, 0, 0, "bark", sides=6) for i in range(13)] + [box(3.8, 0.1, 0.12, z=1.6, sw="bark_dark")]),
    "gate": ("bldg", lambda: [box(0.4, 0.4, 3.4, -1.6, sw="bark"), box(0.4, 0.4, 3.4, 1.6, sw="bark"), box(3.6, 0.4, 0.4, z=3.4, sw="bark"), box(2.8, 0.1, 2.8, sw="wood_light")]),
    "drying_rack": ("bldg", lambda: [box(0.08, 0.08, 1.6, x, 0, 0, "bark") for x in (-1.2, 1.2)] + [box(2.6, 0.06, 0.06, z=z, sw="bark") for z in (0.9, 1.5)] + [box(0.3, 0.02, 0.4, x, 0, 1.05, "russet") for x in (-0.7, 0.0, 0.7)]),
    # nature (4)
    "rock_small": ("nature", lambda: [ball(0.35, z=0.2, sw="granite", segs=6)]),
    "rock_large": ("nature", lambda: [ball(0.9, z=0.5, sw="granite_dark", segs=7), ball(0.5, 0.7, 0.2, 0.3, "granite", segs=6)]),
    "boulder": ("nature", lambda: [ball(1.6, z=1.0, sw="granite", segs=8)]),
    "bush": ("nature", lambda: [ball(0.6, z=0.5, sw="leaf_dark", segs=7), ball(0.45, 0.4, 0.2, 0.55, "leaf", segs=6)]),
}
