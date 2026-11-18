# Checks the class HUD's flow under the class plate without the game: replicas of DeployStrip.Slot and
# EffectStack.Slot (Assets/_Project/Scripts/UI/Hud/Classes), with the numbers read from the game's own table
# (ClassHud.csv Deploy.* and Effects.*). Run it after changing the table or either layout:
#   python Tools/Checks/class_hud_layout_check.py
# For 0 to 8 kinds on the count line and 1 to 8 timed effects it asserts that no two chips or slots overlap, that none
# leaves the stack's width or goes under the floor, and that the leaning pieces (each at its own angle about its own
# pivot) keep apart at both ends. Change the C# layouts and these replicas together.
import math
import csv, io, os

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
TABLES = os.path.join(REPO, "Assets", "_Project", "Data", "Resources", "Tables")


def rows_of(*path):
    text = open(os.path.join(TABLES, *path), encoding="utf-8-sig").read()
    return list(csv.reader(io.StringIO(text)))


def number(cell, fallback):
    try:
        return float(cell)
    except (TypeError, ValueError):
        return fallback


WIDTH, ABOVE, PLATE_LEAN = 300.0, 112.0, 1.5      # ClassHUD's own: the plate's width, its bottom edge, its lean
_t = {row[0]: row[1] for row in rows_of("Hud", "ClassHud.csv")[1:] if len(row) > 1}
DEPLOY = dict(below=number(_t.get("Deploy.below"), 11), height=number(_t.get("Deploy.height"), 30), chip=number(_t.get("Deploy.chip"), 56),
              chipMost=number(_t.get("Deploy.chipMost"), 92), gap=number(_t.get("Deploy.gap"), 4), lean=number(_t.get("Deploy.lean"), -1))
EFFECTS = dict(below=number(_t.get("Effects.below"), 8), height=number(_t.get("Effects.height"), 18), gap=number(_t.get("Effects.gap"), 3),
               floor=number(_t.get("Effects.floor"), 8), lean=number(_t.get("Effects.lean"), .5))
print("ClassHud.csv:", DEPLOY, EFFECTS)


def deploy_rows(count, width, least, gap):
    per = max(1, int((width + gap) // (least + gap)))
    return 0 if count <= 0 else (count + per - 1) // per


def deploy_slot(i, count, width, least, most, gap, height):
    per = max(1, int((width + gap) // (least + gap)))
    rows = deploy_rows(count, width, least, gap)
    row, col = i // per, i % per
    in_row = per if row < rows - 1 else count - per * (rows - 1)
    chip = min(most, (width - (in_row - 1) * gap) / max(1, in_row))
    return (col * (chip + gap), row * (height + gap), chip, height)


def effect_fit(start, floor, height, gap):
    return max(1, int((start - floor + gap) // (height + gap)))


def effect_slot(i, count, width, start, floor, height, gap):
    fit = effect_fit(start, floor, height, gap)
    columns = max(1, (count + fit - 1) // fit)
    column = (width - (columns - 1) * gap) / columns
    return (i // fit * (column + gap), i % fit * (height + gap), column, height)


def overlap(a, b):
    return a[0] < b[0] + b[2] - 1e-6 and b[0] < a[0] + a[2] - 1e-6 and a[1] < b[1] + b[3] - 1e-6 and b[1] < a[1] + a[3] - 1e-6


def rise(lean):  # how far an end 150 px from the pivot moves up (+) at the RIGHT end
    return WIDTH / 2 * math.sin(math.radians(lean))


worst = 1e9
for kinds in range(0, 9):
    d = DEPLOY
    rows = deploy_rows(kinds, WIDTH, d["chip"], d["gap"])
    chips = [deploy_slot(i, kinds, WIDTH, d["chip"], d["chipMost"], d["gap"], d["height"]) for i in range(kinds)]
    for i in range(kinds):
        assert chips[i][0] >= -1e-6 and chips[i][0] + chips[i][2] <= WIDTH + 1e-6, ("chip leaves the line", kinds, i)
        for j in range(i):
            assert not overlap(chips[i], chips[j]), ("chips overlap", kinds, i, j)
    reach = d["below"] + rows * d["height"] + (rows - 1) * d["gap"] if kinds else 0.0
    top = ABOVE - reach                      # what the effects hang under
    e = EFFECTS
    start = top - e["below"]
    # leaning ends: the piece above the effects is the count line (when it shows) or the plate
    above_lean = d["lean"] if kinds else PLATE_LEAN
    for side in (-1, 1):
        gap_end = (top + side * rise(above_lean)) - (start + side * rise(e["lean"]))
        worst = min(worst, gap_end)
        assert gap_end > 1.0, ("effects touch the piece above at an end", kinds, side, gap_end)
    if kinds:
        for side in (-1, 1):
            gap_end = (ABOVE + side * rise(PLATE_LEAN)) - (ABOVE - d["below"] + side * rise(d["lean"]))
            worst = min(worst, gap_end)
            assert gap_end > 1.0, ("count line touches the plate at an end", kinds, side, gap_end)
    for count in range(1, 9):
        slots = [effect_slot(i, count, WIDTH, start, e["floor"], e["height"], e["gap"]) for i in range(count)]
        for i in range(count):
            s = slots[i]
            assert s[0] >= -1e-6 and s[0] + s[2] <= WIDTH + 1e-6, ("slot leaves the stack", kinds, count, i)
            for j in range(i):
                assert not overlap(s, slots[j]), ("slots overlap", kinds, count, i, j)
            if i < effect_fit(start, e["floor"], e["height"], e["gap"]) * 99:
                bottom = start - (s[1] + s[3])
                # one that fits by the rule stays over the floor; only a stack the rule could not fit (start too low for one) may not
                if start - e["floor"] >= e["height"]:
                    assert bottom >= e["floor"] - 1e-6, ("slot under the floor", kinds, count, i, bottom)
    fit = effect_fit(start, e["floor"], e["height"], e["gap"])
    print("kinds %d: count line %d row(s), reach %5.1f | effects start %5.1f, %d to a column, slot widths for 1..8: %s"
          % (kinds, rows, reach, start, fit, " ".join("%.0f" % effect_slot(0, n, WIDTH, start, e["floor"], e["height"], e["gap"])[2] for n in range(1, 9))))
print("closest two leaning pieces come at an end: %.2f px" % worst)
print("ALL CHECKS PASSED")
