# Modular Varrow settlers

Original FeudalSim procedural geometry. Export with the repo exporter:

```sh
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P tools/art/export.py -- art/generators/settlers/p1.py game/assets/characters/body_male.glb '{"sex":"male"}'
```

Use `female` or `child` and its corresponding destination for the other libraries.
`settlers.py` remains the reproducible P0 generator; `p1.py` appends two adult head
variants and four clothing layers, preserving every P0 modular object name.

`skeleton.py` defines `BONES` as `(parent, head, tail)` tuples and provides
`create_skeleton(scale=1.0)`. It implements the contract's literal hierarchy:
38 listed bones plus two non-deform hand sockets, 40 total. The brief's stated
37 count does not match its complete list. There are no control bones or extra
exported rig bones. Root never deforms or moves in clips.

The child uses the same names and hierarchy. `child_point` maps the common rest
coordinates into child proportions: about 1.235m, larger head and shorter limbs.
Retarget shared actions by local bone rotations; use the child rest lengths for
translation scale and foot placement. Do not run the adult-space authoring IK
function directly on a child's rest rig; its metre-space targets are for adults.

Show one Head, one Hair, at most one Beard and one upper hood. The wool/fur/oiled
cloaks and sailcloth poncho are alternatives. The hide jerkin is optional and is
counted conservatively in the largest assembled budget. `Headwear_wool_hood` is
hidden in first person; `Cloth_wool_hood` is its shoulder cape and stays visible.

Cloth vertex COLOR_0.R stores wind weights, anchored 0 to free hem 1. A standard
glTF material multiplies that data into albedo; the game's wind material must
sample the palette separately. The repo preview removes the standard vertex
tint for appearance review while preserving the exported data.

Run `review_p1.py` in Blender for contract audits, all heads/expressions, all four
outfits on each body, silhouettes and a child motion sheet. `review_fp_p1.py`
renders six first-person work checks across each outer-layer alternative, with
three poses per check and the player head modules hidden. Generated evidence
is under `art/previews/settlers`; `audit_p1.json` records imported-file checks.
The preview script can also select `--head`, `--hair`, and `--outercloak` indices.
All exported assets are `review`; only the owner can approve their appearance.
