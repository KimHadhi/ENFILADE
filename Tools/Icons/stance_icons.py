"""
The stance figures drawn over the health bar's right end (2026-11-11): what the player's body is doing, at a glance. The
user crouched without knowing it and wondered why the shamblers looked so big ("앉아 있는 줄 모르고 어 쉠블러들이 왜 이리
크지 싶었는데 걍 내가 계속 앉아 있는 상태였구연"), and asked for a figure standing with a gun, sitting, or lying prone
as they are. White silhouettes on transparent, 128 px, every pose on one ground line and one scale, so a change of
stance reads as the body going down or getting up. Drawn here from plain geometry (python stance_icons.py, standard
library only), written to Assets/_Project/Resources/UI/Icons/Stance_<id>.png, which StanceFigure loads.

Each limb is a capsule between two joints, each its own layer (shapes overlapping in one layer would cancel, even-odd).
"""
import math
import os
import struct
import sys
import zlib

SIZE = 128
SUPER = 4
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "_Project", "Resources", "UI", "Icons")
GROUND = 122


# ---- shapes -------------------------------------------------------------------------------------------------------

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


def limb(*joints, widths):
    """Capsules joint to joint; widths per segment."""
    return [capsule(joints[i], joints[i + 1], widths[i]) for i in range(len(joints) - 1)]


def rifle(stock, muzzle, scale=1.0):
    """A rifle from the butt to the muzzle: stock, receiver, handguard, barrel and a curved magazine under it."""
    (sx, sy), (mx, my) = stock, muzzle
    length = math.hypot(mx - sx, my - sy)
    ux, uy = (mx - sx) / length, (my - sy) / length
    nx, ny = -uy, ux  # 'down' from the bore for a gun pointing right is +n when uy ~ 0 ... keep it the screen's down

    if ny < 0:
        nx, ny = -nx, -ny

    def at(u, v):
        return (sx + ux * u + nx * v, sy + uy * u + ny * v)

    k = scale
    parts = [
        [at(0, -3 * k), at(14 * k, -3 * k), at(14 * k, 4 * k), at(0, 7 * k)],                      # stock
        capsule(at(10 * k, 0), at(length - 16 * k, 0), 8 * k, 6),                                 # receiver + handguard
        capsule(at(length - 18 * k, -.5 * k), at(length, -.5 * k), 4.6 * k, 4),                  # barrel
        [at(26 * k, 2 * k), at(33 * k, 2 * k), at(31 * k, 14 * k), at(24 * k, 13 * k)],          # magazine
        capsule(at(19 * k, 2 * k), at(17 * k, 9 * k), 4.5 * k, 4),                                 # grip
    ]
    return parts


def pistol(hand, toward):
    """A pistol in the hand, pointing at 'toward'."""
    (hx, hy), (tx, ty) = hand, toward
    length = math.hypot(tx - hx, ty - hy)
    ux, uy = (tx - hx) / length, (ty - hy) / length
    nx, ny = -uy, ux
    if ny < 0:
        nx, ny = -nx, -ny

    def at(u, v):
        return (hx + ux * u + nx * v, hy + uy * u + ny * v)

    return [capsule(at(-3, -4), at(15, -4), 6, 4), capsule(at(0, -3), at(-2, 5), 5, 4)]


# ---- the rasteriser (as class_icons.py) ---------------------------------------------------------------------------

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

G = GROUND


def stand():
    """Standing, rifle shouldered and level: ready to shoot."""
    parts = [circle(62, 22, 10)]
    parts += [capsule((58, 32), (57, 38), 9)]
    parts += [capsule((57, 44), (54, 72), 22)]
    parts += limb((52, 74), (47, 97), (45, G - 4), widths=(15, 12))
    parts += [capsule((43, G - 2), (55, G - 1), 7)]
    parts += limb((57, 74), (63, 97), (66, G - 4), widths=(15, 12))
    parts += [capsule((65, G - 2), (77, G - 1), 7)]
    parts += rifle((50, 38), (118, 41))
    parts += limb((54, 42), (50, 56), (68, 49), widths=(10, 9))   # far arm to the grip
    parts += limb((60, 42), (74, 56), (90, 45), widths=(10, 9))   # near arm to the handguard
    return parts


def crouch():
    """On one knee, rifle shouldered: low and steady."""
    y0 = 36
    parts = [circle(68, y0 + 16, 10)]
    parts += [capsule((64, y0 + 26), (62, y0 + 32), 9)]
    parts += [capsule((62, y0 + 38), (56, y0 + 64), 22)]
    parts += limb((60, y0 + 66), (82, y0 + 64), (82, G - 4), widths=(15, 12))      # front leg: thigh forward, shin down
    parts += [capsule((81, G - 2), (92, G - 1), 7)]
    parts += limb((54, y0 + 66), (50, G - 5), (28, G - 3), widths=(15, 12))        # back knee on the ground
    parts += [capsule((28, G - 3), (22, G - 9), 7)]                                   # toes tucked
    parts += rifle((56, y0 + 32), (122, y0 + 35))
    parts += limb((60, y0 + 36), (55, y0 + 50), (74, y0 + 43), widths=(10, 9))
    parts += limb((66, y0 + 36), (79, y0 + 50), (95, y0 + 39), widths=(10, 9))
    return parts


def prone():
    """Flat on the belly, on the elbows, rifle forward."""
    y = G - 9
    parts = [circle(92, y - 17, 10)]
    parts += [capsule((82, y - 8), (44, y), 20)]                                      # chest up on the elbows
    parts += limb((44, y + 1), (22, y + 3), (6, y + 5), widths=(14, 11))
    parts += [capsule((6, y + 5), (2, y - 2), 6)]
    parts += limb((78, y - 8), (86, y + 5), (100, y - 9), widths=(10, 9))            # elbow down, hand on the gun
    parts += rifle((82, y - 18), (127, y - 15), .85)
    return parts


def slide():
    """Feet first along the ground, leaning back, gun held up: a slide."""
    parts = [circle(36, 70, 10)]
    parts += [capsule((40, 80), (54, 104), 20)]
    parts += limb((56, 108), (80, 112), (104, G - 3), widths=(14, 11))              # straight leg ahead
    parts += limb((54, 106), (72, 96), (88, G - 6), widths=(14, 11))                # bent leg
    parts += [capsule((103, G - 3), (110, G - 10), 6)]
    parts += rifle((40, 86), (104, 70), .9)
    parts += limb((44, 84), (54, 96), (66, 82), widths=(10, 9))
    # The ground streaking past behind.
    parts += [capsule((4, G - 1), (30, G - 1), 3), capsule((12, G - 9), (32, G - 9), 3)]
    return parts


def supine():
    """On the back, knees up, rifle held over the chest pointing past the feet."""
    y = G - 8
    parts = [circle(18, y - 6, 10)]
    parts += [capsule((30, y - 4), (64, y), 19)]
    parts += limb((64, y + 1), (86, y - 16), (102, y + 3), widths=(14, 11))         # knee up
    parts += limb((64, y + 2), (92, y + 4), (118, y + 5), widths=(14, 11))          # leg along the ground
    parts += rifle((36, y - 14), (92, y - 30), .85)
    parts += limb((36, y - 6), (40, y - 18), (52, y - 18), widths=(10, 9))
    return parts


def downed():
    """Down and hurt: sitting on the ground, propped on one arm, a pistol out in the other."""
    parts = [circle(46, 66, 11)]
    parts += [capsule((46, 78), (50, 104), 21)]
    parts += limb((52, 108), (80, 110), (104, G - 3), widths=(14, 11))
    parts += limb((50, 108), (74, 104), (92, G - 6), widths=(14, 11))
    parts += [capsule((103, G - 3), (109, G - 10), 6)]
    parts += limb((44, 82), (34, 100), (28, G - 3), widths=(10, 9))                 # arm propped behind
    parts += limb((50, 82), (64, 90), (80, 80), widths=(10, 9))                     # gun arm
    parts += pistol((80, 80), (110, 70))
    return parts


def climb():
    """Hands up on a rung, legs on the way up, the ladder faint behind: climbing or hanging."""
    parts = [(capsule((44, 2), (44, G), 5), .4), (capsule((84, 2), (84, G), 5), .4)]
    parts += [(capsule((44, y), (84, y), 4), .4) for y in (10, 34, 58, 82, 106)]
    parts += [circle(64, 34, 10)]
    parts += [capsule((63, 46), (61, 78), 22)]
    parts += limb((58, 48), (50, 30), (55, 8), widths=(10, 9))
    parts += limb((68, 48), (77, 30), (72, 8), widths=(10, 9))
    parts += limb((58, 80), (50, 100), (56, G - 4), widths=(15, 12))
    parts += limb((64, 80), (76, 94), (74, 112), widths=(15, 12))
    parts += [capsule((55, G - 3), (64, G - 2), 7), capsule((74, 113), (82, 114), 7)]
    return parts


POSES = {"stand": stand, "crouch": crouch, "prone": prone, "slide": slide, "supine": supine, "downed": downed,
         "climb": climb}


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


if __name__ == "__main__":
    drawn = {name: draw(make()) for name, make in POSES.items()}
    if len(sys.argv) > 1:
        sheet(list(drawn.values()), sys.argv[1])  # a preview sheet only
    else:
        os.makedirs(OUT, exist_ok=True)
        for name, alpha in drawn.items():
            png(alpha, os.path.join(OUT, "Stance_" + name + ".png"))
            print("drew Stance_" + name)
