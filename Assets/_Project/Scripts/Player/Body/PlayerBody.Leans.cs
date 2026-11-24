using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Running while the view turns, the legs lean into the turn: each forward cycle hands its share over to its leaning
/// cycle (BIPER Female Runner's Lean, a lean held while running straight) as the turn quickens, and on to its arc cycle
/// (Arc, the run of someone curving round, its own turn taken out of the pose) as the turn reaches the arc's own rate
/// (Tables/Player/BodyLeans.csv). The body itself always turns with the view; these only bank the stride, which a body
/// turning in its tracks at a run otherwise doesn't (the user, 2026-11-21: "출발 멈춤 꺾기"). The rate is the view's turn
/// as far as the way the body runs turns with it, as every PC sees them, so friends see the same lean: a run that curves
/// banks, a flick of the view or a cut aside while the run goes on straight doesn't. Until 0.9.386 it was the view's turn
/// alone, and a flick of the view banked the run into its arc for half a second after the view had stopped (걸음 기록 of
/// 2026-11-24: Jog_Arc and Sprint_Arc for 0.5 s after the view's turn back).
/// </summary>
public sealed partial class PlayerBody
{
    private const string LeansTable = "BodyLeans";
    // How quickly the lean follows the view's turn (s): about a step, so a flick of the mouse doesn't jerk the hips.
    private const float LeanFollowTime = .2f;

    private sealed class Lean
    {
        public Channel Straight, Left, Right, ArcLeft, ArcRight;
        public float ArcRate;
    }

    private readonly List<Lean> leans = new List<Lean>();
    private float turnRate, lastFacing, lastRunWay;
    private bool facingKnown, runWayKnown;
    // The frame the view's turn was last followed (FollowTurn runs only while the legs walk or run).
    private int followedTurnAt = -10;

    private void AddLeans()
    {
        leans.Clear();
        facingKnown = false;
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

    // The view's turn now (degrees a second, + to the right), eased.
    // Back on the legs after something else had them (a slide, a roll, a landing or a vault playing out, the air), the view's
    // turn meanwhile is no turn of the run, so the measure starts again from nothing. Until 0.9.385 it went on from the last
    // frame the legs had: the view's turn over the whole slide or roll came back as one turn of a frame, and every run after
    // one banked hard into its arc for half a second with the view still (걸음 기록 of 2026-11-24: Jog_Arc_R90 at 0.6 and the
    // leaning jog, whose own pace is slower, played a seventh faster; the user: "슬라이딩 후 조깅 여전히 좀 빠른 것 같은데
    // 그니까 모션이 어색하다고").
    private void FollowTurn(float dt)
    {
        if (Time.frameCount - followedTurnAt > 1)
        {
            facingKnown = runWayKnown = false;
            turnRate = 0f;
        }
        followedTurnAt = Time.frameCount;
        float facing = transform.eulerAngles.y;
        float rate = facingKnown && dt > 0f ? Mathf.DeltaAngle(lastFacing, facing) / dt : 0f;
        lastFacing = facing;
        facingKnown = true;
        // The way the run goes (the stride, Locomotion) turns too, or it is no curve.
        float runs = 0f;
        if (stride.sqrMagnitude > StillTravel * StillTravel)
        {
            float way = Mathf.Atan2(stride.x, stride.z) * Mathf.Rad2Deg;
            if (runWayKnown && dt > 0f) runs = Mathf.DeltaAngle(lastRunWay, way) / dt;
            lastRunWay = way;
            runWayKnown = true;
        }
        else runWayKnown = false;
        rate = rate * runs > 0f ? Mathf.Sign(rate) * Mathf.Min(Mathf.Abs(rate), Mathf.Abs(runs)) : 0f;
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
