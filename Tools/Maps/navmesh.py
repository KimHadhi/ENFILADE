# An offline reader of a Unity NavMeshData asset (binary serialized file, Unity 6000.3; tile magic 'DNAV' version 16)
# and a path search over it: enough to measure walk distances without the editor (used by flow.py and sim.py; run it
# on its own for a file's counts: python Tools/Maps/navmesh.py <NavMesh.asset>). NOT the engine's own search: a
# corridor over polygons (A* through portal midpoints) pulled tight by the funnel; the engine's lengths will differ a
# little. Layout found by reading the bytes (2026-11-18): header 72 bytes (magic, version, x, y, userId, polyCount,
# vertCount, detailMeshCount, detailVertCount, detailTriCount, bvNodeCount, bmin[3], bmax[3], bvQuantFactor), verts
# float3, polys 32 bytes (verts u16[6], neis u16[6], flags u32, vertCount u8, area u8, pad 2), detail meshes 12 bytes,
# detail verts float3, detail tris u16[4], bv nodes 16 bytes. After the tiles: hash, build settings, heightmaps,
# height meshes, then the generated links (start float3, end float3, radius, type u16, area u8, direction u8).
import re, struct, math, heapq

EXT = 0x8000


class Poly:
    __slots__ = ("verts", "neis", "tile", "index", "area", "links", "centre", "ymin", "ymax", "box", "tris")

    def __init__(self):
        self.links = []  # (other poly index, portal a, portal b)  a/b = 3D points; for a jump link a == b == its start


class NavMesh:
    def __init__(self, path):
        b = open(path, "rb").read()
        self.polys = []
        offs = [m.start() for m in re.finditer(b"VAND", b)]
        self.tiles = []
        last_end = 0
        for o in offs:
            size = struct.unpack_from("<i", b, o - 4)[0]
            (magic, ver, tx, ty, uid, np_, nv, ndm, ndv, ndt, nbv) = struct.unpack_from("<4sii i I iiiiii", b, o)
            if ver != 16:
                raise SystemExit("tile version %d, this reader knows 16" % ver)
            bmin = struct.unpack_from("<3f", b, o + 44)
            bmax = struct.unpack_from("<3f", b, o + 56)
            at = o + 72
            verts = [struct.unpack_from("<3f", b, at + 12 * i) for i in range(nv)]
            at += 12 * nv
            base = len(self.polys)
            # each polygon's own surface: its detail mesh (12 bytes: vertBase u32, triBase u32, vertCount u16, triCount
            # u16), whose triangles (u16[4]: three corners and flags) name the polygon's corners first and then the
            # tile's detail vertices from vertBase on. A polygon is not flat: one may run from a ramp out onto a floor.
            dm_at = at + 32 * np_
            dv_at = dm_at + 12 * ndm
            dt_at = dv_at + 12 * ndv
            for i in range(np_):
                raw = b[at + 32 * i: at + 32 * i + 32]
                v = struct.unpack_from("<6H6H", raw, 0)
                vc, area = raw[28], raw[29]
                p = Poly()
                p.verts = [verts[v[k]] for k in range(vc)]
                p.neis = list(v[6:6 + vc])
                p.tile = (tx, ty)
                p.index = base + i
                p.area = area
                p.tris = []
                if i < ndm:
                    vert_base, tri_base, extra, tri_count = struct.unpack_from("<IIHH", b, dm_at + 12 * i)
                    for k in range(tri_count):
                        corners = struct.unpack_from("<4H", b, dt_at + 8 * (tri_base + k))[:3]
                        tri = []
                        for c in corners:
                            if c < vc:
                                tri.append(p.verts[c])
                            elif c - vc < extra:
                                tri.append(struct.unpack_from("<3f", b, dv_at + 12 * (vert_base + c - vc)))
                        if len(tri) == 3:
                            p.tris.append(tri)
                self.polys.append(p)
            expect = 72 + 12 * nv + 32 * np_ + 12 * ndm + 12 * ndv + 8 * ndt + 16 * nbv
            if expect != size:
                raise SystemExit("tile at %d: size %d but the layout gives %d" % (o, size, expect))
            self.tiles.append(dict(x=tx, y=ty, bmin=bmin, bmax=bmax, base=base, count=np_))
            last_end = o + size
        # after the last tile: align, its hash, then the build settings
        at = (last_end + 3) & ~3
        at += 16
        (self.agent_type, self.radius, self.height, self.slope, self.climb, self.drop, self.jump, self.min_region) = struct.unpack_from("<i7f", b, at)
        at += 32
        at += 4 * 8  # manualCellSize, cellSize, manualTileSize, tileSize, buildHeightMesh, maxJobWorkers, preserveTilesOutsideBounds, debug flags
        heightmaps = struct.unpack_from("<i", b, at)[0]
        at += 4
        heightmeshes = struct.unpack_from("<i", b, at)[0]
        at += 4
        if heightmaps or heightmeshes:
            raise SystemExit("height maps/meshes present: not handled")
        nlinks = struct.unpack_from("<i", b, at)[0]
        at += 4
        self.raw_links = []
        for i in range(nlinks):
            sx, sy, sz, ex, ey, ez, rad, kind, area, direction = struct.unpack_from("<7fHBB", b, at + 32 * i)
            self.raw_links.append(((sx, sy, sz), (ex, ey, ez), rad, kind, area, direction))
        at += 32 * nlinks
        self.end_of_links = at
        self.file_len = len(b)
        self._finish()

    # ---- building the graph ----
    def _finish(self):
        for p in self.polys:
            n = len(p.verts)
            p.centre = (sum(v[0] for v in p.verts) / n, sum(v[1] for v in p.verts) / n, sum(v[2] for v in p.verts) / n)
            p.ymin = min(v[1] for v in p.verts)
            p.ymax = max(v[1] for v in p.verts)
            p.box = (min(v[0] for v in p.verts), min(v[2] for v in p.verts), max(v[0] for v in p.verts), max(v[2] for v in p.verts))
        # inside a tile
        ext = []  # (poly, edge index, a, b)
        for t in self.tiles:
            for i in range(t["count"]):
                p = self.polys[t["base"] + i]
                n = len(p.verts)
                for e in range(n):
                    nei = p.neis[e]
                    a, c = p.verts[e], p.verts[(e + 1) % n]
                    if nei == 0:
                        continue
                    if nei & EXT:
                        ext.append((p, e, a, c, nei & 0xF))
                    else:
                        p.links.append((t["base"] + nei - 1, a, c))
        # across tiles: two border edges on the same line that overlap, at about the same height
        by_line = {}
        for (p, e, a, c, d) in ext:
            if d in (0, 4):  # an edge along z at a fixed x
                key = ("x", round((a[0] + c[0]) * .5, 2))
            else:
                key = ("z", round((a[2] + c[2]) * .5, 2))
            by_line.setdefault(key, []).append((p, a, c, d))
        self.cross = 0
        for key, edges in by_line.items():
            axis = 2 if key[0] == "x" else 0
            for i in range(len(edges)):
                p, a, c, d = edges[i]
                for j in range(len(edges)):
                    q, a2, c2, d2 = edges[j]
                    if q is p or q.tile == p.tile or d2 != (d + 4) % 8:
                        continue
                    lo = max(min(a[axis], c[axis]), min(a2[axis], c2[axis]))
                    hi = min(max(a[axis], c[axis]), max(a2[axis], c2[axis]))
                    if hi - lo < .01:
                        continue
                    # heights along the shared part
                    def y_at(u, s, e_):
                        span = e_[axis] - s[axis]
                        t_ = 0 if abs(span) < 1e-6 else (u - s[axis]) / span
                        return s[1] + (e_[1] - s[1]) * t_
                    mid = (lo + hi) * .5
                    if abs(y_at(mid, a, c) - y_at(mid, a2, c2)) > self.climb:
                        continue

                    def pt(u):
                        span = c[axis] - a[axis]
                        t_ = 0 if abs(span) < 1e-6 else (u - a[axis]) / span
                        return tuple(a[k] + (c[k] - a[k]) * t_ for k in range(3))
                    # keep the portal in p's own winding order
                    first, second = (pt(lo), pt(hi)) if (a[axis] <= c[axis]) else (pt(hi), pt(lo))
                    p.links.append((q.index, first, second))
                    self.cross += 1
        # generated links (drop down, jump across): from the polygon under the start to the one under the end
        self.links = []
        self.grid = {}
        for p in self.polys:
            for gx in range(int(math.floor(p.box[0] / 4)), int(math.floor(p.box[2] / 4)) + 1):
                for gz in range(int(math.floor(p.box[1] / 4)), int(math.floor(p.box[3] / 4)) + 1):
                    self.grid.setdefault((gx, gz), []).append(p)
        unplaced = 0
        for (s, e, rad, kind, area, direction) in self.raw_links:
            ps, pe = self.locate(s, 1.0, 1.5), self.locate(e, 1.0, 1.5)
            if ps is None or pe is None:
                unplaced += 1
                continue
            self.links.append((ps, pe, s, e, kind, direction))
            # A 4-tuple marks a link: enter at s, come out at e. One way only: every generated link here has direction 0,
            # the drops (type 1) all start higher than they end, and of the jumps (type 2) few have a twin going back.
            self.polys[ps].links.append((pe, s, s, e))
        self.unplaced_links = unplaced

    # ---- queries ----
    @staticmethod
    def _inside(p, x, z):
        n = len(p.verts)
        sign = 0
        for i in range(n):
            a, b = p.verts[i], p.verts[(i + 1) % n]
            cr = (b[0] - a[0]) * (z - a[2]) - (b[2] - a[2]) * (x - a[0])
            if abs(cr) < 1e-7:
                continue
            s = 1 if cr > 0 else -1
            if sign == 0:
                sign = s
            elif s != sign:
                return False
        return True

    @staticmethod
    def _closest_on(p, pos):
        best, bd = None, 1e18
        n = len(p.verts)
        for i in range(n):
            a, b = p.verts[i], p.verts[(i + 1) % n]
            ab = (b[0] - a[0], b[1] - a[1], b[2] - a[2])
            l2 = ab[0] ** 2 + ab[2] ** 2
            t = 0 if l2 < 1e-12 else max(0, min(1, ((pos[0] - a[0]) * ab[0] + (pos[2] - a[2]) * ab[2]) / l2))
            c = (a[0] + ab[0] * t, a[1] + ab[1] * t, a[2] + ab[2] * t)
            d = (c[0] - pos[0]) ** 2 + (c[2] - pos[2]) ** 2
            if d < bd:
                bd, best = d, c
        return best, math.sqrt(bd)

    def height_on(self, p, x, z):
        """The polygon's height under a point: that of the triangle of its detail mesh the point is over. A polygon need
        not be flat (one runs from a ramp out onto the floor it reaches), so the plane of its first three corners was
        metres off under some points (a spawn point on a roof read as off the mesh, a spot on a landing read as on the
        ramp below it)."""
        v = p.verts
        tris = p.tris or [(v[0], v[i], v[i + 1]) for i in range(1, len(v) - 1)]
        nearest, gap = p.centre[1], 1e18
        for (ax, ay, az), (bx, by, bz), (cx, cy, cz) in tris:
            den = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz)
            if abs(den) < 1e-9:
                continue
            wa = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / den
            wb = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / den
            wc = 1 - wa - wb
            h = wa * ay + wb * by + wc * cy
            out = max(0.0, -wa, -wb, -wc)
            if out < 1e-6:
                return h
            if out < gap:  # just outside every triangle (an edge): the one it is least outside of
                gap, nearest = out, h
        return nearest

    def locate(self, pos, reach=2.0, up=1.5):
        """The polygon under or nearest pos: within `reach` m sideways and `up` m of height. Returns its index or None."""
        x, y, z = pos
        best, bd = None, 1e18
        seen = set()
        for gx in range(int(math.floor((x - reach) / 4)), int(math.floor((x + reach) / 4)) + 1):
            for gz in range(int(math.floor((z - reach) / 4)), int(math.floor((z + reach) / 4)) + 1):
                for p in self.grid.get((gx, gz), ()):
                    if p.index in seen:
                        continue
                    seen.add(p.index)
                    if self._inside(p, x, z):
                        dy = abs(self.height_on(p, x, z) - y)
                        if dy <= up and dy < bd:
                            bd, best = dy, p.index
        if best is not None:
            return best
        # not over any polygon: the nearest edge
        for gx in range(int(math.floor((x - reach) / 4)), int(math.floor((x + reach) / 4)) + 1):
            for gz in range(int(math.floor((z - reach) / 4)), int(math.floor((z + reach) / 4)) + 1):
                for p in self.grid.get((gx, gz), ()):
                    c, d = self._closest_on(p, pos)
                    dy = abs(c[1] - y)
                    if d <= reach and dy <= up and d + dy < bd:
                        bd, best = d + dy, p.index
        return best

    def snap(self, pos, poly):
        p = self.polys[poly]
        if self._inside(p, pos[0], pos[2]):
            return (pos[0], self.height_on(p, pos[0], pos[2]), pos[2])
        c, _ = self._closest_on(p, pos)
        return c

    def path(self, start, goal, reach=2.0, up=1.5):
        """(status, length, corners): status 'complete', 'partial' (the corners end at the reachable place nearest
        the goal) or 'none' (start or goal is off the mesh)."""
        ps, pg = self.locate(start, reach, up), self.locate(goal, reach, up)
        if ps is None or pg is None:
            return "none", 0.0, []
        s, g = self.snap(start, ps), self.snap(goal, pg)
        dist = {ps: 0.0}
        entry = {ps: s}
        prev = {}
        heap = [(self._d(s, g), 0.0, ps)]
        closest, closest_h = ps, self._d(s, g)
        done = set()
        while heap:
            f, gcost, cur = heapq.heappop(heap)
            if cur in done:
                continue
            done.add(cur)
            if cur == pg:
                break
            here = entry[cur]
            h = self._d(here, g)
            if h < closest_h:
                closest, closest_h = cur, h
            for link in self.polys[cur].links:
                nxt = link[0]
                if nxt in done:
                    continue
                if len(link) == 4:  # a drop or a jump: walk to its start, come out at its end
                    step = self._d(here, link[1]) + self._d(link[1], link[3])
                    into = link[3]
                else:
                    into = ((link[1][0] + link[2][0]) * .5, (link[1][1] + link[2][1]) * .5, (link[1][2] + link[2][2]) * .5)
                    step = self._d(here, into)
                cost = gcost + step
                if cost < dist.get(nxt, 1e18):
                    dist[nxt] = cost
                    entry[nxt] = into
                    prev[nxt] = (cur, link)
                    heapq.heappush(heap, (cost + self._d(into, g), cost, nxt))
        status = "complete" if pg in done else "partial"
        end_poly = pg if status == "complete" else closest
        end_point = g if status == "complete" else entry[closest]
        # the corridor back to the start
        chain = []
        cur = end_poly
        while cur != ps:
            cur_prev, link = prev[cur]
            chain.append(link)
            cur = cur_prev
        chain.reverse()
        corners = self._pull(s, end_point, chain)
        length = sum(self._d(corners[i], corners[i + 1]) for i in range(len(corners) - 1))
        return status, length, corners

    @staticmethod
    def _d(a, b):
        return math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2)

    def _pull(self, start, end, chain):
        """Funnel over the portals; a drop or jump link cuts the corridor in two (walk to its start, land at its end)."""
        corners = [start]
        seg_start = start
        portals = []
        for link in chain:
            if len(link) == 4:
                corners += self._funnel(seg_start, link[1], portals)[1:]
                corners.append(link[3])
                seg_start = link[3]
                portals = []
            else:
                portals.append((link[1], link[2]))
        corners += self._funnel(seg_start, end, portals)[1:]
        return corners

    @staticmethod
    def _funnel(start, end, portals):
        def tri(a, b, c):
            return (c[0] - a[0]) * (b[2] - a[2]) - (b[0] - a[0]) * (c[2] - a[2])

        def on_segment(p, a, b):
            ux, uz = b[0] - a[0], b[2] - a[2]
            l2 = ux * ux + uz * uz
            t = 0.0 if l2 < 1e-12 else max(0.0, min(1.0, ((p[0] - a[0]) * ux + (p[2] - a[2]) * uz) / l2))
            return (a[0] + ux * t - p[0]) ** 2 + (a[2] + uz * t - p[2]) ** 2 < 1e-4
        # a portal's corners in the walking polygon's winding: which is left and which right is found from the first
        pts = [(start, start)] + list(portals) + [(end, end)]
        out = [start]
        apex = start
        left = right = start
        ai = li = ri = 0
        i = 1
        guard = 0
        while i < len(pts):
            guard += 1
            if guard > 100000:
                break
            a, b = pts[i]
            # A portal the apex stands on (a spot on a tile's border, as many are at z 0) narrows nothing: weighed, its
            # two ends lie in one line with the apex and the funnel shuts to no width, sending the path to a far corner.
            if i < len(pts) - 1 and on_segment(apex, a, b):
                i += 1
                continue
            # polygons here wind clockwise seen from above (Unity, y up, left-handed): the edge's first corner is on the right
            new_right, new_left = b, a
            # right side
            if tri(apex, right, new_right) <= 0:
                if apex == right or tri(apex, left, new_right) > 0:
                    right, ri = new_right, i
                else:
                    out.append(left)
                    apex = left
                    ai = li
                    left = right = apex
                    li = ri = ai
                    i = ai + 1
                    continue
            if tri(apex, left, new_left) >= 0:
                if apex == left or tri(apex, right, new_left) < 0:
                    left, li = new_left, i
                else:
                    out.append(right)
                    apex = right
                    ai = ri
                    left = right = apex
                    li = ri = ai
                    i = ai + 1
                    continue
            i += 1
        out.append(end)
        # drop repeats
        clean = [out[0]]
        for p in out[1:]:
            if NavMesh._d(p, clean[-1]) > 1e-4:
                clean.append(p)
        return clean


if __name__ == "__main__":
    import sys
    nm = NavMesh(sys.argv[1])
    print("tiles", len(nm.tiles), "polys", len(nm.polys), "cross-tile joins", nm.cross, "links", len(nm.raw_links), "unplaced", nm.unplaced_links)
    print("agent radius %.2f height %.2f climb %.2f drop %.1f jump %.1f" % (nm.radius, nm.height, nm.climb, nm.drop, nm.jump))
    print("bytes after links", nm.file_len - nm.end_of_links)
    kinds = {}
    for l in nm.raw_links:
        kinds[(l[3], l[5])] = kinds.get((l[3], l[5]), 0) + 1
    print("link (type, direction) counts", kinds)
