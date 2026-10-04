#!/usr/bin/env python3
"""Read-only M2 delivery audit. --final requires P2; --palette checks decoded PNGs.

Uses standard Python and macOS Ruby/Psych for YAML. Pillow is optional for palette
checks. Writes only its JSON report; does not import, rebuild, stage or alter art.
This checks delivery structure and measurements; owner visual approval is separate.
"""
import argparse
import collections
import json
from pathlib import Path
import re
import struct
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
BUDGETS = {'character': (2500, 4000, 3), 'animal_small': (300, 800, 1),
           'animal_medium': (1200, 2500, 2), 'tree': (40, 1500, 2),
           'shrub': (30, 300, 1), 'rock': (50, 400, 1),
           'prop_handheld': (50, 600, 2), 'workstation': (300, 2000, 2),
           'building_small': (300, 1500, 3), 'building_medium': (1500, 4000, 3)}
HUMAN_CLIPS = {
    'locomotion': 'idle idle_alt walk jog sprint turn_left turn_right crouch_walk carry_walk jump fall land',
    'rest': 'sit_ground_down sit_ground_loop sit_ground_up lie_down sleep_loop lie_up warm_hands_loop',
    'social': 'talk_a talk_b talk_c nod shake_head point wave shrug laugh argue_a argue_b cry',
    'needs': 'eat_loop drink_kneel_loop drink_skin',
    'work': 'chop_loop pick_up forage_pick_loop knap_loop carry_log_walk dig_loop stoke_fire',
    'combat_basic': 'unarmed_ready punch_a punch_b block shove hit_react_front stagger downed_loop death_a'}
ANIMALS = 'red_deer_stag red_deer_hind wild_boar hare grey_wolf brown_bear fox wild_goat'.split()
ANIMAL_CLIPS = set('idle walk run graze_loop alert flee hit death sleep_loop'.split())
BINARY_EXTENSIONS = {'.glb', '.png', '.blend', '.gltf', '.fbx', '.jpg', '.jpeg',
                     '.exr', '.psd', '.kra', '.wav', '.ogg', '.flac', '.mp3', '.sf2'}


def command(args, **kwargs):
    return subprocess.run(args, cwd=ROOT, capture_output=True, check=True, **kwargs)


def read_manifest(path):
    try:
        return json.loads(path.read_text())
    except json.JSONDecodeError:
        return json.loads(command(['ruby', '-ryaml', '-rjson', '-e',
            'puts JSON.generate(YAML.safe_load(File.read(ARGV[0])))', str(path)], text=True).stdout)


def requirements():
    core, p2 = set(), set()
    def paths(folder, names, target=core):
        target.update('game/assets/' + folder + '/' + n + '.glb' for n in names)
    for folder, counts in {
        'flora/trees': dict(scots_pine=4, oak=4, birch=3, ash=3, beech=3, alder=3, willow=3, elm=3, yew=3, lime=3),
        'nature/rocks': dict(boulder=4, fieldstone=4, flint_scatter=3, driftwood=3, outcrop=4),
        'flora/bushes': dict(hazel=3, bramble=2, gorse=2, hawthorn=2, blackthorn=2, elder=2, crab_apple=2, juniper=2, dog_rose=2, bilberry=2),
        'flora/plants': dict(reeds=3, grass_tuft=4, fern=3)}.items():
        for name, count in counts.items():
            paths(folder, [name + '_' + chr(97+i) for i in range(count)])
    for name in 'scots_pine oak birch ash beech alder willow elm yew lime'.split():
        paths('flora/trees', [name + '_stump', name + '_log'])
    plants = set(re.findall(r'id:\s*node\.([a-z_]+)', (ROOT/'content/nodes/plants.yaml').read_text())) - {'reeds'}
    for name in plants:
        paths('flora/plants', [name + '_a', name + '_b'])
    paths('buildings', 'campfire sailcloth_shelter lean_to drying_rack knapping_stone storage_pit latrine hut'.split())
    for name in 'sailcloth_shelter lean_to hut'.split():
        paths('buildings', [name + '_stage1', name + '_stage2'])
    paths('props', 'bough_bed crate barrel sack chest water_skin wooden_bucket firewood_pile firewood_bundle flint_knife stone_axe iron_axe iron_knife hammerstone flint_nodule flint_flake rough_log pole reed_thatch clay antler_billet pressure_flaker'.split())
    forage = set(re.findall(r'id:\s*item\.([a-z_]+)', (ROOT/'content/items/forage.yaml').read_text()))
    paths('props', ['item_' + n for n in forage])
    paths('wreck', 'hull_bow hull_stern mast_broken rigging_tangle reef_rocks flotsam_planks flotsam_barrel flotsam_crate rope_coil sail_heap'.split())
    paths('characters', ['body_male', 'body_female', 'body_child'])
    paths('characters/anims', [n for n in HUMAN_CLIPS if n != 'combat_basic'])
    core.update('game/assets/terrain/' + n + s + '.png' for n in
                'sand shingle grass grass_rough forest_floor heather_moor mud rock'.split()
                for s in ('_albedo_height', '_normal_rough'))
    paths('animals', ANIMALS, p2)
    paths('characters/anims', ['combat_basic'], p2)
    paths('props', 'wooden_spear sling self_bow arrows round_shield deer_carcass hide_frame snare'.split(), p2)
    return core, p2


def inspect_glb(path, character=False):
    raw = path.read_bytes()
    if raw[:4] != b'glTF' or struct.unpack_from('<I', raw, 8)[0] != len(raw):
        raise ValueError('invalid GLB header/length')
    size, kind = struct.unpack_from('<II', raw, 12)
    if kind != 0x4e4f534a:
        raise ValueError('missing JSON chunk')
    doc = json.loads(raw[20:20+size])
    meshes = doc.get('meshes', [])
    def triangles(index):
        return sum(doc['accessors'][p.get('indices', p['attributes']['POSITION'])]['count']//3
                   for p in meshes[index]['primitives'])
    visible = [n for n in doc.get('nodes', []) if 'mesh' in n and
               not n.get('name', '').endswith(('-colonly', '-convcolonly')) and
               not re.search(r'_lod[1-9]\d*$', n.get('name', ''))]
    selected = visible
    if character:
        base, slots = [], collections.defaultdict(list)
        for node in visible:
            name = node.get('name', '')
            prefix = name.split('_')[0]
            if name in ('Cloth_wool_cloak', 'Cloth_fur_cloak', 'Cloth_oiled_cloak', 'Cloth_sailcloth_poncho'):
                prefix = 'OuterCloak'
            (slots[prefix] if prefix in ('Head', 'Hair', 'Beard', 'Headwear', 'OuterCloak') else base).append(node)
        selected = base + [max(v, key=lambda n: triangles(n['mesh'])) for v in slots.values()]
    used = {p['material'] for n in visible for p in meshes[n['mesh']]['primitives'] if 'material' in p}
    return {'tris_lod0': sum(triangles(n['mesh']) for n in selected), 'materials': len(used),
            'clips': [a.get('name', '') for a in doc.get('animations', [])],
            'bones': max((len(s['joints']) for s in doc.get('skins', [])), default=0)}


def audit(final=False, palette=False):
    errors, warnings, pending = [], [], []
    entries = [a for p in sorted((ROOT/'content/assets').glob('*.yaml'))
               for a in read_manifest(p) or [] if a.get('milestone') == 'M2']
    ids, outputs = collections.Counter(), {}
    for a in entries:
        ids[a['id']] += 1
        if a.get('status') != 'review':
            errors.append(a['id'] + ': status is not review')
        source = a.get('source', {}).get('generator')
        if source and not (ROOT/source).is_file():
            errors.append(a['id'] + ': missing generator ' + source)
        for p in a.get('outputs', []):
            if p in outputs:
                errors.append(p + ': duplicate output')
            outputs[p] = a
    errors.extend(i + ': duplicate id' for i, count in ids.items() if count > 1)
    core, p2 = requirements()
    def delivery_issue(path, detail):
        # P2 exports and their importer files may still be in progress. Geometry
        # corruption/incorrect measurements remain errors in either mode.
        (pending if path in p2 and not final else errors).append(path + ': ' + detail)
    for label, required in [('P0/P1', core), ('P2', p2)]:
        for p in sorted(required - outputs.keys()):
            (errors if label == 'P0/P1' or final else pending).append(label + ': missing manifest output ' + p)
    checked = {}
    for p, a in sorted(outputs.items()):
        file = ROOT/p
        if not file.is_file():
            delivery_issue(p, 'missing output'); continue
        if p.startswith('game/assets/') and file.suffix in ('.glb', '.png'):
            sidecar = Path(str(file) + '.import')
            if not sidecar.is_file():
                delivery_issue(p, 'missing Godot import sidecar')
            elif 'source_file="res://' + p.removeprefix('game/') + '"' not in sidecar.read_text():
                errors.append(p + ': import source_file mismatch')
        if file.suffix != '.glb':
            continue
        try:
            actual = inspect_glb(file, a.get('budget_class') == 'character')
        except (ValueError, KeyError, struct.error) as exc:
            errors.append(p + ': GLB inspection failed: ' + str(exc)); continue
        checked[p] = actual
        for key in ('tris_lod0', 'materials'):
            if a.get('measured', {}).get(key) != actual[key]:
                errors.append(p + ': measured ' + key + ' differs from GLB (' + str(actual[key]) + ')')
        if a.get('kind') == 'model':
            budget = BUDGETS.get(a.get('budget_class'))
            if not budget:
                errors.append(p + ': missing/unknown budget_class'); continue
            lo, hi, mats = budget
            if file.stem in ('flint_knife', 'stone_axe', 'iron_axe', 'hammerstone'): hi = 1200
            if file.stem.startswith('item_'): hi = 150
            if file.stem.startswith('grass_tuft_'): hi = 60
            if not lo <= actual['tris_lod0'] <= hi or actual['materials'] > mats:
                errors.append(p + ': actual geometry/materials outside ' + a['budget_class'] + ' budget')
    for setname, names in HUMAN_CLIPS.items():
        p = 'game/assets/characters/anims/' + setname + '.glb'
        if p not in checked:
            continue  # Missing file/manifest already reported above.
        missing = set(names.split()) - set(checked[p]['clips'])
        if missing: delivery_issue(p, 'missing clips ' + ', '.join(sorted(missing)))
    for species in ANIMALS:
        p = 'game/assets/animals/' + species + '.glb'
        if p not in checked: continue
        required = ANIMAL_CLIPS | ({'attack'} if species in ('grey_wolf', 'wild_boar') else set())
        missing = required - set(checked[p]['clips'])
        if missing: delivery_issue(p, 'missing clips ' + ', '.join(sorted(missing)))
        if not 1 <= checked[p]['bones'] <= 35: errors.append(p + ': animal bone count outside 1–35')
    binaries = sorted(p for p in outputs if Path(p).suffix in BINARY_EXTENSIONS)
    attrs = command(['git', 'check-attr', 'filter', '--stdin'], input='\n'.join(binaries)+'\n', text=True).stdout
    errors.extend(line + ': expected LFS filter' for line in attrs.splitlines() if not line.endswith(': lfs'))
    tracked = command(['git', 'ls-files', '-s', '--', *binaries], text=True).stdout.splitlines()
    # Inspect staged/current Git blobs, not smudged working-tree binaries.
    batch = subprocess.Popen(['git', 'cat-file', '--batch'], cwd=ROOT, stdin=subprocess.PIPE, stdout=subprocess.PIPE)
    try:
        for line in tracked:
            meta, p = line.split('\t', 1); sha = meta.split()[1]
            batch.stdin.write((sha+'\n').encode()); batch.stdin.flush()
            size = int(batch.stdout.readline().split()[2]); data = batch.stdout.read(size); batch.stdout.read(1)
            if not data.startswith(b'version https://git-lfs.github.com/spec/v1\n'):
                errors.append(p + ': tracked binary is not an LFS pointer')
    finally:
        batch.stdin.close(); batch.wait()
    palette_count = None
    if palette:
        try:
            from PIL import Image
        except ImportError:
            errors.append('--palette requires Pillow; use the bundled Python runtime')
        else:
            reference = Image.open(ROOT/'art/palettes/palette.png').convert('RGBA')
            copies = [p for p in outputs if p.endswith('_palette.png') and (ROOT/p).exists()]
            palette_count = len(copies)
            for p in copies:
                image = Image.open(ROOT/p).convert('RGBA')
                if image.size != reference.size or image.tobytes() != reference.tobytes():
                    errors.append(p + ': palette pixels differ')
    else:
        warnings.append('Decoded palette pixel checks skipped; run with --palette and Pillow.')
    return {'result': 'fail' if errors else 'pending_p2' if pending else 'pass',
            'mode': 'final' if final else 'progress', 'entries': len(entries), 'outputs': len(outputs),
            'glbs_checked': len(checked), 'core_required_outputs': len(core), 'p2_required_outputs': len(p2),
            'human_clips': sum(len(v['clips']) for p, v in checked.items() if '/characters/anims/' in p),
            'animals_checked': sum('/animals/' in p for p in checked), 'binary_filters_checked': len(binaries),
            'tracked_lfs_pointers_checked': len(tracked), 'palette_copies_checked': palette_count,
            'errors': sorted(set(errors)), 'pending': sorted(set(pending)), 'warnings': warnings}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--final', action='store_true', help='Require all P2 delivery paths as well as P0/P1.')
    parser.add_argument('--palette', action='store_true', help='Compare decoded palette copies (requires Pillow).')
    parser.add_argument('--report', type=Path, default=ROOT/'art/review/m2_audit.json')
    args = parser.parse_args()
    report = audit(args.final, args.palette)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2)+'\n')
    print(f"M2 audit: {report['result']} — {report['entries']} entries, {report['glbs_checked']} GLBs, {len(report['errors'])} errors, {len(report['pending'])} pending")
    for issue in report['errors']: print(issue)
    return 1 if report['errors'] else 0


if __name__ == '__main__':
    sys.exit(main())
