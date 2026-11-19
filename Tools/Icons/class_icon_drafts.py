"""
Drafts for new class icons (2026-11-17, the user: "클래스 아이콘들도 좀 바꾸려 하는데", then "시안 만들어 줘"): three
drafts per class, drawn with class_icons.py's own shapes and rasteriser, so a picked draft moves into class_icons.py as
it is and the game gets exactly the picture the user picked. Nothing here writes into Assets: the drafts go to the folder
given on the command line (python class_icon_drafts.py <folder>), with the current icons beside them and one contact
sheet to look at them all at once.

Each draft leans on what the class does in the game now (its kit, its cards and its ultimate), not on a name alone.
"""
import math
import os
import struct
import sys
import zlib

import class_icons as ci
from class_icons import SIZE, SUPER, bar, circle, path, rect, rounded, turned


# ---- a faster rasteriser: each edge only visits its own rows (same result as class_icons.coverage) ----------------

def fast_coverage(polys):
    n = SIZE * SUPER
    rows = [[] for _ in range(n)]
    for poly in polys:
        count = len(poly)
        for i in range(count):
            x0, y0 = poly[i]
            x1, y1 = poly[(i + 1) % count]
            if y0 == y1:
                continue
            x0, y0, x1, y1 = x0 * SUPER, y0 * SUPER, x1 * SUPER, y1 * SUPER
            if y0 > y1:
                x0, y0, x1, y1 = x1, y1, x0, y0
            first = max(0, math.ceil(y0 - 0.5))
            last = min(n - 1, math.ceil(y1 - 0.5) - 1)
            slope = (x1 - x0) / (y1 - y0)
            for r in range(first, last + 1):
                rows[r].append(x0 + (r + 0.5 - y0) * slope)
    grid = [[0.0] * SIZE for _ in range(SIZE)]
    share = 1.0 / (SUPER * SUPER)
    for row in range(n):
        xs = rows[row]
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


ci.coverage = fast_coverage


# ---- more shapes -------------------------------------------------------------------------------------------------

def ellipse(cx, cy, rx, ry, steps=96):
    return [(cx + rx * math.cos(2 * math.pi * i / steps), cy + ry * math.sin(2 * math.pi * i / steps)) for i in range(steps)]


def star(cx, cy, outer, inner, points, rot=-90):
    out = []
    for i in range(points * 2):
        a = math.radians(rot + 180 * i / points)
        r = outer if i % 2 == 0 else inner
        out.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return out


def diamond(cx, cy, r):
    return [(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)]


def arc_band(cx, cy, inner, outer, a0, a1, steps=40):
    pts = []
    for i in range(steps + 1):
        a = math.radians(a0 + (a1 - a0) * i / steps)
        pts.append((cx + outer * math.cos(a), cy + outer * math.sin(a)))
    for i in range(steps, -1, -1):
        a = math.radians(a0 + (a1 - a0) * i / steps)
        pts.append((cx + inner * math.cos(a), cy + inner * math.sin(a)))
    return pts


def curve(p0, p1, p2, steps=24):
    return [p0] + ci.quad(p0, p1, p2, steps)


def tapered(pts, width):
    """A stroke along the points, widest in the middle and sharp at both ends: a claw mark."""
    left, right = [], []
    n = len(pts)
    for i, (x, y) in enumerate(pts):
        ax, ay = pts[max(0, i - 1)]
        bx, by = pts[min(n - 1, i + 1)]
        dx, dy = bx - ax, by - ay
        length = math.hypot(dx, dy) or 1
        nx, ny = -dy / length, dx / length
        w = width / 2 * math.sin(math.pi * i / (n - 1))
        left.append((x + nx * w, y + ny * w))
        right.append((x - nx * w, y - ny * w))
    return left + right[::-1]


def placed(polys, angle=0.0, scale=1.0, cx=128.0, cy=128.0):
    return [turned([(x * scale, y * scale) for x, y in p], angle, cx, cy) for p in polys]


def moved(polys, scale=1.0, dx=0.0, dy=0.0, about=(128, 128)):
    ox, oy = about
    return [[(ox + (x - ox) * scale + dx, oy + (y - oy) * scale + dy) for x, y in p] for p in polys]


def each(polys):
    """Each polygon its own layer, so overlapping parts add up instead of cancelling (even-odd within a layer)."""
    return [('+', [p]) for p in polys]


def gap(polys, width, steps=8):
    """Cuts a band round the polygons (copies pushed every way), so a shape laid on another reads apart from it."""
    layers = []
    for i in range(steps):
        a = 2 * math.pi * i / steps
        dx, dy = width * math.cos(a), width * math.sin(a)
        layers.append(('-', [[(x + dx, y + dy) for x, y in p] for p in polys]))
    return layers


def polyline(points, width, joints=True):
    parts = [bar(*points[i], *points[i + 1], width) for i in range(len(points) - 1)]
    if joints:
        parts += [circle(x, y, width / 2, 24) for x, y in points]
    return parts


# ---- KNIGHT: parry, cover defense, shadow clone ------------------------------------------------------------------

def knight_a():
    """A great helm with its eye slit and a cross of breathing holes: the armoured one who stands in front."""
    helm = path((62, 64), ('q', (62, 24), (128, 22)), ('q', (194, 24), (194, 64)), (194, 200), ('q', (128, 242), (62, 200)))
    band = rect(66, 70, 190, 76)
    slits = [rect(72, 96, 120, 114), rect(136, 96, 184, 114)]
    return [('+', [helm]), ('-', [band] + slits), ('-', [rect(121, 128, 135, 204)]), ('-', [rect(96, 152, 160, 166)])]


def knight_b():
    """A heater shield with the spark of a blow it turned at its corner: the parry."""
    shield = path((46, 58), (198, 58), (198, 124), ('q', (194, 194), (122, 236)), ('q', (50, 194), (46, 124)))
    inner = path((62, 74), (182, 74), (182, 124), ('q', (178, 182), (122, 218)), ('q', (66, 182), (62, 124)))
    core = path((74, 86), (170, 86), (170, 124), ('q', (166, 172), (122, 204)), ('q', (78, 172), (74, 124)))
    spark = star(204, 50, 46, 9, 4, -90)
    spark2 = star(236, 104, 16, 4, 4, -45)
    layers = [('+', [shield]), ('-', [inner]), ('+', [core])]
    layers += [('-', [star(204, 50, 60, 18, 4, -90)]), ('-', [star(236, 104, 24, 9, 4, -45)])]
    layers += [('+', [spark]), ('+', [spark2])]
    return layers


def knight_c():
    """The chess knight with its shadow clone a step behind it, see-through: the ultimate's echo. The clone is the
    picture's own faint part (its alpha at `dim`), so the game's tint makes it a paler copy of the same colour."""
    raw = ci.knight()
    solid, cuts = raw[0][1], raw[1][1]
    back = moved(solid, .8, 46, -18)
    back_cuts = moved(cuts, .8, 46, -18)
    front = moved(solid, .8, -18, 22)
    front_cuts = moved(cuts, .8, -18, 22)
    front_layers = [('+', front), ('-', front_cuts)]
    return {"layers": [('+', back), ('-', back_cuts)] + gap(front, 8) + front_layers, "front": front_layers, "dim": .42}


# ---- ASSAULT: adrenaline, CHARGE, artillery ----------------------------------------------------------------------

def assault_a():
    """A charge bugle with a pennant cut with a rank chevron: the officer's CHARGE."""
    tube = rect(30, 146, 176, 158)
    mouth = path((14, 138), (22, 138), (32, 146), (32, 158), (22, 166), (14, 166))
    bell = path((164, 146), (212, 110), ('q', (222, 104), (232, 110)), (232, 194), ('q', (222, 200), (212, 194)), (164, 158))
    loop = [ellipse(104, 116, 66, 38), ellipse(104, 116, 53, 25)]
    ties = [rect(66, 156, 74, 170), rect(138, 156, 146, 170)]
    pennant = path((58, 168), (154, 168), (106, 240))
    chevron = path((80, 180), (106, 200), (132, 180), (132, 194), (106, 214), (80, 194))
    return each([tube, mouth, bell] + ties) + [('+', loop), ('+', [pennant]), ('-', [chevron])]


def assault_b():
    """A banner leaning forward on its pole with two chevrons pointing on: the charge the team follows."""
    pole = bar(56, 246, 98, 26, 13)
    tip = diamond(99, 18, 12)
    flag = path((94, 34), ('q', (140, 8), (176, 34)), ('q', (208, 58), (240, 34)), (240, 116), ('q', (208, 138), (176, 114)),
                ('q', (140, 88), (80, 118)))
    arrows = [path((x, 50), (x + 30, 76), (x, 102), (x, 88), (x + 14, 76), (x, 64)) for x in (134, 170)]
    return each([pole, tip]) + [('+', [flag]), ('-', arrows)]


def assault_c():
    """An assault rifle over two rank chevrons."""
    parts = [path((16, 82), (66, 90), (66, 118), (20, 132), (16, 124)), rect(64, 84, 150, 108), rect(92, 68, 140, 80),
             rect(98, 78, 104, 86), rect(130, 78, 136, 86), rect(148, 86, 204, 104), rect(202, 91, 240, 99),
             path((190, 86), (195, 64), (202, 64), (204, 86)), path((112, 106), (132, 106), (142, 150), (120, 154)),
             path((76, 106), (94, 106), (88, 142), (70, 140)), path((94, 108), (112, 108), (112, 116), (98, 122))]
    rifle = moved(parts, 1.0, 0, -14)
    chevrons = [path((128, 162), (212, 200), (212, 220), (128, 182), (44, 220), (44, 200)),
                path((128, 196), (212, 234), (212, 252), (128, 214), (44, 252), (44, 234))]
    return each(rifle) + [('+', chevrons)]


# ---- GHOST: sniping, seeing through walls, stealth, the phantom, White Tiger -------------------------------------

def ghost_a():
    """An eye whose iris is a reticle: the one who sees through walls and shoots first."""
    eye = path((14, 128), ('q', (128, 30), (242, 128)), ('q', (128, 226), (14, 128)))
    ticks = [bar(128, 14, 128, 64, 9), bar(128, 192, 128, 242, 9)]
    ring = [circle(128, 128, 42), circle(128, 128, 32)]
    cross = []
    for a in range(0, 360, 90):
        c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
        cross.append(bar(128 + c * 9, 128 + s * 9, 128 + c * 28, 128 + s * 28, 5))
    return [('+', [eye]), ('-', [circle(128, 128, 52)]), ('+', ring), ('+', cross), ('+', [circle(128, 128, 4, 24)]),
            ('+', ticks)]


def ghost_b():
    """A hooded head with a masked face and one scope glint for an eye: the assassin in the dark."""
    hood = path((128, 14), ('q', (198, 34), (202, 124)), (206, 190), ('q', (232, 206), (246, 242)), (10, 242),
                ('q', (24, 206), (50, 190)), (54, 124), ('q', (58, 34), (128, 14)))
    face = path((128, 64), ('q', (178, 74), (176, 136)), ('q', (172, 186), (128, 200)), ('q', (84, 186), (80, 136)),
                ('q', (78, 74), (128, 64)))
    mask = path((82, 148), ('q', (128, 132), (174, 148)), ('q', (168, 188), (128, 200)), ('q', (88, 188), (82, 148)))
    glint = star(148, 112, 26, 4, 4)
    return [('+', [hood]), ('-', [face]), ('+', [mask]), ('+', [glint]), ('+', [circle(108, 114, 4, 24)])]


def ghost_c():
    """A scope ring torn across by three claw marks: White Tiger, the sniper who became a legend."""
    ring = [circle(128, 128, 110), circle(128, 128, 95)]
    posts = []
    for a in range(0, 360, 90):
        c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
        posts.append(bar(128 + c * 96, 128 + s * 96, 128 + c * 66, 128 + s * 66, 12))
    lines = []
    for k in (-1, 0, 1):
        ox, oy = 30 * k * 0.8, 30 * k * 0.6
        lines.append(curve((192 + ox, 46 + oy), (160 + ox, 136 + oy), (64 + ox, 210 + oy), 30))
    layers = [('+', ring), ('+', posts), ('+', [circle(128, 128, 6, 24)])]
    # Every claw's gap first, then the claws, so one claw's gap never eats into its neighbour.
    layers += [('-', [tapered(line, 38)]) for line in lines]
    layers += each([tapered(line, 22) for line in lines])
    return layers


# ---- POINT: two guns kept loaded, gun kata, the nine-bang ---------------------------------------------------------

def point_a():
    """Two arrows round a shotgun shell: the gun put away reloads itself while the other fires."""
    cx, cy = 128, 132
    hull = rounded(104, 64, 152, 170, 16)
    brass = rect(104, 166, 152, 190)
    rim = rect(98, 190, 158, 200)
    layers = each([hull, brass, rim]) + [('-', [rect(100, 158, 156, 165)])]
    for a0, a1 in ((196, 322), (16, 142)):
        layers.append(('+', [arc_band(cx, cy, 92, 110, a0, a1)]))
        a = math.radians(a1)
        px, py = cx + 101 * math.cos(a), cy + 101 * math.sin(a)
        dx, dy = -math.sin(a), math.cos(a)
        nx, ny = math.cos(a), math.sin(a)
        head = [(px + dx * 30, py + dy * 30), (px + nx * 26, py + ny * 26), (px - nx * 26, py - ny * 26)]
        layers.append(('+', [head]))
    return layers


def point_b():
    """A shotgun shell crossed over an SMG magazine: the class's two guns."""
    mag = [path((-18, -84), (18, -84), (22, 64), (-14, 64)), rect(-12, -98, 12, -84), rect(-22, 62, 26, 76)]
    mag_cuts = [circle(4, y, 5, 20) for y in (-56, -28, 0, 28)]
    shell = [rounded(-24, -80, 24, 40, 16), rect(-24, 36, 24, 64), rect(-30, 62, 30, 74)]
    shell_cut = [rect(-26, 28, 26, 35)]
    back = placed(mag, 34, 1.12, 128, 130)
    front = placed(shell, -34, 1.12, 128, 130)
    layers = each(back) + [('-', placed(mag_cuts, 34, 1.12, 128, 130))]
    layers += gap(front, 7)
    layers += each(front) + [('-', placed(shell_cut, -34, 1.12, 128, 130))]
    return layers


def point_c():
    """A nine-bang with its nine holes, spoon and pin, bursting: the ultimate's breach charge."""
    can = rounded(100, 94, 156, 206, 10)
    fuze = rect(108, 72, 148, 96)
    spoon = path((146, 70), (170, 78), (176, 182), (167, 184), (161, 88), (146, 80))
    pin = [circle(84, 74, 18), circle(84, 74, 10)]
    holes = [rounded(x - 4, y - 8, x + 4, y + 8, 3) for y in (120, 150, 180) for x in (115, 128, 141)]
    rays = []
    for i in range(8):
        a = math.radians(-90 + 45 * i)
        c, s = math.cos(a), math.sin(a)
        r0, r1 = (100, 126) if i % 2 == 0 else (104, 120)
        nx, ny = -s, c
        rays.append([(128 + c * r1, 140 + s * r1), (128 + c * r0 + nx * 7, 140 + s * r0 + ny * 7),
                     (128 + c * r0 - nx * 7, 140 + s * r0 - ny * 7)])
    return each([can, fuze, spoon, bar(98, 74, 110, 76, 6)]) + [('+', pin), ('-', holes), ('+', rays)]


# ---- DÉTENTE: the syringe, Golden Hour, Resurrection, Second Life ------------------------------------------------

def detente_a():
    """A syringe half full, its needle up: the healer's own tool."""
    body = [rect(-56, -22, 64, 22), rect(-64, -36, -56, 36), rect(-112, -6, -64, 6), rect(-124, -28, -112, 28),
            path((64, -12), (86, -6), (86, 6), (64, 12)), rect(86, -3.5, 134, 3.5)]
    window = [rect(-48, -14, 56, 14)]
    liquid = [rect(-4, -14, 56, 14)]
    marks = [rect(x, -14, x + 4, -4) for x in (8, 22, 36)]
    a, k = -42, 0.92
    return each(placed(body, a, k)) + [('-', placed(window, a, k)), ('+', placed(liquid, a, k)),
                                       ('-', placed(marks, a, k))]


def detente_b():
    """A white cross on two spread wings: the one who brings the fallen back."""
    left = path((106, 102), ('q', (64, 54), (10, 42)), ('q', (30, 68), (46, 80)), (16, 94), ('q', (40, 100), (56, 106)),
                (28, 136), ('q', (52, 132), (68, 128)), (50, 168), ('q', (82, 150), (104, 136)))
    right = [(256 - x, y) for x, y in left]
    upright, across = (110, 44, 146, 216), (70, 92, 186, 128)
    layers = [('+', [left]), ('+', [right])]
    layers += [('-', [rounded(x0 - 8, y0 - 8, x1 + 8, y1 + 8, 8)]) for x0, y0, x1, y1 in (upright, across)]
    layers += [('+', [rect(*upright)]), ('+', [rect(*across)])]
    return layers


def detente_c():
    """A heart crossed by a heartbeat line: Second Life, everyone up again."""
    heart = []
    for i in range(120):
        t = 2 * math.pi * i / 120
        x = 16 * math.sin(t) ** 3
        y = -(13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t))
        heart.append((128 + x * 6.6, 32 + (y + 12) * 6.6))
    ecg = [(6, 134), (78, 134), (92, 110), (108, 168), (128, 62), (146, 172), (160, 120), (172, 134), (250, 134)]
    return [('+', [heart])] + [('-', [p]) for p in polyline(ecg, 26)] + each(polyline(ecg, 11))


# ---- SCHIESSHUND: bounty marks, the Desperado, the duel ----------------------------------------------------------

def schiesshund_a():
    """The Desperado's cylinder face-on, one chamber loaded, the bounty diamond at its pin."""
    layers = [('+', [circle(128, 128, 106)])]
    flutes, chambers = [], []
    for i in range(6):
        a = math.radians(-60 + 60 * i)
        flutes.append(circle(128 + 106 * math.cos(a), 128 + 106 * math.sin(a), 17, 40))
        b = math.radians(-90 + 60 * i)
        chambers.append(circle(128 + 58 * math.cos(b), 128 + 58 * math.sin(b), 25, 48))
    layers += [('-', flutes), ('-', chambers), ('-', [circle(128, 128, 22)])]
    layers += [('+', [circle(128, 70, 20, 48)]), ('-', [circle(128, 70, 9, 32)]), ('+', [circle(128, 70, 5, 24)])]
    layers += [('+', [diamond(128, 128, 15)])]
    return layers


def schiesshund_b():
    """A wanted poster nailed up, the bounty diamond on it and a bullet hole through it."""
    sheet = path((-84, -104), (84, -104), (84, 92), (70, 104), (52, 94), (34, 106), (14, 96), (-6, 106), (-26, 95),
                 (-46, 105), (-64, 96), (-84, 104))
    band, line = rect(-66, -86, 66, -62), rect(-66, -52, 66, -45)
    cracks = []
    for a in (20, 110, 200, 290):
        c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
        cracks.append([(60 + c * 26, -8 + s * 26), (60 - s * 4, -8 + c * 4), (60 + s * 4, -8 - c * 4)])

    def put(polys):
        return placed(polys, -6, 1.0, 128, 132)

    layers = [('+', put([sheet])), ('-', put([band, line]))]
    layers += [('-', put([diamond(-4, 26, 56)])), ('+', put([diamond(-4, 26, 44)])), ('-', put([diamond(-4, 26, 31)])),
               ('+', put([diamond(-4, 26, 17)]))]
    layers += [('-', put([circle(60, -8, 11, 32)]))] + [('-', put([c])) for c in cracks]
    layers += [('-', put([circle(0, -98, 14, 32)])), ('+', put([circle(0, -98, 8, 32)]))]
    return layers


def schiesshund_c():
    """The hound's head inside its own bounty diamond."""
    frame = [diamond(128, 128, 124), diamond(128, 128, 104)]
    raw = ci.schiesshund()[:3]
    layers = [('+', frame)]
    for op, polys in raw:
        layers.append((op, moved(polys, .6, -2, 2, about=(122, 120))))
    return layers


# ---- SUPPORT: the supply bag, barricades, hammer and torch, ammo crates ------------------------------------------

def support_a():
    """An ammo crate with rounds stencilled on its side."""
    handle = [rounded(100, 70, 156, 104, 14)]
    handle_hole = [rounded(110, 80, 146, 104, 6)]
    box = rect(28, 96, 228, 226)
    sides = [rounded(8, 146, 26, 180, 6), rounded(230, 146, 248, 180, 6)]
    side_holes = [rounded(13, 153, 26, 173, 3), rounded(230, 153, 243, 173, 3)]
    stencil = []
    for x in (88, 128, 168):
        stencil.append(path((x, 142), ('q', (x + 15, 154), (x + 15, 172)), (x - 15, 172), ('q', (x - 15, 154), (x, 142))))
        stencil.append(rect(x - 15, 178, x + 15, 212))
    return [('+', handle), ('-', handle_hole), ('+', [box]), ('-', [rect(28, 120, 228, 127)]), ('+', sides),
            ('-', side_holes), ('-', stencil), ('+', [rect(117, 110, 139, 136)]), ('-', [rect(123, 122, 133, 130)])]


def support_b():
    """A hammer and a wrench crossed: the one who builds and mends."""
    hammer = [rounded(-10, -60, 10, 104, 8), rect(-48, -102, 44, -64), rect(44, -106, 58, -60)]
    wrench = [rounded(-11, -40, 11, 92, 8), circle(0, -68, 36), circle(0, 98, 19)]
    wrench_cuts = [rect(-14, -116, 14, -72), circle(0, -72, 14, 32), circle(0, 98, 8, 24)]
    back = placed(hammer, 40, 1.0, 128, 130)
    front = placed(wrench, -40, 1.0, 128, 130)
    layers = each(back) + gap(front, 8) + each(front) + [('-', [p]) for p in placed(wrench_cuts, -40, 1.0, 128, 130)]
    return layers


def support_c():
    """A plank barricade on two posts: the waist-high wall the Support puts up."""
    posts = [path((44, 62), (56, 44), (68, 62), (68, 240), (44, 240)), path((188, 62), (200, 44), (212, 62), (212, 240),
                                                                            (188, 240))]
    top = path((8, 88), (248, 74), (248, 106), (8, 120))
    brace = path((18, 194), (234, 112), (240, 136), (24, 218))
    low = path((8, 176), (248, 186), (248, 214), (8, 206))
    nails = [circle(56, 104, 4.5, 20), circle(200, 95, 4.5, 20), circle(56, 191, 4.5, 20), circle(200, 199, 4.5, 20)]
    layers = each(posts)
    for plank in (low, brace, top):
        layers += gap([plank], 6) + [('+', [plank])]
    layers.append(('-', nails))
    return layers


DRAFTS = {
    "knight": {"A": knight_a, "B": knight_b, "C": knight_c},
    "assault": {"A": assault_a, "B": assault_b, "C": assault_c},
    "ghost": {"A": ghost_a, "B": ghost_b, "C": ghost_c},
    "point": {"A": point_a, "B": point_b, "C": point_c},
    "detente": {"A": detente_a, "B": detente_b, "C": detente_c},
    "schiesshund": {"A": schiesshund_a, "B": schiesshund_b, "C": schiesshund_c},
    "support": {"A": support_a, "B": support_b, "C": support_c},
}


def render(made):
    """A draft's picture: its layers drawn, or for a draft with a faint part, the front at full strength and the rest
    at `dim` of it."""
    if not isinstance(made, dict):
        return ci.draw(made)
    full, front, dim = ci.draw(made["layers"]), ci.draw(made["front"]), made["dim"]
    return [[f + max(0.0, a - f) * dim for a, f in zip(full[y], front[y])] for y in range(SIZE)]


def png(width, height, pixels, channels):
    raw = bytearray()
    for y in range(height):
        raw.append(0)
        raw.extend(pixels[y])
    kind = 6 if channels == 4 else 0

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xffffffff)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, kind, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b""))


def save(folder, name, alpha):
    rows = [bytes(v for x in range(SIZE) for v in (255, 255, 255, int(round(min(1.0, alpha[y][x]) * 255))))
            for y in range(SIZE)]
    with open(os.path.join(folder, name + ".png"), "wb") as f:
        f.write(png(SIZE, SIZE, rows, 4))


def sheet(folder, cells):
    """All icons in one grey picture (rows = classes, columns = now, A, B, C), half size, to look at in one go."""
    cell, pad = 128, 12
    cols, rows_n = 4, len(cells)
    w, h = cols * (cell + pad) + pad, rows_n * (cell + pad) + pad
    grid = [bytearray([22] * w) for _ in range(h)]
    for r, row in enumerate(cells):
        for c, alpha in enumerate(row):
            ox, oy = pad + c * (cell + pad), pad + r * (cell + pad)
            for y in range(cell):
                for x in range(cell):
                    a = (alpha[2 * y][2 * x] + alpha[2 * y][2 * x + 1] + alpha[2 * y + 1][2 * x] + alpha[2 * y + 1][2 * x + 1]) / 4
                    grid[oy + y][ox + x] = int(22 + (240 - 22) * min(1.0, a))
    with open(os.path.join(folder, "sheet.png"), "wb") as f:
        f.write(png(w, h, [bytes(r) for r in grid], 1))


if __name__ == "__main__":
    folder = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "drafts")
    only = sys.argv[2:]
    os.makedirs(folder, exist_ok=True)
    cells = []
    for cls, drafts in DRAFTS.items():
        if only and cls not in only:
            continue
        row = []
        now = ci.draw(ci.ICONS[cls]())
        save(folder, cls + "_now", now)
        row.append(now)
        for key, make in drafts.items():
            alpha = render(make())
            save(folder, f"{cls}_{key}", alpha)
            row.append(alpha)
        cells.append(row)
        print("drew", cls, flush=True)
    sheet(folder, cells)
