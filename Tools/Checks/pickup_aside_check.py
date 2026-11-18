# Checks, without the game, where a field pickup comes to lie when a Support's structure is set down over it:
# replicas of Placement.Holds and Placement.Sides (Assets/_Project/Scripts/Classes/Deployables/Placement.cs), run over
# the game's own numbers:
#   PickupRules.csv aside (how far off the structure's face the pickup's middle comes to lie), reach, touchReach;
#   ClassAbilities.csv Support.barricadeAhead / barricadeReach (how far ahead of the builder a structure goes up);
#   Barricade.cs (the structures' sizes: read out of the source, since they are the look's own constants).
#   python Tools/Checks/pickup_aside_check.py
# A pickup has no body, so a structure goes up over it as over bare floor; once it stands, each pickup its solid body
# holds makes way to the free floor beside it: the builder's side first, then by the shortest way out (Placement.Lie).
# Whether a side's floor is free is asked of the physics scene in the game (Placement.Free); here every side is free, or
# sides are closed by hand to see which comes next. The asserts: a point inside the body is held and one beside or on
# top of it is not; every spot is exactly `aside` off the body's face and keeps its place along that face; the first
# spot is on the builder's side wherever in the body the pickup lay and however far the builder stood; with the
# builder's side closed the shortest way is next; laid into a standing structure (no builder) the shortest way is
# first; and nothing that made way lies further from where it was than the structure is wide.
# Change Placement.Sides / Placement.Holds and these replicas together.
import csv, io, math, os, re, sys

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
TABLES = os.path.join(REPO, "Assets", "_Project", "Data", "Resources", "Tables")
SOURCE = os.path.join(REPO, "Assets", "_Project", "Scripts", "Classes", "Support", "Barricade.cs")
FLOOR_SKIN = 0.05  # Placement.FloorSkin


def rows_of(*path):
    return list(csv.reader(io.StringIO(open(os.path.join(TABLES, *path), encoding="utf-8-sig").read())))


def number(text):
    return float(text.rstrip("f"))


# ---- The game's numbers ----

rules = rows_of("Economy", "PickupRules.csv")
default = dict(zip(rules[0], next(row for row in rules if row and row[0] == "Default")))
ASIDE, REACH, TOUCH = float(default["aside"]), float(default["reach"]), float(default["touchReach"])
abilities = {row[0]: row[1] for row in rows_of("Classes", "ClassAbilities.csv")[1:] if len(row) > 1}
AHEAD, FAR = float(abilities["Support.barricadeAhead"]), float(abilities["Support.barricadeReach"])

source = open(SOURCE, encoding="utf-8-sig").read()
found = re.search(r"const float Width = ([\d.]+f?), Height = ([\d.]+f?), Depth = ([\d.]+f?);", source)
planks = tuple(number(found.group(i)) for i in (1, 2, 3))
sizes = {"planks": planks}
for kind, name in (("Crate", "crate"), ("Light", "light"), ("Wire", "wire")):
    found = re.search(r"Kind\." + kind + r" => new Vector3\(([\d.]+f?), ([\d.]+f?), ([\d.]+f?)\)", source)
    sizes[name] = tuple(number(found.group(i)) for i in (1, 2, 3))
# The searchlight's body is its post, thinner than the room it is given (Barricade.Build).
found = re.search(r"kind == Kind\.Light \? new Vector3\(([\d.]+f?), size\.y, ([\d.]+f?)\) : size", source)
bodies = dict(sizes)
bodies["light"] = (number(found.group(1)), sizes["light"][1], number(found.group(2)))
SOLID = ("planks", "crate", "light")  # wire is a trigger: walked through, nothing makes way for it


# ---- Replicas ----

def holds(body, point):
    """Placement.Holds for a box standing on the floor at the origin (its centre half its height up), unturned."""
    x, y, z = point
    return abs(x) <= body[0] * .5 and abs(y - body[1] * .5) <= body[1] * .5 and abs(z) <= body[2] * .5


def flat_square(a, b):
    return (a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2


def sides(at, half_along, half_through, toward):
    """Placement.Sides, line for line, flat (x along the structure, z through it; the middle at the origin)."""
    x, z = at
    into = [(x, -half_through), (x, half_through), (-half_along, z), (half_along, z)]
    for i in range(1, 4):
        j = i
        while j > 0 and flat_square(into[j], at) < flat_square(into[j - 1], at):
            into[j], into[j - 1] = into[j - 1], into[j]
            j -= 1
    first = 0
    for i in range(1, 4):
        if flat_square(into[i], toward) < flat_square(into[first], toward):
            first = i
    for j in range(first, 0, -1):
        into[j], into[j - 1] = into[j - 1], into[j]
    return into


def off_face(spot, body):
    """How far a spot beside a body is from the body's nearest face, flat (0 or less: inside)."""
    dx, dz = abs(spot[0]) - body[0] * .5, abs(spot[1]) - body[2] * .5
    return max(dx, dz) if min(dx, dz) <= 0 else math.hypot(dx, dz)


def grid(body, n=9):
    for i in range(n):
        for j in range(n):
            yield ((i / (n - 1) - .5) * body[0] * .98, (j / (n - 1) - .5) * body[2] * .98)


# ---- The checks ----

problems = []


def expect(ok, what):
    if not ok:
        problems.append(what)


print(f"PickupRules: aside {ASIDE} m, [F] reach {REACH} m, walk-over reach {TOUCH} m; a structure goes up {AHEAD}..{FAR} m ahead of its builder")
print(f"{'structure':10} {'body (m)':18} {'moved, builder side':22} {'from the builder after':24} next if that side is closed")
for name in SOLID:
    body = bodies[name]
    half_along, half_through = body[0] * .5 + ASIDE, body[2] * .5 + ASIDE
    # Held or not: the floor point a hair up, as Placement.Holder asks.
    for at in grid(body):
        expect(holds(body, (at[0], FLOOR_SKIN, at[1])), f"{name}: a pickup inside the body at {at} is not held")
    expect(not holds(body, (0, body[1] + FLOOR_SKIN, 0)), f"{name}: a pickup resting on top of it counts as inside")
    expect(not holds(body, (body[0] * .5 + .01, FLOOR_SKIN, 0)), f"{name}: a pickup beside its end counts as inside")
    expect(not holds(body, (0, FLOOR_SKIN, body[2] * .5 + .01)), f"{name}: a pickup before its face counts as inside")
    moved_least, moved_most, far_least, far_most, seconds = 9e9, 0.0, 9e9, 0.0, set()
    for ahead in (AHEAD, (AHEAD + FAR) * .5, FAR):
        builder = (0.0, -ahead)  # the structure faces the way its builder looked: the builder is straight behind it
        for at in grid(body):
            spots = sides(at, half_along, half_through, builder)
            for spot in spots:
                expect(abs(off_face(spot, body) - ASIDE) < 1e-6, f"{name}: a spot {spot} is not {ASIDE} m off the face")
                expect(abs(spot[0] - at[0]) < 1e-9 or abs(spot[1] - at[1]) < 1e-9, f"{name}: a spot {spot} lost its place along the face from {at}")
            expect(len(set(spots)) == 4, f"{name}: the four spots are not four ({spots})")
            first = spots[0]
            expect(abs(first[1] + half_through) < 1e-9, f"{name}: from {at}, builder {ahead} m behind, the first spot {first} is not on the builder's side")
            moved = math.sqrt(flat_square(first, at))
            away = math.sqrt(flat_square(first, builder))
            moved_least, moved_most = min(moved_least, moved), max(moved_most, moved)
            far_least, far_most = min(far_least, away), max(far_most, away)
            expect(away < math.sqrt(flat_square(at, builder)), f"{name}: making way took it further from the builder ({at} -> {first})")
            # The builder's side closed (something stands right behind the structure): the shortest way of the rest.
            rest = spots[1:]
            expect(all(flat_square(rest[i], at) <= flat_square(rest[i + 1], at) + 1e-12 for i in range(2)), f"{name}: after the builder's side the rest are not by the shortest way ({at}: {rest})")
            second = rest[0]
            seconds.add("front" if abs(second[1] - half_through) < 1e-9 else "an end")
            expect(math.sqrt(flat_square(second, at)) <= max(body[0], body[2]) * .5 + ASIDE + 1e-9, f"{name}: the second way out is longer than half the structure ({at} -> {second})")
    # Laid into a standing structure (a gun put down at an ammo crate): no builder, the shortest way out first.
    for at in grid(body):
        spots = sides(at, half_along, half_through, at)
        ways = [math.sqrt(flat_square(spot, at)) for spot in spots]
        expect(all(ways[i] <= ways[i + 1] + 1e-12 for i in range(3)), f"{name}: laid into it at {at}, the ways out are not shortest first ({ways})")
        expect(ways[0] <= min(body[0], body[2]) * .5 + ASIDE + 1e-9, f"{name}: laid into it at {at}, the first way out is {ways[0]:.2f} m")
    print(f"{name:10} {body[0]:.2f} x {body[1]:.2f} x {body[2]:.2f}  {moved_least:.2f} .. {moved_most:.2f} m{'':8} {far_least:.2f} .. {far_most:.2f} m{'':10} {' or '.join(sorted(seconds))}")
    # Within the builder's [F] reach after a build at the nearest spot (further builds: a step forward).
    nearest = AHEAD - half_through
    expect(nearest > 0, f"{name}: the spot on the builder's side is behind the builder")

wire = sizes["wire"]
print(f"wire       {wire[0]:.2f} x {wire[1]:.2f} x {wire[2]:.2f}  stays where it lay: wire is walked through (a trigger), Placement.Holder never finds it")
print(f"reach      built at the nearest ({AHEAD} m): the pickup lies {AHEAD - bodies['planks'][2] * .5 - ASIDE:.2f} m (planks) / "
      f"{AHEAD - bodies['crate'][2] * .5 - ASIDE:.2f} m (crate) / {AHEAD - bodies['light'][2] * .5 - ASIDE:.2f} m (light) ahead of the builder: "
      f"inside the [F] reach of {REACH} m; built at the farthest ({FAR} m) it is a step away")
for name in SOLID:
    expect(AHEAD - bodies[name][2] * .5 - ASIDE <= REACH, f"{name}: built at the nearest spot, the pickup that made way is out of [F] reach")
    # Taken by walking over (ammo, money): standing against the face on the builder's side, it is within touch reach.
    expect(ASIDE <= TOUCH, f"{name}: a walk-over pickup that made way is out of touch reach from beside it")

if problems:
    print(f"\n{len(problems)} PROBLEM(S):")
    for line in problems[:20]:
        print("  " + line)
    sys.exit(1)
print("\nALL CHECKS PASSED")
