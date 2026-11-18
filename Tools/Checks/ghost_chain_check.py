# Checks the Ghost's 암살 연쇄 (assassination chain) without the game: replicas of GhostCards.ChainMomentOf
# (Assets/_Project/Scripts/Classes/Ghost/GhostCards.cs) and ExecutionRules.Timing
# (Assets/_Project/Scripts/Melee/Executions/ExecutionRules.cs), run over the game's own tables:
#   ClassAbilities.csv Ghost.chainWindow, ExecutionRules.csv (killIn, freeAfter), Executions.csv (each pair's own
#   length and kill), Perks.csv ghost_quickknife (the quicker knife).
#   python Tools/Checks/ghost_chain_check.py
# The chain is open from a takedown's start until Ghost.chainWindow seconds after its blow. Until 2026-11-18 it was open
# "while the takedown holds the player", which was the pair's whole length (1.2 to 1.5 s); takedowns then became quick
# (blow at killIn, let go freeAfter later) and the card's time fell with them. This prints, per takedown rule, how long
# the Ghost has to name the next zombie now and how long it had then, and asserts that it is never less than it was,
# that the chain is still open after the takedown has let the Ghost go, and that the rule's moments are what the C#
# says. Change GhostCards.ChainMomentOf / ExecutionRules.Timing and these replicas together.
import csv, io, os

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
TABLES = os.path.join(REPO, "Assets", "_Project", "Data", "Resources", "Tables")
CLOSED, WAITING, OPEN = "Closed", "Waiting", "Open"


def rows_of(*path):
    return list(csv.reader(io.StringIO(open(os.path.join(TABLES, *path), encoding="utf-8-sig").read())))


def number(cell, fallback):
    try:
        return float(cell)
    except (TypeError, ValueError):
        return fallback


def chain_moment(run, left, struck_at, now, window):
    """GhostCards.ChainMomentOf, line for line."""
    if not run or left <= 0:
        return CLOSED
    if struck_at < 0:
        return WAITING
    return OPEN if now - struck_at <= max(0.0, window) else CLOSED


def timing(kill_in, free_after, kill, length, card=1.0):
    """ExecutionRules.Timing: (pace, struck, free, over) for a rule over a pair's own kill and length."""
    length = max(.05, length)
    kill = min(max(0.0, kill), length)
    card = max(.1, card)
    pace = kill / kill_in if kill_in > 0 and kill > 0 else 1.0
    pace = max(.1, pace * card)
    struck, over = kill / pace, length / pace
    free = min(over, struck + free_after / card) if free_after >= 0 else over
    return pace, struck, max(struck, free), over


abilities = {row[0]: row[1] for row in rows_of("Classes", "ClassAbilities.csv")[1:] if len(row) > 1}
WINDOW = number(abilities.get("Ghost.chainWindow"), 1.1)
pairs = {}
table = rows_of("Melee", "Executions.csv")
for row in table[1:]:
    if row and not row[0].startswith("#"):
        d = dict(zip(table[0], row))
        pairs[row[0]] = (d.get("kind", ""), number(d.get("length"), 0), number(d.get("kill"), 0))
quick = [0.0]
for row in rows_of("Classes", "Perks.csv")[1:]:
    if row and row[0] == "ghost_quickknife":
        head = rows_of("Classes", "Perks.csv")[0]
        value = dict(zip(head, row)).get("value", "")
        quick = [0.0] + [number(v, 0) for v in value.split("/")] if "/" in value else [0.0, number(value, 0)]
print("Ghost.chainWindow = %.2f s after the blow | quicker knife (ghost_quickknife) +%s %%" % (WINDOW, "/".join("%g" % q for q in quick[1:])))

# ---- The rule's moments ----
assert chain_moment(False, 2, -1, 0.0, WINDOW) == CLOSED, "no run"
assert chain_moment(True, 0, 0.3, 0.4, WINDOW) == CLOSED, "no chain left"
assert chain_moment(True, 1, -1, 5.0, WINDOW) == WAITING, "before the blow it waits, however long the dash in takes"
assert chain_moment(True, 1, 0.3, 0.3, WINDOW) == OPEN, "open as the blow lands"
assert chain_moment(True, 1, 0.3, 0.3 + WINDOW, WINDOW) == OPEN, "open to the end of its time"
assert chain_moment(True, 1, 0.3, 0.3 + WINDOW + .01, WINDOW) == CLOSED, "closed once its time is out"
print("the rule's moments: ok (closed without a run or a chain left; waiting before the blow; open for the window after it)")

# ---- Each takedown rule: how long the Ghost has to name the next one ----
rules = rows_of("Melee", "ExecutionRules.csv")
head = rules[0]
print("\nrule      | then (held to the pair's end)      | now                                            | change")
print("          | blow   let go = chain's end        | blow   let go   chain's end   after let go      | from the start / after the blow")
worst_total = worst_after = 1e9
for row in rules[1:]:
    if not row or row[0].startswith("#"):
        continue
    d = dict(zip(head, row))
    if d.get("victim") == "broken" or d.get("killIn", "") == "":
        continue                                   # an execution of a broken enemy opens no run of chains
    kind, length, kill = pairs.get(d.get("pair", ""), ("", 0, 0))
    if length <= 0:
        continue
    kill_in, free_after = number(d["killIn"], 0), number(d.get("freeAfter"), -1)
    for card in quick:
        factor = 1 + card / 100
        _, struck, free, _ = timing(kill_in, free_after, kill, length, factor)
        end = struck + WINDOW
        # Then: one clock for "let go" and "the pair ends", played at the card's pace alone.
        then_struck, then_end = kill / factor, length / factor
        existed = kind == "takedown"               # the front takedown came with the quick ones: nothing to compare
        gain_total, gain_after = end - then_end, WINDOW - (then_end - then_struck)
        name = d["id"] + (" +%g%%" % card if card else "")
        if existed:
            worst_total, worst_after = min(worst_total, gain_total), min(worst_after, gain_after)
            print("%-9s | %.2f   %.2f                         | %.2f   %.2f     %.2f          %.2f s still open  | %+.2f s / %+.2f s"
                  % (name, then_struck, then_end, struck, free, end, end - free, gain_total, gain_after))
        else:
            print("%-9s | (no such takedown then)            | %.2f   %.2f     %.2f          %.2f s still open  |" % (name, struck, free, end, end - free))
        # The point of the window: the chain is still open after the takedown has let the Ghost go.
        assert chain_moment(True, 1, struck, free + .05, WINDOW) == (OPEN if WINDOW >= free - struck + .05 else CLOSED)
        # A press before the blow waits for it; one after it goes at once; one past the window is too late.
        assert chain_moment(True, 1, -1, struck * .5, WINDOW) == WAITING
        assert chain_moment(True, 1, struck, end - .01, WINDOW) == OPEN
        assert chain_moment(True, 1, struck, end + .01, WINDOW) == CLOSED

print("\nleast change against then, over the takedowns that existed: %+.2f s from the takedown's start, %+.2f s after the blow"
      % (worst_total, worst_after))
assert worst_total >= -1e-6, "with this window the Ghost has less time from the takedown's start than before takedowns were quick"
assert worst_after >= -1e-6, "with this window the Ghost has less time after the blow than before takedowns were quick"
print("ALL CHECKS PASSED")
