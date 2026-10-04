#!/usr/bin/env python3
"""Original M2 terrain: periodic painterly albedo/height and OpenGL normal/roughness."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[3]
SIZE = 1024
LAYERS = {
    "sand": (3101, .018, .86, {"sand": .91, "soil": .09}),
    "shingle": (3102, .028, .78, {"granite": .64, "sand": .20, "flint": .16}),
    "grass": (3103, .022, .94, {"grass": .68, "moss": .25, "soil": .07}),
    "grass_rough": (3104, .040, .94, {"grass": .42, "moss": .22, "sand": .36}),
    "forest_floor": (3105, .040, .92, {"soil": .64, "bark": .22, "moss": .14}),
    "heather_moor": (3106, .040, .95, {"moss": .51, "leaf_dark": .21, "soil": .28}),
    "mud": (3107, .018, .54, {"soil": .63, "soil_dark": .32, "clay": .05}),
    "rock": (3108, .045, .84, {"granite": .81, "granite_dark": .16, "limestone": .03}),
}
PALETTE = {s["name"]: np.array(tuple(bytes.fromhex(s["hex"][1:])), dtype=float)
           for s in json.loads((ROOT / "art/palettes/palette.json").read_text())["swatches"]}


def noise(rng: np.random.Generator, scale: float) -> np.ndarray:
    """Gaussian-filtered noise on a torus; no seam crossfade or edge flattening."""
    f = np.fft.fftfreq(SIZE)
    weight = np.exp(-2 * np.pi ** 2 * scale ** 2 * (f[:, None] ** 2 + f[None, :] ** 2))
    n = np.fft.ifft2(np.fft.fft2(rng.standard_normal((SIZE, SIZE))) * weight).real
    return np.clip(n / n.std(), -2.8, 2.8) / 2.8


def stamps(rng, rgb, height, count, radii, colors, color_mix, relief, sharp=False):
    """Wrapped irregular brush dabs; each edge-spanning dab writes opposite edges."""
    for _ in range(count):
        cx, cy = rng.integers(0, SIZE, 2)
        rx = rng.uniform(*radii[0]); ry = rng.uniform(*radii[1])
        angle = rng.uniform(0, 2 * np.pi)
        radius = int(np.ceil(max(rx, ry) * 1.2))
        yy, xx = np.mgrid[-radius:radius + 1, -radius:radius + 1]
        u = (xx * np.cos(angle) + yy * np.sin(angle)) / rx
        v = (-xx * np.sin(angle) + yy * np.cos(angle)) / ry
        d = u * u + v * v
        # Smooth edges, asymmetric brush interior, no directional baked light.
        mask = np.clip((1.0 - d) * (5 if sharp else 2), 0, 1)
        bump = np.clip(1.0 - d, 0, 1) ** .65
        iy = (yy + cy) % SIZE; ix = (xx + cx) % SIZE
        color = PALETTE[colors[int(rng.integers(len(colors)))]]
        target = color * rng.uniform(.96, 1.04)
        a = mask[..., None] * color_mix
        rgb[iy, ix] = rgb[iy, ix] * (1 - a) + target * a
        height[iy, ix] += bump * relief * rng.uniform(.7, 1.1)


def build_layer(layer):
    seed, depth, roughness, palette_mix = LAYERS[layer]
    rng = np.random.default_rng(seed)
    coarse = noise(rng, 34); mid = noise(rng, 9); fine = noise(rng, 1.7)
    base = sum(PALETTE[name] * weight for name, weight in palette_mix.items())
    rgb = base[None, None, :] + (coarse * 5 + mid * 3 + fine * 1.4)[..., None]
    height = .48 + coarse * .10 + mid * .055 + fine * .020
    y, x = np.mgrid[:SIZE, :SIZE] / SIZE
    if layer == "sand":
        ripples = np.sin(2 * np.pi * (19 * y + 2 * x) + coarse * 1.2)
        height += ripples * .025
        rgb += ripples[..., None] * 1.4
        stamps(rng, rgb, height, 950, ((1, 3), (1, 2)), ["sand", "granite"], .09, .006)
    elif layer == "shingle":
        stamps(rng, rgb, height, 1450, ((7, 23), (5, 16)),
               ["granite", "granite_dark", "sand", "flint", "limestone"], .26, .17, True)
        stamps(rng, rgb, height, 1800, ((2, 5), (2, 4)),
               ["granite", "flint", "limestone"], .12, .04)
    elif layer in ("grass", "grass_rough"):
        if layer == "grass_rough":
            height += noise(rng, 22) * .14
        stamps(rng, rgb, height, 14000, ((.65, 1.4), (4, 14)),
               ["grass", "leaf_light", "moss", "sand"] if layer == "grass_rough"
               else ["grass", "leaf_light", "moss"], .17, .020)
    elif layer == "forest_floor":
        # Small subdued leaf marks and longer pine needles mixed with moss dabs.
        stamps(rng, rgb, height, 2200, ((2, 6), (4, 10)),
               ["bark", "soil", "wood_light", "moss"], .21, .032)
        stamps(rng, rgb, height, 2600, ((.5, 1), (4, 12)),
               ["bark", "wood_light"], .15, .011)
    elif layer == "heather_moor":
        stamps(rng, rgb, height, 950, ((8, 19), (7, 16)),
               ["moss", "leaf_dark", "soil"], .14, .042)
        stamps(rng, rgb, height, 7600, ((1, 2.5), (2, 5)),
               ["leaf", "moss", "bark", "wood_light"], .14, .012)
    elif layer == "mud":
        pockets = np.maximum(0, noise(rng, 28) - .15)
        height -= pockets * .14
        rgb -= pockets[..., None] * 7
        stamps(rng, rgb, height, 900, ((1, 4), (1, 3)),
               ["soil_dark", "granite_dark"], .10, .011)
    elif layer == "rock":
        # Subtle intersecting granular veins, deformed periodically by noise.
        veins = np.exp(-((np.sin(2 * np.pi * (4 * x + 3 * y) + coarse)) / .075) ** 2)
        veins += .6 * np.exp(-((np.sin(2 * np.pi * (3 * x - 5 * y) + mid * .4)) / .065) ** 2)
        height -= veins * .022
        rgb -= veins[..., None] * 2.5
        stamps(rng, rgb, height, 6800, ((1, 2.4), (1, 2)),
               ["granite", "limestone", "granite_dark"], .09, .008)
    # Softly constrain relief rather than clipping stacked brush marks.
    height = .5 + .35 * np.tanh((height - .5) / .35)
    dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * (depth * SIZE / 4)
    dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * (depth * SIZE / 4)
    normal = np.stack((-dx, dy, np.ones_like(height)), axis=-1)
    normal /= np.linalg.norm(normal, axis=2, keepdims=True)
    rough = np.clip(roughness + mid * .035 + fine * .013, .38, .99)
    if layer == "mud":
        rough -= np.maximum(0, .49 - height) * .65
    albedo_height = np.dstack((np.clip(rgb, 0, 255), height * 255)).round().astype(np.uint8)
    normal_rough = np.dstack(((normal * .5 + .5) * 255, rough * 255)).round().astype(np.uint8)
    return albedo_height, normal_rough


def check(layer, output):
    result = {"layer": layer, "size": [SIZE, SIZE], "tile_m": 2,
              "seed": LAYERS[layer][0], "relief_m": LAYERS[layer][1], "files": {}}
    for suffix in ("albedo_height", "normal_rough"):
        path = output / f"{layer}_{suffix}.png"
        with Image.open(path) as img:
            assert img.size == (SIZE, SIZE) and img.mode == "RGBA", f"Bad packing: {path}"
            p = np.asarray(img).astype(float)
        assert p[..., 3].std() > 1, f"Missing varying packed alpha: {path}"
        # Compare opposite-edge adjacent samples against all interior adjacent samples.
        seam = np.concatenate((abs(p[0] - p[-1]).ravel(), abs(p[:, 0] - p[:, -1]).ravel()))
        interior = np.concatenate((abs(np.diff(p, axis=0)).ravel(), abs(np.diff(p, axis=1)).ravel()))
        ratio = float(seam.mean() / max(.01, interior.mean()))
        assert ratio < 1.6, f"Seam discontinuity: {path} {ratio}"
        stats = {"sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                 "alpha_range": [int(p[..., 3].min()), int(p[..., 3].max())],
                 "wrap_difference_mean": round(float(seam.mean()), 5),
                 "interior_difference_mean": round(float(interior.mean()), 5),
                 "wrap_to_interior_ratio": round(ratio, 5)}
        if suffix == "normal_rough":
            n = p[..., :3] / 255 * 2 - 1
            error = np.abs(np.linalg.norm(n, axis=2) - 1)
            assert error.max() < .008 and n[..., 2].min() > .5, f"Invalid normals: {path}"
            stats["normal_length_max_error"] = round(float(error.max()), 6)
            stats["normal_z_min"] = round(float(n[..., 2].min()), 5)
        else:
            stats["rgb_mean"] = [round(float(v), 2) for v in p[..., :3].mean(axis=(0, 1))]
            stats["rgb_std"] = [round(float(v), 2) for v in p[..., :3].std(axis=(0, 1))]
            assert p[..., :3].std(axis=(0, 1)).max() < 18, f"High contrast: {path}"
        result["files"][path.name] = stats
    return result


def previews(output, review):
    review.mkdir(parents=True, exist_ok=True)
    contact = Image.new("RGB", (2080, 1080), "#242a28")
    channels = Image.new("RGB", (1500, 8 * 200), "#242a28")
    cdraw = ImageDraw.Draw(contact); pdraw = ImageDraw.Draw(channels)
    for i, layer in enumerate(LAYERS):
        albedo = Image.open(output / f"{layer}_albedo_height.png")
        normal = Image.open(output / f"{layer}_normal_rough.png")
        thumb = albedo.convert("RGB").resize((240, 240), Image.Resampling.LANCZOS)
        tile = Image.new("RGB", (480, 480))
        for tx in (0, 240):
            for ty in (0, 240): tile.paste(thumb, (tx, ty))
        # Keep the texels square: four columns by two rows, each a 4 m square.
        cx = 20 + (i % 4) * 520; cy = 25 + (i // 4) * 540
        contact.paste(tile, (cx, cy)); cdraw.text((cx, cy - 17), layer + " | 2 x 2 repeats", fill="white")
        a = np.asarray(albedo).astype(float); n = np.asarray(normal).astype(float)
        light = n[..., :3] / 255 * 2 - 1
        light = np.maximum(0, (light * np.array([-.35, .4, .847])).sum(axis=2))
        shaded = Image.fromarray(np.clip(a[..., :3] * (.25 + .75 * light[..., None]), 0, 255).astype(np.uint8))
        panels = [albedo.convert("RGB"), albedo.getchannel("A").convert("RGB"),
                  normal.convert("RGB"), normal.getchannel("A").convert("RGB"), shaded, tile]
        labels = ["albedo", "height", "normal +Y", "roughness", "preview light", "repeat"]
        for j, panel in enumerate(panels):
            channels.paste(panel.resize((165, 165), Image.Resampling.LANCZOS), (j * 250 + 5, i * 200 + 25))
            pdraw.text((j * 250 + 5, i * 200 + 6), layer + " / " + labels[j], fill="white")
    contact.save(review / "terrain_tiled_contact.png")
    channels.save(review / "terrain_channels_contact.png")
    if all((review / (name + "_walking.png")).exists() for name in LAYERS):
        walking = Image.new("RGB", (1280, 530), "#242a28")
        draw = ImageDraw.Draw(walking)
        for i, name in enumerate(LAYERS):
            x = (i % 4) * 320; y = (i // 4) * 265
            panel = Image.open(review / (name + "_walking.png")).convert("RGB")
            walking.paste(panel.resize((320, 240), Image.Resampling.LANCZOS), (x, y + 25))
            draw.text((x + 8, y + 8), name + " | eye 1.7m / 2m tile", fill="white")
        walking.save(review / "terrain_walking_contact.png")


def manifest(path):
    lines = ["# yaml-language-server: $schema=../schemas/asset.schema.json",
             "# Original procedural P0.3 terrain; packed-channel measurements in art/generators/terrain/review/checks.json."]
    for layer, (seed, depth, _, mix) in LAYERS.items():
        lines += [f"- id: asset.terrain.{layer}", "  kind: texture", "  status: review", "  milestone: M2",
                  "  source:", "    type: generator", "    generator: art/generators/terrain/terrain.py",
                  f"    params: {{ layer: {layer}, seed: {seed}, size: 1024, tile_m: 2 }}",
                  "    tool: Python 3 with NumPy and Pillow",
                  f"  outputs: [game/assets/terrain/{layer}_albedo_height.png, game/assets/terrain/{layer}_normal_rough.png]",
                  "  measured: {}",
                  "  license: { name: Proprietary-own-work, author: FeudalSim, attribution_required: false }",
                  "  notes: " + json.dumps(f"P0.3; two tileable 1024x1024 RGBA maps over 2m. RGB albedo/A height; OpenGL RGB normal/A roughness. Relief {depth}m. Palette-derived {', '.join(mix)}. Texture checker and tiled previews pass; owner look review pending.")]
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n")


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--output", type=Path, default=ROOT / "game/assets/terrain")
    ap.add_argument("--review", type=Path, default=ROOT / "art/previews/terrain")
    ap.add_argument("--manifest", type=Path)
    ap.add_argument("--check", action="store_true", help="Check existing files without regenerating")
    args = ap.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    reports = []
    for layer in LAYERS:
        if not args.check:
            a, n = build_layer(layer)
            Image.fromarray(a).save(args.output / f"{layer}_albedo_height.png", compress_level=9)
            Image.fromarray(n).save(args.output / f"{layer}_normal_rough.png", compress_level=9)
        reports.append(check(layer, args.output))
        print("RESULT " + json.dumps({"layer": layer, "check": "pass"}), flush=True)
    args.review.mkdir(parents=True, exist_ok=True)
    evidence = ROOT / "art/generators/terrain/review"
    evidence.mkdir(parents=True, exist_ok=True)
    (evidence / "checks.json").write_text(json.dumps({"result": "pass", "layers": reports}, indent=2) + "\n")
    previews(args.output, args.review)
    if args.manifest: manifest(args.manifest)


if __name__ == "__main__": main()
