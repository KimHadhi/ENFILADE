using System.Collections.Generic;
using UnityEngine;

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
    [SerializeField] private string rollWays = "", rollRunWays = "", dodgeWays = "", dodgeRunWays = "", dodgeSprintWays = "";
    [SerializeField] private string jumpWays = "", runJumpWays = "";
    [SerializeField, Tooltip("Landing on the move: lightly, hard, and hard at a sprint (a stumble).")]
    private string landRunLight = "", landRunHard = "", landRunStumble = "";
    [SerializeField, Min(0f), Tooltip("A hard landing at least this fast stumbles on (m/s).")]
    private float landStumbleSpeed = 6f;

    private readonly Dictionary<string, List<(string clip, float way)>> ways = new Dictionary<string, List<(string, float)>>();

    // Every clip of every way set, made ready with the others (Build).
    private IEnumerable<string> WayClips()
    {
        foreach (string set in new[] { rollWays, rollRunWays, dodgeWays, dodgeRunWays, dodgeSprintWays, jumpWays, runJumpWays })
            foreach (var (clip, _) in WaysOf(set))
                yield return clip;
        yield return landRunLight;
        yield return landRunHard;
        yield return landRunStumble;
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

    // A dodge's clip: stepping aside standing or walking, shifting into the run or the sprint on the move.
    private string DodgeFor()
    {
        float heading = HeadingOf(player.Abilities.ActionDirection);
        float speed = HorizontalSpeed();
        string side = heading < 0f ? dodgeLeft : dodgeRight;
        string standing = NearestWay(dodgeWays, heading, side);
        if (player.SprintProgress > .5f) return NearestWay(dodgeSprintWays, heading, standing);
        return speed >= jumpFastSpeed ? NearestWay(dodgeRunWays, heading, standing) : standing;
    }

    // A roll's clip the way the body is going: up running at a run's speed, else standing up.
    private string RollWay(string fallback)
    {
        Vector3 velocity = player.ActualVelocity;
        float heading = HeadingOf(velocity);
        return HorizontalSpeed() >= jumpFastSpeed ? NearestWay(rollRunWays, heading, fallback) : NearestWay(rollWays, heading, fallback);
    }

    // A jump's clip: by its speed as before when it goes ahead or back, else the leap to that side.
    private string JumpWay(string bySpeed, float speed)
    {
        float heading = HeadingOf(player.ActualVelocity);
        float side = Mathf.Abs(heading);
        if (speed < jumpStillSpeed || side < 45f || side > 135f) return bySpeed;
        return NearestWay(speed >= jumpFastSpeed ? runJumpWays : jumpWays, heading, bySpeed);
    }

    // A landing on the move runs on out of it; null when the body stands, or there is no such clip.
    private string LandingOnTheMove(bool hard)
    {
        float speed = HorizontalSpeed();
        if (speed <= walkClipSpeed) return null;
        if (Mathf.Abs(HeadingOf(player.ActualVelocity)) > 45f) return null;
        string clip = !hard ? landRunLight : speed >= landStumbleSpeed ? landRunStumble : landRunHard;
        return Find(clip) != null ? clip : null;
    }
}
