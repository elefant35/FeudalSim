"""Rebuild all five P0 human animation libraries with repo fsart export."""
import pathlib,sys
ROOT=pathlib.Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'tools/art'))
sys.path.insert(0,str(pathlib.Path(__file__).parent))
import bpy,fsart,human_motion
for family in human_motion.SETS:
 fsart.reset(); objects=human_motion.generate({'set':family})
 out=ROOT/'game/assets/characters/anims'/f'{family}.glb'
 fsart.export_glb(out,objects)
 fsart.result(ok=True,out=str(out),clips=list(human_motion.SETS[family]),fps=30,root_motion=False)
