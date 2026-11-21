using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// One-off moves that go a way of their own, picked by the way the body goes (the user, 2026-11-21, on BIPER Female Runner:
/// "점프 착지 구르기 회피"): a roll, a dodge and a jump each have a clip for each way their pack made them (Player.csv rows
/// Body.*Ways, "clip@degrees;…", 0 ahead, 90 right), and the one whose way is nearest is the one that plays. A dodge on the
/// run shifts into the run (ShiftToRun, ShiftToSprint) where standing it steps aside (QuickShift); a roll at a run comes up
/// running (RollToRun) where a slower one stands up (Roll); a jump to the side leaps that way (Jump_Air, RunJump_Air); a
/// landing on the move runs on out of it (Land_ToRun, Land_ToStumble). Ways as their names say them; ClipBench measured
/// the travel of every one.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("One-off moves by the way they go (clip@degrees;…)")]
    [SerializeField] private string rollWays = "", rollRunWays = "", dodgeWays = "", dodgeSprintWays = "";
    [SerializeField, Tooltip("A dive roll (Z) the way it goes: up standing out of it, and at a sprint running on.")]
    private string diveRollWays = "", diveRollRunWays = "";
    [SerializeField] private string jumpWays = "", runJumpWays = "";
    [SerializeField, Tooltip("Landing on the move: lightly, lightly from higher (Body.landMidHeight), hard, hard at a sprint "
        + "(a stumble), and hard at a jog (down to a hand and on).")]
    private string landRunLight = "", landRunMid = "", landRunHard = "", landRunStumble = "", landSlam = "";
    [SerializeField, Min(0f), Tooltip("A hard landing at least this fast stumbles on (m/s).")]
    private float landStumbleSpeed = 6f;

    private readonly Dictionary<string, List<(string clip, float way)>> ways = new Dictionary<string, List<(string, float)>>();

    // Every clip of every way set, made ready with the others (Build).
    private IEnumerable<string> WayClips()
    {
        foreach (string set in new[] { rollWays, rollRunWays, dodgeWays, dodgeSprintWays, jumpWays, runJumpWays, diveRollWays, diveRollRunWays })
            foreach (var (clip, _) in WaysOf(set))
                yield return clip;
        yield return landRunLight;
        yield return landRunMid;
        yield return landRunHard;
        yield return landRunStumble;
        yield return landSlam;
    }

    private List<(string clip, float way)> WaysOf(string set)
    {
        set ??= "";
        if (ways.TryGetValue(set, out var known)) return known;
        var list = new List<(string, float)>();
        foreach (string entry in set.Split(';'))
        {
            int at = entry.IndexOf('@');
            if (at <= 0) continue;
            if (float.TryParse(entry.Substring(at + 1), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float degrees))
                list.Add((entry.Substring(0, at).Trim(), degrees));
        }
        ways[set] = list;
        return list;
    }

    // A set's way further than this from the heading is no move that way (degrees): the fallback plays instead (a dodge
    // back at a run is no shift into the run sideways).
    private const float WayWithin = 45f;

    // The clip of a set whose way is nearest this heading (degrees from the body's ahead), or the fallback for an empty set
    // or one with no way near it.
    private string NearestWay(string set, float heading, string fallback)
    {
        string best = fallback;
        float gap = WayWithin;
        foreach (var (clip, way) in WaysOf(set))
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

    // A dodge's clip: stepping aside standing, walking or jogging; sprinting, shifting aside into the sprint. Picked by the
    // gait (MovementGait.Position, the same on every PC), not by the speed now: the body meets a dodge in the frame its step
    // has already thrown it at about 18 m/s (Ability.stepDistance over stepDuration), so until 0.9.284 the speed picked the
    // dodge that runs on (ShiftToRun) for every dodge but straight ahead or back, even from a walk, and its running steps
    // went on at their own pace after it (the user, 2026-11-21: "움직이면서 회피하면 회피 끝나고 캐릭터가 굉장히 빠른 속도로
    // 달리는 모션이 생겨"). That dodge went with the run it ran on into (0.9.283).
    private string DodgeFor()
    {
        float heading = HeadingOf(player.Abilities.ActionDirection);
        string side = heading < 0f ? dodgeLeft : dodgeRight;
        string standing = NearestWay(dodgeWays, heading, side);
        var gait = player.Actions != null ? player.Actions.Gait : null;
        return gait != null && gait.Position > SprintsFrom ? NearestWay(dodgeSprintWays, heading, standing) : standing;
    }

    // The gait past which a dodge shifts into the sprint: halfway from the jog (1) to the sprint (2).
    private const float SprintsFrom = 1.5f;

    // A roll's clip the way the body is going: begun at a sprint it runs on, else it stands up (MovementLanding.RollRuns, the
    // movement's own choice, whose travel it goes at; the same on every PC).
    private string RollWay(string fallback)
    {
        Vector3 velocity = player.ActualVelocity;
        float heading = HeadingOf(velocity);
        return player.Abilities.Landing.RollRuns ? NearestWay(rollRunWays, heading, fallback) : NearestWay(rollWays, heading, fallback);
    }

    // A jump's clip: by its speed as before when it goes ahead or back, else the leap to that side.
    private string JumpWay(string bySpeed, float speed)
    {
        float heading = HeadingOf(player.ActualVelocity);
        float side = Mathf.Abs(heading);
        if (speed < jumpStillSpeed || side < 45f || side > 135f) return bySpeed;
        return NearestWay(speed >= jumpFastSpeed ? runJumpWays : jumpWays, heading, bySpeed);
    }

    // A landing at a run runs on out of it; null slower, or with no such clip. Its steps are a run's (Land_ToRun1 goes
    // 5.6 m/s): taken at a jog's or a walk's speed, they ran ahead of the body.
    private string LandingOnTheMove(bool hard)
    {
        float speed = HorizontalSpeed();
        if (speed < jumpFastSpeed) return null;
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
        if (speed <= walkClipSpeed || speed >= jumpFastSpeed || Mathf.Abs(HeadingOf(player.ActualVelocity)) > 45f) return null;
        return Find(landSlam) != null ? landSlam : null;
    }

    // A dive roll's clip the way it goes (the move's direction, as a dodge's): standing, it rolls and gets up; begun at a
    // sprint, it rolls and runs on (MovementLanding.RollRuns, the same on every PC).
    private string DiveRollWay()
    {
        float heading = HeadingOf(player.Abilities.ActionDirection);
        string standing = NearestWay(diveRollWays, heading, roll);
        return player.Abilities.Landing.RollRuns ? NearestWay(diveRollRunWays, heading, standing) : standing;
    }

    // The one-offs that end running: up out of a slide (slideEnd), a landing or a roll on the run, a dodge that shifts into
    // the run or the sprint. Their last steps are a run's, so they play out only while the body runs on; a body that has
    // stopped never shows them (the user, 2026-11-21: a slide's end ran in place, "갑자기 와다닥 달리는 모션하다 서기로").
    private bool RunsOut(Channel channel)
    {
        if (channel == null) return false;
        if (channel == Find(slideEnd) || channel == Find(landRunLight) || channel == Find(landRunMid) || channel == Find(landRunHard)
            || channel == Find(landRunStumble) || channel == Find(landSlam))
            return true;
        foreach (string set in new[] { rollRunWays, dodgeSprintWays, diveRollRunWays })
            foreach (var (clip, _) in WaysOf(set))
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
