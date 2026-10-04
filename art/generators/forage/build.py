#!/usr/bin/env python3
"""Measure or export/check/preview 70 original P1 plants and bushes."""
import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import json
from pathlib import Path
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[3]
B='/Applications/Blender.app/Contents/MacOS/Blender'
HERE=Path(__file__).resolve().parent
GEN=HERE/'forage.py'
PREVIEWS=ROOT/'art/previews/forage'
BUSHES=('hawthorn','blackthorn','elder','crab_apple','juniper','dog_rose','bilberry')
REF={
    'wild_carrot':'B01','hemlock':'B02','water_hemlock':'B03',
    'field_mushroom':'B04','death_cap':'B04','fly_agaric':'B04',
    'ramsons':'B05','lily_of_the_valley':'B05','comfrey':'B06','foxglove':'B06',
    'deadly_nightshade':'B07','bilberry':'B07','hawthorn':'B08','blackthorn':'B08',
    'elder':'B09','crab_apple':'B10','juniper':'B11','dog_rose':'B12',
}
TELLS={
    'wild_carrot':'Pale compound umbels, divided foliage, dark central floret, small stem hairs and bracts.',
    'hemlock':'Pale compound umbels and divided foliage; smooth stems with conspicuous woad-blue blotches (palette proxy for purple).',
    'water_hemlock':'Cicuta virosa; pale compound umbels, narrow deeply toothed divided leaflets.',
    'field_mushroom':'Pale cap and dark brown gill underside; thin stem ring; no basal cup.',
    'death_cap':'Similar pale cap with subdued olive center, white gill underside, stem ring and visible basal volva cup.',
    'fly_agaric':'Madder-red cap with pale patches, white underside, ring and basal cup.',
    'ramsons':'Broad basal leaves and clusters of six-point pale star flowers.',
    'lily_of_the_valley':'Broad basal leaves and one-sided arching sprays of five hanging pale bells.',
    'comfrey':'Coarse broad rosette, visible hair accents, short drooping tubular flower clusters.',
    'foxglove':'Similar broad rosette with tall spire of dangling tubular flowers; existing woad/madder substitute purple.',
    'deadly_nightshade':'Branching herb with broad paired upper leaves; dark berries with a large five-point green calyx and dull bell flowers.',
    'bilberry':'Low angular green stems, small ovate leaves and dark berries without the large nightshade calyx.',
    'hawthorn':'Lobed leaves, thorn accents, red haws and pale five-petal blossoms.',
    'blackthorn':'Oval leaves, angular thorn tips, dark sloes and pale five-petal blossoms.',
    'elder':'Pinnate five-leaflet details, pale flowers and dark berry clusters.',
    'crab_apple':'Broad small-tree crown, oval leaves and larger red/gold fruit.',
    'juniper':'Dense upright needle-green crown, pointed needle details and dark berry-cones.',
    'dog_rose':'Arching thorny canes, compound leaves, pale open five-petal flowers and elongated red hips.',
    'yarrow':'Slender stems, finely divided foliage and flat pale flower clusters.',
    'plantain':'Broad basal rosettes with upright dark seed spikes.',
    'feverfew':'Pale daisy petals around small gold centers.',
    'meadowsweet':'Airy cream flower plumes over broad leaflets.',
    'wild_thyme':'Low herb carpet with tiny leaves and small muted pink-proxy flowers.',
    'sphagnum':'Moss cushions and pale star-shaped capitula.',
    'valerian':'Pinnate foliage and pale pink-proxy flower heads.',
    'water_mint':'Opposite serrated leaves and pale flower whorls.',
    'nettle':'Opposite sharply toothed leaves, small stinging hair accents and green flower strands.',
    'sorrel':'Arrow/lobed basal foliage and sparse reddish seed strands.',
    'wild_oats':'Tall blades and loose nodding panicles of pale spikelets.',
    'sea_beet':'Broad wavy leaf rosettes and narrow green flower strands.',
    'wild_cabbage':'Waxy blue-green broad rosettes with small yellow four-petal flowers.',
    'wild_pea':'Twining stems, small paired leaflets, pale pea flowers and green pods.',
    'wild_flax':'Thin upright stems with linear leaves and small five-petal blue flowers.',
    'wild_hops':'Twining stems, lobed leaves and pendant light-green cones.',
    'wild_strawberry':'Trifoliate toothed foliage, pale five-petal flowers and small elongated red fruit.',
}


def call(script,args,log,require_ok=True):
    run=subprocess.run([B,'-b','--factory-startup','-P',str(ROOT/script),'--',*args],cwd=ROOT,capture_output=True,text=True)
    (PREVIEWS/log).write_text(run.stdout+run.stderr)
    if run.returncode:raise RuntimeError(f'{script} failed ({run.returncode}); see {PREVIEWS/log}')
    result=[json.loads(line[7:]) for line in run.stdout.splitlines() if line.startswith('RESULT ')]
    if not result or (require_ok and not result[-1].get('ok')):
        raise RuntimeError(f'{script} failed its result check; see {PREVIEWS/log}')
    return result[-1]


def asset_job(species,variant,index):
    stem=species+'_'+variant
    folder='bushes' if species in BUSHES else 'plants'
    output=f'game/assets/flora/{folder}/{stem}.glb'
    params={'species':species,'variant':variant,'seed':7101+index*100+ord(variant)-97}
    call('tools/art/export.py',[str(GEN),str(ROOT/output),json.dumps(params)],stem+'_export.log')
    check=call('tools/art/check.py',[str(ROOT/output),'shrub'],stem+'_check.log')
    call('tools/art/preview.py',[str(ROOT/output),str(PREVIEWS/stem)],stem+'_preview.log')
    return {'species':species,'variant':variant,'output':output,'params':params,'check':check}


def manifest(reports):
    lines=['# yaml-language-server: $schema=../schemas/asset.schema.json',
           '# Original procedural P1 botanical family; fact references in art/generators/forage/SOURCES.md.']
    for report in reports:
        species=report['species'];stem=species+'_'+report['variant'];check=report['check']
        ref=REF.get(species,'conventional morphology')
        note=TELLS[species]+f' P1; original procedural work. Reference {ref} in forage/SOURCES.md. Grounded metre-scale patch; no collision. One palette material, COLOR_0 R wind. Separate flowers/fruit for optional seasonal hiding. Owner look review pending.'
        lines += [f'- id: asset.flora.{stem}','  kind: model','  status: review','  milestone: M2',
                  '  source:','    type: generator','    generator: art/generators/forage/forage.py',
                  '    params: '+json.dumps(report['params']),'    tool: Blender 5.1.0 (headless)',
                  '  outputs: ['+report['output']+']','  budget_class: shrub',
                  f'  measured: {{ tris_lod0: {check["tris"]["0"]}, materials: {len(check["materials"])} }}',
                  '  license: { name: Proprietary-own-work, author: FeudalSim, attribution_required: false }',
                  '  notes: '+json.dumps(note)]
    (ROOT/'content/assets/forage_m2.yaml').write_text('\n'.join(lines)+'\n')


def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--prepare-only',action='store_true')
    ap.add_argument('--workers',type=int,default=2)
    args=ap.parse_args();PREVIEWS.mkdir(parents=True,exist_ok=True)
    call(GEN.relative_to(ROOT),[],'preparation.log',False)
    prep=json.loads((HERE/'preparation.json').read_text())['assets']
    assert len(prep)==70 and all(30<=a['tris']<=300 for a in prep),'preparation count/budget failed'
    species=list(dict.fromkeys(a['species'] for a in prep))
    if args.prepare_only:
        print('PASS: 70 prepared botanical variants, all 30-300 triangles; no files exported');return
    reports=[]
    with ThreadPoolExecutor(max_workers=max(1,min(3,args.workers))) as pool:
        jobs=[pool.submit(asset_job,s,v,i) for i,s in enumerate(species) for v in ('a','b')]
        for job in as_completed(jobs):
            report=job.result();reports.append(report)
            print('RESULT '+json.dumps({'asset':report['species']+'_'+report['variant'],'check':'pass','tris':report['check']['tris']['0']}),flush=True)
    reports.sort(key=lambda a:(species.index(a['species']),a['variant']))
    (HERE/'checks.json').write_text(json.dumps({'result':'pass','assets':reports},indent=2)+'\n')
    manifest(reports)
    subprocess.run([sys.executable,str(HERE/'review.py')],check=True,cwd=ROOT)


if __name__=='__main__':main()
