"""Rebuild P0+P1 libraries; use only after P0 delivery, preserving original clips.
Blender -b --factory-startup -P art/generators/animations/build_extended.py
"""
import pathlib,sys,json
ROOT=pathlib.Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'tools/art'));sys.path.insert(0,str(pathlib.Path(__file__).parent))
import fsart,human_motion_extended as motion
for family in motion.base.SETS:
 fsart.reset();objects=motion.generate({'set':family});out=ROOT/'game/assets/characters/anims'/f'{family}.glb'
 fsart.export_glb(out,objects);fsart.result(ok=True,out=str(out),clips=list(motion.base.SETS[family])+list(motion.P1.get(family,{})),fps=30)
