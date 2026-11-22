using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Down into a crouch and up out of it, on BIPER Female Runner's own clips (IdleToCrouch, CrouchToIdle; 0.9.285). One measure
/// says how far down the body is (postureDown), and the clip of the way it goes takes it there at its own pace, 0.4 s down and
/// 0.5 s up, from the height it is at (ClipMotion). Standing still the clip shows; moving, the two stances' cycles share the
/// legs by the same measure (Locomotion).
/// Turned back on the way, the body does not turn in a frame: the clip's pace goes over to the other way's through a stand,
/// in Ability.turnTime. Until 0.9.353 the other clip took over at once at its full pace: crouching and standing over and over
/// the hips went from 2.6 m/s down to as much up in a frame, some 25 g, and the hair and chest were thrown about (the user,
/// 2026-11-23: "캐릭터가 너무 순식간에 움직여서 물리가 ... 텔포되고", "한 프레임만에 서로 바뀌지 않게 하는 게 더 나을 것
/// 같은데"). Until 0.9.348 the clips followed the eyes' ease, half the way in 0.05 s (2026-11-22: "걍 한번에 확 내려가고").
/// Turned back over and over it also tires, as in CS2: each turn back takes some of its pace (Ability.crouchTire) and it comes
/// back by itself, so a crouch tapped again and again only bobs the body a little (10-13 cm at ten taps a second, at any
/// frame rate), and one that is let end is never slowed (0.9.354, the user, 2026-11-23).
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Tooltip("Standing still, down into a crouch and up out of one, at their own pace.")]
    private string crouchDown = "IdleToCrouch", standUp = "CrouchToIdle";

    // Its body sets off this far off where it begins and is there this near where it ends (share of its way).
    private const float SetsOff = .005f;
    // Down or up in this long where the body has no clip for it (s).
    private const float PlainPosture = .4f;
    // How far down the body is (0 standing, 1 crouched); which way it goes and how much of that way's clip's pace it has (1 down,
    // -1 up, 0 at rest); how much of the pace it has left (1 rested); the clip's moment; and whether it is on its way.
    private float postureDown, postureWay, posturePace = 1f, postureMoment;
    private bool posturing;

    // From Enter: the stance changed. Turned back on the way, the same body goes on (TickPosture), more tired.
    private void StartPosture(MovementState from, MovementState to, bool onGround)
    {
        bool goes = onGround && (from == MovementState.Standing && to == MovementState.Crouching
            || from == MovementState.Crouching && to == MovementState.Standing);
        if (goes && posturing)
        {
            var ability = player.AbilitySettings;
            posturePace = Mathf.Max(ability.crouchTiredPace, posturePace - ability.crouchTire);
            return;
        }
        posturing = goes;
        postureWay = 0f;
        if (goes) postureDown = from == MovementState.Standing ? 0f : 1f;
    }

    // From ChoosePose, standing or crouched on the ground: how far down the body is now.
    private void TickPosture(bool crouched, float dt)
    {
        var ability = player.AbilitySettings;
        posturePace = Mathf.MoveTowards(posturePace, 1f, ability.crouchRest * dt);
        if (!posturing)
        {
            postureDown = crouched ? 1f : 0f;
            return;
        }
        postureWay = Mathf.MoveTowards(postureWay, crouched ? posturePace : -posturePace, 2f * dt / Mathf.Max(.01f, ability.turnTime));
        bool down = postureWay >= 0f, there;
        var motion = MotionOf(down ? crouchDown : standUp);
        if (motion != null)
        {
            postureMoment = motion.MomentAt(Mathf.Clamp(1f - postureDown, SetsOff, 1f - SetsOff)) + Mathf.Abs(postureWay) * dt;
            postureDown = 1f - Mathf.Clamp01(motion.HeightAt(postureMoment));
            there = postureMoment >= motion.MomentAt(down ? SetsOff : 1f - SetsOff);
        }
        else
        {
            postureDown = Mathf.Clamp01(postureDown + postureWay * dt / PlainPosture);
            there = down ? postureDown >= 1f : postureDown <= 0f;
        }
        if (!there || down != crouched) return;
        postureDown = crouched ? 1f : 0f;
        posturing = false;
    }

    // Standing still on its way, the clip itself at the moment the body is at. False once it is there or moves off: the stances'
    // own sets take over, by the same measure.
    private bool ShowPosture()
    {
        if (!posturing || HorizontalSpeed() > StillTravel) return false;
        string clip = postureWay >= 0f ? crouchDown : standUp;
        var channel = Find(clip);
        var motion = MotionOf(clip);
        if (channel == null || channel.Synced || motion == null) return false;
        Drive(clip, postureMoment / motion.Length);
        return true;
    }
}
