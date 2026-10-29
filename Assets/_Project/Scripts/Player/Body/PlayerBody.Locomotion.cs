using UnityEngine;

/// <summary>
/// Ground locomotion that keeps the body facing the camera: walk, run and crouch each blend a forward, right, back and
/// left clip by the direction of travel (diagonals mix the two either side), and every cycle shares one phase so the
/// feet stay in step. Sprint has only a forward clip, so the hips turn toward the heading as it takes over.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Directional locomotion")]
    [SerializeField] private string walkLeft = "mx_strafe_walk_left", walkRight = "mx_strafe_walk_right";
    [SerializeField] private string runLeft = "mx_strafe_run_left", runRight = "mx_strafe_run_right";
    [SerializeField] private string crouchLeft = "crouchleft", crouchRight = "crouchright";

    private void AddLocomotionChannels()
    {
        foreach (string clip in new[] { walk, walkRight, walkBack, walkLeft }) AddChannel(clip, walkClipSpeed);
        foreach (string clip in new[] { run, runRight, runBack, runLeft }) AddChannel(clip, runClipSpeed);
        AddChannel(sprint, sprintClipSpeed);
        foreach (string clip in new[] { crouchWalk, crouchRight, crouchBack, crouchLeft }) AddChannel(clip, crouchClipSpeed);
    }

    private void Locomotion(bool crouched, float dt)
    {
        Vector3 local = transform.InverseTransformDirection(player.ActualVelocity);
        local.y = 0f;
        float speed = local.magnitude;
        float heading = speed > .3f ? Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg : 0f;
        if (crouched)
        {
            float moving = Mathf.Clamp01(speed / (crouchClipSpeed * .5f));
            Mix(crouchIdle, 1f - moving);
            Directions(heading, moving, crouchWalk, crouchRight, crouchBack, crouchLeft);
        }
        else
        {
            float toWalk = Mathf.Clamp01(speed / walkClipSpeed);
            float toRun = Mathf.Clamp01((speed - walkClipSpeed) / Mathf.Max(.01f, runClipSpeed - walkClipSpeed));
            float toSprint = Mathf.Clamp01((speed - runClipSpeed) / Mathf.Max(.01f, sprintClipSpeed - runClipSpeed));
            // Speed alone can come from strafing, moving backwards or carried momentum. The sprint clip only
            // has a forward stride, so blend it in with the movement system's actual forward sprint intent.
            toSprint *= player.SprintProgress * Mathf.Clamp01(local.z / Mathf.Max(.01f, speed));
            Mix(idle, 1f - toWalk);
            Directions(heading, toWalk * (1f - toRun), walk, walkRight, walkBack, walkLeft);
            Directions(heading, toRun * (1f - toSprint), run, runRight, runBack, runLeft);
            Mix(sprint, toSprint);
            targetYaw = Mathf.Clamp(heading, -maxStrafeYaw, maxStrafeYaw) * toSprint;
        }

        float weight = 0f, length = 0f, authored = 0f;
        foreach (var channel in channels)
        {
            if (!channel.Synced || channel.Target <= 0f) continue;
            weight += channel.Target;
            length += channel.Target * channel.Length;
            authored += channel.Target * channel.AuthoredSpeed;
        }
        if (weight <= 0f) return;
        // Fast gameplay movement should lengthen the apparent stride, not turn the feet into a fast shuffle.
        float rate = Mathf.Clamp(speed / Mathf.Max(.1f, authored / weight), .65f, 1.1f);
        phase = Mathf.Repeat(phase + dt * rate / (length / weight), 1f);
    }

    // Shares the weight between the two direction clips either side of the heading (0° forward, 90° right).
    private void Directions(float heading, float weight, string forward, string right, string back, string left)
    {
        if (weight <= 0f) return;
        float sector = Mathf.Repeat(heading, 360f) / 90f;
        int from = Mathf.FloorToInt(sector) % 4;
        float toNext = sector - Mathf.Floor(sector);
        Mix(Around(from), weight * (1f - toNext));
        Mix(Around((from + 1) % 4), weight * toNext);

        string Around(int quarter) => quarter == 0 ? forward : quarter == 1 ? right : quarter == 2 ? back : left;
    }
}
