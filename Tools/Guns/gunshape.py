"""A gun's shape for measuring its mounts: its own body (no attachments) in the gun's sight frame (origin at the gun's
own eye place, z along the sight line, y up, x right; metres), its pieces (connected shells), which pieces are its
iron sights, and height fields of its top and underside."""
import glob
import os
import re
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gunmesh as G

FOLDERS = ["Assets/_Project/Data/Guns/FPSPackGuns", "Assets/_Project/Data/Guns/TacticalGuns"]
# Mount names the two builders use (ours on FPS Animation Pack guns, KINEMATION's bones on Tactical Shooter Pack guns).
MOUNT_NAMES = {"sight": ("SightMount", "ScopeMount"), "muzzle": ("MuzzleMount",), "grip": ("GripMount",),
               "under": ("UnderMount", "UnderbarrelMount")}
ALL_MOUNTS = tuple(n for names in MOUNT_NAMES.values() for n in names)
# The children of a part that say where things are on it: a sight's eye point and window, a scope's lens and camera.
PART_POINTS = ("AimPoint", "Reticle", "Lens", "CapturePoint")


def prefab_paths():
    out = {}
    for folder in FOLDERS:
        for p in sorted(glob.glob(os.path.join(G.REPO, folder, "*.prefab"))):
            out[os.path.basename(p)[:-7]] = p
    return out


class Gun:
    def __init__(self, name, path=None):
        self.name = name
        self.path = path or prefab_paths()[name]
        self.prefab = p = G.Prefab(self.path)
        self.pack = "fps" if "FPSPackGuns" in self.path.replace("\\", "/") else "tactical"
        self.parts = G.load_parts(p, quiet=True)
        # GunSights: the list of (part transform or None, aim transform, iron flag, scope flag) and iron objects.
        self.sights, self.iron_objects, self.cas_aim, self.initial = [], [], None, 0
        for fid, body in p.monos():
            m = re.search(r"^  aimPoint: \{fileID: (-?\d+)\}", body, flags=re.M)
            if m and "weaponSettings" in body and m.group(1) in p.tr:
                self.cas_aim = m.group(1)
            if "8b0b3ede67b7caf4d9a8aae1b5f45853" in body:   # GunSights
                for sm in re.finditer(r"- part: \{fileID: (-?\d+)\}\s+aimPoint: \{fileID: (-?\d+)\}\s+ironSight: (\d)\s+scope: \{fileID: (-?\d+)\}", body):
                    part, aim, flag, scope = sm.groups()
                    self.sights.append(dict(part=p.tr_of_go.get(part), aim=aim if aim in p.tr else None, iron=flag == "1", scope=scope != "0"))
                im = re.search(r"ironSights:\s*\n((?:\s*- .*\n)*)", body)
                if im:
                    self.iron_objects = [p.tr_of_go[g] for g in re.findall(r"fileID: (-?\d+)", im.group(1)) if g in p.tr_of_go]
                m2 = re.search(r"^  initial: (\d+)", body, flags=re.M)
                self.initial = int(m2.group(1)) if m2 else 0
        # The gun's own aim point: the iron entry of its sights, else the model's own AimPoint child, else the CAS one.
        own = [s["aim"] for s in self.sights if s["part"] is None and s["aim"]]
        named = [t for t in p.find("AimPoint") if not self._under_any_part(t)]
        self.eye_tr = own[0] if own else (named[0] if named else self.cas_aim)
        m = p.world(self.eye_tr)
        self.eye = m[:3, 3].copy()
        f, u = m[:3, 2] / np.linalg.norm(m[:3, 2]), m[:3, 1] / np.linalg.norm(m[:3, 1])
        r = np.cross(u, f)
        r /= np.linalg.norm(r)
        u = np.cross(f, r)
        self.frame = np.stack([r, u, f], axis=1)           # columns: right, up, forward in gun-root space
        self.mounts = {}
        for kind, names in MOUNT_NAMES.items():
            for n in names:
                found = p.find(n)
                if found:
                    self.mounts[kind] = found[0]
        self.body = [part for part in self.parts if not self._attachment(part.tr_id)]

    def _under_any_part(self, tr):
        p = self.prefab
        roots = [s["part"] for s in self.sights if s["part"]]
        t = tr
        while t in p.tr:
            if t in roots or p.name(t) in ALL_MOUNTS:
                return True
            t = p.tr[t]["father"]
        return False

    def _attachment(self, tr):
        """Under a mount, a sight part or one of the pack's iron sight objects: not the gun's own body."""
        p = self.prefab
        t = tr
        while t in p.tr:
            if p.name(t) in ALL_MOUNTS or t in self.iron_objects or any(s["part"] == t for s in self.sights):
                return True
            t = p.tr[t]["father"]
        return False

    def sockets(self):
        """Socket -> the transform its parts hang on: the mounts by name, and for the sights the parent of the first
        sight part when the model names that bone otherwise."""
        out = dict(self.mounts)
        if "sight" not in out:
            for s in self.sights:
                if s["part"]:
                    out["sight"] = self.prefab.tr[s["part"]]["father"]
                    break
        return out

    def part_local(self, root_tr):
        """A part's own mesh in its own frame (vertices, triangles) and its named points there: name -> (position, axes)."""
        p = self.prefab
        inv = np.linalg.inv(p.world(root_tr))
        verts, tris, base = [], [], 0
        for part in self.parts:
            if p.is_under(part.tr_id, root_tr):
                verts.append((inv[:3, :3] @ part.verts.T).T + inv[:3, 3])
                tris.append(part.tris + base)
                base += len(part.verts)
        points = {}
        for t in p.tr:
            if t != root_tr and p.is_under(t, root_tr):
                n = p.name(t)
                if n in PART_POINTS and n not in points:
                    m = inv @ p.world(t)
                    points[n] = (m[:3, 3].copy(), m[:3, :3].copy())
        if not verts:
            return np.zeros((0, 3)), np.zeros((0, 3), int), points
        return np.concatenate(verts), np.concatenate(tris), points

    def part_draws(self, root_tr):
        """What draws each triangle of a part, and who (the triangles in part_local's order)."""
        p = self.prefab
        own = [part for part in self.parts if p.is_under(part.tr_id, root_tr)]
        if not own:
            return np.zeros(0, object), np.zeros(0, object)
        return np.concatenate([part.draws() for part in own]), np.concatenate([part.labels() for part in own])

    def body_draws(self, only_active=True):
        """The same for the gun's own body (the triangles in body_mesh's order)."""
        own = [part for part in self.body if part.active or not only_active]
        if not own:
            return np.zeros(0, object), np.zeros(0, object)
        return np.concatenate([part.draws() for part in own]), np.concatenate([part.labels() for part in own])

    def to_sight(self, pts):
        """Gun-root points into the sight frame."""
        return (np.asarray(pts, float) - self.eye) @ self.frame

    def from_sight(self, pts):
        return np.asarray(pts, float) @ self.frame.T + self.eye

    def body_mesh(self, only_active=True):
        """The body as one mesh in the sight frame: verts, tris, per-vertex (part index, bone name)."""
        verts, tris, owner, bones = [], [], [], []
        base = 0
        for i, part in enumerate(self.body):
            if only_active and not part.active:
                continue
            verts.append(self.to_sight(part.verts))
            tris.append(part.tris + base)
            owner.append(np.full(len(part.verts), i))
            bones.append(part.bone if part.bone is not None else np.array([""] * len(part.verts), dtype=object))
            base += len(part.verts)
        if not verts:
            return np.zeros((0, 3)), np.zeros((0, 3), int), np.zeros(0, int), np.zeros(0, object)
        return np.concatenate(verts), np.concatenate(tris), np.concatenate(owner), np.concatenate(bones)


def height_field(verts, tris, x_half, z0, z1, cell=.002, top=True, tri_mask=None):
    """The highest (top) or lowest y of the surface over each cell of an x-z grid: rows z, columns x. NaN where none.
    Triangles are sampled densely enough for the cell (their corners and inner points)."""
    nx, nz = int(round(2 * x_half / cell)) + 1, int(round((z1 - z0) / cell)) + 1
    field = np.full((nz, nx), -np.inf if top else np.inf)
    t = tris if tri_mask is None else tris[tri_mask]
    if len(t) == 0:
        return np.full((nz, nx), np.nan), z0, cell
    a, b, c = verts[t[:, 0]], verts[t[:, 1]], verts[t[:, 2]]
    # how finely each triangle is sampled: its longest edge over the cell
    edge = np.maximum(np.maximum(np.linalg.norm(b - a, axis=1), np.linalg.norm(c - b, axis=1)), np.linalg.norm(a - c, axis=1))
    steps = np.clip(np.ceil(edge / (cell * .7)).astype(int), 1, 400)
    for n in np.unique(steps):
        sel = steps == n
        u, v = np.meshgrid(np.arange(n + 1) / n, np.arange(n + 1) / n)
        keep = (u + v) <= 1.0 + 1e-9
        u, v = u[keep], v[keep]
        pts = (a[sel][:, None, :] * (1 - u - v)[None, :, None] + b[sel][:, None, :] * u[None, :, None] + c[sel][:, None, :] * v[None, :, None]).reshape(-1, 3)
        ix = np.round((pts[:, 0] + x_half) / cell).astype(int)
        iz = np.round((pts[:, 2] - z0) / cell).astype(int)
        ok = (ix >= 0) & (ix < nx) & (iz >= 0) & (iz < nz)
        ix, iz, y = ix[ok], iz[ok], pts[ok, 1]
        if top:
            np.maximum.at(field, (iz, ix), y)
        else:
            np.minimum.at(field, (iz, ix), y)
    field[~np.isfinite(field)] = np.nan
    return field, z0, cell


def profile(field, cols=None, top=True):
    """One value per z row: the highest (lowest) of the row's cells (or of the given columns)."""
    f = field if cols is None else field[:, cols]
    with np.errstate(all="ignore"):
        return np.nanmax(f, axis=1) if top else np.nanmin(f, axis=1)


if __name__ == "__main__":
    for name in sys.argv[1:]:
        g = Gun(name)
        v, t, owner, bones = g.body_mesh()
        piece, n = G.shells(v, t)
        print("== %s: eye %s  body verts %d tris %d pieces %d  bones %s" % (name, np.round(g.eye, 3), len(v), len(t), n, sorted(set(bones))))
        lo, hi = v.min(0), v.max(0)
        print("   body in the sight frame: x[%+.3f %+.3f] y[%+.3f %+.3f] z[%+.3f %+.3f]" % (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2]))
        rows = []
        for k in range(n):
            pv = v[piece == k]
            plo, phi = pv.min(0), pv.max(0)
            ahead = pv[pv[:, 2] > .03]
            reach = np.min(np.hypot(ahead[:, 0], ahead[:, 1])) if len(ahead) else 9
            b = bones[piece == k]
            names, counts = np.unique(b.astype(str), return_counts=True)
            rows.append((phi[1], k, len(pv), plo, phi, reach, names[np.argmax(counts)]))
        rows.sort(key=lambda r: -r[0])
        for top, k, count, plo, phi, reach, bone in rows[:28]:
            print("   piece %3d verts %5d  x[%+.3f %+.3f] y[%+.3f %+.3f] z[%+.3f %+.3f]  nearest the line %.1f mm  bone %s" % (
                k, count, plo[0], phi[0], plo[1], phi[1], plo[2], phi[2], reach * 1000, bone))
