"""Gun mounts without Unity: reads the gun prefabs, the FBX meshes they use and the two tables
(Tables/Weapons/GunMounts.csv, GunParts.csv), seats every part by the game's own rule (mounts_model.py mirrors
GunMounts.cs) and says what came of it. Nothing of Unity is run; README.md beside this file says more.

  python gun_mounts.py check [gun ...]
        Logs/gun-mounts.txt and Logs/GunMounts/<gun>.png (+ <gun>.eye.<sight>.png): every part where the tables put it
  python gun_mounts.py measure <gun> ... [--pieces] [--points] [--write]
        measures a gun's sockets from its mesh and prints its rows beside the table's; --write puts the measured cells
        into GunMounts.csv (fits and starts are never touched); --pieces lists and draws the pieces that may be its
        iron sights (for hints.json); --points reads the prefab's mount bones as points instead of measuring
  python gun_mounts.py settle [gun ...] [--write]
        lets each measured sight rail's sights down onto the gun and says how far its row's y should go
  python gun_mounts.py prefabs [gun ...] [--write]
        puts the saved FPS Animation Pack prefabs' mounts and parts where the tables say
  python gun_mounts.py parts
        every part the prefabs carry: its measured foot beside its GunParts.csv row

Needs numpy and Pillow (Blender's bundled python has both: <Blender>/<version>/python/bin/python.exe)."""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import drop as D
import gunmesh as G
import gunshape as GS
import measure as ME
import mounts_model as M
import prefabs as PF
import raster as R
import tables as T

SIGHT_COLOURS = [(240, 170, 60), (90, 200, 240), (230, 110, 200), (150, 230, 120)]
PART_COLOURS = {"muzzle": (120, 140, 200), "grip": (120, 190, 150), "under": (200, 160, 110)}
# What the report marks for a person to look at (its own marks; the game decides nothing by them): a sight floating
# this far over the gun, one this deep in a measured rail, a window the gun's own body covers this much of, and a line
# of sight through the window that meets something solid this often.
FLOATS, SUNK, COVERED, SOLID = .002, -.004, .25, .25
# The view the report's fields are worked out for: the game's default setting on a wide screen.
HIP_FOV_ACROSS, ASPECT = 90.0, 16.0 / 9.0
OUT_DIR = os.path.join(G.REPO, "Logs", "GunMounts")
REPORT = os.path.join(G.REPO, "Logs", "gun-mounts.txt")


class Seated:
    """One gun with every part where the tables put it."""

    def __init__(self, name, mounts, parts):
        self.name = name
        self.g = g = GS.Gun(name)
        p = g.prefab
        self.v, self.t, _, self.bones = g.body_mesh()
        self.piece, self.n = G.shells(self.v, self.t)
        self.world = g.from_sight(self.v)                       # the body in the gun's own space
        self.rows = {k: mounts.get("%s.%s" % (name, k)) for k in T.SOCKETS}
        eye = self.rows["eye"]
        self.eye = np.array([T.number(eye, k) for k in "xyz"]) if eye else g.eye
        self.eye_axes = M.pose(eye)[1] if eye else g.frame
        # pieces parked far over the gun by a bone at rest are not its shape (a stripper clip waiting for its reload)
        self.junk = np.array([self.v[self.piece == k][:, 1].min() > ME.PARKED for k in range(self.n)]) if self.n else np.zeros(0, bool)
        sight = self.rows["sight"]
        self.folds = self._inside(T.boxes_of(sight.get("folds") if sight else ""))
        self.hides = self._inside(T.boxes_of(sight.get("hides") if sight else ""))
        self.body_draws, self.body_labels = g.body_draws()
        self.seats = {}
        # A gun with rows has the sockets its rows say; one with no rows at all wears what its prefab carries (GunMounts.Known).
        known = eye is not None
        sockets = g.sockets()
        kinds = {"sight": [s["part"] for s in g.sights if s["part"]]}
        for kind in ("muzzle", "grip", "under"):
            if kind in sockets:
                kinds[kind] = list(p.tr[sockets[kind]]["kids"])
        for kind, trs in kinds.items():
            row = self.rows.get(kind)
            for tr in trs:
                verts, tris, points = g.part_local(tr)
                draws, labels = g.part_draws(tr)
                pid = p.name(tr)
                data = parts.get(pid, {})
                back, front = T.number(data, "footBack"), T.number(data, "footFront")
                if row is None:
                    w = p.world(tr)                             # no row: as the prefab has it
                    origin, axes, t = w[:3, 3], w[:3, :3], 0.0
                else:
                    if kind == "sight":
                        t = M.seat_sight(row, self.eye, points.get("AimPoint", (np.zeros(3), None))[0], back, front)
                    elif kind == "under":
                        t = M.seat_front(row, front)
                    elif kind == "grip":
                        t = M.seat_grip(row, back, front)
                    else:
                        t = 0.0
                    origin, axes = M.place(row, t)
                self.seats[tr] = dict(id=pid, kind=kind, origin=origin, axes=axes, t=t, verts=verts, tris=tris, points=points, draws=draws, labels=labels,
                                      data=data, known=bool(data), offered=T.offers(row, pid) if row is not None else not known)

    def _inside(self, boxes):
        """Triangle mask: pieces of the body lying wholly inside one of the boxes (as GunSights cuts them)."""
        mask = np.zeros(self.n, bool)
        for lo, hi in boxes:
            ok = np.all((self.world >= lo) & (self.world <= hi), axis=1)
            for k in range(self.n):
                if ok[self.piece == k].all():
                    mask[k] = True
        return mask[self.piece[self.t[:, 0]]] if len(self.t) else np.zeros(0, bool)

    def at(self, seat, pts):
        return np.asarray(pts, float) @ seat["axes"].T + seat["origin"]

    def in_sight(self, pts):
        """Gun-root points in the frame of the gun's eye row."""
        return (np.asarray(pts, float) - self.eye) @ self.eye_axes

    def body_shown(self, optic):
        """Triangles of the body drawn with an optic on (iron sights folded) or with the gun's own sights."""
        gone = self.hides | self.junk[self.piece[self.t[:, 0]]]
        return ~(gone | self.folds) if optic else ~gone


def ray_hits(o, d, a, b, c, near):
    """Moller-Trumbore against many triangles at once: does the ray meet any beyond `near`?"""
    if len(a) == 0:
        return False
    e1, e2 = b - a, c - a
    h = np.cross(d, e2)
    det = np.einsum("ij,ij->i", e1, h)
    ok = np.abs(det) > 1e-12
    inv = np.zeros_like(det)
    inv[ok] = 1.0 / det[ok]
    sv = o - a
    u = np.einsum("ij,ij->i", sv, h) * inv
    q = np.cross(sv, e1)
    vv = (q @ d) * inv
    t = np.einsum("ij,ij->i", e2, q) * inv
    return bool(np.any(ok & (u >= 0) & (vv >= 0) & (u + vv <= 1) & (t > near)))


def check_sight(s, seat):
    """Numbers for one seated sight: its eye point from the gun's own, how it rests on the gun, how much of its window
    the gun's own body covers and whether the window's middle is clear."""
    out = {}
    aim = seat["points"].get("AimPoint")
    eye_w = s.at(seat, aim[0]) if aim else seat["origin"]
    out["eye"] = s.in_sight(eye_w)
    shown = s.body_shown(True)
    body = s.in_sight(s.world)
    pv = s.in_sight(s.at(seat, seat["verts"]))
    if len(pv) == 0:
        return out
    out["gap"], _ = D.gaps(body, s.t, shown, pv, seat["tris"])
    win = seat["points"].get("Reticle") or seat["points"].get("Lens")
    if win and aim:
        wpos, waxes = win
        scale = np.linalg.norm(waxes, axis=0)
        half_w, half_h = (scale[0] * .5, scale[1] * .5) if "Reticle" in seat["points"] else (scale[0], scale[1])
        tt = s.t[shown]
        a, b, c = body[tt[:, 0]], body[tt[:, 1]], body[tt[:, 2]]
        hits, total = 0, 0
        for fx in np.linspace(-.8, .8, 5):
            for fy in np.linspace(-.8, .8, 5):
                d = s.in_sight(s.at(seat, wpos + np.array([fx * half_w, fy * half_h, 0.0]))) - out["eye"]
                hit = ray_hits(out["eye"], d / np.linalg.norm(d), a, b, c, .02)
                total += 1
                hits += hit
                if abs(fx) < 1e-9 and abs(fy) < 1e-9:
                    out["middle"] = not hit
        out["covered"] = hits / total
        out["window_from_eye"] = float(np.linalg.norm(s.at(seat, wpos) - eye_w))
    return out


def sight_line(s, seat):
    """Rays from the sight's eye point through its window (its Reticle plate), against everything the gun shows with
    that sight on, each triangle with what its material draws as far as files say: the share of rays that meet
    something solid (and the first such thing's name), and the share that meet only what a texture's alpha may cut out.
    A file cannot say what a shader draws; what the player really sees is the editor's own check (GunSightBench)."""
    aim, window = seat["points"].get("AimPoint"), seat["points"].get("Reticle")
    if not aim or not window or len(seat["tris"]) == 0:
        return None
    eye = s.at(seat, aim[0])
    wpos, waxes = window
    half_w, half_h = np.linalg.norm(waxes[:, 0]) * .5, np.linalg.norm(waxes[:, 1]) * .5
    shown = s.body_shown(True)
    body_t = s.t[shown]
    pv = s.at(seat, seat["verts"])
    a = np.concatenate([s.world[body_t[:, 0]], pv[seat["tris"][:, 0]]])
    b = np.concatenate([s.world[body_t[:, 1]], pv[seat["tris"][:, 1]]])
    c = np.concatenate([s.world[body_t[:, 2]], pv[seat["tris"][:, 2]]])
    draws = np.concatenate([s.body_draws[shown], seat["draws"]])
    labels = np.concatenate([s.body_labels[shown], seat["labels"]])
    solid, cut = draws == "solid", draws == "cut-out"
    blocked, maybe, total, by = 0, 0, 0, {}
    for fx in np.linspace(-.8, .8, 9):
        for fy in np.linspace(-.8, .8, 9):
            d = s.at(seat, wpos + np.array([fx * half_w, fy * half_h, 0.0])) - eye
            d = d / np.linalg.norm(d)
            total += 1
            first = first_hit(eye, d, a[solid], b[solid], c[solid], .02)
            if first >= 0:
                blocked += 1
                name = labels[solid][first]
                by[name] = by.get(name, 0) + 1
            elif ray_hits(eye, d, a[cut], b[cut], c[cut], .02):
                maybe += 1
    return dict(solid=blocked / total, cutout=maybe / total, by=max(by, key=by.get) if by else None)


def first_hit(o, d, a, b, c, near):
    """The nearest triangle a ray meets beyond `near` (its index), or -1."""
    if len(a) == 0:
        return -1
    e1, e2 = b - a, c - a
    h = np.cross(d, e2)
    det = np.einsum("ij,ij->i", e1, h)
    ok = np.abs(det) > 1e-14
    inv = np.zeros_like(det)
    inv[ok] = 1.0 / det[ok]
    sv = o - a
    u = np.einsum("ij,ij->i", sv, h) * inv
    q = np.cross(sv, e1)
    vv = (q @ d) * inv
    t = np.einsum("ij,ij->i", e2, q) * inv
    hit = ok & (u >= 0) & (vv >= 0) & (u + vv <= 1) & (t > near)
    if not hit.any():
        return -1
    return int(np.argmin(np.where(hit, t, np.inf)))


def draw_side(s, path):
    """The gun from its right side, every offered part where the tables put it."""
    keep = s.body_shown(False)
    used = np.unique(s.t[keep])
    lo, hi = s.world[used].min(0), s.world[used].max(0)
    span = max(hi[2] - lo[2], .3) + .34
    w, h = 1500, 560
    canvas = R.Canvas(w, h)
    centre = (0, (lo[1] + hi[1]) / 2 + .01, (lo[2] + hi[2]) / 2 + .02)
    right, up, fwd = (0, 0, 1), (0, 1, 0), (-1, 0, 0)
    R.ortho(canvas, s.world, s.t[s.body_shown(True)], (165, 165, 172), right, up, fwd, centre, span)
    if s.folds.any():
        R.ortho(canvas, s.world + np.array([-.2, 0, 0]), s.t[s.folds & keep], (210, 70, 70), right, up, fwd, centre, span)
    colours, i = {}, 0
    for tr, seat in s.seats.items():
        if not seat["offered"]:
            continue
        if seat["kind"] == "sight":
            colour = SIGHT_COLOURS[i % len(SIGHT_COLOURS)]
            i += 1
        else:
            colour = PART_COLOURS[seat["kind"]]
        colours[tr] = colour
        if len(seat["tris"]):
            R.ortho(canvas, s.at(seat, seat["verts"]), seat["tris"], colour, right, up, fwd, centre, span)
    scale = w / span

    def px(pt):
        return (pt[2] - centre[2]) * scale + w / 2, h / 2 - (pt[1] - centre[1]) * scale
    x, y = px(s.eye)
    R.hline(canvas, y, (60, 220, 90), x, w)
    R.dot(canvas, x, y, (60, 255, 90), 4)
    for kind in ("sight", "grip", "under"):
        row = s.rows.get(kind)
        if row:
            o, axes = M.pose(row)
            x0, y0 = px(o)
            x1, y1 = px(o + axes[:, 2] * M.span_of(row))
            n = int(max(abs(x1 - x0), 1))
            for k in range(n + 1):
                R.dot(canvas, x0 + (x1 - x0) * k / n, y0 + (y1 - y0) * k / n, (255, 60, 60), 0)
            R.dot(canvas, x0, y0, (255, 60, 60), 3)
            R.dot(canvas, x1, y1, (255, 60, 60), 3)
    for tr, seat in s.seats.items():
        aim = seat["points"].get("AimPoint")
        if aim and tr in colours and seat["kind"] == "sight":
            x, y = px(s.at(seat, aim[0]))
            R.dot(canvas, x, y, colours[tr], 4)
    for k in range(-6, 12):
        xk, _ = px((0, 0, k * .1))
        R.vline(canvas, xk, (230, 230, 230) if k == 0 else (150, 150, 150), h - 14, h)
    img = canvas.image()
    from PIL import ImageDraw
    ImageDraw.Draw(img).text((8, 6), s.name + "   green: the gun's own eye place and sight line; red: its rails and (set aside) the iron sights "
                             "an optic folds; squares: each sight's eye point; ticks: 10 cm", fill=(255, 255, 255))
    img.save(path)


def draw_eye(s, tr, path, fov, size=(640, 360)):
    """What the eye sees through one sight: from its eye point along its line, the gun as it is with that sight on. The
    window itself is drawn solid (no glass here): what matters is where it is and what stands before it. A scope's
    housing fills the picture: its lens is the game's own camera's, which the report's numbers describe."""
    seat = s.seats[tr]
    aim = seat["points"].get("AimPoint")
    eye = s.at(seat, aim[0]) if aim else s.eye
    fwd = seat["axes"] @ (aim[1][:, 2] / np.linalg.norm(aim[1][:, 2])) if aim else s.eye_axes[:, 2]
    canvas = R.Canvas(size[0], size[1], (140, 170, 200))
    R.perspective(canvas, s.world, s.t[s.body_shown(True)], (150, 150, 158), eye, fwd, seat["axes"][:, 1], fov)
    for other, o in s.seats.items():
        if (o["kind"] == "sight" and other != tr) or len(o["tris"]) == 0 or not o["offered"]:
            continue
        if o["kind"] in ("muzzle", "grip"):
            continue                                            # nothing a gun starts with
        R.perspective(canvas, s.at(o, o["verts"]), o["tris"], (235, 170, 70) if other == tr else (120, 150, 190), eye, fwd, seat["axes"][:, 1], fov)
    cx, cy = size[0] // 2, size[1] // 2
    R.hline(canvas, cy, (255, 40, 40), cx - 6, cx + 7)
    R.vline(canvas, cx, (255, 40, 40), cy - 6, cy + 7)
    canvas.image().save(path)


def check(guns):
    mounts, parts = T.table(T.MOUNTS), T.table(T.PARTS)
    os.makedirs(OUT_DIR, exist_ok=True)
    hip_v = 2 * math.degrees(math.atan(math.tan(math.radians(HIP_FOV_ACROSS) / 2) / ASPECT))
    lines = [
        "gun mounts, checked without Unity (Tools/Guns/gun_mounts.py check): every gun's parts seated by the game's rule from",
        "Tables/Weapons/GunMounts.csv and GunParts.csv, on the meshes the prefabs use.",
        "  along      how far along its rail the part sits (cm from the rail's rear end)",
        "  eye        the sight's eye point from the gun's own eye place, cm: right, up, ahead (ahead > 0: the gun comes that",
        "             much nearer the eye when aimed through it, because its rail does not reach further back)",
        "  rests      the sight let down onto the gun where it sits: the least gap between its underside and the gun's top,",
        "             mm, small bumps aside (0 touches; over 0 floats; under 0 is that deep in the gun: a sight's jaws go",
        "             round a rail, and of two sights on one rail the shallower-footed one sits a little in)",
        "  window     how far the sight's window is from the eye, cm, and how much of it the gun's own body covers as the eye",
        "             sees it, iron sights folded (a scope's lens is drawn by its own camera, which leaves the gun out)",
        "  sight line rays from the eye through a dot's or a holograph's window against the gun and the sight itself, each",
        "             triangle with what its material draws as far as its file says (solid, cut out by a texture, see-through).",
        "             Files cannot say what a shader draws: what the player really sees is the editor's own check, drawn by the",
        "             game's shaders (ENFILADE > 총 > 조준경 점검, Logs/gun-sights.txt).",
        "  '!' marks what a person should look at in the picture beside it (Logs/GunMounts/<gun>.png, .eye.<sight>.png).",
        "The view: hip field %.1f deg (a %g deg setting across a %g:1 screen)." % (hip_v, HIP_FOV_ACROSS, ASPECT),
        "",
    ]
    marks, unread = 0, 0
    names = [n for n in GS.prefab_paths() if not guns or n in guns]
    for name in names:
        try:
            s = Seated(name, mounts, parts)
        except Exception as error:                              # a model this reader can't place (README: what it can't read)
            lines.append("== %s: not read (%s)" % (name, error))
            unread += 1
            continue
        row = s.rows["sight"]
        head = "== %s" % name
        if row:
            rail = s.in_sight(M.pose(row)[0])
            head += "  sight rail from %.1f cm ahead of the eye, %.1f cm long, %.1f mm under the gun's own line; %d triangles fold under an optic, %d never drawn" % (
                rail[2] * 100, M.span_of(row) * 100, -rail[1] * 1000, int((s.folds & ~s.hides).sum()), int(s.hides.sum()))
        lines.append(head)
        for tr, seat in s.seats.items():
            if not seat["known"]:
                marks += 1
                lines.append("!  %-26s has no row in GunParts.csv (it sits with no foot and no name)" % seat["id"])
            if not seat["offered"]:
                lines.append("   %-26s not offered (%s)" % (seat["id"], "its row's fits leaves it out" if s.rows.get(seat["kind"])
                                                            else "the gun's rows give it no %s socket" % seat["kind"]))
                continue
            if seat["kind"] != "sight":
                if s.rows.get(seat["kind"]):
                    lines.append("   %-26s %-6s along %.1f cm" % (seat["id"], seat["kind"], seat["t"] * 100))
                continue
            c = check_sight(s, seat)
            data = seat["data"]
            mag = max(1.0, T.number(data, "magnification", 1.0))
            view = (data.get("view") or "").strip().lower()
            zooms, pip = view == "zoom" and mag > 1.001, view == "pip" and mag > 1.001
            fov = M.aimed_fov(mag, zooms, hip_v)
            e = c["eye"]
            bad = []
            if c.get("gap") is not None and c["gap"] > FLOATS:
                bad.append("floats")
            # KINEMATION seats its own parts 4-10 mm into its own rails: a point it put there is its to say
            if row and M.span_of(row) > 0 and c.get("gap") is not None and c["gap"] < SUNK:
                bad.append("sunk")
            if not (zooms or pip) and c.get("covered", 0) > COVERED:
                bad.append("covered")
            if not (zooms or pip) and c.get("middle") is False:
                bad.append("middle")
            # What stands in the line of sight through a window that is looked through as it is (a dot, a holograph).
            line = sight_line(s, seat) if not (zooms or pip) else None
            if line and line["solid"] > SOLID:
                bad.append("sight line blocked")
            marks += len(bad)
            text = "%s  %-26s along %.1f cm; eye (%+.1f, %+.1f, %+.1f) cm; rests %s mm" % (
                "!" if bad else " ", seat["id"], seat["t"] * 100, e[0] * 100, e[1] * 100, e[2] * 100,
                "?" if c.get("gap") is None else "%+.1f" % (c["gap"] * 1000))
            if "covered" in c:
                text += "; window %.1f cm from the eye, %d%% covered, middle %s" % (
                    c["window_from_eye"] * 100, round(c["covered"] * 100), "clear" if c.get("middle") else "BLOCKED")
            if line and line["solid"] > 0:
                text += "; sight line: %d%% of it meets something solid, first %s" % (round(line["solid"] * 100), line["by"])
            elif line:
                text += "; sight line: nothing solid in it" + (" (%d%% through a material a texture cuts out)" % round(line["cutout"] * 100) if line["cutout"] > 0 else "")
            if zooms:
                # GunScope: the picture's edge as the eye sees it, the camera that takes it, and a thing at the picture's
                # edge in the lens against the same thing beside the scope
                window = min(max(T.number(data, "window", .9), .2), 2.0)
                edge = math.radians(min(max(window * fov / 2, 1.0), 30.0))
                text += "; x%g zoom: the whole view %.1f deg, the lens picture %.0f%% of the screen's height through a %.1f deg camera, lens / world magnification 1.000 at the middle, %.3f at its edge" % (
                    mag, fov, window * 100, 2 * math.degrees(math.atan(math.sin(edge))), edge / math.atan(math.sin(edge)))
            elif pip:
                text += "; x%g pip: the view stays as the gun has it and the lens alone magnifies (two magnifications, by its row's choice)" % mag
            if bad:
                text += "   <- " + ", ".join(bad)
            lines.append(text)
            draw_eye(s, tr, os.path.join(OUT_DIR, "%s.eye.%s.png" % (name, seat["id"])), fov)
        draw_side(s, os.path.join(OUT_DIR, name + ".png"))
        print("checked", name)
    lines.append("")
    lines.append("(%d guns, %d not read, %d marks)" % (len(names), unread, marks))
    open(REPORT, "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("\n".join(lines))
    return marks


def measure(guns, pieces, points, write):
    mounts = T.table(T.MOUNTS)
    os.makedirs(OUT_DIR, exist_ok=True)
    put = {}
    for name in guns:
        if pieces:
            print("\n".join(ME.pieces(name, os.path.join(OUT_DIR, name + ".pieces.png"))))
            continue
        rows, notes, m = ME.rows_of(name, True if points else None)
        print("== %s (%s)" % (name, "measured from its shape" if m is not None else "its mount bones, as points"))
        if m is not None:
            print("\n".join(ME.describe(name, m)))
        for key, cells in rows.items():
            old = mounts.get(key)
            differs = [c for c in cells if old is None or (old.get(c) or "") != cells[c]]
            print("   %-18s %s" % (key, "  ".join("%s=%s" % (c, cells[c]) for c in T.MEASURED if c in cells and c not in ("folds", "hides"))))
            if old is None:
                print("   %-18s (no row in the table yet: fits and starts are yours to write)" % "")
            elif differs:
                print("   %-18s the table has: %s" % ("", "  ".join("%s=%s" % (c, old.get(c) or "") for c in differs)))
            if differs:
                put[key] = {c: cells[c] for c in differs}
        for note in notes:
            print("   ! " + note)
    if pieces:
        return
    if not put:
        print("the table already has what was measured")
    elif write:
        added = T.put(T.MOUNTS, put)
        print("%d rows written (%d new). Next: settle --write, check, prefabs --write" % (len(put), added))
    else:
        print("%d rows differ; nothing written without --write (a settled rail's y is lower than its measured top: see settle)" % len(put))


def settle(guns, write):
    """Each measured sight rail's height: the sights the rail offers are let down onto the gun where the rule seats them
    (drop.rest), and the rail's y becomes the height the deepest of them rests at. A rail measured as the top of the
    gun's middle line stands its sights on that crest; a sight's foot is a channel with a jaw each side and rests lower,
    round a rib or a narrow rail (KINEMATION seats its own sights 4-5 mm into its own rails the same way). Rows without
    a span (points someone put there) are left alone."""
    mounts, parts = T.table(T.MOUNTS), T.table(T.PARTS)
    changes = {}
    for name in GS.prefab_paths():
        row = mounts.get(name + ".sight")
        if (guns and name not in guns) or not row or M.span_of(row) <= 0:
            continue
        if any(abs(T.number(row, k)) > 1e-6 for k in ("rx", "ry", "rz")):
            print("%-9s its sight rail is turned: its height is not the gun's y, set it by hand" % name)
            continue
        s = Seated(name, mounts, parts)
        body, shown = s.in_sight(s.world), s.body_shown(True)
        level = s.in_sight(M.pose(row)[0])[1]
        rests = []
        for seat in s.seats.values():
            if seat["kind"] != "sight" or not seat["offered"] or len(seat["verts"]) == 0:
                continue
            pv = s.in_sight(s.at(seat, seat["verts"])) - np.array([0, level, 0])
            r = D.rest(body, s.t, shown, pv, seat["tris"])
            if r is not None:
                rests.append((r - level, seat["id"]))
        if not rests:
            continue
        down = min(r for r, _ in rests)
        print("%-9s rests: %s -> the rail goes %+.1f mm" % (name, ", ".join("%s %+.1f" % (i, r * 1000) for r, i in rests), down * 1000))
        if abs(down) >= .0002:
            changes[name + ".sight"] = {"y": T.cell_of(T.number(row, "y") + down)}
    if not changes:
        print("every measured rail is settled")
    elif write:
        T.put(T.MOUNTS, changes)
    else:
        print("%d rails would move; nothing written without --write" % len(changes))


def part_feet():
    parts = T.table(T.PARTS)
    seen = {}
    for name in GS.prefab_paths():
        try:
            g = GS.Gun(name)
        except Exception:
            continue
        p = g.prefab
        for kind, tr in g.sockets().items():
            for kid in p.tr[tr]["kids"]:
                pid = p.name(kid)
                verts, _, points = g.part_local(kid)
                if pid in seen or len(verts) == 0:
                    seen.setdefault(pid, None)
                    continue
                seen[pid] = (kind, ME.foot_of(verts, kind in ("grip", "under")), points.get("AimPoint"), name)
    print("every part the gun prefabs carry: its foot measured from its mesh (a sight's lowest 4 mm; a hanging part's top),")
    print("m behind / ahead of its origin, beside its GunParts.csv row. A grip's and a light's feet are a person's reading")
    print("of their shape (a light's clamp, a grip's whole top), so those rows may rightly differ.")
    for pid, found in seen.items():
        if found is None:
            continue
        kind, (back, front), aim, on = found
        row = parts.get(pid)
        text = "%-26s %-6s measured %.4f / %.4f" % (pid, kind, back, front)
        text += "   table %s / %s" % (row.get("footBack") or "0", row.get("footFront") or "0") if row else "   NO ROW in GunParts.csv"
        if aim is not None:
            text += "   eye point %.1f cm behind its origin, %.1f cm over it" % (-aim[0][2] * 100, aim[0][1] * 100)
        print(text + "   (on %s)" % on)


def main():
    args = [a for a in sys.argv[2:] if not a.startswith("--")]
    flags = set(a for a in sys.argv[2:] if a.startswith("--"))
    command = sys.argv[1] if len(sys.argv) > 1 else ""
    if command == "check":
        check(args)
    elif command == "measure" and args:
        measure(args, "--pieces" in flags, "--points" in flags, "--write" in flags)
    elif command == "settle":
        settle(args, "--write" in flags)
    elif command == "prefabs":
        PF.fit(args, "--write" in flags)
    elif command == "parts":
        part_feet()
    else:
        print(__doc__)


if __name__ == "__main__":
    main()
