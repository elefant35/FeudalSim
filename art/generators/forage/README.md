# P1 forage and fruiting bushes

Original low-poly procedural models for every plant node in `content/nodes/plants.yaml`
except P0 reeds, and the seven P1 bush species: two variants each, 70 GLBs total.
The generator uses metre-space geometry, flat palette UVs, one shared material,
and anchored vertex-color R wind. Mushrooms are rigid and have zero wind throughout.

```sh
python3 art/generators/forage/build.py --prepare-only
python3 art/generators/forage/build.py --workers 2
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P art/generators/forage/lookalikes.py
python3 art/generators/forage/check_export.py
python3 art/generators/forage/review.py
```

Use a Python with NumPy and Pillow. Blender 5.1.0 performs export, mesh checks and
previews through the repo tools. `--prepare-only` builds meshes in memory and logs
their triangles and bounds without exporting. The full build exports independent
variants concurrently, preserves deterministic seeds, and writes the review-only
manifest `content/assets/forage_m2.yaml` after every mesh passes its check.
PNG previews and logs go in gitignored `art/previews/forage`; measurements are text
files next to this generator. `check_export.py` reads the actual GLB color
accessors and checks complete node/variant coverage. The lookalike renders inspect
nearby leaves, stems, flowers and fruit; a separate low camera shows mushroom gills
and stem-base cups. Only the owner can approve the final look.

Botanical fact references and the modeled tells are in [SOURCES.md](SOURCES.md).
No borrowed imagery or mesh assets are used. The families intentionally share
shapes at a distance, with structural tells in the close view. Flowers/fruit are
slightly exaggerated to remain readable in the game; they are separate named
parts (`<category>_<species>_<variant>_flowers` / `_fruit`) for seasonal hiding.
Mushroom caps, gills, stem rings and basal cups are fixed parts of the main mesh.
Hemlock's existing-palette blotches are muted blue instead of true purple. The
model keeps the spotted pattern; foxglove/comfrey colors are palette approximations.

Simplifications: the moss cushion represents several small sphagnum shoots;
meadowsweet plumes are simple cream faceted clusters; young hop/pea vines are
modeled as self-supporting low patches without a generated tree or fence; seeds,
needles, leaf hairs and gill edges use a few broad readable shapes rather than fine
surface detail. Flowers and fruit may appear together in the authoring preview;
runtime seasonal visibility belongs to the code agent. No collision is included.
