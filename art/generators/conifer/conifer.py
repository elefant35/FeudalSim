"""Conifer generator (stylized low-poly pine): the M0 test asset for the art pipeline (32 §6, M0-A4).

generate(params) -> [lod0, lod1]. Params: variant (str), seed (int), tiers (int), sides (int),
height_m (float), foliage (palette swatch). Deterministic for a given params dict.
"""
import math
import random

import bpy

import fsart


def _cone(radius, depth, z, sides, rot):
    bpy.ops.mesh.primitive_cone_add(vertices=sides, radius1=radius, radius2=0.0, depth=depth, location=(0, 0, z))
    o = bpy.context.object
    o.rotation_euler[2] = rot
    return o


def _cylinder(radius, depth, sides):
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius, depth=depth, location=(0, 0, depth / 2))
    return bpy.context.object


def _join(parts, name):
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = obj.data.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj


def _build(params, sides, trunk_sides, tiers, name, rng):
    h = float(params.get("height_m", 3.7))
    trunk_h = h * 0.32
    trunk = _cylinder(h * 0.05, trunk_h, trunk_sides)
    fsart.paint(trunk, "bark")
    parts = [trunk]
    for i in range(tiers):
        t = i / max(1, tiers - 1)
        radius = h * (0.36 - 0.18 * t) * rng.uniform(0.92, 1.08)
        depth = h * 0.38
        z = trunk_h * 0.7 + i * (h - trunk_h * 0.7 - depth / 2) / max(1, tiers - 1) * 0.92 + depth / 2
        cone = _cone(radius, depth, z, sides, rng.uniform(0, math.tau))
        fsart.paint(cone, params.get("foliage", "pine"))
        parts.append(cone)
    return _join(parts, name)


def generate(params):
    variant = params.get("variant", "a")
    seed = int(params.get("seed", 11))
    tiers = int(params.get("tiers", 3))
    sides = int(params.get("sides", 7))
    lod0 = _build(params, sides, 6, tiers, f"flora_pine_{variant}_lod0", random.Random(seed))
    lod1 = _build(params, max(4, sides - 3), 4, max(2, tiers - 1), f"flora_pine_{variant}_lod1", random.Random(seed))
    return [lod0, lod1]
