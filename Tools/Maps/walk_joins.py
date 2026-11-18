# Where the baked walk mesh joins the mission's route and where it does not, read from a built scene without Unity:
# for each step's stretch (the spot before it to its own spot) whether a zombie can walk it, only drop or hop along it
# (one way), or not at all; for each spawn point whether it stands on the walk mesh, inside something solid, and
# whether anything it sends can get to the route's ground. For every break it finds the places where the two sides of
# the walk mesh come nearest, in the order a walker would meet them, and reads the scene's boxes there: nothing between
# (the way is open to a body and only too narrow for the agent the mesh was baked for: its clear width), a low thing
# across the way (how high over the floor), a difference of height with no stair that the mesh follows, or a wall.
# The build's own audit says the same from the engine at every rebuild (GreyboxBuilder.Audit, '경로와 걷기 메시').
#   python Tools/Maps/walk_joins.py [scene name ...]            -> Logs/walk-joins.txt (and the lines on the screen)
#   python Tools/Maps/walk_joins.py --radius 0.28 --voxel 0.1 [--hop NAME ...] [scene name ...]
#       -> Logs/walk-joins-predicted.txt: the same report for the walk mesh as a rebuild with that agent would bake it,
#          as far as this can be known without baking. Every open break of today's mesh is measured (the opening's two
#          sides at body height, on the bake's voxel grid) and counted in voxels left once the bake has taken the
#          agent's radius off both sides: two or more and the two islands are taken as joined; one is said and left
#          cut (a bake keeps or drops a single voxel by its neighbours). --hop NAME lays a link over every box of that
#          name, as the builder's Hop does. The same count at the radius the mesh WAS baked with must leave every
#          break cut: that line is the model's check against the engine's own bake. --without NAME leaves out the
#          boxes of that name (a prop the rebuild's own rules move out of a doorway: the scene still has it there).
# What it cannot know: mesh colliders (a bought building's walls are not read, so a break beside them is named open),
# links a build adds by hand over a low thing (the builder's Hop: they are in the scene, not in the baked file; read
# from the scene and counted as joining, see hops), and what a run changes (a gate shut, a variant's blocker, fire).
import heapq
import io
import math
import os
import re
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import flow
import unity_scene
from navmesh import NavMesh

REPO = flow.REPO
# The game looks this far for the walk mesh at a spot and at a spawn point (SpawnRoute.SpotSearch, CampaignHorde.PointSearch).
SPOT_SEARCH, POINT_SEARCH = 3.0, 2.0
# A zombie's body (Tables/Enemies/ZombieCrowd bodyRadius) and the band of height over the floor a body fills (m).
BODY, BODY_LOW, BODY_HIGH = .28, .35, 1.6
# What a walking body steps over without a link (the walk mesh's agentClimb), and what counts as a thing to get over
# rather than a wall (m over the floor).
STEP, LOW_THING = .75, 1.6
# Two islands this near on one level with floor and nothing else between them face each other across an open way (m):
# a door's wall and what the bake trimmed either side of it, or a prop's width.
OPEN_BREAK = 3.0


# ---- the walk mesh ----
def islands(nm):
    """Island index per polygon, walking only (no drop or hop links)."""
    isle = [-1] * len(nm.polys)
    n = 0
    for p in nm.polys:
        if isle[p.index] >= 0:
            continue
        stack = [p.index]
        isle[p.index] = n
        while stack:
            cur = stack.pop()
            for link in nm.polys[cur].links:
                if len(link) == 3 and isle[link[0]] < 0:
                    isle[link[0]] = n
                    stack.append(link[0])
        n += 1
    return isle, n


def load_detail(nm, path):
    """Each polygon's detail triangles (the surface the engine stands a body on), read from the baked file: a polygon
    is flat only in plan, and one with a corner on a kerb or a low thing has a plane through its first three corners
    that is metres off at its far side (navmesh.height_on). {polygon: [(a, b, c), ...]}; a polygon without is left out."""
    data = open(path, "rb").read()
    detail = {}
    base = 0
    for m in re.finditer(b"VAND", data):
        o = m.start()
        (magic, ver, tx, ty, uid, np_, nv, ndm, ndv, ndt, nbv) = struct.unpack_from("<4sii i I iiiiii", data, o)
        at = o + 72
        verts = [struct.unpack_from("<3f", data, at + 12 * i) for i in range(nv)]
        at += 12 * nv
        polys = []
        for i in range(np_):
            raw = data[at + 32 * i: at + 32 * i + 32]
            polys.append((struct.unpack_from("<6H", raw, 0), raw[28]))
        at += 32 * np_
        meshes = [struct.unpack_from("<IIHH", data, at + 12 * i) for i in range(ndm)]
        # the counts as two bytes each, if the two-shorts reading does not fit the tile
        if any(vb + vc > ndv or tb + tc > ndt for vb, tb, vc, tc in meshes):
            meshes = [struct.unpack_from("<IIBB", data, at + 12 * i) for i in range(ndm)]
        at += 12 * ndm
        dverts = [struct.unpack_from("<3f", data, at + 12 * i) for i in range(ndv)]
        at += 12 * ndv
        tris = [struct.unpack_from("<4H", data, at + 8 * i) for i in range(ndt)]
        for i in range(min(np_, ndm)):
            vb, tb, vc, tc = meshes[i]
            own, count = polys[i]
            if vb + vc > ndv or tb + tc > ndt:
                continue
            out = []
            for t in range(tb, tb + tc):
                corners = []
                for k in tris[t][:3]:
                    if k < count:
                        corners.append(verts[own[k]])
                    elif vb + k - count < ndv:
                        corners.append(dverts[vb + k - count])
                if len(corners) == 3:
                    out.append(tuple(corners))
            if out:
                detail[base + i] = out
        base += np_
    return detail


def height_at(nm, p, x, z):
    """The mesh's height under (x, z) on a polygon: on its detail triangle there, else the polygon's own plane."""
    for a, b, c in getattr(nm, "detail", {}).get(p.index, ()):
        d = (b[2] - c[2]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[2] - c[2])
        if abs(d) < 1e-9:
            continue
        u = ((b[2] - c[2]) * (x - c[0]) + (c[0] - b[0]) * (z - c[2])) / d
        v = ((c[2] - a[2]) * (x - c[0]) + (a[0] - c[0]) * (z - c[2])) / d
        if u >= -1e-3 and v >= -1e-3 and u + v <= 1.001:
            return u * a[1] + v * b[1] + (1 - u - v) * c[1]
    return nm.height_on(p, x, z)


def nearest(nm, pos, reach):
    """The walk mesh point nearest pos in space, within reach: (polygon, point, distance) or None. (navmesh.locate
    prefers a polygon straight over or under the place, which on a building's storeys is the wrong floor.)"""
    x, y, z = pos
    best = None
    seen = set()
    for gx in range(int(math.floor((x - reach) / 4)), int(math.floor((x + reach) / 4)) + 1):
        for gz in range(int(math.floor((z - reach) / 4)), int(math.floor((z + reach) / 4)) + 1):
            for p in nm.grid.get((gx, gz), ()):
                if p.index in seen:
                    continue
                seen.add(p.index)
                if nm._inside(p, x, z):
                    at = (x, height_at(nm, p, x, z), z)
                else:
                    at, _ = nm._closest_on(p, pos)
                d = math.sqrt((at[0] - x) ** 2 + (at[1] - y) ** 2 + (at[2] - z) ** 2)
                if d <= reach and (best is None or d < best[2]):
                    best = (p.index, at, d)
    return best


def closure(nm, start, forward=True, reverse=None):
    """Every polygon a walker starting on these polygons can come to (walking, and links the way they go); with
    forward False, every polygon from which these can be come to."""
    seen = set(start)
    stack = list(start)
    while stack:
        cur = stack.pop()
        nexts = [link[0] for link in nm.polys[cur].links] if forward else reverse[cur]
        for nxt in nexts:
            if nxt not in seen:
                seen.add(nxt)
                stack.append(nxt)
    return seen


def reversed_links(nm):
    back = [[] for _ in nm.polys]
    for p in nm.polys:
        for link in p.links:
            back[link[0]].append(p.index)
    return back


def rim(nm, polys):
    """Points along the open edges of a set of polygons (edges no neighbour shares): [(point, polygon)]."""
    out = []
    for i in polys:
        p = nm.polys[i]
        joined = set()
        for link in p.links:
            if len(link) == 3:
                joined.add((round(link[1][0], 2), round(link[1][2], 2), round(link[2][0], 2), round(link[2][2], 2)))
        n = len(p.verts)
        for e in range(n):
            a, b = p.verts[e], p.verts[(e + 1) % n]
            # an edge wholly shared with a neighbour is inside; one shared in part (across tiles) still has open ends
            if (round(a[0], 2), round(a[2], 2), round(b[0], 2), round(b[2], 2)) in joined:
                continue
            length = math.hypot(b[0] - a[0], b[2] - a[2])
            steps = max(1, int(length / .25))
            for s in range(steps + 1):
                t = s / steps
                out.append(((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t), i))
    return out


def closest_pair(side_a, side_b, within=1e9, polys=False):
    """The two rim points, one of each side, that are nearest across the floor (height counts double, so the same
    storey is preferred): (a, b, flat distance, rise), or None when none are within `within`. With polys, the two
    polygons they lie on as well."""
    cell = 3.0
    grid = {}
    for point, poly in side_b:
        grid.setdefault((int(math.floor(point[0] / cell)), int(math.floor(point[2] / cell))), []).append((point, poly))
    best = None
    for ring in (1, 3, 8, 30, 120):
        for point, poly in side_a:
            cx, cz = int(math.floor(point[0] / cell)), int(math.floor(point[2] / cell))
            for gx in range(cx - ring, cx + ring + 1):
                for gz in range(cz - ring, cz + ring + 1):
                    for other, other_poly in grid.get((gx, gz), ()):
                        flat = math.hypot(other[0] - point[0], other[2] - point[2])
                        score = flat + 2.0 * abs(other[1] - point[1])
                        if best is None or score < best[0]:
                            best = (score, point, other, flat, other[1] - point[1], poly, other_poly)
        if (best is not None and best[0] <= ring * cell) or ring * cell >= within:
            break
    if best is None or best[0] > within:
        return None
    if polys:
        return (best[1], best[2], best[3], best[4]), best[5], best[6]
    return best[1], best[2], best[3], best[4]


def near_pairs(side_a, side_b, within):
    """Pairs of rim points, one of each side, no farther apart than `within` across the floor, nearest first, one for
    each half metre of the first side's rim: [((a, b, flat, rise), polygon a, polygon b)]. The nearest pair of two
    islands is often through the wall between them; the door is a little farther along."""
    cell = max(within, 1.0)
    grid = {}
    for point, poly in side_b:
        grid.setdefault((int(math.floor(point[0] / cell)), int(math.floor(point[2] / cell))), []).append((point, poly))
    best = {}
    for point, poly in side_a:
        cx, cz = int(math.floor(point[0] / cell)), int(math.floor(point[2] / cell))
        found = None
        for gx in range(cx - 1, cx + 2):
            for gz in range(cz - 1, cz + 2):
                for other, other_poly in grid.get((gx, gz), ()):
                    flat = math.hypot(other[0] - point[0], other[2] - point[2])
                    if flat > within:
                        continue
                    score = flat + 2.0 * abs(other[1] - point[1])
                    if found is None or score < found[0]:
                        found = (score, (point, other, flat, other[1] - point[1]), poly, other_poly)
        if found is None:
            continue
        key = (round(point[0] * 2), round(point[1]), round(point[2] * 2))
        if key not in best or found[0] < best[key][0]:
            best[key] = found
    return [(pair, pa, pb) for score, pair, pa, pb in sorted(best.values(), key=lambda f: f[0])]


# ---- the scene's boxes ----
class Box:
    __slots__ = ("name", "centre", "half", "rot", "inv", "lo", "hi")

    def __init__(self, name, centre, dims, rot):
        self.name, self.centre, self.rot = name, centre, rot
        self.half = [d * .5 for d in dims]
        self.inv = [-rot[0], -rot[1], -rot[2], rot[3]]
        corners = []
        for sx in (-1, 1):
            for sy in (-1, 1):
                for sz in (-1, 1):
                    w = unity_scene.qrot(rot, [sx * self.half[0], sy * self.half[1], sz * self.half[2]])
                    corners.append([centre[i] + w[i] for i in range(3)])
        self.lo = [min(c[i] for c in corners) for i in range(3)]
        self.hi = [max(c[i] for c in corners) for i in range(3)]

    def away(self, point):
        """How far the point is from the box (0 inside)."""
        local = unity_scene.qrot(self.inv, [point[i] - self.centre[i] for i in range(3)])
        return math.sqrt(sum(max(abs(local[i]) - self.half[i], 0.0) ** 2 for i in range(3)))


def scene_parts(path):
    """The scene's solid boxes, and the links a build laid by hand (NavMeshLink: world start, world end, width, both ways)."""
    objs, docs = unity_scene.load(path)
    boxes, hops = [], []
    for o in objs.values():
        if not getattr(o, "active", True):
            continue
        for kind, body in o.comps:
            if kind == "BoxCollider" and unity_scene.enabled(body) and not unity_scene.is_trigger(body):
                size = unity_scene._vec(body, rb"m_Size", 3) or [1, 1, 1]
                centre = unity_scene._vec(body, rb"m_Center", 3) or [0, 0, 0]
                c = unity_scene.qrot(o.wrot, [centre[i] * o.wscale[i] for i in range(3)])
                boxes.append(Box(o.name, [o.wpos[i] + c[i] for i in range(3)], [abs(size[i] * o.wscale[i]) for i in range(3)], o.wrot))
            elif kind == "MonoBehaviour" and b"::NavMeshLink" in body and unity_scene.enabled(body):
                start = unity_scene._vec(body, rb"m_StartPoint", 3)
                end = unity_scene._vec(body, rb"m_EndPoint", 3)
                if start is None or end is None:
                    continue
                def world(local):
                    r = unity_scene.qrot(o.wrot, [local[i] * o.wscale[i] for i in range(3)])
                    return tuple(o.wpos[i] + r[i] for i in range(3))
                m = re.search(rb"m_Width: ([-\d.eE]+)", body)
                both = re.search(rb"m_Bidirectional: (\d)", body)
                hops.append((o.name, world(start), world(end), float(m.group(1)) if m else 0.0, both is None or both.group(1) == b"1"))
    return boxes, hops


class Solids:
    def __init__(self, boxes):
        self.boxes = boxes
        self.grid = {}
        for k, b in enumerate(boxes):
            # the ground and other wide slabs go in a list of their own
            if (b.hi[0] - b.lo[0]) * (b.hi[2] - b.lo[2]) > 900:
                self.grid.setdefault(None, []).append(k)
                continue
            for gx in range(int(math.floor(b.lo[0] / 4)), int(math.floor(b.hi[0] / 4)) + 1):
                for gz in range(int(math.floor(b.lo[2] / 4)), int(math.floor(b.hi[2] / 4)) + 1):
                    self.grid.setdefault((gx, gz), []).append(k)

    def near(self, point, reach):
        out = set(self.grid.get(None, ()))
        for gx in range(int(math.floor((point[0] - reach) / 4)), int(math.floor((point[0] + reach) / 4)) + 1):
            for gz in range(int(math.floor((point[2] - reach) / 4)), int(math.floor((point[2] + reach) / 4)) + 1):
                out.update(self.grid.get((gx, gz), ()))
        return [self.boxes[k] for k in out]

    def touching(self, point, radius):
        return [b for b in self.near(point, radius) if all(b.lo[i] - radius <= point[i] <= b.hi[i] + radius for i in range(3)) and b.away(point) < radius]

    def holding(self, point):
        """The boxes a point is inside of."""
        return [b for b in self.near(point, .1) if all(b.lo[i] <= point[i] <= b.hi[i] for i in range(3)) and b.away(point) <= 0.0]


def between(solids, a, b, slid=False):
    """What stands between two places of the walk mesh for a body going straight from one to the other: the boxes a
    body of BODY round would meet (name, top over the floor it leaves), and the widest body that meets none (the
    way's clear width there, m, capped at 2)."""
    flat = math.hypot(b[0] - a[0], b[2] - a[2])
    steps = max(2, int(flat / .05))
    floor = min(a[1], b[1])

    def meets(radius):
        hit = {}
        # a ball rolled along the way at each height of the body's band, never lower than rests on the floor
        low = max(BODY_LOW, radius + .03)
        for s in range(steps + 1):
            t = s / steps
            x, z = a[0] + (b[0] - a[0]) * t, a[2] + (b[2] - a[2]) * t
            ground = a[1] + (b[1] - a[1]) * t
            h = low
            while True:
                for box in solids.touching((x, ground + h, z), radius):
                    hit[id(box)] = box
                if h >= BODY_HIGH - 1e-6:
                    break
                h = min(BODY_HIGH, h + .25)
        return list(hit.values())

    blockers = meets(BODY)
    if blockers and slid:
        return blockers, None
    if blockers:
        return [(box.name, box.hi[1] - floor, box) for box in blockers], None
    if not slid and flat > 1e-3:
        # the two rim points seldom face each other at the middle of an opening: the same line slid sideways, and the
        # widest of them is the opening's width
        side = ((b[2] - a[2]) / flat, 0.0, -(b[0] - a[0]) / flat)
        widest = 0.0
        for k in range(-12, 13):
            off = k * .05
            got = between(solids, tuple(a[i] + side[i] * off for i in range(3)), tuple(b[i] + side[i] * off for i in range(3)), True)
            if not got[0] and got[1] is not None:
                widest = max(widest, got[1])
        return [], widest
    low, high = BODY, 1.0
    if not meets(high):
        return [], high * 2
    for _ in range(10):
        mid = (low + high) * .5
        if meets(mid):
            high = mid
        else:
            low = mid
    return [], low * 2


def opening(solids, a, b):
    """The open way between two places of the walk mesh, across it: (unit vector across, how far it is free to one
    side, how far to the other), each from the line a-b, at the narrowest place along it and at any height of a body."""
    flat = math.hypot(b[0] - a[0], b[2] - a[2])
    if flat < 1e-3:
        return None
    across = ((b[2] - a[2]) / flat, 0.0, -(b[0] - a[0]) / flat)
    steps = max(2, int(flat / .1))
    free = [3.0, 3.0]
    for s in range(steps + 1):
        t = s / steps
        x, z, ground = a[0] + (b[0] - a[0]) * t, a[2] + (b[2] - a[2]) * t, a[1] + (b[1] - a[1]) * t
        for side in (0, 1):
            sign = 1.0 if side == 0 else -1.0
            reach = 0.0
            while reach < free[side]:
                hit = False
                h = BODY_LOW
                while h <= BODY_HIGH + 1e-6 and not hit:
                    hit = bool(solids.touching((x + across[0] * sign * reach, ground + h, z + across[2] * sign * reach), .02))
                    h += .25
                if hit:
                    break
                reach += .01
            free[side] = min(free[side], reach)
    return across, free[0], free[1]


def voxels_left(solids, a, b, radius, voxel):
    """How many voxels wide the walk mesh would be between two places once a bake with this agent has trimmed the
    opening they face each other through: its two sides put on the bake's grid (a voxel a side touches is lost), less
    the radius in whole voxels off each. None: no opening there to measure."""
    got = opening(solids, a, b)
    if got is None:
        return None
    across, plus, minus = got
    if plus >= 3.0 and minus >= 3.0:
        return 99
    trim = int(math.ceil(radius / voxel - 1e-3))
    mid = ((a[0] + b[0]) * .5, (a[2] + b[2]) * .5)
    for axis, index in ((0, 0), (2, 1)):
        if abs(abs(across[axis]) - 1.0) < 1e-3:
            # the sides lie along the world's own axis: where they fall on the grid is known
            sign = 1.0 if across[axis] > 0 else -1.0
            low, high = mid[index] - (minus if sign > 0 else plus), mid[index] + (plus if sign > 0 else minus)
            return int(math.floor(high / voxel + 1e-4)) - int(math.ceil(low / voxel - 1e-4)) - 2 * trim
    # a way that runs askew of the grid: a voxel lost at each side for sure
    return int(math.floor((plus + minus) / voxel)) - 2 - 2 * trim


def describe(solids, pair):
    """A break in words: where, how far across, and what is there."""
    a, b, flat, rise = pair
    where = "(%.1f, %.1f, %.1f) ~ (%.1f, %.1f, %.1f)" % (a[0], a[1], a[2], b[0], b[1], b[2])
    if abs(rise) > STEP + .3 and flat < 4.0:
        return "LEVELS", "높이가 %.1f m 다름 %s: 그 사이를 걸어 오르내릴 길(계단·경사)이 메시에 없음" % (abs(rise), where)
    blockers, width = between(solids, a, b)
    if not blockers:
        if flat < 6.0:
            return "NARROW", "열려 있는데 메시가 끊김 %s: 사이 %.2f m, 몸이 지나갈 폭 %.2f m" % (where, flat, width)
        return "FAR", "메시 사이가 %.1f m 떨어져 있고 그 사이에 상자는 없음 %s (바닥이 없거나 메시 콜라이더)" % (flat, where)
    tall = max(top for name, top, box in blockers)
    names = sorted({name for name, top, box in blockers})
    listed = ", ".join(names[:4]) + (" 외 %d" % (len(names) - 4) if len(names) > 4 else "")
    if tall <= STEP:
        return "LOWSTEP", "발 높이(%.2f m)의 것 %s: %s. 걷기 메시가 넘어갈 높이인데 끊김" % (tall, where, listed)
    if tall <= LOW_THING:
        return "OVER", "넘어야 하는 것(바닥에서 %.2f m) %s: %s. 좀비의 걸음(%.2f m)보다 높아 메시가 끊김" % (tall, where, listed, STEP)
    return "WALL", "막힘(바닥에서 %.1f m) %s: %s" % (tall, where, listed)


# ---- a chapter ----
class Chapter:
    def __init__(self, name, without=()):
        self.name = name
        path = flow.SCENES + name + ".unity"
        self.spots, self.points = flow.campaign_points(path)
        self.steps = flow.steps_of(name)
        self.nm = NavMesh(flow.SCENES + name + "/NavMesh.asset")
        self.nm.detail = load_detail(self.nm, flow.SCENES + name + "/NavMesh.asset")
        boxes, self.hops = scene_parts(path)
        self.left_out = [b for b in boxes if any(word in b.name for word in without)]
        self.solids = Solids([b for b in boxes if not any(word in b.name for word in without)])
        self.lay_hops()
        self.isle, self.count = islands(self.nm)
        self.back = reversed_links(self.nm)
        self.findings = []
        self.groups = None

    def lay_hops(self):
        """The links a build laid by hand join the polygons at their two ends (as the engine joins them when the
        scene loads): added to the walk mesh's own links, one way or both."""
        self.hops_laid = 0
        for name, start, end, width, both in self.hops:
            a, b = nearest(self.nm, start, 1.0), nearest(self.nm, end, 1.0)
            if a is None or b is None or a[0] == b[0]:
                continue
            self.nm.polys[a[0]].links.append((b[0], start, start, end))
            if both:
                self.nm.polys[b[0]].links.append((a[0], end, end, start))
            self.hops_laid += 1

    def stand(self, pos, reach):
        return nearest(self.nm, pos, reach)

    def open_breaks(self):
        """Every place where two islands face each other across an open way on one level (nothing of the scene
        between them at body height): [(pair, polygon before, polygon after)]. A door, a landing, a gap between props."""
        nm = self.nm
        members = {}
        for p in nm.polys:
            members.setdefault(self.isle[p.index], []).append(p.index)
        rims = {}
        for key, polys in members.items():
            pts = rim(nm, polys)
            if pts:
                rims[key] = (pts, (min(q[0][0] for q in pts), min(q[0][2] for q in pts), max(q[0][0] for q in pts), max(q[0][2] for q in pts)))
        keys = sorted(rims)
        out = []
        for i, key in enumerate(keys):
            pts, box = rims[key]
            for other in keys[i + 1:]:
                opts, obox = rims[other]
                if obox[0] > box[2] + OPEN_BREAK or obox[2] < box[0] - OPEN_BREAK or obox[1] > box[3] + OPEN_BREAK or obox[3] < box[1] - OPEN_BREAK:
                    continue
                # the nearest place where the two face each other with nothing between (not the nearest of all, which
                # is often through the wall beside the door)
                for pair, pa, pb in near_pairs(pts, opts, OPEN_BREAK)[:80]:
                    if abs(pair[3]) > .3 or pair[2] < .05:
                        continue
                    blockers, width = between(self.solids, pair[0], pair[1])
                    if not blockers and self.floored(pair[0], pair[1]):
                        out.append((pair, pa, pb))
                        break
        return out

    def floored(self, a, b):
        """Whether there is floor all the way between two places (a box's top just under the line): a way, not a gap."""
        flat = math.hypot(b[0] - a[0], b[2] - a[2])
        steps = max(2, int(flat / .1))
        for s in range(steps + 1):
            t = s / steps
            under = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t - .25, a[2] + (b[2] - a[2]) * t)
            if not self.solids.touching(under, .2):
                return False
        return True

    def predict(self, radius, voxel, hop_names, w):
        """The walk mesh as a rebuild with this agent would join it, as far as today's mesh and the scene's boxes
        tell: open breaks wide enough are joined, links are laid over the boxes named. Says what it did."""
        nm = self.nm
        breaks = self.open_breaks()
        if self.left_out:
            w("  (예측) 빼고 봄: %s\n" % ", ".join("%s %d개" % (n, sum(1 for b in self.left_out if b.name == n)) for n in sorted({b.name for b in self.left_out})))
        # the same count at the agent the mesh was baked with (Unity's voxel then: a third of the radius)
        baked = 0
        for pair, pa, pb in breaks:
            if (voxels_left(self.solids, pair[0], pair[1], nm.radius, nm.radius / 3.0) or 0) >= 2:
                baked += 1
                w("  (모델 점검) 지금의 굽기로도 이어져야 한다고 나옴: (%.1f, %.1f, %.1f) ~ (%.1f, %.1f, %.1f) (곁에 메시 콜라이더가 있으면 그것을 읽지 못한 것)\n" % (pair[0] + pair[1]))
        joined = thin = shut = 0
        for pair, pa, pb in breaks:
            left = voxels_left(self.solids, pair[0], pair[1], radius, voxel)
            where = "(%.1f, %.1f, %.1f) ~ (%.1f, %.1f, %.1f)" % (pair[0] + pair[1])
            if left is not None and left >= 2:
                joined += 1
                nm.polys[pa].links.append((pb, pair[0], pair[1]))
                nm.polys[pb].links.append((pa, pair[1], pair[0]))
            elif left == 1:
                thin += 1
                w("  (예측) 한 칸만 남음, 이어진다고 보지 않음: %s\n" % where)
            else:
                shut += 1
                w("  (예측) 여전히 끊김 (복셀 %s칸): %s\n" % (left, where))
        laid = 0
        for name in hop_names:
            for box in self.solids.boxes:
                if name not in box.name:
                    continue
                # across its thinner side, from the floor before it to the floor past it, as GreyboxBuilder.Hop lays it
                x_thin = (box.hi[0] - box.lo[0]) <= (box.hi[2] - box.lo[2])
                thinness = (box.hi[0] - box.lo[0]) if x_thin else (box.hi[2] - box.lo[2])
                reach = thinness * .5 + (math.ceil(radius / voxel - 1e-3) + 1) * voxel + .15
                cx, cz, floor = (box.lo[0] + box.hi[0]) * .5, (box.lo[2] + box.hi[2]) * .5, box.lo[1]
                start = (cx - reach, floor, cz) if x_thin else (cx, floor, cz - reach)
                end = (cx + reach, floor, cz) if x_thin else (cx, floor, cz + reach)
                a, b = nearest(nm, start, 1.0), nearest(nm, end, 1.0)
                if a is None or b is None or a[0] == b[0]:
                    w("  (예측) 넘는 길을 놓지 못함: %s (%.1f, %.1f, %.1f)\n" % (box.name, cx, floor, cz))
                    continue
                nm.polys[a[0]].links.append((b[0], start, start, end))
                nm.polys[b[0]].links.append((a[0], end, end, start))
                laid += 1
        w("  (예측) 열린 끊김 %d곳 가운데 반지름 %.2f m·복셀 %.2f m로 이어지는 곳 %d, 한 칸만 남는 곳 %d, 끊긴 채인 곳 %d; 넘는 길 %d개\n"
          % (len(breaks), radius, voxel, joined, thin, shut, laid))
        w("  (모델 점검) 지금 구운 반지름 %.2f m에서 모델이 '이어진다'고 보는 끊김: %d곳 (0이어야 모델이 지금의 굽기와 맞음)\n" % (nm.radius, baked))
        self.isle, self.count = islands(nm)
        self.back = reversed_links(nm)
        self.groups = None

    def breaks(self, start, goal):
        """The places where the way from one polygon to another is broken, in the order a walker meets them. Of all the
        ways from island to island of the walk mesh (free where a link leads; else across the place where two
        islands come nearest), the one that costs least, a gap costing by what it is: an open narrow way or a low
        thing little, a difference of height or a wide gap much, a wall most of all (so it is named only when nothing
        else leads there). Each: (rim point before, rim point after, flat distance, rise, kind, words)."""
        nm = self.nm
        if self.groups is None:
            members = {}
            for p in nm.polys:
                members.setdefault(self.isle[p.index], []).append(p.index)
            self.groups = members
            self.group_rim, self.pairs, self.leads = {}, {}, {}
            for p in nm.polys:
                for link in p.links:
                    if len(link) == 4 and self.isle[link[0]] != self.isle[p.index]:
                        self.leads.setdefault(self.isle[p.index], set()).add(self.isle[link[0]])

        def rim_of(key):
            if key not in self.group_rim:
                pts = rim(nm, self.groups[key])
                box = (min(q[0][0] for q in pts), min(q[0][2] for q in pts), max(q[0][0] for q in pts), max(q[0][2] for q in pts)) if pts else None
                self.group_rim[key] = (pts, box)
            return self.group_rim[key]

        def gap(key, other):
            if (key, other) not in self.pairs:
                pts, box = rim_of(key)
                opts, obox = rim_of(other)
                found = None
                if box is not None and obox is not None and not (obox[0] > box[2] + 20 or obox[2] < box[0] - 20 or obox[1] > box[3] + 20 or obox[3] < box[1] - 20):
                    pair = closest_pair(pts, opts, 20.0)
                    if pair is not None:
                        kind, words = describe(self.solids, pair)
                        cost = {"NARROW": pair[2], "LOWSTEP": pair[2], "OVER": pair[2] + 1.0, "LEVELS": 10.0 + 3.0 * abs(pair[3]),
                                "FAR": 10.0 + pair[2], "WALL": 1000.0 + pair[2]}[kind]
                        found = (cost, pair + (kind, words))
                self.pairs[(key, other)] = found
                # the same place the other way round
                if found is not None:
                    a, b, flat, rise, kind, words = found[1]
                    self.pairs.setdefault((other, key), (found[0], (b, a, flat, -rise, kind, words)))
                else:
                    self.pairs.setdefault((other, key), None)
            return self.pairs[(key, other)]

        goal_islands = {self.isle[q] for q in closure(nm, [goal], False, self.back)}
        first = self.isle[start]
        best = {first: 0.0}
        prev = {}
        heap = [(0.0, first)]
        done = set()
        reached = None
        while heap:
            cost, key = heapq.heappop(heap)
            if key in done:
                continue
            done.add(key)
            if key in goal_islands:
                reached = key
                break
            for other in self.leads.get(key, ()):
                if other not in done and cost < best.get(other, 1e18):
                    best[other] = cost
                    prev[other] = (key, None)
                    heapq.heappush(heap, (cost, other))
            for other in self.groups:
                if other in done or other == key:
                    continue
                found = gap(key, other)
                if found is None:
                    continue
                if cost + found[0] < best.get(other, 1e18):
                    best[other] = cost + found[0]
                    prev[other] = (key, found[1])
                    heapq.heappush(heap, (cost + found[0], other))
        chain = []
        while reached is not None and reached in prev:
            reached, pair = prev[reached]
            if pair is not None:
                chain.append(pair)
        chain.reverse()
        return chain

    def legs(self, w):
        nm = self.nm
        w("=== %s: 걷기 메시 %d 폴리곤, 섬 %d개 (걸어서만), 링크 %d개%s, 굽는 반지름 %.2f m, 걸음 %.2f m\n"
          % (self.name, len(nm.polys), self.count, len(nm.links), " + 손으로 놓은 링크 %d개" % self.hops_laid if self.hops else "", nm.radius, nm.climb))
        at_name, at = "Start", self.spots.get("Start")
        route_polys = set()
        for step in self.steps:
            to = self.spots.get(step["spot"])
            if to is None:
                w("  %-9s %-10s %-16s 그 이름의 자리가 씬에 없음\n" % (step["id"], step["kind"], step["spot"]))
                self.findings.append(("NOSPOT", step["id"], "자리 '%s'가 씬에 없음" % step["spot"]))
                continue
            if at is None:
                at_name, at = step["spot"], to
                continue
            a, b = self.stand(at, SPOT_SEARCH), self.stand(to, SPOT_SEARCH)
            label = "%-9s %-10s %s -> %s" % (step["id"], step["kind"], at_name, step["spot"])
            if a is None or b is None:
                off = at_name if a is None else step["spot"]
                w("  %s: 자리 '%s'의 %.0f m 안에 걷기 메시가 없음\n" % (label, off, SPOT_SEARCH))
                self.findings.append(("OFFMESH", step["id"], "자리 '%s' 둘레 %.0f m 안에 걷기 메시가 없음" % (off, SPOT_SEARCH)))
            else:
                route_polys.add(a[0])
                route_polys.add(b[0])
                if math.dist(at, to) < .05 or self.isle[a[0]] == self.isle[b[0]]:
                    w("  %s: 걸어서 이어짐\n" % label)
                else:
                    there, back = b[0] in closure(nm, [a[0]]), a[0] in closure(nm, [b[0]])
                    if there and back:
                        w("  %s: 링크(내려뛰기·건너뛰기·넘기)로 양쪽 다 이어짐\n" % label)
                    elif there:
                        w("  %s: 앞으로만 이어짐(내려뛰기·건너뛰기). 되돌아오는 길은 없음\n" % label)
                    else:
                        w("  %s: 이어지지 않음%s\n" % (label, " (거꾸로는 이어짐)" if back else ""))
                        chain = self.breaks(a[0], b[0])
                        if not chain:
                            w("      끊긴 곳을 찾지 못함 (20 m 안에 맞닿는 메시가 없음)\n")
                            self.findings.append(("FAR", step["id"], "%s -> %s: 맞닿는 곳을 찾지 못함" % (at_name, step["spot"])))
                        for pair in chain:
                            kind, words = pair[4], pair[5]
                            w("      끊긴 곳: %s\n" % words)
                            self.findings.append((kind, step["id"], "%s -> %s: %s" % (at_name, step["spot"], words)))
            at_name, at = step["spot"], to
        # The route's ground: wherever someone who stood at a spot can get to on foot or by dropping (the squad comes
        # down off a roof or through a floor's hole between two spots; the game's own route follows the walk mesh
        # from each spot as far as it goes, SpawnRoute.Join).
        ground = set()
        for poly in route_polys:
            if poly not in ground:
                ground |= closure(nm, [poly])
        self.route_polys = sorted(ground)
        self.route_islands = {self.isle[p] for p in ground}

    def spawn_points(self, w):
        nm = self.nm
        to_route = closure(nm, self.route_polys, False, self.back)
        route_rim = None
        w("  -- 스폰 지점 %d개\n" % len(self.points))
        for name, pos in self.points:
            held = self.solids.holding((pos[0], pos[1] + 1.0, pos[2]))
            got = self.stand(pos, POINT_SEARCH)
            if held:
                names = ", ".join(sorted({b.name for b in held})[:3])
                w("  %-16s 단단한 것 속에 있음: %s (%.1f, %.1f, %.1f)\n" % (name, names, pos[0], pos[1], pos[2]))
                self.findings.append(("POINT-IN", name, "단단한 것 속: %s (%.1f, %.1f, %.1f)" % (names, pos[0], pos[1], pos[2])))
                continue
            if got is None:
                by = sorted({b.name for b in self.solids.touching((pos[0], pos[1] + 1.0, pos[2]), 1.2) if b.hi[1] > pos[1] + .3})
                what = "곁에 선 것: " + ", ".join(by[:3]) if by else "곁에 선 상자는 없음"
                w("  %-16s 걷기 메시 밖 (%.0f m 안에 메시 없음) (%.1f, %.1f, %.1f). %s\n" % (name, POINT_SEARCH, pos[0], pos[1], pos[2], what))
                self.findings.append(("POINT-OFF", name, "걷기 메시 밖 (%.1f, %.1f, %.1f). %s" % (pos[0], pos[1], pos[2], what)))
                continue
            poly, at, d = got
            if poly in to_route:
                how = "경로의 땅 위" if self.isle[poly] in self.route_islands else "경로로 내려뛰거나 건너뛰어 감"
                w("  %-16s %s%s\n" % (name, how, "" if d < .6 else " (메시까지 %.1f m)" % d))
                continue
            if route_rim is None:
                route_rim = rim(nm, to_route)
            pair = closest_pair(rim(nm, closure(nm, [poly])), route_rim)
            kind, words = describe(self.solids, pair) if pair else ("FAR", "맞닿는 곳을 찾지 못함")
            w("  %-16s 경로의 어느 곳으로도 못 감. %s\n" % (name, words))
            self.findings.append(("POINT-" + kind, name, "경로로 못 감: " + words))

    def report(self, w, predicted=None):
        if predicted is not None:
            w("--- %s: 예측\n" % self.name)
            self.predict(predicted[0], predicted[1], predicted[2], w)
        self.legs(w)
        self.spawn_points(w)
        tally = {}
        for kind, who, words in self.findings:
            tally[kind] = tally.get(kind, 0) + 1
        w("  # 합계: %s\n\n" % (", ".join("%s %d" % kv for kv in sorted(tally.items())) or "끊긴 곳 없음"))


def main():
    args = sys.argv[1:]
    radius = voxel = None
    hops = []
    without = []
    names = []
    i = 0
    while i < len(args):
        if args[i] == "--radius":
            radius = float(args[i + 1])
            i += 2
        elif args[i] == "--voxel":
            voxel = float(args[i + 1])
            i += 2
        elif args[i] == "--hop":
            hops.append(args[i + 1])
            i += 2
        elif args[i] == "--without":
            without.append(args[i + 1])
            i += 2
        else:
            names.append(args[i])
            i += 1
    names = names or flow.CHAPTERS
    predicted = None
    if radius is not None or hops:
        predicted = (radius if radius is not None else BODY, voxel if voxel is not None else (radius if radius is not None else BODY) / 3.0, hops)
    path = REPO + ("/Logs/walk-joins-predicted.txt" if predicted else "/Logs/walk-joins.txt")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with io.open(path, "w", encoding="utf-8") as out:
        def w(text):
            out.write(text)
            sys.stdout.write(text)
        w("경로와 걷기 메시: 지은 씬과 구운 걷기 메시에서 Unity 없이 읽음 (Tools/Maps/walk_joins.py)%s\n\n"
          % (" - 다시 구웠을 때의 예측" if predicted else ""))
        for name in names:
            Chapter(name, without).report(w, predicted)
    print("wrote", path)


if __name__ == "__main__":
    main()
