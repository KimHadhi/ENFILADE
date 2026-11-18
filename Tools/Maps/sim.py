# What each step of each chapter would have to draw on under Tables/SpawnRules, read without the editor: a squad
# standing together at places along the step's stretch, every spawn point judged as the game's SpawnPick.Judge judges
# it, but for sight (walls are not read here, so "fit" is the most there could be: a seen point would still be refused
# or outranked). Everything behind the step under way is taken as cleared (the worst case for a rule that keeps
# cleared ground shut), and the walk mesh as baked (what a run carves out of it, a shut gate, is not known here).
#   python Tools/Maps/sim.py [chapter scene name ...]      -> Logs/spawn-sim.txt
# A place where nothing fits and nothing lies farther is written NOTHING: there the horde would wait.
import sys, os, io

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from flow import Flow, d3, table, REPO, CHAPTERS

EVERY = 8.0  # the squad is stood every this many metres along a stretch
COLUMNS = ("from", "cleared", "otherwise", "nearest", "farthest", "behind")


def rule_for(rows, chapter, step):
    by_id = {r["id"]: r for r in rows}
    out = {}
    for column in COLUMNS:
        for row in (step["id"], chapter + "." + step["kind"], step["kind"], "Default"):
            if row in by_id and by_id[row].get(column, "") != "":
                out[column] = by_id[row][column].lower()
                break
    for column in ("nearest", "farthest", "behind"):
        out[column] = float(out[column])
    return out


def judge(point, front, rear, rule):
    """SpawnPick.Judge without sight (InView, Seen) and with the ground taken as good."""
    if rule["from"] == "ahead" and point["flow"] <= front:
        return "NotAhead"
    if rule["cleared"] == "shut" and point["cleared"]:
        return "Cleared"
    if point["near"] < rule["nearest"]:
        return "TooNear"
    if point["walk"] < 0:
        return "NoWay"
    if point["flow"] < rear and point["walk"] < rule["behind"]:
        return "RightBehind"
    return "TooFar" if point["walk"] > rule["farthest"] else "Fit"


def main():
    names = sys.argv[1:] or CHAPTERS
    rows = table("SpawnRules")
    path = REPO + "/Logs/spawn-sim.txt"
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with io.open(path, "w", encoding="utf-8") as out:
        out.write("What each step could draw on under Tables/SpawnRules, the squad stood together every %.0f m along its stretch; sight is not read (Tools/Maps/sim.py).\n\n" % EVERY)
        for name in names:
            f = Flow(name)
            out.write("=== %s\n" % name)
            for i, leg in enumerate(f.legs):
                if not leg["has_spot"]:
                    continue
                step = f.steps[i]
                rule = rule_for(rows, name, step)
                flows = []
                s = leg["start"]
                while s < leg["end"] - .01:
                    flows.append(s)
                    s += EVERY
                flows.append(leg["end"])
                worst, lines = None, []
                for s in flows:
                    k = min(range(len(f.samples)), key=lambda j: abs(f.samples[j][0] - s))
                    at = f.samples[k][1]
                    # a place of the route that is itself off the walk mesh (the straight part over a gap) holds nobody
                    if f.nm.locate(at, 2.0, 1.5) is None:
                        lines.append("    at flow %6.1f: (off the walk mesh: the route crosses a gap here)" % s)
                        continue
                    tally, fits = {}, []
                    for rec in f.placed:
                        st, ln, co = f.nm.path(rec["pos"], at)
                        point = dict(flow=rec["flow"], cleared=rec["leg"] < i, near=d3(rec["pos"], at), walk=ln if st == "complete" else -1.0)
                        verdict = judge(point, s, s, rule)
                        tally[verdict] = tally.get(verdict, 0) + 1
                        if verdict == "Fit":
                            fits.append("%s(%.0fm)" % (rec["name"], ln))
                    fit, far = tally.get("Fit", 0), tally.get("TooFar", 0)
                    result = "fit %d" % fit if fit else ("FARTHER %d" % far if far and rule["otherwise"] == "farther" else "NOTHING")
                    if worst is None or (fit, far) < worst[0]:
                        worst = ((fit, far), result)
                    lines.append("    at flow %6.1f: %-10s %s   %s" % (s, result, " ".join("%s %d" % kv for kv in sorted(tally.items())), " ".join(fits[:6])))
                out.write("  %-9s %-10s rule %s/%s/%s  worst place: %s\n" % (step["id"], step["kind"], rule["from"], rule["cleared"], rule["otherwise"], worst[1] if worst else "-"))
                for line in lines:
                    out.write(line + "\n")
            out.write("\n")
            print(name, "done")
    print("wrote", path)


if __name__ == "__main__":
    main()
