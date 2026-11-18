# The mission's flow per chapter, read without the editor from the scene files and the baked walk mesh: the route
# (Start, then each step's spot in order) laid over the walk mesh, every spawn point's place on it, and what is worth
# a look in it (FINDING lines). A twin of the game's SpawnRoute (Lay, Place) for reading the maps when Unity is shut;
# the game's own numbers come from the engine's path search and differ by a metre or few (compare Logs/spawns.txt,
# which the game writes itself when a chapter starts in the editor).
#   python Tools/Maps/flow.py [chapter scene name ...]      -> Logs/spawn-flow.txt
# What it cannot know: what a run carves out of the walk mesh (shut gates, blockers of a run's variant, fire).
import sys, os, re, math, csv, io

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from navmesh import NavMesh
import unity_scene

REPO = os.path.normpath(os.path.join(HERE, "..", "..")).replace("\\", "/")
SCENES = REPO + "/Assets/_Project/Scenes/Campaign/"
TABLES = REPO + "/Assets/_Project/Data/Resources/Tables/Modes/"
CHAPTERS = ["Ch1_Rooftops", "Ch2_Overpass", "Ch3_Subway", "Ch4_Airport"]
STRIDE = 3  # SpawnRoute.Stride


def d3(a, b):
    return math.sqrt(sum((a[i] - b[i]) ** 2 for i in range(3)))


def table(name):
    rows = list(csv.reader(io.open(TABLES + name + ".csv", encoding="utf-8-sig")))
    head = rows[0]
    return [dict(zip(head, r + [""] * (len(head) - len(r)))) for r in rows[1:] if r and not r[0].startswith("#")]


def model(column, fallback):
    for row in table("SpawnRules"):
        if row["id"] == "Default" and row.get(column, "") != "":
            return float(row[column])
    return fallback


def steps_of(chapter):
    out = [r for r in table("CampaignSteps") if r["chapter"].lower() == chapter.lower()]
    out.sort(key=lambda r: int(float(r["order"] or 0)))
    return out


def rooms_of(chapter):
    return {r["point"] for r in table("SpawnPoints") if r["chapter"].lower() == chapter.lower() and r["role"].lower() == "room"}


def campaign_points(path):
    """The scene's CampaignRun as the builder filled it: its spots by name and its spawn points in order, each with its
    world position (unity_scene reads the scene)."""
    objs, docs = unity_scene.load(path)
    by_transform = {o.transform: o for o in objs.values()}
    for o in objs.values():
        for kind, body in o.comps:
            if kind != "MonoBehaviour" or b"\n  spots:" not in body or b"\n  spawns:" not in body:
                continue

            def refs(key):
                m = re.search(rb"^  " + key + rb":\r?\n((?:  - \{fileID: -?\d+\}\r?\n)*)", body, re.M)
                return [int(x) for x in re.findall(rb"fileID: (-?\d+)", m.group(1))] if m else []
            spots = {by_transform[f].name: tuple(by_transform[f].wpos) for f in refs(b"spots") if f in by_transform}
            points = [(by_transform[f].name, tuple(by_transform[f].wpos)) for f in refs(b"spawns") if f in by_transform]
            return spots, points
    raise SystemExit("no CampaignRun with spots and spawns in " + path)


class Flow:
    def __init__(self, chapter):
        self.chapter = chapter
        self.grain = model("grain", 2.0)
        self.reach = model("reach", 80.0)
        self.spots, self.points = campaign_points(SCENES + chapter + ".unity")
        self.rooms = rooms_of(chapter)
        self.nm = NavMesh(SCENES + chapter + "/NavMesh.asset")
        self.steps = steps_of(chapter)
        self.lay_route()
        self.place_points()

    # ---- the route (SpawnRoute.Lay) ----
    def lay_route(self):
        nm = self.nm
        at = self.spots.get("Start")
        self.legs = []
        line, owner = [], []
        begun = at is not None
        if begun:
            line.append(at)
            owner.append(0)
        for i, st in enumerate(self.steps):
            to = self.spots.get(st["spot"])
            leg = dict(step=st["id"], kind=st["kind"], spot=st["spot"], has_spot=to is not None, joined=False, walk=0.0, gap=0.0)
            if to is None:
                self.legs.append(leg)
                continue
            if not begun:
                begun = leg["joined"] = True
                line.append(to)
                owner.append(i)
            elif d3(at, to) < .05:
                leg["joined"] = True
            else:
                status, length, corners = nm.path(at, to, 3.0, 3.0)
                last = at
                if status != "none":
                    for c in corners:
                        if d3(c, last) < .05:
                            continue
                        line.append(c)
                        owner.append(i)
                        last = c
                    leg["walk"] = length
                    leg["joined"] = status == "complete"
                if d3(to, last) >= .05:
                    if not leg["joined"]:
                        leg["gap"] = d3(last, to)
                    line.append(to)
                    owner.append(i)
            at = to
            self.legs.append(leg)
        cum = [0.0]
        self.samples = []  # (flow, place)
        ends = [0.0] * len(self.legs)
        flow = 0.0
        for k in range(1, len(line)):
            a, b = line[k - 1], line[k]
            seg = d3(a, b)
            n = max(1, int(math.ceil(seg / self.grain)))
            for j in range(n):
                t = j / n
                self.samples.append((flow + seg * t, tuple(a[i] + (b[i] - a[i]) * t for i in range(3))))
            flow += seg
            ends[owner[k]] = flow
        if line:
            self.samples.append((flow, line[-1]))
        self.length = flow
        before = 0.0
        for i, leg in enumerate(self.legs):
            leg["start"] = before
            leg["end"] = max(before, ends[i])
            before = leg["end"]

    def leg_of(self, flow):
        last = -1
        for i, leg in enumerate(self.legs):
            if not (leg["has_spot"] and leg["end"] - leg["start"] > .01):
                continue
            last = i
            if flow <= leg["end"] + .001:
                return i
        return max(0, last)

    # ---- a spawn point's place (SpawnRoute.Place with no hint: Weigh, Shortest, Round, Crossing) ----
    def place(self, pos, stride=STRIDE):
        nm = self.nm
        count = len(self.samples)
        away = [d3(pos, s[1]) for s in self.samples]
        on_mesh = nm.locate(pos, 2.0, 1.5) is not None
        walks = {}       # a weighed place -> the walk to it, negative when out of reach or not joined
        searches = [0]

        def weigh(i):
            if i in walks:
                return
            if away[i] > self.reach:
                walks[i] = -1.0
                return
            searches[0] += 1
            st, ln, co = nm.path(pos, self.samples[i][1])
            walks[i] = ln if st == "complete" else -1.0

        def shortest(low, high):
            best = -1
            for i in range(low, high + 1):
                if walks.get(i, -1.0) >= 0 and (best < 0 or walks[i] < walks[best]):
                    best = i
            return best

        def round_(index):
            if index < 0:
                return index
            low, high = max(0, index - stride + 1), min(count - 1, index + stride - 1)
            for i in range(low, high + 1):
                weigh(i)
            return shortest(low, high)

        def crossing(nearest):
            most = walks[nearest] + self.grain
            chosen = run = -1
            for i in range(count + 1):
                if i < count and i not in walks:
                    continue
                if i < count and 0 <= walks[i] <= most:
                    if run < 0 or walks[i] < walks[run]:
                        run = i
                    continue
                if run >= 0 and (chosen < 0 or self.samples[run][0] > self.samples[chosen][0]):
                    chosen = run
                run = -1
            return chosen

        best = -1
        if on_mesh:
            for i in range(0, count, stride):
                weigh(i)
            weigh(count - 1)
            best = round_(shortest(0, count - 1))
            if best >= 0:
                best = round_(crossing(best))
        by_walk = best >= 0
        if best < 0:
            level = [i for i in range(count) if abs(self.samples[i][1][1] - pos[1]) < nm.height]
            best = min(level or range(count), key=lambda i: away[i])
        flow = self.samples[best][0]
        return dict(flow=flow, off=walks[best] if by_walk else away[best], by_walk=by_walk, on_mesh=on_mesh, leg=self.leg_of(flow), searches=searches[0])

    def place_points(self):
        self.placed = []
        for name, pos in self.points:
            rec = self.place(pos)
            rec.update(name=name, pos=pos, room=name in self.rooms)
            self.placed.append(rec)

    # ---- the report ----
    def report(self, w):
        w("=== %s: route %.0f m over %d steps, %d places %.1f m apart, %d spawn points, walk mesh %d polygons\n"
          % (self.chapter, self.length, len(self.steps), len(self.samples), self.grain, len(self.points), len(self.nm.polys)))
        for leg in self.legs:
            if not leg["has_spot"]:
                w("  %-9s %-10s %-16s NO SPOT of that name in the scene\n" % (leg["step"], leg["kind"], leg["spot"]))
                continue
            w("  %-9s %-10s %-16s flow %6.1f -> %6.1f  walk %6.1f  gap %5.1f%s\n"
              % (leg["step"], leg["kind"], leg["spot"], leg["start"], leg["end"], leg["walk"], leg["gap"], "" if leg["joined"] else "  NOT JOINED by the walk mesh"))
        w("POINTS (%d path searches)\n" % sum(r["searches"] for r in self.placed))
        for rec in sorted(self.placed, key=lambda r: r["flow"]):
            leg = self.legs[rec["leg"]]
            how = "by walk" if rec["by_walk"] else ("in a STRAIGHT line (the walk mesh joins it to no part of the route)" if rec["on_mesh"] else "in a STRAIGHT line (OFF the walk mesh)")
            w("  %-18s flow %6.1f  stretch %-9s %-10s %-5s %5.1f m from the route %s\n"
              % (rec["name"], rec["flow"], leg["step"], leg["kind"], "room" if rec["room"] else "open", rec["off"], how))
        for i, leg in enumerate(self.legs):
            if not leg["has_spot"]:
                w("FINDING %s: no spot '%s' in the scene, so the route skips it\n" % (leg["step"], leg["spot"]))
                continue
            if not leg["joined"]:
                w("FINDING %s: the walk mesh does not join it to the spot before it (%.1f m straight across): no zombie walks that way\n" % (leg["step"], leg["gap"]))
            if i + 1 < len(self.legs) and not any(r["flow"] > leg["end"] for r in self.placed):
                w("FINDING %s: no spawn point lies ahead of its spot\n" % leg["step"])
        for rec in self.placed:
            if not rec["by_walk"]:
                w("FINDING point %s: the walk mesh joins it to no part of the route within %.0f m: it can only send to someone standing on its own island\n" % (rec["name"], self.reach))
        w("\n")


def main():
    names = sys.argv[1:] or CHAPTERS
    path = REPO + "/Logs/spawn-flow.txt"
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with io.open(path, "w", encoding="utf-8") as out:
        out.write("The mission's flow, read from the scene files and the baked walk mesh without the editor (Tools/Maps/flow.py).\n\n")
        for name in names:
            Flow(name).report(out.write)
            print(name, "done")
    print("wrote", path)


if __name__ == "__main__":
    main()
