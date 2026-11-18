# Checks how a thrown grenade flies without the game: a line-for-line replica of GrenadeFlight.Step and Turn
# (Assets/_Project/Scripts/Grenades/GrenadeFlight.cs) flown in a world of planes (a floor and a wall), with the numbers
# read from the game's own tables (GrenadeRules.csv flight*, Grenades.csv). Run it after changing either:
#   python Tools/Checks/grenade_flight_check.py
# It asserts that no face adds speed, that the ball is never inside a face (a sweep that starts inside one never sees
# it again), that every kind left alone comes to rest, and that one throw flown twice ends at one point (the aiming line
# and the grenade are the same function). It prints where each kind bursts, to read against what the game shows.
# Change GrenadeFlight.cs and this replica together.
import math
import csv, io, os

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
TABLES = os.path.join(REPO, "Assets", "_Project", "Data", "Resources", "Tables")


def rows_of(*path):
    text = open(os.path.join(TABLES, *path), encoding="utf-8-sig").read()
    return list(csv.reader(io.StringIO(text)))


def number(cell, fallback):
    try:
        return float(cell)
    except (TypeError, ValueError):
        return fallback


G = (0.0, -9.81, 0.0)
SKIN, LEVEL, MOST_TURNS = .005, .6, 4
_rules = rows_of("Grenades", "GrenadeRules.csv")
_r = dict(zip(_rules[0], next(row for row in _rules[1:] if row and row[0] == "Default")))
RULES = dict(tick=number(_r.get("flightTick"), .02), drag=number(_r.get("flightDrag"), .05), grip=number(_r.get("flightGrip"), .29),
             roll=number(_r.get("flightRoll"), 3.0), bounce_from=number(_r.get("flightBounceFrom"), 1.4), rest=number(_r.get("flightRest"), .3))
print("rules (GrenadeRules.csv):", RULES)


def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def mag(a): return math.sqrt(dot(a, a))


# The world: planes as (normal, offset) with normal . p >= offset the free side.
WORLD = [((0, 1, 0), 0.0),      # floor at y = 0
         ((-1, 0, 0), -9.0)]    # wall at x = 9, facing back along -x


def sphere_cast(origin, radius, way, reach):
    best = None
    for normal, offset in WORLD:
        closing = dot(normal, way)
        if closing >= -1e-9:
            continue
        gap = dot(normal, origin) - offset - radius
        if gap < -1e-6:
            continue                      # started inside: a sweep does not see it
        t = max(0.0, gap) / -closing
        if t <= reach and (best is None or t < best[0]):
            best = (t, normal, sub(add(origin, mul(way, t)), mul(normal, radius)))
    return best


def turn(velocity, normal, bounce, grip, roll, bounce_from, seconds):
    along = dot(velocity, normal)
    into = max(0.0, -along)
    bounces = into >= bounce_from
    if along >= 0:
        return velocity, into, bounces
    slide = sub(velocity, mul(normal, along))
    if bounces:
        return add(mul(slide, 1 - grip), mul(normal, into * bounce)), into, True
    fast = mag(slide)
    slower = max(0.0, fast - roll * seconds)
    return (mul(slide, slower / fast) if fast > 1e-5 else (0, 0, 0)), into, False


class Flight:
    def __init__(self, at, velocity):
        self.p, self.v, self.age, self.bounces, self.resting = at, velocity, 0.0, 0, False
        self.rolled_at = -1.0

    def step(self, radius, gravity, bounce, r=RULES):
        dt = r["tick"]
        self.age += dt
        struck = None
        if self.resting:
            return struck
        self.v = add(self.v, mul(G, gravity * dt))
        self.v = mul(self.v, max(0.0, 1 - r["drag"] * dt))
        left, rolled = dt, False
        for _ in range(MOST_TURNS):
            if left <= 1e-6:
                break
            move = mul(self.v, left)
            length = mag(move)
            if length < 1e-6:
                break
            way = mul(move, 1 / length)
            hit = sphere_cast(self.p, radius, way, length + SKIN)
            if hit is None:
                self.p = add(self.p, move)
                break
            self.p = add(add(self.p, mul(way, hit[0])), mul(hit[1], SKIN))
            left *= 1 - min(hit[0], length) / length
            self.least_gap = min(getattr(self, "least_gap", 1.0), min(dot(n, self.p) - o - radius for n, o in WORLD))
            before = mag(self.v)
            rolling = 0.0 if rolled else (self.age - self.rolled_at if self.rolled_at >= 0 and self.age - self.rolled_at <= dt * 3.5 else dt)
            self.v, into, bounced = turn(self.v, hit[1], bounce, r["grip"], r["roll"], r["bounce_from"], rolling)
            assert mag(self.v) <= before + 1e-6, "a face added speed"
            if bounced:
                struck = hit[2]
                self.bounces += 1
                self.rolled_at = -1.0
                continue
            rolled = True
            self.rolled_at = self.age
            if hit[1][1] >= LEVEL and mag(self.v) <= r["rest"]:
                self.v, self.resting = (0, 0, 0), True
                break
        return struck

    def over(self, seconds): return self.age >= seconds - 1e-4


def fly(at, velocity, seconds, radius, gravity, bounce):
    f = Flight(at, velocity)
    marks = []
    f.clear = 1.0
    while not f.over(seconds):
        s = f.step(radius, gravity, bounce)
        # never inside a face: a sweep that starts inside one would never see it again
        gap = min(dot(n, f.p) - o - radius for n, o in WORLD)
        assert gap > 1e-4, ("inside or touching a face", gap, f.p)
        f.clear = min(f.clear, gap)
        if s:
            marks.append(s)
        if f.resting:
            break
    return f, marks


_g = rows_of("Grenades", "Grenades.csv")
KINDS = {}  # Grenades.csv: fuse, speed, lift, gravity, bounce, size (the kinds with a fuse: the ones that fly on)
for _row in _g[1:]:
    _d = dict(zip(_g[0], _row))
    if _row and not _row[0].startswith("#") and number(_d.get("fuse"), 0) > 0 and number(_d.get("speed"), 0) > 0:
        KINDS[_row[0]] = (number(_d["fuse"], 1.5), number(_d["speed"], 16), number(_d.get("lift"), 2.5), number(_d.get("gravity"), 1),
                          number(_d.get("bounce"), .3), number(_d.get("size"), .08))

print("level throw from the hand (1.4 m up) toward a wall 9 m ahead, each kind's own fuse:")
for name, (fuse, speed, lift, gravity, bounce, size) in KINDS.items():
    start, velocity = (0, 1.4, 0), (speed, lift, 0)
    a, marks = fly(start, velocity, fuse, size / 2, gravity, bounce)
    b, _ = fly(start, velocity, fuse, size / 2, gravity, bounce)
    assert a.p == b.p, "two flights of one throw differ"
    where = ", ".join("(%.2f, %.2f)" % (m[0], m[1]) for m in marks)
    print("  %-5s bursts at x %.2f y %.2f after %d bounce(s) [%s]%s" % (name, a.p[0], a.p[1], a.bounces, where, " (at rest)" if a.resting else ""))

print("thrown down at the floor 3 m ahead (steep), frag: where the old line stopped vs where it bursts")
f, marks = fly((0, 1.4, 0), (10, -4, 0), 2.0, .0425, 1, .30)
print("  first floor contact x %.2f; bursts at x %.2f y %.2f after %d bounces%s" % (marks[0][0], f.p[0], f.p[1], f.bounces, " (at rest)" if f.resting else ""))

print("dropped from 2 m, each kind left alone for 6 s: must come to rest on the floor")
for name, (fuse, speed, lift, gravity, bounce, size) in KINDS.items():
    f, marks = fly((0, 2, 0), (0, 0, 0), 6.0, size / 2, gravity, bounce)
    assert f.resting and abs(f.p[1] - size / 2) < .02, (name, f.p, f.resting)
    print("  %-5s rests at y %.3f (radius %.3f) after %d bounce(s), %.2f s" % (name, f.p[1], size / 2, f.bounces, f.age))

print("rolled along the floor at 5 m/s: stops by the roll's wear (3 m/s2) -> about 4.2 m, 1.7 s")
f, _ = fly((0, .0425 + SKIN, 0), (5, 0, 0), 6.0, .0425, 1, .30)
print("  stopped at x %.2f after %.2f s%s; least clearance from the floor on the way %.2f mm" % (f.p[0], f.age, " (at rest)" if f.resting else "", f.clear * 1000))
print("rolled fast and far with no wear (roll 0, 12 m/s, 6 s): the floor must hold it all the way")
slick = dict(RULES, roll=0.0)
g = Flight((0, .0425 + SKIN, 0), (12, 0, 0))
least = 1.0
while not g.over(6.0):
    g.step(.0425, 1, .30, slick)
    gap = g.p[1] - .0425
    assert gap > 1e-4, ("fell into the floor", gap, g.age)
    least = min(least, gap)
    if g.p[0] > 8.9:
        break
print("  least clearance %.2f mm over %.1f m" % (least * 1000, g.p[0]))
print("ALL CHECKS PASSED")
