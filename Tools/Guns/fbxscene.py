"""Binary FBX scene reader (data only, never executed): every Model's global transform (full FBX formula with pre/post
rotation and pivots), meshes with their vertices placed in the file's global frame, skin clusters, blend shapes, and the
file's axis/unit settings. Coordinates stay in the FBX's own global frame (file units); `to_metres_yup` brings a point
to metres with Y up (right-handed, as the file has it)."""
import math
import struct
import zlib


def _array(f, kind):
    length, enc, clen = struct.unpack('<III', f.read(12))
    raw = f.read(clen)
    if enc == 1:
        raw = zlib.decompress(raw)
    fmt = {'f': 'f', 'd': 'd', 'l': 'q', 'i': 'i', 'b': 'b', 'c': 'b'}[kind]
    return list(struct.unpack('<%d%s' % (length, fmt), raw[:length * struct.calcsize(fmt)]))


def _props(f, n):
    out = []
    for _ in range(n):
        t = f.read(1)
        if t in (b'S', b'R'):
            ln = struct.unpack('<I', f.read(4))[0]
            out.append(f.read(ln))
        elif t == b'Y':
            out.append(struct.unpack('<h', f.read(2))[0])
        elif t == b'C':
            out.append(f.read(1) != b'\x00')
        elif t == b'I':
            out.append(struct.unpack('<i', f.read(4))[0])
        elif t == b'F':
            out.append(struct.unpack('<f', f.read(4))[0])
        elif t == b'D':
            out.append(struct.unpack('<d', f.read(8))[0])
        elif t == b'L':
            out.append(struct.unpack('<q', f.read(8))[0])
        elif t in (b'f', b'd', b'l', b'i', b'b', b'c'):
            out.append(_array(f, t.decode()))
        else:
            raise ValueError('bad prop %r' % t)
    return out


def _node(f, wide):
    if wide:
        end, nprops, plen = struct.unpack('<QQQ', f.read(24))
    else:
        end, nprops, plen = struct.unpack('<III', f.read(12))
    nlen = f.read(1)[0]
    name = f.read(nlen).decode('ascii', 'replace')
    if end == 0:
        return None
    start = f.tell()
    props = _props(f, nprops)
    f.seek(start + plen)
    kids = []
    sentinel = 25 if wide else 13
    while f.tell() < end:
        if end - f.tell() == sentinel:
            f.read(sentinel)
            break
        c = _node(f, wide)
        if c is None:
            break
        kids.append(c)
    f.seek(end)
    return (name, props, kids)


def parse(path):
    with open(path, 'rb') as f:
        head = f.read(27)
        if not head.startswith(b'Kaydara FBX Binary'):
            raise ValueError('not a binary FBX: %s' % path)
        wide = struct.unpack('<I', head[23:27])[0] >= 7500
        nodes = []
        while True:
            try:
                n = _node(f, wide)
            except Exception:
                break
            if n is None:
                break
            nodes.append(n)
    return nodes


def text(b):
    return b.split(b'\x00\x01')[0].decode('utf-8', 'replace') if isinstance(b, bytes) else str(b)


def child(node, name):
    for c in node[2]:
        if c[0] == name:
            return c
    return None


def p70(node):
    out = {}
    pr = child(node, 'Properties70')
    if pr:
        for p in pr[2]:
            if p[0] == 'P':
                out[text(p[1][0])] = p[1][4:]
    return out


# ---- 4x4 matrices as row-major lists acting on column vectors ----
def ident():
    return [[1.0 if i == j else 0.0 for j in range(4)] for i in range(4)]


def mmul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def trans(t):
    m = ident()
    m[0][3], m[1][3], m[2][3] = t
    return m


def scale(s):
    m = ident()
    m[0][0], m[1][1], m[2][2] = s
    return m


def rot_euler(r, order=0):
    """FBX euler (degrees). Order 0 = eEulerXYZ: R = Rz * Ry * Rx."""
    x, y, z = (math.radians(a) for a in r)
    cx, sx, cy, sy, cz, sz = math.cos(x), math.sin(x), math.cos(y), math.sin(y), math.cos(z), math.sin(z)
    rx = [[1, 0, 0, 0], [0, cx, -sx, 0], [0, sx, cx, 0], [0, 0, 0, 1]]
    ry = [[cy, 0, sy, 0], [0, 1, 0, 0], [-sy, 0, cy, 0], [0, 0, 0, 1]]
    rz = [[cz, -sz, 0, 0], [sz, cz, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]]
    seq = {0: (rz, ry, rx), 1: (ry, rz, rx), 2: (rz, rx, ry), 3: (rx, rz, ry), 4: (ry, rx, rz), 5: (rx, ry, rz)}[order]
    return mmul(seq[0], mmul(seq[1], seq[2]))


def inv_rigid_scale(m):
    """Inverse of an affine matrix (general 3x3 inverse)."""
    a = [row[:3] for row in m[:3]]
    det = (a[0][0] * (a[1][1] * a[2][2] - a[1][2] * a[2][1]) - a[0][1] * (a[1][0] * a[2][2] - a[1][2] * a[2][0])
           + a[0][2] * (a[1][0] * a[2][1] - a[1][1] * a[2][0]))
    inv = [[(a[1][1] * a[2][2] - a[1][2] * a[2][1]) / det, (a[0][2] * a[2][1] - a[0][1] * a[2][2]) / det, (a[0][1] * a[1][2] - a[0][2] * a[1][1]) / det],
           [(a[1][2] * a[2][0] - a[1][0] * a[2][2]) / det, (a[0][0] * a[2][2] - a[0][2] * a[2][0]) / det, (a[0][2] * a[1][0] - a[0][0] * a[1][2]) / det],
           [(a[1][0] * a[2][1] - a[1][1] * a[2][0]) / det, (a[0][1] * a[2][0] - a[0][0] * a[2][1]) / det, (a[0][0] * a[1][1] - a[0][1] * a[1][0]) / det]]
    t = [m[0][3], m[1][3], m[2][3]]
    it = [-(inv[i][0] * t[0] + inv[i][1] * t[1] + inv[i][2] * t[2]) for i in range(3)]
    return [inv[0] + [it[0]], inv[1] + [it[1]], inv[2] + [it[2]], [0, 0, 0, 1]]


def apply(m, v):
    return (m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2] + m[0][3],
            m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2] + m[1][3],
            m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2] + m[2][3])


def from_flat(a):
    """FBX stores matrices column-major (16 doubles)."""
    return [[a[0], a[4], a[8], a[12]], [a[1], a[5], a[9], a[13]], [a[2], a[6], a[10], a[14]], [0, 0, 0, 1]]


def local_matrix(props):
    def v(name, default):
        got = props.get(name)
        return tuple(got[:3]) if got and len(got) >= 3 else default
    order = props.get('RotationOrder', [0])[0] if props.get('RotationOrder') else 0
    t = v('Lcl Translation', (0, 0, 0))
    r = v('Lcl Rotation', (0, 0, 0))
    s = v('Lcl Scaling', (1, 1, 1))
    pre = v('PreRotation', (0, 0, 0))
    post = v('PostRotation', (0, 0, 0))
    roff = v('RotationOffset', (0, 0, 0))
    rp = v('RotationPivot', (0, 0, 0))
    soff = v('ScalingOffset', (0, 0, 0))
    sp = v('ScalingPivot', (0, 0, 0))
    m = trans(t)
    m = mmul(m, trans(roff))
    m = mmul(m, trans(rp))
    m = mmul(m, rot_euler(pre, 0))
    m = mmul(m, rot_euler(r, order))
    m = mmul(m, inv_rigid_scale(rot_euler(post, 0)))
    m = mmul(m, trans(tuple(-x for x in rp)))
    m = mmul(m, trans(soff))
    m = mmul(m, trans(sp))
    m = mmul(m, scale(s))
    m = mmul(m, trans(tuple(-x for x in sp)))
    return m


def geometric_matrix(props):
    def v(name, default):
        got = props.get(name)
        return tuple(got[:3]) if got and len(got) >= 3 else default
    m = trans(v('GeometricTranslation', (0, 0, 0)))
    m = mmul(m, rot_euler(v('GeometricRotation', (0, 0, 0)), 0))
    return mmul(m, scale(v('GeometricScaling', (1, 1, 1))))


class Scene:
    def __init__(self, path, want_shapes=False):
        nodes = parse(path)
        self.objects, conns, self.settings = {}, [], {}
        for name, props, kids in nodes:
            if name == 'Objects':
                for c in kids:
                    if len(c[1]) >= 3:
                        self.objects[c[1][0]] = c
            elif name == 'Connections':
                for c in kids:
                    if c[0] == 'C':
                        conns.append((text(c[1][0]), c[1][1], c[1][2], text(c[1][3]) if len(c[1]) > 3 else None))
            elif name == 'GlobalSettings':
                self.settings = {k: v[0] for k, v in p70((name, props, kids)).items() if v}
        self.kids, self.parents = {}, {}
        for kind, c, p, prop in conns:
            self.kids.setdefault(p, []).append((c, kind, prop))
            self.parents.setdefault(c, []).append((p, kind, prop))
        self.models = {oid: n for oid, n in self.objects.items() if n[0] == 'Model'}
        self.name = {oid: text(n[1][1]) for oid, n in self.objects.items()}
        self.kind = {oid: text(n[1][2]) for oid, n in self.objects.items()}
        self.parent_model = {}
        for oid in self.models:
            for p, kind, prop in self.parents.get(oid, []):
                if kind == 'OO' and p in self.models:
                    self.parent_model[oid] = p
        self._global = {}
        self.want_shapes = want_shapes

    def global_matrix(self, oid):
        if oid in self._global:
            return self._global[oid]
        local = local_matrix(p70(self.models[oid]))
        parent = self.parent_model.get(oid)
        g = mmul(self.global_matrix(parent), local) if parent is not None else local
        self._global[oid] = g
        return g

    def model_by_name(self, name):
        for oid in self.models:
            if self.name[oid] == name:
                return oid
        return None

    def path_of(self, oid):
        names = []
        while oid is not None:
            names.append(self.name[oid])
            oid = self.parent_model.get(oid)
        return '/'.join(reversed(names))

    def meshes(self):
        """name -> dict(points (global, file units), polys, model id, clusters {bone name: (indexes, weights)}, shapes)."""
        out = {}
        for gid, node in self.objects.items():
            if node[0] != 'Geometry' or self.kind[gid] != 'Mesh':
                continue
            models = [p for p, kind, prop in self.parents.get(gid, []) if p in self.models]
            if not models:
                continue
            mid = models[0]
            props = p70(self.models[mid])
            g = mmul(self.global_matrix(mid), geometric_matrix(props))
            raw = child(node, 'Vertices')[1][0]
            pts = [apply(g, (raw[i], raw[i + 1], raw[i + 2])) for i in range(0, len(raw), 3)]
            polys, poly = [], []
            pvi = child(node, 'PolygonVertexIndex')
            for idx in (pvi[1][0] if pvi else []):
                if idx < 0:
                    poly.append(~idx)
                    polys.append(poly)
                    poly = []
                else:
                    poly.append(idx)
            clusters, shapes = {}, {}
            for d, kind, prop in self.kids.get(gid, []):
                if d not in self.objects or self.objects[d][0] != 'Deformer':
                    continue
                if self.kind[d] == 'Skin':
                    for cid, _, _ in self.kids.get(d, []):
                        if cid not in self.objects:
                            continue
                        cl = self.objects[cid]
                        bones = [b for b, _, _ in self.kids.get(cid, []) if b in self.models]
                        idx = child(cl, 'Indexes')
                        wts = child(cl, 'Weights')
                        tl = child(cl, 'TransformLink')
                        tr = child(cl, 'Transform')
                        if bones:
                            clusters[self.name[bones[0]]] = (idx[1][0] if idx else [], wts[1][0] if wts else [],
                                                             from_flat(tl[1][0]) if tl else None, from_flat(tr[1][0]) if tr else None)
                elif self.kind[d] == 'BlendShape' and self.want_shapes:
                    for chid, _, _ in self.kids.get(d, []):
                        if chid not in self.objects:
                            continue
                        for sg, _, _ in self.kids.get(chid, []):
                            if sg in self.objects and self.objects[sg][0] == 'Geometry':
                                idx = child(self.objects[sg], 'Indexes')[1][0]
                                dv = child(self.objects[sg], 'Vertices')[1][0]
                                rot = [row[:3] + [0] for row in g[:3]] + [[0, 0, 0, 1]]
                                shapes[self.name[chid]] = {i: apply(rot, (dv[3 * k], dv[3 * k + 1], dv[3 * k + 2])) for k, i in enumerate(idx)}
            out[self.name[mid]] = {'points': pts, 'polys': polys, 'model': mid, 'clusters': clusters, 'shapes': shapes}
        return out

    def to_metres_yup(self, p):
        """File global → metres, Y up (right-handed, file's own handedness)."""
        unit = self.settings.get('UnitScaleFactor', 1.0) / 100.0
        up, sign = self.settings.get('UpAxis', 1), self.settings.get('UpAxisSign', 1)
        x, y, z = p[0] * unit, p[1] * unit, p[2] * unit
        if up == 2:
            # Z up → Y up, keeping right-handedness: (x, y, z) → (x, z, -y)
            return (x, z * sign, -y * sign)
        if up == 0:
            return (-y, x * sign, z)
        return (x, y * sign, z)
