"""Where a knife swing's moments fall, worked out from its motion: the seed of the marks the user then moves.

A swing in the game is its clip with three marks (KnifeSwing, PlayerMeleeCombat): the blade is live from 벰 to 벰 끝 (it
cuts what it passes through: the game traces the knife's own model then, never before or after), and the next action
may begin at 다음 (the recovery ends there; the rest of the clip is the arm going home, which a next swing cuts short).
Until 2026-11-18 a clip had one mark, 벰, the moment the blade met, and the game never read it (it hit with a sphere
from the eyes for a fixed 0.18 s instead).

This file has no Blender in it: knife_tools.py (the workspace: a clip whose marks were never set gets these), the port of
the addons' clips and the one-off that seeded the game's tables on 2026-11-18 all call seed() on the same samples, the
clip's rows as the game's tables carry them (two a frame at 30 fps), so they agree to the half frame.

  seed(samples, meet) -> (start, end, next) in seconds from the clip's start
  samples: [(time, wrist (x, y, z), along (x, y, z))] in time order, the view's axes in metres (the table's columns);
  meet: the old 벰, the moment the blade met (seconds), or None to find it (the knuckles' fastest moment in the first
  seven tenths of the clip).
"""
import math

# The knuckles: this far along the hand from the wrist (m), as the port's hit_of takes them.
KNUCKLES = 0.1
# The cut is over once the knuckles slow to this share of their fastest in the swing.
SLOW_SHARE = 0.25
# The blade stays live at least this long past the moment it meets, and the recovery lasts this long past the cut (s).
LEAST_AFTER = 1.0 / 30
RECOVERY = 4.0 / 30
# Marks sit on whole frames at 30 fps: the workspace's timeline markers do.
STEP = 1.0 / 30


def _point(sample):
    _t, wrist, along = sample
    return tuple(wrist[i] + along[i] * KNUCKLES for i in range(3))


def _distance(a, b):
    return math.sqrt(sum((a[i] - b[i]) ** 2 for i in range(3)))


def _snap(value):
    return round(value / STEP) * STEP


def seed(samples, meet=None):
    """The cut's start and end and where the next action may begin (seconds), for a swing whose blade meets at `meet`."""
    if len(samples) < 2:
        return 0.0, 0.0, 0.0
    first, last = samples[0][0], samples[-1][0]
    points = [_point(s) for s in samples]
    speeds = [0.0] + [_distance(points[i], points[i - 1]) / max(1e-6, samples[i][0] - samples[i - 1][0]) for i in range(1, len(samples))]
    if meet is None:
        limit = first + (last - first) * 0.7
        best = max((i for i in range(1, len(samples)) if samples[i][0] <= limit), key=lambda i: speeds[i], default=1)
        meet = samples[best][0]
    meet = min(max(meet, first), last)
    at_meet = min(range(len(samples)), key=lambda i: abs(samples[i][0] - meet))
    # The cut starts where the wind-up is at its furthest: of the moments before the blade meets, the one whose wrist is
    # furthest from where it meets (the port's cocked_of). A clip that starts cocked starts cutting.
    start = max(range(at_meet + 1), key=lambda i: (_distance(samples[i][1], samples[at_meet][1]), -i))
    if _distance(samples[start][1], samples[at_meet][1]) < 1e-4:
        start = at_meet if at_meet == 0 else 0
    peak = max(speeds[start:] or [0.0])
    # The way the knuckles go as the blade meets: over the step into that moment, or out of it for a clip that starts there.
    a, b = (at_meet - 1, at_meet) if at_meet > 0 else (0, 1)
    way = tuple(points[b][i] - points[a][i] for i in range(3))
    end = len(samples) - 1
    for i in range(at_meet + 1, len(samples)):
        if samples[i][0] < meet + LEAST_AFTER - 1e-6:
            continue
        step = tuple(points[i][j] - points[i - 1][j] for j in range(3))
        # Over once the hand has slowed to a quarter of its fastest, or has turned back the way it came (the return of a
        # swing is fast too, and is no cut).
        if speeds[i] < peak * SLOW_SHARE or sum(step[j] * way[j] for j in range(3)) < 0.0:
            end = i if speeds[i] < peak * SLOW_SHARE else i - 1
            break
    cut_from, cut_to = _snap(samples[start][0] - first), _snap(samples[end][0] - first)
    return cut_from, cut_to, _snap(min(last - first, cut_to + RECOVERY))
