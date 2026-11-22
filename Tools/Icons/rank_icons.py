"""
The HUD's rank emblems (2026-11-22): the class rank pictures (LAYERLAB Space Rank, its Simple style, Resources/UI/Ranks/
rank_01..24.png) as plain white shapes without their dark hexagon, as the HUD's other pictures are. The user: "계급
아이콘은 너무 혼자서 튀니까 simple로 해 주고": the plate and the colours stood out alone among the addon HUD's white
pictures; the shapes alone already tell the 24 ranks apart (chevrons; chevrons with an open and then a filled diamond;
diamonds; flowers; stars). Menus, the Tab board and the climb at a chapter's end keep the coloured pictures.

Each picture: what is brighter than the hexagon's grey becomes white, shading gone, moved to the middle of the picture,
a family of ranks at one size. The bars that trimmed the hexagon's foot (13 and up) mean nothing without it and go; the
diamonds and flowers, small in a row on the hexagon, are set bigger (one, two, three as a triangle, four as a square).
128 px, drawn with the standard library only (python rank_icons.py; python rank_icons.py sheet.png draws one sheet to
look at). Written to Resources/UI/Ranks/Flat/rank_<n>.png, which ClassGrowth.Emblem loads.
"""
import os
import struct
import sys
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from icon_kit import SIZE, png, sheet  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
RANKS = os.path.join(HERE, "..", "..", "Assets", "_Project", "Art", "Resources", "UI", "Ranks")
COUNT = 24
# The hexagon's grey, and the darkest shade a shape has (its shadowed facets): that and brighter is wholly white.
PLATE, SHAPE = 0x33, 0xB4
# The widest emblem of a family fills this much of the picture.
FILL = .9
# From 13 up the hexagon's foot is trimmed with bars below this row of the source.
BARS = 170
# Diamonds and flowers: how tall one is (a share of the picture), and where each of one to four stands (in its own sizes).
PIECE = .42
PLACES = {1: [(0, 0)], 2: [(-.5, 0), (.5, 0)], 3: [(0, -.5), (-.5, .5), (.5, .5)], 4: [(-.5, -.5), (.5, -.5), (-.5, .5), (.5, .5)]}
SAMPLES = 3


def read(path):
    """An 8-bit RGBA PNG's rows of (r, g, b, a)."""
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", path
    at, body, head = 8, b"", None
    while at < len(data):
        size, kind = struct.unpack(">I4s", data[at:at + 8])
        chunk = data[at + 8:at + 8 + size]
        if kind == b"IHDR":
            head = struct.unpack(">IIBBBBB", chunk)
        elif kind == b"IDAT":
            body += chunk
        at += 12 + size
    width, height, depth, colour, _, _, interlace = head
    assert depth == 8 and colour == 6 and interlace == 0, path
    raw, stride, rows, before, at = zlib.decompress(body), width * 4, [], bytearray(width * 4), 0
    for _ in range(height):
        kind, line = raw[at], bytearray(raw[at + 1:at + 1 + stride])
        at += 1 + stride
        for i in range(stride):
            left = line[i - 4] if i >= 4 else 0
            up = before[i]
            corner = before[i - 4] if i >= 4 else 0
            if kind == 1:
                line[i] = (line[i] + left) & 255
            elif kind == 2:
                line[i] = (line[i] + up) & 255
            elif kind == 3:
                line[i] = (line[i] + (left + up) // 2) & 255
            elif kind == 4:
                p = left + up - corner
                a, b, c = abs(p - left), abs(p - up), abs(p - corner)
                line[i] = (line[i] + (left if a <= b and a <= c else up if b <= c else corner)) & 255
        rows.append(line)
        before = line
    return width, height, rows


def emblem(path):
    """How much of each pixel is the emblem: 0 on the hexagon and outside it, 1 on the shapes."""
    width, height, rows = read(path)
    out = []
    for line in rows:
        row = []
        for x in range(width):
            r, g, b, a = line[x * 4:x * 4 + 4]
            row.append(max(0.0, min(1.0, (max(r, g, b) - PLATE - 6) / (SHAPE - PLATE - 6))) * a / 255)
        out.append(row)
    return out


def bounds(cover):
    xs = [x for row in cover for x, v in enumerate(row) if v > .3]
    ys = [y for y, row in enumerate(cover) if any(v > .3 for v in row)]
    return min(xs), min(ys), max(xs) + 1, max(ys) + 1


def sample(cover, x, y):
    n = len(cover)
    x, y = x - .5, y - .5
    x0, y0 = int(x // 1), int(y // 1)
    fx, fy = x - x0, y - y0

    def at(px, py):
        return cover[py][px] if 0 <= px < n and 0 <= py < n else 0.0

    top = at(x0, y0) * (1 - fx) + at(x0 + 1, y0) * fx
    low = at(x0, y0 + 1) * (1 - fx) + at(x0 + 1, y0 + 1) * fx
    return top * (1 - fy) + low * fy


def flat(cover, parts):
    """A SIZE picture of the source's parts: each (box, where its middle goes, source pixels to one of its own)."""
    out = []
    for y in range(SIZE):
        row = []
        for x in range(SIZE):
            total = 0.0
            for j in range(SAMPLES):
                for i in range(SAMPLES):
                    px, py, most = x + (i + .5) / SAMPLES, y + (j + .5) / SAMPLES, 0.0
                    for box, (mx, my), step in parts:
                        sx, sy = (box[0] + box[2]) / 2 + (px - mx) * step, (box[1] + box[3]) / 2 + (py - my) * step
                        if box[0] - 1 <= sx <= box[2] + 1 and box[1] - 1 <= sy <= box[3] + 1:
                            most = max(most, sample(cover, sx, sy))
                    total += most
            row.append(total / (SAMPLES * SAMPLES))
        out.append(row)
    return out


def family(covers):
    """Whole emblems, each in the middle, all at the size the widest of them fits at."""
    boxes = [bounds(c) for c in covers]
    step = max(max(b[2] - b[0], b[3] - b[1]) for b in boxes) / (SIZE * FILL)
    return [flat(c, [(b, (SIZE / 2, SIZE / 2), step)]) for c, b in zip(covers, boxes)]


def pieces(cover, gap):
    """One to four of the one piece on this picture."""
    box = bounds(cover)
    width, height = box[2] - box[0], box[3] - box[1]
    step = height / (SIZE * PIECE)
    wide, tall = width / step + gap * SIZE, height / step + gap * SIZE
    return [flat(cover, [(box, (SIZE / 2 + x * wide, SIZE / 2 + y * tall), step) for x, y in PLACES[n]]) for n in range(1, 5)]


if __name__ == "__main__":
    covers = [emblem(os.path.join(RANKS, "rank_%02d.png" % n)) for n in range(1, COUNT + 1)]
    for cover in covers[12:]:
        for y in range(BARS, len(cover)):
            cover[y] = [0.0] * len(cover[y])
    drawn = family(covers[:12]) + pieces(covers[12], .05) + pieces(covers[16], .04) + family(covers[20:])
    if len(sys.argv) > 1:
        sheet(drawn, sys.argv[1], 1)
    else:
        out = os.path.join(RANKS, "Flat")
        os.makedirs(out, exist_ok=True)
        for n, alpha in enumerate(drawn, 1):
            png(alpha, os.path.join(out, "rank_%02d.png" % n))
            print("drew rank_%02d" % n)
