"""
The seven class icons for the title screen's 클래스 page and the class HUD (stage 7, 2026-11-11): white shapes on
transparent, 256 px, drawn here from plain geometry so they can be changed and drawn again (python class_icons.py,
standard library only). Written to Assets/_Project/Art/Resources/UI/Icons/Class_<id>.png, which KitButtons.IconImage loads.

The user's brief: Knight a chess knight or something that reads melee; Assault a bullet, a gun or an officer; Ghost a
sniper scope's reticle; Point after Killing Floor 2's gunslinger; Détente a dove or a white cross (the healer); Schießhund
the hunting hound with its bounty mark; Support a backpack or something that reads supply. Our own drawings, after the
ideas only (KF2's and GMod Horde's icons are theirs).

The user's own drawings come first (2026-11-20, "내가 클래스 아이콘들 만들어 왔는데 이걸로 교체해 줘"): a class with a
picture in ArtSource/Hud/ClassIconArt/<id>.png (grey, white on black, as the user drew it; <id>.original.* is the file
they gave) is made from that picture instead of the shapes here, so running this again never draws over their work. Its
brightness is the icon's coverage (black clear, white solid, their grey parts see-through so they read the same grey on
the game's dark plates: from_drawing), and it is written at DRAWN_SIZE, larger than the drawn ones, to keep their fine
lines (the game sizes every icon by its own rect).
"""
import math
import os
import struct
import zlib

SIZE = 256
SUPER = 4  # samples per pixel along each axis
DRAWN_SIZE = 512
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "_Project", "Art", "Resources", "UI", "Icons")
ART = os.path.join(os.path.dirname(__file__), "..", "..", "ArtSource", "Hud", "ClassIconArt")


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
    """White at the given coverage, as large as `alpha` is (SIZE for the drawn icons, DRAWN_SIZE for the user's)."""
    size = len(alpha)
    raw = bytearray()
    for y in range(size):
        raw.append(0)
        for x in range(size):
            raw.extend((255, 255, 255, int(round(min(1.0, max(0.0, alpha[y][x])) * 255))))

    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xffffffff)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
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

def dove():
    """A dove in flight, wing raised, drawn about its middle facing -x with its body along x: its white parts, one
    polygon each (they overlap, and overlaps in one layer cancel), and the eye to cut."""
    head = circle(-96, -14, 22, 64)
    beak = path((-112, -24), (-136, -12), (-112, -2))
    body = path((-100, -32), ('q', (-30, -46), (50, -20)), (84, -8), (84, 14), ('q', (10, 48), (-70, 28)),
                ('q', (-116, 16), (-116, -10)))
    # The tail ends in the same long pointed feathers as the wing, so wing and tail read as one bird either side of the
    # cross (rounded scallops read as a paw).
    tail = path((60, -14), ('q', (104, -26), (146, -38)), ('q', (132, -22), (122, -16)), ('q', (140, -14), (152, -6)),
                ('q', (136, -2), (124, 2)), ('q', (138, 10), (144, 26)), ('q', (104, 26), (60, 16)))
    wing = path((-64, -24), ('q', (-72, -96), (-8, -142)), ('q', (16, -160), (44, -160)),
                ('q', (32, -142), (20, -130)), ('q', (58, -138), (70, -128)),
                ('q', (50, -110), (36, -100)), ('q', (72, -102), (80, -90)),
                ('q', (58, -76), (42, -66)), ('q', (76, -66), (78, -54)),
                ('q', (52, -38), (30, -22)))
    eye = circle(-100, -18, 4, 24)
    return [head, beak, body, tail, wing], [eye]


def detente():
    """A white cross, the healer's, with a dove rising behind it: the one who mends, and the peace the name means.
    The dove fills the icon and the cross lies over its body, so the head shows above the left arm, the raised wing
    above the right and the tail below it: one mark, not two (the user, 2026-11-11: the dove off the cross's arm was
    too small and apart, "비둘기가 배경으로써 잘 어울리게"). Seen from the side with its wing raised, the peace dove;
    from the front with both wings spread it read as a heraldic eagle."""
    def place(poly):
        # Tipped 34 degrees so it rises to the upper left; the whole mark centred in the square.
        return turned([(x * 0.9, y * 0.9) for x, y in poly], 34, 112, 138)

    parts, cuts = dove()
    layers = [('+', [place(p)]) for p in parts]
    layers.append(('-', [place(p) for p in cuts]))
    across, upright = (52, 128, 164, 168), (88, 92, 128, 204)
    # A rounded gap round the cross where it lies on the dove, so the two read apart.
    gap = 7
    layers += [('-', [rounded(x0 - gap, y0 - gap, x1 + gap, y1 + gap, gap)]) for x0, y0, x1, y1 in (across, upright)]
    layers += [('+', [rect(*across)]), ('+', [rect(*upright)])]
    return layers


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


# ---- the user's drawings ------------------------------------------------------------------------------------------

def read_png(path):
    """An 8-bit, non-interlaced PNG (grey, grey + alpha, RGB or RGBA) as rows of brightness 0-1 times its alpha."""
    with open(path, "rb") as f:
        data = f.read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(path + " is not a PNG")
    at, idat = 8, bytearray()
    width = height = kind = 0
    while at < len(data):
        length, tag = struct.unpack(">I4s", data[at:at + 8])
        body = data[at + 8:at + 8 + length]
        if tag == b"IHDR":
            width, height, depth, kind, _, _, interlace = struct.unpack(">IIBBBBB", body)
            if depth != 8 or interlace != 0 or kind not in (0, 2, 4, 6):
                raise ValueError(path + ": only 8-bit, non-interlaced grey/RGB(A) PNGs are read")
        elif tag == b"IDAT":
            idat += body
        elif tag == b"IEND":
            break
        at += 12 + length
    channels = {0: 1, 2: 3, 4: 2, 6: 4}[kind]
    stride = width * channels
    raw = zlib.decompress(bytes(idat))
    rows, prev = [], bytearray(stride)
    for y in range(height):
        start = y * (stride + 1)
        kind_of_filter, line = raw[start], bytearray(raw[start + 1:start + 1 + stride])
        if kind_of_filter == 1:
            for i in range(channels, stride):
                line[i] = (line[i] + line[i - channels]) & 255
        elif kind_of_filter == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 255
        elif kind_of_filter == 3:
            for i in range(stride):
                left = line[i - channels] if i >= channels else 0
                line[i] = (line[i] + ((left + prev[i]) >> 1)) & 255
        elif kind_of_filter == 4:
            for i in range(stride):
                a = line[i - channels] if i >= channels else 0
                b = prev[i]
                c = prev[i - channels] if i >= channels else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        prev = line
        if channels == 1:
            rows.append([v / 255.0 for v in line])
        elif channels == 2:
            rows.append([line[i] * line[i + 1] / 65025.0 for i in range(0, stride, 2)])
        else:
            rows.append([max(line[i:i + 3]) / 255.0 * (line[i + 3] / 255.0 if channels == 4 else 1.0)
                         for i in range(0, stride, channels)])
    return rows


def shrink(rows, size):
    """Each new pixel the mean of the area it covers (both ways), so edges stay soft and no line is lost or rings."""
    def along(line, n):
        scale = len(line) / n
        out = []
        for o in range(n):
            a, b = o * scale, (o + 1) * scale
            i, total = int(a), 0.0
            while i < b and i < len(line):
                total += line[i] * (min(b, i + 1) - max(a, i))
                i += 1
            out.append(total / scale)
        return out

    across = [along(row, size) for row in rows]
    columns = [along([row[x] for row in across], size) for x in range(size)]
    return [[columns[x][y] for x in range(size)] for y in range(size)]


def from_drawing(path):
    """
    The user's picture as coverage: black (and the webp's faint noise in it) clear, white solid, grey between. A grey
    is its LINEAR brightness, not its 0-255 value: the game blends in linear colour, so on the dark plates every class
    icon stands on (class tab, class UI, cards) a part shows the very grey it has in the picture (Détente's cross 55/255
    reads 55, not the 128 its plain value as coverage gave in the first preview).
    """
    low, high = 4 / 255.0, 251 / 255.0

    def linear(v):
        v = min(1.0, max(0.0, (v - low) / (high - low)))
        return v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4

    return shrink([[linear(v) for v in row] for row in read_png(path)], DRAWN_SIZE)


if __name__ == "__main__":
    for name, make in ICONS.items():
        drawing = os.path.join(ART, name + ".png")
        if os.path.exists(drawing):
            save("Class_" + name, from_drawing(drawing))
            print("Class_" + name + " from the user's drawing")
        else:
            save("Class_" + name, draw(make()))
            print("drew Class_" + name)
