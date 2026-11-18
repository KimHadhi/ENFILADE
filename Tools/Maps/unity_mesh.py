"""Reads a Unity Mesh saved as a text asset (.asset, YAML): its vertices and triangles, without Unity.
Enough for map audits: the cut and turned meshes a build keeps beside its scene (Scenes/Campaign/<scene>/Pack)."""
import re
import struct


def load(path):
    """Returns (name, vertices [(x, y, z)], triangles [(i, j, k)])."""
    text = open(path, encoding="utf-8", errors="replace").read()
    name = re.search(r"m_Name: (.*)", text).group(1).strip()
    count = int(re.search(r"m_VertexCount: (\d+)", text).group(1))
    fmt = int(re.search(r"m_IndexFormat: (\d+)", text).group(1))
    index_hex = re.search(r"m_IndexBuffer: ([0-9a-f]*)", text).group(1)
    size = int(re.search(r"m_DataSize: (\d+)", text).group(1))
    data_hex = re.search(r"_typelessdata: ([0-9a-f]*)", text).group(1)
    raw = bytes.fromhex(data_hex)
    stride = size // count if count else 0
    # The position is the first stream's first channel: three floats at the start of each vertex.
    vertices = [struct.unpack_from("<3f", raw, i * stride) for i in range(count)]
    idx = bytes.fromhex(index_hex)
    width, code = (2, "H") if fmt == 0 else (4, "I")
    indices = struct.unpack("<%d%s" % (len(idx) // width, code), idx)
    triangles = [(indices[i], indices[i + 1], indices[i + 2]) for i in range(0, len(indices) - 2, 3)]
    return name, vertices, triangles


def bounds(vertices):
    xs, ys, zs = [v[0] for v in vertices], [v[1] for v in vertices], [v[2] for v in vertices]
    return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))
