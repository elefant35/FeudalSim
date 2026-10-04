Human animation generators and evidence
=======================================
Run these commands from the FeudalSim repository root. Blender executable used:
/Applications/Blender.app/Contents/MacOS/Blender (5.1.0).
The generators are original hand-authored semantic poses sampled on a 30fps grid.
All six animation-only GLBs carry the same complete 40-bone skeleton as the bodies.
Root is fixed and Hips horizontal translation is fixed. Vertical articulation
must be retained for grounded kneel/sit/lie/collapse and small gait posture changes.

Rebuild the complete delivered P0/P1 catalog (41 clips, five files):
  Blender -b --factory-startup -P art/generators/animations/build_extended.py
P0-only historical rebuild (27 clips; overwrites the five P1-extended files):
  Blender -b --factory-startup -P art/generators/animations/build_sets.py
Rebuild P2 (nine clips, sixth file):
  Blender -b --factory-startup -P tools/art/export.py -- art/generators/animations/combat_motion.py game/assets/characters/anims/combat_basic.glb '{}'

Direct exported GLB data audits (exact clip names, time grid, loop closure, Root,
Hips horizontal, skeleton cardinality; output under ignored art/previews):
  python3 art/generators/animations/measure_extended.py
  python3 art/generators/animations/measure_combat.py
  python3 art/generators/animations/compare_committed.py <P0-baseline-revision>
The preservation audit compares every original clip channel/time/value against
Git/LFS bytes, rather than asserting that a generator's source stayed unchanged.
Run repository check.py with budget class animation on each exported GLB:
  Blender -b --factory-startup -P tools/art/check.py -- game/assets/characters/anims/combat_basic.glb animation

Character-attached three-phase samples (body from settlers generator, camera
located at animated eyes, head/hair/beard/headwear hidden for first-person):
  Blender -b --factory-startup -P art/generators/animations/render_samples.py
  Blender -b --factory-startup -P art/generators/animations/render_extended.py
  Blender -b --factory-startup -P art/generators/animations/render_combat.py
Numerical wrist camera/frustum and actual solved-bone stance evidence:
  Blender -b --factory-startup -P art/generators/animations/audit_pose.py
  Blender -b --factory-startup -P art/generators/animations/audit_extended_pose.py
  Blender -b --factory-startup -P art/generators/animations/audit_combat_pose.py

Godot probes are read-only and do not import/modify project resource files:
  /Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path game --script ../art/generators/animations/godot_probe.gd -- --combat
  /Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path game --script ../art/generators/animations/godot_forward_probe.gd
  /Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path game --script ../art/generators/animations/godot_heading_audit.gd
Forward probe measures imported eye, nose and toe direction (+Z in Godot) and
actual left-foot walk stance speed (-1.6m/s relative to the body). Heading audit
uses Godot Basis, actual body landmarks and current bridge source hash/snippets,
then tests eight cardinal/diagonal movement+idle directions and eight camera
yaws. Legacy Pi movement offsets give heading.dot(velocity)=-1 (moonwalking);
correct atan2(dx,dz) gives +1. Simulation yaw is atan2(dx,-dz), so an idle M2 body's
visual yaw is Pi-simYaw. Camera yaw has the opposite convention to simulation yaw.
These scripts provide art-side evidence only; the game code is separately owned.

All deliveries remain review status pending human approval. Rendered evidence is
ignored locally; source scripts and rebuild instructions are tracked for reuse.
