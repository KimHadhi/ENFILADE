# How a set piece shows its progress from stepwise reports: the rule of SetPiecePace.cs in Python (change both
# together), run against the rules it replaced, without Unity.
#   python Tools/Checks/set_piece_pace_check.py
# The jet bridge's progress is shared as a switch's state in twentieths, one every half second it is driven; a guest
# hears of a hold's time in whole seconds (the tug's pushback). Before 2026-11-18 each PC ran its piece to every report
# and stood waiting for the next. This prints, for both rules, how often and how long the piece stands still while it
# is being driven, and checks what the new rule promises. Exit code 1 when a promise is broken.
import math
import random
import sys

CATCH_UP, PATIENCE, SAME = 1.25, 1.5, 1e-5      # SetPiecePace: CatchUp, Patience, Same
STILL = .05                                     # standing this long while driven is a stutter one sees (s)


def toward(value, goal, most):
    return goal if abs(goal - value) <= most else value + math.copysign(most, goal - value)


class Pace:
    """SetPiecePace.Follow and Put."""

    def __init__(self):
        self.shown, self.reported, self.step, self.since = 0.0, -1.0, 0.0, math.inf

    def put(self, progress):
        self.shown = self.reported = min(1.0, max(0.0, progress))
        self.step, self.since = 0.0, math.inf

    def follow(self, report, pace, dt):
        report = min(1.0, max(0.0, report))
        if self.reported < 0 or pace <= 0 or report < self.reported - SAME:
            self.put(report)
            return self.shown
        if report > self.reported + SAME:
            self.step, self.reported, self.since = report - self.reported, report, 0.0
        else:
            self.since += dt
        driven = self.step > 0 and self.since <= PATIENCE * self.step / pace
        goal = max(self.shown, min(1.0, self.reported + self.step) if driven else self.reported)
        self.shown = toward(self.shown, goal, (CATCH_UP if self.shown < self.reported else 1.0) * pace * dt)
        return self.shown


class OldBridge:
    """GimmickJetBridge before: shown ran at 1.6 times the pace to the state's twentieth and waited there."""

    def __init__(self):
        self.shown = 0.0

    def follow(self, report, pace, dt):
        self.shown = toward(self.shown, report, pace * 1.6 * dt)
        return self.shown


class OldPushback:
    """CampaignPushback before: toward the run's progress at 1.25 times the hold's pace."""

    def __init__(self):
        self.shown = 0.0

    def follow(self, report, pace, dt):
        self.shown = toward(self.shown, report, pace * 1.25 * dt) if report >= self.shown else report
        return self.shown


def run(follower, seconds, grain, fps, manned, lag=0.0, jitter=0.0, seed=1, exact=False, total=None):
    """Drives a piece for `seconds` of driving (progress 1 after them): the truth advances while manned(t) says someone
    drives; the report is the truth cut down to `grain` (a twentieth, a second's worth), heard `lag` seconds late, or
    the truth itself (exact: the host's own count). Returns what a watcher would see."""
    rng = random.Random(seed)
    pace = 1.0 / seconds
    t, truth, history = 0.0, 0.0, []
    out = dict(stills=0, longest=0.0, back=False, ahead=0.0, behind=0.0, frames=0, done_at=None, shown_done_at=None, first_move=None,
               stopped_ahead=0.0, speeds=[])
    still = 0.0
    last = follower.shown
    end = total if total is not None else seconds * 3
    while t < end:
        dt = (1.0 / fps) * (1.0 + (rng.uniform(-jitter, jitter) if jitter else 0.0))
        driving = manned(t) and truth < 1.0
        if driving:
            truth = min(1.0, truth + pace * dt)
        history.append((t, truth))
        heard = truth
        if lag > 0:
            heard = 0.0
            for when, value in reversed(history):
                if when <= t - lag:
                    heard = value
                    break
        report = heard if exact else math.floor(heard / grain + 1e-9) * grain
        shown = follower.follow(report, pace, dt)
        t += dt
        out["frames"] += 1
        if shown < last - 1e-9:
            out["back"] = True
        moved = shown > last + 1e-9
        if moved and out["first_move"] is None:
            out["first_move"] = t
        if driving and shown < 1.0 - 1e-6 and out["first_move"] is not None:
            if moved:
                still = 0.0
                out["speeds"].append((shown - last) / dt / pace)
            else:
                if still < STILL <= still + dt:
                    out["stills"] += 1
                still += dt
                if still >= STILL:
                    out["longest"] = max(out["longest"], still)
        else:
            still = 0.0
        out["ahead"] = max(out["ahead"], shown - truth)
        out["behind"] = max(out["behind"], truth - shown)
        if not driving and truth < 1.0:
            out["stopped_ahead"] = max(out["stopped_ahead"], shown - truth)
        if truth >= 1.0 and out["done_at"] is None:
            out["done_at"] = t
        if shown >= 1.0 - 1e-6 and out["shown_done_at"] is None:
            out["shown_done_at"] = t
        last = shown
    return out


def line(label, r, grain):
    speeds = r["speeds"]
    return ("  %-46s stood still %2d times (longest %3.0f ms)  moving at %.2f-%.2f of the pace  ahead %.3f behind %.3f (report %.3f)  "
            "first move %.2f s  at its end %s s (truth %s s)%s"
            % (label, r["stills"], r["longest"] * 1000, min(speeds) if speeds else 0, max(speeds) if speeds else 0, r["ahead"], r["behind"], grain,
               r["first_move"] or 0, "%.2f" % r["shown_done_at"] if r["shown_done_at"] else "never", "%.2f" % r["done_at"] if r["done_at"] else "never",
               "  RAN BACK" if r["back"] else ""))


def main():
    bad = []
    always = lambda t: True

    def promise(what, ok):
        if not ok:
            bad.append(what)
            print("    BROKEN: " + what)

    print("Jet bridge: 10 s of driving, the state in twentieths (one report every 0.5 s)")
    for fps in (60, 144, 30):
        old = run(OldBridge(), 10.0, .05, fps, always)
        new = run(Pace(), 10.0, .05, fps, always)
        host = run(Pace(), 10.0, .05, fps, always, exact=True)
        print(line("%3d fps  before (ran to each twentieth)" % fps, old, .05))
        print(line("%3d fps  now, from the twentieths" % fps, new, .05))
        print(line("%3d fps  now, the host's own count" % fps, host, .05))
        promise("%d fps: the bridge stood still while driven" % fps, new["stills"] == 0 and host["stills"] == 0)
        promise("%d fps: ran back" % fps, not new["back"] and not host["back"])
        promise("%d fps: more than a report ahead of the truth" % fps, new["ahead"] <= .05 + 1e-6)
        promise("%d fps: not at its end within 0.2 s of docking" % fps, new["shown_done_at"] is not None and abs(new["shown_done_at"] - new["done_at"]) <= .2)
    late = run(Pace(), 10.0, .05, 60, always, lag=.12, jitter=.3)
    print(line(" 60 fps  now, a guest 120 ms behind, uneven frames", late, .05))
    promise("guest: stood still while driven", late["stills"] == 0)

    print("Jet bridge: the driver steps out of the cab from 3.3 s to 5.3 s")
    leaves = lambda t: not (3.3 <= t < 5.3)
    old = run(OldBridge(), 10.0, .05, 60, leaves, total=14.0)
    new = run(Pace(), 10.0, .05, 60, leaves, total=14.0)
    print(line(" 60 fps  before", old, .05))
    print(line(" 60 fps  now", new, .05))
    print("    while nobody drives it stands at most %.3f ahead of the truth (one report is %.3f: %.1f m of an 8 m reach)" % (new["stopped_ahead"], .05, .05 * 8))
    promise("left the cab: more than one report ahead", new["stopped_ahead"] <= .05 + 1e-6)
    promise("left the cab: ran back", not new["back"])
    promise("left the cab: never reached its end", new["shown_done_at"] is not None)

    print("Tug's pushback as a guest sees it: a 45 s hold heard in whole seconds")
    old = run(OldPushback(), 45.0, 1 / 45.0, 60, always, lag=.1)
    new = run(Pace(), 45.0, 1 / 45.0, 60, always, lag=.1)
    print(line(" 60 fps  before (1.25 times the pace to each second)", old, 1 / 45.0))
    print(line(" 60 fps  now", new, 1 / 45.0))
    promise("pushback guest: stood still while driven", new["stills"] == 0)

    print("Simply there")
    pace = Pace()
    first = pace.follow(.6, .1, 1 / 60)
    pace.follow(.65, .1, 1 / 60)
    back = pace.follow(0.0, .1, 1 / 60)
    print("    someone joining late at 0.60 is shown at %.2f on the first frame; a wipe's 0.00 is shown at %.2f at once" % (first, back))
    promise("late joiner not put where it is", abs(first - .6) < 1e-6)
    promise("a lower report not taken at once", abs(back) < 1e-6)

    print("OK" if not bad else "BROKEN: %d" % len(bad))
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
