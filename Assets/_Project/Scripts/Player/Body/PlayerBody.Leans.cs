using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A run that curves leans into it: each forward cycle hands its share over to its leaning cycle (BIPER Female Runner's
/// Lean, a lean held while running straight) as the curve tightens, and on to its arc cycle (Arc, the run of someone curving
/// round, its own turn taken out of the pose) as it reaches the arc's own rate (Tables/Player/BodyLeans.csv), the rate a run
/// ahead curves at most (PlayerBody.ArcRate, LocomotionState), so at its tightest the run is the arc as it was made. The rate
/// is the way the legs go turning (the stride), as every PC sees it, so friends see the same lean: a flick of the view while
/// the run goes on straight banks nothing. Until 0.9.416 it was the view's turn as far as the run's turned with it, eased
/// over 0.2 s: a run that went on curving after the view had stopped didn't bank, and the arc came a fifth of a second
/// behind the curve; until 0.9.386 the view's turn alone (걸음 기록 of 2026-11-24: Jog_Arc and Sprint_Arc for 0.5 s after the
/// view's turn back).
/// </summary>
public sealed partial class PlayerBody
{
    private const string LeansTable = "BodyLeans";
    // How quickly the lean follows the run's curve (s): a few frames, so another PC's run, seen in network steps, doesn't jerk.
    private const float LeanFollowTime = .08f;

    private sealed class Lean
    {
        public Channel Straight, Left, Right, ArcLeft, ArcRight;
        public float ArcRate;
    }

    private readonly List<Lean> leans = new List<Lean>();
    private float turnRate, lastRunWay;
    private bool runWayKnown;
    // The frame the run's curve was last followed (FollowTurn runs only while the legs walk or run).
    private int followedTurnAt = -10;

    private void AddLeans()
    {
        leans.Clear();
        runWayKnown = false;
        foreach (string id in GameTables.RowIds(LeansTable))
        {
            if (id.StartsWith("#", System.StringComparison.Ordinal) || !GameTables.TryGetRow(LeansTable, id, out var row)) continue;
            var lean = new Lean
            {
                Straight = AddCycle(id), Left = AddCycle(row.Text("left")), Right = AddCycle(row.Text("right")),
                ArcLeft = AddCycle(row.Text("arcLeft")), ArcRight = AddCycle(row.Text("arcRight")),
                ArcRate = row.Number("arcRate", 0f)
            };
            if (lean.Straight != null && lean.ArcRate > 0f) leans.Add(lean);
        }
    }

    // The run's curve now (degrees a second, + to the right), eased.
    // Back on the legs after something else had them (a slide, a roll, a landing or a vault playing out, the air), the turn
    // meanwhile is no curve of the run, so the measure starts again from nothing. Until 0.9.385 it went on from the last
    // frame the legs had: the view's turn over the whole slide or roll came back as one turn of a frame, and every run after
    // one banked hard into its arc for half a second with the view still (걸음 기록 of 2026-11-24: Jog_Arc_R90 at 0.6 and the
    // leaning jog, whose own pace is slower, played a seventh faster; the user: "슬라이딩 후 조깅 여전히 좀 빠른 것 같은데
    // 그니까 모션이 어색하다고").
    private void FollowTurn(float dt)
    {
        if (Time.frameCount - followedTurnAt > 1)
        {
            runWayKnown = false;
            turnRate = 0f;
        }
        followedTurnAt = Time.frameCount;
        // The body's own turn on the move (its facing, as Update last laid it), not the way the stride swings: keys swept round
        // under a body that faces the view (a diamond, A and D) turn no body, and read off the stride they leaned and arced the
        // ahead cycles in the mix with the view still (the gait record of 2026-11-26: Jog_Arc_R90 at .42 and Walk_Arc_R90 at .35
        // with the view still; the experts' review). A run ahead curving and the view turned on the move still turn it.
        float rate = 0f;
        if (stride.sqrMagnitude > StillTravel * StillTravel)
        {
            if (runWayKnown && dt > 0f) rate = Mathf.DeltaAngle(lastRunWay, legYaw) / dt;
            lastRunWay = legYaw;
            runWayKnown = true;
        }
        else runWayKnown = false;
        turnRate = Mathf.Lerp(turnRate, rate, 1f - Mathf.Exp(-dt / LeanFollowTime));
    }

    // Each forward cycle in the mix gives the turn's share of its weight to its lean, and past half the arc's rate on to
    // its arc: straight at no turn, leaning at half the arc's rate, arcing at it.
    private void Bank()
    {
        if (leans.Count == 0 || Mathf.Abs(turnRate) < 1f) return;
        bool right = turnRate > 0f;
        foreach (var lean in leans)
        {
            float weight = lean.Straight.Target;
            if (weight <= 0f) continue;
            float share = Mathf.Clamp01(Mathf.Abs(turnRate) / lean.ArcRate);
            float leaning = share < .5f ? share * 2f : 2f - share * 2f, arcing = share < .5f ? 0f : share * 2f - 1f;
            var side = right ? lean.Right : lean.Left;
            var arc = right ? lean.ArcRight : lean.ArcLeft;
            if (side == null) leaning = 0f;
            if (arc == null) { leaning += arcing; arcing = 0f; if (side == null) leaning = 0f; }
            lean.Straight.Target -= weight * (leaning + arcing);
            if (side != null) side.Target += weight * leaning;
            if (arc != null) arc.Target += weight * arcing;
        }
    }
}
