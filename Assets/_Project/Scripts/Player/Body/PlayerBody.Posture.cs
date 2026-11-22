using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Down into a crouch and up out of it, on BIPER Female Runner's own clips (IdleToCrouch, CrouchToIdle; 0.9.285). One measure
/// says how far down the body is (postureDown), and the clip of the way it goes takes it there at its own pace, 0.4 s down and
/// 0.5 s up, whether the body stands or moves: the part of it that stands plays the clip itself, the part that moves the two
/// stances' cycles, shared by the same measure (Locomotion).
/// Until 0.9.356 a moving body's cycles crossed over in 0.3 s instead, each at its own rate: aside and at a slant, where two or
/// three of them share the legs, the body was down in a tenth of a second (the user, 2026-11-23: "횡으로 이동할 때 앉으면
/// 이런지 모르겠는데 가끔 앉기가 빠르게 될 때가 있고"), and a body that moved off half way down went over to that.
/// Until 0.9.357 the clip took the whole body once it stood still and nothing else of the legs went on meanwhile: the speed they
/// had still stopping in (stride) was kept, the feet and the way the body faces were let go of and caught again, and the body
/// took a step as the clip ended (the user, 2026-11-23: "움직이면서 앉다가 속도 0인채 일어나면 덜컥 거리는 느낌").
/// Turned back on its way, the other clip goes on at once from the height the body is at. Nothing stands between the key and
/// that: the pace turning over in 0.2 s, slowing when tapped over and over, taps too soon left out and a way begun going to its
/// end were each tried on 2026-11-23 (0.9.353-0.9.357), the lean's alike, and taken out (the user: "그 앉기랑 기울이기에 뭐 이상한
/// 기능 넣지 말고"). Until 0.9.348 the clips followed the eyes' ease (Posture.transitionSharpness), half the way in 0.05 s
/// (2026-11-22: "걍 한번에 확 내려가고").
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
    // While the legs are shared out (Locomotion), the clip that has the part of them that stands; null for the stances' idles.
    private Channel postureClip;

    private ClipMotion PostureMotion(bool down) => MotionOf(down ? crouchDown : standUp);

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
            else if (postureMoment < motion.MomentAt(crouched ? SetsOff : 1f - SetsOff)) postureDown = 1f - Mathf.Clamp01(motion.HeightAt(postureMoment));
            else postureDown = end;
            posturing = postureDown != end;
        }
        else postureDown = end;
    }

    // The clip of the way the body goes, held at the moment it is at; null once it is there.
    private Channel PostureClip(bool crouched)
    {
        if (!posturing) return null;
        var channel = Find(crouched ? crouchDown : standUp);
        var motion = PostureMotion(crouched);
        if (channel == null || channel.Synced || motion == null) return null;
        HoldAt(channel, postureMoment / motion.Length);
        return channel;
    }
}
