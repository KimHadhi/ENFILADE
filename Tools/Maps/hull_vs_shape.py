"""How much of a model's collision box is air to a shot: rays fired straight into each side of the box and down through
its top, a grid apart, and the share of them that meet no triangle of the model. The same measure the builder writes
for every prop judged by its seen shape (GreyboxBuilder.Audit ShotReport, Tables/MapArt shots = mesh), read here from
the model's FBX without Unity. The triangle test and the leaves are ShotShape.cs's, ported, and checked here against a
plain test of every triangle.
usage: python hull_vs_shape.py <model.fbx> [...]   (binary FBX 7.x; the box is the mesh's bounds, its top raised to
       2.05 m when it falls between 1.5 and 2.05 m, as the builder raises it)"""
import math
import os
import struct
import sys
import zlib

GRID, LEAF = 0.25, 24


def read(data):
    version = struct.unpack_from("<I", data, 23)[0]
    wide = version >= 7500
    head, hs = ("<QQQ", 24) if wide else ("<III", 12)

    def props(pos, count):
        values = []
        for _ in range(count):
            kind = chr(data[pos]); pos += 1
            if kind == "Y": values.append(struct.unpack_from("<h", data, pos)[0]); pos += 2
            elif kind == "C": values.append(data[pos]); pos += 1
            elif kind == "I": values.append(struct.unpack_from("<i", data, pos)[0]); pos += 4
            elif kind == "F": values.append(struct.unpack_from("<f", data, pos)[0]); pos += 4
            elif kind == "D": values.append(struct.unpack_from("<d", data, pos)[0]); pos += 8
            elif kind == "L": values.append(struct.unpack_from("<q", data, pos)[0]); pos += 8
            elif kind in "fdlib":
                length, encoding, size = struct.unpack_from("<III", data, pos); pos += 12
                raw = data[pos:pos + size]; pos += size
                if encoding == 1: raw = zlib.decompress(raw)
                values.append(list(struct.unpack("<%d%s" % (length, {"f": "f", "d": "d", "l": "q", "i": "i", "b": "b"}[kind]), raw)))
            elif kind in "SR":
                size = struct.unpack_from("<I", data, pos)[0]; pos += 4
                values.append(data[pos:pos + size]); pos += size
            else:
                raise ValueError(kind)
        return values

    def walk(pos, end):
        nodes = []
        while pos + hs < end:
            end_offset, count, plen = struct.unpack_from(head, data, pos)
            if end_offset == 0: break
            nlen = data[pos + hs]
            name = data[pos + hs + 1:pos + hs + 1 + nlen].decode("latin-1")
            pstart = pos + hs + 1 + nlen
            nodes.append((name, props(pstart, count), walk(pstart + plen, end_offset) if pstart + plen < end_offset else []))
            pos = end_offset
        return nodes

    return walk(27, len(data))


def find(nodes, name):
    for n in nodes:
        if n[0] == name: yield n
        yield from find(n[2], name)


def meshes(path):
    """The first geometry's triangles (a model's first level of detail comes first), in metres."""
    nodes = read(open(path, "rb").read())
    unit = 1.0
    for g in find(nodes, "GlobalSettings"):
        for block in g[2]:
            if block[0] == "Properties70":
                for p in block[2]:
                    if p[1] and p[1][0] == b"UnitScaleFactor":
                        unit = [v for v in p[1] if isinstance(v, (int, float))][-1]
    out = []
    for geo in find(nodes, "Geometry"):
        verts = idx = None
        for child in geo[2]:
            if child[0] == "Vertices": verts = child[1][0]
            if child[0] == "PolygonVertexIndex": idx = child[1][0]
        if not verts or not idx: continue
        k = unit / 100.0
        pts = [(verts[i] * k, verts[i + 1] * k, verts[i + 2] * k) for i in range(0, len(verts), 3)]
        tris, poly = [], []
        for i in idx:
            if i < 0:
                poly.append(-i - 1)
                for j in range(1, len(poly) - 1): tris.append((pts[poly[0]], pts[poly[j]], pts[poly[j + 1]]))
                poly = []
            else:
                poly.append(i)
        name = geo[1][1].split(b"\x00")[0].decode("latin-1", "replace") if len(geo[1]) > 1 and isinstance(geo[1][1], bytes) else "?"
        out.append((name, tris))
    return out


def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def hit(tri, o, d, near, far):
    a = tri[0]
    ab, ac = sub(tri[1], a), sub(tri[2], a)
    h = cross(d, ac)
    det = dot(ab, h)
    if -1e-9 < det < 1e-9: return None
    f = 1.0 / det
    s = sub(o, a)
    u = f * dot(s, h)
    if u < 0 or u > 1: return None
    q = cross(s, ab)
    v = f * dot(d, q)
    if v < 0 or u + v > 1: return None
    t = f * dot(ac, q)
    return t if near <= t < far else None


def halve(order, mids, lo, hi):
    n = hi - lo
    if n <= LEAF: return
    box = [(min(mids[i][k] for i in order[lo:hi]), max(mids[i][k] for i in order[lo:hi])) for k in range(3)]
    size = [b[1] - b[0] for b in box]
    axis = 0 if size[0] >= size[1] and size[0] >= size[2] else (1 if size[1] >= size[2] else 2)
    order[lo:hi] = sorted(order[lo:hi], key=lambda i: mids[i][axis])
    half = max(LEAF, (n // 2 + LEAF - 1) // LEAF * LEAF)
    if half >= n: return
    halve(order, mids, lo, lo + half)
    halve(order, mids, lo + half, hi)


def soup(tris):
    mids = [tuple((t[0][k] + t[1][k] + t[2][k]) / 3 for k in range(3)) for t in tris]
    order = list(range(len(tris)))
    halve(order, mids, 0, len(order))
    sorted_tris = [tris[i] for i in order]
    leaves = []
    for i in range(0, len(sorted_tris), LEAF):
        chunk = sorted_tris[i:i + LEAF]
        leaves.append(([min(p[k] for t in chunk for p in t) for k in range(3)], [max(p[k] for t in chunk for p in t) for k in range(3)]))
    return sorted_tris, leaves


def enters(leaf, o, inv, near, far):
    lo, hi = leaf
    for k in range(3):
        if math.isinf(inv[k]):
            if o[k] < lo[k] or o[k] > hi[k]: return False
            continue
        a, b = (lo[k] - o[k]) * inv[k], (hi[k] - o[k]) * inv[k]
        if a > b: a, b = b, a
        near, far = max(near, a), min(far, b)
        if near > far: return False
    return True


def cast(sp, o, d, near, far):
    tris, leaves = sp
    inv = tuple(float("inf") if abs(x) < 1e-9 else 1.0 / x for x in d)
    best = None
    for li, leaf in enumerate(leaves):
        if not enters(leaf, o, inv, near, far if best is None else best): continue
        for tri in tris[li * LEAF:(li + 1) * LEAF]:
            t = hit(tri, o, d, near, far if best is None else best)
            if t is not None: best = t
    return best


for path in sys.argv[1:]:
    for name, tris in meshes(path)[:1]:
        lo = [min(p[k] for t in tris for p in t) for k in range(3)]
        hi = [max(p[k] for t in tris for p in t) for k in range(3)]
        top = hi[1]
        height = top - lo[1]
        raised = lo[1] + (2.05 if 1.65 <= height < 2.05 else (1.5 if 1.5 < height < 1.65 else height))
        sp = soup(tris)
        print("%s: %d triangles, box %.2f x %.2f x %.2f m (top raised to %.2f)" % (os.path.basename(path), len(tris), hi[0] - lo[0], height, hi[2] - lo[2], raised - lo[1]))
        for label, box_top in (("model's own box", top), ("box as the builder raises it", raised)):
            fired = missed = disagree = 0
            for face in range(5):
                inward = [(1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1), (0, -1, 0)][face]
                across = (0, 0, 1) if face < 2 else (1, 0, 0)
                up = (0, 0, 1) if face == 4 else (0, 1, 0)
                size = (hi[0] - lo[0], box_top - lo[1], hi[2] - lo[2])
                centre = ((lo[0] + hi[0]) / 2, (lo[1] + box_top) / 2, (lo[2] + hi[2]) / 2)
                deep, wide, tall = abs(dot(size, inward)), abs(dot(size, across)), abs(dot(size, up))
                cols, rows = max(1, round(wide / GRID)), max(1, round(tall / GRID))
                for c in range(cols):
                    for r in range(rows):
                        local = tuple(-inward[k] * .5 + across[k] * ((c + .5) / cols - .5) + up[k] * ((r + .5) / rows - .5) for k in range(3))
                        o = tuple(centre[k] + local[k] * size[k] - inward[k] * .01 for k in range(3))
                        fired += 1
                        t = cast(sp, o, inward, 0.0, deep + .02)
                        if t is None: missed += 1
                        if fired % 7 == 0:   # the leaves against every triangle, on a sample
                            plain = None
                            for tri in tris:
                                x = hit(tri, o, inward, 0.0, deep + .02 if plain is None else plain)
                                if x is not None: plain = x
                            if (plain is None) != (t is None) or (plain is not None and abs(plain - t) > 1e-6): disagree += 1
            print("   %-30s rays %5d, missing the model %5d (%.0f%%)  [leaves vs every triangle disagree: %d]" % (label, fired, missed, 100 * missed / fired, disagree))
