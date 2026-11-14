"""Checks each map's baked probe light (Adaptive Probe Volume) without Unity: the baking set (<map> Baking Set.asset)
against its cell files, as the map builder's ProbeDataCheck does after a bake. For every cell the set lists for its
scene, each data file's table must hold the cell with as many elements as the cell's description gives (bricks for the
bricks file, chunks of probes for the rest), and each file must be long enough. A set and files from two bakes made the
loader throw every frame (2026-11-14: chapter 2 "KeyNotFoundException: The given key '2' ...", chapter 1
"ArgumentOutOfRangeException: sub array range ... outside the range of the native array"). Reads files only.

    python -I Tools/Props/apv_check.py [Assets/_Project/Scenes/Campaign]
"""
import glob, os, re, struct, sys
root = sys.argv[1] if len(sys.argv) > 1 else 'Assets/_Project/Scenes/Campaign'


def ints(hexs):
    raw = bytes.fromhex(hexs.strip())
    return list(struct.unpack('<%di' % (len(raw) // 4), raw))


def block(lines, start):
    """The lines indented under lines[start]."""
    indent = len(lines[start]) - len(lines[start].lstrip())
    out = []
    for line in lines[start + 1:]:
        if line.strip() and len(line) - len(line.lstrip()) <= indent:
            break
        out.append(line)
    return out


def streamable(lines):
    guid = size = None
    keys, values = [], []
    for i, line in enumerate(lines):
        s = line.strip()
        if s.startswith('m_AssetGUID:'):
            guid = s.split(':', 1)[1].strip()
        elif s.startswith('m_ElementSize:'):
            size = int(s.split(':', 1)[1])
        elif s.startswith('m_Keys:') and not keys:
            keys = ints(s.split(':', 1)[1]) if s.split(':', 1)[1].strip() else []
        elif s.startswith('- offset:'):
            off = int(s.split(':', 1)[1])
            cnt = int(lines[i + 1].strip().split(':', 1)[1])
            values.append((off, cnt))
    return guid, size, dict(zip(keys, values))


for folder in sorted(glob.glob(os.path.join(root, '*/'))):
    sets = glob.glob(os.path.join(folder, '* Baking Set.asset'))
    if not sets:
        continue
    lines = open(sets[0], encoding='utf-8').read().split('\n')
    guids = {}
    for meta in glob.glob(os.path.join(folder, '*.bytes.meta')):
        m = re.search(r'guid: ([0-9a-f]+)', open(meta, encoding='utf-8').read())
        if m:
            guids[m.group(1)] = meta[:-5]
    scene_cells = None
    assets = []
    counts = {}
    for i, line in enumerate(lines):
        if line.strip() == 'cellDescs:':
            cell = None
            for l in block(lines, i):
                t = l.strip()
                if t.startswith('index:'): cell = int(t.split(':', 1)[1]); counts[cell] = {}
                elif cell is not None and (t.startswith('shChunkCount:') or t.startswith('bricksCount:')):
                    k, v = t.split(':', 1); counts[cell][k] = int(v)
            break
    for i, line in enumerate(lines):
        s = line.strip()
        if s.startswith('cellList:') and scene_cells is None:
            scene_cells = ints(s.split(':', 1)[1])
        for name in ('cellSharedDataAsset', 'cellBricksDataAsset', 'cellSupportDataAsset', 'cellDataAsset', 'cellOptionalDataAsset'):
            if s == name + ':':
                assets.append((name, streamable(block(lines, i))))
    problems = []
    for name, (guid, size, table) in assets:
        if not guid:
            continue
        path = guids.get(guid)
        if path is None:
            problems.append('%s: no file with GUID %s' % (name, guid))
            continue
        need = 0
        for cell in scene_cells or []:
            if cell not in table:
                problems.append('%s: cell %d not in its table' % (name, cell))
                break
            off, cnt = table[cell]
            want = counts.get(cell, {}).get('bricksCount' if name == 'cellBricksDataAsset' else 'shChunkCount')
            if want is not None and cnt != want:
                problems.append('%s: cell %d has %d elements, its description %d' % (name, cell, cnt, want))
                break
            need = max(need, off + cnt * (size or 0))
        have = os.path.getsize(path)
        if have < need:
            problems.append('%s: file %d B < cells need %d B' % (name, have, need))
    print('%-14s cells %-3s files %d -> %s' % (os.path.basename(folder.rstrip('/\\')), len(scene_cells or []), len(assets),
                                             'OK' if not problems else '; '.join(problems)))
