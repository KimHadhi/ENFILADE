using UnityEngine;
using UnityEngine.Playables;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The body on the world's ladders, ziplines and balance beams, with Beatrun's clips: taking hold, a clip for each rung
/// by the hand going up, the fast slide down and the climb over the top; the ride; and the walk with arms out, leaning
/// a little with the tilt, one stride to a beat. Beatrun's walkbalancelosebalance clips stay unused: they throw the head about 30 cm to each
/// side, and with the camera on the eyes that swing made players sick. Names and numbers come from Player.csv, row Body.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Ladder, zipline, balance beam")]
    [SerializeField, Tooltip("Taking hold of a ladder from the ground; catching one in the air.")]
    private string ladderEnter = "ladderenterbottom", ladderCatch = "ladderclimbhangstart";
    [SerializeField, Tooltip("One rung up, by the hand going up; then holding still with that hand high.")]
    private string ladderLeft = "ladderclimbuplefthand", ladderRight = "ladderclimbuprighthand";
    [SerializeField] private string ladderStillLeft = "ladderclimbuplefthandstill", ladderStillRight = "ladderclimbuprighthandstill";
    [SerializeField] private string ladderDown = "ladderclimbdownfast";
    [SerializeField, Tooltip("Climbing off the top, by the hand that went up last.")]
    private string ladderExitLeft = "ladderexittoplefthand", ladderExitRight = "ladderexittoprighthand";
    [SerializeField] private string ziplineStart = "ziplinestart", zipline = "zipline";
    [SerializeField] private string balanceWalk = "walkbalancefwd", balanceStill = "walkbalancestill";
    [SerializeField] private string balanceFallLeft = "walkbalancefalloffleft", balanceFallRight = "walkbalancefalloffright";
    [SerializeField, Range(0f, 45f), Tooltip("How far the whole body leans at the point of falling off a beam (degrees). "
        + "The camera rides the eyes, so each degree swings the view about 3 cm sideways: keep it small or it makes people sick.")]
    private float balanceLean = 3f;
    [SerializeField, Range(0f, 180f), Tooltip("On a ladder, a zipline or a beam, look limit either side of the way the body faces (degrees).")]
    private float fixtureTurnLimit = 90f;

    public float FixtureLookYawLimit => Mathf.Clamp(fixtureTurnLimit, 0f, 180f);

    private string balanceFallClip;
    private float ladderStepAt, bodyLean;
    private int ladderStepsSeen;

    private string[] FixtureClips => new[]
    {
        ladderEnter, ladderCatch, ladderLeft, ladderRight, ladderStillLeft, ladderStillRight, ladderDown, ladderExitLeft, ladderExitRight,
        ziplineStart, zipline, balanceWalk, balanceStill, balanceFallLeft, balanceFallRight
    };

    private void StartLadder()
    {
        ladderStepsSeen = 0;
        Restart(Find(player.Abilities.Fixtures.LadderCaught ? ladderCatch : ladderEnter));
    }

    private void StartBalanceFall()
    {
        balanceFallClip = player.Abilities.Fixtures.BalanceFallSide >= 0 ? balanceFallRight : balanceFallLeft;
        Restart(Find(balanceFallClip));
    }

    private string LadderExitClip() => player.Abilities.Fixtures.LadderLeftHand ? ladderExitLeft : ladderExitRight;

    private void LadderPose()
    {
        var fixtures = player.Abilities.Fixtures;
        if (fixtures.Ladder != null) targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, fixtures.Ladder.ClimberYaw);
        if (fixtures.LadderSliding) { Want(ladderDown); return; }
        string step = fixtures.LadderLeftHand ? ladderLeft : ladderRight, still = fixtures.LadderLeftHand ? ladderStillLeft : ladderStillRight;
        if (fixtures.LadderSteps == 0)
        {
            Sequence(fixtures.LadderCaught ? ladderCatch : ladderEnter, still);
            return;
        }
        if (fixtures.LadderSteps != ladderStepsSeen)
        {
            ladderStepsSeen = fixtures.LadderSteps;
            ladderStepAt = stateTime;
            Restart(Find(step));
        }
        var climbing = Find(step);
        Want(climbing != null && stateTime - ladderStepAt < climbing.Length - blendTime ? step : still);
    }

    private void ZiplinePose()
    {
        var fixtures = player.Abilities.Fixtures;
        targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, MovementFixtures.YawOf(fixtures.ZiplineDirection));
        Sequence(ziplineStart, zipline);
    }

    // The clip makes the steps; post-IK foot contacts own the judgement and sound. Changing pace never snaps its phase.
    private void BalancePose()
    {
        var fixtures = player.Abilities.Fixtures;
        targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, MovementFixtures.YawOf(fixtures.BalanceFacing));
        var walking = Find(balanceWalk);
        if (walking != null)
        {
            walking.Playable.SetSpeed(fixtures.BalanceTurning ? 0f : walking.Length / (2f * Mathf.Max(.05f, fixtures.BalanceBeatTime)));
            if (walking.Playable.GetTime() >= walking.Length)
            {
                walking.Playable.SetTime(Mathf.Repeat((float)walking.Playable.GetTime(), walking.Length));
                walking.Playable.SetDone(false);
            }
        }
        Want(fixtures.BalanceTurning ? balanceStill : balanceWalk);
    }

    // Read the phase after animation evaluation, before PlayerFootsteps observes this frame's contacts.
    // Preview timing can then follow the real cycle through pace changes without guessing footfall at 0 or .5.
    private void PublishBalanceCycle()
    {
        var fixtures = player.Abilities.Fixtures;
        if (!fixtures.BalanceActive || fixtures.BalanceTurning) return;
        if (state == MovementState.Balance)
        {
            var walking = Find(balanceWalk);
            if (walking != null && walking.Weight >= .999f)
                fixtures.ObserveBalanceCycle(Mathf.Repeat((float)(walking.Playable.GetTime() / walking.Length), 1f));
        }
        // Do not calibrate a ring against the transient foot pose while the entry gait crossfade settles.
        else if (fixtures.Run.IsActive && beamRunEntry == fixtures.Run.EntrySerial
            && beamRunChannel != null && beamRunTime >= blendTime)
            fixtures.ObserveBalanceCycle(phase);
    }

    // The whole body tips over its feet, about the way it faces.
    private Quaternion BalanceLean(float dt)
    {
        var fixtures = player.Abilities.Fixtures;
        float wanted = fixtures.BalanceActive ? fixtures.Balance * .01f * balanceLean : 0f;
        bodyLean = Mathf.Lerp(bodyLean, wanted, 1f - Mathf.Exp(-dt / blendTime));
        return Quaternion.AngleAxis(-bodyLean, Vector3.forward);
    }

    // On a beam the feet stay on it, so the body stands right over the capsule instead of following the leaning head.
    private void CentreOverFeet(float dt) => headAlignment *= Mathf.Exp(-dt / headFollowTime);
}
