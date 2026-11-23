using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// The step dodge on the body: its clip whole, from the moment the movement begins it at (DodgeMove), on the clip's own
/// clock. When the legs are the player's again the rest of it, getting up, plays out on the spot as a landing does, giving
/// way slowly (dodgeStandTime) once the body moves off; one from a crouch goes into the crouch as slowly.
/// Out of a run that goes on (StepDodgeState.RunsOn) it goes straight on into the run: aside into a sprint ahead with the
/// clip the pack has for it (DodgeMove.RunOutToward), else into the cycles, and either way the run picks up on the foot that is
/// down (PickUpStep). Until 0.9.341 the cycles came in wherever in their stride they had been left, so the legs changed
/// over in the crossfade (the user, 2026-11-22: "달리면서 회피 이후 달리는 모션이 자연스럽게 이어지지 않는 거 같아").
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
    // The run-out takes over this quickly (s): it starts in the pose the dodge is in. Off the dodge's own speed the body is up
    // to the run's in a moment; the run-out waits this long for it (s; a copy's velocity is smoothed too).
    private const float RunOutTakes = .1f, RunOutGrace = .2f;
    // A copy that leaves the dodge this much before its end (s) runs on, whatever its smoothed velocity says yet.
    private const float LeftEarly = .08f;
    // Half a stride at a run, the feet this far apart along it as one lands (share of the leg), and a foot this much higher
    // than the other (share of the leg) carries nothing.
    private const float StepSpread = 1.1f, FootLifted = .08f;
    private string dodgeClip, dodgeRunOut = "";
    private float runOutGrace;
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
        if (to != PlayerMovement.MovementState.Standing && to != PlayerMovement.MovementState.Crouching) return;
        SlowBlend(dodgeStandTime);
        if (to == PlayerMovement.MovementState.Crouching) return;
        bool goes = player.HorizontalVelocity.magnitude > walkClipSpeed
            || (player.IsNetworkDriven && stateTime < player.Abilities.DodgeMove.LegsAt - player.Abilities.DodgeMove.From - LeftEarly);
        if (!goes) PlayOn(dodgeClip);
        else if (!StartRunOut()) StepFrom(Find(dodgeClip));
    }

    // The dodge aside comes up into the sprint ahead; false where there is no clip for it.
    private bool StartRunOut()
    {
        dodgeRunOut = player.Abilities.DodgeMove.RunOutToward(player.HorizontalVelocity);
        var channel = Find(dodgeRunOut);
        // From the moment the dodge is at: the two were made of one shift.
        float from = Find(dodgeClip) is Channel dodge && !dodge.Synced ? (float)dodge.Playable.GetTime() : 0f;
        if (channel == null || channel.Synced || channel.Length - from <= blendTime) return false;
        Restart(channel);
        channel.Playable.SetTime(from);
        transition = channel;
        transitionStance = PlayerMovement.MovementState.Standing;
        transitionLeft = channel.Length - from;
        playingOut = false;
        transitionRuns = true;
        runOutGrace = RunOutGrace;
        SlowBlend(RunOutTakes);
        return true;
    }

    private bool OutOfDodge(Channel channel) => channel != null && (channel == Find(dodgeClip) || channel == Find(dodgeRunOut));

    // A walk or run taking over from a one-shot clip takes the stride where that clip has its feet: the clip's own footfalls at
    // the moment it is at (ClipMotion.StepAt), so it goes on with the foot the clip has going, the same on every PC; its speed
    // and way the body's own from the first step. Until 0.9.378 only a dodge did, and by where the feet stood after the foot IK
    // (PickUpStep, now for a clip with no footfalls read): out of a slide, a landing or a roll the run came back at the stride
    // it had left off before them, and the legs swapped as it took over (the experts' read of 2026-11-23).
    private void StepFrom(Channel channel)
    {
        if (channel != null && !channel.Synced && channel.Playable.IsValid() && motions.TryGetValue(channel.Clip, out var motion)
            && motion != null && motion.StepAt((float)channel.Playable.GetTime(), out float place))
        {
            phase = place;
            stride = Vector3.ProjectOnPlane(player.HorizontalVelocity, Vector3.up);
            return;
        }
        PickUpStep();
    }

    // Where the feet are: the stride's place (phase: 0 the left foot landing, .5 the right) by which foot is down and how far
    // the feet are apart along the run.
    private void PickUpStep()
    {
        stride = Vector3.ProjectOnPlane(player.HorizontalVelocity, Vector3.up);
        if (hips == null || leftLeg == null || rightLeg == null) return;
        Vector3 way = stride.sqrMagnitude > .01f ? stride.normalized : transform.forward;
        Vector3 left = leftLeg.Foot.position - hips.position, right = rightLeg.Foot.position - hips.position;
        float leg = Mathf.Max(.1f, legLength), lifted = FootLifted * leg;
        // Both down, the weight is on the one nearer under the hips.
        bool onLeft = Mathf.Abs(left.y - right.y) > lifted ? left.y < right.y
            : Vector3.ProjectOnPlane(left, Vector3.up).sqrMagnitude <= Vector3.ProjectOnPlane(right, Vector3.up).sqrMagnitude;
        float ahead = Vector3.Dot(onLeft ? left - right : right - left, way) / leg;
        float since = Mathf.Acos(Mathf.Clamp(ahead / StepSpread, -1f, 1f)) / (2f * Mathf.PI);
        phase = Mathf.Repeat((onLeft ? 0f : .5f) + since, 1f);
    }
}
