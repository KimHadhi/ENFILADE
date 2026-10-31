using System.Collections.Generic;
using UnityEngine;

/// <summary>How a foot placement on a balance beam went.</summary>
public enum BalanceJudgement { Perfect, Good, Miss }

internal struct BalanceDifficulty
{
    public float Beat, QuickestBeat, LeadBeats;
    public float PerfectWindow, GoodWindow;
    public int SlipLimit;
}

/// <summary>
/// Each foot owns its upcoming preview, actual contact window and recent key presses. Actual contacts calibrate
/// the next cycle and decide judgement time; a press never moves a ring, changes body phase or blocks another key.
/// The evaluated animation phase drives every preview. Walking pace and sway ease continuously between frames.
/// </summary>
internal sealed class BalanceRhythm
{
    private sealed class FootNote
    {
        public readonly int Side;
        public readonly List<float> Presses = new List<float>(8);
        public bool Calibrated, Shown, Pending, Judged;
        public float DueCycle, ShownAt, LandedAt;
        public FootNote(int side) => Side = side;
        public void Reset()
        {
            Calibrated = Shown = Pending = Judged = false;
            Presses.Clear();
        }
    }

    private readonly PlayerMovement ctx;
    private readonly FootNote left = new FootNote(-1), right = new FootNote(1);
    private BalanceDifficulty difficulty;
    private float clock, leadUntil, targetBeat, cycle, lastPhase, observedAt;
    private int goodHits;
    private bool hasCycle, observedContact;
    private MovementFixtures Fixtures => ctx.Abilities.Fixtures;

    internal BalanceRhythm(PlayerMovement context) => ctx = context;

    internal void Begin(BalanceDifficulty how)
    {
        difficulty = how;
        clock = 0f;
        goodHits = 0;
        Fixtures.Balance = 0f;
        Fixtures.BalanceFallSide = 1;
        Fixtures.BalanceSlips = 0;
        Fixtures.BalanceSlipLimit = how.SlipLimit;
        Fixtures.BeginBalanceRhythm(this);
        Lead(0f);
    }

    internal void End() => Fixtures.EndBalanceRhythm(this);

    internal void Lead(float pause)
    {
        Fixtures.BalanceBeatTime = targetBeat = difficulty.Beat;
        leadUntil = clock + pause + difficulty.Beat * difficulty.LeadBeats;
        left.Reset();
        right.Reset();
        hasCycle = observedContact = false;
        Publish();
    }

    /// <summary>Evaluated cycle, sampled before post-IK foot contacts. Contact phases are measured, never guessed.</summary>
    internal void ObserveCycle(float phase)
    {
        phase = Mathf.Repeat(phase, 1f);
        if (!hasCycle) { cycle = phase; hasCycle = true; }
        else cycle += Mathf.Max(0f, Mathf.Repeat(phase - lastPhase + .5f, 1f) - .5f);
        lastPhase = phase;
        Publish();
    }

    internal bool Tick(float dt)
    {
        clock += dt;
        float follow = 1f - Mathf.Exp(-dt / Mathf.Max(.01f, ctx.AbilitySettings.balanceTempoResponse));
        Fixtures.BalanceBeatTime = Mathf.Lerp(Fixtures.BalanceBeatTime, targetBeat, follow);
        // This small natural sway depends only on the body's cycle, never on presses, note selection or judgement.
        float sway = hasCycle ? Mathf.Sin(cycle * Mathf.PI * 2f) * 25f : 0f;
        Fixtures.Balance = Mathf.Lerp(Fixtures.Balance, sway, 1f - Mathf.Exp(-dt / .12f));
        if (Resolve(left) || Resolve(right)) return true;
        Trim(left);
        Trim(right);
        Publish();
        return false;
    }

    /// <summary>Capture every fresh edge immediately. Both feet can receive input in the same frame.</summary>
    internal bool Press(int side)
    {
        if (side == 0) return false;
        ctx.RaiseBalanceKeyPressed(side);
        var foot = side < 0 ? left : right;
        if (foot.Presses.Count == 32) foot.Presses.RemoveAt(0);
        foot.Presses.Add(clock);
        return false;
    }

    /// <summary>Actual visible foot contact is the sole source of a judgement window.</summary>
    internal bool Contact(int side)
    {
        if (side == 0 || !hasCycle) return false;
        if (observedContact && clock - observedAt < .05f) return false;
        observedContact = true;
        observedAt = clock;
        var foot = side < 0 ? left : right;
        bool offered = foot.Calibrated && foot.Shown && clock - foot.ShownAt >= .2f && clock >= leadUntil;
        if (offered && foot.Pending && !foot.Judged)
        {
            foot.Judged = true;
            if (Miss(foot)) return true;
        }
        // The next ring starts on actual placement, regardless of when the previous key was pressed.
        foot.Calibrated = true;
        foot.DueCycle = cycle + 1f;
        foot.Shown = false;
        if (offered)
        {
            foot.Pending = true;
            foot.Judged = false;
            foot.LandedAt = clock;
            if (Resolve(foot)) return true;
        }
        Publish();
        return false;
    }

    private bool Resolve(FootNote foot)
    {
        if (!foot.Pending || foot.Judged) return false;
        float best = float.PositiveInfinity;
        int bestIndex = -1;
        for (int i = 0; i < foot.Presses.Count; i++)
        {
            float offset = Mathf.Abs(foot.Presses[i] - foot.LandedAt);
            if (offset <= difficulty.GoodWindow && offset < best)
            {
                best = offset;
                bestIndex = i;
            }
        }
        if (bestIndex >= 0)
        {
            foot.Presses.RemoveAt(bestIndex);
            foot.Judged = true;
            Hit(foot, best <= difficulty.PerfectWindow ? BalanceJudgement.Perfect : BalanceJudgement.Good);
        }
        else if (clock > foot.LandedAt + difficulty.GoodWindow)
        {
            foot.Judged = true;
            return Miss(foot);
        }
        return false;
    }

    private void Trim(FootNote foot)
    {
        float keep = Mathf.Max(ctx.AbilitySettings.balanceHitRange, difficulty.GoodWindow);
        for (int i = foot.Presses.Count - 1; i >= 0; i--)
            if (clock - foot.Presses[i] > keep) foot.Presses.RemoveAt(i);
    }

    private void Hit(FootNote foot, BalanceJudgement judgement)
    {
        var settings = ctx.AbilitySettings;
        targetBeat = Mathf.MoveTowards(targetBeat, difficulty.QuickestBeat, settings.balanceTempoStep);
        if (Fixtures.BalanceSlips > 0 && ++goodHits >= settings.balanceSlipRecovery)
        {
            Fixtures.BalanceSlips--;
            goodHits = 0;
        }
        Report(foot, judgement);
    }

    private bool Miss(FootNote foot)
    {
        goodHits = 0;
        targetBeat = difficulty.Beat;
        Fixtures.BalanceSlips++;
        Fixtures.BalanceFallSide = foot.Side;
        Report(foot, BalanceJudgement.Miss);
        return Fixtures.BalanceSlips >= difficulty.SlipLimit;
    }

    private void Report(FootNote foot, BalanceJudgement judgement)
    {
        Fixtures.BalanceNoteSide = foot.Side;
        ctx.RaiseBalanceBeat(judgement);
    }

    private float Ring(FootNote foot)
    {
        float late = clock - foot.LandedAt;
        if (foot.Pending && late <= difficulty.GoodWindow)
            return -late / Mathf.Max(.05f, Fixtures.BalanceBeatTime);
        if (!hasCycle || !foot.Calibrated) return 1f;
        float span = Mathf.Clamp(ctx.AbilitySettings.balanceApproachBeats * .5f, .05f, 1f);
        float ring = Mathf.Clamp((foot.DueCycle - cycle) / span, .025f, 1f);
        if (ring < 1f && !foot.Shown)
        {
            foot.Shown = true;
            foot.ShownAt = clock;
        }
        return ring;
    }

    private void Publish()
    {
        Fixtures.BalanceLeftRing = Ring(left);
        Fixtures.BalanceRightRing = Ring(right);
        if (left.Pending && !left.Judged) Fixtures.BalanceNoteSide = -1;
        else if (right.Pending && !right.Judged) Fixtures.BalanceNoteSide = 1;
        else if (left.Calibrated || right.Calibrated)
            Fixtures.BalanceNoteSide = !right.Calibrated || (left.Calibrated && left.DueCycle <= right.DueCycle) ? -1 : 1;
        else Fixtures.BalanceNoteSide = 0;
    }
}
