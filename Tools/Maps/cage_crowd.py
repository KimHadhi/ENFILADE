# A cage's crowd (GimmickCage), measured from a built scene without Unity, and what is left in a scene of the crowds
# a map once laid on the floor. Usage: python Tools/Maps/cage_crowd.py [scene name ...] [--held N]
#   (no names: the four chapters; --held N: a scene built before the cages said who they hold, measured as if each held N)
# For every scene it counts the objects of the removed lying crowds ('잠든 무리 N', the component SleeperNest: none
# after a rebuild). For every cage store it reads the cages' boxes, who they hold and the stretch they are set in,
# works out each crowd's middle and reach as the game does (GimmickCage.AddCrowds: the middle of the box on its floor,
# IN_FROM_BARS in from its sides; change both together), and stands the crowd on the baked walk mesh as the game does
# (CampaignHorde.PlaceCaged: count x 8 tries, each a point in the circle, taken to the walk mesh within POINT_SEARCH,
# on the cage's floor, CAGED_APART from those set before). Not measured here: the body's room (Physics.CheckCapsule)
# and the players' nearness, which are the game's to test where it runs.
import math
import os
import random
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_scene
import navmesh

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SCENES = os.path.join(ROOT, "Assets", "_Project", "Scenes", "Campaign")
CHAPTERS = ["Ch1_Rooftops", "Ch2_Overpass", "Ch3_Subway", "Ch4_Airport"]
# The removed component's script (SleeperNest.cs.meta, deleted 2026-11-18): a scene that still names it was built before.
NEST_GUID = b"d1666d4fb020412182f574cf9c15ddf0"
# GimmickCage.InFromBars; CampaignHorde.PointSearch, CagedApart, the floor band of PlaceCaged; GimmickCage.BiteReach.
IN_FROM_BARS, POINT_SEARCH, CAGED_APART, FLOOR_BAND, BITE_REACH = .9, 2.0, 1.1, 1.2, 1.7
GRID, RUNS = .1, 2000


def boxes(body, key):
    """A Bounds or a list of Bounds under `key:` of a component: [(centre, extent)]."""
    m = re.search(rb"\n  " + key + rb":(.*?)(?=\n  [A-Za-z_]|\Z)", body, re.S)
    if not m:
        return []
    found = re.findall(rb"m_Center: \{x: ([-\d.eE]+), y: ([-\d.eE]+), z: ([-\d.eE]+)\}\s*m_Extent: \{x: ([-\d.eE]+), y: ([-\d.eE]+), z: ([-\d.eE]+)\}", m.group(1))
    return [([float(v) for v in f[:3]], [float(v) for v in f[3:]]) for f in found]


def ints(body, key):
    """An int[] of a component: Unity writes it as one hex string (4 bytes each, little end first) or as a list."""
    m = re.search(rb"\n  " + key + rb":[ \t]*(?:\[\]|([0-9a-fA-F]*))[ \t]*\r?\n((?:  - -?\d+\r?\n)*)", body)
    if not m:
        return None
    if m.group(1):
        raw = bytes.fromhex(m.group(1).decode())
        return [struct.unpack_from("<i", raw, i)[0] for i in range(0, len(raw) - 3, 4)]
    return [int(v) for v in re.findall(rb"  - (-?\d+)", m.group(2))]


def text(body, key):
    m = re.search(rb"\n  " + key + rb":[ \t]*([^\r\n]*)", body)
    return m.group(1).decode("utf-8", "replace").strip() if m else None


def on_mesh(mesh, x, y, z):
    """The walk mesh point the game would take for (x, y, z): under it, or the nearest within POINT_SEARCH. None: none."""
    poly = mesh.locate((x, y, z), POINT_SEARCH, POINT_SEARCH)
    if poly is None:
        return None
    at = mesh.snap((x, y, z), poly)
    if math.hypot(at[0] - x, at[2] - z) > POINT_SEARCH:
        return None
    return at


def measure(mesh, name, centre, extent, count, old=None):
    floor = centre[1] - extent[1]
    middle = (centre[0], floor, centre[2])
    radius = max(0.0, min(extent[0], extent[2]) - IN_FROM_BARS)
    if old is not None:
        middle, radius = old
    inside = lambda p: abs(p[0] - centre[0]) <= extent[0] + 1e-4 and abs(p[2] - centre[2]) <= extent[2] + 1e-4
    # the circle on a grid: how much of it is walk mesh of the cage's own floor, and where the rest would be taken
    under = beside = out = lost = total = 0
    steps = int(radius / GRID) + 1
    for ix in range(-steps, steps + 1):
        for iz in range(-steps, steps + 1):
            x, z = middle[0] + ix * GRID, middle[2] + iz * GRID
            if math.hypot(x - middle[0], z - middle[2]) > radius:
                continue
            total += 1
            at = on_mesh(mesh, x, floor, z)
            if at is None or abs(at[1] - floor) > FLOOR_BAND:
                lost += 1
            elif not inside(at):
                out += 1
            elif math.hypot(at[0] - x, at[2] - z) < .02:
                under += 1
            else:
                beside += 1
    # the game's own way of standing them, many times over
    rng = random.Random(22)
    placed_hist = {}
    escaped = 0
    for _ in range(RUNS):
        taken = []
        for _try in range(count * 8):
            if len(taken) >= count:
                break
            # Random.insideUnitCircle: even over the disc
            r, a = radius * math.sqrt(rng.random()), rng.random() * math.tau
            at = on_mesh(mesh, middle[0] + r * math.cos(a), floor, middle[2] + r * math.sin(a))
            if at is None or abs(at[1] - floor) > FLOOR_BAND:
                continue
            if any(math.hypot(at[0] - t[0], at[2] - t[2]) < CAGED_APART for t in taken):
                continue
            taken.append(at)
            if not inside(at):
                escaped += 1
        placed_hist[len(taken)] = placed_hist.get(len(taken), 0) + 1
    whole = placed_hist.get(count, 0)
    print("  %s: 가운데 (%.2f, %.2f, %.2f) 반경 %.2f m, %d 가둠" % (name, middle[0], middle[1], middle[2], radius, count))
    print("    원 안 %d점(%.1f m 격자): 걷기 메시 바로 위 %d, 옆 메시로 옮겨짐 %d, 칸 밖으로 옮겨짐 %d, 메시 없음 %d"
          % (total, GRID, under, beside, out, lost))
    print("    게임처럼 %d번 세워 봄: %d 모두 선 판 %d (%.1f%%), 덜 선 판 %s, 칸 밖에 선 놈 %d"
          % (RUNS, count, whole, 100.0 * whole / RUNS,
             ", ".join("%d명 %d판" % (k, v) for k, v in sorted(placed_hist.items()) if k != count) or "없음", escaped))
    return middle, radius, whole == RUNS and escaped == 0 and out == 0


def scene_report(name, assume):
    path = os.path.join(SCENES, name + ".unity")
    if not os.path.exists(path):
        print(name + ": 씬 파일이 없어요")
        return True
    raw = open(path, "rb").read()
    nests = raw.count(NEST_GUID)
    objs, _ = unity_scene.load(path)
    named = [o for o in objs.values() if o.name.startswith("잠든 무리") or o.name.startswith("갇힌 무리")]
    print("%s: 지워진 무리 자리 부품(SleeperNest) %d개, 그 이름의 오브젝트 %d개%s"
          % (name, nests, len(named), "" if nests == 0 and not named else "  <- 다시 지으면 0이 됨"))
    good = nests == 0 and not named
    cages = [(o, body) for o in objs.values() for kind, body in o.comps
             if kind == "MonoBehaviour" and b"::GimmickCage" in body]
    if not cages:
        return good
    mesh_path = os.path.join(SCENES, name, "NavMesh.asset")
    if not os.path.exists(mesh_path):
        print("  걷기 메시 파일이 없어 철창 무리를 재지 못함: " + mesh_path)
        return False
    mesh = navmesh.NavMesh(mesh_path)
    for o, body in cages:
        boxes_ = boxes(body, rb"cages")
        held = ints(body, rb"held")
        step = text(body, rb"crowdStep")
        passage = boxes(body, rb"passage")
        if held is None:
            if assume is None:
                print("  철창 '%s': 누가 갇혔는지 적혀 있지 않음(2026-11-18 전에 지은 씬). 칸 %d개. --held N으로 재 볼 수 있음" % (o.name, len(boxes_)))
                good = False
                continue
            print("  철창 '%s': 누가 갇혔는지 적혀 있지 않음(전에 지은 씬). 칸마다 %d명으로 보고 잼" % (o.name, assume))
            held = [assume] * len(boxes_)
        else:
            print("  철창 '%s': 칸 %d개, 갇힌 수 %s, 놓이는 구간 %s" % (o.name, len(boxes_), held, step or "(없음)"))
            if not step:
                print("    놓이는 구간이 비어 있어 게임이 세우지 않음")
                good = False
        for i, (centre, extent) in enumerate(boxes_):
            if i >= len(held) or held[i] <= 0:
                print("  칸 %d: 비워 둠" % (i + 1))
                continue
            middle, radius, ok = measure(mesh, "칸 %d" % (i + 1), centre, extent, held[i])
            good = good and ok
            if passage:
                pc, pe = passage[0]
                # the nearest a caged one can stand to someone in the passage: both on the walk mesh, the passer hugging the bars
                gap = max(abs(middle[0] - pc[0]) - pe[0], 0.0) if abs(middle[0] - pc[0]) > abs(middle[2] - pc[2]) else max(abs(middle[2] - pc[2]) - pe[2], 0.0)
                near = max(gap - radius, 0.0)
                print("    통로 상자 가장자리에서 가장 가까이 설 수 있는 자리까지 %.2f m (철망 사이로 무는 거리 %.1f m)" % (near, BITE_REACH))
    return good


def main():
    args = sys.argv[1:]
    assume = None
    if "--held" in args:
        at = args.index("--held")
        assume = int(args[at + 1])
        del args[at:at + 2]
    good = True
    for name in args or CHAPTERS:
        good = scene_report(name, assume) and good
    print("OK" if good else "CHECK")


if __name__ == "__main__":
    main()
