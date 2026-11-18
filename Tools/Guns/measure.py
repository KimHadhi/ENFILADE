"""A gun's sockets measured from its own shape, as rows for Tables/Weapons/GunMounts.csv (the cells a shape can say:
x y z rx ry rz span folds hides; which parts a socket offers and starts with is a person's choice and stays theirs).
Everything in the gun prefab's root space (metres).
  eye     the gun's own aim point as the prefab has it (where the eye is when it is aimed with its own sights)
  sight   the top rail: the flat stretch of the gun's top, iron sights left out, about where a sight's window wants to
          be ahead of the eye; its folds are the boxes of the gun's own iron sights, its hides the boxes of glass the
          pack draws as solid metal
  muzzle  the muzzle mount as the prefab has it (GunMuzzle measured the barrel's front face when the gun was made)
  grip    the underside of the handguard between the two ends hints.json gives, on the line its lowest points lie on
  under   the same, for a light (a pistol's dust cover)
A gun whose model carries KINEMATION's own mount bones (Tactical Shooter Pack) is not measured: each bone is its socket,
a point (span 0).
What only a person can say after looking is in hints.json beside this file: which pieces of the mesh are the iron
sights, which are glass drawn solid, where a rail really ends, which stretch of the underside takes parts."""
import json
import os
import re

import numpy as np

import gunmesh as G
import gunshape as GS
import mounts_model as M
import raster as R
import tables as T

HINTS_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "hints.json")

# The measuring rule's own numbers (m).
FLAT = .0025          # a rail's top stays within this of its level
CLOSE = .012          # slots shorter than this along a rail are bridged (a Picatinny slot is 5.2 mm)
BAND = .006           # half the width of the middle band the profiles are taken over
PREFER = .15          # where a sight's window wants to be ahead of the eye (the packs' sights' own eye relief)
LONG_ENOUGH = .05     # a stretch shorter than this is no rail
GROW_TOUCH = .0015    # pieces nearer than this to an iron sight piece belong to it when they stand above the rail
PARKED = .03          # a piece wholly this far over the sight line is parked there by a bone at rest (a stripper clip)
NEAR_LINE = .016      # pieces this near the sight line ahead of the eye are listed as what may be a sight
LEVEL_UNDER = 1.5     # an underside that slopes less than this many degrees is taken as level
# Bones that carry rounds and magazines: no part of the gun's underside.
MAGAZINE_BONES = re.compile(r"mag|ammo|bullet|cartridge|shell|gauge|round|clip|belt|box|9mm|drum|loader", re.I)


def hints():
    try:
        return json.load(open(HINTS_FILE, encoding="utf-8")).get("guns", {})
    except (OSError, ValueError):
        return {}


class Measured:
    pass


def flat_stretch(top, z0, cell, around, level=None):
    """The stretch of the profile about `around` that stays within FLAT of its level: (from, to, level) in z."""
    n = len(top)
    i = min(max(int(round((around - z0) / cell)), 0), n - 1)
    if not np.isfinite(top[i]):
        return None
    if level is None:
        level = float(np.nanmedian(top[max(i - 5, 0):min(i + 6, n)]))
    a = i
    while a > 0 and np.isfinite(top[a - 1]) and abs(top[a - 1] - level) <= FLAT:
        a -= 1
    b = i
    while b < n - 1 and np.isfinite(top[b + 1]) and abs(top[b + 1] - level) <= FLAT:
        b += 1
    return z0 + a * cell, z0 + b * cell, float(np.nanmedian(top[a:b + 1]))


def closed(top, cell):
    """The profile with dips shorter than CLOSE bridged at the height of their sides (a running maximum, then minimum)."""
    k = max(1, int(round(CLOSE / cell / 2)))
    pad = np.pad(top, k, mode="edge")
    with np.errstate(all="ignore"):
        up = np.array([np.nanmax(pad[i:i + 2 * k + 1]) if np.isfinite(pad[i:i + 2 * k + 1]).any() else np.nan for i in range(len(top))])
        pad2 = np.pad(up, k, mode="edge")
        return np.array([np.nanmin(pad2[i:i + 2 * k + 1]) if np.isfinite(pad2[i:i + 2 * k + 1]).any() else np.nan for i in range(len(top))])


def merge(boxes):
    """Boxes that overlap or touch become one."""
    boxes = [(lo.copy(), hi.copy()) for lo, hi in boxes]
    changed = True
    while changed:
        changed = False
        for i in range(len(boxes)):
            for j in range(i + 1, len(boxes)):
                a, b = boxes[i], boxes[j]
                if np.all(a[0] <= b[1] + .001) and np.all(b[0] <= a[1] + .001):
                    boxes[i] = (np.minimum(a[0], b[0]), np.maximum(a[1], b[1]))
                    del boxes[j]
                    changed = True
                    break
            if changed:
                break
    return boxes


def measure(name, hint=None):
    """The gun's body in its sight frame (origin at its own eye place, z along its sight line, y up), its pieces, its
    top rail, the boxes of its iron sights and its underside's profile."""
    g = GS.Gun(name)
    hint = hints().get(name, {}) if hint is None else hint
    v, t, _, bones = g.body_mesh()
    piece, n = G.shells(v, t)
    m = Measured()
    m.gun, m.v, m.t, m.piece, m.n, m.bones, m.hint = g, v, t, piece, n, bones, hint
    m.junk = [k for k in range(n) if v[piece == k][:, 1].min() > PARKED]
    irons = list(hint.get("irons", []))
    hides = list(hint.get("hides", []))
    tri_piece = piece[t[:, 0]]
    z0, z1, cell = -.12, float(v[:, 2].max()) + .01, .002
    m.z0, m.cell = z0, cell

    def top_profile(leave):
        field, _, _ = GS.height_field(v, t, BAND, z0, z1, cell, True, ~np.isin(tri_piece, leave))
        with np.errstate(all="ignore"):
            return closed(GS.profile(field), cell)

    # The rail: the flat stretch of the top, iron sights left out, about where a sight's window wants to be; when the
    # top there is no rail (too short), the nearest stretch ahead that is.
    top = top_profile(irons + m.junk + hides)
    if "rail" in hint:
        a, b = hint["rail"][0] / 100.0, hint["rail"][1] / 100.0
        ia, ib = int(round((a - z0) / cell)), int(round((b - z0) / cell))
        rail = (a, b, float(np.nanmax(top[ia:ib + 1])))
    else:
        rail = None
        for around in np.arange(PREFER, PREFER + .30, .01):
            s = flat_stretch(top, z0, cell, around)
            if s and s[1] - s[0] >= LONG_ENOUGH:
                # the highest stretch among those that begin within reach: a body top under a raised rail is no rail
                if rail is None or s[2] > rail[2] + FLAT:
                    rail = s
                if rail is not None and around > rail[1] + .10:
                    break
    # Iron sight pieces grow by what touches them and stands above the rail (a carry handle's clamps, a sight's base).
    if rail is not None and irons and hint.get("grow", True):
        level = rail[2]
        tops = np.array([v[piece == k][:, 1].max() for k in range(n)])
        los = np.array([v[piece == k].min(0) for k in range(n)])
        his = np.array([v[piece == k].max(0) for k in range(n)])
        length = his[:, 2] - los[:, 2]
        changed = True
        while changed:
            changed = False
            for k in range(n):
                if k in irons or k in m.junk or k in hides or tops[k] <= level + .001 or length[k] > .12:
                    continue
                # a piece that reaches far below the rail is the gun's body, however it touches a sight
                if los[k][1] < level - .02:
                    continue
                for i in irons:
                    if np.all(los[k] <= his[i] + GROW_TOUCH) and np.all(his[k] >= los[i] - GROW_TOUCH):
                        irons.append(k)
                        changed = True
                        break
        top = top_profile(irons + m.junk + hides)
        if "rail" not in hint:
            rail = flat_stretch(top, z0, cell, (rail[0] + rail[1]) / 2, rail[2]) or rail
    m.top, m.irons, m.hides, m.rail = top, irons, hides, rail

    def boxes(pieces):
        return merge([(v[piece == k].min(0) - .0006, v[piece == k].max(0) + .0006) for k in pieces])
    m.boxes, m.hide_boxes = boxes(irons), boxes(hides)
    # The underside over the gun's length: every piece but magazines, rounds and what is parked.
    loose = []
    for k in range(n):
        names, counts = np.unique(bones[piece == k].astype(str), return_counts=True)
        if MAGAZINE_BONES.search(names[np.argmax(counts)] or ""):
            loose.append(k)
    field, _, _ = GS.height_field(v, t, BAND, z0, z1, cell, False, ~np.isin(tri_piece, loose + m.junk))
    with np.errstate(all="ignore"):
        m.under = GS.profile(field, top=False)
    return m


def under_line(m, a, b):
    """The straight line the underside's lowest points lie on between a and b (sight frame): (z0, y0, z1, y1). A rail's
    teeth and a handguard's ribs stand above it."""
    ia, ib = int(round((a - m.z0) / m.cell)), int(round((b - m.z0) / m.cell))
    zs = m.z0 + np.arange(ia, ib + 1) * m.cell
    ys = m.under[ia:ib + 1]
    ok = np.isfinite(ys)
    zs, ys = zs[ok], ys[ok]
    if len(zs) == 0:
        return None
    slope = np.polyfit(zs, ys, 1)[0] if len(zs) > 3 else 0.0
    if abs(np.degrees(np.arctan(slope))) < LEVEL_UNDER:
        level = float(np.min(ys))
        return a, level, b, level
    off = float(np.min(ys - slope * zs))
    return a, slope * a + off, b, slope * b + off


def socket_cells(origin, axes, span=None):
    e = M.matrix_to_euler(axes)
    cells = dict(x=T.cell_of(origin[0]), y=T.cell_of(origin[1]), z=T.cell_of(origin[2]),
                 rx=T.cell_of(e[0], 2), ry=T.cell_of(e[1], 2), rz=T.cell_of(e[2], 2))
    if span is not None:
        cells["span"] = T.cell_of(span)
    return cells


def boxes_text(g, boxes):
    """Sight-frame boxes as a folds / hides cell (the gun's root space)."""
    out = []
    for lo, hi in boxes:
        corners = np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])])
        w = g.from_sight(corners)
        out.append(" ".join("%.4f" % c for c in list(w.min(0)) + list(w.max(0))))
    return "|".join(out)


def measured_rows(name):
    """(rows {id: cells}, notes, Measured) for a gun measured from its shape."""
    m = measure(name)
    g, frame = m.gun, m.gun.frame
    rows, notes = {name + ".eye": socket_cells(g.eye, frame)}, []
    if m.rail:
        a, b, level = m.rail
        cells = socket_cells(g.from_sight([0.0, level, a]), frame, b - a)
        cells["folds"], cells["hides"] = boxes_text(g, m.boxes), boxes_text(g, m.hide_boxes)
        rows[name + ".sight"] = cells
    else:
        notes.append("no top rail found: give hints.json a rail for it (cm from the eye along the sight line)")
    # A muzzle with nothing to hang on it needs no row (the editor writes one itself for a gun that has no rows at all).
    if "muzzle" in g.mounts and g.prefab.tr[g.mounts["muzzle"]]["kids"]:
        w = g.prefab.world(g.mounts["muzzle"])
        rows[name + ".muzzle"] = socket_cells(w[:3, 3], w[:3, :3] / np.linalg.norm(w[:3, :3], axis=0), 0.0)
    for kind in ("grip", "under"):
        stretch = m.hint.get(kind)
        if not stretch:
            continue
        line = under_line(m, stretch[0] / 100.0, stretch[1] / 100.0)
        if line is None:
            notes.append("nothing of the gun is under its %s stretch %s" % (kind, stretch))
            continue
        z0, y0, z1, y1 = line
        along = np.array([0.0, y1 - y0, z1 - z0])
        length = float(np.linalg.norm(along))
        fwd = frame @ (along / length)
        up = frame[:, 1] - fwd * (frame[:, 1] @ fwd)
        up /= np.linalg.norm(up)
        rows["%s.%s" % (name, kind)] = socket_cells(g.from_sight([0.0, y0, z0]), np.stack([np.cross(up, fwd), up, fwd], axis=1), length)
    return rows, notes, m


def authored_rows(name):
    """Rows for a gun whose model carries its mount bones: each bone as a point."""
    g = GS.Gun(name)
    p = g.prefab
    rows = {name + ".eye": socket_cells(g.eye, g.frame)}
    for kind, tr in g.sockets().items():
        w = p.world(tr)
        rows["%s.%s" % (name, kind)] = socket_cells(w[:3, 3], w[:3, :3] / np.linalg.norm(w[:3, :3], axis=0), 0.0)
    return rows, [], None


def rows_of(name, points=None):
    """points: True reads the prefab's mounts as points, False measures the shape, None decides by the pack."""
    if points is None:
        points = GS.Gun(name).pack != "fps"
    return authored_rows(name) if points else measured_rows(name)


def foot_of(verts, hanging):
    """How far a part's foot reaches behind and ahead of its origin along its rail (m, both positive): a sight's is its
    lowest 4 mm, a hanging part's (a grip, a light) what is within 4 mm of its origin's plane or above it."""
    if len(verts) == 0:
        return 0.0, 0.0
    slab = verts[verts[:, 1] >= -.004] if hanging else verts[verts[:, 1] <= verts[:, 1].min() + .004]
    if len(slab) == 0:
        return 0.0, 0.0
    return max(0.0, -float(slab[:, 2].min())), max(0.0, float(slab[:, 2].max()))


def describe(name, m):
    """What was found, for a person to read: the rail, the sights' boxes, the underside in centimetre steps."""
    lines = []
    if m.rail:
        a, b, level = m.rail
        lines.append("   rail (from the eye along the sight line): %.1f..%.1f cm (%.1f long), its top %.1f mm under the line" % (
            a * 100, b * 100, (b - a) * 100, -level * 1000))
    lines.append("   iron sight pieces %s -> %d boxes; glass never drawn %s; parked pieces %s" % (sorted(m.irons), len(m.boxes), m.hides, m.junk))
    zs = np.arange(0, m.z0 + len(m.under) * m.cell, .01)
    cells = []
    for z in zs:
        i = int(round((z - m.z0) / m.cell))
        if 0 <= i < len(m.under):
            cells.append("  . " if not np.isfinite(m.under[i]) else "%4d" % round(m.under[i] * 1000))
    lines.append("   underside, mm from the sight line at each cm ahead of the eye (for a grip / under stretch in hints.json):")
    lines.append("   cm :" + "".join("%4d" % round(z * 100) for z in zs[:len(cells)]))
    lines.append("   mm :" + "".join(cells))
    return lines


PIECE_COLOURS = [(240, 170, 60), (90, 200, 240), (230, 110, 200), (150, 230, 120), (250, 240, 90), (250, 120, 90),
                 (150, 140, 250), (90, 230, 200)]


def pieces(name, out_path):
    """The pieces of the gun's mesh that may be its sights (near its sight line ahead of the eye, or standing over its
    top), listed and drawn, each in its own colour with its number: what hints.json's irons and hides name."""
    m = measure(name, {})
    g, v, t, piece = m.gun, m.v, m.t, m.piece
    rows = []
    for k in range(m.n):
        pv = v[piece == k]
        ahead = pv[pv[:, 2] > .03]
        reach = float(np.min(np.hypot(ahead[:, 0], ahead[:, 1]))) if len(ahead) else 9.0
        over = m.rail is not None and pv[:, 1].max() > m.rail[2] + .004 and pv[:, 2].max() - pv[:, 2].min() <= .20
        if reach <= NEAR_LINE or over:
            names, counts = np.unique(m.bones[piece == k].astype(str), return_counts=True)
            rows.append((k, reach, pv.min(0), pv.max(0), len(pv), names[np.argmax(counts)]))
    rows.sort(key=lambda r: r[2][2])
    lines = ["== %s: %d pieces; those near the sight line or standing over the top (cm in the sight frame: right, up, ahead)" % (name, m.n)]
    world = g.from_sight(v)
    lo, hi = world.min(0), world.max(0)
    span = max(hi[2] - lo[2], .3) + .2
    w, h = 1800, 760
    canvas = R.Canvas(w, h)
    centre = (0, (lo[1] + hi[1]) / 2 + .02, (lo[2] + hi[2]) / 2)
    right, up, fwd = (0, 0, 1), (0, 1, 0), (-1, 0, 0)
    listed = [r[0] for r in rows]
    tri_piece = piece[t[:, 0]]
    R.ortho(canvas, world, t[~np.isin(tri_piece, listed)], (150, 150, 158), right, up, fwd, centre, span)
    scale = w / span
    marks = []
    for i, (k, reach, plo, phi, count, bone) in enumerate(rows):
        colour = PIECE_COLOURS[i % len(PIECE_COLOURS)]
        # drawn a little nearer the viewer so a piece inside the body still shows
        R.ortho(canvas, world + np.array([-.3, 0, 0]), t[tri_piece == k], colour, right, up, fwd, centre, span)
        mid = g.from_sight([[0.0, phi[1], (plo[2] + phi[2]) / 2]])[0]
        marks.append(((mid[2] - centre[2]) * scale + w / 2, h / 2 - (mid[1] - centre[1]) * scale, k, colour))
        lines.append("   piece %3d  x[%+5.1f %+5.1f] y[%+5.1f %+5.1f] z[%+6.1f %+6.1f]  %5d vertices, %4.1f mm from the line, bone %s" % (
            k, plo[0] * 100, phi[0] * 100, plo[1] * 100, phi[1] * 100, plo[2] * 100, phi[2] * 100, count, min(reach, .999) * 1000, bone))
    x, y = (g.eye[2] - centre[2]) * scale + w / 2, h / 2 - (g.eye[1] - centre[1]) * scale
    R.hline(canvas, y, (60, 220, 90), x, w)
    img = canvas.image()
    from PIL import ImageDraw
    d = ImageDraw.Draw(img)
    d.text((8, 6), name + "   pieces that may be its sights, each with its number (hints.json: irons, hides); green: its sight line", fill=(255, 255, 255))
    for i, (px, py, k, colour) in enumerate(marks):
        d.text((px - 6, max(20, py - 14 - 11 * (i % 3))), str(k), fill=colour)
    img.save(out_path)
    lines.append("   picture: " + os.path.relpath(out_path, G.REPO))
    return lines
