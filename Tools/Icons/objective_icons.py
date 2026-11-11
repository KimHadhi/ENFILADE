"""
The objective guide's kind pictures (2026-11-11), drawn inside the HUD's pink diamond that points the way
(ObjectiveGuideHUD): one per kind of step, so the mark says what waits there and not only where. The user found the
guide always a door ("무조건 문 모양이라서 헷갈리는데"), though the door told them at once to go there; the rest stop keeps
the door. White shapes on transparent, 128 px, thick strokes so they still read at 20 px. Same drawing kit as
stance_icons.py (python objective_icons.py, standard library only); written to
Assets/_Project/Resources/UI/Icons/Objective_<kind>.png.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from stance_icons import capsule, circle, draw, png, OUT  # noqa: E402

W = 14  # stroke width


def ring(cx, cy, r, width, steps=48):
    """A circle's outline as capsules round it."""
    pts = [(cx + r * math.cos(2 * math.pi * i / steps), cy + r * math.sin(2 * math.pi * i / steps)) for i in range(steps + 1)]
    return [capsule(pts[i], pts[i + 1], width, 3) for i in range(steps)]


def outline(points, width, closed=True):
    count = len(points) if closed else len(points) - 1
    return [capsule(points[i], points[(i + 1) % len(points)], width, 4) for i in range(count)]


def reach():
    """A flag on its pole: get to this point."""
    parts = [capsule((40, 112), (40, 16), W)]
    parts.append([(40, 16), (104, 34), (40, 58)])
    return parts


def press():
    """A round button with a finger's press in the middle: do something here."""
    return ring(64, 64, 44, W) + [circle(64, 64, 18)]


def hold():
    """A shield: stand and hold the place."""
    shield = [(22, 20), (106, 20), (106, 62), (96, 88), (64, 114), (32, 88), (22, 62)]
    return outline(shield, W) + [capsule((64, 34), (64, 92), W - 2)]


def gather():
    """Two arrows coming together on a point: everyone here."""
    parts = [circle(64, 64, 13)]
    parts += [capsule((8, 64), (36, 64), W), [(30, 46), (48, 64), (30, 82)]]
    parts += [capsule((120, 64), (92, 64), W), [(98, 46), (80, 64), (98, 82)]]
    return parts


def collect():
    """A fuel can with its spout and the cross pressed into its side: bring cans here."""
    body = [(24, 38), (78, 38), (104, 58), (104, 116), (24, 116)]
    parts = outline(body, W)
    parts += [capsule((44, 68), (84, 102), W - 4), capsule((84, 68), (44, 102), W - 4)]
    parts += [capsule((30, 36), (30, 14), W - 2), capsule((30, 14), (60, 14), W - 2)]  # the handle
    parts += [capsule((92, 48), (98, 22), W - 2), capsule((92, 20), (106, 24), W - 4)]  # the spout and its cap
    return parts


def checkpoint():
    """A doorway, as the rest stop's light stands: the safe room."""
    frame = [(30, 118), (30, 12), (98, 12), (98, 118)]
    return outline(frame, W, closed=False) + [circle(84, 68, 8)]


def escape():
    """Two chevrons rising: out of here."""
    return [
        [(64, 8), (114, 58), (96, 74), (64, 42), (32, 74), (14, 58)],
        [(64, 56), (114, 106), (96, 122), (64, 90), (32, 122), (14, 106)],
    ]


ICONS = {"reach": reach, "press": press, "hold": hold, "gather": gather, "collect": collect, "checkpoint": checkpoint,
         "escape": escape}

if __name__ == "__main__":
    drawn = {name: draw(make()) for name, make in ICONS.items()}
    if len(sys.argv) > 1:
        from stance_icons import sheet
        sheet(list(drawn.values()), sys.argv[1])
    else:
        for name, alpha in drawn.items():
            png(alpha, os.path.join(OUT, "Objective_" + name + ".png"))
            print("drew Objective_" + name)
