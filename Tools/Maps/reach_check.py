"""Every Reach step's spot of a built campaign scene against the rule the game uses for "someone got there"
(CampaignRun.Reach.cs: within the step's radius, and nothing solid between the player's chest and the spot's): is the
spot's own point buried in a box, and from how much of the floor round it is it in the open. Boxes only (mesh colliders
are not read, so a bought scene's walls don't count here; the builder checks those with physics when it builds).
usage: python reach_check.py <CampaignSteps.csv> <scene.unity> [...]"""
import csv
import math
import os
import sys
sys.path.insert(0, os.path.dirname(__file__))
import unity_scene as us
import walk_probe as wp

CHEST = 1.2
steps_file = sys.argv[1]
rows = list(csv.reader(open(steps_file, encoding="utf-8-sig")))
head = rows[0]
col = {name: i for i, name in enumerate(head)}


def segment_hits(solids, a, b):
    """The first box a segment from a to b enters, or None."""
    d = [b[i] - a[i] for i in range(3)]
    length = math.sqrt(sum(v * v for v in d))
    if length < 1e-6:
        return None
    best = None
    for s in solids:
        o = s.local(a)
        dl = us.qrot(s.qi, d)
        t0, t1 = 0.0, 1.0
        ok = True
        for i in range(3):
            if abs(dl[i]) < 1e-12:
                if abs(o[i]) > s.h[i]:
                    ok = False
                    break
                continue
            x, y = (-s.h[i] - o[i]) / dl[i], (s.h[i] - o[i]) / dl[i]
            if x > y:
                x, y = y, x
            t0, t1 = max(t0, x), min(t1, y)
            if t0 > t1:
                ok = False
                break
        if ok and (best is None or t0 < best[0]):
            best = (t0, s.name)
    return best


for scene in sys.argv[2:]:
    name = os.path.splitext(os.path.basename(scene))[0]
    objs, _ = us.load(scene)
    solids = wp.load(scene)
    spots = {}
    for o in objs.values():
        if o.parent is not None and "진행 자리" in o.parent.name:
            spots[o.name] = o.wpos
        # a spot on a moving group (the pushed airliner) hangs under that group
    print("==", name, "spots", len(spots), "boxes", len(solids))
    for row in rows[1:]:
        if len(row) <= col["kind"] or row[col["chapter"]] != name or row[col["kind"]] != "Reach":
            continue
        spot = row[col["spot"]]
        radius = float(row[col["radius"]] or 4)
        if spot not in spots:
            found = [o for o in objs.values() if o.name == spot]
            if not found:
                print("  %-8s %-14s NOT IN THE SCENE" % (row[0], spot))
                continue
            spots[spot] = found[0].wpos
        p = spots[spot]
        chest = [p[0], p[1] + CHEST, p[2]]
        buried = [s.name for s in solids if s.distance(chest) < .05]
        clear = total = 0
        blockers = {}
        for ring in (min(radius, 1.0), min(radius, 2.5), radius * .9):
            for k in range(16):
                a = math.radians(k * 22.5)
                feet = [p[0] + math.cos(a) * ring, p[1], p[2] + math.sin(a) * ring]
                y, on = wp.floor_under(solids, feet, up=.5, down=1.0)
                if y is None or abs(y - p[1]) > .6 or wp.blocked(solids, [feet[0], y, feet[2]]):
                    continue        # nobody stands there
                total += 1
                hit = segment_hits(solids, [feet[0], y + CHEST, feet[2]], chest)
                if hit is None:
                    clear += 1
                else:
                    blockers[hit[1]] = blockers.get(hit[1], 0) + 1
        seen = ", ".join("%s x%d" % kv for kv in sorted(blockers.items(), key=lambda kv: -kv[1])[:3])
        print("  %-8s %-14s r %-4g at (%.1f, %.1f, %.1f) %s in the open from %d of %d standing places%s" % (
            row[0], spot, radius, p[0], p[1], p[2], ("BURIED in " + ", ".join(buried[:2])) if buried else "",
            clear, total, (" | hidden by: " + seen) if seen else ""))
