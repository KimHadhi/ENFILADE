"""Walks the player's capsule along a path through a built scene's solid boxes, without Unity, and says where it is
blocked or has no floor. Boxes only (the builder-made maps are boxes and ramps); mesh colliders are not read.
usage: python walk_probe.py <scene.unity> x,y,z x,y,z [...]   (feet points; y is where to look for the floor from)"""
import math
import sys
sys.path.insert(0, __import__("os").path.dirname(__file__))
import unity_scene as us

RADIUS, HEIGHT, STEP = 0.3, 1.75, 0.3


def conj(q):
    return [-q[0], -q[1], -q[2], q[3]]


class Solid:
    def __init__(self, o):
        body = o.comp("BoxCollider")
        size = us._vec(body, rb"m_Size", 3) or [1, 1, 1]
        centre = us._vec(body, rb"m_Center", 3) or [0, 0, 0]
        c = us.qrot(o.wrot, [centre[i] * o.wscale[i] for i in range(3)])
        self.c = [o.wpos[i] + c[i] for i in range(3)]
        self.h = [abs(size[i] * o.wscale[i]) * .5 for i in range(3)]
        self.q = o.wrot
        self.qi = conj(o.wrot)
        self.name = o.name
        self.reach = math.sqrt(sum(v * v for v in self.h))

    def local(self, p):
        return us.qrot(self.qi, [p[i] - self.c[i] for i in range(3)])

    def distance(self, p):
        l = self.local(p)
        d = [max(abs(l[i]) - self.h[i], 0.0) for i in range(3)]
        return math.sqrt(sum(v * v for v in d))

    def ray_down(self, p, far):
        """Distance down from p to this box's surface, or None."""
        o = self.local(p)
        d = us.qrot(self.qi, [0, -1, 0])
        t0, t1 = 0.0, far
        for i in range(3):
            if abs(d[i]) < 1e-9:
                if abs(o[i]) > self.h[i]:
                    return None
                continue
            a = (-self.h[i] - o[i]) / d[i]
            b = (self.h[i] - o[i]) / d[i]
            if a > b:
                a, b = b, a
            t0, t1 = max(t0, a), min(t1, b)
            if t0 > t1:
                return None
        return t0


def load(scene):
    objs, _ = us.load(scene)
    solids = []
    for o in objs.values():
        body = o.comp("BoxCollider")
        if body is None or not o.active or not us.enabled(body) or us.is_trigger(body):
            continue
        # every ancestor active
        p, on = o.parent, True
        while p is not None:
            on = on and p.active
            p = p.parent
        if on:
            solids.append(Solid(o))
    return solids


def floor_under(solids, p, up=0.6, down=1.5):
    best = None
    start = [p[0], p[1] + up, p[2]]
    for s in solids:
        if abs(s.c[0] - p[0]) > s.reach + .1 or abs(s.c[2] - p[2]) > s.reach + .1:
            continue
        t = s.ray_down(start, up + down)
        if t is not None and (best is None or t < best[0]):
            best = (t, s)
    if best is None:
        return None, None
    return start[1] - best[0], best[1]


def blocked(solids, feet, skip=None):
    """Names of the boxes the standing capsule would be inside of at these feet."""
    hits = []
    n = 6
    for k in range(n):
        y = feet[1] + RADIUS + .06 + (HEIGHT - 2 * RADIUS - .06) * k / (n - 1)
        centre = [feet[0], y, feet[2]]
        for s in solids:
            if s is skip:
                continue
            if abs(s.c[0] - centre[0]) > s.reach + RADIUS or abs(s.c[1] - centre[1]) > s.reach + RADIUS or abs(s.c[2] - centre[2]) > s.reach + RADIUS:
                continue
            if s.distance(centre) < RADIUS - .02 and s.name not in hits:
                hits.append(s.name)
    return hits


def walk(solids, points, pace=0.1, quiet=False):
    """Returns the list of troubles along the path."""
    troubles = []
    last_y = None
    for a, b in zip(points, points[1:]):
        length = math.dist(a, b)
        steps = max(1, int(length / pace))
        for i in range(steps + 1):
            t = i / steps
            p = [a[k] + (b[k] - a[k]) * t for k in range(3)]
            if last_y is not None:
                p[1] = last_y
            y, on = floor_under(solids, p)
            if y is None:
                troubles.append(("no floor", p, ""))
                last_y = p[1]
                continue
            if last_y is not None and y - last_y > STEP + .02:
                troubles.append(("step up %.2f" % (y - last_y), p, on.name))
            feet = [p[0], y, p[2]]
            hit = blocked(solids, feet)
            if hit:
                troubles.append(("inside", feet, ", ".join(hit)))
            last_y = y
            if not quiet and i % 5 == 0:
                print("  at (%.2f, %.2f, %.2f) on %s%s" % (feet[0], feet[1], feet[2], on.name, (" BLOCKED by " + ", ".join(hit)) if hit else ""))
    return troubles


if __name__ == "__main__":
    solids = load(sys.argv[1])
    pts = [[float(v) for v in a.split(",")] for a in sys.argv[2:]]
    print("solid boxes", len(solids))
    for kind, p, what in walk(solids, pts):
        print("TROUBLE %s at (%.2f, %.2f, %.2f) %s" % (kind, p[0], p[1], p[2], what))
