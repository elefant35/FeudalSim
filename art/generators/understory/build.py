#!/usr/bin/env python3
"""Export/check/preview the original 17-variant P0.6 family; review-only manifest."""
import json
from pathlib import Path
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[3]
B="/Applications/Blender.app/Contents/MacOS/Blender"
GEN=ROOT/"art/generators/understory/understory.py"
OUT=ROOT/"art/previews/understory"
OUT.mkdir(parents=True,exist_ok=True)
SPEC={"hazel":3,"bramble":2,"gorse":2,"reeds":3,"grass_tuft":4,"fern":3}


def call(script,args,log):
    p=subprocess.run([B,"-b","--factory-startup","-P",str(ROOT/script),"--",*args],cwd=ROOT,capture_output=True,text=True)
    (OUT/log).write_text(p.stdout+p.stderr)
    if p.returncode:
        raise RuntimeError(f"{script} failed ({p.returncode}); see {OUT/log}")
    records=[json.loads(s[7:]) for s in p.stdout.splitlines() if s.startswith("RESULT ")]
    if not records or not records[-1].get("ok",False):
        raise RuntimeError(f"{script} did not report a pass; see {OUT/log}")
    return records[-1]


def main():
    reports=[]; manifest=["# yaml-language-server: $schema=../schemas/asset.schema.json"]
    for family_idx,(species,count) in enumerate(SPEC.items()):
        for i in range(count):
            variant=chr(97+i); stem=species+"_"+variant
            folder="bushes" if species in ("hazel","bramble","gorse") else "plants"
            relative=f"game/assets/flora/{folder}/{stem}.glb"
            params={"species":species,"variant":variant,"seed":6001+family_idx*100+i}
            call("tools/art/export.py",[str(GEN),str(ROOT/relative),json.dumps(params)],stem+"_export.log")
            report=call("tools/art/check.py",[str(ROOT/relative),"shrub"],stem+"_check.log")
            if species=="grass_tuft" and report["tris"]["0"]>60:
                raise RuntimeError("grass exceeds its 60-triangle brief")
            call("tools/art/preview.py",[str(ROOT/relative),str(OUT/stem)],stem+"_preview.log")
            reports.append(report)
            notes={
                "hazel":"Multistem coppice shrub, 3.5-4.7m, broad lobed foliage; no collision.",
                "bramble":"Low arched tangled canes with broad leaves and separate bush_bramble_<variant>_berries part for seasonal hiding; no collision.",
                "gorse":"Dark needle sprays in a spiky dome with muted straw-yellow flowers; no collision.",
                "reeds":"Dense wet-ground culms, long leaves and brown seed heads, 1.7-2.34m; no collision.",
                "grass_tuft":"48-triangle folded-blade grass clump, 0.22-0.58m; no collision.",
                "fern":"Six arching bracken fronds with paired tapering pinnae, 0.65-1.1m; no collision.",
            }[species]+" Palette material, vertex R anchored at ground and weighted foliage tips. Checked export and turntable/silhouette previews; owner look review pending."
            manifest += [f"- id: asset.flora.{stem}","  kind: model","  status: review","  milestone: M2",
                         "  source:","    type: generator","    generator: art/generators/understory/understory.py",
                         "    params: "+json.dumps(params),"    tool: Blender 5.1.0 (headless)",
                         "  outputs: ["+relative+"]","  budget_class: shrub",
                         "  measured: { tris_lod0: "+str(report["tris"]["0"])+", materials: "+str(len(report["materials"]))+" }",
                         "  license: { name: Proprietary-own-work, author: FeudalSim, attribution_required: false }",
                         "  notes: "+json.dumps(notes)]
            print("RESULT "+json.dumps({"asset":stem,"check":"pass","tris":report["tris"]["0"]}),flush=True)
    (GEN.parent/"checks.json").write_text(json.dumps({"result":"pass","assets":reports},indent=2)+"\n")
    (ROOT/"content/assets/understory_m2.yaml").write_text("\n".join(manifest)+"\n")
    subprocess.run([sys.executable,str(GEN.parent/'check_wind.py')],check=True,cwd=ROOT)
    subprocess.run([sys.executable,str(GEN.parent/'review.py')],check=True,cwd=ROOT)


if __name__=="__main__":main()
