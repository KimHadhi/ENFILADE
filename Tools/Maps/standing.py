"""The builder's rule for where a solid prop may stand (GreyboxBuilder.Standing.cs), ported line for line, to run it on
a built scene's boxes without Unity: what the next rebuild will move, refuse or leave crossing."""
import math
import os
import sys
sys.path.insert(0, os.path.dirname(__file__))
import unity_scene as us

TOUCH, SUNK, WALL_GAP, WALL_STEP, TRIES = .03, .1, .02, .1, 6


class Block:
    def __init__(self, name, centre, size, yaw, prop=False):
        self.name, self.c, self.size, self.yaw, self.prop = name, centre, size, yaw, prop
        r = math.radians(yaw)
        c, s = abs(math.cos(r)), abs(math.sin(r))
        self.min_y, self.max_y = centre[1] - size[1] / 2, centre[1] + size[1] / 2


def overlap(a, ha, yaw_a, b, hb, yaw_b):
    ra, rb = math.radians(yaw_a), math.radians(yaw_b)
    ax, az = (math.cos(ra), -math.sin(ra)), (math.sin(ra), math.cos(ra))
    bx, bz = (math.cos(rb), -math.sin(rb)), (math.sin(rb), math.cos(rb))
    between = (a[0] - b[0], a[1] - b[1])
    least, free = float("inf"), (0.0, 0.0)
    dot = lambda u, v: u[0] * v[0] + u[1] * v[1]
    for axis in (ax, az, bx, bz):
        reach_a = ha[0] * abs(dot(axis, ax)) + ha[1] * abs(dot(axis, az))
        reach_b = hb[0] * abs(dot(axis, bx)) + hb[1] * abs(dot(axis, bz))
        apart = dot(between, axis)
        depth = reach_a + reach_b - abs(apart)
        if depth <= TOUCH:
            return None
        if depth >= least:
            continue
        least = depth
        k = depth if apart >= 0 else -depth
        free = (axis[0] * k, axis[1] * k)
    return free


class World:
    def __init__(self):
        self.blocks = []
        self.ways = []
        self.log = []

    def keep_clear(self, name, x0, x1, y0, y1, z0, z1):
        self.ways.append((name, ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (abs(x1 - x0), abs(y1 - y0), abs(z1 - z0))))

    def crosses(self, floor, half, height, yaw, me=None, as_prop=True):
        what, free, deepest = None, (0.0, 0.0), 0.0
        bottom, top = floor[1], floor[1] + height
        centre, own = (floor[0], floor[2]), (half[0], half[2])
        for other in self.blocks:
            if other is me:
                continue
            under = max(bottom + SUNK, (bottom + top) / 2) if other.prop and as_prop else bottom + SUNK
            if other.max_y <= under or other.min_y >= top - TOUCH:
                continue
            way = overlap(centre, own, yaw, (other.c[0], other.c[2]), (other.size[0] / 2, other.size[2] / 2), other.yaw)
            if way is None:
                continue
            m = way[0] ** 2 + way[1] ** 2
            if m <= deepest:
                continue
            deepest, free, what = m, way, other.name
        for name, c, size in self.ways:
            if not as_prop:
                break
            if c[1] + size[1] / 2 <= bottom + TOUCH or c[1] - size[1] / 2 >= top - TOUCH:
                continue
            way = overlap(centre, own, yaw, (c[0], c[2]), (size[0] / 2, size[2] / 2), 0.0)
            if way is None:
                continue
            m = way[0] ** 2 + way[1] ** 2
            if m <= deepest:
                continue
            deepest, free, what = m, way, name
        return what, free

    def stand(self, kind, floor, half, yaw, asked, moved, band=True, cap=False):
        height = half[1] * 2
        if not cap and band and 1.5 < height < 2.05:
            height = 1.5 if height < 1.65 else 2.05
        b = Block(kind + " (충돌)", (floor[0], floor[1] + height / 2, floor[2]), (half[0] * 2, height, half[2] * 2), yaw, True)
        self.blocks.append(b)
        self.log.append(("moved" if moved else "placed", kind, asked, floor, moved, b, half, yaw))
        return floor

    def prop_solid(self, kind, floor, size, yaw=0.0, cap=False):
        half = (size[0] / 2, size[1] / 2, size[2] / 2)
        asked, at, why = floor, list(floor), None
        farthest = max(half[0], half[2]) * 2
        for _ in range(TRIES + 1):
            what, free = self.crosses(at, half, half[1] * 2, yaw)
            if what is None:
                return self.stand(kind, tuple(at), half, yaw, asked, why, cap=cap)
            why = why or what
            at[0] += free[0]
            at[2] += free[1]
            if (at[0] - asked[0]) ** 2 + (at[2] - asked[2]) ** 2 > farthest ** 2:
                break
        self.log.append(("refused", kind, asked, asked, why, None, half, yaw))
        return None

    def furnish(self, kind, room, wall, size, along=.5, cap=False, turn=0.0):
        """room = (x0, x1, y0, y1, z0, z1); wall in W E S N."""
        x0, x1, y0, y1, z0, z1 = room
        along_x = wall in "SN"
        yaw = {"W": 90.0, "E": 270.0, "S": 0.0, "N": 180.0}[wall] + turn
        half = (size[0] / 2, size[1] / 2, size[2] / 2)
        r = math.radians(yaw)
        c, s = abs(math.cos(r)), abs(math.sin(r))
        turned = (size[0] * c + size[2] * s, size[1], size[0] * s + size[2] * c)
        width, depth = (turned[0], turned[2]) if along_x else (turned[2], turned[0])
        length, deep = (x1 - x0, z1 - z0) if along_x else (z1 - z0, x1 - x0)
        off = depth / 2 + WALL_GAP
        held = {"W": x0 + off, "E": x1 - off, "S": z0 + off, "N": z1 - off}[wall]
        low = (x0 if along_x else z0) + width / 2
        high = (x1 if along_x else z1) - width / 2
        wanted = low + (max(low, high) - low) * min(1, max(0, along))
        at = (lambda on: (on, y0, held)) if along_x else (lambda on: (held, y0, on))
        if width > length or depth + WALL_GAP > deep or turned[1] > y1 - y0:
            self.log.append(("refused", kind, at(wanted), at(wanted), "bigger than the room %s vs %s" % (turned, (x1 - x0, y1 - y0, z1 - z0)), None, half, yaw))
            return None
        in_the_way, away = None, 0.0
        while away <= high - low + WALL_STEP:
            for sign in ((1,) if away == 0 else (1, -1)):
                on = wanted + away * sign
                if on < low - 1e-4 or on > high + 1e-4:
                    continue
                what, _ = self.crosses(at(on), half, turned[1], yaw)
                if what is not None:
                    in_the_way = in_the_way or what
                    continue
                return self.stand(kind, at(on), half, yaw, at(wanted), in_the_way if away > 0 else None, cap=cap)
            away += WALL_STEP
        self.log.append(("refused", kind, at(wanted), at(wanted), "no free place on the wall (%s)" % in_the_way, None, half, yaw))
        return None

    def audit(self):
        bad = []
        for entry in self.log:
            if entry[0] == "refused":
                continue
            _, kind, asked, floor, moved, block, half, yaw = entry
            what, free = self.crosses(floor, half, half[1] * 2, yaw, block)
            if what:
                bad.append((kind, floor, what, math.hypot(*free)))
        return bad


def scene_blocks(path, keep=lambda o, box: True):
    """The layout's solid boxes of a built scene (upright ones: ramps and the caps' ridges are not blocks)."""
    objs, _ = us.load(path)
    out = []
    for o in objs.values():
        body = o.comp("BoxCollider")
        if body is None or not o.active or not us.enabled(body) or us.is_trigger(body):
            continue
        box = us.box_of(o)
        centre, dims, yaw, upright = box
        if not upright or not keep(o, box):
            continue
        out.append((o, Block(o.name, centre, dims, yaw, o.name.endswith("(충돌)"))))
    return out
