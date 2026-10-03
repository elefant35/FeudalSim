"""Run a generator and export its LODs to .glb (32 §6 steps 2-4).

Usage: Blender -b --factory-startup -P tools/art/export.py -- <generator.py> <out.glb> [json-params]
The generator module must define generate(params) -> list of LOD objects (lod0 first).
"""
import importlib.util, json, pathlib, sys

sys.path.insert(0, str(pathlib.Path(__file__).parent))
import fsart  # noqa: E402

gen_path, out_path, *rest = fsart.args()
params = json.loads(rest[0]) if rest else {}
spec = importlib.util.spec_from_file_location("generator", gen_path)
gen = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gen)

fsart.reset()
lods = gen.generate(params)
fsart.export_glb(out_path, lods)
fsart.result(ok=True, out=out_path, objects=[o.name for o in lods], tris=[fsart.triangles(o) for o in lods],
             params=params, generator=gen_path)
