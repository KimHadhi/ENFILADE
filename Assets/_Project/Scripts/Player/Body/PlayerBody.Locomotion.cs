using UnityEngine;
using UnityEngine.Playables;

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

    [Header("Turning on the spot")]
    [SerializeField] private string turnLeft = "mx_turn_left", turnRight = "mx_turn_right";
    [SerializeField, Range(10f, 120f), Tooltip("Standing still, the legs stay planted until the view has turned this far "
        + "from them (degrees); the chest twists toward the view meanwhile. Then the legs step round.")]
    private float turnStartAngle = 60f;
    [SerializeField, Min(10f), Tooltip("How fast the legs step round to the view (degrees per second).")]
    private float turnSpeed = 150f;
    [SerializeField, Min(.1f), Tooltip("Playback speed of the turn clips while the legs step round.")]
    private float turnClipSpeed = 1.5f;

    private float legYaw;
    private int turning;
    private bool legsPlanted;
    private Channel beamRunChannel;
    private int beamRunEntry = -1;
    private float beamRunRate, beamRunTime;

    private void AddLocomotionChannels()
    {
        foreach (string clip in new[] { walk, walkRight, walkBack, walkLeft }) AddChannel(clip, walkClipSpeed);
        foreach (string clip in new[] { run, runRight, runBack, runLeft }) AddChannel(clip, runClipSpeed);
        AddChannel(sprint, sprintClipSpeed);
        foreach (string clip in new[] { crouchWalk, crouchRight, crouchBack, crouchLeft }) AddChannel(clip, crouchClipSpeed);
        AddChannel(turnLeft, 0f);
        AddChannel(turnRight, 0f);
        turning = 0;
        beamRunChannel = null;
        beamRunEntry = -1;
        beamRunTime = beamRunRate = 0f;
    }

    private void Locomotion(bool crouched, float dt)
    {
        if (!crouched && player.Abilities.Fixtures.Run.IsActive)
        {
            BeamRunPose(dt);
            return;
        }
        beamRunChannel = null;
        beamRunEntry = -1;
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
            toSprint *= player.RunIntent * Mathf.Clamp01(local.z / Mathf.Max(.01f, speed));
            var stepping = TurnOnTheSpot(speed < .3f, dt);
            if (stepping != null) stepping.Target += 1f - toWalk;
            else Mix(idle, 1f - toWalk);
            Directions(heading, toWalk * (1f - toRun), walk, walkRight, walkBack, walkLeft);
            Directions(heading, toRun * (1f - toSprint), run, runRight, runBack, runLeft);
            Mix(sprint, toSprint);
            if (!legsPlanted) targetYaw = Mathf.Clamp(heading, -maxStrafeYaw, maxStrafeYaw) * toSprint;
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

    // A beam fixes the body's travel direction. Changing the view or blending to a different gait must not move
    // a footfall that its ring has already announced. Keep one forward channel until this beam run ends.
    private void BeamRunPose(float dt)
    {
        var fixtures = player.Abilities.Fixtures;
        float speed = HorizontalSpeed();
        if (beamRunEntry != fixtures.Run.EntrySerial || beamRunChannel == null)
        {
            var running = Find(run);
            var sprinting = Find(sprint);
            bool useSprint = running == null || (sprinting != null && (sprinting.Weight > running.Weight
                || (sprinting.Weight + running.Weight <= .001f && speed >= (runClipSpeed + sprintClipSpeed) * .5f)));
            beamRunChannel = useSprint ? sprinting : running;
            beamRunEntry = fixtures.Run.EntrySerial;
            beamRunTime = 0f;
            beamRunRate = beamRunChannel != null ? Mathf.Clamp(speed / Mathf.Max(.1f, beamRunChannel.AuthoredSpeed), .65f, 1.1f) : 1f;
        }
        targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, MovementFixtures.YawOf(fixtures.BalanceFacing));
        legsPlanted = true;
        turning = 0;
        if (beamRunChannel == null) { Want(run); return; }
        beamRunTime += dt;
        beamRunChannel.Target = 1f;
        float targetRate = Mathf.Clamp(speed / Mathf.Max(.1f, beamRunChannel.AuthoredSpeed), .65f, 1.1f);
        beamRunRate = Mathf.Lerp(beamRunRate, targetRate, 1f - Mathf.Exp(-dt / Mathf.Max(.01f, blendTime)));
        phase = Mathf.Repeat(phase + dt * beamRunRate / beamRunChannel.Length, 1f);
    }

    // Standing still, the legs keep their place while the view turns and step round once it has turned far enough
    // (the spine twists the chest toward the view meanwhile). Returns the turn clip while stepping.
    private Channel TurnOnTheSpot(bool still, float dt)
    {
        if (!still) { turning = 0; return null; }
        float look = transform.eulerAngles.y, limit = turnStartAngle + 30f;
        float offset = Mathf.Clamp(Mathf.DeltaAngle(look, legYaw), -limit, limit);
        if (turning == 0 && Mathf.Abs(offset) > turnStartAngle)
        {
            // Legs left behind on the left step round to the right, and the other way round.
            turning = offset < 0f ? 1 : -1;
            Restart(Find(turning > 0 ? turnRight : turnLeft));
        }
        Channel clip = null;
        if (turning != 0)
        {
            offset = Mathf.MoveTowards(offset, 0f, turnSpeed * dt);
            clip = Find(turning > 0 ? turnRight : turnLeft);
            if (clip != null)
            {
                if (clip.Playable.GetTime() >= clip.Length) clip.Playable.SetTime(0);
                clip.Playable.SetSpeed(turnClipSpeed);
            }
            if (Mathf.Abs(offset) < 1f) turning = 0;
        }
        legYaw = look + offset;
        targetYaw = offset;
        legsPlanted = true;
        return clip;
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
