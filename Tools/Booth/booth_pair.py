"""Makes a pair prefab of a one-sided BOOTH accessory sold for any avatar (an earring), without Unity, for OutfitSpot.

The package's prefab holds one earring with bones of its own. The pair prefab holds two copies of it under a new root,
"<name> R" and "<name> L", each a side piece whose origin is the point that sits on the character's spot (where the
earring goes through the lobe) and whose front faces +Z as the maker made it. In the file the two stand side by side
facing +Z (the wardrobe's picture); at dressing, OutfitSpot.Hang moves them onto the spots measured per character
(OutfitSpots.csv) and turns them outward. Every object of a copy gets a new file id and a name of its own
("<tag>R Bone.001"), because the game's cloth rows find their bones by name across the whole dressed character and two
copies of "Bone.001" would leave one side still. VRChat PhysBones are copied with their references moved, so BoothImport
reads both sides' sway; materials can be swapped (the Ene earrings' Standard materials draw pink in URP).

Usage (paths under the project; the prefab is downloaded data, read as text and never run):
    python -I Tools/Booth/booth_pair.py <package prefab> <pair prefab> <pierce x,y,z> <tag> [<old .mat>=<new .mat> ...]
The pierce point is in the package prefab's own frame (Unity metres). The ids come from the tag, so running it again
writes the same file; an existing .meta is kept.
"""
import os
import random
import re
import sys
import uuid

SPREAD = 0.025  # how far each side stands from the middle in the file (m)
HEAD = re.compile(r'^--- !u!(\d+) &(-?\d+)\s*$', re.M)
LOCAL = re.compile(r'\{fileID: (-?\d+)\}')


def guid_of(path):
    for line in open(path + '.meta', encoding='utf-8'):
        if line.startswith('guid: '):
            return line.split()[1]
    raise SystemExit('no guid in ' + path + '.meta')


def documents(text):
    heads = list(HEAD.finditer(text))
    out = []
    for i, m in enumerate(heads):
        end = heads[i + 1].start() if i + 1 < len(heads) else len(text)
        out.append((int(m.group(1)), m.group(2), text[m.end() + 1:end]))
    return out


def number(x):
    return '0' if abs(x) < 1e-12 else '%.9g' % x


def vector(text, field):
    # Unity wraps long values onto a second line ("z: -1.1e-14,\n    w: 1}").
    m = re.search(r'^  %s: \{x: ([^,]+),\s*y: ([^,]+),\s*z: ([^,}]+)(?:,\s*w: ([^}]+))?\}' % field, text, re.M)
    return tuple(float(g.strip()) for g in m.groups() if g is not None)


def set_field(text, field, value):
    new, n = re.subn(r'^  %s: \{[^}]*\}' % field, '  %s: %s' % (field, value), text, count=1, flags=re.M)
    if n != 1:
        raise SystemExit('no field ' + field)
    return new


def side_copy(side, original, pierce, tag, materials, ids, father):
    sign = 1 if side == 'R' else -1
    remap = {old: ids() for _, old, _ in original}
    bodies = {old: body for _, old, body in original}
    root_tr = next(old for cls, old, body in original if cls == 4 and re.search(r'^  m_Father: \{fileID: 0\}', body, re.M))
    root_go = re.search(r'm_GameObject: \{fileID: (-?\d+)\}', bodies[root_tr]).group(1)
    root_name = re.search(r'^  m_Name: (.*)$', bodies[root_go], re.M).group(1)
    children = set(re.findall(r'- \{fileID: (-?\d+)\}', re.search(r'm_Children:\n((?:  - .*\n)*)', bodies[root_tr]).group(1)))
    out = []
    for cls, old, body in original:
        text = body
        if cls == 1:
            name = re.search(r'^  m_Name: (.*)$', text, re.M).group(1)
            new_name = '%s %s' % (root_name, side) if old == root_go else '%s%s %s' % (tag, side, name)
            text = re.sub(r'^  m_Name: .*$', lambda _: '  m_Name: ' + new_name, text, count=1, flags=re.M)
        if cls == 4 and old == root_tr:
            text = set_field(text, 'm_LocalRotation', '{x: 0, y: 0, z: 0, w: 1}')
            text = set_field(text, 'm_LocalPosition', '{x: %s, y: 0, z: 0}' % number(sign * SPREAD))
            text = set_field(text, 'm_LocalScale', '{x: 1, y: 1, z: 1}')
            text = set_field(text, 'm_LocalEulerAnglesHint', '{x: 0, y: 0, z: 0}')
            text = re.sub(r'^  m_Father: \{fileID: 0\}', '  m_Father: {fileID: %s}' % father, text, count=1, flags=re.M)
            text = re.sub(r'^  m_RootOrder: \d+', '  m_RootOrder: %d' % (0 if side == 'R' else 1), text, count=1, flags=re.M)
        elif cls == 4 and old in children:
            # Only moved: the pierce point becomes the side piece's origin.
            p = vector(text, 'm_LocalPosition')
            text = set_field(text, 'm_LocalPosition', '{x: %s, y: %s, z: %s}' % tuple(number(p[i] - pierce[i]) for i in range(3)))
        if cls == 137:
            for old_guid, new_guid in materials.items():
                text = text.replace('{fileID: 2100000, guid: %s, type: 2}' % old_guid, '{fileID: 2100000, guid: %s, type: 2}' % new_guid)
        text = LOCAL.sub(lambda m: '{fileID: %s}' % remap.get(m.group(1), m.group(1)), text)
        out.append((cls, remap[old], text))
    return out, remap[root_tr], root_name


def main(source, target, pierce, tag, swaps):
    original = documents(open(source, encoding='utf-8').read())
    materials = {}
    for swap in swaps:
        old, _, new = swap.partition('=')
        materials[guid_of(old)] = guid_of(new)
    rng = random.Random(tag)
    used = set()

    def ids():
        while True:
            v = str(rng.getrandbits(62) + 1000)
            if v not in used:
                used.add(v)
                return v
    root_go, root_tr = ids(), ids()
    right, right_tr, name = side_copy('R', original, pierce, tag, materials, ids, root_tr)
    left, left_tr, _ = side_copy('L', original, pierce, tag, materials, ids, root_tr)
    text = ('%%YAML 1.1\n%%TAG !u! tag:unity3d.com,2011:\n'
            '--- !u!1 &%s\nGameObject:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n'
            '  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  serializedVersion: 6\n  m_Component:\n'
            '  - component: {fileID: %s}\n  m_Layer: 0\n  m_Name: %s Pair\n  m_TagString: Untagged\n'
            '  m_Icon: {fileID: 0}\n  m_NavMeshLayer: 0\n  m_StaticEditorFlags: 0\n  m_IsActive: 1\n'
            '--- !u!4 &%s\nTransform:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n'
            '  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: %s}\n'
            '  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\n  m_LocalPosition: {x: 0, y: 0, z: 0}\n'
            '  m_LocalScale: {x: 1, y: 1, z: 1}\n  m_Children:\n  - {fileID: %s}\n  - {fileID: %s}\n'
            '  m_Father: {fileID: 0}\n  m_RootOrder: 0\n  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}\n'
            % (root_go, root_tr, name, root_tr, root_go, right_tr, left_tr))
    for cls, fid, body in right + left:
        text += '--- !u!%d &%s\n%s' % (cls, fid, body)
    if not text.endswith('\n'):
        text += '\n'
    os.makedirs(os.path.dirname(target), exist_ok=True)
    with open(target, 'w', encoding='utf-8', newline='\n') as handle:
        handle.write(text)
    if not os.path.exists(target + '.meta'):
        with open(target + '.meta', 'w', encoding='utf-8', newline='\n') as handle:
            handle.write('fileFormatVersion: 2\nguid: %s\nPrefabImporter:\n  externalObjects: {}\n  userData: \n'
                         '  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)
    print('wrote %s (%d objects)' % (target, len(right) + len(left) + 2))


if __name__ == '__main__':
    if len(sys.argv) < 5:
        raise SystemExit(__doc__)
    main(sys.argv[1], sys.argv[2], tuple(float(v) for v in sys.argv[3].split(',')), sys.argv[4], sys.argv[5:])
