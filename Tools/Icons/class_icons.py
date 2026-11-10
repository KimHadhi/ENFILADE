"""
The seven class icons for the title screen's 클래스 page and the class HUD (stage 7, 2026-11-11): white shapes on
transparent, 256 px, drawn here from plain geometry so they can be changed and drawn again (python class_icons.py,
standard library only). Written to Assets/_Project/Resources/UI/Icons/Class_<id>.png, which KitButtons.IconImage loads.

The user's brief: Knight a chess knight or something that reads melee; Assault a bullet, a gun or an officer; Ghost a
sniper scope's reticle; Point after Killing Floor 2's gunslinger; Détente a dove or a white cross (the healer); Schießhund
the hunting hound with its bounty mark; Support a backpack or something that reads supply. Our own drawings, after the
ideas only (KF2's and GMod Horde's icons are theirs).
"""
import math
import os
import struct
import zlib

SIZE = 256
SUPER = 4  # samples per pixel along each axis
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "_Project", "Resources", "UI", "Icons")


# ---- shapes as closed polygons (lists of (x, y), y down) -------------------------------------------------------

def circle(cx, cy, r, steps=96):
    return [(cx + r * math.cos(2 * math.pi * i / steps), cy + r * math.sin(2 * math.pi * i / steps)) for i in range(steps)]


def rect(x0, y0, x1, y1):
    return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]


def rounded(x0, y0, x1, y1, r, steps=10):
    r = min(r, (x1 - x0) / 2, (y1 - y0) / 2)
    pts = []
    for cx, cy, start in ((x1 - r, y0 + r, -90), (x1 - r, y1 - r, 0), (x0 + r, y1 - r, 90), (x0 + r, y0 + r, 180)):
        for i in range(steps + 1):
            a = math.radians(start + 90 * i / steps)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def bar(x0, y0, x1, y1, width):
    """A straight stroke from one point to another, square ended."""
    dx, dy = x1 - x0, y1 - y0
    length = math.hypot(dx, dy) or 1
    nx, ny = -dy / length * width / 2, dx / length * width / 2
    return [(x0 + nx, y0 + ny), (x1 + nx, y1 + ny), (x1 - nx, y1 - ny), (x0 - nx, y0 - ny)]


def quad(p0, p1, p2, steps=12):
    """A quadratic curve from p0 to p2 pulled toward p1, without its first point (to chain)."""
    pts = []
    for i in range(1, steps + 1):
        t = i / steps
        a = (1 - t) ** 2
        b = 2 * (1 - t) * t
        c = t * t
        pts.append((a * p0[0] + b * p1[0] + c * p2[0], a * p0[1] + b * p1[1] + c * p2[1]))
    return pts


def path(*parts):
    """Chains points and curves: a part is a point (x, y) or ('q', control, end) from the last point."""
    pts = []
    for part in parts:
        if isinstance(part, tuple) and len(part) == 3 and part[0] == 'q':
            pts.extend(quad(pts[-1], part[1], part[2]))
        else:
            pts.append(part)
    return pts


def turned(poly, angle, cx=0.0, cy=0.0, dx=0.0, dy=0.0, mirror=False):
    a = math.radians(angle)
    c, s = math.cos(a), math.sin(a)
    out = []
    for x, y in poly:
        if mirror:
            x = -x
        out.append((cx + dx + x * c - y * s, cy + dy + x * s + y * c))
    return out


# ---- the rasteriser ---------------------------------------------------------------------------------------------

def coverage(polys):
    """How much of each pixel the polygons cover (even-odd, so a polygon inside another is a hole), 0 to 1."""
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


def draw(layers):
    """Layers in order: ('+', polys) adds what they cover, ('-', polys) cuts it away."""
    alpha = [[0.0] * SIZE for _ in range(SIZE)]
    for op, polys in layers:
        cover = coverage(polys)
        for y in range(SIZE):
            a, c = alpha[y], cover[y]
            for x in range(SIZE):
                v = min(1.0, c[x])
                if v <= 0:
                    continue
                a[x] = max(a[x], v) if op == '+' else a[x] * (1 - v)
    return alpha


def save(name, alpha):
    raw = bytearray()
    for y in range(SIZE):
        raw.append(0)
        for x in range(SIZE):
            raw.extend((255, 255, 255, int(round(min(1.0, alpha[y][x]) * 255))))

    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xffffffff)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, name + ".png"), "wb") as f:
        f.write(png)


# ---- the icons ----------------------------------------------------------------------------------------------------

def knight():
    """A chess knight facing left on its plinth: the class that stands and fights hand to hand."""
    head = path((86, 188), ('q', (82, 160), (100, 140)), (104, 130), (82, 136), (58, 142), (40, 134), (36, 118), (44, 104),
                (62, 84), (78, 66), (90, 50), (98, 26), (112, 46), (128, 44), ('q', (178, 52), (184, 112)), (180, 188))
    eye = circle(84, 82, 7)
    nostril = circle(48, 118, 5)
    mouth = bar(40, 130, 66, 136, 4)
    mane = [bar(146, 58, 158, 72, 6), bar(164, 78, 178, 88, 6), bar(172, 104, 188, 108, 6), bar(174, 132, 190, 132, 6)]
    collar = rect(76, 188, 188, 202)
    plinth = path((66, 212), (198, 212), (208, 238), (56, 238))
    groove = rect(62, 204, 202, 212)
    return [('+', [head, collar, plinth]), ('-', [eye, nostril, mouth] + mane + [groove])]

def assault():
    """A rifle round standing over two rank chevrons: the officer's charge."""
    tip = path((128, 14), ('q', (150, 40), (152, 88)), (104, 88), ('q', (106, 40), (128, 14)))
    case = rect(104, 96, 152, 150)
    rim = rect(98, 154, 158, 164)
    chevrons = [path((128, 166), (214, 210), (214, 236), (128, 192), (42, 236), (42, 210)),
                path((128, 204), (214, 246), (214, 254), (128, 220), (42, 254), (42, 246))]
    return [('+', [tip, case, rim] + chevrons)]


def ghost():
    """A sniper scope's reticle: ring, posts, the fine cross and its mil dots."""
    ring = [circle(128, 128, 112), circle(128, 128, 98)]
    posts, dots = [], [circle(128, 128, 7, 32)]
    for a in range(0, 360, 90):
        r = math.radians(a)
        c, s = math.cos(r), math.sin(r)
        posts.append(bar(128 + c * 104, 128 + s * 104, 128 + c * 62, 128 + s * 62, 16))
        posts.append(bar(128 + c * 61, 128 + s * 61, 128 + c * 18, 128 + s * 18, 5))
        dots.append(circle(128 + c * 40, 128 + s * 40, 6, 24))
    # Each its own layer: shapes that overlap in one layer cancel out (even-odd).
    return [('+', ring), ('+', posts)] + [('+', [dot]) for dot in dots]

def pistol(angle, mirror):
    """One pistol, barrel along +x before it turns, gripped near the origin."""
    slide = path((-14, -24), (112, -24), (112, -28), (120, -28), (122, -4), (-14, -4))
    frame = rect(52, -4, 116, 4)
    grip = path((-12, -4), (22, -4), (34, 56), (4, 64), (-14, 8))
    guard = path((20, -4), (56, -4), (52, 12), ('q', (44, 26), (26, 22)))
    hole = path((26, 0), (48, 0), (45, 10), (30, 14))
    scale = 1.18

    def place(poly):
        return turned([(x * scale, y * scale) for x, y in poly], angle, 128, 168, mirror=mirror)

    return [place(p) for p in (slide, frame, grip, guard)], [place(hole)]

def point():
    """Two pistols crossed, barrels up: the gunslinger's crossed guns."""
    back, back_hole = pistol(-58, False)
    front, front_hole = pistol(58, True)
    # A gap round the front gun where it crosses the back one (the front gun pushed a few px every way), so the two
    # read apart.
    gap = []
    for i in range(8):
        a = 2 * math.pi * i / 8
        dx, dy = 5 * math.cos(a), 5 * math.sin(a)
        gap.extend([[(x + dx, y + dy) for x, y in part] for part in front])
    layers = [('+', back), ('-', back_hole)]
    layers += [('-', [part]) for part in gap]
    layers += [('+', [part]) for part in front]
    layers.append(('-', front_hole))
    return layers

def detente():
    """A dove in flight with its wing raised: the peace the name means, the one who mends."""
    head = circle(70, 104, 19, 48)
    beak = path((54, 100), (32, 110), (54, 114))
    body = path((78, 92), ('q', (122, 100), (150, 124)), (196, 138), (238, 124), (228, 140), (246, 148), (228, 156), (238, 172),
                (196, 158), ('q', (140, 178), (94, 152)), (68, 124))
    wing = path((102, 116), (110, 84), ('q', (128, 42), (176, 18)), (164, 46), (206, 28), (186, 60), (224, 54), (194, 88),
                (228, 92), (180, 118), (140, 128))
    eye = circle(66, 100, 4, 24)
    seam = bar(104, 122, 150, 130, 5)
    return [('+', [head]), ('+', [beak]), ('+', [body]), ('-', [seam]), ('+', [wing]), ('-', [eye])]

def schiesshund():
    """The hunting hound's head with its collar, and the diamond of its bounty mark over it."""
    head = path((36, 132), (50, 116), (86, 104), (100, 88), (108, 82), (122, 30), (144, 74), (152, 66), (164, 26), (182, 82),
                ('q', (200, 104), (198, 132)), (204, 196), (150, 214), ('q', (128, 176), (102, 156)), (70, 148), (46, 146))
    eye = circle(106, 106, 6, 24)
    nose = circle(42, 128, 7, 24)
    mouth = bar(56, 140, 92, 146, 5)
    collar = bar(140, 172, 208, 156, 12)
    mark = [path((206, 12), (238, 44), (206, 76), (174, 44)), path((206, 26), (224, 44), (206, 62), (188, 44))]
    return [('+', [head]), ('-', [eye, mouth, collar]), ('-', [nose]), ('+', mark)]


def support():
    """A field pack with its carry loop, its flap and the rounds in its pocket: the walking supply."""
    pack = rounded(56, 66, 200, 232, 28)
    loop = [rounded(94, 30, 162, 86, 22), rounded(110, 46, 146, 86, 12)]
    flap = bar(56, 122, 200, 122, 7)
    pocket = [rounded(82, 140, 174, 214, 14), rounded(90, 148, 166, 206, 9)]
    rounds = []
    for x in (104, 128, 152):
        rounds.append(path((x, 152), ('q', (x + 9, 160), (x + 9, 172)), (x + 9, 200), (x - 9, 200), (x - 9, 172), ('q', (x - 9, 160), (x, 152))))
    straps = [rect(40, 96, 56, 196), rect(200, 96, 216, 196)]
    return [('+', [pack] + straps), ('+', loop), ('-', [flap] + pocket[1:]), ('+', rounds)]


ICONS = {"knight": knight, "assault": assault, "ghost": ghost, "point": point, "detente": detente,
         "schiesshund": schiesshund, "support": support}

if __name__ == "__main__":
    for name, make in ICONS.items():
        save("Class_" + name, draw(make()))
        print("drew Class_" + name)
