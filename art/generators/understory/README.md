# M2 understory family (P0.6)

`understory.py` produces 17 original seeded variants: hazel ×3, bramble ×2,
gorse ×2, reeds ×3, grass tufts ×4 and bracken fern ×3. It uses the repo's
Blender primitives and palette UVs. No source models, photos or services are used.

```sh
python3 art/generators/understory/build.py  # Python with NumPy and Pillow
```

The build runs the repo's export, check and turntable/silhouette tools, writes a
review manifest at `content/assets/understory_m2.yaml`, and stores checks as text
in `checks.json`. `check_wind.py` also reads exported COLOR_0 accessors to confirm
the wind channel survived export, and `review.py` assembles visual sheets.
Logs and PNG previews go in gitignored `art/previews/understory`.
All plants use one palette material and have no collision. Broadleaf hazel uses
coppice stems and overlapping crowns; bramble uses low arching canes, broad leaves
and dark fruit; gorse uses needle sprays and straw-yellow blossoms. Reeds have
tall culms, long leaves and seed heads. Ferns have six arching rachises with paired
triangular pinnae. Each grass tuft has sixteen folded blades (48 triangles).

Vertex-color R stores wind weight. Stems are anchored at the ground; leaf tips
increase to 1 with a local blade gradient. No vertex color is used as albedo.
Each bramble GLB has a separate `bush_bramble_<variant>_berries` object so the game
can hide the fruit outside summer. Seasonal hiding and plant placement belong to
the code agent. The manifest remains `status: review` until the owner approves.
