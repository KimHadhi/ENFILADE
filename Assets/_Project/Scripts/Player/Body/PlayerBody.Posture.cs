using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Down into a crouch and up out of it standing still, on BIPER Female Runner's own clips (IdleToCrouch, CrouchToIdle;
/// 0.9.285, the user: "Female Runner first", the pack's clips not yet in the game). Until then the two stances' still
/// poses blended into each other in a straight line. Each clip plays at its own pace, 0.4 s down and 0.5 s up, from the
/// moment its body sets off, and turned back half way the
/// other goes on from the height the body is at: how far of its way down it is (postureDown) is the clips' common measure,
/// so neither begins at its far end. Until 0.9.348 they followed the eyes' ease (Posture.transitionSharpness), so the view
/// would not lag the key: half the way in 0.05 s, 21 % of it in the first frame, the chest dropped and shot up in one go
/// (the user, 2026-11-22: "원래 애니메이션 속도 이렇게 빨라? 가슴이 앉을 때 자연스럽게 내려가는 게 아니라 걍 한번에 확 내려가고").
/// Standing still only: moving, the walk's crouched cycles take the body down and up, over about as long (MovingPosture).
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Tooltip("Standing still, down into a crouch and up out of one, at their own pace.")]
    private string crouchDown = "IdleToCrouch", standUp = "CrouchToIdle";

    // Its body sets off this far off where it begins and is there this near where it ends (share of its way).
    private const float SetsOff = .02f;
    // The posture clip under way, or null, and how far down the body is (0 standing, 1 crouched).
    private string postureClip;
    private float postureDown;
    // Moving, the stances' cycles cross over this slowly (s), about as long as the clips take.
    private const float MovingPosture = .3f;

    // From Enter, on the ground: a body standing still going down or getting up takes its clip, from the height it is at; one
    // moving, the cycles.
    private void StartPosture(MovementState from, MovementState to)
    {
        bool turnsBack = postureClip != null;
        postureClip = null;
        string clip = from == MovementState.Standing && to == MovementState.Crouching ? crouchDown
            : from == MovementState.Crouching && to == MovementState.Standing ? standUp : null;
        if (clip == null) return;
        if (HorizontalSpeed() > StillTravel || MotionOf(clip) == null || Find(clip) == null)
        {
            SlowBlend(MovingPosture);
            return;
        }
        if (!turnsBack) postureDown = from == MovementState.Standing ? 0f : 1f;
        Restart(Find(clip));
        postureClip = clip;
    }

    // From ChoosePose, standing or crouched on the ground: the clip a step on from where the body is in it. False once it is
    // there, or the body moves off (the stance's own moves take over, blended).
    private bool FollowPosture(float dt)
    {
        if (postureClip == null) return false;
        var channel = Find(postureClip);
        bool goesDown = postureClip == crouchDown;
        var motion = MotionOf(postureClip);
        if (channel == null || channel.Synced || motion == null)
        {
            postureClip = null;
            return false;
        }
        if (HorizontalSpeed() > StillTravel)
        {
            postureClip = null;
            SlowBlend(MovingPosture);
            return false;
        }
        // How high the body is (0 crouched, 1 standing) is both clips' common measure.
        float moment = motion.MomentAt(Mathf.Clamp(1f - postureDown, SetsOff, 1f - SetsOff)) + dt;
        if (moment >= motion.MomentAt(goesDown ? SetsOff : 1f - SetsOff))
        {
            postureDown = goesDown ? 1f : 0f;
            postureClip = null;
            return false;
        }
        postureDown = 1f - Mathf.Clamp01(motion.HeightAt(moment));
        Drive(postureClip, moment / motion.Length);
        return true;
    }
}
