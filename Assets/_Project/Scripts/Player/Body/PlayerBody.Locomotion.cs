using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// Ground locomotion that keeps the body facing the camera: walk, run and crouch each blend a forward, right, back and
/// left clip by the direction of travel (diagonals mix the two either side), and every cycle shares one phase so the
/// feet stay in step. Sprint has only a forward clip, so the hips turn toward the heading as it takes over.
/// With a gun out, standing moves take that gun's set; crouching keeps its clips, whose eyes fit the crouched camera.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Directional locomotion")]
    [SerializeField] private string walkLeft = "mx_strafe_walk_left", walkRight = "mx_strafe_walk_right";
    [SerializeField] private string runLeft = "mx_strafe_run_left", runRight = "mx_strafe_run_right";
    [SerializeField] private string crouchLeft = "crouchleft", crouchRight = "crouchright";
    [SerializeField, Tooltip("Standing moves with that gun out, in this order: idle; walk forward, right, back, left; run forward, "
        + "right, back, left; sprint. A set with a clip missing leaves the plain moves in place.")]
    private string[] rifleMoves = GunMoves, pistolMoves = GunMoves;

    [Header("Turning on the spot")]
    [SerializeField] private string turnLeft = "mx_turn_left", turnRight = "mx_turn_right";
    [SerializeField, Range(10f, 120f), Tooltip("Standing still, the legs stay planted until the view has turned this far "
        + "from them (degrees); the chest twists toward the view meanwhile. Then the legs step round.")]
    private float turnStartAngle = 60f;
    [SerializeField, Min(10f), Tooltip("How fast the legs step round to the view (degrees per second).")]
    private float turnSpeed = 150f;
    [SerializeField, Min(.1f), Tooltip("Playback speed of the turn clips while the legs step round.")]
    private float turnClipSpeed = 1.5f;
    [SerializeField, Min(0f), Tooltip("How quickly the legs follow a change in the way and speed of travel (s). Quick A/D or "
        + "W taps even out instead of flipping the stride from one side to the other.")]
    private float strideFollowTime = .12f;

    private static readonly string[] GunMoves =
    {
        "W1_Stand_Aim_Idle_IPC", "W1_Walk_Aim_F_Loop_IPC", "W1_Walk_Aim_R_Loop_IPC", "W1_Walk_Aim_B_Loop_IPC", "W1_Walk_Aim_L_Loop_IPC",
        "W1_Jog_Aim_F_Loop_IPC", "W1_Jog_Aim_R_Loop_IPC", "W1_Jog_Aim_B_Loop_IPC", "W1_Jog_Aim_L_Loop_IPC", "W1_Run_F_Loop_IPC"
    };

    private string[] plainMoves, rifleSet, pistolSet;
    private float legYaw;
    private Vector3 stride;
    private int turning;
    private bool legsPlanted;
    private void AddLocomotionChannels()
    {
        plainMoves = new[] { idle, walk, walkRight, walkBack, walkLeft, run, runRight, runBack, runLeft, sprint };
        rifleSet = AddMoves(rifleMoves);
        pistolSet = AddMoves(pistolMoves);
        AddMoves(plainMoves);
        foreach (string clip in new[] { crouchWalk, crouchRight, crouchBack, crouchLeft }) AddChannel(clip, crouchClipSpeed);
        AddChannel(turnLeft, 0f);
        AddChannel(turnRight, 0f);
        turning = 0;
    }

    private string[] AddMoves(string[] moves)
    {
        if (moves == null || moves.Length != plainMoves.Length) return plainMoves;
        for (int i = 0; i < moves.Length; i++)
            AddChannel(moves[i], i == 0 ? 0f : i <= 4 ? walkClipSpeed : i <= 8 ? runClipSpeed : sprintClipSpeed);
        foreach (string clip in moves)
            if (!byName.ContainsKey(clip)) return plainMoves;
        return moves;
    }

    private string[] MovesInHand()
    {
        if (weapons == null) weapons = player.GetComponent<WeaponController>();
        var gun = weapons != null && weapons.ShowsWeapon ? weapons.EquippedData : null;
        if (gun == null) return plainMoves;
        return gun.SoundProfile == GunSoundProfile.Pistol ? pistolSet : rifleSet;
    }

    private void Locomotion(bool crouched, float dt)
    {
        // Followed in the world, so turning the view doesn't drag the legs behind it.
        Vector3 travel = Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up);
        stride = Vector3.Lerp(stride, travel, strideFollowTime > 0f ? 1f - Mathf.Exp(-dt / strideFollowTime) : 1f);
        Vector3 local = transform.InverseTransformDirection(stride);
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
            var moves = MovesInHand();
            var stepping = TurnOnTheSpot(speed < .3f, dt);
            if (stepping != null) stepping.Target += 1f - toWalk;
            else Mix(moves[0], 1f - toWalk);
            Directions(heading, toWalk * (1f - toRun), moves[1], moves[2], moves[3], moves[4]);
            Directions(heading, toRun * (1f - toSprint), moves[5], moves[6], moves[7], moves[8]);
            Mix(moves[9], toSprint);
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
        if (player.IsNetworkDriven) FollowStep(dt);
        if (weight <= 0f) return;
        // Fast gameplay movement should lengthen the apparent stride, not turn the feet into a fast shuffle.
        float rate = Mathf.Clamp(speed / Mathf.Max(.1f, authored / weight), .65f, 1.1f);
        phase = Mathf.Repeat(phase + dt * rate / (length / weight), 1f);
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
