using UnityEngine;

/// <summary>
/// How far a set piece shows itself along its motion, from the reports of how far it has really come.
/// The reports come in steps: a switch's state counts a jet bridge's way out in twentieths, one every half second it
/// is driven, and a guest hears of a hold's time in whole seconds. Shown as they came, the piece ran to each report
/// and stood waiting for the next: the jet bridge went out in twenty jerks, a third of a second of moving and a fifth
/// of standing each, on every PC (the user, 2026-11-18: "탑승교 발판이 하나씩 생길 때마다 렉이 걸렸습니다"), and a
/// guest's airliner rolled four fifths of every second behind its tug. The stutter was in the motion, whatever the
/// frames did. A report says where the piece was; it does not say the piece stopped there.
/// So while reports keep coming the piece is taken to be driven: it goes on at the driving's own pace toward where
/// the next report will put it, one report ahead at most, and the report that comes finds it there. When they stop
/// coming it stops within that one report. Behind what is reported (the first report after it starts, a guest whose
/// word came late) it catches up a little faster than the pace. It never runs back: a report lower than the last
/// (a wipe that sends the run back) puts it there at once, as Put does for a piece that is simply where it is (before
/// its step, after it, for someone joining late).
/// It also keeps the play's record of each run of motion (Logs/set-pieces.txt, PlayRecord): how long it took, the
/// longest frame, the frames of 25 ms or more, and how often the piece stood still while it was being driven, so
/// whether a set piece stutters, and whether by its frames or by its own motion, is read after a play and not asked
/// of the player. Tools/Checks/set_piece_pace_check.py is this rule in Python, run on the reports as they come.
/// </summary>
public sealed class SetPiecePace
{
    public const string RecordName = "set-pieces";
    private const string RecordHeading = "what moved: from where to where along its motion (0 as laid, 1 at its end), in how long, over how many frames, " +
        "the longest frame, the frames of 25 ms or more (a hitch of the frame), how often it stood still for 50 ms or more while it was being driven " +
        "(a stutter of the motion itself) and the longest of those, and how many reports of its progress came";
    // Behind the last report it catches up this much faster than the driving's own pace.
    private const float CatchUp = 1.25f;
    // After a report the driving is taken to go on for this many of the reports' own intervals: one that comes a
    // little late must not read as a stop.
    private const float Patience = 1.5f;
    // Two progresses nearer than this are the same.
    private const float Same = 1e-5f;
    // The record: a frame this long is a hitch, standing this long while driven is a stutter (s), and a run of motion
    // is over when the piece has not moved for this long (s).
    private const float HitchSeconds = .025f, StillSeconds = .05f, RunEndsAfter = 1f;

    private readonly string name;
    private float shown, reported = -1f, step, sinceStep = float.PositiveInfinity;
    // The run of motion under way, for the record.
    private bool running;
    private float runFrom, runTime, longestFrame, stillFor, longestStill, sinceMoved;
    private int runFrames, hitches, stills, reports;

    public SetPiecePace(string name) => this.name = name;

    /// <summary>How far along its motion it shows now (0 as laid, 1 at its end).</summary>
    public float Shown => shown;

    /// <summary>It is simply there: no travel (before its step, after it, a late joiner, back after a wipe).</summary>
    public void Put(float progress)
    {
        EndRun();
        shown = reported = Mathf.Clamp01(progress);
        step = 0f;
        sinceStep = float.PositiveInfinity;
    }

    /// <summary>
    /// One frame. <paramref name="report"/>: how far it has really come by the latest word (0..1);
    /// <paramref name="pace"/>: how much of its motion a second of driving makes. Returns how far it shows now.
    /// </summary>
    public float Follow(float report, float pace, float dt)
    {
        report = Mathf.Clamp01(report);
        if (reported < 0f || pace <= 0f || report < reported - Same)
        {
            Put(report);
            return shown;
        }
        if (report > reported + Same)
        {
            step = report - reported;
            reported = report;
            sinceStep = 0f;
            reports++;
        }
        else sinceStep += dt;
        bool driven = step > 0f && sinceStep <= Patience * step / pace;
        float goal = Mathf.Max(shown, driven ? Mathf.Min(1f, reported + step) : reported);
        float before = shown;
        shown = Mathf.MoveTowards(shown, goal, (shown < reported ? CatchUp : 1f) * pace * dt);
        Tally(before, driven, dt);
        return shown;
    }

    private void Tally(float before, bool driven, float dt)
    {
        if (!PlayRecord.On) return;
        bool moved = shown > before + Same * .1f;
        if (moved && !running)
        {
            running = true;
            runFrom = before;
            runTime = longestFrame = stillFor = longestStill = sinceMoved = 0f;
            runFrames = hitches = stills = 0;
            reports = 1;
        }
        if (!running) return;
        runTime += dt;
        runFrames++;
        if (dt > longestFrame) longestFrame = dt;
        if (dt >= HitchSeconds) hitches++;
        if (moved)
        {
            stillFor = sinceMoved = 0f;
        }
        else
        {
            sinceMoved += dt;
            if (driven)
            {
                // Counted once, when the standing has lasted long enough to be seen.
                if (stillFor < StillSeconds && stillFor + dt >= StillSeconds) stills++;
                stillFor += dt;
                if (stillFor >= StillSeconds && stillFor > longestStill) longestStill = stillFor;
            }
        }
        if (shown >= 1f - Same || sinceMoved > RunEndsAfter) EndRun();
    }

    private void EndRun()
    {
        if (!running) return;
        running = false;
        PlayRecord.Line(RecordName, RecordHeading,
            $"{name}: {runFrom:0.00} to {shown:0.00} in {Mathf.Max(0f, runTime - sinceMoved):0.00} s, {runFrames} frames, longest frame {longestFrame * 1000f:0} ms, " +
            $"frames of 25 ms or more {hitches}, stood still while driven {stills} times (longest {longestStill * 1000f:0} ms), reports {reports}");
    }
}
