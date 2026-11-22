using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// The step dodge on the body: its clip whole, from the moment the movement begins it at (DodgeMove), on the clip's own
/// clock. When the legs are the player's again the rest of it, getting up, plays out on the spot as a landing does, giving
/// way slowly (dodgeStandTime) once the body moves off; one that shifts into the sprint plays out only on the run (RunsOut),
/// and one from a crouch goes into the crouch as slowly.
/// With a gun out too, the whole body: the hands are put on the gun where FPSBody keeps it in view, as in a slide. Until
/// 0.9.334 only the legs dodged under a standing hold (the user, 2026-11-09: "총 들었을 때 회피 모션은 상체 안 건들였으면",
/// on a clip whose arms flew up over the gun), so the hips stayed up while the legs went low and the motion was never seen;
/// the user then asked for the motion ("회피는 모션을 위해 개편", "QuickShift 끝까지", 2026-11-22).
/// The body turns with the view through it, so the aim stays the mouse's.
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Min(.01f), Tooltip("Out of a dodge into a walk, a run or a crouch, the clips cross over this slowly (s): the dodge is "
        + "still low as the legs are given back.")]
    private float dodgeStandTime = .25f;
    // It takes the body this quickly (s): its burst is a tenth of a second in.
    private const float DodgeTakes = .07f;
    private string dodgeClip;
    // The dodge the clip was started for (DodgeMove.Serial).
    private int dodgeSerial = -1;

    private void StartDodge()
    {
        var move = player.Abilities.DodgeMove;
        dodgeSerial = move.Serial;
        dodgeClip = move.Clip;
        var channel = Find(dodgeClip);
        if (channel == null || channel.Synced) return;
        Restart(channel);
        channel.Playable.SetTime(Mathf.Min(move.From, channel.Length));
        SlowBlend(DodgeTakes);
    }

    // From ChoosePose. A dodge chained on a confirmed one begins in the same state.
    private void DodgePose()
    {
        if (player.Abilities.DodgeMove.Serial != dodgeSerial) StartDodge();
        Want(dodgeClip);
    }

    // From Enter, the dodge over on the ground.
    private void LeaveDodge(PlayerMovement.MovementState to)
    {
        if (to == PlayerMovement.MovementState.Standing) PlayOn(dodgeClip);
        if (to == PlayerMovement.MovementState.Standing || to == PlayerMovement.MovementState.Crouching) SlowBlend(dodgeStandTime);
    }
}
