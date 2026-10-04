"""Export selected species in brief order; run review.py after each family."""
import pathlib,sys,json
ROOT=pathlib.Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'tools/art'));sys.path.insert(0,str(pathlib.Path(__file__).parent))
import fsart,animals
names=fsart.args() or list(animals.PROFILES);record={}
for species in names:
 fsart.reset();objects=animals.generate({'species':species});dest=ROOT/f'game/assets/animals/{species}.glb';fsart.export_glb(dest,objects)
 record[species]={'triangles':sum(fsart.triangles(o) for o in objects),'bones':len(objects[0].data.bones),'clips':[a.name for a in __import__('bpy').data.actions]}
 print('SPECIES',species,record[species],flush=True)
path=ROOT/'art/generators/animals/build_counts.json'
old=json.loads(path.read_text()) if path.exists() else {};old.update(record);path.write_text(json.dumps(old,indent=2)+'\n')
fsart.result(ok=True,build_counts=record)
