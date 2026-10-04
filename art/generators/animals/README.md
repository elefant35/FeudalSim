# Island fauna

Original FeudalSim procedural geometry and authored motion. Each species has a
28-bone skeleton, a single palette material and mesh, and 30 fps in-place clips.
The animal rigs use their own proportions, rather than the human hierarchy.
Root stays at the floor and never moves. Pelvis X/Y remain static; vertical
posture follows the gait and ground contact. Stag antlers, boar tusks, hare ears,
canid ruffs/tails, bear claws and goat horns/beard define the species silhouettes.

Run through the normal exporter, for example:

```sh
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P tools/art/export.py -- art/generators/animals/animals.py game/assets/animals/red_deer_stag.glb '{"species":"red_deer_stag"}'
```

`build.py` exports species in brief order, optionally filtered by arguments.
`review.py` imports each delivered file, runs the shared contract check and
preview, then renders motion sheets. Rows are walk, run, idle/graze/alert/hit,
death, sleep, and attack (or four grazing poses for non-attacking animals).
`audit.py` is a normal Python script checking delivered skeleton size, required
clip names, 30 fps sample alignment, loop closure, Root invariance and Pelvis
horizontal invariance. `ground_audit.py` runs in Blender and measures the actual
imported skinned surfaces at three-frame intervals through every clip.

All animals have idle, walk, run, graze_loop, alert, flee, hit, death, sleep_loop;
boar and wolf add attack. Flee uses the corresponding run gait. Walk is a planted
four-beat gait; boar runs with diagonal pairs and hare bounds, while deer/canids
use offset fore/hind contacts. These original actions are deliberately simple
and compatible with the flat silhouettes, not captured movement.

Outputs are `review`. Only the owner approves appearance. Measurements live in
this folder; turntables, silhouettes and motion sheets are under
`art/previews/animals`. Palette-only meshes have no cloth wind attributes.
