# Checks whose a player's two hands are without the game: a replica of the pure rule in
# Assets/_Project/Scripts/Player/Hands/HandBoard.cs (Beats, Could, Kept, Take, State, Give, Settle, Limiting, Limit), run over the game's own
# table Assets/_Project/Data/Resources/Tables/Player/HandUses.csv, and a reading of the scripts that name its rows.
#   python Tools/Checks/hands_check.py
# It asserts
#   1. the table: its columns, every cell a word the game reads, every stance a state of PlayerMovement.MovementState,
#      no row twice, and the bands the design gives the ranks (Docs/Features/Hands.md 3.2);
#   2. the code: every row the game's own code names (the constants of HandUses.cs) is in the table, and every row of
#      the table is one of them;
#   3. the user's own example (2026-11-18): the right hand aims and fires while the left holds a grenade ready, and a
#      reload asked for meanwhile does not start at all;
#   4. what the user decided that day: first come keeps the hand between a reload and a dose; a repair gives way to a
#      reload and never the other way; V with the left hand elsewhere is no gun bash; a grenade in the fist stays
#      through a landing roll and goes back for a vault, a grab, an execution; and what the game did already and must
#      go on doing: a body gone down keeps its pistol and all the pistol does (a reload too) and nothing else of the
#      hands, and V in a zombie's grip is still the shove, not the kick;
#   5. today's order between the left hand's uses, row by row of the design's table;
#   6. for every pair of rows, asked or stated in either order, the board's answer is the one their ranks give (and
#      the one a row that lets only some families be asked for gives); and through thousands of random runs no ask
#      stands without a hand it needs, no hand lies free while something standing wants it, nothing weaker holds a
#      hand over something stronger that wants it, and nothing stands that a use standing does not let be asked for.
# It also writes Logs/hands-matrix.txt: for every pair of uses, whether the one asking takes the hand from the one
# holding it, so the order can be read (and changed in the table) without reading code.
# Change HandBoard's rule and these replicas together.
import csv, io, itertools, os, random, re, sys

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
TABLE = os.path.join(REPO, "Assets", "_Project", "Data", "Resources", "Tables", "Player", "HandUses.csv")
SCRIPTS = os.path.join(REPO, "Assets", "_Project", "Scripts")
USES_CS = os.path.join(SCRIPTS, "Player", "Hands", "HandUses.cs")
MOVEMENT_CS = os.path.join(SCRIPTS, "Player", "Movement", "PlayerMovement.cs")
MATRIX = os.path.join(REPO, "Logs", "hands-matrix.txt")
LEFT, RIGHT = "left", "right"
EACH = (LEFT, RIGHT)
NO_RANK = -1
HEAD = ["id", "hands", "also", "rank", "stances", "gun", "locks", "only", "#설명"]
checks = 0


def expect(holds, what):
    global checks
    checks += 1
    if not holds:
        sys.exit("FAILED: " + what)


# ---- The table, read as HandUses.Read reads it ----

class Use:
    def __init__(self, name, needs, also, rank, gun="", locks=False, stances=None, only=()):
        self.id, self.needs, self.rank, self.gun, self.locks, self.stances = name, frozenset(needs), rank, gun, locks, stances
        self.also = frozenset(also) - self.needs
        # Its family is its id up to the first dot; `only` the families that may be asked for while it stands (None: any).
        self.family = name.split(".")[0]
        self.only = frozenset(part.lower() for part in only) or None

    def lets(self, other):
        return self.only is None or other.family.lower() in self.only

    def __repr__(self):
        return self.id


def sides(text, where):
    text = text.strip().lower()
    expect(text in ("", "left", "right", "both"), "%s: '%s' is not left, right or both" % (where, text))
    return {"": (), "left": (LEFT,), "right": (RIGHT,), "both": EACH}[text]


def states_of_the_body():
    text = open(MOVEMENT_CS, encoding="utf-8-sig").read()
    body = re.search(r"public enum MovementState\s*\{(.*?)\}", text, re.S)
    expect(body is not None, "PlayerMovement.MovementState not found")
    names = [re.sub(r"\s*=.*", "", part).strip() for part in re.sub(r"//[^\n]*", "", body.group(1)).split(",")]
    return {name for name in names if name}


def load():
    rows = list(csv.reader(io.StringIO(open(TABLE, encoding="utf-8-sig").read())))
    expect([h.strip() for h in rows[0]] == HEAD, "the columns are %r, not %r" % (rows[0], HEAD))
    states = states_of_the_body()
    uses, order = {}, []
    for row in rows[1:]:
        expect(len(row) == len(HEAD), "row %s has %d cells for %d columns (a comma without quotes cuts a cell)" % (row[:1], len(row), len(HEAD)))
        name = row[0].strip()
        if not name or name.startswith("#"):
            continue
        expect(name not in uses, "row written twice: " + name)
        cell = dict(zip(HEAD, (c.strip() for c in row)))
        rank = NO_RANK
        if cell["rank"]:
            expect(re.fullmatch(r"-?\d+", cell["rank"]) is not None, "%s: rank '%s' is not a whole number" % (name, cell["rank"]))
            rank = int(cell["rank"])
        expect(cell["gun"] in ("", "away", "stays"), "%s: gun '%s' is neither away nor stays" % (name, cell["gun"]))
        expect(cell["locks"] in ("", "0", "1"), "%s: locks '%s' is not 1 or empty" % (name, cell["locks"]))
        stances = None
        if cell["stances"] and cell["stances"].lower() != "any":
            stances = {part.strip() for part in cell["stances"].split("|") if part.strip()}
            for state in stances:
                expect(state in states, "%s: stances names '%s', which is no state of the body" % (name, state))
        only = [part.strip() for part in cell["only"].split("|") if part.strip()]
        use = Use(name, sides(cell["hands"], name + " hands"), sides(cell["also"], name + " also"), rank, cell["gun"], cell["locks"] == "1", stances, only)
        expect(not (set(sides(cell["hands"], name)) & set(sides(cell["also"], name))), name + ": a hand both needed and only used while free")
        uses[name] = use
        order.append(use)
    return uses, order


USES, ORDER = load()
U = USES.get


# ---- HandBoard's rule, line for line ----

class Entry:
    def __init__(self, who, use, fact, arrived, came):
        self.who, self.use, self.fact, self.arrived, self.came, self.owns = who, use, fact, arrived, came, set()


class Board:
    def __init__(self):
        self.entries, self.owner, self.arrivals, self.changes = [], {LEFT: None, RIGHT: None}, 0, []

    def holder(self, hand):
        return self.owner[hand]

    def asked(self, who):
        for entry in self.entries:
            if not entry.fact and entry.who == who:
                return entry
        return None

    def has(self, who):
        return self.asked(who) is not None

    def standing(self, name):
        for entry in self.entries:
            if entry.use.id == name and entry.use.needs <= entry.owns:
                return entry
        return None

    def locked(self):
        return any(entry.use.locks for entry in self.entries)

    @staticmethod
    def beats(asker, needs, frame, holder, hand):
        if asker.rank != holder.use.rank:
            return asker.rank > holder.use.rank
        holder_needs = hand in holder.use.needs
        if needs != holder_needs:
            return needs
        return holder.came == frame and asker.id < holder.use.id

    def limiting(self, use, but):
        for entry in self.entries:
            if entry is not but and entry.use.rank >= use.rank and not entry.use.lets(use):
                return entry
        return None

    def limit(self, by):
        if by.use.only is None:
            return
        for entry in reversed(list(self.entries)):
            if entry is not by and not entry.fact and entry.use.rank <= by.use.rank and not by.use.lets(entry.use):
                self.remove(entry)

    def could(self, use, who, frame):
        """(True, None), or (False, (the hand it could not have or None, the use in its way, whether that is a fact))."""
        mine = self.asked(who) if who is not None else None
        limit = self.limiting(use, mine)
        if limit is not None:
            return False, (None, limit.use, limit.fact)
        for hand in EACH:
            if hand not in use.needs:
                continue
            holder = self.holder(hand)
            if holder is None or holder is mine or self.beats(use, True, frame, holder, hand):
                continue
            return False, (hand, holder.use, holder.fact)
        return True, None

    def kept(self, use, who, frame):
        ok, why = self.could(use, who, frame)
        return not ok and why[0] is not None and not why[2]

    def take(self, use, who, frame):
        mine = self.asked(who)
        if mine is not None and mine.use.id == use.id:
            return True
        ok, _ = self.could(use, who, frame)
        if not ok:
            return False
        before = self.snapshot()
        if mine is None:
            self.arrivals += 1
            mine = Entry(who, use, False, self.arrivals, frame)
            self.entries.append(mine)
        else:
            for hand in EACH:
                if hand in mine.owns and hand not in (use.needs | use.also):
                    self.free(mine, hand)
        mine.use, mine.fact, mine.came = use, False, frame
        self.limit(mine)
        self.claim(mine, frame)
        self.end(before)
        return True

    def state(self, use, who, frame):
        before = self.snapshot()
        mine = next((e for e in self.entries if e.fact and e.who == who and e.use.id == use.id), None)
        if mine is None:
            self.arrivals += 1
            mine = Entry(who, use, True, self.arrivals, frame)
            self.entries.append(mine)
        mine.use = use
        self.limit(mine)
        self.claim(mine, frame)
        self.end(before)

    def give(self, who):
        mine = self.asked(who)
        if mine is None:
            return False
        before = self.snapshot()
        self.remove(mine)
        self.end(before)
        return True

    def withdraw(self, who, name):
        mine = next((e for e in self.entries if e.fact and e.who == who and e.use.id == name), None)
        if mine is None:
            return
        before = self.snapshot()
        self.remove(mine)
        self.end(before)

    def claim(self, entry, frame):
        for hand in EACH:
            if hand in entry.use.needs:
                self.reach(entry, hand, True, frame)
        for hand in EACH:
            if hand in entry.use.also:
                self.reach(entry, hand, False, frame)

    def reach(self, entry, hand, needs, frame):
        holder = self.holder(hand)
        if holder is entry:
            return
        if holder is not None:
            if not self.beats(entry.use, needs, frame, holder, hand):
                return
            self.lose(holder, hand)
        self.put(hand, entry)

    def lose(self, holder, hand):
        self.free(holder, hand)
        if holder.fact or hand not in holder.use.needs:
            return
        self.remove(holder)

    def remove(self, entry):
        if entry in self.entries:
            self.entries.remove(entry)
        for hand in EACH:
            if hand in entry.owns:
                self.free(entry, hand)

    def free(self, entry, hand):
        entry.owns.discard(hand)
        if self.owner[hand] is entry:
            self.owner[hand] = None

    def put(self, hand, entry):
        self.owner[hand] = entry
        entry.owns.add(hand)

    def settle(self):
        for hand in EACH:
            if self.owner[hand] is not None:
                continue
            best, best_needs = None, False
            for entry in self.entries:
                needs = hand in entry.use.needs
                if (not entry.fact) if needs else (hand not in entry.use.also):
                    continue
                if (best is None or entry.use.rank > best.use.rank
                        or (entry.use.rank == best.use.rank and (needs if needs != best_needs else entry.arrived < best.arrived))):
                    best, best_needs = entry, needs
            if best is not None:
                self.put(hand, best)

    def snapshot(self):
        return {hand: (self.owner[hand].use.id if self.owner[hand] else None) for hand in EACH}

    def end(self, before):
        self.settle()
        after = self.snapshot()
        for hand in EACH:
            if before[hand] != after[hand]:
                self.changes.append((hand, before[hand], after[hand]))

    def of(self, hand):
        return self.owner[hand].use.id if self.owner[hand] else None


# ---- 1. The table ----

FACTS = [u for u in ORDER if u.rank >= 100 or u.id == "move.roll"]
ASKED = [u for u in ORDER if u.rank != NO_RANK and u not in FACTS]
ONLY_ASKS = [u for u in ORDER if u.rank == NO_RANK]
FAMILIES = {u.family.lower() for u in ORDER}
for use in ORDER:
    expect(use.needs or use.also or use.only, use.id + " names no hand and limits nothing")
    for family in use.only or ():
        expect(family in FAMILIES, "%s: only names '%s', which is the family of no row (a family is an id up to its first dot)" % (use.id, family))
    if use.locks:
        expect(use.rank >= 100 and use.needs == frozenset(EACH), use.id + " locks the hands but is not a fact of both hands")
    if use.gun:
        expect(RIGHT in use.needs, use.id + " says what becomes of the gun but does not take the right hand")
    if use.rank >= 100 and RIGHT in use.needs:
        expect(use.gun in ("away", "stays"), use.id + " takes the right hand as a fact and does not say what becomes of the gun")
expect([u.id for u in ONLY_ASKS] == ["gun.fire"], "the only row that asks without holding is gun.fire, not %r" % ONLY_ASKS)
# The one row that limits what may be asked: a body gone down, which keeps its hands on its pistol and takes none.
expect([(u.id, sorted(u.only), sorted(u.needs | u.also)) for u in ORDER if u.only] == [("life.downed", ["gun"], [])],
       "the rows that let only some families be asked for are not life.downed alone, letting the gun's and taking no hand")
fact_ranks = [u.rank for u in ORDER if u.rank >= 100]
expect(all(rank > 60 for rank in fact_ranks), "a fact of the body ranks no higher than something held in the fist")
print("table: %d rows (%d asked for, %d stated, %d only asking)" % (len(ORDER), len(ASKED), len(FACTS), len(ONLY_ASKS)))

# ---- 2. The code names the table's rows, and the table has nothing the code does not name ----

named = set(re.findall(r'=\s*"([a-z]+\.[a-z]+)"', open(USES_CS, encoding="utf-8-sig").read()))
expect(named, "no row names found in HandUses.cs")
for name in sorted(named):
    expect(name in USES, "HandUses.cs names '%s', which the table does not have" % name)
for use in ORDER:
    expect(use.id in named, "the table's row '%s' has no constant in HandUses.cs" % use.id)
print("code: %d rows named in HandUses.cs, all in the table and none missing" % len(named))

# ---- 3. The user's own example ----

GUN, NADE, DOSE, TOOL, POINT, POSE, BODY, KNIFE = "gun", "grenades", "syringe", "tool", "point", "pose", "body", "knife"


def gun_in_hand(frame=0):
    board = Board()
    expect(board.take(U("gun.hold"), GUN, frame), "the gun could not be held in empty hands")
    expect(board.of(LEFT) == "gun.hold" and board.of(RIGHT) == "gun.hold", "the gun at rest does not have both hands")
    return board


board = gun_in_hand()
expect(board.take(U("grenade.throw"), NADE, 1), "G did not get the left hand from a gun at rest")
expect(board.of(LEFT) == "grenade.throw" and board.of(RIGHT) == "gun.hold" and board.has(GUN), "the grenade took more than the left hand")
expect(board.take(U("gun.aim"), GUN, 2), "the right hand could not aim with a grenade in the left")
expect(board.of(LEFT) == "grenade.throw" and board.of(RIGHT) == "gun.aim", "aiming moved the grenade's hand")
expect(board.could(U("gun.fire"), GUN, 3)[0], "the trigger is not the gun's with a grenade in the left hand")
ok, why = board.could(U("gun.reload"), GUN, 4)
expect(not ok and why == (LEFT, U("grenade.throw"), False), "a reload asked with a grenade held was not refused for the left hand: %r" % (why,))
expect(not board.take(U("gun.reload"), GUN, 4) and board.of(RIGHT) == "gun.aim" and board.has(GUN), "a refused reload changed what the gun has")
expect(board.give(NADE) and board.of(LEFT) == "gun.aim", "the left hand did not go back under the aimed gun after the throw")
expect(board.take(U("gun.reload"), GUN, 5) and board.of(LEFT) == "gun.reload" == board.of(RIGHT), "a reload did not start once the hand was back")
print("the user's example: aims and fires with a grenade in the left hand; a reload asked meanwhile never starts")

# ---- 4. What the user decided on 2026-11-18 ----

# A grenade taken out during a reload takes the left hand, and the reload is over (as before).
board = gun_in_hand()
board.take(U("gun.reload"), GUN, 1)
expect(board.take(U("grenade.throw"), NADE, 2) and not board.has(GUN), "G during a reload did not end the reload")
expect(board.take(U("gun.hold"), GUN, 3) and board.of(RIGHT) == "gun.hold" and board.of(LEFT) == "grenade.throw", "the gun did not go on in the right hand")

# "먼저 하는 게 일단 짱": between a reload and a dose, whichever began first keeps the hand.
board = gun_in_hand()
board.take(U("gun.reload"), GUN, 1)
expect(not board.take(U("detente.dose"), DOSE, 2) and board.of(LEFT) == "gun.reload", "a dose took the hand from a reload under way")
board = gun_in_hand()
expect(board.take(U("detente.dose"), DOSE, 1) and board.has(GUN), "a dose could not have the left hand of a gun at rest")
expect(not board.could(U("gun.reload"), GUN, 2)[0], "a reload could start with the syringe in the left hand")
board.give(DOSE)
expect(board.could(U("gun.reload"), GUN, 3)[0], "a reload could not start after the dose")

# "수리 중엔 장전으로 넘어갈 수 있게 … 그 역은 안 되겠죠": a repair gives way to a reload, never the other way.
board = gun_in_hand()
expect(board.take(U("support.repair"), TOOL, 1), "the tool could not have the left hand of a gun at rest")
expect(board.take(U("gun.reload"), GUN, 2) and not board.has(TOOL), "a reload could not take over from a repair")
expect(not board.take(U("support.repair"), TOOL, 3), "a repair started during a reload")
board.take(U("gun.hold"), GUN, 4)
expect(board.take(U("support.repair"), TOOL, 5), "the tool did not come back after the reload")

# "발차기로 하죠": V is the gun's bash only with both hands on the gun.
board = gun_in_hand()
expect(board.could(U("gun.bash"), GUN, 1)[0], "the gun could not bash with both hands on it")
board.take(U("grenade.throw"), NADE, 1)
expect(not board.could(U("gun.bash"), GUN, 2)[0], "the gun could bash with a grenade in the left hand (V must be the kick)")
board = gun_in_hand()
board.take(U("detente.dose"), DOSE, 1)
expect(not board.could(U("gun.bash"), GUN, 2)[0], "the gun could bash with the syringe in the left hand")
board = gun_in_hand()
board.take(U("support.repair"), TOOL, 1)
expect(not board.kept(U("gun.bash"), GUN, 2), "the tool keeps the hand from a bash (a bash outranks a repair)")
expect(board.take(U("gun.bash"), GUN, 2) and not board.has(TOOL), "a bash did not take the hand from a repair")
# What V asks is whether the hand is busy with something else that was ASKED for (Kept): then it is the kick. A fact of
# the body having the hands is not that: in a zombie's grip V is the shove that fights it off, as it always was.
board = gun_in_hand()
expect(not board.kept(U("gun.bash"), GUN, 1), "V is the kick with both hands on the gun")
board.take(U("grenade.throw"), NADE, 1)
expect(board.kept(U("gun.bash"), GUN, 2), "V is not the kick with a grenade in the left hand")
for fact in ("zombie.hold", "move.vault", "move.climb", "move.roll", "life.downed", "life.dead", "carry.can"):
    board = gun_in_hand()
    board.state(U(fact), BODY, 1)
    expect(not board.kept(U("gun.bash"), GUN, 2), "V became the kick because of %s (a fact of the body: V there is what it was)" % fact)

# A grenade in the fist: kept through a landing roll (the gun is put away), given up for a vault, a grab, an execution.
board = gun_in_hand()
board.take(U("grenade.throw"), NADE, 1)
board.state(U("move.roll"), BODY, 2)
expect(board.has(NADE) and board.of(LEFT) == "grenade.throw" and board.of(RIGHT) == "move.roll" and not board.has(GUN),
       "a landing roll did not leave the grenade in the fist and take the gun's hand")
board.withdraw(BODY, "move.roll")
expect(board.take(U("gun.hold"), GUN, 3) and board.of(LEFT) == "grenade.throw", "the gun did not come back after the roll")
for fact in ("move.vault", "zombie.hold", "melee.execution", "move.climb", "life.dead", "carry.can", "detente.bandage"):
    board = gun_in_hand()
    board.take(U("grenade.throw"), NADE, 1)
    board.state(U(fact), BODY, 2)
    expect(not board.has(NADE) and board.of(LEFT) == fact, fact + " did not take the grenade's hand")
    expect(not board.take(U("grenade.throw"), NADE, 3), "a grenade could be taken out during " + fact)

# A body gone down has its pistol and whatever the pistol does, and nothing else of the hands (PlayerWeaponSlots
# .HoldSidearm; the gun's code never asked whether its player is down, so a reload on the ground always worked).
board = gun_in_hand()
board.take(U("gun.reload"), GUN, 1)
board.state(U("life.downed"), BODY, 2)
expect(board.has(GUN) and board.of(LEFT) == "gun.reload" == board.of(RIGHT), "going down ended a reload under way")
board = gun_in_hand()
board.state(U("life.downed"), BODY, 1)
expect(board.has(GUN) and board.of(LEFT) == "gun.hold" == board.of(RIGHT), "going down took a hand from the gun")
for name in ("gun.aim", "gun.reload", "gun.inspect", "gun.bash", "gun.swap", "gun.hold"):
    expect(board.take(U(name), GUN, 2) and board.has(GUN), "a body gone down could not have " + name)
expect(board.could(U("gun.fire"), GUN, 3)[0], "a body gone down could not fire")
for name in ("grenade.throw", "detente.dose", "support.repair", "guard.pose", "point.ping", "point.hold", "callout.arm"):
    ok, why = board.could(U(name), name, 3)
    expect(not ok and why == (None, U("life.downed"), True), "%s was not refused by the body being down: %r" % (name, why))
    expect(not board.take(U(name), name, 3), name + " could be had by a body gone down")
for name, who in (("grenade.throw", NADE), ("detente.dose", DOSE), ("support.repair", TOOL), ("point.hold", POINT), ("guard.pose", POSE)):
    board = gun_in_hand()
    expect(board.take(U(name), who, 1), name + " could not have the left hand of a gun at rest")
    board.state(U("life.downed"), BODY, 2)
    expect(not board.has(who) and board.has(GUN) and board.of(LEFT) == "gun.hold", "going down did not end %s and give the hand back to the gun" % name)
    board.withdraw(BODY, "life.downed")
    expect(board.take(U(name), who, 3), name + " could not be had again once the body was up")
# Dead on the ground: death takes both hands, the gun's too.
board = gun_in_hand()
board.state(U("life.downed"), BODY, 1)
board.state(U("life.dead"), BODY, 2)
expect(not board.has(GUN) and board.of(LEFT) == "life.dead" == board.of(RIGHT), "death did not take the hands of a body gone down")
# The knife going back after an execution has the right hand only: a grenade may come out, the gun may not.
board = Board()
board.state(U("melee.sheath"), KNIFE, 1)
expect(board.take(U("grenade.throw"), NADE, 2) and not board.could(U("gun.hold"), GUN, 2)[0], "the sheathing knife did not leave the left hand free and keep the right")

# What locks the hands is what PlayerMovement.BodyLocked is made of today.
expect({u.id for u in ORDER if u.locks} == {"melee.execution", "melee.guardbreak", "melee.counter", "detente.bandage"}, "the rows that lock the hands changed")
board = gun_in_hand()
board.state(U("melee.execution"), BODY, 1)
expect(board.locked() and not board.has(GUN), "an execution did not lock the hands and take the gun's")
board.withdraw(BODY, "melee.execution")
expect(not board.locked(), "the lock outlived the execution")
print("the user's decisions: first come between reload and dose, repair gives way to a reload, no bash without both hands, a grenade stays through a roll")
print("kept as it was: a body gone down reloads its pistol and has nothing else of the hands; V in a zombie's grip is the shove")

# ---- 5. Today's order between the left hand's uses ----

def left_use(first, then):
    """With `first` standing, `then` asks: who has the left hand after."""
    board = gun_in_hand()
    who = {"gun": GUN}
    for at, name in enumerate((first, then), 1):
        owner = GUN if name.startswith("gun.") else name
        board.take(U(name), owner, at)
    return board.of(LEFT)


for first, then, wins in [
    ("point.hold", "gun.aim", "gun.aim"),           # aiming ends a held point
    ("gun.aim", "point.hold", "gun.aim"),           # and none starts while aiming
    ("gun.aim", "point.ping", "point.ping"),        # a ping's tap of the left hand is allowed down the sights
    ("gun.reload", "point.ping", "gun.reload"),     # not during a reload,
    ("gun.swap", "point.ping", "gun.swap"),         # a draw
    ("gun.inspect", "point.ping", "gun.inspect"),   # or a look over the gun
    ("gun.reload", "point.hold", "gun.reload"),
    ("point.hold", "gun.reload", "gun.reload"),     # a reload takes the hand from a point
    ("gun.aim", "guard.pose", "guard.pose"),        # the guard pose is struck down the sights,
    ("gun.swap", "guard.pose", "guard.pose"),       # during a draw,
    ("gun.reload", "guard.pose", "gun.reload"),     # never during a reload
    ("grenade.throw", "guard.pose", "grenade.throw"),
    ("grenade.throw", "point.hold", "grenade.throw"),
    ("grenade.throw", "support.repair", "grenade.throw"),
    ("grenade.throw", "detente.dose", "grenade.throw"),
    ("point.hold", "grenade.throw", "grenade.throw"),
    ("support.repair", "grenade.throw", "grenade.throw"),
    ("detente.dose", "grenade.throw", "grenade.throw"),
    ("gun.inspect", "grenade.throw", "grenade.throw"),
    ("gun.inspect", "detente.dose", "detente.dose"),  # a look over the gun gives way to whatever wants a hand
    ("gun.swap", "detente.dose", "detente.dose"),     # a draw leaves the left hand to a dose,
    ("gun.swap", "support.repair", "support.repair"),  # a tool
    ("gun.swap", "grenade.throw", "grenade.throw"),   # and a grenade
    ("gun.aim", "support.repair", "support.repair"),
    ("gun.aim", "detente.dose", "detente.dose"),
    ("gun.hold", "callout.arm", "gun.hold"),          # a callout's arm never breaks the gun hold
]:
    got = left_use(first, then)
    expect(got == wins, "with %s standing, %s asked: the left hand is %s's, not %s's" % (first, then, got, wins))
board = Board()
expect(board.take(U("callout.arm"), "gesture", 1), "a callout's arm could not move with no gun in the hands")
print("today's order: 26 cases of the left hand's uses against each other and the gun")

# ---- 6. Every pair, and random runs ----

def strength(use, hand):
    """What a use brings to a hand: (rank, needs it) or None when it has no want of it."""
    if hand in use.needs:
        return (use.rank, True)
    if hand in use.also:
        return (use.rank, False)
    return None


def limits(by, other):
    """Whether `by`, standing, keeps `other` from being asked for: it lets only other families, and ranks no lower."""
    return by.only is not None and not by.lets(other) and by.rank >= other.rank


def expected_takes(asker, holder_use, hand, holder_came, frame):
    mine, theirs = strength(asker, hand), strength(holder_use, hand)
    if mine[0] != theirs[0]:
        return mine[0] > theirs[0]
    if mine[1] != theirs[1]:
        return mine[1]
    return holder_came == frame and asker.id < holder_use.id


pairs = 0
lines = []
for holder_use in ORDER:
    if holder_use.rank == NO_RANK:
        continue
    for asker in ORDER:
        if asker is holder_use or asker.rank == NO_RANK:
            continue
        for holder_fact, asker_fact in itertools.product((False, True), repeat=2):
            if holder_fact != (holder_use in FACTS) or asker_fact != (asker in FACTS):
                continue
            board = Board()
            if holder_fact:
                board.state(holder_use, "A", 1)
            else:
                board.take(holder_use, "A", 1)
            held = {hand: board.of(hand) for hand in EACH}
            contested = [hand for hand in EACH if hand in asker.needs and held[hand] == holder_use.id]
            wins = [expected_takes(asker, holder_use, hand, 1, 2) for hand in contested]
            if asker_fact:
                board.state(asker, "B", 2)
                if not holder_fact and limits(asker, holder_use):
                    # It lets nothing of the holder's family be asked for: the holder is over, whatever the hands say.
                    expect(not board.has("A"), "%s stated over %s: what it does not let be asked for still stands" % (asker, holder_use))
                    pairs += 1
                    continue
                for hand, win in zip(contested, wins):
                    expect((board.of(hand) == asker.id) == win, "%s stated over %s: the %s hand went wrong" % (asker, holder_use, hand))
                granted = True
            else:
                granted = board.take(asker, "B", 2)
                due = all(wins) and not limits(holder_use, asker)
                expect(granted == due, "%s asked over %s: granted %s, the ranks and the families say %s" % (asker, holder_use, granted, due))
                if granted:
                    for hand in asker.needs:
                        expect(board.of(hand) == asker.id, "%s was granted over %s without its %s hand" % (asker, holder_use, hand))
                else:
                    expect({hand: board.of(hand) for hand in EACH} == held, "%s was refused over %s and something still changed" % (asker, holder_use))
            if not holder_fact and granted and any(hand in holder_use.needs for hand in contested) and any(wins):
                lost = any(win and hand in holder_use.needs for hand, win in zip(contested, wins))
                expect(board.has("A") != lost, "%s under %s: stands %s after losing a hand it needs %s" % (holder_use, asker, board.has("A"), lost))
            pairs += 1
print("every pair: %d orderings of two uses give the answer their ranks give" % pairs)

random.seed(20261118)
runs = 0
for run in range(4000):
    board, frame, who_of = Board(), 0, {}
    for step in range(random.randint(3, 14)):
        frame += random.choice((0, 1, 1, 1))
        use = random.choice(ORDER)
        if use.rank == NO_RANK:
            continue
        roll = random.random()
        if use in FACTS:
            if roll < .7:
                board.state(use, "body", frame)
            else:
                board.withdraw("body", use.id)
        else:
            owner = "gun" if use.id.startswith("gun.") else use.id
            if roll < .75:
                board.take(use, owner, frame)
            else:
                board.give(owner)
        for entry in board.entries:
            if not entry.fact:
                expect(entry.use.needs <= entry.owns, "run %d: %s stands without a hand it needs" % (run, entry.use))
                for other in board.entries:
                    expect(other is entry or not limits(other.use, entry.use), "run %d: %s stands though %s lets only %s be asked for"
                           % (run, entry.use, other.use, sorted(other.use.only or ())))
            for hand in entry.owns:
                expect(board.owner[hand] is entry, "run %d: %s thinks it has a hand that is %s's" % (run, entry.use, board.of(hand)))
        for hand in EACH:
            holder = board.owner[hand]
            wanting = [e for e in board.entries if e is not holder and ((hand in e.use.needs and e.fact) or hand in e.use.also)]
            if holder is None:
                expect(not wanting, "run %d: the %s hand lies free while %s wants it" % (run, hand, wanting and wanting[0].use))
                continue
            expect(hand in holder.owns, "run %d: the %s hand's owner does not know it" % (run, hand))
            for other in wanting:
                expect(other.use.rank <= holder.use.rank, "run %d: %s holds the %s hand over %s, which is stronger" % (run, holder.use, hand, other.use))
    runs += 1
print("random runs: %d, no ask without its hands, no hand free while wanted, nothing weaker over something stronger, nothing a standing use does not let" % runs)

# ---- The matrix: who takes the hand from whom ----

def meets(asker, holder_use):
    if limits(holder_use, asker) and asker not in FACTS:
        return " x "
    if limits(asker, holder_use) and holder_use not in FACTS:
        return " # "
    shared = [hand for hand in EACH if hand in asker.needs and strength(holder_use, hand)]
    if shared:
        wins = [expected_takes(asker, holder_use, hand, 1, 2) for hand in shared]
        return " O " if all(wins) else " x " if not any(wins) else " / "
    # No hand it needs is the other's: a hand it only uses while free may still be.
    spare = [hand for hand in EACH if hand in asker.also and strength(holder_use, hand)]
    if any(expected_takes(asker, holder_use, hand, 1, 2) for hand in spare):
        return " o "
    return " . "


listed = [u for u in ORDER if u.rank != NO_RANK]
width = max(len(u.id) for u in listed)
out = ["whose hand: for the use asking (rows) against the use that has the hand (columns),",
       "  O  the one asking gets every hand it needs    x  it gets none: refused (a fact stands without that hand)",
       "  /  it would get one hand and not the other     o  it starts anyway and also takes a hand it only uses while free",
       "  .  it starts and takes nothing from the other",
       "  #  it takes no hand, and the other is over all the same: it lets only its own families be asked for (the column only)",
       "the table: Assets/_Project/Data/Resources/Tables/Player/HandUses.csv (rank: the bigger takes; equal: who was first keeps it)", ""]
out.append(" " * (width + 6) + " ".join("%2d " % (at + 1) for at in range(len(listed))))
for at, asker in enumerate(listed):
    out.append("%2d %-*s %3d " % (at + 1, width, asker.id, asker.rank) + " ".join(meets(asker, holder) if holder is not asker else " - " for holder in listed))
os.makedirs(os.path.dirname(MATRIX), exist_ok=True)
with open(MATRIX, "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(out) + "\n")
print("wrote " + os.path.relpath(MATRIX, REPO))
print("ALL CHECKS PASSED (%d)" % checks)
