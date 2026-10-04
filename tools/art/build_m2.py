"""Reproducible M2 family builder: export → check → previews → measured manifest.
Usage python3 tools/art/build_m2.py trees [--previews]. Other families registered below.
Fails without writing a new manifest if ANY asset fails. Review still needs eyes on previews.
"""
import argparse,json,pathlib,subprocess,sys
ROOT=pathlib.Path(__file__).resolve().parents[2]
BLENDER='/Applications/Blender.app/Contents/MacOS/Blender'

def specs(family):
    entries=[]
    def add(name,gen,folder,budget,params,notes):
        entries.append(dict(name=name,gen=gen,out=f'game/assets/{folder}/{name}.glb',budget=budget,params=params,notes=notes))
    if family=='trees':
        for species,count in [('scots_pine',4),('oak',4),('birch',3),('ash',3),('beech',3),('alder',3),('willow',3)]:
            for i in range(count):
                v=chr(97+i);add(species+'_'+v,'art/generators/broadleaf/broadleaf.py','flora/trees','tree',dict(species=species,seed=101+i,variant=v),
                  'Timber-size '+species+'; species-specific crown, west-wind asymmetry, trunk-only convex collision; COLOR_0.R wind weights.')
    elif family=='trees_p1':
        for species in ('elm','yew','lime'):
            for i in range(3):
                v=chr(97+i);add(species+'_'+v,'art/generators/broadleaf/broadleaf.py','flora/trees','tree',dict(species=species,seed=401+i,variant=v),'P1 timber '+species+'; palette/wind/trunk collision.')
        for species in ('scots_pine','oak','birch','ash','beech','alder','willow','elm','yew','lime'):
            for stage in ('stump','log'):
                add(species+'_'+stage,'art/generators/broadleaf/broadleaf.py','flora/trees','tree',dict(species=species,seed=401,variant='a',stage=stage),'P1 '+species+' cut state; exposed pale end grain, grounded hull.')
    elif family=='camp_p1':
        for item,budget in [('drying_rack','workstation'),('knapping_stone','workstation'),('storage_pit','workstation'),('latrine','building_small'),('hut','building_small')]:
            add(item,'art/generators/camp_later/camp_later.py','buildings',''+budget,dict(item=item,seed=501),'P1 '+item+'; own-work palette geometry.')
        for shelter in ('sailcloth_shelter','lean_to','hut'):
            for stage in (1,2):
                n=shelter+'_stage'+str(stage)
                add(n,'art/generators/camp_later/camp_later.py','buildings','building_small',dict(item=n,seed=501),'Construction stage: frame' if stage==1 else 'Construction stage: half covered')
    elif family=='nature':
        for species,count in [('boulder',4),('fieldstone',4),('flint_scatter',3),('driftwood',3),('outcrop',4)]:
            for i in range(count):
                v=chr(97+i);add(species+'_'+v,'art/generators/nature/nature.py','nature/rocks','rock',dict(species=species,seed=201+i,variant=v),'Grounded '+species+'; palette-painted, deterministic seed.')
    elif family=='pickups_p1':
        names=['wild_carrot','hemlock','field_mushroom','death_cap','ramsons','lily_of_the_valley','comfrey','foxglove','valerian','water_hemlock','bilberries','nightshade_berries','hazelnuts','blackberries','nettles','sorrel','sea_beet','wild_strawberries','yarrow','plantain','sphagnum']
        for item in ['rough_log','pole','reed_thatch','clay','antler_billet','pressure_flaker']+['item_'+n for n in names]:
            add(item,'art/generators/pickups/pickups.py','props','prop_handheld',dict(item=item,seed=601),'P1 harvested pickup / primitive craft material; grip origin and +Z orientation.')
    elif family=='hunting_p2':
        for item in ('wooden_spear','sling','self_bow','arrows','round_shield','deer_carcass','hide_frame','snare'):
            add(item,'art/generators/hunting/hunting.py','props','prop_handheld',dict(item=item,seed=701),'P2 primitive hunting/combat equipment; own-work palette geometry.')
    elif family in ('camp','wreck','tools'):
        groups={
          'camp': [('campfire','buildings','workstation'),('sailcloth_shelter','buildings','building_small'),('lean_to','buildings','building_small'),
            ('bough_bed','props','prop_handheld'),('crate','props','prop_handheld'),('barrel','props','prop_handheld'),('sack','props','prop_handheld'),('chest','props','prop_handheld'),
            ('water_skin','props','prop_handheld'),('wooden_bucket','props','prop_handheld'),('firewood_pile','props','prop_handheld'),('firewood_bundle','props','prop_handheld')],
          'wreck': [(n,'wreck',('building_medium' if n.startswith('hull') else 'rock' if n=='reef_rocks' else 'prop_handheld')) for n in
             ('hull_bow','hull_stern','mast_broken','rigging_tangle','reef_rocks','flotsam_planks','flotsam_barrel','flotsam_crate','rope_coil','sail_heap')],
          'tools':[(n,'props','prop_handheld') for n in ('flint_knife','stone_axe','iron_axe','iron_knife','hammerstone','flint_nodule','flint_flake')]}
        gen='art/generators/'+('wreck/wreck.py' if family=='wreck' else 'camp/camp.py' if family=='camp' else 'handtools/handtools.py')
        for n,folder,budget in groups[family]:add(n,gen,folder,budget,dict(item=n,seed=301), 'M2 P0 '+n.replace('_',' ')+'; worn own-work palette geometry.')
    else:raise ValueError(family)
    return entries


def run(script,*args):
    proc=subprocess.run([BLENDER,'-b','--factory-startup','-P',str(ROOT/script),'--',*args],cwd=ROOT,capture_output=True,text=True)
    lines=[l[7:] for l in proc.stdout.splitlines() if l.startswith('RESULT ')]
    if proc.returncode or not lines:
        raise RuntimeError(f'{script} failed: {proc.stdout[-3000:]}\n{proc.stderr[-1000:]}')
    result=json.loads(lines[-1])
    if not result.get('ok'):raise RuntimeError(str(result))
    return result


def main():
    parser=argparse.ArgumentParser();parser.add_argument('family');parser.add_argument('--previews',action='store_true');args=parser.parse_args()
    manifest=[]
    for entry in specs(args.family):
        run('tools/art/export.py',entry['gen'],entry['out'],json.dumps(entry['params']))
        check=run('tools/art/check.py',entry['out'],entry['budget'])
        if args.previews:run('tools/art/preview.py',entry['out'],'art/previews/m2/'+entry['name'])
        manifest.append(dict(id=f"asset.{args.family}.{entry['name']}",kind='model',status='review',milestone='M2',
         source=dict(type='generator',generator=entry['gen'],params=entry['params'],tool='Blender 5.1.0 (headless)'),outputs=[entry['out']],
         budget_class=entry['budget'],measured=dict(tris_lod0=check['tris']['0'],materials=len(check['materials'])),
         license=dict(name='Proprietary-own-work',author='FeudalSim',attribution_required=False),notes=entry['notes']))
        print(f"{entry['name']}: {check['tris']['0']} triangles / {check.get('height_m')}m",flush=True)
    path=ROOT/'content/assets'/f'{args.family}_m2.yaml';path.write_text(json.dumps(manifest,indent=2)+'\n')
    print(f'Checked {len(manifest)} assets; wrote {path}',flush=True)

if __name__=='__main__':main()
