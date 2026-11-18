# What each step of each chapter would have to draw on under Tables/SpawnRules, read without the editor: a squad
# standing together at places along the step's stretch, every spawn point judged as the game's SpawnPick.Judge judges
# it, but for what cannot be read here:
#   - sight: walls are not read, so "fit" is the most there could be (a seen point is always refused in the game);
#   - which way anyone faces: a point is counted twice, as it would do in front of the player it goes for ("fit") and
#     as it would do even behind their back ("any way": its walk is at least the rule's behind);
#   - the walk mesh as a run carves it (a shut gate, a door still standing): the baked mesh is read as it is.
# Cleared is taken at its worst: every open point the route has passed within clearWithin of, on its floor, up to
# where the squad stands, counts as cleared (as if the squad looked at each and found it empty). Rooms never clear.
#   python Tools/Maps/sim.py [chapter scene name ...]      -> Logs/spawn-sim.txt
# A place where nothing fits and nothing lies farther is written NOTHING: there the horde would wait.
import sys, os, io

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from flow import Flow, d3, table, model, REPO, CHAPTERS

EVERY = 8.0  # the squad is stood every this many metres along a stretch
COLUMNS = ("from", "cleared", "otherwise", "nearest", "farthest", "behind")


def is_finale(steps, i):
    kind = steps[i]["kind"].lower()
    return kind == "escape" or kind == "hold" and i + 1 < len(steps) and steps[i + 1]["kind"].lower() == "escape"


def rule_for(rows, chapter, step, finale):
    """SpawnRules.For: the first row of the chain that writes a cell says it."""
    by_id = {r["id"].lower(): r for r in rows}
    chain = [step["id"]] + ([chapter + ".Finale"] if finale else []) + [chapter + "." + step["kind"]] + (["Finale"] if finale else []) + [step["kind"], "Default"]
    out = {}
    for column in COLUMNS:
        for row in chain:
            cell = by_id.get(row.lower(), {}).get(column, "")
            if cell != "":
                out[column] = cell.lower()
                break
    for column in ("nearest", "farthest", "behind"):
        out[column] = float(out[column])
    return out


def judge(point, front, rule):
    """SpawnPick.Judge without sight (InView, Seen) and the ground taken as good; Behind is left to the caller."""
    if rule["from"] == "ahead" and point["flow"] <= front:
        return "NotAhead"
    if rule["cleared"] == "shut" and point["cleared"]:
        return "Cleared"
    if point["near"] < rule["nearest"]:
        return "TooNear"
    if point["walk"] < 0:
        return "NoWay"
    return "TooFar" if point["walk"] > rule["farthest"] else "Fit"


def main():
    names = sys.argv[1:] or CHAPTERS
    rows = table("SpawnRules")
    within = model("clearWithin", 15.0)
    path = REPO + "/Logs/spawn-sim.txt"
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with io.open(path, "w", encoding="utf-8") as out:
        out.write("What each step could draw on under Tables/SpawnRules, the squad stood together every %.0f m along its stretch (Tools/Maps/sim.py).\n" % EVERY)
        out.write("fit = points that would do in front of the player they go for; 'any way' = of those, the ones that would do even behind their back. "
                  "Sight is not read. Cleared at its worst: every open point the route passed within %.0f m of.\n\n" % within)
        for name in names:
            f = Flow(name)
            # the flow at which the route first comes within clearWithin of each open point, on its floor
            seen_at = []
            for rec in f.placed:
                first = None
                if not rec["room"]:
                    for s, place in f.samples:
                        if abs(place[1] - rec["pos"][1]) < f.nm.height and d3(place, rec["pos"]) <= within:
                            first = s
                            break
                seen_at.append(first)
            out.write("=== %s (rooms: %s)\n" % (name, ", ".join(sorted(r["name"] for r in f.placed if r["room"])) or "none"))
            for i, leg in enumerate(f.legs):
                if not leg["has_spot"]:
                    continue
                step = f.steps[i]
                finale = is_finale(f.steps, i)
                rule = rule_for(rows, name, step, finale)
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
                    any_way = 0
                    for n, rec in enumerate(f.placed):
                        st, ln, co = f.nm.path(rec["pos"], at)
                        point = dict(flow=rec["flow"], cleared=seen_at[n] is not None and seen_at[n] <= s + .01, near=d3(rec["pos"], at),
                                     walk=ln if st == "complete" else -1.0)
                        verdict = judge(point, s, rule)
                        tally[verdict] = tally.get(verdict, 0) + 1
                        if verdict == "Fit":
                            back = ln >= rule["behind"]
                            any_way += back
                            fits.append("%s%s(%.0fm%s)" % (rec["name"], "[room]" if rec["room"] else "", ln, "" if back else ", not behind a back"))
                    fit, far = tally.get("Fit", 0), tally.get("TooFar", 0)
                    result = "fit %d (any way %d)" % (fit, any_way) if fit else ("FARTHER %d" % far if far and rule["otherwise"] == "farther" else "NOTHING")
                    if worst is None or (fit, any_way, far) < worst[0]:
                        worst = ((fit, any_way, far), result)
                    lines.append("    at flow %6.1f: %-20s %s   %s" % (s, result, " ".join("%s %d" % kv for kv in sorted(tally.items())), " ".join(fits[:6])))
                out.write("  %-9s %-10s%s rule %s/%s/%s  worst place: %s\n"
                          % (step["id"], step["kind"], " finale" if finale else "", rule["from"], rule["cleared"], rule["otherwise"], worst[1] if worst else "-"))
                for line in lines:
                    out.write(line + "\n")
            out.write("\n")
            print(name, "done")
    print("wrote", path)


if __name__ == "__main__":
    main()
