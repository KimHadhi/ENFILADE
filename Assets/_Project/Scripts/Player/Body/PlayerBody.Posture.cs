using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Down into a crouch and up out of it, on BIPER Female Runner's own clips (IdleToCrouch, CrouchToIdle; 0.9.285). One measure
/// says how far down the body is (postureDown), and the clip of the way it goes takes it there at its own pace, 0.4 s down and
/// 0.5 s up, whether the body stands or moves: standing still the clip itself shows, moving the two stances' cycles share the
/// legs by the same measure (Locomotion).
/// Until 0.9.356 a moving body's cycles crossed over in 0.3 s instead, each at its own rate: aside and at a slant, where two or
/// three of them share the legs, the body was down in a tenth of a second (the user, 2026-11-23: "횡으로 이동할 때 앉으면
/// 이런지 모르겠는데 가끔 앉기가 빠르게 될 때가 있고"), and a body that moved off half way down went over to that.
/// The key lets a way begun go to its end (MovementActions.KeyTurnsStance); whatever else turns it back, the other clip goes
/// on from the height the body is at. Until 0.9.348 the clips followed the eyes' ease (Posture.transitionSharpness), half the
/// way in 0.05 s (2026-11-22: "걍 한번에 확 내려가고").
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Tooltip("Down into a crouch and up out of one, at their own pace.")]
    private string crouchDown = "IdleToCrouch", standUp = "CrouchToIdle";

    // Its body sets off this far off where it begins and is there this near where it ends (share of its way).
    private const float SetsOff = .02f;
    // Down or up in this long where the body has no clip for it (s).
    private const float PlainPosture = .4f;
    // How far down the body is (0 standing, 1 crouched), the moment its clip is at (s), and whether it is on its way.
    private float postureDown, postureMoment;
    private bool posturing;

    /// <summary>How long going down into a crouch, or up out of it, takes (s).</summary>
    internal float PostureSeconds(bool down)
    {
        var motion = PostureMotion(down);
        return motion != null ? PostureEnd(motion, down) - PostureEnd(motion, !down) : PlainPosture;
    }

    private ClipMotion PostureMotion(bool down) => MotionOf(down ? crouchDown : standUp);

    // The moment a posture clip's body is crouched, or stands.
    private static float PostureEnd(ClipMotion motion, bool down) => motion.MomentAt(down ? SetsOff : 1f - SetsOff);

    // From Enter: on the ground a body goes down or gets up from the height it is at.
    private void StartPosture(MovementState from, MovementState to, bool onGround)
    {
        bool down = to == MovementState.Crouching;
        if (!onGround || !(down ? from == MovementState.Standing : from == MovementState.Crouching && to == MovementState.Standing))
        {
            posturing = false;
            return;
        }
        if (!posturing) postureDown = down ? 0f : 1f;
        posturing = true;
        var motion = PostureMotion(down);
        postureMoment = motion != null ? motion.MomentAt(Mathf.Clamp(1f - postureDown, SetsOff, 1f - SetsOff)) : 0f;
    }

    // From ChoosePose, standing or crouched: how far down the body is now.
    private void TickPosture(bool crouched, float dt)
    {
        float end = crouched ? 1f : 0f;
        if (posturing)
        {
            var motion = PostureMotion(crouched);
            postureMoment += dt;
            if (motion == null) postureDown = Mathf.MoveTowards(postureDown, end, dt / PlainPosture);
            else if (postureMoment < PostureEnd(motion, crouched)) postureDown = 1f - Mathf.Clamp01(motion.HeightAt(postureMoment));
            else postureDown = end;
            posturing = postureDown != end;
        }
        else postureDown = end;
    }

    // Standing still on its way, the clip itself at the moment the body is at. False once it is there or moves off: the stances'
    // own sets take over, by the same measure.
    private bool ShowPosture(bool crouched)
    {
        if (!posturing || HorizontalSpeed() > StillTravel) return false;
        string clip = crouched ? crouchDown : standUp;
        var channel = Find(clip);
        var motion = PostureMotion(crouched);
        if (channel == null || channel.Synced || motion == null) return false;
        Drive(clip, postureMoment / motion.Length);
        return true;
    }
}
