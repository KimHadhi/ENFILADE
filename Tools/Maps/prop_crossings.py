"""The solid props of a built scene (the unseen boxes named "<kind> (충돌)") that stand through the layout's boxes: walls,
slabs, ceilings, parapets. Read from the scene file alone, without Unity, so a rebuild can be checked without playing.
A scene built with the builder's standing rule (GreyboxBuilder.Standing) should print no CROSSES line.
usage: python prop_crossings.py <scene.unity>            every prop, the crossing ones marked
       python prop_crossings.py <scene.unity> x0 x1 y0 y1 z0 z1   every solid box inside a region"""
import math
import os
import sys
sys.path.insert(0, os.path.dirname(__file__))
import unity_scene as us

scene = sys.argv[1]
objs, docs = us.load(scene)
boxes = []
for o in objs.values():
    b = us.box_of(o)
    if b is None:
        continue
    body = o.comp("BoxCollider")
    if not us.enabled(body) or us.is_trigger(body) or not o.active:
        continue
    boxes.append((o, b))
print("solid boxes:", len(boxes))


def corners(c, d, yaw):
    r = math.radians(yaw)
    cs, sn = math.cos(r), math.sin(r)
    hx, hz = d[0] / 2, d[2] / 2
    return [(c[0] + x * cs + z * sn, c[2] - x * sn + z * cs) for x in (-hx, hx) for z in (-hz, hz)]


def aabb(c, d, yaw):
    pts = corners(c, d, yaw)
    xs = [p[0] for p in pts]
    zs = [p[1] for p in pts]
    return min(xs), max(xs), c[1] - d[1] / 2, c[1] + d[1] / 2, min(zs), max(zs)


if len(sys.argv) >= 8:
    x0, x1, y0, y1, z0, z1 = [float(v) for v in sys.argv[2:8]]
    for o, (c, d, yaw, up) in sorted(boxes, key=lambda p: p[1][0][1]):
        a0, a1, b0, b1, c0, c1 = aabb(c, d, yaw)
        if a1 < x0 or a0 > x1 or b1 < y0 or b0 > y1 or c1 < z0 or c0 > z1:
            continue
        print("%-34s x %.2f..%.2f y %.2f..%.2f z %.2f..%.2f yaw %.0f%s" % (o.name, a0, a1, b0, b1, c0, c1, yaw, "" if up else " (tilted)"))
else:
    props = [(o, b) for o, b in boxes if o.name.endswith("(충돌)")]
    walls = [(o, b) for o, b in boxes if not o.name.endswith("(충돌)") and "(못 올라섬)" not in o.name]
    crossing = 0
    for o, (c, d, yaw, up) in sorted(props, key=lambda p: (p[0].name, p[1][0][0])):
        x0, x1, y0, y1, z0, z1 = aabb(c, d, yaw)
        hits = []
        for w, (wc, wd, wyaw, wup) in walls:
            if not wup:
                continue
            a0, a1, b0, b1, c0, c1 = aabb(wc, wd, wyaw)
            ox, oy, oz = min(x1, a1) - max(x0, a0), min(y1, b1) - max(y0, b0), min(z1, c1) - max(z0, c0)
            if ox > 0.03 and oy > 0.03 and oz > 0.03:
                hits.append((min(ox, oy, oz), w.name))
        crossing += 1 if hits else 0
        flag = " ".join("%s(%.2f)" % (n, depth) for depth, n in sorted(hits, reverse=True)[:4])
        print("%-22s at (%.2f, %.2f, %.2f) size %.2fx%.2fx%.2f yaw %.0f %s" % (o.name, c[0], c[1] - d[1] / 2, c[2], d[0], d[1], d[2], yaw,
                                                                              ("CROSSES " + flag) if hits else ""))
    print("props:", len(props), "layout boxes:", len(walls), "crossing:", crossing)
