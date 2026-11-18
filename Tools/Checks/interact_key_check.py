# Checks who gets the interact key (F, Player/Interact) without the game: replicas of the pure rule in
# Assets/_Project/Scripts/UI/Shared/InteractKey.cs (Before, Lead, Shown, Step, HeldBy), run over the game's own table
# Assets/_Project/Data/Resources/Tables/Player/InteractKey.csv, and a reading of the scripts themselves.
#   python Tools/Checks/interact_key_check.py
# It asserts
#   1. the table: every row has a place, no two rows share one, the ping is last and "other" just before it;
#   2. the code: every offer a script makes (InteractKey.Offer / Pressed / Held / Leads / Shows) has a row, every row but
#      "other" has a script that offers it, and nothing reads the key but the owner (a screen's own closing key and a
#      prompt that only names the key aside);
#   3. the two things measured on 2026-11-18 (the user's item 8) are gone by the rule itself: a pickup that refuses does
#      not keep F from what stands behind it, and while a Support plans a structure F is the plan's alone;
#   4. the order that was deliberate before there was an owner still holds (a takedown before a pickup and a ping, a
#      pickup before the mission's own prompts, everything before the ping);
#   5. one press is one act, and a key held on the way to something done while held is taken up there;
#   6. for every pair and every three of the table's rows, each taking or refusing: one leader exactly when anything
#      takes the press, it is the first in the table's order, and the prompts shown are the leader's and the refusals
#      before it, never two things that would both take the key.
# Change InteractKey's rule and these replicas together.
import csv, io, itertools, os, re, sys

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
TABLE = os.path.join(REPO, "Assets", "_Project", "Data", "Resources", "Tables", "Player", "InteractKey.csv")
SCRIPTS = os.path.join(REPO, "Assets", "_Project", "Scripts")
OWNER = "UI/Shared/InteractKey.cs"
OTHER, PING = "other", "ping"
INT_MAX = 2147483647


# ---- The table, read as InteractKey.Load reads it ----

def load_orders():
    rows = list(csv.reader(io.StringIO(open(TABLE, encoding="utf-8-sig").read())))
    head = [h.strip() for h in rows[0]]
    at = head.index("order")
    orders, notes, last = {}, {}, 0
    for row in rows[1:]:
        if not row or not row[0].strip() or row[0].startswith("#"):
            assert not row or len(row) == len(head), "the description row must have as many cells as the header: %r" % row[:1]
            continue
        assert len(row) == len(head), "row %s has %d cells for %d columns" % (row[0], len(row), len(head))
        cell = row[at].strip()
        try:
            last = int(float(cell)) if cell else last + 1      # a row with no number comes right after the row above it
        except ValueError:
            last = last + 1
        assert row[0].strip() not in orders, "row written twice: " + row[0]
        orders[row[0].strip()] = last
        notes[row[0].strip()] = row[-1].strip()
    return orders, notes


ORDERS, NOTES = load_orders()


def order_of(name):
    return ORDERS.get(name, ORDERS.get(OTHER, INT_MAX))


# ---- InteractKey's rule, line for line. An offer is (id, order, held, refusal) ----

def offer(name, held=False, refusal=None):
    return (name, order_of(name), held, refusal)


def takes(o):
    return o[3] is None


def before(a, b):
    """InteractKey.Before."""
    return a[1] < b[1] if a[1] != b[1] else a[0] < b[0]


def lead(offers):
    """InteractKey.Lead."""
    at = -1
    for i, o in enumerate(offers):
        if takes(o) and (at < 0 or before(o, offers[at])):
            at = i
    return at


def shown(offers, index):
    """InteractKey.Shown."""
    if index < 0 or index >= len(offers):
        return False
    at = lead(offers)
    if takes(offers[index]):
        return index == at
    return at < 0 or before(offers[index], offers[at])


def step(pressed, down, leader, leader_held, taker, spent):
    """InteractKey.Step: returns (taker, spent)."""
    if pressed:
        return leader, leader is not None and not leader_held
    if not down:
        return None, False
    return taker, spent


def held_by(name, down, taker, spent, leader, leader_held):
    """InteractKey.HeldBy."""
    return down and name is not None and (taker == name if spent else (leader == name and leader_held))


def leader_of(offers):
    at = lead(offers)
    return (offers[at][0], offers[at][2]) if at >= 0 else (None, False)


def who_shows(offers):
    return [o[0] for i, o in enumerate(offers) if shown(offers, i)]


# ---- 1. The table ----

print("the interact key's order (Tables/Player/InteractKey.csv), first to last:")
for name, place in sorted(ORDERS.items(), key=lambda kv: (kv[1], kv[0])):
    print("  %5d  %-20s %s" % (place, name, NOTES[name]))
places = sorted(ORDERS.values())
assert len(set(places)) == len(places), "two rows share one place: the key would go by name between them"
assert PING in ORDERS and OTHER in ORDERS, "the table needs the rows 'ping' (what F does with nothing else to do) and 'other'"
assert ORDERS[PING] == max(places), "the ping is what F does with nothing else to do: it must be the last row"
assert ORDERS[OTHER] == sorted(places)[-2], "'other' (an offer with no row of its own) must come just before the ping"
print("the table: ok (%d rows, each a place of its own, the ping last, 'other' just before it)\n" % len(ORDERS))


# ---- 2. The scripts ----

def scripts():
    for folder, _, files in os.walk(SCRIPTS):
        for name in files:
            if name.endswith(".cs"):
                full = os.path.join(folder, name)
                yield os.path.relpath(full, SCRIPTS).replace("\\", "/"), open(full, encoding="utf-8-sig").read()


SOURCES = dict(scripts())
assert OWNER in SOURCES, "the owner is not where this check expects it: " + OWNER

# Every name a script gives an offer: a constant (const string XOffer = "pickup") or the words themselves.
CONSTS = {}                                                       # (class by file name, constant) -> value
for path, text in SOURCES.items():
    owner_class = os.path.basename(path).split(".")[0]
    for statement in re.findall(r"const\s+string\s+([^;]+);", text):
        for const, value in re.findall(r"(\w+)\s*=\s*\"([^\"]*)\"", statement):
            CONSTS.setdefault((owner_class, const), value)

ASK = re.compile(r"InteractKey\.(Offer|Pressed|Held|Leads|Shows)\(\s*([A-Za-z_][\w.]*|\"[^\"]*\")")
offered, asked, unresolved = {}, {}, []
for path, text in SOURCES.items():
    if path == OWNER:
        continue
    here = os.path.basename(path).split(".")[0]
    for verb, arg in ASK.findall(text):
        if arg.startswith('"'):
            name = arg.strip('"')
        else:
            parts = arg.split(".")
            name = CONSTS.get((parts[-2] if len(parts) > 1 else here, parts[-1]))
            if name is None:
                unresolved.append("%s: InteractKey.%s(%s)" % (path, verb, arg))
                continue
        (offered if verb == "Offer" else asked).setdefault(name, set()).add(path)

problems = []
problems += ["cannot tell which offer this is (name it by a const string of its own class): " + u for u in unresolved]
for name in sorted(set(offered) | set(asked)):
    if name not in ORDERS:
        problems.append("'%s' has no row in InteractKey.csv (%s)" % (name, ", ".join(sorted(offered.get(name, set()) | asked.get(name, set())))))
for name in sorted(asked):
    if name not in offered:
        problems.append("'%s' is asked for (%s) but never offered: its press can never come" % (name, ", ".join(sorted(asked[name]))))
for name in sorted(ORDERS):
    if name != OTHER and name not in offered:
        problems.append("row '%s' has no script that offers it" % name)

# Nobody reads the key but the owner. What may still write "Player/Interact": the owner; the key window's own lists; a
# prompt that only names the key (KeyBindings.Name / Bracketed); a screen reading its own closing key while it holds
# the player's keys (KeyBindings.Pressed(path, screen)), which is InputHold's question and is asked before this one.
KEY = '"Player/Interact"'
NAMES_ONLY = re.compile(r"KeyBindings\.(Name|Bracketed)\(\s*" + re.escape(KEY) + r"\s*\)")
OWN_SCREEN = re.compile(r"KeyBindings\.(Pressed|Held)\(\s*" + re.escape(KEY) + r"\s*,\s*[^)]+\)")
for path, text in sorted(SOURCES.items()):
    if path == OWNER or path.startswith("Core/Settings/KeyBindings"):
        continue
    for number, line in enumerate(text.split("\n"), 1):
        code = line.split("//")[0]
        if KEY not in code:
            continue
        rest = OWN_SCREEN.sub("", NAMES_ONLY.sub("", code))
        if KEY in rest:
            problems.append("%s:%d reads the interact key for itself (ask InteractKey): %s" % (path, number, line.strip()[:110]))

for problem in problems:
    print("PROBLEM  " + problem)
assert not problems, "%d problem(s) in the scripts" % len(problems)
print("the scripts: ok (%d offers made, each with a row and a script; nothing reads the key but InteractKey)" % len(offered))
for name in sorted(offered, key=order_of):
    print("  %-20s %s" % (name, ", ".join(sorted(offered[name]))))
print()


# ---- 3. What was measured on 2026-11-18 ----

FULL = "체력이 가득해요"
BEHIND_A_PICKUP = [n for n in ORDERS if order_of(n) > order_of("pickup") and n != OTHER]
for name in BEHIND_A_PICKUP:
    is_held = name in ("support.mend", "tower.scanner", "detente.raise", "ping")
    standing = [offer("pickup", refusal=FULL), offer(name, held=is_held)]
    who, _ = leader_of(standing)
    assert who == name, "a pickup that refuses keeps F from %s" % name
    assert who_shows(standing) == ["pickup", name] or sorted(who_shows(standing)) == sorted(["pickup", name]), \
        "with a refused pickup in view both the refusal and %s's prompt must show: %s" % (name, who_shows(standing))
    # ... and the same pickup, wanted, comes first as it always did.
    standing = [offer("pickup"), offer(name, held=is_held)]
    assert leader_of(standing)[0] == "pickup" and who_shows(standing) == ["pickup"], "a pickup looked at must come before " + name
print("1) a pickup that refuses (health full, a shield already there) no longer keeps F: with it in view the press goes to")
print("   each of the %d things behind it in turn (%s)," % (len(BEHIND_A_PICKUP), ", ".join(sorted(BEHIND_A_PICKUP, key=order_of))))
print("   its refusal still shows beside the prompt of what F does; wanted, the pickup still comes first.")

# A Support with full health, a medkit at the foot of the barricade: mends (held), and resupplies a friend.
standing = [offer("pickup", refusal=FULL), offer("support.mend", held=True), offer("ping", held=True)]
taker, spent = step(True, True, *leader_of(standing), None, False)
assert taker == "support.mend" and not spent and held_by("support.mend", True, taker, spent, *leader_of(standing))
standing = [offer("pickup", refusal=FULL), offer("support.give"), offer("ping", held=True)]
assert leader_of(standing)[0] == "support.give"

# While a Support holds the class key, F is the plan's: whatever else is offered, in any combination.
others = [n for n in ORDERS if n not in ("support.plan", OTHER)]
for count in (1, 2, 3):
    for group in itertools.combinations(others, count):
        standing = [offer("support.plan")] + [offer(n, held=n in ("revive", "support.mend", "tower.scanner", "detente.raise", "ping")) for n in group]
        who, who_held = leader_of(standing)
        assert who == "support.plan", "with %s offered, F is not the plan's" % (group,)
        taker, spent = step(True, True, who, who_held, None, False)
        assert taker == "support.plan" and spent
        for n in group:                                           # nobody else gets the press, nor the key still down
            assert not (taker == n) and not held_by(n, True, taker, spent, who, who_held)
        assert who_shows(standing) == ["support.plan"]
print("2) while a Support holds the class key F is the plan's alone: against every 1, 2 and 3 of the other %d offers the" % len(others))
print("   press is the plan's, it is spent on it, and no pickup, revive, takedown or ping is told the key is theirs.\n")


# ---- 4. The order that was deliberate ----

def first(a, b):
    assert order_of(a) < order_of(b), "%s must come before %s" % (a, b)


for late in ("pickup", "ping", "revive", "support.give", "support.mend", "detente.raise", "detente.treat", "test.station"):
    first("takedown", late)                 # FieldPickups.Free, PlayerPing.Busy, HandsFree, PlayerRevive.FindDowned
for late in ("pickup", "ping"):
    first("ghost.chain", late)              # GhostCards.TakesInteract (0.9.180)
for late in ("pickup", "ping", "support.give", "support.mend", "detente.raise", "detente.treat"):
    first("revive", late)
for late in [n for n in ORDERS if n.startswith("campaign.") or n.startswith("tower.") or n == "gimmick"]:
    first("pickup", late)                   # "A field pickup looked at has [F] first" (CampaignRun, TowerRun)
    for last in ("support.give", "support.mend", "detente.raise", "detente.treat"):
        first(late, last)                   # "Whatever else Interact would do here goes first" (SupportKit, DetenteKit)
for late in ("support.give", "support.mend", "detente.raise", "detente.treat"):
    first("pickup", late)
    first("ladder.top", late)
first("campaign.button", "campaign.can.hand"); first("campaign.can.hand", "gimmick"); first("gimmick", "campaign.door")
first("campaign.door", "campaign.shop"); first("campaign.shop", "campaign.can.floor")      # CampaignRun.Prompt's own chain
first("tower.scanner", "tower.shop"); first("detente.raise", "detente.treat"); first("support.give", "support.mend")
first("detente.answer", "support.mend"); first("detente.answer", "crate")                  # "an offered treatment's answer goes first"
for name in ORDERS:
    if name != PING:
        first(name, PING)
print("the order that was deliberate before there was an owner still holds (a takedown before a pickup and a ping, a")
print("pickup before the mission's own prompts, those before a class's own acts, everything before the ping).\n")


# ---- 5. One press, one act; a hold taken up ----

# F on a medkit, the key kept down, the barricade behind it: the press was the pickup's and starts no hammer.
standing = [offer("pickup"), offer("support.mend", held=True), offer("ping", held=True)]
taker, spent = step(True, True, *leader_of(standing), None, False)
assert taker == "pickup" and spent
standing = [offer("support.mend", held=True), offer("ping", held=True)]            # the medkit is gone, the key still down
taker, spent = step(False, True, *leader_of(standing), taker, spent)
assert not held_by("support.mend", True, taker, spent, *leader_of(standing)), "a press spent on a pickup started the hammer"
taker, spent = step(False, False, *leader_of(standing), taker, spent)              # let go
taker, spent = step(True, True, *leader_of(standing), taker, spent)                # and pressed again: now the hammer
assert taker == "support.mend" and held_by("support.mend", True, taker, spent, *leader_of(standing))

# F held with nothing else to do (pointing), walking up to a downed friend: the hold is taken up there.
standing = [offer("ping", held=True)]
taker, spent = step(True, True, *leader_of(standing), None, False)
assert taker == "ping" and not spent and held_by("ping", True, taker, spent, *leader_of(standing))
standing = [offer("revive", held=True), offer("ping", held=True)]
taker, spent = step(False, True, *leader_of(standing), taker, spent)
assert held_by("revive", True, taker, spent, *leader_of(standing)) and not held_by("ping", True, taker, spent, *leader_of(standing))

# A hammer at work, a friend goes down beside the barricade: the hold goes to getting them up, and back afterwards.
standing = [offer("support.mend", held=True)]
taker, spent = step(True, True, *leader_of(standing), None, False)
standing = [offer("revive", held=True), offer("support.mend", held=True)]
taker, spent = step(False, True, *leader_of(standing), taker, spent)
assert held_by("revive", True, taker, spent, *leader_of(standing)) and not held_by("support.mend", True, taker, spent, *leader_of(standing))
standing = [offer("support.mend", held=True)]                                      # the friend is up, the key still down
taker, spent = step(False, True, *leader_of(standing), taker, spent)
assert held_by("support.mend", True, taker, spent, *leader_of(standing)), "the hammer did not come back after the friend was got up"

# A takedown comes on offer while the hammer works: it needs a press of its own, and the hammer waits meanwhile.
standing = [offer("takedown"), offer("support.mend", held=True)]
taker, spent = step(False, True, *leader_of(standing), "support.mend", False)
assert not held_by("takedown", True, taker, spent, *leader_of(standing)) and not held_by("support.mend", True, taker, spent, *leader_of(standing))

# A tap shorter than a frame is still given; a press with nothing offered is nobody's and starts nothing later.
standing = [offer("pickup")]
taker, spent = step(True, False, *leader_of(standing), None, False)
assert taker == "pickup"
taker, spent = step(True, True, None, False, None, False)
assert taker is None and not spent
print("one press is one act: a press spent on a pickup starts no hammer with the key still down; a key held on the way to a")
print("downed friend gets them up on arrival; a hammer at work gives way to a friend who goes down and comes back after.\n")


# ---- 6. Every pair and every three of the rows, each taking or refusing ----

rows = [n for n in ORDERS if n != OTHER] + ["a.mod.thing"]            # and one with no row, which stands where 'other' does
cases = 0
for count in (1, 2, 3):
    for group in itertools.combinations(rows, count):
        for refusing in itertools.product((False, True), repeat=count):
            standing = [offer(n, refusal="no" if r else None) for n, r in zip(group, refusing)]
            for order in itertools.permutations(standing):            # whoever's Update ran first, the answer is the same
                order = list(order)
                takers = [o for o in order if takes(o)]
                at = lead(order)
                if not takers:
                    assert at < 0
                    assert sorted(who_shows(order)) == sorted(o[0] for o in order), "every refusal shows when nothing takes the key"
                else:
                    best = min(takers, key=lambda o: (o[1], o[0]))
                    assert order[at] == best, "the leader is not the first in the table's order"
                    showing = who_shows(order)
                    assert sum(1 for o in order if takes(o) and o[0] in showing) == 1, "two things that take the key show at once"
                    for o in order:
                        want = o == best or (not takes(o) and before(o, best))
                        assert (o[0] in showing) == want
                cases += 1
print("%d standings (every 1, 2 and 3 of the rows and one offer without a row, each taking or refusing, said in every" % cases)
print("order): one leader exactly when something takes the key, always the first in the table's order; shown are its")
print("prompt and the refusals before it, never two things that would both take the key.")
print("ALL CHECKS PASSED")
