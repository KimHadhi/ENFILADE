using UnityEngine;

/// <summary>
/// The body on the world's ladders and ziplines: taking hold, a clip for each rung by the hand going up, the fast
/// slide down and the climb over the top, and the ride. Names come from Player.csv, row Body.
/// The climb itself is MoCap Online's ladder cycle (rungs 400 mm apart, 1.4 s for two), one half for each hand going up,
/// and the movement holds its clock: a rung's clip stands at the movement's own share of the rung (LadderStepProgress),
/// which rises at the even rate the in-place cycle had its rise taken out at (measured: every holding hand and foot goes
/// down the clip at 0.6 m/s), so the hands and feet stay on their rungs and the body goes up as the climber pushes and
/// reaches, not as a block moved. Each half ends in the pose the other starts from and its still holds, so the climb goes
/// on or stops at any rung without a jump. Getting on, off the top and sliding down are Beatrun's.
/// Before 0.9.302 a rung was a clip on its own clock while the capsule was moved up apart from it: 0.9.301 Beatrun's
/// 0.37 s rung clip with a rise eased over 0.35 s, before it this cycle's half at twice its pace with a 0.25 s rise, the view
/// held off the eyes and the body shifted up or down under it (user, 2026-11-22: "애니메이션이 아니라 갑자기 움직이는").
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Ladder, zipline")]
    [SerializeField, Tooltip("Taking hold of a ladder from the ground; catching one in the air.")]
    private string ladderEnter = "ladderenterbottom", ladderCatch = "ladderclimbhangstart";
    [SerializeField, Tooltip("One rung up, by the hand going up (each half of the climbing cycle, played at the movement's share "
        + "of the rung); then holding still with that hand high.")]
    private string ladderLeft = "LDR_I400_Ascd_LU_Loop_IP", ladderRight = "LDR_I400_Ascd_RU_Loop_IP";
    [SerializeField] private string ladderStillLeft = "LDR_I400_Ascd_RU_Idle_IP", ladderStillRight = "LDR_I400_Ascd_LU_Idle_IP";
    [SerializeField] private string ladderDown = "ladderclimbdownfast";
    [SerializeField, Tooltip("Climbing off the top, by the hand that went up last.")]
    private string ladderExitLeft = "ladderexittoplefthand", ladderExitRight = "ladderexittoprighthand";
    [SerializeField] private string ziplineStart = "ziplinestart", zipline = "zipline";
    [SerializeField, Range(0f, 180f), Tooltip("On a ladder or a zipline, look limit either side of the way the body faces (degrees).")]
    private float fixtureTurnLimit = 90f;

    public float FixtureLookYawLimit => Mathf.Clamp(fixtureTurnLimit, 0f, 180f);

    // When a rung was last under way (s into the state): a held W starts the next within a tick or two.
    private float ladderRoseAt = float.NegativeInfinity;

    private string[] FixtureClips => new[]
    {
        ladderEnter, ladderCatch, ladderLeft, ladderRight, ladderStillLeft, ladderStillRight, ladderDown, ladderExitLeft, ladderExitRight,
        ziplineStart, zipline
    };

    private void StartLadder()
    {
        ladderRoseAt = float.NegativeInfinity;
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
        // A rung under way: its half of the cycle where the movement is up the rung. Ended, it stays on its last frame for as
        // long as a held W takes to start the next (the next half starts from that very pose), then gives way to the still,
        // which holds that pose too.
        float rung = fixtures.LadderStepProgress;
        if (rung < 1f) ladderRoseAt = stateTime;
        if (rung < 1f || stateTime - ladderRoseAt < blendTime) Drive(step, rung);
        else Want(still);
    }

    private void ZiplinePose()
    {
        var fixtures = player.Abilities.Fixtures;
        targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, MovementFixtures.YawOf(fixtures.ZiplineDirection));
        Sequence(ziplineStart, zipline);
    }
}
