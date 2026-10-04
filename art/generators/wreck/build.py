"""Rebuild the ten-file wreck family inside Blender; review.py runs contract checks."""
import pathlib,sys,json,importlib.util
ROOT=pathlib.Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'tools/art'));import fsart
sys.path.insert(0,str(pathlib.Path(__file__).parent));from wreck import generate
items={'hull_bow':'building_medium','hull_stern':'building_medium','mast_broken':'workstation','rigging_tangle':'workstation','reef_rocks':'rock','flotsam_planks':'prop_handheld','flotsam_barrel':'prop_handheld','flotsam_crate':'prop_handheld','rope_coil':'prop_handheld','sail_heap':'prop_handheld'}
records={}
for item,cls in items.items():
 fsart.reset();objs=generate({'item':item});path=ROOT/f'game/assets/wreck/{item}.glb';fsart.export_glb(path,objs)
 records[item]={'tris':sum(fsart.triangles(o) for o in objs if not o.name.endswith(('-colonly','-convcolonly'))),'budget_class':cls,'materials':1}
fsart.result(ok=True,assets=records)
(ROOT/'art/generators/wreck/checks.json').write_text(json.dumps(records,indent=2)+'\n')
