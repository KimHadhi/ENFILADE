# Triangle/vertex counts and embedded-media size of binary FBX files, read straight from zips and folders without
# extracting (nested zips included). Usage: python -I fbx_stats.py <root> [<root> ...]
import io
import os
import struct
import sys
import zipfile
import zlib

ARRAY = {"f": 4, "d": 8, "l": 8, "i": 4, "b": 1, "c": 1}
SCALAR = {"Y": 2, "C": 1, "I": 4, "F": 4, "D": 8, "L": 8}


def parse(data):
    if not data.startswith(b"Kaydara FBX Binary"):
        return {"ascii": True}
    version = struct.unpack_from("<I", data, 23)[0]
    wide = version >= 7500
    head = "<QQQ" if wide else "<III"
    head_size = 24 if wide else 12
    stats = {"version": version, "meshes": 0, "tris": 0, "verts": 0, "media_mb": 0.0, "names": []}

    def read_props(pos, count):
        props = []
        for _ in range(count):
            kind = chr(data[pos])
            pos += 1
            if kind in SCALAR:
                pos += SCALAR[kind]
                props.append(None)
            elif kind in ARRAY:
                length, encoding, size = struct.unpack_from("<III", data, pos)
                pos += 12
                raw = data[pos:pos + size]
                pos += size
                props.append((kind, length, encoding, raw))
            elif kind in ("S", "R"):
                size = struct.unpack_from("<I", data, pos)[0]
                pos += 4
                props.append((kind, data[pos:pos + size]))
                pos += size
            else:
                raise ValueError("bad property type %r" % kind)
        return props

    def walk(pos, end, path):
        while pos < end:
            end_offset, count, _ = struct.unpack_from(head, data, pos)
            name_len = data[pos + head_size]
            if end_offset == 0:
                return
            name = data[pos + head_size + 1:pos + head_size + 1 + name_len].decode("latin-1")
            props_start = pos + head_size + 1 + name_len
            props = read_props(props_start, count)
            if name == "Geometry" and len(props) >= 3 and props[2] and props[2][0] == "S" and props[2][1] == b"Mesh":
                stats["meshes"] += 1
                label = props[1][1].split(b"\x00")[0].decode("latin-1", "replace") if props[1] else "?"
                stats["names"].append(label)
            if name == "PolygonVertexIndex" and props and props[0] and props[0][0] == "i":
                _, length, encoding, raw = props[0]
                values = zlib.decompress(raw) if encoding == 1 else raw
                indices = struct.unpack("<%di" % length, values)
                corners = 0
                for index in indices:
                    corners += 1
                    if index < 0:
                        stats["tris"] += corners - 2
                        corners = 0
            if name == "Vertices" and path and path[-1] == "Geometry" and props and props[0] and props[0][0] == "d":
                stats["verts"] += props[0][1] // 3
            if name == "Content" and props and props[0] and props[0][0] == "R":
                stats["media_mb"] += len(props[0][1]) / 1048576
            child = props_start
            # skip the property bytes to reach nested records
            for prop in props:
                pass
            nested_start = props_start + sum(prop_size(p) for p in props)
            if nested_start < end_offset:
                walk(nested_start, end_offset - (25 if wide else 13), path + [name])
            pos = end_offset

    def prop_size(prop):
        if prop is None:
            raise RuntimeError
        return 0

    # Simpler: compute property block length from the record header instead of re-sizing each property.
    def walk2(pos, end, path):
        while pos + head_size < end:
            end_offset, count, plen = struct.unpack_from(head, data, pos)
            if end_offset == 0:
                return
            name_len = data[pos + head_size]
            name = data[pos + head_size + 1:pos + head_size + 1 + name_len].decode("latin-1")
            props_start = pos + head_size + 1 + name_len
            props = read_props(props_start, count)
            if name == "Geometry" and len(props) >= 3 and isinstance(props[2], tuple) and props[2][0] == "S" and props[2][1] == b"Mesh":
                stats["meshes"] += 1
                stats["names"].append(props[1][1].split(b"\x00")[0].decode("latin-1", "replace") if isinstance(props[1], tuple) else "?")
            if name == "PolygonVertexIndex" and props and isinstance(props[0], tuple) and props[0][0] == "i":
                _, length, encoding, raw = props[0]
                values = zlib.decompress(raw) if encoding == 1 else raw
                corners = 0
                for index in struct.unpack("<%di" % length, values):
                    corners += 1
                    if index < 0:
                        stats["tris"] += corners - 2
                        corners = 0
            if name == "Vertices" and path and path[-1] == "Geometry" and props and isinstance(props[0], tuple) and props[0][0] == "d":
                stats["verts"] += props[0][1] // 3
            if name == "Content" and props and isinstance(props[0], tuple) and props[0][0] == "R":
                stats["media_mb"] += len(props[0][1]) / 1048576
            nested_start = props_start + plen
            if nested_start < end_offset:
                walk2(nested_start, end_offset, path + [name])
            pos = end_offset

    walk2(27, len(data), [])
    stats["media_mb"] = round(stats["media_mb"], 1)
    stats["names"] = stats["names"][:6]
    return stats


def report(label, data):
    try:
        stats = parse(data)
    except Exception as error:
        print("%-90s PARSE FAIL %r" % (label[:90], error))
        return
    if stats.get("ascii"):
        print("%-90s ascii FBX" % label[:90])
        return
    print("%-90s meshes %3d  tris %8d  verts %8d  media %5.1f MB  %s" % (
        label[:90], stats["meshes"], stats["tris"], stats["verts"], stats["media_mb"], ",".join(stats["names"])[:60]))


def scan_zip(label, archive):
    for entry in archive.infolist():
        low = entry.filename.lower()
        if low.endswith(".fbx"):
            report(label + "!" + entry.filename, archive.read(entry))
        elif low.endswith(".zip"):
            with zipfile.ZipFile(io.BytesIO(archive.read(entry))) as inner:
                print("%s!%s contains: %s" % (label, entry.filename, ", ".join(e.filename for e in inner.infolist())[:300]))
                scan_zip(label + "!" + entry.filename, inner)


for root in sys.argv[1:]:
    if os.path.isfile(root):
        if root.lower().endswith(".zip"):
            with zipfile.ZipFile(root) as archive:
                scan_zip(os.path.basename(root), archive)
        elif root.lower().endswith(".fbx"):
            with open(root, "rb") as f:
                report(os.path.basename(root), f.read())
        continue
    for base, _, files in os.walk(root):
        for file in sorted(files):
            full = os.path.join(base, file)
            if file.lower().endswith(".fbx"):
                with open(full, "rb") as f:
                    report(os.path.relpath(full, root), f.read())
