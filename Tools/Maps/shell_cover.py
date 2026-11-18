"""How much of a bought building's visible shell (walls, glass) its collision mesh covers, read from the meshes a build
keeps beside its scene (Scenes/Campaign/<scene>/Pack), without Unity: every upright part of the seen meshes is sampled
at walking height over each floor, and a sample with no collision triangle within reach is "seen but not blocking".
usage: python shell_cover.py <Pack folder> --collision colmesh.asset [...] --seen waende.asset "fenster_09 바깥.asset" [...]
       [--floors 0 4] [--reach 0.35] [--list 40]"""
import math
import os
import sys
sys.path.insert(0, os.path.dirname(__file__))
import unity_mesh as um

args = sys.argv[1:]
folder = args[0]
groups = {"--collision": [], "--seen": [], "--floors": [], "--reach": [], "--list": []}
key = None
for a in args[1:]:
    if a in groups:
        key = a
    else:
        groups[key].append(a)
floors = [float(v) for v in groups["--floors"]] or [0.0, 4.0]
reach = float(groups["--reach"][0]) if groups["--reach"] else 0.35
show = int(groups["--list"][0]) if groups["--list"] else 40
BAND = (0.25, 1.85)   # walking height over a floor: knee to head
CELL, STEP = 2.0, 0.5


def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def point_tri2(p, a, b, c):
    """Squared distance from p to triangle abc (Ericson, Real-Time Collision Detection)."""
    ab, ac, ap = sub(b, a), sub(c, a), sub(p, a)
    d1, d2 = dot(ab, ap), dot(ac, ap)
    if d1 <= 0 and d2 <= 0:
        return dot(ap, ap)
    bp = sub(p, b)
    d3, d4 = dot(ab, bp), dot(ac, bp)
    if d3 >= 0 and d4 <= d3:
        return dot(bp, bp)
    vc = d1 * d4 - d3 * d2
    if vc <= 0 and d1 >= 0 and d3 <= 0:
        v = d1 / (d1 - d3)
        q = (a[0] + ab[0] * v, a[1] + ab[1] * v, a[2] + ab[2] * v)
        return dot(sub(p, q), sub(p, q))
    cp = sub(p, c)
    d5, d6 = dot(ab, cp), dot(ac, cp)
    if d6 >= 0 and d5 <= d6:
        return dot(cp, cp)
    vb = d5 * d2 - d1 * d6
    if vb <= 0 and d2 >= 0 and d6 <= 0:
        w = d2 / (d2 - d6)
        q = (a[0] + ac[0] * w, a[1] + ac[1] * w, a[2] + ac[2] * w)
        return dot(sub(p, q), sub(p, q))
    va = d3 * d6 - d5 * d4
    if va <= 0 and (d4 - d3) >= 0 and (d5 - d6) >= 0:
        w = (d4 - d3) / ((d4 - d3) + (d5 - d6))
        q = (b[0] + (c[0] - b[0]) * w, b[1] + (c[1] - b[1]) * w, b[2] + (c[2] - b[2]) * w)
        return dot(sub(p, q), sub(p, q))
    denom = 1.0 / (va + vb + vc)
    v, w = vb * denom, vc * denom
    q = (a[0] + ab[0] * v + ac[0] * w, a[1] + ab[1] * v + ac[1] * w, a[2] + ab[2] * v + ac[2] * w)
    return dot(sub(p, q), sub(p, q))


grid = {}
solid = []
for name in groups["--collision"]:
    _, verts, tris = um.load(os.path.join(folder, name))
    for i, j, k in tris:
        a, b, c = verts[i], verts[j], verts[k]
        solid.append((a, b, c))
        x0, x1 = min(a[0], b[0], c[0]) - reach, max(a[0], b[0], c[0]) + reach
        z0, z1 = min(a[2], b[2], c[2]) - reach, max(a[2], b[2], c[2]) + reach
        for gx in range(int(math.floor(x0 / CELL)), int(math.floor(x1 / CELL)) + 1):
            for gz in range(int(math.floor(z0 / CELL)), int(math.floor(z1 / CELL)) + 1):
                grid.setdefault((gx, gz), []).append(len(solid) - 1)
print("collision triangles:", len(solid))


def covered(p):
    r2 = reach * reach
    for t in grid.get((int(math.floor(p[0] / CELL)), int(math.floor(p[2] / CELL))), ()):
        a, b, c = solid[t]
        if point_tri2(p, a, b, c) <= r2:
            return True
    return False


total_all = bare_all = 0.0
holes = []
for name in groups["--seen"]:
    _, verts, tris = um.load(os.path.join(folder, name))
    total = bare = 0.0
    for i, j, k in tris:
        a, b, c = verts[i], verts[j], verts[k]
        n = cross(sub(b, a), sub(c, a))
        length = math.sqrt(dot(n, n))
        if length < 1e-9 or abs(n[1]) / length > 0.5:
            continue   # not an upright face
        area = length * .5
        # A grid of samples over the triangle, about STEP apart.
        steps = max(1, int(math.ceil(math.sqrt(area * 2) / STEP)))
        pts = []
        for u in range(steps):
            for v in range(steps - u):
                s, t = (u + 1 / 3) / steps, (v + 1 / 3) / steps
                pts.append((a[0] + (b[0] - a[0]) * s + (c[0] - a[0]) * t, a[1] + (b[1] - a[1]) * s + (c[1] - a[1]) * t,
                            a[2] + (b[2] - a[2]) * s + (c[2] - a[2]) * t))
                if u + v + 1 < steps:
                    s2, t2 = (u + 2 / 3) / steps, (v + 2 / 3) / steps
                    pts.append((a[0] + (b[0] - a[0]) * s2 + (c[0] - a[0]) * t2, a[1] + (b[1] - a[1]) * s2 + (c[1] - a[1]) * t2,
                                a[2] + (b[2] - a[2]) * s2 + (c[2] - a[2]) * t2))
        share = area / len(pts)
        for p in pts:
            if not any(f + BAND[0] <= p[1] <= f + BAND[1] for f in floors):
                continue
            total += share
            if not covered(p):
                bare += share
                holes.append((p, share, name))
    total_all += total
    bare_all += bare
    print("%-34s upright area at walking height %8.1f m2, with no collision within %.2f m: %7.1f m2 (%.0f%%)" % (
        name, total, reach, bare, 100 * bare / total if total else 0))
print("ALL: %.1f m2 seen at walking height, %.1f m2 not blocking (%.1f%%)" % (total_all, bare_all, 100 * bare_all / total_all if total_all else 0))
# Where: 2 m cells with the most bare area.
cells = {}
for p, share, name in holes:
    floor = min(floors, key=lambda f: abs(p[1] - f - 1))
    k = (round(p[0] / 2) * 2, floor, round(p[2] / 2) * 2, name)
    cells[k] = cells.get(k, 0.0) + share
for (x, f, z, name), m2 in sorted(cells.items(), key=lambda kv: -kv[1])[:show]:
    print("  bare %5.1f m2 near pack (%.0f, %.0f, %.0f) %s" % (m2, x, f, z, name))
