"""
The small drawing kit the icon tools share (objective_icons.py): shapes as polygons, filled without antialias tricks and
written as 128 px white silhouettes on transparent (standard library only). Each piece is a list of shapes drawn as one
alpha; each limb a capsule between two joints, each its own layer (shapes overlapping in one layer would cancel, even-odd).
"""
import math
import os
import struct
import zlib

SIZE = 128
SUPER = 4
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "_Project", "Art", "Resources", "UI", "Icons")


def circle(cx, cy, r, steps=40):
    return [(cx + r * math.cos(2 * math.pi * i / steps), cy + r * math.sin(2 * math.pi * i / steps)) for i in range(steps)]


def capsule(p0, p1, width, steps=10):
    """A limb: a bar from p0 to p1 with round ends."""
    (x0, y0), (x1, y1) = p0, p1
    r = width / 2
    a = math.atan2(y1 - y0, x1 - x0)
    pts = []
    for i in range(steps + 1):  # round end at p1, from one side to the other
        t = a - math.pi / 2 + math.pi * i / steps
        pts.append((x1 + r * math.cos(t), y1 + r * math.sin(t)))
    for i in range(steps + 1):  # and at p0
        t = a + math.pi / 2 + math.pi * i / steps
        pts.append((x0 + r * math.cos(t), y0 + r * math.sin(t)))
    return pts


def coverage(polys):
    grid = [[0.0] * SIZE for _ in range(SIZE)]
    edges = []
    for poly in polys:
        n = len(poly)
        for i in range(n):
            x0, y0 = poly[i]
            x1, y1 = poly[(i + 1) % n]
            if y0 == y1:
                continue
            edges.append((x0 * SUPER, y0 * SUPER, x1 * SUPER, y1 * SUPER))
    share = 1.0 / (SUPER * SUPER)
    for row in range(SIZE * SUPER):
        y = row + 0.5
        xs = []
        for x0, y0, x1, y1 in edges:
            if (y0 <= y < y1) or (y1 <= y < y0):
                xs.append(x0 + (y - y0) * (x1 - x0) / (y1 - y0))
        if not xs:
            continue
        xs.sort()
        line = grid[row // SUPER]
        for i in range(0, len(xs) - 1, 2):
            a, b = max(0.0, xs[i]), min(SIZE * SUPER, xs[i + 1])
            if b <= a:
                continue
            first, last = int(a // SUPER), int((b - 1e-9) // SUPER)
            for px in range(first, min(last, SIZE - 1) + 1):
                lo, hi = max(a, px * SUPER), min(b, (px + 1) * SUPER)
                if hi > lo:
                    line[px] += (hi - lo) * share
    return grid


def draw(parts):
    """Every part its own layer, added: the figure is their union. A part given as (polygon, alpha) is drawn faint (a
    ladder behind the climber)."""
    alpha = [[0.0] * SIZE for _ in range(SIZE)]
    for part in parts:
        poly, strength = part if isinstance(part, tuple) else (part, 1.0)
        cover = coverage([poly])
        for y in range(SIZE):
            a, c = alpha[y], cover[y]
            for x in range(SIZE):
                v = min(1.0, c[x]) * strength
                if v > a[x]:
                    a[x] = v
    return alpha


def png(alpha, path):
    raw = bytearray()
    for y in range(SIZE):
        raw.append(0)
        for x in range(SIZE):
            raw.extend((255, 255, 255, int(round(min(1.0, alpha[y][x]) * 255))))

    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xffffffff)

    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
    data += chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)


# ---- the poses (facing right, y down, feet on GROUND) ----------------------------------------------------------------


def sheet(alphas, path, scale=2):
    """Every figure side by side on grey, larger, to look at while drawing them."""
    n = len(alphas)
    width, height = SIZE * n * scale, SIZE * scale
    raw = bytearray()
    for y in range(height):
        raw.append(0)
        for i in range(n):
            row = alphas[i][y // scale]
            for x in range(SIZE * scale):
                a = row[x // scale]
                g = 46 if (i % 2 == 0) else 58
                v = int(g + (255 - g) * a)
                raw.extend((v, v, v))

    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xffffffff)

    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
    data += chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)
