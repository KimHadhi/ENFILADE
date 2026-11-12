using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Ground locomotion, from walk and run cycles each measured on this body as it is made (GaitClips: how fast its feet carry
/// the body, which way, where the left foot comes down), so it fits any character and any cycle without numbers per clip:
/// - Each way of travel (ahead, right, back, left) lists its cycles; the speed blends the two either side of it by their
///   measured speeds. The two ways either side of the heading share the weight so the blended feet push straight back
///   along the travel: a slow side cycle takes more of a diagonal than a fast forward one, so neither skates sideways.
/// - Every cycle shares one step: each stride starts with the left foot down, wherever its file starts or however many
///   strides it holds.
/// - The pace follows a person's (gaitCadence by speed, for this body's leg) within gaitRate of the cycle's own, and the
///   stride is stretched for the rest (gaitStretch; FootPlanting.Stride), so the feet cover the ground the body covers and
///   stay where they come down. Until 0.8.260 the cycles played at their own pace whatever the speed and the feet skated
///   30-60% of it, with short quick steps (the user, 2026-11-12: "우스꽝스럽고 초보 같고 급하던데").
/// - Running fast the hips turn toward the way of travel (hipTurn, from hipTurnSpeeds), as a person running sideways or
///   back at a slant does, and the spine turns the chest back to the view (SpineLook, or FPSAF's Look with a gun), so the
///   legs run a forward or back cycle instead of side-stepping at 5 m/s.
/// - With a gun out the idle is the gun's stance; aiming, the gun's aim cycles take over as far as they reach the speed.
/// - Crouched, the crouch cycles, facing the view.
/// Every PC works this out from the same speed and step (PlayerBody.Pace), so teammates see the same legs.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Directional locomotion")]
    [SerializeField, Tooltip("Cycles that move ahead, in any order: the body measures how fast each carries it (GaitClips) and "
        + "blends the two either side of the speed.")]
    private string[] forwardMoves = { "mx_walk", "mx_run", "sprintfwd" };
    [SerializeField, Tooltip("Cycles that move right, in any order.")]
    private string[] rightMoves = { "mx_strafe_walk_right", "mx_strafe_run_right" };
    [SerializeField, Tooltip("Cycles that move back, in any order.")]
    private string[] backMoves = { "mx_walk_back", "mx_run_back" };
    [SerializeField, Tooltip("Cycles that move left, in any order.")]
    private string[] leftMoves = { "mx_strafe_walk_left", "mx_strafe_run_left" };
    [SerializeField] private string crouchLeft = "crouchleft", crouchRight = "crouchright";
    [SerializeField, Tooltip("Standing moves with that gun out, in this order: idle; walk forward, right, back, left; jog forward, "
        + "right, back, left; run forward. The idle is the gun's stance; aiming, the moves take over as far as they reach the "
        + "speed. A set with its idle or a way missing leaves the plain moves in place.")]
    private string[] rifleMoves = GunMoves, pistolMoves = GunMoves;

    [Header("Stride")]
    [SerializeField, Tooltip("A person's steps a second by speed for a leg of gaitLeg: pairs of speed (m/s); steps a second.")]
    private float[] gaitCadence = { 0f, 1.5f, 1.4f, 1.9f, 2.2f, 2.3f, 2.6f, 2.6f, 3.5f, 2.75f, 5f, 2.95f, 7f, 3.3f, 9f, 3.85f };
    [SerializeField, Min(.3f), Tooltip("The leg (hip joint to ankle, m) gaitCadence is for: a shorter one steps faster at a speed.")]
    private float gaitLeg = .9f;
    [SerializeField, Tooltip("How much slower (x) and faster (y) than its own pace a cycle may play.")]
    private Vector2 gaitRate = new Vector2(.85f, 1.2f);
    [SerializeField, Tooltip("How much shorter (x) and longer (y) than its own a cycle's stride may be stretched.")]
    private Vector2 gaitStretch = new Vector2(.8f, 1.3f);
    [SerializeField, Range(0f, .3f), Tooltip("The most the hips come down for a stretched stride's leg to reach, as a share of the leg.")]
    private float gaitStretchDrop = .08f;
    [SerializeField, Range(0f, 1f), Tooltip("With a gun out and not aiming, how much the gun's aim cycles still take the legs, as far as "
        + "they reach the speed (aiming they always do). 0: the plain run under the gun; 1: the gun's cycles, stride-matched.")]
    private float gunHipMoves;
    [SerializeField, Tooltip("Running, the most the hips turn toward the way of travel (degrees): x heading ahead or to a side, y heading back.")]
    private Vector2 hipTurn = new Vector2(70f, 45f);
    [SerializeField, Tooltip("The hips start turning toward the way of travel at x and turn all the way from y (m/s).")]
    private Vector2 hipTurnSpeeds = new Vector2(3f, 5f);

    [Header("Turning on the spot")]
    [SerializeField] private string turnLeft = "mx_turn_left", turnRight = "mx_turn_right";
    [SerializeField, Range(10f, 120f), Tooltip("Standing still, the legs stay planted until the view has turned this far "
        + "from them (degrees); the chest twists toward the view meanwhile. Then the legs step round.")]
    private float turnStartAngle = 60f;
    [SerializeField, Min(10f), Tooltip("How fast the legs turn round to the view (degrees per second).")]
    private float turnSpeed = 150f;
    // Off by default (user, 2026-11-09): the clips' own turn is cut out (inPlaceYaw) and the body turns the legs itself,
    // but a clip's legs still swing as though its hips had turned, by the clip's angle over its 0.8 s, while the body
    // turns about 60 degrees at turnSpeed with the hips kept on the standing clip. So mid-step the legs swung aside, and
    // snapped back when the clip let go.
    [SerializeField, Tooltip("Step round with the turn clips on the legs. Off, the legs turn on the spot as they stand.")]
    private bool turnSteps;
    [SerializeField, Min(0f), Tooltip("Turning on the spot without steps, how softly the legs start and stop (s).")]
    private float turnEaseTime = .08f;
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

    // A stance and its cycles: ways ahead, right, back and left (0-3), each slowest first.
    private sealed class MoveSet
    {
        public Channel Idle;
        public readonly List<Channel>[] Ways = { new List<Channel>(), new List<Channel>(), new List<Channel>(), new List<Channel>() };
        public bool Complete => Idle != null && Ways[0].Count > 0 && Ways[1].Count > 0 && Ways[2].Count > 0 && Ways[3].Count > 0;
    }

    private MoveSet plainSet, rifleSet, pistolSet, crouchSet;
    private float legYaw, turnVelocity;
    private Vector3 stride;
    private int turning;
    private bool legsPlanted, runsBack;
    // This body's leg, hip joint to ankle (m), for the person's cadence (gaitCadence).
    private float legLength = .9f;
    // This frame's blend of cycles: their weight, weight by stride time (s), and weight by the velocity their feet carry the
    // body (m/s, in the body's own frame: x right, y ahead).
    private float mixWeight, mixStride;
    private Vector2 mixVelocity;
    // What the feet stretch the stride by (PlantFeet), the frame it was worked out on (a landing or a jump plays no cycles and
    // stretches nothing), and the numbers behind it for the /gait readout.
    private FootPlanting.Stride strideStretch;
    private int strideFrame = -1;
    private float gaitSpeed, gaitNatural, gaitRateNow = 1f, gaitStretchNow = 1f;
    // How much of the body was on its cycles this frame rather than standing, for the run's calm camera (PlayerBody.Look).
    private float runCalmShare;

    /// <summary>This frame's stride stretch for the feet, none unless the walk and run cycles played this frame.</summary>
    private FootPlanting.Stride StrideNow => strideFrame == Time.frameCount ? strideStretch : FootPlanting.Stride.None;

    private void AddLocomotionChannels()
    {
        legLength = LegOf(animator);
        plainSet = MakeSet(idle, forwardMoves, rightMoves, backMoves, leftMoves);
        rifleSet = GunSet(rifleMoves);
        pistolSet = GunSet(pistolMoves);
        crouchSet = MakeSet(crouchIdle, new[] { crouchWalk }, new[] { crouchRight }, new[] { crouchBack }, new[] { crouchLeft });
        turning = 0;
        strideCount = 0;
        strideStretch = FootPlanting.Stride.None;
    }

    private static float LegOf(Animator animator)
    {
        var upper = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg) : null;
        var lower = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg) : null;
        var foot = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
        if (upper == null || lower == null || foot == null) return .9f;
        float leg = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, foot.position);
        return leg > .2f ? leg : .9f;
    }

    // The gun's moves (idle; walk F, R, B, L; jog F, R, B, L; run F) as a set, or the plain moves if one is missing.
    private MoveSet GunSet(string[] moves)
    {
        if (moves == null || moves.Length < 10) return plainSet;
        var set = MakeSet(moves[0], new[] { moves[1], moves[5], moves[9] }, new[] { moves[2], moves[6] },
            new[] { moves[3], moves[7] }, new[] { moves[4], moves[8] });
        return set.Complete ? set : plainSet;
    }

    private MoveSet MakeSet(string still, string[] forward, string[] right, string[] back, string[] left)
    {
        var set = new MoveSet();
        AddChannel(still, 0f);
        byName.TryGetValue(still ?? "", out set.Idle);
        var ways = new[] { forward, right, back, left };
        for (int w = 0; w < 4; w++)
        {
            if (ways[w] == null) continue;
            foreach (string clip in ways[w])
            {
                var cycle = AddCycle(clip);
                if (cycle != null && cycle.Gait != null && cycle.Gait.Moves && !set.Ways[w].Contains(cycle)) set.Ways[w].Add(cycle);
                else if (cycle != null && (cycle.Gait == null || !cycle.Gait.Moves))
                    Report($"'{clip}' was listed as a walk or run but its feet don't carry the body (GaitClips): left out.");
            }
            set.Ways[w].Sort((a, b) => a.Gait.Speed.CompareTo(b.Gait.Speed));
        }
        return set;
    }

    // A cycle polished in the Blender run workspace (ArtSource/Run, "게임에 보내기") comes into the profile as "enf_" and the
    // cycle's name, on Kazusa's avatar; where it is there it plays instead of the cycle the table lists, so one export changes
    // the game and deleting it changes it back.
    private const string PolishedPrefix = "enf_";

    // A walk or run cycle: on the shared step, measured on this body.
    private Channel AddCycle(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return null;
        if (!clipName.StartsWith(PolishedPrefix, System.StringComparison.OrdinalIgnoreCase)
            && profile.Find(PolishedPrefix + clipName) != null)
            clipName = PolishedPrefix + clipName;
        AddChannel(clipName, 1f);
        if (!byName.TryGetValue(clipName, out var channel) || !channel.Synced) return null;
        if (channel.Gait == null && channel.Playable.IsValid())
            channel.Gait = GaitClips.For(loadedModel, channel.Playable.GetAnimationClip(), animator);
        return channel;
    }

    // The turn clips play on the legs alone (TurnLayer, the kick's leg mask): the hips, spine and head stay on the standing
    // clip, so the steps don't rock the head, and with it the camera on the eyes (item 312). The hips' turn comes from the
    // body's own yaw (targetYaw), not the clip.
    private AnimationMixerPlayable turnMixer;
    private AnimationClipPlayable turnLeftClip, turnRightClip;
    private float turnLayerWeight, turnLayerTarget;

    // From BuildLayers, after BuildKick has made the leg mask.
    private void BuildTurn()
    {
        layers.SetLayerMaskFromAvatarMask(TurnLayer, legMask);
        turnMixer = AnimationMixerPlayable.Create(graph, 2);
        graph.Connect(turnMixer, 0, layers, TurnLayer);
        turnLeftClip = TurnClip(turnLeft, 0);
        turnRightClip = TurnClip(turnRight, 1);
        turnLayerWeight = turnLayerTarget = 0f;
        layers.SetInputWeight(TurnLayer, 0f);
    }

    private AnimationClipPlayable TurnClip(string clipName, int input)
    {
        if (string.IsNullOrEmpty(clipName)) return default;
        var clip = profile.Find(clipName);
        if (clip == null)
        {
            Report($"clip '{clipName}' is not in the PlayerBody profile.");
            return default;
        }
        var playable = ClipPlayable(clip);
        playable.SetSpeed(0);
        graph.Connect(playable, 0, turnMixer, input);
        return playable;
    }

    private void TeardownTurn()
    {
        turnMixer = default;
        turnLeftClip = turnRightClip = default;
        turnLayerWeight = turnLayerTarget = 0f;
    }

    // From BlendLayers: the legs step round while TurnOnTheSpot asked for it this frame, and ease back otherwise.
    private void BlendTurn(float dt)
    {
        if (!turnMixer.IsValid()) return;
        float target = dead ? 0f : turnLayerTarget;
        turnLayerTarget = 0f;
        turnLayerWeight = Mathf.MoveTowards(turnLayerWeight, target, dt / blendTime);
        layers.SetInputWeight(TurnLayer, turnLayerWeight);
    }

    // The set in the hands: the gun's while one is out, else the plain one.
    private MoveSet SetInHand()
    {
        if (weapons == null) weapons = player.GetComponent<WeaponController>();
        var gun = weapons != null && weapons.ShowsWeapon ? weapons.EquippedData : null;
        if (gun == null) return plainSet;
        return gun.SoundProfile == GunSoundProfile.Pistol ? pistolSet : rifleSet;
    }

    // The cycles are measured in the Animator's own units; its scale (the fit to the camera) makes them metres.
    private float GaitScale => animator != null ? Mathf.Abs(animator.transform.lossyScale.y) : 1f;

    private void Locomotion(bool crouched, float dt)
    {
        // Followed in the world, so turning the view doesn't drag the legs behind it.
        Vector3 travel = Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up);
        stride = Vector3.Lerp(stride, travel, strideFollowTime > 0f ? 1f - Mathf.Exp(-dt / strideFollowTime) : 1f);
        Vector3 local = transform.InverseTransformDirection(stride);
        local.y = 0f;
        float speed = local.magnitude;
        float heading = speed > .3f ? Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg : 0f;
        float scale = GaitScale;
        mixWeight = mixStride = 0f;
        mixVelocity = Vector2.zero;

        float legs = heading, moving;
        if (crouched)
        {
            moving = MovingShare(crouchSet, heading, speed, scale);
            MixIdle(crouchSet, 1f - moving);
            Ways(crouchSet, heading, speed, moving, scale);
        }
        else
        {
            var hand = SetInHand();
            float turn = HipTurn(heading, speed);
            legs = Mathf.DeltaAngle(0f, heading - turn);
            float aim = hand != plainSet ? AimShare(hand, legs, speed, scale) : 0f;
            moving = Mathf.Lerp(MovingShare(plainSet, legs, speed, scale), MovingShare(hand, legs, speed, scale), aim);
            MixIdle(hand, 1f - moving);
            if (TurnOnTheSpot(speed < .3f, dt)) turnLayerTarget = 1f - moving;
            Ways(plainSet, legs, speed, moving * (1f - aim), scale);
            Ways(hand, legs, speed, moving * aim, scale);
            if (!legsPlanted) targetYaw = turn;
        }
        Pace(speed, legs, moving, dt);
    }

    private void MixIdle(MoveSet set, float weight)
    {
        if (set.Idle != null && weight > 0f) set.Idle.Target += weight;
    }

    // Running fast the hips turn toward the way of travel: ahead up to past the side, back beyond, with a margin either way
    // so a heading near the line doesn't flip from one to the other. Slow, the side and back cycles step facing the view.
    private const float BackFrom = 112f, BackMargin = 10f;
    private float HipTurn(float heading, float speed)
    {
        float share = Mathf.InverseLerp(hipTurnSpeeds.x, Mathf.Max(hipTurnSpeeds.x + .01f, hipTurnSpeeds.y), speed);
        float away = Mathf.Abs(heading);
        if (runsBack ? away < BackFrom - BackMargin : away > BackFrom + BackMargin) runsBack = !runsBack;
        if (share <= 0f) return 0f;
        float turn = runsBack ? Mathf.Clamp(Mathf.DeltaAngle(180f, heading), -hipTurn.y, hipTurn.y)
            : Mathf.Clamp(heading, -hipTurn.x, hipTurn.x);
        return turn * share;
    }

    // How much of the body moves rather than stands: up to the slowest cycle the way it goes, the stance shares it.
    private float MovingShare(MoveSet set, float heading, float speed, float scale)
    {
        Around(heading, out int from, out int to, out float toward);
        float slow = Mathf.Lerp(Slowest(set.Ways[from]), Slowest(set.Ways[to]), toward / 90f) * scale;
        return Mathf.Clamp01(speed / Mathf.Max(.1f, slow));

        static float Slowest(List<Channel> cycles) => cycles.Count > 0 ? cycles[0].Gait.Speed : 1f;
    }

    // Aiming with a gun, its aim cycles take the legs as far as they can reach the speed (their fastest, played as fast and
    // stretched as long as they may); past that the plain cycles run on under the aimed gun.
    private float AimShare(MoveSet set, float heading, float speed, float scale)
    {
        Around(heading, out int from, out int to, out float toward);
        float top = Mathf.Lerp(Fastest(set.Ways[from]), Fastest(set.Ways[to]), toward / 90f) * scale * gaitRate.y * gaitStretch.y;
        return Mathf.Max(AimBlend(), Mathf.Clamp01(gunHipMoves)) * (1f - Mathf.InverseLerp(top * .85f, top * 1.1f, speed));

        static float Fastest(List<Channel> cycles) => cycles.Count > 0 ? cycles[cycles.Count - 1].Gait.Speed : 1f;
    }

    // The two ways either side of a heading (0 ahead, 90 right) and how far it is past the first toward the second.
    private static void Around(float heading, out int from, out int to, out float toward)
    {
        float sector = Mathf.Repeat(heading, 360f) / 90f;
        from = Mathf.FloorToInt(sector) % 4;
        to = (from + 1) % 4;
        toward = (sector - Mathf.Floor(sector)) * 90f;
    }

    // Shares the weight between the two ways either side of the heading so their blended feet push back along it: each
    // way's cycles at this speed carry the body their own way at their own speed, and the share is the one whose sum of the
    // two points down the heading. Shared by angle alone, a fast forward run and a slow side step blended into a stride
    // that pointed well off the travel, and the feet slid sideways.
    private void Ways(MoveSet set, float heading, float speed, float weight, float scale)
    {
        if (weight <= 0f) return;
        Around(heading, out int from, out int to, out float toward);
        var a = set.Ways[from];
        var b = set.Ways[to];
        float share = toward / 90f;
        if (a.Count > 0 && b.Count > 0 && toward > .01f && toward < 89.99f)
        {
            float rad = heading * Mathf.Deg2Rad;
            var way = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            Vector2 first = LadderVelocity(a, speed, scale), second = LadderVelocity(b, speed, scale);
            float x = Cross(first, way), y = Cross(second, way);
            if (Mathf.Abs(x - y) > 1e-4f) share = Mathf.Clamp01(x / (x - y));
        }
        else if (a.Count == 0) share = 1f;
        else if (b.Count == 0) share = 0f;
        MixLadder(a, speed, weight * (1f - share), scale);
        MixLadder(b, speed, weight * share, scale);

        static float Cross(Vector2 p, Vector2 q) => p.x * q.y - p.y * q.x;
    }

    // The two cycles either side of the speed by their measured speeds; below the slowest or above the fastest, that one.
    // Named MixLadder, not Ladder: inside PlayerBody a member called Ladder hides the Ladder fixture type (PlayerBody.LadderFeet).
    private void MixLadder(List<Channel> cycles, float speed, float weight, float scale)
    {
        if (weight <= 0f || cycles.Count == 0) return;
        Bracket(cycles, speed, scale, out int low, out int high, out float t);
        MixCycle(cycles[low], weight * (1f - t), scale);
        if (high != low) MixCycle(cycles[high], weight * t, scale);
    }

    private static Vector2 LadderVelocity(List<Channel> cycles, float speed, float scale)
    {
        Bracket(cycles, speed, scale, out int low, out int high, out float t);
        return Velocity(cycles[low], scale) * (1f - t) + Velocity(cycles[high], scale) * t;
    }

    private static void Bracket(List<Channel> cycles, float speed, float scale, out int low, out int high, out float t)
    {
        low = high = 0;
        t = 0f;
        int last = cycles.Count - 1;
        if (last <= 0 || speed <= cycles[0].Gait.Speed * scale) return;
        if (speed >= cycles[last].Gait.Speed * scale) { low = high = last; return; }
        for (int i = 0; i < last; i++)
        {
            float a = cycles[i].Gait.Speed * scale, b = cycles[i + 1].Gait.Speed * scale;
            if (speed > b) continue;
            low = i;
            high = i + 1;
            t = b - a > 1e-3f ? (speed - a) / (b - a) : 1f;
            return;
        }
    }

    private static Vector2 Velocity(Channel cycle, float scale) => cycle.Gait.Way * (cycle.Gait.Speed * scale);

    private void MixCycle(Channel cycle, float weight, float scale)
    {
        if (weight <= 0f) return;
        cycle.Target += weight;
        mixWeight += weight;
        mixStride += weight * cycle.Gait.Stride;
        mixVelocity += weight * Velocity(cycle, scale);
    }

    // The step's pace and the stride's stretch. The blended cycles carry the body at their own speed along the travel and
    // step at their own pace; a person at this speed steps at gaitCadence's pace (for this leg), so the cycles play that much
    // faster or slower, within gaitRate, and the stride is stretched by what is left, within gaitStretch. If the stretch runs
    // out the pace makes up the rest, within gaitRate again; anything past that the feet slide.
    private void Pace(float speed, float legs, float moving, float dt)
    {
        if (player.IsNetworkDriven) FollowStep(dt);
        gaitSpeed = speed;
        runCalmShare = moving;
        if (mixWeight <= 0f)
        {
            strideStretch = FootPlanting.Stride.None;
            gaitNatural = 0f;
            gaitRateNow = gaitStretchNow = 1f;
            return;
        }
        float strideTime = Mathf.Max(.05f, mixStride / mixWeight);
        float rad = legs * Mathf.Deg2Rad;
        float along = Vector2.Dot(mixVelocity / mixWeight, new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)));
        float rate = 1f, stretch = 1f;
        if (along > .05f && speed > .05f)
        {
            float lo = Mathf.Min(gaitRate.x, gaitRate.y), hi = Mathf.Max(gaitRate.x, gaitRate.y);
            float shortest = Mathf.Min(gaitStretch.x, gaitStretch.y), longest = Mathf.Max(gaitStretch.x, gaitStretch.y);
            rate = Mathf.Clamp(CadenceFor(speed) * strideTime * .5f, lo, hi);
            stretch = speed / (rate * along);
            if (stretch > longest || stretch < shortest)
            {
                stretch = Mathf.Clamp(stretch, shortest, longest);
                rate = Mathf.Clamp(speed / (stretch * along), lo, hi);
            }
        }
        phase += dt * rate / strideTime;
        if (phase >= 1f)
        {
            strideCount += Mathf.FloorToInt(phase);
            phase -= Mathf.Floor(phase);
        }
        gaitNatural = along;
        gaitRateNow = rate;
        gaitStretchNow = stretch;
        // Not while a layer has the legs (a reload kick, the steps of a turn): their feet aren't the cycles' stride.
        float legsOwn = (1f - kickWeight) * (1f - turnLayerWeight);
        strideStretch = new FootPlanting.Stride(stride, stretch, moving * legsOwn, gaitStretchDrop * legLength);
        strideFrame = Time.frameCount;
    }

    // A person's steps a second at this speed (gaitCadence), for this body's leg: alike in build, a shorter leg at the same
    // speed runs as a longer one does at a speed higher by the root of their ratio, and steps that much faster.
    private float CadenceFor(float speed)
    {
        float like = Mathf.Sqrt(Mathf.Max(.3f, gaitLeg) / Mathf.Max(.2f, legLength));
        float at = speed * like;
        var points = gaitCadence;
        if (points == null || points.Length < 2) return 2.8f * like;
        float steps = points[1];
        for (int i = 0; i + 3 < points.Length; i += 2)
        {
            if (at <= points[i]) { steps = points[i + 1]; break; }
            steps = points[i + 3];
            if (at <= points[i + 2])
            {
                float t = points[i + 2] - points[i] > 1e-4f ? (at - points[i]) / (points[i + 2] - points[i]) : 1f;
                steps = Mathf.Lerp(points[i + 1], points[i + 3], t);
                break;
            }
        }
        return steps * like;
    }

    /// <summary>This frame's run for the /gait averages: the speed (m/s), steps a minute, the stride's stretch and how fast the
    /// feet still slide (m/s); false when no walk or run cycle played this frame.</summary>
    internal bool TryGaitNow(out float speed, out float steps, out float stretch, out float slide)
    {
        float strideTime = mixWeight > 0f ? mixStride / mixWeight : 0f;
        speed = gaitSpeed;
        steps = strideTime > 0f ? 120f * gaitRateNow / strideTime : 0f;
        stretch = gaitStretchNow;
        slide = Mathf.Max(0f, gaitSpeed - gaitNatural * gaitRateNow * gaitStretchNow);
        return strideFrame == Time.frameCount && mixWeight > 0f;
    }

    /// <summary>The stride as this body runs it now, for the /gait readout: the speed, the speed its cycles carry it at their own
    /// pace, the pace against theirs, the stretch, steps a minute and how fast the feet still slide (m/s).</summary>
    internal string GaitReadout()
    {
        TryGaitNow(out _, out float steps, out _, out float slide);
        if (mixWeight <= 0f) slide = 0f;
        var text = new System.Text.StringBuilder();
        text.Append($"속도 {gaitSpeed:0.00} m/s · 클립 제 속도 {gaitNatural:0.00} · 박자 ×{gaitRateNow:0.00} · 보폭 ×{gaitStretchNow:0.00}");
        text.Append($" · 분당 {steps:0}걸음 · 미끄러짐 {slide:0.00} m/s · 골반 {yaw:0}° · 다리 {legLength:0.00} m");
        foreach (var channel in channels)
            if (channel.Gait != null && channel.Target > .05f)
                text.Append($"\n  {channel.Playable.GetAnimationClip()?.name} {channel.Target:0.00} ({channel.Gait.Speed * GaitScale:0.00} m/s, "
                    + $"분당 {120f / Mathf.Max(.01f, channel.Gait.Stride):0}걸음)");
        return text.ToString();
    }

    // Standing still, the legs keep their place while the view turns and turn round once it has turned far enough
    // (the spine twists the chest toward the view meanwhile): on the spot, or stepping with the turn clips (turnSteps).
    // True while the legs step (BlendTurn plays the clip on them).
    private bool TurnOnTheSpot(bool still, float dt)
    {
        if (!still) { turning = 0; return false; }
        float look = transform.eulerAngles.y, limit = turnStartAngle + 30f;
        float offset = Mathf.Clamp(Mathf.DeltaAngle(look, legYaw), -limit, limit);
        if (turning == 0 && Mathf.Abs(offset) > turnStartAngle)
        {
            // Legs left behind on the left turn round to the right, and the other way round.
            turning = offset < 0f ? 1 : -1;
            turnVelocity = 0f;
            var start = turning > 0 ? turnRightClip : turnLeftClip;
            if (turnSteps && start.IsValid()) { start.SetTime(0); start.SetDone(false); }
        }
        bool stepping = false;
        if (turning != 0)
        {
            offset = turnSteps ? Mathf.MoveTowards(offset, 0f, turnSpeed * dt)
                : Mathf.SmoothDamp(offset, 0f, ref turnVelocity, turnEaseTime, turnSpeed, dt);
            var clip = turning > 0 ? turnRightClip : turnLeftClip;
            var other = turning > 0 ? turnLeftClip : turnRightClip;
            if (turnSteps && clip.IsValid())
            {
                if (clip.GetTime() >= clip.GetAnimationClip().length) clip.SetTime(0);
                clip.SetSpeed(turnClipSpeed);
                turnMixer.SetInputWeight(turning > 0 ? 1 : 0, 1f);
                turnMixer.SetInputWeight(turning > 0 ? 0 : 1, 0f);
                if (other.IsValid()) other.SetSpeed(0);
                stepping = true;
            }
            if (Mathf.Abs(offset) < 1f) turning = 0;
        }
        legYaw = look + offset;
        targetYaw = offset;
        legsPlanted = true;
        return stepping;
    }
}
