using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// One-off moves that go a way of their own, picked by the way the body goes (the user, 2026-11-21, on BIPER Female Runner:
/// "점프 착지 구르기 회피"): a roll has a clip for each way its pack made it (Player.csv rows Body.*Ways, ClipWays), and the one
/// whose way is nearest is the one that plays. A roll at a run comes up running (RollToRun) where a slower one stands up
/// (Roll); a landing on the move runs on out of it (Land_ToRun, Land_ToStumble). Ways as their names say them; ClipBench
/// measured the travel of every one. A dodge's way is the movement's own pick (DodgeMove); a jump has one clip whatever its
/// way (Body.jump).
/// </summary>
public sealed partial class PlayerBody
{
    [Header("One-off moves by the way they go (clip@degrees;…)")]
    [SerializeField] private string rollWays = "", rollRunWays = "";
    [SerializeField, Tooltip("Landing on the move: lightly, lightly from higher (Body.landMidHeight), hard, hard at a sprint "
        + "(a stumble), and hard at a jog (down to a hand and on).")]
    private string landRunLight = "", landRunMid = "", landRunHard = "", landRunStumble = "", landSlam = "";
    [SerializeField, Min(0f), Tooltip("A hard landing at least this fast stumbles on (m/s).")]
    private float landStumbleSpeed = 6f;

    // Every clip of every way set, made ready with the others (Build).
    private IEnumerable<string> WayClips()
    {
        foreach (string set in new[] { rollWays, rollRunWays, player.AbilitySettings.dodgeWays, player.AbilitySettings.dodgeSprintWays })
            foreach (var (clip, _) in ClipWays.Of(set))
                yield return clip;
        yield return player.AbilitySettings.diveRoll;
        yield return landRunLight;
        yield return landRunMid;
        yield return landRunHard;
        yield return landRunStumble;
        yield return landSlam;
    }

    // A set's way further than this from the heading is no move that way (degrees): the fallback plays instead.
    private const float WayWithin = 45f;

    // The clip of a set whose way is nearest this heading (degrees from the body's ahead), or the fallback for an empty set
    // or one with no way near it.
    private string NearestWay(string set, float heading, string fallback)
    {
        string best = fallback;
        float gap = WayWithin;
        foreach (var (clip, way) in ClipWays.Of(set))
        {
            float off = Mathf.Abs(Mathf.DeltaAngle(way, heading));
            if (off <= gap && Find(clip) != null) { gap = off; best = clip; }
        }
        return best;
    }

    // Which way something goes from the body's ahead (degrees, + to the right); 0 for nothing.
    private float HeadingOf(Vector3 direction)
    {
        Vector3 local = transform.InverseTransformDirection(Vector3.ProjectOnPlane(direction, Vector3.up));
        return local.sqrMagnitude > 1e-6f ? Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg : 0f;
    }

    // A roll's clip the way the body is going: begun at a sprint it runs on, else it stands up (MovementLanding.RollRuns, the
    // movement's own choice, whose travel it goes at; the same on every PC).
    private string RollWay(string fallback)
    {
        Vector3 velocity = player.ActualVelocity;
        float heading = HeadingOf(velocity);
        return player.Abilities.Landing.RollRuns ? NearestWay(rollRunWays, heading, fallback) : NearestWay(rollWays, heading, fallback);
    }

    // A landing at a run runs on out of it, the body at its pace (RunOutPace); null slower, or with no such clip: its steps
    // are a run's.
    private string LandingOnTheMove(bool hard)
    {
        float speed = HorizontalSpeed();
        if (speed < landRunSpeed) return null;
        if (Mathf.Abs(HeadingOf(player.ActualVelocity)) > 45f) return null;
        string clip = !hard ? (DroppedFar && Find(landRunMid) != null ? landRunMid : landRunLight)
            : speed >= landStumbleSpeed ? landRunStumble : landRunHard;
        return Find(clip) != null ? clip : null;
    }

    // A hard landing going ahead at a jog: down to a hand and on (Land_ToSlam; its steps go 3.3 m/s, a jog's). Faster, the
    // sprint's landings (LandingOnTheMove); slower or any other way, the knees take it on the spot.
    private string SlamOnTheMove()
    {
        float speed = HorizontalSpeed();
        if (speed <= walkClipSpeed || speed >= landRunSpeed || Mathf.Abs(HeadingOf(player.ActualVelocity)) > 45f) return null;
        return Find(landSlam) != null ? landSlam : null;
    }

    // The one-offs that end running: up out of a slide (slideEnd), a landing or a roll on the run, a dodge's coming up into
    // the sprint. Their last steps are a run's, so they play out only while the body runs on; a body that has
    // stopped never shows them (the user, 2026-11-21: a slide's end ran in place, "갑자기 와다닥 달리는 모션하다 서기로").
    private bool RunsOut(Channel channel)
    {
        if (channel == null) return false;
        if (channel == Find(slideEnd) || channel == Find(landRunLight) || channel == Find(landRunMid) || channel == Find(landRunHard)
            || channel == Find(landRunStumble) || channel == Find(landSlam))
            return true;
        foreach (string set in new[] { rollRunWays, player.AbilitySettings.dodgeSprintWays })
            foreach (var (clip, _) in ClipWays.Of(set))
                if (channel == Find(clip)) return true;
        return false;
    }

    // A clip that ends running, given up or never played on: its pose holds where it is while what follows takes over, so no
    // running step shows as it fades; a body that has stopped settles into standing slowly (Body.standSettleTime), as there
    // is no clip from those poses to standing.
    private void GiveUpRunOut(Channel channel)
    {
        if (channel == null || channel.Synced) return;
        channel.Playable.SetSpeed(0);
        if (HorizontalSpeed() <= walkClipSpeed) SlowBlend(standSettleTime);
    }
}
