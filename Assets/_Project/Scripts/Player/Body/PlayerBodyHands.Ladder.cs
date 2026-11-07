using UnityEngine;

/// <summary>Persistent holds in the ladder's space. Only the reaching hand changes rungs; the support never follows the clip.</summary>
public sealed partial class PlayerBodyHands
{
    [Header("Ladder grip")]
    [SerializeField, Min(.01f), Tooltip("Distance between the hands across the ladder (m), limited by the rails.")]
    private float ladderHandSpacing = .32f;
    [SerializeField, Min(0f), Tooltip("How far the reaching hand clears the front of the rungs (m).")]
    private float ladderReachArc = .08f;
    [SerializeField, Min(0f), Tooltip("Palm clearance from the front of the rung (m).")]
    private float ladderPalmClearance = .0015f;
    [SerializeField, Tooltip("Top-exit animation fractions when the high hand, then the supporting hand, starts letting go (0–1).")]
    private Vector2 ladderExitRelease = new Vector2(.18f, .24f);
    private const float ExitGatherEnd = .08f, ExitReleaseFade = .12f;
    private const float RailSectionAngle = -45f;

    private sealed class LadderHand
    {
        public int Rung, PreviousRung;
        public Vector3 From, Target, SlideFrom, LastContact;
        public float Progress = 1f;
        public bool Stepping, HasContact;
    }

    private readonly LadderHand leftLadderHand = new LadderHand(), rightLadderHand = new LadderHand();
    private Ladder heldLadder;
    private int ladderStepsSeen;
    private bool wasOnLadder, wasSliding;
    private bool ladderTopExit, ladderExitFirstLeft;
    private float ladderExitProgress, ladderSlideBlend, ladderCatchDuration;

    private void UpdateLadder(bool onLadder, bool leavingLadder, float dt)
    {
        if (leavingLadder) { UpdateLadderExit(); wasOnLadder = false; return; }
        if (!onLadder) { wasOnLadder = false; return; }
        ladderTopExit = false;
        var fixtures = player.Abilities.Fixtures;
        bool sliding = fixtures.LadderSliding;
        bool fresh = !wasOnLadder || heldLadder != fixtures.Ladder;
        bool startSlide = sliding && (!wasSliding || fresh);
        if (fresh || (wasSliding && !sliding))
        {
            if (fresh)
            {
                ladderSlideBlend = 0f; wasSliding = false;
                leftLadderHand.HasContact = rightLadderHand.HasContact = false;
            }
            heldLadder = fixtures.Ladder;
            ladderStepsSeen = fixtures.LadderSteps;
            // The last climbing hand starts one real rung above the other. Choose the pair once, never every frame.
            // Entry/catch clips can start with lowered shoulders. Use the fitted standing eye height so their
            // opening pose cannot choose a pair that becomes unreachable once the hanging pose settles.
            // From the top (backlog 141) the hands take the rungs for where the body ends up, not the floor above.
            Vector3 feet = fresh && fixtures.LadderFromTop ? fixtures.LadderTopHold : player.transform.position;
            Vector3 highGrip = feet + heldLadder.transform.up * (player.runtimeBodyDefaults.w - .05f);
            int high = Mathf.Clamp(heldLadder.NearestRung(highGrip), Mathf.Min(2, heldLadder.RungCount), heldLadder.RungCount);
            Catch(leftLadderHand, left, true, fixtures.LadderLeftHand ? high : Mathf.Max(1, high - 1));
            Catch(rightLadderHand, right, false, fixtures.LadderLeftHand ? Mathf.Max(1, high - 1) : high);
            ladderCatchDuration = !fresh || fixtures.LadderSteps > 0 ? blendTime
                : fixtures.LadderCaught ? player.AbilitySettings.ladderCatchTime
                : fixtures.LadderFromTop ? player.AbilitySettings.ladderTopEnterTime : player.AbilitySettings.ladderEnterTime;
        }
        if (startSlide)
        {
            float remaining = Mathf.Max(.001f, 1f - SlideShare);
            leftLadderHand.SlideFrom = (SampleLadderContact(leftLadderHand, left, true) - RailPoint(left, true)) / remaining;
            rightLadderHand.SlideFrom = (SampleLadderContact(rightLadderHand, right, false) - RailPoint(right, false)) / remaining;
        }
        wasOnLadder = true;
        wasSliding = sliding;
        ladderSlideBlend = Mathf.MoveTowards(ladderSlideBlend, sliding ? 1f : 0f, dt / Mathf.Max(.01f, blendTime));

        if (sliding)
        {
            return;
        }
        if (fixtures.LadderSteps != ladderStepsSeen)
        {
            LadderHand moving = fixtures.LadderLeftHand ? leftLadderHand : rightLadderHand;
            LadderHand support = fixtures.LadderLeftHand ? rightLadderHand : leftLadderHand;
            moving.From = SampleLadderContact(moving, fixtures.LadderLeftHand ? left : right, fixtures.LadderLeftHand);
            moving.PreviousRung = moving.Rung;
            moving.Rung = Mathf.Min(support.Rung + 1, heldLadder.RungCount);
            moving.Target = LocalRung(moving.Rung, fixtures.LadderLeftHand);
            moving.Progress = 0f;
            moving.Stepping = true;
            support.Stepping = false;
            ladderStepsSeen = fixtures.LadderSteps;
        }
        var activeHand = fixtures.LadderLeftHand ? leftLadderHand : rightLadderHand;
        if (fixtures.LadderStepBlocked && activeHand.Stepping)
        {
            var moving = fixtures.LadderLeftHand ? leftLadderHand : rightLadderHand;
            moving.From = SampleLadderContact(moving, fixtures.LadderLeftHand ? left : right, fixtures.LadderLeftHand);
            moving.Rung = moving.PreviousRung;
            moving.Target = LocalRung(moving.Rung, fixtures.LadderLeftHand);
            moving.Progress = 0f;
            moving.Stepping = false;
            ladderCatchDuration = blendTime;
        }
        // Both returning hands finish independently. Starting a new rung must not freeze the other catch.
        Advance(leftLadderHand);
        Advance(rightLadderHand);
        void Advance(LadderHand hand)
        {
            hand.Progress = hand.Stepping ? fixtures.LadderStepProgress
                : Mathf.MoveTowards(hand.Progress, 1f, dt / Mathf.Max(.01f, ladderCatchDuration));
        }
    }

    private void UpdateLadderExit()
    {
        var traversal = player.Abilities.Traversal;
        var ladder = traversal.Obstacle.ExitLadder;
        if (!ladderTopExit || heldLadder != ladder)
        {
            ladderExitFirstLeft = player.Abilities.Fixtures.LadderLeftHand;
            if (heldLadder != ladder || !wasOnLadder)
            {
                // Handles a character/model rebind during the exit without borrowing an unrelated old hold.
                heldLadder = ladder;
                int high = heldLadder.NearestRung(traversal.Obstacle.Start + heldLadder.transform.up * (player.runtimeBodyDefaults.w - .05f));
                Catch(leftLadderHand, left, true, high);
                Catch(rightLadderHand, right, false, high);
            }
            int top = Mathf.Max(leftLadderHand.Rung, rightLadderHand.Rung);
            Gather(leftLadderHand, true, top);
            Gather(rightLadderHand, false, top);
            wasSliding = false;
            ladderSlideBlend = 0f;
            ladderTopExit = true;
        }
        ladderExitProgress = traversal.ObstacleProgress;
        float gather = Mathf.Clamp01(ladderExitProgress / ExitGatherEnd);
        if (leftLadderHand.Progress < 1f) leftLadderHand.Progress = gather;
        if (rightLadderHand.Progress < 1f) rightLadderHand.Progress = gather;
        // Off the top rung onto the rails (ExitRailPoint), as the climb takes the body up past the rungs.
        exitRail = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ExitGatherEnd, ExitGatherEnd + ExitRailBlend, ladderExitProgress));
        exitReach[0] = RailInReach(left, true);
        exitReach[1] = RailInReach(right, false);
    }

    // Climbing off the top, the hands hold the ladder's bars the whole way (user, 2026-11-08: the hands on the bar, not the
    // roof): off the top rung onto the rail on their side, each where the clip holds the hand, no higher than the rail's
    // top end, and each lets go only when its rail is out of the arm's reach as the body stands, or by the climb's end.
    private const float ExitRailBlend = .15f;
    private float exitRail;
    private readonly float[] exitReach = { 1f, 1f };

    private Vector3 ExitRailPoint(ArmIK arm, bool isLeft)
    {
        Vector3 up = heldLadder.transform.up;
        float height = Vector3.Dot(arm.Hand.position - heldLadder.transform.position, up);
        // A climber faces -Outward, so its left side is the ladder's positive X (LocalRung).
        Vector3 rail = heldLadder.transform.position + up * Mathf.Clamp(height, 0f, heldLadder.Height - Ladder.BarThickness)
            + heldLadder.transform.right * (heldLadder.Width * .5f * (isLeft ? 1f : -1f));
        return heldLadder.transform.InverseTransformPoint(rail);
    }

    // 1 while the hand's place on the rail is within the arm's reach, easing to 0 as it goes past it.
    private float RailInReach(ArmIK arm, bool isLeft)
    {
        if (arm == null || arm.Upper == null || heldLadder == null) return 1f;
        float distance = Vector3.Distance(heldLadder.transform.TransformPoint(ExitRailPoint(arm, isLeft)), arm.Upper.position);
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(arm.Reach * .95f, arm.Reach * 1.05f, distance));
    }

    // Holding a rail rather than a rung: sliding down, or climbing off the top.
    private float RailShare => Mathf.Max(SlideShare, ladderTopExit ? exitRail : 0f);

    private void Gather(LadderHand hold, bool isLeft, int rung)
    {
        hold.From = SampleLadderHand(hold);
        hold.Target = LocalRung(rung, isLeft);
        hold.Progress = (hold.Target - hold.From).sqrMagnitude < 1e-6f ? 1f : 0f;
        hold.Rung = rung;
        hold.Stepping = false;
    }

    // Keep the cached exit progress after the state ends, so fading out cannot re-grab a released rung.
    private float LadderHandWeight(bool isLeft)
    {
        if (!ladderTopExit) return 1f;
        // On the rails until out of reach (RailInReach), and off by the end of the climb.
        return exitReach[isLeft ? 0 : 1] * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.85f, 1f, ladderExitProgress)));
    }

    // Before the eye camera is placed, keep supporting wrists within reach. A retargeted exit clip can raise
    // the shoulders ahead of the capsule's climb; hanging from the rung must then carry the visible body too.
    internal Vector3 LadderExitReachOffset()
    {
        var ladder = player.Abilities.Traversal.Obstacle.ExitLadder;
        if (ladder == null || !ladder.isActiveAndEnabled || heldLadder != ladder
            || left == null || right == null || left.Upper == null || right.Upper == null) return Vector3.zero;
        UpdateLadderExit();
        Vector3 offset = Vector3.zero;
        for (int i = 0; i < 4; i++) { Fit(left, true); Fit(right, false); }
        // On the rails the hands let go out of reach instead of holding the body back (LadderHandWeight).
        return offset * (1f - exitRail);

        void Fit(ArmIK arm, bool isLeft)
        {
            float weight = LadderHandWeight(isLeft);
            if (weight <= 0f || !LadderTarget(arm, isLeft, out Vector3 wrist, out _, out _)) return;
            Vector3 reach = wrist - arm.Upper.position - offset;
            float distance = reach.magnitude, limit = arm.Reach * .98f;
            if (distance > limit) offset += reach * ((distance - limit) / distance) * weight;
        }
    }

    private Vector3 LocalRung(int rung, bool isLeft)
    {
        // A climber faces -Outward, so its left side is the ladder's positive X.
        var arm = isLeft ? left : right;
        float across = Mathf.Min(ladderHandSpacing * .5f, Mathf.Max(0f, heldLadder.Width * .5f - Ladder.BarThickness * .5f - arm.HalfPalmWidth));
        return heldLadder.transform.InverseTransformPoint(heldLadder.RungPoint(rung, across * (isLeft ? 1f : -1f)));
    }

    private void Catch(LadderHand hold, ArmIK arm, bool isLeft, int rung)
    {
        hold.Rung = hold.PreviousRung = rung;
        hold.Stepping = false;
        // Store palm contact, not the wrist, so different characters can share the same rung.
        LadderFrame(isLeft, out _, out Vector3 palmFacing);
        Vector3 palm = arm.PalmPosition + palmFacing * (LadderSectionDepth() + arm.PalmDepth + ladderPalmClearance);
        // The clip has already re-posed the arm this frame. Continue from the last displayed hold,
        // not that transient animated wrist, when returning from a slide.
        hold.From = hold.HasContact ? hold.LastContact : heldLadder.transform.InverseTransformPoint(palm);
        hold.Target = LocalRung(rung, isLeft);
        hold.Progress = 0f;
    }

    private Vector3 RailPoint(ArmIK arm, bool isLeft)
    {
        Vector3 up = heldLadder.transform.up;
        // Slide with the hands below the shoulders, away from the eyes even on narrow ladders.
        float height = Vector3.Dot(arm.Upper.position - heldLadder.transform.position, up) - arm.Reach * .25f;
        Vector3 rail = heldLadder.transform.position + up * Mathf.Clamp(height, 0f, heldLadder.Height)
            + heldLadder.transform.right * (heldLadder.Width * .5f * (isLeft ? 1f : -1f));
        return heldLadder.transform.InverseTransformPoint(rail);
    }

    private float SlideShare => Mathf.SmoothStep(0f, 1f, ladderSlideBlend);

    private Vector3 SampleLadderContact(LadderHand hold, ArmIK arm, bool isLeft)
    {
        if (ladderTopExit) return Vector3.Lerp(SampleLadderHand(hold), ExitRailPoint(arm, isLeft), exitRail);
        Vector3 rail = RailPoint(arm, isLeft);
        return wasSliding ? rail + hold.SlideFrom * (1f - SlideShare) : SampleLadderHand(hold);
    }

    private float LadderSectionDepth()
    {
        float angle = RailSectionAngle * RailShare * Mathf.Deg2Rad;
        return Ladder.BarThickness * .5f * (Mathf.Abs(Mathf.Cos(angle)) + Mathf.Abs(Mathf.Sin(angle)));
    }

    private void LadderFrame(bool isLeft, out Vector3 fingers, out Vector3 palmFacing)
    {
        Vector3 outward = heldLadder.Outward, side = heldLadder.transform.right * (isLeft ? 1f : -1f);
        var frame = Quaternion.Slerp(Quaternion.LookRotation(heldLadder.transform.up, -outward),
            Quaternion.LookRotation((side - outward).normalized, (-side - outward).normalized), RailShare);
        fingers = frame * Vector3.forward;
        palmFacing = frame * Vector3.up;
    }

    private Vector3 SampleLadderHand(LadderHand hold)
    {
        float progress = Mathf.SmoothStep(0f, 1f, hold.Progress);
        Vector3 arc = heldLadder.transform.InverseTransformVector(heldLadder.Outward * ladderReachArc);
        return Vector3.Lerp(hold.From, hold.Target, progress) + arc * Mathf.Sin(progress * Mathf.PI);
    }

    private bool LadderTarget(ArmIK arm, bool isLeft, out Vector3 position, out Quaternion rotation, out Vector3 hint)
    {
        position = hint = Vector3.zero;
        rotation = Quaternion.identity;
        if (heldLadder == null) return false;
        Vector3 up = heldLadder.transform.up, outward = heldLadder.Outward;
        Vector3 side = heldLadder.transform.right * (isLeft ? 1f : -1f);
        var hold = isLeft ? leftLadderHand : rightLadderHand;
        // Hold the outside front corner, letting the fingers follow the forearms toward the ladder.
        // This keeps thumb opposition without bending the wrists sideways across the view.
        LadderFrame(isLeft, out Vector3 fingers, out Vector3 palmFacing);
        rotation = arm.HandRotation(fingers, palmFacing);
        Vector3 contact = SampleLadderContact(hold, arm, isLeft);
        hold.LastContact = contact;
        hold.HasContact = true;
        Vector3 palm = heldLadder.transform.TransformPoint(contact)
            - palmFacing * (LadderSectionDepth() + arm.PalmDepth + ladderPalmClearance);
        position = arm.WristAtPalm(palm, rotation);
        // Let the elbows drop outside the torso instead of pulling them in toward the camera.
        hint = arm.Upper.position + side * Mathf.Lerp(.3f, .15f, RailShare) + outward * .12f - up * Mathf.Lerp(.25f, .35f, RailShare);
        return true;
    }

    private void CurlLadderFingers(ArmIK arm, bool isLeft)
    {
        if (heldLadder == null || weights[(int)Hold.Ladder] <= 0f) return;
        var hold = isLeft ? leftLadderHand : rightLadderHand;
        // Open only the travelling hand, then close it again as it lands on the next rung.
        float progress = Mathf.Clamp01(hold.Progress);
        float grip = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.65f, 1f, progress));
        if (hold.Stepping) grip += 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .2f, progress));
        // The hand lets go between the horizontal rung and the vertical rail; don't twist a closed fist.
        float transfer = Mathf.Sin(RailShare * Mathf.PI);
        grip = Mathf.Lerp(Mathf.Clamp01(grip), 1f, RailShare) * (1f - transfer);
        arm.GripBar(Ladder.BarThickness * .5f, ladderPalmClearance, fingerSurfaceClearance,
            weights[(int)Hold.Ladder] * LadderHandWeight(isLeft), grip, RailSectionAngle * RailShare);
    }
}
