using System.Collections.Generic;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// A turn on the spot (0.9.430). Standing still, the legs keep their facing while the view turns, the chest twisting toward
/// it (SpineLook); once the view has gone turnStartAngle away they turn on BIPER's own turn clip for the stance
/// (BodyMoves.csv turns: TurnIP_L/R 90, 135 and 180 bare-handed or with a gun at the hip, a gun's AimTurn_L/R90 aimed): the
/// one on the side the view went whose own turn is nearest how far it has gone, picked again as the view comes to rest
/// until the clip's root begins to turn; come into a moment before then and played to its end, faster as far as the view is
/// round ahead of the body (up to TurnHurryMost). The body's root turns as the clip's root does, drawn out or in
/// (TurnBendMost) to end facing the view, so the feet go as the clip's go. Keys that ask the legs to go end it and the walk
/// takes over from its pose (BodyInertia); once its root has turned TurnHandOn of its way, a view gone round again past the
/// angle starts the next. A stance with no turn clip (crouched with nothing or a gun at the hip) brings its legs round with
/// the view past the angle, the feet with them. 0.9.430 picked the clip as the view passed the angle, played it at its own
/// pace and began the next only once its root was done: a view flicked 180 turned 90 and then 90 again, slowly (the user:
/// "고개 돌리는 건 turn in place가 존나 느린데").
/// From 0.9.279 to 0.9.429 the feet stepped round by themselves (TurnPlanner, FootPlanting): lifted 3 to 6 cm under a
/// standing pose that turned whole, it looked like no turn at all (the feet record of 2026-11-25 07:31; the user: "turn in
/// place는 왜 모션 없음?", "turn in place도 시발 안 됨", "다 죽여"). From 0.9.261 to 0.9.278 the same clips were layered on
/// the legs alone, from their start, and the feet held against them (the user then: "turn in place 모션이 있는 거야 없는
/// 거야"): the legs waited through the clip's weight shift with the view already 60 degrees round, and the clip's legs hung
/// from the standing clip's hips.
/// </summary>
public sealed partial class PlayerBody
{
    // A stance's turn clip, and how far its root turns from where it is come into to its end (degrees, + to the right).
    private sealed class StandTurnClip
    {
        public Channel Channel;
        public ClipMotion Motion;
        public float From, Turn, Done;
    }

    // The turn playing (null for none): its clip's moment (s), the bend on its root's turn, and the way the body faced as it
    // began (world yaw); whether its first frame is yet to come (the clip is begun then).
    private StandTurnClip standTurn;
    private float standTurnAt, standTurnBend, standTurnFrom;
    private bool standTurnBegins;

    // The clip's root turns no more than this many times its own turn, nor less than its inverse. It plays up to TurnHurryMost
    // times its pace as the view is round ahead of the body from the angle to HurryRange past it, and hands on to the next
    // turn once its root has turned TurnHandOn of its way.
    private const float TurnBendMost = 1.4f, TurnHurryMost = 1.5f, HurryRange = 90f, TurnHandOn = .7f;
    // The stance's turns the playing one was picked from.
    private List<StandTurnClip> standTurns;
    // A clip's root is still within this of where it begins or ends (degrees); it is come into this long before its root turns
    // (s), the first foot lifting.
    private const float TurnRootStill = 2f, TurnLeadIn = .15f;

    // A set's turn clips from its table row (BodyMoves.csv turns): each measured on this body, where it is come into, how far
    // its root turns from there and when it is done turning. One that doesn't turn is left out.
    private void AddStandTurns(MoveSet set, string clips)
    {
        foreach (string name in (clips ?? "").Split(new[] { ';', ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            AddChannel(name, 0f);
            var motion = MotionOf(name);
            if (!byName.TryGetValue(name, out var channel) || motion == null) continue;
            float start = motion.TurnedAt(0f), end = motion.TurnedAt(motion.Length), from = 0f, done = motion.Length;
            for (float t = 0f; t <= motion.Length; t += 1f / 60f)
                if (Mathf.Abs(Mathf.DeltaAngle(start, motion.TurnedAt(t))) > TurnRootStill) { from = Mathf.Max(0f, t - TurnLeadIn); break; }
            for (float t = from; t <= motion.Length; t += 1f / 60f)
                if (Mathf.Abs(Mathf.DeltaAngle(end, motion.TurnedAt(t))) <= TurnRootStill) { done = t; break; }
            float turn = end - motion.TurnedAt(from);
            if (Mathf.Abs(turn) < 20f)
            {
                Report($"'{name}' was listed as a turn on the spot but its root turns {turn:0} degrees: left out.");
                continue;
            }
            set.Turns.Add(new StandTurnClip { Channel = channel, Motion = motion, From = from, Turn = turn, Done = done });
        }
    }

    // The turns of the stance the legs stand in now: a gun's aimed set's when it is aimed, else the plain or crouched set's.
    private List<StandTurnClip> StandTurns(bool crouched)
    {
        var gun = GunInHand();
        if (gun != null && AimBlend() >= .5f)
        {
            var aimed = crouched ? gun.CrouchAim : gun.Aim;
            if (aimed != null && aimed.Turns.Count > 0) return aimed.Turns;
        }
        return (crouched ? crouchSet : plainSet)?.Turns;
    }

    // Standing still (Locomotion): the legs keep their facing until the view is turnStartAngle away, then turn to it on the
    // stance's turn clip (from the next frame, StandTurnPose), or without one come round with the view.
    private void TurnOnTheSpot(bool still, bool crouched)
    {
        if (!still) return;
        float look = transform.eulerAngles.y, lead = Mathf.DeltaAngle(legYaw, look);
        if (Mathf.Abs(lead) > turnStartAngle && !BeginStandTurn(crouched, lead))
            legYaw = look - Mathf.Sign(lead) * turnStartAngle;
        targetYaw = Mathf.DeltaAngle(look, legYaw);
        legsPlanted = true;
    }

    // The turn clip on the side the view went (lead: degrees, + to the right) whose own turn is nearest it. False for none.
    private bool BeginStandTurn(bool crouched, float lead)
    {
        var turns = StandTurns(crouched);
        var best = NearestTurn(turns, lead);
        if (best == null) return false;
        standTurns = turns;
        standTurn = best;
        standTurnAt = best.From;
        standTurnBend = Mathf.Clamp(lead / best.Turn, 1f / TurnBendMost, TurnBendMost);
        standTurnFrom = legYaw;
        standTurnBegins = true;
        return true;
    }

    // Of these turns, the one on the lead's side whose own turn is nearest it; null for none.
    private static StandTurnClip NearestTurn(List<StandTurnClip> turns, float lead)
    {
        StandTurnClip best = null;
        if (turns == null) return null;
        foreach (var turn in turns)
            if (turn.Turn * lead > 0f && (best == null || Mathf.Abs(Mathf.Abs(turn.Turn) - Mathf.Abs(lead)) < Mathf.Abs(Mathf.Abs(best.Turn) - Mathf.Abs(lead))))
                best = turn;
        return best;
    }

    // On the ground standing or crouched: true while a turn on the spot has the legs.
    private bool StandTurnPose(float dt)
    {
        if (standTurn == null) return false;
        // Keys that ask the legs to go end it, and the walk takes over from the clip's pose.
        if (!grounded || dead || AskedSpeed() > StillLeave)
        {
            EndStandTurn();
            return false;
        }
        float look = transform.eulerAngles.y, ahead = Mathf.Abs(Mathf.DeltaAngle(legYaw, look));
        // Until its root begins to turn, a bigger turn is picked for a view that has gone on round (a flick still going on).
        if (standTurnAt < standTurn.From + TurnLeadIn)
        {
            float lead = Mathf.DeltaAngle(standTurnFrom, look);
            var better = NearestTurn(standTurns, lead);
            if (better != null && Mathf.Abs(better.Turn) > Mathf.Abs(standTurn.Turn))
            {
                standTurn = better;
                standTurnAt = better.From;
                standTurnBegins = true;
            }
            if (lead * standTurn.Turn > 0f) standTurnBend = Mathf.Clamp(lead / standTurn.Turn, 1f / TurnBendMost, TurnBendMost);
        }
        if (!standTurnBegins) standTurnAt += dt * Mathf.Lerp(1f, TurnHurryMost, Mathf.InverseLerp(turnStartAngle, turnStartAngle + HurryRange, ahead));
        var turn = standTurn;
        float length = turn.Channel.Length;
        // Most of the way round, a view gone round again past the angle starts the next turn; at its end the stance has the legs.
        float turned = (turn.Motion.TurnedAt(Mathf.Min(standTurnAt, length)) - turn.Motion.TurnedAt(turn.From)) / turn.Turn;
        if (standTurnAt >= length || (standTurnAt >= turn.Done || turned >= TurnHandOn) && ahead > turnStartAngle)
        {
            EndStandTurn();
            return false;
        }
        if (standTurnBegins)
        {
            Restart(turn.Channel);
            standTurnBegins = false;
        }
        else rootByClip = true;
        Drive(turn.Channel.Clip, standTurnAt / length);
        legYaw = standTurnFrom + standTurnBend * (turn.Motion.TurnedAt(standTurnAt) - turn.Motion.TurnedAt(turn.From));
        targetYaw = Mathf.DeltaAngle(look, legYaw);
        legsPlanted = keepsPlace = true;
        return true;
    }

    // Before the pose is chosen: a turn the body has left (a jump, a slide, a ladder) is over.
    private void KeepStandTurn()
    {
        if (standTurn != null && (state != MovementState.Standing && state != MovementState.Crouching || !grounded || dead))
            EndStandTurn();
    }

    private void EndStandTurn()
    {
        standTurn = null;
        standTurns = null;
        yawVelocity = 0f;
    }

    // How fast the legs are asked to go this frame (m/s): the player's own by the movement's stride, another PC's as it goes.
    private float AskedSpeed()
    {
        Vector3 way = !player.IsNetworkDriven && player.StrideFrame == Time.frameCount ? player.Stride : player.ActualVelocity;
        return new Vector2(way.x, way.z).magnitude;
    }
}
