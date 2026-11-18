"""Puts each FPS Animation Pack gun prefab's saved mounts and parts where the tables have them, by the game's own rule
(mounts_model), so the prefab as saved is the gun as it wakes. The game seats every part from the tables when a gun
wakes, so play never needs this; it is for what shows a gun without waking it (a pickup's model on the floor is the
prefab with its scripts taken off: PickupProps) and for reading a prefab in the editor.
Only numbers inside existing Transform documents and the grips' hand points in GunAttachments change; nothing is added
or removed. Unity must not be saving the same prefabs meanwhile (run it with the editor idle, or closed)."""
import re

import numpy as np

import gunmesh as G
import gunshape as GS
import mounts_model as M
import tables as T

_VEC = r"\{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+)\}"


def m_to_q(m):
    """3x3 rotation -> Unity quaternion (x, y, z, w)."""
    t = m[0, 0] + m[1, 1] + m[2, 2]
    if t > 0:
        s = np.sqrt(t + 1.0) * 2
        return ((m[2, 1] - m[1, 2]) / s, (m[0, 2] - m[2, 0]) / s, (m[1, 0] - m[0, 1]) / s, .25 * s)
    if m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = np.sqrt(1.0 + m[0, 0] - m[1, 1] - m[2, 2]) * 2
        return (.25 * s, (m[0, 1] + m[1, 0]) / s, (m[0, 2] + m[2, 0]) / s, (m[2, 1] - m[1, 2]) / s)
    if m[1, 1] > m[2, 2]:
        s = np.sqrt(1.0 + m[1, 1] - m[0, 0] - m[2, 2]) * 2
        return ((m[0, 1] + m[1, 0]) / s, .25 * s, (m[1, 2] + m[2, 1]) / s, (m[0, 2] - m[2, 0]) / s)
    s = np.sqrt(1.0 + m[2, 2] - m[0, 0] - m[1, 1]) * 2
    return ((m[0, 2] + m[2, 0]) / s, (m[1, 2] + m[2, 1]) / s, .25 * s, (m[1, 0] - m[0, 1]) / s)


def fmt(v):
    return "%.7g" % (0.0 if abs(v) < 5e-8 else v)


def set_transform(text, tr_id, pos=None, rot=None, euler=None):
    """Rewrites the local position / rotation lines of Transform document tr_id."""
    m = re.search(r"^--- !u!4 &%s\s*$" % re.escape(tr_id), text, flags=re.M)
    assert m, "no transform " + tr_id
    end = text.find("\n--- !u!", m.end())
    end = len(text) if end < 0 else end
    new = text[m.start():end]
    if pos is not None:
        new, n = re.subn(r"m_LocalPosition: \{[^}]*\}", "m_LocalPosition: {x: %s, y: %s, z: %s}" % tuple(fmt(c) for c in pos), new)
        assert n == 1
    if rot is not None:
        new, n = re.subn(r"m_LocalRotation: \{[^}]*\}", "m_LocalRotation: {x: %s, y: %s, z: %s, w: %s}" % tuple(fmt(c) for c in rot), new)
        assert n == 1
        if euler is not None:
            new, n = re.subn(r"m_LocalEulerAnglesHint: \{[^}]*\}", "m_LocalEulerAnglesHint: {x: %s, y: %s, z: %s}" % tuple(fmt(c) for c in euler), new)
    return text[:m.start()] + new + text[end:]


def move_hand(text, part_name, old_axes, old_o, new_axes, new_o):
    """The grips entry of GunAttachments whose part is named part_name: handPalm (a point) and handFingers / handFacing
    (directions) are kept in the gun's space, so they go with the part from its old pose to its new one."""
    go = None
    for m in re.finditer(r"^--- !u!1 &(-?\d+)\s*\nGameObject:(.*?)(?=^--- !u!)", text, flags=re.M | re.S):
        if re.search(r"^  m_Name: %s\s*$" % re.escape(part_name), m.group(2), flags=re.M):
            go = m.group(1)
    assert go, part_name
    # among the entries naming that part, the one with a hand (a grip)
    pat = re.compile(r"(part: \{fileID: %s\}.*?hasHand: 1\s*\n\s+handPalm: )%s(\s*\n\s+handFingers: )%s(\s*\n\s+handFacing: )%s" % (
        re.escape(go), _VEC, _VEC, _VEC), re.S)
    m = pat.search(text)
    if not m:
        return text
    g = m.groups()
    palm = np.array([float(g[1]), float(g[2]), float(g[3])])
    fingers = np.array([float(g[5]), float(g[6]), float(g[7])])
    facing = np.array([float(g[9]), float(g[10]), float(g[11])])
    turn = new_axes @ old_axes.T

    def vec(v):
        return "{x: %s, y: %s, z: %s}" % tuple(fmt(c) for c in v)
    new = g[0] + vec(turn @ (palm - old_o) + new_o) + g[4] + vec(turn @ fingers) + g[8] + vec(turn @ facing)
    return text[:m.start()] + new + text[m.end():]


def fit(guns, write):
    mounts, parts = T.table(T.MOUNTS), T.table(T.PARTS)
    changed = 0
    for name, path in GS.prefab_paths().items():
        if "FPSPackGuns" not in path.replace("\\", "/") or (guns and name not in guns):
            continue
        g = GS.Gun(name, path)
        p = g.prefab
        text = open(path, encoding="utf-8", newline="").read()
        assert "\r" not in text, "a prefab with CR line ends: " + path
        before = text
        eye_row = mounts.get(name + ".eye")
        if eye_row is None:
            print("%-9s no eye row: left as it is" % name)
            continue
        eye = np.array([T.number(eye_row, k) for k in "xyz"])
        notes = []
        for kind in ("sight", "grip", "under"):
            row = mounts.get("%s.%s" % (name, kind))
            if not row or kind not in g.mounts:
                continue
            mount = g.mounts[kind]
            assert p.tr[mount]["father"] == p.root, "a mount that is not the root's child: " + name
            origin, axes = M.pose(row)
            old_world = {k: p.world(k).copy() for k in p.tr[mount]["kids"]}
            text = set_transform(text, mount, origin, m_to_q(axes), [T.number(row, "rx"), T.number(row, "ry"), T.number(row, "rz")])
            for kid in p.tr[mount]["kids"]:
                pid = p.name(kid)
                data = parts.get(pid, {})
                back, front = T.number(data, "footBack"), T.number(data, "footFront")
                if kind == "sight":
                    aim = np.zeros(3)
                    for s in g.sights:
                        if s["part"] == kid and s["aim"]:
                            aim = (np.linalg.inv(p.world(kid)) @ p.world(s["aim"]))[:3, 3]
                    t = M.seat_sight(row, eye, aim, back, front)
                elif kind == "under":
                    t = M.seat_front(row, front)
                else:
                    t = M.seat_grip(row, back, front)
                text = set_transform(text, kid, (0.0, 0.0, t), (0.0, 0.0, 0.0, 1.0), (0.0, 0.0, 0.0))
                notes.append("%s %s %.1f cm" % (kind, pid, t * 100))
                if kind == "grip":
                    new_o, new_axes = M.place(row, t)
                    old = old_world[kid]
                    text = move_hand(text, pid, old[:3, :3], old[:3, 3], new_axes, new_o)
        if text == before:
            print("%-9s as the tables have it" % name)
            continue
        changed += 1
        print("%-9s %s: %s" % (name, "written" if write else "differs (nothing written without --write)", "; ".join(notes)))
        if write:
            with open(path, "w", encoding="utf-8", newline="") as f:
                f.write(text)
    return changed
