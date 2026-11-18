"""The size of each mesh in binary FBX files (old 6.x and 7.x), read without Blender or Unity: the bounds of its
vertices in the file's own units and axes, its node's own scaling, and the bounds times the importer's scale from the
.meta beside it (globalScale; useFileScale on = the file's UnitScaleFactor in cm per unit too).
usage: python fbx_size.py <file.fbx> [...]"""
import os
import re
import struct
import sys
import zlib


def read(data):
    version = struct.unpack_from("<I", data, 23)[0]
    wide = version >= 7500
    head = "<QQQ" if wide else "<III"
    hs = 24 if wide else 12
    out = []

    def props(pos, count):
        values = []
        for _ in range(count):
            kind = chr(data[pos])
            pos += 1
            if kind == "Y":
                values.append(struct.unpack_from("<h", data, pos)[0]); pos += 2
            elif kind == "C":
                values.append(data[pos]); pos += 1
            elif kind == "I":
                values.append(struct.unpack_from("<i", data, pos)[0]); pos += 4
            elif kind == "F":
                values.append(struct.unpack_from("<f", data, pos)[0]); pos += 4
            elif kind == "D":
                values.append(struct.unpack_from("<d", data, pos)[0]); pos += 8
            elif kind == "L":
                values.append(struct.unpack_from("<q", data, pos)[0]); pos += 8
            elif kind in "fdlib":
                length, encoding, size = struct.unpack_from("<III", data, pos)
                pos += 12
                raw = data[pos:pos + size]
                pos += size
                if encoding == 1:
                    raw = zlib.decompress(raw)
                fmt = {"f": "f", "d": "d", "l": "q", "i": "i", "b": "b"}[kind]
                values.append(list(struct.unpack("<%d%s" % (length, fmt), raw)))
            elif kind in "SR":
                size = struct.unpack_from("<I", data, pos)[0]
                pos += 4
                values.append(data[pos:pos + size])
                pos += size
            else:
                raise ValueError("bad property %r" % kind)
        return values

    def walk(pos, end, path):
        nodes = []
        while pos + hs < end:
            end_offset, count, plen = struct.unpack_from(head, data, pos)
            if end_offset == 0:
                break
            nlen = data[pos + hs]
            name = data[pos + hs + 1:pos + hs + 1 + nlen].decode("latin-1")
            pstart = pos + hs + 1 + nlen
            p = props(pstart, count)
            children = walk(pstart + plen, end_offset, path + [name]) if pstart + plen < end_offset else []
            nodes.append((name, p, children))
            pos = end_offset
        return nodes

    return version, walk(27, len(data), [])


def find(nodes, name):
    for n in nodes:
        if n[0] == name:
            yield n
        yield from find(n[2], name)


def flat(values):
    if len(values) == 1 and isinstance(values[0], list):
        return values[0]
    return [v for v in values if isinstance(v, (int, float))]


def bounds(vs):
    xs, ys, zs = vs[0::3], vs[1::3], vs[2::3]
    return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))


def prop70(node, wanted):
    """A Properties60/70 value by name."""
    for block in node[2]:
        if block[0] in ("Properties60", "Properties70"):
            for p in block[2]:
                if p[1] and isinstance(p[1][0], bytes) and p[1][0] == wanted.encode():
                    return [v for v in p[1] if isinstance(v, (int, float))]
    return None


def meta_scale(path):
    meta = path + ".meta"
    if not os.path.exists(meta):
        return 1.0, False
    text = open(meta, encoding="utf-8", errors="replace").read()
    g = re.search(r"globalScale: ([-\d.eE]+)", text)
    f = re.search(r"useFileScale: (\d)", text)
    return (float(g.group(1)) if g else 1.0), (f is not None and f.group(1) == "1")


for path in sys.argv[1:]:
    data = open(path, "rb").read()
    if not data.startswith(b"Kaydara FBX Binary"):
        print(path, "ascii FBX, not read")
        continue
    version, nodes = read(data)
    unit = 1.0
    for settings in find(nodes, "GlobalSettings"):
        v = prop70(settings, "UnitScaleFactor")
        if v:
            unit = v[-1]
    scale, file_scale = meta_scale(path)
    factor = scale * (unit / 100.0 if file_scale else 1.0)
    print("%s  (FBX %d, UnitScaleFactor %g, meta globalScale %g useFileScale %d -> x%g)" % (os.path.basename(path), version, unit, scale, file_scale, factor))
    lo_all, hi_all = [1e18] * 3, [-1e18] * 3
    holders = list(find(nodes, "Model")) + list(find(nodes, "Geometry"))
    for holder in holders:
        for v in holder[2]:
            if v[0] != "Vertices":
                continue
            vs = flat(v[1])
            if len(vs) < 9:
                continue
            lo, hi = bounds(vs)
            scaling = prop70(holder, "Lcl Scaling") or [1, 1, 1]
            rotation = prop70(holder, "Lcl Rotation") or [0, 0, 0]
            name = holder[1][0].split(b"\x00")[0].decode("latin-1", "replace") if holder[1] and isinstance(holder[1][0], bytes) else (
                holder[1][1].split(b"\x00")[0].decode("latin-1", "replace") if len(holder[1]) > 1 and isinstance(holder[1][1], bytes) else "?")
            size = [(hi[i] - lo[i]) * abs(scaling[i] if len(scaling) > i else 1) * factor for i in range(3)]
            print("   %-40s raw %.1f x %.1f x %.1f  scaling %s rotation %s -> %.3f x %.3f x %.3f m (file axes)" % (
                name[:40], hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2], [round(s, 3) for s in scaling[-3:]],
                [round(r, 1) for r in rotation[-3:]], size[0], size[1], size[2]))
            for i in range(3):
                lo_all[i] = min(lo_all[i], lo[i] * factor)
                hi_all[i] = max(hi_all[i], hi[i] * factor)
    if lo_all[0] < 1e17:
        print("   all meshes together (no node transforms): %.3f x %.3f x %.3f m" % tuple(hi_all[i] - lo_all[i] for i in range(3)))
