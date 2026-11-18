"""Reads a built Unity scene's YAML without Unity: game objects, their transforms in world space, and their components.
Enough for map audits: where every box of a builder-made scene stands, how big it is and what it carries.
Only objects written in the scene itself are placed in the world (a prefab instance's inner transforms live in the prefab)."""
import json
import math
import re

HEAD = re.compile(rb"^--- !u!(\d+) &(-?\d+)([^\r\n]*)\r?\n", re.M)


def _vec(body, key, n):
    pat = key + rb": \{x: ([-\d.eE]+), y: ([-\d.eE]+), z: ([-\d.eE]+)" + (rb", w: ([-\d.eE]+)" if n == 4 else b"") + rb"\}"
    m = re.search(pat, body)
    return [float(v) for v in m.groups()] if m else None


def _ref(body, key):
    m = re.search(key + rb": \{fileID: (-?\d+)", body)
    return int(m.group(1)) if m else 0


def _name(body):
    """An object's m_Name. Unity writes a name with non-ASCII letters as a double-quoted YAML scalar with \\uXXXX
    escapes, and folds a long one over several lines (a line break and the next line's indent stand for one space)."""
    m = re.search(rb"m_Name: ", body)
    if not m:
        return ""
    rest = body[m.end():]
    if rest.startswith(b'"'):
        end = 1
        while end < len(rest) and not (rest[end:end + 1] == b'"' and rest[end - 1:end] != b"\\"):
            end += 1
        raw = re.sub(rb"\r?\n\s*", b" ", rest[1:end]).decode("utf-8", "replace")
        try:
            return json.loads('"' + raw + '"')
        except ValueError:
            return raw
    line = rest.split(b"\n", 1)[0].strip().decode("utf-8", "replace")
    if line.startswith("'") and line.endswith("'") and len(line) >= 2:
        return line[1:-1].replace("''", "'")
    return line


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return [aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz]


def qrot(q, v):
    x, y, z, w = q
    uv = (y * v[2] - z * v[1], z * v[0] - x * v[2], x * v[1] - y * v[0])
    uuv = (y * uv[2] - z * uv[1], z * uv[0] - x * uv[2], x * uv[1] - y * uv[0])
    return [v[i] + 2 * (w * uv[i] + uuv[i]) for i in range(3)]


class Obj:
    __slots__ = ("id", "name", "active", "layer", "transform", "parent", "children", "pos", "rot", "scale",
                 "wpos", "wrot", "wscale", "comps", "path", "static")

    def __init__(self):
        self.children = []
        self.comps = []
        self.parent = None
        self.pos = [0, 0, 0]
        self.rot = [0, 0, 0, 1]
        self.scale = [1, 1, 1]

    def has(self, kind):
        return any(c[0] == kind for c in self.comps)

    def comp(self, kind):
        for c in self.comps:
            if c[0] == kind:
                return c[1]
        return None

    def yaw(self):
        f = qrot(self.wrot, [0, 0, 1])
        return math.degrees(math.atan2(f[0], f[2]))


# Unity class ids worth knowing here.
KINDS = {1: "GameObject", 4: "Transform", 23: "MeshRenderer", 33: "MeshFilter", 64: "MeshCollider", 65: "BoxCollider",
         135: "SphereCollider", 136: "CapsuleCollider", 114: "MonoBehaviour", 108: "Light", 205: "LODGroup",
         208: "NavMeshObstacle", 1001: "PrefabInstance", 137: "SkinnedMeshRenderer", 54: "Rigidbody", 224: "RectTransform"}


def load(path):
    data = open(path, "rb").read()
    heads = list(HEAD.finditer(data))
    docs = {}
    for i, m in enumerate(heads):
        end = heads[i + 1].start() if i + 1 < len(heads) else len(data)
        docs[int(m.group(2))] = (int(m.group(1)), b"stripped" in m.group(3), data[m.end():end])
    objs = {}
    by_transform = {}
    for fid, (cls, stripped, body) in docs.items():
        if cls != 1 or stripped:
            continue
        o = Obj()
        o.id = fid
        o.name = _name(body)
        m = re.search(rb"m_IsActive: (\d)", body)
        o.active = m is None or m.group(1) == b"1"
        m = re.search(rb"m_Layer: (\d+)", body)
        o.layer = int(m.group(1)) if m else 0
        m = re.search(rb"m_StaticEditorFlags: (\d+)", body)
        o.static = int(m.group(1)) if m else 0
        o.transform = 0
        for cm in re.finditer(rb"- component: \{fileID: (-?\d+)\}", body):
            cid = int(cm.group(1))
            if cid not in docs:
                continue
            ccls, _, cbody = docs[cid]
            kind = KINDS.get(ccls, str(ccls))
            if ccls in (4, 224):
                o.transform = cid
                by_transform[cid] = o
                o.pos = _vec(cbody, rb"m_LocalPosition", 3) or [0, 0, 0]
                o.rot = _vec(cbody, rb"m_LocalRotation", 4) or [0, 0, 0, 1]
                o.scale = _vec(cbody, rb"m_LocalScale", 3) or [1, 1, 1]
                o.parent = _ref(cbody, rb"m_Father")
            else:
                o.comps.append((kind, cbody))
        objs[fid] = o
    # parents
    for o in objs.values():
        p = by_transform.get(o.parent)
        o.parent = p
        if p is not None:
            p.children.append(o)

    def place(o, wpos, wrot, wscale, path):
        o.wscale = [wscale[i] * o.scale[i] for i in range(3)]
        o.wrot = qmul(wrot, o.rot)
        local = [o.pos[i] * wscale[i] for i in range(3)]
        r = qrot(wrot, local)
        o.wpos = [wpos[i] + r[i] for i in range(3)]
        o.path = path + "/" + o.name if path else o.name
        for c in o.children:
            place(c, o.wpos, o.wrot, o.wscale, o.path)

    import sys
    sys.setrecursionlimit(10000)
    for o in objs.values():
        if o.parent is None:
            place(o, [0, 0, 0], [0, 0, 0, 1], [1, 1, 1], "")
    return objs, docs


def box_of(o):
    """World box of an object's BoxCollider: (centre, size, yaw degrees) for boxes turned about Y only, else None."""
    body = o.comp("BoxCollider")
    if body is None:
        return None
    size = _vec(body, rb"m_Size", 3) or [1, 1, 1]
    centre = _vec(body, rb"m_Center", 3) or [0, 0, 0]
    c = qrot(o.wrot, [centre[i] * o.wscale[i] for i in range(3)])
    world = [o.wpos[i] + c[i] for i in range(3)]
    dims = [abs(size[i] * o.wscale[i]) for i in range(3)]
    up = qrot(o.wrot, [0, 1, 0])
    upright = up[1] > 0.999
    return world, dims, o.yaw(), upright


def enabled(body):
    m = re.search(rb"m_Enabled: (\d)", body)
    return m is None or m.group(1) == b"1"


def is_trigger(body):
    m = re.search(rb"m_IsTrigger: (\d)", body)
    return m is not None and m.group(1) == b"1"
