# M2 Terrain3D texture family

`terrain.py` builds eight original procedural terrain layers. It uses only NumPy,
Pillow and the repo palette; no external assets or services. The fixed layer seed
and periodic noise/stamp coordinates make every output reproducible and tileable.

Run from the repo root with a Python that provides NumPy and Pillow:

```sh
python3 art/generators/terrain/terrain.py
python3 art/generators/terrain/terrain.py --check
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P art/generators/terrain/preview.py -- game/assets/terrain
python3 art/generators/terrain/terrain.py --check
```

The default output is `game/assets/terrain`. `--output <directory>` can prepare a
draft away from the shipped path. `--manifest <path>` writes provenance only after
the build passes its texture checks. Image evidence goes in gitignored
`art/previews/terrain`: an albedo contact sheet (2 × 2 repeats over 4 m),
packed-channel sheet, and walking renders. JSON measurements including each output's
SHA-256 stay beside this generator in `review/checks.json`.
The Blender preview script renders packed materials on a 30 m ground plane with a
camera at eye height (1.7 m) and 2 m repeats. A later `--check` adds those renders
to the walking-height contact sheet. Render files are evidence, not game assets.

Each layer represents a 2 m square, sampled at 1024 × 1024 pixels. RGB albedo is
sRGB; albedo alpha is linear height. Normal RGB is a tangent-space OpenGL (+Y)
normal and normal alpha is linear roughness. Since image rows increase downward,
green stores +d(height)/d(image-row); red stores -d(height)/d(image-column).
Normals are derived from the height field with wrapped central differences and
an explicitly recorded, conservative relief depth per layer. They carry no baked
directional lighting. Terrain3D should interpret the normal texture as linear.

The checker validates RGBA packing, size, channel variation, positive normal Z,
unit normal length after 8-bit quantization, and wrap-edge differences against
interior neighboring pixels. Edge pixels need not be identical: they sample
distinct texel centers in a periodic field. The manifest schema has no texture
measurement fields, so texture measurements are in the report and manifest notes;
`tools/art/check.py` and the mesh silhouette tool are inapplicable to PNG layers.

The textures are a painterly first pass for owner review. The foliage layers use
muted palette greens, with pale earth-colored grasses in coastal rough grass and
green/brown heather undergrowth. The full moor palette is deliberately grounded in
the contract's earth/foliage/stone rows. Large-scale terrain biome mixing and fog
break repetition in-engine; the contact sheet permits inspection of this 2 m base
tile before that blending.
