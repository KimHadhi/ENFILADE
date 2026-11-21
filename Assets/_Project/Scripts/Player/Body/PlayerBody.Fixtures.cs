using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// The body on the world's ladders and ziplines: taking hold, a clip for each rung by the hand going up, the fast
/// slide down and the climb over the top, and the ride. All of the ladder is Beatrun's one set: each rung clip ends in the
/// pose the other starts from and its still holds that pose, the getting-on clips end in a still, and the head stays at
/// one height through all of them, so the view stays on the eyes there as everywhere else. Played at their own pace (a
/// rung clip is about Ability.ladderRungInterval long). Until 0.9.301 the rungs were MoCap Online's two-rung loop halved
/// and played twice as fast, its head bobbing 0.15 m, and to hide that the view left the eyes on a ladder while the body
/// was moved up or down under it (user, 2026-11-22: scrap the ladder's patchwork, make it natural).
/// Names come from Player.csv, row Body.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Ladder, zipline")]
    [SerializeField, Tooltip("Taking hold of a ladder from the ground; catching one in the air.")]
    private string ladderEnter = "ladderenterbottom", ladderCatch = "ladderclimbhangstart";
    [SerializeField, Tooltip("One rung up, by the hand going up; then holding still with that hand high.")]
    private string ladderLeft = "ladderclimbuplefthand", ladderRight = "ladderclimbuprighthand";
    [SerializeField] private string ladderStillLeft = "ladderclimbuplefthandstill", ladderStillRight = "ladderclimbuprighthandstill";
    [SerializeField] private string ladderDown = "ladderclimbdownfast";
    [SerializeField, Tooltip("Climbing off the top, by the hand that went up last.")]
    private string ladderExitLeft = "ladderexittoplefthand", ladderExitRight = "ladderexittoprighthand";
    [SerializeField] private string ziplineStart = "ziplinestart", zipline = "zipline";
    [SerializeField, Range(0f, 180f), Tooltip("On a ladder or a zipline, look limit either side of the way the body faces (degrees).")]
    private float fixtureTurnLimit = 90f;

    public float FixtureLookYawLimit => Mathf.Clamp(fixtureTurnLimit, 0f, 180f);

    private float ladderStepAt;
    private int ladderStepsSeen;

    private string[] FixtureClips => new[]
    {
        ladderEnter, ladderCatch, ladderLeft, ladderRight, ladderStillLeft, ladderStillRight, ladderDown, ladderExitLeft, ladderExitRight,
        ziplineStart, zipline
    };

    private void StartLadder()
    {
        ladderStepsSeen = 0;
        Restart(Find(player.Abilities.Fixtures.LadderCaught ? ladderCatch : ladderEnter));
    }

    private string LadderExitClip() => player.Abilities.Fixtures.LadderLeftHand ? ladderExitLeft : ladderExitRight;

    private void LadderPose()
    {
        var fixtures = player.Abilities.Fixtures;
        if (fixtures.Ladder != null) targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, fixtures.Ladder.ClimberYaw);
        if (fixtures.LadderSliding) { Want(ladderDown); return; }
        string step = fixtures.LadderLeftHand ? ladderLeft : ladderRight, still = fixtures.LadderLeftHand ? ladderStillLeft : ladderStillRight;
        // Getting on from the top (backlog 141): the climb off the top played backwards, from standing up there to
        // hanging on the top rungs, kept to the movement's own progress out over the edge and down.
        if (fixtures.LadderSteps == 0 && fixtures.LadderFromTop && fixtures.LadderStepProgress < 1f)
        {
            Drive(LadderExitClip(), 1f - fixtures.LadderStepProgress);
            return;
        }
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
        // The rung clip ends in its still's pose, so handing over to the still before its end changes nothing but the clock.
        var climbing = Find(step);
        Want(climbing != null && stateTime - ladderStepAt < climbing.Length - blendTime ? step : still);
    }

    private void ZiplinePose()
    {
        var fixtures = player.Abilities.Fixtures;
        targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, MovementFixtures.YawOf(fixtures.ZiplineDirection));
        Sequence(ziplineStart, zipline);
    }
}
