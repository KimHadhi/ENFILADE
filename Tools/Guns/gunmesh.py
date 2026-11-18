"""A gun prefab's geometry without Unity: every renderer's triangles in the prefab root's space (metres, Unity's
left-handed frame), read from the prefab's YAML (transforms, renderers, bones) and the FBX files its meshes come from.
Skinned meshes are posed by the prefab's own bone transforms (bone-local bind points from the FBX clusters).
Data only: nothing is run. Needs numpy (Blender's bundled python has it).
What it cannot read: a mesh that is no FBX (a .asset, a .blend) is left out of the gun's shape; an FBX that is not binary
and a model whose axes Unity's importer bakes (bakeAxisConversion: the Desperado's) raise, and the gun is "not read"."""
import hashlib
import json
import os
import re
import sys
import tempfile

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fbxscene as F

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
DOC = re.compile(r"^--- !u!(\d+) &(-?\d+)( stripped)?\s*$", re.M)
_guid_index = None
_guid_index_fresh = False
_fbx_cache = {}


def _guid_cache():
    """Where the guid index is kept between runs: the system's temp folder, a file per checkout (nothing in the project)."""
    tag = hashlib.md5(REPO.lower().encode("utf-8")).hexdigest()[:10]
    return os.path.join(tempfile.gettempdir(), "enfilade-gun-mounts-guids-%s.json" % tag)


def guid_index(rebuild=False):
    """guid -> asset path (relative to the repo), for the folders gun assets live in. Walking every .meta takes a while,
    so the index is kept between runs; asset_of builds it again when a guid is missing from it."""
    global _guid_index, _guid_index_fresh
    if _guid_index is not None and not rebuild:
        return _guid_index
    cache = _guid_cache()
    if os.path.exists(cache) and not rebuild:
        try:
            _guid_index = json.load(open(cache, encoding="utf-8"))
            return _guid_index
        except (OSError, ValueError):
            pass
    index = {}
    for top in ("Assets/ThirdParty", "Assets/_Project"):
        for root, _, files in os.walk(os.path.join(REPO, top)):
            for name in files:
                if not name.endswith(".meta"):
                    continue
                path = os.path.join(root, name)
                try:
                    with open(path, encoding="utf-8", errors="replace") as f:
                        for _ in range(4):
                            line = f.readline()
                            if line.startswith("guid: "):
                                index[line[6:].strip()] = os.path.relpath(path[:-5], REPO).replace("\\", "/")
                                break
                except OSError:
                    pass
    try:
        json.dump(index, open(cache, "w", encoding="utf-8"))
    except OSError:
        pass
    _guid_index, _guid_index_fresh = index, True
    return index


def asset_of(guid):
    """The asset a guid names (a path relative to the repo), or None. An asset added since the index was kept is found by
    building the index once more."""
    path = guid_index().get(guid)
    if path is None and not _guid_index_fresh:
        path = guid_index(rebuild=True).get(guid)
    return path


# ---- quaternions / matrices (numpy, column vectors) ----
def q_to_m(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def trs(pos, rot, scale):
    m = np.eye(4)
    m[:3, :3] = q_to_m(rot) * np.array(scale)[None, :]
    m[:3, 3] = pos
    return m


def field(body, name):
    m = re.search(r"^\s*" + re.escape(name) + r": (.*)$", body, re.M)
    return m.group(1).strip() if m else None


def vec(text, keys):
    return tuple(float(re.search(k + r": ([-\d.eE+]+)", text).group(1)) for k in keys)


class Prefab:
    def __init__(self, path):
        self.path = path
        text = open(path, encoding="utf-8", errors="replace").read()
        self.docs = {}
        heads = list(DOC.finditer(text))
        for i, m in enumerate(heads):
            end = heads[i + 1].start() if i + 1 < len(heads) else len(text)
            self.docs[m.group(2)] = (int(m.group(1)), text[m.end():end])
        self.go_name, self.go_active, self.go_comps = {}, {}, {}
        self.tr = {}
        for fid, (cls, body) in self.docs.items():
            if cls == 1:
                self.go_name[fid] = field(body, "m_Name")
                self.go_active[fid] = field(body, "m_IsActive") == "1"
                self.go_comps[fid] = re.findall(r"component: \{fileID: (-?\d+)\}", body)
            elif cls in (4, 224):
                go = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body).group(1)
                father = re.search(r"m_Father: \{fileID: (-?\d+)\}", body).group(1)
                kids = []
                km = re.search(r"m_Children:\s*\n((?:\s*- .*\n)*)", body)
                if km:
                    kids = re.findall(r"fileID: (-?\d+)", km.group(1))
                self.tr[fid] = dict(go=go, father=father, kids=kids,
                                    pos=vec(field(body, "m_LocalPosition"), "xyz"),
                                    rot=vec(field(body, "m_LocalRotation"), "xyzw"),
                                    scale=vec(field(body, "m_LocalScale"), "xyz"))
        self.tr_of_go = {t["go"]: fid for fid, t in self.tr.items()}
        self.root = next(fid for fid, t in self.tr.items() if t["father"] == "0")
        self._world = {}

    def name(self, tr_id):
        return self.go_name.get(self.tr[tr_id]["go"], "?")

    def local(self, tr_id):
        t = self.tr[tr_id]
        return trs(t["pos"], t["rot"], t["scale"])

    def world(self, tr_id):
        """The transform's matrix in the prefab root's own space (the root itself is identity)."""
        if tr_id in self._world:
            return self._world[tr_id]
        if tr_id == self.root:
            m = np.eye(4)
        else:
            m = self.world(self.tr[tr_id]["father"]) @ self.local(tr_id)
        self._world[tr_id] = m
        return m

    def set_local(self, tr_id, pos=None, rot=None):
        if pos is not None:
            self.tr[tr_id]["pos"] = tuple(pos)
        if rot is not None:
            self.tr[tr_id]["rot"] = tuple(rot)
        self._world = {}

    def active(self, tr_id):
        """Active in the hierarchy as the prefab is saved."""
        while tr_id in self.tr:
            if not self.go_active.get(self.tr[tr_id]["go"], True):
                return False
            tr_id = self.tr[tr_id]["father"]
        return True

    def path_of(self, tr_id):
        names = []
        while tr_id in self.tr:
            names.append(self.name(tr_id))
            tr_id = self.tr[tr_id]["father"]
        return "/".join(reversed(names))

    def find(self, name, under=None):
        out = []
        for fid in self.tr:
            if self.name(fid) == name and (under is None or self.is_under(fid, under)):
                out.append(fid)
        return out

    def is_under(self, tr_id, ancestor):
        while tr_id in self.tr:
            if tr_id == ancestor:
                return True
            tr_id = self.tr[tr_id]["father"]
        return False

    def components(self, tr_id, cls):
        go = self.tr[tr_id]["go"]
        return [(c, self.docs[c][1]) for c in self.go_comps.get(go, []) if c in self.docs and self.docs[c][0] == cls]

    def monos(self):
        return [(fid, body) for fid, (cls, body) in self.docs.items() if cls == 114]

    def renderers(self):
        """(transform id, 'skinned'|'static', mesh guid, mesh fileID, bones [transform ids], enabled)."""
        out = []
        for fid, (cls, body) in self.docs.items():
            if cls == 137:
                go = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body).group(1)
                mesh = re.search(r"m_Mesh: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]+))?", body)
                bm = re.search(r"m_Bones:\s*\n((?:\s*- .*\n)*)", body)
                bones = re.findall(r"fileID: (-?\d+)", bm.group(1)) if bm else []
                out.append((self.tr_of_go[go], "skinned", mesh.group(2), mesh.group(1), bones, field(body, "m_Enabled") == "1"))
            elif cls == 33:
                go = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body).group(1)
                mesh = re.search(r"m_Mesh: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]+))?", body)
                rend = [b for c, b in ((c, self.docs[c][1]) for c in self.go_comps.get(go, []) if c in self.docs and self.docs[c][0] == 23)]
                enabled = bool(rend) and field(rend[0], "m_Enabled") == "1"
                if rend:
                    out.append((self.tr_of_go[go], "static", mesh.group(2), mesh.group(1), [], enabled))
        return out


def fbx(path):
    if path not in _fbx_cache:
        scene = F.Scene(os.path.join(REPO, path))
        _fbx_cache[path] = (scene, scene.meshes())
    return _fbx_cache[path]


def file_scale(path):
    """Unity's import scale for an FBX: its meta's globalScale, times the file's unit (cm -> m) when useFileScale is on."""
    meta = open(os.path.join(REPO, path + ".meta"), encoding="utf-8", errors="replace").read()
    scale = float(re.search(r"globalScale: ([-\d.eE+]+)", meta).group(1)) if "globalScale" in meta else 1.0
    use = re.search(r"useFileScale: (\d)", meta)
    scene, _ = fbx(path)
    if use is None or use.group(1) == "1":
        scale *= scene.settings.get("UnitScaleFactor", 1.0) / 100.0
    return scale


class Unread(Exception):
    """A model this reader cannot place as Unity does."""


_baked = {}


def baked_axes(path):
    """Unity's importer bakes this model's axis conversion into its meshes and bones (the .meta's bakeAxisConversion):
    its vertices then stand otherwise than this reader puts them (the Desperado's Fatebringer came out upside down)."""
    if path not in _baked:
        meta = open(os.path.join(REPO, path + ".meta"), encoding="utf-8", errors="replace").read()
        _baked[path] = re.search(r"bakeAxisConversion: 1\b", meta) is not None
    return _baked[path]


BUILTIN = {"10202": "Cube", "10206": "Cylinder", "10207": "Sphere", "10208": "Capsule", "10209": "Plane", "10210": "Quad"}


def builtin_mesh(kind):
    if kind == "Quad":
        v = np.array([[-.5, -.5, 0], [.5, -.5, 0], [-.5, .5, 0], [.5, .5, 0]], float)
        return v, np.array([[0, 2, 1], [2, 3, 1]])
    if kind == "Plane":
        v = np.array([[-5, 0, -5], [5, 0, -5], [-5, 0, 5], [5, 0, 5]], float)
        return v, np.array([[0, 2, 1], [2, 3, 1]])
    if kind == "Cube":
        v = np.array([[x, y, z] for x in (-.5, .5) for y in (-.5, .5) for z in (-.5, .5)], float)
        t = [[0, 1, 3], [0, 3, 2], [4, 6, 7], [4, 7, 5], [0, 4, 5], [0, 5, 1], [2, 3, 7], [2, 7, 6], [0, 2, 6], [0, 6, 4], [1, 5, 7], [1, 7, 3]]
        return v, np.array(t)
    if kind in ("Sphere", "Cylinder", "Capsule"):
        n, rings = 24, 12
        verts, tris = [], []
        for r in range(rings + 1):
            phi = np.pi * r / rings
            for s in range(n):
                th = 2 * np.pi * s / n
                if kind == "Sphere":
                    verts.append([.5 * np.sin(phi) * np.cos(th), .5 * np.cos(phi), .5 * np.sin(phi) * np.sin(th)])
                else:
                    y = 1.0 - 2.0 * r / rings
                    verts.append([.5 * np.cos(th), y, .5 * np.sin(th)])
        for r in range(rings):
            for s in range(n):
                a, b = r * n + s, r * n + (s + 1) % n
                c, d = a + n, b + n
                tris += [[a, c, b], [b, c, d]]
        return np.array(verts, float), np.array(tris)
    return None


class Part:
    """One renderer's triangles in the gun's space."""

    def __init__(self, tr_id, name, path, kind, active, verts, tris, source, groups=None):
        self.tr_id, self.name, self.path, self.kind, self.active = tr_id, name, path, kind, active
        self.verts, self.tris, self.source = verts, tris, source
        # For a skinned part: the bone each vertex mostly follows (names), else None.
        self.bone = groups


def load_parts(prefab, want=None, quiet=False):
    """Every renderer of the prefab as a Part. want(tr_id) filters."""
    parts = []
    for tr_id, kind, guid, file_id, bones, enabled in prefab.renderers():
        if want is not None and not want(tr_id):
            continue
        name = prefab.name(tr_id)
        active = prefab.active(tr_id) and enabled
        if guid == "0000000000000000e000000000000000" or guid is None:
            shape = builtin_mesh(BUILTIN.get(file_id, ""))
            if shape is None:
                if not quiet:
                    print("  (no mesh for %s: builtin %s)" % (name, file_id))
                continue
            v, t = shape
            m = prefab.world(tr_id)
            parts.append(Part(tr_id, name, prefab.path_of(tr_id), "builtin", active, (m[:3, :3] @ v.T).T + m[:3, 3], t, "builtin:" + BUILTIN.get(file_id, "?")))
            continue
        path = asset_of(guid)
        if path is None or not path.lower().endswith(".fbx"):
            if not quiet:
                print("  (mesh of %s is not an FBX: %s %s)" % (name, guid, path))
            continue
        if baked_axes(path):
            raise Unread("the importer bakes the axes of %s" % os.path.basename(path))
        scene, meshes = fbx(path)
        mesh = meshes.get(name)
        if mesh is None:
            if len(meshes) == 1:
                mesh = next(iter(meshes.values()))
            else:
                # by the cleaned name (Unity strips nothing, but "(Clone)" comes from Instantiate)
                clean = name.replace("(Clone)", "")
                mesh = meshes.get(clean)
                if mesh is None:
                    cands = [k for k in meshes if k.lower() == clean.lower()]
                    mesh = meshes[cands[0]] if cands else None
        if mesh is None:
            if not quiet:
                print("  (no mesh named %s in %s: %s)" % (name, path, list(meshes)[:8]))
            continue
        s = file_scale(path)
        model = mesh["model"]
        raw_global = np.array(mesh["points"], float)            # file global, as the node stands in the file
        tris = []
        for poly in mesh["polys"]:
            for k in range(1, len(poly) - 1):
                tris.append((poly[0], poly[k + 1], poly[k]))    # winding flips with the mirror
        tris = np.array(tris, int) if tris else np.zeros((0, 3), int)
        flip = np.array([-s, s, s])
        if kind == "static" or not mesh["clusters"]:
            g_inv = np.array(F.inv_rigid_scale(scene.global_matrix(model)))
            local = (g_inv[:3, :3] @ raw_global.T).T + g_inv[:3, 3]
            local = local * flip
            m = prefab.world(tr_id)
            verts = (m[:3, :3] @ local.T).T + m[:3, 3]
        else:
            bone_by_name = {prefab.name(b): b for b in bones if b in prefab.tr}
            acc = np.zeros_like(raw_global)
            wsum = np.zeros(len(raw_global))
            best = np.zeros(len(raw_global))
            lead = np.array([""] * len(raw_global), dtype=object)
            missing = []
            for bone_name, (idx, wts, link, tr_mesh) in mesh["clusters"].items():
                if not idx:
                    continue
                b = bone_by_name.get(bone_name)
                if b is None:
                    # a bone the prefab's renderer doesn't list by that name: look anywhere under the prefab
                    found = prefab.find(bone_name)
                    b = found[0] if found else None
                if b is None or link is None:
                    missing.append(bone_name)
                    continue
                idx = np.array(idx, int)
                wts = np.array(wts, float)
                pts = raw_global[idx]
                g_now_inv = np.array(F.inv_rigid_scale(scene.global_matrix(model)))
                if tr_mesh is not None:
                    # The file's cluster "Transform" is the mesh at bind time seen from the bone (link^-1 x mesh global),
                    # so a point of the mesh's own space goes straight into the bone's.
                    to_bone = np.array(tr_mesh) @ g_now_inv
                else:
                    to_bone = np.array(F.inv_rigid_scale(link))
                local = (to_bone[:3, :3] @ pts.T).T + to_bone[:3, 3]
                local = local * flip
                bw = prefab.world(b)
                acc[idx] += wts[:, None] * ((bw[:3, :3] @ local.T).T + bw[:3, 3])
                wsum[idx] += wts
                more = wts > best[idx]
                best[idx[more]] = wts[more]
                lead[idx[more]] = bone_name
            if missing and not quiet:
                print("  (%s: bones not in the prefab: %s)" % (name, missing[:6]))
            ok = wsum > 1e-6
            verts = np.zeros_like(raw_global)
            verts[ok] = acc[ok] / wsum[ok, None]
            if (~ok).any():
                # unweighted vertices: as a static mesh under the renderer's own transform
                g_inv = np.array(F.inv_rigid_scale(scene.global_matrix(model)))
                local = ((g_inv[:3, :3] @ raw_global[~ok].T).T + g_inv[:3, 3]) * flip
                m = prefab.world(tr_id)
                verts[~ok] = (m[:3, :3] @ local.T).T + m[:3, 3]
        parts.append(Part(tr_id, name, prefab.path_of(tr_id), kind, active, verts, tris, path,
                          lead if (kind != "static" and mesh["clusters"]) else None))
    return parts


def shells(verts, tris, weld=1e-5):
    """Connected pieces of a mesh, vertices at one place welded: an array of piece numbers per vertex, and the count."""
    key = np.round(verts / weld).astype(np.int64)
    _, first = np.unique(key, axis=0, return_inverse=True)
    first = first.reshape(-1)
    n = int(first.max()) + 1 if len(first) else 0
    parent = np.arange(n)

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    for a, b, c in first[tris]:
        ra, rb, rc = find(a), find(b), find(c)
        if ra != rb:
            parent[ra] = rb
        rb = find(b)
        if rc != rb:
            parent[rc] = rb
    roots = np.array([find(i) for i in range(n)])
    _, piece = np.unique(roots, return_inverse=True)
    return piece[first], int(piece.max()) + 1 if n else 0


if __name__ == "__main__":
    p = Prefab(os.path.join(REPO, sys.argv[1]))
    for part in load_parts(p):
        lo, hi = part.verts.min(0), part.verts.max(0)
        print("%-34s %-8s active=%d verts=%6d tris=%6d  x[%+.3f %+.3f] y[%+.3f %+.3f] z[%+.3f %+.3f]  %s" % (
            part.path.split("/", 1)[-1][-34:], part.kind, part.active, len(part.verts), len(part.tris), lo[0], hi[0], lo[1], hi[1], lo[2], hi[2],
            os.path.basename(part.source)))
