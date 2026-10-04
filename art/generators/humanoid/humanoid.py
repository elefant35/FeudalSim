"""Blockout humanoid with a skeleton and two in-place cycles (walk, idle): the M1-S1 crowd-render stand-in (32 §7).

generate(params) -> [armature, mesh]. Params: variant (str), segments (int, ring sides of limbs), tunic, skin, hair
(palette swatches). Primitives joined per body part, each part bound rigidly to one bone. Facing -Y, feet at z = 0,
1.75 m tall. Not a look: a performance stand-in with a character's triangle count and a real skeleton.
"""
import math

import bpy

import fsart

# bone: (head, tail, parent)
BONES = {
    "hips": ((0, 0, 0.95), (0, 0, 1.05), None),
    "spine": ((0, 0, 1.05), (0, 0, 1.30), "hips"),
    "chest": ((0, 0, 1.30), (0, 0, 1.48), "spine"),
    "neck": ((0, 0, 1.48), (0, 0, 1.56), "chest"),
    "head": ((0, 0, 1.56), (0, 0, 1.75), "neck"),
    "upper_arm.L": ((0.20, 0, 1.44), (0.22, 0, 1.16), "chest"),
    "lower_arm.L": ((0.22, 0, 1.16), (0.23, 0, 0.92), "upper_arm.L"),
    "hand.L": ((0.23, 0, 0.92), (0.23, 0, 0.84), "lower_arm.L"),
    "upper_arm.R": ((-0.20, 0, 1.44), (-0.22, 0, 1.16), "chest"),
    "lower_arm.R": ((-0.22, 0, 1.16), (-0.23, 0, 0.92), "upper_arm.R"),
    "hand.R": ((-0.23, 0, 0.92), (-0.23, 0, 0.84), "lower_arm.R"),
    "thigh.L": ((0.10, 0, 0.95), (0.10, 0, 0.52), "hips"),
    "shin.L": ((0.10, 0, 0.52), (0.10, 0, 0.08), "thigh.L"),
    "foot.L": ((0.10, 0, 0.08), (0.10, -0.14, 0.03), "shin.L"),
    "thigh.R": ((-0.10, 0, 0.95), (-0.10, 0, 0.52), "hips"),
    "shin.R": ((-0.10, 0, 0.52), (-0.10, 0, 0.08), "thigh.R"),
    "foot.R": ((-0.10, 0, 0.08), (-0.10, -0.14, 0.03), "shin.R"),
}


def _limb(a, b, r0, r1, sides, rings):
    """A tapered tube from a to b (rings × sides quads + caps)."""
    ax, ay, az = a
    bx, by, bz = b
    d = math.dist(a, b)
    bpy.ops.mesh.primitive_cone_add(vertices=sides, radius1=r0, radius2=r1, depth=d, location=((ax + bx) / 2, (ay + by) / 2, (az + bz) / 2))
    o = bpy.context.object
    dx, dy, dz = bx - ax, by - ay, bz - az
    o.rotation_euler = (math.atan2(math.hypot(dx, dy), dz) * (1 if dy <= 0 else -1) if dx == 0 else 0, math.atan2(dx, dz) if dy == 0 else 0, 0)
    if rings > 1:
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.subdivide(number_cuts=rings - 1)
        bpy.ops.object.mode_set(mode="OBJECT")
    return o


def _sphere(c, r, segs, rings):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=rings, radius=r, location=c)
    return bpy.context.object


def _bind(obj, bone):
    vg = obj.vertex_groups.new(name=bone)
    vg.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")


def _armature(name):
    bpy.ops.object.armature_add(enter_editmode=True, location=(0, 0, 0))
    arm = bpy.context.object
    arm.name = arm.data.name = name
    eb = arm.data.edit_bones
    eb.remove(eb[0])
    for bname, (h, t, parent) in BONES.items():
        b = eb.new(bname)
        b.head, b.tail = h, t
        if parent:
            b.parent = eb[parent]
            b.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def _key(arm, action, frames):
    """frames: {frame: {bone: (x_deg, y_deg, z_deg)}} as pose rotations."""
    arm.animation_data_create()
    act = bpy.data.actions.new(action)
    arm.animation_data.action = act
    for f, pose in frames.items():
        for bname, (x, y, z) in pose.items():
            pb = arm.pose.bones[bname]
            pb.rotation_mode = "XYZ"
            pb.rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
            pb.keyframe_insert("rotation_euler", frame=f)
    track = arm.animation_data.nla_tracks.new()
    track.name = action
    track.strips.new(action, 1, act)
    arm.animation_data.action = None


def generate(params):
    seg = int(params.get("segments", 12))
    tunic, skin, hair = params.get("tunic", "wool_brown"), params.get("skin", "skin_3"), params.get("hair", "hair_brown")
    parts = []

    def part(obj, bone, swatch):
        fsart.paint(obj, swatch)
        _bind(obj, bone)
        parts.append(obj)

    part(_limb((0, 0, 0.92), (0, 0, 1.30), 0.17, 0.15, seg, 4), "spine", tunic)
    part(_limb((0, 0, 1.30), (0, 0, 1.48), 0.19, 0.12, seg, 3), "chest", tunic)
    part(_limb((0, 0, 1.48), (0, 0, 1.58), 0.05, 0.05, seg // 2, 1), "neck", skin)
    part(_sphere((0, 0, 1.66), 0.11, seg, seg // 2 + 2), "head", skin)
    part(_sphere((0, 0.01, 1.70), 0.115, seg, seg // 2), "head", hair)
    for side, s in (("L", 1), ("R", -1)):
        part(_limb((0.20 * s, 0, 1.44), (0.22 * s, 0, 1.16), 0.055, 0.045, seg, 3), f"upper_arm.{side}", tunic)
        part(_limb((0.22 * s, 0, 1.16), (0.23 * s, 0, 0.92), 0.045, 0.035, seg, 3), f"lower_arm.{side}", skin)
        part(_sphere((0.23 * s, 0, 0.88), 0.045, seg // 2 + 2, seg // 3 + 2), f"hand.{side}", skin)
        part(_limb((0.10 * s, 0, 0.95), (0.10 * s, 0, 0.52), 0.085, 0.06, seg, 4), f"thigh.{side}", "wool_grey")
        part(_limb((0.10 * s, 0, 0.52), (0.10 * s, 0, 0.08), 0.06, 0.045, seg, 4), f"shin.{side}", "wool_grey")
        part(_limb((0.10 * s, 0.02, 0.04), (0.10 * s, -0.16, 0.04), 0.05, 0.045, seg // 2 + 2, 2), f"foot.{side}", "bark_dark")

    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    mesh = bpy.context.object
    variant = params.get("variant", "a")
    mesh.name = mesh.data.name = f"char_humanoid_{variant}_lod0"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    arm = _armature(f"char_humanoid_{variant}_rig")
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    mesh.parent = arm

    # In-place cycles at 24 fps: walk 1 s (two steps), idle 2 s (breath and a small sway).
    walk = {}
    for f, ph in ((1, 0), (7, 1), (13, 2), (19, 3), (25, 0)):
        sw = (30, 0, -30, 0, 30)[ph]
        walk[f] = {
            "thigh.L": (sw, 0, 0), "thigh.R": (-sw, 0, 0),
            "shin.L": (-25 if ph == 1 else -5, 0, 0), "shin.R": (-25 if ph == 3 else -5, 0, 0),
            "upper_arm.L": (-sw * 0.7, 0, 0), "upper_arm.R": (sw * 0.7, 0, 0),
            "lower_arm.L": (-15, 0, 0), "lower_arm.R": (-15, 0, 0),
            "spine": (3, 0, sw * 0.15), "hips": (0, 0, -sw * 0.1),
        }
    _key(arm, "walk", walk)
    idle = {f: {"chest": (1.5 * math.sin(f / 48 * math.tau), 0, 0), "spine": (0, 0, 1.0 * math.sin(f / 48 * math.tau)),
                "head": (0, 0, 3 * math.sin(f / 48 * math.tau + 1))} for f in (1, 13, 25, 37, 49)}
    _key(arm, "idle", idle)
    return [arm, mesh]
