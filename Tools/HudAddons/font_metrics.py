"""Reads the numbers a Garry's Mod font size needs from TrueType/OpenType files, for Tables/Hud/AddonFonts.csv.

Garry's Mod (Source's Windows surface) asks GDI for a font whose CELL height is the size given to surface.CreateFont,
so a size of 20 makes usWinAscent + usWinDescent come to 20 px. TextMeshPro sizes a font by its em instead, and the em
is a different share of the cell in every face. HudAddons.Fonts turns one into the other with these three numbers
(unitsPerEm from 'head', usWinAscent and usWinDescent from 'OS/2'), and places the baseline usWinAscent down the cell,
where GDI puts it. usWeightClass says whether a face is already bold (GDI only fakes bold on a face that is not).

The names printed are the ones GDI can find the face by (family nameID 1, full name nameID 4, typographic family
nameID 16 + 17). A variable font (an 'fvar' table) also lists its named instances: TextMeshPro draws only the default
instance of such a file, so an instance an addon names (say "Hitmarker VF Condensed Light") needs a static file cut
from it first.

Usage: python -I font_metrics.py <font.ttf|folder> [...]   (folders are searched for .ttf and .otf)
       add --csv to print AddonFonts.csv rows instead (id = full name; check each id against the addon's Lua).
"""
import os
import struct
import sys


def tables(data):
    count = struct.unpack_from('>H', data, 4)[0]
    found = {}
    for i in range(count):
        tag, _, offset, length = struct.unpack_from('>4sIII', data, 12 + 16 * i)
        found[tag.decode('latin-1')] = (offset, length)
    return found


def names(data, at):
    offset, _ = at['name']
    _, count, strings = struct.unpack_from('>HHH', data, offset)
    best = {}
    for i in range(count):
        platform, encoding, language, name_id, length, start = struct.unpack_from('>HHHHHH', data, offset + 6 + 12 * i)
        raw = data[offset + strings + start: offset + strings + start + length]
        if platform == 3 and encoding in (0, 1, 10):
            rank = 0 if language == 0x409 else 1
            text = raw.decode('utf-16-be', 'replace')
        elif platform == 1 and encoding == 0:
            rank = 2
            text = raw.decode('mac-roman', 'replace')
        else:
            continue
        if name_id not in best or rank < best[name_id][0]:
            best[name_id] = (rank, text)
    return {k: v[1] for k, v in best.items()}


def read(path):
    data = open(path, 'rb').read()
    at = tables(data)
    head = at['head'][0]
    os2 = at['OS/2'][0]
    hhea = at['hhea'][0]
    units = struct.unpack_from('>H', data, head + 18)[0]
    weight = struct.unpack_from('>H', data, os2 + 4)[0]
    win_ascent, win_descent = struct.unpack_from('>HH', data, os2 + 74)
    typo = struct.unpack_from('>hhh', data, os2 + 68)
    ascender, descender, gap = struct.unpack_from('>hhh', data, hhea + 4)
    named = names(data, at)
    instances = []
    if 'fvar' in at:
        fvar = at['fvar'][0]
        axes_at, _, axis_count, axis_size, instance_count, instance_size = struct.unpack_from('>HHHHHH', data, fvar + 4)
        first = fvar + axes_at + axis_count * axis_size
        for i in range(instance_count):
            sub_id = struct.unpack_from('>H', data, first + i * instance_size)[0]
            instances.append(named.get(sub_id, '#%d' % sub_id))
    return {
        'file': os.path.splitext(os.path.basename(path))[0], 'units': units, 'weight': weight,
        'winAscent': win_ascent, 'winDescent': win_descent, 'typo': typo, 'hhea': (ascender, descender, gap),
        'family': named.get(1, ''), 'sub': named.get(2, ''), 'full': named.get(4, ''),
        'typoFamily': named.get(16, ''), 'typoSub': named.get(17, ''), 'instances': instances,
    }


def files(args):
    for arg in args:
        if os.path.isdir(arg):
            for root, _, names_ in os.walk(arg):
                for name in sorted(names_):
                    if name.lower().endswith(('.ttf', '.otf')):
                        yield os.path.join(root, name)
        else:
            yield arg


def main(argv):
    csv = '--csv' in argv
    paths = [a for a in argv if a != '--csv']
    if not paths:
        print(__doc__)
        return 2
    for path in files(paths):
        f = read(path)
        if csv:
            print('%s,%s,%d,%d,%d,%d,' % (f['full'] or f['family'], f['file'], f['units'], f['winAscent'], f['winDescent'], f['weight']))
            continue
        cell = f['winAscent'] + f['winDescent']
        print('%s\n  family "%s" / "%s", full "%s", typographic "%s" / "%s"' % (path, f['family'], f['sub'], f['full'], f['typoFamily'], f['typoSub']))
        print('  unitsPerEm %d, win %d/%d (TMP size = GMod size x %.4f, baseline %.4f of the cell down), weight %d'
              % (f['units'], f['winAscent'], f['winDescent'], f['units'] / cell, f['winAscent'] / cell, f['weight']))
        print('  hhea %d/%d gap %d, typo %d/%d gap %d' % (f['hhea'] + f['typo']))
        if f['instances']:
            print('  variable: named instances %s (TextMeshPro draws only the default one)' % ', '.join(f['instances']))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
